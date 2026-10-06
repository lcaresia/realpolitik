using System;
using System.IO;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Log em disco de cada decisão (dossiê, prompt, respostas, uso e custo), um arquivo por nação por turno, em
    /// BepInEx\DiplomaciaIA\logs\&lt;partida&gt;\. Nunca contém a chave da API (ela só vai no cabeçalho HTTP).
    /// </summary>
    internal static class IaLog
    {
        internal static string Root => Path.Combine(Paths.BepInExRootPath, "DiplomaciaIA", "logs");

        private static string GameFolder(string gameGuid)
        {
            string id = string.IsNullOrEmpty(gameGuid) ? "sem-partida" : (gameGuid.Length > 8 ? gameGuid.Substring(0, 8) : gameGuid);
            return Path.Combine(Root, id);
        }

        internal static void Write(string gameGuid, DecisionJob job, DecisionResult result)
        {
            if (!IaConfig.SaveLogs.Value)
            {
                return;
            }
            try
            {
                string folder = GameFolder(gameGuid);
                Directory.CreateDirectory(folder);
                var root = new JObject
                {
                    ["turno"] = job.Turn,
                    ["imperio"] = job.EmpireIndex,
                    ["nome"] = job.NationName,
                    ["modelo"] = result.Calls.Count > 0 ? result.Calls[result.Calls.Count - 1].Provider : null,
                    ["raciocinio"] = job.ReasoningEffort ?? "desligado",
                    ["sistema"] = job.System,
                    ["dossie"] = job.User,
                    ["resposta"] = result.Raw,
                    ["raciocinioDoModelo"] = result.Reasoning,
                    ["erros"] = new JArray(result.Errors ?? new System.Collections.Generic.List<string>()),
                    ["erroFatal"] = result.FatalError,
                    ["custoUSD"] = result.CostUsd,
                    ["chamadas"] = JArray.FromObject(result.Calls),
                    ["avisos"] = result.Decision != null ? new JArray(result.Decision.Warnings) : new JArray(),
                };
                string file = Path.Combine(folder, $"T{job.Turn:000}_E{job.EmpireIndex}.json");
                File.WriteAllText(file, root.ToString(Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Falha ao gravar log: {ex.Message}");
            }
        }

        /// <summary>Log de uma chamada do conselho do jogador (reunião ou resposta): T&lt;turno&gt;_E&lt;jogador&gt;_conselho_&lt;rótulo&gt;.json.</summary>
        internal static void WriteCouncil(string gameGuid, int turn, int player, string label, string system, string user, string raw, string reasoning, string error, double cost)
        {
            if (!IaConfig.SaveLogs.Value)
            {
                return;
            }
            try
            {
                string folder = GameFolder(gameGuid);
                Directory.CreateDirectory(folder);
                var root = new JObject
                {
                    ["turno"] = turn,
                    ["imperio"] = player,
                    ["conselho"] = label,
                    ["sistema"] = system,
                    ["entrada"] = user,
                    ["resposta"] = raw,
                    ["raciocinioDoModelo"] = reasoning,
                    ["erro"] = error,
                    ["custoUSD"] = cost,
                };
                string file = Path.Combine(folder, $"T{turn:000}_E{player}_conselho_{label}.json");
                File.WriteAllText(file, root.ToString(Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Falha ao gravar log do conselho: {ex.Message}");
            }
        }

        /// <summary>
        /// Apaga os logs de turnos antigos da partida (mantém os últimos N do .cfg). Os turnos "do futuro" (de quando um
        /// save mais antigo é carregado) ficam: são o material da bancada (_Modding\tools\ia-bench) e o mesmo turno jogado
        /// de novo sobrescreve o arquivo. Antes eles eram apagados, e assim se perderam os logs da Teste16.
        /// </summary>
        internal static void Prune(string gameGuid, int currentTurn)
        {
            try
            {
                string folder = GameFolder(gameGuid);
                if (!Directory.Exists(folder))
                {
                    return;
                }
                int keepFrom = currentTurn - Math.Max(1, IaConfig.LogTurnsKept.Value);
                foreach (string file in Directory.GetFiles(folder, "T*_E*.json"))
                {
                    string name = Path.GetFileName(file);
                    int underscore = name.IndexOf('_');
                    if (underscore > 1 && int.TryParse(name.Substring(1, underscore - 1), out int turn) && turn < keepFrom)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
