# Espiões, janela nativa de espionagem e interceptação de cartas — pesquisa (2026-10-04)

Pesquisa só de leitura, para o design `docs\design-diplomacia-ia.md` §8.3.1 e §8.1. Linhas do código descompilado.
Raízes: SIM = `_Modding\decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\`, IO = `...\Amplitude.Mercury.Interop\`,
DATA = `_Modding\decompiled\Amplitude.Mercury.Data\Amplitude.Mercury.Data.Simulation\`,
UI = `_Modding\decompiled\Assembly-CSharp\Amplitude.Mercury.UI\`, AIB = `_Modding\decompiled\Amplitude.Mercury.AI.Brain\`,
MOD = `_Modding\src\CurrencyMod\`.

## 0. Principais achados
- **Espião = exército com pelo menos uma unidade com `UnitTagAsAbility.Agent` (34).** Tudo o que as regras precisam dá
  para ler no postfix de `SandboxState_TurnMain.Begin`: as infiltrações num pool só
  (`Sandbox.AgentAncillary.InfiltrationInfoAllocator`) e furtividade/detecção por território e império em
  `Sandbox.StealthAncillary` (`MajorEmpireTerritory.DetectionForTerritory`).
- **"Desde quando" só existe para infiltração** (`InfiltrationInfo.TurnCompleted`, ou `Total - Remaining` em
  andamento). Presença simples num território o mod precisa lembrar sozinho.
- **A IA nativa nunca usa "Vigiar cidade" (WatchCity)**: só pontua Exploit, Manipulate e Disrupt
  (AIB …\Analysis.Military\ComputeAgentStealthActions.cs:32-35). Se a interceptação exigisse WatchCity, as IAs nunca
  interceptariam: a regra tem que contar presença simples e as outras infiltrações.
- **Notificações nativas não servem** para "seus espiões interceptaram": são tipadas, registradas por reflexão no
  Firstpass e gravadas no save. Usar selo no botão de furtividade e a interface do mod.
- **Interface recomendada:** uma terceira aba "Cartas" na `AllMilitaryForcesStealthLayerWindow`, ao lado de
  "Furtividade" e "Detecção". Dois patches Harmony e assinaturas de eventos; mudar `UIToggle.State` por código não
  dispara `Switch`.

## 1. Simulação

### 1.1 O que é um espião
- Unidade com `TagAsAbilities[34]` (DATA UnitTagAsAbility.cs:42 "Agent"; tag de IA `UnitDefinition.AIUnitTags.Agent =
  0x100000`, DATA UnitDefinition.cs:36). Papel na IA: `UnitRole.AgentStealth = 25`. Enviados usam a tag `Diplomat`
  (33), outra coisa. A aba nativa "Agentes" lista exércitos com tag 33 **ou** 34
  (IO AllMilitaryForcesStealthLayerWindowSnapshot.cs:144). O mod já marca espiões assim (MOD …\Capture\TurnCapture.cs).
- Limite de agentes: `MajorEmpire.AgentHardCap` × `AgentCountingTowardHardCap` + fila (SIM MajorEmpire.cs:335-338).
- Nomes das unidades são dados: `Databases.GetDatabase<ConstructibleDefinition>()` filtrando
  `UnitDefinition.TagAsAbilities[34]`; atributos em `Interop.AI.Snapshots.Data.UnitDataPerName[nome]`.
- Furtividade de unidade: `StealthMax`, `StealthRegen`, `StealthValue`, `StealthDetection` (SIM Unit.cs:42-48),
  `HackingRadius` (:151), `SpyHuntDamageRatio` (:175). No exército vale o **mínimo** entre as unidades
  (SIM UnitCollection.cs:96-103). Invisível: `StealthAncillary.IsCollectionInvisible(exército, contra)`
  (SIM StealthAncillary.cs:1129-1140), falso contra quem tem `ShareStealthVision`.

### 1.2 Ciclo da furtividade (SIM StealthAncillary.cs)
- A captura no `TurnMain.Begin` já vê o turno novo (furtividade, contagens de infiltração, `TurnCompleted`).
- **Todo turno** (`NewTurnBegin_UpdateStealth` :586-697): sem falhas, cada unidade ganha `+StealthRegen` até o
  máximo; sob detecção, perde `GetBestStealthDetection(dono, território).DetectionForTerritory` (:633-645). Falha a
  regeneração ao saquear, roubar comércio, invadir ou com esquadrilha não furtiva (:1053-1103). Distrito com
  `ProtectAgainstStealthDetection` protege (:1083-1097).
- **Atividade furtiva:** o dono do território recebe `StealthActivityNotificationData` e
  `Territory.IsUnderStealthActivity = true` quando há exército furtivo estrangeiro **e** detecção contra ele > 0
  (:699-763).
- **Furtividade zerada** (`UpdateStealthStatus` :957-1030): perde o status invisível, avisos aos dois lados, caça ao
  espião, infiltração cancelada (SIM InfiltrationArmyAction.cs:237-243), `SimulationEvent_AgentDiscovered`
  (SIM AgentAncillary.cs:1229-1247).
- **Caça ao espião** (SIM DepartmentOfDefense.cs:8203-8257): espião revelado em território de assentamento cujo dono tem
  `HuntRevealedSpyOnSelfTerritory` leva dano `SpyHuntDamageRatio` no fim do turno e pode morrer (:6308-6380).
- **Aliados:** `MilitaryAgreements.ShareStealthOperation` (padrão da Aliança) dá
  `ShareStealthVision | ProtectAgainstDetection | StopAgentAction` (SIM DiplomaticAgreementHelper.cs:67).
  `StopAgentAction` bloqueia e cancela infiltrações nos dois sentidos.
- **Consulado "RevealAgentsLocation"** revela exércitos invisíveis do outro no seu território
  (SIM DiplomaticConsulatHelper.cs:187-189, 709-730).

### 1.3 Infiltrações
Regras comuns (SIM AgentAncillary.cs:108-158): exército invisível, de império maior, sem invadir, parado **no tile** do
distrito alvo (distrito de extensão ativo de um assentamento). Durações vêm de dados (`ArmyAction_<X>_Duration`).
Ao chegar a 0: `ArmyState.Infiltrated` e `TurnCompleted = Turn` (InfiltrationArmyAction.cs:139-180).

| Tipo | Efeito | Duração |
|---|---|---|
| WatchCity (Vigiar cidade) | visão de todos os distritos da cidade; `Settlement.WatchingCityEmpireBits` | **permanente** enquanto o exército ficar |
| ExploitDistrict | rouba produção, recurso ou alavanca de consulado | enquanto o exército ficar |
| DisruptDistrict | desliga o distrito por um tempo | tempo de efeito + recarga |
| ManipulateCity | civismo, ciência ou ordem pública da cidade | tempo de efeito |
| TrackArmy / DisorganizeArmy / ArmyDecoy | visão, desorganização, exército falso | tempo de efeito |

`IO RequestInfiltrationActionsCandidates.cs` lista alvos válidos por ação (útil para uma ferramenta "vigiar_cidade").

### 1.4 Por império: onde estão os espiões
- Exércitos agentes: `Sandbox.MajorEmpires[i].Armies` com `HasUnitTag(Agent)`; território pelo tile; invisível;
  `army.State` (`Infiltrating`/`Infiltrated`), `army.InfiltrationInfoIndex`; vigiando:
  `army.HasUnitStatus(AgentAncillary.WatchCityStatus)`.
- Todas as infiltrações: percorrer `Sandbox.AgentAncillary.InfiltrationInfoAllocator` (Capacity, GetReferenceAt,
  `PoolAllocationIndex >= 0`); campos em IO InfiltrationInfo.cs:10-38 (`ArmyEmpireIndex`, `TargetEmpireIndex`,
  `TileIndex`, `SettlementGUID`, `InfiltrationType`, contagens, `TurnCompleted`).
- Quem vigia cada cidade: `Settlement.WatchingCityEmpireBits`.
- Lado da interface (só o jogador): `GameSnapshot.PresentationData.InfiltrationInfo` (todos os impérios; filtrar pelo
  jogador), `SettlementInfo.WatchingCityEmpireBits`, `ArmyInfo.IsInvisible`/`InfiltrationInfoIndex`.

### 1.5 Detecção por território
- `MajorEmpireTerritory` por (território, império): `DetectionForTerritory` combina melhor detecção de exércitos,
  esquadrilhas e distritos do dono, mais bônus.
- Detecção hostil contra o dono C do espião em t: `GetBestStealthDetection(C, t)` (:246-262).
- Turnos até ser revelado: `GetNumberOfTurnBeforeStealthDepleted(army, t, _)` (:333-359).
- Ver um tile com unidade oculta: `VisibilityController.IsWorldPositionDetectedFor(tile, e)`.
- O jogador vê legitimamente a detecção hostil por território na camada nativa.

## 2. Interface nativa de espionagem
- **Botão:** `ControlBanner.stealthToggle` (UI ControlBanner.cs:117), filho 1 do `layerTogglesTable` (0 = poluição,
  escondido; 2 = comércio, que o `MailButton` clona). Aparece para império maior **fora da primeira era**
  (GameUtils.cs:3555-3566). Clique → estado Stealth → mostra `StealthLayerWindow` e
  `AllMilitaryForcesStealthLayerWindow` e troca para o `StealthCursor` (:1036-1043). Atalho em
  StealthLayerWindow.cs:120-132.
- **`StealthLayerWindow`:** painel pequeno de filtros (detecção contra você × sua detecção, filtros de pinos).
- **`AllMilitaryForcesStealthLayerWindow`** (GameWindow) — a "janela de espionagem":
  - cartão do território selecionado (:16);
  - **abas de modo** no alto: `statModeGroup`/`statModeSample`/`allStatModeItems` (:35-40), Furtividade e Detecção;
  - **abas de lista**: `tabsGroup`/`tabSample` (:44-49): Exércitos, Esquadrilhas, Forças militares, Agentes (a de
    Agentes mostra "quantos/limite");
  - barra de ordenação, listas (`armiesList` + clones `_SquadronsList`, `_MilitaryForcesList`, `_AgentsList`),
    `noMilitaryForceGroup`;
  - `Refresh()` (:180-193) reexibe listas e o estado vazio a cada atualização.
  - Clique numa aba: evento `Switch` → `SetStatMode` (:583-610) / `SetMode` (:612-625). `SetState` por código não
    dispara `Switch`.

### Opções para "Cartas interceptadas"
1. **Recomendada: terceira aba de modo "Cartas".** Clonar `statModeSample` em `statModeGroup` sem o componente
   `…StatModeTabItem`; painel clonado de `armiesList` (rolagem, grupo, separadores "Neste território / Outros");
   cartões reaproveitando os do MailWindow (mover os helpers para uma classe comum). Ativar: desligar as abas do jogo,
   esconder abas de lista, ordenação, listas e estado vazio. Patches: **postfix em `Refresh`** (reesconder enquanto a
   nossa aba está ativa) e **prefix em `OnPresentationShuttingDown`** (limpeza; também no `OnDestroy` para a recarga
   a quente). Botão "Ver" no cartão: `StealthCursor.SetSelectedTerritoryIndex(t)` ou centralizar a câmera.
2. Alternativa: quinta aba de lista ao lado de "Agentes" (mais frágil: mais dois postfixes).
3. Plano B: aba "Interceptadas" no correio + botão na janela de espionagem.
- **Selo:** clonar o chip do `MailButton.CreateBadge` no `stealthToggle`, contando cartas interceptadas não lidas.

## 3. Regras de interceptação propostas
- **O que:** só cartas `privada`. Ultimato (vai por enviado formal) e declaração pública não.
- **Quem:** qualquer império maior vivo C, fora remetente A e destinatário B, jogador incluso. Um interceptador por
  carta; ele lê a carta inteira; a carta nunca é entregue.
- **Aliados** com `StopAgentAction` não se interceptam.
- **Só espiões:** exércitos com a tag Agent, invisíveis, num território do império da ponta E (A ou B).

### Peso de cada espião s de C num território t de E (calculado por turno)
peso = local × missão × qualidade × cobertura × visto × tempo

| Fator | Valores |
|---|---|
| local | 1,0 região da capital de E ou território do consulado de E; 0,5 outra cidade de E; 0,25 outro território de E |
| missão | ×2,0 vigiando cidade de E (×2,5 a capital); ×1,5 explorando o consulado de E; ×1,3 outra infiltração ativa; ×1,1 infiltração em andamento; ×1,0 nenhuma |
| qualidade | (1 + 0,25 × (agentes − 1)) × (1 + 0,1 × veterania), no máximo 2 |
| cobertura | 1,0 sem detecção hostil; senão min(1, turnos até ser revelado / 4); × 1 / (1 + 0,15 × detecção de E no território) |
| visto | ×0,5 se E enxerga o tile do espião |
| tempo (opcional) | 0,6 chegou neste turno; 0,8 há 1-2 turnos; 1,0 há 3 ou mais |

- exposição[C,E] = soma dos pesos; × 0,85 se E caça espiões.
- Chance numa ponta: **p = Pmax × (1 − e^(−k × exposição))**, padrão Pmax = 0,5, k = 0,7.
- Exemplos: 1 agente não detectado na capital ≈ 0,25; vigiando a capital ≈ 0,38; vigiando a capital + explorando o
  consulado ≈ 0,46; espião num posto distante ≈ 0,08; vigiando as capitais de A e de B ≈ 0,62.
- Recalibrar depois de um dump real de detecção e furtividade.

### Momento e determinismo (recomendado)
- Risco na origem (A) guardado quando a carta é criada; risco no destino (B) calculado na primeira foto com
  `Turn >= DeliverTurn`. p(C) = 1 − (1 − p_A) × (1 − p_B). Sorteio determinístico por hash FNV-1a (guid da partida,
  id da carta, C): recarregar dá o mesmo resultado se os espiões não se moveram. Teto total ~0,75; opcional: no máximo
  1 carta por exército espião por turno.
- Resolver antes de qualquer dossiê (no `Tick`, antes do `Schedule`) e logo depois de criar carta com entrega imediata.

### Quem sabe o quê
- **Jogador:** só vê as cartas que **ele** interceptou (texto inteiro, remetente, destinatário, turno, espião e
  território; nomes só de nações que conhece). Não sabe das cartas dele interceptadas por outros: "Entregue" nunca vira
  "Lida". Pode ver uma "cobertura" qualitativa por nação, só com os próprios espiões.
- **IA que interceptou:** seção "CARTAS INTERCEPTADAS PELOS SEUS ESPIÕES" e "Sua rede" no dossiê.
- **IA vítima:** só "atividade de espiões estrangeiros nos territórios…" (o `IsUnderStealthActivity` que o jogo já avisa).
- **Regras no prompt:** carta privada pode ser interceptada por espiões no seu território ou vigiando suas cidades,
  principalmente a capital; ultimato e declaração pública não; carta interceptada nunca chega; você só sabe o que os
  seus espiões interceptam; usar a informação pode expor a sua rede.

### Opcionais (v2)
Rota de mensageiro passando por territórios com espiões; malote diplomático (enviado no território do destino reduz o
risco); aviso de "correspondência comprometida" quando um espião que interceptou é descoberto; ferramenta
`vigiar_cidade` para a IA de linguagem (a nativa nunca vigia).

## 4. Notificações e save
- Notificações nativas: não reutilizar (tipadas, por reflexão, gravadas no save; `MarkAsRead` posta ordem na
  simulação). Usar o selo, um popup opcional e o correio.
- Notificações nativas que valem no dossiê (conhecimento legítimo): StealthActivity, revelações, caça ao espião,
  cidade/distrito/exército "por eles", alavanca de consulado. As por império somem no TurnEnd.
- Save: tudo no `IaWorld` (`DiplomaciaIA.json`); nada no estado do jogo. `IaWorld.Version` 3 com `Migrate` marcando
  as cartas antigas como já conferidas (não interceptar retroativamente). Na recarga a quente, desassinar eventos da
  simulação no `IaModule.Shutdown`.

## 5. Riscos
Layout da janela só no jogo (largura com 3 abas, área da lista); postfix do `Refresh` roda muito (só visibilidade,
`instant: true`); interceptação das IAs depende do peso de presença (a IA nativa não vigia cidades); magnitudes de
detecção a calibrar; segurar no inbox do jogador as cartas ainda não resolvidas; limitar a ~3 cartas interceptadas
inteiras por turno no dossiê.

## 6. Plano
1. `ia espionagem` (dump de agentes, infiltrações, detecção e tabela de exposição) + dumps da interface.
2. `EspionageCapture` na foto do turno.
3. `[IA]` no `.cfg`: Pmax, k, pesos, teto, orçamento por espião, popup.
4. `Letter`: `InterceptionChecked`, `InterceptedBy`, `InterceptedTurn`, `InterceptedTerritory`, `InterceptedArmy*`,
   `InterceptedVia`, `InterceptorRead`, `OriginRisks`; `IsFor` exclui interceptadas; versão 3.
5. `Diplomacia\Espionage.cs`: risco na origem, resolução, hash, orçamento, log.
6. Filtros do inbox do jogador e seções do dossiê; regras no prompt.
7. `InterceptedLettersTab.cs` com os 2 patches; helpers de cartão em classe comum; selo.
8. F10 e `ia interceptar <carta> <E>` para testar.
9. Testes: vigiar capital de IA → cartas IA↔IA na aba; espião de IA na capital do jogador → carta do jogador some
   ("Entregue" sem "Lida"); aliados nunca se interceptam; save/load com os mesmos sorteios; recarga a quente sem sobras;
   primeira era sem erro.
