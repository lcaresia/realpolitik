# Catálogo completo de ordens do Humankind + caixa de ferramentas da IA de linguagem (pesquisa 2026-10-04)

Pesquisa só de leitura. Nenhum arquivo foi alterado. Caminhos relativos a `_Modding\decompiled\`.

**Notação**
- **Pastas**
  - `SIM\` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\`
  - `SBX\` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Sandbox\`
  - `IO\` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Interop\`
  - `FPAI\` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.AI\` (contém `AIController.cs`)
  - `BB` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.AI.Battle\BattleBrain.cs`
  - `AIA\` = `Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain.Actuators\`
  - `BT\` = `Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain.BehaviorTrees\`
  - `BTM\` = `...BehaviorTrees.Major\`
  - `GEN\` = `...Amplitude.Mercury.AI.Brain.Generators*\`
  - `UI\` = `Assembly-CSharp\Amplitude.Mercury.UI\`
  - `PR\` = `Assembly-CSharp\Amplitude.Mercury.Presentation\`
- **Departamentos** (todos em `SIM\`)
  - DOFA = DepartmentOfForeignAffairs.cs
  - DOTI = DepartmentOfTheInterior.cs
  - DOD = DepartmentOfDefense.cs
  - DOT = DepartmentOfTransportation.cs
  - DOB = DepartmentOfBattles.cs
  - DOI = DepartmentOfIndustry.cs
  - DOS = DepartmentOfScience.cs
  - DODv = DepartmentOfDevelopment.cs
  - DOR = DepartmentOfReligion.cs
  - DOC = DepartmentOfCulture.cs
  - DORs = DepartmentOfResources.cs
  - DOCm = DepartmentOfCommunication.cs
  - DOL = DepartmentOfLabour.cs
  - DOTr = DepartmentOfTheTreasury.cs
  - DOH = DepartmentOfHistory.cs
- **"V/P"** = linha do `ValidateOrderX` / linha do `ProcessOrderX`.
- **Coluna LLM**
  - **SIM**: vira ferramenta direta.
  - **COMP**: só como passo de uma ferramenta composta.
  - **VIÉS**: melhor enviesar a IA nativa, porque ela re-otimiza a cada turno e desfaria a ordem.
  - **INT**: só o executor do mod usa (por exemplo, para aplicar um acordo já fechado).
  - **NÃO**: confirmação de interface, tática, câmera ou sistema.
  - **CHEAT**: sem validação real no sandbox, ou ordem de debug.

---

## 0. Resumo

**Quantas ordens existem**
- 245 classes `Order*` concretas em `IO\`, mais `OrderChangeDumpBreakOnNetworkDesynchronization` (`SBX\`) e 67 `EditorOrder*`.
- 242 têm `[OrderProcessor]` com Validate e Process em algum departamento.
- `OrderRemoveExplorationAt` não tem processador. A validação loga erro e devolve false (`SIM\Empire.cs:1373-1377`).
- `OrderChangeGameOption` e `OrderWaitForOrderReplication` são `SandboxOrder` (`SBX\Sandbox.cs:1842-1865`).
- 22 valores de `OrderIdentifier` não têm classe nenhuma (§2.20).

**Como a validação funciona**
- O despacho é por reflexão: o atributo `[OrderProcessor(typeof(OrderX))]` fica na classe do departamento, e o jogo procura os métodos privados `ValidateOrderX` e `ProcessOrderX` (`SIM\Empire.cs:1353-1417`).
- **O Validate só devolve bool. O motivo da recusa nunca volta.** Para explicar o erro à IA de linguagem, o mod tem que chamar antes as funções internas `Get…FailureFlags` / `Can…` (§4.3).

**A IA nativa e os cheats**
- A IA nativa usa uns 100 tipos de ordem, e nenhum deles é cheat.
- As ordens de cheat (`OrderGainMoney`, `OrderSpawnArmy`, `OrderForceCaptureCity`…) só são barradas no cliente, por `GodMode.Enabled` (por exemplo `UI\ManagementBanner_Money.cs:155-160`, `UI\CivicsScreen.cs:478-483`). **O sandbox aceita essas ordens de qualquer império.**

**Threads e execução**
- Postar ordens é seguro em qualquer thread: elas entram numa fila concorrente.
- Validar e processar acontece sempre na thread do sandbox.
- O ticket só fecha na thread que postou. Recomendo um executor na thread do sandbox (§4).

**Ferramentas especiais**
- Exércitos: o caminho certo é o mesmo avaliador que o clique direito do jogador usa, `SimulationEvaluator.Evaluate(RequestArmyActionAt)` (`SIM\SimulationEvaluator.cs:396`). Ele devolve o caminho multi-turno, a ação no destino e os failure flags. Depois a ordem é montada como em `PR\BaseArmyCursor.cs:1324-1501`.
- Escolhas de "tela" são ordens comuns: cultura, cívicos, dogmas, eventos narrativos e votos. A IA nativa decide cada uma num analysis/generator próprio, que pode ser suprimido ou enviesado (§5).

**Riscos mais sérios (§6)**
- `ForceSign*` e `DeclareForcedWar` passam na validação e assinam ou declaram **sem o consentimento do outro**.
- `OrderMakeNarrativeEventChoice` não checa o dono do evento nem os pré-requisitos da escolha.
- Uma exceção dentro de um Validate deixa o ticket pendurado para sempre.

---

## 1. Caminho de uma ordem

1. **Postagem.** `SandboxManager.PostOrder` / `PostAndTrackOrder(order, empireIndex)` (`SBX\SandboxManager.cs:267/301`).
   - Grava `TargetEmpireIndex` e põe a ordem numa fila concorrente (`SBX\PostOrderController.cs:173-177`).
   - O ticket recebe número (`SBX\Ticket.cs:13-24`) e guarda a thread que postou (`SBX\SandboxTicket.cs:9-13`).
   - **Use sempre a sobrecarga com `empireIndex`.** Sem ela, a ordem vai para o império local (o humano).
2. **Envio.** Na thread do sandbox, `PostOrderController.Update()` (`:106-138`) aplica a política "Post" da fase atual (Accept, Queue ou Reject) e envia uma `PostOrderMessage`.
3. **Validação e processamento.** `Sandbox.ValidateOrder` (`SBX\Sandbox.cs:1731-1789`):
   - aplica a política "Validate" e chama `Empires[i].ValidateOrder`;
   - se der true, `ProcessOrder` roda **na hora** (`:1791-1840`, dentro de try/catch) e a resposta é `Valid`; se der false, a resposta é `Invalid`.
4. **Fechamento do ticket.** No laço seguinte, `RaiseTicket` chama `SandboxManager.Raise` (`:159-182`):
   - se o ticket é da thread do sandbox, fecha na hora;
   - senão, espera a thread dona chamar `RaiseTicketsFromCallingThread` (`:105-119`). A principal faz isso em `PR\Presentation.cs:292`; a IA, em `AI.Brain\Brain.cs:190-217`.
5. **Depois de processar.** O jogo roda `RefreshAll`, a visibilidade e dispara **`SimulationEvent_OrderProcessed`** (`Sandbox.cs:1819`, campo `Order`). Assinando esse evento, o mod vê todas as ordens, inclusive as da IA nativa: é o jeito de detectar quando ela desfaz uma ordem da IA de linguagem.

---

## 2. Catálogo por domínio

### 2.1 Diplomacia entre impérios maiores

| Ordem | Para quê | Campos-chave | V/P | IA nativa posta? | LLM |
|---|---|---|---|---|---|
| OrderDiplomaticAction | Qualquer valor do enum `DiplomaticAction` (§2.2) | OtherEmpireIndex, DiplomaticAction | DOFA:1325/1319 (`GetDiplomaticActionFailureFlags` :1330) | AIA\DiplomaticAction.cs:51,60; AIA\HandleDiplomaticProposition.cs:130 | SIM (por valor) |
| OrderDeclareAnyWar | Declara guerra surpresa se puder, senão formal | OtherEmpireIndex | DOFA:1258/1271 | não (a UI usa: PR\BaseArmyCursor.cs:725, ArmyBombardCursor.cs:367) | SIM |
| OrderDeclareWarAndGoTo | Guerra e movimento num pedido só | OrderDeclareWar, OrderGoTo | DOT:602/619 | não | COMP (opcional) |
| OrderDeclareWarGoToAndCreateBattle | Guerra, movimento e ataque | OrderDeclareWar, OrderGoToAndCreateBattle | DOT:625/646 (só vale se hoje **não** pode atacar o alvo) | não | COMP |
| OrderDeclareWarAndCreateBattle | Guerra e ataque a alvo adjacente | OrderDeclareWar, OrderCreateBattle | DOB:1599/1621 | não | COMP |
| OrderExecuteGrievanceAction | Transforma uma reclamação em exigência, ou renuncia a ela | GrievanceAction (RenounceGrievance/CreateDemand), OtherEmpireIndex, GrievanceIndex, TargetEntityGUID (não é usado) | DOFA:1558/1582 | AIA\DiplomaticGrievanceAction.cs:20 | SIM |
| OrderExecuteGrievanceActionBatch | "Exigir tudo" / "renunciar a tudo" | GrievanceAction, OtherEmpireIndex | DOFA:1649/1679 | não (UI\DiplomaticCrisisPanel_CrisisGroup.cs:638) | SIM |
| OrderChangeSurrenderDemandState | Põe ou tira uma exigência dos termos de rendição | OtherEmpireIndex, GrievanceType, Included, SurrenderDemandIndex | DOFA:1123/1150 | AIA\ChangeSurrenderTerm.cs:32 | COMP |
| OrderChangeSurrenderMoneyState | Idem, dinheiro | OtherEmpireIndex, Include | DOFA:1158/1176 | AIA\ChangeSurrenderTerm.cs:20 | COMP |
| OrderChangeSurrenderSubmissionState | Idem, submissão (vassalagem) | OtherEmpireIndex, Included | DOFA:1184/1206 | :26 | COMP |
| OrderChangeSurrenderTerritoryState | Idem, território | OtherEmpireIndex, TerritoryIndex, Included | DOFA:1214/1250 | :39 | COMP |
| OrderUpdateGiftInfo | Põe ou tira cidade, posto ou exército do presente | GiftPropositionIndex, EntityGuid, Include | DOFA:2026/2048 | não | COMP |
| OrderUpdateGiftFimsInfo | Ouro ou influência no presente | GiftPropositionIndex, Currency, Amount | DOFA:1999/2021 | não | COMP |
| OrderMakeConsulatAgreementAction | Acordo de consulado: Propose / Accept / Counter / Refuse / Cancel | Action, Agreement (ReduceKnownTechCost, BuildOtherEmblematicUnit, ParticipateInOtherWonder, ShareDiplomatVision, ApplyStatusOnPopulationLost, SphereOfInfluenceBonusForOther), OtherEmpireIndex | DOFA:1828/1842 | AIA\DiplomaticConsulateAgreementAction.cs:16 | SIM |
| OrderUseLeverageAction | Alavancagem do consulado: LeverageReduceWarScore, MarkArmiesAsUnwelcommed, EnforceDemands, RevealAgentsLocation, EconomicSanctions | OtherEmpireIndex, Action | DOFA:2103/2113 | AIA\DiplomaticLeverageAction.cs:16 | SIM |
| OrderDemilitarizeTerritory | Desmilitariza um território (afinidade diplomata) | TerritoryIndex | DOFA:1276/1295 | AIA\DoDiplomatAction.cs:16 | SIM |
| OrderUpdateRelationAIFeedback | Grava o humor da IA (estado, scores, mensagem) que o humano vê; pode notificá-lo | 10 campos | DOFA:2053 (sempre true)/2062 | AIA\UpdateAIFeedback.cs:145 | INT (mostrar a postura da IA de linguagem) |
| OrderChangeArchetypes / OrderChangeBiases | Troca os traços nativos | Archetypes / Biases | DODv:2467/2472, 2478/2483 (sempre true) | não (só a janela de debug) | INT |
| OrderAcknowledgeTransactions | Marca falas diplomáticas como lidas | LastTransactionIndex, OtherEmpireIndex | DOFA:1056/1071 | não (PR\PresentationAvatarController.cs:595) | NÃO |

### 2.2 Enum `DiplomaticAction` completo

Arquivo: `Amplitude.Mercury.Data\Amplitude.Mercury.Data.Simulation\DiplomaticAction.cs`.
Pré-checagens em `SIM\BaseDiplomaticState.cs:163-582`; disponibilidade por estado em `IsAvailable` de cada `SIM\DiplomaticState_*.cs`.
Fase: propor só em TurnBegin/TurnMain; respostas também em TurnFinish (`BaseDiplomaticState.cs:682-726`).

| # | Valor | Efeito / pré-condição | Quem usa | LLM |
|---|---|---|---|---|
| 0 | DeclareSurpriseWar | Exige moral ≥ limiar de surpresa; falha com `UseFormalWarInstead` quando a formal é possível (:209-218) | UI; a IA só depois de perder crise internacional (AIA\DiplomaticAction.cs:61-72) | SIM |
| 1 | DeclareFormalWar | Exige moral ≥ 80 ou casus belli, e nenhuma crise internacional pendente (:199-208) | UI, IA | SIM |
| 2 | DeclareEndOfAlliance | Aliança → Paz | UI, IA | SIM |
| 3 | DeclareSurrender | O vencedor impõe a rendição forçada já preenchida (:293-301) | UI, IA | COMP |
| 4 / 5 / 6 | RefuseDemands / WithdrawDemands / AcceptDemands | Recusar gera casus belli para o outro (:374-414) | UI, IA | SIM |
| 7 | IntroduceYourself | Contato de mão única → apresentar-se (DiplomaticState_PartialyKnown.cs:46-61) | UI | SIM |
| 8 | FreeVassal | O suserano liberta um vassalo (DiplomaticState_VassalToLiege.cs:119-124) | UI | SIM (raro) |
| 9 | StallForTime | "Enrolar", uma vez por crise (:415-433) | IA | SIM |
| 10 | ProposeAllianceTreaty | Só sem crise (:221-223) | UI, IA | SIM |
| 11 | ProposeEndWarTreaty | Paz branca; só em guerra e sem rendição pendente (:319-334) | UI, IA | SIM |
| 12 / 13 | ProposeEndCrisisTreaty / ProposeEndRebellionTreaty | Exigências mútuas (:224-246) | UI, IA | SIM |
| 14 / 15 / 16 | SignTreaty / CounterTreaty / IgnoreTreaty | A contraproposta só acrescenta um preço em ouro calculado pelo jogo (DiplomaticRelationHelper.cs:1307) | UI, IA | SIM |
| 17 | InsultTreaty | Sempre `Locked` (:335-338) | — | NÃO |
| 18-21 | Propose{Economical, Information, Cultural, Military}Agreement | Sobe um nível do acordo (:344-349); recarga de 5 turnos | UI, IA | SIM |
| 22 / 23 / 24 | SignAgreement / CounterAgreement / IgnoreAgreement | Respostas a acordos (:360-373) | UI, IA | SIM |
| 25 | InsultAgreement | Sem uso; a IA recusa usar (AIA\DiplomaticAction.cs:83-101) | — | NÃO |
| 26-29 | Break{Economical, Information, Cultural, Military}Agreement | Romper ou baixar um acordo (:350-359) | UI, IA | SIM |
| 30 / 31 | StartToFillSurrenderProposition / CancelSurrenderProposition | Abre ou descarta o rascunho de rendição (:247-268) | UI, IA | COMP |
| 32 | ProposeToSurrender | O perdedor oferece rendição (:302-318) | UI, IA | COMP |
| 33 / 34 | RefuseSurrender / AcceptSurrender | Respostas (:269-292) | UI, IA | SIM |
| 35 | FirstMeet | Interno (primeiro contato) | sistema | NÃO |
| 36 | ForceWhitePeace | Disponível em guerra, mas o Notify loga "You should not use order" (DOFA:1362-1365) | sistema | NÃO |
| 37 | AllowToForceOtherToSurrender | Abre rendição forçada quando o outro está com moral 0 (:434-456) | UI, IA | COMP |
| 38 | AllowToForceOtherToSurrenderToAlly | Idem para aliado; via ordem loga erro | sistema | NÃO |
| 39 | DeclareForcedWar | Guerra "puxada" por aliança. **Passa na validação em Paz sem checar moral** (DiplomaticState_Peace.cs:105) | sistema | INT/NÃO |
| 40-46 | ForceSign{Alliance, EndWar, EndCrisis, Cultural, Information, Economical, Military} | **Assina sem o outro aceitar** (DiplomaticRelationHelper.cs:546-570). Em Paz valem Alliance e os acordos (Peace.cs:106-111); em Guerra vale EndWar (War.cs:86) | sistema | INT (aplicar acordo fechado pelo mod) |
| 47 | DeclareInternationalCrisis | Exige ter exigências contra o outro e moral ou justificativa (:487-507) | UI, IA | SIM |
| 48 | ProposeInternationalCrisisCompliance | Ceder durante a votação da crise (:508-513) | UI | SIM |
| 49 | ProposeConsulatAgreement | Os estados devolvem WrongDiplomaticAction; use `OrderMakeConsulatAgreementAction` | — | NÃO |
| 50 / 51 / 52 | StartToFillGiftProposition / CancelGiftProposition / ProposeGift | O índice do presente fica em `embassy.CurrentGiftToOtherProposition` (DiplomaticRelationHelper.cs:944-958); pré-checagens em :514-543 | UI, IA | COMP |
| 53 / 54 | AcceptGift / RefuseGift | Respostas (:544-567) | UI, IA | SIM |
| 55 | Count | nunca usar | — | — |

### 2.3 Congresso mundial (votações)

| Ordem | Para quê | Campos | V/P | IA | LLM |
|---|---|---|---|---|---|
| OrderStartCivicVote | Abre votação de um cívico | CivicName | DOFA:1971/1980 (InternationalAncillary.cs:342) | AIA\StartInternationalCivicVote.cs:17 | SIM |
| OrderInternationalAction | Votar, subornar, tomar lado, contribuir | InternationalAction, CivicChoiceIndex, CrisisVoteIndex, IdeologicalConsensusIndex, TargetedEmpireIndex, NumberOfBribeActions | DOFA:1814/1823 (InternationalAncillary.cs:1277) | AIA\InternationalAction.cs:21-36 | SIM para VoteForCivicChoiceIndex, BribeForCivicVote, SideWithCrisisDeclarator/Targeted, BribeForCrisisVote, ContributeToIdeologicalConsensus. ForceUnlock*/ForceEnd*/ForceApply* aparecem na janela de debug (provável CHEAT, não confirmado) |
| OrderStartCrisisVote | Abre votação de crise direto | TargetedEmpireIndex | DOFA:1985/1994 | não (só a janela de debug) | NÃO (o caminho normal é `DeclareInternationalCrisis`) |

### 2.4 Povos independentes

| Ordem | Para quê | Campos | V/P | IA | LLM |
|---|---|---|---|---|---|
| OrderPatronizeMinorEmpire | Nível de patrocínio numa moeda | MinorEmpireIndex (byte), Currency (Money/Influence), Investment (None/Low/Medium/High) | DOFA:1927/1943 (FillPatronizeMinorEmpireFailures :1848) | não (UI\MinorFactionPatronagePanel_InvestmentRuler.cs:111) | SIM |
| OrderSetPatronageInvestmentLevels | Nível nas duas moedas | MinorEmpireIndex, MoneyInvestment, InfluenceInvestment | DOFA:1948/1964 | AIA\PatronizeMinor.cs:58 | SIM |
| OrderEnactTreatyMinorEmpire | Tratado imediato, pago em influência: OpenContact, TradingCharter, MercenaryCharter, ScienceCollaboration, ProfitSharing, PrivilegedContract, CulturalExchange, **Puppet**, **Assimilate** | MinorEmpireIndex, Treaty | DOFA:1533/1549 (FillEnactMinorEmpireTreatyFailures :1431) | AIA\EnactMinorTreaty.cs:16; PatronizeMinor.cs:51 | SIM |
| OrderRentArmy | Alugar mercenários | ArmyGUID | DOD:4901/4918 (ArmyActionHelper.FillRentArmyFailures) | AIA\RentArmyActuator.cs:16 | SIM |
| OrderGiveCampToMinorFaction | Dar um posto a povo nômade que pede posto | SettlementGUID, ArmyGUID (exército do povo) | DOTI:5754/5791 (ArmyActionHelper.cs:851) | AIA\GiveCampActuator.cs:16 | SIM |
| OrderLiberateSettlement | Libertar cidade, que vira povo independente | SettlementGUID, DetachAllTerritories | DOTI:5999/6021 (CanLiberateSettlement :5937) | AIA\LiberateCity.cs:31 | SIM |
| OrderChangePatronageAmount | Soma patrocínio de graça | MinorEmpireIndex, PatronageDelta | DOFA:1098/1115 | não (ninguém posta) | CHEAT |
| OrderForcePatronageStock / OrderForceMinorLifeTime | Fixa patrocínio / vida restante | — | DOFA:1787/1808; 1767/1782 | não (GodMode: UI\MinorFactionPatronagePanel.cs:355, MinorFactionHeaderGroup.cs:255) | CHEAT |
| OrderMinorBuildConstructible | Construção do próprio povo menor | SettlementGUID, ConstructibleName, WorldPosition | DOI:2284/2313 | AIA\..Actuators.Minor\BuildConstruction.cs:47 | NÃO |

### 2.5 Exércitos no mapa (mover, atacar, assentar)

| Ordem | Para quê | Campos | V/P | IA | LLM |
|---|---|---|---|---|---|
| OrderGoTo | Mover por caminho A*; aceita caminho multi-turno. A `ArmyGoToAction` continua sozinha, postando `OrderContinueGotoAction` (SIM\ArmyGoToAction.cs:473-480) | SimulationEntityGUID, AStarResults, AdditionalPathfindingFlags, IsAutoExplore | DOT:1020/1038. ValidatePath :855 (1º passo adjacente, não volta ao início); ValidateArmy :925; ValidateDestination :962 | BT\GoToSubTree.cs:327 (só o trecho do turno) | COMP |
| OrderSplitAndGoTo | Separar unidades e mover | + UnitGUIDsToSplit | DOD:5072/5118 | não | COMP |
| OrderSplitArmy | Separar unidades para um tile adjacente | ArmySimulationEntityGUID, WorldPosition, UnitGUIDsToSplit | DOD:5146/5204 | AIA\SplitUnits.cs:104; BT\Nodes.cs:1773 | COMP (difícil) |
| OrderTransferUnits | Passar unidades para outro exército | SourceArmyGUID, TargetArmyGUID, UnitGUIDsToSplit | DOD:5498/5650 | Nodes.cs:1706 | COMP |
| OrderGoToAndMerge | Ir e fundir exércitos | + TargetGUID, UnitGUIDsToSplitAndMerge | DOT:1396/1401 | não | COMP |
| OrderGoToAndCreateBattle | Ir e atacar exército ou cidade (o último passo tem que ser o tile do alvo) | + TargetGUID | DOT:1046/1103 (exige `HasAttackDiplomaticAbility`) | não (a IA usa GoTo + CreateBattle) | COMP |
| OrderCreateBattle | Atacar alvo adjacente | AttackerArmyGUID, TargetGUID, UseInstantResolve | DOB:1558/1588 | Nodes.cs:1336,1509 | COMP |
| OrderJoinBattle / OrderGoToAndJoinBattle | Reforçar uma batalha | ArmyGUID, BattleGUID | DOB:1627/1663; DOT:1230/1262 | Nodes.cs:1864; BB:868 | COMP |
| OrderCancelArmyMovement | Parar o movimento | ArmyGUID | DOT:565/582 | não | COMP |
| OrderChangeEntityAwakeState | Dormir, pular turno, dormir até curar | EntityGuid, AwakeState | DOL:159/184 | não (UI\ArmyScreen_ArmyActionsPanel.cs:528) | COMP |
| OrderToggleAutoExplore | Ligar ou desligar a auto-exploração | EntityGUID, AutoExplore | DOT:1666/1685 | AIA\ToggleOffAutoExploreActuator.cs:16; Nodes.cs:2135 | SIM |
| OrderUseAirport | Aerotransporte de exército | ArmyGUID, DestinationTileIndex | DOT:1757/1832 | GoToSubTree.cs:144 | COMP |
| OrderArmySettle / OrderGoToAndSettle(false) | Fundar posto | ArmyGUID | DOTI:4870/4905 (FillCreateCampFailures); DOT:1476/1512 | Nodes.cs:1244 | COMP |
| OrderArmyConvertToCity / OrderGoToAndSettle(true) | Fundar cidade com o exército | ArmyGUID | DOTI:4816/4848 (FillFoundCityFailures) | não | COMP |
| OrderUseSettlerAbility / OrderGoToAndUseSettlerAbility | Fundar cidade com colono | ArmyGUID, UnitGUID | DOTI:6809/6850; DOT:1596/1617 | Nodes.cs:1292 | COMP |
| OrderRansack / OrderGoToAndRansack | Saquear | ArmyGUID, TargetTileIndex, StopAction | DOD:4829/4863; DOT:1463/1468 | Nodes.cs:1422 | COMP |
| OrderStealTrade / OrderGoToAndStealTrade | Roubar rota comercial | idem | DOD:5235/5265; DOT:1583/1588 | Nodes.cs:1462 | COMP |
| OrderToggleArmyStealTerritoryAction / OrderGoToAndStealTerritory | Roubar território (afinidade expansionista) | ArmyGUID | DOD:5293/5338; DOT:1520/1553 | Nodes.cs:1376 | COMP |
| OrderCutForest / OrderGoToAndCutForest | Cortar floresta | ArmyGUID, StopAction | DOD:3661/3699; DOT:1135/1140 | Nodes.cs:1944 | NÃO |
| OrderArtilleryStrike | Bombardeio | ArmyGUID, UnitGUID, TargetTileIndex, ActionType | DOD:3402/3449 | Nodes.cs:2181; BB:395 | NÃO |
| OrderDisbandUnits | Dissolver unidades (exército ou esquadrilha) | UnitCollectionSimulationEntityGUID, UnitGUIDsToDisband | DOD:3889/3940 | Nodes.cs:1639 | SIM |
| OrderDisbandArmy | Dissolver o exército inteiro | ArmyGuid | DOD:3830/3852 | não (só Automation) | INT |
| OrderUpgradeUnits | Modernizar unidades | EntityGUID, UnitGUIDs, UpgradeNames | DOD:5783/5874 | Nodes.cs:2009 | SIM |
| OrderHealUnits | Curar pagando | EntityGUID, UnitGUIDs | DOD:4357/4426 | não | SIM |
| OrderRaiseSettlementReservistArmy | Reservistas (afinidade militarista) | SettlementGUID | DOD:4784/4809 | AIA\DoAffinityActionOnSettlement.cs:32 | SIM |
| OrderChangeUnitSpawnPosition | Onde as unidades da cidade surgem | SettlementGUID, UnitSpawnType, WorldPosition | DOD:3533/3563 | AIA\ChangeSpawnPosition.cs:16 | NÃO |
| OrderContinueGotoAction / OrderCancelAction | Continuação / cancelamento automáticos | ActionGUID | DOT:588/597; DOL:121/130 | postadas pela própria ArmyGoToAction (:459, :475) | NÃO |
| OrderFurtherActions / OrderTutorialMinorArmyFreeze | Fim de turno / tutorial | — | DOT:841/850; DOD:5747/5769 | não | NÃO |

### 2.6 Cercos e batalhas

| Ordem | Para quê | V/P | IA | LLM |
|---|---|---|---|---|
| OrderBattleLaunchCityAssault {BattleGUID, AssaultRole} | Assaltar a cidade (Attacker) ou fazer surtida (Defender) | DOB:623/641 | AIA\LaunchSortie.cs:33; Nodes.cs:1546 | SIM |
| OrderBattleContinueSiege {BattleGUID, EmpireIndex} | Manter o cerco | DOB:392/414 | Nodes.cs:1592 | SIM |
| OrderBattleAbandonSiege {BattleGUID, EmpireIndex} | Levantar o cerco (só o líder atacante) | DOB:164/182 | não | SIM |
| OrderBattleSurrender {BattleGUID, EmpireIndex} | O defensor entrega a cidade sitiada | DOB:978/996 | **não: a IA nativa nunca rende cidade** | SIM |
| OrderBattleRetreat | Recuar da batalha | DOB:419/437 | AIA\ConfirmOrRetreatFromBattle.cs:48; BB:1225 | VIÉS/NÃO (decide-se em segundos) |
| OrderBattleConfirmation | Confirmar a batalha | DOB:369/387 | ConfirmOrRetreatFromBattle.cs:42; BB:1183 | NÃO |
| OrderBattleBrainAddReinforcement / AddSupport | Reforço e apoio | DOB:233/268; 273/301 | Nodes.cs:1884; 2215 | NÃO |
| OrderBattleSetInstantResolve / OrderBattleInstantResolve / SetForcedAIControl / SetAutoDeploy | Configuração da batalha (InstantResolve **não checa se o império participa**) | DOB:911/941; 600/618; 884/906; 846/868 | — | NÃO |

- **Ordens táticas (NÃO), postadas pela BB:** OrderBattleAttack (187/228), Move (646/667), MoveAndAttack (672/729), MoveSwap (734/785), EndRound (488/510), EndUnitRound (515/548), EndDeployment (465/483), EndUnstack (553/572), SwapDeployingUnit (1001/1068), UnitConfirmDeployment (1177/1218), ChangeReserveUnitOrder (307/364), StopWaitingForThirdPartyReinforcement (946/964).
- **Confirmações (NÃO):**
  - OrderBattleResultAcknowledge (790/813), RoundAcknowledge (818/841), DeploymentAcknowledge (442/460), UnstackAcknowledge (1251/1269);
  - UnitActionAcknowledge, ClientAcknowledge e ServerAcknowledge (1073/1096, 1101/1134, 1139/1172);
  - OrderAirStrikeStart/ResultAcknowledge (92/111, 68/87), OrderArtilleryStrikeStart/ResultAcknowledge (140/159, 116/135);
  - OrderAcknowledgeBombardmentAftermath (DOCm:328/341).
- **Debug (CHEAT):** OrderBattleForceNextState (577/595), OrderBattleUnitSetHealth (1223/1246).

### 2.7 Aéreo, mísseis e nuclear (LLM: NÃO direto; no máximo VIÉS, P3)

| Ordem | V/P | IA nativa |
|---|---|---|
| OrderFlyTo | DOT:789/807 | Nodes.cs:2366; BTM\AssistMilitaryMissionByAir.cs:122 |
| OrderFlyToAndMerge | DOD:4192/4272 | BTM\RegroupAirforce.cs:75 |
| OrderSplitAndFlyTo | DOD:5032/5059 | AIA\SplitSquadron.cs:26 |
| OrderCreateAirStrike | DOB:1274/1358 | BT\PatrolAndBombardSubTree.cs:192; BB:473 |
| OrderToggleUnitsPatrol | DOD:5362/5411 | PatrolAndBombardSubTree.cs:120 |
| OrderMissileStrike / OrderNuclearStrike | DOD:4510/4566; 4586/4650 | BTM\UseMissiles.cs:119/122 |
| OrderDisbandSquadron | DOD:3862/3880 | — (só debug; o humano usa OrderDisbandUnits) |
| OrderSpawnSquadron / OrderAddUnitToSquadron | DOD:4980/5020; 3342/3394 | — (CHEAT) |

### 2.8 Espionagem

Os agentes são exércitos furtivos. Campos: ArmyGUID, (UnitGUID), TargetTileIndex, StopAction. Explicação de erro: `SIM\AgentAncillary.cs:312-677`.

| Ordem | V/P | IA nativa (BTM\AgentStealth.cs) | LLM |
|---|---|---|---|
| OrderWatchCity / OrderGoToAndWatchCity | DOD:5905/5939; DOT:1653/1658 | não | COMP |
| OrderTrackArmy | DOD:5452/5477 | :614 | COMP |
| OrderExploitDistrict / GoTo | DOD:4129/4163; DOT:1217/1222 | :630 | COMP |
| OrderDisruptDistrict / GoTo | DOD:4066/4100; DOT:1176/1181 | :633 | COMP |
| OrderManipulateCity / GoTo | DOD:4447/4481; DOT:1300/1305 | :648 | COMP |
| OrderDisorganizeArmy | DOD:4020/4045 | :626 | COMP |
| OrderCreateArmyDecoy | DOD:3627/3651 | :644 | COMP |

### 2.9 Assentamentos e territórios

| Ordem | Para quê | Campos | V/P (e explicação de erro) | IA | LLM |
|---|---|---|---|---|---|
| OrderSettlementEvolveToCity | Posto → cidade | SettlementGUID | DOTI:6575/6592 (CanEvolveToCity :6536) | AIA\DoSettlementAction.cs:39 | SIM |
| OrderAttachTerritoryToCity | Anexar posto a cidade | CampSettlementGUID, CitySettlementGUID | DOTI:5133/5187 (CanTerritoryBeAttachedToCity :5066; posse checada em :5110) | AIA\AttachTerritory.cs:49 | SIM |
| OrderMergeCityIntoCity | Fundir cidades | CityToMergeGUID, CityGUID | DOTI:6152/6190 (CanCityBeMergedIntoCity :6089) | AIA\MergeCity.cs:21 | SIM |
| OrderDetachTerritoryFromCity | Separar território, que vira posto | CityGUID, TerritoryToDetach | DOTI:5599/5634 (CanDetachTerritoryFromCity :5550) | não | SIM |
| OrderChangeCapital | Mudar a capital | NewCapitalGUID | DOTI:5421/5447 (CanChangeCapital :5296) | AIA\ChangeCapital.cs:16 | SIM |
| OrderBuyNeighbourTerritoryWithMoney | Comprar território estrangeiro (ação desbloqueável) | TerritoryIndex, ReceivingSettlementGUID | DOTI:5204/5235 (AffinityActionHelper.cs:204) | não | SIM |
| OrderRenameSimulationEntity | Renomear | SimulationEntityGUID, Name | DOTI:6302/6324 | AIA\RenameEntity.cs:16 | SIM |
| OrderRemoveSettlementImprovement | Demolir uma infraestrutura | SettlementGUID, SettlementImprovementName | DOTI:6263/6294 | não | talvez |
| OrderCancelCampConstruction | Cancelar posto em fundação | CampGUID | DOTI:5272/5289 | não (nem a UI usa) | NÃO |
| Arrasar cidade capturada | **Não é ordem própria**: `OrderEnqueueConstructible` com a ação construível RazeDistrict no tile central marca `DistrictCenterBeingRazed` (DOTI:3705-3715, 3763-3772) | — | — | — | COMP |
| OrderForceSettlementOwnerChange | Dá qualquer assentamento a qualquer império | SettlementGUID, EmpireIndex | DOTI:5729/5747 (não checa o dono) | ninguém | CHEAT |
| OrderForceCaptureCity | Captura forçada | CityGuid, NewOwnerIndex | DOD:4327/4349 | ninguém (UI de debug) | CHEAT |
| OrderChangeGodPublicOrder | Estabilidade "divina" em qualquer cidade | — | DOTI:5457/5475 (não checa o dono) | — | CHEAT |
| OrderGiveVisionAtPosition / OrderDiscoverWorld / OrderEnableFogOfWar (**global**) / OrderForceDistrictAffinity | — | — | DOTI:5883/5912; 5640/5649; 5654/5667; 5672/5710 | — | CHEAT |
| OrderSetControlledByHuman | Troca o controle humano/IA | ControlledByHuman | DOTI:6466 (sempre true)/6471 | — | NÃO (perigoso, §6) |
| OrderRemoveExplorationAt | — | WorldPosition, Range | sem processador | — | NÃO |

### 2.10 População, ações de cidade e afinidades culturais

| Ordem | Para quê | V/P | IA | LLM |
|---|---|---|---|---|
| OrderChangeWorkplacePopulation {SettlementGUID, WorkplaceType, PopulationDiff} | Especialistas | DOTI:5490/5538 | AIA\AssignPopulationToWorkplaces.cs:58 (re-otimiza sempre) | VIÉS |
| OrderMoveWorkplacePopulation | Idem (versão da UI) | DOTI:6207/6256 | não | VIÉS |
| OrderSetAssignmentPolicyDefinition / Expert | Política automática de população | DOTI:6394/6421; 6429/6458 | não | VIÉS (a IA sobrescreve) |
| OrderSettlementBanishPopulation | Banir população | DOTI:6491/6512 (CanSettlementBanishPopulation :6476) | AIA\DoSettlementAction.cs:19 | SIM |
| OrderSacrificePopulation | Sacrificar população | DOTI:6358/6379 (:6340) | :24 | SIM |
| OrderSettlementStartProcession | Procissão | DOTI:6677/6694 (:6650) | :29 | SIM |
| OrderSettlementLaunchInquisition | Inquisição | DOTI:6615/6636 (:6600) | :34 | SIM |
| OrderStealPopulationWithFarmerAffinity | Roubar população (afinidade agrária) | DOTI:6706/6731 (AffinityActionHelper.cs:40) | AIA\DoAffinityActionOnSettlement.cs:37 | SIM |
| OrderToggleIndustryModeAt / OrderToggleScienceModeAt | Modo indústria / ciência da cidade | DOI:2686/2703; DOS:1556/1573 | DoAffinityActionOnSettlement.cs:22,27; CancelProductionModeOnSettlement.cs:21,26 | SIM |
| OrderUseCulturalAffinity {TerritoryIndex} | Ganhar influência num território (afinidade cultural) | DOC:684/703 | AIA\DoCulturalAction.cs:16 | SIM |
| OrderResourceDepositInvestment / Buyout {DepositIndex} | Investir em jazida (afinidade mercante) | DOC:524/550; 580/606 | AIA\DoMerchantAction.cs:23/18 | SIM |
| OrderPurchaseCulturalOsmosisEvent / OrderDiscardCulturalOsmosisEvent {index} | Osmose cultural: comprar ou descartar | DOC:484/519; 448/479 | AIA\DoOsmosisAction.cs:23/18 | SIM |
| OrderBoostCulturalEconomicGain | Bônus cultural (não usada por UI nem IA) | DOC:429/442 | não | talvez |

### 2.11 Produção, construções e maravilhas

| Ordem | Para quê | Campos | V/P | IA | LLM |
|---|---|---|---|---|---|
| OrderEnqueueConstructible | Pôr item na fila. Distritos exigem tile; itens "só compra" são recusados | EnqueuePosition, SettlementGUID, ConstructibleName, WorldPosition | DOI:2181/2245 | AIA\BuildConstruction.cs:125 | SIM |
| OrderBuyoutAvailableConstruction (+WithPopulation) | Comprar algo que não está na fila | + CurrencyType | DOI:1495/1558; 1604/1647 | BuildConstruction.cs:117 | SIM |
| OrderBuyoutConstructionByGuid / At (+WithPopulation) | Comprar item da fila | SettlementGUID, ConstructionGUID ou índice | DOI:1767/1833; 1692/1745; 1942/2002; 1865/1916 | AIA\BuyoutQueuedConstruction.cs:55,61 | SIM |
| OrderMoveConstructionInConstructionQueue | Reordenar a fila | From, To | DOI:2340/2363 | não | SIM |
| OrderRemoveConstructionByGuid / At / ByName / RemoveAll | Cancelar | — | DOI:2508/2544; 2479/2502; 2562/2599; 2454/2468 | BuildConstruction.cs:143 (só os itens da própria IA) | SIM |
| OrderStartEmpireWideConstruction | Começar maravilha ou projeto nacional num tile | EmpireWideConstructionName, WorldPosition | DOI:2612/2679 (:2636) | AIA\PlaceArtificialWonderConstruction.cs:150, PlaceConsulateConstruction.cs:150, PlaceEmpireWideConstruction.cs:88 | SIM (médio) |
| OrderParticipateToEmpireWideConstruction / OrderCancelEmpireWideConstruction | Uma cidade contribui / cancelar | — | DOI:2369/2423; 2038/2069 | não | SIM |
| OrderClaimArtificialWonder / OrderUnclaimArtificialWonder | Reivindicar maravilha | ArtificialWonderName | DODv:2570/2580; 2773/2783 | AIA\ClaimArtificialWonder.cs:19 | SIM |
| OrderAdviseConstructible / OrderAskAdviseConstructible | Conselheiro de produção (só quando a IA está desativada) | — | DOI:1421/1464; 1474/1487 | BuildConstruction.cs:99 | NÃO |
| OrderCompleteAvailableConstruction / CompleteConstructionAt / CompleteEmpireWideConstruction | Concluir de graça | — | DOI:2074/2107; 2119/2152; 2161/2174 | não (GodMode: PR\BaseConstructiblePlacementCursor.cs:99-101) | CHEAT |

### 2.12 Ciência

| Ordem | Campos | V/P | IA | LLM |
|---|---|---|---|---|
| OrderEnqueueTechnology | TechnologyName, EnqueuePosition | DOS:1362/1385 (recusa Completed, LockedByEra, já na fila) | AIA\ResearchTechnology.cs:29 | SIM |
| OrderMoveTechnologyInQueue / RemoveTechnologyAt / RemoveTechnologyByName / ClearTechnologyQueue | — | DOS:1481/1503; 1508/1522; 1527/1551; 1352/1357 | ResearchTechnology.cs:78 (ByName, só as próprias) | SIM |
| OrderInvestResearch / OrderForceUnlockResourceTechnology | — | DOS:1467/1476; 1405/1414 | — | CHEAT |

### 2.13 Cívicos, ideologia e revolução

| Ordem | V/P | IA | LLM |
|---|---|---|---|
| OrderActivateCivic {CivicName, ChoiceIndex} | DODv:2389/2411 (FillCivicActivateFailureFlags :569; paga influência) | AIA\ActivateCivic.cs:17 | SIM |
| OrderCancelCivic {CivicName} | DODv:2442/2460 (:588) | não | SIM |
| OrderChangeRevolutionState, OrderGainRevolutionPoints, OrderModifyIdeologicalAxis, OrderUnlockIdeologicalAxes, OrderUnlockIdeologicalCombintation, OrderClearIdeologicalAxesDebug | DODv:2543/2552, 2683/2692, 2725/2738, 2788/2797, 2803/2812, 2585/2604 | — (GodMode, por exemplo UI\IdeologicalAxisRuler.cs:315) | CHEAT |

### 2.14 Religião

| Ordem | V/P | IA | LLM |
|---|---|---|---|
| OrderCreateFirstReligion {TenetName de tier 0} | DOR:330/348 (exige `CanCreateFirstReligion`) | AIA\ChooseTenet.cs:19 | SIM |
| OrderChooseNextTenet {TenetName, ReligionAffinity} | DOR:283/303 (FillLocalReligionCanChooseNextTenetFailureFlags :260) | ChooseTenet.cs:32 (a afinidade é sorteada: `(Archetypes*Turn) % n`, :25-31) | SIM |
| OrderChangeReligion {ReligionIndex} | DOR:174/184 (CanChangeReligion) | AIA\ChangeReligion.cs:16 | SIM |
| OrderChangeReligionAffinity {EmpireIndex, ReligionAffinityName} | DOR:203/220 (**muda a religião de qualquer império**; ninguém posta) | — | NÃO |
| OrderUnlockFirstReligion | DOR:360/373 (ninguém posta) | — | CHEAT |

### 2.15 Era, cultura, fama e recursos do império

| Ordem | V/P | IA | LLM |
|---|---|---|---|
| OrderChangeFaction {NextFactionName, ConfirmFactionChange, IgnoreFactionStatus} | DODv:2489/2528. Trava a cultura para os outros; aplicada no TurnEnd (`TurnEndPass_ChangeFactionIfNecessary` :2888 → `ApplyFactionChange` :2941) | AIA\ChangeFaction.cs:16 (Confirm=true) | SIM |
| OrderConfirmChangeFaction / OrderCancelChangeFaction | DODv:2625/2638; 2418/2431 (só dá para cancelar antes de confirmar) | não (UI\NextFactionChoicePanel.cs:1450/1457) | SIM |
| OrderInstantChangeFaction, OrderForceGainFame, OrderForceGainEraStarScore, OrderGainInfluence, OrderGainMoney (DOTr:1287, `return true`) | DODv:2714/2719, 2672/2677, 2644/2649, 2704/2709 | — | CHEAT |

### 2.16 Comércio de recursos

| Ordem | V/P | IA | LLM |
|---|---|---|---|
| OrderSetTradeResourceAccessCount {OtherEmpireIndex, ResourceType, Value} | DORs:1141/1166 (TradeController.GetTradeFailureFlags) | AIA\BuyOneResourceAccess.cs:19 | SIM |
| OrderBuyAllTradeResourceAccesses {OtherEmpireIndex, ResourceCategory} | DORs:1029/1054 | não (UI\TradeTable.cs:364) | SIM |
| OrderClearBoughtResourceAccesses {OtherEmpireIndex, ResourceBits} | DORs:1059/1084 | AIA\ClearBoughtResourceAccesses.cs:16 | SIM |
| OrderGiveGodResource | DORs:1099/1108 | — | CHEAT |

### 2.17 Eventos narrativos

| Ordem | V/P | IA | LLM |
|---|---|---|---|
| OrderMakeNarrativeEventChoice {NarrativeEventGUID, Choice} | DOCm:692/705. Só checa se o evento existe e ainda não foi escolhido. `NarrativeEventManager.MakeNarrativeEventChoice` (SIM\NarrativeEventManager.cs:1497-1520) **não checa o dono nem os FailureFlags da escolha** | AIA\MakeNarrativeEventChoice.cs:16 | SIM (o mod checa: dono, `0 ≤ escolha < n`, `NarrativeEventChoices[j].FailureFlags == None`) |

### 2.18 Turno, sistema e interface (todas NÃO, salvo indicação)

- **Turno**
  - OrderEmpireReady: DOCm:613/622; postada pela IA em FPAI\AIController.cs:1442. **INT**: segurar (§4.5).
  - OrderEmpireResign: DOCm:629/643; só para humano.
  - OrderEmpireLockedByMandatories: DOCm:603/608, sempre true. Perigosa (§6).
  - OrderForceFinishTurn: DOCm:661/671; postada pelo próprio TurnFinish em SBX\SandboxState_TurnFinish.cs:61.
- **Pings e notificações**
  - OrderCreatePingAt {TileIndex, Content (Defense/Offense/Desire), TargetFilter (Allies/AllMajorEmpires/TileOwner)}: DOCm:495/537; IA em AIA\PlacePing.cs:29. **SIM**: sinalizar no mapa para aliados.
  - OrderAcknowledgePing e OrderDestroyPing: DOCm:356/374 e 583/596.
  - OrderChangeNotificationStatus e Flags: DOCm:465/479 e 437/451.
- **Narração e câmera**
  - OrderAcknowledgeNarratorSentence: DOCm:346/351.
  - OrderChangeCameraSequenceState: DOCm:380/432.
  - OrderStartEndGameCameraSequence: DOCm:713/718.
- **Fim de jogo**
  - OrderContinueGameAfterEndGame: DOH:29/47.
  - OrderForceEndGame: DOH:53/58. **CHEAT**.
- **SandboxOrders**
  - OrderChangeGameOption: SIM\GameOptionsController.cs:46/60.
  - OrderWaitForOrderReplication e OrderChangeDumpBreakOnNetworkDesynchronization: SBX\Sandbox.cs:1851-1865.
- **Ações genéricas por dados** (sem uso por UI nem IA)
  - OrderCreateArmyAction e OrderCreateEmpireAction: DOL:207/212 e 242/247.
  - OrderCancelArmyAction e OrderCancelEmpireAction: DOL:135/140 e 147/152.

### 2.19 Ordens de editor e debug (todas CHEAT)

- **Quais são:** 67 `EditorOrder*` (IO\EditorOrder*.cs), processadas em `SIM\EditorOrderProcessors.cs:15-73` (Validate :172, Process :166); algumas ficam em departamentos (DOCm:12, DOC:14-17, DODv:15, DOFA:17, DOT:13).
- **Lista:**
  - Diplomacia e moral: ForceWar, ForcePeace, ForceAlliance, ForceVassalToLiege, MeetEverybody, ForceGainMoral, SetWarScore, AddOrRemoveLeveragePointStock.
  - Estoques: SetMoneyStock, SetInfluenceStock, AddOrRemoveAtmospherePollution.
  - Assentamentos e distritos: Create/Destroy Settlement, Camp e City; CreateExtensionDistrictAt, CreateArtificialWonderAt, RemoveDistrictAt, Set/RemoveSettlementImprovement, EvolveCampToCity, SetCapital, MergeSettlementIntoCity, DetachTerritoryFromCity, AddOrRemovePopulation, RenameSettlement, EnqueueConstructible, RemoveConstructionAt, SetGodCityCap.
  - Exércitos e esquadrilhas: Create/Destroy, Add/RemoveUnit, SetArmyMovementRatio, SetArmyMission, SetInfiniteMovement, Set*StealthValue, Rename*.
  - Tecnologia e cívicos: Complete(All)Technology, EnqueueTechnology, ClearTechnologyQueue, ActivateCivic, UnlockCivic.
  - Mundo e povos: Create/RemoveCuriosity e Collectible; Create/RemoveMinorEmpire e AnimalGroup; SetExploration, SetResourceDepositAccess; PathCuriosity*; RecomputeTradePath; SelectFirstValidFaction.
  - Recargas de afinidade: Reset*Cooldown e ResetMerchantAffinityGauge.
- **Atenção:** a política de TurnMain é `EmpireNotAlive→Reject, CatchAll→Accept` (`SBX\Sandbox.cs:1702`) e ordens de editor têm `TargetEmpireIndex = -1` (IO\EditorOrder.cs:9). Ou seja, **são aceitas em partida normal**.

### 2.20 IDs mortos, sem processador e não usados

- **Valores de `OrderIdentifier` sem classe** (IO\OrderIdentifier.cs; `Order.ScanAssembliesForOrderTypes` só procura no Firstpass, Order.cs:115-129):
  - OrderToggleMoneyModeAt, OrderMoveThrough, OrderCreateSiegeUnit, OrderCreateMilitiaArmy;
  - OrderNotifyEvolutionChecked, OrderChoosePrehistoricFactionTrait, OrderForceGainFameFeatScore;
  - OrderCreateTradeRoad, OrderCreateForwardedTradeRoad, OrderCloseTrade, OrderForceDiscoverResource;
  - os 8 `OrderNarrativeEvent*`, EditorOrderDryRunNarrativeEvent e OrderChangeDumpFrequency.
- **Com classe, sem processador:** OrderRemoveExplorationAt.
- **Funcionam, mas ninguém posta:**
  - OrderDeclareWar* combinadas (a UI declara com `OrderDeclareAnyWar` e manda o movimento separado, PR\BaseArmyCursor.cs:720-740);
  - OrderBoostCulturalEconomicGain, OrderCancelCampConstruction, OrderChangePatronageAmount, OrderChangeReligionAffinity, OrderStartCrisisVote (só debug);
  - as ações genéricas da §2.18.

---

## 3. Ferramentas para a IA de linguagem

### 3.1 Códigos curtos (handles) necessários

O mod mantém a tabela código → id a cada turno, na thread do sandbox. Busca de entidade: `Sandbox.SimulationEntityRepository.TryGetSimulationEntity(guid, out T)`.

| Código | Entidade no jogo | Id interno / onde ler |
|---|---|---|
| E# | império maior | índice (`MajorEmpires[i].Index == i`) |
| M# | povo independente | índice do menor (≥ NumberOfMajorEmpires). **Conferir o nome a cada turno: índice de menor é reaproveitado** |
| C# | cidade ou posto (o dossiê diz o tipo) | `Settlement.GUID` |
| A# | exército (Q# para esquadrilha, opcional) | `Army.GUID`. **Muda ao dividir ou fundir** |
| T# | território | índice. Tile-alvo: centro administrativo ou `Territory.VisualCenter` |
| G# | reclamação disponível contra E# | `embassy.AvailableGrievances[i]` |
| S# | cerco | `settlement.Siege.Entity.Battle.GUID` (SIM\Siege.cs:34) |
| N# | evento narrativo pendente | `World.NarrativeEventInfo` + `NarrativeEvent.GUID` |
| K# / B# / V# / F# / R# / W# / X# / O# | opções do turno: tecnologia, construível por cidade, cívico.escolha, cultura, dogma ou religião, maravilha, recurso, oferta de osmose | nomes de definição (`StaticString`), mostrados como **menus do dossiê só com o que é válido** |

Onde ler os menus:
- tecnologias: `DOS.Technologies` (DOS:27);
- construíveis: `Settlement.AvailableConstructions[i]`, com FailureFlags e custos (`SIM\AvailableConstruction.cs`);
- cívicos: `DODv.Civics` (DODv:46);
- culturas: `DODv.NextFactionInfos`, com Status e LockingEmpireIndex (DODv:52);
- dogmas: `ReligionManager.TenetInfo` / `TenetTierInfo`;
- eventos: `NarrativeEvent.NarrativeEventChoices[j].FailureFlags`.

### 3.2 Lista priorizada (F = fácil, M = médio, D = difícil)

**P0: diplomacia (exclusiva da IA de linguagem, §11.0). Quase tudo é uma `OrderDiplomaticAction`.**

| Ferramenta | JSON | Ordem(ns) | Dif. | Pré-checagem / notas |
|---|---|---|---|---|
| declarar_guerra | `{"acao":"declarar_guerra","nacao":"E2","tipo":"formal\|surpresa\|auto","motivo":"..."}` | DeclareFormalWar / DeclareSurpriseWar; "auto" = OrderDeclareAnyWar | F | `GetDiplomaticActionFailureFlags` (DOFA:1330). Traduzir: BelowFormalWarMoralThreshold, UseFormalWarInstead, Answer*First, PendingInternationalCrisis, NotEnoughInfluence, OtherIsVassal (atacar o suserano, como PR\BaseArmyCursor.cs:727) |
| propor_tratado | `{"acao":"propor_tratado","nacao":"E2","tratado":"paz\|alianca\|fim_crise\|fim_rebeliao"}` | ProposeEndWarTreaty / ProposeAllianceTreaty / ProposeEndCrisisTreaty / ProposeEndRebellionTreaty | F | CantProposeAgainYet (repropor custa influência), TreatyRefusedBy* |
| responder_tratado | `{"acao":"responder_tratado","nacao":"E2","resposta":"aceitar\|contrapropor\|ignorar"}` | SignTreaty / CounterTreaty / IgnoreTreaty | F | Permitido também em TurnFinish |
| propor_acordo / responder_acordo / romper_acordo | `{"acao":"propor_acordo","nacao":"E2","acordo":"economico\|informacao\|cultural\|militar"}`; `{"acao":"responder_acordo","nacao":"E2","resposta":"aceitar\|contrapropor\|ignorar"}`; `{"acao":"romper_acordo","nacao":"E2","acordo":"..."}` | Propose*Agreement / Sign, Counter, IgnoreAgreement / Break*Agreement | F | AgreementsAtTheirHighest, OwnerHasDemand |
| romper_alianca | `{"acao":"romper_alianca","nacao":"E2"}` | DeclareEndOfAlliance | F | — |
| exigir / renunciar_queixas | `{"acao":"exigir","nacao":"E2","queixas":["G1","G4"]}` ou `"queixas":"todas"` | OrderExecuteGrievanceAction(CreateDemand) / ...Batch / RenounceGrievance | F | `DiplomaticState.CheckPrerequisitesFor(grievanceAction, eu, idx)` (BaseDiplomaticState.cs:643). Exigência em texto livre não existe no jogo: fica como carta ou ultimato do mod |
| responder_exigencias / retirar_exigencias | `{"acao":"responder_exigencias","nacao":"E2","resposta":"aceitar\|recusar\|enrolar"}`; `{"acao":"retirar_exigencias","nacao":"E2"}` | AcceptDemands / RefuseDemands / StallForTime / WithdrawDemands | F | AlreadyStalling, DemandRefused |
| crise_internacional / ceder_na_crise | `{"acao":"crise_internacional","nacao":"E2"}` | DeclareInternationalCrisis / ProposeInternationalCrisisCompliance | F | OwnerHasNoDemand, Locked |
| rendicao | `{"acao":"oferecer_rendicao"\|"impor_rendicao","nacao":"E2","termos":{"exigencias":["D1"],"territorios":["T7"],"dinheiro":true,"submissao":false}}`; `{"acao":"responder_rendicao","nacao":"E2","resposta":"aceitar\|recusar"}` | StartToFill → ChangeSurrender*State → ProposeToSurrender; ou AllowToForceOtherToSurrender → termos → DeclareSurrender; Accept/RefuseSurrender | M | Ver `crisis-negotiation.md`. Termos checados em DOFA:1123-1250 (FailureFlags por território, placar ≥ custo) |
| presentear / responder_presente | `{"acao":"presentear","nacao":"E2","ouro":200,"influencia":0,"assentamentos":["C9"],"exercitos":["A3"]}`; `{"acao":"responder_presente","nacao":"E2","resposta":"aceitar\|recusar"}` | StartToFillGiftProposition → ler `embassy.CurrentGiftToOtherProposition` → OrderUpdateGiftInfo × n / OrderUpdateGiftFimsInfo → ProposeGift; Accept/RefuseGift | M | DiplomaticAncillary.cs: GetGiftFimsFailureFlags :1154, GetEntityFailureFlags :1175, GetPropositionFailureFlags :1194, GetSettlementGiftFailureFlags :1230, GetArmyGiftFailureFlags :1255. **Presente sem resposta é aceito sozinho no TurnFinish** |
| apresentar_se | `{"acao":"apresentar_se","nacao":"E5"}` | IntroduceYourself | F | Só com contato de mão única |
| acordo_consular / alavancagem | `{"acao":"acordo_consular","nacao":"E2","acordo":"tecnologia\|unidade_emblematica\|maravilha\|visao\|status_populacao\|esfera","modo":"propor\|aceitar\|contrapropor\|recusar\|cancelar"}`; `{"acao":"alavancagem","nacao":"E2","tipo":"reduzir_placar\|exercitos_indesejados\|forcar_exigencias\|revelar_agentes\|sancoes"}` | OrderMakeConsulatAgreementAction / OrderUseLeverageAction | F | DiplomaticConsulatHelper.GetConsulatAgreementFailureFlags / ComputeLeverageActionInfos |

**P0: decisões de Estado (fáceis, mas exigem suprimir ou enviesar a IA nativa, §5)**

| Ferramenta | JSON | Ordem | Dif. | Pré-checagem |
|---|---|---|---|---|
| escolher_cultura | `{"acao":"escolher_cultura","cultura":"F2"}` | OrderChangeFaction{Confirm=true} | F (M com controle do momento) | `NextFactionInfos[i].Status == Unlocked`; validação em DODv:2489-2526. **Irreversível depois de confirmar** |
| adotar_civismo / revogar_civismo | `{"acao":"adotar_civismo","civismo":"V3","opcao":2}` | OrderActivateCivic / OrderCancelCivic | F | `GetCivicIndex` (DODv:201) + `FillCivicActivateFailureFlags` (:569) |
| escolher_dogma | `{"acao":"escolher_dogma","dogma":"R4","afinidade":"opcional"}` | OrderCreateFirstReligion (tier 0) / OrderChooseNextTenet | F | DOR:260 / :330 |
| mudar_religiao | `{"acao":"mudar_religiao","religiao":"R1"}` | OrderChangeReligion | F | CanChangeReligion |
| decidir_evento | `{"acao":"decidir_evento","evento":"N1","escolha":2}` | OrderMakeNarrativeEventChoice | F | O mod checa dono, faixa e FailureFlags (§2.17) |
| pesquisar | `{"acao":"pesquisar","tecnologia":"K7","quando":"agora\|depois"}` | OrderEnqueueTechnology(AtBegin\|AtEnd) | F | A IA só cancela o que ela mesma pôs na fila (AIA\ResearchTechnology.cs:76-82); "agora" sobrevive |
| renomear (já existe) | `{"acao":"renomear","alvo":"C14\|A7","nome":"..."}` | OrderRenameSimulationEntity | F | DOTI:6302 |
| definir_postura / definir_foco (já existem) | — | sem ordem (viés); opcionalmente refletir no humor da UI (§5) | — | — |

**P1: economia e império**

| Ferramenta | JSON | Ordem(ns) | Dif. | Notas |
|---|---|---|---|---|
| produzir | `{"acao":"produzir","cidade":"C3","item":"B12","quando":"agora\|depois","comprar":"nao\|ouro\|influencia\|populacao"}` | OrderEnqueueConstructible (AtBegin) ou OrderBuyoutAvailableConstruction(*) | F; M para distritos | Distrito precisa de tile: `RequestDistrictPlacementEvaluation{SettlementGUID, DistrictName}` → maior ganho, como em AIA\BuildConstruction.cs:216-233, 345-428 |
| comprar_producao / cancelar_producao | `{"acao":"comprar_producao","cidade":"C3","moeda":"ouro\|populacao"}`; `{"acao":"cancelar_producao","cidade":"C3","item":"B12"}` | OrderBuyoutConstructionByGuid(*) / OrderRemoveConstructionByGuid | F | Só cancelar itens que a própria IA de linguagem pôs |
| gerir_assentamento | `{"acao":"evoluir_posto","posto":"C9"}`; `{"acao":"anexar_posto","posto":"C9","cidade":"C3"}`; `{"acao":"fundir_cidades","cidade":"C4","em":"C3"}`; `{"acao":"destacar_territorio","cidade":"C3","territorio":"T7"}`; `{"acao":"mudar_capital","cidade":"C3"}`; `{"acao":"libertar_cidade","cidade":"C5"}`; `{"acao":"comprar_territorio","territorio":"T12","cidade":"C3"}` | ordens da §2.9 / §2.4 | F | Funções `Can…` da §2.9 |
| arrasar_cidade | `{"acao":"arrasar_cidade","cidade":"C5"}` | OrderEnqueueConstructible com a ação RazeDistrict no tile central | M | Fila de cidade capturada aceita 1 item (DOI:1031-1034) |
| comercio_recurso | `{"acao":"comprar_recurso","nacao":"E2","recurso":"X4","quantidade":2}` (ou `"tudo"`, ou `0` para parar) | OrderSetTradeResourceAccessCount / BuyAll / ClearBought | F | TradeController.Get*FailureFlags |
| povos | `{"acao":"patrocinar","povo":"M2","ouro":"nenhum\|baixo\|medio\|alto","influencia":"..."}`; `{"acao":"tratado_povo","povo":"M2","tratado":"contato\|comercio\|mercenarios\|ciencia\|lucros\|contrato\|intercambio\|vassalo\|anexar"}`; `{"acao":"contratar_mercenarios","exercito":"A20"}`; `{"acao":"dar_posto_a_povo","posto":"C9","povo":"M2"}` | §2.4 | F | Efeito imediato, sem proposta |
| congresso | `{"acao":"propor_votacao_civismo","civismo":"V5"}`; `{"acao":"votar","votacao":"civismo\|crise","opcao":1}` (crise: `"declarante\|alvo"`); `{"acao":"subornar_voto","votacao":"...","nacao":"E3","vezes":1}`; `{"acao":"apoiar_consenso","eixo":"I2"}` | OrderStartCivicVote / OrderInternationalAction | F | InternationalAncillary.cs:342 / :1277 |
| maravilha | `{"acao":"reivindicar_maravilha","maravilha":"W3"}`; `{"acao":"construir_maravilha","maravilha":"W3","territorio":"T9"}`; `{"acao":"contribuir_maravilha","cidade":"C3","maravilha":"W3"}` | Claim / StartEmpireWideConstruction / Participate | M | Tile via `RequestEmpireWideConstructionPlacement` (Sandbox.cs:2292, 2501) |
| habilidade_afinidade | `{"acao":"habilidade","tipo":"reservistas\|roubar_populacao\|modo_industria\|modo_ciencia\|influencia_cultural\|desmilitarizar\|investir_deposito\|comprar_deposito","alvo":"C3\|T7\|X2"}` | §2.10 | F | AffinityActionHelper.cs: 40, 72, 145, 499; DOC:147; DOTI:454 |
| acao_cidade | `{"acao":"acao_cidade","cidade":"C3","tipo":"procissao\|inquisicao\|banir\|sacrificar"}` | §2.10 | F | DOTI:6650, 6600, 6476, 6340 |
| osmose | `{"acao":"osmose","oferta":"O1","resposta":"comprar\|descartar"}` | Purchase / DiscardCulturalOsmosisEvent | F | — |

**P2: militar**

| Ferramenta | JSON | Ordem(ns) | Dif. | Notas |
|---|---|---|---|---|
| ordem_exercito (ampliar a atual) | `{"acao":"ordem_exercito","exercito":"A7","objetivo":"mover\|atacar\|cercar\|reforcar\|juntar\|saquear\|acampar\|fundar_cidade\|roubar_territorio\|parar\|descansar\|explorar","alvo":"T31\|C14\|A9\|S2","declarar_guerra_se_preciso":false,"turnos_max":5,"motivo":"..."}` | `SimulationEvaluator.Evaluate(RequestArmyActionAt)` → GoTo / GoToAndCreateBattle / CreateBattle / GoToAndJoinBattle / GoToAndSettle / GoToAndRansack… (mapeamento em PR\BaseArmyCursor.cs:1324-1501) | D | Inicializar o request como `PR\ArmyActionAtAsyncOperation.cs:37-58` (PenultimateTileIndex = -1 etc.). Exige a trava do exército contra a IA nativa (ArmyAllocator.cs:53/75) e estado de missão multi-turno. Flag `NeedToDeclareWar` = recusar ou, se autorizado, `OrderDeclareAnyWar` antes |
| cerco | `{"acao":"cerco","cerco":"S2","decisao":"assaltar\|manter\|abandonar\|surtida\|render_cidade"}` | §2.6 | F | Preencher `EmpireIndex` = o próprio império |
| tropas | `{"acao":"dissolver"\|"modernizar"\|"curar","exercito":"A7"}` | DisbandUnits / UpgradeUnits / HealUnits | F | Modernizar e curar exigem estar em território com a habilidade (`HasUpgradeUnitAbilityAt` / `HasHealUnitAbilityAt`) |
| espionar | `{"acao":"espionar","agente":"A12","tipo":"vigiar_cidade\|rastrear_exercito\|explorar_distrito\|sabotar_distrito\|manipular_cidade\|desorganizar_exercito\|isca","alvo":"C3\|A9\|T5"}` | §2.8 (versão GoTo se longe) | M | AgentAncillary.Get*FailureFlags |
| sinalizar | `{"acao":"sinalizar","tipo":"defesa\|ataque\|desejo","territorio":"T5","para":"aliados\|todos\|dono"}` | OrderCreatePingAt | F | Avisa o humano aliado de forma visual |

**P3: fica com a IA nativa (difícil)**
- Golpes aéreos, mísseis e nuclear, divisão de exército por unidade, batalha tática.
- Se quiser, só uma flag de viés do tipo "autorizar_nuclear" (não pesquisado).

**Nunca vira ferramenta**
- Ordens de confirmação, câmera e opção de jogo.
- Editor e cheats.
- ForceSign*, DeclareForcedWar e ForceWhitePeace (estas só como INT).
- SetControlledByHuman, EmpireReady, Resign, ForceFinishTurn, LockedByMandatories.

**Como as ações atuais do `DecisionParser` mudam**

| Ação atual | Vira |
|---|---|
| propor_paz | ProposeEndWarTreaty (termos livres continuam em carta) |
| propor_acordo / romper_acordo com `alianca` | ProposeAllianceTreaty / DeclareEndOfAlliance |
| exigir | G-handles + carta |
| presentear | cadeia de presente |
| ordem_exercito | avaliador + trava |
| renomear | direta |
| definir_postura / definir_foco | continuam viés |
| bloquear_correspondencia | continua do mod |

---

## 4. Mecânica de execução

### 4.1 Threads

**Postar**
- `PostOrder` e `PostAndTrackOrder` são seguros em qualquer thread: só enfileiram (SBX\PostOrderController.cs:173-177).
- Validar e processar acontece sempre na thread do sandbox.

**Montar a ordem**
- Montar a ordem (GUIDs, caminhos, failure flags) exige ler a simulação viva.
- Isso só é seguro na thread do sandbox (`llm-dossier-api.md` §1a).

**Onde o ticket fecha**
- **Thread do sandbox:** na hora (SandboxManager.cs:161-165).
- **Thread principal:** via `Presentation.Update` (Presentation.cs:292).
- **`Task` de fundo:** **nunca**, a menos que ela mesma chame `RaiseTicketsFromCallingThread`. Além disso, a fila de tickets por thread cresce sem limite.

**Recomendação: executor na thread do sandbox**
- Prefix em `PostOrderController.Update()` (classe internal; SBX\PostOrderController.cs:106).
- Ou postfix em `AIController.LateUpdate` (FPAI\AIController.cs:1369).
- O executor esvazia uma `System.Collections.Concurrent.ConcurrentQueue<Intent>` que a `Task` do HTTP preenche.
- **Nunca esperar um ticket dentro da thread do sandbox (trava tudo).** Use callback.

### 4.2 Resultado do ticket

`ticket.IsDone`, `ticket.Result` (SBX\PostOrderResponse.cs) e `ticket.UponCompletion(Action)` / `UponCompletionWithParam(Action<IBillet>)` (PostOrderTicket.cs:16-44).

| Resultado | Significado | O que devolver à IA |
|---|---|---|
| Valid | Validou **e o Process já rodou**. Exceção dentro do Process é engolida (Sandbox.cs:1830-1833) e o ticket continua Valid. Ações longas (GoTo, batalha) podem falhar depois | "executado"; conferir o efeito no próximo dossiê ou por `SimulationEvent_OrderProcessed` |
| Invalid | Validate deu false | Rodar de novo a pré-checagem e traduzir os flags |
| Rejected | Política da fase (por exemplo, TurnFinish ou TurnEnd) | "fora de hora; tente no próximo turno" |
| Undefined, ou sem resposta depois de ~5 s | Exceção dentro do Validate: não há resposta nenhuma (Sandbox.cs:1756 sem try; o laço loga em :3898) | "erro interno"; olhar o log |

### 4.3 Funções para explicar a recusa (internas, publicizadas, só na thread do sandbox)

| Área | Funções |
|---|---|
| Diplomacia | `GetDiplomaticActionFailureFlags` (DOFA:1330); `CheckPrerequisitesFor(grievanceAction,…)` (BaseDiplomaticState.cs:643) |
| Presentes | DiplomaticAncillary.cs:1154-1255 |
| Povos e tratados | FillPatronizeMinorEmpireFailures (DOFA:1848), FillEnactMinorEmpireTreatyFailures (:1431) |
| Congresso | InternationalAncillary.cs: 342 / 758 / 1277 |
| Cívicos | DODv:569 / 588 |
| Religião | DOR:227 / 260 |
| Interior | DOTI: 4994, 5066, 5296, 5550, 5937, 5979, 6089, 6340, 6476, 6536, 6600, 6650 |
| Exército | ArmyActionHelper (FillCreateCamp / FoundCity / GiveCamp / RentArmy); RansackController.cs:232/295; AgentAncillary.cs:312-677; BombardmentHelper.cs:1011 |
| Afinidades | AffinityActionHelper.cs:40-499 |
| Construção | `Settlement.AvailableConstructions[].FailureFlags`, `ConstructibleHelper.IsValid` |
| Maravilhas | `ArtificialWonderManager.CanClaimArtificialWonder` (DODv:2573) |
| Movimento | `RequestArmyActionAt.{PathActionFailureFlags, ArmyActionFailureFlags, PathActionConditionFlags}` |

### 4.4 Políticas por fase (SBX\Sandbox.cs:1691-1724)

- **TurnBegin:** tudo é **enfileirado** e validado quando chega o TurnMain (:1700).
- **TurnMain:** aceita tudo, menos ordens de império morto (:1702).
- **TurnFinish / TurnFinished:** só uma lista branca é aceita (:1703):
  - OrderDiplomaticAction, mas só as respostas (BaseDiplomaticState.cs:682-726);
  - OrderMakeConsulatAgreementAction, OrderTransferUnits, OrderContinueGotoAction, OrderCancelAction, OrderArmySettle, OrderArmyConvertToCity;
  - as ordens de batalha e as confirmações.
- **TurnEnd / TurnEnded / AutoSave:** rejeita (:1706, 1713-1714).

### 4.5 Ordem e momento dentro do turno

**Linha do tempo**
- TurnMain.Begin: captura do dossiê e `AIController.Unlock` (SBX\SandboxState_TurnMain.cs:12-21).
- A IA nativa começa uns 2 s depois e volta a cada ~200 ms.
- A resposta da IA de linguagem chega depois de segundos, então a IA nativa já agiu (ver §5 e a supressão do design §11.0).

**Segurar a vez da nação** (senão o turno acaba antes das ordens)
- O turno só fecha quando todos os impérios vivos estão "prontos" (`VerifyWhetherAllEmpiresAreReady`, SandboxState_TurnMain.cs:45-56).
- A IA nativa posta `OrderEmpireReady` em FPAI\AIController.cs:1430-1444.
- Para segurar: prefix em `AIController.LateUpdate` que zera `empireRunStatePerEmpireIndex[k].IsReadyToPassTheTurn` enquanto houver ordem pendente. Sempre com timeout, por exemplo 60-90 s.
- A alternativa (prefix em `ValidateOrderEmpireReady`, DOCm:613) faz a IA repostar a ordem a cada laço.

**Sequência sugerida para um lote de ordens**
1. Diplomacia: declarar guerra antes de atacar, porque o ataque exige `HasAttackDiplomaticAbility`.
2. Cultura, cívicos, dogmas, pesquisa.
3. Produção e compras.
4. Território.
5. Exércitos, avaliados **depois** que o ticket da guerra voltar Valid.

**Encadeamento**
- As ordens são processadas na ordem de postagem, e cada validação já vê o efeito da anterior.
- Encadeie com callback só quando a ordem seguinte precisa de um id que a anterior criou: índice do presente, GUID de exército novo, GUID de batalha.

**Revalidar**
- Revalide tudo na hora de executar: o dossiê tem segundos ou minutos de atraso.

**Alternativa determinística**
- Aplicar as decisões do turno N no `TurnMain.Begin` do turno N+1, antes de a IA nativa destravar.
- Atrasa um turno, mas não há corrida.

### 4.6 Esqueleto do executor (thread do sandbox)

```csharp
[HarmonyPatch("Amplitude.Mercury.Sandbox.PostOrderController", "Update")]
static class BombaDeOrdens {
    static void Prefix() {
        var sb = SandboxManager.Sandbox;
        if (sb == null || sb.IsSessionOnline) return;
        bool main = sb.CurrentStateName == "SandboxState_TurnMain";
        for (int n = Fila.Count; n > 0 && Fila.TryDequeue(out var it); n--) {
            if (!main && !it.PermitidoEmTurnFinish) { Fila.Enqueue(it); continue; }
            if (!Resolver.TentaMontar(it, out Order ordem, out string erro)) { Retorno.Falha(it, erro); continue; } // handles → GUID + failure flags
            PostOrderTicket t = SandboxManager.PostAndTrackOrder(ordem, it.Imperio);   // nunca o índice do humano
            t.UponCompletion(() => Retorno.Resultado(it, t.Result));                    // dispara na thread do sandbox
            Timeouts.Registrar(it, t, TimeSpan.FromSeconds(5));
        }
    }
}
```

---

## 5. Escolhas que são fluxo de interface

São ordens comuns; o "fluxo de tela" só existe para o humano. Opções para cada uma:
- **(A) Suprimir e postar:** prefix no generator da IA nativa só para impérios da IA de linguagem, e o executor posta a ordem. Volta ao nativo se a API cair.
- **(B) Enviesar:** postfix na análise para a IA nativa escolher o que a IA de linguagem quer, no momento dela.
- **(C) Correr na frente:** postar no `TurnMain.Begin` uma decisão tomada no turno anterior.

A IA nativa roda numa thread própria: a tabela de decisões consultada pelos patches tem que ser imutável e trocada de forma atômica.

| Decisão | Interface humana | Ordem(ns) | Onde a IA nativa decide | Recomendação |
|---|---|---|---|---|
| Cultura da próxima era (e quando trocar) | UI\NextFactionChoicePanel.cs:1428-1460 (escolher → confirmar / cancelar) | OrderChangeFaction (+ Confirm / Cancel) | `NextFactionChoice.Process` (AI.Brain\…Analysis.Economy\NextFactionChoice.cs:56-63; sorteio ponderado entre as 3 melhores, :492-547); momento em `WantFactionChange` (WantFactionChange.cs:31-76); `GEN\ChangeFaction.cs:30-86`; atuador AIA\ChangeFaction.cs:14-21 (confirma direto) | **A**: prefix em `Generators.ChangeFaction.GetContexts` + `OrderChangeFaction{Confirm=true}`. Fallback **B**: postfix em `NextFactionChoice.Process` gravando `analysisData.WantedFaction` (só com Status Unlocked) |
| Cívicos | UI\CivicsChoicePanel.cs:239/247 | OrderActivateCivic / OrderCancelCivic | `CivicsNeed.Process` (…Analysis.Culture\CivicsNeed.cs:38-63, `BestChoiceIndex`) → `GEN.Culture\ActivateOneCivic.cs:60-107` | **B**: postfix em CivicsNeed ajustando `CivicEvaluations[i].BestChoiceIndex/Scores`; ou **A** para escolhas pontuais |
| Fundar religião e dogmas | UI\CreateReligionWindow.cs:184; UI\ReligionTenetsScreen.cs:250 | OrderCreateFirstReligion / OrderChooseNextTenet | `GEN\CreateReligion`, `ChooseTenet<T>` (GEN\ChooseTenet.cs:26-71), `ChooseTenetForArchetype` / `ChooseTenetForFims` (IsTenetValid) | **A**, que também deixa escolher a afinidade (a nativa sorteia) |
| Mudar de religião | UI\ReligionChoiceWindow.cs:281 | OrderChangeReligion | `GEN\ChangeReligion` → AIA\ChangeReligion.cs | **A** |
| Eventos narrativos | UI\NarrativeWindow.cs:534 | OrderMakeNarrativeEventChoice | `GEN\MakeNarrativeEventChoice.cs:34-110`: motivação = soma dos pesos de Archetype de cada escolha; roda cedo (ExecutionMode Minimal) | **A** com prazo dentro do turno: eventos são fechados no TurnEnd (`Pass_TurnEnd_ClearObsoleteEvents`, NarrativeEventManager.cs:2021). Fallback: o nativo decide se a IA de linguagem não responder |
| Votações do congresso | UI\InternationalScreen_CivicSelectionPanel.cs:201; InternationalScreen_BribePopup.cs:275 | OrderStartCivicVote / OrderInternationalAction | `GEN.International\*` (StartInternationalCivicVote, ManageInternationalCivicVote, ManageInternationalCrisisVote, ResolveInternationalCrisisVote, ContributeToInternationalIdeologicalConsensus) | **A** |
| Osmose cultural | UI\CulturalOsmosisAppendixSalePanel.cs:342/356 | Purchase / Discard | `GEN.Culture\AnswerCivicsOsmosis`, `GainOneTechnologyByOsmosis` → AIA\DoOsmosisAction | **A** ou deixar com o nativo |
| Maravilhas | UI\ArtificialWondersWindow.cs:428/437; PR\EmpireWideConstructionPlacementCursor.cs:135 | Claim / Unclaim / StartEmpireWideConstruction | `GEN\ClaimArtificialWonder`, `PlaceArtificialWonderConstruction`, `GEN.Construction\BuildWonders` | **B** (a IA de linguagem escolhe qual; o nativo posiciona) |
| Presentes, tratados, exigências e rendição recebidos | UI\DiplomaticRelationsPanel_GiftPanel.cs:349/359; DiplomaticCrisisPanel_* (:305, :638, :666, SurrenderPanel :524-544); DiplomaticConsulatPanel.cs:328/343 | OrderDiplomaticAction, etc. | `GEN.Diplomacy\*` (AnswerGiftProposition, HandleDiplomaticProposition, ManageTreaties, ManageDemands, ManageGrievances, Surrender, ForceSurrender, FillSurrenderTerms, EndSurrender, ManageConsulateAgreements, DoDiplomaticAction, DoDiplomaticLeverageAction) | **A**, já decidido no design §11.0. Lembrar a impaciência do TurnFinish (`diplomacy-native-timers.md`) |
| Povos independentes | UI\MinorFactionPatronagePanel_InvestmentRuler.cs:111; MinorFactionTreatiesPopup.cs:166 | §2.4 | `GEN.MinorRelation\*` (PatronizeMinor, Start/StopPatronizingMinor, EnactMinor*Treaty incl. Puppet/Assimilation, RentMinorArmy, GiveCampToMinor) | **A** para Puppet/Assimilate (decisão grande); **B** para o resto |
| Humor mostrado ao humano | tela de diplomacia (embassy.CurrentAIBehaviourState) | OrderUpdateRelationAIFeedback | `GEN.Diplomacy\UpdateAIFeedback` → AIA\UpdateAIFeedback.cs:145 | Patch em `UpdateAIFeedback.CreateAttitudeFeedbacks` para refletir a postura da IA de linguagem |
| População da cidade | UI\PopulationManagementPanel*.cs | OrderMove/ChangeWorkplacePopulation, SetAssignmentPolicy* | AIA\AssignPopulationToWorkplaces (refaz todo turno) | **B** (foco e necessidades; a ordem seria desfeita) |

---

## 6. Lacunas e riscos

1. **Cheats aceitos pelo sandbox** (o único filtro é o `GodMode` do cliente). Nenhum deve ser exposto.
   - Recursos e pontos: OrderGainMoney (DOTr:1287), OrderGainInfluence, OrderForceGainFame, OrderForceGainEraStarScore, OrderInvestResearch, OrderGiveGodResource.
   - Unidades: OrderSpawnArmy / OrderSpawnSquadron, OrderAddUnitToArmy, OrderChangeUnitsXP, OrderDamageUnits, OrderChangeMovementRatio.
   - Posse e estabilidade: OrderForceCaptureCity, OrderForceSettlementOwnerChange, OrderChangeGodPublicOrder.
   - Construção grátis: OrderComplete*Construction.
   - Mapa: OrderEnableFogOfWar (**global**), OrderDiscoverWorld.
   - Ideologia e revolução: ordens de ideologia e revolução da §2.13.
   - Povos: OrderForce* de povos.
   - Fim de jogo: OrderForceEndGame.
   - Editor: todas as `EditorOrder*` (aceitas em TurnMain).
2. **Atalhos diplomáticos sem consentimento.**
   - `ForceSignAlliance` e os `ForceSign*Agreement` passam em Paz; `ForceSignEndWar` passa em Guerra (DiplomaticState_Peace.cs:105-111; DiplomaticState_War.cs:83-86).
   - Eles assinam sozinhos (DiplomaticRelationHelper.cs:546-570).
   - `DeclareForcedWar` declara guerra em Paz sem o limiar de moral.
   - Uso: só pelo executor, para aplicar algo que os dois lados aceitaram no sistema do mod.
3. **Faltam checagens de dono: o mod tem que garantir.**
   - `OrderMakeNarrativeEventChoice` não checa dono, faixa da escolha nem FailureFlags. Uma escolha "indisponível" seria aplicada com custos que não dá para pagar.
   - `OrderChangeReligionAffinity` muda a afinidade de qualquer império.
   - Ordens de batalha com campo `EmpireIndex` (Confirmation, Retreat, Surrender, ContinueSiege, SetInstantResolve): preencher sempre com o próprio império.
   - `OrderBattleInstantResolve` não checa se o império participa.
   - `OrderUpdateRelationAIFeedback`, `OrderChangeArchetypes/Biases` e `OrderSetControlledByHuman` são sempre válidas.
4. **Tickets pendurados e efeitos silenciosos.**
   - Exceção dentro do Validate: nenhuma resposta, ticket aberto para sempre. Exemplos: enum fora da faixa em `OrderDiplomaticAction`, `default: throw` em `IsAvailable`. Use timeout e nunca poste `Count`.
   - Exceção dentro do Process: o ticket volta `Valid`, mas `RefreshAll`, a visibilidade e os passes daquela ordem são pulados (Sandbox.cs:1830).
5. **Travamentos possíveis.**
   - `OrderEmpireLockedByMandatories(true)` no humano: o TurnFinish nunca escala a impaciência (SandboxState_TurnFinish.cs:103-109).
   - Segurar o "pronto" de uma nação sem timeout: o turno nunca acaba.
   - `OrderSetControlledByHuman(true)` numa IA: ninguém posta `OrderEmpireReady` (AIController.cs:1440 exige `!IsControlledByHuman`).
   - Postar ordens válidas sem parar durante o TurnFinish zera o cronômetro de impaciência (:73-78).
   - Proposta, presente ou exigência forçada pendentes bloqueiam o fim do turno (`diplomacy-native-timers.md`).
   - Esperar ticket na thread do sandbox causa deadlock.
6. **Decisões que não têm volta.**
   - `OrderChangeFaction` com Confirm não dá para cancelar (DODv:2521, 2424).
   - Sem Confirm, ela **trava a cultura para os outros** até alguém cancelar.
   - Arrasar uma cidade, libertar uma cidade e dissolver unidades também não têm volta.
7. **A IA nativa desfaz ordens da IA de linguagem.**
   - **Exércitos:** replanejados todo turno; precisam da trava no ArmyAllocator (`llm-diplomacy-feasibility.md` §7).
   - **População:** refeita todo turno.
   - **Humor mostrado ao humano:** o UpdateAIFeedback sobrescreve.
   - **Escolhas de §5:** a IA nativa escolhe primeiro.
   - **Respostas diplomáticas:** a IA nativa responde na hora.
   - **Produção e pesquisa:** aditivas e seguras com "AtBegin", porque a IA só cancela o que ela mesma pôs.
8. **Coisas que acontecem sem nenhuma ordem.**
   - Impaciência do TurnFinish: aceita presente, ignora tratado, recusa exigência e rendição.
   - Troca de cultura no TurnEnd.
   - `ArmyGoToAction` posta sozinha `OrderContinueGotoAction` / `OrderCancelAction`.
   - Aliados arrastados para a guerra (`DeclareForcedWar` interno).
   - Eventos narrativos e reclamações expiram.
   - Revolução (`TurnEndPass_ComputeRevolution`, DODv:2896).
   - Ciclo de vida dos povos, bônus de dificuldade.
   - Batalhas da IA (BattleBrain) e o `AIRequestManager`, que responde à UI qual cultura ou maravilha a IA quer (AIA\AnswerAIRequests.cs:77-84).
9. **Armadilhas de identificadores.**
   - GUID de exército muda ao dividir ou fundir.
   - Índice de povo menor é reaproveitado.
   - Nome de definição (`StaticString`) não é o título localizado.
   - O caminho do avaliador respeita a névoa do império; o da IA nativa ignora a névoa (GoToSubTree.cs:223).

---

### Arquivos críticos para a implementação
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Sandbox\Sandbox.cs` (políticas por fase :1691-1724; Validate/Process :1731-1840; laço :3828-3897)
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Sandbox\PostOrderController.cs` e `SandboxManager.cs` (postagem e tickets)
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\DepartmentOfForeignAffairs.cs` e `BaseDiplomaticState.cs` (validação diplomática e failure flags)
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\SimulationEvaluator.cs` (+ `Assembly-CSharp\Amplitude.Mercury.Presentation\BaseArmyCursor.cs`) (ordens de exército)
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.AI\AIController.cs` (segurar o "pronto" :1430-1444) e os generators de `Amplitude.Mercury.AI.Brain` citados na §5 (pontos de supressão e viés)
