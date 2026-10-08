using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CurrencyMod
{
    /// <summary>Um ponto do histórico de um império, gravado a cada fim de turno.</summary>
    public class HistoryPoint
    {
        public int Turn;
        public double ExchangeValue;
        public double InflationRate;
        public double InterestRate;
    }

    /// <summary>Uma compra de recursos por rota comercial num turno: o comprador paga a manutenção ao vendedor (na moeda do comprador).</summary>
    public class TradeFlow
    {
        public int Buyer;
        public int Seller;
        public double Value;
        public int Goods;
    }

    /// <summary>
    /// Dinheiro que passou de um império para outro num turno. Paid sai do caixa de From (na moeda dele); Gain entra no de To
    /// (na moeda dele, já convertido). Kind: ver MoneyKind.
    /// </summary>
    public class MoneyFlow
    {
        public int From;
        public int To;
        public double Paid;
        public double Gain;
        public int Kind;
    }

    public static class MoneyKind
    {
        /// <summary>Compra de recursos: o comprador paga na hora e o vendedor ganha uma parte, uma vez só.</summary>
        public const int Resources = 0;
        /// <summary>Manutenção das rotas comerciais, por turno, paga pelo comprador (não vai para o vendedor).</summary>
        public const int Upkeep = 1;
        /// <summary>Pedágio dos postos: por turno, do dono da rota para o dono do posto.</summary>
        public const int Toll = 2;
        /// <summary>Presentes, exigências, rendição e outras transferências entre impérios.</summary>
        public const int Other = 3;
    }

    /// <summary>Fluxos comerciais ativos de um turno (só quem tem recurso comprado; o histórico é esparso).</summary>
    public class TradePoint
    {
        public int Turn;
        public List<TradeFlow> Flows = new List<TradeFlow>();
        /// <summary>O dinheiro que mudou de mãos entre impérios neste turno (esparso).</summary>
        public List<MoneyFlow> Money = new List<MoneyFlow>();
    }

    /// <summary>Estado monetário de um império. Taxas são por turno (0.01 = 1%).</summary>
    public class EmpireCurrency
    {
        public const int MaxHistory = 60;

        public int EmpireIndex;
        public string Name;
        public string Plural;
        public string Symbol;
        public bool NamedByPlayer;

        /// <summary>Valor externo da moeda (1.0 = média mundial).</summary>
        public double ExchangeValue = 1.0;
        public double InflationRate;
        public double InterestRate = 0.01;
        public bool AutoInterest = true;
        /// <summary>Índice de preços acumulado (começa em 1.0).</summary>
        public double PriceIndex = 1.0;

        // Indicadores do último turno processado (só para exibição).
        public double Gdp;
        public double Stability;
        public double Strength;
        public double LastInterestFlow;
        public double LastIncomeEffect;
        public double LastConversionGain;

        // Ciclo produção → inflação → juros (proposta-ciclo-economico.md). Só exibição e o patch de produção.
        /// <summary>Multiplicador da produção das cidades pelos juros (crédito caro ou barato).</summary>
        public double CreditFactor = 1.0;
        /// <summary>Produção efetiva por moeda nova, relativa à mediana do mundo (1 = na média).</summary>
        public double Coverage = 1.0;
        /// <summary>Parcela da inflação-alvo vinda da cobertura (negativa = produção segurando os preços).</summary>
        public double CoverageInflation;
        /// <summary>Estabilidade tirada de cada cidade no último turno pela inflação alta.</summary>
        public double LastStabilityPenalty;
        /// <summary>Juros fixados por comando de teste mesmo numa nação da IA (balanceamento).</summary>
        public bool ForcedInterest;
        /// <summary>Produção efetiva e renda suavizadas (média móvel exponencial): a cobertura não pula com um turno ruim.</summary>
        public double SmoothedProduction;
        public double SmoothedIncome;
        /// <summary>Referência adaptativa de dinheiro por produção (acompanha a razão atual em ~15 turnos).</summary>
        public double DemandBaseline;
        /// <summary>Pressão de demanda: dinheiro por produção contra a referência − 1 (positivo = dinheiro demais, falta oferta).</summary>
        public double Pressure;
        /// <summary>Estabilidade tirada de cada cidade no último turno pelo desemprego (produção cortada por crédito caro).</summary>
        public double LastUnemploymentPenalty;
        /// <summary>Rumo da inflação no último turno (a inflação anda até ele aos poucos) e as parcelas dele, para a aba Ciclo.
        /// A parcela da oferta × demanda é CoverageInflation; a base vem da configuração.</summary>
        public double InflationTarget;
        public double InflationFromDebt;
        /// <summary>Quanto da inflação atual veio da dívida: anda até InflationFromDebt no mesmo ritmo da inflação. É a parte
        /// que a regra automática dos juros pesa com teto suave (EconomySimulation.DebtInRule).</summary>
        public double InflationRateFromDebt;
        public double InflationFromHoarding;
        public double InflationFromStability;
        public double InflationFromInterest;
        /// <summary>Falso em saves anteriores à decomposição, até o primeiro fim de turno.</summary>
        public bool HasInflationBreakdown;

        public List<HistoryPoint> History = new List<HistoryPoint>();

        [JsonIgnore]
        public string PluralOrName => string.IsNullOrEmpty(Plural) ? Name : Plural;

        public void PushHistory(int turn)
        {
            if (History.Count > 0 && History[History.Count - 1].Turn == turn)
            {
                History.RemoveAt(History.Count - 1);
            }
            History.Add(new HistoryPoint { Turn = turn, ExchangeValue = ExchangeValue, InflationRate = InflationRate, InterestRate = InterestRate });
            if (History.Count > MaxHistory)
            {
                History.RemoveRange(0, History.Count - MaxHistory);
            }
        }

        /// <summary>Valor do câmbio no turno anterior, para mostrar tendência.</summary>
        public double PreviousExchangeValue => History.Count >= 2 ? History[History.Count - 2].ExchangeValue : ExchangeValue;
    }

    /// <summary>Estado de uma partida inteira, persistido dentro do save.</summary>
    public class CurrencyWorld
    {
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;
        public string GameGuid;
        /// <summary>Último turno em que a economia global foi recalculada (evita processar duas vezes).</summary>
        public int LastProcessedTurn = -1;
        public List<EmpireCurrency> Empires = new List<EmpireCurrency>();

        // Bloqueio comercial (ver Trade/TradePolicy.cs).
        public List<TradeRule> TradeRules = new List<TradeRule>();
        public List<TradeIncident> TradeIncidents = new List<TradeIncident>();
        public List<TollRecord> LastTolls = new List<TollRecord>();
        /// <summary>Preço geral de pedágio de cada dono para cada império (o preço de cada posto fica na TradeRule).</summary>
        public List<TollPrice> TollPrices = new List<TollPrice>();
        public int LastTradeTurn = -1;
        /// <summary>Histórico do comércio entre impérios (últimos MaxTradeHistory turnos), gravado no fim de cada turno.</summary>
        public List<TradePoint> TradeHistory = new List<TradePoint>();
        public const int MaxTradeHistory = 60;

        /// <summary>O ponto do turno (cria se não houver). Quem chama já tem o CurrencyManager.Lock.</summary>
        public TradePoint PointFor(int turn)
        {
            TradeHistory = TradeHistory ?? new List<TradePoint>();
            foreach (TradePoint known in TradeHistory)
            {
                if (known.Turn == turn)
                {
                    return known;
                }
            }
            var point = new TradePoint { Turn = turn };
            TradeHistory.Add(point);
            TradeHistory.Sort((a, b) => a.Turn.CompareTo(b.Turn));
            if (TradeHistory.Count > MaxTradeHistory)
            {
                TradeHistory.RemoveRange(0, TradeHistory.Count - MaxTradeHistory);
            }
            return point;
        }

        public void RecordMoney(int turn, int from, int to, double paid, double gain, int kind)
        {
            if (double.IsNaN(paid) || double.IsNaN(gain) || double.IsInfinity(paid) || double.IsInfinity(gain) || (paid <= 0 && gain <= 0))
            {
                return;
            }
            // Duas casas bastam (o livro entra no JSON do save).
            TradePoint point = PointFor(turn);
            point.Money.Add(new MoneyFlow { From = from, To = to, Paid = Math.Round(paid, 2), Gain = Math.Round(gain, 2), Kind = kind });
        }

        public EmpireCurrency Get(int empireIndex)
        {
            return empireIndex >= 0 && empireIndex < Empires.Count ? Empires[empireIndex] : null;
        }

        /// <summary>Quantas unidades da moeda 'to' valem 1 unidade da moeda 'from'.</summary>
        public double Rate(int fromEmpire, int toEmpire)
        {
            EmpireCurrency from = Get(fromEmpire);
            EmpireCurrency to = Get(toEmpire);
            if (from == null || to == null || to.ExchangeValue <= 0)
            {
                return 1.0;
            }
            return from.ExchangeValue / to.ExchangeValue;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        public static CurrencyWorld FromJson(string json)
        {
            CurrencyWorld world = JsonConvert.DeserializeObject<CurrencyWorld>(json);
            if (world != null)
            {
                foreach (EmpireCurrency empire in world.Empires)
                {
                    // Saves da versão 1 não tinham plural nem histórico.
                    empire.History = empire.History ?? new List<HistoryPoint>();
                    // Símbolos que a fonte do jogo não tem viram letras.
                    empire.Symbol = (empire.Symbol ?? string.Empty).Replace("₹", "Rs").Replace("₽", "Rb").Replace("ℳ", "Mk").Replace("ƒ", "Fl");
                    if (string.IsNullOrEmpty(empire.Plural))
                    {
                        empire.Plural = CurrencyManager.GuessPlural(empire.Name);
                    }
                }
                // Saves anteriores ao bloqueio comercial.
                world.TradeRules = world.TradeRules ?? new List<TradeRule>();
                world.TradeIncidents = world.TradeIncidents ?? new List<TradeIncident>();
                world.LastTolls = world.LastTolls ?? new List<TollRecord>();
                world.TollPrices = world.TollPrices ?? new List<TollPrice>();
                world.TradeHistory = world.TradeHistory ?? new List<TradePoint>();
                // Saves gravados antes do livro de dinheiro tinham só os Flows: a manutenção deles vira movimento de dinheiro.
                foreach (TradePoint point in world.TradeHistory)
                {
                    point.Flows = point.Flows ?? new List<TradeFlow>();
                    point.Money = point.Money ?? new List<MoneyFlow>();
                    if (!point.Money.Any(m => m.Kind == MoneyKind.Upkeep))
                    {
                        foreach (TradeFlow flow in point.Flows)
                        {
                            if (flow.Value > 0)
                            {
                                point.Money.Add(new MoneyFlow { From = flow.Buyer, To = flow.Seller, Paid = flow.Value, Gain = 0, Kind = MoneyKind.Upkeep });
                            }
                        }
                    }
                }
                world.Version = CurrentVersion;
            }
            return world;
        }
    }

    /// <summary>
    /// Ponto único de acesso ao estado. É lido pela UI (thread principal) e escrito
    /// pela simulação (thread do sandbox), por isso todo acesso passa pelo lock.
    /// </summary>
    public static class CurrencyManager
    {
        public static readonly object Lock = new object();

        private static CurrencyWorld world;

        // Nome, plural e símbolo de moedas históricas, usadas pela IA e como padrão do jogador.
        private static readonly string[][] DefaultCurrencies =
        {
            new[] { "Denário", "Denários", "Dn" }, new[] { "Dracma", "Dracmas", "Dr" }, new[] { "Siclo", "Siclos", "Sc" },
            new[] { "Talento", "Talentos", "Tl" }, new[] { "Florim", "Florins", "Fl" }, new[] { "Ducado", "Ducados", "Dc" },
            new[] { "Libra", "Libras", "£" }, new[] { "Táler", "Táleres", "Th" }, new[] { "Peso", "Pesos", "P$" },
            new[] { "Rublo", "Rublos", "Rb" }, new[] { "Iene", "Ienes", "¥" }, new[] { "Rupia", "Rupias", "Rs" },
            new[] { "Dinar", "Dinares", "DA" }, new[] { "Xelim", "Xelins", "Sh" }, new[] { "Coroa", "Coroas", "Kr" },
            new[] { "Marco", "Marcos", "Mk" }, new[] { "Sestércio", "Sestércios", "HS" }, new[] { "Áureo", "Áureos", "Au" },
        };

        /// <summary>Garante que existe estado para a partida atual; recria se for outra partida.</summary>
        public static CurrencyWorld Ensure(string gameGuid, int numberOfMajorEmpires)
        {
            lock (Lock)
            {
                if (world == null || world.GameGuid != gameGuid)
                {
                    world = CreateDefault(gameGuid, numberOfMajorEmpires);
                    TradePolicy.Invalidate();
                    EconomySimulation.ResetEffectsLater();
                    Plugin.Log.LogInfo($"Novo estado monetário criado para a partida {gameGuid} ({numberOfMajorEmpires} impérios).");
                }
                else
                {
                    while (world.Empires.Count < numberOfMajorEmpires)
                    {
                        world.Empires.Add(CreateDefaultEmpire(gameGuid, world.Empires.Count));
                    }
                }
                return world;
            }
        }

        public static CurrencyWorld Current
        {
            get { lock (Lock) { return world; } }
        }

        public static void Replace(CurrencyWorld loaded)
        {
            lock (Lock) { world = loaded; }
            TradePolicy.Invalidate();
            EconomySimulation.ResetEffectsLater();
        }

        public static string GuessPlural(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }
            string lower = name.ToLowerInvariant();
            if (lower.EndsWith("al") || lower.EndsWith("el") || lower.EndsWith("ol") || lower.EndsWith("ul"))
            {
                // Real -> Reais, Papel -> Papéis (sem acento), Sol -> Sóis (sem acento)
                return name.Substring(0, name.Length - 1) + "is";
            }
            if (lower.EndsWith("m"))
            {
                return name.Substring(0, name.Length - 1) + "ns";
            }
            if (lower.EndsWith("r") || lower.EndsWith("z"))
            {
                return name + "es";
            }
            if (lower.EndsWith("s") || lower.EndsWith("x"))
            {
                return name;
            }
            return name + "s";
        }

        private static CurrencyWorld CreateDefault(string gameGuid, int numberOfMajorEmpires)
        {
            var created = new CurrencyWorld { GameGuid = gameGuid };
            for (int i = 0; i < numberOfMajorEmpires; i++)
            {
                created.Empires.Add(CreateDefaultEmpire(gameGuid, i));
            }
            return created;
        }

        private static EmpireCurrency CreateDefaultEmpire(string gameGuid, int empireIndex)
        {
            // Determinístico por partida: o mesmo save sempre gera os mesmos nomes.
            int seed = StableHash(gameGuid);
            int slot = (int)(((uint)seed + (uint)(empireIndex * 7)) % (uint)DefaultCurrencies.Length);
            return new EmpireCurrency
            {
                EmpireIndex = empireIndex,
                Name = DefaultCurrencies[slot][0],
                Plural = DefaultCurrencies[slot][1],
                Symbol = DefaultCurrencies[slot][2],
                InterestRate = EconomyConfig.NeutralInterest.Value,
            };
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in text ?? string.Empty)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }
    }
}
