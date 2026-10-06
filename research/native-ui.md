# Interface nativa do Humankind — pesquisa (2026-10-03)

Pesquisa estática no código descompilado (`_Modding\decompiled\`). Nada foi testado em jogo.
Abreviações: **UI** = `Amplitude.UI\`, **ACS** = `Assembly-CSharp\`, **FW** = `Amplitude.Framework\`.

`FontFamily` está em `Amplitude.Graphics.dll` (não descompilado); provavelmente um ScriptableObject.

## 1. Arquitetura de janelas

- `UIBehaviour : MonoBehaviour` — ciclo próprio `OnEnable → LoadIfNecessary → Load()` / `OnDisable → Unload()`. Não sobrescrever Awake/Start/OnEnable. Destruir só depois de Unloaded (desativar antes).
- `UIComponent` (exige `UITransform`) → `UIAbstractShowable` (VisibilityState, `Shown`/`Hidden`, `VisibilityChange`, `OnBeginShow`/`OnEndHide`…, animadores `showAnimator`/`hideAnimator`) → `UIContainer` (`Refresh()`, `Dirtyfy()`, `SpecificUpdate()`).
- `UIPanel : UIContainer` — Show/Hide públicos (sub-painéis).
- `UIWindow : UIContainer, IUIManagedWindow` — `PostLoad()` (IEnumerator), `PreUnload()`, `CatchInputEvent(ref InputEvent)`, `Group`. Show/Hide só pelo gerenciador.
- `GameWindow` (ACS\…\Windows\GameWindow.cs): `IsReadyForShowing()` depende de flag setada só para janelas existentes no carregamento → **janela de mod NÃO deve herdar de GameWindow** (herdar de `UIWindow` ou sobrescrever `IsReadyForShowing`).
- Gerenciador: `UIWindowsManager_Base` (UI\Amplitude.UI.Windows\UIWindowsManager_Base.cs): `GetWindow<T>()`, `TryGetWindow<T>`, `ShowWindow`, `HideWindow`, `UpdateWindowVisibility`. `InstantiateAllGroups()` (l.48) = exemplo de UI criada em código. `FindWindow` (l.297) não cacheia → janelas adicionadas depois são encontradas.
- Implementação do jogo: `WindowsManager` (ACS\Amplitude.Mercury.UI.Windows\WindowsManager.cs) — `Instance`, `GetWindowsGroup<T>()`, `AnyInGameFullScreenOpened`.
- Helpers: `WindowsUtils` (ACS\Amplitude.Mercury.UI.Helpers\WindowsUtils.cs) — `GetWindow<T>`, `ShowWindow`, `HideWindow`, `UpdateWindowVisibility`. Grupos: OutGameScreens, InGameBackground, InGamePins, InGameSelection, InGameHUD, InGameFullscreen, InGameOverlays, SystemFullscreen, SystemOverlays.
- **Registro de janela de mod:** `WindowsGroup.AddDebugWindowImplementation(UIWindow)` (privado, ACS\…\Windows\WindowsGroup.cs:68) — aumenta `windows`, seta `Group`, reparenta no `Root`, roda `DoPostLoad`.
- Comportamento dos grupos: `InGameFullscreenGroup` (uma por vez, Esc fecha, esconde banners); `InGameOverlaysGroup` (esconde tudo com menu de pausa); `InGameSelectionGroup`; `FullscreenGroup` (pilha).
- Prefabs: `UIWindowsGroupDefinition` (ScriptableObject) com `PrefabReference[] windowPrefabs` → `AssetDatabase.LoadAsset<T>(Guid)` (FW\Amplitude.Framework.Asset\AssetDatabase.cs). Cena de UI aditiva carregada em `UIManager.DoInstantiateUIScene` (ACS\Amplitude.Mercury.UI\UIManager.cs:932).
- Instâncias em runtime: `WindowsUtils.GetWindow<T>()`, `FindObjectOfType<T>()`; prefabs puros via `Resources.FindObjectsOfTypeAll<UIWindowsGroupDefinition>()` → `GetPrefab(i)`.

## 2. Criar/clonar UI em código

- `UITransform.InstantiateChild(Transform prefab, string name)` (UITransform.cs:1102), `ReserveChildren(n, prefab, nameTemplate)` (:1067), `RefreshChildren<TItem,TData>(list, bind, showHide)` (:1132), `GetChildren<T>`, `HideChildren<T>`, `DestroyChildren()`.
- `Pool<T>` (ACS\…\Helpers\Pool.cs): `Load(root, sample, …)`, `ReserveItems`, `GetOrCreateItem`, `ReleaseItem`. Exemplo: AllMilitaryForcesWindow_MilitaryForcesList.cs:47/82/145.
- Padrão "amostra escondida clonada": AllMilitaryForcesWindow.cs:210-230 (`tabsGroup.ReserveChildren(3, tabSample.transform)`).
- Reparentar controles vivos é suportado (MenuBanner.cs:341; `UITransform.OnTransformParentChanged`).
- Precedente debug: `UIDebugUIManager.Build`, `UIDebugWindow.ApplyConfiguration` → `WindowsGroup.AddDebugWindow` (prefabs podem não existir no build de release; usar só como padrão de código).
- Layout: `UITransformExtension` (`AnchorToBorder`, `Center`, `SetTop/Bottom/Left/RightBorder`, `GetWantedHeight`).

## 3. Componentes

- **UITransform**: `VisibleSelf`, `VisibleGlobally`, `InteractiveSelf`, `X/Y/Width/Height`, `LocalRect`, âncoras `LeftAnchor/RightAnchor/TopAnchor/BottomAnchor` (`UIBorderAnchor` com `SetAttach/SetPercent/SetMargin/SetOffset`), `LayerIdentifierSelf`, `StyleController`, eventos `PositionOrSizeChange`, `VisibleGloballyChange`. Espaço padronizado 1920×1080.
- **UILabel**: `Text` (`%chave` = localização), `FontFamily`, `FontFace`, `FontSize`, `Color`, `Alignment`, `WordWrap`, `AutoAdjustWidth/Height`, `ForceCaps`, `InterpreteRichText` (`<c=RRGGBB>`, `<b>`), `InterpreteSymbols`.
- **Imagens**: `UIImage` (`Texture` = `UITexture`, 9-slice), `UISquircleImage` (raios, `Fill`, `StrokeWidth`). `UIRenderingManager.RegisterTexture(Texture)` → Guid → textura própria do mod como `UITexture`.
- **Widgets do jogo** (ACS\Amplitude.Mercury.UI\): `SquircleBackgroundWidget` (painel padrão; borda dourada `(1,.875,.584)`, fundo `(.184,.184,.251,.85)`), `SquircleButton`/`SquircleToggle`, `TabsWidget`, `HorizontalSeparator`, `HorizontalTextSeparator`, `FramedText`, `LinearGauge`, `CircularGauge`.
- **Controles**: `UIControl` (MouseEnter/Leave, KeyDown, FocusGain/Loss), `UIButton.LeftClick`, `UIToggle` (`State`, `Switch`, `ClickDoesntSwitchOff` = rádio/abas), `UISlider` (`Min/Max/Step/CurrentValue`, `SetCurrentValue`, `ValueChange`), `UITextField` (`ReplaceText`, `MaximumChars`, `TextChange/TextValidation/TextCancellation`), `UIScrollView`, `UIDropList`.
- **Tooltip**: `tooltip.Message = "…"` ou `tooltip.Bind(TooltipUtils.TitleAndDescription, new TitleAndDescription{…})` (ACS\…\Helpers\TooltipUtils.cs:102).
- **Layout**: `UITable1D` (Direction, Margin, Spacing, AutoResize), `UITable2D`, `UIRectMask`.
- **Z-order**: ordem depth-first da hierarquia; `LayerIdentifierSelf`; `UIView` renderiza camadas listadas; janela sob a raiz de um grupo herda a camada do grupo.

## 4. Janelas-modelo

- **SystemSettingsScreen** + **SettingItemsPool**: prefabs de toggle, slider, droplist, botão; `GetOrCreateSettingSlider(parent)`, `GetOrCreateSettingToggle(parent)`. `SettingSlider.Bind(min,max,step,ShowText,Action<float>)` (l.106). Melhor fonte de slider/toggle/campo de texto nativos — **clonar**, não pegar do pool.
- **AllMilitaryForcesWindow** / **AllSettlementsWindow**: título, fechar, abas (`tabSample` = `UILabel` + `UIToggle` + `UITooltip` + tags First/Last), lista com `UIScrollView`. Abertas pela ManagementBanner.
- **TechnologyWindow**: popup com `UITable1D` rolável.
- **MessageModalWindow.ShowMessage(new Message{Title, Description, Buttons})** — usável direto para confirmações (estilos `"SquircleBackground_Button"`, `"SquircleBackground_Button_Red"`).
- **EmpireScreen**: exemplo de tela cheia.

## 5. Estilos, fontes, texturas

- **O sistema de estilos é o que dá o visual nativo**: `UIStyleController.styleNames` em cada componente; `UIStyle` em `UIStylesheet`s via `UIStyleManager` (`AllStyles`, `TryFindStyle`). Aplicar: `StyleController.SetStyleNames(...)` (após Load; antes, escrever `styleNames` por reflexão). Tags: `StyleAdditionalTagListeners` (First, Last, Highlighted).
- Fontes: copiar `label.FontFamily` de um rótulo nativo (ou `Resources.FindObjectsOfTypeAll<FontFamily>()`).
- Materiais: `UIMaterialId`, `UIRenderingManager.Instance.MaterialCollection`. Imagens de dados: `Utils.DataUtils.GetImage(mapper, key)`. Qualquer asset: `AssetDatabase.TryLoadAsset<T>(path, provider)`.

## 6. Integração com a barra do topo

- `ManagementBanner` (ACS\Amplitude.Mercury.UI\ManagementBanner.cs, grupo InGameOverlays): toggles `allSettlementsToggle`, `allMilitaryForcesToggle`, `technologyToggle`. Padrão (l.443-466): `toggle.Switch` → `WindowsUtils.UpdateWindowVisibility<T>(state)`; sincronia em `UpdateTogglesState()` (l.468-514).
- Botão nativo do mod: postfix em `ManagementBanner.PostLoad` → `InstantiateChild(allMilitaryForcesToggle.transform)` → limpar `Stamp`, tooltip, ícone, `Switch`, sincronizar `State`.
- Não há pilha de telas genérica; "tela cheia" = `InGameFullscreenGroup`. Atalhos via `[PresentationShortcut]`.

## Rota recomendada (híbrida)

1. Publicizar `Amplitude.UI` e `Assembly-CSharp`; referenciar `Amplitude.Graphics.dll`.
2. Casca: GameObject inativo + `UITransform` + `CentralBankWindow : UIWindow`; clonar moldura/título/fechar de AllSettlementsWindow ou TechnologyWindow; registrar com `AddDebugWindowImplementation` no `InGameOverlaysGroup` (ou `InGameFullscreenGroup`); abrir/fechar só por `WindowsUtils`; Esc em `CatchInputEvent`.
3. Abas: clonar `tabSample` de AllMilitaryForcesWindow sob `UITable1D`.
4. Cartões: `SquircleBackgroundWidget` + `UILabel`s com fonte copiada + tooltip.
5. Tabela: `UIScrollView` + `UITable1D` + linha-amostra de AllMilitaryForcesWindow_MilitaryForcesList.
6. Slider/toggle/campo de texto: clonar do `SettingItemsPool`.
7. Botão na barra do topo: clonar `allMilitaryForcesToggle`.
8. Segurança: clonar de prefabs (não de janelas vivas), instanciar sob pai inativo, remover scripts acoplados antes de ativar.

**Riscos:** acoplamentos escondidos nos clones; tags de tutorial; grupos escondendo a janela; patches do jogo mudando estilos/prefabs; carregamento assíncrono (`UIBehaviourAsynchronousLoader`).

## O que só um dump em runtime revela

Grupo de cada janela e camadas; nomes/caminhos dos filhos de cada prefab; qual filho cada `[SerializeField]` aponta; `styleNames` de cada componente e a lista de estilos; `FontFamily` disponíveis e tamanhos/cores de título/corpo/números; texturas de ícones; estrutura da linha de botões da ManagementBanner e do `SettingItemsPool`; existência dos prefabs de debug no build.

**Ferramenta de dump sugerida (tecla, grava em `BepInEx\`):** inventário de grupos/janelas; dump recursivo por `UITransform` (caminho, rect, âncoras, camada, componentes, `styleNames`, propriedades de UILabel/UIImage/Squircle/toggle/slider/tooltip); mapa de `[SerializeField]` → caminho do filho; catálogos (`UIStyleManager.AllStyles`, `FontFamily`, `LayerNames`, `UIView`s); opcional: inspetor que mostra o elemento sob o mouse.
