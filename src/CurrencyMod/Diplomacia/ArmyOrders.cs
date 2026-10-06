using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury;
using Amplitude.Mercury.AI.Brain;
using Amplitude.Mercury.AI.Brain.AnalysisData.Army;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;
using Math = System.Math;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// ordem_exercito de verdade (pesquisa: research\army-orders.md). Roda na thread do sandbox, junto do
    /// ActionExecutor:
    /// - a missão é avaliada pelo mesmo avaliador do clique direito do jogador (RequestArmyActionAt), com caminho de
    ///   vários turnos, e vira OrderGoTo / OrderGoToAndCreateBattle / OrderCreateBattle / OrderGoToAndJoinBattle;
    /// - o movimento só anda sozinho dentro do turno: a cada turno novo a missão é reavaliada e repostada até chegar;
    /// - enquanto houver missão, a IA nativa não mexe no exército (três patches abaixo);
    /// - chegada, combate e falha voltam como resultado da ação (memória da nação) e como situação da missão (dossiê).
    /// </summary>
    internal static class ArmyOrders
    {
        /// <summary>Mudança na situação de uma missão, para a thread principal guardar no IaWorld.</summary>
        internal sealed class MissionUpdate
        {
            public string ArmyKey;
            public string Status;
            public bool Active;
            public int Turn;
            /// <summary>Último turno da missão (prazo ou fim da posição segurada); 0 = sem mudança.</summary>
            public int Until;
        }

        private sealed class Mission
        {
            public ulong Army;
            public string ArmyKey;
            public int Empire;
            public int IntentId;
            public string Goal;
            public int Territory = -1;
            public ulong City;
            public ulong Enemy;
            public string TargetLabel;
            public int Until;
            public int HoldUntil = -1;
            public int TargetTile = -1;
            public int LastIssued = -1;
            public int Attempts;
            public bool Reported;
            /// <summary>Unidades do exército na última volta: se ele sumir, diz se foi fundido em outro ou desfeito.</summary>
            public ulong[] Units;
            public ArmyGoToAction Action;
            /// <summary>nova, andando, segurando.</summary>
            public string State = "nova";
        }

        internal static readonly ConcurrentQueue<MissionUpdate> Updates = new ConcurrentQueue<MissionUpdate>();
        private static readonly Dictionary<ulong, Mission> missions = new Dictionary<ulong, Mission>();
        /// <summary>Exércitos sob ordem da IA de linguagem: lido pela thread da IA nativa (troca atômica).</summary>
        private static volatile HashSet<ulong> locked = new HashSet<ulong>();
        /// <summary>Dono de cada exército sob ordem (a trava de ordens só vale para a IA nativa da própria nação).</summary>
        private static volatile Dictionary<ulong, int> owners = new Dictionary<ulong, int>();
        private static Sandbox lastSandbox;
        private static RequestArmyActionAt request;
        private static int locksApplied;
        private static int ordersBlocked;
        private static string lastBlocked;

        internal static bool IsKnown(ulong army) => locked.Contains(army);

        internal static string Describe() => $"exércitos sob ordem: {locked.Count} · tarefas nativas tiradas: {locksApplied} · ordens nativas barradas: {ordersBlocked}"
            + (lastBlocked != null ? $" (última: {lastBlocked})" : string.Empty);

        private static void Publish()
        {
            locked = new HashSet<ulong>(missions.Keys);
            owners = missions.Values.ToDictionary(m => m.Army, m => m.Empire);
        }

        // ---------------- Entrada ----------------

        /// <summary>Nova ordem (thread do sandbox, chamada pelo ActionExecutor). Uma missão por exército: a nova substitui.</summary>
        internal static void Accept(ActionIntent intent)
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (!Sandbox.SimulationEntityRepository.TryGetSimulationEntity((SimulationEntityGUID)intent.Entity, out Army army) || army.EmpireIndex != intent.Empire)
            {
                ActionExecutor.ReportOutcome(intent.Id, intent.Empire, false, "esse exército não existe mais ou não é seu");
                Update(intent.ArmyKey, "falhou: o exército não existe mais", false, sandbox?.Turn ?? 0);
                return;
            }
            if ((army.Flags & ArmyFlags.IsDecoy) != 0 || army.MercenaryIndex >= 0)
            {
                ActionExecutor.ReportOutcome(intent.Id, intent.Empire, false, "esse exército não aceita ordens (isca ou mercenário)");
                Update(intent.ArmyKey, "falhou: isca ou mercenário", false, sandbox.Turn);
                return;
            }
            int turns = Math.Max(1, Math.Min(10, intent.TurnsMax > 0 ? intent.TurnsMax : intent.ArmyGoal == "defender" || intent.ArmyGoal == "parar" ? 5 : 6));
            missions[intent.Entity] = new Mission
            {
                Army = intent.Entity,
                ArmyKey = intent.ArmyKey,
                Empire = intent.Empire,
                IntentId = intent.Id,
                Goal = intent.ArmyGoal,
                Territory = intent.TargetTerritory,
                City = intent.TargetCity,
                Enemy = intent.TargetArmy,
                TargetLabel = intent.TargetLabel,
                Until = intent.ResumeUntil > 0 ? intent.ResumeUntil : sandbox.Turn + turns - 1,
                Reported = intent.Resume,
            };
            // Trava antes de qualquer ordem: o ciclo da IA nativa que começar depois já não pega o exército.
            Publish();
        }

        // ---------------- Laço (thread do sandbox) ----------------

        internal static void Tick()
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null || sandbox.IsSessionOnline)
            {
                return;
            }
            if (!ReferenceEquals(lastSandbox, sandbox))
            {
                // Partida nova ou save carregado: o sandbox é recriado. A thread principal reenvia as missões salvas.
                lastSandbox = sandbox;
                missions.Clear();
                Publish();
                request = null;
            }
            if (missions.Count == 0 || sandbox.CurrentStateName != "SandboxState_TurnMain")
            {
                return;
            }
            int turn = sandbox.Turn;
            int budget = 4;
            foreach (Mission mission in missions.Values.ToArray())
            {
                try
                {
                    // F1: sem a IA de linguagem no comando da nação (API fora, teto de gasto, IA desligada), a IA nativa
                    // volta a mandar em tudo, inclusive nos exércitos.
                    if (!NativeAiLocks.IsLlmActive(mission.Empire))
                    {
                        End(mission, false, "a IA de linguagem parou de comandar a nação; o exército volta à IA nativa", turn);
                        continue;
                    }
                    if (!Sandbox.SimulationEntityRepository.TryGetSimulationEntity((SimulationEntityGUID)mission.Army, out Army army) || army == null || army.EmpireIndex != mission.Empire)
                    {
                        End(mission, false, Fate(mission), turn);
                        continue;
                    }
                    if (mission.Units == null || mission.Units.Length != army.Units.Count)
                    {
                        mission.Units = new ulong[army.Units.Count];
                        for (int u = 0; u < mission.Units.Length; u++)
                        {
                            mission.Units[u] = army.Units[u] != null ? (ulong)army.Units[u].GUID : 0;
                        }
                    }
                    if (Progress(mission, army, turn))
                    {
                        continue;
                    }
                    if (army.IsLocked)
                    {
                        if (mission.Goal == "atacar" && army.IsLockedByBattle)
                        {
                            End(mission, true, "em combate; a batalha segue com seus generais", turn);
                        }
                        continue;
                    }
                    bool newTurn = mission.LastIssued < turn;
                    if (mission.State == "segurando" || !(mission.State == "nova" || newTurn))
                    {
                        continue;
                    }
                    if (Sandbox.AIController != null && Sandbox.AIController.aiDecisionInProgress)
                    {
                        continue; // ciclo da IA nativa em curso: espera a próxima volta (pesquisa §4.4)
                    }
                    if (budget-- <= 0)
                    {
                        break;
                    }
                    Emit(mission, army, turn);
                }
                catch (Exception ex)
                {
                    End(mission, false, "erro interno do mod: " + ex.Message, turn);
                }
            }
        }

        /// <summary>Confere chegada, combate, prazo e a ação em curso. true = nada mais a fazer nesta volta.</summary>
        private static bool Progress(Mission mission, Army army, int turn)
        {
            if (mission.State == "segurando")
            {
                if (turn > mission.HoldUntil)
                {
                    End(mission, true, $"segurou {mission.TargetLabel} até o turno {mission.HoldUntil}; seus generais reassumem", turn);
                }
                return true;
            }
            if (turn > mission.Until)
            {
                End(mission, false, $"prazo esgotado no turno {mission.Until} sem chegar; seus generais reassumem", turn);
                return true;
            }
            ArmyGoToAction action = mission.Action;
            if (action == null)
            {
                return false;
            }
            switch (action.Status)
            {
                case ActionStatus.Running:
                    return true;
                case ActionStatus.Finished:
                    mission.Action = null;
                    if (mission.Goal == "atacar")
                    {
                        End(mission, true, $"chegou e atacou {mission.TargetLabel}; a batalha segue com seus generais", turn);
                        return true;
                    }
                    // Mover segura o lugar até o fim do turno seguinte; defender, até o prazo.
                    mission.State = "segurando";
                    mission.HoldUntil = mission.Goal == "defender" ? mission.Until : turn + 1;
                    Report(mission, true, $"chegou a {mission.TargetLabel}; segura a posição até o turno {mission.HoldUntil}", turn, active: true);
                    return true;
                case ActionStatus.WaitingForFinishTurn:
                    // Acabaram os pontos de movimento: no próximo turno a missão é reavaliada e repostada.
                    if (mission.LastIssued < turn)
                    {
                        mission.Action = null;
                        mission.State = "nova";
                        return false;
                    }
                    return true;
                default:
                    // Failed, Canceled, Terminated: tenta de novo, até três vezes.
                    mission.Action = null;
                    if (++mission.Attempts >= 3)
                    {
                        End(mission, false, "o caminho ficou bloqueado e o exército não conseguiu seguir", turn);
                        return true;
                    }
                    mission.State = "nova";
                    return false;
            }
        }

        private static void Emit(Mission mission, Army army, int turn)
        {
            mission.LastIssued = turn;
            if (mission.Goal == "parar")
            {
                if (army.HasGoToAction())
                {
                    SandboxManager.PostAndTrackOrder(new OrderCancelArmyMovement(army.GUID), mission.Empire);
                }
                mission.State = "segurando";
                mission.HoldUntil = mission.Until;
                Report(mission, true, $"parado; segura a posição até o turno {mission.HoldUntil}", turn, active: true);
                return;
            }
            if (!ChooseTarget(mission, army, out List<int> tiles, out PathArmyAction forced, out PathArmyAction expected, out string problem))
            {
                if (problem != null)
                {
                    End(mission, false, problem, turn);
                }
                return; // sem problema = já estava no lugar (segurando)
            }
            RequestArmyActionAt result = null;
            string refusal = null;
            foreach (int tile in tiles.Take(6))
            {
                RequestArmyActionAt evaluation = Evaluate(army, tile, forced);
                if (Accepted(evaluation, expected, out refusal))
                {
                    if (Deadly(army, evaluation))
                    {
                        refusal = "o único caminho cruza mar profundo e o exército morreria afogado (falta a tecnologia de navegação em alto-mar)";
                        continue;
                    }
                    result = evaluation;
                    mission.TargetTile = tile;
                    break;
                }
                if ((evaluation.PathActionConditionFlags & PathArmyActionConditionFlags.NeedToDeclareWar) != 0)
                {
                    refusal = "precisa declarar guerra antes" + WarTarget(evaluation);
                    break;
                }
            }
            if (result == null)
            {
                if (++mission.Attempts >= 3)
                {
                    End(mission, false, refusal ?? "sem caminho até o destino", turn);
                }
                else
                {
                    mission.State = "nova";
                }
                return;
            }
            if ((result.PathActionConditionFlags & PathArmyActionConditionFlags.NeedToDeclareWar) != 0)
            {
                // A guerra pode estar sendo declarada nesta mesma decisão: tenta de novo algumas voltas antes de desistir.
                if (++mission.Attempts >= 3)
                {
                    End(mission, false, "precisa declarar guerra antes" + WarTarget(result) + " (use declarar_guerra)", turn);
                }
                else
                {
                    mission.State = "nova";
                    mission.LastIssued = turn - 1;
                }
                return;
            }
            string forbidden = Forbidden(result.PathActionConditionFlags);
            if (forbidden != null)
            {
                End(mission, false, forbidden, turn);
                return;
            }
            Order order = BuildOrder(result);
            if (order == null)
            {
                End(mission, false, $"no destino a ação seria {result.PathArmyActionAtPosition}", turn);
                return;
            }
            int eta = result.PathToTarget.StepCount > 0 ? result.PathToTarget.Steps[result.PathToTarget.StepCount - 1].Turn : 1;
            // O prazo cobre a viagem que o jogo calculou (até 12 turnos a partir de agora): a ordem é chegar lá.
            if (!mission.Reported && turn + eta > mission.Until)
            {
                mission.Until = Math.Min(turn + eta + 1, turn + 12);
            }
            mission.State = "andando";
            PostOrderTicket ticket = SandboxManager.PostAndTrackOrder(order, mission.Empire);
            ticket.UponCompletion(() =>
            {
                if (ticket.Result == PostOrderResponse.Valid)
                {
                    if (Sandbox.SimulationEntityRepository.TryGetSimulationEntity((SimulationEntityGUID)mission.Army, out Army moved))
                    {
                        mission.Action = Sandbox.ActionController.GetActionFor<ArmyGoToAction, Army>(moved);
                    }
                    if (!mission.Reported)
                    {
                        mission.Reported = true;
                        string verb = mission.Goal == "atacar" ? "a caminho do ataque a" : mission.Goal == "defender" ? "a caminho para defender" : "a caminho de";
                        Report(mission, true, $"{verb} {mission.TargetLabel}; chegada em ~{Math.Max(1, eta)} turno(s)", turn, active: true);
                    }
                    else
                    {
                        Update(mission.ArmyKey, $"a caminho de {mission.TargetLabel} (~{Math.Max(1, eta)} turno(s))", true, turn, mission.Until);
                    }
                }
                else if (++mission.Attempts >= 3)
                {
                    End(mission, false, "o jogo recusou a ordem três vezes", turn);
                }
                else
                {
                    mission.State = "nova";
                }
            });
        }

        // ---------------- Destino ----------------

        /// <summary>
        /// Tiles candidatos (o primeiro é a âncora), a ação forçada e a esperada, por objetivo (pesquisa §3 e §6).
        /// </summary>
        private static bool ChooseTarget(Mission mission, Army army, out List<int> tiles, out PathArmyAction forced, out PathArmyAction expected, out string problem)
        {
            tiles = new List<int>();
            forced = PathArmyAction.None;
            expected = PathArmyAction.Move;
            problem = null;
            int empire = mission.Empire;
            VisibilityController visibility = Sandbox.VisibilityController;
            if (mission.Goal == "atacar")
            {
                forced = PathArmyAction.Attack | PathArmyAction.AttackCity;
                if (mission.Enemy != 0)
                {
                    if (!Sandbox.SimulationEntityRepository.TryGetSimulationEntity((SimulationEntityGUID)mission.Enemy, out Army enemy))
                    {
                        problem = "o exército inimigo não existe mais";
                        return false;
                    }
                    int tile = enemy.WorldPosition.ToTileIndex();
                    if (!visibility.IsWorldPositionVisibleFor(tile, empire))
                    {
                        problem = "o exército inimigo sumiu de vista";
                        return false;
                    }
                    if (enemy.SiegeAsDefender.Entity != null && JoinSiege(enemy.SiegeAsDefender.Entity, army, tiles))
                    {
                        // Ele defende uma cidade cercada: só dá para chegar nele entrando no cerco pelo lado de fora.
                        forced = PathArmyAction.None;
                        expected = PathArmyAction.JoinBattle;
                        return true;
                    }
                    expected = PathArmyAction.Attack | PathArmyAction.JoinBattle;
                    tiles.Add(tile);
                    return true;
                }
                if (mission.City != 0 && Sandbox.SimulationEntityRepository.TryGetSimulationEntity((SimulationEntityGUID)mission.City, out Settlement target))
                {
                    if (target.Empire.Entity?.Index == empire)
                    {
                        problem = "a cidade é sua: use defender";
                        return false;
                    }
                    if (target.Siege.Entity != null)
                    {
                        // Cerco já existe: entra como reforço, pelo lado de quem cerca (o centro da cidade é a área interna
                        // da batalha, fechada para quem chega de fora).
                        forced = PathArmyAction.None;
                        expected = PathArmyAction.JoinBattle;
                        if (!JoinSiege(target.Siege.Entity, army, tiles))
                        {
                            tiles.Add(target.WorldPosition.ToTileIndex());
                        }
                    }
                    else
                    {
                        expected = PathArmyAction.AttackCity;
                        tiles.Add(target.WorldPosition.ToTileIndex());
                    }
                    return true;
                }
                problem = "alvo de ataque inválido (use uma cidade C ou um exército A à vista)";
                return false;
            }
            // mover / defender: cidade (centro ou vizinhança) ou território (âncora e tiles livres).
            int territory = mission.Territory;
            int anchor = -1;
            if (mission.City != 0 && Sandbox.SimulationEntityRepository.TryGetSimulationEntity((SimulationEntityGUID)mission.City, out Settlement city))
            {
                anchor = city.WorldPosition.ToTileIndex();
                territory = Sandbox.World.TileInfo.Data[anchor].TerritoryIndex;
                if (city.Empire.Entity?.Index == empire)
                {
                    tiles.Add(anchor);
                }
            }
            if (territory < 0 || territory >= Sandbox.World.TerritoryInfo.Data.Length)
            {
                problem = "destino inválido";
                return false;
            }
            TerritoryInfo info = Sandbox.World.TerritoryInfo.Data[territory];
            if (anchor < 0)
            {
                anchor = info.Claimed && info.AdministrativeDistrictTileIndex >= 0 ? info.AdministrativeDistrictTileIndex : info.VisualCenter.ToTileIndex();
                tiles.Add(anchor);
            }
            if (mission.Goal == "defender")
            {
                expected = PathArmyAction.Move | PathArmyAction.JoinBattle;
            }
            // Vizinhança da âncora dentro do território, do mais perto ao mais longe, só tiles que fazem sentido. Navio de
            // verdade fica na água; tropa de terra embarcada (AtSea) desembarca: o destino dela é terra.
            bool naval = army.Units.Count > 0 && army.Units[0]?.UnitDefinition?.SpawnType == Amplitude.Mercury.Data.Simulation.UnitSpawnType.Maritime;
            int armyTile = army.WorldPosition.ToTileIndex();
            if (mission.City == 0 && Sandbox.World.TileInfo.Data[armyTile].TerritoryIndex == territory && (naval || !army.AtSea))
            {
                tiles.Clear();
                tiles.Add(armyTile); // destino é o território e o exército já está nele (em terra, se for tropa de terra)
            }
            foreach (int tile in (info.TileIndexes ?? new int[0]).OrderBy(t => WorldPosition.GetTileIndexDistance(t, anchor)))
            {
                if (tiles.Contains(tile) || tiles.Count >= 12)
                {
                    continue;
                }
                // Mover não entra em batalha alheia: tile dentro da área de uma batalha em curso fica de fora (defender
                // pode, é para reforçar a defesa).
                if (mission.Goal == "mover" && tile != armyTile && Sandbox.BattleRepository.TryGetBattleAt(tile, out Amplitude.Mercury.Simulation.Battle battle) && battle != null && !battle.IsFinished)
                {
                    continue;
                }
                if (tile == armyTile)
                {
                    tiles.Insert(0, tile); // já está lá
                    continue;
                }
                if (!visibility.IsWorldPositionExploredFor(tile, empire))
                {
                    continue;
                }
                SimulationEntityGUID occupant = World.Maps.ArmyMap[tile];
                if (occupant.IsValid && (ulong)occupant != mission.Army)
                {
                    continue;
                }
                if (Sandbox.World.IsPositionInWater(tile) != naval)
                {
                    continue;
                }
                SimulationEntityGUID district = World.Maps.DistrictMap[tile];
                if (district.IsValid && Sandbox.SimulationEntityRepository.TryGetSimulationEntity(district, out District d)
                    && d.Empire?.Index != empire && d.Borough != null && d.Borough.IsMain)
                {
                    continue; // centro de cidade estrangeira
                }
                tiles.Add(tile);
            }
            if (tiles.Count > 0 && tiles[0] == armyTile)
            {
                // Já no lugar: segura sem andar (e desfaz movimento que tenha sobrado de antes).
                if (army.HasGoToAction())
                {
                    SandboxManager.PostAndTrackOrder(new OrderCancelArmyMovement(army.GUID), empire);
                }
                mission.Action = null;
                mission.State = "segurando";
                mission.HoldUntil = mission.Goal == "defender" ? mission.Until : SandboxManager.Sandbox.Turn + 1;
                Report(mission, true, $"já está em {mission.TargetLabel}; segura a posição até o turno {mission.HoldUntil}", SandboxManager.Sandbox.Turn, active: true);
                tiles.Clear();
                problem = null;
                return false;
            }
            return tiles.Count > 0 || Fail(out problem, "nenhum lugar livre no destino");
        }

        private static bool Fail(out string problem, string text)
        {
            problem = text;
            return false;
        }

        /// <summary>
        /// Tiles para entrar num cerco já montado como reforço, como a IA nativa faz (Nodes.FindJoinBattlePosition): um
        /// tile livre da área da batalha (sem exército), fora dos distritos da cidade cercada (a área interna, fechada para
        /// quem chega de fora), do mais perto ao mais longe do exército. Lá a ação é entrar na batalha
        /// (OrderGoToAndJoinBattle com o GUID da batalha). O jogo só deixa entrar quem é o atacante ou o defensor principal.
        /// </summary>
        private static bool JoinSiege(Siege siege, Army army, List<int> tiles)
        {
            Amplitude.Mercury.Simulation.Battle battle = siege?.Battle;
            List<WorldPosition> area = battle?.Arena?.Area?.Positions;
            if (battle == null || battle.IsFinished || area == null)
            {
                return false;
            }
            Settlement city = siege.BesiegedCity.Entity;
            bool naval = army.Units.Count > 0 && army.Units[0]?.UnitDefinition?.SpawnType == Amplitude.Mercury.Data.Simulation.UnitSpawnType.Maritime;
            int from = army.WorldPosition.ToTileIndex();
            var candidates = new List<int>();
            foreach (WorldPosition position in area)
            {
                int tile = position.ToTileIndex();
                if (tile < 0 || World.Maps.ArmyMap[tile].IsValid || Sandbox.World.IsPositionInWater(tile) != naval)
                {
                    continue;
                }
                SimulationEntityGUID district = World.Maps.DistrictMap[tile];
                if (district.IsValid && Sandbox.SimulationEntityRepository.TryGetSimulationEntity(district, out District d) && city != null && d.Settlement.Entity == city)
                {
                    continue; // área interna: a cidade cercada
                }
                candidates.Add(tile);
            }
            foreach (int tile in candidates.OrderBy(t => WorldPosition.GetTileIndexDistance(t, from)).Take(4))
            {
                tiles.Add(tile);
            }
            return tiles.Count > 0;
        }

        // ---------------- Avaliador ----------------

        /// <summary>O mesmo pedido do clique direito, zerado a cada uso (pesquisa §1: o Evaluate só acumula flags).</summary>
        private static RequestArmyActionAt Evaluate(Army army, int tile, PathArmyAction forced)
        {
            RequestArmyActionAt r = request ?? (request = new RequestArmyActionAt());
            r.EntityGUID = army.GUID;
            r.SelectedUnitsCount = 0;
            r.TargetTileIndex = tile;
            r.PenultimateTileIndex = -1;
            r.ForcedPathArmyActionAtPosition = forced;
            r.IsRangedAction = false;
            r.PathToTarget.Clear();
            r.TargetGUID = SimulationEntityGUID.Zero;
            r.TargetEmpireIndex = -1;
            r.DestinationEmpireIndex = -1;
            if (r.RansackTargetedEmpireIndexes != null)
            {
                Array.Clear(r.RansackTargetedEmpireIndexes, 0, r.RansackTargetedEmpireIndexes.Length);
            }
            r.RansackNumberOfTargetedEmpire = 0;
            r.PathArmyActionAtPosition = PathArmyAction.None;
            r.PathActionFailureFlags = PathArmyActionFailureFlags.None;
            r.PathActionConditionFlags = PathArmyActionConditionFlags.None;
            r.ArmyActionFailureFlags = ArmyActionFailureFlags.None;
            r.AvailableAirportFailureFlags = AvailableAirportFailureFlags.None;
            r.BattleReinforcementFailureFlags = BattleReinforcementFailureFlags.None;
            Sandbox.SimulationEvaluator.Evaluate(r);
            return r;
        }

        /// <summary>Mesmos critérios da tela do jogador antes de mandar a ordem (BaseArmyCursor:322-336).</summary>
        private static bool Accepted(RequestArmyActionAt r, PathArmyAction expected, out string problem)
        {
            PathArmyAction action = r.PathArmyActionAtPosition;
            PathResultStatus status = r.PathToTarget.ResultStatus;
            bool trespassOk = status == PathResultStatus.Trespassing && (action == PathArmyAction.Move || action == PathArmyAction.Attack || action == PathArmyAction.AttackCity);
            problem = null;
            if (r.PathActionFailureFlags != PathArmyActionFailureFlags.None || r.ArmyActionFailureFlags != ArmyActionFailureFlags.None
                || r.AvailableAirportFailureFlags != AvailableAirportFailureFlags.None || r.BattleReinforcementFailureFlags != BattleReinforcementFailureFlags.None)
            {
                problem = Explain(r);
            }
            else if (status != PathResultStatus.Success && !trespassOk)
            {
                problem = "não há caminho até lá";
            }
            else if (action == PathArmyAction.None || action == PathArmyAction.ActionFailed || (action & expected) == 0)
            {
                problem = $"no destino a ação seria {action}";
            }
            else if (r.PathToTarget.StepCount == 0)
            {
                problem = "já está no destino";
            }
            return problem == null;
        }

        /// <summary>
        /// O caminho afoga o exército? O avaliador tenta primeiro um caminho que não mata em mar profundo e, se não acha,
        /// devolve o que mata: a tela do jogador mostra a caveira e ele decide. A IA não vê a tela, então caminho mortal
        /// é recusado. Conta os fins de turno em mar profundo (Unit.ComputeTurnsBeforeDyingInDeepWater: quantos o
        /// exército aguenta) e parar em mar profundo sem poder navegar nele.
        /// </summary>
        private static bool Deadly(Army army, RequestArmyActionAt r)
        {
            int survives = int.MaxValue;
            for (int u = 0; u < army.Units.Count; u++)
            {
                if (army.Units[u] != null)
                {
                    survives = Math.Min(survives, army.Units[u].ComputeTurnsBeforeDyingInDeepWater());
                }
            }
            if (survives == int.MaxValue)
            {
                return false;
            }
            int turnEnds = 0;
            int deep = 0;
            int count = r.PathToTarget.StepCount;
            for (int i = 0; i < count; i++)
            {
                if ((r.PathToTarget.Steps[i].TileFlags & DestinationFlags.DeepSea) == 0)
                {
                    continue;
                }
                deep++;
                if (i == count - 1)
                {
                    turnEnds = int.MaxValue; // para em mar profundo
                    break;
                }
                if (r.PathToTarget.Steps[i + 1].Turn > r.PathToTarget.Steps[i].Turn)
                {
                    turnEnds++;
                }
            }
            if (deep > 0)
            {
                Plugin.Log?.LogInfo($"[IA] Caminho de {(ulong)army.GUID:x}: {deep} passo(s) em mar profundo, {(turnEnds == int.MaxValue ? "para lá" : turnEnds + " fim(ns) de turno")}, aguenta {survives}");
            }
            return turnEnds > survives;
        }

        private static Order BuildOrder(RequestArmyActionAt r)
        {
            switch (r.PathArmyActionAtPosition)
            {
                case PathArmyAction.Move:
                    return new OrderGoTo(r.EntityGUID, ref r.PathToTarget);
                case PathArmyAction.Attack:
                case PathArmyAction.AttackCity:
                    return r.PathToTarget.StepCount == 1
                        ? (Order)new OrderCreateBattle(r.EntityGUID, r.TargetGUID) { UseInstantResolve = false }
                        : new OrderGoToAndCreateBattle(r.EntityGUID, ref r.PathToTarget, r.TargetGUID);
                case PathArmyAction.JoinBattle:
                    return new OrderGoToAndJoinBattle(r.EntityGUID, ref r.PathToTarget, r.TargetGUID);
                default:
                    return null;
            }
        }

        /// <summary>Condições que a tela pergunta ao jogador e que a IA não aceita sem pedir (pesquisa §2.4).</summary>
        private static string Forbidden(PathArmyActionConditionFlags flags)
        {
            if ((flags & PathArmyActionConditionFlags.WillBreakSiege) != 0) return "o caminho romperia um cerco seu";
            if ((flags & PathArmyActionConditionFlags.WillReinforceThirdPartyBattle) != 0) return "o caminho entraria na batalha de outras nações";
            if ((flags & PathArmyActionConditionFlags.WillLosePatronage) != 0) return "atacar esse povo independente acabaria com o seu patrocínio";
            return null;
        }

        private static string WarTarget(RequestArmyActionAt r)
        {
            int target = r.TargetEmpireIndex >= 0 ? r.TargetEmpireIndex : r.DestinationEmpireIndex;
            return target >= 0 && target < Sandbox.NumberOfMajorEmpires ? $" contra E{target}" : string.Empty;
        }

        private static string Explain(RequestArmyActionAt r)
        {
            string flags = $"{r.PathActionFailureFlags} {r.ArmyActionFailureFlags} {r.BattleReinforcementFailureFlags}";
            var reasons = new List<string>();
            if (flags.Contains("InvalidPath")) reasons.Add("não há caminho");
            if (flags.Contains("CannotEmbark")) reasons.Add("o exército não sabe navegar até lá");
            if (flags.Contains("NeedToDeclareWar") || flags.Contains("NeedHigherWarMoral")) reasons.Add("precisa de guerra declarada (e apoio à guerra)");
            if (flags.Contains("NeedToFreeVassal")) reasons.Add("o alvo é vassalo: a guerra é com o suserano");
            if (flags.Contains("TargetNotVisible")) reasons.Add("o alvo não está à vista");
            if (flags.Contains("PrehistoricEra")) reasons.Add("ainda na primeira era");
            if (flags.Contains("BoatsCannotSiege")) reasons.Add("barcos não cercam cidades");
            if (flags.Contains("Trespassing")) reasons.Add("fronteira fechada");
            if (flags.Contains("MeleeAttackHover")) reasons.Add("o alvo voa: precisa de unidade à distância");
            if (flags.Contains("CannotJoinInnerAreaOfBattle")) reasons.Add("a área interna da batalha está fechada: só dá para entrar pelo lado de quem cerca");
            else if (flags.Contains("CannotJoinBattle")) reasons.Add("não dá para entrar nessa batalha");
            if (flags.Contains("ActionInvalid")) reasons.Add("o exército não pode fazer essa ação agora");
            if (flags.Contains("ArmyInvalid")) reasons.Add("exército inválido");
            return reasons.Count > 0 ? string.Join(", ", reasons) : flags.Trim();
        }

        // ---------------- Relatório ----------------

        /// <summary>O que houve com um exército que sumiu: as unidades dele estão em outro exército da nação, ou não existem mais.</summary>
        private static string Fate(Mission mission)
        {
            if (mission.Units != null && mission.Units.Length > 0 && mission.Empire >= 0 && mission.Empire < Sandbox.NumberOfMajorEmpires)
            {
                MajorEmpire empire = Sandbox.MajorEmpires[mission.Empire];
                for (int a = 0; a < empire.Armies.Count; a++)
                {
                    Army other = empire.Armies[a];
                    for (int u = 0; other != null && u < other.Units.Count; u++)
                    {
                        if (other.Units[u] != null && Array.IndexOf(mission.Units, (ulong)other.Units[u].GUID) >= 0)
                        {
                            Plugin.Log?.LogInfo($"[IA] Exército {mission.ArmyKey} de E{mission.Empire} sumiu: unidades no exército {(ulong)other.GUID:x}");
                            return "o exército foi fundido com outro exército seu; a ordem acabou";
                        }
                    }
                }
            }
            Plugin.Log?.LogInfo($"[IA] Exército {mission.ArmyKey} de E{mission.Empire} sumiu: unidades não encontradas");
            return "o exército não existe mais (unidades dispensadas, destruídas ou capturadas)";
        }

        private static void Report(Mission mission, bool ok, string detail, int turn, bool active)
        {
            ActionExecutor.ReportOutcome(mission.IntentId, mission.Empire, ok, detail);
            Update(mission.ArmyKey, detail, active, turn, Math.Max(mission.Until, mission.HoldUntil));
        }

        private static void End(Mission mission, bool ok, string detail, int turn)
        {
            missions.Remove(mission.Army);
            Publish();
            Report(mission, ok, detail, turn, active: false);
        }

        private static void Update(string armyKey, string status, bool active, int turn, int until = 0)
        {
            if (armyKey != null)
            {
                Updates.Enqueue(new MissionUpdate { ArmyKey = armyKey, Status = status, Active = active, Turn = turn, Until = until });
            }
        }

        // ---------------- Trava da IA nativa (thread da IA) ----------------

        /// <summary>P1: tira do pool os exércitos sob ordem; a tarefa nativa deles falha e nenhuma nova é dada.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Allocators.ArmyAllocator), "ComputeInstanceList")]
        private static class AllocatorPatch
        {
            private static void Postfix(List<Amplitude.Mercury.Interop.AI.Entities.Army> instanceList)
            {
                HashSet<ulong> current = locked;
                if (current.Count == 0 || instanceList == null)
                {
                    return;
                }
                int removed = instanceList.RemoveAll(a => a != null && current.Contains(a.SimulationEntityGUID));
                if (removed > 0)
                {
                    System.Threading.Interlocked.Add(ref locksApplied, removed);
                }
            }
        }

        /// <summary>P2: missões específicas (reagrupar, dispersar…) não pegam exército sob ordem nem reagrupam dentro dele.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.ArmySpecificMission), "GetContexts")]
        private static class SpecificMissionPatch
        {
            private static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.ArmySpecificMission __instance)
            {
                HashSet<ulong> current = locked;
                if (current.Count == 0)
                {
                    return true;
                }
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    Amplitude.Mercury.Interop.AI.Entities.Army[] armies = brain.ControlledEmpire.Armies;
                    for (int i = 0; i < armies.Length; i++)
                    {
                        Amplitude.Mercury.Interop.AI.Entities.Army army = armies[i];
                        brain.GetAnalysisData<ArmyData, Amplitude.Mercury.Interop.AI.Entities.Army>(army, out ArmyData data);
                        SpecificMissionType type = data.SpecificMissionInfo.Type.Value;
                        if (type == SpecificMissionType.None || current.Contains(army.SimulationEntityGUID))
                        {
                            continue;
                        }
                        if (type == SpecificMissionType.Regroup && current.Contains(data.SpecificMissionInfo.Guid))
                        {
                            continue;
                        }
                        __instance.Continue(army);
                    }
                    return false;
                }
                catch (Exception)
                {
                    return true; // em dúvida, o original
                }
            }
        }

        /// <summary>P3: a IA nativa não divide exército sob ordem.</summary>
        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Military.SplitArmies), "IsArmyValid")]
        private static class SplitPatch
        {
            private static void Postfix(Amplitude.Mercury.Interop.AI.Entities.Army army, ref bool __result)
            {
                if (__result && army != null && locked.Contains(army.SimulationEntityGUID))
                {
                    __result = false;
                }
            }
        }

        // P4: rede de segurança no nível das ordens. Algum comportamento nativo que escape de P1-P3 (dispensar exército
        // "preso", juntar unidades, oportunidades) ainda pode postar ordem para o exército sob ordem; a ordem é marcada
        // quando o cérebro nativo da própria nação posta e recusada pela política de ordens do jogo (o mesmo caminho de
        // uma ordem recusada qualquer: o comportamento nativo vê a falha e segue). Batalha em curso e melhoria de
        // unidades passam.

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Order, string> marked = new System.Runtime.CompilerServices.ConditionalWeakTable<Order, string>();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.FieldInfo[]> guidFields = new System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.FieldInfo[]>();
        private static int pendingMarks;

        /// <summary>Exército sob ordem citado na ordem (campos SimulationEntityGUID), 0 se nenhum.</summary>
        private static ulong LockedIn(Order order, HashSet<ulong> current)
        {
            System.Reflection.FieldInfo[] fields = guidFields.GetOrAdd(order.GetType(), type => type
                .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(f => f.FieldType == typeof(SimulationEntityGUID)).ToArray());
            foreach (System.Reflection.FieldInfo field in fields)
            {
                ulong guid = (ulong)(SimulationEntityGUID)field.GetValue(order);
                if (guid != 0 && current.Contains(guid))
                {
                    return guid;
                }
            }
            return 0;
        }

        [HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Brain), nameof(Amplitude.Mercury.AI.Brain.Brain.PostAndTrackOrder))]
        private static class NativeOrderPatch
        {
            private static void Prefix(Amplitude.Mercury.AI.Brain.Brain __instance, Order order)
            {
                HashSet<ulong> current = locked;
                if (current.Count == 0 || order == null)
                {
                    return;
                }
                try
                {
                    string name = order.GetType().Name;
                    if (name.StartsWith("OrderBattle", StringComparison.Ordinal) || name == "OrderUpgradeUnits")
                    {
                        return;
                    }
                    ulong army = LockedIn(order, current);
                    if (army == 0 || !owners.TryGetValue(army, out int owner) || owner != __instance.EmpireIndex)
                    {
                        return;
                    }
                    marked.Remove(order);
                    marked.Add(order, name);
                    System.Threading.Interlocked.Increment(ref pendingMarks);
                    int count = System.Threading.Interlocked.Increment(ref ordersBlocked);
                    lastBlocked = $"{name} E{owner}";
                    if (count <= 30)
                    {
                        Plugin.Log?.LogInfo($"[IA] Exército {army:x} de E{owner} sob ordem: IA nativa tentou {name}; barrada");
                    }
                }
                catch (Exception)
                {
                    // em dúvida, a ordem segue
                }
            }
        }

        [HarmonyPatch(typeof(OrderPolicyController), nameof(OrderPolicyController.GetPolicy), new[] { typeof(Type), typeof(OrderPolicyChainType), typeof(Order) })]
        private static class PolicyPatch
        {
            private static void Postfix(OrderPolicyChainType chainType, Order order, ref OrderPolicy __result)
            {
                if (chainType != OrderPolicyChainType.Post || pendingMarks <= 0 || order == null)
                {
                    return;
                }
                if (marked.TryGetValue(order, out _))
                {
                    marked.Remove(order);
                    System.Threading.Interlocked.Decrement(ref pendingMarks);
                    __result = OrderPolicy.Reject;
                }
            }
        }
    }
}
