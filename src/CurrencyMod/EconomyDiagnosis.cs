using System;

namespace CurrencyMod
{
    /// <summary>
    /// Diagnóstico da economia de um império: o problema mais urgente, com uma frase do que fazer. Usado pela aba Ciclo
    /// do Banco Central e pelo selo de alerta no botão do Banco Central (só os casos graves acendem o selo).
    /// </summary>
    internal sealed class EconomyDiagnosis
    {
        internal enum Level { Good, Warning, Bad }

        internal Level Severity;
        internal string Headline;
        internal string Advice;

        /// <summary>Perda total na meta de estabilidade a partir da qual o caso conta como grave.</summary>
        private const double SevereStabilityLoss = 10.0;

        internal static EconomyDiagnosis For(EmpireCurrency mine, double stock)
        {
            double inflation = mine.InflationRate;
            double interest = mine.InterestRate;
            double meta = EconomyConfig.TargetInflation.Value;
            double neutralReal = EconomyConfig.NeutralInterest.Value - meta;
            double real = interest - inflation;
            double pressure = mine.Pressure;
            double credit = mine.CreditFactor;
            double creditTarget = EconomySimulation.CreditTarget(interest, inflation);
            double wanted = EconomySimulation.TaylorRate(inflation, pressure, mine.InflationRateFromDebt);
            bool manual = mine.ForcedInterest || !mine.AutoInterest;
            double inflationLoss = EconomySimulation.InflationStabilityLoss(inflation);
            double unemploymentLoss = EconomySimulation.UnemploymentStabilityLoss(credit);
            double threshold1 = EconomyConfig.InflationStabilityThreshold1.Value;
            double threshold2 = EconomyConfig.InflationStabilityThreshold2.Value;

            // O que os juros fariam (ou a regra faria): fecha os conselhos.
            string InterestAdvice(bool raise)
            {
                if (mine.ForcedInterest)
                {
                    return L.F("Juros fixados por comando de teste em {0}.", Pct(interest));
                }
                if (!manual)
                {
                    return Math.Abs(wanted - interest) < 0.0005
                        ? L.T("Os juros automáticos já estão no ponto da regra.")
                        : L.F("Os juros automáticos estão indo de {0} para {1}.", Pct(interest), Pct(wanted));
                }
                if (raise && wanted > interest + 0.0005)
                {
                    return L.F("Juros mais altos esfriam a demanda: a regra automática pediria {0} (hoje {1}, no manual).", Pct(wanted), Pct(interest));
                }
                if (!raise && wanted < interest - 0.0005)
                {
                    return L.F("Juros mais baixos barateiam o crédito: a regra automática pediria {0} (hoje {1}, no manual).", Pct(wanted), Pct(interest));
                }
                return L.F("Juros no manual em {0}; a regra automática pediria {1}.", Pct(interest), Pct(wanted));
            }

            var result = new EconomyDiagnosis();
            if (stock < 0)
            {
                result.Severity = Level.Bad;
                result.Headline = L.T("Em dívida");
                result.Advice = L.F("A dívida custa {0} por turno e empurra a inflação para cima ({1}). Equilibre a renda antes de mexer nos juros.", Format.SignedMoney(mine.LastInterestFlow), SignedPct(mine.InflationFromDebt));
            }
            else if (inflation >= threshold2)
            {
                result.Severity = Level.Bad;
                result.Headline = L.T("Inflação alta");
                result.Advice = L.F("Inflação de {0} por turno: a meta de estabilidade de cada cidade cai {1}. {2}", Pct(inflation), Amount(inflationLoss), InterestAdvice(raise: true));
            }
            else if (inflation > threshold1)
            {
                result.Severity = Level.Warning;
                result.Headline = L.T("Inflação acima do tolerável");
                result.Advice = L.F("Acima de {0} por turno a população sente os preços: a meta de estabilidade de cada cidade cai {1}. {2}", Pct(threshold1), Amount(inflationLoss), InterestAdvice(raise: true));
            }
            else if (credit < 0.95 || creditTarget < 0.95)
            {
                result.Severity = Level.Warning;
                result.Headline = L.T("Crédito caro");
                result.Advice = unemploymentLoss > 0.05
                    ? L.F("Juro real de {0} (neutro {1}): a produção das cidades está ×{2} e o desemprego baixa a meta de estabilidade em {3}. {4}", Pct(real), Pct(neutralReal), Format.Rate(credit), Amount(unemploymentLoss), InterestAdvice(raise: false))
                    : L.F("Juro real de {0} (neutro {1}): a produção das cidades está ×{2}. {3}", Pct(real), Pct(neutralReal), Format.Rate(credit), InterestAdvice(raise: false));
            }
            else if (pressure > 0.10)
            {
                result.Severity = Level.Warning;
                result.Headline = L.T("Dinheiro à frente da produção");
                result.Advice = L.F("A renda cresce {0} acima do ritmo da produção: sobra demanda e a inflação tende a subir. Mais indústria ou juros mais altos seguram os preços.", Pct0(pressure));
            }
            else if (inflation < 0)
            {
                result.Severity = Level.Warning;
                result.Headline = L.T("Deflação");
                result.Advice = L.F("Preços caindo {0} por turno: o saldo rende mais, mas o juro real de {1} encarece o crédito (produção ×{2}). Juros mais baixos reaquecem a economia.", Pct(-inflation), Pct(real), Format.Rate(credit));
            }
            else if (pressure < -0.10)
            {
                result.Severity = Level.Good;
                result.Headline = L.T("Produção à frente do dinheiro");
                result.Advice = L.F("A produção cresce {0} acima do ritmo do dinheiro: os preços tendem a cair. Há espaço para juros mais baixos e mais crédito.", Pct0(-pressure));
            }
            else
            {
                result.Severity = Level.Good;
                result.Headline = L.T("Economia equilibrada");
                result.Advice = L.F("Inflação perto da meta ({0}) e produção ×{1}. Nada a corrigir agora.", Pct(meta), Format.Rate(credit));
            }
            // Desemprego forte também é grave, mesmo sem inflação alta.
            if (result.Severity != Level.Bad && inflationLoss + unemploymentLoss >= SevereStabilityLoss)
            {
                result.Severity = Level.Bad;
            }
            return result;
        }

        private static string Pct(double rate) => Format.Percent(rate);

        private static string SignedPct(double rate) => Math.Abs(rate) < 0.00005 ? "0%" : (rate < 0 ? "−" : "+") + Format.Percent(Math.Abs(rate));

        private static string Pct0(double rate) => (rate * 100).ToString("0", L.Culture) + "%";

        private static string Amount(double value) => value.ToString("0.#", L.Culture);
    }
}
