using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace CurrencyMod.Diplomacia.Council
{
    /// <summary>
    /// Ministers' veteran bonus (melhorias-jogando item 3): whoever stays in office yields +1% every 5 turns in the
    /// portfolio's area, up to +10%. Through the same path the mod economy already uses (Harmony on the game's math, on the
    /// sandbox thread), not through descriptors: those only enter the boot compilation and do not survive a core reload.
    /// - End of turn (before the passes): money, science, influence and food get the bonus on the positive net income;
    ///   Trade gets it on what the empire sold through routes last turn (Central Bank ledger).
    /// - Industry: the same ComputeProductionIncome that credit uses (the build queue, the AI and the screens see it).
    /// - Stability: the public order target rises by the bonus points.
    /// - Combat strength: BattleUnit.GetCombatStrength (only in battle).
    /// - Espionage: spy weight in interception (Espionage).
    /// - Faith: no effect for now (the game has no faith stock to add to).
    /// The table is built on the main thread (IaModule, once per second) and only read on the others.
    /// </summary>
    internal static class VeteranEffects
    {
        internal enum Area { Food, Industry, Money, Science, Influence, Stability, Combat, Trade, Faith, Spy }

        private static readonly int AreaCount = Enum.GetValues(typeof(Area)).Length;

        /// <summary>empire → % per area. Replaced whole on every publish.</summary>
        private static volatile Dictionary<int, int[]> table = new Dictionary<int, int[]>();

        internal static Area? AreaOf(string portfolio)
        {
            switch (portfolio)
            {
                case "agricultura": return Area.Food;
                case "obras": return Area.Industry;
                case "financas": return Area.Money;
                case "ciencia": return Area.Science;
                case "cultura":
                case "chanceler": return Area.Influence;
                case "interior": return Area.Stability;
                case "guerra": return Area.Combat;
                case "comercio": return Area.Trade;
                case "fe": return Area.Faith;
                case "espionagem": return Area.Spy;
                default: return null;
            }
        }

        /// <summary>Main thread: rebuilds the table with the current councils.</summary>
        internal static void Publish(IaWorld world, int turn)
        {
            var built = new Dictionary<int, int[]>();
            if (world != null && IaConfig.Councils.Value)
            {
                foreach (IaNation nation in world.Nations)
                {
                    if (nation.CouncilModel < 1)
                    {
                        continue; // old council, not reset yet: no bonus
                    }
                    foreach (Minister minister in nation.Council)
                    {
                        Area? area = AreaOf(minister.Portfolio);
                        int bonus = CouncilEngine.VeteranBonus(minister, turn);
                        if (area == null || bonus <= 0)
                        {
                            continue;
                        }
                        if (!built.TryGetValue(nation.EmpireIndex, out int[] values))
                        {
                            values = new int[AreaCount];
                            built[nation.EmpireIndex] = values;
                        }
                        values[(int)area.Value] += bonus;
                    }
                }
            }
            table = built;
        }

        /// <summary>The empire's bonus in % for the area (0 with no veteran minister).</summary>
        internal static int Get(int empire, Area area)
        {
            return !SavePatches.IsGameOnline() && table.TryGetValue(empire, out int[] values) ? values[(int)area] : 0;
        }

        internal static double Factor(int empire, Area area) => 1.0 + Get(empire, area) / 100.0;

        // ---------------- End of turn ----------------

        [HarmonyPatch(typeof(SimulationPasses), nameof(SimulationPasses.InvokePasses))]
        private static class TurnEndPatch
        {
            private static void Prefix(SimulationPasses __instance, SimulationPasses.PassContext context)
            {
                if (context != SimulationPasses.PassContext.TurnEnd || !ReferenceEquals(__instance, Ancillary.Passes) || table.Count == 0)
                {
                    return;
                }
                try
                {
                    Sandbox sandbox = SandboxManager.Sandbox;
                    if (sandbox == null || sandbox.Turn == lastTurn)
                    {
                        return;
                    }
                    lastTurn = sandbox.Turn;
                    foreach (KeyValuePair<int, int[]> pair in table)
                    {
                        if (pair.Key >= 0 && pair.Key < Sandbox.NumberOfMajorEmpires && Sandbox.MajorEmpires[pair.Key] is MajorEmpire empire && empire.IsAlive)
                        {
                            Apply(empire, pair.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Veteran bonus at end of turn: {ex.Message}");
                }
            }
        }

        private static int lastTurn = -1;

        private static void Apply(MajorEmpire empire, int[] bonus)
        {
            double Pct(Area area) => bonus[(int)area] / 100.0;
            var log = new List<string>();
            if (bonus[(int)Area.Money] > 0 && (float)empire.MoneyNet.Value > 0)
            {
                empire.DepartmentOfTheTreasury.GainMoney((FixedPoint)((float)empire.MoneyNet.Value * Pct(Area.Money)));
                log.Add($"money +{(float)empire.MoneyNet.Value * Pct(Area.Money):0.0}");
            }
            if (bonus[(int)Area.Science] > 0 && (float)empire.ResearchNet.Value > 0)
            {
                empire.DepartmentOfScience.GainResearch((FixedPoint)((float)empire.ResearchNet.Value * Pct(Area.Science)), raiseSimulationEvents: false);
                log.Add($"science +{(float)empire.ResearchNet.Value * Pct(Area.Science):0.0}");
            }
            if (bonus[(int)Area.Influence] > 0 && (float)empire.InfluenceNet.Value > 0)
            {
                empire.DepartmentOfCulture.GainInfluence((FixedPoint)((float)empire.InfluenceNet.Value * Pct(Area.Influence)));
                log.Add($"influence +{(float)empire.InfluenceNet.Value * Pct(Area.Influence):0.0}");
            }
            if (bonus[(int)Area.Food] > 0)
            {
                for (int i = 0; i < empire.Settlements.Count; i++)
                {
                    Settlement settlement = empire.Settlements[i];
                    float growth = settlement != null ? (float)settlement.GrowthGain.Value : 0f;
                    if (growth > 0)
                    {
                        empire.DepartmentOfTheInterior.AddGrowthToSettlement(settlement, (FixedPoint)(growth * Pct(Area.Food)), raiseSimulationEvents: false);
                        log.Add($"food +{growth * Pct(Area.Food):0.0} in {settlement.EntityName}");
                    }
                }
            }
            if (bonus[(int)Area.Trade] > 0)
            {
                // What the empire earned selling through routes last turn (Central Bank ledger), in its currency.
                double sold = 0;
                lock (CurrencyManager.Lock)
                {
                    TradePoint last = CurrencyManager.Current?.TradeHistory?.LastOrDefault();
                    if (last != null)
                    {
                        sold = last.Money.Where(m => m.To == empire.Index && m.From != empire.Index).Sum(m => m.Gain);
                    }
                }
                if (sold > 0)
                {
                    empire.DepartmentOfTheTreasury.GainMoney((FixedPoint)(float)(sold * Pct(Area.Trade)));
                    log.Add($"trade +{sold * Pct(Area.Trade):0.0}");
                }
            }
            if (log.Count > 0)
            {
                Plugin.Log.LogInfo($"Veteran E{empire.Index}: {string.Join(", ", log)}");
            }
        }

        // ---------------- Industry ----------------

        [HarmonyPatch(typeof(DepartmentOfIndustry), nameof(DepartmentOfIndustry.ComputeProductionIncome))]
        private static class IndustryPatch
        {
            [HarmonyPriority(Priority.Low)]
            private static void Postfix(Settlement settlement, ref FixedPoint __result)
            {
                try
                {
                    if (settlement?.Empire.Entity is MajorEmpire empire && __result > 0)
                    {
                        int bonus = Get(empire.Index, Area.Industry);
                        if (bonus > 0)
                        {
                            __result = (FixedPoint)((float)__result * (1f + bonus / 100f));
                        }
                    }
                }
                catch (Exception)
                {
                    // Never get in the way of construction.
                }
            }
        }

        // ---------------- Faith ----------------

        internal static bool FaithBypass;

        /// <summary>Test ("ia conselho fe T#"): the territory's belief strength without and with the Faith veteran bonus.</summary>
        internal static string MeasureFaith(int territory)
        {
            ReligionManager manager = Sandbox.ReligionManager;
            var method = AccessTools.Method(typeof(ReligionManager), "ComputeTerritoryBeliefStrengths");
            int count = manager.BeliefCount;
            var without = new FixedPoint[count];
            var with = new FixedPoint[count];
            FaithBypass = true;
            try
            {
                method.Invoke(manager, new object[] { territory, without, 0 });
            }
            finally
            {
                FaithBypass = false;
            }
            method.Invoke(manager, new object[] { territory, with, 0 });
            int owner = Sandbox.World.TerritoryInfo.Data[territory].EmpireIndex;
            var parts = new List<string>();
            for (int i = 0; i < count; i++)
            {
                if (without[i] != with[i] || without[i] > 0)
                {
                    parts.Add($"religião {i}: {(float)without[i]:0.##} → {(float)with[i]:0.##}");
                }
            }
            return $"T{territory} (dono E{owner}, bônus Fé +{(owner >= 0 && owner < Sandbox.NumberOfMajorEmpires ? Get(owner, Area.Faith) : 0)}%): " + (parts.Count > 0 ? string.Join("; ", parts) : "sem crença");
        }

        /// <summary>
        /// Faith in the game is not a stock: each territory sums the "belief strength" of each religion, and the largest spreads.
        /// The Faith veteran raises by % the strength of the empire's religion in its territories.
        /// </summary>
        [HarmonyPatch(typeof(ReligionManager), "ComputeTerritoryBeliefStrengths")]
        private static class FaithPatch
        {
            private static void Postfix(int territoryIndex, FixedPoint[] territoriesBeliefStrengths, int startIndex)
            {
                try
                {
                    if (FaithBypass)
                    {
                        return;
                    }
                    int empireIndex = Sandbox.World.TerritoryInfo.Data[territoryIndex].EmpireIndex;
                    if (empireIndex < 0 || empireIndex >= Sandbox.NumberOfMajorEmpires)
                    {
                        return;
                    }
                    int bonus = Get(empireIndex, Area.Faith);
                    Religion religion = bonus > 0 ? Sandbox.MajorEmpires[empireIndex].Religion.Entity : null;
                    if (religion == null)
                    {
                        return;
                    }
                    int slot = startIndex + religion.Index;
                    if (slot >= 0 && slot < territoriesBeliefStrengths.Length && territoriesBeliefStrengths[slot] > 0)
                    {
                        territoriesBeliefStrengths[slot] = territoriesBeliefStrengths[slot] * (FixedPoint)(float)(1.0 + bonus / 100.0);
                    }
                }
                catch (Exception)
                {
                    // Without the bonus, the strength computed by the game stays.
                }
            }
        }

        // ---------------- Stability ----------------

        [HarmonyPatch(typeof(DepartmentOfTheInterior), nameof(DepartmentOfTheInterior.GetSettlementPublicOrderGain))]
        private static class StabilityPatch
        {
            [HarmonyPriority(Priority.Low)]
            private static void Postfix(Settlement settlement, ref FixedPoint __result)
            {
                try
                {
                    if (!(settlement?.Empire.Entity is MajorEmpire empire))
                    {
                        return;
                    }
                    int bonus = Get(empire.Index, Area.Stability);
                    if (bonus <= 0)
                    {
                        return;
                    }
                    // The target rises by the bonus points; the per-turn step respects the game's trends.
                    double target = Math.Min(100.0, (float)NativeEffects.EffectiveStabilityTarget(settlement, NativeEffects.StabilityLoss(settlement)) + bonus);
                    double current = (float)settlement.PublicOrderCurrent.Value;
                    double down = (float)settlement.PublicOrderNegativeTrend.Value;
                    double up = (float)settlement.PublicOrderPositiveTrend.Value;
                    __result = (FixedPoint)(float)Math.Max(-down, Math.Min(Math.Max(up, 1.0), target - current));
                }
                catch (Exception)
                {
                    // Without the bonus, the gain computed before stays.
                }
            }
        }

        // ---------------- Combat ----------------

        [HarmonyPatch(typeof(BattleUnit), nameof(BattleUnit.GetCombatStrength))]
        private static class CombatPatch
        {
            private static void Postfix(BattleUnit __instance, ref FixedPoint __result)
            {
                try
                {
                    int bonus = Get(__instance.EmpireIndex, Area.Combat);
                    if (bonus > 0 && __result > 0)
                    {
                        __result = (FixedPoint)((float)__result * (1f + bonus / 100f));
                    }
                }
                catch (Exception)
                {
                    // Without the bonus, the game's strength stays.
                }
            }
        }
    }
}
