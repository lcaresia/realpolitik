using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using UnityEngine;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Rendição de guerra pelas ordens do jogo (research\surrender.md §5.3). Thread do sandbox, só no turno principal
    /// (as ordens de termo são recusadas no fim do turno):
    /// - oferecer: StartToFillSurrenderProposition → territórios (em ordem de vizinhança) e vassalagem → ProposeToSurrender;
    /// - impor: AllowToForceOtherToSurrender (ou o rascunho forçado já aberto) → termos → DeclareSurrender;
    /// - responder: AcceptSurrender / RefuseSurrender, conferindo o papel (forçada: responde o perdedor; oferta: o vencedor).
    /// O que sobra do placar o jogo põe em ouro sozinho. Se o envio falhar, o rascunho é cancelado (rascunho aberto bloqueia
    /// paz e rendição na guerra e nunca expira), e uma faxina cancela rascunhos órfãos das nações da IA de linguagem.
    /// </summary>
    internal static class SurrenderFlow
    {
        /// <summary>Rascunhos sendo montados agora (império &lt;&lt; 16 | outro): a faxina não mexe neles.</summary>
        private static readonly HashSet<int> assembling = new HashSet<int>();
        /// <summary>Rascunhos forçados abertos pelo mod (os da variante "ao aliado" o jogo dá de presente e ficam).</summary>
        private static readonly HashSet<int> forcedByUs = new HashSet<int>();
        /// <summary>Quando cada rascunho órfão foi visto pela primeira vez (faxina só depois de alguns segundos).</summary>
        private static readonly Dictionary<int, float> orphanSince = new Dictionary<int, float>();
        private static float nextCleanUp;

        private static int Pair(int empire, int other) => (empire << 16) | other;

        // ---------------- Oferecer e impor ----------------

        internal static void Offer(ActionIntent intent, MajorEmpire empire)
        {
            if (!War(intent, out DiplomaticRelation relation))
            {
                return;
            }
            DiplomaticAmbassy mine = relation.GetEmpireEmbassy(intent.Empire);
            if (mine.CurrentSurrenderToOtherProposition >= 0)
            {
                // Rascunho velho de uma tentativa que não terminou: cancela e começa de novo, com termos limpos.
                assembling.Add(Pair(intent.Empire, intent.Other));
                ActionExecutor.Post(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = DiplomaticAction.CancelSurrenderProposition }, intent, null,
                    next => Offer(next, empire), failed => assembling.Remove(Pair(failed.Empire, failed.Other)));
                return;
            }
            DiplomaticActionFailureFlags flags = empire.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(intent.Other, DiplomaticAction.StartToFillSurrenderProposition);
            if (flags != DiplomaticActionFailureFlags.None)
            {
                ActionExecutor.Report(intent, false, "o jogo não deixa oferecer rendição agora: " + ActionExecutor.Explain(flags));
                return;
            }
            assembling.Add(Pair(intent.Empire, intent.Other));
            ActionExecutor.Post(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = DiplomaticAction.StartToFillSurrenderProposition }, intent, null,
                next => Fill(next, forced: false), failed => assembling.Remove(Pair(failed.Empire, failed.Other)));
        }

        internal static void Force(ActionIntent intent, MajorEmpire empire)
        {
            if (!War(intent, out DiplomaticRelation relation))
            {
                return;
            }
            assembling.Add(Pair(intent.Empire, intent.Other));
            if (relation.GetEmpireEmbassy(intent.Empire).ForcedOtherToSurrenderProposition >= 0)
            {
                Fill(intent, forced: true); // já aberto (um aliado deu o direito, ou tentativa anterior)
                return;
            }
            DiplomaticActionFailureFlags flags = empire.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(intent.Other, DiplomaticAction.AllowToForceOtherToSurrender);
            if (flags != DiplomaticActionFailureFlags.None)
            {
                assembling.Remove(Pair(intent.Empire, intent.Other));
                ActionExecutor.Report(intent, false, "o jogo não deixa impor rendição agora: " + ActionExecutor.Explain(flags));
                return;
            }
            forcedByUs.Add(Pair(intent.Empire, intent.Other));
            ActionExecutor.Post(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = DiplomaticAction.AllowToForceOtherToSurrender }, intent, null,
                next => Fill(next, forced: true), failed => assembling.Remove(Pair(failed.Empire, failed.Other)));
        }

        /// <summary>
        /// Termos no rascunho aberto: territórios pedidos em ordem de vizinhança (cada um tem de encostar na fronteira do
        /// vencedor, ser cidade ocupada por ele ou vizinho de um já incluído), vassalagem, dentro do placar; depois envia.
        /// As ordens são processadas na ordem em que são postadas.
        /// </summary>
        private static void Fill(ActionIntent intent, bool forced)
        {
            DiplomaticRelation relation = Sandbox.DiplomaticAncillary.GetRelationFor(intent.Empire, intent.Other);
            DiplomaticAmbassy mine = relation?.GetEmpireEmbassy(intent.Empire);
            int index = mine == null ? -1 : forced ? mine.ForcedOtherToSurrenderProposition : mine.CurrentSurrenderToOtherProposition;
            if (index < 0)
            {
                assembling.Remove(Pair(intent.Empire, intent.Other));
                ActionExecutor.Report(intent, false, "o jogo não abriu o rascunho de rendição");
                return;
            }
            SurrenderProposition p = Sandbox.DiplomaticAncillary.SurrenderAllocator.GetReferenceAt(index);
            var problems = new List<string>();
            var ordered = new List<int>();
            var wanted = new List<int>(intent.SurrenderTerritories.Distinct());
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (int t in wanted)
                {
                    if (ordered.Contains(t) || t < 0 || p.SurrenderTerritories == null || t >= p.SurrenderTerritories.Length)
                    {
                        continue;
                    }
                    SurrenderTerritoryInfo info = p.SurrenderTerritories[t];
                    if (info.Included || info.FailureFlags != SurrenderTerritoryFailureFlags.None)
                    {
                        continue;
                    }
                    bool connected = info.Connectivity == SurrenderTerritoryConnectivity.NearBorder || info.Connectivity == SurrenderTerritoryConnectivity.OccupiedCity
                        || info.Connectivity == SurrenderTerritoryConnectivity.NearIncludedTerritory
                        || Sandbox.World.Territories[t].AdjacentTerritories.Any(ordered.Contains);
                    if (connected)
                    {
                        ordered.Add(t);
                        progress = true;
                    }
                }
            }
            foreach (int t in wanted.Where(t => !ordered.Contains(t)))
            {
                problems.Add($"T{t}: {TerritoryProblem(p, t)}");
            }
            int score = (int)(float)p.WinnerWarScore;
            int demands = (int)(float)p.IncludedDemandCost;
            int submissionCost = (int)(float)p.SubmissionDemand.WarCost;
            bool submission = intent.SurrenderSubmission && p.SubmissionDemand.FailureFlags == SurrenderSubmissionFailureFlags.None;
            if (intent.SurrenderSubmission && !submission)
            {
                problems.Add("vassalagem indisponível");
            }
            if (submission && demands + submissionCost > score)
            {
                submission = false;
                problems.Add("vassalagem: não coube no placar");
            }
            while (ordered.Count > 0 && demands + ordered.Count * 25 + (submission ? submissionCost : 0) > score)
            {
                problems.Add($"T{ordered[ordered.Count - 1]}: não coube no placar");
                ordered.RemoveAt(ordered.Count - 1);
            }
            foreach (int t in ordered)
            {
                SandboxManager.PostAndTrackOrder(new OrderChangeSurrenderTerritoryState { OtherEmpireIndex = intent.Other, TerritoryIndex = t, Included = true }, intent.Empire);
            }
            if (submission != p.SubmissionDemand.Included && (submission || p.SubmissionDemand.FailureFlags == SurrenderSubmissionFailureFlags.None))
            {
                SandboxManager.PostAndTrackOrder(new OrderChangeSurrenderSubmissionState { OtherEmpireIndex = intent.Other, Included = submission }, intent.Empire);
            }
            int used = demands + ordered.Count * 25 + (submission ? submissionCost : 0);
            int money = Math.Max(0, score - used);
            int gold = (int)((float)p.MoneyRetribution.MoneyGainPerRetribution * money);
            var terms = new List<string>();
            int demandCount = p.IncludedDemands?.Count(d => d.Included) ?? 0;
            if (demandCount > 0) terms.Add($"{demandCount} exigência(s) ({demands})");
            if (ordered.Count > 0) terms.Add($"territórios {string.Join(", ", ordered.Select(t => "T" + t))} ({ordered.Count * 25})");
            if (submission) terms.Add($"vassalagem ({submissionCost})");
            if (gold > 0) terms.Add($"{gold} de ouro ({money})");
            string description = (forced ? $"rendição imposta a E{intent.Other}" : $"rendição oferecida a E{intent.Other}")
                + $": {(terms.Count > 0 ? string.Join(" + ", terms) : "sem termos")} = {score} pontos; "
                + (forced ? $"E{intent.Other} aceita ou recusa" : $"E{intent.Other} aceita ou recusa (aceitando, a guerra acaba e os termos são cumpridos)")
                + (problems.Count > 0 ? $" (ficou de fora: {string.Join("; ", problems)})" : string.Empty);
            Send(intent, forced, description);
        }

        private static void Send(ActionIntent intent, bool forced, string description)
        {
            DiplomaticAction action = forced ? DiplomaticAction.DeclareSurrender : DiplomaticAction.ProposeToSurrender;
            PostOrderTicket ticket = SandboxManager.PostAndTrackOrder(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = action }, intent.Empire);
            ticket.UponCompletionWithParam(_ =>
            {
                assembling.Remove(Pair(intent.Empire, intent.Other));
                if (ticket.Result == PostOrderResponse.Valid)
                {
                    forcedByUs.Remove(Pair(intent.Empire, intent.Other));
                    ActionExecutor.Report(intent, true, description);
                    return;
                }
                string reason = "termos inválidos";
                try
                {
                    DiplomaticActionFailureFlags flags = Sandbox.MajorEmpires[intent.Empire].DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(intent.Other, action);
                    if (flags != DiplomaticActionFailureFlags.None)
                    {
                        reason = ActionExecutor.Explain(flags);
                    }
                }
                catch (Exception)
                {
                }
                // Rascunho que não foi enviado bloquearia paz e rendição nesta guerra: cancela.
                SandboxManager.PostAndTrackOrder(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = DiplomaticAction.CancelSurrenderProposition }, intent.Empire);
                forcedByUs.Remove(Pair(intent.Empire, intent.Other));
                ActionExecutor.Report(intent, false, "o jogo recusou a rendição: " + reason);
            });
        }

        // ---------------- Responder ----------------

        internal static void Answer(ActionIntent intent, MajorEmpire empire)
        {
            DiplomaticRelation relation = Sandbox.DiplomaticAncillary.GetRelationFor(intent.Empire, intent.Other);
            if (!(relation?.DiplomaticState is DiplomaticState_War war) || war.ProposedSurrenderIndex < 0)
            {
                ActionExecutor.Report(intent, false, "não há mais rendição esperando resposta (a guerra acabou, a proposta caiu ou foi retirada)");
                return;
            }
            SurrenderProposition p = Sandbox.DiplomaticAncillary.SurrenderAllocator.GetReferenceAt(war.ProposedSurrenderIndex);
            int responder = p.IsSurrenderForced ? p.LoserEmpire : p.WinnerEmpire;
            if (responder != intent.Empire)
            {
                ActionExecutor.Report(intent, false, "a rendição pendente espera a resposta deles, não a sua");
                return;
            }
            DiplomaticActionFailureFlags flags = empire.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(intent.Other, intent.Diplomatic);
            if (flags != DiplomaticActionFailureFlags.None)
            {
                ActionExecutor.Report(intent, false, "o jogo não permite responder agora: " + ActionExecutor.Explain(flags));
                return;
            }
            bool accept = intent.Diplomatic == DiplomaticAction.AcceptSurrender;
            ActionExecutor.Post(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = intent.Diplomatic }, intent,
                accept ? "rendição aceita: a guerra acabou e os termos foram cumpridos" : "rendição recusada: a guerra continua");
        }

        // ---------------- Faxina ----------------

        /// <summary>
        /// Rascunho de rendição aberto por nação da IA de linguagem sem montagem em curso (núcleo recarregado ou save
        /// carregado no meio, ou falha sem cancelamento) é cancelado depois de alguns segundos: ele bloquearia paz e rendição.
        /// O rascunho forçado que o jogo dá de presente (variante "ao aliado") fica, para impor_rendicao usar.
        /// </summary>
        internal static void CleanUp()
        {
            if (Time.realtimeSinceStartup < nextCleanUp)
            {
                return;
            }
            nextCleanUp = Time.realtimeSinceStartup + 3f;
            Sandbox sandbox = SandboxManager.Sandbox;
            DiplomaticAncillary ancillary = Sandbox.DiplomaticAncillary;
            if (sandbox == null || sandbox.IsSessionOnline || ancillary?.DiplomaticRelations == null || sandbox.CurrentStateName != "SandboxState_TurnMain")
            {
                return;
            }
            var stillOrphan = new HashSet<int>();
            foreach (DiplomaticRelation relation in ancillary.DiplomaticRelations)
            {
                if (relation == null || relation.CurrentState != DiplomaticStateType.War)
                {
                    continue;
                }
                for (int side = 0; side < 2; side++)
                {
                    DiplomaticAmbassy ambassy = relation.GetEmpireEmbassy(side == 0 ? relation.LeftEmpireIndex : relation.RightEmpireIndex);
                    if (ambassy == null)
                    {
                        continue;
                    }
                    int empire = side == 0 ? relation.LeftEmpireIndex : relation.RightEmpireIndex;
                    int other = side == 0 ? relation.RightEmpireIndex : relation.LeftEmpireIndex;
                    int pair = Pair(empire, other);
                    bool offerDraft = ambassy.CurrentSurrenderToOtherProposition >= 0;
                    bool ourForcedDraft = ambassy.ForcedOtherToSurrenderProposition >= 0 && forcedByUs.Contains(pair);
                    if (!(offerDraft || ourForcedDraft) || !NativeAiLocks.IsLlmActive(empire) || assembling.Contains(pair))
                    {
                        continue;
                    }
                    stillOrphan.Add(pair);
                    if (!orphanSince.TryGetValue(pair, out float since))
                    {
                        orphanSince[pair] = Time.realtimeSinceStartup;
                        continue;
                    }
                    if (Time.realtimeSinceStartup - since < 10f)
                    {
                        continue;
                    }
                    orphanSince.Remove(pair);
                    forcedByUs.Remove(pair);
                    SandboxManager.PostAndTrackOrder(new OrderDiplomaticAction { OtherEmpireIndex = other, DiplomaticAction = DiplomaticAction.CancelSurrenderProposition }, empire);
                    Plugin.Log?.LogInfo($"[IA] Rascunho de rendição órfão de E{empire} contra E{other} cancelado.");
                }
            }
            foreach (int pair in orphanSince.Keys.Where(k => !stillOrphan.Contains(k)).ToList())
            {
                orphanSince.Remove(pair);
            }
        }

        // ---------------- Utilidades ----------------

        private static bool War(ActionIntent intent, out DiplomaticRelation relation)
        {
            relation = Sandbox.DiplomaticAncillary.GetRelationFor(intent.Empire, intent.Other);
            if (relation == null || relation.CurrentState != DiplomaticStateType.War)
            {
                ActionExecutor.Report(intent, false, "vocês não estão (mais) em guerra");
                return false;
            }
            return true;
        }

        private static string TerritoryProblem(SurrenderProposition p, int t)
        {
            if (p.SurrenderTerritories == null || t < 0 || t >= p.SurrenderTerritories.Length)
            {
                return "território inválido";
            }
            SurrenderTerritoryInfo info = p.SurrenderTerritories[t];
            if (info.Included)
            {
                return "já estava incluído";
            }
            SurrenderTerritoryFailureFlags flags = info.FailureFlags;
            if ((flags & SurrenderTerritoryFailureFlags.WrongOwner) != 0) return "não é do perdedor";
            if ((flags & SurrenderTerritoryFailureFlags.CityCenterNotOccupied) != 0) return "centro de cidade: só se o vencedor a estiver ocupando";
            if ((flags & SurrenderTerritoryFailureFlags.OccupiedBySomeoneElse) != 0) return "ocupado por outra nação";
            if ((flags & (SurrenderTerritoryFailureFlags.CityBeingBesieged | SurrenderTerritoryFailureFlags.ContainsBoroughBeingBesieged)) != 0) return "sitiado";
            if ((flags & SurrenderTerritoryFailureFlags.LinkedToDemand) != 0) return "já está numa exigência (entra com ela)";
            if ((flags & SurrenderTerritoryFailureFlags.NotEnoughToFulfillDemands) != 0) return "o placar não sobra depois das exigências";
            if ((flags & SurrenderTerritoryFailureFlags.NotClaimed) != 0) return "sem dono";
            return "não encosta na fronteira do vencedor nem num território já incluído";
        }
    }
}
