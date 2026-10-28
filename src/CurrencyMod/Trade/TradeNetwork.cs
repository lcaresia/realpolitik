using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using Territory = Amplitude.Mercury.Simulation.Territory;
using SettlementStatuses = Amplitude.Mercury.Data.Simulation.SettlementStatuses;

namespace CurrencyMod
{
    /// <summary>
    /// The trade network of the world (melhorias-jogando item 36), read from the game's active routes:
    /// - trade hubs: cities whose territories the routes cross or end in, with a level from the number of routes;
    /// - grand routes: long routes across many lands, with a name, drawn thick and golden on the route map;
    /// - cargo: what each route carries besides goods (the seller's faith and culture on the territories it crosses,
    ///   and its technology for the buyer).
    /// Built on the sandbox thread (Refresh) and published whole as an immutable snapshot that the other threads only read.
    /// Effects live in TradeHubs and TradeCargo; this class only describes the network.
    /// </summary>
    internal static class TradeNetwork
    {
        internal const int MaxLevel = 4;

        internal sealed class Hub
        {
            public ulong Guid;
            public int Empire;
            public string Name;
            public int Tile = -1;
            public int[] Territories = new int[0];
            public int Routes;
            public int Goods;
            public int GrandRoutes;
            public int Level;
            public double Stability;
            public double Growth;
            public int Population;
            /// <summary>Share of the city's territories under another faith or culture (minorities).</summary>
            public double ForeignShare;
            public TradeHubs.Effects Effects = new TradeHubs.Effects();
        }

        internal sealed class GrandRoute
        {
            public int Left;
            public int Right;
            public string Name;
            /// <summary>Just the title ("Rota do Ouro"): what the map shows; the full name goes to the hover and the legend.</summary>
            public string Title;
            public int Territories;
            public int Nations;
            public int Goods;
            public int NameTile = -1;
            /// <summary>Same middle tile as RouteVisuals (to know whether the view shows the route).</summary>
            public int MidTile = -1;
            public List<int> Transit = new List<int>();
            public List<string> HubNames = new List<string>();
            public List<int> GoodsTypes = new List<int>();
            public HashSet<ulong> Settlements = new HashSet<ulong>();
            /// <summary>Trade tile of each path node, in order (the name goes on the explored stretch nearest the middle).</summary>
            public List<int> PathTiles = new List<int>();
            /// <summary>"Cartago–Tebas": the two capitals.</summary>
            public string Ends;
        }

        /// <summary>A seller's goods passing through a territory (weight already applied: full on the buyer's land, half elsewhere).</summary>
        internal struct Cargo
        {
            public int Seller;
            public double Weight;
        }

        internal sealed class Snapshot
        {
            public int Turn = -1;
            public List<Hub> Hubs = new List<Hub>();
            public Dictionary<ulong, Hub> HubByGuid = new Dictionary<ulong, Hub>();
            public List<GrandRoute> GrandRoutes = new List<GrandRoute>();
            /// <summary>(left &lt;&lt; 16 | right) of each grand route, for the route map.</summary>
            public HashSet<int> GrandPairs = new HashSet<int>();
            public Dictionary<int, List<Cargo>> CargoByTerritory = new Dictionary<int, List<Cargo>>();
            /// <summary>Per major empire: bits of the major empires it trades with (active route carrying goods).</summary>
            public int[] PartnerBits = new int[0];
            /// <summary>Per major empire and resource: accesses bought through routes, and from whom (for the supply crisis).</summary>
            public Dictionary<long, Dictionary<int, int>> BoughtFrom = new Dictionary<long, Dictionary<int, int>>();
        }

        private static volatile Snapshot current = new Snapshot();

        internal static Snapshot Current => current;

        internal static bool Enabled => EconomyConfig.EnableTradeNetwork.Value && !Multiplayer.MpSession.EconomyBlocked;

        private static int[] levelRoutes;
        private static string levelRoutesText;

        /// <summary>Routes needed for levels 1..4 (from the configuration; bad values fall back to the default).</summary>
        internal static int[] LevelRoutes()
        {
            string text = EconomyConfig.HubLevelRoutes.Value;
            if (levelRoutes == null || text != levelRoutesText)
            {
                int[] parsed = (text ?? string.Empty).Split(',').Select(s => int.TryParse(s.Trim(), out int n) ? n : -1).ToArray();
                levelRoutes = parsed.Length == MaxLevel && parsed.All(n => n > 0) ? parsed : new[] { 2, 4, 7, 11 };
                levelRoutesText = text;
            }
            return levelRoutes;
        }

        internal static int LevelFor(int score)
        {
            int[] needed = LevelRoutes();
            int level = 0;
            for (int i = 0; i < needed.Length; i++)
            {
                if (score >= needed[i])
                {
                    level = i + 1;
                }
            }
            return level;
        }

        /// <summary>Player-facing name of a hub level.</summary>
        internal static string LevelName(int level)
        {
            switch (level)
            {
                case 1: return L.T("Mercado");
                case 2: return L.T("Entreposto");
                case 3: return L.T("Grande entreposto");
                case 4: return L.T("Metrópole comercial");
                default: return string.Empty;
            }
        }

        /// <summary>Rebuilds the snapshot when the turn changed (sandbox thread). Cheap enough to call from any sandbox hook.</summary>
        internal static Snapshot EnsureFresh()
        {
            Snapshot known = current;
            Amplitude.Mercury.Sandbox.Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return known;
            }
            return known.Turn == sandbox.Turn ? known : Refresh();
        }

        /// <summary>
        /// Reads the network from the game's routes (sandbox thread) and, with <paramref name="publish"/>, makes it the one the
        /// simulation reads. Only the simulation's own passes publish: the map drawing refreshes whenever it redraws, which
        /// differs on each machine, and in multiplayer the simulation must read the same snapshot on every peer.
        /// </summary>
        internal static Snapshot Refresh(bool publish = true)
        {
            var snapshot = new Snapshot { Turn = SandboxManager.Sandbox?.Turn ?? -1 };
            int majors = Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires;
            snapshot.PartnerBits = new int[majors];
            ExternalTradeRelation[] relations = Amplitude.Mercury.Sandbox.Sandbox.TradeController?.ExternalTradeRelations;
            if (relations == null || !Enabled)
            {
                if (publish)
                {
                    current = snapshot;
                }
                return snapshot;
            }
            HashSet<ExternalTradeRelation> grandSet = PickGrandRoutes(relations);
            var settlementRoutes = new Dictionary<ulong, (Settlement settlement, int routes, int goods, int grand)>();
            foreach (ExternalTradeRelation relation in relations)
            {
                if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active)
                {
                    continue;
                }
                ExternalTradeExchange leftBuys = relation.LeftTradeExchange.Entity;
                ExternalTradeExchange rightBuys = relation.RightTradeExchange.Entity;
                int leftGoods = TradeBlockade.ResourcesBought(leftBuys);
                int rightGoods = TradeBlockade.ResourcesBought(rightBuys);
                int goods = leftGoods + rightGoods;
                if (goods <= 0)
                {
                    continue;
                }
                int left = relation.LeftEmpireIndex;
                int right = relation.RightEmpireIndex;
                if (left >= 0 && left < majors && right >= 0 && right < majors)
                {
                    snapshot.PartnerBits[left] |= 1 << right;
                    snapshot.PartnerBits[right] |= 1 << left;
                }
                RecordBought(snapshot, leftBuys, majors);
                RecordBought(snapshot, rightBuys, majors);

                // Territories and the lands they belong to.
                var territories = new List<Territory>();
                var owners = new HashSet<int>();
                for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                {
                    Territory territory = relation.PathAsTerritories[i];
                    if (territory == null || territories.Contains(territory))
                    {
                        continue;
                    }
                    territories.Add(territory);
                    int owner = OwnerIndex(territory.Index);
                    if (owner >= 0)
                    {
                        owners.Add(owner);
                    }
                }
                owners.Add(left);
                owners.Add(right);
                bool grand = grandSet.Contains(relation);
                if (grand)
                {
                    snapshot.GrandPairs.Add((left << 16) | right);
                    snapshot.GrandRoutes.Add(DescribeGrand(relation, territories, owners, goods, leftBuys, rightBuys));
                }

                // Hubs: each city counts the route once, however many of its territories the route crosses.
                var touched = new HashSet<ulong>();
                foreach (Territory territory in territories)
                {
                    Settlement settlement = SettlementOf(territory);
                    if (settlement == null || settlement.SettlementStatus != SettlementStatuses.City || !(settlement.Empire.Entity is MajorEmpire))
                    {
                        continue;
                    }
                    ulong guid = (ulong)settlement.GUID;
                    if (!touched.Add(guid))
                    {
                        continue;
                    }
                    if (grand)
                    {
                        snapshot.GrandRoutes[snapshot.GrandRoutes.Count - 1].Settlements.Add(guid);
                    }
                    settlementRoutes.TryGetValue(guid, out var known);
                    settlementRoutes[guid] = (settlement, known.routes + 1, known.goods + goods, known.grand + (grand ? 1 : 0));
                }

                // Cargo: each side carries the seller's faith and culture into the buyer's land (and, weaker, the lands on the way).
                AddCargo(snapshot, territories, leftBuys, leftGoods);
                AddCargo(snapshot, territories, rightBuys, rightGoods);
            }

            // Levels are relative (melhorias item 37): with fixed thresholds almost every capital became a Metrópole and
            // the badge stopped meaning anything. The thresholds still set the ceiling; the rank among the busiest cities
            // decides: Metrópole only for the top 10%, Grande entreposto up to 30%, Entreposto up to 60%.
            var ranked = settlementRoutes.Values.Where(v => LevelFor(v.routes + v.grand) > 0).OrderByDescending(v => v.routes + v.grand).ToList();
            for (int rank = 0; rank < ranked.Count; rank++)
            {
                var item = ranked[rank];
                double share = (rank + 1) / (double)ranked.Count;
                int byRank = rank == 0 || share <= 0.10 ? 4 : share <= 0.30 ? 3 : share <= 0.60 ? 2 : 1;
                int level = Math.Min(LevelFor(item.routes + item.grand), byRank);
                if (level <= 0)
                {
                    continue;
                }
                Hub hub = DescribeHub(item.settlement, item.routes, item.goods, item.grand, level);
                snapshot.Hubs.Add(hub);
                snapshot.HubByGuid[hub.Guid] = hub;
            }
            NameGrandRoutes(snapshot.GrandRoutes);
            foreach (GrandRoute route in snapshot.GrandRoutes)
            {
                // Hubs along each grand route (for the tooltip and the dossier).
                route.HubNames = snapshot.Hubs.Where(h => route.Settlements.Contains(h.Guid))
                    .OrderByDescending(h => h.Level).Select(h => h.Name).Take(4).ToList();
            }
            TradeHubs.Compute(snapshot);
            if (publish)
            {
                current = snapshot;
            }
            return snapshot;
        }

        /// <summary>
        /// Grand routes: the longest GrandRouteTopShare of the active routes carrying goods (at least one), counted in
        /// territories crossed, then lands crossed, then goods; never shorter than GrandRouteMinTerritories. Relative to
        /// the match, so there are always a few, early or late in the game, on small or huge maps.
        /// </summary>
        private static HashSet<ExternalTradeRelation> PickGrandRoutes(ExternalTradeRelation[] relations)
        {
            int minTerritories = Math.Max(2, EconomyConfig.GrandRouteMinTerritories.Value);
            var candidates = new List<(ExternalTradeRelation relation, int territories, int lands, int goods)>();
            int routes = 0;
            foreach (ExternalTradeRelation relation in relations)
            {
                if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active)
                {
                    continue;
                }
                int goods = TradeBlockade.ResourcesBought(relation.LeftTradeExchange.Entity) + TradeBlockade.ResourcesBought(relation.RightTradeExchange.Entity);
                if (goods <= 0)
                {
                    continue;
                }
                routes++;
                var territories = new HashSet<int>();
                var lands = new HashSet<int> { relation.LeftEmpireIndex, relation.RightEmpireIndex };
                for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                {
                    Territory territory = relation.PathAsTerritories[i];
                    if (territory == null || !territories.Add(territory.Index))
                    {
                        continue;
                    }
                    int owner = OwnerIndex(territory.Index);
                    if (owner >= 0)
                    {
                        lands.Add(owner);
                    }
                }
                if (territories.Count >= minTerritories)
                {
                    candidates.Add((relation, territories.Count, lands.Count, goods));
                }
            }
            int take = Math.Max(1, (int)Math.Round(routes * Math.Max(0, EconomyConfig.GrandRouteTopShare.Value)));
            return new HashSet<ExternalTradeRelation>(candidates
                .OrderByDescending(c => c.territories).ThenByDescending(c => c.lands).ThenByDescending(c => c.goods)
                .ThenBy(c => c.relation.LeftEmpireIndex).ThenBy(c => c.relation.RightEmpireIndex)
                .Take(take).Select(c => c.relation));
        }

        /// <summary>The territory's owner (major or minor empire), or -1 when unclaimed (the game stores "nobody" as 255).</summary>
        internal static int OwnerIndex(int territory)
        {
            int owner = Amplitude.Mercury.Sandbox.Sandbox.World.TerritoryInfo.Data[territory].EmpireIndex;
            return owner >= 0 && owner < Amplitude.Mercury.Sandbox.Sandbox.NumberOfEmpires ? owner : -1;
        }

        internal static Settlement SettlementOf(Territory territory) => territory?.Region.Entity?.Settlement.Entity;

        private static void RecordBought(Snapshot snapshot, ExternalTradeExchange exchange, int majors)
        {
            Empire buyer = exchange?.BuyerEmpire.Entity;
            Empire seller = exchange?.SellerEmpire.Entity;
            if (buyer == null || seller == null || buyer.Index >= majors)
            {
                return;
            }
            for (int type = 0; type < 64; type++)
            {
                int count = (int)(float)exchange.GetBoughtResourceAccessCount((Amplitude.Mercury.Data.Simulation.ResourceType)type);
                if (count <= 0)
                {
                    continue;
                }
                long key = ((long)buyer.Index << 8) | (uint)type;
                if (!snapshot.BoughtFrom.TryGetValue(key, out Dictionary<int, int> sellers))
                {
                    sellers = new Dictionary<int, int>();
                    snapshot.BoughtFrom[key] = sellers;
                }
                sellers.TryGetValue(seller.Index, out int known);
                sellers[seller.Index] = known + count;
            }
        }

        private static void AddCargo(Snapshot snapshot, List<Territory> territories, ExternalTradeExchange exchange, int goods)
        {
            Empire buyer = exchange?.BuyerEmpire.Entity;
            Empire seller = exchange?.SellerEmpire.Entity;
            if (buyer == null || !(seller is MajorEmpire) || goods <= 0)
            {
                return;
            }
            foreach (Territory territory in territories)
            {
                int owner = OwnerIndex(territory.Index);
                if (owner < 0 || owner == seller.Index)
                {
                    continue; // unclaimed land cannot be influenced; the seller's own land needs no push
                }
                double weight = owner == buyer.Index ? goods : goods * 0.5;
                if (!snapshot.CargoByTerritory.TryGetValue(territory.Index, out List<Cargo> list))
                {
                    list = new List<Cargo>();
                    snapshot.CargoByTerritory[territory.Index] = list;
                }
                list.Add(new Cargo { Seller = seller.Index, Weight = weight });
            }
        }

        private static Hub DescribeHub(Settlement settlement, int routes, int goods, int grand, int level)
        {
            var hub = new Hub
            {
                Guid = (ulong)settlement.GUID,
                Empire = settlement.Empire.Entity.Index,
                Name = Diplomacy.GameAccess.Clean(settlement.EntityName.ToString()),
                Tile = settlement.WorldPosition.ToTileIndex(),
                Routes = routes,
                Goods = goods,
                GrandRoutes = grand,
                Level = level,
                Stability = (float)settlement.PublicOrderCurrent.Value,
                Growth = (float)settlement.GrowthNet.Value,
                Population = (int)(float)settlement.TotalPopulation,
            };
            Region region = settlement.Region.Entity;
            var indices = new List<int>();
            int foreign = 0;
            MajorEmpire owner = settlement.Empire.Entity as MajorEmpire;
            int religion = owner?.Religion.Entity != null ? owner.Religion.Entity.Index : -1;
            if (region != null)
            {
                for (int i = 0; i < region.Territories.Count; i++)
                {
                    Territory territory = region.Territories[i];
                    if (territory == null)
                    {
                        continue;
                    }
                    indices.Add(territory.Index);
                    if (IsForeign(territory.Index, owner?.Index ?? -1, religion))
                    {
                        foreign++;
                    }
                }
            }
            hub.Territories = indices.ToArray();
            hub.ForeignShare = indices.Count > 0 ? (double)foreign / indices.Count : 0;
            return hub;
        }

        /// <summary>The territory follows another faith or another culture than its owner's.</summary>
        private static bool IsForeign(int territory, int empire, int religion)
        {
            try
            {
                ReligionManager religions = Amplitude.Mercury.Sandbox.Sandbox.ReligionManager;
                if (religion >= 0 && religions != null && territory < religions.BeliefInfoPerTerritory.Length)
                {
                    short faith = religions.BeliefInfoPerTerritory.Data[territory].InfluencerBeliefIndex;
                    if (faith >= 0 && faith != religion)
                    {
                        return true;
                    }
                }
                CultureManager cultures = Amplitude.Mercury.Sandbox.Sandbox.CultureManager;
                if (empire >= 0 && cultures != null && territory < cultures.BeliefInfoPerTerritory.Length)
                {
                    short culture = cultures.BeliefInfoPerTerritory.Data[territory].InfluencerBeliefIndex;
                    if (culture >= 0 && culture != empire)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        // ---------------- Grand routes ----------------

        private static GrandRoute DescribeGrand(ExternalTradeRelation relation, List<Territory> territories, HashSet<int> owners, int goods,
            ExternalTradeExchange leftBuys, ExternalTradeExchange rightBuys)
        {
            int left = relation.LeftEmpireIndex;
            int right = relation.RightEmpireIndex;
            var route = new GrandRoute
            {
                Left = left,
                Right = right,
                Territories = territories.Count,
                Nations = owners.Count,
                Goods = goods,
                Transit = owners.Where(o => o != left && o != right && o < Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires).ToList(),
            };
            int nodes = relation.PathAsPathNode.Length;
            if (nodes > 0)
            {
                // A third of the way: the middle is where the toll label goes.
                int node = relation.PathAsPathNode.Data[Math.Max(0, nodes / 3)].TerritoryNodeIndex;
                route.NameTile = Amplitude.Mercury.Sandbox.Sandbox.TerritoryPathfindManager.TerritoryNodes[node].GetCurrentTradeTileIndex();
                for (int i = 0; i < nodes; i++)
                {
                    int step = relation.PathAsPathNode.Data[i].TerritoryNodeIndex;
                    route.PathTiles.Add(Amplitude.Mercury.Sandbox.Sandbox.TerritoryPathfindManager.TerritoryNodes[step].GetCurrentTradeTileIndex());
                }
                int middle = relation.PathAsPathNode.Data[nodes / 2].TerritoryNodeIndex;
                route.MidTile = Amplitude.Mercury.Sandbox.Sandbox.TerritoryPathfindManager.TerritoryNodes[middle].GetCurrentTradeTileIndex();
            }
            foreach (ExternalTradeExchange exchange in new[] { leftBuys, rightBuys })
            {
                for (int type = 0; exchange != null && type < 64; type++)
                {
                    if ((float)exchange.GetBoughtResourceAccessCount((Amplitude.Mercury.Data.Simulation.ResourceType)type) > 0 && !route.GoodsTypes.Contains(type))
                    {
                        route.GoodsTypes.Add(type);
                    }
                }
            }
            route.Ends = CapitalName(relation.LeftEmpire.Entity) + "–" + CapitalName(relation.RightEmpire.Entity);
            return route;
        }

        private static readonly string[] Titles =
        {
            "Estrada Real", "Grande Rota", "Rota das Caravanas", "Caminho dos Mercadores", "Rota do Ouro", "Via Imperial",
            "Rota da Seda", "Rota do Incenso", "Rota das Especiarias", "Caminho dos Reis", "Rota do Sal", "Estrada dos Viajantes",
        };

        /// <summary>
        /// "Estrada Real Cartago–Tebas": a title picked by the pair and the two capitals. Two grand routes never share a
        /// title while there are titles left (the next free one is taken), so each reads as a different road on the map.
        /// </summary>
        private static void NameGrandRoutes(List<GrandRoute> routes)
        {
            var used = new HashSet<int>();
            foreach (GrandRoute route in routes.OrderBy(r => r.Left).ThenBy(r => r.Right))
            {
                int pick = Math.Abs((route.Left * 31 + route.Right * 17) % Titles.Length);
                for (int tries = 0; tries < Titles.Length && used.Contains(pick); tries++)
                {
                    pick = (pick + 1) % Titles.Length;
                }
                used.Add(pick);
                route.Title = L.T(Titles[pick]);
                route.Name = $"{route.Title} {route.Ends}";
            }
        }

        private static string CapitalName(Empire empire)
        {
            Settlement capital = (empire as MajorEmpire)?.Capital.Entity;
            if (capital == null && empire != null && empire.Settlements.Count > 0)
            {
                capital = empire.Settlements[0];
            }
            return capital != null ? Diplomacy.GameAccess.Clean(capital.EntityName.ToString()) : "?";
        }
    }
}
