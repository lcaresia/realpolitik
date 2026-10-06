# LLM diplomacy — viabilidade técnica (pesquisa 2026-10-03)

Notação: FP = `_Modding\decompiled\Amplitude.Mercury.Firstpass\`, IO = `FP\Amplitude.Mercury.Interop\`, SIM = `FP\Amplitude.Mercury.Simulation\`, AI = `_Modding\decompiled\Amplitude.Mercury.AI.Brain\`.
Grievances, demandas, rendição e a IA de crise já estão em `crisis-negotiation.md`.

## Base: dá para emitir ordens por qualquer império
`SandboxManager.PostOrder(Order, int empireIndex)` / `PostAndTrackOrder` (FP\Amplitude.Mercury.Sandbox\SandboxManager.cs:267/301). A IA vanilla faz exatamente isso (`Brain.PostAndTrackOrder`, AI\...\Brain.cs:162). O `PostOrderTicket` devolvido diz se a ordem foi Valid, Invalid ou Rejected. Cada ordem passa por um `Validate…` antes do `Process…`.

## 1. Mover e atacar: fácil a médio
- Ordens: `OrderGoTo`, `OrderGoToAndCreateBattle`, `OrderDeclareWarAndGoTo`, `OrderDeclareWarGoToAndCreateBattle`, `OrderSplitAndGoTo`, `OrderCancelArmyMovement` (em IO). A validação fica em SIM\DepartmentOfTransportation.cs:608/631.
- A interface do jogador monta essas ordens em Assembly-CSharp\...\BaseArmyCursor.cs:1334-1371.
- Pathfinding: `AIPathfindManager.Instance.FindPath` / `FindNearest` (FP\Amplitude.Mercury.Interop.AI\AIPathfindManager.cs:242/320). É assíncrono: devolve um id e o resultado sai depois via `TryPop…Result`.
- A IA vanilla manda só o trecho do turno atual, com `PathfindingFlags.IgnoreFogOfWar`, e reenvia a cada turno (AI\...\GoToSubTree.cs:305-328).
- Atacar exige guerra ou a habilidade `HasAttackDiplomaticAbility`.

## 2. Ceder território ou cidade em paz: médio (o jogo já tem presentes)
- Fluxo, tudo por `OrderDiplomaticAction`: `StartToFillGiftProposition` → `OrderUpdateGiftInfo{GiftPropositionIndex, EntityGuid, Include}` → (opcional) `OrderUpdateGiftFimsInfo` (dinheiro/influência) → `ProposeGift`. O outro lado responde com `AcceptGift` ou `RefuseGift`.
- Ao aceitar, `DiplomaticAncillary.AcceptGift` (SIM\DiplomaticAncillary.cs:1098) chama `GiveSettlementTo` (cidades) e `ChangeArmyOwner` (exércitos).
- Não dá para dar: capital, cidade capturada ou sitiada, nem algo abaixo de acampamento/posto avançado (`GetSettlementGiftFailureFlags`, :1230).
- Outras vias: `OrderForceSettlementOwnerChange` (parece ordem de debug, mas não achei trava de cheat; SIM\DepartmentOfTheInterior.cs:5729), `OrderBuyNeighbourTerritoryWithMoney`, `OrderChangeSurrenderTerritoryState`.
- Para ceder um único território de uma cidade com vários, primeiro é preciso destacá-lo (`OrderDetachTerritoryFromCity`).

## 3. Renomear: fácil
`OrderRenameSimulationEntity{SimulationEntityGUID, Name}`. Vale para cidades, exércitos e tudo que é `ISimulationEntityWithNameInfo`. Validação em SIM\DepartmentOfTheInterior.cs:6302-6338. A IA já usa (AI\...\Actuators\RenameEntity.cs).

## 4. Guerra, tratados e posturas: fácil
- `OrderDiplomaticAction{OtherEmpireIndex, DiplomaticAction}` e `OrderDeclareAnyWar`. O enum `DiplomaticAction` cobre guerra surpresa e formal, fim de aliança, propor/assinar/contrapropor/ignorar/insultar tratados, acordos (econômico, informação, cultural, militar), demandas, rendição, crise internacional, consulado e presentes, mais variantes ForceSign….
- Pré-checagem: `DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags` (SIM\DepartmentOfForeignAffairs.cs:1330). É internal.

## 5. "Embaixador": NÃO existe a palavra no código
- O conceito é `DiplomaticAmbassy` (SIM\DiplomaticAmbassy.cs), o lado de um império numa `DiplomaticRelation`. Guarda grievances, demandas, moral de guerra, placar de guerra, índices de rendição e presente, `CurrentAbilities`, humor da IA, dados de consulado e zona desmilitarizada.
- Espelho na IA: `DiplomaticEmbassy`, acessado via `MajorEmpire.EmbassiesByEmpireIndex`.
- Quem usa: todos os `DiplomaticState_*`, os helpers de Relation/Moral/Grievance/Consulat, StealthAncillary (espionagem), Battle, NarrativeEvents, ~80 arquivos da IA e os snapshots de UI.
- **Não reutilizar nem mexer.** O "bloquear" do correio deve ser um sistema 100% nosso.

## 6. Névoa de guerra / conhecimento: fácil de ler
- `Sandbox.VisibilityController` (internal): `IsWorldPositionVisibleFor` / `ExploredFor` / `DetectedFor`.
- Via pública: `Interop.AI.Snapshots.Game.IsPositionVisibleFor` / `IsPositionExploredFor` (GameSnapshot.cs:71/95).
- Se um império conhece outro: `DiplomaticStateType` Unknown / PartialyKnown e `OwnerKnowsOther`.
- Os snapshots de UI (DiplomaticEmpireSummary) só existem para o humano local. A IA vanilla ignora a névoa ao calcular caminhos e na dificuldade Difícil ou acima.

## 7. Estrutura da IA e ponto de hook: médio
- `AIController` roda numa thread própria, a cada ~200 ms, e chama `aiPlayerByEmpireIndex[i].Run()` (FP\Amplitude.Mercury.AI\AIController.cs:1695-1760).
- Hooks: prefix no `AIPlayer.Run`, ou `AIController.OnNewTurnStarted` (privado). `AIPlayer.SetControlledByHuman(bool)` desliga a IA inteira de um império.
- Tirar um exército da IA: não existe flag. Fazer postfix em `ArmyAllocator.ComputeInstanceList` (AI\...\Allocators\ArmyAllocator.cs:75) e forçar `IsAllocationStillValid` = false. Behavior trees de escolta/reagrupamento também podem mexer no exército.
- A IA lê snapshots numa thread de fundo. Sempre postar ordens, nunca mutar a simulação.

## 7.1 Controles da IA nativa para posturas e foco (pesquisa 2026-10-03)
Caminhos: AI = `Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain.*`, DATA = `Amplitude.Mercury.Data\Amplitude.Mercury.Data.AI\`.
A IA roda numa thread própria: a tabela de posturas precisa ser thread-safe (dicionário imutável trocado de forma atômica).
Todo `RelationData` é por par (império da IA, outro império), então dá para enviesar por par.

**Atitude por nação (controle principal)**
- `RelationData.RapportScore` (o quanto gosta) e `SuperiorityScore` (o quanto domina). São `ScoreBook` com `.Value`, que começa em 0,5.
- Hook: postfix em `ComputeDiplomaticScore.ProcessGenericModifiers(ScoreBook score, RelationData rd, float, float)` (AI.Analysis.Diplomacy). Usar `score.IsSuperiorityScore` para separar e somar o viés em `score.Value`. O outro império é `rd.Embassy.OtherEmpireIndex`.
- Esses dois valores alimentam a máquina de humor `DiplomacyStateMachine`, que escolhe o `AIBehaviourState` (Friendly, Pleasant, Bound, Suspicious, Aggressive, HateFilled, Tyrannical, Afraid…) mais próximo no espaço (R, S) definido em `AIDiplomaticStateConfiguration`. A mudança leva de 1 a 2 turnos; `rd.DiplomaticContext.DiplomacyContext.TriggerInstantTransitions = true` torna instantânea.
- O humor decide quais tratados propor, aceitar ou contrapropor, a animosidade e a ameaça, a tolerância a demandas e boa parte da lógica militar. **Não editar a config** (ela é compartilhada por todas as relações): enviesar os scores.
- O humor é copiado para `DiplomaticAmbassy` (AIRapportScore, CurrentAIBehaviourState, AIMoodMessage). É só saída, mas o jogador vê.
- Tratados: postfix em `ComputeAgreementScores.Process` (`rd.AgreementsScores[0..3]` = Econômico, Informação, Cultural, Militar).
- Demandas: postfix em `ComputeDemandsAction.AreDemandsTolerable`.

**Guerra**
- Cadeia: `AnimosityAndThreat` → `ComputeNemesis` → `ComputeWantWageWar` → `ComputeCanDeclareWar`.
- O controle mais limpo é um postfix em `AnimosityAndThreat.Process` com `rd.Animosity.Add(x)` e depois um clamp.
- `ComputeNemesis` escolhe um único `IsNemesis` (animosidade ≥ 0,4). Um postfix pode forçar o alvo.
- `CanDeclareWar = false` impede ataque. **Forçar true pula as checagens de prontidão e causa guerras suicidas.**
- Paz e rendição: os booleanos `rd.Want*` de `MilitaryStrategy.Process` (AI.Analysis.Military).

**Foco estratégico**
- Postfix em `ComputeMotivationPropagation` dos geradores de objetivos, multiplicando por império:
  - `ExpandEmpire` (1,0), `DevelopEmpireEconomy` (1,0), `DevelopEmpireMilitary` (0,5)
  - também `DefendEmpire`, `BuildWonders`, `ImproveDiplomaticPower`…
- Necessidades: `ComputeFIMSNeeds.Process` (Food/Industry/Money/Science/InfluenceNeed, de 0 a 1), `FaithNeed.cs`, `ReligionNeed.cs`, `CultureNeed.cs`, `WillForNewCity`.
- Manter os multiplicadores entre 0,5 e 2×.

**Personalidade nativa**
- `Archetype` (Cruel/Benevolent, Traitorous/Loyal, Pacifist/Militarist, Careful/RiskTaking, Impulsive/CoolHeaded, Introvert/Extrovert, Hateful/Open, Wary/Trusting, Vindictive/Forgiving…) e `Bias` (Rusher, Turtle, PeaceAndLove, Avenger…), guardados em `MajorEmpire.Archetypes/Biases` (salvos no save).
- Dá para mudar durante a partida com `OrderChangeArchetypes` / `OrderChangeBiases` (a validação sempre aceita).
- Útil para dar origem à persona da IA de linguagem e para refletir as mudanças dela.

## 8. Ler dados compactos: fácil
- Snapshot `Interop.AI.Entities.MajorEmpire`: Money/Influence/Research, Stability, Cities, Fame, estrelas de era, WarEmpireBits, Embassies.
- `Empire.Armies` / `CombatStrength`; `Army.TileIndex` / `HealthRatio`.
- Simulação ao vivo: `Sandbox.MajorEmpires[i].Department*`, `SimulationEntityRepository.TryGetSimulationEntity`.
