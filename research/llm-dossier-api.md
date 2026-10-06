# Dossiê da IA de linguagem — cola de API do jogo (pesquisa 2026-10-04)

Só leitura do código descompilado. Caminhos relativos a `_Modding\decompiled\`. Notação: FP = `Amplitude.Mercury.Firstpass\`,
SIM = `FP\Amplitude.Mercury.Simulation\`, SBX = `FP\Amplitude.Mercury.Sandbox\`, IO = `FP\Amplitude.Mercury.Interop\`,
UIH = `FP\Amplitude.Mercury.UI.Helpers\`, AC = `Assembly-CSharp\`, DATA = `Amplitude.Mercury.Data\Amplitude.Mercury.Data.Simulation\`,
FW = `Amplitude.Framework\`. Quase tudo é `internal` (`Sandbox`, `MajorEmpire`, `Settlement`, `World`, `VisibilityController`,
campos dos departamentos…) e só compila por causa do Publicizer. Complementa `llm-diplomacy-feasibility.md` e `crisis-negotiation.md`.

## Resumo rápido

**Hook recomendado:** Postfix Harmony em `SandboxState_TurnMain.Begin(params object[])` (SBX\SandboxState_TurnMain.cs:12).
- Roda na **thread do sandbox**, logo depois de todo o processamento de início de turno: passes NewTurnBegin/TurnBegin,
  `SimulationController.RefreshAll()` e refresh de visibilidade (SBX\SandboxState_TurnBegin.cs:21-56).
- Roda antes de a IA nativa agir: ela só começa ~2 s depois (FP\Amplitude.Mercury.AI\AIController.cs:336,1575).
- Enquanto o postfix roda, nenhuma ordem é processada, porque é a mesma thread.
- Deduplicar por `(Sandbox.GUID, Sandbox.Turn)`: o hook também dispara depois de carregar um save. Ignorar se `IsSessionOnline`.

**Threads:**
1. No postfix, copiar tudo para objetos simples (POCO/strings). Leva poucos ms.
2. Texto e HTTP numa `Task` de fundo.
3. Aplicar resultados e postar ordens na thread principal (Update do MonoBehaviour).

```csharp
[HarmonyPatch(typeof(SandboxState_TurnMain), nameof(SandboxState_TurnMain.Begin))]
static class InicioDoTurno {
    static string ultimo;                                   // "GUID:turno" já processado
    static void Postfix() {                                 // thread do sandbox
        Sandbox sb = SandboxManager.Sandbox;
        if (sb == null || sb.IsSessionOnline) return;
        string chave = sb.GUID + ":" + sb.Turn;
        if (chave == ultimo) return;
        ultimo = chave;
        try { var dados = Captura.Tudo(sb.Turn); Task.Run(() => Dossie.Enviar(dados)); }
        catch (Exception ex) { Plugin.Log.LogError(ex); }    // exceção aqui pula o resto do Begin da FSM
    }
}
```

**As 15 chamadas principais**

| # | Chamada | Onde |
|---|---|---|
| 1 | `SandboxManager.Sandbox` (internal static) → `.Turn`, `.GUID`, `.LocalEmpireIndex`, `.IsSessionOnline` | SBX\SandboxManager.cs:26; SBX\Sandbox.cs:306, :294, :302, :377 |
| 2 | `Sandbox.MajorEmpires[i]`, `Sandbox.NumberOfMajorEmpires`; `e.IsAlive`, `e.IsControlledByHuman` | Sandbox.cs:242-250; SIM\Empire.cs:132, :899 |
| 3 | `e.DiplomaticRelationByOtherEmpireIndex[j]` → `.CurrentState`, `.GetEmpireEmbassy(i)`, `.CurrentAgreements`, `.DiplomaticState.Crisis` | SIM\MajorEmpire.cs:39 |
| 4 | Regra "i conhece j" (snippet no §8) | IO\EmpireNameSnapshot.cs:146-167 |
| 5 | `Sandbox.VisibilityController.IsWorldPositionVisibleFor / ExploredFor / DetectedFor(tile, i)` | SIM\VisibilityController.cs:240-315 |
| 6 | `StealthAncillary.IsCollectionInvisible(army, observador)` | SIM\StealthAncillary.cs:1129 |
| 7 | Nomes já localizados: `Sandbox.EmpireNamesRepository.EmpireNamePerIndex[j]` | SIM\EmpireNamesRepository.cs:23 |
| 8 | Definição → texto: `Amplitude.Mercury.Interop.AI.LocalizationHelpers.Localize(StaticString)` (public static). Chave `%…` → texto: `UIServiceAccessManager.LocalizationService.Localize(key, padrão)` | FP\Amplitude.Mercury.Interop.AI\LocalizationHelpers.cs:28 |
| 9 | Números: `(float)e.MoneyStock.Value`, `MoneyNet`, `InfluenceStock/Net`, `ResearchNet`, `Stability`, `FameScore`, `EraStarsCount`, `SumOfPopulation` | §4 |
| 10 | `e.Settlements[k]` → `SettlementStatus`, `IsCapital`, `EntityName.ToString()`, `Region.Entity.TerritoryIndices`, `WorldPosition` | §5 |
| 11 | `Sandbox.World.TileInfo.Data[tile].TerritoryIndex`; `World.Territories[t].AdjacentTerritories`; `World.TerritoryInfo.Data[t]` (`EmpireIndex`, `LocalizedName`) | §6 |
| 12 | `e.Armies[k]` → `CombatStrength.Value`, `HealthRatio`, `Units.Count`, `WorldPosition`, `State`, `HasUnitTag(...)` | §7 |
| 13 | `Sandbox.FameRankingController.GetCurrentFameRank(i)`; `Sandbox.GameStatisticsController.EmpireStatistics[i].PerTurnData[turno]` | §10 |
| 14 | `e.DepartmentOfCommunication.Notifications` (copiar num prefix de `TurnEnd_ClearNotifications`); `SimulationEvent<T>.Raised` | §11 |
| 15 | Só leitura na thread principal: `Snapshots.GameSnapshot.PresentationData` (`EmpireInfo[]`, `VisibilityInfo`, `TerritoryInfo`, `SettlementInfo`, `ArmyInfo`) e `Snapshots.SandboxSnapshot.PresentationData.CurrentSandboxStateName` | §1 |

## 1. Ciclo de turno e threads

### 1a. Threads e pontos de sincronia
- **Sandbox:** thread própria "SandboxThread" (Sandbox.cs:479, :3427-3430; `Sandbox.ThreadId` :278). Laço (:3809-3897): ordens, requests e
  mensagens → `Ancillary.Update()` → `FiniteStateMachine.Update()` → `Snapshots.Synchronize()` a cada 100 ms, por um `Timer` (:3788-3790,
  :3888-3893) → `SimulationController.Update()` → `AIController.LateUpdate()` → `Thread.Sleep(1)`.
- **IA nativa:** thread "AI" (AIController.cs:825). Lê só os snapshots `Interop.AI`, sob `apiMutex` (:1744-1747), e posta ordens.
- **UI (thread Unity):** o Assembly-CSharp nem enxerga `Sandbox`, que é uma classe internal (não há nenhum `Sandbox.*` em AC).
  - Lê só `Snapshot<T>.PresentationData`, que tem buffer duplo (IO\Snapshot.cs:121-275).
  - O sandbox escreve no buffer da simulação em `Synchronize(frame)` (:248).
  - A thread principal troca os buffers em `Present()` (:203), chamado por `Presentation.Update()` (AC\Amplitude.Mercury.Presentation\Presentation.cs:291).
    Depois dispara `PresentationDataChanged`, também na thread principal.
  - Dados sob demanda vêm por `Request`s executados no sandbox. Exemplo: AC\Amplitude.Mercury.EffectMapper\WorldPositionAsyncOperation.cs:72-79 → `Sandbox.ProcessRequest` (Sandbox.cs:2061).
- **Ler objetos vivos (`Sandbox.MajorEmpires[i]…`) da thread principal durante o TurnMain do humano não é seguro.** Funciona quase sempre, mas sem garantia.
  - O sandbox continua processando ordens: as do humano a qualquer momento, as da IA a partir de ~2 s e depois a cada 200 ms (AIController.cs:336, 344, 1575).
  - Também continua com movimentos (`ActionController.ServerUpdate`, SandboxState_TurnMain.cs:34) e batalhas.
  - `ReferenceCollection.Add` realoca arrays (FW\Amplitude.Framework.Simulation\ReferenceCollection.cs:41-75), e o indexador `[i]` resolve a entidade no repositório a cada acesso (:23-30).
  - `Property.Value` é só um int (FW\Amplitude.Framework.Simulation\Property.cs:13). Uma leitura isolada não quebra, mas o conjunto pode sair inconsistente.
- **Opções seguras:**
  1. Postfix na thread do sandbox (recomendado).
  2. Só snapshots `Interop`, na thread principal.
  3. `Request` próprio:
     - subclasse de `IO\Request` (abstrata, IO\Request.cs:5) + `SandboxManager.PostAndTrackRequest` (SandboxManager.cs:333);
     - prefix em `Sandbox.ProcessRequest` (Sandbox.cs:2061) que trata o tipo e **retorna true**, porque é o original que envia a resposta no fim (:2473-2477);
     - o ticket fecha na thread que postou, quando ela chama `SandboxManager.RaiseTicketsFromCallingThread()`. A principal já faz isso em Presentation.cs:292.

### 1b. Fases e detecção de turno
- **FSM** (`Sandbox.FiniteStateMachine`, protected, Sandbox.cs:353; estados :493-514): `TurnBegin` → `TurnMain` → `TurnFinish` (todos
  prontos) → `TurnFinished` (trava a IA, SandboxState_TurnFinished.cs:18) → `TurnEnd` → `TurnEnded` (**`Turn++`**, SandboxState_TurnEnded.cs:30-32)
  → `AutoSave` → `TurnBegin` (SandboxState_AutoSave.cs:98). `PostStateChange` só agenda a troca; o `Begin()` do novo estado roda dentro de
  `FiniteStateMachine.RefreshState`, na thread do sandbox (FW\Amplitude.Framework\FiniteStateMachine.cs:131-161).
- **`TurnBegin.DoRun`**: `flag = TurnWhenLastBegun < Turn` (:21; `TurnWhenLastBegun` vai no save, Sandbox.cs:1044) → só em turno novo, passes
  `NewTurnBegin` + `SimulationEvent_NewTurnBegin` (:31-38) → passes `TurnBegin` (:40-46) → `SimulationEvent_TurnBegin(turn, isNewTurn)` (:47) →
  `RefreshAll` (:49) → refresh de visibilidade (:50) → troca para `TurnMain` (:56).
- **`TurnMain.Begin`** (:12-21) chama `AIController.Unlock()` (:18) e inicia o timer. O `Run` (:23-38) segue até todos ficarem `IsReady`.
- **Turno atual:** `SandboxManager.Sandbox.Turn` (internal).
  - O número exibido é o próprio `Turn`, sem +1 (AC\Amplitude.Mercury.UI\EndTurnWindow.cs:325). Começa em 1 (Sandbox.cs:516).
  - Fase, na thread do sandbox: `Sandbox.CurrentStateName` (:329) ou `IsCurrentStateOfType<T>()` (:527).
  - Fase, na thread principal: `Snapshots.SandboxSnapshot.PresentationData.CurrentSandboxStateName` (IO\SandboxSnapshot.cs:12) e
    `GameSnapshot.PresentationData.LocalEmpireInfo.CanEndTurn` (= vivo && TurnMain, IO\GameSnapshot.cs:922).
- **Ganchos e em que thread rodam**

| Gancho | Thread | Observação |
|---|---|---|
| Postfix em `SandboxState_TurnMain.Begin` | sandbox | **recomendado** |
| `SimulationEvent<SimulationEvent_TurnBegin>.Raised += …` | sandbox | Antes do RefreshAll/visibilidade; tem `e.IsNewTurn`. `SimulationEvent_NewTurnBegin` só dispara em turno novo |
| `SandboxManager.Sandbox.FiniteStateMachine.StateChange` (Begun/Ended) | sandbox | FiniteStateMachine.cs:18, 163-167 |
| `AIController.OnNewTurnStarted` (private) | sandbox | Roda no LateUpdate quando `Turn` muda (AIController.cs:1389-1393, 1670) |
| Polling de `GameSnapshot.PresentationData.CurrentTurn` / `GameSnapshot.PresentationDataChanged +=` | principal | Não existe evento de turno na thread principal; a UI faz esse polling (AC\Amplitude.Mercury.UI\ControlBannerFxHandler.cs:124) |

- Cuidados com os SimulationEvents:
  - Só inscreva com o sandbox já iniciado: com `Sandbox == null`, o `+=` loga erro e devolve null (SIM\SimulationEvent.cs:76-85).
  - É preciso inscrever de novo a cada partida: o jogo zera as inscrições no shutdown (:214-229).
- Cuidado com o polling: `CurrentTurn` muda no `TurnEnded`, **antes** do AutoSave e do TurnBegin. Exigir também
  `CurrentSandboxStateName == "SandboxState_TurnMain"`.

### 1c. Partida carregada e GUID
- **Em jogo:** `SandboxManager.IsStarted` (= `Sandbox != null && Sandbox.IsInitialized`, SandboxManager.cs:40-50). Na UI: `Presentation.IsPresentationRunning`
  (Presentation.cs:100-110) e o evento `Presentation.PresentationChange` (:120; `Starting/Started/ShuttingDown/Shutdown`,
  AC\Amplitude.Mercury.Presentation\PresentationChangeAction.cs), na thread principal. Menu x jogo:
  `IRuntimeService.Runtime.FiniteStateMachine.CurrentState is RuntimeState_OutGame` (AC\Amplitude.Mercury.UI\LoadSavesScreen.cs:344); as classes
  RuntimeState_* são internal do Assembly-CSharp (não confirmado se o Publicizer cobre essa assembly).
- **Descarregar:** o laço termina, rodam os passes `SandboxShutdown` e ficam `MajorEmpires/World… = null`, `NumberOf* = 0` (Sandbox.cs:4012-4018);
  `SandboxManager.Sandbox` vira null quando o shutdown termina (SandboxManager.cs:184-191); `Snapshots.*` também (IO\Snapshots.cs:267-280).
- **GUID:** `SandboxManager.Sandbox.GUID` (internal `System.Guid`, Sandbox.cs:294; vai no save, :1177-1181; nova partida ganha um novo, :490).
  Versão string: `Sandbox.GameID` (static, :178) = `Snapshots.GameSnapshot.PresentationData.GameID` (IO\GameSnapshot.cs:26, 505).

## 2. Impérios
- **Ordem em `Sandbox.Empires`:** primeiro os maiores, depois os menores e por último `LesserEmpire` (animais) (Sandbox.cs:1142-1160, 246-252).
  - `MajorEmpires[i].Index == i`.
  - `Bits = 1 << i` (SIM\MajorEmpire.cs:866): é o bit usado nos mapas de visão.
- **Vivo ou eliminado:**
  - `Empire.IsAlive` (campo, SIM\Empire.cs:132).
  - `MajorEmpire.EliminationCountdown` / `IsEliminationCountdownActive` (:155, :537).
  - `EmpireEndGameStatus`: InGame, Resigned ou Dead (:161).
  - A relação vira `PartialyEliminated` ou `BothEliminated`, e sai o evento `SimulationEvent_MajorEmpireEliminated`.
- **Humano:**
  - `Empire.IsControlledByHuman` (Empire.cs:899).
  - Índice do humano local: no sandbox, `SandboxManager.Sandbox.LocalEmpireIndex` (Sandbox.cs:302); na thread principal, `Snapshots.GameSnapshot.PresentationData.LocalEmpireInfo.EmpireIndex` (GameSnapshot.cs:916).
- **Menores (povos independentes):** `Sandbox.MinorEmpires[k]`, com `Index = NumberOfMajorEmpires + k`. Campos em SIM\MinorEmpire.cs:30-76:
  - `MinorFactionStatus`: Inactive/Young/Zenith/InDecline/Dying;
  - `IsPeaceful`;
  - `MajorEmpireMet`: bits dos maiores que já os encontraram;
  - `RelationsToMajor`: patronagem.

  O nome sai de `EmpireNamesRepository.EmpireNamePerIndex[minor.Index]`.
- **Snapshot da thread principal, com todos os impérios:** `GameSnapshot.Data.EmpireInfo[]`, de tamanho `NumberOfEmpires` (IO\EmpireInfo.cs;
  preenchido em IO\GameSnapshot.cs:961-1294): `IsAlive`, `IsControlledByHuman`, `EraIndex`, `EmpireStability`, `MoneyStock/Net`,
  `InfluenceStock/Net`, `ResearchNet`, `SumOfPopulation`, `FameStock`, `CurrentFameRank`, `CityCount`, `SettlementCount`, `ArmyCount`,
  `Archetypes`, `Biases`, `PersonaName`, `CurrentFactionDefinitionName`, `FactionDefinitionNames[]`, `CurrentTechnologyInfo`, `ResearchedTechnologiesCount`.

## 3. Identidade e textos localizados
- **Repositório de nomes:** `Sandbox.EmpireNamesRepository.EmpireNamePerIndex[j]` (`EmpireNameInfo`, IO\EmpireNameInfo.cs:8-38).
  Os textos já vêm no idioma atual e são recalculados no sandbox quando muda a facção ou o controle humano.
  - `EmpireNameWithoutSymbolPerEmpireColorIndex[0].StringValue`: título do império, montado com a EmpireNamingDefinition e a persona (SIM\EmpireNamesRepository.cs:276-289).
  - `AvatarNameWithoutSymbolPerEmpireColorIndex[0].StringValue`: o líder. É o `PersonaName` (IA) ou o nome do usuário (humano) (:241-244).
  - `EmpireRoughName = Localize("%EmpireFactionNameTitle", FactionUIMapper.Title)` (:354-358). Formato exato da chave não confirmado.
  - Índice 0 = sem tags de cor; 1 a 3 = `<c=RRGGBB>`. Os arrays sem "WithoutSymbol" começam com o glifo do símbolo + espaço inseparável U+00A0 (:442-492).
  - Na thread principal, os mesmos dados estão em `Snapshots.EmpireNameSnapshot.PresentationData.EmpireNamePerIndex[j]`.
- **Não usar `GetEmpireNameParameter(j)` para a IA.** Ele devolve o nome "desconhecido" quando o **humano** não conhece j (IO\EmpireNameSnapshot.cs:37-54).
- **Cultura:**
  - Atual: `e.FactionDefinition.Name` (StaticString, Empire.cs:200) → `LocalizationHelpers.Localize(nome)`, que devolve o título da UIMapper (LocalizationHelpers.cs:28-46).
    A UI faz o equivalente com `Utils.DataUtils.GetLocalizedTitle(info.CurrentFactionDefinitionName)` (AC\Amplitude.Mercury.UI\LoadSavesScreen.cs:355).
  - Sabor extra: `FactionUIMapper.LeaderTitle/Adjective/Quote`, que são chaves a localizar (Amplitude.Mercury.Data\Amplitude.Mercury.UI\FactionUIMapper.cs:12-24).
  - Histórico: `e.CivilizationInfo.EraSummariesByEraIndex[k]`, com `.FactionDefinitionName` e `.StartingTurn` (IO\CivilizationInfo.cs:11; IO\EraSummaryInfo.cs:17-19).
  - Próxima: `DepartmentOfDevelopment.NextFactionName` / `IsNextFactionConfirmed` (SIM\DepartmentOfDevelopment.cs:145-147).
- **Era:** `e.DepartmentOfDevelopment.CurrentEraIndex` (:54; 0 = Neolítico) e `GetCurrentEraDefinition().Name` (:925) → `LocalizationHelpers.Localize`.
  A UI faz `GetLocalizedTitle(eraInfo.EraDefinitionName)` (AC\Amplitude.Mercury.UI\TechnologyScreen_EraToggle.cs:35).
- **Personalidade:** `MajorEmpire.Archetypes` (`Archetype` flags), `Biases` (`Bias` flags), `PersonaStrengthDefinitions[]`, `PersonaName`,
  `PersonaContent` (MajorEmpire.cs:179, 215-221; preenchidos em `SetPersonaContent`, :1749-1784). `PersonaContent.Name` já vem localizado
  (serializado como "LocalizedName", FP\Amplitude.Mercury.Persona\PersonaContent.cs:11, 141).
  - Texto de um traço: `Utils.DataUtils.EnumUIMappers.GetEnumUIMapper(valor).Title` → localizar, um bit por vez (como
    AC\Amplitude.Mercury.UI\ProfileScreen_PersonaAttributeArchetypeItem.cs:45-47; ctor em UIH\DataUtils.cs:438-447). Strengths via UIMapper: não confirmado.
- **Religião:** `e.Religion.Entity?.Index` → `Sandbox.ReligionManager.ReligionInfo.Data[idx].Name.ToString()` (SIM\Religion.cs:93). O nome pode ter sido personalizado.
- **Cor:** só RGB, sem nome. `EmpireNameInfo.ColorPerEmpireColorIndex[1..3]` ou `ColorStringPerEmpireColorIndex`; slot em `Empire.ColorIndex` (EmpireNamesRepository.cs:122-135).
- **Enums em texto:** `GetEnumUIMapper(DiplomaticStateType.War).Title` (DATA\DiplomaticStateType.cs:5). O mesmo vale para os níveis de acordo.
- **Localizar fora da thread principal:**
  - `UIServiceAccessManager.LocalizationService.Localize(key, padrão)` (Amplitude.UI\Amplitude.UI\UIServiceAccessManager.cs:33) é seguro em qualquer thread: o contexto do `LocalizationManager` é `[ThreadStatic]` (FW\Amplitude.Framework.Localization\LocalizationContext.cs:29-47).
  - `Amplitude.Mercury.Utils.TextUtils.Localize(key)`, com um só argumento, também serve. As sobrecargas com parâmetros usam arrays compartilhados (UIH\TextUtils.cs:102-121, 319-359) e disputam com a UI.
  - Existem duas instâncias: `Amplitude.Mercury.Utils` (FP\Amplitude.Mercury\Utils.cs:5-29, usada pelo sandbox) e `Amplitude.Mercury.UI.Utils` (AC). As duas são iniciadas pelo `UIManager` (AC\Amplitude.Mercury.UI\UIManager.cs:1016-1017).

## 4. Economia e estado próprio (sandbox; ler sempre como `(float)prop.Value`)

**Valores prontos**

| Valor | Membro | Arquivo:linha |
|---|---|---|
| Dinheiro: renda | `MoneyNet` | SIM\Empire.cs:685 |
| Dinheiro: estoque | `MoneyStock` | SIM\MajorEmpire.cs:255 |
| Influência | `InfluenceNet` / `InfluenceStock` | MajorEmpire.cs:293 / :295 |
| Ciência | `ResearchNet` | MajorEmpire.cs:253 |
| Estabilidade | `Stability` (0 a 100) + `EmpireStabilityDefinition` | MajorEmpire.cs:282 / :109 |
| Fama | `FameScore` | MajorEmpire.cs:440 |
| Estrelas de era | `EraStarsCount` / `SumOfEraStars` | MajorEmpire.cs:261 / :263 |
| População | `SumOfPopulation` (sem cidades capturadas) / `PopulationCount` | MajorEmpire.cs:376 / Empire.cs:699 |
| Unidades | `SumOfUnits` / `MilitaryForceCount` | MajorEmpire.cs:380 / :427 |
| Tecnologias | `NumberOfUnlockedTechnologies` | MajorEmpire.cs:358 |
| Guerras ativas | `NumberOfOngoingWar` | MajorEmpire.cs:526 |
| Déficit | `ConsecutiveTurnEndedInMoneyDeficit` | MajorEmpire.cs:496 |
| Batalhas vencidas | `BattleWonCount` | MajorEmpire.cs:101 |
| Territórios | `TerritoryCount` | Empire.cs:688 |
| Assentamentos / cidades | `SettlementCount` / `CityCount` | Empire.cs:701 / :703 |
| Cidades capturadas / ocupadas | `CapturedCityCount` / `OccupiedCityCount` | Empire.cs:705 / :707 |

**O que precisa ser calculado**
- **Comida e indústria do império:** somar, por assentamento, `FoodProduced` e `ProductionNetAfterAffinityBonuses`. É o que o próprio jogo grava nas estatísticas (SIM\GameStatisticsController.cs:369-401).
  Por cidade há ainda `MoneyNet`, `ScienceNet(AfterAffinityBonuses)` e `GrowthNet` (SIM\Settlement.cs:147-170).
- **Pesquisa atual:**
  - `e.TechnologyQueue.Entity.TechnologyIndices[0]` → `DepartmentOfScience.Technologies.Data[idx]`, com `TechnologyDefinition.Name`, `Cost`, `InvestedResource` e `EraIndex` (SIM\DepartmentOfScience.cs:27).
  - Turnos restantes: `GetNumberOfRemainingResearchTurn` (:772). Exemplo de uso em IO\GameSnapshot.cs:1207-1257.
  - Total pesquisado: `ComputeTechnologiesCountByTechnologyState(TechnologyStates.Completed)` (:73).
  - Era tecnológica: `DepartmentOfScience.TechnologicalEras`, um estado por era (:29). Não confirmado como a UI deriva a era atual.
- **Era e estrelas:** `DepartmentOfDevelopment.CurrentEraIndex`, `CurrentEraStarRequirement` (:76) e `EraStarInfoPool` (:72).
- **Força militar total:** não existe pronta. Somar `army.CombatStrength.Value` (soma das unidades, SIM\UnitCollection.cs:36-37) sobre `e.Armies`.
- **Apoio à guerra por relação:** `e.DiplomaticRelationByOtherEmpireIndex[j].GetEmpireEmbassy(e.Index).EmpireMoral.Moral`, FixedPoint de 0 a 100 (SIM\DiplomaticAmbassy.cs:59; SIM\DiplomaticMoral.cs:29).
  Para o outro lado, use `GetEmpireEmbassy(j)`. A tela mostra os dois (IO\DiplomaticEmpireSummary.cs:158, 168).

## 5. Assentamentos (cidades e postos avançados)

**Coleções do império** (SIM\Empire.cs:162-168; semântica em SIM\DepartmentOfTheInterior.cs:8296-8337)

| Coleção | O que contém |
|---|---|
| `e.Settlements` | Tudo o que possui: cidades, postos avançados e cidades capturadas |
| `e.Cities` | Cidades próprias |
| `e.CapturedCities` | Cidades tomadas de outros, ainda com a flag "Captured" |
| `e.OccupiedCities` | Cidades suas ocupadas por outro (ficam fora de `Settlements`) |

**Campos de `Settlement`** (SIM\Settlement.cs)

| Campo | Linha | Observação |
|---|---|---|
| `EntityName` | :18 | ver "Nome" abaixo |
| `SettlementStatus` | :26 | `City`; posto avançado = `Camp`; também `BeingSettled`, `Evolving`, `Relocating` (DATA\SettlementStatuses.cs) |
| `IsCapital` | :30 | ou `MajorEmpire.Capital.Entity` (MajorEmpire.cs:117) |
| `WorldPosition` | :32 | `.ToTileIndex()` dá o tile |
| `FoundationTurn` | :34 | |
| `Empire`, `OriginalEmpire`, `Region` | :74-78 | São `Reference<T>`: usar `.Entity`. `OriginalEmpire.Entity != null` = cidade capturada |
| `PublicOrderCurrent` | :206 | é a "estabilidade" da cidade |
| `Population` / `TotalPopulation` | :219 / :422 | |
| `TerritoryCount` | :290 | |
| `Siege` | :412 | |
| `CityFlags` | :414 | `Besieged` = 1, `Captured` = 2 (SIM\CityFlags.cs) |

- **Territórios da cidade:** `s.Region.Entity.TerritoryIndices` (List<int>) ou `.Territories` (SIM\Region.cs:138-144).
  O território do centro é `World.TileInfo.Data[s.WorldPosition.ToTileIndex()].TerritoryIndex`.
- **Nome:** `s.EntityName.ToString()` devolve `VerifiedUserDefinedName`, senão `UserDefinedName`, senão `GetDefaultName()` (padrão localizado)
  (IO\EntityNameInfo.cs:149-208, 226-234). `OrderRenameSimulationEntity` grava `UserDefinedName` na hora (Settlement.cs:486-489;
  SIM\DepartmentOfTheInterior.cs:6324-6338) e depois `VerifiedUserDefinedName` (Settlement.cs:501-508); só vale para cidade própria e não capturada
  (:510-526). Não chame `GetFullName()` fora da thread principal: usa o LocalizationWorker compartilhado (EntityNameInfo.cs:222).
- **Cópias leves:** `Sandbox.World.SettlementInfo.GetReferenceAt(s.PoolAllocationIndex)` ou o snapshot `GameSnapshot.PresentationData.SettlementInfo.Data[…]`.
  Têm `IsBesieged`, `IsCaptured`, `Population`, `TerritoryCount`, `TileIndex` e `EntityName` (IO\SettlementInfo.cs:43-111).

## 6. Territórios e mapa
- **Total e conversões:** total = `Sandbox.World.Territories.Length` (SIM\World.cs:187). Tile → território = `World.TileInfo.Data[tile].TerritoryIndex`
  (short; World.cs:276; IO\TileInfo.cs:27); na thread principal, `GameSnapshot.Data.GetTerritoryIndexAt(tile)` (IO\GameSnapshot.cs:399).
  Posição ↔ tile: `WorldPosition.ToTileIndex()` (FP\Amplitude.Mercury\WorldPosition.cs:687) e `new WorldPosition(tile)` (:74).
- **Campos de `Territory`** (SIM\Territory.cs): `Index` :12, `ContinentIndex` :14, `IsOcean` :18, `TileIndexes` :22, `AdjacentTerritories` :24, `VisualCenter` :32, `Region` :102.
  - Os vizinhos (`AdjacentTerritories`) são fixos, calculados ao carregar o mundo (World.cs:840). O snapshot tem a mesma lista em `TerritoryInfo.AdjacentTerritoryIndexes`.
  - Helpers: `World.AreTerritoriesAdjacent` (:2561), `IsTerritoryTouchingEmpire` (:2590), `GetContinentIndex(tile)` (:2271).
  - Contorno da cidade inteira: `Region.SurroundingTerritories` (Region.cs:140).
- **Dono do território:** `World.TerritoryInfo.Data[t].EmpireIndex` — **255 = sem dono**; menores aparecem com índice ≥ `NumberOfMajorEmpires`.
  Também `Claimed`, `SettlementIndex`, `IsOwnedByCity` (IO\TerritoryInfo.cs:14-22; atualizados em World.cs:2345-2408). O mod usa
  `territory.Region.Entity?.Settlement.Entity?.Empire.Entity` (`TradeBlockade.OwnerOf`), que dá no mesmo.
- **Nome do território** (regra da UI, UIH\GameUtils.cs:4297-4322):
  - Se `AdministrativeDistrictGUID != 0` e o centro administrativo está no tile do assentamento, o território leva o nome do assentamento.
  - Senão, `TerritoryInfo.LocalizedName`. Esse texto é localizado ao carregar o mundo e ganha " -- N" quando o nome se repete (World.cs:1328-1341, chamado em :945).
- **Continente:** `World.ContinentInfo.Data[c]`, com `ContinentName` (é uma chave: localizar, como UIH\GameUtils.cs:2497), `TerritoryIndexes` e `IsOcean` (IO\ContinentInfo.cs).
- **Distância:**
  - Em hexes: `WorldPosition.GetDistance(WorldPosition|int)` (:613, :636) ou `WorldPosition.GetTileIndexDistance(a, b)` (:325). As duas tratam o wrap do mapa.
  - Entre territórios: distância entre os `VisualCenter`. Em saltos: BFS próprio em `AdjacentTerritories`.
  - Caminho real: `Sandbox.TerritoryPathfindManager.FindPath(context, …)` (SIM\TerritoryPathfindManager.cs:693), que pede um contexto de comércio (não confirmado para uso genérico); ou `AIPathfindManager`, que é assíncrono (ver feasibility §1).

## 7. Exércitos
- **Listas por império:** `e.Armies` (SIM\Empire.cs:170). Há também `Decoys` (:176) e `Squadrons` (:178, aéreos).
- **`UnitCollection`** (SIM\UnitCollection.cs): `Units` :14 (`Units.Count` = nº de unidades), `Empire` :16, `SiegeAsBesieger` /
  `SiegeAsDefender` :20-22 (cerco), `WorldPosition` :30, `CombatStrength` :37 (soma das unidades), `HealthRatio` :123 (média de 0 a 1),
  `IsLockedByBattle` :119, `HasUnitTag(UnitTagAsAbility)` :166.
- **`Army`** (SIM\Army.cs): `EntityName` :62; `State` :96 (Idle, Ransacking, Attacking, Defending, Bombarding, …, Infiltrating, StealingTrade —
  SIM\ArmyState.cs); `Flags` :100 (`AtSea`, `AtHighSea`, `IsDecoy`, `IsAnimal`… — SIM\ArmyFlags.cs); `AtSea` :252; `HasGoToAction()` :305 = está se movendo.
- **Espião ou naval:**
  - Espião/agente: `HasUnitTag(UnitTagAsAbility.Agent)` (valor 34) ou `Stealth` (27).
  - Naval: `HasUnitTag(UnitTagAsAbility.Seafaring)` (11) ou `AtSea` (DATA\UnitTagAsAbility.cs).
- **Território do exército:** `World.TileInfo.Data[army.WorldPosition.ToTileIndex()].TerritoryIndex`.
- **Cópia leve:** `World.ArmyInfo` ou `GameSnapshot.ArmyInfo` (IO\ArmyInfo.cs; preenchido em Army.cs:625-781).
  Tem `NumberOfUnits`, `CombatStrength`, `HealthRatio`, `ArmyState`, `HasGoToAction`, `SiegeInfoIndex`, `IsLockedByBattle` e `…NotInvisible`.
  **`ArmyInfo.IsInvisible` é calculado contra o humano local** (Army.cs:683), não contra a IA.

## 8. Diplomacia por par (i, j)
- **Acesso:**
  - `MajorEmpire.DiplomaticRelationByOtherEmpireIndex[j]` (MajorEmpire.cs:39). **É null para o próprio índice i** (:589-596).
  - A relação é um objeto só para os dois lados (`LeftEmpireIndex/RightEmpireIndex`, SIM\DiplomaticRelation.cs:29-31).
  - Lado de i: `rel.GetEmpireEmbassy(i)` (:426). Lado do outro: `GetEmpireEmbassy(j)` ou `.OtherAmbassy.Entity`.
- **Estados** (`DiplomaticStateType`, DATA\DiplomaticStateType.cs): Unknown, PartialyKnown, Peace, Alliance, VassalToLiege, VassalToFellowVassal,
  VassalToExternal, War, PartialyEliminated, BothEliminated.
  - `rel.CurrentState` (:67); o objeto do estado é `rel.DiplomaticState` (:69).
  - Suserano e vassalos: `e.Liege.Entity` e `e.Vassals` (Empire.cs:194-196).
- **i conhece j** — mesma regra do jogo (IO\EmpireNameSnapshot.cs:157-159; IO\DiplomaticEmpireSummary.cs:123-131):
```csharp
static bool Conhece(MajorEmpire eu, int outro) {
    if (outro == eu.Index) return true;
    DiplomaticRelation r = eu.DiplomaticRelationByOtherEmpireIndex[outro];
    if (r.CurrentState >= DiplomaticStateType.Peace) return true;            // Peace até BothEliminated
    return r.CurrentState == DiplomaticStateType.PartialyKnown               // contato de mão única
        && ((DiplomaticState_PartialyKnown)r.DiplomaticState).LeftKnowRight == (r.LeftEmpireIndex == eu.Index);
}
```
- **Relações entre outros:** o jogo só avisa i de uma guerra entre j e k se i está em Paz ou acima com **os dois** (`CurrentState > PartialyKnown`, SIM\DiplomaticRelationHelper.cs:341-364).
  Usar a mesma regra para o que i sabe das relações entre outros. Não confirmado se a tela de diplomacia filtra exatamente assim.
- **Acordos:** `rel.CurrentAgreements` (struct `Agreements`, IO\Agreements.cs:17-35) traz `EconomicalAgreementLevel`, `InformationAgreementLevel`,
  `CulturalAgreementLevel`, `MilitaryAgreementLevel` e `GetLevel(AgreementCategory)`.
  - São níveis, não booleanos. "Assinado" = nível acima do piso do estado atual: `nível > rel.DiplomaticState.LowestAgreements.X` (piso e teto em SIM\BaseDiplomaticState.cs:23-25).
  - Exemplo em Paz (SIM\DiplomaticState_Peace.cs:14-27):

    | Categoria | Piso | Teto |
    |---|---|---|
    | Econômica | ForbidNewTrade | AllResourceTrade |
    | Cultural | ClosedBorder | OpenBorder |
    | Informação | ShareCapitalPosition | ShareMaps |
    | Militar | Skirmish | NonAggression |

  - Booleanos práticos: fronteiras abertas = `Cultural >= OpenBorder`; comércio = `Economical >= LuxuryTrade` (`AllResourceTrade` = todos os recursos);
    mapas compartilhados = `Information >= ShareMaps`; não agressão = `Military >= NonAggression`.
  - Efeitos em forma de flags: `amb.CurrentAbilities` (`DiplomaticAbility`, DiplomaticAmbassy.cs:73). O mapa nível → habilidade está em SIM\DiplomaticAgreementHelper.cs:9-83.
- **Tratados:** aliança = `CurrentState == Alliance`. Proposta pendente: `HasPendingTreatyProposition()` / `HasPendingAgreementProposition()` (:341-359),
  com os detalhes em `DiplomaticState.CurrentTreatyInfo` / `CurrentAgreementsInfo` (BaseDiplomaticState.cs:17-19).
- **Guerra:**
  - `rel.IsAtWar()` (:399) e `IsAllOutWar()` (:404).
  - `(rel.DiplomaticState as DiplomaticState_War).WarInfo`, com `StartingTurn`, `InitiatorEmpire` e `Flags` (Surprise/AllOutWar) (SIM\DiplomaticState_War.cs:15; IO\DiplomaticWarInfo.cs:18-24).
  - Placar: `rel.GetWarScoreFor(i)`, que vale 0 fora de guerra (:435-443).
  - Rendição pendente: `HasPendingSurrenderProposition()` (:366).
- **Reclamações, exigências e crise** (mais detalhes em `crisis-negotiation.md`):
  - Reclamações: `amb.AvailableGrievances` (`Count` e indexador; IO\AvailableGrievanceCollection.cs:9-34). Cada índice aponta para
    `amb.MyEmpire.Entity.DepartmentOfForeignAffairs.GrievanceAllocator.GetReferenceAt(idx)` (SIM\DepartmentOfForeignAffairs.cs:71), um `DiplomaticGrievanceInfo`
    com `GrievanceType`, `TurnWhenObserved`, `MaxDuration` e `DemandGain` (IO\DiplomaticGrievanceInfo.cs).
  - Exigências: `amb.OnGoingDemands.DemandIndexesCount` + `DemandsAllocator` (:73); atalhos `rel.HasDemandsAgainstOther(i)` / `HasDemandsAgainstMe(i)` (:293-323).
  - Crise: `rel.DiplomaticState.Crisis` (SIM\CrisisInfo.cs:8-21), com `CrisisStatus` (None, OnGoing, DemandRefused, InternationalCrisisVote…) e `LastRefuserEmpireIndex`.
- **Humor da IA nativa** (só saída): `amb.CurrentAIBehaviourState` (`AIBehaviourState`), `AIRapportScore` e `AISuperiorityScore` (DiplomaticAmbassy.cs:41-49).
  O apoio mínimo para guerra formal é `DeclareFormalWarMoralThreshold` (80, :279).
- **Detectar mudanças:** `rel.Frame` (:73) muda sempre que uma das embaixadas muda.

## 9. Névoa de guerra
- **No sandbox:** `Sandbox.VisibilityController` (classe internal com métodos public; Sandbox.cs:82). Testes por império, com `int tile` ou
  `WorldPosition` (SIM\VisibilityController.cs:240-315): `IsWorldPositionVisibleFor(tile, empire)` (visível agora), `IsWorldPositionExploredFor`
  (já explorado) e `IsWorldPositionDetectedFor` (detecção de furtivos).
  - Por baixo há mapas por tile com o bit `1 << i`: `ExplorationMap`, `VisibilityMap`, `DetectionMap`, `VisibleByPatrolMap`, `VisionPerRow`,
    mais os `Battle*Map` (:36-52). A visão compartilhada já está embutida nos bits.
  - Para impérios que não são maiores, os testes retornam `true`.
- **Unidades furtivas:**
  - Um exército de j é furtivo para i se `StealthAncillary.IsCollectionInvisible(army, Sandbox.MajorEmpires[i])` for verdadeiro (SIM\StealthAncillary.cs:1129-1140). É falso quando j compartilha visão furtiva com i.
  - Nesse caso i só vê o exército inteiro se o tile estiver **detectado**. Sem detecção, vê só a parte não furtiva: `NumberOfUnitsNotInvisible`, `CombatStrengthNotInvisible` e `HealthRatioSumNotInvisible` (UnitCollection.cs:72-88, 113).
  - É a mesma regra da UI (AC\Amplitude.Mercury.UI\ArmyPin.cs:710).
```csharp
VisibilityController vc = Sandbox.VisibilityController;
int tile = a.WorldPosition.ToTileIndex();
if (vc.IsWorldPositionVisibleFor(tile, eu.Index)) {
    bool oculto = StealthAncillary.IsCollectionInvisible(a, eu) && !vc.IsWorldPositionDetectedFor(tile, eu.Index);
    int unidades = oculto ? a.NumberOfUnitsNotInvisible : a.Units.Count;   // 0 = não aparece
    float forca  = (float)(oculto ? a.CombatStrengthNotInvisible.Value : a.CombatStrength.Value);
}
```
- **Na thread principal** (cópia segura, todos os impérios): `Snapshots.GameSnapshot.PresentationData.VisibilityInfo`, com os mesmos cinco mapas (copiados em IO\GameSnapshot.cs:825-841).
  - Não inclui os mapas de batalha.
  - Fica tudo marcado se `IsPresentationFogOfWarEnabled == false`.
  - Helpers prontos: `Presentation.PresentationVisibilityController.IsTileVisible(tile, empire)` e `IsTileDetected(tile, empire)` (AC\Amplitude.Mercury.Presentation\PresentationVisibilityController.cs:148-196). O helper de "explorado" existe só para o jogador local.
- **Não usar da thread principal** `Interop.AI.Snapshots.Game.IsPositionVisibleFor` / `IsPositionExploredFor` (FP\Amplitude.Mercury.Interop.AI\GameSnapshot.cs:71, 95): são da IA nativa, com buffer único escrito sob `apiMutex`.
- **Listas baratas do que i conhece:**
  - **Assentamentos:** `MajorEmpire.DistrictInfo` (MajorEmpire.cs:59) é a memória "última vista" de i: os distritos são copiados quando o tile fica visível (VisibilityController.cs:1368-1399).
    Cada `DistrictInfo` tem `SettlementSimulationEntityGUID`, `EmpireIndex` e `TileIndex` (IO\DistrictInfo.cs:12-22). Não confirmado para postos avançados.
    Aproximação simples: `IsWorldPositionExploredFor(s.WorldPosition.ToTileIndex(), i)`. Dados vivos (população, cerco) só se o tile estiver visível agora.
  - **Terreno:** `MajorEmpire.TileInfo` guarda o último estado visto de cada tile (:65; World.cs:2635-2639).
  - **Territórios pisados:** `TerritoryInfo.ExploredByEmpires`, um bitmask (IO\TerritoryInfo.cs:28) marcado quando um exército entra (World.cs:2776-2781).
  - **Exércitos:** não há lista pronta; filtrar todos os `Armies` com o teste de visibilidade e detecção acima.
  - **Espionagem:** `MajorEmpire.ArmyStealthIntels`, com `TileIndex` e `EmpireIndex` (:183).

## 10. Dados públicos
- **Turnos e velocidade:** turno = `Sandbox.Turn`. Limite = `Sandbox.EndGameController.TurnLimit` (-1 sem limite; já multiplicado pela velocidade,
  SIM\EndGameController.cs:24, 357; snapshot `EndGameSnapshot.PresentationData.TurnLimit`, IO\EndGameSnapshot.cs:22). Velocidade =
  `Sandbox.GameSpeedController.CurrentGameSpeedDefinition` (internal, SIM\GameSpeedController.cs:14; localizar `.Name`). Eras do jogo =
  `Sandbox.Timeline.StartingEraIndex`, `EndingEraIndex`, `EraCount`, `GetGlobalEraIndex()` (SIM\Timeline.cs:14-22, 115).
- **Fama:** `e.FameScore`; posição `Sandbox.FameRankingController.GetCurrentFameRank(i)`; lista ordenada `FillCurrentEmpireRankings(int[] porPosicao)`
  (SIM\FameRankingController.cs:44-57). Snapshot: `EmpireInfo.FameStock` / `CurrentFameRank` e `GameSnapshot.GlobalEraIndex` (IO\GameSnapshot.cs:860);
  proezas competitivas em `FameSnapshot` (IO\FameSnapshot.cs:9-11).
- **Corrida de eras:** `EmpireInfo.EraIndex` de todos e as estrelas (`EraStarsCount`). Não confirmado o quanto disso o jogador vê de impérios que não conhece; na dúvida, anonimizar.
- **Vitória:** `EndGameController.EndGameConditionActivation[]`, `ComputeEndGameConditionsBits()` (:459) e `TryGetEndCondition<T>` (:222).

## 11. Histórico e "o que mudou"
- **Notificações por império** (a IA também recebe): `e.DepartmentOfCommunication.Notifications` (`List<Notification>`, SIM\DepartmentOfCommunication.cs:49).
  - Cada uma é um `Notification<T>.Data` com `T : INotificationData` (IO\Notification.cs:44-48). São 183 tipos em `IO\*NotificationData.cs`.
    Exemplos: WarDeclared, WarDeclaredBetweenThirdParties, CityOwnerChanged, BattleWon/Lost, DiplomaticStateChanged, DemandsReceived, GrievanceAvailable,
    AllianceStarted/Broken, WarEndedBy*, TerritoryOwnerChanged, StealthActivity.
  - Já respeitam o que cada império sabe.
  - **São apagadas a cada TurnEnd** (`TurnEnd_ClearNotifications`, :723-746). Copiar num prefix desse método (private, por império, roda no sandbox) e completar no hook de TurnMain.
- **Eventos da simulação** (rodam no sandbox): `SimulationEvent<T>.Raised += handler` (SIM\SimulationEvent.cs:241-255), ou `SimulationEvent.Raised` para receber todos. São 193 tipos `SIM\SimulationEvent_*.cs`.

  | Evento | Campos úteis |
  |---|---|
  | `DiplomaticStateChanged` | `EmpireIndex`, `OtherEmpireIndex`, `OldState`, `NewState` |
  | `CityCaptured` | `Settlement`, `OldEmpireIndex`, `NewEmpireIndex` |
  | `BattleTerminated` | `AttackerLeaderEmpireIndex`, `DefenderLeaderEmpireIndex`, resultados |
  | `TerritoryOwnerChanged` | território, dono antigo, dono novo |

  Outros úteis: `WarStarted`, `TreatySigned`, `DiplomaticActionExecuted`, `MajorEmpireEliminated`, `FactionChanged`, `EraChanged`, `GrievanceCreated`, `CreateDemand`,
  `SettlementSiegeStarted`, `AgentDiscovered`.
  Cada tipo reaproveita uma única instância (`Self`): copie os campos na hora, não guarde a referência.
- **Log diplomático por relação:** `rel.LogEntries`, lista de `DiplomaticLogEntry` com `TurnNumber`, `InitiatorEmpireIndex`, `LogEntryType`, `DiplomaticParameter` (= `DiplomaticAction`)
  e as variações de apoio (SIM\DiplomaticRelation.cs:47, 479-520; SIM\DiplomaticLogEntry.cs). Guarda no máximo 10 entradas e cada uma some após 30 turnos.
- **Séries por turno:** `Sandbox.GameStatisticsController.EmpireStatistics[i].PerTurnData[turno]`. Gravadas no TurnEnd (SIM\GameStatisticsController.cs:29, 78, 369-401; IO\EmpireTurnStatistics.cs).
  - Por turno: `Fame`, `FoodProduced`, `IndustryProduced`, `MoneyProduced`, `ScienceProduced`, `InfluenceProduced`, `EmpireStability`, `NumberOfPopulations`,
    `NumberOfTechnologies`, `NumberOfMilitaryUnits`, `NumberOfCities`.
  - Acumulados: `NumberOfBattleWonPerLoserEmpireIndex` e `CurrentWarPeriods` (IO\EmpireStatistics.cs).
- **Não serve:** `AIEventRepository` (FP\Amplitude.Mercury.Interop.AI\AIEventRepository.cs) é consumido pela IA nativa a cada execução; não guarda histórico.

## Armadilhas (gotchas)
- **FixedPoint:** converter com `(float)x`, `(int)x` ou `FixedPoint.RoundToInt(x)`. Não existe cast para double (FW\Amplitude\FixedPoint.cs:38-54).
  `Property.Value` e `EditableProperty.Value` só leem um int, sem cálculo preguiçoso.
- **`StaticString` não é texto:** nomes de definição passam pela UIMapper (`LocalizationHelpers.Localize`). Chaves que começam com `%` passam pelo LocalizationService; texto sem `%` volta igual (UIH\TextUtils.cs:123-130).
- **`Reference<T>`:** sempre `.Entity`, que pode ser null. `ReferenceCollection[i]` consulta o repositório a cada acesso: guarde em variável local.
- **Valores que enganam:** a relação com você mesmo é null; `TerritoryInfo.EmpireIndex` vale 255 sem dono; os testes de visão retornam true para menores e neutros.
- **Cópias `World.*Info`** (ArmyInfo, SettlementInfo) só atualizam na sincronização. Dentro do hook, prefira os objetos vivos.
- **Tickets de `PostAndTrackOrder` / `PostAndTrackRequest`** só fecham na thread que os criou, e só quando ela chama `RaiseTicketsFromCallingThread` (SBX\SandboxManager.cs:105-119, 159-182).
  Poste da thread principal ou do sandbox, nunca de uma `Task`.
- **Não segurar a thread do sandbox:** nada de HTTP no hook. Uma exceção no postfix interrompe o `RefreshState` da FSM antes do evento Begun, então use sempre try/catch.
- **Hot-reload:** no `Stop`, remova os handlers de `SimulationEvent` (`-=`); senão o do assembly antigo continua inscrito. No fim da partida o jogo já zera tudo (SimulationEvent.cs:214-229).

### Problemas no código de então (`src\CurrencyMod\Diplomacia\`) — **todos corrigidos em 2026-10-04**
Correções:
- leitura pelo gancho `TurnMain.Begin` (`Capture\TurnCapture.cs`);
- turno = `CurrentTurn`, com migração do estado salvo;
- nomes vindos de `EmpireNamesRepository`;
- regra do contato parcial;
- plano B na thread principal só em `SandboxState_TurnMain`.

Lista original:
- `GameAccess.TryGetSession` usa `CurrentTurn + 1`. O jogo mostra `CurrentTurn` direto (EndTurnWindow.cs:325; começa em 1, Sandbox.cs:516), então o turno sai 1 a mais.
- `GameAccess.EmpireName` e `GameText.CultureName` usam `GetEmpireNameParameter`. Ele esconde os impérios que o **humano** não conhece e devolve o título completo, não a cultura.
  Usar `EmpireNamePerIndex[j]` e `FactionDefinition` (§3). `GameText.EraName` usa nomes fixos: trocar pela localização.
- `DossierBuilder` lê objetos vivos na thread principal (§1a) e trata `PartialyKnown` como conhecido nos dois sentidos (regra no §8).
- `IaModule.Tick` espera 3 s depois que `CurrentTurn` muda. Mas `CurrentTurn` muda antes do AutoSave e do TurnBegin, e a IA nativa começa ~2 s depois do TurnMain.
  Trocar pelo hook do §1 ou exigir `CurrentSandboxStateName == "SandboxState_TurnMain"`.
