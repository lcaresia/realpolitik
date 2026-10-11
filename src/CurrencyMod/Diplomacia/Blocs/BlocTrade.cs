using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod.Diplomacia.Blocs
{
    /// <summary>
    /// Intra-bloc trade: route upkeep between members is 25% cheaper. The game charges the upkeep to the buyer; the
    /// mod refunds 25% of it at end of turn (simulation thread, in the same step as the toll).
    /// Toll and blockade between members already fall away in TradePolicy.GetMode.
    /// </summary>
    internal static class BlocTrade
    {
        internal const double UpkeepDiscount = 0.25;

        /// <summary>Refunded last turn, per empire (for the Alliances screen). Replaced wholesale every turn.</summary>
        internal static volatile Dictionary<int, double> LastRefund = new Dictionary<int, double>();

        internal static void RefundUpkeep()
        {
            var refunds = new Dictionary<int, double>();
            foreach (ExternalTradeRelation relation in Sandbox.TradeController.ExternalTradeRelations)
            {
                if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active || !BlocIndex.SameBloc(relation.LeftEmpireIndex, relation.RightEmpireIndex))
                {
                    continue;
                }
                foreach (ExternalTradeExchange exchange in new[] { relation.LeftTradeExchange.Entity, relation.RightTradeExchange.Entity })
                {
                    Empire buyer = exchange?.BuyerEmpire.Entity;
                    if (buyer == null || buyer.Index >= Sandbox.NumberOfMajorEmpires)
                    {
                        continue;
                    }
                    double upkeep = (float)exchange.TradeUpkeepAfterResourceDiversification.Value;
                    if (upkeep > 0)
                    {
                        refunds.TryGetValue(buyer.Index, out double sum);
                        refunds[buyer.Index] = sum + upkeep * UpkeepDiscount;
                    }
                }
            }
            foreach (KeyValuePair<int, double> refund in refunds)
            {
                MajorEmpire empire = Sandbox.MajorEmpires[refund.Key];
                double amount = Math.Round(refund.Value, 1);
                if (empire == null || amount <= 0)
                {
                    continue;
                }
                TransferConversion.WithoutRecording(() => empire.DepartmentOfTheTreasury.GainMoney((FixedPoint)(float)amount));
                Plugin.Log.LogInfo($"Bloc: empire {refund.Key} got {amount} back from route upkeep with the bloc.");
            }
            LastRefund = refunds;
        }
    }
}
