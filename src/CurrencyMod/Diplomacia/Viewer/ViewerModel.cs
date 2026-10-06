using System.Collections.Generic;
using System.Linq;
using CurrencyMod.Diplomacia.Capture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia.Viewer
{
    /// <summary>Monta os jsons que o visualizador lê (na thread principal, a partir do estado da IA).</summary>
    internal static class ViewerModel
    {
        private static int version;

        internal static string BuildState(IaWorld world, IaRuntime runtime, WorldCapture capture, int turn, int playerIndex, ICollection<int> inFlight,
            string status, string statusLevel, string statusDetail)
        {
            var nations = new JArray();
            IEnumerable<int> empires = capture != null ? capture.AliveEmpires() : (world?.Nations.Select(n => n.EmpireIndex) ?? Enumerable.Empty<int>());
            foreach (int empire in empires)
            {
                IaNation nation = world?.Get(empire);
                runtime.Details.TryGetValue(empire, out NationDetail detail);
                CapturedEmpire captured = capture?.Empire(empire);
                bool human = captured?.Human ?? empire == playerIndex;
                var item = new JObject
                {
                    ["index"] = empire,
                    ["name"] = captured?.FullName ?? $"Império {empire + 1}",
                    ["culture"] = captured?.Culture,
                    ["era"] = captured?.EraName,
                    ["isHuman"] = human,
                    ["state"] = inFlight.Contains(empire) ? "pensando" : detail?.State ?? (human ? "jogador" : "sem dados"),
                    ["error"] = detail?.LastError,
                    ["detailVersion"] = detail?.Version ?? 0,
                    ["lastTurn"] = nation?.LastDecisionTurn ?? -1,
                    ["leader"] = nation?.Persona?.LeaderName,
                    ["traits"] = new JArray(nation?.Persona?.Traits ?? new List<string>()),
                    ["quirk"] = nation?.Persona?.Quirk,
                    ["diary"] = new JArray((nation?.Diary ?? new List<DiaryEntry>()).Select(d => new JObject { ["turn"] = d.Turn, ["text"] = d.Text })),
                    ["feelings"] = new JArray((nation?.Feelings ?? new Dictionary<int, Feelings>())
                        .OrderBy(p => p.Key)
                        .Select(p => new JObject
                        {
                            ["other"] = p.Key,
                            ["affection"] = p.Value.Affection,
                            ["trust"] = p.Value.Trust,
                            ["fear"] = p.Value.Fear,
                            ["anger"] = p.Value.Anger,
                            ["reason"] = p.Value.Reason,
                            ["turn"] = p.Value.Turn,
                        })),
                    ["notes"] = new JArray((nation?.Notes ?? new List<MemoryNote>()).Select(n => new JObject { ["turn"] = n.Turn, ["about"] = n.About, ["text"] = n.Text })),
                    ["actions"] = new JArray((nation?.Actions ?? new List<ActionRecord>()).Select(a => new JObject
                    {
                        ["turn"] = a.Turn,
                        ["name"] = a.Name,
                        ["json"] = a.Json,
                        ["status"] = a.Status,
                        ["summary"] = Summary(a),
                    })),
                    ["blocked"] = new JArray(nation?.BlockedEmpires ?? new List<int>()),
                };
                nations.Add(item);
            }

            var letters = new JArray();
            if (world != null)
            {
                foreach (Letter letter in world.Letters)
                {
                    letters.Add(new JObject
                    {
                        ["id"] = letter.Id,
                        ["from"] = letter.From,
                        ["to"] = letter.To,
                        ["type"] = letter.Type,
                        ["subject"] = letter.Subject,
                        ["text"] = letter.Text,
                        ["demand"] = letter.Demand,
                        ["deadline"] = letter.DeadlineTurns,
                        ["sentTurn"] = letter.SentTurn,
                        ["deliverTurn"] = letter.DeliverTurn,
                        ["readBy"] = new JArray(letter.ReadBy),
                        ["rejectedBy"] = new JArray(letter.RejectedBy),
                        ["checked"] = letter.InterceptionChecked,
                        ["interceptedBy"] = letter.InterceptedBy,
                        ["interceptedTurn"] = letter.InterceptedTurn,
                        ["interceptedVia"] = letter.InterceptedVia,
                        ["interceptedSpy"] = letter.InterceptedSpy,
                    });
                }
            }

            var root = new JObject
            {
                ["version"] = ++version,
                ["turn"] = turn,
                ["status"] = status,
                ["statusLevel"] = statusLevel,
                ["statusDetail"] = statusDetail,
                ["playerIndex"] = playerIndex,
                ["cost"] = world?.TotalCostUsd ?? 0,
                ["cap"] = IaConfig.SpendingCapUsd.Value,
                ["calls"] = world?.TotalCalls ?? 0,
                ["promptTokens"] = world?.TotalPromptTokens ?? 0,
                ["cacheHitTokens"] = world?.TotalCacheHitTokens ?? 0,
                ["completionTokens"] = world?.TotalCompletionTokens ?? 0,
                ["reasoningTokens"] = world?.TotalReasoningTokens ?? 0,
                ["inFlight"] = inFlight.Count,
                ["nations"] = nations,
                ["letters"] = letters,
                ["events"] = new JArray(runtime.Events),
            };
            return root.ToString(Formatting.None);
        }

        internal static string BuildDetail(NationDetail detail)
        {
            var root = new JObject
            {
                ["turn"] = detail.Turn,
                ["persona"] = detail.Persona,
                ["dossier"] = detail.Dossier,
                ["system"] = detail.System,
                ["user"] = detail.User,
                ["raw"] = detail.Raw,
                ["reasoning"] = detail.Reasoning,
                ["errors"] = new JArray(detail.Errors ?? new List<string>()),
                ["warnings"] = new JArray(detail.Warnings ?? new List<string>()),
                ["calls"] = JArray.FromObject(detail.Calls.Select(c => new
                {
                    turn = c.Turn,
                    attempt = c.Attempt,
                    ok = c.Ok,
                    error = c.Error,
                    errors = c.Errors,
                    seconds = c.Seconds,
                    prompt = c.Prompt,
                    hit = c.Hit,
                    miss = c.Miss,
                    completion = c.Completion,
                    reasoning = c.Reasoning,
                    cost = c.Cost,
                    finish = c.FinishReason,
                })),
            };
            return root.ToString(Formatting.None);
        }

        private static string Summary(ActionRecord action)
        {
            try
            {
                JObject json = JObject.Parse(action.Json);
                return string.Join(", ", json.Properties().Where(p => p.Name != "acao").Select(p => $"{p.Name}: {p.Value}"));
            }
            catch (System.Exception)
            {
                return action.Json;
            }
        }
    }
}
