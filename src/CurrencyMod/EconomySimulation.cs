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
    /// Inflation, interest and exchange rate, with the cycle production → inflation → interest → production
    /// (docs\proposta-ciclo-economico.md, research on economics and games in the same document):
    /// - supply × demand: money growing faster than production becomes inflation; production growing faster holds
    ///   prices down. The reference tracks the empire itself over ~15 turns (inflation ≈ money growth − real growth),
    ///   so the advantage is about pace, not size;
    /// - inflation pulls the automatic interest rate (Taylor rule with a demand gap) and, above 0.75% per turn, lowers
    ///   the cities' stability target, more strongly above 1.5%;
    /// - interest acts strongly and quickly on demand (directly on inflation) and slowly on supply: a real rate above
    ///   neutral cuts investment and production moves to the new level over ~6 turns (CreditProductionPatch).
    ///   Production cut by expensive credit becomes unemployment, which also lowers the stability target;
    /// - these effects go into the game's own numbers and breakdowns (NativeEffects.cs);
    /// - interest on the balance has a soft cap: it pays fully up to ~15 turns of income and less and less above that.
    /// Runs on the sandbox thread, right before the game adds up the turn's income
    /// (DepartmentOfTheTreasury.TurnEndPass_CollectMoney), once per empire.
    /// </summary>
    [HarmonyPatch(typeof(DepartmentOfTheTreasury), "TurnEndPass_CollectMoney")]
    internal static class EconomySimulation
    {
        /// <summary>
        /// An empire's effects that the game queries all the time (city production, stability target, interest
        /// projected in income): one immutable copy per turn, read without a lock by the sandbox, AI and UI threads.
        /// </summary>
        internal sealed class EmpireEffects
        {
            internal static readonly EmpireEffects Neutral = new EmpireEffects();

            /// <summary>City production multiplier from credit (interest).</summary>
            internal double Credit = 1.0;
            /// <summary>How much each city's stability target drops from inflation and unemployment.</summary>
            internal double InflationLoss;
            internal double UnemploymentLoss;
            internal double InterestRate;
            internal double InflationRate;
            internal double SmoothedIncome;
            internal double Gdp;
            internal bool Active;

            internal double StabilityLoss => InflationLoss + UnemploymentLoss;
        }

        private static volatile EmpireEffects[] effects = new EmpireEffects[0];
        /// <summary>Weight of the new turn in the moving averages of production and income (0.3 ≈ memory of about 3 turns).</summary>
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

        /// <summary>Clears the table: the next read rebuilds it from the state.</summary>
        internal static void ResetEffectsLater()
        {
            effects = new EmpireEffects[0];
        }

        /// <summary>Rebuilds the effects table from the saved state (save loaded, new game, interest changed by command).</summary>
        internal static EmpireEffects[] ResetEffects()
        {
            CurrencyWorld world = CurrencyManager.Current;
            EmpireEffects[] built = new EmpireEffects[0];
            if (world != null)
            {
                lock (CurrencyManager.Lock)
                {
                    built = world.Empires.Select(BuildEffects).ToArray();
                    // Publish inside the lock: outside it, the end-of-turn table written in the meantime would be
                    // overwritten by this one, already stale.
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
                Gdp = currency.Gdp,
                Active = true,
            };
        }

        /// <summary>
        /// Global economy before the passes of all empires. The game runs all passes of one empire before the next
        /// (SandboxState_TurnEnd), so, done only in the first one's CollectMoney, empire 0 invested production and
        /// computed public order with the previous turn's credit and target, and the others with the current turn's.
        /// CollectMoney stays as a fallback (LastProcessedTurn prevents running twice).
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
                    Plugin.Log.LogError($"Error in the economic simulation: {ex}");
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
                Plugin.Log.LogError($"Error in the economic simulation: {ex}");
            }
        }

        /// <summary>Neutral real rate: the neutral rate minus the inflation target.</summary>
        private static double NeutralRealRate => EconomyConfig.NeutralInterest.Value - EconomyConfig.TargetInflation.Value;

        /// <summary>How far the automatic interest rate moves per turn toward what the Taylor rule asks for.</summary>
        internal const double AutoInterestStep = 0.25;

        /// <summary>
        /// Taylor rule: the interest rate the automatic bank aims for. Inflation above target and demand above supply raise it.
        /// The part of inflation that came from debt counts with a soft cap (DebtInRule): raising interest because of it makes
        /// the debt itself more expensive, and indebted AI got stuck (40% of the long-game snapshots; calibration report).
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
        /// How much of the debt-driven inflation the interest rule takes into account: almost all when it is small, less and
        /// less as it grows, approaching [Juros] DividaNaRegra (0 = all, as before). Never zero: debt still weighs in.
        /// </summary>
        internal static double DebtInRule(double debtInflation)
        {
            double cap = EconomyConfig.DebtRuleCap.Value;
            return debtInflation <= 0 || cap <= 0 ? debtInflation : cap * Math.Tanh(debtInflation / cap);
        }

        /// <summary>Target of the production multiplier from the real rate (investment): expensive cuts, cheap heats up.</summary>
        internal static double CreditTarget(double interestRate, double inflationRate)
        {
            double real = interestRate - inflationRate;
            double factor = 1.0 - EconomyConfig.RealRateProductionEffect.Value * (real - NeutralRealRate);
            return Clamp(factor, EconomyConfig.MinCreditFactor.Value, EconomyConfig.MaxCreditFactor.Value);
        }

        /// <summary>Recomputes GDP, demand pressure, inflation, automatic interest, investment, prices and exchange rate for everyone.</summary>
        private static void UpdateGlobalEconomy(CurrencyWorld world, int turn)
        {
            int count = Math.Min(Sandbox.NumberOfMajorEmpires, world.Empires.Count);
            var alive = new bool[count];
            var logPressure = new double[count];
            var hasPressure = new bool[count];

            // 1. Economic strength of each living empire (effective production: the turn's credit already applied to it).
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
                    // Science mode: industry becomes science (the game returns 0 in ComputeProductionIncome) and is
                    // already in ResearchNet; adding it here would count it twice.
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

                // 2. Supply × demand: money per unit of production against the empire's own reference
                //    (the deviation from the world trend is removed just below).
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

            // The game's production grows faster than money for everyone early on (that is the game's design, not
            // monetary policy): only each empire's deviation from the world median counts.
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
                // Soft cap: near zero it is the usual linear value; far away, it approaches the limit gradually. The hard clamp
                // left the term stuck at ±limit in 58% of the long-game snapshots, jumping from one side to the other.
                double limit = EconomyConfig.PressureInflationLimit.Value;
                double raw = EconomyConfig.PressureInflation.Value * currency.Pressure;
                currency.CoverageInflation = limit > 0 ? limit * Math.Tanh(raw / limit) : 0;
                currency.Coverage = 1.0 / (1.0 + currency.Pressure);
            }

            // 3. Inflation, interest, investment and price index.
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
                // The debt part within current inflation moves at the same pace as inflation.
                currency.InflationRateFromDebt += EconomyConfig.InflationSmoothing.Value * (currency.InflationFromDebt - currency.InflationRateFromDebt);

                bool autoInterest = !currency.ForcedInterest && (currency.AutoInterest || !empire.IsControlledByHuman);
                if (autoInterest)
                {
                    currency.InterestRate += AutoInterestStep * (TaylorRate(currency.InflationRate, currency.Pressure, currency.InflationRateFromDebt) - currency.InterestRate);
                }
                currency.InterestRate = Clamp(currency.InterestRate, EconomyConfig.MinInterest.Value, EconomyConfig.MaxInterest.Value);

                // Investment: production moves slowly to the level the real rate allows (applies from the next turn on).
                double creditTarget = CreditTarget(currency.InterestRate, currency.InflationRate);
                currency.CreditFactor += EconomyConfig.InvestmentSpeed.Value * (creditTarget - currency.CreditFactor);

                currency.PriceIndex = Math.Max(0.01, currency.PriceIndex * (1 + currency.InflationRate));
                logPriceSum += Math.Log(currency.PriceIndex);
            }
            // New credit, stability target and interest apply from now on for the whole game.
            effects = world.Empires.Select(BuildEffects).ToArray();
            double priceGeoMean = Math.Exp(logPriceSum / aliveCount);

            // 4. Exchange rate: relative strength divided by the relative price level.
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

            // Normalize so the geometric mean is 1.0 (only changes the scale, not the rates between currencies).
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
                if (IsHuman(i))
                {
                    // Central Bank Resources tab: access to each strategic and luxury resource, per turn (humans only: light save).
                    currency.History[currency.History.Count - 1].Resources = ResourceAccess(Sandbox.MajorEmpires[i]);
                }
            }
            RecordTrade(world, turn);
        }

        /// <summary>
        /// Central Bank ledger: records a money movement in the current turn, only if one of the sides is a human player
        /// (events between AIs are not shown on screen and would only bloat the save). The caller already holds CurrencyManager.Lock.
        /// </summary>
        internal static void RecordMoney(CurrencyWorld world, int from, int to, double paid, double gain, int kind)
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null || world == null)
            {
                return;
            }
            if (!IsHuman(from) && !IsHuman(to))
            {
                return;
            }
            world.RecordMoney(sandbox.Turn, from, to, paid, gain, kind);
        }

        /// <summary>The empire's access to each strategic and luxury resource (type → amount), only the ones it has.</summary>
        private static Dictionary<int, int> ResourceAccess(MajorEmpire empire)
        {
            var access = new Dictionary<int, int>();
            try
            {
                Amplitude.Mercury.Data.Simulation.ResourceDefinition[] definitions = DepartmentOfResources.ResourceDefinitionByResourceType;
                DepartmentOfResources resources = empire?.DepartmentOfResources;
                if (definitions == null || resources == null)
                {
                    return access;
                }
                for (int t = 0; t < definitions.Length; t++)
                {
                    if (definitions[t] is Amplitude.Mercury.Data.Simulation.LuxuryResourceDefinition || definitions[t] is Amplitude.Mercury.Data.Simulation.StrategicResourceDefinition)
                    {
                        int count = (int)(float)resources.GetResourceAccess((Amplitude.Mercury.Data.Simulation.ResourceType)t);
                        if (count > 0)
                        {
                            access[t] = count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"Central Bank: turn resources: {ex.Message}");
            }
            return access;
        }

        private static bool IsHuman(int empireIndex)
        {
            return empireIndex >= 0 && empireIndex < Sandbox.NumberOfMajorEmpires && Sandbox.MajorEmpires[empireIndex] != null
                && Sandbox.MajorEmpires[empireIndex].IsControlledByHuman;
        }

        /// <summary>
        /// Records this turn's resource purchases by route (who buys from whom and how much they pay per turn), for the
        /// Central Bank's import and export chart. Runs on the sandbox thread, at end of turn.
        /// </summary>
        private static void RecordTrade(CurrencyWorld world, int turn)
        {
            try
            {
                TradePoint point = world.PointFor(turn);
                point.Flows.Clear();
                point.Money.RemoveAll(m => m.Kind == MoneyKind.Upkeep);
                int majors = Sandbox.NumberOfMajorEmpires;
                TradeController controller = Sandbox.TradeController;
                if (controller != null)
                {
                    foreach (ExternalTradeRelation relation in controller.ExternalTradeRelations)
                    {
                        if (relation == null || relation.TradeRoadStatus != Amplitude.Mercury.Interop.TradeRoadStatus.Active)
                        {
                            continue;
                        }
                        foreach (ExternalTradeExchange exchange in new[] { relation.LeftTradeExchange.Entity, relation.RightTradeExchange.Entity })
                        {
                            Empire buyer = exchange?.BuyerEmpire.Entity;
                            Empire seller = exchange?.SellerEmpire.Entity;
                            if (buyer == null || seller == null || buyer.Index >= majors || seller.Index >= majors)
                            {
                                continue;
                            }
                            int goods = (int)(float)exchange.NumberOfDifferentResourceTraded.Value;
                            double value = (float)exchange.TradeUpkeepAfterResourceDiversification.Value;
                            if ((goods > 0 || value > 0) && (IsHuman(buyer.Index) || IsHuman(seller.Index)))
                            {
                                point.Flows.Add(new TradeFlow { Buyer = buyer.Index, Seller = seller.Index, Value = Math.Round(value, 2), Goods = goods });
                                point.Money.Add(new MoneyFlow { From = buyer.Index, To = seller.Index, Paid = Math.Round(value, 2), Gain = 0, Kind = MoneyKind.Upkeep });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Turn trade not recorded: {ex.Message}");
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
                // Too much idle money relative to income becomes inflationary pressure. With zero or negative income the
                // empire is spending its reserve, not hoarding (it used to hit the income floor of 5 and get the maximum).
                double turnsOfIncome = stock / Math.Max(5.0, income);
                double excess = turnsOfIncome / EconomyConfig.HoardingTurns.Value - 1.0;
                if (excess > 0)
                {
                    hoarding = Math.Min(0.01, EconomyConfig.HoardingInflation.Value * excess);
                }
            }

            double stability = (0.6 - currency.Stability) * EconomyConfig.StabilityInflation.Value;
            // Interest acts directly on demand: stronger than its effect on supply (otherwise raising interest would raise inflation).
            double interest = InterestInflationPush(currency.InterestRate);

            currency.InflationFromDebt = debt;
            currency.InflationFromHoarding = hoarding;
            currency.InflationFromStability = stability;
            currency.InflationFromInterest = interest;
            currency.HasInflationBreakdown = true;
            // Supply × demand (CoverageInflation) was already computed for everyone beforehand.
            currency.InflationTarget = EconomyConfig.BaseInflation.Value + debt + hoarding + currency.CoverageInflation + stability + interest;
            return currency.InflationTarget;
        }

        /// <summary>
        /// Interest on the balance (or debt) in one turn. A positive balance with a positive real rate has a soft cap: the
        /// bank invests well up to ~N turns of income and the rest yields less and less. Inflation above interest erodes
        /// the whole balance; debt pays a spread, and high inflation erodes debt too. The income projection uses the same math.
        /// The cap's base is the larger of income and a share of GDP: an empire with a big treasury and a deficit (imports
        /// paid from savings) still earns on it; with income alone as the base, a negative income collapsed the cap to the
        /// 20-per-turn floor and a 2,600 balance yielded almost nothing (real save, 2026-10-11).
        /// </summary>
        /// <summary>Real rate per turn on the balance: interest − inflation (plus the spread on debt), within the cap.</summary>
        internal static double RealRate(double stock, double interestRate, double inflationRate)
        {
            double maxRate = EconomyConfig.MaxRealRatePerTurn.Value;
            double rate = stock >= 0 ? interestRate - inflationRate : interestRate + EconomyConfig.DebtSpread.Value - inflationRate;
            return Clamp(rate, -maxRate, maxRate);
        }

        /// <summary>Direct push of the policy rate on inflation (negative above the neutral rate).</summary>
        internal static double InterestInflationPush(double interestRate)
        {
            return -(interestRate - EconomyConfig.NeutralInterest.Value) * EconomyConfig.InterestInflationEffect.Value;
        }

        /// <summary>Balance that yields almost fully: N turns of the larger of income (min 20) and a share of GDP.</summary>
        internal static double SavingsCapacity(double smoothedIncome, double gdp)
        {
            double basis = Math.Max(Math.Max(20.0, smoothedIncome), EconomyConfig.SavingsCapacityGdpShare.Value * Math.Max(0, gdp));
            return EconomyConfig.SavingsCapacityTurns.Value * basis;
        }

        internal static double InterestFlow(double stock, double interestRate, double inflationRate, double smoothedIncome, double gdp = 0)
        {
            double realRate = RealRate(stock, interestRate, inflationRate);
            if (stock < 0 || realRate <= 0)
            {
                return stock * realRate;
            }
            double capacity = SavingsCapacity(smoothedIncome, gdp);
            double invested = capacity > 0 ? stock * capacity / (stock + capacity) : stock;
            return invested * realRate;
        }

        /// <summary>
        /// Interest and erosion on the balance, and the old effect of interest on income. Inflation and unemployment no
        /// longer touch stability directly: they lower the cities' stability target (NativeEffects), like the game's sources.
        /// </summary>
        private static void ApplyMonetaryEffects(CurrencyWorld world, MajorEmpire empire, DepartmentOfTheTreasury treasury)
        {
            EmpireCurrency currency = world.Get(empire.Index);
            if (currency == null || !empire.IsAlive)
            {
                return;
            }

            double stock = (float)empire.MoneyStock.Value;
            double interestFlow = InterestFlow(stock, currency.InterestRate, currency.InflationRate, currency.SmoothedIncome, currency.Gdp);

            // Interest on money income (off by default: the cost of interest moved to production).
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

            // Display only: the cities' stability target already deducts this (NativeEffects).
            currency.LastStabilityPenalty = InflationStabilityLoss(currency.InflationRate);
            currency.LastUnemploymentPenalty = UnemploymentStabilityLoss(currency.CreditFactor);
        }

        /// <summary>How much each city's stability target drops with this inflation (in bands).</summary>
        internal static double InflationStabilityLoss(double inflationRate)
        {
            double points = inflationRate * 100;
            return EconomyConfig.InflationStabilityLoss1.Value * Math.Max(0, points - EconomyConfig.InflationStabilityThreshold1.Value * 100)
                + EconomyConfig.InflationStabilityLoss2.Value * Math.Max(0, points - EconomyConfig.InflationStabilityThreshold2.Value * 100);
        }

        /// <summary>How much each city's stability target drops with production cut by expensive credit (unemployment).</summary>
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
    /// Interest → production: multiplies the production that goes into constructions by the empire's credit/investment.
    /// The AI, the city list and the construction queue read this same value; the city screen and the breakdown get the
    /// credit in NativeEffects.
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
                // Never get in the way of construction: without credit, the game's value stays.
            }
        }
    }
}
