using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace CurrencyMod
{
    /// <summary>
    /// What routes carry besides goods (melhorias-jogando item 36, system C):
    /// - faith: the seller's religion gains strength in the territories the route crosses (full on the buyer's land, half
    ///   on third parties'), through the same belief strength the game sums in each territory, so it competes with local
    ///   faith, shows in the game's tooltips and converts at the game's own pace;
    /// - culture: the same with the seller's cultural strength (the game's cultural influence over territories, which feeds
    ///   osmosis of civics and ideology);
    /// - technology: a technology a trade partner already has costs CargoTechDiscount less, through the game's own
    ///   "contributing empires" multiplier (the research screen shows the partner as a contributor).
    /// Unclaimed land is never influenced (the game's rule). Sandbox thread only.
    /// </summary>
    internal static class TradeCargo
    {
        [HarmonyPatch(typeof(ReligionManager), "ComputeTerritoryBeliefStrengths")]
        private static class FaithPatch
        {
            [HarmonyPriority(Priority.Low)]
            private static void Postfix(int territoryIndex, FixedPoint[] territoriesBeliefStrengths, int startIndex)
            {
                Add(territoryIndex, territoriesBeliefStrengths, startIndex, EconomyConfig.CargoFaithPerGood.Value, seller =>
                {
                    Religion religion = Sandbox.MajorEmpires[seller].Religion.Entity;
                    return religion != null ? religion.Index : -1;
                });
            }
        }

        [HarmonyPatch(typeof(CultureManager), "ComputeTerritoryBeliefStrengths")]
        private static class CulturePatch
        {
            private static void Postfix(int territoryIndex, FixedPoint[] territoriesBeliefStrengths, int startIndex)
            {
                Add(territoryIndex, territoriesBeliefStrengths, startIndex, EconomyConfig.CargoCulturePerGood.Value, seller => seller);
            }
        }

        private static void Add(int territory, FixedPoint[] strengths, int start, double perGood, Func<int, int> beliefOf)
        {
            if (!TradeNetwork.Enabled || perGood <= 0)
            {
                return;
            }
            try
            {
                TradeNetwork.Snapshot snapshot = TradeNetwork.EnsureFresh();
                if (!snapshot.CargoByTerritory.TryGetValue(territory, out List<TradeNetwork.Cargo> cargo))
                {
                    return;
                }
                foreach (TradeNetwork.Cargo item in cargo)
                {
                    if (item.Seller < 0 || item.Seller >= Sandbox.NumberOfMajorEmpires)
                    {
                        continue;
                    }
                    int belief = beliefOf(item.Seller);
                    int slot = start + belief;
                    if (belief >= 0 && slot < strengths.Length)
                    {
                        strengths[slot] += (FixedPoint)(float)(perGood * item.Weight);
                    }
                }
            }
            catch (Exception)
            {
                // Without the cargo, the game's own strengths stay.
            }
        }

        /// <summary>Faith and culture the routes add in a territory (dev "game trade cargo T#"): seller → weight.</summary>
        internal static string Describe(int territory)
        {
            TradeNetwork.Snapshot snapshot = TradeNetwork.Current;
            if (!snapshot.CargoByTerritory.TryGetValue(territory, out List<TradeNetwork.Cargo> cargo))
            {
                return $"T{territory}: no route cargo";
            }
            var parts = new List<string>();
            foreach (TradeNetwork.Cargo item in cargo)
            {
                parts.Add($"E{item.Seller} goods {item.Weight:0.#} → faith +{item.Weight * EconomyConfig.CargoFaithPerGood.Value:0.#}, culture +{item.Weight * EconomyConfig.CargoCulturePerGood.Value:0.#}");
            }
            return $"T{territory}: " + string.Join("; ", parts);
        }

        // ---------------- Technology ----------------

        [HarmonyPatch(typeof(DepartmentOfScience), "GetTechnologyCostReductionMultiplierFromOtherEmpiresContributions")]
        private static class TechnologyPatch
        {
            private static void Postfix(MajorEmpire empire, StaticString technologyName, ref int otherEmpireContributingFlag, ref FixedPoint __result)
            {
                if (!TradeNetwork.Enabled || empire == null)
                {
                    return;
                }
                try
                {
                    int partners = Partners(empire.Index);
                    if (partners == 0 || !Sandbox.CivilizationsManager.MajorEmpireBitsWithTechnologyCompleted.TryGetValue(technologyName, out int known))
                    {
                        return;
                    }
                    int teachers = partners & known & ~empire.Bits;
                    if (teachers == 0)
                    {
                        return;
                    }
                    __result = FixedPoint.Max(FixedPoint.Zero, __result * (FixedPoint)(float)(1.0 - EconomyConfig.CargoTechDiscount.Value));
                    otherEmpireContributingFlag |= teachers;
                }
                catch (Exception)
                {
                }
            }
        }

        private static int Partners(int empire)
        {
            int[] bits = TradeNetwork.Current.PartnerBits;
            return empire >= 0 && empire < bits.Length ? bits[empire] : 0;
        }

        /// <summary>End of turn: recomputes the technology costs of the empires whose trade partners changed.</summary>
        internal static void RefreshTechnologyCosts(TradeNetwork.Snapshot snapshot)
        {
            CurrencyWorld world = CurrencyManager.Current;
            if (world == null)
            {
                return;
            }
            int[] lastPartners = world.TradePartners;
            int[] now = snapshot.PartnerBits;
            for (int i = 0; i < now.Length && i < Sandbox.NumberOfMajorEmpires; i++)
            {
                int before = i < lastPartners.Length ? lastPartners[i] : -1;
                if (before != now[i] && Sandbox.MajorEmpires[i] is MajorEmpire empire && empire.IsAlive)
                {
                    empire.DepartmentOfScience.UpdateTechnologiesCost();
                }
            }
            lock (CurrencyManager.Lock)
            {
                world.TradePartners = (int[])now.Clone();
            }
        }
    }
}
