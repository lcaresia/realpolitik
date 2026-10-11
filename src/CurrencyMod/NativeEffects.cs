using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Amplitude;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;
using SettlementStatuses = Amplitude.Mercury.Data.Simulation.SettlementStatuses;

namespace CurrencyMod
{
    /// <summary>
    /// The economy's effects enter the game like the game's own effects: in the numbers the game uses and shows, and with
    /// their own line in the native breakdowns (the "buffs and debuffs summary" of cities and money). The game does not
    /// allow creating new data effects at runtime (descriptors are compiled), so each effect is injected where the game
    /// computes and where it shows the value:
    /// - credit (interest → production): construction and AI (CreditProductionPatch), city screen (SettlementCursorSnapshot)
    ///   and the "Credit" line in the industry breakdown;
    /// - inflation and unemployment: lower each city's stability target. Stability moves toward the target at the game's
    ///   pace (GetSettlementPublicOrderGain), the displayed target is already discounted (SettlementInfo), the AI sees the
    ///   discounted target and each loss has its own line in the stability breakdown;
    /// - interest on the balance: added to the displayed per-turn income (top bar) and to a line in the money breakdown.
    /// Everything runs on the sandbox and AI threads and reads only the immutable EconomySimulation.Effects table.
    /// </summary>
    internal static class NativeEffects
    {
        internal const string ProductionProperty = "ProductionNetAfterAffinityBonuses";
        internal const string StabilityProperty = "PublicOrderTarget";
        internal const string MoneyProperty = "MoneyNet";

        internal static bool Enabled => EconomyConfig.EnableEconomy.Value && !SavePatches.IsGameOnline();

        /// <summary>Effects of the owner of a major-empire city; null if there is no effect to apply.</summary>
        internal static EconomySimulation.EmpireEffects EffectsOf(Settlement settlement)
        {
            if (settlement == null || !Enabled || !(settlement.Empire.Entity is MajorEmpire empire))
            {
                return null;
            }
            EconomySimulation.EmpireEffects effects = EconomySimulation.Effects(empire.Index);
            return effects.Active ? effects : null;
        }

        /// <summary>Total loss on a city's stability target (0 if it is not a major-empire city).</summary>
        internal static double StabilityLoss(Settlement settlement)
        {
            if (settlement == null || settlement.SettlementStatus != SettlementStatuses.City)
            {
                return 0;
            }
            EconomySimulation.EmpireEffects effects = EffectsOf(settlement);
            return effects != null ? effects.StabilityLoss : 0;
        }

        /// <summary>Stability target with inflation and unemployment applied, never below the city's guaranteed minimum.</summary>
        internal static FixedPoint EffectiveStabilityTarget(Settlement settlement, double loss)
        {
            double target = (float)settlement.PublicOrderTarget.Value;
            double minimum = Math.Max(0, Math.Min(target, (float)settlement.PublicOrderMinimum.Value));
            return (FixedPoint)(float)Math.Max(minimum, target - loss);
        }

        /// <summary>Interest the balance will yield (or cost) at the next end of turn: the same calculation as the simulation.</summary>
        internal static double ProjectedInterest(Empire empire)
        {
            if (!(empire is MajorEmpire major) || !major.IsAlive || !Enabled)
            {
                return 0;
            }
            EconomySimulation.EmpireEffects effects = EconomySimulation.Effects(major.Index);
            if (!effects.Active)
            {
                return 0;
            }
            return EconomySimulation.InterestFlow((float)major.MoneyStock.Value, effects.InterestRate, effects.InflationRate, effects.SmoothedIncome, effects.Gdp);
        }

        /// <summary>
        /// Route transit the empire will receive at the next end of turn: last turn's amount (RouteTransit), which stays the
        /// same while the routes do not change. Zero until the first end of turn after loading.
        /// </summary>
        internal static double ProjectedTransit(Empire empire)
        {
            return Enabled && empire is MajorEmpire major && RouteTransit.LastReceived.TryGetValue(major.Index, out double amount) ? amount : 0;
        }

        /// <summary>Bloc refund of route upkeep expected at the next end of turn: last turn's amount (BlocTrade).</summary>
        internal static double ProjectedBlocRefund(Empire empire)
        {
            return Enabled && empire is MajorEmpire major && Diplomacy.Blocs.BlocTrade.LastRefund.TryGetValue(major.Index, out double amount) ? amount : 0;
        }

        /// <summary>
        /// Per-turn income the game shows: its own plus what the mod adds or removes at the end of turn (balance interest,
        /// route transit, bloc upkeep refund), so the top bar matches the money that will actually arrive (MoneyNetDisplayPatch).
        /// </summary>
        public static FixedPoint DisplayedMoneyNet(Empire empire)
        {
            FixedPoint net = empire.MoneyNet.Value;
            try
            {
                double extra = ProjectedInterest(empire) + ProjectedTransit(empire) + ProjectedBlocRefund(empire);
                if (Math.Abs(extra) >= 0.05)
                {
                    return net + (FixedPoint)(float)extra;
                }
            }
            catch (Exception)
            {
                // Never break the top bar: without interest, the game's value stays.
            }
            return net;
        }

        internal static string Percent(double rate) => Format.Percent(rate);
    }

    /// <summary>
    /// Our own lines in the native breakdowns. The game's builder gathers the parts from each source (BreakdownParts),
    /// sorts them and only then writes the lines in its own format; a part with LocalizedSourceName shows up just like
    /// the game's (colored value, resource icon, gains before losses).
    /// </summary>
    [HarmonyPatch(typeof(PropertyBreakdownWorker), nameof(PropertyBreakdownWorker.Evaluate))]
    internal static class BreakdownLinesPatch
    {
        private static readonly StaticString CreditId = new StaticString("CurrencyMod_Credit");
        private static readonly StaticString InflationId = new StaticString("CurrencyMod_Inflation");
        private static readonly StaticString UnemploymentId = new StaticString("CurrencyMod_Unemployment");
        private static readonly StaticString InterestId = new StaticString("CurrencyMod_Interest");
        private static readonly StaticString TransitId = new StaticString("CurrencyMod_RouteTransit");
        private static readonly StaticString BlocRefundId = new StaticString("CurrencyMod_BlocRefund");
        private static bool loggedError;

        private static void Postfix(ref PropertyBreakdownWorker __instance, SimulationEntity simulationEntity, string propertyName, BreakdownOptions options)
        {
            try
            {
                if (simulationEntity == null || __instance.BreakdownParts == null || !NativeEffects.Enabled)
                {
                    return;
                }
                bool added = false;
                if (simulationEntity is Settlement settlement)
                {
                    if (propertyName == NativeEffects.ProductionProperty)
                    {
                        added = AddCredit(ref __instance, settlement);
                    }
                    else if (propertyName == NativeEffects.StabilityProperty)
                    {
                        added = AddStability(ref __instance, settlement);
                    }
                }
                else if (simulationEntity is MajorEmpire empire && propertyName == NativeEffects.MoneyProperty)
                {
                    added = AddInterest(ref __instance, empire);
                    added |= AddPart(ref __instance, NativeEffects.ProjectedTransit(empire), L.T("Passagem das rotas pelos seus territórios"), TransitId);
                    added |= AddPart(ref __instance, NativeEffects.ProjectedBlocRefund(empire), L.T("Desconto do bloco na manutenção das rotas"), BlocRefundId);
                }
                if (added)
                {
                    __instance.BreakdownParts.Sort((options & BreakdownOptions.IgnoreCategoryForSorting) != BreakdownOptions.None
                        ? PropertyBreakdownPart.CompareRefIgnoreCategories
                        : PropertyBreakdownPart.CompareRef);
                }
            }
            catch (Exception ex)
            {
                if (!loggedError)
                {
                    loggedError = true;
                    Plugin.Log.LogError($"Native economy breakdown: {ex}");
                }
            }
        }

        private static bool AddCredit(ref PropertyBreakdownWorker worker, Settlement settlement)
        {
            EconomySimulation.EmpireEffects effects = NativeEffects.EffectsOf(settlement);
            if (effects == null || Math.Abs(effects.Credit - 1.0) < 0.0005 || (settlement.CityFlags & CityFlags.ScienceMode) != CityFlags.None)
            {
                return false;
            }
            double production = (float)settlement.ProductionNetAfterAffinityBonuses.Value;
            if (production <= 0)
            {
                return false;
            }
            string source = effects.Credit > 1.0
                ? L.F("Crédito barato (juros de {0})", NativeEffects.Percent(effects.InterestRate))
                : L.F("Crédito caro (juros de {0})", NativeEffects.Percent(effects.InterestRate));
            return AddPart(ref worker, production * (effects.Credit - 1.0), source, CreditId);
        }

        private static bool AddStability(ref PropertyBreakdownWorker worker, Settlement settlement)
        {
            if (settlement.SettlementStatus != SettlementStatuses.City)
            {
                return false;
            }
            EconomySimulation.EmpireEffects effects = NativeEffects.EffectsOf(settlement);
            if (effects == null)
            {
                return false;
            }
            bool added = AddPart(ref worker, -effects.InflationLoss, L.F("Inflação de {0} por turno", NativeEffects.Percent(effects.InflationRate)), InflationId);
            added |= AddPart(ref worker, -effects.UnemploymentLoss, L.T("Desemprego pelo crédito caro"), UnemploymentId);
            return added;
        }

        private static bool AddInterest(ref PropertyBreakdownWorker worker, MajorEmpire empire)
        {
            double interest = NativeEffects.ProjectedInterest(empire);
            string source = (float)empire.MoneyStock.Value < 0
                ? L.T("Juros da dívida")
                : interest >= 0 ? L.T("Juros do saldo") : L.T("Inflação corroendo o saldo");
            return AddPart(ref worker, interest, source, InterestId);
        }

        private static bool AddPart(ref PropertyBreakdownWorker worker, double gain, string source, StaticString id)
        {
            // The game itself discards parts smaller than half a point before writing the lines.
            if (Math.Abs(gain) < 0.5)
            {
                return false;
            }
            var part = new PropertyBreakdownPart
            {
                BaseValue = BreakdownSources.BaseValues.None,
                SourceCategory = BreakdownSources.Categories.None,
                SourceType = BreakdownSources.Types.None,
                SourceName = StaticString.Empty,
                PropertyName = StaticString.Empty,
                DescriptorName = id,
                LocalizedSourceName = source,
                NumberOfSources = -1,
                Gain = (FixedPoint)(float)gain,
            };
            worker.BreakdownParts.Add(ref part);
            return true;
        }
    }

    /// <summary>
    /// Stability moves toward the discounted target. Same formula as the game (RPN "PublicOrderGain"):
    /// gain = clamp(target − current, −fall per turn, +rise per turn).
    /// </summary>
    [HarmonyPatch(typeof(DepartmentOfTheInterior), nameof(DepartmentOfTheInterior.GetSettlementPublicOrderGain))]
    internal static class StabilityGainPatch
    {
        private static void Postfix(Settlement settlement, ref FixedPoint __result)
        {
            try
            {
                double loss = NativeEffects.StabilityLoss(settlement);
                if (loss < 0.01)
                {
                    return;
                }
                double target = (float)NativeEffects.EffectiveStabilityTarget(settlement, loss);
                double current = (float)settlement.PublicOrderCurrent.Value;
                double down = (float)settlement.PublicOrderNegativeTrend.Value;
                double up = (float)settlement.PublicOrderPositiveTrend.Value;
                __result = (FixedPoint)(float)Math.Max(-down, Math.Min(up, target - current));
            }
            catch (Exception)
            {
                // Without the loss, the game's gain stays.
            }
        }
    }

    /// <summary>The stability target the screens show (SettlementInfo, copied to the city screen and the list).</summary>
    [HarmonyPatch]
    internal static class StabilityInfoPatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(Settlement).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name.EndsWith("OnBeforeSynchronization") && m.GetParameters().Length == 0);
        }

        private static void Postfix(Settlement __instance)
        {
            try
            {
                double loss = NativeEffects.StabilityLoss(__instance);
                if (loss < 0.01 || __instance.PoolAllocationIndex < 0)
                {
                    return;
                }
                ref SettlementInfo info = ref Sandbox.World.SettlementInfo.GetReferenceAt(__instance.PoolAllocationIndex);
                info.PublicOrderTargetInPercent = NativeEffects.EffectiveStabilityTarget(__instance, loss) / 100;
            }
            catch (Exception)
            {
                // A screen with the game's target beats a broken screen.
            }
        }
    }

    /// <summary>The AI sees the discounted stability target (builds stability, avoids expanding during a crisis...).</summary>
    [HarmonyPatch(typeof(Amplitude.Mercury.Interop.AI.Entities.Settlement.Synchronizer), "Synchronize")]
    internal static class StabilityAiPatch
    {
        private static void Postfix(Settlement simulationEntity, Amplitude.Mercury.Interop.AI.Entities.Settlement aiEntity)
        {
            try
            {
                double loss = NativeEffects.StabilityLoss(simulationEntity);
                if (loss >= 0.01 && aiEntity != null)
                {
                    aiEntity.PublicOrderTarget = NativeEffects.EffectiveStabilityTarget(simulationEntity, loss);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>City screen production with credit applied (construction already gets it via CreditProductionPatch).</summary>
    [HarmonyPatch(typeof(SettlementCursorSnapshot), "Synchronize")]
    internal static class CreditCursorPatch
    {
        private static void Postfix(SettlementCursorSnapshot.Data simulationData, bool __result)
        {
            try
            {
                if (!__result || simulationData == null || !NativeEffects.Enabled)
                {
                    return;
                }
                int index = simulationData.EmpireIndex;
                if (index < 0 || index >= Sandbox.NumberOfMajorEmpires || !(simulationData.ProductionNet > 0))
                {
                    return;
                }
                double factor = EconomySimulation.CreditFactor(index);
                if (Math.Abs(factor - 1.0) > 0.0005)
                {
                    simulationData.ProductionNet = (FixedPoint)((float)simulationData.ProductionNet * (float)factor);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// The empire's per-turn income with the balance's projected interest, in both copies the game makes for the UI
    /// (InitializeOnLoad and SynchronizeEmpireInfo). Replaces "empire.MoneyNet.Value" with NativeEffects.DisplayedMoneyNet,
    /// also in the change-detection comparison, so the copy is not marked "dirty" every frame.
    /// </summary>
    [HarmonyPatch]
    internal static class MoneyNetDisplayPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase initialize = AccessTools.Method(typeof(GameSnapshot), "InitializeOnLoad");
            MethodBase synchronize = AccessTools.Method(typeof(GameSnapshot), "SynchronizeEmpireInfo");
            if (initialize != null)
            {
                yield return initialize;
            }
            if (synchronize != null)
            {
                yield return synchronize;
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            FieldInfo moneyNet = AccessTools.Field(typeof(Empire), "MoneyNet");
            MethodInfo getValue = AccessTools.PropertyGetter(typeof(Amplitude.Framework.Simulation.Property), "Value");
            MethodInfo displayed = AccessTools.Method(typeof(NativeEffects), nameof(NativeEffects.DisplayedMoneyNet));
            List<CodeInstruction> list = instructions.ToList();
            int replaced = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldflda && Equals(list[i].operand, moneyNet) && list[i + 1].Calls(getValue))
                {
                    // Keeps the labels and blocks of the original instructions.
                    list[i].opcode = OpCodes.Call;
                    list[i].operand = displayed;
                    list[i + 1].opcode = OpCodes.Nop;
                    list[i + 1].operand = null;
                    replaced++;
                }
            }
            Plugin.Log.LogInfo($"Income with interest in the UI: {replaced} read(s) replaced in GameSnapshot.{__originalMethod?.Name}.");
            return list;
        }
    }
}
