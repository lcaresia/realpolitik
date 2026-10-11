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
    ///   jogo estado              game state, turn and whether the turn can be ended
    ///   jogo saves               lists the saves (newest first)
    ///   jogo carregar [title]    loads the save by title; without a title, the most recent
    ///   jogo salvar &lt;title&gt;      saves the game with that title
    ///   jogo turno               ends the turn (OrderEmpireReady), like the button
    ///   jogo propostas           pending treaties and agreements between the player and each nation
    ///   jogo responder &lt;E#&gt; aceitar|ignorar   answers, for the player, a pending proposal from that nation
    ///   jogo propor &lt;E#&gt; economico|informacao|cultural|militar|alianca|paz   formal proposal by the player (tests)
    ///   jogo crises [E#]         grievances (G = in-game index), demands and crisis of all relations (or of one nation)
    ///   jogo exigir &lt;E#&gt; todas|G#...   the player turns grievances into demands against the nation (tests)
    ///   jogo exigencias &lt;E#&gt; aceitar|recusar|enrolar|retirar   the player answers the demands (or withdraws their own)
    ///   jogo travado [n]         why end of turn does not finish (pending items, battles, decisions) and the last n orders
    ///   jogo batalha [auto|recuar]   ongoing battles; answers, for the player, those awaiting their confirmation
    /// </summary>
    internal static class GameCommands
    {
        /// <summary>
        /// "jogo posto E# T#": founds an outpost for the empire in a free territory, using the game's own editor order
        /// (EditorOrderCreateCampAt). Used to build test scenarios (e.g. a route crossing two territories of the
        /// same owner, for the inner-outpost toll).
        /// </summary>
        private static string SpawnCamp(string args)
        {
            string[] items = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (items.Length < 2 || !int.TryParse(items[0].TrimStart('E', 'e'), out int owner) || !int.TryParse(items[1].TrimStart('T', 't'), out int territory))
            {
                return "uso: jogo posto E# T#";
            }
            Amplitude.Mercury.Simulation.World world = Amplitude.Mercury.Sandbox.Sandbox.World;
            if (world == null || owner < 0 || owner >= Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires)
            {
                return "erro: sem partida ou império inválido";
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
                return $"ok: posto de E{owner} pedido no tile {tile} de T{territory} (confira com 'jogo rotas detalhe')";
            }
            return $"erro: nenhum tile de terra livre em T{territory}";
        }

        /// <summary>
        /// "jogo espiao E# T# [unit]": creates a spy army for that empire on a free land tile of the territory, using
        /// the game's own debug order (OrderSpawnArmy, the same as the editor's create-unit shortcut).
        /// Used to test letter interception. Default: the medieval spy (LandUnit_Era3_Common_Spies).
        /// </summary>
        private static string SpawnSpy(string args)
        {
            string[] items = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (items.Length < 2 || !int.TryParse(items[0].TrimStart('E', 'e'), out int owner) || !int.TryParse(items[1].TrimStart('T', 't'), out int territory))
            {
                return "uso: jogo espiao E# T# [unidade]";
            }
            string unit = items.Length > 2 ? items[2] : "LandUnit_Era3_Common_Spies";
            Amplitude.Mercury.Simulation.World world = Amplitude.Mercury.Sandbox.Sandbox.World;
            if (world == null || owner < 0 || owner >= Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires)
            {
                return "erro: sem partida ou império inválido";
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
                return $"erro: nenhum tile de terra livre em T{territory}";
            }
            SandboxManager.PostOrder(new OrderSpawnArmy
            {
                WorldPosition = new Amplitude.Mercury.WorldPosition(chosen),
                UnitDefinitions = new[] { new Amplitude.StaticString(unit) },
            }, owner);
            return $"ok: {unit} de E{owner} pedido no tile {chosen} de T{territory} (confira com 'ia foto' e 'ia espionagem')";
        }

        internal static string Execute(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, 2);
            string command = parts[0].ToLowerInvariant();
            string rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            switch (command)
            {
                case "estado":
                    return State();
                case "saves":
                    return rest == "reler" ? RescanSaves() : ListSaves();
                case "carregar":
                    return Load(rest);
                case "salvar":
                    return Save(rest);
                case "turno":
                    return EndTurn();
                case "propostas":
                    return Proposals();
                case "responder":
                    return Answer(rest);
                case "propor":
                    return Propose(rest);
                case "crises":
                    return Crises(rest);
                case "exigir":
                    return Demand(rest);
                case "exigencias":
                    return AnswerDemands(rest);
                case "travado":
                    return Stuck(rest);
                case "batalha":
                    return Battles(rest);
                case "humor":
                    return Moods(rest);
                case "novo":
                    return NewGame(rest);
                case "espectador":
                    return Spectator(rest);
                case "sair":
                    // Quits the game the normal way: killing the process leaves Steam thinking it is still running (AppError_16).
                    UnityEngine.Application.Quit();
                    return "fechando o jogo";
                case "economia":
                    return EconomyAudit();
                case "juros":
                    return ForceInterest(rest);
                case "rpn":
                    return DumpRpn(rest);
                case "detalhe":
                    return Breakdown(rest);
                case "interacao":
                    return Interaction(rest);
                case "foco":
                    return FocusField(rest);
                case "espiao":
                    return SpawnSpy(rest);
                case "posto":
                    return SpawnCamp(rest);
                case "camera":
                    return CenterCamera(rest);
                case "zoom":
                    return SetZoom(rest);
                case "rendicao":
                    return Surrender(rest);
                case "congresso":
                    return Congress(rest);
                case "rotas":
                {
                    string what = (rest ?? string.Empty).Trim();
                    if (what == "rotulos")
                    {
                        // "jogo rotas rotulos": the value labels from the last render (tile and text), to aim the camera.
                        var labels = RouteVisuals.Labels;
                        return labels == null || labels.Count == 0 ? "nenhum rótulo (abra a visão de comércio)"
                            : string.Join("\n", labels.Select(l => $"tile {l.Tile}: {l.Text}{(l.Paid ? " (pago)" : string.Empty)}"));
                    }
                    if (what.StartsWith("cores"))
                    {
                        // "jogo rotas cores E5": colors the routes as if the player were E5 (test); "jogo rotas cores" goes back to normal.
                        string arg = what.Substring(5).Trim().TrimStart('E', 'e');
                        RouteVisuals.LabelAll = arg == "todas";
                        RouteVisuals.ViewAs = int.TryParse(arg, out int viewAs) ? viewAs : -1;
                        return RouteVisuals.ViewAs >= 0 ? $"cores das rotas vistas pelo E{RouteVisuals.ViewAs} (saia e entre na visão de comércio)" : "cores das rotas do jogador local";
                    }
                    return TradeBlockade.Describe(what == "detalhe");
                }
                case "civico":
                {
                    // "jogo civico E# CivicName": an empire's current law (active choice, status, turn of the last change);
                    // checks what happened to a law imposed by the Congress.
                    string[] words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length < 2 || !TryEmpire(words[0], out int holder))
                    {
                        return "uso: jogo civico E# NomeDoCivico";
                    }
                    Amplitude.Mercury.Simulation.DepartmentOfDevelopment development = Sandbox.MajorEmpires[holder].DepartmentOfDevelopment;
                    int civicIndex = development.GetCivicIndex(new Amplitude.StaticString(words[1]));
                    if (civicIndex < 0)
                    {
                        return $"erro: cívico {words[1]} não encontrado";
                    }
                    Amplitude.Mercury.Simulation.Civic civic = development.Civics.Data[civicIndex];
                    string choice = civic.ActiveChoiceName.ToString();
                    return $"E{holder} {words[1]}: {civic.CivicStatus} · escolha {(string.IsNullOrEmpty(choice) ? "nenhuma" : choice)} · última troca no turno {civic.TurnOnLastChoice}";
                }
                case "comprar":
                {
                    // "jogo comprar E# luxo|estrategico": the player buys all of the empire's resources in that category (the
                    // trade "buy all" button). Creates the route if there is none: tests a new route's path.
                    string[] words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length < 2 || !TryEmpire(words[0], out int seller))
                    {
                        return "uso: jogo comprar E# luxo|estrategico [comprador E#]";
                    }
                    var category = words[1].StartsWith("estr") ? Amplitude.Mercury.Data.Simulation.ResourceCategory.Strategic : Amplitude.Mercury.Data.Simulation.ResourceCategory.Luxury;
                    var buy = new Amplitude.Mercury.Interop.OrderBuyAllTradeResourceAccesses { OtherEmpireIndex = seller, ResourceCategory = category };
                    if (words.Length > 2 && TryEmpire(words[2], out int buyer))
                    {
                        SandboxManager.PostOrder(buy, buyer);
                        return $"ok: E{buyer} compra {category} de E{seller} (veja o log \"Rota E…: StartTrade\" e jogo rotas detalhe)";
                    }
                    SandboxManager.PostOrder(buy);
                    return $"ok: compra de {category} de E{seller} enviada (veja o log \"Rota E…: StartTrade\" e jogo rotas detalhe)";
                }
                case "pedagio":
                {
                    // "jogo pedagio E# E# livre|pedagio|bloqueio": the first applies the mode to the second's routes in all of
                    // its territories (tests; the same as the outpost window does for the player).
                    string[] words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    CurrencyWorld world = CurrencyManager.Current;
                    if (words.Length == 2 && words[0] == "peso" && double.TryParse(words[1].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double weight))
                    {
                        // "jogo pedagio peso n": PesoPedagioNasRotas (0 = the route ignores the toll and pays; 1 = detours if cheaper).
                        EconomyConfig.TollDetourCost.Value = weight;
                        TradePolicy.PathsDirty = true;
                        return $"ok: PesoPedagioNasRotas = {weight} (as rotas são recalculadas)";
                    }
                    if (words.Length >= 6 && words[0] == "teste" && TryEmpire(words[1], out int testOwner) && TryEmpire(words[4], out int testLeft) && TryEmpire(words[5], out int testRight)
                        && int.TryParse(words[2].TrimStart('T', 't'), out int freePost) && int.TryParse(words[3].TrimStart('T', 't'), out int tolledPost))
                    {
                        // "jogo pedagio teste E# T_free T_taxed E_left E_right [price]": step cost of the inner outpost.
                        double testPrice = words.Length > 6 && double.TryParse(words[6], out double p) ? p : 50;
                        return TradeBlockade.SelfTestInnerPost(testOwner, freePost, tolledPost, testLeft, testRight, testPrice);
                    }
                    if (words.Length == 4 && words[0] == "preco" && TryEmpire(words[1], out int seller) && TryEmpire(words[2], out int buyer) && world != null
                        && double.TryParse(words[3], out double general))
                    {
                        // "jogo pedagio preco E# E# n": general price from the first to the second (0 = default), like the diplomacy buttons.
                        lock (CurrencyManager.Lock)
                        {
                            TradePolicy.SetGeneralPrice(world, seller, buyer, general);
                        }
                        return $"ok: E{seller} cobra de E{buyer} {(general > 0 ? TradePolicy.ClampPrice(general).ToString() : "o padrão")} por recurso em todos os postos";
                    }
                    if (words.Length < 3 || !TryEmpire(words[0], out int owner) || !TryEmpire(words[1], out int target) || world == null)
                    {
                        return "uso: jogo pedagio E# E# livre|pedagio|bloqueio | jogo pedagio preco E# E# n | jogo pedagio peso n";
                    }
                    TradeMode mode = words[2].StartsWith("bloq") ? TradeMode.Block : words[2].StartsWith("ped") ? TradeMode.Toll : TradeMode.Free;
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
                    return $"ok: E{owner} → E{target}: {TradePolicy.ModeName(mode)} em {territories} território(s) ({changed} mudança(s))";
                }
                case "expansao":
                {
                    string mode = rest.ToLowerInvariant();
                    if (mode == "on" || mode == "off")
                    {
                        Diplomacy.AiConfig.ExpansionInOldSaves.Value = mode == "on";
                    }
                    return Diplomacy.ExpansionSavePatch.Describe() + (mode == "on" ? " (vale no próximo carregamento de save)" : string.Empty);
                }
                case "alerta":
                    // Visual test of the Central Bank badge: "jogo alerta <text>" lights it up, "jogo alerta off" returns to diagnostics.
                    NativeUI.NativeBankButton.TestAlert = string.IsNullOrEmpty(rest) || rest == "off" ? null : rest;
                    return NativeUI.NativeBankButton.TestAlert == null ? "selo de alerta volta a seguir o diagnóstico" : $"selo de alerta forçado: {rest}";
                case "menu":
                {
                    // Leaves the game to the main menu without saving (like the game's own automation, Operations.cs:441).
                    var runtime = Services.GetService<Amplitude.Framework.Runtime.IRuntimeService>();
                    if (runtime?.Runtime?.FiniteStateMachine == null)
                    {
                        return "erro: o jogo ainda não terminou de abrir";
                    }
                    runtime.Runtime.FiniteStateMachine.PostStateChange(typeof(RuntimeState_OutGame));
                    return "voltando ao menu principal (sem salvar)";
                }
                default:
                    return "uso: jogo estado | saves | carregar [título] | salvar <título> | turno | propostas | responder <E#> aceitar|ignorar"
                        + " | propor <E#> <tipo> | crises [E#] | exigir <E#> todas|G# | exigencias <E#> aceitar|recusar|enrolar|retirar";
            }
        }

        /// <summary>
        /// "jogo detalhe industria|estabilidade|dinheiro [n]": asks the game for the native breakdown, the same as the tooltips
        /// (industry and stability of the player's n-th city; the empire's money), and writes the lines the way the game
        /// builds them. The answer arrives later, in its own result.txt entry.
        /// </summary>
        private static string Breakdown(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string kind = parts.Length > 0 ? parts[0].ToLowerInvariant() : "industria";
            int cityIndex = parts.Length > 1 && int.TryParse(parts[1], out int parsed) ? parsed : 0;
            if (!Diplomacy.GameAccess.TryGetSession(out _, out _, out int player))
            {
                return "erro: sem partida";
            }
            var service = Services.GetService<Amplitude.Mercury.EffectMapper.IEffectMapperService>();
            if (service == null)
            {
                return "erro: serviço de detalhamento indisponível";
            }

            var header = new StringBuilder();
            Amplitude.Mercury.Simulation.SimulationEntityGUID guid;
            string property;
            string sub = string.Empty;
            if (kind.StartsWith("din"))
            {
                guid = Snapshots.GameSnapshot.PresentationData.EmpireInfo[player].SimulationEntityGuid;
                property = NativeEffects.MoneyProperty;
                EmpireInfo info = Snapshots.GameSnapshot.PresentationData.EmpireInfo[player];
                header.AppendLine($"dinheiro do império E{player}: renda mostrada {(float)info.MoneyNet:0.#} (jogo {(float)Sandbox.MajorEmpires[player].MoneyNet.Value:0.#}, juros projetados {NativeEffects.ProjectedInterest(Sandbox.MajorEmpires[player]):+0.#;-0.#})");
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
                    return $"cidade {cityIndex} não existe (o jogador tem {cities.Count} cidades, de 0 a {cities.Count - 1})";
                }
                Amplitude.Mercury.Simulation.Settlement city = cities[cityIndex];
                guid = city.GUID;
                EconomySimulation.EmpireEffects effects = EconomySimulation.Effects(player);
                if (kind.StartsWith("est"))
                {
                    property = NativeEffects.StabilityProperty;
                    header.AppendLine($"estabilidade da cidade {cityIndex}: atual {(float)city.PublicOrderCurrent.Value:0.#}, meta do jogo {(float)city.PublicOrderTarget.Value:0.#}, meta com a economia {(float)NativeEffects.EffectiveStabilityTarget(city, NativeEffects.StabilityLoss(city)):0.#} (inflação −{effects.InflationLoss:0.#}, desemprego −{effects.UnemploymentLoss:0.#})");
                }
                else
                {
                    property = NativeEffects.ProductionProperty;
                    sub = "ProductionNet";
                    header.AppendLine($"indústria da cidade {cityIndex}: jogo {(float)city.ProductionNetAfterAffinityBonuses.Value:0.#}, com o crédito ×{effects.Credit:0.000} = {(float)Amplitude.Mercury.Simulation.DepartmentOfIndustry.ComputeProductionIncome(city):0.#}");
                }
            }

            string command = "jogo detalhe " + args + " (resposta do jogo)";
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
            return headerText + "(detalhamento pedido ao jogo; chega numa entrada própria logo abaixo)";
        }

        /// <summary>
        /// "jogo congresso ...": World Congress tests (research\congress.md §7.9).
        ///   liberar E#                forces the Congress to form (game debug orders); E# presides the 1st session
        ///   encerrar lei|K#           closes the vote immediately
        ///   consulado E#              creates the consulate in the nation's capital (editor order)
        ///   alavancagem E# E# n       gives n leverage of the first against the second (editor order)
        ///   lei E# CivicName          makes the law available to the nation (editor order)
        ///   propor V#                 the player proposes the law (when presiding)
        ///   votar lei A|B             the player votes on the law
        ///   votar K# E#               the player backs one side of the crisis
        ///   subornar lei|K# E# n      the player bribes
        ///   cumprir|guerra E#         the player answers the verdict
        /// </summary>
        private static string Congress(string args)
        {
            string[] words = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Amplitude.Mercury.Sandbox.Sandbox sandbox = SandboxManager.Sandbox;
            var congress = Amplitude.Mercury.Sandbox.Sandbox.InternationalAncillary;
            if (sandbox == null || congress == null || words.Length == 0)
            {
                return "uso: jogo congresso liberar E# | encerrar lei|K# | consulado E# | alavancagem E# E# n | lei E# Nome | propor V# | votar lei A|B | votar K# E# | subornar lei|K# E# n | cumprir|guerra E#";
            }
            int local = sandbox.LocalEmpireIndex;
            switch (words[0].ToLowerInvariant())
            {
                case "liberar":
                    return words.Length > 1 && TryEmpire(words[1], out int unlocker) ? Diplomacy.CongressFlow.ForceUnlock(unlocker) : "uso: jogo congresso liberar E#";
                case "encerrar":
                    return Diplomacy.CongressFlow.ForceEnd(words.Length > 1 ? words[1] : null);
                case "consulado":
                    return words.Length > 1 && TryEmpire(words[1], out int builder) ? Diplomacy.CongressFlow.CreateConsulate(builder) : "uso: jogo congresso consulado E#";
                case "alavancagem":
                    return words.Length > 3 && TryEmpire(words[1], out int gainer) && TryEmpire(words[2], out int other) && int.TryParse(words[3], out int delta)
                        ? Diplomacy.CongressFlow.AddLeverage(gainer, other, delta) : "uso: jogo congresso alavancagem E# E# n";
                case "lei":
                    return words.Length > 2 && TryEmpire(words[1], out int receiver) ? Diplomacy.CongressFlow.UnlockLaw(receiver, words[2]) : "uso: jogo congresso lei E# NomeDoCivico";
                case "propor":
                {
                    string civic = Diplomacy.CongressFlow.CivicForCode(words.Length > 1 ? words[1] : null);
                    if (civic == null)
                    {
                        return "uso: jogo congresso propor V# (código de lei internacional)";
                    }
                    InternationalFailureFlags flags = congress.GetStartCivicVoteFailureFlags(local, new Amplitude.StaticString(civic));
                    if (flags != InternationalFailureFlags.None)
                    {
                        return "o Congresso não aceitaria: " + Diplomacy.CongressFlow.Explain(flags);
                    }
                    SandboxManager.PostOrder(new OrderStartCivicVote { CivicName = new Amplitude.StaticString(civic) }, local);
                    return $"ok: o jogador propôs {civic}";
                }
                case "votar":
                {
                    if (words.Length < 3)
                    {
                        return "uso: jogo congresso votar lei A|B | votar K# E#";
                    }
                    OrderInternationalAction order;
                    if (words[1].Equals("lei", StringComparison.OrdinalIgnoreCase))
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
                            return $"E{side} não é lado da crise K{pool}";
                        }
                        order = new OrderInternationalAction { InternationalAction = action, CrisisVoteIndex = pool };
                    }
                    else
                    {
                        return "uso: jogo congresso votar lei A|B | votar K# E#";
                    }
                    InternationalFailureFlags flags = congress.GetInternationalActionFailureFlags(local, order);
                    if (flags != InternationalFailureFlags.None)
                    {
                        return "o Congresso não aceitaria: " + Diplomacy.CongressFlow.Explain(flags);
                    }
                    SandboxManager.PostOrder(order, local);
                    return $"ok: voto do jogador enviado ({order.InternationalAction})";
                }
                case "subornar":
                {
                    if (words.Length < 4 || !TryEmpire(words[2], out int target) || !int.TryParse(words[3], out int count) || target == local)
                    {
                        return "uso: jogo congresso subornar lei|K# E# n";
                    }
                    bool law = words[1].Equals("lei", StringComparison.OrdinalIgnoreCase);
                    int pool = -1;
                    if (!law && !Diplomacy.CongressFlow.TryCrisisCode(words[1], null, out pool))
                    {
                        return "votação inválida";
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
                        return "o Congresso não aceitaria: " + Diplomacy.CongressFlow.Explain(flags);
                    }
                    SandboxManager.PostOrder(order, local);
                    return $"ok: {count} suborno(s) do jogador contra E{target}";
                }
                case "cumprir":
                case "guerra":
                {
                    if (words.Length < 2 || !TryEmpire(words[1], out int winner))
                    {
                        return "uso: jogo congresso cumprir|guerra E#";
                    }
                    var action = words[0] == "cumprir" ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.AcceptDemands : Amplitude.Mercury.Data.Simulation.DiplomaticAction.DeclareSurpriseWar;
                    DiplomaticActionFailureFlags flags = Sandbox.MajorEmpires[local].DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(winner, action);
                    if (flags != DiplomaticActionFailureFlags.None)
                    {
                        return "o jogo não permite: " + Diplomacy.ActionExecutor.Explain(flags);
                    }
                    SandboxManager.PostOrder(new OrderDiplomaticAction { OtherEmpireIndex = winner, DiplomaticAction = action }, local);
                    return $"ok: {action} enviado a E{winner}";
                }
                default:
                    return "comando desconhecido; use jogo congresso sem argumentos para ver a lista";
            }
        }

        /// <summary>
        /// "jogo rendicao oferecer|impor|aceitar|recusar|cancelar E#": the player acts in a war as through the Crisis tab
        /// (surrender tests with the language-model AI nations). Offer and impose use the game's default terms
        /// (demands and the rest in gold).
        /// </summary>
        private static string Surrender(string args)
        {
            string[] words = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Amplitude.Mercury.Sandbox.Sandbox sandbox = SandboxManager.Sandbox;
            if (words.Length < 2 || !TryEmpire(words[1], out int other) || sandbox == null)
            {
                return "uso: jogo rendicao oferecer|impor|aceitar|recusar|cancelar E#";
            }
            int local = sandbox.LocalEmpireIndex;
            var dofa = Amplitude.Mercury.Sandbox.Sandbox.MajorEmpires[local].DepartmentOfForeignAffairs;
            var steps = new System.Collections.Generic.List<Amplitude.Mercury.Data.Simulation.DiplomaticAction>();
            switch (words[0].ToLowerInvariant())
            {
                case "oferecer":
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.StartToFillSurrenderProposition);
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeToSurrender);
                    break;
                case "impor":
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.AllowToForceOtherToSurrender);
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.DeclareSurrender);
                    break;
                case "aceitar":
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.AcceptSurrender);
                    break;
                case "recusar":
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.RefuseSurrender);
                    break;
                case "cancelar":
                    steps.Add(Amplitude.Mercury.Data.Simulation.DiplomaticAction.CancelSurrenderProposition);
                    break;
                default:
                    return "uso: jogo rendicao oferecer|impor|aceitar|recusar|cancelar E#";
            }
            var flags = dofa.GetDiplomaticActionFailureFlags(other, steps[0]);
            if (flags != Amplitude.Mercury.Interop.DiplomaticActionFailureFlags.None)
            {
                return $"erro: {steps[0]} recusada pelo jogo: {Diplomacy.ActionExecutor.Explain(flags)}";
            }
            foreach (var step in steps)
            {
                SandboxManager.PostOrder(new Amplitude.Mercury.Interop.OrderDiplomaticAction { OtherEmpireIndex = other, DiplomaticAction = step }, local);
            }
            return $"ok: {string.Join(" → ", steps)} postada(s) para E{local} na guerra com E{other}";
        }

        /// <summary>"jogo zoom 0..1": sets the camera zoom (1 = strategic view) without the mouse wheel.</summary>
        private static string SetZoom(string args)
        {
            var camera = Amplitude.Mercury.Presentation.Presentation.PresentationCameraController;
            if (camera == null || !float.TryParse((args ?? string.Empty).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
            {
                return "uso: jogo zoom 0..1 (com partida aberta; 1 = visão estratégica)";
            }
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            object mover = typeof(Amplitude.Mercury.Presentation.PresentationCameraController).GetField("mercuryCameraMover", flags)?.GetValue(camera);
            var field = typeof(AbstractMercuryCameraMover).GetField("abscissa", flags);
            if (mover == null || field == null)
            {
                return "erro: não achei o controle de zoom da câmera";
            }
            field.SetValue(mover, UnityEngine.Mathf.Clamp01(value));
            return $"zoom em {value:0.00}";
        }

        /// <summary>
        /// "jogo camera T#|tile": centers the camera on a territory (administrative center or visual center) or on a tile.
        /// </summary>
        private static string CenterCamera(string args)
        {
            var world = Amplitude.Mercury.Sandbox.Sandbox.World;
            var camera = Amplitude.Mercury.Presentation.Presentation.PresentationCameraController;
            string target = (args ?? string.Empty).Trim();
            if (world == null || camera == null || target.Length == 0)
            {
                return "uso: jogo camera T#|tile (com partida aberta)";
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
                return "erro: alvo inválido (T# ou número do tile)";
            }
            camera.CenterCameraAt(tile);
            return $"câmera no tile {tile}";
        }

        /// <summary>
        /// "jogo interacao esc|tropa|cidade|fe|sociedade|normal|estado": does what the player would do (ESC key through the
        /// game's own path, select a troop or city, open the Faith or Society menu) and shows which windows stayed
        /// open. Used to test whether the mod's windows close when the player goes to interact with something else.
        /// </summary>
        /// <summary>
        /// "jogo foco &lt;path&gt;|off": puts the cursor in a text field (like the player's click) or removes it. With the cursor
        /// in the field, "jogo interacao estado" says "digitando: sim" and the mod's shortcuts (F8, F10) are disabled.
        /// </summary>
        private static string FocusField(string args)
        {
            var manager = Amplitude.UI.Interactables.UIInteractivityManager.Instance;
            if (manager == null)
            {
                return "erro: sem interface";
            }
            string path = (args ?? string.Empty).Trim();
            if (path == "off")
            {
                manager.SetFocus();
                return "ok: foco solto";
            }
            UnityEngine.Transform target = DevTools.FindByPath(path);
            Amplitude.UI.Interactables.UITextField field = target?.GetComponentInChildren<Amplitude.UI.Interactables.UITextField>(true);
            if (field == null)
            {
                return "erro: campo de texto não encontrado em " + path;
            }
            manager.SetFocus(field.TextFieldResponder);
            return $"ok: foco em {field.name} · digitando: {(NativeUI.NativeUIKit.TypingInField() ? "sim" : "não")}";
        }

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
                case "tropa":
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
                case "cidade":
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
                case "fe":
                    banner?.RequestReligionState();
                    break;
                case "comercio":
                    cursors?.ChangeToTradeViewCursor();
                    break;
                case "aba":
                {
                    // "aba relacoes|comercio|tratados|crise": same as clicking the tab on the diplomacy screen.
                    var screen = Amplitude.Mercury.UI.Helpers.WindowsUtils.GetWindow<Amplitude.Mercury.UI.DiplomaticScreen>();
                    string[] words = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string name = words.Length > 1 ? words[1].ToLowerInvariant() : "relacoes";
                    var mode = name.StartsWith("com") ? Amplitude.Mercury.UI.DiplomaticScreenMode.Trade
                        : name.StartsWith("tra") ? Amplitude.Mercury.UI.DiplomaticScreenMode.Treaties
                        : name.StartsWith("cri") ? Amplitude.Mercury.UI.DiplomaticScreenMode.Crisis
                        : Amplitude.Mercury.UI.DiplomaticScreenMode.Relations;
                    if (screen == null || !screen.Shown)
                    {
                        return "erro: a tela de diplomacia não está aberta";
                    }
                    screen.NegociationGroup_TabSwitch(mode);
                    break;
                }
                case "diplomacia":
                {
                    // "diplomacia E3": opens the diplomacy screen with that nation (same as clicking its portrait).
                    int other = -1;
                    string[] words = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 1)
                    {
                        int.TryParse(words[1].TrimStart('E', 'e'), out other);
                    }
                    if (other < 0)
                    {
                        return "uso: jogo interacao diplomacia E#";
                    }
                    cursors?.ChangeToDiplomaticCursor(other);
                    break;
                }
                case "sociedade":
                    banner?.RequestSocietyState();
                    break;
                case "espionagem":
                    banner?.RequestStealthState();
                    break;
                case "normal":
                    cursors?.ChangeToDefaultCursor();
                    banner?.RequestNoneState();
                    break;
                case "estado":
                case "":
                    break;
                default:
                    return "uso: jogo interacao esc|tropa|cidade|fe|sociedade|normal|estado";
            }
            var text = new StringBuilder();
            if (what != "estado" && what.Length > 0)
            {
                text.AppendLine($"feito: {what} (o efeito nas janelas aparece no próximo quadro; rode 'jogo interacao estado')");
            }
            text.AppendLine($"cursor: {cursors?.CurrentCursor?.GetType().Name ?? "?"} · barra: {banner?.State.ToString() ?? "?"}");
            text.AppendLine($"Banco Central: {(NativeUI.NativeBankWindow.IsOpen ? "aberto" : "fechado")} · posto comercial: {(NativeUI.TradePostWindow.Instance?.Shown == true ? "aberto" : "fechado")} · correio: {(Diplomacy.UI.MailScreen.IsOpen ? "aberto" : "fechado")} · aba Cartas: {(Diplomacy.UI.CorrespondenceTab.Active ? "ativa" : "não")}");
            var diplomacy = Amplitude.Mercury.UI.Helpers.WindowsUtils.GetWindow<Amplitude.Mercury.UI.DiplomaticScreen>();
            if (diplomacy != null && diplomacy.Shown)
            {
                text.AppendLine($"diplomacia: nação E{Snapshots.DiplomaticCursorSnapshot.PresentationData.OtherEmpireIndex}, aba do jogo {diplomacy.currentMode}");
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
            text.AppendLine("janelas abertas (laterais e telas cheias): " + (open.Count == 0 ? "nenhuma" : string.Join(", ", open)));
            var focused = Amplitude.UI.Interactables.UIInteractivityManager.Instance?.FocusedResponder;
            text.AppendLine("foco do teclado: " + (focused == null ? "nenhum" : focused.ToString()) + $" · digitando: {(NativeUI.NativeUIKit.TypingInField() ? "sim" : "não")}");
            return text.ToString();
        }

        /// <summary>"jogo rpn &lt;name&gt;": the game's data formula (operation stack, constants and properties), to understand a calculation.</summary>
        private static string DumpRpn(string name)
        {
            var database = Databases.GetDatabase<Amplitude.Framework.Simulation.Rpn.RpnDefinition>();
            if (database == null)
            {
                return "erro: banco de fórmulas indisponível";
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Join(", ", database.GetValues().Select(d => d.Name.ToString()).OrderBy(n => n));
            }
            Amplitude.Framework.Simulation.Rpn.RpnDefinition rpn = database.GetValues().FirstOrDefault(d => string.Equals(d.Name.ToString(), name, StringComparison.OrdinalIgnoreCase));
            if (rpn == null)
            {
                return $"fórmula '{name}' não encontrada (jogo rpn sem nome lista todas)";
            }
            var text = new StringBuilder();
            text.AppendLine($"{rpn.Name} (alvo {rpn.TargetTypeName}, fonte {rpn.SourceTypeName})");
            text.AppendLine("operações: " + string.Join(" ", (rpn.RpnOperationStack ?? new Amplitude.Framework.Simulation.Operation[0]).Select(o => o.ToString())));
            text.AppendLine("constantes: " + string.Join(" ", (rpn.ConstantStack ?? new Amplitude.FixedPoint[0]).Select(c => ((float)c).ToString("0.###"))));
            text.AppendLine("propriedades: " + string.Join(" ", rpn.PropertyLocalNameStack ?? new string[0]));
            text.AppendLine("variáveis: " + string.Join(" ", rpn.VariableNameStack ?? new string[0]));
            if (!string.IsNullOrEmpty(rpn.Note))
            {
                text.AppendLine("nota: " + rpn.Note);
            }
            return text.ToString();
        }

        private static string State()
        {
            var text = new StringBuilder();
            var runtime = Services.GetService<Amplitude.Framework.Runtime.IRuntimeService>();
            text.AppendLine("runtime: " + (runtime?.Runtime?.FiniteStateMachine?.CurrentState?.GetType().Name ?? "?"));
            text.AppendLine("sandbox: " + (Diplomacy.GameAccess.SandboxStateName() ?? "(sem partida)"));
            if (Diplomacy.GameAccess.TryGetSession(out string guid, out int turn, out int player))
            {
                LocalEmpireInfo local = Snapshots.GameSnapshot.PresentationData.LocalEmpireInfo;
                text.AppendLine($"partida: {guid} · turno {turn} · jogador E{player} · pode passar o turno: {(local.CanEndTurn ? "sim" : "não")}");
                string battles = Battles(string.Empty);
                if (battles.Contains("espera você"))
                {
                    text.AppendLine(battles);
                    text.AppendLine("(o fim do turno espera essa batalha: use jogo batalha auto|recuar)");
                }
            }
            else
            {
                text.AppendLine("partida: nenhuma carregada");
            }
            return text.ToString().TrimEnd();
        }

        private static IGameSerializationService Serialization()
        {
            return Services.GetService<IGameSerializationService>() ?? throw new InvalidOperationException("serviço de saves ainda não está pronto");
        }

        /// <summary>
        /// "jogo saves reler": rereads the current saves folder (the same directory switch the saves screen's cloud button does),
        /// so the game sees a save copied while it is running. Does not change the player's local/cloud choice.
        /// </summary>
        private static string RescanSaves()
        {
            var storage = Services.GetService<Amplitude.Framework.Storage.IStorageManagementService>() as Amplitude.Framework.Storage.LocalDrive.LocalDriveStorageManager;
            if (storage == null)
            {
                return "erro: armazenamento de saves não é a pasta local";
            }
            storage.ChangeDirectory(Amplitude.Mercury.Application.GameSaveDirectory);
            return "ok: pasta relida (" + Amplitude.Mercury.Application.GameSaveDirectory + "), " + Serialization().EnumerateGameSaveFiles().Count() + " saves";
        }

        private static string ListSaves()
        {
            var saves = Serialization().EnumerateGameSaveFiles()
                .Select(f => f.StorageContainerInfo)
                .Select(info => new { info.Name, Date = Modified(info) })
                .OrderByDescending(s => s.Date)
                .Take(15)
                .ToList();
            return saves.Count == 0 ? "nenhum save" : string.Join("\n", saves.Select(s => $"{s.Date:yyyy-MM-dd HH:mm}  {s.Name}"));
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
                return serialization.LoadLatestSave() ? "carregando o save mais recente" : "erro: não achei save para carregar";
            }
            GameSaveDescriptor descriptor = serialization.RetrieveGameSaveDescriptorFromSave(title);
            if (descriptor == null)
            {
                return $"erro: save '{title}' não encontrado";
            }
            var runtime = Services.GetService<Amplitude.Framework.Runtime.IRuntimeService>();
            // Same path as the saves screen's Load button (LoadSavesScreen.OnLoadRequested), single-player game.
            runtime.Runtime.FiniteStateMachine.PostStateChange(typeof(RuntimeState_OutGame), new RuntimeStateGameSaveDescriptorParameter(descriptor));
            return $"carregando '{descriptor.Title}'";
        }

        private static string Save(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return "uso: jogo salvar <título>";
            }
            if (!Diplomacy.GameAccess.TryGetSession(out _, out _, out _))
            {
                return "erro: nenhuma partida carregada";
            }
            Serialization().SaveGame(title);
            return $"salvando '{title}'";
        }

        /// <summary>The player's pending treaties and agreements. Reads the simulation on the main thread: tests only.</summary>
        private static string Proposals()
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "erro: nenhuma partida carregada";
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
                    lines.AppendLine($"E{other}: tratado pendente ({relation.DiplomaticState.CurrentTreatyInfo.PropositionInfo.Status}) — {(mineToAnswer ? "você responde" : "proposta sua, aguardando")}");
                }
                if (relation.HasPendingAgreementProposition())
                {
                    bool mineToAnswer = mine.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, Amplitude.Mercury.Data.Simulation.DiplomaticAction.SignAgreement) == DiplomaticActionFailureFlags.None
                        || mine.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, Amplitude.Mercury.Data.Simulation.DiplomaticAction.IgnoreAgreement) == DiplomaticActionFailureFlags.None;
                    lines.AppendLine($"E{other}: acordo pendente ({relation.DiplomaticState.CurrentAgreementsInfo.PropositionInfo.Status}) — {(mineToAnswer ? "você responde" : "proposta sua, aguardando")}");
                }
            }
            return lines.Length > 0 ? lines.ToString().TrimEnd() : "nenhuma proposta pendente";
        }

        /// <summary>Answers for the player (tests): aceitar signs, ignorar refuses without insulting.</summary>
        private static string Answer(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].StartsWith("E", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[0].Substring(1), out int other))
            {
                return "uso: jogo responder <E#> aceitar|ignorar";
            }
            bool accept = parts[1].StartsWith("a", StringComparison.OrdinalIgnoreCase);
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "erro: nenhuma partida carregada";
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
            return sent.Count > 0 ? $"enviado para E{other}: {string.Join(", ", sent)}" : $"nada para responder a E{other}";
        }

        /// <summary>Formal proposal by the player, as through the diplomacy screen (tests of F3 option B).</summary>
        private static string Propose(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].StartsWith("E", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[0].Substring(1), out int other))
            {
                return "uso: jogo propor <E#> economico|informacao|cultural|militar|alianca|paz";
            }
            Amplitude.Mercury.Data.Simulation.DiplomaticAction action;
            switch (parts[1].ToLowerInvariant())
            {
                case "economico": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeEconomicalAgreement; break;
                case "informacao": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeInformationAgreement; break;
                case "cultural": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeCulturalAgreement; break;
                case "militar": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeMilitaryAgreement; break;
                case "alianca": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeAllianceTreaty; break;
                case "paz": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeEndWarTreaty; break;
                default: return "tipo desconhecido";
            }
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "erro: nenhuma partida carregada";
            }
            DiplomaticActionFailureFlags flags = Sandbox.MajorEmpires[sandbox.LocalEmpireIndex].DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, action);
            if (flags != DiplomaticActionFailureFlags.None)
            {
                return "o jogo não permite: " + Diplomacy.ActionExecutor.Explain(flags);
            }
            SandboxManager.PostOrder(new OrderDiplomaticAction { OtherEmpireIndex = other, DiplomaticAction = action });
            return $"proposta enviada a E{other}: {action}";
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
                return "erro: nenhuma partida carregada";
            }
            int count = int.TryParse(args, out int n) && n > 0 ? n : 25;
            var text = new StringBuilder();
            text.AppendLine($"estado {sandbox.CurrentStateName} · última ordem #{sandbox.OrderHistory.LastOrderSerial} · ordens desde o autosave: {sandbox.OrderHistory.Length}");
            foreach (Amplitude.Mercury.Simulation.DiplomaticRelation relation in Sandbox.DiplomaticAncillary.DiplomaticRelations)
            {
                string pair = $"E{relation.LeftEmpireIndex}-E{relation.RightEmpireIndex}";
                if (relation.HasPendingTreatyProposition())
                {
                    var info = relation.DiplomaticState.CurrentTreatyInfo.PropositionInfo;
                    text.AppendLine($"tratado pendente {pair}: {info.Status}, de E{info.EmpireIndexWhoInitiated} para E{info.EmpireIndexWhoNeedToSign}, turno {info.TurnWhenProposed}");
                }
                if (relation.HasPendingAgreementProposition())
                {
                    var info = relation.DiplomaticState.CurrentAgreementsInfo.PropositionInfo;
                    text.AppendLine($"acordo pendente {pair}: {info.Status}, de E{info.EmpireIndexWhoInitiated} para E{info.EmpireIndexWhoNeedToSign}, turno {info.TurnWhenProposed}");
                }
                if (relation.HasPendingConsulateProposition()) text.AppendLine($"consulado pendente {pair}");
                if (relation.HasPendingSurrenderProposition()) text.AppendLine($"rendição pendente {pair}");
                if (relation.HasPendingGiftProposition()) text.AppendLine($"presente pendente {pair} (#{relation.ProposedGiftPoolIndex})");
                if (relation.EmpireWhoNeedToAnswerEnforcedDemand >= 0) text.AppendLine($"exigência forçada {pair}: E{relation.EmpireWhoNeedToAnswerEnforcedDemand} responde ({relation.DiplomaticState.Crisis.CrisisStatus})");
            }
            var log = new Amplitude.IO.StringBuilder(1024);
            bool actions = Sandbox.ActionController.ReadyForTurnFinishCompletion(SandboxImpatience.None, log);
            bool battles = Sandbox.BattleRepository.ReadyForTurnFinishCompletion(SandboxImpatience.None, log);
            bool air = Sandbox.AirStrikeRepository.ReadyForTurnFinishCompletion(SandboxImpatience.None, log);
            bool artillery = Sandbox.ArtilleryStrikeRepository.ReadyForTurnFinishCompletion(SandboxImpatience.None, log);
            bool camera = Sandbox.CameraSequenceController.ReadyForTurnFinishCompletion(SandboxImpatience.None, log);
            text.AppendLine($"prontos: ações {actions} · batalhas {battles} · ataques aéreos {air} · artilharia {artillery} · câmera {camera}");
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
                    text.AppendLine($"E{i}: humano {empire.IsControlledByHuman} · travado por decisão obrigatória {empire.IsLockedByMandatories}");
                }
            }
            System.Collections.Generic.List<Order> orders = sandbox.OrderHistory.Orders;
            text.AppendLine($"últimas {System.Math.Min(count, orders.Count)} ordens:");
            for (int i = System.Math.Max(0, orders.Count - count); i < orders.Count; i++)
            {
                Order order = orders[i];
                string extra = order is OrderDiplomaticAction diplomatic ? $" {diplomatic.DiplomaticAction} → E{diplomatic.OtherEmpireIndex}" : string.Empty;
                text.AppendLine($"#{order.Serial} E{((IBillet)order).TargetEmpireIndex} {order.OrderIdentifier}{extra}");
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Ongoing battles. With "auto" or "recuar", answers for the player those awaiting their confirmation (end of
        /// turn waits for that), like the battle screen's instant-resolve and retreat buttons.
        /// </summary>
        private static string Battles(string args)
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "erro: nenhuma partida carregada";
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
                text.Append($"batalha em {battle.AttackerGroup.OriginPosition}: E{battle.AttackerGroup.LeaderEmpireIndex} ataca E{battle.DefenderGroup.LeaderEmpireIndex} · {battle.BattleState}{(waitingForMe ? " · espera você" : string.Empty)}");
                if (waitingForMe && mode == "auto")
                {
                    SandboxManager.PostOrder(new OrderBattleSetInstantResolve(battle.GUID, me), me);
                    text.Append(" → resolução instantânea pedida");
                }
                else if (waitingForMe && mode == "recuar")
                {
                    SandboxManager.PostOrder(new OrderBattleRetreat { BattleGUID = battle.GUID, EmpireIndex = me }, me);
                    text.Append(" → recuo pedido");
                }
                text.AppendLine();
            }
            return text.Length > 0 ? text.ToString().TrimEnd() : "nenhuma batalha em andamento";
        }

        /// <summary>
        /// Each nation's native AI mood toward the others (what the diplomacy screen shows), next to the stance the
        /// language-model AI set: checks the NativeAiBias skew. Read on the main thread: tests only.
        /// </summary>
        private static string Moods(string args)
        {
            if (SandboxManager.Sandbox == null)
            {
                return "erro: nenhuma partida carregada";
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
                    items.Add($"E{b} {ambassy.CurrentAIBehaviourState} (simpatia {ambassy.AIRapportScore:0.00}, postura {posture})");
                }
                string focus = nation?.Focus?.Value ?? "-";
                text.AppendLine($"E{a} [foco {focus}]: {string.Join(" · ", items)}");
            }
            return text.Length > 0 ? text.ToString().TrimEnd() : "nenhuma nação da IA";
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
                return "uso: jogo espectador on|off";
            }
            if (!Diplomacy.GameAccess.TryGetSession(out _, out int turn, out int player))
            {
                return "erro: nenhuma partida carregada";
            }
            bool human = mode == "off";
            SandboxManager.PostOrder(new OrderSetControlledByHuman(human));
            return human
                ? $"E{player} volta para você (turno {turn})"
                : $"E{player} agora é jogado pela IA nativa: os turnos andam sozinhos a partir do turno {turn} (jogo espectador off para parar)";
        }

        /// <summary>
        /// Economy audit (balance tests): game and CurrencyMod numbers for each empire, with warnings for impossible or
        /// outlier values (NaN/infinity, exchange rate at its limit, extreme inflation or interest, jumps in the history,
        /// treasury, income, science or army far above/below the median, interest yielding more than the real economy).
        /// Writes dev\out\economia_T{turno}.csv to compare turns.
        /// </summary>
        private static string EconomyAudit()
        {
            Diplomacy.Capture.WorldCapture capture = Diplomacy.Capture.TurnCapture.CaptureFromMainThread();
            CurrencyWorld world = CurrencyManager.Current;
            if (capture == null || world == null)
            {
                return "erro: sem partida ou sem estado monetário";
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
                return "nenhum império vivo";
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
                if (values.Any(Bad)) Flag(row, "valor inválido (NaN ou infinito)");
                if (e.Money > 10 * Math.Max(200, medMoney)) Flag(row, $"tesouro {e.Money:0} é {e.Money / Math.Max(1, medMoney):0.0}× a mediana");
                if (e.Money < -2000) Flag(row, $"dívida grande ({e.Money:0})");
                if (Math.Abs(e.MoneyNet) > 5 * Math.Max(20, medNet)) Flag(row, $"renda {e.MoneyNet:0}/turno é {Math.Abs(e.MoneyNet) / Math.Max(1, medNet):0.0}× a mediana");
                if (e.Science > 5 * Math.Max(10, medScience)) Flag(row, $"ciência {e.Science:0}/turno é {e.Science / Math.Max(1, medScience):0.0}× a mediana");
                if (row.Industry > 5 * Math.Max(10, medIndustry)) Flag(row, $"produção {row.Industry:0}/turno é {row.Industry / Math.Max(1, medIndustry):0.0}× a mediana");
                if (e.MilitaryStrength > 5 * Math.Max(10, medMilitary)) Flag(row, $"exército {e.MilitaryStrength:0} é {e.MilitaryStrength / Math.Max(1, medMilitary):0.0}× a mediana");
                if (e.Influence > 10 * Math.Max(50, medInfluence)) Flag(row, $"influência {e.Influence:0} é {e.Influence / Math.Max(1, medInfluence):0.0}× a mediana");
                if (e.Fame > 4 * Math.Max(50, medFame)) Flag(row, $"fama {e.Fame:0} é {e.Fame / Math.Max(1, medFame):0.0}× a mediana");
                if (e.TerritoryCount > 4 * Math.Max(2, medTerritories)) Flag(row, $"{e.TerritoryCount} territórios ({e.TerritoryCount / Math.Max(1, medTerritories):0.0}× a mediana)");
                if (e.Stability < 15) Flag(row, $"estabilidade {e.Stability:0}%");
                if (c == null)
                {
                    Flag(row, "sem estado monetário");
                    continue;
                }
                if (c.ExchangeValue <= EconomyConfig.ExchangeMin.Value * 1.02 || c.ExchangeValue >= EconomyConfig.ExchangeMax.Value * 0.98) Flag(row, $"câmbio no limite ({c.ExchangeValue:0.00})");
                if (c.InflationRate >= EconomyConfig.MaxInflation.Value * 0.95 || c.InflationRate <= EconomyConfig.MinInflation.Value * 0.95) Flag(row, $"inflação no limite ({c.InflationRate:P1}/turno)");
                if (c.PriceIndex > 20 || c.PriceIndex < 0.05) Flag(row, $"índice de preços {c.PriceIndex:0.00} (preços acumulados fora da curva)");
                if (c.InterestRate >= EconomyConfig.MaxInterest.Value * 0.98) Flag(row, $"juros no teto ({c.InterestRate:P1}/turno)");
                if (e.MoneyNet > 0 && c.LastInterestFlow > 0.5 * e.MoneyNet) Flag(row, $"juros rendem {c.LastInterestFlow:0}/turno = {c.LastInterestFlow / e.MoneyNet:P0} da renda do jogo (renda financeira dominante)");
                if (c.LastInterestFlow < -0.5 * Math.Max(20, Math.Abs(e.MoneyNet))) Flag(row, $"juros comem {-c.LastInterestFlow:0}/turno do caixa");
                for (int h = 1; h < c.History.Count; h++)
                {
                    double before = c.History[h - 1].ExchangeValue, after = c.History[h].ExchangeValue;
                    if (before > 0 && (after / before > 1.5 || after / before < 0.67))
                    {
                        Flag(row, $"salto de câmbio no turno {c.History[h].Turn}: {before:0.00} → {after:0.00}");
                        break;
                    }
                }
            }

            var text = new StringBuilder();
            text.AppendLine($"Turno {capture.Turn} · {rows.Count} impérios vivos · medianas: tesouro {medMoney:0}, renda {medNet:0}/t, produção {medIndustry:0}/t, ciência {medScience:0}/t, exército {medMilitary:0}, fama {medFame:0}");
            text.AppendLine(alerts.Count == 0 ? "Nenhum alerta." : $"ALERTAS ({alerts.Count}):\n- " + string.Join("\n- ", alerts));
            text.AppendLine("E   povo                 era tesouro   renda  prod  ciênc  infl   exérc  estab | câmbio infl%  juros% preços  PIB    jurosR crédito pressão infPres% -estInf -desemp | dívInf% regra% parado%");
            var csv = new StringBuilder("empire;culture;era;money;moneyNet;industry;science;influence;influenceNet;military;fame;territories;population;stability;exchange;inflation;interest;priceIndex;gdp;strength;interestFlow;incomeEffect;creditFactor;pressure;pressureInflation;stabilityPenalty;unemploymentPenalty;forcedInterest;exchangeMin60;exchangeMax60;inflationMax60;interestMax60;debtInflation;ruleRate;hoardingInflation\n");
            foreach (EconomyRow row in rows.OrderByDescending(r => r.Currency?.Strength ?? 0))
            {
                Diplomacy.Capture.CapturedEmpire e = row.Empire;
                EmpireCurrency c = row.Currency ?? new EmpireCurrency();
                string culture = (e.Culture ?? "?").Length > 20 ? e.Culture.Substring(0, 20) : (e.Culture ?? "?");
                text.AppendLine($"E{e.Index,-2} {culture,-20} {e.EraIndex,3} {e.Money,8:0} {e.MoneyNet,7:0} {row.Industry,5:0} {e.Science,6:0} {e.Influence,6:0} {e.MilitaryStrength,6:0} {e.Stability,5:0}% | {c.ExchangeValue,6:0.00} {c.InflationRate * 100,5:0.00} {c.InterestRate * 100,6:0.00} {c.PriceIndex,6:0.00} {c.Gdp,6:0} {c.LastInterestFlow,7:0} {c.CreditFactor,7:0.00} {c.Pressure * 100,6:0}% {c.CoverageInflation * 100,7:0.00} {c.LastStabilityPenalty,6:0.0} {c.LastUnemploymentPenalty,6:0.0} | {c.InflationRateFromDebt * 100,6:0.00} {EconomySimulation.TaylorRate(c.InflationRate, c.Pressure, c.InflationRateFromDebt) * 100,6:0.00} {c.InflationFromHoarding * 100,6:0.00}{(c.ForcedInterest ? " (juros fixos)" : string.Empty)}");
                double exMin = c.History.Count > 0 ? c.History.Min(h => h.ExchangeValue) : c.ExchangeValue;
                double exMax = c.History.Count > 0 ? c.History.Max(h => h.ExchangeValue) : c.ExchangeValue;
                double inMax = c.History.Count > 0 ? c.History.Max(h => h.InflationRate) : c.InflationRate;
                double irMax = c.History.Count > 0 ? c.History.Max(h => h.InterestRate) : c.InterestRate;
                csv.AppendLine(string.Join(";", new object[] { e.Index, e.Culture, e.EraIndex, e.Money, e.MoneyNet, row.Industry, e.Science, e.Influence, e.InfluenceNet, e.MilitaryStrength, e.Fame, e.TerritoryCount, e.Population, e.Stability, c.ExchangeValue, c.InflationRate, c.InterestRate, c.PriceIndex, c.Gdp, c.Strength, c.LastInterestFlow, c.LastIncomeEffect, c.CreditFactor, c.Pressure, c.CoverageInflation, c.LastStabilityPenalty, c.LastUnemploymentPenalty, c.ForcedInterest ? 1 : 0, exMin, exMax, inMax, irMax, c.InflationRateFromDebt, EconomySimulation.TaylorRate(c.InflationRate, c.Pressure, c.InflationRateFromDebt), c.InflationFromHoarding }
                    .Select(v => v is double d ? d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))));
            }
            try
            {
                string file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DevTools.ResultPath), $"economia_T{capture.Turn}.csv");
                System.IO.File.WriteAllText(file, csv.ToString(), Encoding.UTF8);
                text.AppendLine("CSV: " + file);
            }
            catch (Exception ex)
            {
                text.AppendLine("CSV não gravado: " + ex.Message);
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Pins a nation's interest rate (AI ones too) to compare balance strategies: "jogo juros E3 5" locks it
        /// at 5% per turn; "jogo juros E3 auto" restores the automatic rule.
        /// </summary>
        private static string ForceInterest(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            CurrencyWorld world = CurrencyManager.Current;
            if (world == null || parts.Length < 2 || !TryEmpire(parts[0], out int empire))
            {
                return "uso: jogo juros <E#> <percentual por turno, ex.: 5 ou 0,5>|auto";
            }
            string answer;
            lock (CurrencyManager.Lock)
            {
                EmpireCurrency currency = world.Get(empire);
                if (currency == null)
                {
                    return "erro: nação sem moeda";
                }
                if (parts[1].Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    currency.ForcedInterest = false;
                    currency.AutoInterest = true;
                    answer = $"E{empire}: juros automáticos de novo (agora {currency.InterestRate:P2})";
                }
                else if (double.TryParse(parts[1].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                {
                    double rate = Math.Max(EconomyConfig.MinInterest.Value, Math.Min(EconomyConfig.MaxInterest.Value, percent / 100.0));
                    currency.InterestRate = rate;
                    currency.ForcedInterest = true;
                    currency.AutoInterest = false;
                    double target = EconomySimulation.CreditTarget(rate, currency.InflationRate);
                    answer = $"E{empire}: juros fixos em {rate:P2} por turno → a produção anda até ×{target:0.00} em alguns turnos (hoje ×{currency.CreditFactor:0.00})";
                }
                else
                {
                    return "valor inválido";
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
        /// (SessionState_LobbyOwner reads GameOption_SlotCount). "jogo novo restaurar" restores the previous options.
        /// </summary>
        private static string NewGame(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Amplitude.Framework.Registry registry = Amplitude.Framework.Application.Registry;
            string slotPath = Amplitude.Mercury.Options.GameOptionsManager.BuildRegistryPath("GameOption_SlotCount");
            string sizePath = Amplitude.Mercury.Options.GameOptionsManager.BuildRegistryPath("GameOption_WorldSize");
            if (parts.Length > 0 && parts[0].Equals("restaurar", StringComparison.OrdinalIgnoreCase))
            {
                // "jogo novo restaurar 10 Large" writes explicit values (a core reload loses the stored ones).
                if (parts.Length >= 3 && int.TryParse(parts[1], out int explicitPlayers))
                {
                    string explicitSize = WorldSizes.FirstOrDefault(s => s.Equals(parts[2], StringComparison.OrdinalIgnoreCase)) ?? "Large";
                    registry.SetValue(slotPath, explicitPlayers.ToString());
                    registry.SetValue(sizePath, explicitSize);
                    lobbyOptionsSaved = false;
                    return $"opções do lobby gravadas: {explicitPlayers} jogadores, mapa {explicitSize}";
                }
                if (!lobbyOptionsSaved)
                {
                    return "nada para restaurar (jogo novo não foi usado nesta sessão); use jogo novo restaurar <jogadores> <tamanho>";
                }
                registry.SetValue(slotPath, savedSlotCount ?? "6");
                registry.SetValue(sizePath, savedWorldSize ?? "Large");
                lobbyOptionsSaved = false;
                return $"opções do lobby restauradas: {savedSlotCount ?? "6"} jogadores, mapa {savedWorldSize ?? "Large"}";
            }
            if (parts.Length == 0 || !int.TryParse(parts[0], out int players) || players < 2 || players > 16)
            {
                return "uso: jogo novo <jogadores 2-16> [Tiny|Small|Normal|Large|Huge] | jogo novo restaurar";
            }
            string size = parts.Length > 1 ? WorldSizes.FirstOrDefault(s => s.Equals(parts[1], StringComparison.OrdinalIgnoreCase)) : null;
            if (parts.Length > 1 && size == null)
            {
                return "tamanho desconhecido (use Tiny, Small, Normal, Large ou Huge)";
            }
            if (Diplomacy.GameAccess.TryGetSession(out _, out _, out _))
            {
                return "erro: saia para o menu principal antes";
            }
            var runtime = Services.GetService<Amplitude.Framework.Runtime.IRuntimeService>();
            if (runtime?.Runtime?.FiniteStateMachine == null)
            {
                return "erro: o jogo ainda não terminou de abrir";
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
            return $"começando partida nova: {players} jogadores{(size != null ? ", mapa " + size : string.Empty)} (antes: {savedSlotCount ?? "?"} jogadores, {savedWorldSize ?? "?"}; use 'jogo novo restaurar' depois)";
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
                return "erro: nenhuma partida carregada";
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
                        items.Add($"G{pool} {info.GrievanceType} ({info.DemandGainType}, {info.MaxDuration - (turn - info.TurnWhenObserved)}t{(info.DemandIndex >= 0 ? ", já exigida" : string.Empty)}{(info.Renounced ? ", perdoada" : string.Empty)}; {flags})");
                    }
                    for (int i = 0; i < ambassy.OnGoingDemands.DemandIndexesCount; i++)
                    {
                        int index = ambassy.OnGoingDemands.DemandIndexes[i];
                        ref DiplomaticDemandInfo demand = ref owner.DepartmentOfForeignAffairs.DemandsAllocator.GetReferenceAt(index);
                        items.Add($"EXIGÊNCIA {demand.GrievanceType} ({demand.DemandGainType}, turno {demand.CreationTurn})");
                    }
                    if (items.Count > 0)
                    {
                        lines.AppendLine($"E{a} → E{b} [{relation.CurrentState}, crise {relation.DiplomaticState.Crisis.CrisisStatus}{(ambassy.HasStalled ? ", enrolou" : string.Empty)}]: {string.Join(" · ", items)}");
                    }
                }
            }
            return lines.Length > 0 ? lines.ToString().TrimEnd() : "nenhuma reclamação nem exigência";
        }

        /// <summary>The player turns grievances into demands (tests of the language-model AI's answers).</summary>
        private static string Demand(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (SandboxManager.Sandbox == null || parts.Length < 2 || !TryEmpire(parts[0], out int other))
            {
                return "uso: jogo exigir <E#> todas|G# [G# ...]";
            }
            int me = SandboxManager.Sandbox.LocalEmpireIndex;
            Amplitude.Mercury.Simulation.BaseDiplomaticState state = Sandbox.DiplomaticAncillary.GetRelationFor(me, other).DiplomaticState;
            var action = Amplitude.Mercury.Data.Simulation.DiplomaticGrievanceAction.CreateDemand;
            if (Diplomacy.DecisionParser.IsAll(parts[1]))
            {
                int usable = state.CheckPrerequisitesFor(action, me);
                if (usable <= 0)
                {
                    return "nenhuma reclamação pode virar exigência agora";
                }
                SandboxManager.PostOrder(new OrderExecuteGrievanceActionBatch { GrievanceAction = action, OtherEmpireIndex = other });
                return $"{usable} exigência(s) enviada(s) contra E{other}";
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
                sent.Add($"G{pool}: enviada");
            }
            return sent.Count > 0 ? string.Join("; ", sent) : "nenhum código G válido";
        }

        /// <summary>The player answers a nation's demands, or withdraws their own (tests).</summary>
        private static string AnswerDemands(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (SandboxManager.Sandbox == null || parts.Length < 2 || !TryEmpire(parts[0], out int other))
            {
                return "uso: jogo exigencias <E#> aceitar|recusar|enrolar|retirar";
            }
            Amplitude.Mercury.Data.Simulation.DiplomaticAction action;
            switch (parts[1].ToLowerInvariant())
            {
                case "aceitar": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.AcceptDemands; break;
                case "recusar": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.RefuseDemands; break;
                case "enrolar": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.StallForTime; break;
                case "retirar": action = Amplitude.Mercury.Data.Simulation.DiplomaticAction.WithdrawDemands; break;
                default: return "resposta desconhecida";
            }
            DiplomaticActionFailureFlags flags = Sandbox.MajorEmpires[SandboxManager.Sandbox.LocalEmpireIndex].DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, action);
            if (flags != DiplomaticActionFailureFlags.None)
            {
                return "o jogo não permite: " + Diplomacy.ActionExecutor.Explain(flags);
            }
            SandboxManager.PostOrder(new OrderDiplomaticAction { OtherEmpireIndex = other, DiplomaticAction = action });
            return $"enviado a E{other}: {action}";
        }

        private static string EndTurn()
        {
            if (!Diplomacy.GameAccess.TryGetSession(out _, out int turn, out _))
            {
                return "erro: nenhuma partida carregada";
            }
            if (!Diplomacy.GameAccess.IsTurnMain())
            {
                return $"erro: o jogo não está no turno principal ({Diplomacy.GameAccess.SandboxStateName()})";
            }
            if (!Snapshots.GameSnapshot.PresentationData.LocalEmpireInfo.CanEndTurn)
            {
                return $"erro: o jogo não deixa passar o turno {turn} agora (decisão obrigatória pendente?)";
            }
            // Same request as the end-turn button (EndTurnWindow.TryEndTurn).
            SandboxManager.PostOrder(new OrderEmpireReady());
            // A proposal awaiting the player holds the end of turn until they answer.
            string pending = Proposals();
            return pending.Contains("você responde")
                ? $"turno {turn} encerrado, mas o jogo vai esperar você responder:\n{pending}\n(use: jogo responder <E#> aceitar|ignorar)"
                : $"turno {turn} encerrado (se não virar, veja jogo travado; batalha contra você espera jogo batalha auto)";
        }
    }
}
