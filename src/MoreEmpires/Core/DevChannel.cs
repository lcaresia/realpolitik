using System;
using System.IO;
using System.Linq;
using System.Text;
using Amplitude.Mercury.UI;
using BepInEx;
using UnityEngine;

namespace MoreEmpires
{
    /// <summary>
    /// Canal de comandos de desenvolvimento do MoreEmpires, separado do canal do CurrencyMod (que lê dev\cmd.txt):
    ///   entrada: _Modding\dev\moreempires_cmd.txt (uma linha por comando; o arquivo é apagado ao ser lido)
    ///   saída:   _Modding\dev\out\moreempires.txt (acrescenta)
    /// Só existe em máquinas com a pasta _Modding. Comandos: ajuda, status, opcoes, cores, nascimento, mapa, turnos [n],
    /// visao, lobby, ui, screenshot &lt;nome&gt;.
    /// </summary>
    internal static class DevChannel
    {
        private static string commandPath;
        private static string resultPath;
        private static string outDir;
        private static float nextPoll;

        internal static void Init()
        {
            string modding = Path.Combine(Paths.GameRootPath, "_Modding");
            if (!MeConfig.Canal || !Directory.Exists(modding))
            {
                return;
            }
            outDir = Path.Combine(modding, "dev", "out");
            Directory.CreateDirectory(outDir);
            commandPath = Path.Combine(modding, "dev", "moreempires_cmd.txt");
            resultPath = Path.Combine(outDir, "moreempires.txt");
            Log.Info("Canal de comandos: " + commandPath);
        }

        internal static void Poll()
        {
            if (commandPath == null || Time.unscaledTime < nextPoll)
            {
                return;
            }
            nextPoll = Time.unscaledTime + 0.5f;
            if (!File.Exists(commandPath))
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
                return;
            }
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }
                string answer;
                try
                {
                    answer = Execute(line);
                }
                catch (Exception ex)
                {
                    answer = "erro: " + ex;
                }
                Write(line, answer);
            }
        }

        private static void Write(string command, string answer)
        {
            try
            {
                File.AppendAllText(resultPath, $"[{DateTime.Now:HH:mm:ss}] > {command}\n{answer}\n\n", Encoding.UTF8);
            }
            catch (IOException)
            {
            }
        }

        internal static string Execute(string line)
        {
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int start = parts.Length > 0 && parts[0].Equals("me", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            string verb = parts.Length > start ? parts[start].ToLowerInvariant() : "ajuda";
            string arg = parts.Length > start + 1 ? parts[start + 1] : null;
            switch (verb)
            {
                case "status":
                    return Status();
                case "opcoes":
                case "opções":
                    return OptionTweaks.Report() + string.Join("\n", EraStarPages.Changes.ToArray());
                case "cores":
                    return Colors.Describe();
                case "nascimento":
                case "spawns":
                    return SpawnShuffle.Describe() + "\n" + SpawnCheck.Describe();
                case "mapa":
                    return SpawnCheck.Describe();
                case "turnos":
                    return TurnTimer.Describe(int.TryParse(arg, out int n) ? n : 30);
                case "visao":
                case "visão":
                    return VisibilityFast.CompareNow();
                case "lobby":
                    return LobbyLayout.Describe(UnityEngine.Object.FindObjectOfType<LobbyScreen_LobbySlotsPanel>());
                case "ui":
                    return UiProbe.Describe();
                case "imperios":
                case "impérios":
                    return EmpireReport.Describe();
                case "screenshot":
                    return Screenshot(arg);
                default:
                    return "Comandos: status | opcoes | cores | nascimento | mapa | turnos [n] | visao | imperios | lobby | ui | screenshot <nome>";
            }
        }

        private static string Status()
        {
            var text = new StringBuilder();
            text.AppendLine($"MoreEmpires {MoreEmpiresPlugin.Version}: MaxImperios {Limits.MaxImperios} ({(Limits.Active ? "ativo" : "original")}), cores {Limits.ColorCount}.");
            text.AppendLine($"Config: nascimento {MeConfig.NascimentoModo}, compactar lobby {MeConfig.Compactar}, ajustar banner {MeConfig.Banner}, " +
                            $"ajuste de mapa {MeConfig.AjusteMapa}, visibilidade {MeConfig.VisibilidadeModo}, medir turnos {MeConfig.Medir}.");
            text.AppendLine($"Personas recicladas: {Personas.Recycled}. Opções criadas: {OptionTweaks.CreateOptionCalls}.");
            text.AppendLine("Patches:");
            foreach (string line in MoreEmpiresPlugin.PatchReport)
            {
                text.AppendLine("  " + line);
            }
            return text.ToString();
        }

        private static string Screenshot(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                name = "moreempires_" + DateTime.Now.ToString("HHmmss");
            }
            name = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            string path = Path.Combine(outDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            return "ok: " + path + " (gravado no fim do quadro)";
        }
    }
}
