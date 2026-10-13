using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Civics chosen by the language AI (melhorias-jogando item 34, part 2):
    /// - choose_civic: OrderActivateCivic for a civic waiting for a choice (CV code of the dossier) with the chosen option;
    /// - while the language AI answers for a nation, its native AI does not enact civics (NativeAiLocks.CivicPatch);
    /// - a civic left waiting for FallbackTurns turns while affordable goes back to the native AI, so a quiet nation never
    ///   stays without laws forever.
    /// </summary>
    internal static class CivicFlow
    {
        internal const int FallbackTurns = 5;

        /// <summary>Choosable civic of the dossier: definition name and option names, in the game's choice order.</summary>
        internal sealed class CivicRef
        {
            public string Name;
            public List<string> Options = new List<string>();
        }

        internal static void Choose(ActionIntent intent, MajorEmpire empire)
        {
            DepartmentOfDevelopment development = empire.DepartmentOfDevelopment;
            int index = development.GetCivicIndex(new StaticString(intent.CivicName ?? string.Empty));
            if (index < 0)
            {
                ActionExecutor.Report(intent, false, "cívico desconhecido");
                return;
            }
            Civic civic = development.Civics.Data[index];
            int choices = civic.CivicDefinition?.Choices?.Length ?? 0;
            if (intent.Choice < 0 || intent.Choice >= choices)
            {
                ActionExecutor.Report(intent, false, "opção inválida para esse cívico");
                return;
            }
            CivicActionFailureFlag flags = CivicActionFailureFlag.None;
            development.FillCivicActivateFailureFlags(index, ref flags);
            if (flags != CivicActionFailureFlag.None)
            {
                ActionExecutor.Report(intent, false, "o jogo não permite agora: " + Explain(flags));
                return;
            }
            string choiceName = civic.CivicDefinition.Choices[intent.Choice].Name.ToString();
            string label = $"«{GameGlossary.Title(intent.CivicName) ?? intent.CivicName}»: «{GameGlossary.Title(choiceName) ?? choiceName}»";
            ActionExecutor.Post(new OrderActivateCivic { CivicName = new StaticString(intent.CivicName), ChoiceIndex = intent.Choice }, intent, null, next =>
            {
                Civic after = development.Civics.Data[index];
                bool ok = after.CivicStatus == CivicStatuses.Enacted && after.ActiveChoiceName.ToString() == choiceName;
                ActionExecutor.Report(next, ok, ok ? "lei adotada: " + label : "o jogo não adotou " + label);
            });
        }

        internal static string Explain(CivicActionFailureFlag flags)
        {
            var parts = new List<string>();
            if ((flags & CivicActionFailureFlag.MustBeAvailable) != 0)
            {
                parts.Add("o cívico não está esperando escolha");
            }
            if ((flags & CivicActionFailureFlag.NotEnoughInfluence) != 0)
            {
                parts.Add("influência insuficiente");
            }
            if ((flags & CivicActionFailureFlag.CivicForbiddenByOther) != 0)
            {
                parts.Add("proibido por outra lei sua");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : flags.ToString();
        }

        // ---------------- Fallback to the native AI (main thread) ----------------

        private static Dictionary<long, int> waitingSince = new Dictionary<long, int>();

        /// <summary>
        /// Every second (AiModule.UpdateLocks): for each nation, since when each civic has been waiting for a choice that the
        /// nation can afford. A wait of FallbackTurns turns hands that nation's civics back to the native AI.
        /// </summary>
        internal static void TrackFallbacks(int turn)
        {
            MajorEmpire[] majors = Amplitude.Mercury.Sandbox.Sandbox.MajorEmpires;
            if (majors == null)
            {
                NativeAiLocks.SetCivicFallback(null);
                return;
            }
            var seen = new Dictionary<long, int>();
            var fallback = new HashSet<int>();
            foreach (MajorEmpire empire in majors)
            {
                if (empire == null || !empire.IsAlive || !NativeAiLocks.IsLlmActive(empire.Index))
                {
                    continue;
                }
                DepartmentOfDevelopment development = empire.DepartmentOfDevelopment;
                var civics = development.Civics;
                for (int k = 0; k < civics.Length; k++)
                {
                    CivicActionFailureFlag flags = CivicActionFailureFlag.None;
                    development.FillCivicActivateFailureFlags(k, ref flags);
                    if (flags != CivicActionFailureFlag.None)
                    {
                        continue;
                    }
                    long key = ((long)empire.Index << 32) | (uint)k;
                    int first = waitingSince.TryGetValue(key, out int known) && known <= turn ? known : turn;
                    seen[key] = first;
                    if (turn - first >= FallbackTurns)
                    {
                        fallback.Add(empire.Index);
                    }
                }
            }
            waitingSince = seen;
            NativeAiLocks.SetCivicFallback(fallback);
        }

        /// <summary>Turns a civic of this nation has been waiting (affordable), for the dossier; -1 = not tracked.</summary>
        internal static int WaitingTurns(int empire, int civicIndex, int turn) =>
            waitingSince.TryGetValue(((long)empire << 32) | (uint)civicIndex, out int first) ? turn - first : -1;
    }
}
