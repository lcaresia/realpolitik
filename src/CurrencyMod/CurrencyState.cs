using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CurrencyMod
{
    /// <summary>A point in an empire's history, recorded at every end of turn.</summary>
    public class HistoryPoint
    {
        public int Turn;
        public double ExchangeValue;
        public double InflationRate;
        public double InterestRate;
        /// <summary>Strategic and luxury resource accesses (type → amount), human player only; null in old saves.</summary>
        public Dictionary<int, int> Resources;
    }

    /// <summary>A resource purchase over a trade route in one turn: the buyer pays the upkeep to the seller (in the buyer's currency).</summary>
    public class TradeFlow
    {
        public int Buyer;
        public int Seller;
        public double Value;
        public int Goods;
    }

    /// <summary>
    /// Money that went from one empire to another in one turn. Paid leaves From's treasury (in its currency); Gain enters
    /// To's (in its currency, already converted). Kind: see MoneyKind.
    /// </summary>
    public class MoneyFlow
    {
        public int From;
        public int To;
        public double Paid;
        public double Gain;
        public int Kind;
        /// <summary>Resource type of a single-resource purchase (MoneyKind.Resources); -1 when unknown (old saves, bulk buys).</summary>
        public int Resource = -1;
    }

    public static class MoneyKind
    {
        /// <summary>Resource purchase: the buyer pays up front and the seller gets a share, only once.</summary>
        public const int Resources = 0;
        /// <summary>Trade route upkeep, per turn, paid by the buyer (does not go to the seller).</summary>
        public const int Upkeep = 1;
        /// <summary>Post tolls: per turn, from the route owner to the post owner.</summary>
        public const int Toll = 2;
        /// <summary>Gifts, demands, surrender and other transfers between empires.</summary>
        public const int Other = 3;
        /// <summary>Route transit: a share of the buyer's route upkeep paid to the owners of the territories the route crosses.</summary>
        public const int Transit = 4;
    }

    /// <summary>Active trade flows of one turn (only those with purchased resources; the history is sparse).</summary>
    public class TradePoint
    {
        public int Turn;
        public List<TradeFlow> Flows = new List<TradeFlow>();
        /// <summary>The money that changed hands between empires this turn (sparse).</summary>
        public List<MoneyFlow> Money = new List<MoneyFlow>();
    }

    /// <summary>Monetary state of an empire. Rates are per turn (0.01 = 1%).</summary>
    public class EmpireCurrency
    {
        public const int MaxHistory = 60;

        public int EmpireIndex;
        public string Name;
        public string Plural;
        public string Symbol;
        public bool NamedByPlayer;

        /// <summary>External value of the currency (1.0 = world average).</summary>
        public double ExchangeValue = 1.0;
        public double InflationRate;
        public double InterestRate = 0.01;
        public bool AutoInterest = true;
        /// <summary>Cumulative price index (starts at 1.0).</summary>
        public double PriceIndex = 1.0;

        // Indicators of the last processed turn (display only).
        public double Gdp;
        public double Stability;
        public double Strength;
        public double LastInterestFlow;
        public double LastIncomeEffect;
        public double LastConversionGain;

        // Production → inflation → interest cycle (proposta-ciclo-economico.md). Display and the production patch only.
        /// <summary>Multiplier on city production from the interest rate (expensive or cheap credit).</summary>
        public double CreditFactor = 1.0;
        /// <summary>Effective production per new currency unit, relative to the world median (1 = average).</summary>
        public double Coverage = 1.0;
        /// <summary>Share of the target inflation coming from coverage (negative = production holding prices down).</summary>
        public double CoverageInflation;
        /// <summary>Stability taken from each city last turn by high inflation.</summary>
        public double LastStabilityPenalty;
        /// <summary>Interest rate fixed by a test command even on an AI nation (balancing).</summary>
        public bool ForcedInterest;
        /// <summary>Smoothed effective production and income (exponential moving average): coverage does not jump on one bad turn.</summary>
        public double SmoothedProduction;
        public double SmoothedIncome;
        /// <summary>Adaptive reference of money per production (follows the current ratio over ~15 turns).</summary>
        public double DemandBaseline;
        /// <summary>Demand pressure: money per production against the reference − 1 (positive = too much money, short supply).</summary>
        public double Pressure;
        /// <summary>Stability taken from each city last turn by unemployment (production cut by expensive credit).</summary>
        public double LastUnemploymentPenalty;
        /// <summary>Inflation heading last turn (inflation moves toward it gradually) and its components, for the Cycle tab.
        /// The supply × demand component is CoverageInflation; the base comes from the configuration.</summary>
        public double InflationTarget;
        public double InflationFromDebt;
        /// <summary>How much of the current inflation came from debt: moves toward InflationFromDebt at the same pace as
        /// inflation. It is the part the automatic interest rule weighs with a soft cap (EconomySimulation.DebtInRule).</summary>
        public double InflationRateFromDebt;
        public double InflationFromHoarding;
        public double InflationFromStability;
        public double InflationFromInterest;
        /// <summary>False in saves older than the breakdown, until the first end of turn.</summary>
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

        /// <summary>Exchange value on the previous turn, to show the trend.</summary>
        public double PreviousExchangeValue => History.Count >= 2 ? History[History.Count - 2].ExchangeValue : ExchangeValue;
    }

    /// <summary>State of a whole game, persisted inside the save.</summary>
    public class CurrencyWorld
    {
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;
        public string GameGuid;
        /// <summary>Last turn in which the global economy was recalculated (avoids processing twice).</summary>
        public int LastProcessedTurn = -1;
        public List<EmpireCurrency> Empires = new List<EmpireCurrency>();

        // Trade blockade (see Trade/TradePolicy.cs).
        public List<TradeRule> TradeRules = new List<TradeRule>();
        public List<TradeIncident> TradeIncidents = new List<TradeIncident>();
        public List<TollRecord> LastTolls = new List<TollRecord>();
        /// <summary>General toll price of each owner for each empire (each post's price lives in the TradeRule).</summary>
        public List<TollPrice> TollPrices = new List<TollPrice>();
        /// <summary>Pay or detour around each owner's tolls, chosen by the payer (language AI).</summary>
        public List<TollChoice> TollChoices = new List<TollChoice>();
        public int LastTradeTurn = -1;
        /// <summary>History of trade between empires (last MaxTradeHistory turns), recorded at the end of each turn.</summary>
        public List<TradePoint> TradeHistory = new List<TradePoint>();
        public const int MaxTradeHistory = 60;

        /// <summary>The turn's point (created if missing). The caller already holds CurrencyManager.Lock.</summary>
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

        public void RecordMoney(int turn, int from, int to, double paid, double gain, int kind, int resource = -1)
        {
            if (double.IsNaN(paid) || double.IsNaN(gain) || double.IsInfinity(paid) || double.IsInfinity(gain) || (paid <= 0 && gain <= 0))
            {
                return;
            }
            // Two decimals are enough (the ledger goes into the save's JSON).
            TradePoint point = PointFor(turn);
            point.Money.Add(new MoneyFlow { From = from, To = to, Paid = Math.Round(paid, 2), Gain = Math.Round(gain, 2), Kind = kind, Resource = resource });
        }

        public EmpireCurrency Get(int empireIndex)
        {
            return empireIndex >= 0 && empireIndex < Empires.Count ? Empires[empireIndex] : null;
        }

        /// <summary>How many units of currency 'to' are worth 1 unit of currency 'from'.</summary>
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
                    // Version 1 saves had neither plural nor history.
                    empire.History = empire.History ?? new List<HistoryPoint>();
                    // Symbols the game's font lacks become letters.
                    empire.Symbol = (empire.Symbol ?? string.Empty).Replace("₹", "Rs").Replace("₽", "Rb").Replace("ℳ", "Mk").Replace("ƒ", "Fl");
                    if (string.IsNullOrEmpty(empire.Plural))
                    {
                        empire.Plural = CurrencyManager.GuessPlural(empire.Name);
                    }
                }
                // Saves older than the trade blockade.
                world.TradeRules = world.TradeRules ?? new List<TradeRule>();
                world.TradeIncidents = world.TradeIncidents ?? new List<TradeIncident>();
                world.LastTolls = world.LastTolls ?? new List<TollRecord>();
                world.TollPrices = world.TollPrices ?? new List<TollPrice>();
                world.TollChoices = world.TollChoices ?? new List<TollChoice>();
                world.TradeHistory = world.TradeHistory ?? new List<TradePoint>();
                // Saves written before the money ledger only had Flows: their upkeep becomes money movement.
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
    /// Single access point to the state. It is read by the UI (main thread) and written
    /// by the simulation (sandbox thread), so every access goes through the lock.
    /// </summary>
    public static class CurrencyManager
    {
        public static readonly object Lock = new object();

        private static CurrencyWorld world;

        // Name, plural and symbol of historical currencies, used by the AI and as the player's default.
        private static readonly string[][] DefaultCurrencies =
        {
            new[] { "Denário", "Denários", "Dn" }, new[] { "Dracma", "Dracmas", "Dr" }, new[] { "Siclo", "Siclos", "Sc" },
            new[] { "Talento", "Talentos", "Tl" }, new[] { "Florim", "Florins", "Fl" }, new[] { "Ducado", "Ducados", "Dc" },
            new[] { "Libra", "Libras", "£" }, new[] { "Táler", "Táleres", "Th" }, new[] { "Peso", "Pesos", "P$" },
            new[] { "Rublo", "Rublos", "Rb" }, new[] { "Iene", "Ienes", "¥" }, new[] { "Rupia", "Rupias", "Rs" },
            new[] { "Dinar", "Dinares", "DA" }, new[] { "Xelim", "Xelins", "Sh" }, new[] { "Coroa", "Coroas", "Kr" },
            new[] { "Marco", "Marcos", "Mk" }, new[] { "Sestércio", "Sestércios", "HS" }, new[] { "Áureo", "Áureos", "Au" },
        };

        /// <summary>Ensures there is state for the current game; recreates it if it is another game.</summary>
        public static CurrencyWorld Ensure(string gameGuid, int numberOfMajorEmpires)
        {
            lock (Lock)
            {
                if (world == null || world.GameGuid != gameGuid)
                {
                    world = CreateDefault(gameGuid, numberOfMajorEmpires);
                    TradePolicy.Invalidate();
                    EconomySimulation.ResetEffectsLater();
                    Plugin.Log.LogInfo($"New monetary state created for game {gameGuid} ({numberOfMajorEmpires} empires).");
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
                // Real -> Reais, Papel -> Papéis (without accent), Sol -> Sóis (without accent)
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
            // Deterministic per game: the same save always generates the same names.
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
