using System;
using Amplitude.AI.Heuristics;
using Amplitude.Mercury.AI.Brain;
using Amplitude.Mercury.AI.Brain.Analysis.Diplomacy;
using Amplitude.Mercury.AI.Brain.AnalysisData.Relation;
using Amplitude.Mercury.Interop.AI.Data;
using Amplitude.Mercury.Interop.AI.Entities;
using HarmonyLib;
using DiplomaticAction = Amplitude.Mercury.Data.Simulation.DiplomaticAction;
using DiplomaticGrievanceAction = Amplitude.Mercury.Data.Simulation.DiplomaticGrievanceAction;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Big decisions belong to the language AI (design §11.0). While it answers for a nation, that nation's native AI
    /// does not declare war, does not propose or break treaties and agreements, does not answer other nations'
    /// proposals (they wait for the language AI, via ProposalHold), does not turn grievances into demands nor forgive
    /// them, and does not answer, withdraw or take demands to the Congress. It still handles everything else (build,
    /// research, move troops, fight) and the demands enforced by the consulate, which the game forces to be answered in
    /// the same turn.
    /// The lock only applies to nations that decided recently: if the API goes down, the lock drops by itself (F1).
    /// The patches run on the native AI thread: the table is an array swapped whole, never edited in place.
    /// </summary>
    internal static class NativeAiLocks
    {
        private static volatile bool[] llmActive = new bool[0];
        private static volatile bool errorLogged;
        private static int blockedTreaties;
        private static int blockedGrievances;
        private static int blockedDemands;
        private static int lockedWarChecks;
        private static volatile string lastBlocked;

        /// <summary>Summary for "ai status": locked nations and what the lock has held back so far.</summary>
        internal static string Describe()
        {
            bool[] current = llmActive;
            var locked = new System.Collections.Generic.List<string>();
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i])
                {
                    locked.Add("E" + i);
                }
            }
            return $"native AI locks: {(locked.Count > 0 ? string.Join(", ", locked) : "none")} · treaty initiatives held: {blockedTreaties}"
                + $" · grievances held: {blockedGrievances} · demand answers held: {blockedDemands}"
                + $"{(lastBlocked != null ? $" (last: {lastBlocked})" : string.Empty)} · war checks locked: {lockedWarChecks}"
                + $" · surrenders held: {blockedSurrenders} · {DescribeMinors()} · {DescribeCongress()}";
        }

        /// <summary>Called by the main thread (AiModule) every second.</summary>
        internal static void SetActive(bool[] active)
        {
            llmActive = active ?? new bool[0];
        }

        private const string CarryKey = "CurrencyMod.NativeAiLocks.Active";

        /// <summary>
        /// Hot reload: the new generation starts with an empty table until AiModule's first second, and in that gap the
        /// native AI released at once everything the lock had been holding (at T75 of a test, 7 Congress crises that no
        /// language-AI nation asked for). ModEntry.Stop stores the table and Start restores it before the patches.
        /// </summary>
        internal static void Carry()
        {
            AppDomain.CurrentDomain.SetData(CarryKey, (bool[])llmActive.Clone());
        }

        internal static void RestoreCarried()
        {
            if (AppDomain.CurrentDomain.GetData(CarryKey) is bool[] carried)
            {
                llmActive = carried;
                AppDomain.CurrentDomain.SetData(CarryKey, null);
                int count = 0;
                foreach (bool locked in carried)
                {
                    count += locked ? 1 : 0;
                }
                Plugin.Log?.LogInfo($"[IA] Native AI locks carried over from the previous generation: {count} nation(s), already before the patches.");
            }
        }

        internal static bool IsLlmActive(int empire)
        {
            bool[] current = llmActive;
            return empire >= 0 && empire < current.Length && current[empire];
        }

        [HarmonyPatch(typeof(ComputeCanDeclareWar), nameof(ComputeCanDeclareWar.Process))]
        private static class WarPatch
        {
            private static void Postfix(ComputeCanDeclareWar __instance)
            {
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    if (brain == null || !IsLlmActive(brain.EmpireIndex))
                    {
                        return;
                    }
                    foreach (DiplomaticEmbassy embassy in brain.ControlledEmpire.AliveEmbassies)
                    {
                        brain.GetAnalysisData<RelationData, DiplomaticRelation>(embassy.Relation, out RelationData data);
                        if (data.CanDeclareWar.Value)
                        {
                            System.Threading.Interlocked.Increment(ref lockedWarChecks);
                        }
                        data.CanDeclareWar.Set(operand: false);
                    }
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        [HarmonyPatch(typeof(ComputeTreatyAction), nameof(ComputeTreatyAction.Process))]
        private static class TreatyPatch
        {
            private static void Postfix(ComputeTreatyAction __instance)
            {
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    if (brain == null || !IsLlmActive(brain.EmpireIndex))
                    {
                        return;
                    }
                    foreach (DiplomaticEmbassy embassy in brain.ControlledEmpire.AliveEmbassies)
                    {
                        brain.GetAnalysisData<RelationData, DiplomaticRelation>(embassy.Relation, out RelationData data);
                        DiplomaticAction chosen = data.ChosenDiplomaticActionTreaty.Value;
                        if (IsInitiative(chosen))
                        {
                            System.Threading.Interlocked.Increment(ref blockedTreaties);
                            lastBlocked = $"E{brain.EmpireIndex} {chosen} → E{embassy.OtherEmpireIndex}";
                            // DiplomaticAction.Count = "no action" for the ManageTreaties generator.
                            data.ChosenDiplomaticActionTreaty = new HeuristicValue<DiplomaticAction>(DiplomaticAction.Count);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        /// <summary>Grievances: the native AI neither turns them into demands nor forgives them (tools exigir and perdoar_queixas).</summary>
        [HarmonyPatch(typeof(ComputeGrievanceActions), nameof(ComputeGrievanceActions.Process))]
        private static class GrievancePatch
        {
            private static void Postfix(ComputeGrievanceActions __instance)
            {
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    if (brain == null || !IsLlmActive(brain.EmpireIndex))
                    {
                        return;
                    }
                    foreach (DiplomaticEmbassy embassy in brain.ControlledEmpire.AliveEmbassies)
                    {
                        brain.GetAnalysisData<RelationData, DiplomaticRelation>(embassy.Relation, out RelationData data);
                        ChosenGrievanceDiplomaticAction[] chosen = data.ChosenDiplomaticActionForGrievances;
                        for (int i = 0; chosen != null && i < chosen.Length; i++)
                        {
                            if (chosen[i].Action.Value == DiplomaticGrievanceAction.Count)
                            {
                                continue;
                            }
                            System.Threading.Interlocked.Increment(ref blockedGrievances);
                            lastBlocked = $"E{brain.EmpireIndex} {chosen[i].Action.Value} → E{embassy.OtherEmpireIndex}";
                            // Count = "nothing to do" for the ManageGrievances generator.
                            chosen[i].Action = new HeuristicValue<DiplomaticGrievanceAction>(DiplomaticGrievanceAction.Count);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        /// <summary>
        /// Demands: accepting, refusing, stalling, withdrawing, taking to the Congress (and, on escalation, breaking an
        /// alliance) belong to the language AI. The demand enforced by the consulate stays with the native AI: the game
        /// blocks the end of turn until it is answered.
        /// </summary>
        [HarmonyPatch(typeof(ComputeDemandsAction), nameof(ComputeDemandsAction.Process))]
        private static class DemandsPatch
        {
            private static void Postfix(ComputeDemandsAction __instance)
            {
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    if (brain == null || !IsLlmActive(brain.EmpireIndex))
                    {
                        return;
                    }
                    foreach (DiplomaticEmbassy embassy in brain.ControlledEmpire.AliveEmbassies)
                    {
                        if (embassy.AnyEnforcedDemand)
                        {
                            continue;
                        }
                        brain.GetAnalysisData<RelationData, DiplomaticRelation>(embassy.Relation, out RelationData data);
                        DiplomaticAction chosen = data.ChosenDiplomaticActionDemand.Value;
                        if (chosen == DiplomaticAction.Count)
                        {
                            continue;
                        }
                        System.Threading.Interlocked.Increment(ref blockedDemands);
                        lastBlocked = $"E{brain.EmpireIndex} {chosen} → E{embassy.OtherEmpireIndex}";
                        data.ChosenDiplomaticActionDemand = new HeuristicValue<DiplomaticAction>(DiplomaticAction.Count);
                    }
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        /// <summary>
        /// Proposing, breaking and also answering treaties and agreements: all of it belongs to the language AI. The
        /// proposals it must answer stay pending via ProposalHold (option B of F3) until it answers.
        /// </summary>
        private static bool IsInitiative(DiplomaticAction action)
        {
            switch (action)
            {
                case DiplomaticAction.SignTreaty:
                case DiplomaticAction.CounterTreaty:
                case DiplomaticAction.IgnoreTreaty:
                case DiplomaticAction.SignAgreement:
                case DiplomaticAction.CounterAgreement:
                case DiplomaticAction.IgnoreAgreement:
                case DiplomaticAction.ProposeAllianceTreaty:
                case DiplomaticAction.ProposeEndWarTreaty:
                case DiplomaticAction.ProposeEndCrisisTreaty:
                case DiplomaticAction.ProposeEconomicalAgreement:
                case DiplomaticAction.ProposeInformationAgreement:
                case DiplomaticAction.ProposeCulturalAgreement:
                case DiplomaticAction.ProposeMilitaryAgreement:
                case DiplomaticAction.DeclareEndOfAlliance:
                case DiplomaticAction.BreakEconomicalAgreement:
                case DiplomaticAction.BreakInformationAgreement:
                case DiplomaticAction.BreakCulturalAgreement:
                case DiplomaticAction.BreakMilitaryAgreement:
                    return true;
                default:
                    return false;
            }
        }

        // ---------------- Surrender (research\surrender.md §3.2) ----------------

        private static int blockedSurrenders;
        /// <summary>
        /// Relations (responder &lt;&lt; 16 | other) in which the native AI answers a surrender again: the language AI did
        /// not answer within the hold deadline. Published by the main thread (ProposalHold.TrackSurrenders).
        /// </summary>
        private static volatile System.Collections.Generic.HashSet<int> surrenderFallback = new System.Collections.Generic.HashSet<int>();

        internal static void SetSurrenderFallback(System.Collections.Generic.HashSet<int> pairs)
        {
            surrenderFallback = pairs ?? new System.Collections.Generic.HashSet<int>();
        }

        /// <summary>The native AI does not open a surrender offer for the nation (oferecer_rendicao belongs to the language AI).</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.Surrender), "GetParents")]
        private static class SurrenderOfferPatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.Surrender __instance) => !Blocks(__instance.Brain);
        }

        /// <summary>Nor opens a forced surrender (impor_rendicao).</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.ForceSurrender), "GetParents")]
        private static class SurrenderForcePatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.ForceSurrender __instance) => !Blocks(__instance.Brain);
        }

        /// <summary>Nor touches the terms of the draft the mod's executor is building.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.FillSurrenderTerms), "GetContexts")]
        private static class SurrenderTermsPatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.FillSurrenderTerms __instance) => !Blocks(__instance.Brain);
        }

        /// <summary>
        /// Nor sends, accepts or refuses: responder_rendicao belongs to the language AI. Per relation: when the hold expires
        /// with no answer (or the API goes down and the whole lock disappears), the native AI answers in that relation.
        /// </summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.EndSurrender), "TryComputeAction")]
        private static class SurrenderEndPatch
        {
            private static void Postfix(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.EndSurrender __instance, DiplomaticEmbassy context, ref bool __result, ref DiplomaticAction result)
            {
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    if (!__result || brain == null || context == null || !IsLlmActive(brain.EmpireIndex))
                    {
                        return;
                    }
                    if (surrenderFallback.Contains((brain.EmpireIndex << 16) | context.OtherEmpireIndex))
                    {
                        return;
                    }
                    System.Threading.Interlocked.Increment(ref blockedSurrenders);
                    lastBlocked = $"E{brain.EmpireIndex} {result} → E{context.OtherEmpireIndex}";
                    __result = false;
                    result = DiplomaticAction.Count;
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        private static bool Blocks(MajorEmpireBrain brain)
        {
            try
            {
                if (brain != null && IsLlmActive(brain.EmpireIndex))
                {
                    System.Threading.Interlocked.Increment(ref blockedSurrenders);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogOnce(ex);
            }
            return false;
        }

        // ---------------- Independent peoples ----------------

        private static int blockedMinorTreaties;
        private static int keptPatronage;

        /// <summary>
        /// Patronage chosen by the language AI (patrocinar), per (empire, people): the native AI re-plans the investment
        /// every so often and would undo the choice. In memory only (reload or load: the native AI decides again).
        /// </summary>
        private static volatile System.Collections.Generic.Dictionary<int, int> patronage = new System.Collections.Generic.Dictionary<int, int>();

        internal static void SetPatronage(int empire, int minor, Amplitude.Mercury.Data.Simulation.PatronageDefinition.PatronageInvestment money,
            Amplitude.Mercury.Data.Simulation.PatronageDefinition.PatronageInvestment influence)
        {
            var copy = new System.Collections.Generic.Dictionary<int, int>(patronage);
            copy[(empire << 16) | minor] = ((int)money << 8) | (int)influence;
            patronage = copy;
        }

        /// <summary>Whole table, published by AiModule every second from the nations' saved state.</summary>
        internal static void SetPatronageTable(System.Collections.Generic.Dictionary<int, int> table)
        {
            patronage = table ?? new System.Collections.Generic.Dictionary<int, int>();
        }

        internal static string DescribeMinors() => $"minor peoples: vassalizations/annexations held {blockedMinorTreaties} · patronages kept {keptPatronage}";

        /// <summary>Vassalizing and annexing an independent people are big decisions: only through the language AI (tratado_povo).</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.MinorRelation.EnactMinorPuppetTreaty), "IsTreatyValid")]
        private static class PuppetPatch
        {
            private static void Postfix(Amplitude.Mercury.AI.Brain.Generators.MinorRelation.EnactMinorPuppetTreaty __instance, ref bool __result)
            {
                BlockMinorTreaty(__instance.Brain, ref __result);
            }
        }

        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.MinorRelation.EnactMinorAssimilationTreaty), "IsTreatyValid")]
        private static class AssimilationPatch
        {
            private static void Postfix(Amplitude.Mercury.AI.Brain.Generators.MinorRelation.EnactMinorAssimilationTreaty __instance, ref bool __result)
            {
                BlockMinorTreaty(__instance.Brain, ref __result);
            }
        }

        private static void BlockMinorTreaty(MajorEmpireBrain brain, ref bool result)
        {
            try
            {
                if (result && brain != null && IsLlmActive(brain.EmpireIndex))
                {
                    result = false;
                    System.Threading.Interlocked.Increment(ref blockedMinorTreaties);
                }
            }
            catch (Exception ex)
            {
                LogOnce(ex);
            }
        }

        /// <summary>
        /// People with patronage chosen by the language AI: the native AI's patronage generators (start and stop
        /// patronizing) do not even consider the people. Only swapping the levels of its order (below) left the native AI
        /// unsatisfied, and it repeated the order endlessly (821 orders in 10 minutes on turn 113).
        /// </summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.MinorRelation.StartPatronizingMinor), "IsRelevant")]
        private static class StartPatronagePatch
        {
            private static void Postfix(Amplitude.Mercury.AI.Brain.Generators.MinorRelation.StartPatronizingMinor __instance, MinorEmpire minorEmpire, ref bool __result)
            {
                SkipDirectedMinor(__instance.Brain, minorEmpire, ref __result);
            }
        }

        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.MinorRelation.StopPatronizingMinor), "IsRelevant")]
        private static class StopPatronagePatch
        {
            private static void Postfix(Amplitude.Mercury.AI.Brain.Generators.MinorRelation.StopPatronizingMinor __instance, MinorEmpire minorEmpire, ref bool __result)
            {
                SkipDirectedMinor(__instance.Brain, minorEmpire, ref __result);
            }
        }

        private static void SkipDirectedMinor(MajorEmpireBrain brain, MinorEmpire minor, ref bool result)
        {
            try
            {
                if (result && brain != null && minor != null && IsLlmActive(brain.EmpireIndex) && patronage.ContainsKey((brain.EmpireIndex << 16) | minor.EmpireIndex))
                {
                    result = false;
                }
            }
            catch (Exception ex)
            {
                LogOnce(ex);
            }
        }

        /// <summary>Safety net: if an investment order from the native AI for that people still goes out, it carries the language AI's levels.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Actuators.PatronizeMinor), "CreateOrder")]
        private static class PatronagePatch
        {
            private static void Postfix(Amplitude.Mercury.AI.Brain.Actuators.PatronizeMinor __instance, Amplitude.Mercury.Interop.Order __result)
            {
                try
                {
                    if (!(__result is Amplitude.Mercury.Interop.OrderSetPatronageInvestmentLevels order))
                    {
                        return;
                    }
                    int empire = __instance.Brain.EmpireIndex;
                    if (!IsLlmActive(empire) || !patronage.TryGetValue((empire << 16) | order.MinorEmpireIndex, out int levels))
                    {
                        return;
                    }
                    order.MoneyInvestment = (Amplitude.Mercury.Data.Simulation.PatronageDefinition.PatronageInvestment)((levels >> 8) & 0xFF);
                    order.InfluenceInvestment = (Amplitude.Mercury.Data.Simulation.PatronageDefinition.PatronageInvestment)(levels & 0xFF);
                    System.Threading.Interlocked.Increment(ref keptPatronage);
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        // ---------------- Congress (research\congress.md §4.2) ----------------

        private static int blockedVerdicts;
        private static int keptStances;
        private static readonly System.Collections.Generic.HashSet<long> ImposedSeen = new System.Collections.Generic.HashSet<long>();
        /// <summary>
        /// Published by the main thread (CongressFlow.TrackFallbacks): verdicts (loser &lt;&lt; 16 | winner) and
        /// 1st-session presidents for which the native AI acts again because the deadline expired, and the language AI's
        /// answer for each imposed law ((empire &lt;&lt; 32) | osmosis event → true = refuse).
        /// </summary>
        private static volatile System.Collections.Generic.HashSet<int> verdictFallback = new System.Collections.Generic.HashSet<int>();
        private static volatile System.Collections.Generic.HashSet<int> leaderFallback = new System.Collections.Generic.HashSet<int>();
        private static volatile System.Collections.Generic.Dictionary<string, LawStance> lawStances = new System.Collections.Generic.Dictionary<string, LawStance>();

        /// <summary>What a language-AI nation wants from a Congress law: the option it voted for and what to do with the
        /// imposed law if it loses (se_perder). Key: "empire|civic".</summary>
        internal sealed class LawStance
        {
            public string Voted;
            public string IfLost;
        }

        internal static string LawStanceKey(int empire, string civic) => empire + "|" + civic;

        /// <summary>Result of the last law vote (civic, winning option, president): recognizes the law imposed by the
        /// Congress when the game has already swapped its effects (after loading the save). Published by the main thread.</summary>
        private static volatile string[] lastLawResult = new string[0];

        internal static void SetLastLawResult(string civic, string choice, int leader)
        {
            lastLawResult = civic == null || choice == null ? new string[0] : new[] { civic, choice, leader.ToString() };
        }

        private static bool IsCongressLaw(Amplitude.Mercury.Interop.CulturalOsmosisEventInfo info)
        {
            if (info.OsmosisEffectsDefinitionName.ToString() == "OsmosisEffectsInternationalCivicVoteResult")
            {
                return true;
            }
            string[] last = lastLawResult;
            return last.Length == 3 && info.ItemName.ToString() == last[0] && info.CivicChoiceName.ToString() == last[1] && info.ProposingEmpireIndex.ToString() == last[2];
        }

        internal static void SetCongressFallback(System.Collections.Generic.HashSet<int> verdicts, System.Collections.Generic.HashSet<int> leaders,
            System.Collections.Generic.Dictionary<string, LawStance> stances)
        {
            verdictFallback = verdicts ?? new System.Collections.Generic.HashSet<int>();
            leaderFallback = leaders ?? new System.Collections.Generic.HashSet<int>();
            lawStances = stances ?? new System.Collections.Generic.Dictionary<string, LawStance>();
        }

        internal static string DescribeCongress() =>
            $"Congress: verdicts held {blockedVerdicts}, handed back to the native AI {verdictFallback.Count} · imposed laws answered as the language AI wanted {keptStances} · consulate: initiatives held {BlockedConsulateActions}"
            + (leaderFallback.Count > 0 ? $" · 1st session proposed by the native AI of E{string.Join(", E", leaderFallback)}" : string.Empty);

        private static bool BlocksCongress(MajorEmpireBrain brain)
        {
            try
            {
                return brain != null && IsLlmActive(brain.EmpireIndex);
            }
            catch (Exception ex)
            {
                LogOnce(ex);
                return false;
            }
        }

        internal static int BlockedConsulateActions;

        /// <summary>
        /// Consulate, "demands and crises" of design §11.0: the native AI does not use leverage (enforced demand, sanctions,
        /// reduce war score, unwanted troops, reveal agents) on behalf of a language-AI nation.
        /// </summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.DoDiplomaticLeverageAction), "GetContexts")]
        private static class ConsulateLeveragePatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.DoDiplomaticLeverageAction __instance)
            {
                if (!BlocksCongress(__instance.Brain))
                {
                    return true;
                }
                System.Threading.Interlocked.Increment(ref BlockedConsulateActions);
                return false;
            }
        }

        /// <summary>
        /// Nor proposes or cancels consulate agreements. Answering (accept, counter, refuse) an agreement someone else
        /// proposed stays with it, like the enforced demand: the language AI has no such action and the proposal would stall.
        /// </summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.ManageConsulateAgreements), "TryContinue")]
        private static class ConsulateAgreementPatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.ManageConsulateAgreements __instance, Amplitude.Mercury.Interop.ConsulatAgreementAction action)
            {
                if ((action != Amplitude.Mercury.Interop.ConsulatAgreementAction.Propose && action != Amplitude.Mercury.Interop.ConsulatAgreementAction.Cancel)
                    || !BlocksCongress(__instance.Brain))
                {
                    return true;
                }
                System.Threading.Interlocked.Increment(ref BlockedConsulateActions);
                return false;
            }
        }

        /// <summary>The native AI does not propose a law when presiding (propor_votacao); only in a 1st session stalled past its deadline.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.International.StartInternationalCivicVote), "GetContexts")]
        private static class CongressProposePatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.International.StartInternationalCivicVote __instance)
            {
                MajorEmpireBrain brain = __instance.Brain;
                return brain == null || leaderFallback.Contains(brain.EmpireIndex) || !BlocksCongress(brain);
            }
        }

        /// <summary>Nor votes or bribes on laws (votar_congresso, subornar). Abstaining is the language AI's choice.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.International.ManageInternationalCivicVote), "GetContexts")]
        private static class CongressLawVotePatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.International.ManageInternationalCivicVote __instance) => !BlocksCongress(__instance.Brain);
        }

        /// <summary>Nor votes or bribes on crises.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.International.ManageInternationalCrisisVote), "GetContexts")]
        private static class CongressCrisisVotePatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.International.ManageInternationalCrisisVote __instance) => !BlocksCongress(__instance.Brain);
        }

        /// <summary>Nor contributes to the ideological consensus (contribuir_consenso).</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.International.ContributeToInternationalIdeologicalConsensus), "GetContexts")]
        private static class CongressConsensusPatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.International.ContributeToInternationalIdeologicalConsensus __instance) => !BlocksCongress(__instance.Brain);
        }

        /// <summary>
        /// Nor answers the verdict of a lost crisis (responder_congresso): without this lock, the native AI of a language-AI
        /// nation would declare a surprise war on its own. Per relation: once the deadline expires, the native AI answers.
        /// </summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.International.ResolveInternationalCrisisVote), "GenerateDesires")]
        private static class CongressVerdictPatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.International.ResolveInternationalCrisisVote __instance, DiplomaticEmbassy context)
            {
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    if (brain == null || context == null || !IsLlmActive(brain.EmpireIndex) || verdictFallback.Contains((brain.EmpireIndex << 16) | context.OtherEmpireIndex))
                    {
                        return true;
                    }
                    System.Threading.Interlocked.Increment(ref blockedVerdicts);
                    return false;
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                    return true;
                }
            }
        }

        /// <summary>
        /// Law imposed by the Congress: the native AI answers in the same turn (otherwise the end of turn refuses and charges
        /// influence), but with the answer the language AI chose: voted for the imposed option = adopt; otherwise, se_perder.
        /// The table is published before the vote closes, and the decision is made when the order is created (the native AI
        /// answers in the first second).
        /// </summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Actuators.DoOsmosisAction), "CreateOrder")]
        private static class ImposedLawPatch
        {
            private static void Postfix(Amplitude.Mercury.AI.Brain.Actuators.DoOsmosisAction __instance, ref Amplitude.Mercury.Interop.Order __result)
            {
                try
                {
                    int empire = __instance.Brain.EmpireIndex;
                    int index = __instance.Task.CulturalOsmosisEventIndex;
                    System.Collections.Generic.Dictionary<string, LawStance> stances = lawStances;
                    if (__result == null || stances.Count == 0 || !IsLlmActive(empire))
                    {
                        return;
                    }
                    var events = Amplitude.Mercury.Interop.AI.Snapshots.Culture.CulturalOsmosisEventInfo.Data;
                    if (events == null || index < 0 || index >= events.Length)
                    {
                        return;
                    }
                    Amplitude.Mercury.Interop.CulturalOsmosisEventInfo info = events[index];
                    if (info.EventType != Amplitude.Mercury.Interop.CulturalOsmosisEventInfo.Type.CivicsShakedown || !IsCongressLaw(info)
                        || !stances.TryGetValue(LawStanceKey(empire, info.ItemName.ToString()), out LawStance stance))
                    {
                        return;
                    }
                    bool discard;
                    if (stance.Voted != null && stance.Voted == info.CivicChoiceName.ToString())
                    {
                        discard = false;
                    }
                    else if (stance.IfLost == "recusar")
                    {
                        // Without influence to refuse, the end of turn would refuse and charge anyway: adopt.
                        discard = info.DiscardFailureFlags == Amplitude.Mercury.Simulation.FailureFlags.None;
                    }
                    else if (stance.IfLost == "adotar")
                    {
                        discard = false;
                    }
                    else
                    {
                        return;
                    }
                    bool nativeDiscard = __result is Amplitude.Mercury.Interop.OrderDiscardCulturalOsmosisEvent;
                    // Count and log once per event (the actuator may request the order again); the turn is part of the key
                    // because the event index is reused later.
                    bool first;
                    lock (ImposedSeen)
                    {
                        long turn = Amplitude.Mercury.Sandbox.SandboxManager.Sandbox?.Turn ?? 0;
                        first = ImposedSeen.Add((turn << 40) | ((long)empire << 32) | (uint)index);
                    }
                    if (first)
                    {
                        System.Threading.Interlocked.Increment(ref keptStances);
                        Plugin.Log?.LogInfo($"[IA] Law imposed on E{empire} ({info.ItemName} → {info.CivicChoiceName}): {(discard ? "refused" : "adopted")}, as the language AI wanted"
                            + (discard != nativeDiscard ? $" (the native AI would have {(nativeDiscard ? "refused" : "adopted")})." : " (the native AI would do the same)."));
                    }
                    if (discard == nativeDiscard)
                    {
                        return;
                    }
                    __result = discard
                        ? (Amplitude.Mercury.Interop.Order)new Amplitude.Mercury.Interop.OrderDiscardCulturalOsmosisEvent { CulturalOsmosisEventIndex = index }
                        : new Amplitude.Mercury.Interop.OrderPurchaseCulturalOsmosisEvent { CulturalOsmosisEventIndex = index };
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        private static void LogOnce(Exception ex)
        {
            if (!errorLogged)
            {
                errorLogged = true;
                Plugin.Log?.LogError($"[IA] Native AI lock: {ex}");
            }
        }
    }
}
