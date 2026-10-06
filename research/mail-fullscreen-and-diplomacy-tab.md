# Correio em tela cheia e aba "Correspondência" na diplomacia — pesquisa (2026-10-04)

Pesquisa só de leitura, para o design `docs\design-diplomacia-ia.md` §8.1. As referências de linha foram conferidas no
código descompilado; o que só dá para confirmar no jogo está marcado **CONFERIR NO JOGO**.

Raízes: ACS = `_Modding\decompiled\Assembly-CSharp\`, AUI = `_Modding\decompiled\Amplitude.UI\`,
MOD = `_Modding\src\CurrencyMod\`, OUT = `_Modding\dev\out\` (dumps antigos de runtime).

---

## A. Correio em tela cheia

### A1. Como as telas cheias do jogo funcionam

**Quais são.** O grupo `InGameFullscreen` tem InternationalScreen, EndGameWindow, TechnologyScreen,
ReligionTenetsScreen, ScenarioEndGameScreen, EmpireScreen e CivicsScreen (OUT 222750_windows.txt:33-39). O grupo
`SystemFullscreen` tem LoadSavesScreen, SystemSettingsScreen, TutorialVideosScreen e os modais (67-75). A
DiplomaticScreen **não** é tela cheia: fica no `InGameSelection` (48).

**Criação.** Nada é criado sob demanda.
- Cada grupo é um asset `UIWindowsGroupDefinition` com `PrefabReference[] windowPrefabs`, `criticity` e
  `rootLayerIndex` (AUI Amplitude.UI.Windows\UIWindowsGroupDefinition.cs:11,21,25).
- `InstantiateAllGroups` cria uma raiz `UITransform` esticada por grupo sob `WindowsRoot`
  (AUI ...\UIWindowsManager_Base.cs:48-67).
- `DoInstantiateWindows` instancia os prefabs; depois vem `DoPostLoadWindows` (AUI ...\UIWindowsGroup.cs:204-246, 273-288).
- Criticidade 0 (fora do jogo e grupos System*) carrega no boot; 1 (grupos do jogo) quando a partida começa
  (ACS Amplitude.Mercury.UI\UIManager.cs:1103, 1114).
- Toda tela existe desde o começo, escondida. Alguns subpainéis são instanciados no PostLoad
  (EmpireScreen.cs:155-164; DiplomaticRelationsPanel.cs:112-115).

**Classes base.** `UIWindow` (AUI ...\UIWindow.cs:7) → `GameWindow` (ACS Amplitude.Mercury.UI.Windows\GameWindow.cs:11)
→ EmpireScreen, CivicsScreen etc. `GameWindow.IsReadyForShowing` espera `UIPresentationStartedFinished`
(GameWindow.cs:63-70), que só é ligado para janelas coletadas no carregamento (WindowsManager.cs:244-257, 394-414).
**A tela do mod deriva de `UIWindow`, não de `GameWindow`** (mesma regra do `guia-telas-nativas.md`).

**Abrir e fechar.**
1. `WindowsUtils.ShowWindow` / `HideWindow` / `UpdateWindowVisibility` (ACS Amplitude.Mercury.UI.Helpers\WindowsUtils.cs:48-79).
2. `UIWindowsManager_Base.ShowWindow`, que exige `window.Loaded` (UIWindowsManager_Base.cs:196-212).
3. `Group.ShowWindow` → `InternalShowWindow`, que exige `LoadingState >= Loaded` (UIWindowsGroup.cs:307-321).
4. Máquina de estados: `RequestShow` → PreShowing → `IsReadyForShowing` → Showing → animação → `OnEndShow` → Visible
   (AUI Amplitude.UI\UIAbstractShowable.cs:397-457).

**O que o `InGameFullscreenGroup` faz** (ACS Amplitude.Mercury.UI.Windows\InGameFullscreenGroup.cs):
- Uma tela cheia por vez: `ShowWindow` esconde a anterior (`lastOpenedWindow`) e cancela popups (83-92); `HideWindow`
  limpa (94-101).
- ESC: `CatchInputEvent` esconde a `lastOpenedWindow` (103-115).
- Esconde tudo sozinho quando o cursor é `BaseDiplomaticCursor`, `StealthCursor` ou `TradeViewCursor`, em sequências
  de câmera ou fora da partida (`Refresh`, 54-81).
- Publica `AnyInGameFullScreenOpened` (`FillSharedData`, 127-131).

**ESC.** `WindowsManager.PostLoad` assina `Presentation.Generic.ExitWindow` (WindowsManager.cs:194), que só gera o
Escape se nenhum campo de texto tiver o teclado (341-349). Grupos de cima para baixo (UIWindowsManager_Base.cs:269-279),
janelas visíveis da última para a primeira (UIWindowsGroup.cs:174-189). Se ninguém consome, abre o menu de pausa
(WindowsManager.cs:88-101).

**Convivência com o HUD.**
- Ordem de desenho: InGameBackground, InGamePins, InGameSelection, **InGameFullscreen**, **InGameOverlays**,
  OutGameScreens, SystemFullscreen, SystemOverlays. A tela cheia fica acima das janelas laterais e **abaixo do HUD**.
- Com `AnyInGameFullScreenOpened` ligado:
  - o InGameSelectionGroup esconde todas as janelas dele, inclusive o Banco Central e o correio lateral
    (InGameSelectionGroup.cs:67-71);
  - o InGameOverlaysGroup esconde ManagementBanner, NotificationBanner, DiplomaticBanner e EraStarCompletionBanner
    (InGameOverlaysGroup.cs:240-265, 304-310, 398-421, 453-473);
  - a ControlBanner continua visível, com o estilo "OverFullscreen" (ControlBanner.cs:243-246, 453-467). **O botão do
    envelope continua clicável por cima da tela.**
  - EndTurnWindow, ToastBanner (coluna esquerda), TutorialWindow e MouseMarkers continuam visíveis.
- Tela opaca entra em `UIRenderPipelineAsset.ScreensThatHideBackground` no `OnEndShow` e sai no `OnBeginHide`: o mundo
  3D para de ser redesenhado (EmpireScreen.cs:215-223; UIRenderPipelineAsset.cs:57-61,106).

### A2. Doador

O grupo dá o comportamento (esconder HUD, ESC, uma por vez); o doador só dá a aparência.

| Candidato | Encaixe | Observações |
|---|---|---|
| **SystemSettingsScreen** (SystemFullscreen, `UIWindow`) | Exato: navegação vertical à esquerda, título, lista grande com rolagem, botão de voltar, listas suspensas | Filhos genéricos, fácil de limpar, sempre carregado, sem DLC. Parece o menu de opções, não as telas desenhadas do jogo. |
| InternationalScreen (InGameFullscreen) | Cabeçalho, fechar, abas **em cima**, lista + detalhe | Melhor aparência de jogo; muitos scripts próprios (suborno, votos); hierarquia desconhecida. |
| LoadSavesScreen | Lista grande ordenável, linhas expansíveis, campo de texto; sem navegação | Bom doador de peças (cabeçalho ordenável, linhas expansíveis). |
| EmpireScreen, CivicsScreen, TechnologyScreen, ReligionTenetsScreen | Arte e grafos, sem modelo de lista | Peças úteis: busca da TechnologyScreen, lista da esquerda da ReligionTenetsScreen. |

**Recomendação: SystemSettingsScreen como esqueleto**, decidida depois de 5 minutos de olhada no jogo (passo 0); se
o visual de opções incomodar, InternationalScreen.

Hierarquia pelo código (ACS Amplitude.Mercury.UI\SystemSettingsScreen.cs:19-53):
- `settingsPanelTogglesTable`: um filho por seção (`IUIToggle` + `UILabel`), coletados no PostLoad (94-99); o toggle
  i abre `settingsPanels[i]` (160-174).
- `settingsPanels`: lista de `SettingsPanel` (`UIPanel`) com `settingItemsTable`, `settingItemsScrollView`,
  `settingItemsGradient`, `resetToDefaultButton` (SettingsPanel.cs:15-30) — a lista grande com rolagem.
- `settingItemsPool`: modelos de toggle, slider, lista suspensa, botão etc. (SettingItemsPool.cs:9-64).
- `closeMenuButton` (+ título e dica), `settingsTitle`; apagar `keyBindingUnderline`, `keyBindingClearButton` e os
  toggles de mouse/controle/toque.

Scripts a remover: `SystemSettingsScreen`, todas as subclasses de `SettingsPanel` (`*SettingsGroup`),
`SettingItemsPool`, `SettingToggle`, `SettingSlider`, `SettingDropList`, `SettingButton`, `SettingCaptionedImage`,
`Setting*Binder`, `SettingDropListEntry`. Para doadores grandes, **lista branca** em vez da lista negra do
`NativeBankWindow.StripGameScripts` (3 passadas, `DestroyImmediate`):
```csharp
// manter: widgets do framework e widgets visuais simples do jogo
ns.StartsWith("Amplitude.UI") || typeof(Amplitude.Mercury.UI.UIWidget).IsAssignableFrom(t)   // UIWidget.cs:11
|| t == typeof(MercuryTooltip) || t == typeof(WindowMouseCatcher) || t == typeof(UIParentResizer)
|| t == typeof(UILabelHeightAdjuster) || t == typeof(DynamicColorMixerComponent)
|| t == typeof(DropListItemText) || t == typeof(SquircleButton) || t == typeof(SquircleToggle)
```

Peças de conteúdo:
- **Cartões e lista:** `AllSettlementsWindow/_SettlementsList/Scrollview` (UIScrollView + Viewport +
  `SettlementItemsTable` + `Scrollbar/Thumb`) com `_SettlementItemSample` e o `AddBody` do MailWindow.
- **Escrever:** `SaveNotesInputField` da LoadSaves (já funciona no MailWindow).
- **Filtro por nação:** discos de `InGameOverlays/DiplomaticBanner/MovableBanner/DiplomaticTable/_ItemSample`
  (sempre vivos na partida). Tirar `DiplomaticPortrait` (o clique abre a diplomacia, DiplomaticPortrait.cs:308-312),
  manter `UIButtonDisk` e `MercuryTooltip`; preencher como o `Bind` faz: `Picto.Texture =
  Utils.GameUtils.GetEmpireIcon(i)` e `Background.Color = Utils.GameUtils.GetEmpireColor(i, EmpireColor.Primary)`
  (DiplomaticPortrait.cs:79-84).
- **Filtro por período:** `UIDropList` clonado (do pool das configurações ou de
  `InGameOptionsWindow/…/DropListOptionItem`, tirando `DropListOptionItem`): `Configure(bind, unbind)` antes de
  `Bind(lista)`, depois `SelectIndex` e o evento `SelectionChange` (AUI Amplitude.UI.Interactables\UIDropList.cs:98-138,176-196).
- **Botões:** ações da diplomacia `…/_DefaultGroup/Content/_ActionItems/Item001` (tirar `DiplomaticScreen_ActionItem`).

**Atalho (fase 0):** manter o clone atual do MailWindow, mas registrá-lo no `InGameFullscreenGroup` em vez do
`InGameSelectionGroup`, e aumentá-lo e centralizá-lo. Isso já dá o comportamento de tela cheia; o doador novo vira
uma melhoria visual depois.

### A3. Receita no estilo do kit

0. **Olhar no jogo:** `tree all SystemSettingsScreen`, `show SystemSettingsScreen` + `screenshot`,
   `tree all InGameFullscreen/InternationalScreen`; anotar os caminhos (toggles, Scrollview/Table do primeiro painel,
   título, voltar) e `inspect` numa lista suspensa aberta (`LayerIdentifierSelf`).
1. `MailScreen : UIWindow` em MOD Diplomacia\UI\, copiando `MailWindow.Create` (MailWindow.cs:98-134), com:
   - doador `Resources.FindObjectsOfTypeAll<SystemSettingsScreen>().First(w => w.gameObject.scene.IsValid())`;
   - grupo `WindowsManager.Instance.GetWindowsGroup<InGameFullscreenGroup>()` com `IsReady`;
   - clone sob um esconderijo inativo; ler `showAnimator`/`hideAnimator` do doador **antes** de limpar;
   - limpeza por lista branca, apagar o que não usa;
   - `AddComponent<MailScreen>()`, devolver os dois animadores;
   - registrar com `group.AddDebugWindowImplementation(window)` (WindowsGroup.cs:68-78).
2. Raiz cobrindo a tela toda (0,0, 1920×1080, ou as quatro âncoras).
3. `PostLoad`:
   - seis toggles de navegação (Novas, A responder, Lidas, Respondidas, Enviadas, Comunicados públicos) com
     `ClickDoesntSwitchOff = true` (clonar com `InstantiateChild` se faltar);
   - botão de fechar e `ClearInheritedTooltips`;
   - barra de filtros (discos + período) **fora** do viewport da rolagem, senão o `UIRectMask` corta a lista suspensa;
   - lista de cartões e painel de leitura/escrita.
   - Layout sugerido: navegação x≈380-640, lista ≈660-1240, leitura/escrita ≈1260-1900, y entre 150 e 930 (livre do
     EmpireBanner, dos avisos e da ControlBanner à esquerda e do EndTurn embaixo à direita).
4. `Open()`:
   - adiar enquanto `LoadingState != Loaded` (o `pendingOpen`);
   - se o cursor for `BaseDiplomaticCursor`, `TradeViewCursor` ou `StealthCursor`, chamar antes
     `Presentation.PresentationCursorController.ChangeToDefaultCursor()` (PresentationCursorController.cs:665), senão o
     `Refresh` do grupo esconde a tela na hora;
   - `WindowsUtils.GetWindow<ControlBanner>()?.RequestNoneState()` (ControlBanner.cs:182), senão os popups de
     sociedade, religião e internacional ficam por cima;
   - `WindowsUtils.ShowWindow(this)`.
5. Reagir ao `VisibilityChange` em vez de sobrescrever `OnEndShow`/`OnBeginHide` (com o Publicizer, sobrescrever
   exige `<DoNotPublicize Include="Amplitude.UI:Amplitude.UI.UIAbstractShowable.OnEndShow"/>` etc.). Visible: entrar em
   `ScreensThatHideBackground` **só se o fundo for opaco**. Hiding: sair dela e soltar o teclado com
   `UIInteractivityManager.Instance.SetFocus()` (como LoadSavesScreen.cs:363-369).
6. `CatchInputEvent`: no ESC, fechar primeiro uma lista suspensa aberta; senão deixar o grupo fechar a tela.
7. Atualizar a cada 0,5 s enquanto `Shown`; refazer linhas só quando as cartas mudarem (quantidade, último id, lidas)
   e reaproveitar linhas. Com centenas de cartas: as 50 mais novas + "Mostrar mais"; prévia de 3 linhas na lista e o
   texto inteiro no painel de leitura.
8. `MailButton` passa a abrir e fechar o `MailScreen` (a sincronia de estado em MailButton.cs:63-67 continua valendo).
9. `DestroyWindow` (chamado do `ModEntry.Stop` e do patch `ControlBanner.OnPresentationShuttingDown`) — **crítico**:
   ```csharp
   if (w.Shown) WindowsUtils.HideWindow(w, instant: true);
   var g = (InGameFullscreenGroup)w.Group;
   if (g.lastOpenedWindow == w) g.lastOpenedWindow = null;   // InGameFullscreenGroup.cs:18
   UIRenderPipelineAsset.ScreensThatHideBackground.Remove(w);
   UIInteractivityManager.Instance.SetFocus();                  // solta o teclado
   g.windows = g.windows.Where(x => x != w).ToArray();
   w.gameObject.SetActive(false); Destroy(w.gameObject); WindowsManager.Instance.Dirtyfy();
   ```
10. Dados: "A responder" e "Respondidas" precisam do vínculo de resposta. `Letter` tem `ExpectsReply` mas não tem
    referência à carta original (MOD Diplomacia\IaWorld.cs:172-197): criar `int InReplyTo = -1`, preencher pelo botão
    Responder e passar por `SendPlayerLetter` (IaModule.cs:677).

### A4. Armadilhas
- **Tela cheia "presa" (crítico).** Se a janela for destruída aberta, `lastOpenedWindow` continua apontando para ela,
  `AnyInGameFullScreenOpened` fica ligado para sempre e o jogo esconde os banners e **todas** as janelas do
  InGameSelection (cidade, exércitos, diplomacia). O mesmo vale para `ScreensThatHideBackground` (mundo congelado).
  O passo 9 trata os dois.
- **Carregamento:** `ShowWindow` é ignorado até `Loaded` e `LoadingState == Loaded`. Manter o `pendingOpen`.
- **Animações:** os animadores da raiz vêm junto no clone. Layout não se arruma escondido: `ArrangeChildren` a cada
  atualização. Esperar ~1 s antes de tirar print.
- **Teclado:** um `UITextField` focado prende o teclado (AUI …\UITextFieldResponder.cs:40-61) e bloqueia atalhos e o
  ExitWindow (WindowsManager.cs:341-349). Esconder ou destruir sem `SetFocus()` deixa ESC e atalhos mortos. ESC
  digitando só cancela a edição; o segundo ESC fecha a tela.
- **Ordem de desenho:** o HUD fica por cima (EmpireBanner, ControlBanner 14,913 356×153, EndTurn, avisos,
  tutorial). Desenhar a tela em volta deles.
- **Fechamento automático:** sequências de câmera, cursores de diplomacia/comércio/furtividade e o menu de pausa
  escondem a tela. Abrir a diplomacia a partir do correio fecha o correio (esperado).
- **Lista suspensa de tela System:** pode trazer `LayerIdentifierSelf` de camada de sistema. **CONFERIR NO JOGO**.

---

## B. Aba "Correspondência" na tela de diplomacia

### B1. Como as abas funcionam
- **Enum fixo:** `DiplomaticScreenMode` = Relations, Trade, Treaties, Crisis, Consulat, WarResolution, None
  (ACS Amplitude.Mercury.UI\DiplomaticScreenMode.cs:6-16).
- **Painéis em campos fixos:** `relationsPanel`, `tradePanel`, `treatiesPanel`, `crisisPanel`, `consulatPanel`
  (DiplomaticScreen.cs:141-153), todos `IDiplomaticPanel` (IDiplomaticPanel.cs:7-30); `allPanels` tem o tamanho do
  enum (7) e é preenchido por índice no `Load` (DiplomaticScreen.cs:301-306).
- **Abas em campos fixos:** `DiplomaticScreen_NegociationGroup` tem `relationsTab`, `tradeTab`, `treatiesTab`,
  `consulatTab`, `crisisTab` (NegociationGroup.cs:214-228), ligados aos modos no `OnPresentationStarted` (232-240).
- Só texto e dica vêm de dados: `EnumUIMappers.TryGetTitleAndDescription(mode)`; habilitada pelo
  `ComputeTabsFailures` do painel (TabItem.cs:73-95).
- **Hierarquia no jogo** (OUT 213923…:35-57): `_NegociationGroup/TabsTable` (`UITable1D`) com `Left`,
  `TabItem_Relation`, `TabItem_Trade`, `TabItem_Treaties`, `TabItem_Crisis`, (`TabItem_Consulat`, escondida sem a
  DLC) e `Right`. Cada aba: `[DiplomaticScreen_TabItem, UIToggle, UITooltip, UIRectMask]`, filhos `Background`,
  `Label/Underline`, `Bottom`, 192×37. Painéis em `PanelsGroup` (372,134 1176×900): `Background` (desfoque,
  WindowMouseCatcher, altura animada por aba), `_RelationsPanel`, `_TradePanel`...
- **Troca de aba:** o `Switch` do toggle chama `onSwitch(mode)` (TabItem.cs:97-102) → `NegociationGroup_TabSwitch`
  (DiplomaticScreen.cs:759-762) → `SetCurrentMode` (614-651): esconde o painel antigo na hora, mostra o novo,
  `AnimateBackgroundHeight(PreferredHeight)` (753-757), acerta os cinco toggles (NegociationGroup.cs:252-264), cursor
  de comércio na aba Comércio, avisa o tutorial. Mudar `UIToggle.State` por código **não** dispara `Switch`
  (AUI …\UIToggle.cs:34-48).
- **Troca automática:** `Refresh` (401-447) chama `SetCurrentMode` quando o modo é None, quando a aba atual deixa de
  ser interativa, quando aparece uma preferência "forte" (agravo, exigência ou extrator selecionado) ou **ao trocar
  de império** (`isSwitchingOtherEmpire`, linha 171; lógica 550-612).
- **Império selecionado:** `Snapshots.DiplomaticCursorSnapshot.PresentationData.OtherEmpireIndex` (linhas 404, 628,
  666; já usado em DiplomacyEconomyPanel.cs:162).

### B2. Caminho recomendado: aba sobreposta
- **Descartado: aumentar o enum ou `allPanels`.** O `DiplomaticNotificationsController` dimensiona arrays pelo enum e
  indexa por `(int)mode` (244-247, 254-270; arrays em 25-34 e 60-72): um modo 7 estoura em `ComputeNotificationType` e
  `SetCurrentMode`, e `IsTabInteractive` loga erro (NegociationGroup.cs:369-394). Seriam uns dez patches.
- **Recomendado:** clonar `TabItem_Relation` dentro de `TabsTable` (sem o `DiplomaticScreen_TabItem`) e criar um
  painel nosso em `PanelsGroup`. O jogo nunca percorre os filhos de `TabsTable` nem de `PanelsGroup`; só usa os campos
  serializados e o `allPanels` (RefreshTabs 430-448, AnyInteractiveTab 343-367). Não existe troca de aba por teclado
  ou controle (o controle move um cursor virtual que clica no toggle como qualquer outro).
- **Um único patch Harmony**, em `DiplomaticScreen.SetCurrentMode` (privado; o Publicizer deixa compilar o `nameof`):
  ```csharp
  [HarmonyPatch(typeof(DiplomaticScreen), nameof(DiplomaticScreen.SetCurrentMode))]
  static class CorrSetModePatch {
    static void Prefix(DiplomaticScreen __instance, out bool __state) {
      __state = CorrTab.Active && !CorrTab.InternalCall && __instance.isSwitchingOtherEmpire; // trocou de império: fica na nossa
      if (CorrTab.Active && !CorrTab.InternalCall && !__state) CorrTab.Deactivate();          // clicou numa aba do jogo
    }
    static void Postfix(DiplomaticScreen __instance, bool __state) { if (__state) CorrTab.Reassert(__instance); }
  }
  ```
- **Ativar** (pelo `Switch(true)` do nosso toggle): `InternalCall = true` e `screen.SetCurrentMode(Relations)` (aba
  sempre interativa: o `Refresh` nunca força troca, e o cursor de comércio/agravo é desfeito); depois `Active = true` e
  `Reassert`: `relationsPanel.Hide(instant: true)`, `negociationGroup.SetCurrentMode(None)` (desliga os toggles do
  jogo), liga o nosso toggle, mostra o nosso painel e `screen.AnimateBackgroundHeight(AlturaDoPainel, false)` — o fundo
  tem que cobrir o painel inteiro, senão o clique atravessa para o mapa.
- **Desativar:** esconder o painel, desligar o toggle, soltar o teclado; o `SetCurrentMode` do jogo mostra a aba clicada.
- **Trocar de império com a aba ativa:** a animação de troca chama `SetCurrentMode(Relations)` com
  `isSwitchingOtherEmpire`; o postfix esconde Relações no mesmo quadro (sem piscar) e o conteúdo é refeito para o novo
  `OtherEmpireIndex`.

### B3. Plano
1. **Olhar no jogo:** `tree all TabItem_Relation` (filho `DiplomaticNotificationBlip`), `inspect _NegociationGroup/TabsTable 1`
   (`Left`/`Right` elásticos?), `tree all HistoryGroup` (possível doador de linha de conversa,
   DiplomaticScreen_HistoryPanel.cs:14-27,127-179).
2. `CorrespondenceTab : MonoBehaviour` em MOD NativeUI\, no molde do DiplomacyEconomyPanel: verificar a cada 0,25-0,5 s se
   a DiplomaticScreen está `Shown`, pular partida online, construir uma vez, limpar sobras pelo prefixo do nome (como
   DiplomacyTradePolicyPanel.cs:82-89).
3. **Aba:** `NativeUIKit.Clone(TabItem_Relation, TabsTable, "CurrencyMod_CorrTab", "DiplomaticScreen_TabItem")`, antes de
   `Right` (`SetSiblingIndex`); `State = false`, `ClickDoesntSwitchOff = true`, `InteractiveSelf = true`; texto
   ("Cartas" ou "Correspondência", **CONFERIR** se cabe em 192 px); dica; `Stamp.ClearTags()` depois de carregar;
   `TabsTable.ArrangeChildren()`. O `DiplomaticNotificationBlip` clonado vira marcador de não lidas
   (`UpdateVisibility(naoLidas > 0)`, ícone `SdfIcons.Letter`).
4. **Painel:** raiz `UITransform` em `PanelsGroup` (0,0, 1176 × ≈780, acima do NotificationBanner em y ≥ 988), escondida
   no começo. Cabeçalho clonado de `_RelationsPanel/_DefaultGroup/Content/Label`; caixa fosca de
   `_MyRelations/Scrollview` sem o Viewport; conversa com a rolagem e o cartão do AllSettlementsWindow + `AddBody`
   (cartas deles à esquerda, minhas à direita); discos de `_MyRelations/.../Table/Item00X` sem `DiplomaticPortrait`;
   separadores "Turno N" de `_MyRelations/.../Table/Label`; rolar para o fim com `UIScrollView.ResetVertically(toEnd: true)`
   (UIScrollView.cs:496-509). Escrever: campo da LoadSaves (multilinha, ~150 px), tipo com 3 × `_TabSample` do
   AllSettlements, "Enviar" de `_ActionItems/Item001` sem `DiplomaticScreen_ActionItem`, contador de palavras de
   `_MyMoral/Label`.
5. **Ligações:** `Switch` do toggle → Ativar; `screen.VisibilityChange` (Hiding/Invisible) → Desativar; o patch acima
   entra pelo `harmony.PatchAll` do Plugin.cs:74-75.
6. **Conteúdo:** cartas recebidas com `From == outro` + enviadas com `To == outro` (declarações públicas dele: decidir);
   ordem crescente por `DeliverTurn`/`SentTurn` e `Id`; refazer só quando mudar; ao abrir, `MarkReadByPlayer`; enviar
   com `IaModule.SendPlayerLetter(outro, …)` (IaModule.cs:677); escrever desabilitado se o império não for IA ou não
   for conhecido (mesma regra do `KnownNations`, MailWindow.cs:1008-1017).
7. **Código comum com A:** a conversa vira um componente reutilizável (pai, retângulo, império); a tela cheia mostra a
   mesma conversa com o filtro de nação. "Ver na diplomacia" chamaria `ChangeToDiplomaticCursor(império)`
   (PresentationCursorController.cs:696) com a aba pendente, ativada quando a tela estiver `Shown` e `currentMode != None`.
8. **Limpeza no Stop/OnDestroy:** com a aba ativa, devolver o estado do jogo (`allPanels[(int)currentMode]?.Show(true)`,
   `negociationGroup.SetCurrentMode(currentMode)`, altura do painel), destruir os clones e rearrumar o `TabsTable`.

### B4. Riscos
- **Largura das abas:** com a DLC do Consulado são 6 × 190 px; só cabe em 1176 se `Left`/`Right` encolherem. **CONFERIR**.
- **Clique atravessando:** fundo mais baixo que o painel deixa o clique passar para o mapa.
- **Notificações:** com a nossa aba aberta o `currentMode` continua Relations, então o `Refresh` marca como lidas as
  notificações de Relações. Pequeno.
- **Recarga a quente:** o `ModEntry.Stop` destrói componentes no fim do quadro, depois de a nova geração começar
  (Plugin.cs:112-118). O "devolver o estado" da geração velha não pode desfazer a aba da nova: só devolver se o clone
  velho ainda existir.
- **Teclado:** soltar o foco ao desativar e ao esconder a tela.

### Arquivos principais
- ACS Amplitude.Mercury.UI\DiplomaticScreen.cs e DiplomaticScreen_NegociationGroup.cs
- ACS Amplitude.Mercury.UI.Windows\InGameFullscreenGroup.cs
- ACS Amplitude.Mercury.UI\SystemSettingsScreen.cs
- MOD Diplomacia\UI\MailWindow.cs
