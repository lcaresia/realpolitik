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
    /// Os efeitos da economia entram no jogo como os efeitos dele: nos números que o jogo usa e mostra, e com uma linha
    /// própria nos detalhamentos nativos (o "resumo de buffs e debuffs" das cidades e do dinheiro). O jogo não deixa
    /// criar efeitos de dados novos em tempo de execução (os descritores são compilados), então cada efeito entra onde
    /// o jogo calcula e onde ele mostra o valor:
    /// - crédito (juros → produção): construção e IA (CreditProductionPatch), tela da cidade (SettlementCursorSnapshot)
    ///   e a linha "Crédito" no detalhamento de indústria;
    /// - inflação e desemprego: baixam a meta de estabilidade de cada cidade. A estabilidade anda até a meta no ritmo do
    ///   jogo (GetSettlementPublicOrderGain), a meta mostrada já vem descontada (SettlementInfo), a IA vê a meta
    ///   descontada e cada perda tem a sua linha no detalhamento de estabilidade;
    /// - juros do saldo: entram na renda por turno mostrada (barra do topo) e numa linha do detalhamento de dinheiro.
    /// Tudo roda nas threads do sandbox e da IA e lê só a tabela imutável de EconomySimulation.Effects.
    /// </summary>
    internal static class NativeEffects
    {
        internal const string ProductionProperty = "ProductionNetAfterAffinityBonuses";
        internal const string StabilityProperty = "PublicOrderTarget";
        internal const string MoneyProperty = "MoneyNet";

        internal static bool Enabled => EconomyConfig.EnableEconomy.Value && !SavePatches.IsGameOnline();

        /// <summary>Efeitos do dono de uma cidade de império maior; null se não houver efeito a aplicar.</summary>
        internal static EconomySimulation.EmpireEffects EffectsOf(Settlement settlement)
        {
            if (settlement == null || !Enabled || !(settlement.Empire.Entity is MajorEmpire empire))
            {
                return null;
            }
            EconomySimulation.EmpireEffects effects = EconomySimulation.Effects(empire.Index);
            return effects.Active ? effects : null;
        }

        /// <summary>Perda total na meta de estabilidade de uma cidade (0 se não for cidade de império maior).</summary>
        internal static double StabilityLoss(Settlement settlement)
        {
            if (settlement == null || settlement.SettlementStatus != SettlementStatuses.City)
            {
                return 0;
            }
            EconomySimulation.EmpireEffects effects = EffectsOf(settlement);
            return effects != null ? effects.StabilityLoss : 0;
        }

        /// <summary>Meta de estabilidade já com a inflação e o desemprego, sem passar do mínimo garantido da cidade.</summary>
        internal static FixedPoint EffectiveStabilityTarget(Settlement settlement, double loss)
        {
            double target = (float)settlement.PublicOrderTarget.Value;
            double minimum = Math.Max(0, Math.Min(target, (float)settlement.PublicOrderMinimum.Value));
            return (FixedPoint)(float)Math.Max(minimum, target - loss);
        }

        /// <summary>Juros que o saldo vai render (ou custar) no próximo fim de turno: a mesma conta da simulação.</summary>
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
            return EconomySimulation.InterestFlow((float)major.MoneyStock.Value, effects.InterestRate, effects.InflationRate, effects.SmoothedIncome);
        }

        /// <summary>Renda por turno que o jogo mostra: a dele mais os juros projetados (MoneyNetDisplayPatch).</summary>
        public static FixedPoint DisplayedMoneyNet(Empire empire)
        {
            FixedPoint net = empire.MoneyNet.Value;
            try
            {
                double interest = ProjectedInterest(empire);
                if (Math.Abs(interest) >= 0.05)
                {
                    return net + (FixedPoint)(float)interest;
                }
            }
            catch (Exception)
            {
                // Nunca atrapalhar a barra do topo: sem juros, fica o valor do jogo.
            }
            return net;
        }

        internal static string Percent(double rate) => Format.Percent(rate);
    }

    /// <summary>
    /// Linhas próprias nos detalhamentos nativos. O montador do jogo junta as partes de cada fonte (BreakdownParts),
    /// ordena e só depois escreve as linhas com o formato dele; uma parte com LocalizedSourceName entra igual às do
    /// jogo (valor colorido, ícone do recurso, ganhos antes das perdas).
    /// </summary>
    [HarmonyPatch(typeof(PropertyBreakdownWorker), nameof(PropertyBreakdownWorker.Evaluate))]
    internal static class BreakdownLinesPatch
    {
        private static readonly StaticString CreditId = new StaticString("CurrencyMod_Credit");
        private static readonly StaticString InflationId = new StaticString("CurrencyMod_Inflation");
        private static readonly StaticString UnemploymentId = new StaticString("CurrencyMod_Unemployment");
        private static readonly StaticString InterestId = new StaticString("CurrencyMod_Interest");
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
                    Plugin.Log.LogError($"Detalhamento nativo da economia: {ex}");
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
            // O próprio jogo descarta partes menores que meio ponto antes de escrever as linhas.
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
    /// A estabilidade anda até a meta descontada. Mesma fórmula do jogo (RPN "PublicOrderGain"):
    /// ganho = clamp(meta − atual, −queda por turno, +subida por turno).
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
                // Sem a perda, fica o ganho do jogo.
            }
        }
    }

    /// <summary>A meta de estabilidade que as telas mostram (SettlementInfo, copiada para a tela da cidade e a lista).</summary>
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
                // Tela com a meta do jogo é melhor que tela quebrada.
            }
        }
    }

    /// <summary>A IA enxerga a meta de estabilidade descontada (constrói estabilidade, evita expandir em crise...).</summary>
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

    /// <summary>Produção da tela da cidade com o crédito (a construção já recebe pelo CreditProductionPatch).</summary>
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
    /// Renda por turno do império com os juros projetados do saldo, nas duas cópias que o jogo faz para a interface
    /// (InitializeOnLoad e SynchronizeEmpireInfo). Troca "império.MoneyNet.Value" por NativeEffects.DisplayedMoneyNet,
    /// também na comparação que detecta mudança, para a cópia não ficar "suja" a cada quadro.
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
                    // Mantém rótulos e blocos das instruções originais.
                    list[i].opcode = OpCodes.Call;
                    list[i].operand = displayed;
                    list[i + 1].opcode = OpCodes.Nop;
                    list[i + 1].operand = null;
                    replaced++;
                }
            }
            Plugin.Log.LogInfo($"Renda com juros na interface: {replaced} leitura(s) trocada(s) em GameSnapshot.{__originalMethod?.Name}.");
            return list;
        }
    }
}
