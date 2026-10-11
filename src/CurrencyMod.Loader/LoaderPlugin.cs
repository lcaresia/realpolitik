using System;
using System.IO;
using System.Reflection;
using BepInEx;
using Mono.Cecil;
using UnityEngine;

namespace CurrencyMod.Loader
{
    /// <summary>
    /// Fixed plugin that loads the CurrencyMod core (BepInEx\CurrencyModCore\CurrencyMod.dll)
    /// and hot-reloads it when the DLL changes or when the "reload" command arrives.
    /// The core exposes CurrencyMod.ModEntry with Start/Stop/Command (called through reflection).
    /// </summary>
    [BepInPlugin("lucas.humankind.currency", "CurrencyMod", "1.1.0")]
    public class LoaderPlugin : BaseUnityPlugin
    {
        private string corePath;
        private string commandPath;
        private string resultPath;
        private DateTime loadedWriteTime;
        private DateTime pendingWriteTime;
        private float pendingSince = -1f;
        private float nextPoll;

        private Type entryType;
        private GameObject host;
        private string carriedState;
        private int generation;

        private void Awake()
        {
            corePath = Path.Combine(Paths.BepInExRootPath, "CurrencyModCore", "CurrencyMod.dll");
            // Development command channel: only exists on machines with the _Modding folder (dev install).
            string modding = Path.Combine(Paths.GameRootPath, "_Modding");
            string devDir = Path.Combine(modding, "dev");
            if (Directory.Exists(modding))
            {
                Directory.CreateDirectory(Path.Combine(devDir, "out"));
                commandPath = Path.Combine(devDir, "cmd.txt");
            }
            resultPath = Path.Combine(Directory.Exists(modding) ? Path.Combine(devDir, "out") : Path.Combine(Paths.BepInExRootPath, "CurrencyModCore"), "result.txt");

            LoadCore();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPoll)
            {
                return;
            }
            nextPoll = Time.unscaledTime + 0.5f;

            WatchCoreFile();
            ProcessCommands();
        }

        private void WatchCoreFile()
        {
            if (!File.Exists(corePath))
            {
                return;
            }
            DateTime writeTime = File.GetLastWriteTimeUtc(corePath);
            if (writeTime == loadedWriteTime)
            {
                pendingSince = -1f;
                return;
            }
            // Waits for the DLL to stop changing (build copy) before reloading.
            if (writeTime != pendingWriteTime)
            {
                pendingWriteTime = writeTime;
                pendingSince = Time.unscaledTime;
                return;
            }
            if (pendingSince >= 0 && Time.unscaledTime - pendingSince > 1.0f)
            {
                Logger.LogInfo("New core version detected, reloading...");
                Reload();
            }
        }

        private void ProcessCommands()
        {
            if (commandPath == null || !File.Exists(commandPath))
            {
                return;
            }
            string[] lines;
            try
            {
                lines = File.ReadAllLines(commandPath);
                File.Delete(commandPath);
            }
            catch (IOException)
            {
                return; // file still being written
            }

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }
                if (line.Equals("reload", StringComparison.OrdinalIgnoreCase))
                {
                    Reload();
                    WriteResult(line, entryType != null ? $"ok: core reloaded (generation {generation})" : "error: the core did not load (see BepInEx\\LogOutput.log)");
                    continue;
                }
                InvokeEntry("Command", line);
            }
        }

        private void Reload()
        {
            UnloadCore();
            LoadCore();
        }

        private void LoadCore()
        {
            if (!File.Exists(corePath))
            {
                Logger.LogError($"Core not found at {corePath}.");
                return;
            }
            try
            {
                loadedWriteTime = File.GetLastWriteTimeUtc(corePath);
                pendingWriteTime = loadedWriteTime;
                generation++;
                byte[] bytes = WithUniqueAssemblyName(File.ReadAllBytes(corePath), generation);
                Assembly assembly = Assembly.Load(bytes);
                entryType = assembly.GetType("CurrencyMod.ModEntry", throwOnError: true);

                host = new GameObject($"CurrencyMod.Core#{generation}");
                DontDestroyOnLoad(host);
                host.hideFlags = HideFlags.HideAndDontSave;

                entryType.GetMethod("Start").Invoke(null, new object[] { Logger, Config, host, carriedState, resultPath });
                carriedState = null;
                Logger.LogInfo($"Core loaded (generation {generation}).");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load the core: {ex}");
                entryType = null;
                // Without this the same DLL was never tried again (its date already counted as loaded) and the mod stayed
                // off until a new build. E.g. an antivirus or the build still holding the file. Tries again in ~5 s.
                loadedWriteTime = default(DateTime);
                pendingSince = Time.unscaledTime + 4f;
            }
        }

        /// <summary>
        /// Unity binds components (MonoBehaviour) by assembly name. If every reloaded version
        /// had the same name, AddComponent would use the classes of the first version.
        /// So each generation gets a unique name before being loaded.
        /// </summary>
        private static byte[] WithUniqueAssemblyName(byte[] original, int generation)
        {
            using (var input = new MemoryStream(original))
            using (AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(input))
            {
                string uniqueName = $"{definition.Name.Name}_g{generation}_{DateTime.UtcNow.Ticks}";
                definition.Name.Name = uniqueName;
                definition.MainModule.Name = uniqueName + ".dll";
                using (var output = new MemoryStream())
                {
                    definition.Write(output);
                    return output.ToArray();
                }
            }
        }

        private void UnloadCore()
        {
            if (entryType != null)
            {
                try
                {
                    carriedState = entryType.GetMethod("Stop").Invoke(null, null) as string;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to stop the core: {ex}");
                }
            }
            if (host != null)
            {
                Destroy(host);
                host = null;
            }
            entryType = null;
        }

        private void InvokeEntry(string method, string argument)
        {
            if (entryType == null)
            {
                WriteResult(argument, "error: core not loaded");
                return;
            }
            try
            {
                entryType.GetMethod(method).Invoke(null, new object[] { argument });
            }
            catch (Exception ex)
            {
                WriteResult(argument, $"error: {ex.InnerException ?? ex}");
            }
        }

        private void WriteResult(string command, string message)
        {
            try
            {
                File.AppendAllText(resultPath, $"[{DateTime.Now:HH:mm:ss}] > {command}\n{message}\n\n");
            }
            catch (IOException)
            {
            }
        }
    }
}
