using System;
using System.IO;
using System.Reflection;
using BepInEx;
using Mono.Cecil;
using UnityEngine;

namespace CurrencyMod.Loader
{
    /// <summary>
    /// Plugin fixo que carrega o núcleo do CurrencyMod (BepInEx\CurrencyModCore\CurrencyMod.dll)
    /// e o recarrega a quente quando a DLL muda ou quando o comando "reload" chega.
    /// O núcleo expõe CurrencyMod.ModEntry com Start/Stop/Command (chamados por reflexão).
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
            // Canal de comandos de desenvolvimento: só existe em máquinas com a pasta _Modding (instalação de dev).
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
            // Espera a DLL parar de mudar (cópia do build) antes de recarregar.
            if (writeTime != pendingWriteTime)
            {
                pendingWriteTime = writeTime;
                pendingSince = Time.unscaledTime;
                return;
            }
            if (pendingSince >= 0 && Time.unscaledTime - pendingSince > 1.0f)
            {
                Logger.LogInfo("Nova versão do núcleo detectada, recarregando...");
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
                return; // arquivo ainda sendo escrito
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
                    WriteResult(line, entryType != null ? $"ok: núcleo recarregado (geração {generation})" : "erro: o núcleo não carregou (ver BepInEx\\LogOutput.log)");
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
                Logger.LogError($"Núcleo não encontrado em {corePath}.");
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
                Logger.LogInfo($"Núcleo carregado (geração {generation}).");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Falha ao carregar o núcleo: {ex}");
                entryType = null;
                // Sem isto a mesma DLL nunca mais era tentada (a data já constava como carregada) e o mod ficava
                // desligado até um build novo. Ex.: antivírus ou o build ainda segurando o arquivo. Tenta de novo em ~5 s.
                loadedWriteTime = default(DateTime);
                pendingSince = Time.unscaledTime + 4f;
            }
        }

        /// <summary>
        /// A Unity associa componentes (MonoBehaviour) pelo nome do assembly. Se toda versão
        /// recarregada tivesse o mesmo nome, AddComponent usaria as classes da primeira versão.
        /// Por isso cada geração recebe um nome único antes de ser carregada.
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
                    Logger.LogError($"Falha ao parar o núcleo: {ex}");
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
                WriteResult(argument, "erro: núcleo não carregado");
                return;
            }
            try
            {
                entryType.GetMethod(method).Invoke(null, new object[] { argument });
            }
            catch (Exception ex)
            {
                WriteResult(argument, $"erro: {ex.InnerException ?? ex}");
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
