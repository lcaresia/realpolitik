using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace CurrencyMod.Diplomacia
{
    /// <summary>Uma ação decidida pela IA de linguagem, pronta para virar ordem do jogo.</summary>
    internal sealed class ActionIntent
    {
        public int Id;
        public string Guid;
        public int Turn;
        public int Empire;
        public string Name;
        public int Other = -1;
        public DiplomaticAction Diplomatic;
        public int Gold;
        public int Influence;
        /// <summary>presentear: cidades, postos e exércitos dados junto (GUIDs).</summary>
        public List<ulong> GiftEntities = new List<ulong>();
        /// <summary>Itens que o jogo não deixou dar (com o motivo), para o relatório.</summary>
        public List<string> GiftProblems;
        /// <summary>ceder_territorio: o território (destacado da cidade antes, se for anexo).</summary>
        public int Territory = -1;
        /// <summary>ordem_exercito: objetivo (mover, atacar, defender, parar), alvo (território, cidade ou exército inimigo),
        /// rótulo para os relatórios, prazo em turnos e a chave (GUID hex) do exército. Resume = missão salva reenviada
        /// depois de carregar o save ou recarregar o núcleo (vale até ResumeUntil).</summary>
        public string ArmyGoal;
        public int TargetTerritory = -1;
        public ulong TargetCity;
        public ulong TargetArmy;
        public string TargetLabel;
        public int TurnsMax;
        public string ArmyKey;
        public bool Resume;
        public int ResumeUntil;
        /// <summary>patrocinar / tratado_povo: o povo independente, o tratado (MinorTreaty) e os investimentos (null = manter).</summary>
        public int Minor = -1;
        public int MinorTreatyIndex = -1;
        public string MoneyInvestment;
        public string InfluenceInvestment;
        public ulong Entity;
        public string NewName;
        public string Summary;
        /// <summary>exigir / perdoar_queixas: o que fazer com as reclamações listadas (ou com todas).</summary>
        public DiplomaticGrievanceAction GrievanceAction;
        public List<GrievanceRef> Grievances = new List<GrievanceRef>();
        public bool AllGrievances;
        /// <summary>oferecer_rendicao / impor_rendicao: territórios pedidos (ou cedidos) e vassalagem; o resto vira ouro.</summary>
        public List<int> SurrenderTerritories = new List<int>();
        public bool SurrenderSubmission;
        /// <summary>Congresso (CongressFlow): lei proposta (nome da definição do cívico); votação de lei (LawVote) ou de crise
        /// (CrisisVote = índice do pool); escolha (lei: 0 = A, 1 = B; crise: a nação apoiada; -1 = abster); suborno (alvo e
        /// vezes); eixo do consenso.</summary>
        public string CivicName;
        public bool LawVote = true;
        public int CrisisVote = -1;
        public int Choice = -1;
        public int BribeTarget = -1;
        public int BribeCount;
        public int ConsensusAxis = -1;
    }

    /// <summary>O que o jogo fez com a ação (volta para a memória da nação).</summary>
    internal sealed class ActionOutcome
    {
        public int Id;
        public int Empire;
        public bool Ok;
        public string Detail;
    }

    /// <summary>
    /// Executa as ações da IA de linguagem como ordens do próprio jogo (design §7 e §11.0;
    /// research\order-catalog.md §4). A thread principal enfileira; a bomba roda na thread do sandbox, num prefix do
    /// PostOrderController.Update (o laço que posta as ordens): só no turno principal, confere antes com as funções
    /// de recusa do jogo, posta com PostAndTrackOrder e devolve o resultado pela fila de saída.
    /// </summary>
    internal static class ActionExecutor
    {
        internal static readonly ConcurrentQueue<ActionIntent> Pending = new ConcurrentQueue<ActionIntent>();
        internal static readonly ConcurrentQueue<ActionOutcome> Outcomes = new ConcurrentQueue<ActionOutcome>();

        [HarmonyPatch(typeof(PostOrderController), nameof(PostOrderController.Update))]
        private static class PumpPatch
        {
            private static void Prefix()
            {
                // Uma exceção aqui pararia o laço de ordens do jogo: nunca deixar escapar.
                try
                {
                    Pump();
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"[IA] Executor de ações: {ex}");
                }
            }
        }

        private static void Pump()
        {
            // Missões de exército andam a cada volta (chegada, repostagem no turno novo), com ou sem ação nova na fila.
            try
            {
                ArmyOrders.Tick();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[IA] Ordens a exércitos: {ex}");
            }
            try
            {
                SurrenderFlow.CleanUp();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[IA] Rascunhos de rendição: {ex}");
            }
            try
            {
                // Prévia de desvio do pedágio (janela do posto e aba Comércio): cálculo de caminho do jogo, nesta thread.
                TollPreview.Pump();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"Prévia de desvio: {ex}");
            }
            if (Pending.IsEmpty)
            {
                return;
            }
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null || sandbox.IsSessionOnline || sandbox.CurrentStateName != "SandboxState_TurnMain")
            {
                return; // fora do turno principal as ordens esperam na fila
            }
            string guid = sandbox.GUID.ToString();
            for (int budget = Pending.Count; budget > 0 && Pending.TryDequeue(out ActionIntent intent); budget--)
            {
                if (intent.Guid != guid)
                {
                    continue; // de outra partida
                }
                try
                {
                    Execute(intent);
                }
                catch (Exception ex)
                {
                    Report(intent, false, "erro interno do mod: " + ex.Message);
                }
            }
        }

        private static void Execute(ActionIntent intent)
        {
            MajorEmpire empire = intent.Empire >= 0 && intent.Empire < Sandbox.NumberOfMajorEmpires ? Sandbox.MajorEmpires[intent.Empire] : null;
            if (empire == null || !empire.IsAlive || empire.IsControlledByHuman)
            {
                Report(intent, false, "a nação não pode agir agora");
                return;
            }
            switch (intent.Name)
            {
                case "presentear":
                    StartGift(intent, empire);
                    return;
                case "ceder_territorio":
                    CedeTerritory(intent, empire);
                    return;
                case "ordem_exercito":
                    ArmyOrders.Accept(intent);
                    return;
                case "patrocinar":
                    Patronize(intent, empire);
                    return;
                case "tratado_povo":
                    EnactMinorTreaty(intent, empire);
                    return;
                case "renomear":
                    Post(new OrderRenameSimulationEntity { SimulationEntityGUID = intent.Entity, Name = intent.NewName }, intent, "feito");
                    return;
                case "exigir":
                case "perdoar_queixas":
                    ExecuteGrievances(intent, empire);
                    return;
                case "oferecer_rendicao":
                    SurrenderFlow.Offer(intent, empire);
                    return;
                case "impor_rendicao":
                    SurrenderFlow.Force(intent, empire);
                    return;
                case "responder_rendicao":
                    SurrenderFlow.Answer(intent, empire);
                    return;
                case "propor_votacao":
                    CongressFlow.Propose(intent, empire);
                    return;
                case "votar_congresso":
                    CongressFlow.Vote(intent, empire);
                    return;
                case "subornar":
                    CongressFlow.Bribe(intent, empire);
                    return;
                case "responder_congresso":
                    CongressFlow.AnswerVerdict(intent, empire);
                    return;
                case "contribuir_consenso":
                    CongressFlow.Contribute(intent, empire);
                    return;
                case "crise_internacional":
                    CongressFlow.DeclareCrisis(intent, empire);
                    return;
                default:
                    DiplomaticActionFailureFlags flags = empire.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(intent.Other, intent.Diplomatic);
                    if (flags != DiplomaticActionFailureFlags.None)
                    {
                        Report(intent, false, "o jogo não permite agora: " + Explain(flags));
                        return;
                    }
                    Post(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = intent.Diplomatic }, intent, "feito");
                    return;
            }
        }

        // ---------------- Presente (três ordens em cadeia) ----------------

        private static void StartGift(ActionIntent intent, MajorEmpire empire)
        {
            DiplomaticActionFailureFlags flags = empire.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(intent.Other, DiplomaticAction.StartToFillGiftProposition);
            if (flags != DiplomaticActionFailureFlags.None)
            {
                Report(intent, false, "o jogo não permite presente agora: " + Explain(flags));
                return;
            }
            // Nunca mais do que a nação tem.
            intent.Gold = Math.Max(0, Math.Min(intent.Gold, (int)(float)empire.MoneyStock.Value));
            intent.Influence = Math.Max(0, Math.Min(intent.Influence, (int)(float)empire.InfluenceStock.Value));
            // Cidades, postos e exércitos: o jogo diz o que não pode (capital, cercada, tomada de outro, mercenário).
            var problems = new List<string>();
            var entities = new List<ulong>();
            foreach (ulong guid in intent.GiftEntities)
            {
                GiftFailureFlags entityFlags = Sandbox.DiplomaticAncillary.GetEntityFailureFlags(intent.Empire, intent.Other, guid);
                if (entityFlags == GiftFailureFlags.None)
                {
                    entities.Add(guid);
                }
                else
                {
                    problems.Add($"{EntityName(guid)}: {ExplainGift(entityFlags)}");
                }
            }
            intent.GiftEntities = entities;
            intent.GiftProblems = problems;
            if (intent.Gold <= 0 && intent.Influence <= 0 && entities.Count == 0)
            {
                Report(intent, false, problems.Count > 0 ? "nada pode ser dado: " + string.Join("; ", problems) : "não há dinheiro nem influência para dar");
                return;
            }
            Post(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = DiplomaticAction.StartToFillGiftProposition }, intent, null, FillGift);
        }

        private static void FillGift(ActionIntent intent)
        {
            DiplomaticRelation relation = Sandbox.DiplomaticAncillary.GetRelationFor(intent.Empire, intent.Other);
            int proposition = relation?.GetEmpireEmbassy(intent.Empire).CurrentGiftToOtherProposition ?? -1;
            if (proposition < 0)
            {
                Report(intent, false, "o jogo não abriu a proposta de presente");
                return;
            }
            // As ordens são processadas na ordem em que são postadas: valores e itens primeiro, proposta por último.
            if (intent.Gold > 0)
            {
                SandboxManager.PostAndTrackOrder(new OrderUpdateGiftFimsInfo { GiftPropositionIndex = proposition, Currency = GiftFimsInfo.CurrencyTypes.Money, Amount = (FixedPoint)(float)intent.Gold }, intent.Empire);
            }
            if (intent.Influence > 0)
            {
                SandboxManager.PostAndTrackOrder(new OrderUpdateGiftFimsInfo { GiftPropositionIndex = proposition, Currency = GiftFimsInfo.CurrencyTypes.Influence, Amount = (FixedPoint)(float)intent.Influence }, intent.Empire);
            }
            foreach (ulong guid in intent.GiftEntities)
            {
                SandboxManager.PostAndTrackOrder(new OrderUpdateGiftInfo { GiftPropositionIndex = proposition, EntityGuid = guid, Include = true }, intent.Empire);
            }
            var parts = new List<string>();
            if (intent.Gold > 0) parts.Add($"{intent.Gold} de dinheiro");
            if (intent.Influence > 0) parts.Add($"{intent.Influence} de influência");
            foreach (ulong guid in intent.GiftEntities)
            {
                parts.Add(EntityName(guid));
            }
            string skipped = intent.GiftProblems != null && intent.GiftProblems.Count > 0 ? $" (ficou de fora: {string.Join("; ", intent.GiftProblems)})" : string.Empty;
            Post(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = DiplomaticAction.ProposeGift }, intent,
                "presente oferecido (" + string.Join(", ", parts) + "); a outra nação aceita ou recusa" + skipped, null,
                failed => SandboxManager.PostAndTrackOrder(new OrderDiplomaticAction { OtherEmpireIndex = failed.Other, DiplomaticAction = DiplomaticAction.CancelGiftProposition }, failed.Empire));
        }

        // ---------------- Ceder território ----------------

        /// <summary>
        /// Cessão de um território: se é o central de um posto ou de uma cidade, vai o assentamento inteiro de presente;
        /// se é anexo de uma cidade, primeiro ele é destacado (vira posto, OrderDetachTerritoryFromCity) e então o posto
        /// novo vai de presente. A outra nação aceita ou recusa como qualquer presente.
        /// </summary>
        private static void CedeTerritory(ActionIntent intent, MajorEmpire empire)
        {
            int t = intent.Territory;
            Settlement owner = null;
            for (int s = 0; s < empire.Settlements.Count; s++)
            {
                Settlement settlement = empire.Settlements[s];
                Region region = settlement?.Region.Entity;
                if (region != null && region.TerritoryIndices.Contains(t))
                {
                    owner = settlement;
                    break;
                }
            }
            if (owner == null)
            {
                Report(intent, false, "esse território não é mais seu");
                return;
            }
            int center = Sandbox.World.TileInfo.Data[owner.WorldPosition.ToTileIndex()].TerritoryIndex;
            if (center == t || owner.SettlementStatus != SettlementStatuses.City)
            {
                // Território central: o assentamento inteiro.
                intent.GiftEntities = new List<ulong> { (ulong)owner.GUID };
                StartGift(intent, empire);
                return;
            }
            Territory territory = Sandbox.World.Territories[t];
            var detachFlags = empire.DepartmentOfTheInterior.CanDetachTerritoryFromCity(territory, owner);
            if (detachFlags != 0)
            {
                Report(intent, false, "o jogo não deixa destacar esse território da cidade: " + ExplainDetach(detachFlags.ToString()));
                return;
            }
            Post(new OrderDetachTerritoryFromCity { CityGUID = owner.GUID, TerritoryToDetach = t }, intent, null, detached =>
            {
                // O território virou posto: acha o assentamento novo e oferece.
                Settlement camp = null;
                for (int s = 0; s < empire.Settlements.Count; s++)
                {
                    Settlement settlement = empire.Settlements[s];
                    Region region = settlement?.Region.Entity;
                    if (region != null && region.TerritoryIndices.Contains(t))
                    {
                        camp = settlement;
                        break;
                    }
                }
                if (camp == null)
                {
                    Report(detached, false, "o território foi destacado, mas o posto novo não apareceu");
                    return;
                }
                detached.GiftEntities = new List<ulong> { (ulong)camp.GUID };
                StartGift(detached, empire);
            });
        }

        // ---------------- Povos independentes ----------------

        /// <summary>
        /// Investimento por turno num povo independente (dinheiro e influência). Sem contato aberto, assina o contato
        /// antes, como a IA nativa faz. A escolha fica guardada para a IA nativa não desfazer (NativeAiLocks).
        /// </summary>
        private static void Patronize(ActionIntent intent, MajorEmpire empire)
        {
            if (!(TryMinor(intent, out MinorEmpire minor)))
            {
                return;
            }
            MinorToMajorRelation relation = minor.RelationsToMajor[empire.Index];
            PatronageDefinition.PatronageInvestment money = InvestmentLevel(intent.MoneyInvestment, relation.MoneyInvestmentLevel);
            PatronageDefinition.PatronageInvestment influence = InvestmentLevel(intent.InfluenceInvestment, relation.InfluenceInvestmentLevel);
            var order = new OrderSetPatronageInvestmentLevels { MinorEmpireIndex = (byte)minor.Index, MoneyInvestment = money, InfluenceInvestment = influence };
            string done = $"patrocínio definido: dinheiro {InvestmentName(money)}, influência {InvestmentName(influence)}";
            DepartmentOfForeignAffairs.FillPatronizeMinorEmpireFailures(empire, minor, out Amplitude.Mercury.Simulation.FailureFlags flags);
            if (flags == Amplitude.Mercury.Simulation.FailureFlags.None)
            {
                NativeAiLocks.SetPatronage(empire.Index, minor.Index, money, influence);
                Post(order, intent, done);
                return;
            }
            if (flags == Amplitude.Mercury.Simulation.FailureFlags.NotInContactWithMinor)
            {
                DepartmentOfForeignAffairs.FillEnactMinorEmpireTreatyFailures(empire, minor, MinorTreaty.OpenContact, out Amplitude.Mercury.Simulation.FailureFlags contact);
                if (contact != Amplitude.Mercury.Simulation.FailureFlags.None)
                {
                    Report(intent, false, "sem contato aberto com eles, e o contato não pode ser assinado agora: " + Capture.TurnCapture.MinorBlock(contact));
                    return;
                }
                NativeAiLocks.SetPatronage(empire.Index, minor.Index, money, influence);
                Post(new OrderEnactTreatyMinorEmpire { MinorEmpireIndex = (byte)minor.Index, Treaty = MinorTreaty.OpenContact }, intent, null,
                    next => Post(order, next, "contato aberto assinado; " + done));
                return;
            }
            Report(intent, false, "o jogo não permite patrocinar agora: " + Capture.TurnCapture.MinorBlock(flags));
        }

        private static void EnactMinorTreaty(ActionIntent intent, MajorEmpire empire)
        {
            if (!TryMinor(intent, out MinorEmpire minor) || intent.MinorTreatyIndex < 0 || intent.MinorTreatyIndex >= (int)MinorTreaty.Count)
            {
                if (minor != null)
                {
                    Report(intent, false, "tratado inválido");
                }
                return;
            }
            var treaty = (MinorTreaty)intent.MinorTreatyIndex;
            MinorToMajorRelation relation = minor.RelationsToMajor[empire.Index];
            if ((relation.TreatyEnacted & (1 << (int)treaty)) != 0)
            {
                Report(intent, false, "esse tratado já está assinado");
                return;
            }
            MinorPatronageTreaty definition = minor.PatronageDefinition?.GetTreatyDefinition(treaty);
            int group = definition != null ? (int)definition.GroupIndex : -1;
            if (group >= 0 && relation.PatronageTreatyChosen != null && group < relation.PatronageTreatyChosen.Length && relation.PatronageTreatyChosen[group] >= 0)
            {
                Report(intent, false, "outro tratado do mesmo grupo já foi escolhido com esse povo");
                return;
            }
            DepartmentOfForeignAffairs.FillEnactMinorEmpireTreatyFailures(empire, minor, treaty, out Amplitude.Mercury.Simulation.FailureFlags flags);
            if (flags != Amplitude.Mercury.Simulation.FailureFlags.None)
            {
                Report(intent, false, "o jogo não permite agora: " + Capture.TurnCapture.MinorBlock(flags));
                return;
            }
            Post(new OrderEnactTreatyMinorEmpire { MinorEmpireIndex = (byte)minor.Index, Treaty = treaty }, intent, $"tratado assinado ({DecisionParser.MinorTreatyIds[(int)treaty]})");
        }

        private static bool TryMinor(ActionIntent intent, out MinorEmpire minor)
        {
            minor = intent.Minor >= Sandbox.NumberOfMajorEmpires && intent.Minor < Sandbox.NumberOfEmpires ? Sandbox.Empires[intent.Minor] as MinorEmpire : null;
            if (minor == null || !minor.IsAlive || minor.Settlements.Count == 0)
            {
                Report(intent, false, "esse povo independente não existe mais");
                minor = null;
                return false;
            }
            return true;
        }

        private static PatronageDefinition.PatronageInvestment InvestmentLevel(string value, PatronageDefinition.PatronageInvestment current)
        {
            switch (value)
            {
                case "nenhum": return PatronageDefinition.PatronageInvestment.None;
                case "baixo": return PatronageDefinition.PatronageInvestment.Low;
                case "medio": return PatronageDefinition.PatronageInvestment.Medium;
                case "alto": return PatronageDefinition.PatronageInvestment.High;
                default: return current;
            }
        }

        private static string InvestmentName(PatronageDefinition.PatronageInvestment level)
        {
            switch (level)
            {
                case PatronageDefinition.PatronageInvestment.Low: return "baixo";
                case PatronageDefinition.PatronageInvestment.Medium: return "médio";
                case PatronageDefinition.PatronageInvestment.High: return "alto";
                default: return "nenhum";
            }
        }

        private static string EntityName(ulong guid)
        {
            if (Sandbox.SimulationEntityRepository.TryGetSimulationEntity((SimulationEntityGUID)guid, out SimulationEntity entity))
            {
                if (entity is Settlement settlement)
                {
                    string name = settlement.EntityName.ToString();
                    return (settlement.SettlementStatus == SettlementStatuses.City ? "a cidade " : "o posto ") + (string.IsNullOrWhiteSpace(name) ? "sem nome" : GameAccess.Clean(name));
                }
                if (entity is Army army)
                {
                    return $"o exército {GameAccess.Clean(army.EntityName.ToString())} ({army.Units.Count} unidade(s))";
                }
            }
            return "um item que não existe mais";
        }

        private static string ExplainGift(GiftFailureFlags flags)
        {
            var reasons = new List<string>();
            if ((flags & GiftFailureFlags.Capital) != 0) reasons.Add("a capital não pode ser dada");
            if ((flags & GiftFailureFlags.Besieged) != 0) reasons.Add("está cercada");
            if ((flags & GiftFailureFlags.Occupied) != 0) reasons.Add("foi tomada de outra nação e não pode ser dada");
            if ((flags & GiftFailureFlags.NotAnOutpost) != 0) reasons.Add("não é cidade nem posto");
            if ((flags & GiftFailureFlags.NotOwnedByInitiator) != 0) reasons.Add("não é sua");
            if ((flags & GiftFailureFlags.MercenaryArmy) != 0) reasons.Add("mercenários não podem ser dados");
            if ((flags & GiftFailureFlags.CantAcceptAgent) != 0) reasons.Add("eles já estão no limite de agentes");
            if ((flags & GiftFailureFlags.InvalidEntity) != 0) reasons.Add("não existe mais");
            if ((flags & GiftFailureFlags.Ransacked) != 0) reasons.Add("está sendo saqueada");
            return reasons.Count > 0 ? string.Join(", ", reasons) : flags.ToString();
        }

        private static string ExplainDetach(string flags)
        {
            var reasons = new List<string>();
            if (flags.Contains("CannotDetachMainTerritory")) reasons.Add("é o território central da cidade");
            if (flags.Contains("CannotDetachConnectorTerritory")) reasons.Add("ele liga outros territórios da cidade");
            if (flags.Contains("SettlementBesieged")) reasons.Add("a cidade está cercada");
            if (flags.Contains("SettlementCaptured")) reasons.Add("a cidade foi tomada");
            if (flags.Contains("HostileArmyOnTopOfSettlement")) reasons.Add("há exército inimigo na cidade");
            if (flags.Contains("RansackInProgress")) reasons.Add("há saque em andamento");
            if (flags.Contains("NotEnough") || flags.Contains("Cooldown") || flags.Contains("Action")) reasons.Add("a ação de destacar território ainda não está disponível (custo ou recarga)");
            return reasons.Count > 0 ? string.Join(", ", reasons) : flags;
        }

        // ---------------- Reclamações: exigir ou perdoar ----------------

        /// <summary>
        /// Uma OrderExecuteGrievanceAction por reclamação (o índice é o do GrievanceAllocator, o mesmo do código G), ou a
        /// versão em lote para "todas". O jogo reaproveita índices, então cada reclamação é conferida de novo (dona,
        /// alvo e tipo) antes de postar.
        /// </summary>
        private static void ExecuteGrievances(ActionIntent intent, MajorEmpire empire)
        {
            BaseDiplomaticState state = Sandbox.DiplomaticAncillary.GetRelationFor(intent.Empire, intent.Other)?.DiplomaticState;
            if (state == null)
            {
                Report(intent, false, "relação inválida");
                return;
            }
            DiplomaticGrievanceAction action = intent.GrievanceAction;
            string verb = action == DiplomaticGrievanceAction.CreateDemand ? "exigência(s) feita(s)" : "reclamação(ões) perdoada(s)";
            if (intent.AllGrievances)
            {
                DiplomaticGrievanceActionFailureFlags global = DiplomaticGrievanceActionFailureFlags.None;
                DiplomaticGrievanceActionFailureFlags[] each = null;
                int usable = state.CheckPrerequisitesFor(action, intent.Empire, ref global, ref each);
                if (usable <= 0)
                {
                    DiplomaticGrievanceActionFailureFlags why = global != DiplomaticGrievanceActionFailureFlags.None ? global
                        : each != null && each.Length > 0 ? each[0] : DiplomaticGrievanceActionFailureFlags.NoAvailableGrievances;
                    Report(intent, false, "o jogo não permite agora: " + Capture.TurnCapture.GrievanceBlock(why));
                    return;
                }
                Post(new OrderExecuteGrievanceActionBatch { GrievanceAction = action, OtherEmpireIndex = intent.Other }, intent, $"{usable} {verb}");
                return;
            }
            var allocator = empire.DepartmentOfForeignAffairs.GrievanceAllocator;
            var orders = new List<Order>();
            var problems = new List<string>();
            foreach (GrievanceRef reference in intent.Grievances)
            {
                string code = "G" + reference.Pool;
                if (reference.Pool < 0 || reference.Pool >= allocator.Capacity)
                {
                    problems.Add(code + " não existe mais");
                    continue;
                }
                ref DiplomaticGrievanceInfo info = ref allocator.GetReferenceAt(reference.Pool);
                if (!info.IsAllocated || info.OtherEmpireIndex != intent.Other || info.GrievanceType.ToString() != reference.Type)
                {
                    problems.Add(code + " expirou");
                    continue;
                }
                DiplomaticGrievanceActionFailureFlags flags = state.CheckPrerequisitesFor(action, intent.Empire, reference.Pool);
                if (flags != DiplomaticGrievanceActionFailureFlags.None)
                {
                    problems.Add($"{code}: {Capture.TurnCapture.GrievanceBlock(flags)}");
                    continue;
                }
                orders.Add(new OrderExecuteGrievanceAction { GrievanceAction = action, OtherEmpireIndex = intent.Other, GrievanceIndex = reference.Pool });
            }
            if (orders.Count == 0)
            {
                Report(intent, false, "o jogo não permite agora: " + (problems.Count > 0 ? string.Join("; ", problems) : "nenhuma reclamação válida"));
                return;
            }
            PostAll(orders, intent, verb, problems);
        }

        /// <summary>Várias ordens de uma ação só: um resultado quando todas voltarem (os tickets fecham na thread do sandbox).</summary>
        private static void PostAll(List<Order> orders, ActionIntent intent, string verb, List<string> problems)
        {
            int remaining = orders.Count;
            int done = 0;
            foreach (Order order in orders)
            {
                PostOrderTicket ticket = SandboxManager.PostAndTrackOrder(order, intent.Empire);
                ticket.UponCompletionWithParam(_ =>
                {
                    if (ticket.Result == PostOrderResponse.Valid)
                    {
                        done++;
                    }
                    else
                    {
                        problems.Add("o jogo recusou uma delas");
                    }
                    if (--remaining == 0)
                    {
                        string detail = done > 0 ? $"{done} {verb}" : "o jogo recusou a ordem";
                        Report(intent, done > 0, problems.Count > 0 ? $"{detail} ({string.Join("; ", problems)})" : detail);
                    }
                });
            }
        }

        // ---------------- Postagem e resultado ----------------

        internal static void Post(Order order, ActionIntent intent, string success, Action<ActionIntent> next = null, Action<ActionIntent> onFailure = null)
        {
            PostOrderTicket ticket = SandboxManager.PostAndTrackOrder(order, intent.Empire);
            ticket.UponCompletionWithParam(_ =>
            {
                PostOrderResponse result = ticket.Result;
                if (result == PostOrderResponse.Valid)
                {
                    if (next != null)
                    {
                        next(intent);
                    }
                    else
                    {
                        Report(intent, true, success);
                    }
                    return;
                }
                onFailure?.Invoke(intent);
                Report(intent, false, result == PostOrderResponse.Rejected ? "fora de hora: o jogo recusou neste momento do turno" : "o jogo recusou a ordem");
            });
        }

        internal static void Report(ActionIntent intent, bool ok, string detail)
        {
            Outcomes.Enqueue(new ActionOutcome { Id = intent.Id, Empire = intent.Empire, Ok = ok, Detail = detail });
        }

        /// <summary>Resultado vindo de fora do executor (missões de exército, que relatam várias vezes).</summary>
        internal static void ReportOutcome(int id, int empire, bool ok, string detail)
        {
            Outcomes.Enqueue(new ActionOutcome { Id = id, Empire = empire, Ok = ok, Detail = detail });
        }

        /// <summary>Motivos da recusa em português, para a IA entender no próximo dossiê.</summary>
        internal static string Explain(DiplomaticActionFailureFlags flags)
        {
            var reasons = new List<string>();
            void Add(DiplomaticActionFailureFlags flag, string text)
            {
                if ((flags & flag) != 0)
                {
                    reasons.Add(text);
                }
            }
            Add(DiplomaticActionFailureFlags.InPrehistory, "ainda na pré-história");
            Add(DiplomaticActionFailureFlags.WrongEmpire, "nação inválida");
            Add(DiplomaticActionFailureFlags.WrongDiplomaticAction | DiplomaticActionFailureFlags.WrongTreatyStatus | DiplomaticActionFailureFlags.WrongEscalationStatus,
                "não cabe no estado atual da relação (sem relações formais, acordos exigem paz)");
            Add(DiplomaticActionFailureFlags.OwnerHasNoDemand, "você não tem exigências contra eles");
            Add(DiplomaticActionFailureFlags.OtherHasNoDemand, "eles não têm exigências contra você");
            Add(DiplomaticActionFailureFlags.OwnerHasDemand | DiplomaticActionFailureFlags.PendingDemand, "há exigência sua pendente contra eles");
            Add(DiplomaticActionFailureFlags.OtherHasDemand, "eles têm exigência pendente contra você; responda a ela antes");
            Add(DiplomaticActionFailureFlags.AlreadyCountered, "já houve contraproposta");
            Add(DiplomaticActionFailureFlags.AlreadyStalling, "você já pediu prazo");
            Add(DiplomaticActionFailureFlags.WrongOnGoingSurrenderState, "já há rendição esperando resposta nesta guerra (ou não há nenhuma para responder)");
            Add(DiplomaticActionFailureFlags.WrongForceOtherToSurrenderState, "há rendição forçada sendo preparada nesta guerra");
            Add(DiplomaticActionFailureFlags.WrongSurrenderToOtherState, "já há um rascunho de rendição seu aberto (ou falta o rascunho)");
            Add(DiplomaticActionFailureFlags.WrongMoralForceOtherToSurrender, "só dá para impor rendição com o apoio à guerra deles em 0 e o seu acima de 0");
            Add(DiplomaticActionFailureFlags.BetterPropositionPossible, "os termos deixam mais de 5 pontos do placar sem uso: peça mais ou deixe o ouro cobrir");
            Add(DiplomaticActionFailureFlags.NotAvailableInScenario, "indisponível neste cenário");
            Add(DiplomaticActionFailureFlags.Insulted, "proposta insultada recentemente");
            Add(DiplomaticActionFailureFlags.NotEnoughWarScore, "os termos custam mais que o placar de guerra do vencedor");
            Add(DiplomaticActionFailureFlags.NoMoreMoral | DiplomaticActionFailureFlags.CannotAffordMoralCost, "apoio à guerra insuficiente");
            Add(DiplomaticActionFailureFlags.CannotAffordMoneyCost, "dinheiro insuficiente");
            Add(DiplomaticActionFailureFlags.NotEnoughInfluence, "influência insuficiente");
            Add(DiplomaticActionFailureFlags.AnswerAgreementFirst | DiplomaticActionFailureFlags.AnswerPropositionFirst | DiplomaticActionFailureFlags.AnswerGiftPropositionFirst,
                "responda antes à proposta que está aberta");
            Add(DiplomaticActionFailureFlags.GiftPropositionInProgress, "já há um presente em andamento");
            Add(DiplomaticActionFailureFlags.CanOnlyTradeWithAlly | DiplomaticActionFailureFlags.OtherCanOnlyTradeWithAlly, "só pode negociar isso com aliado");
            Add(DiplomaticActionFailureFlags.Locked, "travado no momento");
            Add(DiplomaticActionFailureFlags.CantProposeAgainYet, "proposta recente demais; espere alguns turnos");
            Add(DiplomaticActionFailureFlags.BelowFormalWarMoralThreshold, "guerra formal exige uma exigência recusada e apoio à guerra suficiente");
            Add(DiplomaticActionFailureFlags.BelowSurpriseWarMoralThreshold, "apoio à guerra insuficiente para guerra surpresa");
            Add(DiplomaticActionFailureFlags.UseFormalWarInstead, "aqui cabe guerra formal, não surpresa");
            Add(DiplomaticActionFailureFlags.DemandRefused, "exigência já recusada");
            Add(DiplomaticActionFailureFlags.IsVassal | DiplomaticActionFailureFlags.OtherIsVassal, "vassalagem impede");
            Add(DiplomaticActionFailureFlags.NotAtWar, "vocês não estão em guerra");
            Add(DiplomaticActionFailureFlags.AlreadyAtWar, "vocês já estão em guerra");
            Add(DiplomaticActionFailureFlags.PendingInternationalCrisis, "há crise internacional pendente");
            Add(DiplomaticActionFailureFlags.AgreementsAtTheirHighest, "os acordos já estão no nível máximo");
            Add(DiplomaticActionFailureFlags.NotInCrisis, "não há crise entre vocês");
            Add(DiplomaticActionFailureFlags.WrongSandboxState, "fora de hora no turno");
            Add(DiplomaticActionFailureFlags.TreatyRefusedByYou | DiplomaticActionFailureFlags.TreatyRefusedByOther, "tratado recusado há pouco");
            Add(DiplomaticActionFailureFlags.InvalidGiftProposition, "presente inválido");
            return reasons.Count > 0 ? string.Join("; ", reasons) : flags.ToString();
        }
    }
}
