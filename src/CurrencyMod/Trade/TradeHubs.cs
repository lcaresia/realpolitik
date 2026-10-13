using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using Amplitude.Framework.Simulation;
using HarmonyLib;
using SettlementStatuses = Amplitude.Mercury.Data.Simulation.SettlementStatuses;

namespace CurrencyMod
{
    /// <summary>
    /// Effects of the trade hubs (melhorias-jogando item 36). Being rich is always worth it: the buffs grow with the level
    /// and always outweigh the debuffs, which only act as a brake on runaway growth.
    /// - Buffs by level: money per route (1+), growth (2+), influence (3+), science (4); fame for the ends of grand routes.
    /// - Debuffs are exposure × vulnerability: the level opens the door (inflation 2+, spies 3+, crime, minorities and
    ///   epidemic 4), and the city's own state decides how much comes in. A very stable city has no crime and no spy
    ///   bonus; a treasury without debt feels no inflation; a city of one faith and culture has no minorities; a stable,
    ///   well-fed city almost never gets an epidemic. Cutting a route the nation depended on brings a short supply crisis.
    /// - The stability debuffs together never exceed HubMaxStabilityLoss.
    /// Applied on the sandbox thread: the stock gains at the end of the turn (VeteranEffects pattern), stability through
    /// NativeEffects.StabilityLoss, spies through the stealth drain, inflation through EconomySimulation.TargetInflation.
    /// </summary>
    internal static class TradeHubs
    {
        internal sealed class Effects
        {
            public double Money;
            public double GrowthPercent;
            public double Influence;
            public double SciencePercent;
            public double CrimeLoss;
            public double MinorityLoss;
            /// <summary>Share of the stealth drain foreign spies escape in this city (0..HubSpyMax).</summary>
            public double SpyShield;
            public double EpidemicChance;
        }

        private const double StableCity = 80;
        private const int SupplyCrisisTurns = 5;
        private const int EpidemicCooldown = 15;

        /// <summary>0 at 80 stability or more, 1 at 30 or less.</summary>
        internal static double Instability(double stability) => Math.Max(0, Math.Min(1, (StableCity - stability) / 50.0));

        /// <summary>Fills each hub's effects from its level and its city's state (sandbox thread, inside TradeNetwork.Refresh).</summary>
        internal static void Compute(TradeNetwork.Snapshot snapshot)
        {
            foreach (TradeNetwork.Hub hub in snapshot.Hubs)
            {
                var effects = new Effects();
                MajorEmpire owner = hub.Empire >= 0 && hub.Empire < Sandbox.NumberOfMajorEmpires ? Sandbox.MajorEmpires[hub.Empire] : null;
                int era = Math.Max(1, owner?.DepartmentOfDevelopment.CurrentEraIndex ?? 1);
                double exposure = hub.Level / (double)TradeNetwork.MaxLevel;
                double instability = Instability(hub.Stability);
                effects.Money = EconomyConfig.HubMoneyPerRoute.Value * hub.Routes * era;
                if (hub.Level >= 2)
                {
                    effects.GrowthPercent = EconomyConfig.HubGrowthPerLevel.Value * (hub.Level - 1);
                }
                if (hub.Level >= 3)
                {
                    effects.Influence = EconomyConfig.HubInfluencePerLevel.Value * hub.Level * era;
                    effects.SpyShield = EconomyConfig.HubSpyMax.Value * exposure * instability;
                }
                if (hub.Level >= 4)
                {
                    effects.SciencePercent = EconomyConfig.HubSciencePercent.Value;
                    effects.CrimeLoss = EconomyConfig.HubCrimeMax.Value * exposure * instability;
                    effects.MinorityLoss = EconomyConfig.HubMinorityMax.Value * exposure * hub.ForeignShare;
                    double hungry = hub.Growth <= 0 ? 0.4 : 0;
                    double unrest = Math.Max(0, Math.Min(1, (70 - hub.Stability) / 70.0)) * 0.6;
                    effects.EpidemicChance = EconomyConfig.HubEpidemicChance.Value * exposure * (unrest + hungry);
                }
                hub.Effects = effects;
            }
        }

        // ---------------- Readings (any thread) ----------------

        internal static TradeNetwork.Hub HubOf(Settlement settlement)
        {
            if (settlement == null || !TradeNetwork.Enabled)
            {
                return null;
            }
            return TradeNetwork.Current.HubByGuid.TryGetValue((ulong)settlement.GUID, out TradeNetwork.Hub hub) ? hub : null;
        }

        /// <summary>The stability parts of trade on a city (already capped together).</summary>
        internal static void StabilityParts(Settlement settlement, out double crime, out double minorities, out double supply)
        {
            crime = minorities = supply = 0;
            if (settlement == null || settlement.SettlementStatus != SettlementStatuses.City || !TradeNetwork.Enabled || !(settlement.Empire.Entity is MajorEmpire empire))
            {
                return;
            }
            TradeNetwork.Hub hub = HubOf(settlement);
            if (hub != null)
            {
                crime = hub.Effects.CrimeLoss;
                minorities = hub.Effects.MinorityLoss;
            }
            if (SupplyCrisis(empire.Index, out _))
            {
                double exposure = hub != null ? hub.Level / (double)TradeNetwork.MaxLevel : 0;
                supply = EconomyConfig.HubSupplyCrisis.Value * (0.5 + exposure);
            }
            double total = crime + minorities + supply;
            double cap = EconomyConfig.HubMaxStabilityLoss.Value;
            if (total > cap && total > 0)
            {
                double scale = cap / total;
                crime *= scale;
                minorities *= scale;
                supply *= scale;
            }
        }

        internal static double StabilityLoss(Settlement settlement)
        {
            StabilityParts(settlement, out double crime, out double minorities, out double supply);
            return crime + minorities + supply;
        }

        /// <summary>Only with debt: the debt inflation grows with the empire's hubs (1 = no change).</summary>
        internal static double DebtInflationFactor(int empire)
        {
            if (!TradeNetwork.Enabled)
            {
                return 1;
            }
            int levels = TradeNetwork.Current.Hubs.Where(h => h.Empire == empire && h.Level >= 2).Sum(h => h.Level - 1);
            return 1 + Math.Min(0.5, EconomyConfig.HubDebtInflation.Value * levels);
        }

        /// <summary>Money the empire's hubs yield next end of turn (income tooltip).</summary>
        internal static double ProjectedMoney(int empire)
        {
            return TradeNetwork.Enabled ? TradeNetwork.Current.Hubs.Where(h => h.Empire == empire).Sum(h => h.Effects.Money) : 0;
        }

        // ---------------- Supply crisis ----------------

        private static volatile Dictionary<int, (int until, string what)> crises = new Dictionary<int, (int, string)>();
        private static Dictionary<long, double> lastShares = new Dictionary<long, double>();

        internal static bool SupplyCrisis(int empire, out string what)
        {
            what = null;
            int turn = SandboxManager.Sandbox?.Turn ?? -1;
            if (crises.TryGetValue(empire, out var crisis) && crisis.until >= turn)
            {
                what = crisis.what;
                return true;
            }
            return false;
        }

        /// <summary>
        /// End of turn: a resource the nation got 60% or more of through routes, now down to under 20%, means a route it
        /// depended on was cut (blockade, war, destroyed route): a supply crisis for SupplyCrisisTurns turns.
        /// </summary>
        private static void TrackSupply(TradeNetwork.Snapshot snapshot, int turn)
        {
            var shares = new Dictionary<long, double>();
            foreach (KeyValuePair<long, Dictionary<int, int>> item in snapshot.BoughtFrom)
            {
                int buyer = (int)(item.Key >> 8);
                int type = (int)(item.Key & 0xFF);
                MajorEmpire empire = buyer < Sandbox.NumberOfMajorEmpires ? Sandbox.MajorEmpires[buyer] : null;
                double access = empire != null ? (float)empire.DepartmentOfResources.GetResourceAccess((Amplitude.Mercury.Data.Simulation.ResourceType)type) : 0;
                double bought = item.Value.Values.Sum();
                shares[item.Key] = access > 0 ? Math.Min(1, bought / access) : 1;
            }
            var updated = new Dictionary<int, (int, string)>(crises);
            foreach (KeyValuePair<long, double> before in lastShares)
            {
                if (before.Value < 0.6 || (shares.TryGetValue(before.Key, out double now) && now >= 0.2))
                {
                    continue;
                }
                int buyer = (int)(before.Key >> 8);
                int type = (int)(before.Key & 0xFF);
                string what = Diplomacy.GameGlossary.Resource(type)?.Name ?? type.ToString();
                updated[buyer] = (turn + SupplyCrisisTurns, what);
                Plugin.Log.LogInfo($"Trade network: supply crisis for E{buyer} ({what}: a route it depended on was cut).");
            }
            lastShares = shares;
            crises = updated;
        }

        // ---------------- End of turn ----------------

        private static int lastTurn = -1;
        private static readonly Dictionary<ulong, int> lastEpidemic = new Dictionary<ulong, int>();

        /// <summary>Test ("game trade epidemic <city>"): the next end of turn strikes that hub (by name).</summary>
        internal static volatile string ForcedEpidemic;

        /// <summary>Epidemics of the last turns, for the dossier and the player (turn, empire, city, lost population).</summary>
        internal static volatile List<(int turn, int empire, string city, int lost)> Epidemics = new List<(int, int, string, int)>();

        [HarmonyPatch(typeof(SimulationPasses), nameof(SimulationPasses.InvokePasses))]
        private static class TurnEndPatch
        {
            private static void Prefix(SimulationPasses __instance, SimulationPasses.PassContext context)
            {
                if (context != SimulationPasses.PassContext.TurnEnd || !ReferenceEquals(__instance, Ancillary.Passes) || !TradeNetwork.Enabled)
                {
                    return;
                }
                try
                {
                    Sandbox sandbox = SandboxManager.Sandbox;
                    if (sandbox == null || sandbox.Turn == lastTurn)
                    {
                        return;
                    }
                    lastTurn = sandbox.Turn;
                    TradeNetwork.Snapshot snapshot = TradeNetwork.Refresh();
                    TrackSupply(snapshot, sandbox.Turn);
                    Apply(snapshot, sandbox.Turn);
                    TradeCargo.RefreshTechnologyCosts(snapshot);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Trade network at end of turn: {ex}");
                }
            }
        }

        private static void Apply(TradeNetwork.Snapshot snapshot, int turn)
        {
            var totals = new Dictionary<int, (double money, double influence, double science, int hubs)>();
            foreach (TradeNetwork.Hub hub in snapshot.Hubs)
            {
                MajorEmpire empire = hub.Empire < Sandbox.NumberOfMajorEmpires ? Sandbox.MajorEmpires[hub.Empire] : null;
                Settlement settlement = FindSettlement(empire, hub.Guid);
                if (empire == null || !empire.IsAlive || settlement == null)
                {
                    continue;
                }
                Effects effects = hub.Effects;
                if (effects.Money > 0)
                {
                    empire.DepartmentOfTheTreasury.GainMoney((FixedPoint)(float)effects.Money);
                }
                double growth = (float)settlement.GrowthGain.Value;
                if (effects.GrowthPercent > 0 && growth > 0)
                {
                    empire.DepartmentOfTheInterior.AddGrowthToSettlement(settlement, (FixedPoint)(float)(growth * effects.GrowthPercent), raiseSimulationEvents: false);
                }
                if (effects.Influence > 0)
                {
                    empire.DepartmentOfCulture.GainInfluence((FixedPoint)(float)effects.Influence);
                }
                double science = (float)settlement.ScienceNet.Value * effects.SciencePercent;
                if (science > 0)
                {
                    empire.DepartmentOfScience.GainResearch((FixedPoint)(float)science, raiseSimulationEvents: false);
                }
                totals.TryGetValue(hub.Empire, out var sum);
                totals[hub.Empire] = (sum.money + effects.Money, sum.influence + effects.Influence, sum.science + science, sum.hubs + 1);
                TryEpidemic(empire, settlement, hub, turn);
            }
            foreach (TradeNetwork.GrandRoute route in snapshot.GrandRoutes)
            {
                foreach (int end in new[] { route.Left, route.Right })
                {
                    if (end >= 0 && end < Sandbox.NumberOfMajorEmpires && Sandbox.MajorEmpires[end] is MajorEmpire empire && empire.IsAlive)
                    {
                        int era = Math.Max(1, empire.DepartmentOfDevelopment.CurrentEraIndex);
                        empire.DepartmentOfDevelopment.GainFame((FixedPoint)(float)(EconomyConfig.GrandRouteFame.Value * era), raiseSimulationEvents: false);
                    }
                }
            }
            if (totals.Count > 0)
            {
                Plugin.Log.LogInfo("Trade hubs: " + string.Join("; ", totals.OrderBy(t => t.Key).Select(t =>
                    $"E{t.Key} {t.Value.hubs} hub(s) money +{t.Value.money:0} influence +{t.Value.influence:0} science +{t.Value.science:0}"))
                    + $" · grand routes {snapshot.GrandRoutes.Count} · spies shielded so far {ShieldedSpies}");
            }
        }

        internal static Settlement FindSettlement(MajorEmpire empire, ulong guid)
        {
            if (empire == null)
            {
                return null;
            }
            for (int i = 0; i < empire.Settlements.Count; i++)
            {
                Settlement settlement = empire.Settlements[i];
                if (settlement != null && (ulong)settlement.GUID == guid)
                {
                    return settlement;
                }
            }
            return null;
        }

        private static void TryEpidemic(MajorEmpire empire, Settlement settlement, TradeNetwork.Hub hub, int turn)
        {
            string forced = ForcedEpidemic;
            bool force = forced != null && hub.Name.Equals(forced, StringComparison.OrdinalIgnoreCase);
            if (!force && (hub.Effects.EpidemicChance <= 0 || (lastEpidemic.TryGetValue(hub.Guid, out int last) && turn - last < EpidemicCooldown)))
            {
                return;
            }
            // Deterministic draw per city and turn (the same save replays the same way).
            ulong mix = hub.Guid * 0x9E3779B97F4A7C15UL ^ (ulong)(turn * 2654435761L);
            mix ^= mix >> 29;
            double roll = (mix % 10000) / 10000.0;
            if (!force && roll >= hub.Effects.EpidemicChance)
            {
                return;
            }
            if (force)
            {
                ForcedEpidemic = null;
            }
            int population = (int)(float)settlement.TotalPopulation;
            if (population <= 2)
            {
                return;
            }
            int lost = population >= 14 ? 2 : 1;
            int removed = empire.DepartmentOfTheInterior.AddOrRemovePopulationToSettlement((FixedPoint)(-lost), settlement, raiseSimulationEvents: true);
            empire.DepartmentOfTheInterior.NotifyPopulationVariation(settlement, -lost);
            lastEpidemic[hub.Guid] = turn;
            var list = new List<(int, int, string, int)>(Epidemics.Where(e => turn - e.turn < 10)) { (turn, hub.Empire, hub.Name, lost) };
            Epidemics = list;
            Plugin.Log.LogInfo($"Trade network: epidemic in {hub.Name} (E{hub.Empire}, hub level {hub.Level}, stability {hub.Stability:0}, growth {hub.Growth:0.0}): -{lost} population (game returned {removed}).");
            if (empire.Index == SandboxManager.Sandbox?.LocalEmpireIndex)
            {
                PendingNotices.Enqueue(L.F("Epidemia em {0}: os viajantes das rotas trouxeram doença e a cidade perdeu {1} de população. Cidades estáveis e bem alimentadas resistem melhor.", hub.Name, lost));
            }
        }

        /// <summary>Messages for the player, shown on the main thread (TradeHubLabels).</summary>
        internal static readonly System.Collections.Concurrent.ConcurrentQueue<string> PendingNotices = new System.Collections.Concurrent.ConcurrentQueue<string>();

        // ---------------- Spies ----------------

        /// <summary>
        /// Foreign spies in an unstable hub stay hidden longer: before the game drains their stealth, part of that drain is
        /// given back (HubSpyMax at most, only where the hub's instability lets them blend into the crowd).
        /// </summary>
        [HarmonyPatch(typeof(StealthAncillary), "NewTurnBegin_UpdateStealth")]
        private static class StealthPatch
        {
            private static void Prefix(StealthAncillary __instance)
            {
                if (!TradeNetwork.Enabled)
                {
                    return;
                }
                try
                {
                    TradeNetwork.Snapshot snapshot = TradeNetwork.Current;
                    if (snapshot.Hubs.Count == 0 || !snapshot.Hubs.Any(h => h.Effects.SpyShield > 0))
                    {
                        return;
                    }
                    var shieldByTerritory = new Dictionary<int, (double shield, int owner)>();
                    foreach (TradeNetwork.Hub hub in snapshot.Hubs.Where(h => h.Effects.SpyShield > 0))
                    {
                        foreach (int territory in hub.Territories)
                        {
                            shieldByTerritory[territory] = (hub.Effects.SpyShield, hub.Empire);
                        }
                    }
                    foreach (MajorEmpire empire in Sandbox.MajorEmpires)
                    {
                        ReferenceCollection<Army> armies = empire.Armies;
                        for (int j = 0; j < armies.Count; j++)
                        {
                            Army army = armies[j];
                            if (army == null || army.StealthMax.Value == FixedPoint.Zero)
                            {
                                continue;
                            }
                            int tile = army.WorldPosition.ToTileIndex();
                            int territory = Sandbox.World.TileInfo[tile].TerritoryIndex;
                            if (!shieldByTerritory.TryGetValue(territory, out var shield) || shield.owner == empire.Index)
                            {
                                continue;
                            }
                            if ((StealthAncillary.GetStealthRegenFailureFlag(army, ignoreDistrict: false) & StealthRegenFailureFlags.UnderDetection) == 0)
                            {
                                continue;
                            }
                            FixedPoint detection = __instance.GetBestStealthDetection(empire, territory)?.DetectionForTerritory.Value ?? FixedPoint.Zero;
                            if (detection <= FixedPoint.Zero)
                            {
                                continue;
                            }
                            FixedPoint refund = detection * (FixedPoint)(float)shield.shield;
                            for (int k = 0; k < army.Units.Count; k++)
                            {
                                Unit unit = army.Units[k];
                                unit.StealthValue.Value = FixedPoint.Min(unit.StealthValue.Value + refund, unit.StealthMax.Value);
                            }
                            System.Threading.Interlocked.Increment(ref ShieldedSpies);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Trade hubs (spies): {ex.Message}");
                }
            }
        }

        /// <summary>How many times a spy was shielded by a hub (dev "game trade").</summary>
        internal static int ShieldedSpies;
    }
}
