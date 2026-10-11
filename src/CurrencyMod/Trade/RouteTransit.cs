using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod
{
    /// <summary>
    /// Route transit (Lucas, 2026-10-11): in the base game the buyer's per-turn route upkeep is destroyed. Here the whole
    /// upkeep is paid out to the empires that own the territories the route crosses, other than the buyer, in proportion
    /// to how many of the path's territories each one owns (4 of X and 2 of Y: X gets 2/3, Y 1/3). The buyer's own land
    /// and neutral land do not count; a route crossing only those pays nobody, as before. The game already charged the
    /// buyer, so the mod only credits the owners, converted into their currency. Runs at end of turn on the simulation
    /// thread, in the same step as the tolls.
    /// </summary>
    internal static class RouteTransit
    {
        /// <summary>Received last turn, per empire (for screens and the dev channel). Replaced wholesale each turn.</summary>
        internal static volatile Dictionary<int, double> LastReceived = new Dictionary<int, double>();

        internal static void PayOwners(CurrencyWorld world)
        {
            var received = new Dictionary<int, double>();
            foreach (ExternalTradeRelation relation in Sandbox.TradeController.ExternalTradeRelations)
            {
                if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active || relation.PathAsTerritories == null)
                {
                    continue;
                }
                foreach (ExternalTradeExchange exchange in new[] { relation.LeftTradeExchange.Entity, relation.RightTradeExchange.Entity })
                {
                    Empire buyer = exchange?.BuyerEmpire.Entity;
                    double upkeep = exchange != null ? (float)exchange.TradeUpkeepAfterResourceDiversification.Value : 0;
                    if (buyer == null || buyer.Index >= Sandbox.NumberOfMajorEmpires || upkeep <= 0 || EconomyConfig.RouteTransitShare.Value <= 0)
                    {
                        continue;
                    }
                    var owned = new Dictionary<int, int>();
                    int counted = 0;
                    for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                    {
                        Territory territory = relation.PathAsTerritories[i];
                        MajorEmpire owner = territory != null ? TradeBlockade.OwnerOf(territory.Index) : null;
                        if (owner == null || owner.Index == buyer.Index)
                        {
                            continue;
                        }
                        owned.TryGetValue(owner.Index, out int count);
                        owned[owner.Index] = count + 1;
                        counted++;
                    }
                    foreach (KeyValuePair<int, int> share in owned)
                    {
                        double paidByBuyer = upkeep * EconomyConfig.RouteTransitShare.Value * share.Value / counted;
                        double gain = paidByBuyer * world.Rate(buyer.Index, share.Key);
                        received.TryGetValue(share.Key, out double sum);
                        received[share.Key] = sum + gain;
                        EconomySimulation.RecordMoney(world, buyer.Index, share.Key, paidByBuyer, gain, MoneyKind.Transit);
                    }
                }
            }
            foreach (KeyValuePair<int, double> pay in received)
            {
                MajorEmpire owner = Sandbox.MajorEmpires[pay.Key];
                double amount = Math.Round(pay.Value, 1);
                if (owner == null || amount <= 0)
                {
                    continue;
                }
                TransferConversion.WithoutRecording(() => owner.DepartmentOfTheTreasury.GainMoney((FixedPoint)(float)amount));
                Plugin.Log.LogInfo($"Route transit: empire {pay.Key} received {amount} for routes crossing its territories.");
            }
            LastReceived = received;
        }
    }
}
