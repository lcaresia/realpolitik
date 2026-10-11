using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace CurrencyMod
{
    /// <summary>
    /// Blockade and toll on the simulation side (sandbox thread):
    /// - route pathfinding treats a blocked territory as impassable and a toll as an expensive detour;
    /// - when the rules change, routes are recomputed (they detour or are destroyed);
    /// - at end of turn the toll is collected, the diplomatic grievance is kept up and the AI reviews its rules.
    /// </summary>
    internal static class TradeBlockade
    {
        internal static bool Enabled => EconomyConfig.EnableTradeBlockade.Value && !SavePatches.IsGameOnline();

        internal static MajorEmpire OwnerOf(int territoryIndex)
        {
            if (territoryIndex < 0 || territoryIndex >= Sandbox.World.Territories.Length)
            {
                return null;
            }
            Territory territory = Sandbox.World.Territories[territoryIndex];
            return territory?.Region.Entity?.Settlement.Entity?.Empire.Entity as MajorEmpire;
        }

        private static bool IsMajor(int empireIndex) => empireIndex >= 0 && empireIndex < Sandbox.NumberOfMajorEmpires;

        /// <summary>
        /// "game routes": active trade routes between major empires that cross a third party's territory, with what
        /// that owner does to each side; toll and blockade rules per owner; tolls paid at the last end of turn.
        /// </summary>
        internal static string Describe(bool fullPaths = false)
        {
            if (Sandbox.TradeController == null || Sandbox.World == null)
            {
                return "error: no game";
            }
            if (fullPaths)
            {
                // "game routes detail": the full path of each active route, territory by territory, with the owner.
                var paths = new System.Text.StringBuilder();
                foreach (ExternalTradeRelation relation in Sandbox.TradeController.ExternalTradeRelations)
                {
                    if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active)
                    {
                        continue;
                    }
                    var steps = new List<string>();
                    for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                    {
                        Territory territory = relation.PathAsTerritories[i];
                        if (territory != null)
                        {
                            steps.Add($"T{territory.Index}(E{OwnerOf(territory.Index)?.Index.ToString() ?? "-"})");
                        }
                    }
                    paths.AppendLine($"E{relation.LeftEmpireIndex}↔E{relation.RightEmpireIndex}: {string.Join(" ", steps)}");
                }
                return paths.ToString().TrimEnd();
            }
            var text = new System.Text.StringBuilder();
            int active = 0;
            int crossing = 0;
            int charged = 0;
            foreach (ExternalTradeRelation relation in Sandbox.TradeController.ExternalTradeRelations)
            {
                if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active)
                {
                    continue;
                }
                active++;
                int left = relation.LeftEmpireIndex;
                int right = relation.RightEmpireIndex;
                int goods = ResourcesBought(relation.LeftTradeExchange.Entity) + ResourcesBought(relation.RightTradeExchange.Entity);
                var crossed = new List<string>();
                bool tolled = false;
                for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                {
                    Territory territory = relation.PathAsTerritories[i];
                    MajorEmpire owner = territory != null ? OwnerOf(territory.Index) : null;
                    if (owner == null || owner.Index == left || owner.Index == right)
                    {
                        continue;
                    }
                    TradeMode forLeft = IsMajor(left) ? TradePolicy.GetMode(territory.Index, left, owner.Index) : TradeMode.Free;
                    TradeMode forRight = IsMajor(right) ? TradePolicy.GetMode(territory.Index, right, owner.Index) : TradeMode.Free;
                    tolled |= forLeft == TradeMode.Toll || forRight == TradeMode.Toll;
                    crossed.Add($"T{territory.Index} of E{owner.Index}"
                        + (forLeft != TradeMode.Free ? $" ({forLeft} for E{left})" : string.Empty)
                        + (forRight != TradeMode.Free ? $" ({forRight} for E{right})" : string.Empty));
                }
                if (crossed.Count == 0)
                {
                    continue;
                }
                crossing++;
                if (tolled && goods > 0)
                {
                    charged++;
                }
                text.AppendLine($"E{left} ↔ E{right} ({goods} resource(s)): {string.Join(", ", crossed)}");
            }
            CurrencyWorld world = CurrencyManager.Current;
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"active routes between empires: {active} · crossing third parties: {crossing} · with a toll to charge: {charged}");
            if (world != null)
            {
                lock (CurrencyManager.Lock)
                {
                    var byOwner = new Dictionary<int, int[]>();
                    foreach (TradeRule rule in world.TradeRules)
                    {
                        if (!byOwner.TryGetValue(rule.Owner, out int[] counts))
                        {
                            counts = new int[3];
                            byOwner[rule.Owner] = counts;
                        }
                        counts[(int)rule.Mode]++;
                    }
                    var owners = new List<string>();
                    foreach (KeyValuePair<int, int[]> pair in byOwner)
                    {
                        owners.Add($"E{pair.Key}: {pair.Value[1]} toll(s), {pair.Value[2]} block(s)");
                    }
                    summary.AppendLine("rules (per territory and target): " + (owners.Count > 0 ? string.Join(" · ", owners) : "none"));
                    var tolls = new List<string>();
                    foreach (TollRecord toll in world.LastTolls)
                    {
                        tolls.Add($"E{toll.Payer} paid {toll.PaidByPayer:0} to E{toll.Owner} ({toll.Routes} route(s))");
                    }
                    summary.AppendLine($"tolls at the last end of turn (turn {world.LastTradeTurn}): " + (tolls.Count > 0 ? string.Join("; ", tolls) : "none"));
                }
            }
            return summary.ToString() + text.ToString().TrimEnd();
        }

        /// <summary>Most restrictive mode the territory owner applies to either side of the route.</summary>
        internal static TradeMode ModeFor(int territoryIndex, int ownerIndex, int left, int right)
        {
            TradeMode mode = TradeMode.Free;
            if (IsMajor(left) && left != ownerIndex)
            {
                mode = TradePolicy.GetMode(territoryIndex, left, ownerIndex);
            }
            if (IsMajor(right) && right != ownerIndex && mode != TradeMode.Block)
            {
                TradeMode other = TradePolicy.GetMode(territoryIndex, right, ownerIndex);
                if (other > mode)
                {
                    mode = other;
                }
            }
            return mode;
        }

        // ---------------- Route pathfinding ----------------

        /// <summary>External route being computed right now (the game's context only holds the two empires).</summary>
        private static ExternalTradeRelation currentRelation;

        [HarmonyPatch(typeof(TradePathfindContext), nameof(TradePathfindContext.Clear))]
        private static class ContextClearPatch
        {
            private static void Postfix() => currentRelation = null;
        }

        [HarmonyPatch(typeof(TradePathfindContext), nameof(TradePathfindContext.TryFillContext), new[] { typeof(ExternalTradeRelation) })]
        private static class ContextFillPatch
        {
            private static void Prefix(ExternalTradeRelation externalTradeRelation) => currentRelation = externalTradeRelation;
        }

        /// <summary>
        /// New route: the game clears PathAsPathNode before CreatePath, and the exchanges have no TradeNodes yet, so the toll
        /// of the first path came out with the per-territory upkeep floor (very expensive toll, exaggerated detour) and nothing
        /// requested a recompute. Marked here, the game's OnOrderProcessedOrTurnBeginOrTurnEnd redoes the path with real values.
        /// </summary>
        [HarmonyPatch(typeof(BaseTradeRelation), nameof(BaseTradeRelation.ApplyTradeAction))]
        private static class StartTradePatch
        {
            private static void Postfix(BaseTradeRelation __instance, TradeRoadAction action, bool __result)
            {
                // Also when an existing route gains goods (StartTrade again): its toll changed.
                if (action == TradeRoadAction.StartTrade && __result && Enabled && __instance is ExternalTradeRelation
                    && __instance.TradeRoadStatus == TradeRoadStatus.Active)
                {
                    __instance.NeedPathRefresh = true;
                    var relation = (ExternalTradeRelation)__instance;
                    var path = new List<string>();
                    for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                    {
                        if (relation.PathAsTerritories[i] != null)
                        {
                            path.Add("T" + relation.PathAsTerritories[i].Index);
                        }
                    }
                    Plugin.Log.LogInfo($"Route E{relation.LeftEmpireIndex}↔E{relation.RightEmpireIndex}: StartTrade along path {string.Join(",", path)}; marked for recompute with the real toll.");
                }
            }
        }

        /// <summary>
        /// Toll as a path cost: the route compares paying the toll with detouring. Detouring costs
        /// upkeep — the game charges trade upkeep per territory crossed — so the per-turn toll
        /// is converted into "equivalent territories" using this route's upkeep per territory.
        /// If paying is cheaper than the detour, the route passes and pays; otherwise, it detours.
        /// </summary>
        /// <summary>Step cost for whoever chose to detour: far above any detour, but finite.</summary>
        private const float DetourCost = 1000f;

        private static float TollPathCost(int territory, MajorEmpire owner, int left, int right)
        {
            ExternalTradeRelation relation = currentRelation;
            double weight = EconomyConfig.TollDetourCost.Value;
            if (relation == null || weight <= 0)
            {
                return 0f;
            }
            ExternalTradeExchange leftExchange = relation.LeftTradeExchange.Entity;
            ExternalTradeExchange rightExchange = relation.RightTradeExchange.Entity;
            int goods = ResourcesBought(leftExchange) + ResourcesBought(rightExchange);
            if (goods <= 0)
            {
                return 0f; // a route without goods pays no toll
            }
            int era = Math.Max(1, owner.DepartmentOfDevelopment.CurrentEraIndex);
            double toll = 0;
            foreach (int payer in new[] { left, right })
            {
                if (IsMajor(payer) && TradePolicy.GetMode(territory, payer, owner.Index) == TradeMode.Toll)
                {
                    // The payer's choice (rota_pedagio from the language AI): pagar = passes without weighing the price;
                    // desviar = avoids this place if there is another path (high cost, not infinite: with no detour, it passes and pays).
                    switch (TradePolicy.GetTollChoice(payer, owner.Index))
                    {
                        case TollChoiceKind.Pay:
                            continue;
                        case TollChoiceKind.Detour:
                            return DetourCost;
                    }
                    toll += TradePolicy.GetPrice(territory, payer, owner.Index, era) * goods;
                }
            }
            if (toll <= 0)
            {
                return 0f;
            }
            double upkeep = 0;
            // Nodes of the route's current path. The exchanges' TradeNodes cannot be counted: in the game's recompute (RefreshPath)
            // DestroyPath empties those lists before searching for the new path, and the count fell to the floor (very expensive
            // toll, and the detour preview, which runs without destroying anything, disagreed with the game). PathAsPathNode only
            // changes afterwards.
            int nodes = relation.PathAsPathNode.Length;
            foreach (ExternalTradeExchange exchange in new[] { leftExchange, rightExchange })
            {
                if (exchange != null)
                {
                    upkeep += (float)exchange.TradeUpkeepAfterResourceDiversification.Value;
                    nodes = Math.Max(nodes, exchange.TradeNodes.Count);
                }
            }
            double upkeepPerTerritory = Math.Max(0.5, nodes > 0 ? upkeep / nodes : 0);
            // Entering a foreign territory costs 2 in the game's pathfinding: that is the "unit" of one more territory.
            const double TerritoryStep = 2.0;
            return (float)(toll / upkeepPerTerritory * TerritoryStep * weight);
        }

        [HarmonyPatch(typeof(TradePathfindContext), nameof(TradePathfindContext.GetAdjacentTransitionCost))]
        private static class PathCostPatch
        {
            internal static void Postfix(TradePathfindContext __instance, ref TerritoryNode fromNode, ref TerritoryNode toNode, ref float __result)
            {
                if (float.IsInfinity(__result) || !Enabled)
                {
                    return;
                }
                MajorEmpire owner = OwnerOf(toNode.TerritoryIndex);
                if (owner == null)
                {
                    return;
                }
                int left = __instance.LeftEmpire?.Index ?? -1;
                int right = __instance.RightEmpire?.Index ?? -1;
                if (owner.Index == left || owner.Index == right)
                {
                    return; // nobody blocks their own trade
                }
                switch (ModeFor(toNode.TerritoryIndex, owner.Index, left, right))
                {
                    case TradeMode.Block:
                        __result = float.PositiveInfinity;
                        break;
                    case TradeMode.Toll:
                        // The owner charges once per route, at its most expensive post on the path (CollectTolls). Coming
                        // from outside, this post's price applies; coming from another territory of the same owner, only
                        // how much more this one costs than that one. Before, only the entry counted: an inner post (or one
                        // more expensive than the entry) never made the route detour, but was still charged.
                        float cost = TollPathCost(toNode.TerritoryIndex, owner, left, right);
                        if (OwnerOf(fromNode.TerritoryIndex)?.Index == owner.Index
                            && ModeFor(fromNode.TerritoryIndex, owner.Index, left, right) == TradeMode.Toll)
                        {
                            cost -= TollPathCost(fromNode.TerritoryIndex, owner, left, right);
                        }
                        if (cost > 0)
                        {
                            __result += cost;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Test ("game toll test owner T_free T_tolled E_left E_right [price]"): the mod's step cost, with the context of the
        /// real route between E_left and E_right, for entering the tolled post from outside, from the same owner's free post
        /// (the inner post) and for going to the free post. The tolled post uses the preview's "what if" scenario (this territory only).
        /// </summary>
        internal static string SelfTestInnerPost(int owner, int free, int tolled, int left, int right, double price)
        {
            ExternalTradeRelation relation = Sandbox.TradeController?.GetExternalTradeRelation(left, right);
            if (relation == null || Sandbox.World == null)
            {
                return $"error: there is no trade relation E{left}↔E{right}";
            }
            if (OwnerOf(free)?.Index != owner || OwnerOf(tolled)?.Index != owner)
            {
                return $"error: T{free} and T{tolled} must belong to E{owner}";
            }
            int outside = -1;
            for (int t = 0; t < Sandbox.World.Territories.Length && outside < 0; t++)
            {
                int territoryOwner = OwnerOf(t)?.Index ?? -1;
                if (territoryOwner == left || territoryOwner == right)
                {
                    outside = t;
                }
            }
            var context = new TradePathfindContext { LeftEmpire = relation.LeftEmpire.Entity, RightEmpire = relation.RightEmpire.Entity };
            ExternalTradeRelation previous = currentRelation;
            TradePolicy.Scenario previousWhatIf = TradePolicy.WhatIf;
            currentRelation = relation;
            TradePolicy.WhatIf = new TradePolicy.Scenario { Owner = owner, Territory = tolled, Target = -1, Mode = TradeMode.Toll, Price = price };
            try
            {
                float Step(int from, int to)
                {
                    var fromNode = new TerritoryNode { TerritoryIndex = (short)from };
                    var toNode = new TerritoryNode { TerritoryIndex = (short)to };
                    float cost = 2f;
                    PathCostPatch.Postfix(context, ref fromNode, ref toNode, ref cost);
                    return cost - 2f;
                }
                MajorEmpire ownerEmpire = Sandbox.MajorEmpires[owner];
                float alone = TollPathCost(tolled, ownerEmpire, left, right);
                return $"route E{left}↔E{right}, owner E{owner}, T{tolled} tolled at {price} (toll cost alone: {alone:0.##}), T{free} free:\n"
                    + $"  from outside (T{outside}) → T{tolled}: +{Step(outside, tolled):0.##}\n"
                    + $"  from T{free} (same owner, free) → T{tolled}: +{Step(free, tolled):0.##}   ← inner post (before the fix: +0)\n"
                    + $"  from T{tolled} → T{free}: +{Step(tolled, free):0.##}";
            }
            finally
            {
                currentRelation = previous;
                TradePolicy.WhatIf = previousWhatIf;
            }
        }

        /// <summary>Who, on the route's current path, has started blocking it (-1 if nobody).</summary>
        private static int FindBlocker(BaseTradeRelation relation, int left, int right)
        {
            for (int i = 0; i < relation.PathAsTerritories.Count; i++)
            {
                Territory territory = relation.PathAsTerritories[i];
                if (territory == null)
                {
                    continue;
                }
                MajorEmpire owner = OwnerOf(territory.Index);
                if (owner != null && owner.Index != left && owner.Index != right
                    && ModeFor(territory.Index, owner.Index, left, right) == TradeMode.Block)
                {
                    return owner.Index;
                }
            }
            return -1;
        }

        [HarmonyPatch(typeof(TradeController), nameof(TradeController.OnOrderProcessedOrTurnBeginOrTurnEnd))]
        private static class RefreshPatch
        {
            /// <summary>Rules changed: routes that cross a blockade are recomputed on behalf of the blocker
            /// (the game's notification says who destroyed them); the rest are only marked for the game to recompute.</summary>
            private static void Prefix(TradeController __instance)
            {
                if (!TradePolicy.PathsDirty)
                {
                    return;
                }
                TradePolicy.PathsDirty = false;
                if (SavePatches.IsGameOnline())
                {
                    return;
                }
                try
                {
                    CurrencyWorld world = CurrencyManager.Ensure(SandboxManager.Sandbox.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
                    int rerouted = 0;
                    foreach (ExternalTradeRelation relation in __instance.ExternalTradeRelations)
                    {
                        if (relation == null || (relation.TradeRoadStatus != TradeRoadStatus.Active && relation.TradeRoadStatus != TradeRoadStatus.Suspended))
                        {
                            continue;
                        }
                        int left = relation.LeftEmpireIndex;
                        int right = relation.RightEmpireIndex;
                        int blocker = Enabled ? FindBlocker(relation, left, right) : -1;
                        if (blocker >= 0)
                        {
                            lock (CurrencyManager.Lock)
                            {
                                foreach (int victim in new[] { left, right })
                                {
                                    if (IsMajor(victim) && HasBlockOnPath(relation, blocker, victim))
                                    {
                                        TradePolicy.AddIncident(world, blocker, victim);
                                    }
                                }
                            }
                            relation.ForceRecomputePath(blocker);
                            rerouted++;
                        }
                        else
                        {
                            relation.NeedPathRefresh = true;
                        }
                    }
                    foreach (InternalTradeToCapital relation in __instance.InternalTradePerTerritoryIndex)
                    {
                        if (relation != null && relation.TradeRoadStatus != TradeRoadStatus.None)
                        {
                            relation.NeedPathRefresh = true;
                        }
                    }
                    if (rerouted > 0)
                    {
                        Plugin.Log.LogInfo($"Trade blockade: {rerouted} route(s) recomputed.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Trade blockade (route recompute): {ex}");
                }
            }

            /// <summary>After the game's processing: publishes the routes per territory for the trade post window.</summary>
            private static void Postfix(TradeController __instance)
            {
                PublishTransit(__instance);
            }
        }

        /// <summary>
        /// Map territory → foreign routes passing through it. Normally runs on the sandbox after each
        /// order; the window also calls it (read-only) when there is no map yet, right after loading.
        /// </summary>
        internal static void PublishTransit(TradeController controller)
        {
            {
                var __instance = controller;
                try
                {
                    var routes = new Dictionary<int, List<TransitRoute>>();
                    foreach (ExternalTradeRelation relation in __instance.ExternalTradeRelations)
                    {
                        if (relation == null || (relation.TradeRoadStatus != TradeRoadStatus.Active && relation.TradeRoadStatus != TradeRoadStatus.Suspended))
                        {
                            continue;
                        }
                        var route = new TransitRoute
                        {
                            Left = relation.LeftEmpireIndex,
                            Right = relation.RightEmpireIndex,
                            LeftBuys = ResourcesBought(relation.LeftTradeExchange.Entity),
                            RightBuys = ResourcesBought(relation.RightTradeExchange.Entity),
                            Suspended = relation.TradeRoadStatus == TradeRoadStatus.Suspended,
                        };
                        for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                        {
                            Territory territory = relation.PathAsTerritories[i];
                            if (territory == null)
                            {
                                continue;
                            }
                            if (!routes.TryGetValue(territory.Index, out List<TransitRoute> list))
                            {
                                list = new List<TransitRoute>();
                                routes[territory.Index] = list;
                            }
                            list.Add(route);
                        }
                    }
                    TradePolicy.PublishTransit(routes);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Trade blockade (routes for the UI): {ex.Message}");
                }
            }
        }

        private static bool HasBlockOnPath(BaseTradeRelation relation, int owner, int victim)
        {
            for (int i = 0; i < relation.PathAsTerritories.Count; i++)
            {
                Territory territory = relation.PathAsTerritories[i];
                if (territory != null && OwnerOf(territory.Index)?.Index == owner
                    && TradePolicy.GetMode(territory.Index, victim, owner) == TradeMode.Block)
                {
                    return true;
                }
            }
            return false;
        }

        internal static int ResourcesBought(ExternalTradeExchange exchange)
        {
            return exchange == null ? 0 : (int)exchange.NumberOfDifferentResourceTraded.Value;
        }

        // ---------------- End of turn: tolls, grievances and AI ----------------

        [HarmonyPatch(typeof(DepartmentOfTheTreasury), "TurnEndPass_CollectMoney")]
        private static class TurnEndPatch
        {
            private static void Prefix()
            {
                if (!Enabled)
                {
                    return;
                }
                try
                {
                    Sandbox sandbox = SandboxManager.Sandbox;
                    CurrencyWorld world = CurrencyManager.Ensure(sandbox.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
                    lock (CurrencyManager.Lock)
                    {
                        if (world.LastTradeTurn == sandbox.Turn)
                        {
                            return;
                        }
                        world.LastTradeTurn = sandbox.Turn;
                        DropLostTerritoryRules(world);
                        CollectTolls(world);
                        Diplomacy.Blocs.BlocTrade.RefundUpkeep();
                        RouteTransit.PayOwners(world);
                        if (EconomyConfig.EnableAiTradePolicy.Value)
                        {
                            TradeAi.Review(world, sandbox.Turn);
                        }
                        KeepGrievances(world);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Trade blockade (end of turn): {ex}");
                }
            }
        }

        /// <summary>
        /// Each tolled side pays, per route, (resources the route carries) × toll price. Each owner charges once per
        /// route, even if it crosses several of its posts: at the price of the most expensive post on the path. The price is
        /// the post's, else the empire's general one, else the default (config × the collector's era).
        /// </summary>
        private static void CollectTolls(CurrencyWorld world)
        {
            world.LastTolls.Clear();
            var owed = new Dictionary<long, TollRecord>();
            foreach (ExternalTradeRelation relation in Sandbox.TradeController.ExternalTradeRelations)
            {
                if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active)
                {
                    continue;
                }
                int left = relation.LeftEmpireIndex;
                int right = relation.RightEmpireIndex;
                int goods = ResourcesBought(relation.LeftTradeExchange.Entity) + ResourcesBought(relation.RightTradeExchange.Entity);
                if (goods <= 0)
                {
                    continue;
                }
                var highest = new Dictionary<long, double>();
                for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                {
                    Territory territory = relation.PathAsTerritories[i];
                    MajorEmpire owner = territory != null ? OwnerOf(territory.Index) : null;
                    if (owner == null || owner.Index == left || owner.Index == right)
                    {
                        continue;
                    }
                    int era = Math.Max(1, owner.DepartmentOfDevelopment.CurrentEraIndex);
                    foreach (int payer in new[] { left, right })
                    {
                        if (!IsMajor(payer) || TradePolicy.GetMode(territory.Index, payer, owner.Index) != TradeMode.Toll)
                        {
                            continue;
                        }
                        long key = (long)owner.Index * 256 + payer;
                        double price = TradePolicy.GetPrice(territory.Index, payer, owner.Index, era);
                        if (!highest.TryGetValue(key, out double known) || price > known)
                        {
                            highest[key] = price;
                        }
                    }
                }
                foreach (KeyValuePair<long, double> pair in highest)
                {
                    if (!owed.TryGetValue(pair.Key, out TollRecord record))
                    {
                        record = new TollRecord { Owner = (int)(pair.Key / 256), Payer = (int)(pair.Key % 256) };
                        owed[pair.Key] = record;
                    }
                    record.ReceivedByOwner += pair.Value * goods;
                    record.Routes++;
                }
            }

            foreach (TollRecord record in owed.Values)
            {
                MajorEmpire owner = Sandbox.MajorEmpires[record.Owner];
                MajorEmpire payer = Sandbox.MajorEmpires[record.Payer];
                if (owner == null || payer == null || !owner.IsAlive || !payer.IsAlive)
                {
                    continue;
                }
                // The toll is charged in the owner's currency; the payer spends the equivalent in its own.
                record.ReceivedByOwner = Math.Round(record.ReceivedByOwner);
                record.PaidByPayer = Math.Round(record.ReceivedByOwner * world.Rate(record.Owner, record.Payer));
                TransferConversion.WithoutRecording(() =>
                {
                    payer.DepartmentOfTheTreasury.Pay((FixedPoint)(float)record.PaidByPayer);
                    owner.DepartmentOfTheTreasury.GainMoney((FixedPoint)(float)record.ReceivedByOwner);
                });
                world.LastTolls.Add(record);
                EconomySimulation.RecordMoney(world, record.Payer, record.Owner, record.PaidByPayer, record.ReceivedByOwner, MoneyKind.Toll);
                TradePolicy.AddIncident(world, record.Owner, record.Payer);
                Plugin.Log.LogInfo($"Toll: empire {record.Payer} paid {record.PaidByPayer} to empire {record.Owner} ({record.Routes} route(s)).");
            }
        }

        /// <summary>
        /// A rule for a territory that changed owner no longer applies on the path (GetMode checks the owner), but it stayed
        /// alive for the victim's grievance, the AI's retaliation and HasAnyRule, and nobody could clear it through the UI
        /// (the post no longer belongs to the owner). Call with the lock held.
        /// </summary>
        private static void DropLostTerritoryRules(CurrencyWorld world)
        {
            int removed = world.TradeRules.RemoveAll(r => OwnerOf(r.Territory)?.Index != r.Owner);
            if (removed > 0)
            {
                TradePolicy.Invalidate();
                Plugin.Log.LogInfo($"Trade blockade: removed {removed} rule(s) for territories that changed owner.");
            }
        }

        /// <summary>While the blockade/toll continues, the victim keeps the "Trade blockade" grievance.</summary>
        private static void KeepGrievances(CurrencyWorld world)
        {
            world.TradeIncidents.RemoveAll(i => !TradePolicy.HasAnyRule(world, i.Owner, i.Victim));
            foreach (TradeIncident incident in world.TradeIncidents)
            {
                try
                {
                    TradeGrievances.Ensure(world, incident.Victim, incident.Owner);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Blockade grievance ({incident.Victim} against {incident.Owner}): {ex.Message}");
                }
            }
        }
    }
}
