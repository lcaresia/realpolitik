using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CurrencyMod.Support
{
    /// <summary>
    /// "Send logs" (Realpolitik screen, Report a problem): bundles the game and mod logs with a short system summary,
    /// strips API keys, e-mails and the Windows user name, saves the bundle locally and uploads it to the report server
    /// (a Cloudflare Worker, loja\reports). The player gets a short code to quote on Discord or GitHub.
    /// Nothing is sent without the player pressing the button.
    /// </summary>
    internal static class ErrorReport
    {
        internal enum State { Idle, Sending, Sent, Failed }

        /// <summary>Tail kept of each log, so a long session still fits the upload limit.</summary>
        private const int MaxFileChars = 1_500_000;
        private const int MaxNoteChars = 1000;

        internal static ConfigEntry<string> ServerUrl;
        private static string configPath;

        private static readonly object Gate = new object();
        private static State state;
        private static string code;
        private static string error;
        private static string savedPath;

        internal static State Current { get { lock (Gate) { return state; } } }
        internal static string Code { get { lock (Gate) { return code; } } }
        internal static string Error { get { lock (Gate) { return error; } } }
        internal static string SavedPath { get { lock (Gate) { return savedPath; } } }

        internal static string Folder => Path.Combine(SessionLog.Folder, "reports");

        internal static void Bind(ConfigFile config)
        {
            configPath = config.ConfigFilePath;
            ServerUrl = config.Bind("Support", "ReportServer", "https://realpolitik-reports.lcaresia.workers.dev/report",
                "Endereço que recebe os relatórios de erro (botão Enviar registros, na tela Realpolitik). Vazio = só salva o arquivo neste PC.");
        }

        /// <summary>Starts building and sending in the background; false when a send is already running.</summary>
        internal static bool Send(string note)
        {
            lock (Gate)
            {
                if (state == State.Sending)
                {
                    return false;
                }
                state = State.Sending;
                code = error = savedPath = null;
            }
            // The system summary reads Unity and the game: on the main thread, before going to the worker thread.
            string system = SystemSummary();
            // The open game's AI log folder (main thread); without a game, the most recent one.
            string aiFolder = Diplomacy.AiModule.World?.GameGuid != null ? Diplomacy.AiLog.GameFolder(Diplomacy.AiModule.World.GameGuid) : null;
            string url = ServerUrl?.Value?.Trim();
            SessionLog.Mark("Error report requested by the player");
            new Thread(() => Run(note, system, url, aiFolder)) { IsBackground = true, Name = "Realpolitik.Report" }.Start();
            return true;
        }

        private static void Run(string note, string system, string url, string aiFolder)
        {
            string path = null;
            try
            {
                byte[] bundle = Build(note, system, aiFolder);
                Directory.CreateDirectory(Folder);
                path = Path.Combine(Folder, $"report-{DateTime.Now:yyyyMMdd-HHmmss}.json.gz");
                File.WriteAllBytes(path, bundle);
                lock (Gate)
                {
                    savedPath = path;
                }
                if (string.IsNullOrEmpty(url))
                {
                    Finish(State.Failed, null, "no report server configured");
                    return;
                }
                string received = Upload(url, bundle);
                Plugin.Log.LogInfo($"Error report sent: {received} ({bundle.Length / 1024} KB, copy at {path}).");
                Finish(State.Sent, received, null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error report not sent: {ex.Message}" + (path != null ? $" (saved at {path})" : string.Empty));
                Finish(State.Failed, null, ex.Message);
            }
        }

        private static void Finish(State result, string receivedCode, string message)
        {
            lock (Gate)
            {
                state = result;
                code = receivedCode;
                error = message;
            }
        }

        // ---------------- Bundle ----------------

        /// <summary>Budget for the language AI's logs (dossiers, prompts, answers): the latest turns until it is spent.</summary>
        private const int MaxAiLogChars = 6_000_000;
        private const int MaxAiFileChars = 400_000;

        internal static byte[] Build(string note, string system, string aiFolder = null)
        {
            var files = new JObject();
            void Add(string name, string filePath)
            {
                string text = ReadTail(filePath);
                if (text != null)
                {
                    files[name] = Scrub(text);
                }
            }
            Add("LogOutput.log", Path.Combine(Paths.BepInExRootPath, "LogOutput.log"));
            Add("session.log", SessionLog.CurrentPath);
            Add("session-prev.log", SessionLog.PreviousPath);
            string unityLog = UnityLogPath();
            if (unityLog != null)
            {
                Add("Player.log", unityLog);
                Add("Player-prev.log", Path.Combine(Path.GetDirectoryName(unityLog) ?? string.Empty, "Player-prev.log"));
            }
            // Every .cfg (mod, MoreEmpires, BepInEx). Keys live elsewhere (credenciais\, deepseek.key) and never go.
            foreach (string cfg in SafeFiles(Paths.ConfigPath, "*.cfg"))
            {
                Add("config/" + Path.GetFileName(cfg), cfg);
            }
            // The game's own diagnostics: the latest session report and pass-turn timeout reports.
            string temporary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Humankind", "Temporary Files");
            foreach (string diagnostic in SafeFiles(temporary, "Diagnostics*.html").Take(2))
            {
                Add("game/" + Path.GetFileName(diagnostic), diagnostic);
            }
            foreach (string timeout in SafeFiles(temporary, "pass_turn_timeout_report*.txt").Take(3))
            {
                Add("game/" + Path.GetFileName(timeout), timeout);
            }
            // The language AI: what each nation received and answered in the latest turns of this game.
            if (aiFolder == null || !Directory.Exists(aiFolder))
            {
                aiFolder = SafeDirectories(Diplomacy.AiLog.Root).FirstOrDefault();
            }
            if (aiFolder != null)
            {
                int budget = MaxAiLogChars;
                foreach (string log in SafeFiles(aiFolder, "*"))
                {
                    string text = ReadTail(log, MaxAiFileChars);
                    if (text == null)
                    {
                        continue;
                    }
                    if (text.Length > budget)
                    {
                        break;
                    }
                    budget -= text.Length;
                    files["ai/" + Path.GetFileName(aiFolder) + "/" + Path.GetFileName(log)] = Scrub(text);
                }
            }
            files["system.txt"] = Scrub(system);
            var payload = new JObject
            {
                ["schema"] = 1,
                ["mod"] = Plugin.ProductVersion,
                ["game"] = Application.version,
                ["language"] = L.Code,
                ["note"] = Clip(Scrub((note ?? string.Empty).Trim()), MaxNoteChars),
                ["created"] = DateTime.UtcNow.ToString("o"),
                ["files"] = files,
            };
            byte[] json = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress))
                {
                    gzip.Write(json, 0, json.Length);
                }
                return output.ToArray();
            }
        }

        private static string Clip(string text, int max) => text.Length > max ? text.Substring(0, max) : text;

        /// <summary>Files of a folder, newest first; empty when the folder is missing.</summary>
        private static IEnumerable<string> SafeFiles(string folder, string pattern)
        {
            try
            {
                return Directory.Exists(folder)
                    ? new DirectoryInfo(folder).GetFiles(pattern).OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName).ToList()
                    : new List<string>();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private static IEnumerable<string> SafeDirectories(string folder)
        {
            try
            {
                return Directory.Exists(folder)
                    ? new DirectoryInfo(folder).GetDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Select(d => d.FullName).ToList()
                    : new List<string>();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private static string UnityLogPath()
        {
            try
            {
                string path = Application.consoleLogPath;
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The end of a log (the part that matters after a crash), read while the game still writes to it.</summary>
        private static string ReadTail(string path, int maxChars = MaxFileChars)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return null;
                }
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long skip = Math.Max(0, stream.Length - maxChars);
                    stream.Seek(skip, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string text = reader.ReadToEnd();
                        return skip > 0 ? $"[... first {skip / 1024} KB cut ...]\n" + text : text;
                    }
                }
            }
            catch (Exception ex)
            {
                return $"[could not read {Path.GetFileName(path)}: {ex.Message}]";
            }
        }

        private static readonly Regex[] Secrets =
        {
            new Regex(@"\b(?:sk|rk|pk)-[A-Za-z0-9_\-]{12,}", RegexOptions.Compiled),
            new Regex(@"\bAIza[0-9A-Za-z_\-]{20,}", RegexOptions.Compiled),
            new Regex(@"\bxai-[A-Za-z0-9]{20,}", RegexOptions.Compiled),
            new Regex(@"\bgh[pousr]_[A-Za-z0-9]{20,}", RegexOptions.Compiled),
            new Regex(@"\beyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}", RegexOptions.Compiled),
            new Regex(@"(?i)\bbearer\s+[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled),
            new Regex(@"(?i)((?:api[_-]?key|access[_-]?token|refresh[_-]?token|secret|password|authorization)[""']?\s*[:=]\s*[""']?)[^\s""',;]{6,}", RegexOptions.Compiled),
        };

        private static readonly Regex Email = new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

        /// <summary>Removes keys, tokens, e-mails and the Windows user name (it shows in every path).</summary>
        internal static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            foreach (Regex secret in Secrets)
            {
                text = secret.Replace(text, m => m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value + "<removed>" : "<removed>");
            }
            text = Email.Replace(text, "<email>");
            // Only inside paths (C:\Users\<name>\...): a user called "user" or "admin" would otherwise erase common words.
            string user = Environment.UserName;
            if (!string.IsNullOrEmpty(user))
            {
                text = Regex.Replace(text, @"(?i)([\\/]Users[\\/])" + Regex.Escape(user) + @"(?=[\\/""'\s]|$)", "$1<user>");
            }
            return text;
        }

        /// <summary>What helps to read the logs: versions, PC, screen and the open game (main thread only).</summary>
        private static string SystemSummary()
        {
            var text = new StringBuilder();
            void Line(string name, Func<object> value)
            {
                try
                {
                    text.AppendLine($"{name}: {value()}");
                }
                catch (Exception ex)
                {
                    text.AppendLine($"{name}: ? ({ex.GetType().Name})");
                }
            }
            Line("Mod", () => $"{Plugin.ProductVersion} (core {Plugin.PluginVersion})");
            Line("Game", () => Application.version);
            Line("Unity", () => Application.unityVersion);
            Line("BepInEx", () => typeof(Paths).Assembly.GetName().Version);
            Line("OS", () => SystemInfo.operatingSystem);
            Line("CPU", () => $"{SystemInfo.processorType} ({SystemInfo.processorCount} threads)");
            Line("RAM", () => $"{SystemInfo.systemMemorySize} MB");
            Line("GPU", () => $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB, {SystemInfo.graphicsDeviceType})");
            Line("Screen", () => $"{Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRate} Hz, {(Screen.fullScreen ? Screen.fullScreenMode.ToString() : "windowed")}, dpi {Screen.dpi}");
            Line("Process memory", () => $"{UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024 * 1024)} MB reserved by Unity, {GC.GetTotalMemory(false) / (1024 * 1024)} MB managed");
            Line("Interface language", () => L.Code);
            Line("Plugins", () => string.Join(", ", Directory.GetDirectories(Paths.PluginPath).Select(Path.GetFileName)
                .Concat(Directory.GetFiles(Paths.PluginPath, "*.dll").Select(Path.GetFileName))));
            Line("In game", () => CentralBankWindow.IsInGame);
            if (CentralBankWindow.TryGetGameData(out Amplitude.Mercury.Interop.GameSnapshot.Data game))
            {
                Line("Turn", () => game.CurrentTurn);
                Line("Empires", () => game.NumberOfMajorEmpires);
                Line("Local empire", () => game.LocalEmpireInfo.EmpireIndex);
            }
            Line("AI diplomacy", () => Diplomacy.AiConfig.Enabled.Value ? "on" : "off");
            Line("AI provider", () => Diplomacy.Llm.Providers.ProviderRouter.Current?.Name ?? "none");
            return text.ToString();
        }

        // ---------------- Upload ----------------

        private static string Upload(string url, byte[] bundle)
        {
            if ((ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls12) == 0)
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "POST";
            http.ContentType = "application/gzip";
            http.UserAgent = "Realpolitik/" + Plugin.ProductVersion;
            http.Timeout = 60000;
            http.ReadWriteTimeout = 60000;
            http.ContentLength = bundle.Length;
            using (Stream body = http.GetRequestStream())
            {
                body.Write(bundle, 0, bundle.Length);
            }
            try
            {
                using (var response = (HttpWebResponse)http.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    JObject answer = JObject.Parse(reader.ReadToEnd());
                    string received = (string)answer["code"];
                    if (string.IsNullOrEmpty(received))
                    {
                        throw new InvalidOperationException("the server did not return a code");
                    }
                    return received;
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse failed)
            {
                string message;
                using (var reader = new StreamReader(failed.GetResponseStream(), Encoding.UTF8))
                {
                    message = reader.ReadToEnd();
                }
                try
                {
                    message = (string)JObject.Parse(message)["error"] ?? message;
                }
                catch (Exception)
                {
                }
                throw new InvalidOperationException($"HTTP {(int)failed.StatusCode}: {message}");
            }
        }
    }
}
