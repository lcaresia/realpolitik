using System;
using System.Collections.Generic;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// F3 option B (design §15.1; research\diplomacy-native-timers.md): a treaty or agreement proposal that a nation
    /// driven by the language AI must answer stays truly pending, on the native screen, until it answers on the next
    /// turn (responder_tratado / responder_acordo).
    /// The game resolves proposals on its own in two places, and both are worked around by hiding these proposals only
    /// during the call (prefix hides, postfix and finalizer restore; all on the sandbox thread):
    /// - DiplomaticAncillary.ReadyForTurnFinishCompletion: holds the end of turn and, with "impatience", ignores or refuses;
    /// - DiplomaticAncillary.NewTurnBegin_ResetTreatyStatus: resets everything at the turn change.
    /// After [IA] TurnosParaResponderPropostas turns without an answer, or if the language AI stops (F1), the native
    /// rule comes back.
    /// </summary>
    internal static class ProposalHold
    {
        private struct Held
        {
            public DiplomaticRelation Relation;
            public bool Treaty;
            public TreatyStatus Status;
            /// <summary>Hidden surrender: the proposal index (DiplomaticState_War.ProposedSurrenderIndex), -1 = not one.</summary>
            public int Surrender;
        }

        // ---------------- Surrender (research\surrender.md §5.4) ----------------

        /// <summary>
        /// Turn in which each pending surrender appeared, by (winner, loser, forced): the game does not save the turn it
        /// was sent. Updated by the main thread; read by the sandbox thread (the whole dictionary is swapped).
        /// </summary>
        private static volatile Dictionary<long, int> surrenderSeen = new Dictionary<long, int>();

        private static long SurrenderKey(int winner, int loser, bool forced) => ((long)winner << 32) | ((long)(loser & 0xFFFF) << 1) | (forced ? 1L : 0L);

        /// <summary>
        /// Main thread, every second (IaModule.UpdateLocks): records when each pending surrender appeared and publishes
        /// the relations in which the native AI answers again (the language AI did not answer in time).
        /// </summary>
        internal static void TrackSurrenders(int turn)
        {
            DiplomaticAncillary ancillary = Sandbox.DiplomaticAncillary;
            if (ancillary?.DiplomaticRelations == null)
            {
                return;
            }
            var seen = new Dictionary<long, int>();
            var fallback = new HashSet<int>();
            Dictionary<long, int> before = surrenderSeen;
            int maxTurns = Math.Max(1, IaConfig.ProposalTurns.Value);
            foreach (DiplomaticRelation relation in ancillary.DiplomaticRelations)
            {
                if (!(relation?.DiplomaticState is DiplomaticState_War war) || war.ProposedSurrenderIndex < 0)
                {
                    continue;
                }
                SurrenderProposition p = ancillary.SurrenderAllocator.GetReferenceAt(war.ProposedSurrenderIndex);
                long key = SurrenderKey(p.WinnerEmpire, p.LoserEmpire, p.IsSurrenderForced);
                int first = before.TryGetValue(key, out int known) ? known : turn;
                seen[key] = first;
                if (turn - first >= maxTurns)
                {
                    int responder = p.IsSurrenderForced ? p.LoserEmpire : p.WinnerEmpire;
                    int other = p.IsSurrenderForced ? p.WinnerEmpire : p.LoserEmpire;
                    fallback.Add((responder << 16) | other);
                }
            }
            surrenderSeen = seen;
            NativeAiLocks.SetSurrenderFallback(fallback);
        }

        /// <summary>Surrender the language AI nation will still answer (within the deadline): the end of turn does not resolve it on its own.</summary>
        private static bool ShouldHoldSurrender(DiplomaticAncillary ancillary, DiplomaticState_War war, int turn)
        {
            if (war == null || war.ProposedSurrenderIndex < 0)
            {
                return false;
            }
            SurrenderProposition p = ancillary.SurrenderAllocator.GetReferenceAt(war.ProposedSurrenderIndex);
            int responder = p.IsSurrenderForced ? p.LoserEmpire : p.WinnerEmpire;
            if (!NativeAiLocks.IsLlmActive(responder))
            {
                return false;
            }
            int first = surrenderSeen.TryGetValue(SurrenderKey(p.WinnerEmpire, p.LoserEmpire, p.IsSurrenderForced), out int known) ? known : turn;
            return turn - first < Math.Max(1, IaConfig.ProposalTurns.Value);
        }

        private static bool ShouldHold(ref DiplomaticPropositionInfo info, int turn)
        {
            if (info.Status != TreatyStatus.Proposed && info.Status != TreatyStatus.Countered)
            {
                return false;
            }
            if (!NativeAiLocks.IsLlmActive(info.EmpireIndexWhoNeedToSign))
            {
                return false;
            }
            int maxTurns = Math.Max(1, IaConfig.ProposalTurns.Value);
            return info.TurnWhenProposed < 0 || turn - info.TurnWhenProposed < maxTurns;
        }

        /// <summary>Surrenders are only hidden at the end of turn; the turn change does not reset them (research\surrender.md §5.4).</summary>
        [ThreadStatic]
        private static bool hideSurrenders;

        private static List<Held> Hide(DiplomaticAncillary ancillary, List<Held> held)
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (ancillary?.DiplomaticRelations == null || sandbox == null || sandbox.IsSessionOnline || !IaConfig.ExecuteActions.Value)
            {
                return held;
            }
            int turn = sandbox.Turn;
            foreach (DiplomaticRelation relation in ancillary.DiplomaticRelations)
            {
                BaseDiplomaticState state = relation?.DiplomaticState;
                if (state == null)
                {
                    continue;
                }
                if (ShouldHold(ref state.CurrentTreatyInfo.PropositionInfo, turn))
                {
                    held.Add(new Held { Relation = relation, Treaty = true, Status = state.CurrentTreatyInfo.PropositionInfo.Status, Surrender = -1 });
                    state.CurrentTreatyInfo.PropositionInfo.Status = TreatyStatus.None;
                }
                if (ShouldHold(ref state.CurrentAgreementsInfo.PropositionInfo, turn))
                {
                    held.Add(new Held { Relation = relation, Treaty = false, Status = state.CurrentAgreementsInfo.PropositionInfo.Status, Surrender = -1 });
                    state.CurrentAgreementsInfo.PropositionInfo.Status = TreatyStatus.None;
                }
                if (hideSurrenders && state is DiplomaticState_War war && ShouldHoldSurrender(ancillary, war, turn))
                {
                    held.Add(new Held { Relation = relation, Surrender = war.ProposedSurrenderIndex });
                    war.ProposedSurrenderIndex = -1;
                }
            }
            return held;
        }

        private static void Restore(List<Held> held)
        {
            if (held == null)
            {
                return;
            }
            foreach (Held item in held)
            {
                BaseDiplomaticState state = item.Relation?.DiplomaticState;
                if (state == null)
                {
                    continue;
                }
                if (item.Surrender >= 0)
                {
                    if (state is DiplomaticState_War war && war.ProposedSurrenderIndex < 0)
                    {
                        war.ProposedSurrenderIndex = item.Surrender;
                    }
                    continue;
                }
                if (item.Treaty)
                {
                    state.CurrentTreatyInfo.PropositionInfo.Status = item.Status;
                }
                else
                {
                    state.CurrentAgreementsInfo.PropositionInfo.Status = item.Status;
                }
            }
            held.Clear();
        }

        private static List<Held> SafeHide(DiplomaticAncillary ancillary)
        {
            var held = new List<Held>();
            try
            {
                return Hide(ancillary, held);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[IA] Proposals under review (hide): {ex}");
                // Whatever was already hidden before the exception is restored, otherwise the proposal would vanish for good.
                Restore(held);
                return null;
            }
        }

        [HarmonyPatch(typeof(DiplomaticAncillary), nameof(DiplomaticAncillary.ReadyForTurnFinishCompletion))]
        private static class TurnFinishPatch
        {
            private static void Prefix(DiplomaticAncillary __instance, out List<Held> __state)
            {
                hideSurrenders = true;
                try
                {
                    __state = SafeHide(__instance);
                }
                finally
                {
                    hideSurrenders = false;
                }
            }

            private static void Postfix(List<Held> __state) => Restore(__state);

            private static Exception Finalizer(Exception __exception, List<Held> __state)
            {
                Restore(__state);
                return __exception;
            }
        }

        /// <summary>
        /// Game bug (research\surrender.md §2.7): whoever imposes a surrender gets stuck in the mandatory popup until the
        /// loser answers, because IsOtherSurrenderPropositionPending only checks "I am the winner" and ignores whether the
        /// surrender is forced. With the native AI the answer comes right away; with the language AI, only on the next
        /// turn. Here the popup stops demanding an answer from whoever is just waiting (forced, winner = player, no draft
        /// of theirs open).
        /// </summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.UI.MandatoryDiplomaticPropositionPending), "RefreshData")]
        private static class MandatorySurrenderPatch
        {
            private static void Postfix(ref bool __result, Amplitude.Mercury.UI.MandatoryDiplomaticPropositionPending.Type[] ___allPendingPropositionsPerEmpireIndex)
            {
                var pending = ___allPendingPropositionsPerEmpireIndex;
                Sandbox sandbox = SandboxManager.Sandbox;
                if (pending == null || sandbox == null)
                {
                    return;
                }
                try
                {
                    int local = sandbox.LocalEmpireIndex;
                    DiplomaticAncillary ancillary = Sandbox.DiplomaticAncillary;
                    bool any = false;
                    for (int i = 0; i < pending.Length; i++)
                    {
                        if (i != local && (pending[i] & Amplitude.Mercury.UI.MandatoryDiplomaticPropositionPending.Type.Surrender) != 0
                            && ancillary.GetRelationFor(local, i) is DiplomaticRelation relation
                            && relation.DiplomaticState is DiplomaticState_War war && war.ProposedSurrenderIndex >= 0)
                        {
                            SurrenderProposition p = ancillary.SurrenderAllocator.GetReferenceAt(war.ProposedSurrenderIndex);
                            if (p.IsSurrenderForced && p.WinnerEmpire == local && relation.GetEmpireEmbassy(local).ForcedOtherToSurrenderProposition < 0)
                            {
                                pending[i] &= ~Amplitude.Mercury.UI.MandatoryDiplomaticPropositionPending.Type.Surrender;
                            }
                        }
                        any |= pending[i] != Amplitude.Mercury.UI.MandatoryDiplomaticPropositionPending.Type.None;
                    }
                    __result = any;
                }
                catch (Exception)
                {
                    // when in doubt, the game's behavior
                }
            }
        }

        [HarmonyPatch(typeof(DiplomaticAncillary), "NewTurnBegin_ResetTreatyStatus")]
        private static class NewTurnResetPatch
        {
            private static void Prefix(DiplomaticAncillary __instance, out List<Held> __state) => __state = SafeHide(__instance);

            private static void Postfix(List<Held> __state) => Restore(__state);

            private static Exception Finalizer(Exception __exception, List<Held> __state)
            {
                Restore(__state);
                return __exception;
            }
        }
    }
}
