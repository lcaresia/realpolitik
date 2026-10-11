using System;
using System.Collections.Generic;

namespace CurrencyMod
{
    /// <summary>What a territory's owner does with the routes of a foreign empire that pass through it.</summary>
    public enum TradeMode
    {
        Free = 0,
        Toll = 1,
        Block = 2,
    }

    /// <summary>Rule of a post: owner, territory, affected empire and mode. Only rules other than Free are stored.</summary>
    public class TradeRule
    {
        public int Owner;
        public int Territory;
        public int Target;
        public TradeMode Mode;
        /// <summary>Toll of this post for that empire, per resource and per turn, in the owner's currency. 0 = the empire's
        /// general price (TollPrice) or, without it, the configured default.</summary>
        public double Price;
    }

    /// <summary>General toll price the owner charges an empire at all posts (diplomacy Trade tab).</summary>
    public class TollPrice
    {
        public int Owner;
        public int Target;
        public double Price;
    }

    /// <summary>What the payer's routes do at an owner's tolled posts: choice of the language-model AI (rota_pedagio).</summary>
    public enum TollChoiceKind { Auto = 0, Pay = 1, Detour = 2 }

    /// <summary>Choice of <see cref="Payer"/> for the tolls of <see cref="Owner"/>, at all of its posts.</summary>
    public class TollChoice
    {
        public int Payer;
        public int Owner;
        public TollChoiceKind Choice;
        public int Turn;
    }

    /// <summary>Pair where the blockade/toll has already hit the victim's routes: keeps the grievance alive.</summary>
    public class TradeIncident
    {
        public int Owner;
        public int Victim;
    }

    /// <summary>Toll paid in the last turn (each in its own currency).</summary>
    public class TollRecord
    {
        public int Owner;
        public int Payer;
        public double PaidByPayer;
        public double ReceivedByOwner;
        public int Routes;
    }

    /// <summary>A foreign route that crosses a territory (for the post window).</summary>
    public struct TransitRoute
    {
        public int Left;
        public int Right;
        /// <summary>Resources that Left buys from Right on this route.</summary>
        public int LeftBuys;
        /// <summary>Resources that Right buys from Left on this route.</summary>
        public int RightBuys;
        public bool Suspended;
    }

    /// <summary>
    /// Blockade/toll rules. The rules live in CurrencyWorld (stored in the save); here there is
    /// a read-only index, swapped whole on every change, queried by the route computation
    /// (thousands of times per recompute, on the sandbox thread) without taking the lock.
    /// </summary>
    public static class TradePolicy
    {
        private static volatile Dictionary<long, int> cache;
        private static volatile bool stale = true;
        private static volatile Dictionary<int, List<TransitRoute>> transit = new Dictionary<int, List<TransitRoute>>();

        /// <summary>Asks the sandbox to recompute the routes on the next order or turn change.</summary>
        public static volatile bool PathsDirty;

        private static long Key(int territory, int target) => (long)territory * 256 + target;

        public static void Invalidate()
        {
            stale = true;
        }

        /// <summary>
        /// "What if" scenario of the detour preview (TollPreview): swaps an owner's mode and price for an empire in one
        /// territory (or in all of its territories, with Territory = -1) only during the computation, on the thread that set it.
        /// </summary>
        public sealed class Scenario
        {
            public int Owner;
            public int Territory;
            public int Target;
            public TradeMode Mode;
            public double Price;

            /// <summary>Target = -1: applies to all empires (with no rule of the owner there).</summary>
            internal bool Matches(int territory, int target, int owner) =>
                (Target < 0 || Target == target) && Owner == owner && (Territory < 0 || Territory == territory);
        }

        [ThreadStatic]
        public static Scenario WhatIf;

        /// <summary>Mode applied by <paramref name="owner"/> to empire <paramref name="target"/> in the territory.</summary>
        public static TradeMode GetMode(int territory, int target, int owner)
        {
            Scenario whatIf = WhatIf;
            if (whatIf != null && whatIf.Matches(territory, target, owner))
            {
                return whatIf.Mode;
            }
            // Blocs (item 16): between members trade passes freely; against outsiders the bloc's common tariff applies,
            // if it is harsher than the post's own rule.
            if (Diplomacia.Blocs.BlocIndex.SameBloc(owner, target))
            {
                return TradeMode.Free;
            }
            TradeMode tariff = Diplomacia.Blocs.BlocIndex.Tariff(owner, target);
            Dictionary<long, int> current = cache;
            if (stale || current == null)
            {
                current = Rebuild();
            }
            if (current.Count == 0 || !current.TryGetValue(Key(territory, target), out int packed))
            {
                return tariff;
            }
            // The rule belongs to the owner at the time; if the territory changed hands, it no longer applies.
            TradeMode rule = packed / 4 == owner ? (TradeMode)(packed % 4) : TradeMode.Free;
            return rule >= tariff ? rule : tariff;
        }

        private static Dictionary<long, int> Rebuild()
        {
            var built = new Dictionary<long, int>();
            var prices = new Dictionary<long, double>();
            var general = new Dictionary<long, double>();
            var choices = new Dictionary<long, TollChoiceKind>();
            // Read and publish inside the lock, and clear "stale" before reading: a rule changed (under the lock) in the
            // middle of a Rebuild outside the lock was wiped by publishing the old index, which stayed in effect until the
            // next change.
            lock (CurrencyManager.Lock)
            {
                stale = false;
                CurrencyWorld world = CurrencyManager.Current;
                if (world != null)
                {
                    foreach (TradeRule rule in world.TradeRules)
                    {
                        built[Key(rule.Territory, rule.Target)] = rule.Owner * 4 + (int)rule.Mode;
                        if (rule.Price > 0)
                        {
                            prices[Key(rule.Territory, rule.Target)] = rule.Price;
                        }
                    }
                    foreach (TollPrice price in world.TollPrices)
                    {
                        if (price.Price > 0)
                        {
                            general[(long)price.Owner * 256 + price.Target] = price.Price;
                        }
                    }
                    foreach (TollChoice choice in world.TollChoices ?? new List<TollChoice>())
                    {
                        if (choice.Choice != TollChoiceKind.Auto)
                        {
                            choices[(long)choice.Payer * 256 + choice.Owner] = choice.Choice;
                        }
                    }
                }
                priceCache = prices;
                generalCache = general;
                choiceCache = choices;
                cache = built;
            }
            return built;
        }

        // ---------------- Toll price ----------------

        private static volatile Dictionary<long, double> priceCache = new Dictionary<long, double>();
        private static volatile Dictionary<long, double> generalCache = new Dictionary<long, double>();
        private static volatile Dictionary<long, TollChoiceKind> choiceCache = new Dictionary<long, TollChoiceKind>();

        /// <summary>What the routes of <paramref name="payer"/> do at the tolled posts of <paramref name="owner"/> (read during route computation).</summary>
        public static TollChoiceKind GetTollChoice(int payer, int owner)
        {
            if (stale || cache == null)
            {
                Rebuild();
            }
            return choiceCache.TryGetValue((long)payer * 256 + owner, out TollChoiceKind choice) ? choice : TollChoiceKind.Auto;
        }

        /// <summary>Stores the payer's choice (call with the lock). Returns true if it changed.</summary>
        public static bool SetTollChoice(CurrencyWorld world, int payer, int owner, TollChoiceKind choice, int turn)
        {
            world.TollChoices = world.TollChoices ?? new List<TollChoice>();
            TollChoice current = world.TollChoices.Find(c => c.Payer == payer && c.Owner == owner);
            if ((current?.Choice ?? TollChoiceKind.Auto) == choice)
            {
                return false;
            }
            world.TollChoices.RemoveAll(c => c.Payer == payer && c.Owner == owner);
            if (choice != TollChoiceKind.Auto)
            {
                world.TollChoices.Add(new TollChoice { Payer = payer, Owner = owner, Choice = choice, Turn = turn });
            }
            stale = true;
            PathsDirty = true;
            return true;
        }

        /// <summary>Default toll per resource: the configured one times the charging empire's era.</summary>
        public static double DefaultPrice(int ownerEra) => Math.Round(EconomyConfig.TollPerResource.Value * Math.Max(1, ownerEra), 1);

        /// <summary>
        /// Toll that <paramref name="owner"/> charges <paramref name="target"/> in that territory, per resource and per turn
        /// (in the owner's currency): the post's price, otherwise the empire's general price, otherwise the default. Also
        /// read on the sandbox thread (route computation and collection), through the index swapped whole.
        /// </summary>
        public static double GetPrice(int territory, int target, int owner, int ownerEra)
        {
            Scenario whatIf = WhatIf;
            if (whatIf != null && whatIf.Matches(territory, target, owner) && whatIf.Mode == TradeMode.Toll)
            {
                return whatIf.Price;
            }
            if (stale || cache == null)
            {
                Rebuild();
            }
            Dictionary<long, int> modes = cache;
            if (priceCache.TryGetValue(Key(territory, target), out double price) && modes.TryGetValue(Key(territory, target), out int packed) && packed / 4 == owner)
            {
                return price;
            }
            return GeneralPriceOrDefault(owner, target, ownerEra);
        }

        /// <summary>The empire's general price (without the post's), or the default.</summary>
        public static double GeneralPriceOrDefault(int owner, int target, int ownerEra)
        {
            if (stale || cache == null)
            {
                Rebuild();
            }
            return generalCache.TryGetValue((long)owner * 256 + target, out double general) ? general : DefaultPrice(ownerEra);
        }

        /// <summary>A post's own price (call with the lock). Only applies to the owner's toll rule.</summary>
        public static bool SetPostPrice(CurrencyWorld world, int owner, int territory, int target, double price)
        {
            TradeRule rule = world.TradeRules.Find(r => r.Territory == territory && r.Target == target && r.Owner == owner);
            if (rule == null || rule.Mode != TradeMode.Toll)
            {
                return false;
            }
            price = ClampPrice(price);
            if (Math.Abs(rule.Price - price) < 0.001)
            {
                return false;
            }
            rule.Price = price;
            stale = true;
            PathsDirty = true;
            return true;
        }

        /// <summary>
        /// General price charged to the empire at all posts (call with the lock). Removes the posts' own prices for that
        /// empire: "general" applies everywhere. Price 0 goes back to the default.
        /// </summary>
        public static void SetGeneralPrice(CurrencyWorld world, int owner, int target, double price)
        {
            world.TollPrices.RemoveAll(p => p.Owner == owner && p.Target == target);
            if (price > 0)
            {
                world.TollPrices.Add(new TollPrice { Owner = owner, Target = target, Price = ClampPrice(price) });
            }
            foreach (TradeRule rule in world.TradeRules)
            {
                if (rule.Owner == owner && rule.Target == target)
                {
                    rule.Price = 0;
                }
            }
            stale = true;
            PathsDirty = true;
        }

        /// <summary>Copies a post's price to another, including 0 (= general price). Call with the lock.</summary>
        public static bool CopyPostPrice(CurrencyWorld world, int owner, int territory, int target, double price)
        {
            TradeRule rule = world.TradeRules.Find(r => r.Territory == territory && r.Target == target && r.Owner == owner);
            double value = price > 0 ? ClampPrice(price) : 0;
            if (rule == null || Math.Abs(rule.Price - value) < 0.001)
            {
                return false;
            }
            rule.Price = value;
            stale = true;
            PathsDirty = true;
            return true;
        }

        /// <summary>Stored general price (0 = none). Call with the lock.</summary>
        public static double StoredGeneralPrice(CurrencyWorld world, int owner, int target)
        {
            return world.TollPrices.Find(p => p.Owner == owner && p.Target == target)?.Price ?? 0;
        }

        /// <summary>A post's stored own price (0 = none). Call with the lock.</summary>
        public static double StoredPostPrice(CurrencyWorld world, int owner, int territory, int target)
        {
            return world.TradeRules.Find(r => r.Territory == territory && r.Target == target && r.Owner == owner)?.Price ?? 0;
        }

        public const double MinPrice = 1;
        public const double MaxPrice = 500;

        public static double ClampPrice(double price) => Math.Max(MinPrice, Math.Min(MaxPrice, Math.Round(price)));

        /// <summary>Next price step on the − and + buttons: by 1 up to 10, by 2 up to 20, by 5 up to 100, by 25 above that.</summary>
        public static double StepPrice(double price, int direction)
        {
            double step = price < 10 || (price == 10 && direction < 0) ? 1
                : price < 20 || (price == 20 && direction < 0) ? 2
                : price < 100 || (price == 100 && direction < 0) ? 5 : 25;
            double next = direction > 0 ? Math.Floor(price / step) * step + step : Math.Ceiling(price / step) * step - step;
            return ClampPrice(next);
        }

        /// <summary>Changes the rule (call with the CurrencyManager lock). Returns true if anything changed.</summary>
        public static bool SetRule(CurrencyWorld world, int owner, int territory, int target, TradeMode mode)
        {
            if (owner == target)
            {
                mode = TradeMode.Free; // nobody blocks themselves
            }
            int index = world.TradeRules.FindIndex(r => r.Territory == territory && r.Target == target);
            if (index >= 0)
            {
                TradeRule rule = world.TradeRules[index];
                if (rule.Owner == owner && rule.Mode == mode)
                {
                    return false;
                }
                if (mode == TradeMode.Free)
                {
                    world.TradeRules.RemoveAt(index);
                }
                else
                {
                    rule.Owner = owner;
                    rule.Mode = mode;
                }
            }
            else
            {
                if (mode == TradeMode.Free)
                {
                    return false;
                }
                world.TradeRules.Add(new TradeRule { Owner = owner, Territory = territory, Target = target, Mode = mode });
            }
            stale = true;
            PathsDirty = true;
            return true;
        }

        /// <summary>Stored mode (without checking the current owner). Call with the lock.</summary>
        public static TradeMode StoredMode(CurrencyWorld world, int owner, int territory, int target)
        {
            TradeRule rule = world.TradeRules.Find(r => r.Territory == territory && r.Target == target && r.Owner == owner);
            return rule?.Mode ?? TradeMode.Free;
        }

        /// <summary>Does the owner have any active rule against the target? Call with the lock.</summary>
        public static bool HasAnyRule(CurrencyWorld world, int owner, int target)
        {
            return world.TradeRules.Exists(r => r.Owner == owner && r.Target == target);
        }

        /// <summary>Removes all of the owner's rules against the target (demand accepted). Call with the lock.</summary>
        public static int ClearRules(CurrencyWorld world, int owner, int target)
        {
            int removed = world.TradeRules.RemoveAll(r => r.Owner == owner && r.Target == target);
            world.TradeIncidents.RemoveAll(i => i.Owner == owner && i.Victim == target);
            if (removed > 0)
            {
                stale = true;
                PathsDirty = true;
            }
            return removed;
        }

        public static void AddIncident(CurrencyWorld world, int owner, int victim)
        {
            if (!world.TradeIncidents.Exists(i => i.Owner == owner && i.Victim == victim))
            {
                world.TradeIncidents.Add(new TradeIncident { Owner = owner, Victim = victim });
            }
        }

        // ---------------- Routes crossing each territory (read by the UI) ----------------

        public static void PublishTransit(Dictionary<int, List<TransitRoute>> routesPerTerritory)
        {
            transit = routesPerTerritory;
            TransitPublished = true;
        }

        public static volatile bool TransitPublished;

        public static List<TransitRoute> RoutesThrough(int territory)
        {
            return transit.TryGetValue(territory, out List<TransitRoute> routes) ? routes : new List<TransitRoute>();
        }

        public static string ModeName(TradeMode mode)
        {
            switch (mode)
            {
                case TradeMode.Toll: return "Pedágio";
                case TradeMode.Block: return "Bloqueado";
                default: return "Livre";
            }
        }
    }
}
