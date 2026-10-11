using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod.Diplomacia.Blocs
{
    /// <summary>
    /// Comércio interno do bloco: a manutenção das rotas entre membros sai 25% mais barata. O jogo cobra a manutenção
    /// de quem compra; o mod devolve 25% dela no fim do turno (thread da simulação, no mesmo passo do pedágio).
    /// O pedágio e o bloqueio entre membros já caem em TradePolicy.GetMode.
    /// </summary>
    internal static class BlocTrade
    {
        internal const double UpkeepDiscount = 0.25;

        /// <summary>Devolvido no último turno, por império (para a tela Alianças). Trocado inteiro a cada turno.</summary>
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
                Plugin.Log.LogInfo($"Bloco: império {refund.Key} recebeu {amount} de volta da manutenção das rotas com o bloco.");
            }
            LastRefund = refunds;
        }
    }
}
