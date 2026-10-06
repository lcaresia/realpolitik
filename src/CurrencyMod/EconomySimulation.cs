using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace CurrencyMod
{
    /// <summary>
    /// Inflação, juros e câmbio, com o ciclo produção → inflação → juros → produção (docs\proposta-ciclo-economico.md,
    /// pesquisa em economia e jogos no mesmo documento):
    /// - oferta × demanda: dinheiro crescendo mais rápido que a produção vira inflação; produção crescendo mais rápido
    ///   segura os preços. A referência acompanha o próprio império em ~15 turnos (inflação ≈ crescimento do dinheiro −
    ///   crescimento real), então a vantagem é de ritmo, não de tamanho;
    /// - a inflação puxa os juros automáticos (regra de Taylor com hiato de demanda) e, acima de 0,75% por turno, baixa
    ///   a meta de estabilidade das cidades, mais forte acima de 1,5%;
    /// - juros agem forte e rápido na demanda (direto na inflação) e devagar na oferta: o juro real acima do neutro
    ///   corta o investimento e a produção anda até o novo patamar em ~6 turnos (CreditProductionPatch). Produção
    ///   cortada por crédito caro vira desemprego, que também baixa a meta de estabilidade;
    /// - esses efeitos entram nos números e nos detalhamentos do próprio jogo (NativeEffects.cs);
    /// - os juros do saldo têm teto suave: rendem plenamente até ~15 turnos de renda e cada vez menos acima disso.
    /// Roda na thread do sandbox, logo antes do jogo somar a renda do turno (DepartmentOfTheTreasury.TurnEndPass_CollectMoney),
    /// uma vez por império.
    /// </summary>
    [HarmonyPatch(typeof(DepartmentOfTheTreasury), "TurnEndPass_CollectMoney")]
    internal static class EconomySimulation
    {
        /// <summary>
        /// Efeitos de um império que o jogo consulta a toda hora (produção das cidades, meta de estabilidade, juros
        /// projetados na renda): uma cópia imutável por turno, lida sem lock pelas threads do sandbox, da IA e da UI.
        /// </summary>
        internal sealed class EmpireEffects
        {
            internal static readonly EmpireEffects Neutral = new EmpireEffects();

            /// <summary>Multiplicador da produção das cidades pelo crédito (juros).</summary>
            internal double Credit = 1.0;
            /// <summary>Quanto a meta de estabilidade de cada cidade cai pela inflação e pelo desemprego.</summary>
            internal double InflationLoss;
            internal double UnemploymentLoss;
            internal double InterestRate;
            internal double InflationRate;
            internal double SmoothedIncome;
            internal bool Active;

            internal double StabilityLoss => InflationLoss + UnemploymentLoss;
        }

        private static volatile EmpireEffects[] effects = new EmpireEffects[0];
        /// <summary>Peso do turno novo nas médias móveis de produção e renda (0,3 ≈ memória de uns 3 turnos).</summary>
        private const double Smoothing = 0.3;
        private const double MaxPressure = 0.5;

        internal static EmpireEffects Effects(int empireIndex)
        {
            EmpireEffects[] current = effects;
            if (current.Length == 0)
            {
                current = ResetEffects();
            }
            return empireIndex >= 0 && empireIndex < current.Length ? current[empireIndex] : EmpireEffects.Neutral;
        }

        internal static double CreditFactor(int empireIndex) => Effects(empireIndex).Credit;

        /// <summary>Esvazia a tabela: a próxima leitura refaz a partir do estado.</summary>
        internal static void ResetEffectsLater()
        {
            effects = new EmpireEffects[0];
        }

        /// <summary>Refaz a tabela de efeitos a partir do estado salvo (save carregado, partida nova, juros mudados por comando).</summary>
        internal static EmpireEffects[] ResetEffects()
        {
            CurrencyWorld world = CurrencyManager.Current;
            EmpireEffects[] built = new EmpireEffects[0];
            if (world != null)
            {
                lock (CurrencyManager.Lock)
                {
                    built = world.Empires.Select(BuildEffects).ToArray();
                    // Publicar dentro do lock: fora dele, a tabela do fim de turno gravada no meio seria sobrescrita
                    // por esta, já velha.
                    effects = built;
                }
                return built;
            }
            effects = built;
            return built;
        }

        private static EmpireEffects BuildEffects(EmpireCurrency currency)
        {
            return new EmpireEffects
            {
                Credit = currency.CreditFactor,
                InflationLoss = InflationStabilityLoss(currency.InflationRate),
                UnemploymentLoss = UnemploymentStabilityLoss(currency.CreditFactor),
                InterestRate = currency.InterestRate,
                InflationRate = currency.InflationRate,
                SmoothedIncome = currency.SmoothedIncome,
                Active = true,
            };
        }

        /// <summary>
        /// Economia global antes dos passes de todos os impérios. O jogo roda todos os passes de um império antes do
        /// próximo (SandboxState_TurnEnd), então, feita só no CollectMoney do primeiro, o império 0 investia a produção
        /// e calculava a ordem pública com o crédito e a meta do turno anterior, e os outros com os do turno atual.
        /// O CollectMoney continua como reserva (LastProcessedTurn impede rodar duas vezes).
        /// </summary>
        [HarmonyPatch(typeof(SimulationPasses), nameof(SimulationPasses.InvokePasses))]
        private static class BeforeEmpirePassesPatch
        {
            private static void Prefix(SimulationPasses __instance, SimulationPasses.PassContext context)
            {
                if (context != SimulationPasses.PassContext.TurnEnd || !ReferenceEquals(__instance, Ancillary.Passes)
                    || !EconomyConfig.EnableEconomy.Value || SavePatches.IsGameOnline())
                {
                    return;
                }
                try
                {
                    Sandbox sandbox = SandboxManager.Sandbox;
                    if (sandbox == null)
                    {
                        return;
                    }
                    CurrencyWorld world = CurrencyManager.Ensure(sandbox.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
                    lock (CurrencyManager.Lock)
                    {
                        if (world.LastProcessedTurn != sandbox.Turn)
                        {
                            UpdateGlobalEconomy(world, sandbox.Turn);
                            world.LastProcessedTurn = sandbox.Turn;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Erro na simulação econômica: {ex}");
                }
            }
        }

        private static void Prefix(DepartmentOfTheTreasury __instance)
        {
            if (!EconomyConfig.EnableEconomy.Value || SavePatches.IsGameOnline())
            {
                return;
            }
            try
            {
                MajorEmpire empire = __instance.majorEmpire;
                Sandbox sandbox = SandboxManager.Sandbox;
                if (empire == null || sandbox == null)
                {
                    return;
                }

                CurrencyWorld world = CurrencyManager.Ensure(sandbox.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
                lock (CurrencyManager.Lock)
                {
                    if (world.LastProcessedTurn != sandbox.Turn)
                    {
                        UpdateGlobalEconomy(world, sandbox.Turn);
                        world.LastProcessedTurn = sandbox.Turn;
                    }
                    ApplyMonetaryEffects(world, empire, __instance);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Erro na simulação econômica: {ex}");
            }
        }

        /// <summary>Juro real neutro: o juro neutro menos a meta de inflação.</summary>
        private static double NeutralRealRate => EconomyConfig.NeutralInterest.Value - EconomyConfig.TargetInflation.Value;

        /// <summary>Quanto os juros automáticos andam por turno em direção ao que a regra de Taylor pede.</summary>
        internal const double AutoInterestStep = 0.25;

        /// <summary>
        /// Regra de Taylor: os juros que o banco automático procura. Inflação acima da meta e demanda acima da oferta sobem.
        /// A parte da inflação que veio da dívida conta com teto suave (DebtInRule): subir juros por ela encarece a própria
        /// dívida, e a IA endividada ficava presa (40% dos retratos da partida longa; relatório de calibração).
        /// </summary>
        internal static double TaylorRate(double inflationRate, double pressure, double debtInflation = 0)
        {
            double ruleInflation = inflationRate - debtInflation + DebtInRule(debtInflation);
            double wanted = EconomyConfig.NeutralInterest.Value
                + EconomyConfig.AutoInterestReaction.Value * (ruleInflation - EconomyConfig.TargetInflation.Value)
                + EconomyConfig.PressureInterestReaction.Value * pressure;
            return Clamp(wanted, EconomyConfig.MinInterest.Value, EconomyConfig.MaxInterest.Value);
        }

        /// <summary>
        /// Quanto da inflação vinda da dívida a regra dos juros leva em conta: quase tudo quando é pequena, cada vez menos
        /// quando cresce, encostando em [Juros] DividaNaRegra (0 = tudo, como antes). Nunca zera: a dívida ainda pesa.
        /// </summary>
        internal static double DebtInRule(double debtInflation)
        {
            double cap = EconomyConfig.DebtRuleCap.Value;
            return debtInflation <= 0 || cap <= 0 ? debtInflation : cap * Math.Tanh(debtInflation / cap);
        }

        /// <summary>Alvo do multiplicador de produção pelo juro real (investimento): caro corta, barato aquece.</summary>
        internal static double CreditTarget(double interestRate, double inflationRate)
        {
            double real = interestRate - inflationRate;
            double factor = 1.0 - EconomyConfig.RealRateProductionEffect.Value * (real - NeutralRealRate);
            return Clamp(factor, EconomyConfig.MinCreditFactor.Value, EconomyConfig.MaxCreditFactor.Value);
        }

        /// <summary>Recalcula PIB, pressão de demanda, inflação, juros automáticos, investimento, preços e câmbio de todos.</summary>
        private static void UpdateGlobalEconomy(CurrencyWorld world, int turn)
        {
            int count = Math.Min(Sandbox.NumberOfMajorEmpires, world.Empires.Count);
            var alive = new bool[count];
            var logPressure = new double[count];
            var hasPressure = new bool[count];

            // 1. Força econômica de cada império vivo (produção efetiva: o crédito do turno já valeu para ela).
            double strengthSum = 0;
            int aliveCount = 0;
            for (int i = 0; i < count; i++)
            {
                MajorEmpire empire = Sandbox.MajorEmpires[i];
                EmpireCurrency currency = world.Empires[i];
                currency.LastConversionGain = 0;
                if (empire == null || !empire.IsAlive)
                {
                    continue;
                }
                alive[i] = true;

                double industry = 0;
                for (int s = 0; s < empire.Settlements.Count; s++)
                {
                    Settlement settlement = empire.Settlements[s];
                    // Modo ciência: a indústria vira ciência (o jogo devolve 0 em ComputeProductionIncome) e já entra
                    // no ResearchNet; somar aqui contaria duas vezes.
                    if (settlement != null && (settlement.CityFlags & CityFlags.ScienceMode) == CityFlags.None)
                    {
                        industry += (float)settlement.ProductionNetAfterAffinityBonuses.Value;
                    }
                }
                double production = industry * currency.CreditFactor;
                double money = Math.Max(0, (float)empire.MoneyNet.Value);
                currency.SmoothedProduction = currency.SmoothedProduction <= 0 ? production : currency.SmoothedProduction + Smoothing * (production - currency.SmoothedProduction);
                currency.SmoothedIncome = currency.SmoothedIncome <= 0 ? money : currency.SmoothedIncome + Smoothing * (money - currency.SmoothedIncome);

                double science = Math.Max(0, (float)empire.ResearchNet.Value);
                double influence = Math.Max(0, (float)empire.InfluenceNet.Value);
                double stability01 = Clamp((float)empire.Stability.Value / 100.0, 0, 1);
                bool bankrupt = (float)empire.MoneyStock.Value < 0;

                currency.Gdp = money + production + science + influence;
                currency.Stability = stability01;
                currency.Strength = Math.Max(1, currency.Gdp) * (0.7 + 0.5 * stability01) * (bankrupt ? EconomyConfig.BankruptStrengthFactor.Value : 1.0);
                strengthSum += currency.Strength;
                aliveCount++;

                // 2. Oferta × demanda: dinheiro por unidade de produção contra a referência do próprio império
                //    (o desvio contra a tendência do mundo sai logo abaixo).
                if (currency.SmoothedProduction > 1)
                {
                    double ratio = Math.Max(10.0, currency.SmoothedIncome) / currency.SmoothedProduction;
                    if (currency.DemandBaseline <= 0)
                    {
                        currency.DemandBaseline = ratio;
                    }
                    logPressure[i] = Math.Log(ratio / currency.DemandBaseline);
                    hasPressure[i] = true;
                    currency.DemandBaseline += EconomyConfig.DemandAdaptation.Value * (ratio - currency.DemandBaseline);
                }
            }
            if (aliveCount == 0)
            {
                return;
            }
            double strengthAverage = strengthSum / aliveCount;

            // A produção do jogo cresce mais rápido que o dinheiro para todo mundo no começo (é o desenho do jogo, não
            // política monetária): só conta o desvio de cada império contra a mediana do mundo.
            var trend = new List<double>();
            for (int i = 0; i < count; i++)
            {
                if (hasPressure[i])
                {
                    trend.Add(logPressure[i]);
                }
            }
            double worldTrend = Median(trend);
            for (int i = 0; i < count; i++)
            {
                EmpireCurrency currency = world.Empires[i];
                if (!alive[i] || !hasPressure[i])
                {
                    currency.Pressure = 0;
                    currency.CoverageInflation = 0;
                    currency.Coverage = 1.0;
                    continue;
                }
                currency.Pressure = Clamp(Math.Exp(logPressure[i] - worldTrend) - 1.0, -MaxPressure, MaxPressure);
                // Teto suave: perto de zero é o linear de sempre; longe, encosta no limite aos poucos. O corte seco deixava a
                // parcela colada em ±limite em 58% dos retratos da partida longa, pulando de um lado para o outro.
                double limit = EconomyConfig.PressureInflationLimit.Value;
                double raw = EconomyConfig.PressureInflation.Value * currency.Pressure;
                currency.CoverageInflation = limit > 0 ? limit * Math.Tanh(raw / limit) : 0;
                currency.Coverage = 1.0 / (1.0 + currency.Pressure);
            }

            // 3. Inflação, juros, investimento e índice de preços.
            double logPriceSum = 0;
            for (int i = 0; i < count; i++)
            {
                MajorEmpire empire = Sandbox.MajorEmpires[i];
                if (!alive[i])
                {
                    continue;
                }
                EmpireCurrency currency = world.Empires[i];
                double target = TargetInflation(empire, currency);
                currency.InflationRate += EconomyConfig.InflationSmoothing.Value * (target - currency.InflationRate);
                currency.InflationRate = Clamp(currency.InflationRate, EconomyConfig.MinInflation.Value, EconomyConfig.MaxInflation.Value);
                // A parte da dívida dentro da inflação de agora anda no mesmo ritmo que ela.
                currency.InflationRateFromDebt += EconomyConfig.InflationSmoothing.Value * (currency.InflationFromDebt - currency.InflationRateFromDebt);

                bool autoInterest = !currency.ForcedInterest && (currency.AutoInterest || !empire.IsControlledByHuman);
                if (autoInterest)
                {
                    currency.InterestRate += AutoInterestStep * (TaylorRate(currency.InflationRate, currency.Pressure, currency.InflationRateFromDebt) - currency.InterestRate);
                }
                currency.InterestRate = Clamp(currency.InterestRate, EconomyConfig.MinInterest.Value, EconomyConfig.MaxInterest.Value);

                // Investimento: a produção anda devagar até o patamar que o juro real permite (vale a partir do próximo turno).
                double creditTarget = CreditTarget(currency.InterestRate, currency.InflationRate);
                currency.CreditFactor += EconomyConfig.InvestmentSpeed.Value * (creditTarget - currency.CreditFactor);

                currency.PriceIndex = Math.Max(0.01, currency.PriceIndex * (1 + currency.InflationRate));
                logPriceSum += Math.Log(currency.PriceIndex);
            }
            // Crédito, meta de estabilidade e juros novos valem a partir de agora para o jogo inteiro.
            effects = world.Empires.Select(BuildEffects).ToArray();
            double priceGeoMean = Math.Exp(logPriceSum / aliveCount);

            // 4. Câmbio: força relativa dividida pelo nível de preços relativo.
            double logExchangeSum = 0;
            for (int i = 0; i < count; i++)
            {
                if (!alive[i])
                {
                    continue;
                }
                EmpireCurrency currency = world.Empires[i];
                double relativeStrength = currency.Strength / Math.Max(1e-6, strengthAverage);
                double relativePrice = currency.PriceIndex / priceGeoMean;
                double target = Math.Pow(relativeStrength, EconomyConfig.ExchangeElasticity.Value) / relativePrice;
                target = Clamp(target, EconomyConfig.ExchangeMin.Value, EconomyConfig.ExchangeMax.Value);
                currency.ExchangeValue += EconomyConfig.ExchangeSmoothing.Value * (target - currency.ExchangeValue);
                logExchangeSum += Math.Log(Math.Max(1e-6, currency.ExchangeValue));
            }

            // Normaliza para a média geométrica ficar em 1.0 (só muda a escala, não as cotações entre moedas).
            double exchangeGeoMean = Math.Exp(logExchangeSum / aliveCount);
            for (int i = 0; i < count; i++)
            {
                if (!alive[i])
                {
                    continue;
                }
                EmpireCurrency currency = world.Empires[i];
                currency.ExchangeValue = Clamp(currency.ExchangeValue / exchangeGeoMean, EconomyConfig.ExchangeMin.Value, EconomyConfig.ExchangeMax.Value);
                currency.PushHistory(turn);
            }
        }

        private static double TargetInflation(MajorEmpire empire, EmpireCurrency currency)
        {
            double stock = (float)empire.MoneyStock.Value;
            double income = (float)empire.MoneyNet.Value;
            double debt = 0;
            double hoarding = 0;

            if (stock < 0)
            {
                int deficitTurns = Math.Min(5, (int)(float)empire.ConsecutiveTurnEndedInMoneyDeficit.Value);
                debt = EconomyConfig.DeficitInflation.Value + EconomyConfig.DeficitInflationPerTurn.Value * deficitTurns;
            }
            else if (income > 0)
            {
                // Dinheiro parado demais em relação à renda vira pressão inflacionária. Com renda zero ou negativa o
                // império está gastando a reserva, não entesourando (antes caía no piso de renda 5 e levava o máximo).
                double turnsOfIncome = stock / Math.Max(5.0, income);
                double excess = turnsOfIncome / EconomyConfig.HoardingTurns.Value - 1.0;
                if (excess > 0)
                {
                    hoarding = Math.Min(0.01, EconomyConfig.HoardingInflation.Value * excess);
                }
            }

            double stability = (0.6 - currency.Stability) * EconomyConfig.StabilityInflation.Value;
            // Juros agem direto na demanda: mais forte que o efeito deles na oferta (senão subir juros subiria a inflação).
            double interest = -(currency.InterestRate - EconomyConfig.NeutralInterest.Value) * EconomyConfig.InterestInflationEffect.Value;

            currency.InflationFromDebt = debt;
            currency.InflationFromHoarding = hoarding;
            currency.InflationFromStability = stability;
            currency.InflationFromInterest = interest;
            currency.HasInflationBreakdown = true;
            // A oferta × demanda (CoverageInflation) já foi calculada para todos antes.
            currency.InflationTarget = EconomyConfig.BaseInflation.Value + debt + hoarding + currency.CoverageInflation + stability + interest;
            return currency.InflationTarget;
        }

        /// <summary>
        /// Juros do saldo (ou da dívida) num turno. Saldo positivo com juro real positivo tem teto suave: o banco aplica
        /// bem até ~N turnos de renda e o resto rende cada vez menos. Inflação acima dos juros corrói o saldo inteiro;
        /// dívida paga spread, e a inflação alta corrói a dívida também. A projeção na renda usa a mesma conta.
        /// </summary>
        internal static double InterestFlow(double stock, double interestRate, double inflationRate, double smoothedIncome)
        {
            double maxRate = EconomyConfig.MaxRealRatePerTurn.Value;
            if (stock < 0)
            {
                return stock * Clamp(interestRate + EconomyConfig.DebtSpread.Value - inflationRate, -maxRate, maxRate);
            }
            double realRate = Clamp(interestRate - inflationRate, -maxRate, maxRate);
            if (realRate <= 0)
            {
                return stock * realRate;
            }
            double capacity = EconomyConfig.SavingsCapacityTurns.Value * Math.Max(20.0, smoothedIncome);
            double invested = capacity > 0 ? stock * capacity / (stock + capacity) : stock;
            return invested * realRate;
        }

        /// <summary>
        /// Juros e erosão sobre o saldo e o efeito antigo dos juros na renda. A inflação e o desemprego não mexem mais
        /// direto na estabilidade: eles baixam a meta de estabilidade das cidades (NativeEffects), como as fontes do jogo.
        /// </summary>
        private static void ApplyMonetaryEffects(CurrencyWorld world, MajorEmpire empire, DepartmentOfTheTreasury treasury)
        {
            EmpireCurrency currency = world.Get(empire.Index);
            if (currency == null || !empire.IsAlive)
            {
                return;
            }

            double stock = (float)empire.MoneyStock.Value;
            double interestFlow = InterestFlow(stock, currency.InterestRate, currency.InflationRate, currency.SmoothedIncome);

            // Juros na renda em dinheiro (desligado por padrão: o custo dos juros passou para a produção).
            double income = (float)empire.MoneyNet.Value;
            double incomeEffect = 0;
            if (income > 0 && EconomyConfig.InterestIncomeDrag.Value > 0)
            {
                incomeEffect = -income * (currency.InterestRate - EconomyConfig.NeutralInterest.Value) * EconomyConfig.InterestIncomeDrag.Value * 10;
                incomeEffect = Clamp(incomeEffect, -income * 0.25, income * 0.15);
            }

            currency.LastInterestFlow = interestFlow;
            currency.LastIncomeEffect = incomeEffect;

            double total = interestFlow + incomeEffect;
            if (Math.Abs(total) >= 0.001)
            {
                TransferConversion.WithoutRecording(() => treasury.GainMoney((FixedPoint)(float)total));
            }

            // Só para exibição: a meta de estabilidade das cidades já desconta isso (NativeEffects).
            currency.LastStabilityPenalty = InflationStabilityLoss(currency.InflationRate);
            currency.LastUnemploymentPenalty = UnemploymentStabilityLoss(currency.CreditFactor);
        }

        /// <summary>Quanto a meta de estabilidade de cada cidade cai com essa inflação (em faixas).</summary>
        internal static double InflationStabilityLoss(double inflationRate)
        {
            double points = inflationRate * 100;
            return EconomyConfig.InflationStabilityLoss1.Value * Math.Max(0, points - EconomyConfig.InflationStabilityThreshold1.Value * 100)
                + EconomyConfig.InflationStabilityLoss2.Value * Math.Max(0, points - EconomyConfig.InflationStabilityThreshold2.Value * 100);
        }

        /// <summary>Quanto a meta de estabilidade de cada cidade cai com a produção cortada pelo crédito caro (desemprego).</summary>
        internal static double UnemploymentStabilityLoss(double creditFactor)
        {
            return EconomyConfig.UnemploymentStability.Value * Math.Max(0, 1.0 - creditFactor);
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0)
            {
                return 0;
            }
            values.Sort();
            int middle = values.Count / 2;
            return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
        }

        private static double Clamp(double value, double min, double max) => value < min ? min : (value > max ? max : value);
    }

    /// <summary>
    /// Juros → produção: multiplica a produção que entra nas construções pelo crédito/investimento do império. A IA, a
    /// lista de cidades e a fila de construção leem essa mesma conta; a tela da cidade e o detalhamento recebem o
    /// crédito em NativeEffects.
    /// </summary>
    [HarmonyPatch(typeof(DepartmentOfIndustry), nameof(DepartmentOfIndustry.ComputeProductionIncome))]
    internal static class CreditProductionPatch
    {
        private static void Postfix(Settlement settlement, ref FixedPoint __result)
        {
            try
            {
                if (!EconomyConfig.EnableEconomy.Value || settlement == null || !(__result > 0) || SavePatches.IsGameOnline())
                {
                    return;
                }
                if (!(settlement.Empire.Entity is MajorEmpire empire))
                {
                    return;
                }
                double factor = EconomySimulation.CreditFactor(empire.Index);
                if (Math.Abs(factor - 1.0) > 0.0005)
                {
                    __result = (FixedPoint)((float)__result * (float)factor);
                }
            }
            catch (Exception)
            {
                // Nunca atrapalhar a construção: sem crédito, fica o valor do jogo.
            }
        }
    }
}
