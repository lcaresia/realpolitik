using System;
using System.Collections.Generic;
using Amplitude.Mercury.Sandbox;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Multiplayer
{
    /// <summary>
    /// The player's decisions that change the economy or trade (Central Bank, trade posts, diplomacy trade panel).
    /// Single-player: applied right away, as always. Multiplayer: posted as a mod order, so every peer applies the same
    /// change at the same point of the game; the screen shows it once the order comes back (a fraction of a second).
    /// Both paths run the same Apply code.
    /// </summary>
    internal static class MpInputs
    {
        /// <summary>One change to the trade policy of an owner.</summary>
        internal sealed class TradeOp
        {
            /// <summary>"rule" (mode at a post), "post" (price at a post), "copy" (price copied to a post), "general" or "choice" (pay or detour at Target's tolls).</summary>
            public string Kind;
            public int Territory = -1;
            public int Target;
            public TradeMode Mode;
            public double Price;
            public TollChoiceKind Choice;
        }

        internal static void Register()
        {
            MpChannel.Register("bank.interest", data => ApplyInterest((int)data["e"], (bool?)data["auto"], (double?)data["rate"]));
            MpChannel.Register("bank.name", data => ApplyName((int)data["e"], (string)data["name"], (string)data["plural"], (string)data["symbol"]));
            MpChannel.Register("trade.edit", data => ApplyTrade((int)data["e"], data["ops"].ToObject<List<TradeOp>>()));
        }

        private static bool Routed => MpSession.IsOnline;

        // ---------------- Central Bank ----------------

        /// <summary>Automatic or manual interest and, in manual, the rate (null keeps the current one).</summary>
        internal static void SetInterest(int empire, bool? auto, double? rate)
        {
            if (Routed)
            {
                MpChannel.Post("bank.interest", new JObject { ["e"] = empire, ["auto"] = auto, ["rate"] = rate });
                return;
            }
            ApplyInterest(empire, auto, rate);
        }

        internal static void SetCurrencyName(int empire, string name, string plural, string symbol)
        {
            if (Routed)
            {
                MpChannel.Post("bank.name", new JObject { ["e"] = empire, ["name"] = name, ["plural"] = plural, ["symbol"] = symbol });
                return;
            }
            ApplyName(empire, name, plural, symbol);
        }

        private static void ApplyInterest(int empire, bool? auto, double? rate)
        {
            EmpireCurrency currency = Mine(empire);
            if (currency == null)
            {
                return;
            }
            lock (CurrencyManager.Lock)
            {
                if (auto.HasValue)
                {
                    currency.AutoInterest = auto.Value;
                }
                if (rate.HasValue)
                {
                    currency.InterestRate = rate.Value;
                }
            }
            // The interest preview (top bar, tooltips) uses the table's rate.
            EconomySimulation.ResetEffects();
        }

        private static void ApplyName(int empire, string name, string plural, string symbol)
        {
            EmpireCurrency currency = Mine(empire);
            if (currency == null || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(symbol))
            {
                return;
            }
            lock (CurrencyManager.Lock)
            {
                currency.Name = name;
                currency.Plural = string.IsNullOrEmpty(plural) ? CurrencyManager.GuessPlural(name) : plural;
                currency.Symbol = symbol;
                currency.NamedByPlayer = true;
            }
            MpSession.RequestTextRefresh();
            Plugin.Log.LogInfo($"E{empire} named the currency: {currency.Name} / {currency.Plural} ({currency.Symbol}).");
        }

        // ---------------- Trade policy ----------------

        /// <summary>Applies the changes of <paramref name="owner"/> (the local empire) to its trade posts.</summary>
        internal static void EditTrade(int owner, List<TradeOp> ops)
        {
            if (ops == null || ops.Count == 0)
            {
                return;
            }
            if (Routed)
            {
                MpChannel.Post("trade.edit", new JObject { ["e"] = owner, ["ops"] = JArray.FromObject(ops) });
                return;
            }
            ApplyTrade(owner, ops);
        }

        internal static TradeOp Rule(int territory, int target, TradeMode mode) => new TradeOp { Kind = "rule", Territory = territory, Target = target, Mode = mode };

        internal static TradeOp PostPrice(int territory, int target, double price) => new TradeOp { Kind = "post", Territory = territory, Target = target, Price = price };

        internal static TradeOp CopyPrice(int territory, int target, double price) => new TradeOp { Kind = "copy", Territory = territory, Target = target, Price = price };

        internal static TradeOp GeneralPrice(int target, double price) => new TradeOp { Kind = "general", Target = target, Price = price };

        /// <summary>The routes of the editing empire at <paramref name="owner"/>'s tolled posts: pay, detour or automatic.</summary>
        internal static TradeOp TollChoice(int owner, TollChoiceKind choice) => new TradeOp { Kind = "choice", Target = owner, Choice = choice };

        private static void ApplyTrade(int owner, List<TradeOp> ops)
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null || ops == null || owner < 0 || owner >= Sandbox.NumberOfMajorEmpires)
            {
                return;
            }
            CurrencyWorld world = CurrencyManager.Ensure(sandbox.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
            int changed = 0;
            lock (CurrencyManager.Lock)
            {
                foreach (TradeOp op in ops)
                {
                    if (op == null || op.Target < 0 || op.Target >= Sandbox.NumberOfMajorEmpires || op.Target == owner)
                    {
                        continue;
                    }
                    switch (op.Kind)
                    {
                        case "rule":
                            changed += TradePolicy.SetRule(world, owner, op.Territory, op.Target, op.Mode) ? 1 : 0;
                            break;
                        case "post":
                            changed += TradePolicy.SetPostPrice(world, owner, op.Territory, op.Target, op.Price) ? 1 : 0;
                            break;
                        case "copy":
                            changed += TradePolicy.CopyPostPrice(world, owner, op.Territory, op.Target, op.Price) ? 1 : 0;
                            break;
                        case "general":
                            TradePolicy.SetGeneralPrice(world, owner, op.Target, op.Price);
                            changed++;
                            break;
                        case "choice":
                            changed += TradePolicy.SetTollChoice(world, owner, op.Target, op.Choice, sandbox.Turn) ? 1 : 0;
                            break;
                    }
                }
            }
            Plugin.Log.LogInfo($"Trade policy of E{owner}: {ops.Count} change(s) requested, {changed} applied.");
        }

        private static EmpireCurrency Mine(int empire)
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return null;
            }
            CurrencyWorld world = CurrencyManager.Ensure(sandbox.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
            lock (CurrencyManager.Lock)
            {
                return world.Get(empire);
            }
        }
    }
}
