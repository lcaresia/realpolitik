using System;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.UI;
using HarmonyLib;
using Utils = Amplitude.Mercury.UI.Utils;

namespace CurrencyMod
{
    /// <summary>
    /// Crise "Bloqueio comercial". O jogo não aceita tipos novos de reclamação (enum fechado, tabelas de
    /// tamanho fixo e saves guardam o número), então ela é uma reclamação "Comércio destruído" (ganho em
    /// dinheiro, categoria agressão) marcada em DemandGain.GainParamName. A marca segue para a exigência e
    /// para o save; sem o mod, a reclamação aparece como "Comércio destruído" comum.
    /// Textos próprios na tela de crise; se a exigência for aceita, o bloqueio e o pedágio contra a vítima
    /// são retirados. Recusar abre o caminho normal do jogo para a guerra justificada.
    /// </summary>
    internal static class TradeGrievances
    {
        internal static readonly StaticString Marker = new StaticString("CurrencyMod_TradeBlockade");
        // Na interface: L.T(Title) (o texto vai para a tela de crise nativa; o dossiê da IA usa texto próprio).
        internal static readonly string Title = L.N("Bloqueio comercial");

        internal static bool IsMarked(ref DemandGain gain) => gain.GainParamName == Marker;

        /// <summary>Cria ou renova a reclamação da vítima contra quem bloqueia (thread do sandbox).</summary>
        internal static void Ensure(CurrencyWorld world, int victimIndex, int offenderIndex)
        {
            MajorEmpire victim = Sandbox.MajorEmpires[victimIndex];
            MajorEmpire offender = Sandbox.MajorEmpires[offenderIndex];
            if (victim == null || offender == null || !victim.IsAlive || !offender.IsAlive)
            {
                return;
            }
            DiplomaticRelation relation = victim.DiplomaticRelationByOtherEmpireIndex[offenderIndex];
            DiplomaticStateType state = relation.CurrentState;
            if (state != DiplomaticStateType.Peace && state != DiplomaticStateType.Alliance)
            {
                return; // em guerra o jogo já bloqueia tudo; vassalos seguem as regras do suserano
            }
            if ((victim.Liege.Entity != null && victim.Liege.Entity != offender) || (offender.Liege.Entity != null && offender.Liege.Entity != victim))
            {
                // O jogo redireciona a reclamação de/para o suserano (CreateGrievance): ela iria para outro par, sem a
                // marca do mod, e aceitar a exigência não tiraria o bloqueio.
                return;
            }

            int tileIndex = -1;
            TradeRule rule = world.TradeRules.Find(r => r.Owner == offenderIndex && r.Target == victimIndex);
            if (rule != null)
            {
                Territory territory = Sandbox.World.Territories[rule.Territory];
                Settlement settlement = territory?.Region.Entity?.Settlement.Entity;
                if (settlement != null)
                {
                    tileIndex = settlement.WorldPosition.ToTileIndex();
                }
            }
            if (tileIndex < 0)
            {
                return;
            }

            // Mesmo GUID que o jogo usa em "Comércio destruído" (o império culpado): uma reclamação por par.
            DiplomaticGrievanceHelper.CreateGrievance(DiplomaticGrievanceType.TradeDestroyed, victimIndex, offenderIndex, offender.GUID, tileIndex);

            DiplomaticAmbassy embassy = relation.GetEmpireEmbassy(victimIndex);
            var allocator = victim.DepartmentOfForeignAffairs.GrievanceAllocator;
            for (int i = 0; i < embassy.AvailableGrievances.GrievanceIndexesCount; i++)
            {
                ref DiplomaticGrievanceInfo info = ref allocator.GetReferenceAt(embassy.AvailableGrievances.GrievanceIndexes[i]);
                if (info.GrievanceType == DiplomaticGrievanceType.TradeDestroyed && info.EntityGUID == offender.GUID && !IsMarked(ref info.DemandGain))
                {
                    info.DemandGain.GainParamName = Marker;
                    relation.Frame = Sandbox.Frame;
                    Sandbox.SimulationEntityRepository.SetSynchronizationDirty(victim);
                    Plugin.Log.LogInfo($"Crise: império {victimIndex} tem a reclamação \"{Title}\" contra o império {offenderIndex}.");
                }
            }
        }

        // ---------------- Exigência aceita: o bloqueio cai ----------------

        [HarmonyPatch(typeof(DiplomaticGrievanceHelper), nameof(DiplomaticGrievanceHelper.TryToApply))]
        private static class ApplyPatch
        {
            private static void Prefix(ref DiplomaticDemandInfo diplomaticDemandInfo, DiplomaticAmbassy winningAmbassy)
            {
                try
                {
                    if (!IsMarked(ref diplomaticDemandInfo.DemandGain))
                    {
                        return;
                    }
                    int victim = diplomaticDemandInfo.MyEmpireIndex;
                    int offender = winningAmbassy.OtherEmpire.Entity?.Index ?? -1;
                    CurrencyWorld world = CurrencyManager.Current;
                    if (world == null || offender < 0)
                    {
                        return;
                    }
                    lock (CurrencyManager.Lock)
                    {
                        int removed = TradePolicy.ClearRules(world, offender, victim);
                        Plugin.Log.LogInfo($"Crise de bloqueio resolvida: império {offender} retirou {removed} bloqueio(s)/pedágio(s) contra o império {victim}.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Bloqueio comercial (exigência aceita): {ex}");
                }
            }
        }

        // ---------------- Textos na tela de crise ----------------

        private static string ShortName(int empireIndex)
        {
            string name = CentralBankWindow.EmpireName(empireIndex);
            int open = name.LastIndexOf('(');
            int close = name.LastIndexOf(')');
            return open >= 0 && close > open ? name.Substring(open + 1, close - open - 1) : name;
        }

        private static bool LocalIsVictim(ref DiplomaticGrievanceInfo info)
        {
            return info.MyEmpireIndex == Snapshots.GameSnapshot.PresentationData.LocalEmpireInfo.EmpireIndex;
        }

        private static readonly string ResolutionNote = L.N("Se a exigência for aceita, os bloqueios e pedágios contra quem exigiu são retirados.");

        [HarmonyPatch(typeof(GameUtils.DiplomacyUtils), nameof(GameUtils.DiplomacyUtils.LocalizeGrievanceBriefDescription))]
        private static class BriefPatch
        {
            private static void Postfix(GameUtils.DiplomacyUtils __instance, ref DiplomaticGrievanceInfo grievanceInfo, ref string __result)
            {
                if (!IsMarked(ref grievanceInfo.DemandGain))
                {
                    return;
                }
                __result = __instance.TryLocalizeGrievanceDescriptionParam(ref grievanceInfo, out string place) && !string.IsNullOrEmpty(place)
                    ? L.F("Bloqueio comercial em {0}", place)
                    : L.T(Title);
            }
        }

        [HarmonyPatch(typeof(GameUtils.DiplomacyUtils), nameof(GameUtils.DiplomacyUtils.LocalizeGrievanceCause))]
        private static class CausePatch
        {
            private static void Postfix(ref DiplomaticGrievanceInfo grievanceInfo, ref string __result)
            {
                if (!IsMarked(ref grievanceInfo.DemandGain))
                {
                    return;
                }
                __result = LocalIsVictim(ref grievanceInfo)
                    ? L.F("{0} está bloqueando ou cobrando pedágio das nossas rotas comerciais que passam pelos postos comerciais deles.", ShortName(grievanceInfo.OtherEmpireIndex))
                    : L.F("Estamos bloqueando ou cobrando pedágio das rotas comerciais {0} nos nossos postos comerciais.", Diplomacia.GameText.OfUi(ShortName(grievanceInfo.MyEmpireIndex)));
            }
        }

        [HarmonyPatch(typeof(DiplomaticCrisisPanel_GrievanceLine), nameof(DiplomaticCrisisPanel_GrievanceLine.Bind))]
        private static class LinePatch
        {
            private static void Postfix(DiplomaticCrisisPanel_GrievanceLine __instance, ref DiplomaticGrievanceInfo grievanceInfo)
            {
                if (IsMarked(ref grievanceInfo.DemandGain))
                {
                    __instance.tooltipTarget.Title = L.T(Title);
                    __instance.tooltip.Bind(TooltipUtils.TitleAndDescription, __instance.tooltipTarget);
                }
            }
        }

        [HarmonyPatch(typeof(DiplomaticCrisisPanel_GrievanceItem), nameof(DiplomaticCrisisPanel_GrievanceItem.Bind))]
        private static class ItemPatch
        {
            private static void Postfix(DiplomaticCrisisPanel_GrievanceItem __instance, ref DiplomaticGrievanceInfo grievanceInfo)
            {
                if (IsMarked(ref grievanceInfo.DemandGain))
                {
                    __instance.tooltipTarget.Title = L.T(Title);
                    __instance.tooltipTarget.Description = L.T("Um império está bloqueando ou cobrando pedágio das rotas comerciais de outro nos seus postos comerciais.") + " " + L.T(ResolutionNote);
                    __instance.tooltip.Bind(TooltipUtils.TitleAndDescription, __instance.tooltipTarget);
                }
            }
        }

        [HarmonyPatch(typeof(DiplomaticCrisisPanel_SelectionPanel), nameof(DiplomaticCrisisPanel_SelectionPanel.ComputeDescription_Grievance))]
        private static class SelectionGrievancePatch
        {
            private static void Postfix(ref DiplomaticGrievanceInfo grievanceInfo, ref string __result)
            {
                if (IsMarked(ref grievanceInfo.DemandGain))
                {
                    __result = __result.TrimEnd() + "\n" + L.T(ResolutionNote);
                }
            }
        }

        [HarmonyPatch(typeof(DiplomaticCrisisPanel_SelectionPanel), nameof(DiplomaticCrisisPanel_SelectionPanel.ComputeDescription_Demand))]
        private static class SelectionDemandPatch
        {
            private static void Postfix(ref DiplomaticDemandInfo demandInfo, ref string __result)
            {
                if (!IsMarked(ref demandInfo.DemandGain))
                {
                    return;
                }
                // A linha "Origem: Comércio destruído" passa a dizer "Bloqueio comercial".
                UIMapper mapper = Utils.DataUtils.EnumUIMappers.GetEnumUIMapper(DiplomaticGrievanceType.TradeDestroyed);
                string vanilla = mapper != null ? Utils.TextUtils.Localize(mapper.Title) : null;
                if (!string.IsNullOrEmpty(vanilla))
                {
                    __result = __result.Replace(vanilla, L.T(Title));
                }
                __result = __result.TrimEnd() + "\n" + L.T(ResolutionNote);
            }
        }
    }
}
