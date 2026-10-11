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
    /// Bônus de veterano dos ministros (melhorias-jogando item 3): quem fica no cargo rende +1% a cada 5 turnos na área da
    /// pasta, até +10%. Pelo mesmo caminho que a economia do mod já usa (Harmony nas contas do jogo, na thread do sandbox),
    /// não por descritores: esses só entram na compilação do boot e não sobrevivem à recarga do núcleo.
    /// - Fim de turno (antes dos passes): dinheiro, ciência, influência e comida ganham o bônus sobre o rendimento líquido
    ///   positivo; Comércio ganha sobre o que o império vendeu pelas rotas no turno passado (livro do Banco Central).
    /// - Indústria: o mesmo ComputeProductionIncome que o crédito usa (a fila de construção, a IA e as telas veem).
    /// - Estabilidade: a meta de ordem pública sobe os pontos do bônus.
    /// - Força de combate: BattleUnit.GetCombatStrength (só na batalha).
    /// - Espionagem: peso dos espiões na interceptação (Espionage).
    /// - Fé: sem efeito por enquanto (o jogo não tem estoque de fé para somar).
    /// A tabela é montada na thread principal (IaModule, uma vez por segundo) e só lida nas outras.
    /// </summary>
    internal static class VeteranEffects
    {
        internal enum Area { Food, Industry, Money, Science, Influence, Stability, Combat, Trade, Faith, Spy }

        private static readonly int AreaCount = Enum.GetValues(typeof(Area)).Length;

        /// <summary>empire → % por área. Trocada inteira a cada publicação.</summary>
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

        /// <summary>Thread principal: refaz a tabela com os conselhos atuais.</summary>
        internal static void Publish(IaWorld world, int turn)
        {
            var built = new Dictionary<int, int[]>();
            if (world != null && IaConfig.Councils.Value)
            {
                foreach (IaNation nation in world.Nations)
                {
                    if (nation.CouncilModel < 1)
                    {
                        continue; // conselho antigo, ainda não zerado: sem bônus
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

        /// <summary>Bônus em % do império na área (0 sem ministro veterano).</summary>
        internal static int Get(int empire, Area area)
        {
            return !SavePatches.IsGameOnline() && table.TryGetValue(empire, out int[] values) ? values[(int)area] : 0;
        }

        internal static double Factor(int empire, Area area) => 1.0 + Get(empire, area) / 100.0;

        // ---------------- Fim de turno ----------------

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
                    Plugin.Log.LogWarning($"Bônus de veterano no fim do turno: {ex.Message}");
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
                log.Add($"dinheiro +{(float)empire.MoneyNet.Value * Pct(Area.Money):0.0}");
            }
            if (bonus[(int)Area.Science] > 0 && (float)empire.ResearchNet.Value > 0)
            {
                empire.DepartmentOfScience.GainResearch((FixedPoint)((float)empire.ResearchNet.Value * Pct(Area.Science)), raiseSimulationEvents: false);
                log.Add($"ciência +{(float)empire.ResearchNet.Value * Pct(Area.Science):0.0}");
            }
            if (bonus[(int)Area.Influence] > 0 && (float)empire.InfluenceNet.Value > 0)
            {
                empire.DepartmentOfCulture.GainInfluence((FixedPoint)((float)empire.InfluenceNet.Value * Pct(Area.Influence)));
                log.Add($"influência +{(float)empire.InfluenceNet.Value * Pct(Area.Influence):0.0}");
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
                        log.Add($"comida +{growth * Pct(Area.Food):0.0} em {settlement.EntityName}");
                    }
                }
            }
            if (bonus[(int)Area.Trade] > 0)
            {
                // O que o império ganhou vendendo pelas rotas no turno passado (livro do Banco Central), na moeda dele.
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
                    log.Add($"comércio +{sold * Pct(Area.Trade):0.0}");
                }
            }
            if (log.Count > 0)
            {
                Plugin.Log.LogInfo($"Veterano E{empire.Index}: {string.Join(", ", log)}");
            }
        }

        // ---------------- Indústria ----------------

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
                    // Nunca atrapalhar a construção.
                }
            }
        }

        // ---------------- Fé ----------------

        internal static bool FaithBypass;

        /// <summary>Teste ("ia conselho fe T#"): força de crença do território sem e com o bônus do veterano da Fé.</summary>
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
        /// A fé no jogo não é estoque: cada território soma a "força de crença" de cada religião, e a maior se espalha.
        /// O veterano da Fé aumenta em % a força da religião do império nos territórios dele.
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
                    // Sem o bônus, fica a força calculada pelo jogo.
                }
            }
        }

        // ---------------- Estabilidade ----------------

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
                    // A meta sobe os pontos do bônus; o passo por turno respeita as tendências do jogo.
                    double target = Math.Min(100.0, (float)NativeEffects.EffectiveStabilityTarget(settlement, NativeEffects.StabilityLoss(settlement)) + bonus);
                    double current = (float)settlement.PublicOrderCurrent.Value;
                    double down = (float)settlement.PublicOrderNegativeTrend.Value;
                    double up = (float)settlement.PublicOrderPositiveTrend.Value;
                    __result = (FixedPoint)(float)Math.Max(-down, Math.Min(Math.Max(up, 1.0), target - current));
                }
                catch (Exception)
                {
                    // Sem o bônus, fica o ganho calculado antes.
                }
            }
        }

        // ---------------- Combate ----------------

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
                    // Sem o bônus, fica a força do jogo.
                }
            }
        }
    }
}
