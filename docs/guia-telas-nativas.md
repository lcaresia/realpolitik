# Guia: criando telas nativas no Humankind

Como montar janelas e botões que parecem 100% do jogo, usando o próprio framework de UI da Amplitude
(`Amplitude.UI`). Tudo aqui foi testado no Banco Central (`_Modding\src\CurrencyMod\NativeUI\`).
Pesquisa estática de apoio: `_Modding\research\native-ui.md`.

---

## 1. Kit de desenvolvimento (jogo aberto, sem reiniciar)

### Peças
| Peça | Onde | Função |
|---|---|---|
| Carregador | `src\CurrencyMod.Loader` → `BepInEx\plugins\CurrencyMod\CurrencyMod.Loader.dll` | Plugin fixo. Carrega o núcleo, recarrega quando a DLL muda e lê comandos. |
| Núcleo | `src\CurrencyMod` → `BepInEx\CurrencyModCore\CurrencyMod.dll` | Todo o mod. Fica fora de `plugins` para o BepInEx não carregar sozinho. |
| Canal de comandos | `_Modding\dev\cmd.txt` (entrada) e `_Modding\dev\out\result.txt` (saída) | Uma linha por comando; o arquivo é apagado ao ser lido. Respostas grandes vão para arquivos próprios em `dev\out\`. |

### Ciclo de trabalho
1. Editar o código do núcleo.
2. `dotnet build _Modding\src\CurrencyMod -c Release` (o build copia a DLL para `BepInEx\CurrencyModCore`).
3. O carregador percebe a DLL nova em ~1 s, chama `ModEntry.Stop()` (desfaz patches Harmony, destrói componentes, devolve o estado em JSON) e `ModEntry.Start()` com a versão nova.
4. Escrever comandos em `cmd.txt` (por exemplo, abrir a tela e tirar print) e ler o resultado.

Só mudanças no **carregador** pedem reinício do jogo.

### Comandos
| Comando | O que faz |
|---|---|
| `reload` | Força recarga do núcleo. |
| `screenshot <nome>` | Salva `dev\out\<nome>.png` (1920×1080, inclui a UI). |
| `find <texto>` | Acha rótulos (`UILabel`) cujo texto visível contém o texto; mostra caminho e chave de localização. |
| `tree [visible\|all] [raiz]` | Árvore de objetos com componentes, retângulo na tela, estilos e textos. `all` inclui amostras escondidas. |
| `inspect <caminho> [profundidade]` | Dump detalhado: propriedades úteis e **todos os campos serializados**, mostrando para qual filho cada um aponta. |
| `windows` | Lista todas as janelas (`UIWindow`), grupo e se estão abertas. |
| `show <Tipo>` / `hide <Tipo>` | Abre ou fecha uma janela nativa pelo nome da classe (ex.: `show AllSettlementsWindow`). |
| `findtype <Tipo.Completo>` | Lista todos os objetos com um componente (ex.: `Amplitude.UI.Interactables.UITextField`). Ótimo para achar doadores. |
| `texdump <caminho>` | Exporta a textura de uma imagem para PNG e mede os canais (usado para entender os ícones SDF). |
| `layout <caminho>` | Diagnóstico de um `UILayout` (carregado? ativo? visível?). |
| `styles [filtro]` | Lista os nomes de estilo da UI. |
| `nbank open\|close\|tab N\|scroll inicio\|fim` | Controla a janela nativa do Banco Central (abas 0 a 3: Câmbio, Política, Ciclo, Sua moeda). `scroll` rola a lista para conferir os cartões de baixo nas capturas. |
| `hover <caminho>` / `hover off` | Abre o tooltip nativo de um elemento como se o mouse estivesse em cima (`UITooltipManager.Instance.OnTooltipHovered`). Serve para capturar tooltips sem mexer no mouse. `hover off` fecha também o balão do mouse de verdade (`CurrentlyHoveredTooltip`): o cursor parado em cima de algo (ex.: a "Atitude" no topo da diplomacia) cobria metade da aba Crise nos prints. O jogo só abre outro quando o cursor entra num elemento de novo. |
| `click <caminho>` | O mesmo que um clique num toggle ou botão do jogo, pelo responder dele (`UIToggleResponder.TrySwitchState`, `UIButtonResponder.OnLeftClick`), sem mexer no mouse. Ex.: `click _CrisisGroup/Table/DemandsGroup/DemandsToggle` abre a seção recolhida "Exigências deles" da aba Crise (as seções são sanfona: abrir uma fecha a outra). Em botão de ação ("Exigir tudo", "Aceitar exigências"), a ação acontece de verdade. |
| `jogo detalhe industria\|estabilidade\|dinheiro [n]` | Pede ao jogo o detalhamento nativo (o mesmo do tooltip) da n-ésima cidade do jogador ou do dinheiro do império e grava as linhas como o jogo as monta. A resposta chega numa entrada própria do `result.txt`. |
| `jogo rpn [nome]` | Mostra uma fórmula de dados do jogo (`RpnDefinition`: operações, constantes, propriedades); sem nome, lista todas. Ex.: `PublicOrderGain`. |
| `trade info\|mine\|open <território>\|close\|set <território> <império> free\|toll\|block` | Bloqueio comercial: regras, incidentes, pedágios, estado da janela; lista seus territórios com rotas; abre o posto; muda uma regra. |

Caminhos aceitam sufixo único: `SecondaryToggles/Item002` em vez do caminho inteiro.

### Recarga a quente: armadilha que já resolvemos
A Unity associa `MonoBehaviour` pelo **nome do assembly**. Se toda versão recarregada se chamasse
`CurrencyMod`, o `AddComponent<T>()` usaria as classes da **primeira** versão. O carregador renomeia cada
geração com Mono.Cecil (`CurrencyMod_g{N}_{ticks}`) antes de `Assembly.Load(bytes)`.

Cada geração tem os próprios campos estáticos, que nascem zerados. Estado que um patch lê a todo momento (ex.: a
tabela de nações travadas da IA nativa) precisa passar para a geração nova **antes** dos patches dela entrarem
(`AppDomain.CurrentDomain.SetData` no `ModEntry.Stop`, leitura no começo do `Start`; ver `NativeAiLocks.Carry`). Senão,
no intervalo até o primeiro `Update` do componente, a trava fica aberta. E nunca recarregue com as nações da IA
pensando (`ia status` → "pensando agora: 0"): as chamadas em voo se perdem.

---

## 2. Como o framework de UI funciona (o essencial)

- **Hierarquia**: `UIBehaviour` (ciclo próprio `Load`/`Unload`, não usar `Awake`/`OnEnable`) → `UIComponent` (exige `UITransform`) → `UIAbstractShowable` (Show/Hide com animação) → `UIContainer` → `UIWindow`.
- **Coordenadas**: espaço padronizado de 1920×1080. `UITransform` tem `X`, `Y`, `Width`, `Height`, `VisibleSelf` e âncoras (`LeftAnchor`… com `SetAttach/SetMargin`). Extensões úteis: `SetTopBorder`, `SetLeftBorder`, `SetRightBorder` (`UITransformExtension`).
- **Layout automático**: `UITable1D` empilha filhos (horizontal ou vertical) com `spacing` e `margin`.
- **Visual nativo**: vem dos componentes já configurados no prefab (`UISquircleImage`, `SquircleBackgroundWidget`, `UILabel` com a fonte do jogo, estilos em `UIStyleController`). Por isso **clonamos peças nativas em vez de criar do zero**.
- **Janelas** ficam em grupos (`WindowsRoot/<Grupo>`): `InGameOverlays`, `InGameSelection`, `InGameFullscreen`, etc. Abrir/fechar sempre por `WindowsUtils.UpdateWindowVisibility(janela, bool)`.

---

## 3. Receita: nova janela nativa

Modelo completo: `NativeUI\NativeBankWindow.cs`.

1. **Escolher a doadora**: uma janela nativa com o esqueleto parecido (título, abas, lista…). Abrir com `show`, fotografar e rodar `tree all <janela>`.
2. **Clonar sob um pai inativo**, para nada carregar antes da hora:
   ```csharp
   var stash = new GameObject("Stash"); stash.SetActive(false);
   GameObject clone = Object.Instantiate(donor.gameObject, stash.transform);
   ```
3. **Guardar os animadores** da janela original (`showAnimator`/`hideAnimator`) e **remover os scripts específicos** dela (ex.: tudo que começa com `AllSettlementsWindow`) com `DestroyImmediate`. Manter renderizadores, layouts, `UIButton`, `UIToggle`, tooltips.
4. **Remover peças que não serão usadas** (`DestroyImmediate`) e manter as "amostras" (`_XxxSample`) para clonar linhas.
5. **Adicionar a classe da janela** (`clone.AddComponent<MinhaJanela>()`, herdando de `UIWindow`, **nunca de `GameWindow`**, porque `GameWindow` só abre para janelas existentes no carregamento) e repassar os animadores.
6. **Registrar no grupo**: `WindowsManager.Instance.GetWindowsGroup<InGameSelectionGroup>().AddDebugWindowImplementation(janela)`. Isso reparenta, ativa e chama `PostLoad`.
7. **Ligar eventos no `PostLoad`** (botão fechar, abas) e tratar Esc em `CatchInputEvent` (`inputEvent.IsExitEvent()`).
8. **Atualizar conteúdo** num `Update()` com intervalo (ex.: 0,5 s) quando `Shown`.
9. **Limpar ao recarregar ou sair da partida**: tirar a janela do array `windows` do grupo, `SetActive(false)` e `Destroy`. Fazer isso em `ModEntry.Stop()` e num patch de `ControlBanner.OnPresentationShuttingDown`.

### Janela aberta por clique no mapa (visão de comércio)
Exemplo: `NativeUI\TradePostWindow.cs` (Posto Comercial).
- O clique na visão de comércio não fazia nada (`TradeViewCursor.OnClick` vazio): um postfix lê
  `Presentation.PresentationCursorController.CurrentHighlightedPosition.ToTileIndex()`, converte em território com
  `Snapshots.GameSnapshot.PresentationData.GetTerritoryIndexAt(tile)` e confere o dono em `TerritoryInfo.Data[t].EmpireIndex`.
- Grupo `InGameOverlaysGroup` (ver armadilhas), abertura adiada até `LoadingState == Loaded`, fechamento automático
  quando o cursor deixa de ser `TradeViewCursor`.
- Esconde o painel nativo do mesmo lugar ("Comércio Internacional") enquanto está aberta e iguala a altura a ele.
- Layout aprovado pelo usuário (bom padrão para listas de decisão):
  - cartão do topo só com informação (título curto + 3 chips), sem botão;
  - uma linha por item relevante (aqui, só quem usa o posto), título = nome, estado colorido no 1º chip, botão de uma palavra;
  - ação em massa ("aplicar a todos") num cartão **no fim** da lista, depois das escolhas que ela copia;
  - estado vazio explicado num cartão ("Nenhum império estrangeiro usa este posto").
- Reaproveita os utilitários de cartão do Banco Central (`SetLabel`, `SetChip`, `HideOutputs`, `SetButtonText`… são `internal static` em `NativeBankWindow`).

### Tela cheia (grupo das telas cheias)
Exemplos: `Diplomacia\UI\MailScreen.cs` (Correio Diplomático) e `Diplomacia\UI\CouncilScreen.cs` (Conselho), que
reaproveita do correio `StripSettings`, `PrepareList`, `AddBody`, `SetBody` e `FitTitle`.
- **Doadora:** `SystemSettingsScreen` (Configurações), com coluna da esquerda (título, descrição, toggles de seção e
  botão de voltar) e painel central com desfoque.
  - Tirar os painéis de configuração (`Center/GroupsContainer`), o `Center/Pool` e os scripts `Setting*` /
    `*SettingsGroup` (`StripSettings`).
  - Os toggles que sobram viram as seções.
  - Para um segundo painel (o "Escrever"), instanciar uma cópia do `Center`.
- **Listas:** vêm da janela de cidades, clonada no mesmo stash (`StripGameScripts` + `RemoveUnusedParts`).
- **Registro:** `GetWindowsGroup<InGameFullscreenGroup>().AddDebugWindowImplementation(tela)`. O grupo esconde o HUD,
  fecha no ESC e mostra uma tela cheia por vez.
- **O que fica por cima:** a barra de controle (canto de baixo à esquerda, até x≈370, a partir de y≈913), o fim de
  turno (embaixo à direita) e o brasão do império (canto de cima à esquerda).
  - Subir o botão de voltar para y = 836.
  - Levar título e descrição para o painel central.
- **Remoção** (recarga, saída da partida), senão o HUD some para sempre, porque o grupo acha que há tela aberta:
  - esconder com `WindowsUtils.HideWindow(tela, instant: true)`;
  - zerar `lastOpenedWindow` se for ela e tirá-la do array `windows`;
  - `UIInteractivityManager.Instance.SetFocus()` e `WindowsManager.Instance.Dirtyfy()`.

  Ver `MailScreen.DestroyWindow`.

### Tela do sistema (menu principal e pausa)
Exemplo: `Diplomacia\UI\ProvidersScreen.cs` (tela Diplomacia IA) e `ProvidersButtons.cs` (botões de entrada).
- **Fora da partida não existe a janela de cidades** (doadora dos cartões). Os doadores que existem nos dois lugares
  ficam nos grupos do sistema: `WindowsRoot/SystemFullscreen` (Configurações, saves, `MessageModalWindow`) e
  `WindowsRoot/OutGameScreens` (menu principal, perfil...). `windows` lista tudo.
- **Doadora:** `SystemSettingsScreen`, registrada no `SystemFullscreenGroup` (o mesmo dela). Esse grupo existe no
  menu principal e na partida, e o ESC fecha a última tela aberta (`FullscreenGroup.CatchInputEvent`).
- **Linhas nativas:** antes do `StripSettings` apagar o `Center/Pool`, tirar dele as amostras e guardar sob um pai
  escondido:
  - `SettingDropListsPool/Item002`: linha com lista suspensa (`DropList`, um `UIDropList`);
  - `SettingButtonsPool/SettingButton`: linha com botão (`Button`, 310 × 58).
  O `StripSettings` tira os scripts `Setting*`; a linha fica só com o visual nativo.
- **Lista suspensa sem o script do jogo:**
  ```csharp
  drop.Configure((item, entry) => item.GetComponent<UILabel>().Text = ((Choice)entry).Text, item => { });
  drop.Bind(choices);                  // qualquer IEnumerable
  drop.SelectIndex(i, silent: true);   // sem disparar o evento
  drop.SelectionChange += (list, index) => ...;
  ```
  A lista aberta precisa passar por cima das linhas de baixo: `row.SetAsLastSibling()` no `DropDownToggle.Switch`.
  Para alargar além dos 310 px, mudar `Width`/`X` do `DropList` e somar a diferença no `CurrentItem` e no `Popup`.
  Para abrir num teste: `UpdatePopupVisibility(true, instant: true)`.
- **Texto corrido:** clone da `Left/ScreenDescription` (quebra de linha, alinhado em cima).
- **Campo de texto:** o das anotações do save (`LoadSavesScreen/.../SaveNotesInputField`, existe fora da partida).
  - Senha: `PasswordChar = '*'`.
  - Enter valida: `actionOnReturn = UITextFieldKeyAction.Validate` (o doador vem com outra ação).
  - Colar com Ctrl+V já funciona no campo; um botão "Colar" (`GUIUtility.systemCopyBuffer`) é mais óbvio.
- **Menu principal escondido enquanto a tela está aberta:** o `OutGameScreensGroup` faz isso para as telas
  "externas" (Configurações, saves, tutoriais) e devolve o menu ao fechar. Pôr a nossa na lista:
  `outGame.externalScreens.Add(tela)` e ligar o `VisibilityChange` dela ao `ExternalScreen_VisibilityChange` do grupo
  (por reflexão, ver armadilhas). Tirar os dois na remoção.
- **Botão de voltar:** os mesmos textos das Configurações, `%SystemSettingMainMenuButtonTitle` ("Menu principal") e
  `%SystemSettingPauseMenuButtonTitle` ("Menu do jogo"); o `UILabel` traduz chaves com `%`.
- **Botão no menu principal:** clone de `MainMenuScreen/Buttons/MyProfileButton` inserido na tabela `Buttons` e
  `UITable1D.ArrangeChildren()`.
- **Botão no menu de pausa:** clone do `PauseMenuModalWindow.settingsButton`, logo abaixo dele. Ao clicar, igual às
  Configurações: `leftContentShowable.Hide(instant: true)` e, ao fechar a tela, `Show()`.
- **Na partida, sempre por cima do menu de pausa:** aberta direto (comando de teste), a tela fica por baixo do HUD
  (brasão, barra do topo, barra de controle). O menu de pausa esconde o HUD; se ele não estiver aberto, abrir antes.
### Clonar uma peça de outra janela
Ver `CloneStripped(...)`: instanciar sob pai inativo, remover o script dono (ex.: `SliderOptionItem`), reparentar
no destino e ligar `VisibleSelf`. Para clonar dentro da própria janela, `UITransform.InstantiateChild(amostra, nome)`.

---

## 4. Catálogo de doadores (testados)

| Peça | Caminho | Observações |
|---|---|---|
| Janela lateral com título, fechar, abas e lista | `InGameSelection/AllSettlementsWindow` | Base do Banco Central. Header (`UILabel` + `CloseButton`), faixa de seção (`_CultureAndWondersPanel/CultureAndWondersTitle`), abas (`TabsTable/_TabSample`), lista (`_SettlementsList/Scrollview/Viewport/SettlementItemsTable/_SettlementItemSample`). |
| Cartão com título, chips e botão | `…/_SettlementItemSample` | `Table/Top/TitleGroup/Title`, chips em `Table/Top/StatsTable/{PopCount,Fortification,ExtensionsCount}`, botão pequeno `Table/Top/LiberateButton`, linha de produção `Table/Top/_Outputs` (com ícones de comida, indústria, ciência, dinheiro…). |
| Controle deslizante | `InGameOverlays/InGameOptionsWindow/Content/OptionItemsPool/SliderOptionItem` | Remover `SliderOptionItem`. `UISlider` em `Slider` (`Min/Max/Step`, `SetCurrentValue(v, force, silent)`, `ValueChange`). Item tem 952 px: encolher (`FitOptionItem`). |
| Campo de texto | `…/ItemsTable/TextFieldOptionItem` (opções da partida) | Remover `TextFieldOptionItem`. **O doador só aceita dígitos**: zerar `whiteList`. `MaximumChars` é só leitura: usar o campo `maximumChars`. Eventos `TextChange`/`TextValidation`; preencher com `ReplaceText`. |
| Outros campos de texto | `findtype Amplitude.UI.Interactables.UITextField` | Renomear cidade, religião, exército, chat, saves. |
| Botão redondo pequeno da barra inferior | `InGameOverlays/ControlBanner/ToggleGroups/SecondaryToggles/Item002` ("Comercializar") | `ControlBannerLayerToggle`: `Toggle`, `Tooltip`, `SetIcon(UITexture)`. Clonado em `NativeBankButton.cs`. |
| Confirmação simples | `MessageModalWindow.ShowMessage(...)` | Usável direto, sem clonar. |
| Caixa fosca (painel com desfoque) | `InGameSelection/DiplomaticScreen/PanelsGroup/_RelationsPanel/_DefaultGroup/_MyRelations/Scrollview` | Remover `UIScrollView` e o `Viewport`; reposicionar também os filhos (`Blur`) para acompanhar o tamanho. |
| Título serifado de seção | `…/_MyRelations/Label` ("Minhas Relações") | Vem esmaecido; para título dourado usar `Color = (1, 0.875, 0.584)`. |
| Rótulo em versalete pequeno | `DiplomaticScreen/_NegociationGroup/_MyMoral/Label` | Bom para nomes de métricas e cabeçalhos de coluna. |
| Barra de comparação | `DiplomaticScreen/_NegociationGroup/_MyMoral/Gauge` | Preenchimento = filho `ValueGroup` (252 px úteis); vem ancorado a 50%: soltar e definir a largura (`Place(ValueGroup, 2, 2, largura, 18)`). Esconder `LockGroup`. |
| Valor numérico com ícone de dinheiro | `DiplomaticScreen/_NegociationGroup/MoneyGroup/Labels/Stock` | Ajusta a largura ao texto: desligar `AutoAdjustWidth` para o alinhamento funcionar. |
| Barra de ordenação | `AllSettlementsWindow/_SettlementsList/SortersTable` | Botões-ícone (`UIToggle` + `Picto` + `Blip` de seleção) e `DescendingToggle`. `RemoveUnusedParts(root, keepSorters: N)` mantém os N primeiros. Trocar o `Picto.Texture` por ícone SDF próprio e **zerar a cor** (`Color.white`): cada doador vinha tingido com a cor do seu recurso. Clicar no já escolhido desliga: re-sincronizar os estados. Usada no Câmbio do Banco Central. |
| Respiro em cartão solto (fora da lista) | `OverviewCard/Table` (`UITable1D`) | `margin = 8` dá espaço em cima e embaixo; o `UIParentResizer` aumenta o cartão sozinho. |
| Botão de escolha (segmentado) | `DiplomaticScreen/.../_TradePanel/.../TradeTableItem/PurchaseToggle` | `UIToggle` com fundo arredondado; ligado fica dourado. Só existe quando o outro império vende algo (achar via `GetComponentsInChildren<TradeTableItem>(true)`). Desligar `AutoAdjustWidth` do rótulo e reposicionar. Usado no bloco "Seus Postos Comerciais" (`DiplomacyTradePolicyPanel.cs`). |
| Caixa e título da aba Comércio | `_TradePanel/Content/MySide/{Background, MyResourcesLabel, MyStrategicsLabel}` | Caixa arredondada, título serifado "Seus Recursos" e versalete pequeno. |
| Frase em texto rico | `…/_DefaultGroup/Content/Label` | Ajusta a altura sozinha: desligar `AutoAdjustHeight` ao fixar posição. |
| **Campo de texto multilinha** | `LoadSavesScreen/Content/Table/SaveGameHeader/SaveNotesInputField` (anotações do save) | Fundo escuro arredondado + máscara + rótulo RubikLight 15. O doador fica **inativo**: `SetActive(true)` no clone. Para várias linhas: `field.multiline = true; field.OnMultilineChanged(false, true);` (liga quebra de linha, altura automática do rótulo e Enter = nova linha; a rolagem vertical na máscara já vem pronta). Ajustar `maximumChars`, `whiteList = ""`, `BlackList`, `InstructionText` e a `Height` do item. Usado no Correio Diplomático. |
| Texto corrido dentro de um cartão | clone de `Table/Top/TitleGroup/Title` como filho de `Table` | Trocar a fonte pela dos chips (`FontFamily` do `StatsTable/PopCount`), `WordWrap`/`AutoAdjustHeight` ligados, `AutoAdjustWidth` desligado, alinhamento em cima à esquerda. **Zerar `ResizeWeight`** (ver armadilhas). O cartão cresce sozinho. Ver `MailScreen.AddBody`. |
| Texto corrido sob os chips (aba Ciclo) | clone de `Table/Top/TitleGroup/Title` como filho do próprio `Top` | Mesma troca de fonte e quebra de linha. Âncoras: esquerda e direita presas (`new UIBorderAnchor(true, 0, 8, 0)` e `(true, 1, 10, 0)`), topo preso em 78 (40 sem chips), base e pivô soltos. Depois de trocar o texto, `AdjustSizesIfNecessary()` (só mede com o rótulo visível) e `HideOutputs(top, 78 + altura + 10)`. Ver `NativeBankWindow.FillCycleCard`. Glifos que a fonte tem: `→ × · − ▲ ▼ ~`. |
| Segundo botão no cartão | `UITransform.InstantiateChild(LiberateButton, "Outro")` no `Top` | Posicionar com `SetTopBorder(8)` e `SetRightBorder(10 + larguraDoPrimeiro + 6)`. Ver `MailScreen.SetSecondButton`. |
| Linha nativa com lista suspensa / com botão (fora da partida também) | `SystemFullscreen/SystemSettingsScreen/Center/Pool/SettingDropListsPool/Item002` e `.../SettingButtonsPool/SettingButton` | Ver "Tela do sistema". 952 × 58, controle de 310 px à direita. |
| Botão grande do menu principal | `OutGameScreens/MainMenuScreen/Buttons/MyProfileButton` | `UIButton` com rótulo; a tabela `Buttons` reorganiza. |
| Botão do menu de pausa | `InGameOverlays/PauseMenuModalWindow/LeftContentShowable/Buttons/SettingsButton` | 306 × 40. || Selo numérico num botão da barra | chip `…/_SettlementItemSample/Table/Top/StatsTable/PopCount` da janela de cidades original | Remover `Picto`, margens 5/5, fonte 13, fundo (`UISquircleImage`) avermelhado, `Place(chip, 32, -6, 22, 20)` dentro do botão. Ver `NativeUIKit.CreateBadge` (usado no correio e no Banco Central). |

---

### Painel dentro de uma tela existente (sem janela nova)
Exemplo: `NativeUI\DiplomacyEconomyPanel.cs` (painel "Economia" na aba Relações da diplomacia).
- Um componente do mod verifica a cada 0,5 s se a tela está aberta (`WindowsUtils.GetWindow<T>().Shown`), monta o painel uma vez e o atualiza.
- O painel é filho de um grupo da própria tela (ex.: `_DefaultGroup` da aba Relações), então aparece e some junto com a aba.
- Posicionamento livre com `NativeUIKit.Place(alvo, esquerda, topo, largura, altura)`: solta as âncoras, zera o pivô e posiciona relativo ao pai. Converter coordenadas de tela com `GlobalRect` do pai.
- Dados da tela: o império selecionado na diplomacia está em `Snapshots.DiplomaticCursorSnapshot.PresentationData.OtherEmpireIndex`.

### Aba nova numa tela de abas fixas (aba "sobreposta")
Exemplo: `Diplomacia\UI\CorrespondenceTab.cs` (aba Cartas da diplomacia). As abas da `DiplomaticScreen` são um enum e
campos no código; não dá para registrar uma aba de verdade. A aba nossa finge ser a Relações:
- **Aba:**
  - clonar `TabItem_Relation` dentro de `_NegociationGroup/TabsTable`, tirar o script `DiplomaticScreen_TabItem` e
    inserir antes do espaçador `Right`;
  - as abas têm 192 px, então encolher os espaçadores `Left`/`Right` para caber (`FitTabs`) e devolver a largura
    original ao remover;
  - o `DiplomaticNotificationBlip` da aba recebe um ícone próprio;
  - **cantos:** o fundo de cada aba é um `SquircleBackgroundWidget`. A primeira tem `cornerRadiusTopLeft = 20`, a
    última `cornerRadiusTopRight = 20` e as do meio são retas. Um clone herda o formato da doadora (a "Cartas" saiu
    com a ponta esquerda, e a "Crise" ficou com a ponta direita no meio da fileira, como o usuário notou). A aba nova
    na ponta fica com o canto direito, e a última visível do jogo perde o dela: `ShapeTabs`, por meio das
    propriedades `CornerRadiusTopLeft`/`TopRight`, que redesenham. Os valores originais voltam na limpeza.
- **Painel:**
  - montar sob o stash: título (clone de `_MyRelations/Label`, dourado), lista e campos;
  - chamar `PrepareList` antes de posicionar;
  - só então reparentar em `PanelsGroup` (1176 × 900).
- **Ativar:**
  - com `InternalCall = true`, chamar o `SetCurrentMode(Relations)` privado, para a aba do jogo sempre ser válida;
  - esconder o `relationsPanel` e fazer `negociationGroup.SetCurrentMode(None)`;
  - acender a aba nossa, mostrar o painel e `AnimateBackgroundHeight(altura)`.
- **Patch em `DiplomaticScreen.SetCurrentMode`:**
  - **prefix:** desativa a aba nossa, a não ser que seja a nossa chamada ou `isSwitchingOtherEmpire`, quando o jogador
    só trocou de nação;
  - **postfix:** reafirma a aba nossa se ela continua ativa.
- **Fechar a tela (ESC):** a aba volta inativa. Ao reabrir, o jogo abre em Relações como sempre.
- **Testes:**
  - `ia cartas E#` abre direto na aba;
  - `jogo interacao aba relacoes|crise|…` clica nas abas do jogo;
  - `jogo interacao diplomacia E#` troca de nação;
  - `jogo interacao estado` mostra a aba e o modo.

### Aba de modo na janela de espionagem (cantos pelo estilo)
Exemplo: `Diplomacia\UI\InterceptedLettersTab.cs` (aba Cartas ao lado de Furtividade e Detecção).
- **Onde:** a janela (`InGameOverlays/AllMilitaryForcesStealthLayerWindow`) é uma tabela vertical:
  - cabeçalho;
  - território selecionado;
  - `_ArmyFilters`, com `StatModeGroup/StatModeTabsTable` e `ListingModeAndSorters`;
  - as listas;
  - `Padding`.

  Ela cresce com o que está visível.
- **Aba:** `InstantiateChild(_StatModeTabSample)` na `StatModeTabsTable`, **com** o componente
  `AllMilitaryForcesStealthLayerWindow_StatModeTabItem`. As abas têm `ResizeWeight = 1` e dividem a largura sozinhas.
- **Cantos:** aqui vêm do estilo, com as marcas First/Last (`StyleAdditionalTagListeners`). Mexer no
  `SquircleBackgroundWidget` não adianta, porque o estilo reaplica. Use a API do jogo:
  - `RefreshPosition(false, true)` na nossa aba (a última);
  - `RefreshPosition(false, false)` na que era a última;
  - na limpeza, devolver.
- **Evento:** o `Switch` da aba fica ambíguo com o Publicizer (evento e campo com o mesmo nome). Ouça o `toggle.Switch`
  do componente.
- **Ativa:** esconde `ListingModeAndSorters`, as quatro listas (`UpdateVisibility(false, instant: true)`) e o
  `noMilitaryForceGroup`, e mostra o painel posto logo depois de `_ArmyFilters`. Também:
  - **postfix em `Refresh`:** a janela reexibe tudo a cada atualização;
  - **prefix em `SetStatMode`:** clique numa aba do jogo; o prefix acende de volta a do modo atual (`restoreNative`),
    porque, se o clique foi na mesma, o jogo não mexe nos estados;
  - **prefix em `OnPresentationShuttingDown`:** limpeza.
- **Altura:** o painel acompanha o conteúdo (`listTable.Height`) até um máximo. A barra de rolagem só aparece quando
  passa; cabendo, ela fica como um risco na borda.
- **Selo:** no `ControlBanner.stealthToggle`, com `NativeUIKit.CreateBadge`.

`NativeUI\NativeUIKit.cs` reúne os utilitários: `Clone`, `Place`, `SetText`, `Align`, `SetVisible`, `Dispose`,
`CreateBadge`/`SetBadge` (selo numérico) e `BlockGameShortcuts`.

## 5. Ícones (formato SDF do jogo)

- Os pictogramas nativos são SVG convertidos em **campo de distância** num atlas: textura **Alpha8**, borda em alfa 0,5, transição de ~±6,5 px num ícone de 64 px, material `DistanceField`. Por isso ficam nítidos em qualquer tamanho e herdam a cor do estilo.
- `NativeUI\SdfIcons.cs` gera ícones assim a partir de formas (círculo, caixa, triângulo, união e subtração). O templo do Banco Central é o exemplo.
- Registrar a textura: `guid = UIRenderingManager.Instance.RegisterTexture(tex)` e `new UITexture(guid, UITextureFlags.AlphaStraight, UITextureColorFormat.Srgb, tex)`. Desregistrar ao descarregar.

## 6. Tooltips nativos

```csharp
var alvo = new TitleAndDescription { Title = "Banco Central", Description = "..." };
tooltip.Bind(TooltipUtils.TitleAndDescription, alvo); // alterar alvo.Title/Description atualiza o texto
```

**Regra do projeto (pedido do usuário): todo número, chip e botão tem ajuda ao passar o mouse** — título curto +
uma ou duas frases, sem exagero. Utilitários:
- `NativeBankWindow.Tip(pai, "caminho", título, descrição)`: para chips/botões de cartão, que já trazem `UITooltip`.
  Só religa quando o texto muda (senão o balão pisca a cada atualização de 0,5 s).
- `NativeUIKit.Tip(alvo, título, descrição)`: para qualquer peça; se ela não tem `UITooltip` (rótulos, barras),
  adiciona o componente nativo.

---

## 7. Armadilhas encontradas (e como resolver)

| Sintoma | Causa | Solução |
|---|---|---|
| Itens de uma tabela não se reorganizam | O `UITable1D` vem **desligado** no prefab (posições fixas do editor) | `layout.enabled = true` e depois `ArrangeChildren()`. |
| `ArrangeChildren()` não faz nada | Só roda com a janela visível (durante a animação de abrir ainda não está) | Chamar a cada atualização, não só quando o texto muda. |
| Chips com texto sobreposto ou encostando na borda | Chips têm largura fixa e margem esquerda reservada para o ícone; estimar largura pelo nº de letras falha | `label.Margins = (10,10,…)`, `label.AutoAdjustWidth = true` e chamar `label.AdjustSizesIfNecessary()` + `layout.ArrangeChildren()` a cada atualização (a medição só ocorre com o rótulo visível). |
| `NullReferenceException` montando a janela | `UIComponent.UITransform` só existe depois do `Load` | Usar `GetComponent<UITransform>()` antes do carregamento. |
| Erro "não é possível alterar modificadores ao substituir" | Publicizer tornou públicos métodos que sobrescrevemos | `<DoNotPublicize Include="Amplitude.UI:Amplitude.UI.Windows.UIWindow.PostLoad" />` (idem `PreUnload`). |
| Campo de texto apaga o que você digita | Lista branca do doador | `whiteList = string.Empty`. |
| Peça esticada ao mover | Âncora de cima e de baixo presas ao mesmo tempo | Soltar uma (`BottomAnchor.SetAttach(false)`) e definir `Height`. |
| Diminuir altura de um cartão desalinha o conteúdo | Filhos ancorados em baixo/pivô para a altura antiga | Para cada filho: soltar `BottomAnchor` e `PivotYAnchor`, `SetTopBorder(margem)` e `Height` fixa; depois mudar só a altura do `Top` (a tabela e o `UIParentResizer` acompanham). Ver `HideOutputs`/`PinToTop`. |
| "?" no lugar de um símbolo | A fonte do jogo não tem o caractere (ex.: ₹, ₽, ℳ) | Usar letras (Rs, Rb, Mk). |
| Aba/controle clonado aparece esmaecido e não clica | O doador estava desabilitado no momento do clone (ex.: aba "Postos Avançados" sem postos) | `UITransform.InteractiveSelf = true` no clone. |
| Clone herda marcações do tutorial | `Stamp` copiado | `Stamp.ClearTags()` quando carregado. |
| Tooltips errados ("Todas as Cidades…", população) | Os `UITooltip` do prefab doador vêm com classe/alvo serializados | No `PostLoad`, `Unbind(preserveTooltipClass: false)` em todos (menos o fechar) **antes** de clonar linhas da amostra; depois `Bind(TooltipUtils.TitleAndDescription, …)` nos que interessam. |
| Botão não fica à direita / estica | `X` não é a borda esquerda e as âncoras recalculam | Soltar `LeftAnchor` e `BottomAnchor`, `SetTopBorder`/`SetRightBorder`, e só então `Width`/`Height`. |
| Alinhamento de rótulo não faz efeito | Rótulo com `AutoAdjustWidth`/`AutoAdjustHeight` ligado redimensiona a caixa ao texto | Desligar o ajuste automático e fixar a largura. |
| Item de opção sai pela borda da janela | Itens de 952 px com rótulo colado à esquerda | Copiar `X`/`Width` da amostra de cartão e dar margem ao rótulo (`FitOptionItem`). |
| Atalhos do jogo disparam ao digitar | O campo clonado (anotações do save) só bloqueia caracteres (`blockedEvents`), não as teclas físicas | `NativeUIKit.BlockGameShortcuts(campo)` (`KeyboardOnly`, igual aos campos da partida). Ver a seção "Campos de texto não podem disparar atalhos". |
| Lista clonada fica com 16 px de altura e vazia | Instanciada direto na tela viva, ela se dimensiona antes de ser configurada; depois o `PrepareList` a devolve para (0, 0) | Montar sob o stash inativo, chamar `PrepareList` **antes** do `Place` e só então reparentar. |
| Rolagem para em 540 px | `UIScrollView.autoAdjustHeight` com `maxHeight = 540` (padrão da lista de cidades) | `autoAdjustHeight = false` e `maxHeight = altura` (ver `MailScreen.PrepareList`). |
| Cartões clonados da amostra não aparecem | A amostra (`_SettlementItemSample`) fica escondida, e o clone nasce escondido | `VisibleSelf = true` explícito em cada cartão novo. |
| Janela "abre" (toggle aceso) mas não aparece na visão de comércio (ou religião, sociedade…) | Nessas visões o grupo `InGameSelection` inteiro fica escondido | Registrar a janela no grupo `InGameOverlaysGroup` (onde mora o painel "Comércio Internacional"). Esse grupo não mexe na visibilidade de janelas que não conhece, então a janela precisa se fechar sozinha ao sair da visão (ver `TradePostWindow.Update`). |
| `UpdateWindowVisibility(janela, true)` é ignorado logo após criar a janela | `Loaded` fica verdadeiro antes do `PostLoad` terminar; o gerenciador só mostra com `LoadingState == Loaded` | Guardar um "abrir pendente" e abrir no `Update` quando `LoadingState == Amplitude.UI.Windows.LoadingState.Loaded`. |
| Título do cartão encosta no botão da direita | Título e botão dividem a mesma linha (~460 px de janela) | Títulos curtos (nome do império, "Pedágio: R$ 6/recurso") e botões de uma palavra ("Taxar", "Bloquear", "Liberar"); estado e detalhes vão nos chips. |
| Ambiguidade de evento com o Publicizer (`VisibilityChange`, `TextValidation` do responder) | O evento e o campo dele têm o mesmo nome depois de publicizados | Usar o evento do próprio componente quando existir (`UITextField.TextValidation`, que repassa ao responder atual) ou ligar por reflexão (`EventInfo.GetAddMethod(true)` + `Delegate.CreateDelegate` com o `MethodInfo` privado). Ver `ProvidersScreen.HookExternal`. |
| Enter no campo de texto clonado não faz nada | O campo cria o responder de novo ao carregar, e a assinatura feita logo depois de clonar se perde; além disso, o doador não valida no Enter | `actionOnReturn = Validate` e religar o `TextValidation` sempre que `TextFieldResponder` mudar (`ProvidersScreen.HookKeyField`). |
| Teclas por `PostMessage` não chegam fora da partida | No menu principal e no menu de pausa, nem o ESC nem o Enter mandados por mensagem chegam (nem nas Configurações nativas) | Testar o caminho do jogo direto: `jogo interacao esc` (`WindowsManager.ExitWindow`) e o `OnReturnKeyDown` do responder (`ia provedor tela enter`). |
| Texto de ajuda do campo fica no idioma antigo | `InstructionText` é montado quando a linha nasce | Reaplicar no refresh se mudou. || Botão do cartão não responde ao clique | O `LiberateButton` do doador vem com `InteractiveSelf = false` (cidade que não pode ser libertada) | `SetButtonText` já liga `InteractiveSelf = true`; vale para qualquer botão clonado. |
| Painel nativo "vaza" por baixo/pelas bordas da janela sobreposta | O desfoque da janela some nas bordas (`fadeEndSize`) | Enquanto a janela do mod estiver aberta, `VisibleSelf = false` no `UITransform` da janela nativa do mesmo lugar; devolver ao fechar e ao destruir (ver `SetTradePanelHidden`). |
| Lista mostra impérios "Desconhecido" | Impérios ainda não encontrados | Filtrar com `Snapshots.DiplomaticSnapshot.PresentationData.LocalEmpireDiplomaticSummary.RelationSummaries[i].IsKnownByOwner()`. |
| Texto acrescentado num cartão vaza por cima dos cartões seguintes | O rótulo clonado do título herda `ResizeWeight = 1`; com um filho "elástico", o `UITable1D` com `autoResize` **desliga o redimensionamento** (só um aviso no log da Unity) | `ui.ResizeWeight = 0` no clone; depois `AdjustSizesIfNecessary()` + `ArrangeChildren()` na tabela do cartão. |
| Título comprido invade o botão mesmo mudando a largura | O título está num `TitleGroup` (tabela horizontal) que estica o rótulo (`ResizeWeight = 1`) até a borda do grupo | Encolher o **grupo**: `groupUi.RightAnchor = groupUi.RightAnchor.SetOffset(espaçoDosBotões)` e `label.AutoTruncate = true`. O rótulo diminui ou corta sozinho. Ver `MailScreen.FitTitle`. |
| Janela recriada numa recarga a quente não abre pelo comando | Mesma causa do "abrir pendente" acima (a recarga recria a janela) | Em `SetOpen(true)`, se `LoadingState != Loaded`, guardar `pendingOpen` e abrir no `Update`. |
| Print pega a janela meio transparente | A animação de abrir ainda está rodando | Esperar ~1 s depois de abrir antes do `screenshot`. Com o jogo em segundo plano, os comandos demoram mais: mande abrir e fotografar no mesmo `cmd.txt`, ou espere entre um e outro. |
| "?" no começo do título ("? Marechal…") | Seta "↳" (e outros símbolos tipográficos) fora da fonte do jogo | Só letras e pontuação comum; para marcar resposta, use um chip ou a ordem dos cartões. |
| Fim da lista da tela cheia fica atrás da barra de controle | Com Banco Central, correio e conselho, a barra vai até x≈560 e entra no painel central (x = 420) | Lista do painel central com 756 px de altura (era 820): termina acima da barra. |
| Precisa mostrar um cartão novo no topo da lista | `UIScrollView.ScrollTo(alvo)` só garante que o cartão apareça (ele fica colado embaixo) | `content.Y -= alvo.GlobalRect.yMin − viewport.GlobalRect.yMin`, limitado a [−(conteúdo − viewport), 0], e `ScrollViewResponder.Refresh()`. Fazer ~0,3 s depois de preencher (layout assentado). Ver `CouncilScreen.ScrollToTop`. |
| Tela cheia abre **vazia** na primeira vez da sessão, só com um botão "Padrão" no meio | A `SystemSettingsScreen` doadora traz o estado de quando foi clonada: a lista clonada nasce escondida (com um `UIAnimatorComponent` que a mantém assim) e os botões `Center/ResetToDefaultButton` e `Center/KeyBindingClearButton` ficam visíveis | `StripSettings` destrói os dois botões; `PrepareList` força `VisibleSelf = true` na raiz da lista e destrói o `UIAnimatorComponent` dela. Vale para correio, conselho, aba Cartas e cartas interceptadas, que usam as mesmas funções. |
| Botões pequenos `-`/`+` na linha dos chips de um cartão | O cartão só tem um botão (`LiberateButton`) | `InstantiateChild(LiberateButton)` duas vezes, rótulo com `AutoAdjustWidth = false` e centralizado, `SetTopBorder(40)` (linha dos chips), `SetRightBorder` (10 e 40), 26 × 24, `InteractiveSelf = true`. Mostrar só nas linhas em que fazem sentido e **esconder o terceiro chip** nelas, senão o texto corre por baixo dos botões. Ver `TradePostWindow.PlacePriceButton`. Use `-` ASCII: é o que fica legível no botão. |
| Linha em versalete passa das bordas do cartão e entra por baixo dos botões | O rótulo clonado (`MyStrategicsLabel`) vem com `AutoAdjustWidth` ligado: ele cresce com o texto, centralizado, e ignora a largura dada no `Place` | `AutoAdjustWidth = false` antes do `Place` e texto curto (o versalete é largo: ~45 caracteres numa linha de 470 px). O que não couber vai para outra linha ou para a dica. Ver `DiplomacyTradePolicyPanel` ("Pedágio: Tl 4 por recurso (padrão)"). |
| Total de um cartão de resumo conta a mesma coisa duas vezes | Cada linha soma o seu número, e uma rota entre dois impérios aparece nas linhas dos dois | Juntar por chave (ex.: o par de impérios da rota, `TollPreview.Answer.RouteKey`) num `HashSet` e contar no fim. |

---

## 8. Checklist para a próxima tela
1. Achar a doadora (`windows`, `show`, `screenshot`, `tree all`).
2. Mapear os campos (`inspect <janela> 0`) para saber qual filho é título, botão, amostra.
3. Copiar `NativeBankWindow.cs` como ponto de partida: `Create` → `StripGameScripts` → `RemoveUnusedParts` → `Bind` → `PostLoad`.
4. Preencher conteúdo clonando amostras; conferir por `screenshot` a cada build.
5. Se precisar de botão de acesso, seguir `NativeBankButton.cs`, com ícone SDF próprio.
6. Garantir limpeza em `ModEntry.Stop()` e na saída da partida.

### Linhas próprias nos detalhamentos nativos (resumo de bônus e penalidades)
Exemplo: `NativeEffects.cs` (economia nas cidades e no dinheiro).
- Os tooltips de propriedade (indústria, estabilidade, dinheiro...) pedem ao sandbox um `RequestPropertyBreakdownEvaluation`.
  O `SimulationEvaluator` usa o `PropertyBreakdownWorker`: `Evaluate` junta as partes (`BreakdownParts`) a partir
  dos modificadores e ordena; `FillOutput` escreve as linhas com o formato do jogo.
- **Para acrescentar uma linha:** postfix em `PropertyBreakdownWorker.Evaluate(entidade, propriedade, ...)` que adiciona
  uma `PropertyBreakdownPart` com `LocalizedSourceName` (o texto da fonte), `SourceCategory = None`,
  `NumberOfSources = -1` e `Gain`. Depois, reordenar com `PropertyBreakdownPart.CompareRef`, ou `CompareRefIgnoreCategories`
  se as opções pedirem. A linha sai como "+2 [ícone] Indústria de **Fonte**".
- **Propriedades usadas pelas telas:**
  - indústria da cidade: `ProductionNetAfterAffinityBonuses`, com sub-propriedade `ProductionNet`;
  - estabilidade da cidade: `PublicOrderTarget`;
  - dinheiro do império: `MoneyNet`.
- **Valores mostrados:** mudar a linha sem mudar o total confunde. Os totais vêm de cópias:
  - `SettlementCursorSnapshot` (tela da cidade);
  - `SettlementInfo` (preenchida em `Settlement.OnBeforeSynchronization`, interface explícita);
  - `GameSnapshot.SynchronizeEmpireInfo` (barra do topo).

  Nas cópias que comparam valores para detectar mudança, troque a leitura também na comparação (transpiler);
  senão a cópia fica "suja" a cada quadro.
- **Efeitos de dados novos não dão:** os descritores são compilados (`CompiledDescriptor`) e a lista não aceita
  entradas novas.

### Janelas do mod se comportam como as do jogo (obrigatório)
O jogo decide as janelas laterais pelo cursor (cidade, tropa, diplomacia) e pelos menus da barra de controle. As
janelas do mod não entram nessa conta e ficavam abertas por cima de outras (bug visto pelo usuário). Toda janela nova
usa `NativeUI\ExclusiveWindows`:
- **Ao abrir, `BeforeOpen(janela)`:**
  - fecha as outras janelas do mod e as telas cheias;
  - volta ao cursor normal, o que fecha a tela da tropa ou da cidade;
  - fecha os menus da barra (`RequestNoneState`);
  - esconde outras janelas laterais abertas.

  O posto comercial passa `keepTradeView: true`, porque vive na visão de comércio.
- **No `Update`, `Interrupted(janela, abertaEm)`:** fecha se o cursor deixou de ser o normal, se um menu da barra
  abriu ou se outra janela lateral apareceu. Tem 0,5 s de folga para as outras terminarem de sair.
- **ESC:** `CatchInputEvent` com `IsExitEvent()` nas laterais. Nas telas cheias o grupo já fecha.
- **Teste sem teclado nem mouse:** `jogo interacao esc|tropa|cidade|fe|sociedade|comercio|diplomacia E#|aba <nome>|normal|estado`
  faz o que o jogador faria pelos mesmos caminhos do jogo (`WindowsManager.ExitWindow`, `ChangeToArmyCursor`,
  `RequestReligionState`, `ChangeToDiplomaticCursor`...) e lista as janelas abertas.
- **Resultado do teste (2026-10-04):**

  | Janela | ESC | Tropa | Cidade | Fé | Outra janela do mod |
  |---|---|---|---|---|---|
  | Banco Central | fecha | fecha | fecha | fecha | fecha |
  | Correio (tela cheia) | fecha | fecha | fecha | fecha | fecha |
  | Conselho (tela cheia) | fecha | fecha | fecha | fecha | fecha ao abrir o correio |
  | Posto comercial | fecha | — | — | — | fecha ao abrir o Banco Central |

  Na aba Cartas da diplomacia, clicar em Relações ou Crise desativa a aba, e trocar de nação a mantém.

### Campos de texto não podem disparar atalhos
Um `UITextField` só segura as teclas que estão no `blockedEvents`. Os campos da partida (renomear cidade, chat, busca de
tecnologias) usam `KeyboardOnly`. O das anotações do save (tela de fora da partida, sem atalhos) só segurava os
caracteres, e as teclas físicas disparavam os atalhos do jogo no meio do texto. Em todo campo clonado:
- `NativeUIKit.BlockGameShortcuts(campo)`;
- texto longo também com `campo.actionOnFocus = UITextFieldFocusAction.PlaceCaretAtCursor`, para o clique pôr o cursor
  em vez de selecionar tudo.

Dois cuidados a mais (revisão de 2026-10-05):
- **Atalhos do próprio mod** (F8 do Banco Central, F10 do visualizador) leem o teclado direto da Unity
  (`KeyboardShortcut.IsDown`) e não passam pelo bloqueio do campo. Confira antes `!NativeUIKit.TypingInField()`.
- **Esconder a janela não solta o foco.** Escondido, o campo continua com o foco e, com `KeyboardOnly`, engole o ESC e
  os atalhos do jogo até o próximo clique (`UIControlResponder.CatchKeyDownEvent` bloqueia mesmo sem estar
  interativo). No `SetOpen(false)` de toda janela com campo de texto, chame `NativeUIKit.ReleaseTextFocus()`.
- `jogo interacao estado` mostra o foco do teclado ("foco do teclado: …").

**Testar teclado sem mexer no PC do usuário.** `SendKeys` e `SetForegroundWindow` não chegam ao jogo (o Windows não
deixa outro processo pôr a janela na frente). Mandar a mensagem direto para a janela funciona, com o jogo em segundo
plano: `PostMessage(MainWindowHandle, WM_KEYDOWN/WM_KEYUP, vk, 1 | scan<<16)` para teclas (F8 = 0x77/0x42, ESC =
0x1B/0x01) e `WM_CHAR` (0x102) para digitar. Com `jogo foco <caminho do campo>` antes, dá para testar campo de texto,
atalhos e ESC do começo ao fim (testado em 2026-10-05: "ola" entrou no campo; F8 bloqueado no campo; foco solto ao
fechar o correio; F8 e ESC voltaram).
