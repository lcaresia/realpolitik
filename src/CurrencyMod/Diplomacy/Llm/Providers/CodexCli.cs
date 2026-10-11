using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BepInEx;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacy.Llm.Providers
{
    /// <summary>
    /// "ChatGPT (Codex)" provider: uses the Codex installed on the player's PC (the same program as @openai/codex-sdk) and
    /// the ChatGPT account they signed into ("codex login"). Usage comes out of their plan's quota. The mod never reads the
    /// Codex login: it only runs "codex exec" and "codex login".
    /// Each call is a "codex exec" with no tools (shell, browser, apps disabled), read-only, in an empty folder,
    /// without saving a session: measured on 2026-10-06, Codex's baseline drops from ~20k to ~10k tokens and from ~15 s
    /// to ~5 s per call.
    /// </summary>
    internal static class CodexCli
    {
        private static readonly object Gate = new object();
        private static string exePath;
        private static DateTime exeCheckedUtc;
        private static bool? loggedIn;
        private static DateTime loginCheckedUtc;
        private static int checking;

        /// <summary>Codex features only useful for programming: disabled on every call (fewer tokens, no tools).</summary>
        private static readonly string[] DisabledFeatures =
        {
            "shell_tool", "unified_exec", "apps", "browser_use", "browser_use_external", "computer_use", "image_generation",
            "multi_agent", "plugins", "skill_search", "tool_suggest", "view_image", "sleep_tool", "goals", "hooks",
            "workspace_dependencies", "shell_snapshot", "remote_plugin", "realtime_conversation",
        };

        private const string Instructions =
            "You are the text engine of a strategy game's AI. Do not use tools, read files or run commands. " +
            "Answer only what the message asks for; when it asks for json, reply with the json object alone.";

        internal static string Folder => Path.Combine(Paths.BepInExRootPath, "DiplomaciaIA", "codex");

        // ---------------- Installation and login ----------------

        /// <summary>Path of codex.exe (or codex.cmd), or null if Codex is not installed.</summary>
        internal static string Exe
        {
            get
            {
                lock (Gate)
                {
                    if (DateTime.UtcNow - exeCheckedUtc > TimeSpan.FromSeconds(30))
                    {
                        exePath = FindExe();
                        exeCheckedUtc = DateTime.UtcNow;
                    }
                    return exePath;
                }
            }
        }

        internal static bool Installed => Exe != null;

        /// <summary>Installed and logged into ChatGPT. The state is checked in the background (at most every 2 min).</summary>
        internal static bool Ready
        {
            get
            {
                if (!Installed)
                {
                    return false;
                }
                RefreshLoginAsync(force: false);
                // Still checking (just loaded) counts as ready: the call itself fails with an auth error if the login
                // is really gone. Treating "unknown" as logged out sent the first nations of a turn to the fallback.
                return loggedIn != false;
            }
        }

        /// <summary>null = still checking.</summary>
        internal static bool? LoggedIn => loggedIn;

        internal static void RefreshLoginAsync(bool force)
        {
            if (!force && DateTime.UtcNow - loginCheckedUtc < TimeSpan.FromMinutes(2))
            {
                return;
            }
            if (Interlocked.Exchange(ref checking, 1) == 1)
            {
                return;
            }
            loginCheckedUtc = DateTime.UtcNow;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    bool? state = CheckLogin();
                    if (state.HasValue)
                    {
                        loggedIn = state.Value;
                    }
                }
                catch (Exception)
                {
                    loggedIn = false;
                }
                finally
                {
                    Interlocked.Exchange(ref checking, 0);
                }
            });
        }

        /// <summary>Logged in, logged out, or null when the check itself failed (timed out under load): keep the last state.</summary>
        private static bool? CheckLogin()
        {
            string exe = Exe;
            if (exe == null)
            {
                return false;
            }
            RunResult run = Run(exe, new List<string> { "login", "status" }, null, 30);
            if (run.TimedOut)
            {
                return null;
            }
            string text = (run.Stdout + "\n" + run.Stderr).ToLowerInvariant();
            return run.ExitCode == 0 && text.Contains("logged in") && !text.Contains("not logged in");
        }

        /// <summary>
        /// "Sign in": runs Codex's own "codex login", which opens the browser on the OpenAI page and stores the login in
        /// Codex. The mod just waits for it to finish (5 minutes) and checks the state.
        /// </summary>
        internal static void Login(LoginSession session)
        {
            string exe = Exe;
            if (exe == null)
            {
                session.Message = L.T("O Codex não está instalado neste PC.");
                session.Stage = LoginStage.Failed;
                return;
            }
            session.Stage = LoginStage.WaitingBrowser;
            session.Message = L.T("Termine o login no navegador que o Codex abriu.");
            RunResult run = Run(exe, new List<string> { "login" }, null, 300, () => session.CancelRequested);
            if (session.CancelRequested)
            {
                return;
            }
            loggedIn = CheckLogin();
            loginCheckedUtc = DateTime.UtcNow;
            if (loggedIn == true)
            {
                session.Message = L.T("Conectado ao ChatGPT pelo Codex.");
                session.Stage = LoginStage.Done;
                ProviderRouter.ResetHealth();
            }
            else
            {
                session.Message = run.TimedOut ? L.T("O tempo do login acabou. Tente de novo.") : L.T("O login não deu certo.");
                session.Stage = LoginStage.Failed;
            }
        }

        private static string FindExe()
        {
            string custom = AiConfig.CodexPath?.Value?.Trim().Trim('"');
            if (!string.IsNullOrEmpty(custom))
            {
                return File.Exists(custom) ? custom : null;
            }
            // Codex app for Windows: %LOCALAPPDATA%\OpenAI\Codex\bin\<version>\codex.exe (the newest version).
            try
            {
                string bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
                if (Directory.Exists(bin))
                {
                    string newest = new DirectoryInfo(bin).GetDirectories()
                        .OrderByDescending(d => d.LastWriteTimeUtc)
                        .Select(d => Path.Combine(d.FullName, "codex.exe"))
                        .FirstOrDefault(File.Exists);
                    if (newest != null)
                    {
                        return newest;
                    }
                }
            }
            catch (Exception)
            {
            }
            // Installed via npm or the command-line installer: search the PATH.
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';'))
            {
                foreach (string name in new[] { "codex.exe", "codex.cmd" })
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim().Trim('"'), name);
                        if (dir.Trim().Length > 0 && File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            return null;
        }

        // ---------------- Call ----------------

        internal static ChatResult Send(ChatRequest request, ProviderDef provider, int timeoutSeconds)
        {
            var result = new ChatResult { ProviderId = provider.Id, Model = request.Model };
            var watch = Stopwatch.StartNew();
            string exe = Exe;
            if (exe == null)
            {
                result.Kind = ErrorKind.Auth;
                result.Error = "Codex não instalado";
                return result;
            }
            string work = Path.Combine(Folder, "vazio");
            string instructions = Path.Combine(Folder, "instrucoes.md");
            try
            {
                Directory.CreateDirectory(work);
                if (!File.Exists(instructions) || File.ReadAllText(instructions) != Instructions)
                {
                    File.WriteAllText(instructions, Instructions, new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                result.Kind = ErrorKind.Other;
                result.Error = "pasta do Codex: " + ex.Message;
                return result;
            }

            var args = new List<string>
            {
                "exec", "--json", "--skip-git-repo-check", "--ephemeral", "--ignore-user-config", "--ignore-rules",
                "--sandbox", "read-only", "-C", work, "--color", "never",
                "-c", "model_instructions_file='" + instructions.Replace("'", "''") + "'",
                "-c", "web_search=\"disabled\"",
                "-c", "include_permissions_instructions=false",
                "-c", "include_environment_context=false",
                "-c", "include_apps_instructions=false",
                "-c", "model_reasoning_effort=\"" + Effort(request.ReasoningEffort) + "\"",
            };
            foreach (string feature in DisabledFeatures)
            {
                args.Add("--disable");
                args.Add(feature);
            }
            if (!string.IsNullOrEmpty(request.Model))
            {
                args.Add("-m");
                args.Add(request.Model);
            }
            args.Add("-");

            RunResult run = Run(exe, args, BuildPrompt(request), Math.Max(30, timeoutSeconds));
            result.Seconds = watch.Elapsed.TotalSeconds;
            Parse(run, result);
            if (result.Ok && request.JsonMode)
            {
                result.Content = LlmClient.ExtractJson(result.Content);
            }
            return result;
        }

        /// <summary>Codex receives a single text: the mod's instructions, the message and, on retries, the conversation.</summary>
        private static string BuildPrompt(ChatRequest request)
        {
            var text = new StringBuilder();
            foreach (ChatMessage message in request.Messages)
            {
                switch (message.Role)
                {
                    case "system":
                        text.AppendLine("# Instructions").AppendLine(message.Content).AppendLine();
                        break;
                    case "assistant":
                        text.AppendLine("# Your previous answer").AppendLine(message.Content).AppendLine();
                        break;
                    default:
                        text.AppendLine("# Message").AppendLine(message.Content).AppendLine();
                        break;
                }
            }
            if (request.JsonMode)
            {
                text.AppendLine("Reply with the json object only.");
            }
            return text.ToString();
        }

        private static string Effort(string effort)
        {
            switch (effort)
            {
                case "high":
                case "max":
                    return "high";
                case "low":
                    return "low";
                default:
                    return "low"; // "off": the cheapest Codex accepts
            }
        }

        /// <summary>Events of "codex exec --json": the agent's last message and the usage at the end of the turn.</summary>
        private static void Parse(RunResult run, ChatResult result)
        {
            string message = null;
            string error = null;
            foreach (string raw in (run.Stdout ?? string.Empty).Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("{"))
                {
                    continue;
                }
                JObject evt;
                try
                {
                    evt = JObject.Parse(line);
                }
                catch (Exception)
                {
                    continue;
                }
                string type = (string)evt["type"];
                if (type == "item.completed" && evt["item"] is JObject item)
                {
                    string itemType = (string)item["type"];
                    if (itemType == "agent_message")
                    {
                        message = (string)item["text"];
                    }
                    else if (itemType == "error")
                    {
                        string text = (string)item["message"];
                        if (!Harmless(text))
                        {
                            error = text;
                        }
                    }
                }
                else if (type == "turn.completed" && evt["usage"] is JObject usage)
                {
                    result.PromptTokens = (int?)usage["input_tokens"] ?? 0;
                    result.CacheHitTokens = (int?)usage["cached_input_tokens"] ?? 0;
                    result.CacheMissTokens = Math.Max(0, result.PromptTokens - result.CacheHitTokens);
                    result.CompletionTokens = (int?)usage["output_tokens"] ?? 0;
                    result.ReasoningTokens = (int?)usage["reasoning_output_tokens"] ?? 0;
                }
                else if (type == "turn.failed")
                {
                    error = (string)evt["error"]?["message"] ?? "turn.failed";
                }
                else if (type == "error")
                {
                    string text = (string)evt["message"];
                    if (!Harmless(text) && !(text ?? string.Empty).StartsWith("Reconnecting"))
                    {
                        error = text;
                    }
                }
            }
            if (!string.IsNullOrEmpty(message) && error == null)
            {
                result.Ok = true;
                result.Content = message;
                result.FinishReason = "stop";
                result.CostUsd = 0; // plan quota, no dollars
                return;
            }
            result.Ok = false;
            if (run.TimedOut)
            {
                result.Kind = ErrorKind.Network;
                result.Transient = true;
                result.Error = "Codex: tempo esgotado";
                return;
            }
            string detail = LlmClient.Redact(error ?? LastLine(run.Stderr) ?? $"código de saída {run.ExitCode}");
            string lower = detail.ToLowerInvariant();
            if (lower.Contains("usage limit") || lower.Contains("quota") || lower.Contains("limit reached") || lower.Contains("upgrade"))
            {
                result.Kind = ErrorKind.Credit;
            }
            else if (lower.Contains("401") || lower.Contains("unauthorized") || lower.Contains("not logged in") || lower.Contains("login"))
            {
                result.Kind = ErrorKind.Auth;
                loggedIn = null;
                loginCheckedUtc = DateTime.MinValue;
            }
            else if (lower.Contains("429") || lower.Contains("rate limit"))
            {
                result.Kind = ErrorKind.Rate;
            }
            else if (lower.Contains("model") && (lower.Contains("not") || lower.Contains("unsupported")))
            {
                result.Kind = ErrorKind.Request;
            }
            else
            {
                result.Kind = ErrorKind.Server;
            }
            result.Error = "Codex: " + (detail.Length > 300 ? detail.Substring(0, 300) : detail);
        }

        /// <summary>Codex warnings that are not call errors.</summary>
        private static bool Harmless(string text)
        {
            text = text ?? string.Empty;
            return text.StartsWith("Codex is ignoring") || text.StartsWith("Code Mode is unavailable") || text.Contains("PATH aliases");
        }

        private static string LastLine(string text)
        {
            return (text ?? string.Empty).Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.Contains("PATH aliases"));
        }

        // ---------------- Process ----------------

        private sealed class RunResult
        {
            public int ExitCode = -1;
            public string Stdout = string.Empty;
            public string Stderr = string.Empty;
            public bool TimedOut;
        }

        private static RunResult Run(string exe, List<string> args, string stdin, int timeoutSeconds, Func<bool> cancel = null)
        {
            var result = new RunResult();
            bool viaCmd = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            string argLine = string.Join(" ", args.Select(Quote));
            var info = new ProcessStartInfo
            {
                FileName = viaCmd ? "cmd.exe" : exe,
                Arguments = viaCmd ? "/d /s /c \"" + Quote(exe) + " " + argLine + "\"" : argLine,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Folder,
            };
            Directory.CreateDirectory(Folder);
            using (var process = new Process { StartInfo = info })
            {
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) { lock (stdout) { stdout.AppendLine(e.Data); } } };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) { lock (stderr) { stderr.AppendLine(e.Data); } } };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try
                {
                    if (stdin != null)
                    {
                        byte[] bytes = new UTF8Encoding(false).GetBytes(stdin);
                        process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                        process.StandardInput.BaseStream.Flush();
                    }
                    process.StandardInput.Close();
                }
                catch (Exception)
                {
                    // The process exited before reading (startup error): the reason comes in the output.
                }
                var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                while (!process.WaitForExit(250))
                {
                    if (DateTime.UtcNow > deadline || (cancel != null && cancel()))
                    {
                        result.TimedOut = DateTime.UtcNow > deadline;
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                        }
                        break;
                    }
                }
                process.WaitForExit(2000);
                try
                {
                    result.ExitCode = process.HasExited ? process.ExitCode : -1;
                }
                catch (Exception)
                {
                }
                lock (stdout)
                {
                    result.Stdout = stdout.ToString();
                }
                lock (stderr)
                {
                    result.Stderr = stderr.ToString();
                }
            }
            return result;
        }

        /// <summary>Windows-style quoting (CommandLineToArgvW), for paths with spaces.</summary>
        private static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return arg;
            }
            var text = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    text.Append('\\', backslashes * 2 + 1).Append('"');
                }
                else
                {
                    text.Append('\\', backslashes).Append(c);
                }
                backslashes = 0;
            }
            text.Append('\\', backslashes * 2).Append('"');
            return text.ToString();
        }
    }
}
