using System;
using System.Collections.Generic;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Opção B do F3 (design §15.1; research\diplomacy-native-timers.md): uma proposta de tratado ou acordo que uma
    /// nação comandada pela IA de linguagem precisa responder fica pendente de verdade, na tela nativa, até ela
    /// responder no turno seguinte (responder_tratado / responder_acordo).
    /// O jogo resolve propostas sozinho em dois lugares, e os dois são contornados escondendo essas propostas só
    /// durante a chamada (prefix esconde, postfix e finalizer devolvem; tudo na thread do sandbox):
    /// - DiplomaticAncillary.ReadyForTurnFinishCompletion: segura o fim do turno e, com "impaciência", ignora ou recusa;
    /// - DiplomaticAncillary.NewTurnBegin_ResetTreatyStatus: zera tudo na virada.
    /// Depois de [IA] TurnosParaResponderPropostas turnos sem resposta, ou se a IA de linguagem parar (F1), a regra
    /// nativa volta.
    /// </summary>
    internal static class ProposalHold
    {
        private struct Held
        {
            public DiplomaticRelation Relation;
            public bool Treaty;
            public TreatyStatus Status;
            /// <summary>Rendição escondida: o índice da proposta (DiplomaticState_War.ProposedSurrenderIndex), -1 = não é.</summary>
            public int Surrender;
        }

        // ---------------- Rendição (research\surrender.md §5.4) ----------------

        /// <summary>
        /// Turno em que cada rendição pendente apareceu, por (vencedor, perdedor, forçada): o jogo não salva o turno do
        /// envio. Atualizado pela thread principal; lido pela do sandbox (dicionário trocado inteiro).
        /// </summary>
        private static volatile Dictionary<long, int> surrenderSeen = new Dictionary<long, int>();

        private static long SurrenderKey(int winner, int loser, bool forced) => ((long)winner << 32) | ((long)(loser & 0xFFFF) << 1) | (forced ? 1L : 0L);

        /// <summary>
        /// Thread principal, a cada segundo (IaModule.UpdateLocks): guarda quando cada rendição pendente apareceu e publica
        /// as relações em que a IA nativa volta a responder (a IA de linguagem não respondeu no prazo).
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

        /// <summary>Rendição que a nação da IA de linguagem ainda vai responder (no prazo): o fim do turno não a resolve sozinho.</summary>
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

        /// <summary>Rendição só se esconde no fim do turno; a virada de turno não a zera (research\surrender.md §5.4).</summary>
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
                Plugin.Log?.LogError($"[IA] Propostas em análise (esconder): {ex}");
                // O que já tinha sido escondido antes da exceção volta, senão a proposta sumiria de vez.
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
        /// Bug do jogo (research\surrender.md §2.7): quem impõe rendição fica preso no popup obrigatório até o perdedor
        /// responder, porque IsOtherSurrenderPropositionPending só olha "o vencedor sou eu" e ignora se a rendição é
        /// forçada. Com a IA nativa a resposta vem na hora; com a IA de linguagem, só no turno seguinte. Aqui o popup deixa
        /// de cobrar resposta de quem só está esperando (forçada, vencedor = jogador, sem rascunho dele aberto).
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
                    // em dúvida, o comportamento do jogo
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
