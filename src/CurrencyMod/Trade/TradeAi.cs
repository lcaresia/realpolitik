using System;
using System.Collections.Generic;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod
{
    /// <summary>
    /// The AI decides, for each rival, whether to let it through, charge a toll or block it at all of its posts. Each AI
    /// reviews from time to time (staggered by index):
    /// - nation driven by the language AI with a chosen policy (politica_comercial): its choice applies;
    /// - otherwise, hostility: grievances against the rival, an ongoing crisis and retaliation for its toll or blockade.
    ///   High hostility blocks; medium charges a toll. A much richer rival only gets a toll if there is some
    ///   hostility (it used to be enough to be richer, and small nations taxed almost everyone);
    /// - a new toll only against those with a route crossing the AI's territories (without a route, the toll earns
    ///   nothing and only causes friction). An existing rule stays while hostility justifies it, so the detoured route
    ///   does not come back.
    /// </summary>
    internal static class TradeAi
    {
        /// <summary>(empire &lt;&lt; 16 | rival) → mode chosen by the language AI. Published by the main thread.</summary>
        private static volatile Dictionary<int, TradeMode> llmStances = new Dictionary<int, TradeMode>();

        internal static void SetLlmStances(Dictionary<int, TradeMode> table)
        {
            llmStances = table ?? new Dictionary<int, TradeMode>();
        }

        internal static void Review(CurrencyWorld world, int turn)
        {
            int interval = Math.Max(1, EconomyConfig.AiReviewInterval.Value);
            int count = Sandbox.NumberOfMajorEmpires;
            Dictionary<int, TradeMode> chosen = llmStances;

            // Territories of each empire (a single pass).
            var territoriesOf = new List<int>[count];
            for (int i = 0; i < count; i++)
            {
                territoriesOf[i] = new List<int>();
            }
            for (int t = 0; t < Sandbox.World.Territories.Length; t++)
            {
                MajorEmpire owner = TradeBlockade.OwnerOf(t);
                if (owner != null && owner.Index < count)
                {
                    territoriesOf[owner.Index].Add(t);
                }
            }

            for (int me = 0; me < count; me++)
            {
                MajorEmpire empire = Sandbox.MajorEmpires[me];
                if (empire == null || !empire.IsAlive || empire.IsControlledByHuman)
                {
                    continue;
                }
                // The language AI's policy applies to every territory, new ones included: applied every turn. The
                // hostility review keeps the usual interval.
                bool review = (turn + me) % interval == 0 && empire.DepartmentOfDevelopment.CurrentEraIndex > 0;
                HashSet<int> users = review ? RouteUsers(territoriesOf[me], me) : null;
                for (int other = 0; other < count; other++)
                {
                    MajorEmpire rival = Sandbox.MajorEmpires[other];
                    if (other == me || rival == null || !rival.IsAlive)
                    {
                        continue;
                    }
                    TradeMode wanted;
                    if (chosen.TryGetValue((me << 16) | other, out TradeMode llm))
                    {
                        wanted = llm;
                    }
                    else if (review)
                    {
                        wanted = Decide(world, empire, rival, users);
                    }
                    else
                    {
                        continue;
                    }
                    int changed = 0;
                    foreach (int territory in territoriesOf[me])
                    {
                        if (TradePolicy.SetRule(world, me, territory, other, wanted))
                        {
                            changed++;
                        }
                    }
                    if (changed > 0)
                    {
                        Plugin.Log.LogInfo($"AI {me} → empire {other}: {TradePolicy.ModeName(wanted)} in {territoriesOf[me].Count} territory(ies){(chosen.ContainsKey((me << 16) | other) ? " (language AI choice)" : string.Empty)}.");
                    }
                }
            }
        }

        /// <summary>Foreign empires with a trade route crossing the territories of <paramref name="me"/>.</summary>
        private static HashSet<int> RouteUsers(List<int> territories, int me)
        {
            var users = new HashSet<int>();
            foreach (int territory in territories)
            {
                foreach (TransitRoute route in TradePolicy.RoutesThrough(territory))
                {
                    if (route.Left != me) users.Add(route.Left);
                    if (route.Right != me) users.Add(route.Right);
                }
            }
            return users;
        }

        private static TradeMode Decide(CurrencyWorld world, MajorEmpire me, MajorEmpire rival, HashSet<int> users)
        {
            DiplomaticRelation relation = me.DiplomaticRelationByOtherEmpireIndex[rival.Index];
            if (relation.CurrentState != DiplomaticStateType.Peace)
            {
                // Allies and vassals pass freely; at war the game itself already closes the territory.
                return TradeMode.Free;
            }

            int hostility = 0;
            DiplomaticAmbassy embassy = relation.GetEmpireEmbassy(me.Index);
            hostility += Math.Min(3, embassy.AvailableGrievances.GrievanceIndexesCount);
            var crisis = relation.DiplomaticState.Crisis.CrisisStatus;
            if (crisis == CrisisInfo.Status.OnGoing || crisis == CrisisInfo.Status.DemandRefused)
            {
                hostility += 3;
            }
            if (TradePolicy.HasAnyRule(world, rival.Index, me.Index))
            {
                hostility += 2; // retaliation
            }

            if (hostility >= 5)
            {
                return TradeMode.Block;
            }
            bool relevant = users.Contains(rival.Index) || TradePolicy.HasAnyRule(world, me.Index, rival.Index);
            if (!relevant)
            {
                return TradeMode.Free;
            }
            EmpireCurrency mine = world.Get(me.Index);
            EmpireCurrency theirs = world.Get(rival.Index);
            bool richer = mine != null && theirs != null && theirs.Strength > mine.Strength * 1.35;
            if (hostility >= 2 || (richer && hostility >= 1))
            {
                return TradeMode.Toll;
            }
            return TradeMode.Free;
        }
    }
}
