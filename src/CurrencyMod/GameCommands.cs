using System;
using System.Linq;
using System.Text;
using Amplitude.Framework;
using Amplitude.Mercury.Game;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Runtime;
using Amplitude.Mercury.Sandbox;
using Math = System.Math;

namespace CurrencyMod
{
    /// <summary>
    /// Dev commands to test without clicking (dev\cmd.txt): load and save games and end the turn.
    /// They do the same as the saves screen and the end-turn button.
    ///   game state               game state, turn and whether the turn can be ended
    ///   game saves [rescan]      lists the saves (newest first); "rescan" rereads the saves folder
    ///   game load [title]        loads the save by title; without a title, the most recent
    ///   game save &lt;title&gt;       saves the game with that title
    ///   game turn                ends the turn (OrderEmpireReady), like the button
    ///   game proposals           pending treaties and agreements between the player and each nation
    ///   game answer &lt;E#&gt; accept|ignore   answers, for the player, a pending proposal from that nation
    ///   game propose &lt;E#&gt; economic|information|cultural|military|alliance|peace   formal proposal by the player (tests)
    ///   game crises [E#]         grievances (G = in-game index), demands and crisis of all relations (or of one nation)
    ///   game demand &lt;E#&gt; all|G#...   the player turns grievances into demands against the nation (tests)
    ///   game demands &lt;E#&gt; accept|refuse|stall|withdraw   the player answers the demands (or withdraws their own)
    ///   game stuck [n]           why end of turn does not finish (pending items, battles, decisions) and the last n orders
    ///   game battle [auto|retreat]   ongoing battles; answers, for the player, those awaiting their confirmation
    /// The full list is in docs\dev-commands.md (section `game`).
    /// </summary>
    internal static class GameCommands
    {
        /// <summary>
        /// "game outpost E# T#": founds an outpost for the empire in a free territory, using the game's own editor order
        /// (EditorOrderCreateCampAt). Used to build test scenarios (e.g. a route crossing two territories of the
        /// same owner, for the inner-outpost toll).
        /// </summary>
        /// <summary>
        /// "game trade [refresh]": the trade network (hubs with level, effects and risks; grand routes; supply crises).
        /// "game trade cargo T#": faith and culture the routes add in a territory. "game trade epidemic City": the next end
        /// of turn strikes that hub (test). "game trade labels": the route map's network labels.
        /// </summary>
        private static string TradeCommand(string args)
        {
            string[] words = args.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string verb = words.Length > 0 ? words[0].ToLowerInvariant() : string.Empty;
            switch (verb)
            {
                case "cargo":
                    return words.Length > 1 && int.TryParse(words[1].TrimStart('T', 't'), out int territory) ? TradeCargo.Describe(territory) : "usage: game trade cargo T#";
                case "epidemic":
                    if (words.Length < 2)
                    {
                        return "usage: game trade epidemic <city name>";
                    }
                    TradeHubs.ForcedEpidemic = words[1].Trim();
                    return $"the next end of turn strikes {TradeHubs.ForcedEpidemic} (if it is a hub)";
                case "hover":
                    RouteLabels.ForcedHover = words.Length > 1 && words[1].Trim() != "off" ? words[1].Trim() : null;
                    return RouteLabels.ForcedHover != null ? $"hover panel forced on the label with '{RouteLabels.ForcedHover}'" : "hover back to the mouse";
                case "labels":
                {
                    var labels = RouteVisuals.Labels;
                    string drawn = $"in trade view {RouteLabels.InView} · drawn {RouteLabels.Drawn} · off screen or under a panel {RouteLabels.Skipped}\n";
                    return labels == null || labels.Count == 0 ? drawn + "no labels (open the trade view)"
                        : drawn + string.Join("\n", labels.Select(l => $"{l.Kind} tile {l.Tile}: {l.Text}{(l.Subtitle != null ? " / " + l.Subtitle : string.Empty)}"));
                }
            }
            TradeNetwork.Snapshot network = verb == "refresh" ? TradeNetwork.Refresh() : TradeNetwork.Current;
            var text = new StringBuilder($"trade network (turn {network.Turn}, enabled {TradeNetwork.Enabled}, levels at {string.Join("/", TradeNetwork.LevelRoutes())} routes): {network.Hubs.Count} hubs, {network.GrandRoutes.Count} grand routes, cargo in {network.CargoByTerritory.Count} territories, spies shielded {TradeHubs.ShieldedSpies}\n");
            foreach (TradeNetwork.Hub hub in network.Hubs.OrderByDescending(h => h.Level).ThenByDescending(h => h.Routes))
            {
                TradeHubs.Effects e = hub.Effects;
                text.AppendLine($"  E{hub.Empire} {hub.Name} [T{string.Join(",T", hub.Territories)}] L{hub.Level} routes {hub.Routes} (grand {hub.GrandRoutes}) goods {hub.Goods} · stability {hub.Stability:0} growth {hub.Growth:0.0} foreign {hub.ForeignShare:P0}"
                    + $" · money +{e.Money:0.#} growth +{e.GrowthPercent:P0} influence +{e.Influence:0.#} science +{e.SciencePercent:P0}"
                    + $" · crime -{e.CrimeLoss:0.##} minorities -{e.MinorityLoss:0.##} spy {e.SpyShield:P0} epidemic {e.EpidemicChance:P1}");
            }
            if (verb == "routes")
            {
                // Length of every active route (territories, lands), to calibrate what counts as a grand route.
                foreach (var relation in Amplitude.Mercury.Sandbox.Sandbox.TradeController.ExternalTradeRelations)
                {
                    if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active)
                    {
                        continue;
                    }
                    var owners = new System.Collections.Generic.HashSet<int> { relation.LeftEmpireIndex, relation.RightEmpireIndex };
                    for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                    {
                        int owner = TradeNetwork.OwnerIndex(relation.PathAsTerritories[i].Index);
                        if (owner >= 0)
                        {
                            owners.Add(owner);
                        }
                    }
                    text.AppendLine($"  route E{relation.LeftEmpireIndex}↔E{relation.RightEmpireIndex}: {relation.PathAsTerritories.Count} territories, {owners.Count} lands, goods {TradeBlockade.ResourcesBought(relation.LeftTradeExchange.Entity) + TradeBlockade.ResourcesBought(relation.RightTradeExchange.Entity)}");
                }
            }
            foreach (TradeNetwork.GrandRoute route in network.GrandRoutes)
            {
                text.AppendLine($"  grand: {route.Name} (E{route.Left}↔E{route.Right}) {route.Territories} territories, {route.Nations} nations, goods {route.Goods}, name tile {route.NameTile}, hubs {string.Join(", ", route.HubNames)}");
            }
            for (int i = 0; i < Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires; i++)
            {
                if (TradeHubs.SupplyCrisis(i, out string what))
                {
                    text.AppendLine($"  supply crisis E{i}: {what}");
                }
                double factor = TradeHubs.DebtInflationFactor(i);
                if (factor > 1.0001)
                {
                    text.AppendLine($"  E{i} debt inflation ×{factor:0.00} (only applies with debt)");
                }
            }
            return text.ToString();
        }

        private static string SpawnCamp(string args)
        {
            string[] items = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (items.Length < 2 || !int.TryParse(items[0].TrimStart('E', 'e'), out int owner) || !int.TryParse(items[1].TrimStart('T', 't'), out int territory))
            {
                return "usage: game outpost E# T#";
            }
            Amplitude.Mercury.Simulation.World world = Amplitude.Mercury.Sandbox.Sandbox.World;
            if (world == null || owner < 0 || owner >= Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires)
            {
                return "error: no game or invalid empire";
            }
            TileInfo[] tiles = world.TileInfo.Data;
            var terrains = Amplitude.Mercury.Simulation.World.Tables.TerrainTypeDefinitions;
            for (int tile = 0; tile < tiles.Length; tile++)
            {
                var terrain = terrains[tiles[tile].TerrainType];
                if (tiles[tile].TerritoryIndex != territory || terrain == null || terrain.IsWater() || !terrain.AllowCityConstruction
                    || Amplitude.Mercury.Simulation.World.Maps.ArmyMap[tile].IsValid || Amplitude.Mercury.Simulation.World.Maps.DistrictMap[tile].IsValid)
                {
                    continue;
                }
                SandboxManager.PostOrder(new Amplitude.Mercury.Interop.EditorOrderCreateCampAt { EmpireIndex = owner, CampTileIndex = tile });
                return $"ok: outpost of E{owner} requested on tile {tile} of T{territory} (check with 'game routes detail')";
            }
            return $"error: no free land tile in T{territory}";
        }

        /// <summary>
        /// "game spy E# T# [unit]": creates a spy army for that empire on a free land tile of the territory, using
        /// the game's own debug order (OrderSpawnArmy, the same as the editor's create-unit shortcut).
        /// Used to test letter interception. Default: the medieval spy (LandUnit_Era3_Common_Spies).
        /// </summary>
        private static string SpawnSpy(string args)
        {
            string[] items = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (items.Length < 2 || !int.TryParse(items[0].TrimStart('E', 'e'), out int owner) || !int.TryParse(items[1].TrimStart('T', 't'), out int territory))
            {
                return "usage: game spy E# T# [unit]";
            }
            string unit = items.Length > 2 ? items[2] : "LandUnit_Era3_Common_Spies";
            Amplitude.Mercury.Simulation.World world = Amplitude.Mercury.Sandbox.Sandbox.World;
            if (world == null || owner < 0 || owner >= Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires)
            {
                return "error: no game or invalid empire";
            }
            TileInfo[] tiles = world.TileInfo.Data;
            var terrains = Amplitude.Mercury.Simulation.World.Tables.TerrainTypeDefinitions;
            int chosen = -1;
            for (int tile = 0; tile < tiles.Length && chosen < 0; tile++)
            {
                if (tiles[tile].TerritoryIndex != territory)
                {
                    continue;
                }
                var terrain = terrains[tiles[tile].TerrainType];
                if (terrain == null || terrain.IsWater() || !terrain.AllowCityConstruction)
                {
                    continue;
                }
                if (Amplitude.Mercury.Simulation.World.Maps.ArmyMap[tile].IsValid || Amplitude.Mercury.Simulation.World.Maps.DistrictMap[tile].IsValid)
                {
                    continue;
                }
                chosen = tile;
            }
            if (chosen < 0)
            {
                return $"error: no free land tile in T{territory}";
            }
            SandboxManager.PostOrder(new OrderSpawnArmy
            {
                WorldPosition = new Amplitude.Mercury.WorldPosition(chosen),
                UnitDefinitions = new[] { new Amplitude.StaticString(unit) },
            }, owner);
            return $"ok: {unit} of E{owner} requested on tile {chosen} of T{territory} (check with 'ai snapshot' and 'ai espionage')";
        }

        /// <summary>`game report [note]` sends a report like the screen button; `game report status` shows the last one.</summary>
        private static string Report(string rest)
        {
            if (rest == "status")
            {
                return $"report: {Support.ErrorReport.Current}, code {Support.ErrorReport.Code ?? "-"}, error {Support.ErrorReport.Error ?? "-"}, file {Support.ErrorReport.SavedPath ?? "-"}";
            }
            return Support.ErrorReport.Send(string.IsNullOrEmpty(rest) ? "dev channel test" : rest)
                ? "ok: report started (check with 'game report status')"
                : "error: a report is already being sent";
        }

        internal static string Execute(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, 2);
            string command = parts[0].ToLowerInvariant();
            string rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            switch (command)
            {
                case "state":
                    return State();
                case "saves":
                    return rest == "rescan" ? RescanSaves() : ListSaves();
                case "load":
                    return Load(rest);
                case "save":
                    return Save(rest);
                case "turn":
                    return EndTurn();
                case "proposals":
                    return Proposals();
                case "answer":
                    return Answer(rest);
                case "propose":
                    return Propose(rest);
                case "crises":
                    return Crises(rest);
                case "demand":
                    return Demand(rest);
                case "demands":
                    return AnswerDemands(rest);
                case "stuck":
                    return Stuck(rest);
                case "battle":
                    return Battles(rest);
                case "mood":
                    return Moods(rest);
                case "host":
                    return HostOnline(rest);
                case "lobby":
                    return Lobby(rest);
                case "new":
                    return NewGame(rest);
                case "spectator":
                    return Spectator(rest);
                case "report":
                    return Report(rest);
                case "quit":
                    // Quits the game the normal way: killing the process leaves Steam thinking it is still running (AppError_16).
                    UnityEngine.Application.Quit();
                    return "closing the game";
                case "economy":
                    return EconomyAudit();
                case "interest":
                    return ForceInterest(rest);
                case "rpn":
                    return DumpRpn(rest);
                case "erastars":
                    return EraStars();
                case "breakdown":
                    return Breakdown(rest);
                case "interaction":
                    return Interaction(rest);
                case "focus":
                    return FocusField(rest);
                case "spy":
                    return SpawnSpy(rest);
                case "outpost":
                    return SpawnCamp(rest);
                case "camera":
                    return CenterCamera(rest);
                case "zoom":
                    return SetZoom(rest);
                case "surrender":
                    return Surrender(rest);
                case "congress":
                    return Congress(rest);
                case "routes":
                {
                    string what = (rest ?? string.Empty).Trim();
                    if (what == "labels")
                    {
                        // "game routes labels": the value labels from the last render (tile and text), to aim the camera.
                        var labels = RouteVisuals.Labels;
                        return labels == null || labels.Count == 0 ? "no labels (open the trade view)"
                            : string.Join("\n", labels.Select(l => $"tile {l.Tile}: {l.Text}{(l.Paid ? " (paid)" : string.Empty)}"));
                    }
                    if (what.StartsWith("colors"))
                    {
                        // "game routes colors E5": colors the routes as if the player were E5 (test); "game routes colors" goes back to normal.
                        string arg = what.Substring(6).Trim().TrimStart('E', 'e');
                        RouteVisuals.LabelAll = arg == "all";
                        RouteVisuals.ViewAs = int.TryParse(arg, out int viewAs) ? viewAs : -1;
                        return RouteVisuals.ViewAs >= 0 ? $"route colors as seen by E{RouteVisuals.ViewAs} (leave and reenter the trade view)" : "route colors of the local player";
                    }
                    return TradeBlockade.Describe(what == "detail");
                }
                case "trade":
                    return TradeCommand((rest ?? string.Empty).Trim());
                case "civic":
                {
                    // "game civic E# CivicName": an empire's current law (active choice, status, turn of the last change);
                    // checks what happened to a law imposed by the Congress.
                    string[] words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length < 2 || !TryEmpire(words[0], out int holder))
                    {
                        return "usage: game civic E# CivicName";
                    }
                    Amplitude.Mercury.Simulation.DepartmentOfDevelopment development = Sandbox.MajorEmpires[holder].DepartmentOfDevelopment;
                    int civicIndex = development.GetCivicIndex(new Amplitude.StaticString(words[1]));
                    if (civicIndex < 0)
                    {
                        return $"error: civic {words[1]} not found";
                    }
                    Amplitude.Mercury.Simulation.Civic civic = development.Civics.Data[civicIndex];
                    string choice = civic.ActiveChoiceName.ToString();
                    return $"E{holder} {words[1]}: {civic.CivicStatus} · choice {(string.IsNullOrEmpty(choice) ? "none" : choice)} · last change on turn {civic.TurnOnLastChoice}";
                }
                case "influence":
                {
                    // "game influence ±N": the player gains or loses influence now, through the game's order (cheats screen);
                    // tests stocks that change mid-turn, after the turn snapshot.
                    int player = Diplomacy.AiModule.Instance?.PlayerIndex ?? -1;
                    if (player < 0 || !int.TryParse((rest ?? string.Empty).Trim(), out int gain))
                    {
                        return "usage: game influence ±N";
                    }
                    Amplitude.Mercury.Sandbox.SandboxManager.PostOrder(new Amplitude.Mercury.Interop.OrderGainInfluence { Gain = gain }, player);
                    return $"order sent: E{player} influence {(gain >= 0 ? "+" : string.Empty)}{gain} (stock before: {Sandbox.MajorEmpires[player].InfluenceStock.Value:0})";
                }
                case "enact":
                {
                    // "game enact CV# N": the player chooses option N (1-based) of a civic waiting for a choice, through the
                    // game's own order (the civics screen); CV# is the index shown in the dossier.
                    string[] words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length < 2 || !words[0].StartsWith("CV", StringComparison.OrdinalIgnoreCase)
                        || !int.TryParse(words[0].Substring(2), out int civicIndex) || !int.TryParse(words[1], out int option))
                    {
                        return "usage: game enact CV# N";
                    }
                    int player = Diplomacy.AiModule.Instance?.PlayerIndex ?? -1;
                    if (player < 0)
                    {
                        return "error: no game";
                    }
                    Amplitude.Mercury.Simulation.DepartmentOfDevelopment development = Sandbox.MajorEmpires[player].DepartmentOfDevelopment;
                    if (civicIndex < 0 || civicIndex >= development.Civics.Length)
                    {
                        return $"error: civic CV{civicIndex} not found";
                    }
                    Amplitude.Mercury.Simulation.Civic civic = development.Civics.Data[civicIndex];
                    int choices = civic.CivicDefinition?.Choices?.Length ?? 0;
                    if (option < 1 || option > choices)
                    {
                        return $"error: CV{civicIndex} has {choices} option(s)";
                    }
                    Amplitude.Mercury.Sandbox.SandboxManager.PostOrder(new Amplitude.Mercury.Interop.OrderActivateCivic
                    {
                        CivicName = civic.CivicDefinition.Name,
                        ChoiceIndex = option - 1,
                    }, player);
                    return $"order sent: {civic.CivicDefinition.Name} → {civic.CivicDefinition.Choices[option - 1].Name} (check with 'game civic E{player} {civic.CivicDefinition.Name}')";
                }
                case "buy":
                {
                    // "game buy E# luxury|strategic": the player buys all of the empire's resources in that category (the
                    // trade "buy all" button). Creates the route if there is none: tests a new route's path.
                    string[] words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length < 2 || !TryEmpire(words[0], out int seller))
                    {
                        return "usage: game buy E# luxury|strategic [buyer E#]";
                    }
                    var category = words[1].StartsWith("str", StringComparison.OrdinalIgnoreCase) ? Amplitude.Mercury.Data.Simulation.ResourceCategory.Strategic : Amplitude.Mercury.Data.Simulation.ResourceCategory.Luxury;
                    var buy = new Amplitude.Mercury.Interop.OrderBuyAllTradeResourceAccesses { OtherEmpireIndex = seller, ResourceCategory = category };
                    if (words.Length > 2 && TryEmpire(words[2], out int buyer))
                    {
                        SandboxManager.PostOrder(buy, buyer);
                        return $"ok: E{buyer} buys {category} from E{seller} (see the log \"Route E…: StartTrade\" and game routes detail)";
                    }
                    SandboxManager.PostOrder(buy);
                    return $"ok: purchase of {category} from E{seller} sent (see the log \"Route E…: StartTrade\" and game routes detail)";
                }
                case "toll":
                {
                    // "game toll E# E# free|toll|block": the first applies the mode to the second's routes in all of
                    // its territories (tests; the same as the outpost window does for the player).
                    string[] words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    CurrencyWorld world = CurrencyManager.Current;
                    if (words.Length == 2 && words[0] == "weight" && double.TryParse(words[1].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double weight))
                    {
                        // "game toll weight n": PesoPedagioNasRotas (0 = the route ignores the toll and pays; 1 = detours if cheaper).
                        EconomyConfig.TollDetourCost.Value = weight;
                        TradePolicy.PathsDirty = true;
                        return $"ok: PesoPedagioNasRotas = {weight} (routes are recomputed)";
                    }
                    if (words.Length == 2 && words[0] == "transit" && double.TryParse(words[1].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double transit))
                    {
                        // "game toll transit f": RepasseDaManutencao, the share of route upkeep paid to the crossed territories' owners.
                        EconomyConfig.RouteTransitShare.Value = Math.Max(0, Math.Min(1, transit));
                        return $"ok: RepasseDaManutencao = {EconomyConfig.RouteTransitShare.Value}";
                    }
                    if (words.Length >= 6 && words[0] == "test" && TryEmpire(words[1], out int testOwner) && TryEmpire(words[4], out int testLeft) && TryEmpire(words[5], out int testRight)
                        && int.TryParse(words[2].TrimStart('T', 't'), out int freePost) && int.TryParse(words[3].TrimStart('T', 't'), out int tolledPost))
                    {
                        // "game toll test E# T_free T_taxed E_left E_right [price]": step cost of the inner outpost.
                        double testPrice = words.Length > 6 && double.TryParse(words[6], out double p) ? p : 50;
                        return TradeBlockade.SelfTestInnerPost(testOwner, freePost, tolledPost, testLeft, testRight, testPrice);
                    }
                    if (words.Length == 4 && words[0] == "price" && TryEmpire(words[1], out int seller) && TryEmpire(words[2], out int buyer) && world != null
                        && double.TryParse(words[3], out double general))
                    {
                        // "game toll price E# E# n": general price from the first to the second (0 = default), like the diplomacy buttons.
                        lock (CurrencyManager.Lock)
                        {
                            TradePolicy.SetGeneralPrice(world, seller, buyer, general);
                        }
                        return $"ok: E{seller} charges E{buyer} {(general > 0 ? TradePolicy.ClampPrice(general).ToString() : "the default")} per resource at all posts";
                    }
                    if (words.Length < 3 || !TryEmpire(words[0], out int owner) || !TryEmpire(words[1], out int target) || world == null)
                    {
                        return "usage: game toll E# E# free|toll|block | game toll price E# E# n | game toll weight n | game toll test E# T# T# E# E# [price]";
                    }
                    string modeWord = words[2].ToLowerInvariant();
                    TradeMode mode = modeWord.StartsWith("block") ? TradeMode.Block : modeWord.StartsWith("toll") ? TradeMode.Toll : TradeMode.Free;
                    int changed = 0;
                    int territories = 0;
                    lock (CurrencyManager.Lock)
                    {
                        for (int t = 0; t < Sandbox.World.Territories.Length; t++)
                        {
                            if (TradeBlockade.OwnerOf(t)?.Index == owner)
                            {
                                territories++;
                                changed += TradePolicy.SetRule(world, owner, t, target, mode) ? 1 : 0;
                            }
                        }
                    }
                    return $"ok: E{owner} → E{target}: {mode} in {territories} territory(ies) ({changed} change(s))";
                }
                case "expansion":
                {
                    string mode = rest.ToLowerInvariant();
                    if (mode == "on" || mode == "off")
                    {
                        Diplomacy.AiConfig.ExpansionInOldSaves.Value = mode == "on";
                    }
                    return Diplomacy.ExpansionSavePatch.Describe() + (mode == "on" ? " (applies on the next save load)" : string.Empty);
                }
                case "alert":
                    // Visual test of the Central Bank badge: "game alert <text>" lights it up, "game alert off" returns to diagnostics.
                    NativeUI.NativeBankButton.TestAlert = string.IsNullOrEmpty(rest) || rest == "off" ? null : rest;
                    return NativeUI.NativeBankButton.TestAlert == null ? "alert badge follows the diagnostics again" : $"alert badge forced: {rest}";
                case "menu":
                {
                    // Leaves the game to the main menu without saving (like the game's own automation, Operations.cs:441).
                    var runtime = Services.GetService<Amplitude.Framework.Runtime.IRuntimeService>();
                    if (runtime?.Runtime?.FiniteStateMachine == null)
                    {
                        return "error: the game has not finished starting yet";
                    }
                    runtime.Runtime.FiniteStateMachine.PostStateChange(typeof(RuntimeState_OutGame));
                    return "going back to the main menu (without saving)";
                }
                default:
                    return "usage: game state | saves [rescan] | load [title] | save <title> | turn | proposals | answer <E#> accept|ignore"
                        + " | propose <E#> <kind> | crises [E#] | demand <E#> all|G# | demands <E#> accept|refuse|stall|withdraw | stuck [n]"
                        + " | battle [auto|retreat] | mood [E#] | new <2-16> [size]|restore | spectator on|off | quit | menu | economy"
                        + " | interest <E#> <pct>|auto | rpn <name> | breakdown industry|stability|money [n] | interaction ... | focus <path>|off"
                        + " | spy E# T# [unit] | outpost E# T# | camera T#|tile | zoom 0..1 | surrender ... | congress ... | routes [detail|labels|colors]"
                        + " | trade [refresh|routes|cargo T#|epidemic City|labels|hover text|off] | civic E# Civic | enact CV# N | influence ±N | buy E# luxury|strategic [E#] | toll ... | expansion [on|off] | alert <text>|off";
            }
        }

        /// <summary>
        /// "game breakdown industry|stability|money [n]": asks the game for the native breakdown, the same as the tooltips
        /// (industry and stability of the player's n-th city; the empire's money), and writes the lines the way the game
        /// builds them. The answer arrives later, in its own result.txt entry.
        /// </summary>
        private static string Breakdown(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string kind = parts.Length > 0 ? parts[0].ToLowerInvariant() : "industry";
            int cityIndex = parts.Length > 1 && int.TryParse(parts[1], out int parsed) ? parsed : 0;
            if (!Diplomacy.GameAccess.TryGetSession(out _, out _, out int player, allowOnline: true))
            {
                return "error: no game";
            }
            var service = Services.GetService<Amplitude.Mercury.EffectMapper.IEffectMapperService>();
            if (service == null)
            {
                return "error: breakdown service unavailable";
            }

            var header = new StringBuilder();
            Amplitude.Mercury.Simulation.SimulationEntityGUID guid;
            string property;
            string sub = string.Empty;
            if (kind.StartsWith("mon"))
            {
                guid = Snapshots.GameSnapshot.PresentationData.EmpireInfo[player].SimulationEntityGuid;
                property = NativeEffects.MoneyProperty;
                EmpireInfo info = Snapshots.GameSnapshot.PresentationData.EmpireInfo[player];
                header.AppendLine($"money of empire E{player}: shown income {(float)info.MoneyNet:0.#} (game {(float)Sandbox.MajorEmpires[player].MoneyNet.Value:0.#}, projected interest {NativeEffects.ProjectedInterest(Sandbox.MajorEmpires[player]):+0.#;-0.#}, route transit {NativeEffects.ProjectedTransit(Sandbox.MajorEmpires[player]):+0.#;-0.#}, bloc refund {NativeEffects.ProjectedBlocRefund(Sandbox.MajorEmpires[player]):+0.#;-0.#})");
            }
            else
            {
                var cities = new System.Collections.Generic.List<Amplitude.Mercury.Simulation.Settlement>();
                var settlements = Sandbox.MajorEmpires[player].Settlements;
                for (int i = 0; i < settlements.Count; i++)
                {
                    if (settlements[i] != null && settlements[i].SettlementStatus == Amplitude.Mercury.Data.Simulation.SettlementStatuses.City)
                    {
                        cities.Add(settlements[i]);
                    }
                }
                if (cityIndex < 0 || cityIndex >= cities.Count)
                {
                    return $"city {cityIndex} does not exist (the player has {cities.Count} cities, 0 to {cities.Count - 1})";
                }
                Amplitude.Mercury.Simulation.Settlement city = cities[cityIndex];
                guid = city.GUID;
                EconomySimulation.EmpireEffects effects = EconomySimulation.Effects(player);
                if (kind.StartsWith("sta"))
                {
                    property = NativeEffects.StabilityProperty;
                    header.AppendLine($"stability of city {cityIndex}: current {(float)city.PublicOrderCurrent.Value:0.#}, game target {(float)city.PublicOrderTarget.Value:0.#}, target with the economy {(float)NativeEffects.EffectiveStabilityTarget(city, NativeEffects.StabilityLoss(city)):0.#} (inflation −{effects.InflationLoss:0.#}, unemployment −{effects.UnemploymentLoss:0.#})");
                }
                else
                {
                    property = NativeEffects.ProductionProperty;
                    sub = "ProductionNet";
                    header.AppendLine($"industry of city {cityIndex}: game {(float)city.ProductionNetAfterAffinityBonuses.Value:0.#}, with credit ×{effects.Credit:0.000} = {(float)Amplitude.Mercury.Simulation.DepartmentOfIndustry.ComputeProductionIncome(city):0.#}");
                }
            }

            string command = "game breakdown " + args + " (game answer)";
            Amplitude.Mercury.EffectMapper.PropertyBreakdownAsyncOperation operation = string.IsNullOrEmpty(sub)
                ? service.GetPropertyBreakdownEvaluation(guid, property, Amplitude.Mercury.Simulation.BreakdownOptions.None)
                : service.GetPropertyBreakdownEvaluation(guid, property, sub, Amplitude.Mercury.Simulation.BreakdownOptions.None);
            string headerText = header.ToString();
            operation.UponCompletion(done =>
            {
                var text = new StringBuilder(headerText);
                foreach (PropertyBreakdownOutput.PropertyBreakdownLine line in ((Amplitude.Mercury.EffectMapper.PropertyBreakdownAsyncOperation)done).PropertyBreakdownOutput.Lines)
                {
                    text.AppendLine($"{(float)line.Gain,8:+0.##;-0.##;0} {(line.BaseValue != Amplitude.Mercury.Simulation.BreakdownSources.BaseValues.None ? "[" + line.BaseValue + "] " : string.Empty)}{line.Line}");
                }
                DevTools.WriteResult(command, text.ToString());
            });
            return headerText + "(breakdown requested from the game; it arrives in its own entry right below)";
        }

        /// <summary>
        /// "game congress ...": World Congress tests (research\congress.md §7.9).
        ///   unlock E#                 forces the Congress to form (game debug orders); E# presides the 1st session
        ///   close law|K#              closes the vote immediately
        ///   consulate E#              creates the consulate in the nation's capital (editor order)
        ///   leverage E# E# n          gives n leverage of the first against the second (editor order)
        ///   law E# CivicName          makes the law available to the nation (editor order)
        ///   propose V#                the player proposes the law (when presiding)
        ///   vote law A|B              the player votes on the law
        ///   vote K# E#                the player backs one side of the crisis
        ///   bribe law|K# E# n         the player bribes
        ///   comply|war E#             the player answers the verdict
        /// </summary>
        private static string Congress(string args)
        {
            string[] words = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Amplitude.Mercury.Sandbox.Sandbox sandbox = SandboxManager.Sandbox;
            var congress = Amplitude.Mercury.Sandbox.Sandbox.InternationalAncillary;
            if (sandbox == null || congress == null || words.Length == 0)
            {
                return "usage: game congress unlock E# | close law|K# | consulate E# | leverage E# E# n | law E# CivicName | propose V# | vote law A|B | vote K# E# | bribe law|K# E# n | comply|war E#";
            }
            int local = sandbox.LocalEmpireIndex;
            switch (words[0].ToLowerInvariant())
            {
                case "unlock":
                    return words.Length > 1 && TryEmpire(words[1], out int unlocker) ? Diplomacy.CongressFlow.ForceUnlock(unlocker) : "usage: game congress unlock E#";
                case "close":
                    // CongressFlow.ForceEnd reads "law" (civic vote) or "K#" (crisis vote).
                    return Diplomacy.CongressFlow.ForceEnd(words.Length > 1 ? words[1] : null);
                case "consulate":
                    return words.Length > 1 && TryEmpire(words[1], out int builder) ? Diplomacy.CongressFlow.CreateConsulate(builder) : "usage: game congress consulate E#";
                case "leverage":
                    return words.Length > 3 && TryEmpire(words[1], out int gainer) && TryEmpire(words[2], out int other) && int.TryParse(words[3], out int delta)
                        ? Diplomacy.CongressFlow.AddLeverage(gainer, other, delta) : "usage: game congress leverage E# E# n";
                case "law":
                    return words.Length > 2 && TryEmpire(words[1], out int receiver) ? Diplomacy.CongressFlow.UnlockLaw(receiver, words[2]) : "usage: game congress law E# CivicName";
                case "propose":
                {
                    string civic = Diplomacy.CongressFlow.CivicForCode(words.Length > 1 ? words[1] : null);
                    if (civic == null)
                    {
                        return "usage: game congress propose V# (international law code)";
                    }
                    InternationalFailureFlags flags = congress.GetStartCivicVoteFailureFlags(local, new Amplitude.StaticString(civic));
                    if (flags != InternationalFailureFlags.None)
                    {
                        return "the Congress would not accept: " + Diplomacy.CongressFlow.Explain(flags);
                    }
                    SandboxManager.PostOrder(new OrderStartCivicVote { CivicName = new Amplitude.StaticString(civic) }, local);
                    return $"ok: the player proposed {civic}";
                }
                case "vote":
                {
                    if (words.Length < 3)
                    {
                        return "usage: game congress vote law A|B | vote K# E#";
                    }
                    OrderInternationalAction order;
                    if (words[1].Equals("law", StringComparison.OrdinalIgnoreCase))
                    {
                        order = new OrderInternationalAction { InternationalAction = Amplitude.Mercury.Data.Simulation.InternationalAction.VoteForCivicChoiceIndex, CivicChoiceIndex = words[2].Equals("B", StringComparison.OrdinalIgnoreCase) ? 1 : 0 };
                    }
                    else if (Diplomacy.CongressFlow.TryCrisisCode(words[1], null, out int pool) && pool < congress.CrisisVoteInfos.Capacity && TryEmpire(words[2], out int side))
                    {
                        ref var crisis = ref congress.CrisisVoteInfos.GetReferenceAt(pool);
                        var action = side == crisis.DeclaratorEmpireIndex ? Amplitude.Mercury.Data.Simulation.InternationalAction.SideWithCrisisDeclarator
                            : side == crisis.TargetedEmpireIndex ? Amplitude.Mercury.Data.Simulation.InternationalAction.SideWithCrisisTargeted : Amplitude.Mercury.Data.Simulation.InternationalAction.None;
                        if (action == Amplitude.Mercury.Data.Simulation.InternationalAction.None)
                        {
                            return $"E{side} is not a side of crisis K{pool}";
                        }
                        order = new OrderInternationalAction { InternationalAction = action, CrisisVoteIndex = pool };
                    }
                    else
                    {
                        return "usage: game congress vote law A|B | vote K# E#";
                    }
                    InternationalFailureFlags flags = congress.GetInternationalActionFailureFlags(local, order);
                    if (flags != InternationalFailureFlags.None)
                    {
                        return "the Congress would not accept: " + Diplomacy.CongressFlow.Explain(flags);
                    }
                    SandboxManager.PostOrder(order, local);
                    return $"ok: player's vote sent ({order.InternationalAction})";
                }
                case "bribe":
                {
                    if (words.Length < 4 || !TryEmpire(words[2], out int target) || !int.TryParse(words[3], out int count) || target == local)
                    {
                        return "usage: game congress bribe law|K# E# n";
                    }
                    bool law = words[1].Equals("law", StringComparison.OrdinalIgnoreCase);
                    int pool = -1;
                    if (!law && !Diplomacy.CongressFlow.TryCrisisCode(words[1], null, out pool))
                    {
                        return "error: invalid vote";
                    }
                    var order = new OrderInternationalAction
                    {
                        InternationalAction = law ? Amplitude.Mercury.Data.Simulation.InternationalAction.BribeForCivicVote : Amplitude.Mercury.Data.Simulation.InternationalAction.BribeForCrisisVote,
                        CrisisVoteIndex = pool,
                        TargetedEmpireIndex = target,
                        NumberOfBribeActions = count,
                    };
                    InternationalFailureFlags flags = congress.GetInternationalActionFailureFlags(local, order);
                    if (flags != InternationalFailureFlags.None)
                    {
                        return "the Congress would not accept: " + Diplomacy.CongressFlow.Explain(flags);
                    }
                    SandboxManager.PostOrder(order, local);
                    return $"ok: {count} bribe(s) by the player against E{target}";
                }
                case "comply":
                case "war":
                {
                    if (words.Length < 2 || !TryEmpire(words[1], out int winner))
                    {
                        return "usage: game congress comply|war E#";
                    }
                    var action = words[0].Equals("comply", StringComparison.OrdinalIgnoreCase) ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.AcceptDemands : Amplitude.Mercury.Data.Simulation.DiplomaticAction.DeclareSurpriseWar;
                    DiplomaticActionFailureFlags flags = Sandbox.MajorEmpires[local].DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(winner, action);
                    if (flags != DiplomaticActionFailureFlags.None)
                    {
                        return "the game does not allow it: " + Diplomacy.ActionExecutor.Explain(flags);
                    }
                    SandboxManager.PostOrder(new OrderDiplomaticAction { OtherEmpireIndex = winner, DiplomaticAction = action }, local);
                    return $"ok: {action} sent to E{winner}";
                }
                default:
                    return "error: unknown command; use game congress without arguments to see the list";
            }
        }

        /// <summary>
        /// "game surrender offer|impose|accept|refuse|cancel E#": the player acts in a war as through the Crisis tab
        /// (surrender tests with the language-model AI nations). Offer and impose use the game's default terms
        /// (demands and the rest in gold).
        /// </summary>
        private static string Surrender(string args)
        {
            string[] words = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Amplitude.Mercury.Sandbox.Sandbox sandbox = SandboxManager.Sandbox;
            if (words.Length < 2 || !TryEmpire(words[1], out int other) || sandbox == null)
            {
                return "usage: game surrender offer|impose|accept|refuse|cancel E#";
            }
            int local = sandbox.LocalEmpireIndex;
            var dofa = Amplitude.Mercury.Sandbox.Sandbox.MajorEmpires[local].DepartmentOfForeignAffairs;
            var steps = new System.Collections.Generic.List<Amplitude.Mercury.Data.Simulation.DiplomaticAction>();
            switch (words[0].ToLowerInvariant())
            {
                case "offer":
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.StartToFillSurrenderProposition);
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeToSurrender);
                    break;
                case "impose":
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.AllowToForceOtherToSurrender);
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.DeclareSurrender);
                    break;
                case "accept":
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.AcceptSurrender);
                    break;
                case "refuse":
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.RefuseSurrender);
                    break;
                case "cancel":
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.CancelSurrenderProposition);
                    break;
                default:
                    return "usage: game surrender offer|impose|accept|refuse|cancel E#";
            }
            var flags = dofa.GetDiplomaticActionFailureFlags(other, steps[0]);
            if (flags != Amplitude.Mercury.Interop.DiplomaticActionFailureFlags.None)
            {
                return $"error: {steps[0]} refused by the game: {Diplomacy.ActionExecutor.Explain(flags)}";
            }
            foreach (var step in steps)
            {
                SandboxManager.PostOrder(new Amplitude.Mercury.Interop.OrderDiplomaticAction { OtherEmpireIndex = other, DiplomaticAction = step }, local);
            }
            return $"ok: {string.Join(" → ", steps)} posted for E{local} in the war with E{other}";
        }

        /// <summary>"game zoom 0..1": sets the camera zoom (1 = strategic view) without the mouse wheel.</summary>
        private static string SetZoom(string args)
        {
            var camera = Amplitude.Mercury.Presentation.Presentation.PresentationCameraController;
            if (camera == null || !float.TryParse((args ?? string.Empty).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
            {
                return "usage: game zoom 0..1 (with a game open; 1 = strategic view)";
            }
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            object mover = typeof(Amplitude.Mercury.Presentation.PresentationCameraController).GetField("mercuryCameraMover", flags)?.GetValue(camera);
            var field = typeof(AbstractMercuryCameraMover).GetField("abscissa", flags);
            if (mover == null || field == null)
            {
                return "error: camera zoom control not found";
            }
            field.SetValue(mover, UnityEngine.Mathf.Clamp01(value));
            return $"zoom at {value:0.00}";
        }

        /// <summary>
        /// "game camera T#|tile": centers the camera on a territory (administrative center or visual center) or on a tile.
        /// </summary>
        private static string CenterCamera(string args)
        {
            var world = Amplitude.Mercury.Sandbox.Sandbox.World;
            var camera = Amplitude.Mercury.Presentation.Presentation.PresentationCameraController;
            string target = (args ?? string.Empty).Trim();
            if (world == null || camera == null || target.Length == 0)
            {
                return "usage: game camera T#|tile (with a game open)";
            }
            int tile;
            if (target.StartsWith("T", StringComparison.OrdinalIgnoreCase) && int.TryParse(target.Substring(1), out int territory)
                && territory >= 0 && territory < world.TerritoryInfo.Data.Length)
            {
                var info = world.TerritoryInfo.Data[territory];
                tile = info.Claimed && info.AdministrativeDistrictTileIndex >= 0 ? info.AdministrativeDistrictTileIndex : info.VisualCenter.ToTileIndex();
            }
            else if (!int.TryParse(target, out tile) || tile < 0 || tile >= world.TileInfo.Data.Length)
            {
                return "error: invalid target (T# or tile number)";
            }
            camera.CenterCameraAt(tile);
            return $"camera on tile {tile}";
        }

        /// <summary>
        /// "game focus &lt;path&gt;|off": puts the cursor in a text field (like the player's click) or removes it. With the cursor
        /// in the field, "game interaction state" says "typing: yes" and the mod's shortcuts (F8, F10) are disabled.
        /// </summary>
        private static string FocusField(string args)
        {
            var manager = Amplitude.UI.Interactables.UIInteractivityManager.Instance;
            if (manager == null)
            {
                return "error: no interface";
            }
            string path = (args ?? string.Empty).Trim();
            if (path == "off")
            {
                manager.SetFocus();
                return "ok: focus released";
            }
            UnityEngine.Transform target = DevTools.FindByPath(path);
            Amplitude.UI.Interactables.UITextField field = target?.GetComponentInChildren<Amplitude.UI.Interactables.UITextField>(true);
            if (field == null)
            {
                return "error: text field not found at " + path;
            }
            manager.SetFocus(field.TextFieldResponder);
            return $"ok: focus on {field.name} · typing: {(NativeUI.NativeUIKit.TypingInField() ? "yes" : "no")}";
        }

        /// <summary>
        /// "game interaction esc|unit|city|faith|trade|tab X|diplomacy E#|society|espionage|normal|state": does what the
        /// player would do (ESC key through the game's own path, select a unit or city, open the Faith or Society menu,
        /// switch a diplomacy tab) and shows which windows stayed open. Used to test whether the mod's windows close when
        /// the player goes to interact with something else.
        /// </summary>
        private static string Interaction(string args)
        {
            string what = (args ?? string.Empty).Trim().ToLowerInvariant().Split(' ')[0];
            var cursors = Amplitude.Mercury.Presentation.Presentation.PresentationCursorController;
            var banner = Amplitude.Mercury.UI.Helpers.WindowsUtils.GetWindow<Amplitude.Mercury.UI.ControlBanner>();
            int local = Snapshots.GameSnapshot?.PresentationData?.LocalEmpireInfo.EmpireIndex ?? -1;
            switch (what)
            {
                case "esc":
                    // Same as the key: WindowsManager.ExitWindow raises ESC and goes through the window groups.
                    Amplitude.Mercury.UI.Windows.WindowsManager.Instance?.ExitWindow(null, Amplitude.Framework.Input.ActionState.Pressed);
                    break;
                case "unit":
                {
                    var armies = Snapshots.GameSnapshot.PresentationData.ArmyInfo;
                    for (int i = 0; i < armies.Length; i++)
                    {
                        if (armies.Data[i].EmpireIndex == local)
                        {
                            cursors?.ChangeToArmyCursor(armies.Data[i].SimulationEntityGUID);
                            break;
                        }
                    }
                    break;
                }
                case "city":
                {
                    var settlements = Snapshots.GameSnapshot.PresentationData.SettlementInfo;
                    for (int i = 0; i < settlements.Length; i++)
                    {
                        if (settlements.Data[i].EmpireIndex == local)
                        {
                            cursors?.ChangeToSettlementCursor(settlements.Data[i].SimulationEntityGUID);
                            break;
                        }
                    }
                    break;
                }
                case "faith":
                    banner?.RequestReligionState();
                    break;
                case "trade":
                    cursors?.ChangeToTradeViewCursor();
                    break;
                case "tab":
                {
                    // "tab relations|trade|treaties|crisis": same as clicking the tab on the diplomacy screen.
                    var screen = Amplitude.Mercury.UI.Helpers.WindowsUtils.GetWindow<Amplitude.Mercury.UI.DiplomaticScreen>();
                    string[] words = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string name = words.Length > 1 ? words[1].ToLowerInvariant() : "relations";
                    var mode = name.StartsWith("trad") ? Amplitude.Mercury.UI.DiplomaticScreenMode.Trade
                        : name.StartsWith("trea") ? Amplitude.Mercury.UI.DiplomaticScreenMode.Treaties
                        : name.StartsWith("cri") ? Amplitude.Mercury.UI.DiplomaticScreenMode.Crisis
                        : Amplitude.Mercury.UI.DiplomaticScreenMode.Relations;
                    if (screen == null || !screen.Shown)
                    {
                        return "error: the diplomacy screen is not open";
                    }
                    screen.NegociationGroup_TabSwitch(mode);
                    break;
                }
                case "diplomacy":
                {
                    // "diplomacy E3": opens the diplomacy screen with that nation (same as clicking its portrait).
                    int other = -1;
                    string[] words = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 1)
                    {
                        int.TryParse(words[1].TrimStart('E', 'e'), out other);
                    }
                    if (other < 0)
                    {
                        return "usage: game interaction diplomacy E#";
                    }
                    cursors?.ChangeToDiplomaticCursor(other);
                    break;
                }
                case "society":
                    banner?.RequestSocietyState();
                    break;
                case "espionage":
                    banner?.RequestStealthState();
                    break;
                case "normal":
                    cursors?.ChangeToDefaultCursor();
                    banner?.RequestNoneState();
                    break;
                case "state":
                case "":
                    break;
                default:
                    return "usage: game interaction esc|unit|city|faith|trade|tab relations|trade|treaties|crisis|diplomacy E#|society|espionage|normal|state";
            }
            var text = new StringBuilder();
            if (what != "state" && what.Length > 0)
            {
                text.AppendLine($"done: {what} (the effect on the windows shows on the next frame; run 'game interaction state')");
            }
            text.AppendLine($"cursor: {cursors?.CurrentCursor?.GetType().Name ?? "?"} · banner: {banner?.State.ToString() ?? "?"}");
            text.AppendLine($"Central Bank: {(NativeUI.NativeBankWindow.IsOpen ? "open" : "closed")} · trade post: {(NativeUI.TradePostWindow.Instance?.Shown == true ? "open" : "closed")} · mail: {(Diplomacy.UI.MailScreen.IsOpen ? "open" : "closed")} · Letters tab: {(Diplomacy.UI.CorrespondenceTab.Active ? "active" : "no")}");
            var diplomacy = Amplitude.Mercury.UI.Helpers.WindowsUtils.GetWindow<Amplitude.Mercury.UI.DiplomaticScreen>();
            if (diplomacy != null && diplomacy.Shown)
            {
                text.AppendLine($"diplomacy: nation E{Snapshots.DiplomaticCursorSnapshot.PresentationData.OtherEmpireIndex}, game tab {diplomacy.currentMode}");
            }
            var selection = Amplitude.Mercury.UI.Windows.WindowsManager.Instance?.GetWindowsGroup<Amplitude.Mercury.UI.Windows.InGameSelectionGroup>();
            var full = Amplitude.Mercury.UI.Windows.WindowsManager.Instance?.GetWindowsGroup<Amplitude.Mercury.UI.Windows.InGameFullscreenGroup>();
            var open = new System.Collections.Generic.List<string>();
            foreach (var group in new Amplitude.UI.Windows.UIWindowsGroup[] { selection, full })
            {
                if (group?.windows == null)
                {
                    continue;
                }
                foreach (var window in group.windows)
                {
                    if (window != null && window.Shown)
                    {
                        open.Add(window.name);
                    }
                }
            }
            text.AppendLine("open windows (side panels and fullscreen): " + (open.Count == 0 ? "none" : string.Join(", ", open)));
            var focused = Amplitude.UI.Interactables.UIInteractivityManager.Instance?.FocusedResponder;
            text.AppendLine("keyboard focus: " + (focused == null ? "none" : focused.ToString()) + $" · typing: {(NativeUI.NativeUIKit.TypingInField() ? "yes" : "no")}");
            return text.ToString();
        }

        /// <summary>"game rpn &lt;name&gt;": the game's data formula (operation stack, constants and properties), to understand a calculation.</summary>
        /// <summary>
        /// The player's era stars: score, thresholds and how the deed behind each one is scored (trigger and evaluator,
        /// dumped by reflection). Read-only, main thread: diagnostics only.
        /// </summary>
        private static string EraStars()
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "error: no game loaded";
            }
            Amplitude.Mercury.Simulation.MajorEmpire mine = Sandbox.MajorEmpires[sandbox.LocalEmpireIndex];
            var deeds = Databases.GetDatabase<Amplitude.Mercury.Data.Simulation.DeedDefinition>();
            var pool = mine.DepartmentOfDevelopment.EraStarInfoPool;
            var text = new StringBuilder();
            for (int i = 0; i < pool.Capacity; i++)
            {
                EraStarInfo star = pool[i];
                if (star.PoolAllocationIndex < 0)
                {
                    continue;
                }
                text.AppendLine($"{star.EraStarDefinitionName} deed {star.DeedDefinitionName} level {star.Level} score {(float)star.Score:0.##} thresholds "
                    + string.Join("/", (star.Thresholds ?? new Amplitude.FixedPoint[0]).Select(t => ((float)t).ToString("0"))));
                Amplitude.Mercury.Data.Simulation.DeedDefinition deed = null;
                if (deeds != null && deeds.TryGetValue(star.DeedDefinitionName, out deed) && deed != null)
                {
                    text.AppendLine("  trigger: " + DumpFields(deed.Trigger));
                    text.AppendLine("  evaluator " + deed.Evaluator?.GetType().Name + ": " + DumpFields(deed.Evaluator));
                }
            }
            text.AppendLine($"money stock {(float)mine.MoneyStock.Value:0} net {(float)mine.MoneyNet.Value:0}");
            return text.ToString();
        }

        private static string DumpFields(object value)
        {
            if (value == null)
            {
                return "null";
            }
            var parts = new System.Collections.Generic.List<string>();
            foreach (System.Reflection.FieldInfo field in value.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                object v = field.GetValue(value);
                string shown = v is System.Array array ? "[" + string.Join(", ", array.Cast<object>().Select(e => e == null ? "null" : DumpShort(e))) + "]" : DumpShort(v);
                parts.Add(field.Name + "=" + shown);
            }
            return string.Join("; ", parts);
        }

        private static string DumpShort(object value)
        {
            if (value == null)
            {
                return "null";
            }
            System.Type type = value.GetType();
            if (type.IsPrimitive || value is string || type.IsEnum || value is Amplitude.FixedPoint || value is Amplitude.StaticString)
            {
                return value.ToString();
            }
            var inner = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Select(f => f.Name + ":" + (f.GetValue(value)?.ToString() ?? "null"));
            return "{" + string.Join(", ", inner) + "}";
        }

        private static string DumpRpn(string name)
        {
            var database = Databases.GetDatabase<Amplitude.Framework.Simulation.Rpn.RpnDefinition>();
            if (database == null)
            {
                return "error: formula database unavailable";
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Join(", ", database.GetValues().Select(d => d.Name.ToString()).OrderBy(n => n));
            }
            Amplitude.Framework.Simulation.Rpn.RpnDefinition rpn = database.GetValues().FirstOrDefault(d => string.Equals(d.Name.ToString(), name, StringComparison.OrdinalIgnoreCase));
            if (rpn == null)
            {
                return $"error: formula '{name}' not found (game rpn without a name lists all)";
            }
            var text = new StringBuilder();
            text.AppendLine($"{rpn.Name} (target {rpn.TargetTypeName}, source {rpn.SourceTypeName})");
            text.AppendLine("operations: " + string.Join(" ", (rpn.RpnOperationStack ?? new Amplitude.Framework.Simulation.Operation[0]).Select(o => o.ToString())));
            text.AppendLine("constants: " + string.Join(" ", (rpn.ConstantStack ?? new Amplitude.FixedPoint[0]).Select(c => ((float)c).ToString("0.###"))));
            text.AppendLine("properties: " + string.Join(" ", rpn.PropertyLocalNameStack ?? new string[0]));
            text.AppendLine("variables: " + string.Join(" ", rpn.VariableNameStack ?? new string[0]));
            if (!string.IsNullOrEmpty(rpn.Note))
            {
                text.AppendLine("note: " + rpn.Note);
            }
            return text.ToString();
        }

        private static string State()
        {
            var text = new StringBuilder();
            var runtime = Services.GetService<Amplitude.Framework.Runtime.IRuntimeService>();
            text.AppendLine("runtime: " + (runtime?.Runtime?.FiniteStateMachine?.CurrentState?.GetType().Name ?? "?"));
            text.AppendLine("sandbox: " + (Diplomacy.GameAccess.SandboxStateName() ?? "(no game)"));
            if (Diplomacy.GameAccess.TryGetSession(out string guid, out int turn, out int player, allowOnline: true))
            {
                LocalEmpireInfo local = Snapshots.GameSnapshot.PresentationData.LocalEmpireInfo;
                text.AppendLine($"game: {guid} · turn {turn} · player E{player} · can end turn: {(local.CanEndTurn ? "yes" : "no")}");
                string battles = Battles(string.Empty);
                if (battles.Contains("waiting for you"))
                {
                    text.AppendLine(battles);
                    text.AppendLine("(the end of turn waits for this battle: use game battle auto|retreat)");
                }
            }
            else
            {
                text.AppendLine("game: none loaded");
            }
            return text.ToString().TrimEnd();
        }

        private static IGameSerializationService Serialization()
        {
            return Services.GetService<IGameSerializationService>() ?? throw new InvalidOperationException("save service is not ready yet");
        }

        /// <summary>
        /// "game saves rescan": rereads the current saves folder (the same directory switch the saves screen's cloud button does),
        /// so the game sees a save copied while it is running. Does not change the player's local/cloud choice.
        /// </summary>
        private static string RescanSaves()
        {
            var storage = Services.GetService<Amplitude.Framework.Storage.IStorageManagementService>() as Amplitude.Framework.Storage.LocalDrive.LocalDriveStorageManager;
            if (storage == null)
            {
                return "error: save storage is not the local folder";
            }
            storage.ChangeDirectory(Amplitude.Mercury.Application.GameSaveDirectory);
            return "ok: folder reread (" + Amplitude.Mercury.Application.GameSaveDirectory + "), " + Serialization().EnumerateGameSaveFiles().Count() + " saves";
        }

        private static string ListSaves()
        {
            var saves = Serialization().EnumerateGameSaveFiles()
                .Select(f => f.StorageContainerInfo)
                .Select(info => new { info.Name, Date = Modified(info) })
                .OrderByDescending(s => s.Date)
                .Take(15)
                .ToList();
            return saves.Count == 0 ? "no saves" : string.Join("\n", saves.Select(s => $"{s.Date:yyyy-MM-dd HH:mm}  {s.Name}"));
        }

        private static DateTime Modified(Amplitude.Framework.Storage.StorageContainerInfo info)
        {
            try
            {
                return info.TryGetMetadata("LastModificationDate", out string value) && DateTime.TryParse(value, out DateTime date) ? date : DateTime.MinValue;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        private static string Load(string title)
        {
            IGameSerializationService serialization = Serialization();
            if (string.IsNullOrEmpty(title))
            {
                return serialization.LoadLatestSave() ? "loading the most recent save" : "error: no save found to load";
            }
            GameSaveDescriptor descriptor = serialization.RetrieveGameSaveDescriptorFromSave(title);
            if (descriptor == null)
            {
                return $"error: save '{title}' not found";
            }
            var runtime = Services.GetService<Amplitude.Framework.Runtime.IRuntimeService>();
            // Same path as the saves screen's Load button (LoadSavesScreen.OnLoadRequested), single-player game.
            runtime.Runtime.FiniteStateMachine.PostStateChange(typeof(RuntimeState_OutGame), new RuntimeStateGameSaveDescriptorParameter(descriptor));
            return $"loading '{descriptor.Title}'";
        }

        private static string Save(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return "usage: game save <title>";
            }
            if (!Diplomacy.GameAccess.TryGetSession(out _, out _, out _, allowOnline: true))
            {
                return "error: no game loaded";
            }
            Serialization().SaveGame(title);
            return $"saving '{title}'";
        }

        /// <summary>The player's pending treaties and agreements. Reads the simulation on the main thread: tests only.</summary>
        private static string Proposals()
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "error: no game loaded";
            }
            int me = sandbox.LocalEmpireIndex;
            Amplitude.Mercury.Simulation.MajorEmpire mine = Sandbox.MajorEmpires[me];
            var lines = new StringBuilder();
            for (int other = 0; other < Sandbox.NumberOfMajorEmpires; other++)
            {
                if (other == me || !Sandbox.MajorEmpires[other].IsAlive)
                {
                    continue;
                }
                Amplitude.Mercury.Simulation.DiplomaticRelation relation = Sandbox.DiplomaticAncillary.GetRelationFor(me, other);
                if (relation.HasPendingTreatyProposition())
                {
                    bool mineToAnswer = mine.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, Amplitude.Mercury.Data.Simulation.DiplomaticAction.SignTreaty) == DiplomaticActionFailureFlags.None
                        || mine.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, Amplitude.Mercury.Data.Simulation.DiplomaticAction.IgnoreTreaty) == DiplomaticActionFailureFlags.None;
                    lines.AppendLine($"E{other}: pending treaty ({relation.DiplomaticState.CurrentTreatyInfo.PropositionInfo.Status}) — {(mineToAnswer ? "you answer" : "your proposal, waiting")}");
                }
                if (relation.HasPendingAgreementProposition())
                {
                    bool mineToAnswer = mine.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, Amplitude.Mercury.Data.Simulation.DiplomaticAction.SignAgreement) == DiplomaticActionFailureFlags.None
                        || mine.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, Amplitude.Mercury.Data.Simulation.DiplomaticAction.IgnoreAgreement) == DiplomaticActionFailureFlags.None;
                    lines.AppendLine($"E{other}: pending agreement ({relation.DiplomaticState.CurrentAgreementsInfo.PropositionInfo.Status}) — {(mineToAnswer ? "you answer" : "your proposal, waiting")}");
                }
            }
            return lines.Length > 0 ? lines.ToString().TrimEnd() : "no pending proposals";
        }

        /// <summary>Answers for the player (tests): accept signs, ignore refuses without insulting.</summary>
        private static string Answer(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].StartsWith("E", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[0].Substring(1), out int other))
            {
                return "usage: game answer <E#> accept|ignore";
            }
            bool accept = parts[1].StartsWith("a", StringComparison.OrdinalIgnoreCase);
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "error: no game loaded";
            }
            int me = sandbox.LocalEmpireIndex;
            Amplitude.Mercury.Simulation.MajorEmpire mine = Sandbox.MajorEmpires[me];
            var sent = new System.Collections.Generic.List<string>();
            foreach (Amplitude.Mercury.Data.Simulation.DiplomaticAction action in accept
                ? new[] { Amplitude.Mercury.Data.Simulation.DiplomaticAction.SignTreaty, Amplitude.Mercury.Data.Simulation.DiplomaticAction.SignAgreement, Amplitude.Mercury.Data.Simulation.DiplomaticAction.AcceptGift }
                : new[] { Amplitude.Mercury.Data.Simulation.DiplomaticAction.IgnoreTreaty, Amplitude.Mercury.Data.Simulation.DiplomaticAction.IgnoreAgreement, Amplitude.Mercury.Data.Simulation.DiplomaticAction.RefuseGift })
            {
                if (mine.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, action) == DiplomaticActionFailureFlags.None)
                {
                    SandboxManager.PostOrder(new OrderDiplomaticAction { OtherEmpireIndex = other, DiplomaticAction = action });
                    sent.Add(action.ToString());
                }
            }
            return sent.Count > 0 ? $"sent to E{other}: {string.Join(", ", sent)}" : $"nothing to answer to E{other}";
        }

        /// <summary>Formal proposal by the player, as through the diplomacy screen (tests of F3 option B).</summary>
        private static string Propose(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].StartsWith("E", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[0].Substring(1), out int other))
            {
                return "usage: game propose <E#> economic|information|cultural|military|alliance|peace";
            }
            Amplitude.Mercury.Data.Simulation.DiplomaticAction action;
            switch (parts[1].ToLowerInvariant())
            {
                case "economic": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeEconomicalAgreement; break;
                case "information": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeInformationAgreement; break;
                case "cultural": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeCulturalAgreement; break;
                case "military": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeMilitaryAgreement; break;
                case "alliance": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeAllianceTreaty; break;
                case "peace": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeEndWarTreaty; break;
                default: return "error: unknown kind (economic|information|cultural|military|alliance|peace)";
            }
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "error: no game loaded";
            }
            DiplomaticActionFailureFlags flags = Sandbox.MajorEmpires[sandbox.LocalEmpireIndex].DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, action);
            if (flags != DiplomaticActionFailureFlags.None)
            {
                return "the game does not allow it: " + Diplomacy.ActionExecutor.Explain(flags);
            }
            SandboxManager.PostOrder(new OrderDiplomaticAction { OtherEmpireIndex = other, DiplomaticAction = action });
            return $"proposal sent to E{other}: {action}";
        }

        /// <summary>
        /// Why end of turn does not finish: pending diplomatic items holding TurnFinish and the last orders
        /// (TurnFinish only fires after 8 s with no new order). Read on the main thread: diagnostics only.
        /// </summary>
        private static string Stuck(string args)
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "error: no game loaded";
            }
            int count = int.TryParse(args, out int n) && n > 0 ? n : 25;
            var text = new StringBuilder();
            text.AppendLine($"state {sandbox.CurrentStateName} · last order #{sandbox.OrderHistory.LastOrderSerial} · orders since the autosave: {sandbox.OrderHistory.Length}");
            foreach (Amplitude.Mercury.Simulation.DiplomaticRelation relation in Sandbox.DiplomaticAncillary.DiplomaticRelations)
            {
                string pair = $"E{relation.LeftEmpireIndex}-E{relation.RightEmpireIndex}";
                if (relation.HasPendingTreatyProposition())
                {
                    var info = relation.DiplomaticState.CurrentTreatyInfo.PropositionInfo;
                    text.AppendLine($"pending treaty {pair}: {info.Status}, from E{info.EmpireIndexWhoInitiated} to E{info.EmpireIndexWhoNeedToSign}, turn {info.TurnWhenProposed}");
                }
                if (relation.HasPendingAgreementProposition())
                {
                    var info = relation.DiplomaticState.CurrentAgreementsInfo.PropositionInfo;
                    text.AppendLine($"pending agreement {pair}: {info.Status}, from E{info.EmpireIndexWhoInitiated} to E{info.EmpireIndexWhoNeedToSign}, turn {info.TurnWhenProposed}");
                }
                if (relation.HasPendingConsulateProposition()) text.AppendLine($"pending consulate {pair}");
                if (relation.HasPendingSurrenderProposition()) text.AppendLine($"pending surrender {pair}");
                if (relation.HasPendingGiftProposition()) text.AppendLine($"pending gift {pair} (#{relation.ProposedGiftPoolIndex})");
                if (relation.EmpireWhoNeedToAnswerEnforcedDemand >= 0) text.AppendLine($"enforced demand {pair}: E{relation.EmpireWhoNeedToAnswerEnforcedDemand} answers ({relation.DiplomaticState.Crisis.CrisisStatus})");
            }
            var log = new Amplitude.IO.StringBuilder(1024);
            bool actions = Sandbox.ActionController.ReadyForTurnFinishCompletion(SandboxImpatience.None, log);
            bool battles = Sandbox.BattleRepository.ReadyForTurnFinishCompletion(SandboxImpatience.None, log);
            bool air = Sandbox.AirStrikeRepository.ReadyForTurnFinishCompletion(SandboxImpatience.None, log);
            bool artillery = Sandbox.ArtilleryStrikeRepository.ReadyForTurnFinishCompletion(SandboxImpatience.None, log);
            bool camera = Sandbox.CameraSequenceController.ReadyForTurnFinishCompletion(SandboxImpatience.None, log);
            text.AppendLine($"ready: actions {actions} · battles {battles} · air strikes {air} · artillery {artillery} · camera {camera}");
            string stuck = log.ToString();
            if (!string.IsNullOrWhiteSpace(stuck))
            {
                text.AppendLine(stuck.TrimEnd());
            }
            for (int i = 0; i < Sandbox.NumberOfMajorEmpires; i++)
            {
                Amplitude.Mercury.Simulation.MajorEmpire empire = Sandbox.MajorEmpires[i];
                if (empire.IsAlive && (empire.IsControlledByHuman || empire.IsLockedByMandatories))
                {
                    text.AppendLine($"E{i}: human {empire.IsControlledByHuman} · locked by mandatory decision {empire.IsLockedByMandatories}");
                }
            }
            System.Collections.Generic.List<Order> orders = sandbox.OrderHistory.Orders;
            text.AppendLine($"last {System.Math.Min(count, orders.Count)} orders:");
            for (int i = System.Math.Max(0, orders.Count - count); i < orders.Count; i++)
            {
                Order order = orders[i];
                string extra = order is OrderDiplomaticAction diplomatic ? $" {diplomatic.DiplomaticAction} → E{diplomatic.OtherEmpireIndex}" : string.Empty;
                text.AppendLine($"#{order.Serial} E{((IBillet)order).TargetEmpireIndex} {order.OrderIdentifier}{extra}");
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Ongoing battles. With "auto" or "retreat", answers for the player those awaiting their confirmation (end of
        /// turn waits for that), like the battle screen's instant-resolve and retreat buttons.
        /// </summary>
        private static string Battles(string args)
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "error: no game loaded";
            }
            string mode = (args ?? string.Empty).Trim().ToLowerInvariant();
            int me = sandbox.LocalEmpireIndex;
            var text = new StringBuilder();
            foreach (Amplitude.Mercury.Simulation.Battle battle in Sandbox.BattleRepository.Battles)
            {
                if (battle == null || battle.IsFinished)
                {
                    continue;
                }
                bool mine = battle.TryGetContender(me, out _);
                bool waitingForMe = mine && battle.BattleState == BattleState.Confirmation && !battle.ConfirmationReadyByEmpire[me];
                text.Append($"battle at {battle.AttackerGroup.OriginPosition}: E{battle.AttackerGroup.LeaderEmpireIndex} attacks E{battle.DefenderGroup.LeaderEmpireIndex} · {battle.BattleState}{(waitingForMe ? " · waiting for you" : string.Empty)}");
                if (waitingForMe && mode == "auto")
                {
                    SandboxManager.PostOrder(new OrderBattleSetInstantResolve(battle.GUID, me), me);
                    text.Append(" → instant resolve requested");
                }
                else if (waitingForMe && mode == "retreat")
                {
                    SandboxManager.PostOrder(new OrderBattleRetreat { BattleGUID = battle.GUID, EmpireIndex = me }, me);
                    text.Append(" → retreat requested");
                }
                text.AppendLine();
            }
            return text.Length > 0 ? text.ToString().TrimEnd() : "no battle in progress";
        }

        /// <summary>
        /// Each nation's native AI mood toward the others (what the diplomacy screen shows), next to the stance the
        /// language-model AI set: checks the NativeAiBias skew. Read on the main thread: tests only.
        /// </summary>
        private static string Moods(string args)
        {
            if (SandboxManager.Sandbox == null)
            {
                return "error: no game loaded";
            }
            int only = TryEmpire(args?.Trim(), out int filter) ? filter : -1;
            Diplomacy.AiWorld world = Diplomacy.AiModule.World;
            var text = new StringBuilder();
            for (int a = 0; a < Sandbox.NumberOfMajorEmpires; a++)
            {
                Amplitude.Mercury.Simulation.MajorEmpire empire = Sandbox.MajorEmpires[a];
                if (!empire.IsAlive || empire.IsControlledByHuman || (only >= 0 && a != only))
                {
                    continue;
                }
                Diplomacy.AiNation nation = world?.Get(a);
                var items = new System.Collections.Generic.List<string>();
                for (int b = 0; b < Sandbox.NumberOfMajorEmpires; b++)
                {
                    if (a == b || !Sandbox.MajorEmpires[b].IsAlive)
                    {
                        continue;
                    }
                    Amplitude.Mercury.Simulation.DiplomaticRelation relation = Sandbox.DiplomaticAncillary.GetRelationFor(a, b);
                    if (relation.CurrentState < Amplitude.Mercury.Data.Simulation.DiplomaticStateType.Peace)
                    {
                        continue;
                    }
                    Amplitude.Mercury.Simulation.DiplomaticAmbassy ambassy = relation.GetEmpireEmbassy(a);
                    string posture = nation != null && nation.Postures.TryGetValue(b, out Diplomacy.Stance stance) ? stance.Value : "-";
                    items.Add($"E{b} {ambassy.CurrentAIBehaviourState} (rapport {ambassy.AIRapportScore:0.00}, stance {posture})");
                }
                string focus = nation?.Focus?.Value ?? "-";
                text.AppendLine($"E{a} [focus {focus}]: {string.Join(" · ", items)}");
            }
            return text.Length > 0 ? text.ToString().TrimEnd() : "no AI nations";
        }

        /// <summary>
        /// Spectator mode (performance tests): the native AI takes over the local empire and turns advance on their
        /// own. Same order the game uses when switching the local empire (Sandbox.cs:953). "off" gives control back.
        /// </summary>
        private static string Spectator(string args)
        {
            string mode = (args ?? string.Empty).Trim().ToLowerInvariant();
            if (mode != "on" && mode != "off")
            {
                return "usage: game spectator on|off";
            }
            if (!Diplomacy.GameAccess.TryGetSession(out _, out int turn, out int player, allowOnline: true))
            {
                return "error: no game loaded";
            }
            bool human = mode == "off";
            SandboxManager.PostOrder(new OrderSetControlledByHuman(human));
            return human
                ? $"E{player} is yours again (turn {turn})"
                : $"E{player} is now played by the native AI: turns advance on their own from turn {turn} (game spectator off to stop)";
        }

        /// <summary>
        /// Economy audit (balance tests): game and CurrencyMod numbers for each empire, with warnings for impossible or
        /// outlier values (NaN/infinity, exchange rate at its limit, extreme inflation or interest, jumps in the history,
        /// treasury, income, science or army far above/below the median, interest yielding more than the real economy).
        /// Writes dev\out\economy_T{turn}.csv to compare turns.
        /// </summary>
        private static string EconomyAudit()
        {
            Diplomacy.Capture.WorldCapture capture = Diplomacy.Capture.TurnCapture.CaptureFromMainThread();
            CurrencyWorld world = CurrencyManager.Current;
            if (capture == null || world == null)
            {
                return "error: no game or no monetary state";
            }
            var rows = new System.Collections.Generic.List<EconomyRow>();
            foreach (Diplomacy.Capture.CapturedEmpire empire in capture.Empires)
            {
                if (empire == null || !empire.Alive)
                {
                    continue;
                }
                EmpireCurrency currency;
                lock (CurrencyManager.Lock)
                {
                    currency = world.Get(empire.Index);
                }
                double industry = 0;
                Amplitude.Mercury.Simulation.MajorEmpire live = Sandbox.MajorEmpires[empire.Index];
                for (int s = 0; s < live.Settlements.Count; s++)
                {
                    if (live.Settlements[s] != null)
                    {
                        industry += (float)live.Settlements[s].ProductionNetAfterAffinityBonuses.Value;
                    }
                }
                rows.Add(new EconomyRow { Empire = empire, Currency = currency, Industry = industry });
            }
            if (rows.Count == 0)
            {
                return "no living empires";
            }

            double Median(Func<EconomyRow, double> pick)
            {
                var values = rows.Select(pick).OrderBy(v => v).ToList();
                return values.Count % 2 == 1 ? values[values.Count / 2] : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2;
            }
            double medMoney = Median(r => r.Empire.Money), medNet = Median(r => Math.Abs(r.Empire.MoneyNet));
            double medScience = Median(r => r.Empire.Science), medIndustry = Median(r => r.Industry);
            double medMilitary = Median(r => r.Empire.MilitaryStrength), medFame = Median(r => r.Empire.Fame);
            double medInfluence = Median(r => r.Empire.Influence), medTerritories = Median(r => r.Empire.TerritoryCount);

            var alerts = new System.Collections.Generic.List<string>();
            void Flag(EconomyRow row, string text) => alerts.Add($"E{row.Empire.Index} {row.Empire.Culture}: {text}");
            bool Bad(double v) => double.IsNaN(v) || double.IsInfinity(v);
            foreach (EconomyRow row in rows)
            {
                Diplomacy.Capture.CapturedEmpire e = row.Empire;
                EmpireCurrency c = row.Currency;
                double[] values = { e.Money, e.MoneyNet, e.Science, e.Influence, row.Industry, c?.ExchangeValue ?? 0, c?.InflationRate ?? 0, c?.InterestRate ?? 0, c?.PriceIndex ?? 0, c?.LastInterestFlow ?? 0 };
                if (values.Any(Bad)) Flag(row, "invalid value (NaN or infinity)");
                if (e.Money > 10 * Math.Max(200, medMoney)) Flag(row, $"treasury {e.Money:0} is {e.Money / Math.Max(1, medMoney):0.0}× the median");
                if (e.Money < -2000) Flag(row, $"large debt ({e.Money:0})");
                if (Math.Abs(e.MoneyNet) > 5 * Math.Max(20, medNet)) Flag(row, $"income {e.MoneyNet:0}/turn is {Math.Abs(e.MoneyNet) / Math.Max(1, medNet):0.0}× the median");
                if (e.Science > 5 * Math.Max(10, medScience)) Flag(row, $"science {e.Science:0}/turn is {e.Science / Math.Max(1, medScience):0.0}× the median");
                if (row.Industry > 5 * Math.Max(10, medIndustry)) Flag(row, $"industry {row.Industry:0}/turn is {row.Industry / Math.Max(1, medIndustry):0.0}× the median");
                if (e.MilitaryStrength > 5 * Math.Max(10, medMilitary)) Flag(row, $"army {e.MilitaryStrength:0} is {e.MilitaryStrength / Math.Max(1, medMilitary):0.0}× the median");
                if (e.Influence > 10 * Math.Max(50, medInfluence)) Flag(row, $"influence {e.Influence:0} is {e.Influence / Math.Max(1, medInfluence):0.0}× the median");
                if (e.Fame > 4 * Math.Max(50, medFame)) Flag(row, $"fame {e.Fame:0} is {e.Fame / Math.Max(1, medFame):0.0}× the median");
                if (e.TerritoryCount > 4 * Math.Max(2, medTerritories)) Flag(row, $"{e.TerritoryCount} territories ({e.TerritoryCount / Math.Max(1, medTerritories):0.0}× the median)");
                if (e.Stability < 15) Flag(row, $"stability {e.Stability:0}%");
                if (c == null)
                {
                    Flag(row, "no monetary state");
                    continue;
                }
                if (c.ExchangeValue <= EconomyConfig.ExchangeMin.Value * 1.02 || c.ExchangeValue >= EconomyConfig.ExchangeMax.Value * 0.98) Flag(row, $"exchange rate at its limit ({c.ExchangeValue:0.00})");
                if (c.InflationRate >= EconomyConfig.MaxInflation.Value * 0.95 || c.InflationRate <= EconomyConfig.MinInflation.Value * 0.95) Flag(row, $"inflation at its limit ({c.InflationRate:P1}/turn)");
                if (c.PriceIndex > 20 || c.PriceIndex < 0.05) Flag(row, $"price index {c.PriceIndex:0.00} (accumulated prices off the curve)");
                if (c.InterestRate >= EconomyConfig.MaxInterest.Value * 0.98) Flag(row, $"interest at the ceiling ({c.InterestRate:P1}/turn)");
                if (e.MoneyNet > 0 && c.LastInterestFlow > 0.5 * e.MoneyNet) Flag(row, $"interest yields {c.LastInterestFlow:0}/turn = {c.LastInterestFlow / e.MoneyNet:P0} of the game income (financial income dominates)");
                if (c.LastInterestFlow < -0.5 * Math.Max(20, Math.Abs(e.MoneyNet))) Flag(row, $"interest eats {-c.LastInterestFlow:0}/turn of the treasury");
                for (int h = 1; h < c.History.Count; h++)
                {
                    double before = c.History[h - 1].ExchangeValue, after = c.History[h].ExchangeValue;
                    if (before > 0 && (after / before > 1.5 || after / before < 0.67))
                    {
                        Flag(row, $"exchange rate jump on turn {c.History[h].Turn}: {before:0.00} → {after:0.00}");
                        break;
                    }
                }
            }

            var text = new StringBuilder();
            text.AppendLine($"Turn {capture.Turn} · {rows.Count} living empires · medians: treasury {medMoney:0}, income {medNet:0}/t, industry {medIndustry:0}/t, science {medScience:0}/t, army {medMilitary:0}, fame {medFame:0}");
            text.AppendLine(alerts.Count == 0 ? "No alerts." : $"ALERTS ({alerts.Count}):\n- " + string.Join("\n- ", alerts));
            text.AppendLine("E   culture              era treasury income  ind   sci    influ  army   stab  | exch   infl%  int%   prices GDP    intFlw  credit pressure infPrs% -stbInf -unemp | debtInf% rule%  hoard%");
            var csv = new StringBuilder("empire;culture;era;money;moneyNet;industry;science;influence;influenceNet;military;fame;territories;population;stability;exchange;inflation;interest;priceIndex;gdp;strength;interestFlow;incomeEffect;creditFactor;pressure;pressureInflation;stabilityPenalty;unemploymentPenalty;forcedInterest;exchangeMin60;exchangeMax60;inflationMax60;interestMax60;debtInflation;ruleRate;hoardingInflation\n");
            foreach (EconomyRow row in rows.OrderByDescending(r => r.Currency?.Strength ?? 0))
            {
                Diplomacy.Capture.CapturedEmpire e = row.Empire;
                EmpireCurrency c = row.Currency ?? new EmpireCurrency();
                string culture = (e.Culture ?? "?").Length > 20 ? e.Culture.Substring(0, 20) : (e.Culture ?? "?");
                text.AppendLine($"E{e.Index,-2} {culture,-20} {e.EraIndex,3} {e.Money,8:0} {e.MoneyNet,7:0} {row.Industry,5:0} {e.Science,6:0} {e.Influence,6:0} {e.MilitaryStrength,6:0} {e.Stability,5:0}% | {c.ExchangeValue,6:0.00} {c.InflationRate * 100,5:0.00} {c.InterestRate * 100,6:0.00} {c.PriceIndex,6:0.00} {c.Gdp,6:0} {c.LastInterestFlow,7:0} {c.CreditFactor,7:0.00} {c.Pressure * 100,6:0}% {c.CoverageInflation * 100,7:0.00} {c.LastStabilityPenalty,6:0.0} {c.LastUnemploymentPenalty,6:0.0} | {c.InflationRateFromDebt * 100,6:0.00} {EconomySimulation.TaylorRate(c.InflationRate, c.Pressure, c.InflationRateFromDebt) * 100,6:0.00} {c.InflationFromHoarding * 100,6:0.00}{(c.ForcedInterest ? " (forced interest)" : string.Empty)}");
                double exMin = c.History.Count > 0 ? c.History.Min(h => h.ExchangeValue) : c.ExchangeValue;
                double exMax = c.History.Count > 0 ? c.History.Max(h => h.ExchangeValue) : c.ExchangeValue;
                double inMax = c.History.Count > 0 ? c.History.Max(h => h.InflationRate) : c.InflationRate;
                double irMax = c.History.Count > 0 ? c.History.Max(h => h.InterestRate) : c.InterestRate;
                csv.AppendLine(string.Join(";", new object[] { e.Index, e.Culture, e.EraIndex, e.Money, e.MoneyNet, row.Industry, e.Science, e.Influence, e.InfluenceNet, e.MilitaryStrength, e.Fame, e.TerritoryCount, e.Population, e.Stability, c.ExchangeValue, c.InflationRate, c.InterestRate, c.PriceIndex, c.Gdp, c.Strength, c.LastInterestFlow, c.LastIncomeEffect, c.CreditFactor, c.Pressure, c.CoverageInflation, c.LastStabilityPenalty, c.LastUnemploymentPenalty, c.ForcedInterest ? 1 : 0, exMin, exMax, inMax, irMax, c.InflationRateFromDebt, EconomySimulation.TaylorRate(c.InflationRate, c.Pressure, c.InflationRateFromDebt), c.InflationFromHoarding }
                    .Select(v => v is double d ? d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))));
            }
            try
            {
                string file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DevTools.ResultPath), $"economy_T{capture.Turn}.csv");
                System.IO.File.WriteAllText(file, csv.ToString(), Encoding.UTF8);
                text.AppendLine("CSV: " + file);
            }
            catch (Exception ex)
            {
                text.AppendLine("CSV not written: " + ex.Message);
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Pins a nation's interest rate (AI ones too) to compare balance strategies: "game interest E3 5" locks it
        /// at 5% per turn; "game interest E3 auto" restores the automatic rule.
        /// </summary>
        private static string ForceInterest(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            CurrencyWorld world = CurrencyManager.Current;
            if (world == null || parts.Length < 2 || !TryEmpire(parts[0], out int empire))
            {
                return "usage: game interest <E#> <percent per turn, e.g. 5 or 0.5>|auto";
            }
            string answer;
            lock (CurrencyManager.Lock)
            {
                EmpireCurrency currency = world.Get(empire);
                if (currency == null)
                {
                    return "error: nation without a currency";
                }
                if (parts[1].Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    currency.ForcedInterest = false;
                    currency.AutoInterest = true;
                    answer = $"E{empire}: automatic interest again (now {currency.InterestRate:P2})";
                }
                else if (double.TryParse(parts[1].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                {
                    double rate = Math.Max(EconomyConfig.MinInterest.Value, Math.Min(EconomyConfig.MaxInterest.Value, percent / 100.0));
                    currency.InterestRate = rate;
                    currency.ForcedInterest = true;
                    currency.AutoInterest = false;
                    double target = EconomySimulation.CreditTarget(rate, currency.InflationRate);
                    answer = $"E{empire}: interest fixed at {rate:P2} per turn → industry moves toward ×{target:0.00} over a few turns (now ×{currency.CreditFactor:0.00})";
                }
                else
                {
                    return "error: invalid value";
                }
            }
            EconomySimulation.ResetEffects();
            return answer;
        }

        private sealed class EconomyRow
        {
            public Diplomacy.Capture.CapturedEmpire Empire;
            public EmpireCurrency Currency;
            public double Industry;
        }

        private static readonly string[] WorldSizes = { "Tiny", "Small", "Normal", "Large", "Huge" };
        private static string savedSlotCount;
        private static string savedWorldSize;
        private static bool lobbyOptionsSaved;

        /// <summary>
        /// New game without clicking (tests): writes the number of players and the map size into the lobby options (the
        /// game's registry) and uses the main menu's quick start, which starts directly with those options
        /// (SessionState_LobbyOwner reads GameOption_SlotCount). "game new restore" restores the previous options.
        /// </summary>
        /// <summary>
        /// "game host [public]": creates an online multiplayer lobby, private by default (same path as the Multiplayer
        /// screen's "New game"). Lets the multiplayer code run online with a single machine.
        /// </summary>
        private static string HostOnline(string args)
        {
            if (Diplomacy.GameAccess.TryGetSession(out _, out _, out _, allowOnline: true))
            {
                return "error: go back to the main menu first";
            }
            var runtime = Services.GetService<Amplitude.Framework.Runtime.IRuntimeService>();
            if (runtime?.Runtime?.FiniteStateMachine == null)
            {
                return "error: the game has not finished starting yet";
            }
            var type = (args ?? string.Empty).Trim().Equals("public", StringComparison.OrdinalIgnoreCase)
                ? Amplitude.Framework.Matchmaking.LobbyType.Public
                : Amplitude.Framework.Matchmaking.LobbyType.Private;
            runtime.Runtime.FiniteStateMachine.PostStateChange(typeof(Amplitude.Mercury.Runtime.RuntimeState_Staging),
                new Amplitude.Mercury.Runtime.RuntimeStateLobbyTypeParameter(type));
            return $"creating a {type} online lobby; then 'game lobby ready'";
        }

        /// <summary>"game lobby ready": marks the local player ready in the lobby (the Start button); "game lobby": lobby state.</summary>
        private static string Lobby(string args)
        {
            Amplitude.Mercury.UI.LobbyScreen screen = Amplitude.Mercury.UI.Helpers.WindowsUtils.GetWindow<Amplitude.Mercury.UI.LobbyScreen>();
            if (screen == null)
            {
                return "error: no lobby screen";
            }
            if ((args ?? string.Empty).Trim().Equals("ready", StringComparison.OrdinalIgnoreCase))
            {
                screen.SetLocalPlayerReady(ready: true);
                return "local player ready";
            }
            var session = Services.GetService<Amplitude.Framework.Session.ISessionService>()?.Session;
            return $"lobby shown {screen.Shown}; session {(session == null ? "none" : session.SessionMode + ", hosting " + session.IsHosting)}";
        }

        private static string NewGame(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Amplitude.Framework.Registry registry = Amplitude.Framework.Application.Registry;
            string slotPath = Amplitude.Mercury.Options.GameOptionsManager.BuildRegistryPath("GameOption_SlotCount");
            string sizePath = Amplitude.Mercury.Options.GameOptionsManager.BuildRegistryPath("GameOption_WorldSize");
            if (parts.Length > 0 && parts[0].Equals("restore", StringComparison.OrdinalIgnoreCase))
            {
                // "game new restore 10 Large" writes explicit values (a core reload loses the stored ones).
                if (parts.Length >= 3 && int.TryParse(parts[1], out int explicitPlayers))
                {
                    string explicitSize = WorldSizes.FirstOrDefault(s => s.Equals(parts[2], StringComparison.OrdinalIgnoreCase)) ?? "Large";
                    registry.SetValue(slotPath, explicitPlayers.ToString());
                    registry.SetValue(sizePath, explicitSize);
                    lobbyOptionsSaved = false;
                    return $"lobby options written: {explicitPlayers} players, map {explicitSize}";
                }
                if (!lobbyOptionsSaved)
                {
                    return "nothing to restore (game new was not used in this session); use game new restore <players> <size>";
                }
                registry.SetValue(slotPath, savedSlotCount ?? "6");
                registry.SetValue(sizePath, savedWorldSize ?? "Large");
                lobbyOptionsSaved = false;
                return $"lobby options restored: {savedSlotCount ?? "6"} players, map {savedWorldSize ?? "Large"}";
            }
            if (parts.Length == 0 || !int.TryParse(parts[0], out int players) || players < 2 || players > 16)
            {
                return "usage: game new <players 2-16> [Tiny|Small|Normal|Large|Huge] | game new restore [<players> <size>]";
            }
            string size = parts.Length > 1 ? WorldSizes.FirstOrDefault(s => s.Equals(parts[1], StringComparison.OrdinalIgnoreCase)) : null;
            if (parts.Length > 1 && size == null)
            {
                return "error: unknown size (use Tiny, Small, Normal, Large or Huge)";
            }
            if (Diplomacy.GameAccess.TryGetSession(out _, out _, out _, allowOnline: true))
            {
                return "error: go back to the main menu first";
            }
            var runtime = Services.GetService<Amplitude.Framework.Runtime.IRuntimeService>();
            if (runtime?.Runtime?.FiniteStateMachine == null)
            {
                return "error: the game has not finished starting yet";
            }
            if (!lobbyOptionsSaved)
            {
                registry.TryGetValue(slotPath, out savedSlotCount);
                registry.TryGetValue(sizePath, out savedWorldSize);
                lobbyOptionsSaved = true;
            }
            registry.SetValue(slotPath, players.ToString());
            if (size != null)
            {
                registry.SetValue(sizePath, size);
            }
            // Same path as the main menu's "Quick start" button (MainMenuScreen.QuickStartButton_LeftClick).
            runtime.Runtime.FiniteStateMachine.PostStateChange(typeof(Amplitude.Mercury.Runtime.RuntimeState_Staging), new Amplitude.Mercury.Runtime.RuntimeStateQuickStartParameter());
            return $"starting a new game: {players} players{(size != null ? ", map " + size : string.Empty)} (before: {savedSlotCount ?? "?"} players, {savedWorldSize ?? "?"}; use 'game new restore' afterwards)";
        }

        private static bool TryEmpire(string token, out int empire)
        {
            empire = -1;
            return token != null && token.StartsWith("E", StringComparison.OrdinalIgnoreCase) && int.TryParse(token.Substring(1), out empire)
                && empire >= 0 && empire < Sandbox.NumberOfMajorEmpires;
        }

        /// <summary>Grievances and demands for each pair of nations (reads the simulation on the main thread: tests only).</summary>
        private static string Crises(string args)
        {
            if (SandboxManager.Sandbox == null)
            {
                return "error: no game loaded";
            }
            int only = TryEmpire(args?.Trim(), out int filter) ? filter : -1;
            int turn = SandboxManager.Sandbox.Turn;
            var lines = new StringBuilder();
            for (int a = 0; a < Sandbox.NumberOfMajorEmpires; a++)
            {
                Amplitude.Mercury.Simulation.MajorEmpire owner = Sandbox.MajorEmpires[a];
                if (!owner.IsAlive)
                {
                    continue;
                }
                for (int b = 0; b < Sandbox.NumberOfMajorEmpires; b++)
                {
                    if (a == b || (only >= 0 && a != only && b != only) || !Sandbox.MajorEmpires[b].IsAlive)
                    {
                        continue;
                    }
                    Amplitude.Mercury.Simulation.DiplomaticRelation relation = Sandbox.DiplomaticAncillary.GetRelationFor(a, b);
                    Amplitude.Mercury.Simulation.DiplomaticAmbassy ambassy = relation.GetEmpireEmbassy(a);
                    var items = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < ambassy.AvailableGrievances.Count; i++)
                    {
                        int pool = ambassy.AvailableGrievances[i];
                        ref DiplomaticGrievanceInfo info = ref owner.DepartmentOfForeignAffairs.GrievanceAllocator.GetReferenceAt(pool);
                        string flags = relation.DiplomaticState.CheckPrerequisitesFor(Amplitude.Mercury.Data.Simulation.DiplomaticGrievanceAction.CreateDemand, a, pool).ToString();
                        items.Add($"G{pool} {info.GrievanceType} ({info.DemandGainType}, {info.MaxDuration - (turn - info.TurnWhenObserved)}t{(info.DemandIndex >= 0 ? ", already demanded" : string.Empty)}{(info.Renounced ? ", forgiven" : string.Empty)}; {flags})");
                    }
                    for (int i = 0; i < ambassy.OnGoingDemands.DemandIndexesCount; i++)
                    {
                        int index = ambassy.OnGoingDemands.DemandIndexes[i];
                        ref DiplomaticDemandInfo demand = ref owner.DepartmentOfForeignAffairs.DemandsAllocator.GetReferenceAt(index);
                        items.Add($"DEMAND {demand.GrievanceType} ({demand.DemandGainType}, turn {demand.CreationTurn})");
                    }
                    if (items.Count > 0)
                    {
                        lines.AppendLine($"E{a} → E{b} [{relation.CurrentState}, crisis {relation.DiplomaticState.Crisis.CrisisStatus}{(ambassy.HasStalled ? ", stalled" : string.Empty)}]: {string.Join(" · ", items)}");
                    }
                }
            }
            return lines.Length > 0 ? lines.ToString().TrimEnd() : "no grievances or demands";
        }

        /// <summary>The player turns grievances into demands (tests of the language-model AI's answers).</summary>
        private static string Demand(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (SandboxManager.Sandbox == null || parts.Length < 2 || !TryEmpire(parts[0], out int other))
            {
                return "usage: game demand <E#> all|G# [G# ...]";
            }
            int me = SandboxManager.Sandbox.LocalEmpireIndex;
            Amplitude.Mercury.Simulation.BaseDiplomaticState state = Sandbox.DiplomaticAncillary.GetRelationFor(me, other).DiplomaticState;
            var action = Amplitude.Mercury.Data.Simulation.DiplomaticGrievanceAction.CreateDemand;
            if (parts[1].Equals("all", StringComparison.OrdinalIgnoreCase) || Diplomacy.DecisionParser.IsAll(parts[1]))
            {
                int usable = state.CheckPrerequisitesFor(action, me);
                if (usable <= 0)
                {
                    return "no grievance can become a demand right now";
                }
                SandboxManager.PostOrder(new OrderExecuteGrievanceActionBatch { GrievanceAction = action, OtherEmpireIndex = other });
                return $"{usable} demand(s) sent against E{other}";
            }
            var sent = new System.Collections.Generic.List<string>();
            foreach (string code in parts.Skip(1))
            {
                if (!int.TryParse(code.TrimStart('G', 'g'), out int pool))
                {
                    continue;
                }
                DiplomaticGrievanceActionFailureFlags flags = state.CheckPrerequisitesFor(action, me, pool);
                if (flags != DiplomaticGrievanceActionFailureFlags.None)
                {
                    sent.Add($"G{pool}: {Diplomacy.Capture.TurnCapture.GrievanceBlock(flags)}");
                    continue;
                }
                SandboxManager.PostOrder(new OrderExecuteGrievanceAction { GrievanceAction = action, OtherEmpireIndex = other, GrievanceIndex = pool });
                sent.Add($"G{pool}: sent");
            }
            return sent.Count > 0 ? string.Join("; ", sent) : "error: no valid G code";
        }

        /// <summary>The player answers a nation's demands, or withdraws their own (tests).</summary>
        private static string AnswerDemands(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (SandboxManager.Sandbox == null || parts.Length < 2 || !TryEmpire(parts[0], out int other))
            {
                return "usage: game demands <E#> accept|refuse|stall|withdraw";
            }
            Amplitude.Mercury.Data.Simulation.DiplomaticAction action;
            switch (parts[1].ToLowerInvariant())
            {
                case "accept": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.AcceptDemands; break;
                case "refuse": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.RefuseDemands; break;
                case "stall": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.StallForTime; break;
                case "withdraw": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.WithdrawDemands; break;
                default: return "error: unknown answer (accept|refuse|stall|withdraw)";
            }
            DiplomaticActionFailureFlags flags = Sandbox.MajorEmpires[SandboxManager.Sandbox.LocalEmpireIndex].DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, action);
            if (flags != DiplomaticActionFailureFlags.None)
            {
                return "the game does not allow it: " + Diplomacy.ActionExecutor.Explain(flags);
            }
            SandboxManager.PostOrder(new OrderDiplomaticAction { OtherEmpireIndex = other, DiplomaticAction = action });
            return $"sent to E{other}: {action}";
        }

        private static string EndTurn()
        {
            if (!Diplomacy.GameAccess.TryGetSession(out _, out int turn, out _, allowOnline: true))
            {
                return "error: no game loaded";
            }
            if (!Diplomacy.GameAccess.IsTurnMain())
            {
                return $"error: the game is not in the main turn state ({Diplomacy.GameAccess.SandboxStateName()})";
            }
            if (!Snapshots.GameSnapshot.PresentationData.LocalEmpireInfo.CanEndTurn)
            {
                return $"error: the game does not allow ending turn {turn} now (mandatory decision pending?)";
            }
            // Same request as the end-turn button (EndTurnWindow.TryEndTurn).
            SandboxManager.PostOrder(new OrderEmpireReady());
            // A proposal awaiting the player holds the end of turn until they answer.
            string pending = Proposals();
            return pending.Contains("you answer")
                ? $"turn {turn} ended, but the game will wait for you to answer:\n{pending}\n(use: game answer <E#> accept|ignore)"
                : $"turn {turn} ended (if it does not advance, see game stuck; a battle against you waits for game battle auto)";
        }
    }
}
