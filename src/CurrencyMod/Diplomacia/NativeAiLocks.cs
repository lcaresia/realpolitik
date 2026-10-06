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

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Grandes decisões são da IA de linguagem (design §11.0). Enquanto ela responde por uma nação, a IA nativa
    /// dessa nação não declara guerra, não propõe nem rompe tratados e acordos, não responde às propostas dos
    /// outros (elas esperam a IA de linguagem, pelo ProposalHold), não transforma reclamações em exigências nem as
    /// perdoa, e não responde, retira ou leva ao Congresso as exigências. Continua cuidando do resto (construir,
    /// pesquisar, mover tropas, lutar) e das exigências forçadas pelo consulado, que o jogo obriga a responder no
    /// mesmo turno.
    /// A trava só vale para nações que decidiram há pouco: se a API cair, ela cai sozinha (F1).
    /// Os patches rodam na thread da IA nativa: a tabela é um array trocado inteiro, nunca editado no lugar.
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

        /// <summary>Resumo para o "ia status": nações travadas e o que a trava já segurou.</summary>
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
            return $"travas da IA nativa: {(locked.Count > 0 ? string.Join(", ", locked) : "nenhuma")} · iniciativas de tratado seguradas: {blockedTreaties}"
                + $" · reclamações seguradas: {blockedGrievances} · respostas a exigências seguradas: {blockedDemands}"
                + $"{(lastBlocked != null ? $" (última: {lastBlocked})" : string.Empty)} · checagens de guerra travadas: {lockedWarChecks}"
                + $" · rendições seguradas: {blockedSurrenders} · {DescribeMinors()} · {DescribeCongress()}";
        }

        /// <summary>Chamado pela thread principal (IaModule) a cada segundo.</summary>
        internal static void SetActive(bool[] active)
        {
            llmActive = active ?? new bool[0];
        }

        private const string CarryKey = "CurrencyMod.NativeAiLocks.Active";

        /// <summary>
        /// Recarga a quente: a geração nova começa com a tabela vazia até o primeiro segundo do IaModule, e nesse intervalo a
        /// IA nativa soltava de uma vez tudo o que a trava vinha segurando (no T75 de um teste, 7 crises no Congresso que
        /// nenhuma nação da IA de linguagem pediu). O ModEntry.Stop guarda a tabela e o Start a devolve antes dos patches.
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
                Plugin.Log?.LogInfo($"[IA] Travas da IA nativa vindas da geração anterior: {count} nação(ões), já antes dos patches.");
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
                            // DiplomaticAction.Count = "nenhuma ação" para o gerador ManageTreaties.
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

        /// <summary>Reclamações: a IA nativa não as transforma em exigência nem as perdoa (ferramentas exigir e perdoar_queixas).</summary>
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
                            // Count = "nada a fazer" para o gerador ManageGrievances.
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
        /// Exigências: aceitar, recusar, enrolar, retirar, levar ao Congresso (e, na escalada, romper aliança) são da IA de
        /// linguagem. A exigência forçada pelo consulado fica com a IA nativa: o jogo trava o fim do turno até a resposta.
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
        /// Propor, romper e também responder a tratados e acordos: tudo isso é da IA de linguagem. As propostas que ela
        /// precisa responder ficam pendentes pelo ProposalHold (opção B do F3) até a resposta dela.
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

        // ---------------- Rendição (research\surrender.md §3.2) ----------------

        private static int blockedSurrenders;
        /// <summary>
        /// Relações (respondente &lt;&lt; 16 | outro) em que a IA nativa volta a responder a uma rendição: a IA de linguagem
        /// não respondeu dentro do prazo do hold. Publicada pela thread principal (ProposalHold.TrackSurrenders).
        /// </summary>
        private static volatile System.Collections.Generic.HashSet<int> surrenderFallback = new System.Collections.Generic.HashSet<int>();

        internal static void SetSurrenderFallback(System.Collections.Generic.HashSet<int> pairs)
        {
            surrenderFallback = pairs ?? new System.Collections.Generic.HashSet<int>();
        }

        /// <summary>A IA nativa não abre oferta de rendição para a nação (oferecer_rendicao é da IA de linguagem).</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.Surrender), "GetParents")]
        private static class SurrenderOfferPatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.Surrender __instance) => !Blocks(__instance.Brain);
        }

        /// <summary>Nem abre rendição forçada (impor_rendicao).</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.ForceSurrender), "GetParents")]
        private static class SurrenderForcePatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.ForceSurrender __instance) => !Blocks(__instance.Brain);
        }

        /// <summary>Nem mexe nos termos do rascunho que o executor do mod está montando.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.FillSurrenderTerms), "GetContexts")]
        private static class SurrenderTermsPatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.Diplomacy.FillSurrenderTerms __instance) => !Blocks(__instance.Brain);
        }

        /// <summary>
        /// Nem envia, aceita ou recusa: responder_rendicao é da IA de linguagem. Por relação: quando o hold vence sem
        /// resposta (ou a API cai e a trava inteira some), a IA nativa responde naquela relação.
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

        // ---------------- Povos independentes ----------------

        private static int blockedMinorTreaties;
        private static int keptPatronage;

        /// <summary>
        /// Patrocínio escolhido pela IA de linguagem (patrocinar), por (império, povo): a IA nativa replaneja o
        /// investimento de tempos em tempos e desfaria a escolha. Só em memória (recarga ou load: a nativa volta a decidir).
        /// </summary>
        private static volatile System.Collections.Generic.Dictionary<int, int> patronage = new System.Collections.Generic.Dictionary<int, int>();

        internal static void SetPatronage(int empire, int minor, Amplitude.Mercury.Data.Simulation.PatronageDefinition.PatronageInvestment money,
            Amplitude.Mercury.Data.Simulation.PatronageDefinition.PatronageInvestment influence)
        {
            var copy = new System.Collections.Generic.Dictionary<int, int>(patronage);
            copy[(empire << 16) | minor] = ((int)money << 8) | (int)influence;
            patronage = copy;
        }

        /// <summary>Tabela inteira, publicada pelo IaModule a cada segundo a partir do estado salvo das nações.</summary>
        internal static void SetPatronageTable(System.Collections.Generic.Dictionary<int, int> table)
        {
            patronage = table ?? new System.Collections.Generic.Dictionary<int, int>();
        }

        internal static string DescribeMinors() => $"povos: vassalagens/anexações seguradas {blockedMinorTreaties} · patrocínios mantidos {keptPatronage}";

        /// <summary>Vassalo e anexação de povo independente são grandes decisões: só pela IA de linguagem (tratado_povo).</summary>
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
        /// Povo com patrocínio escolhido pela IA de linguagem: os geradores de patrocínio da IA nativa (começar e parar de
        /// patrocinar) nem consideram o povo. Trocar só os níveis da ordem dela (abaixo) deixava a IA nativa insatisfeita, e
        /// ela repetia a ordem sem parar (821 ordens em 10 minutos no turno 113).
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

        /// <summary>Rede de segurança: se ainda sair uma ordem de investimento da IA nativa para esse povo, vai com os níveis da IA de linguagem.</summary>
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

        // ---------------- Congresso (research\congress.md §4.2) ----------------

        private static int blockedVerdicts;
        private static int keptStances;
        private static readonly System.Collections.Generic.HashSet<long> ImposedSeen = new System.Collections.Generic.HashSet<long>();
        /// <summary>
        /// Publicados pela thread principal (CongressFlow.TrackFallbacks): vereditos (perdedor &lt;&lt; 16 | vencedor) e
        /// presidentes da 1ª sessão em que a IA nativa volta a agir por prazo vencido, e a resposta da IA de linguagem para
        /// cada lei imposta ((império &lt;&lt; 32) | evento de osmose → true = recusar).
        /// </summary>
        private static volatile System.Collections.Generic.HashSet<int> verdictFallback = new System.Collections.Generic.HashSet<int>();
        private static volatile System.Collections.Generic.HashSet<int> leaderFallback = new System.Collections.Generic.HashSet<int>();
        private static volatile System.Collections.Generic.Dictionary<string, LawStance> lawStances = new System.Collections.Generic.Dictionary<string, LawStance>();

        /// <summary>O que uma nação da IA de linguagem quer de uma lei do Congresso: a opção que ela votou e o que fazer com a
        /// lei imposta se perder (se_perder). Chave: "império|cívico".</summary>
        internal sealed class LawStance
        {
            public string Voted;
            public string IfLost;
        }

        internal static string LawStanceKey(int empire, string civic) => empire + "|" + civic;

        /// <summary>Resultado da última votação de lei (cívico, opção vencedora, presidente): reconhece a lei imposta pelo
        /// Congresso quando o jogo já trocou os efeitos dela (depois de carregar o save). Publicado pela thread principal.</summary>
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
            $"Congresso: vereditos segurados {blockedVerdicts}, de volta à IA nativa {verdictFallback.Count} · leis impostas respondidas como a IA de linguagem quis {keptStances} · consulado: iniciativas seguradas {BlockedConsulateActions}"
            + (leaderFallback.Count > 0 ? $" · 1ª sessão proposta pela IA nativa de E{string.Join(", E", leaderFallback)}" : string.Empty);

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
        /// Consulado, "demandas e crises" do design §11.0: a IA nativa não usa a alavancagem (exigência forçada, sanções,
        /// reduzir placar de guerra, tropas indesejadas, revelar agentes) por uma nação da IA de linguagem.
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
        /// Nem propõe nem cancela acordos de consulado. Responder (aceitar, contrapropor, recusar) a um acordo que outro
        /// propôs fica com ela, como a exigência forçada: a IA de linguagem não tem essa ação e a proposta ficaria parada.
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

        /// <summary>A IA nativa não propõe lei quando preside (propor_votacao); só na 1ª sessão parada por prazo vencido.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.International.StartInternationalCivicVote), "GetContexts")]
        private static class CongressProposePatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.International.StartInternationalCivicVote __instance)
            {
                MajorEmpireBrain brain = __instance.Brain;
                return brain == null || leaderFallback.Contains(brain.EmpireIndex) || !BlocksCongress(brain);
            }
        }

        /// <summary>Nem vota nem suborna nas leis (votar_congresso, subornar). Abster-se é escolha da IA de linguagem.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.International.ManageInternationalCivicVote), "GetContexts")]
        private static class CongressLawVotePatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.International.ManageInternationalCivicVote __instance) => !BlocksCongress(__instance.Brain);
        }

        /// <summary>Nem vota nem suborna nas crises.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.International.ManageInternationalCrisisVote), "GetContexts")]
        private static class CongressCrisisVotePatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.International.ManageInternationalCrisisVote __instance) => !BlocksCongress(__instance.Brain);
        }

        /// <summary>Nem contribui ao consenso ideológico (contribuir_consenso).</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.International.ContributeToInternationalIdeologicalConsensus), "GetContexts")]
        private static class CongressConsensusPatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.International.ContributeToInternationalIdeologicalConsensus __instance) => !BlocksCongress(__instance.Brain);
        }

        /// <summary>
        /// Nem responde ao veredito de uma crise perdida (responder_congresso): sem esta trava, a IA nativa de uma nação da
        /// IA de linguagem declararia guerra surpresa sozinha. Por relação: com o prazo vencido, a IA nativa responde.
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
        /// Lei imposta pelo Congresso: a IA nativa responde no mesmo turno (senão o fim do turno recusa e cobra influência),
        /// mas com a resposta que a IA de linguagem escolheu: votou na opção imposta = adota; senão, se_perder. A tabela é
        /// publicada antes de a votação fechar, e a decisão sai na hora da ordem (a IA nativa responde no primeiro segundo).
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
                        // Sem influência para recusar, o fim do turno recusaria e cobraria assim mesmo: adota.
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
                    // Conta e registra uma vez por evento (o atuador pode pedir a ordem de novo); o turno entra na chave porque
                    // o índice do evento é reaproveitado depois.
                    bool first;
                    lock (ImposedSeen)
                    {
                        long turn = Amplitude.Mercury.Sandbox.SandboxManager.Sandbox?.Turn ?? 0;
                        first = ImposedSeen.Add((turn << 40) | ((long)empire << 32) | (uint)index);
                    }
                    if (first)
                    {
                        System.Threading.Interlocked.Increment(ref keptStances);
                        Plugin.Log?.LogInfo($"[IA] Lei imposta a E{empire} ({info.ItemName} → {info.CivicChoiceName}): {(discard ? "recusada" : "adotada")}, como a IA de linguagem quis"
                            + (discard != nativeDiscard ? $" (a IA nativa ia {(nativeDiscard ? "recusar" : "adotar")})." : " (a IA nativa faria o mesmo)."));
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
                Plugin.Log?.LogError($"[IA] Trava da IA nativa: {ex}");
            }
        }
    }
}
