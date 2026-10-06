# `ordem_exercito`: mover, atacar, defender, cercar e parar exércitos da IA de linguagem (pesquisa 2026-10-04)

Pesquisa só de leitura. Nenhum arquivo do jogo ou do mod foi alterado.
Complementa `order-catalog.md` (§2.5, §2.6, §3.2 P2, §4, §6.7) e `llm-diplomacy-feasibility.md` §7, sem repetir o que já está lá.
O executor existente (`src\CurrencyMod\Diplomacia\ActionExecutor.cs`, prefix em `PostOrderController.Update`) é o ponto de partida da receita (§8).

**Notação** (caminhos relativos a `_Modding\decompiled\`)
- `SIM\` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\`
- `SBX\` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Sandbox\`
- `IO\` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Interop\`
- `IOAI\` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Interop.AI\` (e `.Entities`)
- `FPAI\` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.AI\` (`AIController.cs`)
- `BB` = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.AI.Battle\BattleBrain.cs`
- `PR\` = `Assembly-CSharp\Amplitude.Mercury.Presentation\`; `UI\` = `Assembly-CSharp\Amplitude.Mercury.UI\`
- `AIB\` = `Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain.` + subpasta (`Allocators`, `Generators`, `Analysis.ArmyBehavior`, `Actuators`, `Tasks`…)
- `BT\` = `AIB\BehaviorTrees\`; `BTM\` = `AIB\BehaviorTrees.Major\`; `MM\` = `AIB\BehaviorTrees.Major.MilitaryMissions\`
- `AAI\` = `Amplitude.AI\Amplitude.AI\` e `Amplitude.AI\Amplitude.AI.ProcessingPasses\` (base genérica da IA)
- Departamentos: DOT = `SIM\DepartmentOfTransportation.cs`, DOD = `SIM\DepartmentOfDefense.cs`, DOB = `SIM\DepartmentOfBattles.cs`, DOFA = `SIM\DepartmentOfForeignAffairs.cs`, DOL = `SIM\DepartmentOfLabour.cs`
- `SE` = `SIM\SimulationEvaluator.cs`

---

## 0. Resumo

1. **O avaliador do clique direito serve para qualquer império e roda síncrono na thread do sandbox.**
   - Chamada: `Sandbox.SimulationEvaluator.Evaluate(RequestArmyActionAt)` (SE:396).
   - Existe uma instância só (SBX\Sandbox.cs:80). Não há uma por império.
   - Império, névoa e visibilidade saem do próprio exército (SE:455, 654, 796). O pathfinder usa o mapa conhecido daquele império (SIM\PathfindManager.cs:203-216).
   - A UI chega nele pela fila de pedidos do sandbox (SBX\Sandbox.cs:2264-2267), na mesma thread que roda o `PostOrderController.Update` (SBX\Sandbox.cs:3830). Chamar direto no nosso prefix é seguro.
2. **O pedido tem que ser zerado antes de cada uso**, como faz `ArmyActionAtAsyncOperation.Reset` (PR\ArmyActionAtAsyncOperation.cs:37-58).
   - O `Evaluate` só acumula flags com `|=`.
   - `PenultimateTileIndex` nasce 0, não -1 (IO\RequestArmyActionAt.cs:16).
3. **Mapeamento da UI para ordens** (PR\BaseArmyCursor.cs:409-456, 1324-1377):
   - Move → `OrderGoTo(guid, ref caminho)`, com o caminho multi-turno inteiro.
   - Attack/AttackCity → `OrderCreateBattle` se `StepCount == 1`; senão `OrderGoToAndCreateBattle(guid, ref caminho, TargetGUID)`.
   - JoinBattle → `OrderGoToAndJoinBattle`.
   - Mais nada é preenchido.
4. **Descoberta-chave: a `ArmyGoToAction` só anda sozinha dentro do turno.**
   - Dentro do turno ela posta `OrderContinueGotoAction` (SIM\ArmyGoToAction.cs:437-480).
   - Sem pontos de movimento, ela fica `WaitingForFinishTurn` e **só volta com `OrderFurtherActions`**. Essa ordem só é postada pela tela de fim de turno do humano (UI\EndTurnWindow.cs:716-719).
   - A IA nativa nunca usa `OrderFurtherActions`: ela reenvia um `OrderGoTo` do trecho do turno a cada turno (BT\GoToSubTree.cs:304-332).
   - Portanto **o mod tem que reavaliar e repostar a cada turno** (ou postar `OrderFurtherActions` para o império).
5. **Trava contra a IA nativa: postfix em `ArmyAllocator.ComputeInstanceList`** (AIB\Allocators\ArmyAllocator.cs:75).
   - Esse patch solta a tarefa atual e impede novas, porque o alocador falha toda tarefa cujo exército sumiu da lista (AAI\…\EntityAllocator.cs:59-64).
   - Só forçar `IsAllocationStillValid = false` não basta.
   - Mais dois filtros pequenos: o gerador `ArmySpecificMission` (reagrupar, dispersar…) e `SplitArmies`.
6. **A IA nativa não cancela a ação de um exército que ela não controla.** Restam três vazamentos (§4.6):
   - um reagrupamento que transfere unidades **para** o nosso exército, o que cancela o nosso goto (DOD:5664);
   - o reforço automático de batalha (BB:843-868);
   - um inimigo que trava o nosso exército ao planejar atacá-lo.
7. **O "pronto" da IA não espera exércitos sem tarefa.** O fim do turno só espera a nossa ação enquanto ela está em `Running`, e isso termina sozinho (§4.5).
8. **Chegada e falha:**
   - guardar a `ArmyGoToAction` no retorno do ticket e ler `Status` / `DestinationReached`;
   - ouvir os eventos `ArmyMoveTo`, `BattleStarted`, `SettlementSiegeStarted`, `ArmyDestroying`, `ArmyOwnerChanged` e `OrderProcessed` (§5).

---

## 1. O avaliador (`RequestArmyActionAt`)

### 1.1 Campos (IO\RequestArmyActionAt.cs:6-45)

| Campo (linha) | Entrada/Saída | Valor para o mod | Observação |
|---|---|---|---|
| `EntityGUID` (8) | entrada | GUID do exército | Inexistente ou isca dá `ArmyInvalid` (SE:398-407) |
| `SelectedUnits[8]`, `SelectedUnitsCount` (10-12) | entrada | `0` = exército inteiro | Valor entre 0 e o total avalia uma divisão. A UI manda o total quando nada está selecionado (PR\BaseArmyCursor.cs:249-253) |
| `TargetTileIndex` (14) | entrada | tile de destino | Tem que estar dentro do mapa: é lido em `TileInfo.Data[...]` (SE:802) |
| `PenultimateTileIndex` (16) | entrada e saída | **-1** | Nasce 0. Só conta para ataque (SE:151, 409-423, 751-767). A UI usa o lado do hexágono sob o mouse |
| `ForcedPathArmyActionAtPosition` (18) | entrada | `None` para mover/defender; `Attack \| AttackCity` para atacar/cercar | Se a ação achada não bate com a forçada: `ActionInvalid` (SE:797-800). Clique direito = `None` (PR\ArmyCursor.cs:57-62); botão de ataque = `Attack\|AttackCity` (PR\ArmyAttackCursor.cs:10) |
| `IsRangedAction` (20) | entrada | `false` | `true` pula o pathfinding (SE:655-660) |
| `PathToTarget` (22) | saída | o caminho | `AStarResults { ResultStatus, StepCount, Steps[] }`. Cada passo: `{TileIndex, Turn, TransitionCost, Transition, TileFlags}`, com `Turn` = 1 no turno atual (SIM\AStarResults.cs, SIM\AstarStep.cs) |
| `TargetGUID` (24) | saída | exército alvo, **distrito** alvo, batalha ou aeroporto | Para cidade é o GUID do distrito do centro (`DistrictMap`), não o do assentamento (SE:524-526) |
| `TargetEmpireIndex` (26) | saída (`ref`) | dono do alvo | Só alguns ramos escrevem: zerar para -1 antes |
| `DestinationEmpireIndex` (28) | saída | dono do território de destino, quando entrar exige guerra (SE:906-910, 918-922), ou oponente numa batalha de terceiros (SE:877) | — |
| `RansackTargetedEmpireIndexes`, `RansackNumberOfTargetedEmpire` (30-32) | saída | só saque | O array é criado com `NumberOfMajorEmpires`: criar o pedido **depois** que o sandbox subir |
| `PathArmyActionAtPosition` (34) | saída | ação no destino | `Move`, `Attack`, `AttackCity`, `Transfer`, `JoinBattle`… (IO\PathArmyAction.cs) |
| `AvailableAirportFailureFlags` (36), `BattleReinforcementFailureFlags` (38) | saída | — | — |
| `PathActionFailureFlags` (40) | saída | bloqueios | ArmyInvalid, ActionInvalid, InvalidPath, CannotEmbark, IsEdgeOfWorld, MeleeAttackHover, CannotJoinBattle, NeedToDeclareWarButCannot, NeedHigherWarMoral, NeedToFreeVassal… (IO\PathArmyActionFailureFlags.cs) |
| `PathActionConditionFlags` (42) | saída | os modais de confirmação da UI | NeedToDeclareWar, WillBreakSiege, WillReinforceThirdPartyBattle, WillDestroyTrade, WillLoseStealthStatus, WillLosePatronage, WillLoseWatchCity (IO\PathArmyActionConditionFlags.cs) |
| `ArmyActionFailureFlags` (44) | saída | bloqueios do exército | Ex.: TargetNotVisible, PrehistoricEra, BoatsCannotSiege, RetreatedStatus, TrespassingStatus, DiplomatProhibitedAction (SIM\ArmyActionHelper.cs:1379-1519). `NotEnoughMovement` é apagado no fim (SE:155, 1203): **um caminho com 0 pontos de movimento é aceito** |

### 1.2 O que o `Evaluate` faz (SE:396-430)

1. Busca o exército e recusa isca (SE:398-407).
2. `SetActionAndTargetGUID` (SE:451-650) decide a ação no tile:
   - **Tile não explorado pelo império:** `Move` sem alvo (SE:457-460).
   - **Batalha em andamento** da qual o exército não participa: `JoinBattle` com o GUID da batalha (SE:462-474).
   - **Distrito do bairro principal de uma cidade de outro império:** `AttackCity` com o GUID do distrito (SE:506-528). Vale quando não há direito de cruzar cidades (`CrossCityTile`), o exército não está furtivo e não é diplomata.
   - **Exército visível:** se é do mesmo império, `Transfer`; se é de outro, `Attack`, salvo se estiver invisível por furtividade (SE:549-581).
   - **Senão:** `Move`, ou a ação especial forçada: saque, acampar, fundar… (SE:582-649).
3. `SetPathToTarget` (SE:652-782) monta o contexto do exército e chama `Sandbox.PathfindManager.FindPath`, que é **síncrono** (SE:770).
   - Liga `AvoidDyingInDeepWater` e, se falhar, tenta de novo sem esse flag (SE:662-666, 771-775).
   - Ajusta o `setupData` por ação (SE:682-730). No ataque, aceita o oponente no destino e exige parada no penúltimo tile.
   - Libera a entrada no território do alvo ou do dono (SE:731-739).
   - Se o destino é o tile atual: sucesso com 0 passos (SE:777-781).
4. `FillFailureAndConditionFlags` (SE:784-1204):
   - valida o caminho com a mesma regra do `OrderGoTo` (`ValidateGoTo_ValidatePath`, SE:809-812);
   - faz checagens por ação (SE:897-1158);
   - calcula `WillBreakSiege` (SE:1159-1162);
   - se precisa de guerra, chama `FillNeedToDeclareWarFailureFlags` para alvo e destino, só impérios maiores (SE:1163-1181 → 432-449);
   - destino na água sem capacidade naval vira `CannotEmbark` (SE:1182-1202).

### 1.3 Thread, instância e império não-local

**Instância**
- `Sandbox.SimulationEvaluator` é uma só (SBX\Sandbox.cs:80). A classe é `internal` (acessível via Publicizer).

**Thread**
- A UI não chama o avaliador direto. `PresentationPathfindingController.GetArmyActionAt` (PR\PresentationPathfindingController.cs:38-43) envia um `PostRequestMessage`.
- O sandbox processa esse pedido em `ProcessRequest` (SBX\Sandbox.cs:1612-1616, 2264-2267) e responde com `PostRequestResponseMessage` (:2473-2477).
- Isso acontece na thread do sandbox, a mesma que chama `PostOrderController.Update` (SBX\Sandbox.cs:3830).
- **No prefix é só chamar `Evaluate` direto.** O `setupData` (SE:157) e o `synchronousEvaluationData` (SIM\PathfindManager.cs:24) são compartilhados, mas só essa thread os usa.
- **Nunca chamar de outra thread** (HTTP, principal ou IA).
- Alternativa assíncrona, desnecessária aqui: `SandboxManager.PostAndTrackRequest(request)` (SBX\SandboxManager.cs:333), como a IA faz em BT\GoToSubTree.cs:94-101.

**Império não-local: não há nada a fazer.** Tudo sai do exército:
- `Sandbox.Empires[army.EmpireIndex]` (SE:455) e `army.Empire.Entity` (SE:654, 796);
- visibilidade e exploração com os bits daquele império (`majorEmpire.Bits`, SE:457, 461, 906);
- `FillEvaluationData` copia o mapa conhecido do império (`majorEmpire.TileInfo`, `DistrictInfo`, `DistrictInfoIndexMap`, `TransitTileInfo`; SIM\PathfindManager.cs:203-216) e as habilidades diplomáticas dele (:119-134);
- o `ProcessRequest` não confere quem pediu.

Restrição: não serve para exército de povo menor, porque o código faz `as MajorEmpire` e ficaria com null.

**Custo**
- Cada `FindPath` copia vários mapas inteiros (`FillEvaluationData`, SIM\PathfindManager.cs:92-282).
- Limitar a cerca de 4 a 6 avaliações por volta do laço.

### 1.4 Aceitar o resultado como a UI aceita (PR\BaseArmyCursor.cs:310-340)

**A UI só envia a ordem se:**
- `PathToTarget.ResultStatus == Success`, ou `Trespassing` quando a ação é Move/Attack/AttackCity (:322);
- a ação não é `None` nem `ActionFailed` (:327);
- os quatro conjuntos de bloqueio estão vazios (:332): `PathActionFailureFlags`, `ArmyActionFailureFlags`, `AvailableAirportFailureFlags` e `BattleReinforcementFailureFlags`.

Depois, cada `PathActionConditionFlags` vira um modal de confirmação (:597-659). O mod decide no lugar do jogador (§2.4).

Detalhe: `ArmyActionAt_UponCompletion` apaga o caminho quando há `ActionInvalid` (:275-284).

---

## 2. Do resultado à ordem (BaseArmyCursor)

### 2.1 Mapeamento ação → ordem (PR\BaseArmyCursor.cs:409-456)

| Ação no destino | Condição | Ordem (linha da UI) | Campos |
|---|---|---|---|
| `Move` | exército inteiro | `new OrderGoTo(armyGuid, ref path)` (:1324-1344) | `SimulationEntityGUID`; `AStarResults` (cópia do caminho, IO\OrderGoTo.cs:21-32); `AdditionalPathfindingFlags = None` (:15); `IsAutoExplore = false` (:17) |
| `Move` | parte das unidades | `OrderSplitAndGoTo` (vários passos) / `OrderSplitArmy` (1 passo) | fora da v1 (o GUID muda) |
| `Attack` / `AttackCity` | `StepCount == 1`: o único passo é o tile do alvo, que está adjacente | `new OrderCreateBattle(armyGuid, TargetGUID){UseInstantResolve = opção do registro}` (:1362-1370) | Para a IA: `UseInstantResolve = false`, como a nativa faz (BT\Nodes.cs:1336) |
| `Attack` / `AttackCity` | `StepCount > 1` | `new OrderGoToAndCreateBattle(armyGuid, ref path, TargetGUID)` (:1371) | O último passo tem que ser o tile do alvo (DOT:1094-1099) |
| `JoinBattle` | — | `new OrderGoToAndJoinBattle(armyGuid, ref path, TargetGUID)` (:1374-1377); aqui `TargetGUID` = GUID da batalha | — |
| `Transfer` | — | `OrderTransferUnits` / `OrderGoToAndMerge` (:1346-1360) | fusão, o GUID muda (fora do escopo) |

### 2.2 O que a ordem vai encontrar na validação

**Exército** — `ValidateGoTo_ValidateArmy` (DOT:925-950)
- O exército é do império.
- **Não está travado** (`IsLocked`).
- Não é isca.

**Caminho** — `ValidateGoTo_ValidatePath` (DOT:855-923)
- O primeiro passo é adjacente à posição **no momento da validação**.
- Nenhum passo volta ao tile de partida.
- Regras de trem.

**Destino** — `ValidateOrderGoTo_ValidateDestination` (DOT:962-1013). Estas regras só valem se o destino está visível:
- sem exército próprio no tile;
- sem exército inimigo visível no tile;
- não é centro de cidade de outro império sem `HasCrossCityAbilityWithMe`;
- sem batalha ativa no tile.

**Ataque**
- `ValidateBattleCreation` + `HasAttackDiplomaticAbility(alvo)` (DOT:1046-1101).
- No `OrderCreateBattle`: adjacência e `CheckBattleCombinations` (DOB:1375-1495, 1497-1586).

**Multi-turno**
- Não existe checagem de "chega neste turno": o método retorna `true` (DOT:1015-1018). O caminho multi-turno é aceito.

### 2.3 Caminho multi-turno e continuação (`ArmyGoToAction`)

**Ao processar**
- `ProcessOrderGoTo` cria `ArmyGoToAction(army, ref path, 0, flags, !IsAutoExplore)` e a registra (DOT:1038-1044).
- O construtor grava **o caminho inteiro** em `World.ArmyPathNodes`, com `TurnToReach = step.Turn` (SIM\World.cs:1890-1935).
- `RegisterAction` cancela qualquer outro goto do mesmo exército (SIM\ActionController.cs:55-66, 383-403; SIM\ArmyGoToAction.cs:390-401). **Repostar = substituir.**

**Dentro do turno**
- Enquanto a ação está `Running`, `ActionController.ServerUpdate` roda a cada volta do `SandboxState_TurnMain.Run` (SBX\SandboxState_TurnMain.cs:34).
- A ação posta `OrderContinueGotoAction` a cada `1000 ms × tiles do último passo`; com a opção de jogo de movimento instantâneo, sem espera (SIM\ArmyGoToAction.cs:43, 437-480).
- Cada ordem executa o trecho até o próximo tile onde dá para parar (:682-879).

**Fim do movimento do turno**
- Sem pontos: `WaitingForFinishTurn` (:714-728).
- Com movimento cheio e sem conseguir dar nenhum passo: `Failed` (:716-720).

**Turnos seguintes**
- No início do turno a ação só recalcula o caminho (`OnTurnBegin`, :482-502). **Ela não volta a andar.**
- Só volta com `FurtherIfPossible` (:245-274). Quem chama é `ActionController.FurtherForEmpire` (SIM\ActionController.cs:81-92), ao processar `OrderFurtherActions` (DOT:841-853).
- Essa ordem só é postada por `EndTurnWindow.InitiateArmyMovingRoutine` (UI\EndTurnWindow.cs:716-719). Ela é disparada pela pendência de fim de turno "exércitos que podem continuar" (UI\MandatoryFurtherableArmies.cs:16-20): é o "mover exércitos e passar o turno" do humano.
- A versão sem argumento, `ActionController.FurtherIfPossible()` (:122), não tem quem chame.

**Consequência para a IA de linguagem.** Em cada `TurnMain`, uma de duas:
- **(a) recomendado:** reavaliar e repostar a ordem. Recalcula com a névoa nova e contorna bloqueios.
- **(b)** postar `new OrderFurtherActions()` para o império.
  - Só vale se houver ação esperando (DOT:841-848).
  - Continua **todas** as ações em espera do império.
  - Falha se o próximo nó ficou a 2+ turnos com movimento cheio (SIM\ArmyGoToAction.cs:262-268).

**Exceção: `ArmyGoToAndCreateBattle` (SIM\ArmyGoToAndCreateBattle.cs)**
- Se o próximo nó é o tile do alvo, volta a `Running` sozinha no início do turno (:137-145).
- Ao chegar ao lado do alvo, posta `OrderCreateBattle` com o próprio `ActionGUID` (:158-186).
- **Se o alvo (exército) se mover, a ação falha** (:197-204).
- Se o atacante alcança o penúltimo tile neste turno, **o alvo fica travado** (`army.Lock`, :54-66, 80-88).

**Persistência**
- A ação vai para o save (`ActionController.Serialize`, :571-575).
- Ela sobrevive à limpeza do fim de turno enquanto está `Running` ou `WaitingForFinishTurn` (`CleanUpActions`, :405-444).

### 2.4 Guerra e outras condições

**Como a UI declara guerra**
- Com `NeedToDeclareWar`, a UI posta `OrderDeclareAnyWar{OtherEmpireIndex = líder do grupo diplomático}` para o alvo e para o dono do destino.
- Logo em seguida posta a ordem do exército, sem esperar o resultado. As ordens são processadas em sequência (PR\BaseArmyCursor.cs:720-740).

**Cuidado com guerra-surpresa**
- `OrderDeclareAnyWar` escolhe **guerra-surpresa sempre que ela é possível** (DOFA:1271-1274).
- As compostas `OrderDeclareWarAndGoTo` e `OrderDeclareWarGoToAndCreateBattle` também usam `DeclareSurpriseWar` (IO\OrderDeclareWarAndGoTo.cs:21; IO\OrderDeclareWarGoToAndCreateBattle.cs:21; DOT:602-650).
- Para guerra formal: postar antes `OrderDiplomaticAction{DeclareFormalWar}` (a ferramenta `declarar_guerra`) e reavaliar na volta seguinte.

**Outras falhas de guerra**
- `NeedToFreeVassal`: declarar ao suserano (`DiplomaticState_VassalToLiege.LiegeEmpireIndex`, SIM\DiplomaticState_VassalToLiege.cs:12).
- `NeedHigherWarMoral` e `NeedToDeclareWarButCannot`: recusar a ordem.

**Povos independentes**
- `HasAttackDiplomaticAbility` sempre dá true (DOFA:615-618). O preço é a condição `WillLosePatronage`.

**Política sugerida para os outros modais**
- Recusar sem autorização explícita: `WillBreakSiege`, `WillReinforceThirdPartyBattle` (costuma trazer `NeedToDeclareWar` junto) e `WillLosePatronage`.
- Aceitar: `WillLoseStealthStatus` e `WillLoseWatchCity`.

---

## 3. Escolher o tile de destino

A UI não tem "ir para o território": usa sempre o tile sob o mouse (PR\BaseArmyCursor.cs:256).

**Cidade (C#)**
- O centro é `Settlement.WorldPosition` = distrito principal (SIM\Settlement.cs:32; `GetMainDistrict`, :579-582).
- Para atacar ou cercar, o destino é esse tile. O avaliador devolve `AttackCity` com o GUID do distrito.
- Qualquer distrito com `Borough.IsMain` também é alvo válido (DOB:1443-1491). A IA nativa escolhe um deles (`StartCitySiege`, BT\Nodes.cs:1487-1532).

**Exército inimigo (A#)**
- O destino é `inimigo.WorldPosition.ToTileIndex()`.
- O alvo **tem que estar visível** para o império. Senão o avaliador devolve `Move` (SE:461, 477) ou `TargetNotVisible` (SIM\ArmyActionHelper.cs:1414-1423).

**Território (T#)** — dados em `Sandbox.World.TerritoryInfo.Data[T]` (IO\TerritoryInfo.cs): `EmpireIndex` (14), `SettlementGUID` (16), `Claimed` (22), `AdministrativeDistrictTileIndex` (38), `TileIndexes` (57), `VisualCenter` (59).
- **Âncora:**
  - cidade própria: centro (para defender) ou tile administrativo;
  - posto próprio: `AdministrativeDistrictTileIndex`;
  - o resto: `VisualCenter`.
- É o mesmo critério que a IA nativa e a UI usam para "o lugar" de um território:
  - IA: `GoToClaimTerritory.Start` cai no `VisualCenter` (AIB\Actuators\GoToClaimTerritory.cs) e os sinais no mapa (pings) também (AIB\Generators\PlaceDefensePing.cs:35);
  - UI: tile administrativo se o território foi reivindicado, senão `VisualCenter` (PR\PresentationStealthController.cs:107).
- O jogo não procura sozinho um tile livre: `FindDestinationFromMoveTarget` usa o tile como está (BT\Nodes.cs:958-969). **O mod faz a busca:**
  1. Candidatos: a âncora, depois os `TileIndexes` por distância hexagonal (`WorldPosition.GetTileIndexDistance`).
  2. Filtro barato antes de avaliar:
     - explorado pelo império (`Sandbox.VisibilityController.IsWorldPositionExplored(t, empire.Bits)`);
     - sem exército (`World.Maps.ArmyMap[t]` inválido, ou o próprio exército);
     - sem batalha (`Sandbox.BattleRepository.TryGetBattleAt(t, out _)`);
     - terra, se o exército não tem `NavalSpeed` (`World.IsPositionInWater`, SIM\World.cs:2254);
     - fora dos pontos de nascimento de unidades da cidade própria (`Settlement.UnitSpawnPositions`, SIM\Settlement.cs:36);
     - não é centro de cidade estrangeira.
  3. Avaliar até cerca de 6 candidatos. Aceitar o primeiro com ação `Move` e caminho válido; preferir menos turnos (`Steps[StepCount-1].Turn`).

**"defender" uma cidade própria**
- Copiar `Garrison.FindTileToDefendAgainstIntruder` (MM\Garrison.cs:207-264):
  - distrito do bairro principal vale 0,6; distrito fortificado vale 0,2;
  - pontos de nascimento de unidade são penalizados;
  - os mais perto têm preferência.
- Campos: `Settlement.Districts` (SIM\Settlement.cs:70), `District.Borough.IsMain`, `District.IsFortified` (SIM\District.cs:27, 43).

---

## 4. A IA nativa: quem mexe no exército e como travar

### 4.1 Por onde a IA nativa age num exército

| Caminho | Onde | O que posta |
|---|---|---|
| Missões militares, escolta, guarnição, reivindicar território, exploração (todas `ArmyMission`) | Tarefas com alocação `Resource.Army` → `ArmyAllocator` → atuador com árvore (AIB\Actuators\ArmyMissionActuator.cs). Inclui as árvores Garrison/Hold/Advance/Raid/Siege (MM\) e EscortEmbarkedArmy | GoTo, CreateBattle etc. via `GoToSubTree`: só o trecho do turno, com `IgnoreFogOfWar` (BT\GoToSubTree.cs:211-332) |
| Missões específicas por exército: reagrupar, dispersar, sair do ponto de nascimento, neolítico, coletar comida, mísseis, missão de gameplay | `ComputeSpecificMissions.Process` (AIB\Analysis.ArmyBehavior\ComputeSpecificMissions.cs:45-89) → gerador `ArmySpecificMission` (AIB\Generators\ArmySpecificMission.cs:26-37) → `ArmySpecificMissionActuator` (AIB\Actuators\ArmySpecificMissionActuator.cs:52-114) | **Não passa pelo alocador.** O alocador até exclui esses exércitos (AIB\Allocators\ArmyAllocator.cs:81) |
| Dividir exército | `SplitArmies` (AIB\Generators.Military\SplitArmies.cs:18-25; base SplitUnitCollections.cs:31-41) | `OrderSplitArmy` (BT\Nodes.cs:1773) |
| Reagrupar **em** outro exército | árvore RegroupArmy → `TransferUnitsToArmy` (BT\Nodes.cs:1662-1741) | `OrderTransferUnits(origem, alvo)`. **Cancela o goto do alvo** (DOD:5664) |
| Reforço de batalha | BattleBrain (BB:843-868) | `OrderGoToAndJoinBattle` para exércitos na fila de reforço |
| Decisões de cerco do sitiante | `ArmyBehaviorTree.BuildLockedSubTree` (BT\ArmyBehaviorTree.cs:29-40): no fácil, espera 3 turnos; depois assalta ou mantém | `OrderBattleLaunchCityAssault` / `OrderBattleContinueSiege` (BT\Nodes.cs:1534-1618). **Só acontece se o exército tem tarefa** |

### 4.2 Por que o patch vai no `ComputeInstanceList`

1. `AllocatorPass.Process` (AAI\…\AllocatorPass.cs:41-139) separa tarefas alocáveis de tarefas em andamento. Para as em andamento, `IsAllocationStillValid == false` só volta a alocação a `Pending` (:109-112).
2. `EntityAllocator.AllocateTasks` (AAI\…\EntityAllocator.cs:34-68) pede ao `ComputeInstanceList` os exércitos disponíveis. Para cada tarefa em andamento, procura nessa lista o exército alocado. **Se não acha, a tarefa vira `Failed`** (:59-64) e o log mostra "Can't find entity … Fails the task". O resto é distribuído (ArmyAllocator.cs:88-123).
3. Em `Brain.Run` (AAI\Brain.cs:546-978), a tarefa `Failed` sai no `CleanDesires` com `actuator.Shutdown()` (:1082-1084, 1100-1130). Isso acontece **antes** de as árvores rodarem no ciclo (:878-966).

Resultado: tirar o exército da lista solta a tarefa atual e bloqueia novas.

Só `IsAllocationStillValid = false` cancela a tarefa (:836-855), mas o exército continua na lista e é realocado no ciclo seguinte. Esse patch é desnecessário.

### 4.3 Patches (mínimo: P1 + P2 + P3, todos na thread da IA)

A trava é um `HashSet<ulong>` imutável, trocado de forma atômica pela thread do sandbox. Nas entidades da IA, o GUID é `ulong` (IOAI\AIEntity.cs:12).

```csharp
internal static class ArmyLocks { internal static volatile HashSet<ulong> Snapshot = new HashSet<ulong>(); }

// P1 — tira os travados do pool e falha as tarefas em andamento deles (ArmyAllocator.cs:75-86)
[HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Allocators.ArmyAllocator), "ComputeInstanceList")]
static class P1 {
    static void Postfix(List<Amplitude.Mercury.Interop.AI.Entities.Army> instanceList) {
        var t = ArmyLocks.Snapshot; if (t.Count == 0) return;
        instanceList.RemoveAll(a => t.Contains(a.SimulationEntityGUID));
    }
}

// P2 — missões específicas: não gerar para travados nem para quem iria reagrupar DENTRO de um travado
// (refaz ArmySpecificMission.GetContexts, AIB\Generators\ArmySpecificMission.cs:26-37).
// Sem Continue, a tarefa antiga fica sem avaliação e é removida no CleanDesires(DesireEvaluations) (AAI\Brain.cs:568, 1078-1081).
[HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.ArmySpecificMission), "GetContexts")]
static class P2 {
    static bool Prefix(Amplitude.Mercury.AI.Brain.Generators.ArmySpecificMission __instance) {
        var t = ArmyLocks.Snapshot; if (t.Count == 0) return true;            // roda o original
        var brain = __instance.Brain;                                          // publicizado
        foreach (var army in brain.ControlledEmpire.Armies) {
            brain.GetAnalysisData<ArmyData, Army>(army, out var d);
            var tipo = d.SpecificMissionInfo.Type.Value;
            if (tipo == SpecificMissionType.None || t.Contains(army.SimulationEntityGUID)) continue;
            if (tipo == SpecificMissionType.Regroup && t.Contains(d.SpecificMissionInfo.Guid)) continue; // Guid = exército-alvo do reagrupamento (ComputeSpecificMissions.cs:398)
            __instance.Continue(army);                                         // protected → publicizado
        }
        return false;
    }
}
// Alternativa ao P2: postfix em ComputeSpecificMissions.Process zerando SpecificMissionInfo dos travados e dos reagrupadores
// (o P1 os tira do pool depois).

// P3 — não dividir um exército travado (SplitArmies.cs:18; chamado em SplitUnitCollections.cs:37)
[HarmonyPatch(typeof(Amplitude.Mercury.AI.Brain.Generators.Military.SplitArmies), "IsArmyValid")]
static class P3 {
    static void Postfix(Amplitude.Mercury.Interop.AI.Entities.Army army, ref bool __result) {
        if (__result && ArmyLocks.Snapshot.Contains(army.SimulationEntityGUID)) __result = false;
    }
}
```

**P5 (opcional, rede de segurança, roda na thread do sandbox)**
- Prefix em `Empire.ValidateOrder(Order)` (SIM\Empire.cs:1368-1379), por onde passa toda ordem de império (SBX\Sandbox.cs:1756).
- Recusa (`__result = false`) qualquer ordem cujo exército esteja travado, exceto:
  - (a) ordens do mod, reconhecidas pelo `((IBillet)order).TicketNumber` guardado na postagem (a resposta é casada por esse número, SBX\PostOrderController.cs:179-193);
  - (b) ordens de continuação da nossa própria ação: `order is IOrderWithControlledAction c && c.ActionGUID.IsValid`. Exemplo: o `OrderCreateBattle` que a `ArmyGoToAndCreateBattle` posta (SIM\ArmyGoToAndCreateBattle.cs:174-179).
- Campos que carregam o GUID do exército (ver também o catálogo §2.5):
  - `OrderGoTo.SimulationEntityGUID`, que vale para todas as subclasses `GoToAnd*`;
  - `OrderCreateBattle.AttackerArmyGUID`;
  - `OrderSplitArmy.ArmySimulationEntityGUID`;
  - `OrderTransferUnits.SourceArmyGUID` e `.TargetArmyGUID`;
  - `OrderCancelArmyMovement.ArmyGUID`;
  - `OrderChangeEntityAwakeState.EntityGuid`;
  - `OrderToggleAutoExplore.EntityGUID`;
  - `OrderDisbandUnits.UnitCollectionSimulationEntityGUID`;
  - `OrderJoinBattle.ArmyGUID`;
  - `OrderUseAirport.ArmyGUID`.
- Para a árvore nativa, ordem inválida é uma falha comum: ela trata e não trava.

### 4.4 Corrida com o ciclo da IA

- A IA decide em outra thread.
  - O ciclo começa no `AIController.UpdateAIControllerState`, na thread do sandbox: `aiDecisionInProgress = true` (FPAI\AIController.cs:1492).
  - Ele termina na thread da IA: `= false` (:1771). O campo é `volatile` (:360).
- Um ciclo **já em andamento** pode ter passado do alocador e ainda postar uma ordem para o exército, cancelando a nossa.
- **Regra:**
  1. Publicar a trava.
  2. Postar a ordem só quando `Sandbox.AIController.aiDecisionInProgress == false`.
  - Nosso prefix roda antes do `AIController.LateUpdate` na mesma volta (SBX\Sandbox.cs:3830 e 3895). Então todo ciclo iniciado depois já vê a trava no alocador.
- Defesa extra: o P5, ou ouvir `SimulationEvent_OrderProcessed`. Se aparecer uma ordem do império sobre um exército travado sem ticket nosso, repostar.

### 4.5 Fim do turno ("pronto")

**O "pronto" da IA**
- A IA marca o império pronto ao fim de cada ciclo sem necessidade obrigatória (FPAI\AIController.cs:1504-1512; `PlayerMustRun`, :1594-1609).
- Há um teto de 25 decisões, depois do qual o pronto é forçado (:1394-1413).
- Exército sem tarefa não gera necessidade. **Não há nada a corrigir.**

**O que segura o fim do turno**
- O sandbox só conclui o `TurnFinish` quando nenhuma ação está `Running` (SIM\ActionController.cs:135-166; SBX\SandboxState_TurnFinish.cs:46-54).
- `OrderContinueGotoAction` é aceita no `TurnFinish` (SBX\Sandbox.cs:1703), então o movimento em curso termina.
- `WaitingForFinishTurn` não segura o turno.
- Um cerco parado no estado `Siege` também não (SIM\BattleRepository.cs:395).

**Quando postar**
- `OrderGoTo` é recusada no `TurnFinish`: só uma lista branca passa (SBX\Sandbox.cs:1703). Postar no `TurnMain`, ou segurar a vez da nação (catálogo §4.5).

### 4.6 A IA nativa cancela a ação de um exército que ela não controla?

**Não diretamente.**
- Uma ação de goto só é cancelada por outra ação no **mesmo** exército (`ShouldCancelAction` / `IsCanceledBy`, SIM\ArmyGoToAction.cs:276-287, 390-401).
- Ou por funções que zeram as ações (`ResetArmyOnGoingActions` / `ResetArmyOnGoingGoToActions`, DOD:7781-7791).
- A IA nativa não posta `OrderCancelArmyMovement` nem `OrderCancelAction`.

Com P1 a P3, só sobram estes casos:

| Vazamento | Prova | Defesa |
|---|---|---|
| Outro exército do império transfere unidades **para** o nosso (reagrupamento): cancela o nosso goto | DOD:5650-5664 (`ResetArmyOnGoingGoToActions(alvo)`); BT\Nodes.cs:1706 | Filtro de reagrupamento no P2; opcional, P5 sobre `OrderTransferUnits.TargetArmyGUID` |
| BattleBrain manda o nosso exército reforçar uma batalha vizinha | BB:843-868 | Aceitar (é natural) ou filtrar `OrderGoToAndJoinBattle` no P5 |
| Inimigo planeja atacar: trava o nosso exército (`Lock`) e o goto falha | SIM\ArmyGoToAndCreateBattle.cs:54-66; SIM\ArmyGoToAction.cs:416-420, 521-526, 684-689 | Detectar `IsLocked` e reportar |
| Batalha ou cerco travam o exército | SIM\Army.cs:216-226 | idem |
| Cidade muda de dono: o exército é expulso e as ações zeram | DOD:6896 | Detectar e repostar |

Os três primeiros acontecem no próprio processamento do jogo, não por decisão da IA estratégica sobre o nosso exército.

### 4.7 Alternativa nativa (experimental, não recomendada para a v1): "missão de gameplay"

**Mecanismo**
- `Army.ArmyMissionIndex >= 0` vira `SpecificMissionType.GameplayMission` (ComputeSpecificMissions.cs:58-61).
- O `ArmyAllocator` exclui esse exército sozinho (ArmyAllocator.cs:81).
- `ArmySpecificMissionActuator` escolhe a árvore pelo tipo da missão (:116-170):
  - `KeepOnPosition` → `GoToPosition`: movimento multi-turno nativo, com `IgnoreFogOfWar`;
  - `Garrison(T)`, `Hold(T)`, `Advance(T)`, `Raid(T)`, `Siege(T)`.
- A missão é criada por `Sandbox.ArmyMissionController.StartArmyMission` (SIM\ArmyMissionController.cs:23-97), ou pela ordem de editor `EditorOrderSetArmyMission`:
  - campos `ArmyTileIndex`, `ArmyMissionType`, `TargetTileIndex` / `TargetIndex` / `TargetGuid`;
  - processada em SIM\EditorOrderProcessors.cs:1671-1735; para impérios maiores, usa `AttackOnSight` e os bits de `GetAttackAuthorizationBits`;
  - postada por `SandboxManager.PostAndTrackOrder(EditorOrder)`, com império -1 (SBX\SandboxManager.cs:311-318). O `TurnMain` aceita: a regra de império morto ignora índice < 0.

**Problemas**
- A `Siege` exige `IsCaptain` (MM\Siege.cs; BT\Nodes.cs:2828-2831). O capitão só é definido nas missões militares normais (`ArmyMissionActuator.PrepareForExecution`, AIB\Actuators\ArmyMissionActuator.cs:55-88), então **o cerco nunca começaria**.
- A missão fica gravada no save (`Army.ArmyMissionIndex`, SIM\Army.cs:587; SBX\Sandbox.cs:1062) até ser parada com `ArmyMissionTypes.Undefined`.
- Não segue o caminho "humano" do avaliador.
- O P2 bloquearia essa missão nos exércitos travados: os dois mecanismos se excluem.

**Uso possível no futuro:** `Garrison(T)` como "defender com cérebro nativo".

---

## 5. Detectar chegada e falha

**Ação**
- No retorno `Valid` do ticket, ainda na thread do sandbox, pegar a ação: `Sandbox.ActionController.GetActionFor<ArmyGoToAction, Army>(army)` (SIM\ActionController.cs:260-276). Só devolve ações `Running` ou em espera.
- Guardar a referência. O objeto continua lendo bem mesmo depois do `Release` do fim de turno: `Status` não muda (SIM\Action.cs). Só não tocar em `Entity` depois disso.
- Significado de cada `Status` (IO\ActionStatus.cs):
  - `Finished`: chegou; `DestinationReached = true` (SIM\ArmyGoToAction.cs:333-347). No `GoToAndCreateBattle`, significa que a batalha foi criada.
  - `Failed`: travado (:416-420) ou caminho bloqueado ou impossível (:553-623, 707-850).
  - `Canceled`: substituída por outra ordem no mesmo exército (nossa ou do jogo), por dormir (DOL:184-205), por `OrderCancelArmyMovement` (DOT:582-586) ou por transferência.
  - `Terminated`: o exército foi removido (DOD:840 → SIM\ActionController.cs:308-335).
  - `WaitingForFinishTurn`: acabou o movimento do turno.

**Barato, sem guardar nada**
- `army.HasGoToAction()` (SIM\Army.cs:305-308): só ações `Running` ou em espera.
- `army.WorldPosition.ToTileIndex() == destino`.
- `army.GoToActionIndex` aponta para `Sandbox.ArmyGoToActionController.ArmyGoToActionAllocator[idx]`, com `ActionGUID`, `ActionType`, `ActionStatus` e `ActionTarget` (SIM\ArmyGoToActionController.cs; IO\ArmyGoToActionInfo.cs).
  - Na chegada o índice volta a -1 (SIM\ArmyGoToAction.cs:344).
  - Numa falha ele fica, para a UI mostrar o caminho (:321-331).

**Eventos** (thread do sandbox)
- Assinar com `SimulationEvent<T>.Raised += handler` **depois** que o sandbox existe; antes disso dá erro (SIM\SimulationEvent.cs:76-85).
- São limpos sozinhos ao desligar o sandbox (:214-229). Reassinar a cada partida.

| Evento | Campos | Uso |
|---|---|---|
| `SimulationEvent_ArmyMoveTo` | `Army`, `PreviousPosition` | Progresso a cada passo; chegada |
| `SimulationEvent_BattleStarted` | `Battle`, `AttackerEmpireIndex`, `DefenderEmpireIndex` | "em combate": `battle.AttackerGroup.ContainParticipant(guid)` ou `DefenderGroup`, como em SE:464-465 |
| `SimulationEvent_BattleTerminated` | `Battle`, `AttackerResult`, `DefenderResult`, `VictoryType` | Resultado do ataque |
| `SimulationEvent_SettlementSiegeStarted` | `Settlement`, `BesiegingEmpire` | Cerco iniciado |
| `SimulationEvent_ArmyDestroying` | `ArmyGUID`, `EmpireIndex` | Exército fundido, dispersado ou destruído (DOD:894-897) |
| `SimulationEvent_ArmyOwnerChanged` | `Army`, `OldEmpireIndex`, `NewEmpireIndex` | Dado de presente ou capturado |
| `SimulationEvent_ArmyCreated` (`CreationSource == Split`) | `Army` | Exército dividido. O GUID original fica com o resto (DOD:5204-5228) |
| `SimulationEvent_OrderProcessed` | `EmpireIndex`, `Order` | Vê toda ordem processada (SBX\Sandbox.cs:1819), inclusive cada passo `OrderContinueGotoAction`. Detecta ordem alheia sobre exército travado |

**Lado da IA (para o dossiê)**
- O snapshot do exército já traz `GoToAction.Type`, `Status`, `DestinationTileIndex` e `DestinationTurn` (IOAI\Entities\Army.cs:112-153).

---

## 6. Ataque, cerco e defesa

### 6.1 Atacar um exército (A#)

**Requisitos**
- Alvo **visível** e não furtivo (SIM\ArmyActionHelper.cs:1395-1425).
- `HasAttackDiplomaticAbility(alvo)`: habilidade `Attack`, ou `Skirmish` fora de território de cidade de império maior; povo menor sempre pode (DOFA:599-645).
- Atacante sem status de recuo, sem invasão e sem diplomata (SIM\ArmyActionHelper.cs:1379-1393).
- Corpo a corpo contra unidade que paira exige ao menos uma unidade à distância (`MeleeAttackHover`, SE:923-938).
- O alvo está em estado atacável e não travado por batalha (DOB:1406-1440).
- Alvo dentro do centro de uma cidade de terceiros exige direito de cruzar cidades (DOB:1427-1438).

**Execução**
- Adjacente: `OrderCreateBattle`.
- Longe: `OrderGoToAndCreateBattle`. Ela **falha se o alvo se mover** (SIM\ArmyGoToAndCreateBattle.cs:197-204).
- Perseguir = reavaliar no tile atual do alvo a cada turno, até `turnos_max`.

**Sucesso**
- `BattleStarted` com o nosso exército como atacante.
- A batalha tática fica com o BattleBrain nativo.
- A IA nativa ainda decide confirmar ou recuar (`ConfirmOrRetreatFromBattle`, catálogo §2.6).

### 6.2 Cercar ou atacar uma cidade (C#)

**Execução**
- Destino: centro da cidade, com `ForcedPathArmyActionAtPosition = Attack|AttackCity`. Resultado esperado: `AttackCity`, `TargetGUID` = distrito.
- Com `StepCount == 1`: `OrderCreateBattle`. Com mais passos: `OrderGoToAndCreateBattle`. Esta posta o `OrderCreateBattle` sozinha ao chegar ao lado (SIM\ArmyGoToAndCreateBattle.cs:158-186).

**Requisitos de criação do cerco** (DOB:1443-1491; SIM\ArmyActionHelper.cs:1482-1519)
- O império não está na era 0 (`PrehistoricEra`).
- A ação `ActionType.Siege` está liberada.
- O exército não é só de barcos.
- O alvo é cidade (`SettlementStatus == City`).
- **Não existe cerco** (`Settlement.Siege.Entity == null`).
- Não há batalha no distrito nem no centro.
- O atacante não está em cima de um distrito do bairro principal.
- O tile não é água.
- Atacar o distrito de uma cidade exige a habilidade `Attack` (guerra); `Skirmish` só vale contra assentamento que não é cidade (DOFA:671-698).

**Se o cerco já existe**
- O avaliador devolve `JoinBattle` (batalha no tile), então avaliar com `None`.
- Se o nosso império já é sitiante, dar a missão por cumprida.

**Depois do início**
- Evento `SettlementSiegeStarted`.
- A cada turno, o líder sitiante escolhe:
  - `OrderBattleContinueSiege{BattleGUID, EmpireIndex}`, ou
  - `OrderBattleLaunchCityAssault{BattleGUID, AssaultRole = Attacker}`.
  - Referências: BT\Nodes.cs:1534-1618; SIM\Battle.cs:7290-7328. `BattleGUID` = `settlement.Siege.Entity.Battle.GUID`.
- Sem escolha, o cerco fica "esperando a primeira ação do sitiante". Isso **não segura** o fim do turno (SIM\BattleRepository.cs:395), e ao sair do estado `Siege` todos ficam marcados para continuar (SIM\Battle.cs:8506-8522). Comportamento provável: o cerco continua passivo. **Testar.**
- Duas saídas:
  - (a) manter o exército travado e deixar a IA de linguagem decidir pela ferramenta `cerco`;
  - (b) destravar depois do início. A árvore nativa decide continuar ou assaltar pelo `BuildLockedSubTree` (BT\ArmyBehaviorTree.cs:29-40), desde que o exército ganhe uma tarefa.

### 6.3 Defender

- Ir ao tile defensivo (§3) com `OrderGoTo` e **ficar travado** até `turnos_max`, para a IA nativa não puxá-lo.
- Exércitos dentro da área de uma batalha entram como reforço pelo BattleBrain (BB:843-868). Para defesa, isso é bom.
- **Cidade própria já sitiada:** o avaliador no centro devolve `JoinBattle` e a ordem vira `OrderGoToAndJoinBattle`. `BattleReinforcementFailureFlags` pode bloquear.
- **Não existe ordem de "fortificar" exército.** `IsFortified` é propriedade de distrito.
- **"Dormir"** é `OrderChangeEntityAwakeState{EntityGuid, AwakeState = Sleep}` (DOL:159-205):
  - só vale se o estado muda e o exército é do império; `SleepUntilHealed` exige regeneração > 0;
  - o processamento **cancela as ações** do exército (DOL:193): só depois de chegar;
  - é **cosmético** para a IA: a palavra `AwakeState` não aparece em `Amplitude.Mercury.AI.Brain`;
  - qualquer nova ação acorda o exército (SIM\ArmyAction.cs:22);
  - `SkipOneTurn` e `SleepUntilHealed` acabam no início do turno (SIM\SimulationEntityRepository.cs:127-150).

### 6.4 Parar

- `OrderCancelArmyMovement(armyGuid)` (DOT:565-586).
  - Só vale se há caminho com próximo nó (`PathNodeCurrentIndex`).
  - O processamento chama `ResetArmyOnGoingActions`, que cancela **todas** as ações do exército, inclusive saque e corte de floresta.
- Para "ficar parado", manter a trava pelos turnos pedidos.
- Para devolver o exército à IA nativa, tirar a trava.

---

## 7. Armadilhas

| Armadilha | Onde | O que fazer |
|---|---|---|
| Pedido reutilizado sem zerar: flags acumulam, alvo velho | `\|=` em SE:800-1181; `PenultimateTileIndex = 0` (IO\RequestArmyActionAt.cs:16) | Zerar tudo como `Reset` (PR\ArmyActionAtAsyncOperation.cs:37-58) |
| Exército andou entre avaliar e validar: `InvalidPath` | DOT:855-867 | Avaliar e postar na mesma volta. Não emitir com ação `Running`. Se voltar Invalid, reavaliar (até 3 vezes) |
| Exército travado (batalha, cerco, alvo de ataque) | SIM\Army.cs:216-226; DOT:941-944; SIM\ArmyGoToAction.cs:416-420 | Não emitir; reportar; tentar no próximo turno |
| Zona de controle inimiga | Pathfinder; a estimativa ignora a ZOC depois do 2º turno (SIM\ArmyGoToAction.cs:542-546) | O caminho multi-turno pode parar antes: repostar a cada turno |
| Embarque e mar | SE:662-666, 686-688, 1182-1202 | `CannotEmbark` se o destino é água sem navegação. O ataque não pode terminar embarcando ou desembarcando. Exército embarcado é fraco |
| Pontos de movimento | SE:155/1203; SIM\ArmyGoToAction.cs:714-728 | Ordem com 0 pontos é aceita e fica esperando o próximo turno. Com movimento cheio sem avançar, falha |
| Continuação entre turnos | §2.3 | Repostar a cada turno ou `OrderFurtherActions` |
| GUID muda | Divisão: DOD:5204-5228. Transferência: a origem some (DOD:5722-5737). Troca de dono: DOD:1240 | Ouvir `ArmyDestroying`, `ArmyCreated` e `ArmyOwnerChanged`; encerrar a missão e avisar a IA de linguagem |
| Transferência para o nosso exército cancela o goto | DOD:5664 | Filtro de reagrupamento (P2) |
| Ordens que zeram as ações do exército | Dormir: DOL:193. Cancelar: DOT:585. Saquear, cortar floresta, roubar território: DOD:3714-5951. Expulso da cidade: DOD:6896 | Dormir só depois de chegar; reavaliar quando a ação vier `Canceled` |
| Névoa | O caminho usa o mapa conhecido (SIM\PathfindManager.cs:203-216). Tile desconhecido custa 1,5 (:32). A execução ignora a névoa e pode cair em emboscada (SIM\ArmyGoToAction.cs:693, 729) | Caminho pela névoa pode falhar no meio: repostar. Alvo de ataque tem que estar visível. A IA nativa calcula sem névoa (BT\GoToSubTree.cs:223); o mod, igual ao humano |
| Destino proibido | DOT:962-1013 | Não terminar em tile com exército próprio, centro de cidade estrangeira ou batalha |
| Invasão (fronteira fechada, zona desmilitarizada) | SE:735-745, 906-910. A IA nativa evita zona desmilitarizada (GoToSubTree.cs:228-231) e tira exércitos invasores (BT\ArmyBehaviorTree.cs:44, 59-65) | Exército travado ninguém tira: tratar `NeedToDeclareWar`, `Trespassing` e `TrespassingStatus` |
| Fase do turno | SBX\Sandbox.cs:1700-1714 | Postar só no `TurnMain`. No `TurnBegin` a ordem fica na fila; no `TurnFinish` é recusada |
| Corrida com o ciclo da IA | FPAI\AIController.cs:360, 1492, 1771 | §4.4 |
| Custo das avaliações | SIM\PathfindManager.cs:92-282 | No máximo 4 a 6 avaliações por volta |
| Salvar e carregar | A ação vai no save (SIM\ActionController.cs:571-575); a trava do mod, não | Ao carregar, as missões somem e a IA nativa reassume. Ou salvar as missões no estado do mod |
| Exceção dentro do `Evaluate` | `throw` em `switch` (SE:710-711, 1156-1157) | `try/catch` em volta. Uma exceção no prefix pararia o laço de ordens |

---

## 8. Receita: executor na thread do sandbox

Encaixa no `ActionExecutor` existente.
- `ArmyOrders.Tick()` roda **em toda volta**, antes do `if (Pending.IsEmpty) return;`.
- A intenção `ordem_exercito` vira uma missão.
- O resultado volta pela fila `Outcomes`, com relatórios a cada mudança de estado.

**Entrada da IA de linguagem**

```
{"acao":"ordem_exercito","exercito":"A7","objetivo":"mover|atacar|defender|cercar|parar",
 "alvo":"T31|C14|A9","turnos_max":5,"declarar_guerra_se_preciso":false,"motivo":"..."}
```

- O `DecisionParser` atual usa `"destino"` e só aceita T e C (src\CurrencyMod\Diplomacia\DecisionParser.cs:498-516).
- Ampliar para `"alvo"` com A# (exércitos inimigos **visíveis**, listados no dossiê) e os objetivos novos.

```csharp
// ====== Estado: só a thread do sandbox mexe em Missoes; a thread da IA só lê ArmyLocks.Snapshot ======
enum Obj { Mover, Atacar, Defender, Cercar, Parar }
sealed class Missao {
    public ulong Exercito; public int Imperio; public Obj Objetivo; public int IntentId;
    public int Territorio = -1; public ulong Cidade, Inimigo;           // um dos três
    public bool DeclararGuerra; public int AteTurno;
    public int TileDestino = -1, UltimoTurnoEmitido = -1, Tentativas;
    public ArmyGoToAction Acao; public string Estado = "nova";           // nova, emitida, andando, segurando, combate
}
static readonly Dictionary<ulong, Missao> Missoes = new Dictionary<ulong, Missao>();
static readonly HashSet<uint> NossosTickets = new HashSet<uint>();     // para o P5
static void Publicar() => ArmyLocks.Snapshot = new HashSet<ulong>(Missoes.Keys);   // troca atômica
static Sandbox ultimoSandbox; static RequestArmyActionAt pedido;
// Comparar a REFERÊNCIA do sandbox, não o GUID da partida: recarregar o mesmo save pode manter o GUID,
// mas o sandbox é recriado e as assinaturas de evento são apagadas (SIM\SimulationEvent.cs:214-229).

// ====== Intenção nova (vem do Execute do ActionExecutor, já no TurnMain) ======
static void Aceitar(ActionIntent it) {
    // validar: o exército é do império, não é isca, não é mercenário alugado (se quiser), está vivo.
    // Uma missão por exército: a nova substitui a antiga.
    Missoes[it.Entity] = new Missao { Exercito = it.Entity, Imperio = it.Empire, /* objetivo, alvo */
        AteTurno = SandboxManager.Sandbox.Turn + Math.Max(1, it.TurnsMax) - 1, IntentId = it.Id };
    Publicar();                                   // trava ANTES de qualquer ordem (§4.4)
}

// ====== Toda volta do laço do sandbox ======
static void Tick() {
    Sandbox sb = SandboxManager.Sandbox;
    if (sb == null || sb.IsSessionOnline) return;
    if (!ReferenceEquals(ultimoSandbox, sb)) {     // nova partida ou save carregado (sandbox recriado)
        ultimoSandbox = sb; Missoes.Clear(); Publicar(); pedido = null; NossosTickets.Clear();
        AssinarEventos();                          // §5 (Raised += ...)
    }
    if (Missoes.Count == 0 || sb.CurrentStateName != "SandboxState_TurnMain") return;
    int turno = sb.Turn, orcamento = 4;
    foreach (Missao m in Missoes.Values.ToArray()) {
        if (!Sandbox.SimulationEntityRepository.TryGetSimulationEntity(m.Exercito, out Army a) || a.EmpireIndex != m.Imperio)
            { Encerrar(m, false, "o exército não existe mais (fundido, dividido, destruído ou capturado)"); continue; }
        if (turno > m.AteTurno) { Encerrar(m, Chegou(m, a), "prazo esgotado; a IA nativa reassume"); continue; }
        if (Concluida(m, a)) continue;             // chegou, entrou em combate, cerco iniciado → relata e encerra ou segura
        if (a.IsLocked) continue;                  // batalha, cerco ou alvo de ataque: tenta de novo depois
        bool novoTurno = m.UltimoTurnoEmitido < turno;
        bool caiu = m.Acao != null && (m.Acao.Status == ActionStatus.Failed || m.Acao.Status == ActionStatus.Canceled);
        bool rodando = m.Acao != null && m.Acao.Status == ActionStatus.Running;
        if (rodando || !(m.Estado == "nova" || novoTurno || caiu)) continue;
        if (Sandbox.AIController.aiDecisionInProgress) continue;   // ciclo da IA em curso: espera (§4.4)
        if (orcamento-- <= 0) break;
        try { Emitir(m, a, turno); } catch (Exception ex) { Encerrar(m, false, "erro interno: " + ex.Message); }
    }
}

static void Emitir(Missao m, Army a, int turno) {
    m.UltimoTurnoEmitido = turno;
    if (m.Objetivo == Obj.Parar) {
        if (a.HasGoToAction()) PostarNosso(new OrderCancelArmyMovement(a.GUID), m, null);
        m.Estado = "segurando"; Relatar(m, true, "parado; segurando a posição"); return;
    }
    if (!EscolherAlvo(m, a, out int tile, out PathArmyAction forcada, out PathArmyAction esperada, out string erro))
        { Encerrar(m, false, erro); return; }       // §3 e §6: candidatos de tile, visibilidade, cerco já existente...
    RequestArmyActionAt r = Avaliar(a, tile, forcada);
    if (!CaminhoAceito(r, esperada, out erro)) {
        if (++m.Tentativas >= 3) Encerrar(m, false, erro); else m.Estado = "nova";
        return;
    }
    if ((r.PathActionConditionFlags & PathArmyActionConditionFlags.NeedToDeclareWar) != 0) {
        if (!m.DeclararGuerra) { Encerrar(m, false, $"precisa declarar guerra (alvo E{r.TargetEmpireIndex}, destino E{r.DestinationEmpireIndex})"); return; }
        DeclararGuerra(m, r);                     // OrderDiplomaticAction DeclareFormalWar ao suserano; surpresa só se autorizado
        m.Estado = "nova"; return;                // reavalia na próxima volta, já em guerra
    }
    if (CondicaoProibida(r.PathActionConditionFlags, out erro)) { Encerrar(m, false, erro); return; }   // §2.4
    Order ordem = MontarOrdem(r);
    m.TileDestino = (r.PathArmyActionAtPosition == PathArmyAction.Move) ? tile : -1;
    m.Estado = "emitida";
    int eta = r.PathToTarget.StepCount > 0 ? r.PathToTarget.Steps[r.PathToTarget.StepCount - 1].Turn : 0;
    PostarNosso(ordem, m, ok => {
        if (ok) { m.Acao = Sandbox.ActionController.GetActionFor<ArmyGoToAction, Army>(a); m.Estado = "andando";
                  if (m.UltimoTurnoEmitido == turno && m.Tentativas == 0) Relatar(m, true, $"a caminho; chegada estimada em {eta} turno(s)"); }
        else if (++m.Tentativas >= 3) Encerrar(m, false, "o jogo recusou a ordem 3 vezes");
        else m.Estado = "nova";
    });
}

static RequestArmyActionAt Avaliar(Army a, int tile, PathArmyAction forcada) {
    RequestArmyActionAt r = pedido ?? (pedido = new RequestArmyActionAt());       // criado com o sandbox no ar
    r.EntityGUID = a.GUID; r.SelectedUnitsCount = 0; r.TargetTileIndex = tile;     // = Start (PR\ArmyActionAtAsyncOperation.cs:20-33)
    r.ForcedPathArmyActionAtPosition = forcada; r.PenultimateTileIndex = -1; r.IsRangedAction = false;
    r.PathToTarget.Clear(); r.TargetGUID = SimulationEntityGUID.Zero;              // = Reset (:37-58)
    r.TargetEmpireIndex = -1; r.DestinationEmpireIndex = -1;
    Array.Clear(r.RansackTargetedEmpireIndexes, 0, r.RansackTargetedEmpireIndexes.Length); r.RansackNumberOfTargetedEmpire = 0;
    r.PathArmyActionAtPosition = PathArmyAction.None;
    r.PathActionFailureFlags = PathArmyActionFailureFlags.None; r.PathActionConditionFlags = PathArmyActionConditionFlags.None;
    r.ArmyActionFailureFlags = ArmyActionFailureFlags.None;
    r.AvailableAirportFailureFlags = AvailableAirportFailureFlags.None; r.BattleReinforcementFailureFlags = BattleReinforcementFailureFlags.None;
    Sandbox.SimulationEvaluator.Evaluate(r);                                       // síncrono, thread do sandbox (SE:396)
    return r;
}

static bool CaminhoAceito(RequestArmyActionAt r, PathArmyAction esperada, out string erro) {     // = PR\BaseArmyCursor.cs:322-336
    PathArmyAction acao = r.PathArmyActionAtPosition; PathResultStatus s = r.PathToTarget.ResultStatus;
    bool invasaoOk = s == PathResultStatus.Trespassing &&
                     (acao == PathArmyAction.Move || acao == PathArmyAction.Attack || acao == PathArmyAction.AttackCity);
    erro = null;
    if (r.PathActionFailureFlags != PathArmyActionFailureFlags.None || r.ArmyActionFailureFlags != ArmyActionFailureFlags.None
        || r.AvailableAirportFailureFlags != AvailableAirportFailureFlags.None || r.BattleReinforcementFailureFlags != BattleReinforcementFailureFlags.None)
        erro = Traduzir(r);                                                       // os flags em português para o dossiê
    else if (s != PathResultStatus.Success && !invasaoOk) erro = $"sem caminho ({s})";
    else if (acao == PathArmyAction.None || acao == PathArmyAction.ActionFailed || (acao & esperada) == 0) erro = $"no destino a ação seria {acao}";
    else if (r.PathToTarget.StepCount == 0) erro = "já está no destino";
    return erro == null;
}

static Order MontarOrdem(RequestArmyActionAt r) {                                // = PR\BaseArmyCursor.cs:409-456, 1324-1377
    switch (r.PathArmyActionAtPosition) {
        case PathArmyAction.Move:       return new OrderGoTo(r.EntityGUID, ref r.PathToTarget);
        case PathArmyAction.Attack:
        case PathArmyAction.AttackCity: return r.PathToTarget.StepCount == 1
                                            ? (Order)new OrderCreateBattle(r.EntityGUID, r.TargetGUID) { UseInstantResolve = false }
                                            : new OrderGoToAndCreateBattle(r.EntityGUID, ref r.PathToTarget, r.TargetGUID);
        case PathArmyAction.JoinBattle: return new OrderGoToAndJoinBattle(r.EntityGUID, ref r.PathToTarget, r.TargetGUID);
        default: return null;
    }
}

static void PostarNosso(Order o, Missao m, Action<bool> depois) {
    PostOrderTicket t = SandboxManager.PostAndTrackOrder(o, m.Imperio);          // nunca o índice do humano
    uint n = ((IBillet)o).TicketNumber; NossosTickets.Add(n);
    t.UponCompletion(() => { NossosTickets.Remove(n); depois?.Invoke(t.Result == PostOrderResponse.Valid); });   // fecha na thread do sandbox
}

// Tabela de objetivos usada por EscolherAlvo (forçada / esperada):
//  Mover    : tile pela busca da §3 (âncora + vizinhos)        | None              | Move
//  Defender : tile defensivo da §3 (cidade própria) ou âncora  | None              | Move | JoinBattle
//  Atacar A#: inimigo.WorldPosition (visível!)                 | Attack|AttackCity | Attack (inimigo em batalha: None → JoinBattle)
//  Atacar/Cercar C#: Settlement.WorldPosition (centro)         | Attack|AttackCity | AttackCity
//      já existe cerco: nosso império sitia → concluída; senão None → JoinBattle
//
// Concluida(m, a):
//  Mover    : a.WorldPosition.ToTileIndex() == m.TileDestino → Relatar("chegou") e encerrar (a IA nativa reassume)
//  Defender : chegou → Estado "segurando" (fica travado até AteTurno; dormir é opcional e só cosmético)
//  Atacar   : evento BattleStarted com o exército → "em combate" → encerrar (o BattleBrain luta)
//  Cercar   : evento SettlementSiegeStarted(cidade, império) → "cerco iniciado" → encerrar, ou manter travado se a
//             IA de linguagem for decidir assalto/manter pela ferramenta "cerco" (§6.2)
//  Toda missão: ArmyDestroying / ArmyOwnerChanged / AteTurno → encerrar com o motivo.
//
// Encerrar(m, ok, texto): Missoes.Remove(m.Exercito); Publicar(); Relatar(m, ok, texto) → ActionExecutor.Outcomes.
```

**Notas finais da receita**

1. **Continuação a cada turno.** O `UltimoTurnoEmitido < turno` faz o mod reavaliar e repostar no primeiro `TurnMain` de cada turno. Isso substitui a ação que estava esperando (§2.3).
   - Alternativa mais barata: um `SandboxManager.PostAndTrackOrder(new OrderFurtherActions(), E)` por turno e império, só quando houver missão em `WaitingForFinishTurn`.
2. **Segurar a vez da nação.** As respostas da IA de linguagem chegam com atraso. Se a ordem tem que sair no mesmo turno, usar a trava de "pronto" já existente (catálogo §4.5). Sem ela, a ordem espera o próximo `TurnMain`.
3. **Dossiê.** Mostrar as missões ativas: `A7: mover → T31, ETA 2 turnos, estado andando/segurando/travado em batalha`, e os relatórios do turno anterior.
   - Inimigos só como A# **visíveis**, vindo de `VisibilityController.IsWorldPositionVisibleFor(tile, E)`.
4. **Ordem de postagem dentro do turno.** Diplomacia (guerra) → espera o ticket `Valid` → exército. A missão já faz isso ao reavaliar na volta seguinte.
5. **Testes recomendados** (save "Francos"; dá para passar turnos):
   - (1) mover a 3+ turnos, vendo a continuação;
   - (2) atacar exército adjacente e distante;
   - (3) cercar cidade em guerra; observar o que acontece sem `ContinueSiege`;
   - (4) conferir no log "Can't find entity … Fails the task" que a tarefa nativa caiu;
   - (5) reagrupamento nativo perto de um exército travado (P2);
   - (6) carregar o save com missão ativa.

---

### Arquivos críticos para a implementação

- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\SimulationEvaluator.cs`
  - Evaluate :396-430; SetActionAndTargetGUID :451-650; SetPathToTarget :652-782; flags :784-1204.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Interop\RequestArmyActionAt.cs`
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Assembly-CSharp\Amplitude.Mercury.Presentation\ArmyActionAtAsyncOperation.cs`
  - Start :17-35; Reset :37-58.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Assembly-CSharp\Amplitude.Mercury.Presentation\BaseArmyCursor.cs`
  - :262-340, :409-456, :597-659, :720-740, :1324-1377.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\ArmyGoToAction.cs`
  - Ciclo de vida e continuação.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\ArmyGoToAndCreateBattle.cs`
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\ActionController.cs`
  - FurtherForEmpire :81-92; ReadyForTurnFinishCompletion :135-166.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\DepartmentOfTransportation.cs`
  - Validação e processamento :565-1111; OrderFurtherActions :841-853.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain.Allocators\ArmyAllocator.cs`
  - :75-86, patch P1.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.AI\Amplitude.AI.ProcessingPasses\EntityAllocator.cs`
  - :34-68.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain.Generators\ArmySpecificMission.cs`
  - :26-37, patch P2.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain.Generators.Military\SplitArmies.cs`
  - Patch P3.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.AI\AIController.cs`
  - :360, 1492, 1504-1512, 1771.
- `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\src\CurrencyMod\Diplomacia\ActionExecutor.cs`
  - Onde a receita entra.
