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
    /// "Trade blockade" crisis. The game does not accept new grievance types (closed enum, fixed-size
    /// tables and saves store the number), so it is a "Trade destroyed" grievance (money gain, aggression
    /// category) marked in DemandGain.GainParamName. The mark carries over to the demand and to the
    /// save; without the mod, the grievance shows up as a plain "Trade destroyed".
    /// Custom texts on the crisis screen; if the demand is accepted, the blockade and the toll against the victim
    /// are lifted. Refusing opens the game's normal path to a justified war.
    /// </summary>
    internal static class TradeGrievances
    {
        internal static readonly StaticString Marker = new StaticString("CurrencyMod_TradeBlockade");
        // In the UI: L.T(Title) (the text goes to the native crisis screen; the AI dossier uses its own text).
        internal static readonly string Title = L.N("Bloqueio comercial");

        internal static bool IsMarked(ref DemandGain gain) => gain.GainParamName == Marker;

        /// <summary>Creates or renews the victim's grievance against the blocker (sandbox thread).</summary>
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
                return; // at war the game already blocks everything; vassals follow their overlord's rules
            }
            if ((victim.Liege.Entity != null && victim.Liege.Entity != offender) || (offender.Liege.Entity != null && offender.Liege.Entity != victim))
            {
                // The game redirects the grievance from/to the overlord (CreateGrievance): it would go to another pair,
                // without the mod's mark, and accepting the demand would not lift the blockade.
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

            // Same GUID the game uses for "Trade destroyed" (the guilty empire): one grievance per pair.
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
                    Plugin.Log.LogInfo($"Crisis: empire {victimIndex} has the \"{Title}\" grievance against empire {offenderIndex}.");
                }
            }
        }

        // ---------------- Demand accepted: the blockade is lifted ----------------

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
                        Plugin.Log.LogInfo($"Blockade crisis resolved: empire {offender} lifted {removed} blockade(s)/toll(s) against empire {victim}.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Trade blockade (demand accepted): {ex}");
                }
            }
        }

        // ---------------- Texts on the crisis screen ----------------

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
                // The "Origin: Trade destroyed" line now reads "Trade blockade".
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
