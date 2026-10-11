using System;
using System.Collections.Generic;
using Amplitude.Mercury;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod
{
    /// <summary>
    /// Detour preview: how many routes of an empire would pass through a post (or through an owner's posts) with no toll,
    /// and how many keep passing at a given price. Uses the game's own pathfinding (BaseTradeRelation.TryFindPath, which
    /// already includes the mod's toll and blockade cost) with a "what if" scenario (TradePolicy.WhatIf), without changing
    /// any route. The UI requests (main thread); the computation runs on the sandbox thread, between orders, in the main turn.
    /// </summary>
    internal static class TollPreview
    {
        internal sealed class Ask
        {
            public int Owner;
            /// <summary>-1 = all of the owner's territories (general price from diplomacy).</summary>
            public int Territory;
            public int Target;
            public TradeMode Mode;
            /// <summary>Requested price. Negative = today's rules and prices, no scenario (posts with different prices).</summary>
            public double Price;

            public string Key => $"{Owner}|{Territory}|{Target}|{(int)Mode}|{Price:0.##}";
        }

        internal sealed class Answer
        {
            public string Key;
            /// <summary>Routes of that empire that would pass there with no toll or blockade.</summary>
            public int Routes;
            /// <summary>Of those that would pass, how many keep passing with the requested mode and price.</summary>
            public int Stay;
            public int Away => Routes - Stay;
            /// <summary>When it was computed (Environment.TickCount, ms): valid on any thread.</summary>
            public int At;
            /// <summary>For the test command: each route examined and the path it would take without the owner's rule.</summary>
            public string Detail;
            /// <summary>Other side of each route that keeps passing / that detours (or gets blocked).</summary>
            public readonly List<int> StayPartners = new List<int>();
            public readonly List<int> AwayPartners = new List<int>();
            /// <summary>For each route that detours (same order as AwayPartners): another territory of the same owner it now
            /// goes through (-1 = leaves their lands). Detouring from one of your posts to another of yours still pays you.</summary>
            public readonly List<int> AwayVia = new List<int>();

            /// <summary>Route key (lower|higher), to sum posts and rows without counting the same route twice.</summary>
            public static string RouteKey(int a, int b) => a < b ? $"{a}|{b}" : $"{b}|{a}";
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Ask> pending = new Dictionary<string, Ask>();
        private static volatile Dictionary<string, Answer> answers = new Dictionary<string, Answer>();

        /// <summary>Already computed answer (null = not yet). Answers older than a few seconds are redone on the next request.</summary>
        internal static Answer Get(Ask ask)
        {
            return answers.TryGetValue(ask.Key, out Answer answer) ? answer : null;
        }

        /// <summary>Requests the computation (if the answer does not exist or is stale). Called by the UI.</summary>
        internal static void Request(Ask ask, float maxAgeSeconds = 4f)
        {
            Answer known = Get(ask);
            if (known != null && unchecked(Environment.TickCount - known.At) < maxAgeSeconds * 1000f)
            {
                return;
            }
            lock (Gate)
            {
                pending[ask.Key] = ask;
            }
        }

        /// <summary>Sandbox thread (ActionExecutor pump): computes the accumulated requests.</summary>
        internal static void Pump()
        {
            List<Ask> work;
            lock (Gate)
            {
                if (pending.Count == 0)
                {
                    return;
                }
                work = new List<Ask>(pending.Values);
                pending.Clear();
            }
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null || Sandbox.TradeController == null || sandbox.CurrentStateName != "SandboxState_TurnMain")
            {
                return;
            }
            var updated = new Dictionary<string, Answer>(answers);
            int now = Environment.TickCount;
            foreach (Ask ask in work)
            {
                try
                {
                    Answer answer = Compute(ask);
                    answer.At = now;
                    updated[ask.Key] = answer;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"Detour preview: {ex.Message}");
                }
            }
            answers = updated;
        }

        private static Answer Compute(Ask ask)
        {
            var answer = new Answer { Key = ask.Key };
            var detail = new System.Text.StringBuilder();
            var path = new ListOfStruct<TerritoryPathInfo>();
            foreach (ExternalTradeRelation relation in Sandbox.TradeController.ExternalTradeRelations)
            {
                if (relation == null || (relation.TradeRoadStatus != TradeRoadStatus.Active && relation.TradeRoadStatus != TradeRoadStatus.Suspended))
                {
                    continue;
                }
                int left = relation.LeftEmpireIndex;
                int right = relation.RightEmpireIndex;
                if ((left != ask.Target && right != ask.Target) || left == ask.Owner || right == ask.Owner)
                {
                    continue;
                }
                // With no rule from the owner there (not even for the route's other side, who would also pay): would the route pass?
                bool crosses = Crosses(relation, ask, -1, TradeMode.Free, 0, path);
                detail.Append($"E{left}↔E{right} sem regra: ");
                for (int i = 0; i < path.Length; i++)
                {
                    int territory = path.Data[i].TerritoryIndex;
                    MajorEmpire owner = TradeBlockade.OwnerOf(territory);
                    detail.Append($"T{territory}{(owner != null ? $"(E{owner.Index})" : string.Empty)} ");
                }
                detail.AppendLine(path.Length == 0 ? "sem caminho" : string.Empty);
                if (!crosses)
                {
                    continue;
                }
                answer.Routes++;
                int partner = left == ask.Target ? right : left;
                // With today's rules plus the requested mode and price for that empire: does it keep passing?
                if (ask.Mode != TradeMode.Block && Crosses(relation, ask, ask.Target, ask.Mode, ask.Price, path))
                {
                    answer.Stay++;
                    answer.StayPartners.Add(partner);
                }
                else
                {
                    answer.AwayPartners.Add(partner);
                    int via = -1;
                    if (ask.Mode != TradeMode.Block && ask.Territory >= 0)
                    {
                        for (int i = 0; i < path.Length && via < 0; i++)
                        {
                            int territory = path.Data[i].TerritoryIndex;
                            if (territory != ask.Territory && TradeBlockade.OwnerOf(territory)?.Index == ask.Owner)
                            {
                                via = territory;
                            }
                        }
                    }
                    answer.AwayVia.Add(via);
                }
                if (ask.Mode != TradeMode.Block)
                {
                    detail.Append("    com a regra: ");
                    for (int i = 0; i < path.Length; i++)
                    {
                        int territory = path.Data[i].TerritoryIndex;
                        MajorEmpire owner = TradeBlockade.OwnerOf(territory);
                        detail.Append($"T{territory}{(owner != null ? $"(E{owner.Index})" : string.Empty)} ");
                    }
                    detail.AppendLine(path.Length == 0 ? "sem caminho" : string.Empty);
                }
            }
            answer.Detail = detail.ToString().TrimEnd();
            return answer;
        }

        private static bool Crosses(ExternalTradeRelation relation, Ask ask, int target, TradeMode mode, double price, ListOfStruct<TerritoryPathInfo> path)
        {
            // Negative price in the "with the rule" pass: what is stored today applies (each post with its own price).
            TradePolicy.WhatIf = target >= 0 && price < 0 ? null
                : new TradePolicy.Scenario { Owner = ask.Owner, Territory = ask.Territory, Target = target, Mode = mode, Price = price };
            try
            {
                path.Clear();
                if (!BaseTradeRelation.TryFindPath(relation, path))
                {
                    return false;
                }
                for (int i = 0; i < path.Length; i++)
                {
                    int territory = path.Data[i].TerritoryIndex;
                    if (ask.Territory >= 0 ? territory == ask.Territory : TradeBlockade.OwnerOf(territory)?.Index == ask.Owner)
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                TradePolicy.WhatIf = null;
            }
        }
    }
}
