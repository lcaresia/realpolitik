using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude;
using Amplitude.Framework.Localization;
using Amplitude.Mercury;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;
using FailureFlags = Amplitude.Mercury.Simulation.FailureFlags;

namespace CurrencyMod.Diplomacia.Capture
{
    /// <summary>
    /// Fotografa o mundo no começo de cada turno, na thread do jogo: postfix em SandboxState_TurnMain.Begin, que roda
    /// logo depois do processamento de início de turno e antes de a IA nativa agir. Enquanto o postfix roda nenhuma
    /// ordem é processada, então a leitura é consistente. Pesquisa: research\llm-dossier-api.md §1.
    /// </summary>
    internal static class TurnCapture
    {
        private static volatile WorldCapture latest;

        internal static WorldCapture Latest
        {
            get => latest;
            set => latest = value;
        }

        [HarmonyPatch(typeof(SandboxState_TurnMain), nameof(SandboxState_TurnMain.Begin))]
        private static class TurnMainBeginPatch
        {
            private static void Postfix()
            {
                // Uma exceção aqui interromperia a máquina de estados do jogo: nunca deixar escapar.
                try
                {
                    Sandbox sandbox = SandboxManager.Sandbox;
                    if (sandbox == null || sandbox.IsSessionOnline)
                    {
                        return;
                    }
                    latest = Capture(sandbox, fromMainThread: false);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"[IA] Falha ao fotografar o começo do turno: {ex}");
                }
            }
        }

        /// <summary>
        /// Plano B, na thread principal: usado quando ainda não há foto do turno atual (o núcleo foi recarregado no
        /// meio do turno) e pelos comandos de desenvolvimento. Funciona quase sempre; o chamador trata exceções.
        /// </summary>
        internal static WorldCapture CaptureFromMainThread()
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            return sandbox == null ? null : Capture(sandbox, fromMainThread: true);
        }

        /// <summary>A prévia de rendição usa RPN e uma fila estática do jogo: só na thread do jogo (foto do começo do turno).</summary>
        [ThreadStatic]
        private static bool previewAllowed;

        private static WorldCapture Capture(Sandbox sandbox, bool fromMainThread)
        {
            previewAllowed = !fromMainThread;
            int majors = System.Math.Min(Sandbox.NumberOfMajorEmpires, Sandbox.MajorEmpires?.Length ?? 0);
            World world = Sandbox.World;
            var capture = new WorldCapture
            {
                Guid = sandbox.GUID.ToString(),
                Turn = sandbox.Turn,
                LocalEmpire = sandbox.LocalEmpireIndex,
                FromMainThread = fromMainThread,
                CapturedAtUtc = DateTime.UtcNow,
            };

            CaptureTerritories(world, majors, capture);
            try
            {
                InternationalAncillary international = Sandbox.InternationalAncillary;
                capture.CongressCrisisOpen = international != null && !international.IsInternationalDisabled && international.IsCrisisVoteUnlocked
                    && Amplitude.Mercury.Simulation.DownloadableContentHelper.IsValid(Amplitude.Mercury.Data.Simulation.Prerequisites.DownloadableContentPrerequisite.DownloadableContents.DiplomacyExpansionPack);
            }
            catch (Exception)
            {
                capture.CongressCrisisOpen = false;
            }
            try
            {
                capture.Congress = CongressCapture.Capture(majors, gameThread: !fromMainThread);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Congresso não lido neste turno: {ex.Message}");
            }

            capture.Empires = new CapturedEmpire[majors];
            for (int i = 0; i < majors; i++)
            {
                MajorEmpire empire = Sandbox.MajorEmpires[i];
                if (empire != null)
                {
                    capture.Empires[i] = CaptureEmpire(empire, majors, world);
                }
            }

            // O território do centro de uma cidade leva o nome dela (mesma regra da interface do jogo).
            foreach (CapturedEmpire empire in capture.Empires)
            {
                if (empire == null)
                {
                    continue;
                }
                foreach (CapturedCity city in empire.Cities)
                {
                    if (city.CenterTerritory >= 0 && city.CenterTerritory < capture.TerritoryNames.Length && !string.IsNullOrEmpty(city.Name))
                    {
                        capture.TerritoryNames[city.CenterTerritory] = city.Name;
                    }
                }
            }

            CaptureEconomy(capture, world, majors);

            try
            {
                CaptureSpies(capture, world, majors);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Espiões não lidos neste turno: {ex.Message}");
            }
            try
            {
                CaptureMinors(capture, world, majors);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Povos independentes não lidos neste turno: {ex.Message}");
            }

            for (int i = 0; i < majors; i++)
            {
                if (capture.Empires[i] != null && capture.Empires[i].Alive)
                {
                    CaptureObserverView(capture, i, majors, world);
                }
            }
            return capture;
        }

        // ---------------- Territórios ----------------

        private static void CaptureTerritories(World world, int majors, WorldCapture capture)
        {
            Territory[] territories = world.Territories;
            TerritoryInfo[] info = world.TerritoryInfo.Data;
            int count = territories.Length;
            capture.TerritoryNames = new string[count];
            capture.TerritoryOwner = new int[count];
            capture.TerritoryOcean = new bool[count];
            capture.Adjacent = new int[count][];
            for (int t = 0; t < count; t++)
            {
                Territory territory = territories[t];
                capture.TerritoryOcean[t] = territory != null && territory.IsOcean;
                capture.Adjacent[t] = territory?.AdjacentTerritories ?? new int[0];
                if (info != null && t < info.Length)
                {
                    capture.TerritoryNames[t] = info[t].LocalizedName;
                    int owner = info[t].EmpireIndex;
                    capture.TerritoryOwner[t] = owner < majors ? owner : -1; // 255 = sem dono; acima dos maiores = povos independentes
                }
                else
                {
                    capture.TerritoryOwner[t] = -1;
                }
            }
        }

        private static int TerritoryAt(World world, WorldPosition position)
        {
            if (!position.IsWorldPositionValid())
            {
                return -1;
            }
            int tile = position.ToTileIndex();
            TileInfo[] tiles = world.TileInfo.Data;
            return tile >= 0 && tile < tiles.Length ? tiles[tile].TerritoryIndex : -1;
        }

        // ---------------- Império ----------------

        private static CapturedEmpire CaptureEmpire(MajorEmpire empire, int majors, World world)
        {
            int index = empire.Index;
            var captured = new CapturedEmpire
            {
                Index = index,
                Alive = empire.IsAlive,
                Human = empire.IsControlledByHuman,
                Archetypes = (uint)empire.Archetypes,
                Biases = (uint)empire.Biases,
                EraIndex = empire.DepartmentOfDevelopment.CurrentEraIndex,
            };
            captured.EraName = GameText.EraName(captured.EraIndex);
            FillNames(index, captured);
            if (!captured.Alive)
            {
                return captured;
            }

            captured.Money = (float)empire.MoneyStock.Value;
            captured.MoneyNet = (float)empire.MoneyNet.Value;
            captured.Influence = (float)empire.InfluenceStock.Value;
            captured.InfluenceNet = (float)empire.InfluenceNet.Value;
            captured.Science = (float)empire.ResearchNet.Value;
            captured.Stability = (float)empire.Stability.Value;
            captured.Fame = (float)empire.FameScore.Value;
            captured.EraStars = (int)(float)empire.EraStarsCount.Value;
            captured.Population = (int)(float)empire.SumOfPopulation.Value;
            captured.Technologies = (int)(float)empire.NumberOfUnlockedTechnologies.Value;
            captured.TerritoryCount = (int)(float)empire.TerritoryCount.Value;
            try
            {
                captured.FameRank = Sandbox.FameRankingController != null ? Sandbox.FameRankingController.GetCurrentFameRank(index) : 0;
            }
            catch (Exception)
            {
                captured.FameRank = 0;
            }

            for (int s = 0; s < empire.Settlements.Count; s++)
            {
                Settlement settlement = empire.Settlements[s];
                if (settlement != null)
                {
                    captured.Cities.Add(CaptureCity(settlement, world));
                }
            }
            for (int a = 0; a < empire.Armies.Count; a++)
            {
                Army army = empire.Armies[a];
                if (army == null)
                {
                    continue;
                }
                CapturedArmy item = CaptureArmy(army, world);
                captured.Armies.Add(item);
                if (!item.Spy)
                {
                    captured.MilitaryStrength += item.Strength;
                    captured.UnitCount += item.Units;
                }
            }
            try
            {
                captured.ResourceAccess = ResourceAccess(empire);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Recursos do império {index} não lidos: {ex.Message}");
            }

            captured.Relations = new CapturedRelation[majors];
            for (int other = 0; other < majors; other++)
            {
                if (other == index || other >= empire.DiplomaticRelationByOtherEmpireIndex.Length)
                {
                    continue;
                }
                DiplomaticRelation relation = empire.DiplomaticRelationByOtherEmpireIndex[other];
                if (relation != null)
                {
                    captured.Relations[other] = CaptureRelation(relation, index, other);
                }
            }
            return captured;
        }

        private static void FillNames(int index, CapturedEmpire captured)
        {
            try
            {
                EmpireNameInfo[] names = Sandbox.EmpireNamesRepository?.EmpireNamePerIndex;
                if (names != null && index < names.Length)
                {
                    EmpireNameInfo info = names[index];
                    captured.FullName = First(info.EmpireNameWithoutSymbolPerEmpireColorIndex);
                    captured.Leader = First(info.AvatarNameWithoutSymbolPerEmpireColorIndex);
                    string culture = First(info.CultureNamePerEmpireColorIndex);
                    // A cultura com artigo vem entre parênteses no nome completo: "... (Os Chou)".
                    int open = captured.FullName?.LastIndexOf('(') ?? -1;
                    int close = captured.FullName?.LastIndexOf(')') ?? -1;
                    captured.Culture = open >= 0 && close > open ? captured.FullName.Substring(open + 1, close - open - 1).Trim() : culture;
                }
            }
            catch (Exception)
            {
            }
            if (string.IsNullOrEmpty(captured.FullName))
            {
                captured.FullName = $"Império {index + 1}";
            }
            if (string.IsNullOrEmpty(captured.Culture))
            {
                captured.Culture = "cultura desconhecida";
            }
        }

        private static string First(LocalizedStringParameter[] values)
        {
            // Índice 0 = sem marcações de cor.
            return values != null && values.Length > 0 ? GameAccess.Clean(values[0].StringValue) : null;
        }

        private static CapturedCity CaptureCity(Settlement settlement, World world)
        {
            var city = new CapturedCity
            {
                Key = ((ulong)settlement.GUID).ToString("x"),
                Name = SafeName(settlement.EntityName.ToString()),
                IsCity = settlement.SettlementStatus == SettlementStatuses.City,
                Capital = settlement.IsCapital,
                Population = (int)(float)settlement.TotalPopulation,
                PublicOrder = (float)settlement.PublicOrderCurrent.Value,
                Production = (float)settlement.ProductionNet.Value,
                Growth = (float)settlement.GrowthNet.Value,
                CenterTerritory = TerritoryAt(world, settlement.WorldPosition),
                Besieged = (settlement.CityFlags & CityFlags.Besieged) != 0,
                Captured = (settlement.CityFlags & CityFlags.Captured) != 0,
            };
            Region region = settlement.Region.Entity;
            if (region != null)
            {
                city.Territories.AddRange(region.TerritoryIndices);
            }
            Empire original = settlement.OriginalEmpire.Entity;
            if (original != null)
            {
                city.OriginalOwner = original.Index;
            }
            return city;
        }

        private static CapturedArmy CaptureArmy(Army army, World world)
        {
            return new CapturedArmy
            {
                Key = ((ulong)army.GUID).ToString("x"),
                Name = SafeName(army.EntityName.ToString()),
                Units = army.Units.Count,
                Strength = (float)army.CombatStrength.Value,
                Health = (float)army.HealthRatio,
                Territory = TerritoryAt(world, army.WorldPosition),
                State = army.State.ToString(),
                Moving = army.HasGoToAction(),
                Naval = army.AtSea || army.HasUnitTag(UnitTagAsAbility.Seafaring),
                Spy = SpyUnits(army) > 0,
                Besieging = army.SiegeAsBesieger.Entity != null,
            };
        }

        /// <summary>
        /// Quantas unidades de espionagem o exército tem. Espião é agente furtivo (agentes secretos e mestres espiões, da
        /// era moderna) ou unidade terrestre furtiva (os espiões da Idade Média não têm a marca de agente). Ficam de fora os
        /// enviados (agentes sem furtividade, visíveis) e os navios furtivos.
        /// </summary>
        internal static int SpyUnits(Army army)
        {
            if (army.StealthMax.Value <= 0)
            {
                return 0;
            }
            int count = 0;
            for (int u = 0; u < army.Units.Count; u++)
            {
                Unit unit = army.Units[u];
                if (unit == null || !unit.TagAsAbilities[(int)UnitTagAsAbility.Stealth])
                {
                    continue;
                }
                string name = unit.UnitDefinition?.Name.ToString() ?? string.Empty;
                if (unit.TagAsAbilities[(int)UnitTagAsAbility.Agent] || name.StartsWith("LandUnit_"))
                {
                    count++;
                }
            }
            return count;
        }

        private static string SafeName(string name) => string.IsNullOrWhiteSpace(name) ? null : GameAccess.Clean(name);

        // ---------------- Povos independentes ----------------

        /// <summary>
        /// Povos independentes vivos com cidade: nome, situação, territórios e, para cada império maior que já viu um
        /// assentamento deles, o patrocínio, o investimento e o que dá para assinar agora (pelas mesmas funções de recusa
        /// que validam a ordem). Tratados do mesmo grupo se excluem: um escolhido trava os outros do grupo.
        /// </summary>
        private static void CaptureMinors(WorldCapture capture, World world, int majors)
        {
            int total = System.Math.Min(Sandbox.NumberOfEmpires, Sandbox.Empires?.Length ?? 0);
            VisibilityController visibility = Sandbox.VisibilityController;
            EmpireNameInfo[] names = Sandbox.EmpireNamesRepository?.EmpireNamePerIndex;
            for (int i = majors; i < total; i++)
            {
                if (!(Sandbox.Empires[i] is MinorEmpire minor) || !minor.IsAlive || minor.Settlements.Count == 0)
                {
                    continue;
                }
                Settlement home = minor.Settlements[0];
                var captured = new CapturedMinor
                {
                    Index = i,
                    Name = names != null && i < names.Length ? First(names[i].EmpireNameWithoutSymbolPerEmpireColorIndex) : null,
                    Status = minor.MinorFactionStatus.ToString(),
                    Peaceful = minor.IsPeaceful,
                    CenterTerritory = TerritoryAt(world, home.WorldPosition),
                    CityName = SafeName(home.EntityName.ToString()),
                    Relations = new CapturedMinorRelation[majors],
                };
                captured.Territories.AddRange(minor.TerritoryIndexes);
                double best = 0;
                for (int m = 0; m < majors; m++)
                {
                    MajorEmpire major = Sandbox.MajorEmpires[m];
                    if (major == null || !major.IsAlive || m >= minor.RelationsToMajor.Count)
                    {
                        continue;
                    }
                    MinorToMajorRelation relation = minor.RelationsToMajor[m];
                    if (relation == null)
                    {
                        continue;
                    }
                    double stock = (float)relation.PatronageStock.Value;
                    if (stock > best)
                    {
                        best = stock;
                        captured.TopPatron = m;
                    }
                    bool known = false;
                    for (int s = 0; s < minor.Settlements.Count && !known; s++)
                    {
                        known = visibility.IsWorldPositionExploredFor(minor.Settlements[s].WorldPosition, m);
                    }
                    if (!known)
                    {
                        continue;
                    }
                    var info = new CapturedMinorRelation
                    {
                        Patronage = stock,
                        Share = (float)relation.PatronageShare.Value,
                        MoneyInvestment = relation.MoneyInvestmentLevel.ToString(),
                        InfluenceInvestment = relation.InfluenceInvestmentLevel.ToString(),
                    };
                    DepartmentOfForeignAffairs.FillPatronizeMinorEmpireFailures(major, minor, out FailureFlags patronize);
                    if (patronize != FailureFlags.None)
                    {
                        info.PatronizeBlocked = MinorBlock(patronize);
                    }
                    for (int t = 0; t < (int)MinorTreaty.Count; t++)
                    {
                        var treaty = (MinorTreaty)t;
                        MinorPatronageTreaty definition = minor.PatronageDefinition?.GetTreatyDefinition(treaty);
                        var entry = new CapturedMinorTreaty
                        {
                            Treaty = t,
                            Definition = definition?.Name.ToString(),
                            Enacted = (relation.TreatyEnacted & (1 << t)) != 0,
                        };
                        if (!entry.Enacted)
                        {
                            int group = definition != null ? (int)definition.GroupIndex : -1;
                            sbyte[] chosen = relation.PatronageTreatyChosen;
                            if (group >= 0 && chosen != null && group < chosen.Length && chosen[group] >= 0)
                            {
                                entry.Blocked = "outro tratado do mesmo grupo já foi escolhido";
                            }
                            else
                            {
                                DepartmentOfForeignAffairs.FillEnactMinorEmpireTreatyFailures(major, minor, treaty, out FailureFlags flags);
                                entry.Cost = (float)DepartmentOfTheTreasury.GetMinorTreatyCost(minor, major, treaty, out _);
                                entry.Available = flags == FailureFlags.None;
                                if (!entry.Available)
                                {
                                    entry.Blocked = MinorBlock(flags);
                                }
                            }
                        }
                        info.Treaties.Add(entry);
                    }
                    captured.Relations[m] = info;
                }
                capture.Minors.Add(captured);
            }
        }

        /// <summary>Motivo do jogo em português (patrocínio e tratados com povos independentes).</summary>
        internal static string MinorBlock(FailureFlags flags)
        {
            string text = flags.ToString();
            var reasons = new List<string>();
            if (text.Contains("MinorPatronizationNotHighEnough")) reasons.Add("falta patrocínio");
            if (text.Contains("NotEnoughInfluence")) reasons.Add("falta influência");
            if (text.Contains("NotInContactWithMinor")) reasons.Add("sem contato aberto");
            if (text.Contains("MinorFactionInDecline")) reasons.Add("o povo está em declínio");
            if (text.Contains("MinorEmpireNotValidState")) reasons.Add("o povo está desaparecendo");
            if (text.Contains("InvolvedInBattleWithEmpire")) reasons.Add("vocês estão em batalha");
            if (text.Contains("CannotAssimilateMinorUnderSiege")) reasons.Add("a cidade deles está cercada");
            if (text.Contains("LockedByEra")) reasons.Add("ainda na primeira era");
            if (text.Contains("NotVisible")) reasons.Add("você não conhece a cidade deles");
            if (text.Contains("NoCity")) reasons.Add("o povo não tem cidade");
            return reasons.Count > 0 ? string.Join(", ", reasons) : text;
        }

        // ---------------- Espiões ----------------

        /// <summary>
        /// Cada exército com unidade agente: onde está, se está oculto do dono do território e pode agir contra ele,
        /// a detecção ali, há quantos turnos de ser revelado e a infiltração. Também os territórios que o jogo marcou
        /// com atividade de espiões estrangeiros (o dono recebe o aviso nativo). Base da interceptação de cartas.
        /// </summary>
        private static void CaptureSpies(WorldCapture capture, World world, int majors)
        {
            StealthAncillary stealth = Sandbox.StealthAncillary;
            AgentAncillary agents = Sandbox.AgentAncillary;
            var infiltrations = new Dictionary<ulong, InfiltrationInfo>();
            if (agents != null)
            {
                int capacity = agents.InfiltrationInfoAllocator.Capacity;
                for (int i = 0; i < capacity; i++)
                {
                    ref InfiltrationInfo info = ref agents.InfiltrationInfoAllocator.GetReferenceAt(i);
                    if (info.PoolAllocationIndex >= 0)
                    {
                        infiltrations[(ulong)info.ArmyGUID] = info;
                    }
                }
            }

            for (int c = 0; c < majors; c++)
            {
                MajorEmpire empire = Sandbox.MajorEmpires[c];
                if (empire == null || !empire.IsAlive)
                {
                    continue;
                }
                for (int a = 0; a < empire.Armies.Count; a++)
                {
                    Army army = empire.Armies[a];
                    if (army == null || !army.WorldPosition.IsWorldPositionValid())
                    {
                        continue;
                    }
                    int spyUnits = SpyUnits(army);
                    if (spyUnits == 0)
                    {
                        continue;
                    }
                    int territory = TerritoryAt(world, army.WorldPosition);
                    var spy = new CapturedSpy
                    {
                        Owner = c,
                        Key = ((ulong)army.GUID).ToString("x"),
                        Name = SafeName(army.EntityName.ToString()),
                        Territory = territory,
                        TerritoryOwner = capture.OwnerOf(territory),
                        Agents = spyUnits,
                        Veterancy = (float)army.GetLowestUnitVeterancyLevel(),
                        Stealth = (float)army.StealthValue.Value,
                        StealthMax = (float)army.StealthMax.Value,
                        WatchingCity = army.HasUnitStatus(AgentAncillary.WatchCityStatus),
                    };
                    int owner = spy.TerritoryOwner;
                    if (owner >= 0 && owner != c && owner < majors && stealth != null && Sandbox.MajorEmpires[owner] != null)
                    {
                        MajorEmpire target = Sandbox.MajorEmpires[owner];
                        spy.Hidden = StealthAncillary.IsCollectionInvisible(army, target);
                        spy.CanAct = spy.Hidden && StealthAncillary.IsStealthAgentActionAvailable(army, target);
                        spy.OwnerDetection = (float)stealth.GetStealthDetection(territory, owner).DetectionForTerritory.Value;
                        MajorEmpireTerritory best = stealth.GetBestStealthDetection(empire, territory);
                        spy.HostileDetection = best != null ? (float)best.DetectionForTerritory.Value : 0f;
                        int turns = stealth.GetNumberOfTurnBeforeStealthDepleted(army, (short)territory, FixedPoint.Zero);
                        spy.TurnsBeforeRevealed = turns < 0 ? int.MaxValue : turns;
                        spy.OwnerHuntsSpies = (target.ExoticAbilityFlags & EmpireExoticAbilityFlags.HuntRevealedSpyOnSelfTerritory) != 0;
                    }
                    if (infiltrations.TryGetValue((ulong)army.GUID, out InfiltrationInfo infiltration))
                    {
                        spy.Infiltration = infiltration.InfiltrationType.ToString();
                        spy.InfiltrationTarget = infiltration.TargetEmpireIndex;
                        spy.InfiltrationSettlement = ((ulong)infiltration.SettlementGUID).ToString("x");
                        spy.Infiltrated = infiltration.InfiltrationTurnsRemaining <= 0;
                        spy.InfiltrationTurnsLeft = System.Math.Max(0, infiltration.InfiltrationTurnsRemaining);
                    }
                    capture.Spies.Add(spy);
                }
            }

            Territory[] territories = world.Territories;
            capture.OwnerDetection = new float[territories.Length];
            for (int t = 0; t < territories.Length; t++)
            {
                if (territories[t] != null && territories[t].IsUnderStealthActivity)
                {
                    capture.StealthActivity.Add(t);
                }
                int owner = capture.OwnerOf(t);
                if (owner >= 0 && owner < majors && stealth != null)
                {
                    capture.OwnerDetection[t] = (float)stealth.GetStealthDetection(t, owner).DetectionForTerritory.Value;
                }
            }
        }

        // ---------------- Recursos e comércio ----------------

        /// <summary>Tamanho das tabelas de recursos do jogo (DepartmentOfResources.InitializeStatics).</summary>
        private const int ResourceSlots = 64;

        private static int[] ResourceAccess(MajorEmpire empire)
        {
            var access = new int[ResourceSlots];
            ResourceDefinition[] definitions = DepartmentOfResources.ResourceDefinitionByResourceType;
            DepartmentOfResources resources = empire.DepartmentOfResources;
            if (definitions == null || resources == null)
            {
                return access;
            }
            for (int i = 0; i < ResourceSlots && i < definitions.Length; i++)
            {
                if (definitions[i] != null)
                {
                    access[i] = (int)(float)resources.GetResourceAccess((ResourceType)i);
                }
            }
            return access;
        }

        /// <summary>
        /// Jazidas do mapa, compras das rotas comerciais e o que o bloqueio comercial do mod registrou: pedágios do
        /// último fim de turno, postos com pedágio ou bloqueio e o nome da moeda de cada império.
        /// </summary>
        private static void CaptureEconomy(WorldCapture capture, World world, int majors)
        {
            int[] deposits = world.DepositCountPerResources;
            capture.ResourceDeposits = deposits != null ? (int[])deposits.Clone() : new int[0];

            try
            {
                TradeController controller = Sandbox.TradeController;
                if (controller != null)
                {
                    foreach (ExternalTradeRelation relation in controller.ExternalTradeRelations)
                    {
                        if (relation == null || (relation.TradeRoadStatus != TradeRoadStatus.Active && relation.TradeRoadStatus != TradeRoadStatus.Suspended))
                        {
                            continue;
                        }
                        bool suspended = relation.TradeRoadStatus == TradeRoadStatus.Suspended;
                        AddTrade(capture, relation.LeftTradeExchange.Entity, suspended, majors);
                        AddTrade(capture, relation.RightTradeExchange.Entity, suspended, majors);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Rotas comerciais não lidas: {ex.Message}");
            }

            CurrencyWorld currency = CurrencyManager.Current;
            if (currency == null || (!string.IsNullOrEmpty(currency.GameGuid) && currency.GameGuid != capture.Guid))
            {
                return;
            }
            lock (CurrencyManager.Lock)
            {
                foreach (EmpireCurrency item in currency.Empires)
                {
                    CapturedEmpire empire = capture.Empire(item.EmpireIndex);
                    if (empire != null)
                    {
                        empire.CurrencyName = item.PluralOrName;
                        empire.CurrencySymbol = item.Symbol;
                    }
                }
                foreach (TollRecord toll in currency.LastTolls)
                {
                    capture.Tolls.Add(new CapturedToll
                    {
                        Owner = toll.Owner,
                        Payer = toll.Payer,
                        Received = toll.ReceivedByOwner,
                        Paid = toll.PaidByPayer,
                        Routes = toll.Routes,
                    });
                }
                var rules = new Dictionary<long, CapturedTradeRule>();
                foreach (TradeRule rule in currency.TradeRules)
                {
                    int territory = rule.Territory;
                    // Regra de um dono antigo do território não vale mais (mesma regra do TradePolicy.GetMode).
                    if (rule.Mode == TradeMode.Free || rule.Target < 0 || rule.Target >= majors
                        || territory < 0 || territory >= capture.TerritoryOwner.Length || capture.TerritoryOwner[territory] != rule.Owner)
                    {
                        continue;
                    }
                    bool block = rule.Mode == TradeMode.Block;
                    long key = ((long)rule.Owner * 256 + rule.Target) * 2 + (block ? 1 : 0);
                    if (!rules.TryGetValue(key, out CapturedTradeRule summary))
                    {
                        summary = new CapturedTradeRule { Owner = rule.Owner, Target = rule.Target, Block = block };
                        rules[key] = summary;
                    }
                    summary.Posts++;
                    if (!block)
                    {
                        // Preço do pedágio (do posto, senão o geral do império, senão o padrão): menor e maior entre os postos.
                        int era = rule.Owner < capture.Empires.Length && capture.Empires[rule.Owner] != null ? capture.Empires[rule.Owner].EraIndex : 1;
                        double price = rule.Price > 0 ? rule.Price : TradePolicy.GeneralPriceOrDefault(rule.Owner, rule.Target, era);
                        summary.MinPrice = summary.MinPrice <= 0 ? price : System.Math.Min(summary.MinPrice, price);
                        summary.MaxPrice = System.Math.Max(summary.MaxPrice, price);
                    }
                }
                capture.TradeRules.AddRange(rules.Values);
            }
        }

        private static void AddTrade(WorldCapture capture, ExternalTradeExchange exchange, bool suspended, int majors)
        {
            Empire buyer = exchange?.BuyerEmpire.Entity;
            Empire seller = exchange?.SellerEmpire.Entity;
            if (buyer == null || seller == null || buyer.Index >= majors || seller.Index >= majors)
            {
                return;
            }
            var trade = new CapturedTrade { Buyer = buyer.Index, Seller = seller.Index, Suspended = suspended };
            ResourceDefinition[] definitions = DepartmentOfResources.ResourceDefinitionByResourceType;
            for (int i = 0; definitions != null && i < ResourceSlots && i < definitions.Length; i++)
            {
                if (definitions[i] != null && (float)exchange.GetBoughtResourceAccessCount((ResourceType)i) > 0)
                {
                    trade.Resources.Add(i);
                }
            }
            if (trade.Resources.Count > 0)
            {
                capture.Trades.Add(trade);
            }
        }

        // ---------------- Relações ----------------

        private static CapturedRelation CaptureRelation(DiplomaticRelation relation, int self, int other)
        {
            var captured = new CapturedRelation
            {
                Other = other,
                State = relation.CurrentState.ToString(),
                Knows = Knows(relation, self),
                AtWar = relation.CurrentState == DiplomaticStateType.War,
                Alliance = relation.CurrentState == DiplomaticStateType.Alliance,
            };
            try
            {
                captured.AllOutWar = captured.AtWar && relation.IsAllOutWar();
                Agreements agreements = relation.CurrentAgreements;
                captured.Trade = agreements.EconomicalAgreementLevel >= EconomicalAgreements.LuxuryTrade;
                captured.OpenBorders = agreements.CulturalAgreementLevel >= CulturalAgreements.OpenBorder;
                captured.SharedMaps = agreements.InformationAgreementLevel >= InformationAgreements.ShareMaps;
                captured.NonAggression = agreements.MilitaryAgreementLevel >= MilitaryAgreements.NonAggression;

                DiplomaticAmbassy mine = relation.GetEmpireEmbassy(self);
                DiplomaticAmbassy theirs = relation.GetEmpireEmbassy(other);
                captured.MyMoral = (float)mine.EmpireMoral.Moral;
                captured.TheirMoral = (float)theirs.EmpireMoral.Moral;
                if (captured.AtWar)
                {
                    captured.MyWarScore = (float)relation.GetWarScoreFor(self);
                    captured.TheirWarScore = (float)relation.GetWarScoreFor(other);
                    captured.Surrender = CaptureSurrender(relation, self, other);
                }
                captured.TheirGrievances.AddRange(GrievanceTypes(theirs));
                captured.DemandsAgainstThem = relation.HasDemandsAgainstOther(self);
                captured.DemandsAgainstMe = relation.HasDemandsAgainstMe(self);
                captured.MyDemands.AddRange(Demands(mine));
                captured.TheirDemands.AddRange(Demands(theirs));
                captured.IStalled = mine.HasStalled;
                captured.TheyStalled = theirs.HasStalled;
                captured.EnforcedDemandOn = relation.EmpireWhoNeedToAnswerEnforcedDemand;
                BaseDiplomaticState state = relation.DiplomaticState;
                if (state != null)
                {
                    captured.MyGrievances.AddRange(Grievances(state, mine, self));
                    captured.Crisis = state.Crisis.CrisisStatus.ToString();
                    if (state.Crisis.CrisisStatus == CrisisInfo.Status.DemandRefused)
                    {
                        captured.CrisisRefuser = state.Crisis.LastRefuserEmpireIndex;
                    }
                    CongressCapture.FillVerdict(captured, state, self, other);
                    captured.Treaty = Pending(ref state.CurrentTreatyInfo.PropositionInfo, TreatyName(state.CurrentTreatyInfo.TreatyType));
                    captured.Agreement = Pending(ref state.CurrentAgreementsInfo.PropositionInfo, AgreementName(state.CurrentAgreementsInfo.Category));
                }

                ListOfStruct<DiplomaticLogEntry> log = relation.LogEntries;
                if (log != null)
                {
                    for (int i = 0; i < log.Length; i++)
                    {
                        DiplomaticLogEntry entry = log.Data[i];
                        if (entry.LogEntryType != DiplomaticLogEntryType.DiplomaticTransaction)
                        {
                            continue;
                        }
                        captured.Log.Add(new LogItem
                        {
                            Turn = entry.TurnNumber,
                            Initiator = entry.InitiatorEmpireIndex,
                            Action = ((DiplomaticAction)entry.DiplomaticParameter).ToString(),
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Relação em estado estranho (império eliminado etc.): fica com o que deu para ler.
            }
            return captured;
        }

        // ---------------- Rendição (research\surrender.md) ----------------

        private static CapturedSurrender CaptureSurrender(DiplomaticRelation relation, int self, int other)
        {
            var result = new CapturedSurrender();
            try
            {
                MajorEmpire me = Sandbox.MajorEmpires[self];
                MajorEmpire them = Sandbox.MajorEmpires[other];
                DiplomaticActionFailureFlags force = me.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, DiplomaticAction.AllowToForceOtherToSurrender);
                result.CanForce = force == DiplomaticActionFailureFlags.None;
                result.ForceBlocked = result.CanForce ? null : ActionExecutor.Explain(force);
                result.CanBeForced = them.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(self, DiplomaticAction.AllowToForceOtherToSurrender) == DiplomaticActionFailureFlags.None;
                DiplomaticActionFailureFlags offer = me.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(other, DiplomaticAction.StartToFillSurrenderProposition);
                result.CanOffer = offer == DiplomaticActionFailureFlags.None;
                result.OfferBlocked = result.CanOffer ? null : ActionExecutor.Explain(offer);
                DiplomaticAmbassy mine = relation.GetEmpireEmbassy(self);
                result.MyOfferDraft = mine.CurrentSurrenderToOtherProposition >= 0;
                result.MyForcedDraft = mine.ForcedOtherToSurrenderProposition >= 0;
                if (relation.DiplomaticState is DiplomaticState_War war && war.ProposedSurrenderIndex >= 0)
                {
                    ref SurrenderProposition p = ref Sandbox.DiplomaticAncillary.SurrenderAllocator.GetReferenceAt(war.ProposedSurrenderIndex);
                    var pending = new CapturedSurrenderPending
                    {
                        Winner = p.WinnerEmpire,
                        Loser = p.LoserEmpire,
                        Forced = p.IsSurrenderForced,
                        Responder = p.IsSurrenderForced ? p.LoserEmpire : p.WinnerEmpire,
                        Score = (int)(float)p.WinnerWarScore,
                        Submission = p.SubmissionDemand.Included,
                    };
                    int money = p.MoneyRetribution.IsIncluded ? p.MoneyRetribution.NumberOfRetribution : 0;
                    pending.Money = (int)((float)p.MoneyRetribution.MoneyGainPerRetribution * money);
                    pending.Cost = (int)(float)p.ComputeIncludedTermsCost() + money;
                    for (int i = 0; p.IncludedDemands != null && i < p.IncludedDemands.Length; i++)
                    {
                        if (p.IncludedDemands[i].Included)
                        {
                            pending.Demands++;
                        }
                    }
                    for (int i = 0; p.SurrenderTerritories != null && i < p.SurrenderTerritories.Length; i++)
                    {
                        if (p.SurrenderTerritories[i].Included && p.SurrenderTerritories[i].Connectivity != SurrenderTerritoryConnectivity.LinkedToDemand)
                        {
                            pending.Territories.Add(i);
                        }
                    }
                    result.Pending = pending;
                }
                if (previewAllowed)
                {
                    result.IfIOffer = Preview(relation, winner: other, loser: self);
                    if (result.CanForce || result.MyForcedDraft)
                    {
                        result.IfIForce = Preview(relation, winner: self, loser: other);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Rendição E{self}-E{other} não capturada: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Prévia exata do rascunho que o jogo criaria (DiplomaticAncillary.InitializeSurrenderProposition numa struct
        /// local, sem tocar no pool): exigências, territórios que dá para pedir, vassalagem e o valor do ouro.
        /// </summary>
        private static CapturedSurrenderPreview Preview(DiplomaticRelation relation, int winner, int loser)
        {
            var p = new SurrenderProposition
            {
                PoolAllocationIndex = -1,
                WinnerEmpire = winner,
                LoserEmpire = loser,
                EmpireToFill = -1,
                CurrentPropositionStatus = TreatyStatus.None,
                WinnerWarScore = relation.GetWarScoreFor(winner),
            };
            Sandbox.DiplomaticAncillary.InitializeSurrenderProposition(ref p, relation);
            var preview = new CapturedSurrenderPreview
            {
                Budget = (int)(float)p.WinnerWarScore,
                DemandCount = p.IncludedDemands?.Length ?? 0,
                DemandCost = (int)(float)p.OverallDemandCost,
                DemandsFixed = p.WinnerWarScore > p.OverallDemandCost,
                SubmissionAvailable = p.SubmissionDemand.FailureFlags == SurrenderSubmissionFailureFlags.None,
                SubmissionCost = (int)(float)p.SubmissionDemand.WarCost,
                MoneyPerPoint = (int)(float)p.MoneyRetribution.MoneyGainPerRetribution,
            };
            if (!preview.SubmissionAvailable)
            {
                SurrenderSubmissionFailureFlags flags = p.SubmissionDemand.FailureFlags;
                preview.SubmissionBlocked = (flags & SurrenderSubmissionFailureFlags.HasALiege) != 0 ? "o perdedor já é vassalo de alguém"
                    : (flags & SurrenderSubmissionFailureFlags.NotAvailable) != 0 ? "o vencedor ainda não pode exigir vassalagem"
                    : "o placar não sobra depois das exigências";
            }
            if (p.SurrenderTerritories == null)
            {
                return preview;
            }
            var ready = new List<int>();
            for (int i = 0; i < p.SurrenderTerritories.Length; i++)
            {
                SurrenderTerritoryInfo t = p.SurrenderTerritories[i];
                if (t.FailureFlags == SurrenderTerritoryFailureFlags.None
                    && (t.Connectivity == SurrenderTerritoryConnectivity.NearBorder || t.Connectivity == SurrenderTerritoryConnectivity.OccupiedCity))
                {
                    ready.Add(i);
                }
            }
            var seen = new HashSet<int>(ready);
            foreach (int t in ready.Take(10))
            {
                preview.Territories.Add(new CapturedSurrenderTerritory { Territory = t, Ready = true, Detail = TerritoryDetail(t, p.SurrenderTerritories[t].Connectivity == SurrenderTerritoryConnectivity.OccupiedCity) });
            }
            // Segundo anel: só entram junto com um vizinho já incluído (o jogo checa a conexão a cada inclusão).
            foreach (int t in ready.Take(10))
            {
                foreach (int next in Sandbox.World.Territories[t].AdjacentTerritories)
                {
                    if (seen.Contains(next) || next < 0 || next >= p.SurrenderTerritories.Length)
                    {
                        continue;
                    }
                    SurrenderTerritoryInfo info = p.SurrenderTerritories[next];
                    if (info.FailureFlags == SurrenderTerritoryFailureFlags.None && info.Connectivity == SurrenderTerritoryConnectivity.None)
                    {
                        seen.Add(next);
                        preview.Territories.Add(new CapturedSurrenderTerritory { Territory = next, Ready = false, Via = t, Detail = TerritoryDetail(next, false) });
                    }
                    if (preview.Territories.Count >= 18)
                    {
                        return preview;
                    }
                }
            }
            return preview;
        }

        private static string TerritoryDetail(int territory, bool occupied)
        {
            Territory t = Sandbox.World.Territories[territory];
            Settlement settlement = t.Region.Entity?.Settlement.Entity;
            if (settlement == null)
            {
                return "sem assentamento";
            }
            if (settlement.SettlementStatus != SettlementStatuses.City)
            {
                return "posto";
            }
            string name = SafeName(settlement.EntityName.ToString()) ?? "cidade";
            bool center = t.AdministrativeDistrict.Entity != null && t.AdministrativeDistrict.Entity.WorldPosition == settlement.WorldPosition;
            return center ? $"centro de {name}{(occupied ? " (ocupada pelo vencedor)" : string.Empty)}" : $"anexo de {name}";
        }

        private static PendingProposal Pending(ref DiplomaticPropositionInfo info, string kind)
        {
            if (info.Status != TreatyStatus.Proposed && info.Status != TreatyStatus.Countered)
            {
                return null;
            }
            return new PendingProposal { Kind = kind, From = info.EmpireIndexWhoInitiated, Answerer = info.EmpireIndexWhoNeedToSign, Turn = info.TurnWhenProposed };
        }

        private static string TreatyName(TreatyType type)
        {
            switch (type)
            {
                case TreatyType.Alliance: return "aliança";
                case TreatyType.EndWar: return "paz";
                case TreatyType.EndCrisis: return "fim da crise";
                default: return "tratado";
            }
        }

        private static string AgreementName(AgreementCategory category)
        {
            switch (category)
            {
                case AgreementCategory.Economical: return "acordo econômico (comércio)";
                case AgreementCategory.Information: return "acordo de informação (mapas)";
                case AgreementCategory.Cultural: return "acordo cultural (fronteiras abertas)";
                case AgreementCategory.Military: return "acordo militar (não agressão)";
                default: return "acordo";
            }
        }

        /// <summary>Reclamações que ainda podem virar exigência ou ser perdoadas, com o motivo do jogo quando não dá agora.</summary>
        private static IEnumerable<CapturedGrievance> Grievances(BaseDiplomaticState state, DiplomaticAmbassy ambassy, int self)
        {
            var list = new List<CapturedGrievance>();
            MajorEmpire owner = ambassy.MyEmpire.Entity;
            if (owner == null)
            {
                return list;
            }
            var allocator = owner.DepartmentOfForeignAffairs.GrievanceAllocator;
            int turn = SandboxManager.Sandbox.Turn;
            AvailableGrievanceCollection grievances = ambassy.AvailableGrievances;
            for (int i = 0; i < grievances.Count; i++)
            {
                int pool = grievances[i];
                if (pool < 0)
                {
                    continue;
                }
                ref DiplomaticGrievanceInfo info = ref allocator.GetReferenceAt(pool);
                if (info.DemandIndex >= 0 || info.Renounced || info.OverridedByDemand)
                {
                    continue; // já virou exigência, foi perdoada ou repete uma exigência de território
                }
                DiplomaticGrievanceActionFailureFlags flags = state.CheckPrerequisitesFor(DiplomaticGrievanceAction.CreateDemand, self, pool);
                list.Add(new CapturedGrievance
                {
                    Pool = pool,
                    Type = info.GrievanceType.ToString(),
                    TurnsLeft = info.MaxDuration - (turn - info.TurnWhenObserved),
                    Gain = Gain(info.DemandGainType, ref info.DemandGain, 1, info.MyEmpireIndex, info.OtherEmpireIndex),
                    Blocked = flags == DiplomaticGrievanceActionFailureFlags.None ? null : GrievanceBlock(flags),
                });
            }
            return list;
        }

        private static IEnumerable<CapturedDemand> Demands(DiplomaticAmbassy ambassy)
        {
            var list = new List<CapturedDemand>();
            MajorEmpire owner = ambassy.MyEmpire.Entity;
            if (owner == null)
            {
                return list;
            }
            var allocator = owner.DepartmentOfForeignAffairs.DemandsAllocator;
            OnGoingDemandCollection demands = ambassy.OnGoingDemands;
            for (int i = 0; i < demands.DemandIndexesCount; i++)
            {
                int index = demands.DemandIndexes[i];
                if (index < 0)
                {
                    continue;
                }
                ref DiplomaticDemandInfo info = ref allocator.GetReferenceAt(index);
                list.Add(new CapturedDemand
                {
                    Type = info.GrievanceType.ToString(),
                    Turn = info.CreationTurn,
                    Gain = Gain(info.DemandGainType, ref info.DemandGain, info.GrievanceCount, info.MyEmpireIndex, info.OtherEmpireIndex),
                });
            }
            return list;
        }

        /// <summary>O que a exigência entrega a quem exige (owner) e se o jogo ainda consegue entregar.</summary>
        private static CapturedGain Gain(DemandGainType type, ref DemandGain gain, int count, int owner, int other)
        {
            var captured = new CapturedGain { Kind = type.ToString() };
            try
            {
                switch (type)
                {
                    case DemandGainType.Money:
                        captured.Amount = gain.GainParam * System.Math.Max(1, count);
                        break;
                    case DemandGainType.Territory:
                        if (Sandbox.SimulationEntityRepository.TryGetSimulationEntity((SimulationEntityGUID)gain.GainGuid, out Territory territory))
                        {
                            captured.Territory = territory.Index;
                        }
                        int payable = 0;
                        captured.Valid = DiplomaticGrievanceHelper.GetTerritoryGainValidity(owner, other, ref gain, ref payable);
                        break;
                    case DemandGainType.ForceCivic:
                        captured.Civic = gain.GainParamName.ToString();
                        MajorEmpire target = Sandbox.MajorEmpires[other];
                        int civic = target.DepartmentOfDevelopment.GetCivicIndex(gain.GainParamName);
                        if (civic >= 0 && gain.GainParam >= 0)
                        {
                            captured.CivicChoice = target.DepartmentOfDevelopment.Civics.Data[civic].CivicDefinition?.Choices[gain.GainParam].Name.ToString();
                        }
                        captured.Valid = DiplomaticGrievanceHelper.GetForceCivicGainValidity(owner, other, ref gain, out _);
                        break;
                    case DemandGainType.ForceReligion:
                        captured.Valid = DiplomaticGrievanceHelper.GetForceReligionGainValidity(owner, other);
                        break;
                    case DemandGainType.DiplomaticAction:
                        captured.Action = ((DiplomaticAction)gain.GainParam).ToString();
                        if (Sandbox.SimulationEntityRepository.TryGetSimulationEntity((SimulationEntityGUID)gain.GainGuid, out Empire third) && third is MajorEmpire)
                        {
                            captured.Third = third.Index;
                        }
                        captured.Valid = DiplomaticGrievanceHelper.GetDiplomaticActionValidity(owner, other, ref gain, out _);
                        break;
                }
            }
            catch (Exception)
            {
                // Ganho em estado estranho (território sumiu etc.): fica o tipo.
            }
            return captured;
        }

        /// <summary>Motivo, em português, de uma reclamação não poder virar exigência agora.</summary>
        internal static string GrievanceBlock(DiplomaticGrievanceActionFailureFlags flags)
        {
            var reasons = new List<string>();
            if ((flags & DiplomaticGrievanceActionFailureFlags.InPrehistory) != 0) reasons.Add("ainda na pré-história");
            else if ((flags & DiplomaticGrievanceActionFailureFlags.WrongDiplomaticAction) != 0) reasons.Add("não cabe no estado da relação (em guerra não se exige)");
            if ((flags & (DiplomaticGrievanceActionFailureFlags.FinishSurrenderFirst | DiplomaticGrievanceActionFailureFlags.OnGoingSurrenderProposition)) != 0) reasons.Add("há rendição em andamento");
            if ((flags & DiplomaticGrievanceActionFailureFlags.OnGoingProposition) != 0) reasons.Add("há proposta de tratado aberta entre vocês");
            if ((flags & DiplomaticGrievanceActionFailureFlags.OnGoingInternationalCrisis) != 0) reasons.Add("há crise internacional em votação");
            if ((flags & DiplomaticGrievanceActionFailureFlags.OwnerHasDemand) != 0) reasons.Add("já virou exigência");
            if ((flags & (DiplomaticGrievanceActionFailureFlags.GrievanceIsRenounced | DiplomaticGrievanceActionFailureFlags.GrievanceHasBeenOverrided)) != 0) reasons.Add("reclamação já usada ou perdoada");
            if ((flags & DiplomaticGrievanceActionFailureFlags.WrongEmpire) != 0) reasons.Add("a reclamação não é contra essa nação");
            if ((flags & DiplomaticGrievanceActionFailureFlags.NoAvailableGrievances) != 0) reasons.Add("não há reclamação disponível");
            return reasons.Count > 0 ? string.Join("; ", reasons) : flags.ToString();
        }

        private static IEnumerable<string> GrievanceTypes(DiplomaticAmbassy ambassy)
        {
            var types = new List<string>();
            MajorEmpire owner = ambassy.MyEmpire.Entity;
            if (owner == null)
            {
                return types;
            }
            var allocator = owner.DepartmentOfForeignAffairs.GrievanceAllocator;
            AvailableGrievanceCollection grievances = ambassy.AvailableGrievances;
            for (int i = 0; i < grievances.Count; i++)
            {
                int poolIndex = grievances[i];
                if (poolIndex < 0)
                {
                    continue;
                }
                ref DiplomaticGrievanceInfo info = ref allocator.GetReferenceAt(poolIndex);
                types.Add(info.GrievanceType.ToString());
            }
            return types;
        }

        /// <summary>Mesma regra do jogo (EmpireNameSnapshot): paz ou mais conhece; no contato parcial, só quem descobriu.</summary>
        private static bool Knows(DiplomaticRelation relation, int observer)
        {
            if (relation.CurrentState >= DiplomaticStateType.Peace)
            {
                return true;
            }
            return relation.CurrentState == DiplomaticStateType.PartialyKnown
                && relation.DiplomaticState is DiplomaticState_PartialyKnown partial
                && partial.LeftKnowRight == (relation.LeftEmpireIndex == observer);
        }

        // ---------------- O que cada nação vê ----------------

        private static void CaptureObserverView(WorldCapture capture, int observer, int majors, World world)
        {
            CapturedEmpire me = capture.Empires[observer];
            MajorEmpire observerEmpire = Sandbox.MajorEmpires[observer];
            VisibilityController visibility = Sandbox.VisibilityController;

            // Vizinhos: donos de territórios encostados nos meus.
            var neighbors = new HashSet<int>();
            for (int t = 0; t < capture.TerritoryOwner.Length; t++)
            {
                if (capture.TerritoryOwner[t] != observer)
                {
                    continue;
                }
                foreach (int adjacent in capture.Adjacent[t])
                {
                    int owner = adjacent >= 0 && adjacent < capture.TerritoryOwner.Length ? capture.TerritoryOwner[adjacent] : -1;
                    if (owner >= 0 && owner != observer)
                    {
                        neighbors.Add(owner);
                    }
                }
            }
            me.Neighbors.AddRange(neighbors);
            me.Neighbors.Sort();

            for (int other = 0; other < majors; other++)
            {
                if (other == observer)
                {
                    continue;
                }
                MajorEmpire otherEmpire = Sandbox.MajorEmpires[other];
                if (otherEmpire == null || !otherEmpire.IsAlive)
                {
                    continue;
                }

                // Exércitos que estou vendo agora (furtivos só se detectados).
                for (int a = 0; a < otherEmpire.Armies.Count; a++)
                {
                    Army army = otherEmpire.Armies[a];
                    if (army == null || !army.WorldPosition.IsWorldPositionValid())
                    {
                        continue;
                    }
                    int tile = army.WorldPosition.ToTileIndex();
                    if (!visibility.IsWorldPositionVisibleFor(tile, observer))
                    {
                        continue;
                    }
                    bool hidden = StealthAncillary.IsCollectionInvisible(army, observerEmpire) && !visibility.IsWorldPositionDetectedFor(tile, observer);
                    int units = hidden ? army.NumberOfUnitsNotInvisible : army.Units.Count;
                    if (units <= 0)
                    {
                        continue;
                    }
                    me.SeenArmies.Add(new SeenArmy
                    {
                        Key = ((ulong)army.GUID).ToString("x"),
                        Owner = other,
                        Units = units,
                        Strength = (float)(hidden ? army.CombatStrengthNotInvisible.Value : army.CombatStrength.Value),
                        Territory = TerritoryAt(world, army.WorldPosition),
                        Naval = army.AtSea,
                        Partial = hidden,
                    });
                }

                // Assentamentos em tiles que já explorei (população e cerco só se estiverem à vista).
                for (int s = 0; s < otherEmpire.Settlements.Count; s++)
                {
                    Settlement settlement = otherEmpire.Settlements[s];
                    if (settlement == null || !settlement.WorldPosition.IsWorldPositionValid())
                    {
                        continue;
                    }
                    int tile = settlement.WorldPosition.ToTileIndex();
                    if (!visibility.IsWorldPositionExploredFor(tile, observer))
                    {
                        continue;
                    }
                    bool visible = visibility.IsWorldPositionVisibleFor(tile, observer);
                    CapturedCity source = capture.Empires[other]?.Cities.Find(c => c.Key == ((ulong)settlement.GUID).ToString("x"));
                    me.SeenCities.Add(new SeenCity
                    {
                        Owner = other,
                        Key = source?.Key,
                        Name = source?.Name,
                        IsCity = source?.IsCity ?? true,
                        Capital = source?.Capital ?? false,
                        CenterTerritory = source?.CenterTerritory ?? -1,
                        Visible = visible,
                        Population = visible ? source?.Population ?? 0 : 0,
                        Besieged = visible && (source?.Besieged ?? false),
                    });
                }
            }
        }
    }
}
