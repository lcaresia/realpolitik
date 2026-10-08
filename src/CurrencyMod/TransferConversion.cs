using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Amplitude;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace CurrencyMod
{
    /// <summary>
    /// Converte dinheiro entre moedas quando ele passa de um império para outro.
    ///
    /// O jogo não tem "transferência": cada operação é um Pay/GainMoney separado em cada império.
    /// Por isso os métodos que transferem dinheiro abrem um "contexto": dentro dele, todo Pay e
    /// GainMoney é anotado. Ao sair, se houve exatamente um pagador, cada império que recebeu
    /// tem o valor ajustado pelo câmbio (pagador -> recebedor).
    /// </summary>
    internal static class TransferConversion
    {
        private sealed class Context
        {
            public readonly Dictionary<int, double> Paid = new Dictionary<int, double>();
            public readonly Dictionary<int, double> Received = new Dictionary<int, double>();
            public string Source;
        }

        [ThreadStatic] private static Context current;
        [ThreadStatic] private static int depth;
        [ThreadStatic] private static bool suppressRecording;

        // Métodos da simulação que movem dinheiro entre impérios.
        private static readonly (string type, string method)[] TransferMethods =
        {
            ("Amplitude.Mercury.Simulation.TradeController", "OrderSetTradeResourceAccessCountFor"),
            ("Amplitude.Mercury.Simulation.TradeController", "OrderBuyAllTradeResourceAccessesFor"),
            ("Amplitude.Mercury.Simulation.DiplomaticAncillary", "AcceptGift"),
            ("Amplitude.Mercury.Simulation.DiplomaticAncillary", "AcceptSurrender"),
            ("Amplitude.Mercury.Simulation.DiplomaticRelationHelper", "ExecuteAction"),
            ("Amplitude.Mercury.Simulation.DiplomaticGrievanceHelper", "ApplyMoneyGain"),
            ("Amplitude.Mercury.Simulation.DiplomaticGrievanceHelper", "ApplyObsoleteGain"),
            ("Amplitude.Mercury.Simulation.DepartmentOfForeignAffairs", "OtherAcceptMyDemands"),
            ("Amplitude.Mercury.Simulation.DepartmentOfTheInterior", "ProcessOrderBuyNeighbourTerritoryWithMoney"),
            ("Amplitude.Mercury.Simulation.DepartmentOfIndustry", "OnConstructionCompleted"),
        };

        /// <summary>Executa uma ação de dinheiro do próprio mod sem ela entrar na conversão.</summary>
        public static void WithoutRecording(System.Action action)
        {
            bool previous = suppressRecording;
            suppressRecording = true;
            try
            {
                action();
            }
            finally
            {
                suppressRecording = previous;
            }
        }

        private static bool Active => EconomyConfig.EnableConversion.Value && !SavePatches.IsGameOnline();

        // ---------- Contexto de transferência ----------

        [HarmonyPatch]
        private static class TransferScopePatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach ((string typeName, string methodName) in TransferMethods)
                {
                    Type type = AccessTools.TypeByName(typeName);
                    if (type == null)
                    {
                        Plugin.Log.LogWarning($"Conversão: tipo {typeName} não encontrado.");
                        continue;
                    }
                    List<MethodInfo> methods = AccessTools.GetDeclaredMethods(type).Where(m => m.Name == methodName && !m.IsAbstract).ToList();
                    if (methods.Count == 0)
                    {
                        Plugin.Log.LogWarning($"Conversão: método {typeName}.{methodName} não encontrado.");
                    }
                    foreach (MethodInfo method in methods)
                    {
                        yield return method;
                    }
                }
            }

            private static void Prefix(MethodBase __originalMethod)
            {
                if (depth++ == 0)
                {
                    current = new Context { Source = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name };
                }
            }

            private static Exception Finalizer(Exception __exception)
            {
                if (--depth == 0)
                {
                    Context finished = current;
                    current = null;
                    if (__exception == null && finished != null && Active)
                    {
                        try
                        {
                            Settle(finished);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"Erro na conversão de moeda ({finished.Source}): {ex}");
                        }
                    }
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(DepartmentOfTheTreasury), nameof(DepartmentOfTheTreasury.Pay))]
        private static class PayPatch
        {
            private static void Prefix(DepartmentOfTheTreasury __instance, FixedPoint cost)
            {
                Record(__instance, -(float)cost);
            }
        }

        [HarmonyPatch(typeof(DepartmentOfTheTreasury), nameof(DepartmentOfTheTreasury.GainMoney))]
        private static class GainMoneyPatch
        {
            private static void Prefix(DepartmentOfTheTreasury __instance, FixedPoint gain)
            {
                Record(__instance, (float)gain);
            }
        }

        private static void Record(DepartmentOfTheTreasury treasury, double amount)
        {
            if (current == null || suppressRecording || treasury.majorEmpire == null || amount == 0)
            {
                return;
            }
            int index = treasury.majorEmpire.Index;
            Dictionary<int, double> bucket = amount < 0 ? current.Paid : current.Received;
            bucket.TryGetValue(index, out double total);
            bucket[index] = total + Math.Abs(amount);
        }

        private static void Settle(Context context)
        {
            // Quem pagou mais do que recebeu é pagador líquido.
            List<int> payers = context.Paid
                .Where(p => p.Value > (context.Received.TryGetValue(p.Key, out double r) ? r : 0))
                .Select(p => p.Key)
                .ToList();
            if (payers.Count != 1)
            {
                return;
            }
            int payer = payers[0];

            CurrencyWorld world = CurrencyManager.Current;
            if (world == null)
            {
                return;
            }

            foreach (KeyValuePair<int, double> received in context.Received)
            {
                int receiver = received.Key;
                if (receiver == payer || receiver < 0 || receiver >= Sandbox.NumberOfMajorEmpires)
                {
                    continue;
                }
                double rate;
                lock (CurrencyManager.Lock)
                {
                    rate = world.Rate(payer, receiver);
                    // Livro do Banco Central: quem pagou, quem ganhou e quanto (cada um na sua moeda).
                    double totalReceived = context.Received.Where(r => r.Key != payer).Sum(r => r.Value);
                    double netPaid = Math.Max(0, context.Paid[payer] - (context.Received.TryGetValue(payer, out double back) ? back : 0));
                    double paidShare = totalReceived > 0 ? netPaid * (received.Value / totalReceived) : 0;
                    EconomySimulation.RecordMoney(world, payer, receiver, paidShare, received.Value * rate,
                        context.Source != null && context.Source.StartsWith("TradeController") ? MoneyKind.Resources : MoneyKind.Other);
                }
                double adjustment = received.Value * (rate - 1.0);
                if (Math.Abs(adjustment) < 0.001)
                {
                    continue;
                }
                MajorEmpire receiverEmpire = Sandbox.MajorEmpires[receiver];
                if (receiverEmpire?.DepartmentOfTheTreasury == null)
                {
                    continue;
                }
                WithoutRecording(() => receiverEmpire.DepartmentOfTheTreasury.GainMoney((FixedPoint)(float)adjustment));
                lock (CurrencyManager.Lock)
                {
                    EmpireCurrency receiverCurrency = world.Get(receiver);
                    if (receiverCurrency != null)
                    {
                        receiverCurrency.LastConversionGain += adjustment;
                    }
                }
                Plugin.Log.LogInfo($"[{context.Source}] {received.Value:0.##} de #{payer} -> #{receiver} convertido a {rate:0.###} (ajuste {adjustment:+0.##;-0.##}).");
            }
        }

        // ---------- Preço de recursos na moeda do comprador ----------

        /// <summary>
        /// O preço da licença é definido na moeda do vendedor; o comprador vê e paga convertido.
        /// Este método é a fonte única do preço para simulação, interface e IA.
        /// </summary>
        [HarmonyPatch(typeof(TradeController), nameof(TradeController.GetSetupTradeBuyerCostPerResource))]
        private static class TradePricePatch
        {
            private static void Postfix(MajorEmpire buyerEmpire, Empire sellerEmpire, ref FixedPoint __result)
            {
                if (!Active || buyerEmpire == null || !(sellerEmpire is MajorEmpire seller) || __result <= 0)
                {
                    return;
                }
                CurrencyWorld world = CurrencyManager.Current;
                if (world == null)
                {
                    return;
                }
                double rate;
                lock (CurrencyManager.Lock)
                {
                    rate = world.Rate(seller.Index, buyerEmpire.Index);
                }
                double converted = Math.Max(1.0, (float)__result * rate);
                __result = (FixedPoint)(float)Math.Round(converted);
            }
        }
    }
}
