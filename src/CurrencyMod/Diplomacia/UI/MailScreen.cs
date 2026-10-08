using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Windows;
using Amplitude.UI;
using Amplitude.UI.Animations.Scene;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using Amplitude.UI.Windows;
using CurrencyMod.Diplomacia.Capture;
using CurrencyMod.NativeUI;
using UnityEngine;
using B = CurrencyMod.NativeUI.NativeBankWindow;

namespace CurrencyMod.Diplomacia.UI
{
    /// <summary>
    /// Correio Diplomático em tela cheia (design §8.1). A moldura é um clone da tela de Configurações do jogo
    /// (coluna da esquerda com título, seções e botão de voltar; painel central com desfoque), registrado no grupo das
    /// telas cheias da partida: o jogo esconde o HUD, fecha no ESC e mostra uma tela cheia por vez. A lista de cartões
    /// vem da janela de cidades, como no Banco Central.
    ///   esquerda: Novas, A responder, Lidas, Respondidas, Enviadas, Comunicados públicos, Nações;
    ///   centro: as cartas da seção, com filtro por nação e por período;
    ///   direita: escrever (e responder, ligado à carta original).
    /// As cartas vivem em IaModule.World; esta tela só lê e chama SendPlayerLetter / TogglePlayerBlock.
    /// </summary>
    internal class MailScreen : UIWindow
    {
        private const string WindowName = "CurrencyMod_MailScreen";
        private const string SaveNotesDonor = "LoadSavesScreen/Content/Table/SaveGameHeader/SaveNotesInputField";
        private const string SliderDonor = "InGameOptionsWindow/Content/OptionItemsPool/SliderOptionItem";
        private const string Red = "E8685E";
        private const string Gold = "E3C88A";
        private const string Green = "8FD18A";

        // Geometria (tela padronizada de 1920 × 1080). O HUD que fica por cima da tela cheia: a barra de controle
        // embaixo à esquerda (até x≈370, a partir de y≈913) e o fim de turno embaixo à direita.
        private const float CenterX = 420f;
        private const float CenterWidth = 1030f;
        private const float RightX = 1470f;
        private const float RightWidth = 420f;
        private const float RightHeight = 830f;
        private const float ListWidth = 928f; // os cartões ficam 26 px dentro da lista e 34 px mais estreitos: alinham com a linha divisória
        /// <summary>A lista termina acima da barra de controle (que fica por cima da tela cheia até x≈560).</summary>
        private const float ListHeight = 756f;
        private const float ComposeListWidth = 398f;
        private const float ComposeListHeight = 720f;
        /// <summary>Cartas recebidas mais antigas que isso saem de "A responder" (ninguém responde carta de 20 turnos atrás).</summary>
        private const int AnswerWindowTurns = 20;
        private const int MaxLettersListed = 60;

        internal static MailScreen Instance;

        internal enum Section { New, ToAnswer, Read, Answered, Sent, Public, Nations }

        // Textos marcados com L.N: traduzidos com L.T (ou SectionHelpText) na hora de mostrar.
        private static readonly string[] SectionNames = { L.N("Novas"), L.N("A responder"), L.N("Lidas"), L.N("Respondidas"), L.N("Enviadas"), L.N("Comunicados públicos"), L.N("Nações") };
        private static readonly string[] SectionHelp =
        {
            L.N("Cartas que chegaram e você ainda não leu."),
            L.N("Cartas privadas e ultimatos que pedem resposta e ainda não têm a sua (até {0} turnos atrás)."),
            L.N("Todas as cartas privadas e ultimatos que você já leu."),
            L.N("Cartas que você já respondeu, com a sua resposta ligada."),
            L.N("Suas cartas e a situação de cada uma: a caminho, entregue, lida, respondida ou recusada."),
            L.N("Declarações públicas: todas as nações que conhecem quem escreveu leem."),
            L.N("Nações que você conhece: escrever para elas ou recusar as cartas privadas delas."),
        };
        private static readonly string[] TypeIds = { "privada", "ultimato", "publica" };
        private static readonly string[] TypeNames = { L.N("Carta privada"), L.N("Ultimato"), L.N("Declaração pública") };
        private static readonly int[] PeriodTurns = { 0, 5, 15, 30 };
        private static readonly string[] PeriodNames = { L.N("Período: tudo"), L.N("Últimos 5 turnos"), L.N("Últimos 15 turnos"), L.N("Últimos 30 turnos") };

        /// <summary>Ajuda da seção, traduzida (a de "A responder" leva a janela de turnos no {0}).</summary>
        private static string SectionHelpText(int index) => L.F(SectionHelp[index], AnswerWindowTurns); // SectionHelp: conjunto L.N

        private UILabel titleLabel;
        private UILabel descriptionLabel;
        private UILabel sectionTitleLabel;
        private UILabel composeTitleLabel;
        private UIButton closeButton;
        private readonly List<UIToggle> sectionToggles = new List<UIToggle>();
        private UIButton nationFilterButton;
        private UIButton periodFilterButton;
        private UITransform listTable;
        private Transform cardSample;
        private UITransform composeTable;
        private Transform composeSample;
        private Section currentSection = Section.New;
        private int nationFilter = -1;
        private int periodFilter;
        private float nextRefresh;
        private bool pendingOpen;
        private int pendingSection = -1;
        /// <summary>Cartas mostradas em "Novas" nesta visita: continuam na lista depois de marcadas como lidas.</summary>
        private readonly HashSet<int> freshShown = new HashSet<int>();

        private readonly List<LetterRow> letterRows = new List<LetterRow>();
        private readonly List<NationRow> nationRows = new List<NationRow>();
        private Transform emptyCard;

        // Escrever.
        private Transform recipientCard;
        private Transform typeCard;
        private Transform replyCard;
        private Transform sendCard;
        private UIButton recipientButton;
        private UIButton typeButton;
        private UIButton replyButton;
        private UIButton sendButton;
        private Transform subjectItem;
        private Transform bodyItem;
        private Transform demandItem;
        private Transform deadlineItem;
        private UITextField subjectField;
        private UITextField bodyField;
        private UITextField demandField;
        private UISlider deadlineSlider;
        private int composeTo = -1;
        private int composeType;
        private int replyToId = -1;
        /// <summary>"Soltar": a carta sai sem ligação (senão o módulo liga sozinho à última carta deles que pedia resposta).</summary>
        private bool replyDetached;
        private string feedback;
        private bool feedbackIsError;

        internal static bool IsOpen => Instance != null && Instance.Shown;

        private sealed class LetterRow
        {
            public Transform Card;
            public Transform Body;
            public UIButton Primary;
            public UIButton Secondary;
            public Letter Letter;
        }

        private sealed class NationRow
        {
            public Transform Card;
            public UIButton WriteButton;
            public UIButton BlockButton;
            public int Empire = -1;
        }

        // ---------------- Criação ----------------

        internal static MailScreen Create()
        {
            SystemSettingsScreen donor = Resources.FindObjectsOfTypeAll<SystemSettingsScreen>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            AllSettlementsWindow listDonor = Resources.FindObjectsOfTypeAll<AllSettlementsWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            InGameFullscreenGroup group = WindowsManager.Instance?.GetWindowsGroup<InGameFullscreenGroup>();
            if (donor == null || listDonor == null || group == null || !group.IsReady)
            {
                return null;
            }

            // Tudo é montado sob um pai inativo: nada carrega até a tela estar limpa e registrada.
            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            MailScreen window;
            try
            {
                GameObject clone = Instantiate(donor.gameObject, stash.transform);
                clone.name = WindowName;
                var donorScreen = clone.GetComponent<SystemSettingsScreen>();
                UIAnimatorComponent showAnimator = donorScreen.showAnimator;
                UIAnimatorComponent hideAnimator = donorScreen.hideAnimator;

                GameObject cities = Instantiate(listDonor.gameObject, stash.transform);
                B.StripGameScripts(cities);
                B.RemoveUnusedParts(cities.transform, keepSorters: 0);

                StripSettings(clone.transform);
                window = clone.AddComponent<MailScreen>();
                window.showAnimator = showAnimator;
                window.hideAnimator = hideAnimator;
                window.Bind(clone.transform, cities.transform.Find("_SettlementsList"));
            }
            catch
            {
                Destroy(stash);
                throw;
            }
            group.AddDebugWindowImplementation(window);
            Destroy(stash);
            Instance = window;
            Plugin.Log.LogInfo("Tela cheia do Correio Diplomático criada.");
            return window;
        }

        /// <summary>Tira da cópia os painéis de configuração, o "pool" de itens e os scripts das configurações.</summary>
        internal static void StripSettings(Transform root)
        {
            Transform groups = root.Find("Center/GroupsContainer");
            if (groups != null)
            {
                NativeUIKit.DestroyChildren(groups);
            }
            Transform pool = root.Find("Center/Pool");
            if (pool != null)
            {
                DestroyImmediate(pool.gameObject);
            }
            // Botões das configurações ("Padrão", "Desvincular"): a tela de configurações os mostra ou esconde conforme o
            // grupo aberto, e a cópia herda o estado do momento. Na primeira abertura da sessão eles ficavam à mostra.
            foreach (string name in new[] { "ResetToDefaultButton", "KeyBindingClearButton" })
            {
                Transform button = root.Find("Center/" + name);
                if (button != null)
                {
                    DestroyImmediate(button.gameObject);
                }
            }
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null)
                    {
                        continue;
                    }
                    Type type = behaviour.GetType();
                    if (type.Namespace == "Amplitude.Mercury.UI"
                        && (type.Name == "SystemSettingsScreen" || type.Name.StartsWith("Setting") || type.Name.EndsWith("SettingsGroup")))
                    {
                        DestroyImmediate(behaviour);
                    }
                }
            }
        }

        private void Bind(Transform root, Transform citiesList)
        {
            Transform left = root.Find("Left");
            titleLabel = left.Find("Title").GetComponent<UILabel>();
            descriptionLabel = left.Find("ScreenDescription").GetComponent<UILabel>();
            closeButton = left.Find("CloseMenuButton").GetComponent<UIButton>();
            // A barra de controle fica por cima da tela cheia no canto de baixo: o botão de voltar sobe.
            UITransform closeUi = closeButton.GetComponent<UITransform>();
            NativeUIKit.Place(closeButton.transform, closeUi.X, 836f, closeUi.Width, closeUi.Height);

            Transform toggles = left.Find("SettingsTogglesTable");
            var all = new List<Transform>();
            foreach (Transform child in toggles)
            {
                all.Add(child);
            }
            for (int i = 0; i < all.Count; i++)
            {
                if (i < SectionNames.Length)
                {
                    sectionToggles.Add(all[i].GetComponent<UIToggle>());
                    all[i].GetComponent<UITransform>().VisibleSelf = true;
                }
                else
                {
                    DestroyImmediate(all[i].gameObject);
                }
            }

            // Centro: lista de cartas. Direita: escrever (cópia do painel central, ainda vazio).
            Transform center = root.Find("Center");
            UITransform centerUi = center.GetComponent<UITransform>();
            Transform rightPanel = Instantiate(center.gameObject, root).transform;
            rightPanel.name = "Compose";
            NativeUIKit.Place(center, CenterX, centerUi.Y, CenterWidth, centerUi.Height);
            NativeUIKit.Place(rightPanel, RightX, 56f, RightWidth, RightHeight);

            // O brasão do império (canto de cima) fica por cima da coluna da esquerda: título e descrição vão para o
            // painel central.
            titleLabel.transform.SetParent(center, false);
            NativeUIKit.Place(titleLabel.transform, 64f, 26f, 700f, 60f);
            descriptionLabel.transform.SetParent(center, false);
            NativeUIKit.Place(descriptionLabel.transform, 64f, 84f, CenterWidth - 128f, 24f);
            sectionTitleLabel = center.Find("SettingsTitle").GetComponent<UILabel>();
            NativeUIKit.Place(sectionTitleLabel.transform, 64f, 132f, 560f, 18f);
            NativeUIKit.Place(center.Find("Divider"), 64f, 158f, CenterWidth - 128f, 1f);
            Transform container = center.Find("GroupsContainer");
            NativeUIKit.Place(container, 38f, 170f, ListWidth, ListHeight);
            Transform letters = Instantiate(citiesList.gameObject, container).transform;
            letters.name = "Letters";
            PrepareList(letters, ListWidth, ListHeight, out listTable, out cardSample);

            composeTitleLabel = rightPanel.Find("SettingsTitle").GetComponent<UILabel>();
            NativeUIKit.Place(composeTitleLabel.transform, 24f, 30f, RightWidth - 48f, 18f);
            NativeUIKit.Place(rightPanel.Find("Divider"), 24f, 56f, RightWidth - 48f, 1f);
            Transform composeContainer = rightPanel.Find("GroupsContainer");
            NativeUIKit.Place(composeContainer, -2f, 70f, ComposeListWidth + 8f, ComposeListHeight);
            Transform compose = Instantiate(citiesList.gameObject, composeContainer).transform;
            compose.name = "ComposeList";
            PrepareList(compose, ComposeListWidth + 8f, ComposeListHeight, out composeTable, out composeSample);

            // Filtros (nação e período) na linha do título da seção: botões pequenos dos cartões.
            Transform buttonDonor = cardSample.Find("Table/Top/LiberateButton");
            nationFilterButton = Instantiate(buttonDonor.gameObject, center).GetComponent<UIButton>();
            nationFilterButton.name = "NationFilter";
            periodFilterButton = Instantiate(buttonDonor.gameObject, center).GetComponent<UIButton>();
            periodFilterButton.name = "PeriodFilter";
        }

        /// <summary>
        /// Lista com rolagem da janela de cidades num tamanho novo: a tabela vertical da lista desliga (o tamanho é
        /// nosso) e o cartão modelo ganha a largura da lista, menos a barra de rolagem.
        /// </summary>
        internal static void PrepareList(Transform list, float width, float height, out UITransform table, out Transform sample)
        {
            NativeUIKit.Place(list, 0f, 0f, width, height);
            // A lista de cidades fica escondida até a janela dela abrir pela primeira vez na sessão, e a cópia herda isso
            // (lista vazia na primeira abertura): mostra sempre e tira a animação de mostrar/esconder da janela de origem.
            list.GetComponent<UITransform>().VisibleSelf = true;
            var animator = list.GetComponent<UIAnimatorComponent>();
            if (animator != null)
            {
                DestroyImmediate(animator);
            }
            var layout = list.GetComponent<Amplitude.UI.Layouts.UILayout>();
            if (layout != null)
            {
                layout.enabled = false;
            }
            Transform scroll = list.Find("Scrollview");
            NativeUIKit.Place(scroll, 0f, 0f, width, height);
            // A rolagem da lista de cidades cresce com o conteúdo até 540: aqui a altura é fixa, a da tela.
            var scrollView = scroll.GetComponent<UIScrollView>();
            if (scrollView != null)
            {
                scrollView.autoAdjustHeight = false;
                scrollView.maxHeight = height;
            }
            table = scroll.Find("Viewport/SettlementItemsTable").GetComponent<UITransform>();
            sample = table.transform.Find("_SettlementItemSample");
            sample.GetComponent<UITransform>().Width = width - 34f;
        }

        protected override IEnumerator PostLoad()
        {
            yield return base.PostLoad();
            closeButton.LeftClick += CloseButton_LeftClick;
            B.SetLabel(closeButton.transform, string.Empty, L.T("Voltar ao jogo"));
            titleLabel.Text = L.T("Correio");
            for (int i = 0; i < sectionToggles.Count; i++)
            {
                int index = i;
                UIToggle toggle = sectionToggles[i];
                toggle.ClickDoesntSwitchOff = true;
                toggle.GetComponent<UITransform>().InteractiveSelf = true;
                toggle.Switch += (source, state) =>
                {
                    if (state)
                    {
                        SelectSection((Section)index);
                    }
                };
            }
            nationFilterButton.LeftClick += b => CycleNationFilter();
            periodFilterButton.LeftClick += b =>
            {
                periodFilter = (periodFilter + 1) % PeriodTurns.Length;
                nextRefresh = 0;
            };

            ClearInheritedTooltips();
            for (int i = 0; i < sectionToggles.Count; i++)
            {
                BindTooltip(sectionToggles[i].transform, L.T(SectionNames[i]), SectionHelpText(i));
            }
            BindTooltip(closeButton.transform, L.T("Voltar ao jogo"), L.T("Fecha o correio. O ESC e o botão do envelope também fecham."));
            try
            {
                BuildCompose();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Correio: falha ao montar o painel de escrever: {ex}");
            }
            emptyCard = listTable.InstantiateChild(cardSample, "EmptyCard").transform;
            SelectSection(pendingSection >= 0 ? (Section)pendingSection : Section.New);
            pendingSection = -1;
        }

        /// <summary>Tooltips herdados das configurações e da janela de cidades: limpa todos.</summary>
        private void ClearInheritedTooltips()
        {
            foreach (UITooltip tooltip in GetComponentsInChildren<UITooltip>(true))
            {
                if (tooltip == null)
                {
                    continue;
                }
                try
                {
                    tooltip.Unbind(preserveTooltipClass: false);
                    tooltip.Message = string.Empty;
                }
                catch (Exception)
                {
                }
            }
        }

        private static void BindTooltip(Transform target, string title, string description)
        {
            UITooltip tooltip = target != null ? target.GetComponent<UITooltip>() : null;
            tooltip?.Bind(TooltipUtils.TitleAndDescription, new Amplitude.Mercury.UI.Tooltips.TitleAndDescription(title, description));
        }

        // ---------------- Escrever ----------------

        private void BuildCompose()
        {
            recipientCard = composeTable.InstantiateChild(composeSample, "RecipientCard").transform;
            recipientButton = recipientCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            recipientButton.LeftClick += b => CycleRecipient();

            typeCard = composeTable.InstantiateChild(composeSample, "TypeCard").transform;
            typeButton = typeCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            typeButton.LeftClick += b =>
            {
                composeType = (composeType + 1) % TypeIds.Length;
                feedback = null;
                nextRefresh = 0;
            };

            replyCard = composeTable.InstantiateChild(composeSample, "ReplyCard").transform;
            replyButton = replyCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            replyButton.LeftClick += b =>
            {
                replyToId = -1;
                replyDetached = true;
                nextRefresh = 0;
            };

            Transform fieldDonor = DevTools.FindByPath(SaveNotesDonor);
            if (fieldDonor != null)
            {
                subjectItem = CloneField(fieldDonor, "SubjectField", 34f, multiline: false, maxChars: 120, hint: L.T("Assunto (opcional)"));
                subjectField = subjectItem.GetComponent<UITextField>();
                demandItem = CloneField(fieldDonor, "DemandField", 34f, multiline: false, maxChars: 200, hint: L.T("O que você exige (ultimato)"));
                demandField = demandItem.GetComponent<UITextField>();
                bodyItem = CloneField(fieldDonor, "BodyField", 300f, multiline: true, maxChars: 4000, hint: L.T("Escreva sua carta como o líder do seu povo…"));
                bodyField = bodyItem.GetComponent<UITextField>();
                // TextChange é ligado em Update (EnsureTextChange), depois que o campo carrega.
            }
            else
            {
                Plugin.Log.LogWarning("Correio: doador do campo de texto não encontrado.");
            }

            Transform sliderDonor = DevTools.FindByPath(SliderDonor);
            if (sliderDonor != null)
            {
                deadlineItem = NativeUIKit.Clone(sliderDonor, composeTable.transform, "DeadlineSlider", "SliderOptionItem");
                Transform padlock = deadlineItem.Find("Padlock");
                if (padlock != null)
                {
                    DestroyImmediate(padlock.gameObject);
                }
                FitOptionItem(deadlineItem);
                deadlineSlider = deadlineItem.Find("Slider").GetComponent<UISlider>();
                deadlineSlider.Min = 1f;
                deadlineSlider.Max = 10f;
                deadlineSlider.Step = 1f;
                deadlineSlider.SetCurrentValue(3f, force: true, silent: true);
            }

            sendCard = composeTable.InstantiateChild(composeSample, "SendCard").transform;
            sendButton = sendCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            sendButton.LeftClick += b => Send();
            B.SetVisible(composeSample, false);
        }

        /// <summary>Campo de texto nativo (o das anotações do save) com a largura dos cartões do painel; opcionalmente multilinha.</summary>
        private Transform CloneField(Transform donor, string name, float height, bool multiline, int maxChars, string hint)
        {
            Transform item = NativeUIKit.Clone(donor, composeTable.transform, name);
            item.gameObject.SetActive(true); // o doador fica inativo enquanto a tela de saves está fechada
            UITransform sampleUi = composeSample.GetComponent<UITransform>();
            UITransform ui = item.GetComponent<UITransform>();
            ui.Width = sampleUi.Width;
            ui.X = sampleUi.X;
            ui.Height = height;
            UITextField field = item.GetComponent<UITextField>();
            field.maximumChars = maxChars;
            field.whiteList = string.Empty;
            field.BlackList = "<>{}";
            field.InstructionText = $"<c=F5EBE166><i>{hint}</i></c>";
            // Digitar não pode disparar atalhos do jogo; clicar no texto põe o cursor ali (não seleciona tudo).
            NativeUIKit.BlockGameShortcuts(field);
            field.actionOnFocus = UITextFieldFocusAction.PlaceCaretAtCursor;
            if (multiline)
            {
                field.multiline = true;
                field.OnMultilineChanged(false, true); // liga quebra de linha, altura automática e Enter = nova linha
            }
            NativeUIKit.Tip(item, name == "BodyField" ? L.T("Texto da carta") : name == "SubjectField" ? L.T("Assunto") : L.T("Exigência"),
                name == "BodyField" ? L.F("Enter quebra a linha. Até {0} palavras.", IaConfig.PlayerLetterMaxWords.Value)
                : name == "SubjectField" ? L.T("Uma linha que resume a carta. Opcional.")
                : L.T("O que você exige no ultimato. O prazo fica no controle abaixo."));
            return item;
        }

        private void FitOptionItem(Transform item)
        {
            UITransform sampleUi = composeSample.GetComponent<UITransform>();
            UITransform ui = item.GetComponent<UITransform>();
            ui.Width = sampleUi.Width;
            ui.X = sampleUi.X;
            UILabel label = item.GetComponent<UILabel>();
            if (label != null)
            {
                label.Margins = new RectMargins(14f, label.Margins.Right, label.Margins.Top, label.Margins.Bottom);
            }
            Transform control = item.Find("Slider");
            if (control != null)
            {
                UITransform controlUi = control.GetComponent<UITransform>();
                controlUi.Width = 170f;
                controlUi.X = sampleUi.Width - 170f;
            }
        }

        private void CycleRecipient()
        {
            List<int> nations = KnownNations(TurnCapture.Latest);
            if (nations.Count == 0)
            {
                return;
            }
            int index = nations.IndexOf(composeTo);
            composeTo = nations[(index + 1) % nations.Count];
            replyToId = -1;
            feedback = null;
            nextRefresh = 0;
        }

        private void Send()
        {
            IaModule module = IaModule.Instance;
            if (module == null || bodyField == null)
            {
                return;
            }
            string type = TypeIds[composeType];
            if (type != "publica" && composeTo < 0)
            {
                SetFeedback(L.T("Escolha para quem escrever."), true);
                return;
            }
            string text = bodyField.Text.Trim();
            if (text.Length == 0)
            {
                SetFeedback(L.T("A carta está vazia."), true);
                return;
            }
            string demand = demandField?.Text.Trim();
            if (type == "ultimato" && string.IsNullOrEmpty(demand))
            {
                SetFeedback(L.T("Um ultimato precisa de uma exigência."), true);
                return;
            }
            int deadline = deadlineSlider != null ? Mathf.RoundToInt(deadlineSlider.CurrentValue) : 3;
            int inReplyTo = type == "publica" ? -1 : replyToId >= 0 ? replyToId : replyDetached ? -2 : -1;
            string result = module.SendPlayerLetter(type == "publica" ? Letter.Everyone : composeTo, type, subjectField?.Text.Trim(), text, demand, deadline, inReplyTo: inReplyTo);
            if (result.StartsWith("ok"))
            {
                int arrives = ParseArrival(result);
                SetFeedback(arrives >= 0 ? L.F("Carta enviada. Chega no turno {0}.", arrives) : L.T("Carta enviada."), false);
                bodyField.ReplaceText(string.Empty);
                subjectField?.ReplaceText(string.Empty);
                demandField?.ReplaceText(string.Empty);
                replyToId = -1;
                replyDetached = false;
            }
            else
            {
                SetFeedback(result.Replace("erro: ", string.Empty), true);
            }
        }

        private static int ParseArrival(string result)
        {
            int index = result.LastIndexOf("turno ", StringComparison.Ordinal);
            return index >= 0 && int.TryParse(result.Substring(index + 6).Trim(), out int turn) ? turn : -1;
        }

        private void SetFeedback(string message, bool isError)
        {
            feedback = message;
            feedbackIsError = isError;
            nextRefresh = 0;
        }

        /// <summary>Endereça o painel de escrever a uma nação (Escrever, na seção Nações).</summary>
        internal void ComposeTo(int empire, string subject)
        {
            composeTo = empire;
            replyToId = -1;
            replyDetached = false;
            if (composeType == 2)
            {
                composeType = 0;
            }
            if (subjectField != null && subject != null)
            {
                subjectField.ReplaceText(subject.Length > 120 ? subject.Substring(0, 120) : subject);
            }
            feedback = null;
            nextRefresh = 0;
        }

        /// <summary>Responder: destinatário, assunto "Re:" e a ligação com a carta original.</summary>
        internal void StartReply(Letter letter)
        {
            if (letter == null)
            {
                return;
            }
            string subject = string.IsNullOrWhiteSpace(letter.Subject) ? string.Empty : (letter.Subject.StartsWith("Re:") ? letter.Subject : "Re: " + letter.Subject);
            ComposeTo(letter.From, subject);
            replyToId = letter.Id;
            nextRefresh = 0;
        }

        // ---------------- Abertura, seções e entrada ----------------

        internal static void SetOpen(bool open)
        {
            if (Instance == null)
            {
                return;
            }
            if (!open)
            {
                Instance.pendingOpen = false;
                if (Instance.Shown)
                {
                    NativeUIKit.ReleaseTextFocus();
                    WindowsUtils.HideWindow(Instance);
                }
                return;
            }
            if (Instance.LoadingState != Amplitude.UI.Windows.LoadingState.Loaded)
            {
                // Recém-criada (ou recriada numa recarga), a tela só aceita abrir depois do PostLoad.
                Instance.pendingOpen = true;
                return;
            }
            Instance.pendingOpen = false;
            // O grupo das telas cheias se esconde sozinho com cursor de diplomacia, comércio ou furtividade, e os menus
            // da barra ficariam por cima: cursor normal, menus fechados e as outras janelas do mod fechadas.
            ExclusiveWindows.BeforeOpen(Instance);
            Instance.openedAt = Time.unscaledTime;
            WindowsUtils.ShowWindow(Instance);
            Instance.nextRefresh = 0;
        }

        private float openedAt;
        private bool wasShown;

        internal void SelectSection(Section section)
        {
            if (sectionToggles.Count < SectionNames.Length || emptyCard == null)
            {
                pendingSection = (int)section; // ainda carregando
                return;
            }
            if (section != currentSection || section != Section.New)
            {
                freshShown.Clear();
            }
            currentSection = section;
            for (int i = 0; i < sectionToggles.Count; i++)
            {
                sectionToggles[i].State = i == (int)section;
            }
            if (section == Section.Nations)
            {
                SetLetterRowCount(0);
            }
            else
            {
                SetNationRowCount(0);
            }
            B.SetVisible(emptyCard, false);
            nextRefresh = 0;
        }

        private void CycleNationFilter()
        {
            List<int> nations = KnownNations(TurnCapture.Latest);
            int index = nations.IndexOf(nationFilter);
            nationFilter = index + 1 >= nations.Count ? -1 : nations[index + 1];
            nextRefresh = 0;
        }

        private void CloseButton_LeftClick(IUIButton button) => SetOpen(false);

        protected override void PreUnload()
        {
            if (closeButton != null)
            {
                closeButton.LeftClick -= CloseButton_LeftClick;
            }
            base.PreUnload();
        }

        // ---------------- Conteúdo ----------------

        private Action<Amplitude.UI.Interactables.IUITextField, string> bodyChanged;

        private void Update()
        {
            NativeUIKit.EnsureTextChange(bodyField, bodyChanged ?? (bodyChanged = (f, t) => nextRefresh = 0));
            if (wasShown && !Shown)
            {
                NativeUIKit.ReleaseTextFocus(); // o jogo escondeu a tela sem passar por SetOpen
            }
            wasShown = Shown;
            if (pendingOpen && LoadingState == Amplitude.UI.Windows.LoadingState.Loaded)
            {
                SetOpen(true);
            }
            // Menu da barra aberto (Fé, Sociedade...) ou outra coisa selecionada: fecha, como as telas do jogo.
            if (Shown && ExclusiveWindows.Interrupted(this, openedAt))
            {
                SetOpen(false);
                return;
            }
            if (!Shown || Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + 0.5f;
            try
            {
                RefreshContent();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Correio Diplomático: {ex}");
                nextRefresh = Time.unscaledTime + 5f;
            }
        }

        private void RefreshContent()
        {
            IaModule module = IaModule.Instance;
            WorldCapture capture = TurnCapture.Latest;
            if (module == null || IaModule.World == null || module.PlayerIndex < 0 || capture == null || capture.Guid != IaModule.World.GameGuid)
            {
                descriptionLabel.Text = L.T("O correio abre quando a partida estiver carregada.");
                ShowEmpty(L.T("Aguardando o turno"), L.T("Correio fechado"), null, null);
                return;
            }
            int me = module.PlayerIndex;
            int turn = module.CurrentTurn;
            int era = capture.Empire(me)?.EraIndex ?? 0;
            int delay = Delivery.TurnsForEra(era);
            FillLeft(module, me, turn, era, delay);
            FillFilters(capture);
            if (currentSection == Section.Nations)
            {
                FillNations(module, capture, me);
            }
            else
            {
                FillLetters(module, capture, me, turn);
            }
            FillCompose(module, capture, me, turn, era, delay);
        }

        private void FillLeft(IaModule module, int me, int turn, int era, int delay)
        {
            List<Letter> inbox = module.PlayerInbox();
            int unread = inbox.Count(l => !l.ReadBy.Contains(me));
            int toAnswer = inbox.Count(l => ToAnswer(module, l, turn));
            int publicUnread = inbox.Count(l => l.IsPublic && !l.ReadBy.Contains(me));
            int inTransit = module.PlayerOutbox().Count(l => l.DeliverTurn > turn);
            string courier = Delivery.CourierName(era);
            string arrival = delay == 0 ? L.F("Turno {0}. Suas cartas chegam no mesmo turno ({1}).", turn, courier)
                : delay == 1 ? L.F("Turno {0}. Suas cartas chegam em 1 turno ({1}).", turn, courier)
                : L.F("Turno {0}. Suas cartas chegam em {1} turnos ({2}).", turn, delay, courier);
            descriptionLabel.Text = arrival + (inTransit > 0 ? " " + L.F("{0} a caminho.", inTransit) : string.Empty);
            int[] counts = { unread, toAnswer, -1, -1, -1, publicUnread, -1 };
            for (int i = 0; i < sectionToggles.Count; i++)
            {
                string label = counts[i] > 0 ? $"{L.T(SectionNames[i])} ({counts[i]})" : L.T(SectionNames[i]);
                UILabel toggleLabel = sectionToggles[i].GetComponent<UILabel>();
                if (toggleLabel != null && toggleLabel.Text != label)
                {
                    toggleLabel.Text = label;
                }
            }
        }

        private void FillFilters(WorldCapture capture)
        {
            bool letters = currentSection != Section.Nations;
            B.SetVisible(nationFilterButton.transform, letters);
            B.SetVisible(periodFilterButton.transform, letters);
            if (!letters)
            {
                return;
            }
            string nation = nationFilter < 0 ? L.T("Nação: todas") : L.F("Nação: {0}", capture.Empire(nationFilter)?.Culture ?? DossierBuilder.Name(capture, nationFilter));
            PlaceHeaderButton(periodFilterButton, L.T(PeriodNames[periodFilter]), CenterWidth - 64f);
            float periodWidth = periodFilterButton.GetComponent<UITransform>().Width;
            PlaceHeaderButton(nationFilterButton, nation, CenterWidth - 64f - periodWidth - 8f);
            B.Tip(nationFilterButton.transform, string.Empty, L.T("Filtrar por nação"), L.T("Mostra só as cartas trocadas com uma nação. Clique para passar à próxima; depois da última, volta a todas."));
            B.Tip(periodFilterButton.transform, string.Empty, L.T("Filtrar por período"), L.T("Tudo, ou só as cartas dos últimos 5, 15 ou 30 turnos."));
        }

        /// <summary>Botão pequeno na linha do título da seção, alinhado pela direita em "right".</summary>
        private static void PlaceHeaderButton(UIButton button, string text, float right)
        {
            UILabel label = button.GetComponent<UILabel>();
            float width = Mathf.Ceil(text.Length * 8.5f + 28f);
            UITransform ui = button.GetComponent<UITransform>();
            if (!ui.InteractiveSelf)
            {
                ui.InteractiveSelf = true;
            }
            if (label.Text != text || Mathf.Abs(ui.Width - width) > 0.5f)
            {
                label.Text = text;
                NativeUIKit.Place(button.transform, right - width, 124f, width, 30f);
            }
        }

        private bool ToAnswer(IaModule module, Letter letter, int turn)
        {
            return !letter.IsPublic && letter.ExpectsReply && letter.DeliverTurn >= turn - AnswerWindowTurns && !module.PlayerAnswered(letter)
                && (TurnCapture.Latest?.AiEmpires().Contains(letter.From) ?? false);
        }

        private List<Letter> SectionLetters(IaModule module, int me, int turn)
        {
            IEnumerable<Letter> letters;
            switch (currentSection)
            {
                case Section.New:
                    List<Letter> inbox = module.PlayerInbox();
                    foreach (Letter letter in inbox.Where(l => !l.ReadBy.Contains(me)))
                    {
                        freshShown.Add(letter.Id);
                    }
                    letters = inbox.Where(l => freshShown.Contains(l.Id));
                    break;
                case Section.ToAnswer:
                    letters = module.PlayerInbox().Where(l => ToAnswer(module, l, turn));
                    break;
                case Section.Read:
                    letters = module.PlayerInbox().Where(l => !l.IsPublic && l.ReadBy.Contains(me));
                    break;
                case Section.Answered:
                    letters = module.PlayerInbox().Where(l => !l.IsPublic && module.PlayerAnswered(l));
                    break;
                case Section.Sent:
                    letters = module.PlayerOutbox();
                    break;
                default:
                    letters = IaModule.World.Letters.Where(l => l.IsPublic && l.DeliverTurn <= turn && (l.From == me || l.IsFor(me)));
                    break;
            }
            if (nationFilter >= 0)
            {
                letters = letters.Where(l => l.From == nationFilter || l.To == nationFilter);
            }
            if (PeriodTurns[periodFilter] > 0)
            {
                int since = turn - PeriodTurns[periodFilter];
                letters = letters.Where(l => (currentSection == Section.Sent ? l.SentTurn : l.DeliverTurn) >= since);
            }
            return currentSection == Section.Sent
                ? letters.OrderByDescending(l => l.Id).Take(MaxLettersListed).ToList()
                : letters.OrderByDescending(l => l.DeliverTurn).ThenByDescending(l => l.Id).Take(MaxLettersListed).ToList();
        }

        private void FillLetters(IaModule module, WorldCapture capture, int me, int turn)
        {
            List<Letter> letters = SectionLetters(module, me, turn);
            int count = letters.Count;
            sectionTitleLabel.Text = $"{L.T(SectionNames[(int)currentSection])} · {(count == 1 ? L.T("1 carta") : L.F("{0} cartas", count))}";
            B.SetVisible(emptyCard, false);
            if (count == 0)
            {
                SetLetterRowCount(0);
                ShowEmpty(EmptyTitle(), L.T("Nada por aqui"), L.T(SectionNames[(int)currentSection]), SectionHelpText((int)currentSection));
                return;
            }
            SetLetterRowCount(count);
            List<int> ai = capture.AiEmpires().ToList();
            for (int i = 0; i < count; i++)
            {
                Letter letter = letters[i];
                LetterRow row = letterRows[i];
                row.Letter = letter;
                Transform top = row.Card.Find("Table/Top");
                bool mine = letter.From == me;
                if (mine)
                {
                    FillSentRow(module, capture, row, top, letter, turn);
                }
                else
                {
                    FillReceivedRow(module, capture, row, top, letter, me, turn, ai);
                }
                B.HideOutputs(top);
            }
            // Ler é abrir a seção: o que aparece aqui fica lido (em "Novas" continua listado até sair dela).
            module.MarkReadByPlayer(letters.Where(l => l.From != me));
        }

        private string EmptyTitle()
        {
            switch (currentSection)
            {
                case Section.New: return L.T("Nenhuma carta nova");
                case Section.ToAnswer: return L.T("Nenhuma carta esperando resposta");
                case Section.Read: return L.T("Nenhuma carta lida ainda");
                case Section.Answered: return L.T("Você ainda não respondeu nenhuma carta");
                case Section.Sent: return L.T("Você ainda não escreveu");
                case Section.Public: return L.T("Nenhum comunicado público");
                default: return L.T("Nada por aqui");
            }
        }

        private void FillReceivedRow(IaModule module, WorldCapture capture, LetterRow row, Transform top, Letter letter, int me, int turn, List<int> ai)
        {
            CapturedEmpire sender = capture.Empire(letter.From);
            bool fresh = !letter.ReadBy.Contains(me) || freshShown.Contains(letter.Id) && currentSection == Section.New;
            bool answered = !letter.IsPublic && module.PlayerAnswered(letter);
            B.SetLabel(top, "TitleGroup/Title", (sender?.Culture ?? DossierBuilder.Name(capture, letter.From)) + (letter.IsPublic ? " · " + L.T("a todos") : string.Empty));
            B.SetChip(top, "StatsTable/PopCount", fresh ? $"<c={Green}>{L.T("Nova")}</c> · " + L.F("chegou no turno {0}", letter.DeliverTurn) : L.F("Chegou no turno {0}", letter.DeliverTurn));
            B.SetChip(top, "StatsTable/Fortification", TypeChip(letter.Type));
            B.SetVisible(top.Find("StatsTable/ExtensionsCount"), true);
            string state = answered ? $"<c={Green}>{L.T("Respondida")}</c>"
                : !letter.IsPublic && letter.ExpectsReply ? (letter.Type == "ultimato" ? UltimatumChip(letter, turn) : $"<c={Gold}>{L.T("Pede resposta")}</c>")
                : sender?.Leader ?? "?";
            B.SetChip(top, "StatsTable/ExtensionsCount", state);
            // Só responde a quem ainda existe: sem o remetente na lista, o Escrever trocaria o destinatário calado.
            bool canReply = ai.Contains(letter.From);
            B.SetVisible(row.Primary.transform, canReply);
            if (canReply)
            {
                B.SetButtonText(row.Primary, answered ? L.T("Escrever de novo") : L.T("Responder"));
                // Segundo botão: a conversa inteira com essa nação, na aba Cartas da diplomacia.
                SetSecondButton(row.Secondary, row.Primary, L.T("Na diplomacia"));
            }
            else
            {
                B.SetVisible(row.Secondary.transform, false);
            }
            FitTitle(top, canReply ? row.Primary.GetComponent<UITransform>().Width + row.Secondary.GetComponent<UITransform>().Width + 28f : 12f);
            B.Tip(row.Secondary.transform, string.Empty, L.T("Na diplomacia"), L.T("Abre a tela de diplomacia com essa nação, na aba Cartas: a conversa inteira, em ordem."));
            SetBody(row.Body, LetterBody(module, capture, letter, me));
            B.Tip(top, "StatsTable/PopCount", L.T("Chegada"), L.F("Escrita no turno {0}, chegou no turno {1}.", letter.SentTurn, letter.DeliverTurn));
            B.Tip(top, "StatsTable/Fortification", TypeName(letter.Type), TypeHelp(letter.Type));
            B.Tip(top, "StatsTable/ExtensionsCount", answered ? L.T("Respondida") : letter.ExpectsReply && !letter.IsPublic ? L.T("Pede resposta") : L.T("Quem assina"),
                answered ? L.T("Você já respondeu esta carta; a sua resposta aparece logo abaixo do texto.")
                : letter.ExpectsReply && !letter.IsPublic ? L.T("Quem escreveu espera uma resposta sua. Ficar em silêncio também é uma resposta.") : sender?.FullName ?? "?");
            B.Tip(row.Primary.transform, string.Empty, L.T("Responder"), L.T("Abre o painel de escrever, à direita, endereçado a quem mandou esta carta e ligado a ela."));
        }

        private void FillSentRow(IaModule module, WorldCapture capture, LetterRow row, Transform top, Letter letter, int turn)
        {
            string to = letter.IsPublic ? L.T("Você · a todas as nações") : L.F("Para {0}", GameText.InlineUi(capture.Empire(letter.To)?.Culture ?? DossierBuilder.Name(capture, letter.To)));
            B.SetLabel(top, "TitleGroup/Title", to);
            B.SetChip(top, "StatsTable/PopCount", L.F("Enviada no turno {0}", letter.SentTurn));
            B.SetChip(top, "StatsTable/Fortification", TypeChip(letter.Type));
            B.SetVisible(top.Find("StatsTable/ExtensionsCount"), true);
            B.SetChip(top, "StatsTable/ExtensionsCount", StatusChip(module, letter, turn, out string statusHelp));
            B.SetVisible(row.Primary.transform, false);
            B.SetVisible(row.Secondary.transform, false);
            FitTitle(top, 12f);
            SetBody(row.Body, LetterBody(module, capture, letter, letter.From));
            B.Tip(top, "StatsTable/PopCount", L.T("Envio"), letter.DeliverTurn > turn
                ? L.F("Escrita no turno {0}; chega no turno {1}.", letter.SentTurn, letter.DeliverTurn)
                : L.F("Escrita no turno {0}; chegou no turno {1}.", letter.SentTurn, letter.DeliverTurn));
            B.Tip(top, "StatsTable/Fortification", TypeName(letter.Type), TypeHelp(letter.Type));
            B.Tip(top, "StatsTable/ExtensionsCount", L.T("Situação"), statusHelp);
        }

        internal static string UltimatumChip(Letter letter, int turn)
        {
            int expires = letter.SentTurn + letter.DeadlineTurns;
            return expires >= turn ? $"<c={Red}>{L.F("Vence no turno {0}", expires)}</c>" : $"<c={Red}>{L.F("Venceu no turno {0}", expires)}</c>";
        }

        internal static string StatusChip(IaModule module, Letter letter, int turn, out string help)
        {
            if (letter.RejectedBy.Count > 0)
            {
                help = L.T("O destinatário recusa a sua correspondência privada: a carta voltou sem ser lida.");
                return $"<c={Red}>{L.T("Recusada")}</c>";
            }
            if (letter.DeliverTurn > turn)
            {
                help = L.F("Ainda viajando. Chega no turno {0}; a resposta, se vier, sai no turno seguinte.", letter.DeliverTurn);
                return L.F("Chega no turno {0}", letter.DeliverTurn);
            }
            if (letter.IsPublic)
            {
                help = L.F("Lida por {0} nação(ões) até agora.", letter.ReadBy.Count);
                return L.F("Lida por {0}", letter.ReadBy.Count);
            }
            Letter reply = module.ReplyTo(letter);
            if (reply != null)
            {
                help = reply.DeliverTurn > turn
                    ? L.F("Responderam no turno {0}; a resposta chega no turno {1}.", reply.SentTurn, reply.DeliverTurn)
                    : L.F("Responderam no turno {0}; a resposta está nas suas cartas recebidas.", reply.SentTurn);
                return reply.DeliverTurn > turn ? L.T("Resposta a caminho") : $"<c={Green}>{L.T("Respondida")}</c>";
            }
            if (letter.ReadBy.Contains(letter.To))
            {
                help = L.T("O destinatário já leu. Se ele responder, a carta chega pelo tempo de entrega da era dele.");
                return $"<c={Green}>{L.T("Lida")}</c>";
            }
            help = L.T("Chegou; o destinatário lê no começo do próximo turno dele.");
            return L.T("Entregue");
        }

        /// <summary>Texto da carta: assunto, exigência, texto e, quando houver, a carta que ela responde e a resposta que recebeu.</summary>
        internal static string LetterBody(IaModule module, WorldCapture capture, Letter letter, int me)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(letter.Subject))
            {
                parts.Add($"<c={Gold}>{Escape(letter.Subject)}</c>");
            }
            if (letter.InReplyTo >= 0)
            {
                Letter original = module.LetterById(letter.InReplyTo);
                if (original != null)
                {
                    // "Os Bizantinos" → "à carta dos Bizantinos" (crase e contração, não "a a carta de Os Bizantinos").
                    string quoted = string.IsNullOrWhiteSpace(original.Subject) ? string.Empty : $" (“{Escape(original.Subject)}”)";
                    string line = original.From == me
                        ? L.F("Em resposta à sua carta do turno {0}{1}.", original.SentTurn, quoted)
                        : L.F("Em resposta à carta {0} do turno {1}{2}.", GameText.OfUi(capture.Empire(original.From)?.Culture ?? DossierBuilder.Name(capture, original.From)), original.SentTurn, quoted);
                    parts.Add($"<i>{line}</i>");
                }
            }
            if (!string.IsNullOrWhiteSpace(letter.Demand))
            {
                parts.Add($"<c={Red}>{L.F("Exigência: {0} — prazo de {1} turnos (vence no turno {2}).", Escape(letter.Demand), letter.DeadlineTurns, letter.SentTurn + letter.DeadlineTurns)}</c>");
            }
            parts.Add(Escape(letter.Text));
            return string.Join("\n\n", parts);
        }

        /// <summary>Texto da IA ou do jogador não pode virar marcação de texto rico por acidente.</summary>
        internal static string Escape(string text) => (text ?? string.Empty).Replace("<", "‹").Replace(">", "›");

        internal static string TypeChip(string type)
        {
            switch (type)
            {
                case "ultimato": return $"<c={Red}>{L.T("Ultimato")}</c>";
                case "publica": return L.T("Pública");
                default: return L.T("Privada");
            }
        }

        internal static string TypeName(string type) => type == "ultimato" ? L.T("Ultimato") : type == "publica" ? L.T("Declaração pública") : L.T("Carta privada");

        internal static string TypeHelp(string type)
        {
            switch (type)
            {
                case "ultimato": return L.T("Exigência com prazo. Chega mesmo para quem recusa sua correspondência.");
                case "publica": return L.T("Todas as nações que conhecem quem escreveu leem. Serve de propaganda, aviso ou denúncia.");
                default: return L.T("Só o destinatário lê. Não chega se ele recusar a sua correspondência.");
            }
        }

        private void SetLetterRowCount(int count)
        {
            while (letterRows.Count < count)
            {
                int index = letterRows.Count;
                Transform card = listTable.InstantiateChild(cardSample, "Letter" + index).transform;
                Transform top = card.Find("Table/Top");
                UIButton primary = top.Find("LiberateButton").GetComponent<UIButton>();
                UIButton secondary = top.GetComponent<UITransform>().InstantiateChild(primary.transform, "SecondButton").GetComponent<UIButton>();
                var row = new LetterRow { Card = card, Body = AddBody(card), Primary = primary, Secondary = secondary };
                primary.LeftClick += b => StartReply(letterRows[index].Letter);
                secondary.LeftClick += b =>
                {
                    Letter letter = letterRows[index].Letter;
                    if (letter != null)
                    {
                        SetOpen(false);
                        CorrespondenceTab.OpenFor(letter.From);
                    }
                };
                letterRows.Add(row);
            }
            for (int i = 0; i < letterRows.Count; i++)
            {
                B.SetVisible(letterRows[i].Card, i < count);
            }
            B.SetVisible(cardSample, false);
        }

        /// <summary>
        /// Texto corrido da carta dentro do cartão: um rótulo (fonte do jogo, quebra de linha, altura automática) logo
        /// abaixo da linha de título e chips. A tabela do cartão empilha e o cartão cresce junto.
        /// </summary>
        internal static Transform AddBody(Transform card)
        {
            Transform table = card.Find("Table");
            Transform top = card.Find("Table/Top");
            Transform title = card.Find("Table/Top/TitleGroup/Title");
            Transform chip = card.Find("Table/Top/StatsTable/PopCount");
            UITransform body = table.GetComponent<UITransform>().InstantiateChild(title, "Body");
            body.transform.SetAsLastSibling();
            UILabel label = body.GetComponent<UILabel>();
            UILabel chipLabel = chip.GetComponent<UILabel>();
            label.FontFamily = chipLabel.FontFamily;
            label.FontSize = 16;
            label.ForceCaps = false;
            label.WordWrap = true;
            label.AutoAdjustWidth = false;
            label.AutoAdjustHeight = true;
            label.Alignment = new Alignment(HorizontalAlignment.Left, VerticalAlignment.Top);
            label.Margins = new RectMargins(16f, 16f, 8f, 12f);
            label.Color = new Color(0.96f, 0.92f, 0.86f, 1f);
            body.LeftAnchor = body.LeftAnchor.SetAttach(false);
            body.RightAnchor = body.RightAnchor.SetAttach(false);
            body.TopAnchor = body.TopAnchor.SetAttach(false);
            body.BottomAnchor = body.BottomAnchor.SetAttach(false);
            body.X = 0f;
            body.Width = top.GetComponent<UITransform>().Width;
            // O título doador tem peso de redimensionamento 1; com um filho "elástico" a tabela não cresce sozinha.
            body.ResizeWeight = 0;
            body.VisibleSelf = true;
            var tableLayout = table.GetComponent<Amplitude.UI.Layouts.UITable1D>();
            if (tableLayout != null && !tableLayout.enabled)
            {
                tableLayout.enabled = true;
            }
            return body.transform;
        }

        internal static void SetBody(Transform body, string text)
        {
            UILabel label = body != null ? body.GetComponent<UILabel>() : null;
            if (label == null)
            {
                return;
            }
            if (label.Text != text)
            {
                label.Text = text;
            }
            label.AdjustSizesIfNecessary();
            body.parent.GetComponent<Amplitude.UI.Layouts.UILayout>()?.ArrangeChildren();
        }

        /// <summary>
        /// Título que divide a linha com botões: o grupo do título termina antes dos botões, e o rótulo corta com
        /// reticências, como no jogo.
        /// </summary>
        internal static void FitTitle(Transform top, float rightReserved)
        {
            Transform group = top.Find("TitleGroup");
            UILabel label = top.Find("TitleGroup/Title")?.GetComponent<UILabel>();
            if (group == null || label == null)
            {
                return;
            }
            if (!label.AutoTruncate)
            {
                label.AutoTruncate = true;
            }
            UITransform groupUi = group.GetComponent<UITransform>();
            if (Mathf.Abs(groupUi.RightAnchor.Offset - rightReserved) > 0.5f)
            {
                groupUi.RightAnchor = groupUi.RightAnchor.SetOffset(rightReserved);
                group.GetComponent<Amplitude.UI.Layouts.UILayout>()?.ArrangeChildren();
            }
        }

        private void ShowEmpty(string title, string chip, string tipTitle, string tipText)
        {
            if (emptyCard == null)
            {
                return;
            }
            Transform top = emptyCard.Find("Table/Top");
            B.SetLabel(top, "TitleGroup/Title", title);
            B.SetChip(top, "StatsTable/PopCount", chip);
            B.HideChild(top, "StatsTable/Fortification");
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.HideChild(top, "LiberateButton");
            B.HideOutputs(top);
            if (tipTitle != null)
            {
                B.Tip(top, "StatsTable/PopCount", tipTitle, tipText);
            }
            emptyCard.SetAsLastSibling();
            B.SetVisible(emptyCard, true);
        }

        // ---------------- Escrever (painel da direita) ----------------

        private void FillCompose(IaModule module, WorldCapture capture, int me, int turn, int era, int delay)
        {
            List<int> nations = KnownNations(capture);
            string type = TypeIds[composeType];
            if (type != "publica" && (composeTo < 0 || !nations.Contains(composeTo)))
            {
                composeTo = nations.Count > 0 ? nations[0] : -1;
            }
            Letter replying = replyToId >= 0 ? module.LetterById(replyToId) : null;
            if (replying != null && (type == "publica" || replying.From != composeTo))
            {
                replyToId = -1;
                replying = null;
            }
            composeTitleLabel.Text = replying != null ? L.T("Responder") : L.T("Escrever");
            // Os cartões nascem escondidos como o modelo da lista do jogo.
            B.SetVisible(recipientCard, true);
            B.SetVisible(typeCard, true);
            B.SetVisible(sendCard, true);

            // Destinatário.
            Transform top = recipientCard.Find("Table/Top");
            CapturedEmpire them = capture.Empire(composeTo);
            int arrives = turn + delay;
            if (type == "publica")
            {
                B.SetLabel(top, "TitleGroup/Title", L.T("Para: todas as nações"));
                B.SetChip(top, "StatsTable/PopCount", L.F("Chega no turno {0}", arrives));
                B.SetChip(top, "StatsTable/Fortification", L.F("{0} leem", nations.Count));
                B.HideChild(top, "StatsTable/ExtensionsCount");
                B.HideChild(top, "LiberateButton");
            }
            else if (them != null)
            {
                B.SetLabel(top, "TitleGroup/Title", L.F("Para: {0}", them.Culture));
                B.SetChip(top, "StatsTable/PopCount", L.F("Chega no turno {0}", arrives));
                B.SetChip(top, "StatsTable/Fortification", RelationChip(capture.Empire(me)?.RelationWith(composeTo)));
                B.HideChild(top, "StatsTable/ExtensionsCount");
                B.SetVisible(recipientButton.transform, nations.Count > 1);
                if (nations.Count > 1)
                {
                    B.SetButtonText(recipientButton, L.T("Trocar"));
                }
            }
            else
            {
                B.SetLabel(top, "TitleGroup/Title", L.T("Você ainda não conhece ninguém"));
                B.HideChild(top, "StatsTable/Fortification");
                B.HideChild(top, "StatsTable/ExtensionsCount");
                B.SetChip(top, "StatsTable/PopCount", L.T("Explore o mapa"));
                B.HideChild(top, "LiberateButton");
            }
            FitTitle(top, recipientButton.GetComponent<UITransform>().Width + 20f);
            B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", L.T("Chegada"), L.F("Sua carta viaja por {0} e chega no turno {1}. A resposta sai no turno seguinte à chegada.", Delivery.CourierName(era), arrives));
            B.Tip(top, "StatsTable/Fortification", type == "publica" ? L.T("Leitores") : L.T("Relação atual"), type == "publica" ? L.T("Nações que conhecem você e vão ler a declaração.") : them?.FullName ?? string.Empty);
            B.Tip(recipientButton.transform, string.Empty, L.T("Trocar destinatário"), L.T("Passa para a próxima nação que você conhece. Na seção Nações dá para escolher direto."));

            // Tipo.
            Transform typeTop = typeCard.Find("Table/Top");
            B.SetLabel(typeTop, "TitleGroup/Title", L.T(TypeNames[composeType]));
            B.SetChip(typeTop, "StatsTable/PopCount", type == "ultimato" ? L.T("Exigência com prazo") : type == "publica" ? L.T("Todos leem") : L.T("Só ele lê"));
            B.HideChild(typeTop, "StatsTable/Fortification");
            B.HideChild(typeTop, "StatsTable/ExtensionsCount");
            B.SetButtonText(typeButton, L.T("Trocar"));
            FitTitle(typeTop, typeButton.GetComponent<UITransform>().Width + 20f);
            B.HideOutputs(typeTop);
            B.Tip(typeTop, "StatsTable/PopCount", L.T(TypeNames[composeType]), TypeHelp(type));
            B.Tip(typeButton.transform, string.Empty, L.T("Trocar tipo"), L.T("Carta privada → Ultimato → Declaração pública."));

            // Em resposta a.
            B.SetVisible(replyCard, replying != null);
            if (replying != null)
            {
                Transform replyTop = replyCard.Find("Table/Top");
                B.SetLabel(replyTop, "TitleGroup/Title", string.IsNullOrWhiteSpace(replying.Subject) ? L.F("Resposta à carta do turno {0}", replying.SentTurn) : "Re: " + Escape(replying.Subject).Replace("Re: ", string.Empty));
                B.SetChip(replyTop, "StatsTable/PopCount", L.F("Carta do turno {0}", replying.SentTurn));
                B.HideChild(replyTop, "StatsTable/Fortification");
                B.HideChild(replyTop, "StatsTable/ExtensionsCount");
                B.SetButtonText(replyButton, L.T("Soltar"));
                FitTitle(replyTop, replyButton.GetComponent<UITransform>().Width + 20f);
                B.HideOutputs(replyTop);
                B.Tip(replyTop, "StatsTable/PopCount", L.T("Em resposta a"), L.T("Esta carta fica ligada à original: as duas aparecem juntas em Respondidas."));
                B.Tip(replyButton.transform, string.Empty, L.T("Soltar"), L.T("Escreve uma carta nova, sem ligar a esta."));
            }

            B.SetVisible(demandItem, type == "ultimato");
            B.SetVisible(deadlineItem, type == "ultimato");
            if (deadlineItem != null)
            {
                deadlineItem.GetComponent<UILabel>().Text = L.F("Prazo: {0} turnos", Mathf.RoundToInt(deadlineSlider.CurrentValue));
                NativeUIKit.Tip(deadlineItem, L.T("Prazo do ultimato"), L.T("Turnos que a nação tem para cumprir a exigência, contados a partir do envio."));
            }

            // Envio.
            Transform sendTop = sendCard.Find("Table/Top");
            int words = bodyField != null ? DecisionParser.Words(bodyField.Text) : 0;
            int max = IaConfig.PlayerLetterMaxWords.Value;
            string title = feedback ?? (words == 0 ? L.T("Escreva a carta acima") : L.T("Pronta para enviar"));
            B.SetLabel(sendTop, "TitleGroup/Title", feedback != null && feedbackIsError ? $"<c={Red}>{title}</c>" : title);
            string wordCount = L.F("{0}/{1} palavras", words, max);
            B.SetChip(sendTop, "StatsTable/PopCount", words > max ? $"<c={Red}>{wordCount}</c>" : wordCount);
            B.HideChild(sendTop, "StatsTable/Fortification");
            B.HideChild(sendTop, "StatsTable/ExtensionsCount");
            B.SetButtonText(sendButton, L.T("Enviar"));
            FitTitle(sendTop, sendButton.GetComponent<UITransform>().Width + 20f);
            B.HideOutputs(sendTop);
            B.Tip(sendTop, "StatsTable/PopCount", L.T("Tamanho"), L.F("Cartas acima de {0} palavras são cortadas. Cartas curtas costumam ser mais bem lidas.", max));
            B.Tip(sendButton.transform, string.Empty, L.T("Enviar"), L.T("Manda a carta. Ela aparece em Enviadas com a situação da entrega."));

            Transform[] order = { recipientCard, typeCard, replyCard, subjectItem, demandItem, deadlineItem, bodyItem, sendCard };
            foreach (Transform item in order)
            {
                item?.SetAsLastSibling();
            }
        }

        internal static string RelationChip(CapturedRelation relation)
        {
            if (relation == null)
            {
                return "?";
            }
            if (relation.AtWar) return $"<c={Red}>{L.T("Guerra")}</c>";
            if (relation.Alliance) return $"<c={Green}>{L.T("Aliança")}</c>";
            if (relation.State == "PartialyKnown") return L.T("Contato recente");
            return L.T("Paz");
        }

        /// <summary>Nações do computador vivas que você conhece (mesma regra de conhecimento do jogo).</summary>
        internal static List<int> KnownNations(WorldCapture capture)
        {
            IaModule module = IaModule.Instance;
            if (capture == null || module == null)
            {
                return new List<int>();
            }
            CapturedEmpire me = capture.Empire(module.PlayerIndex);
            return capture.AiEmpires().Where(e => me?.RelationWith(e)?.Knows ?? false).ToList();
        }

        // ---------------- Nações ----------------

        private void FillNations(IaModule module, WorldCapture capture, int me)
        {
            B.SetVisible(emptyCard, false);
            List<int> nations = KnownNations(capture);
            sectionTitleLabel.Text = L.F("Nações · {0} conhecidas", nations.Count);
            if (nations.Count == 0)
            {
                SetNationRowCount(0);
                ShowEmpty(L.T("Você ainda não conhece nenhuma nação"), L.T("Explore o mapa"), L.T("Sem contatos"), L.T("As nações aparecem aqui depois do primeiro contato."));
                return;
            }
            List<Letter> inbox = module.PlayerInbox();
            List<Letter> outbox = module.PlayerOutbox();
            SetNationRowCount(nations.Count);
            for (int i = 0; i < nations.Count; i++)
            {
                int empire = nations[i];
                NationRow row = nationRows[i];
                row.Empire = empire;
                CapturedEmpire them = capture.Empire(empire);
                bool blocked = module.IsBlockedByPlayer(empire);
                int received = inbox.Count(l => l.From == empire);
                int sent = outbox.Count(l => l.To == empire);
                Transform top = row.Card.Find("Table/Top");
                B.SetLabel(top, "TitleGroup/Title", (them?.Culture ?? DossierBuilder.Name(capture, empire)) + (them?.Leader != null ? " · " + them.Leader : string.Empty));
                B.SetChip(top, "StatsTable/PopCount", RelationChip(capture.Empire(me)?.RelationWith(empire)));
                B.SetChip(top, "StatsTable/Fortification", (received == 1 ? L.T("1 recebida") : L.F("{0} recebidas", received)) + " · " + (sent == 1 ? L.T("1 enviada") : L.F("{0} enviadas", sent)));
                B.SetVisible(top.Find("StatsTable/ExtensionsCount"), blocked);
                if (blocked)
                {
                    B.SetChip(top, "StatsTable/ExtensionsCount", $"<c={Red}>{L.T("Correspondência recusada")}</c>");
                }
                B.HideOutputs(top);
                B.SetButtonText(row.WriteButton, L.T("Escrever"));
                SetSecondButton(row.BlockButton, row.WriteButton, blocked ? L.T("Aceitar cartas") : L.T("Recusar cartas"));
                FitTitle(top, row.WriteButton.GetComponent<UITransform>().Width + row.BlockButton.GetComponent<UITransform>().Width + 28f);
                B.Tip(top, "StatsTable/PopCount", L.T("Relação"), L.T("Como vocês estão hoje no jogo."));
                B.Tip(top, "StatsTable/Fortification", L.T("Correspondência"), L.T("Cartas trocadas com essa nação até agora."));
                B.Tip(top, "StatsTable/ExtensionsCount", L.T("Correspondência recusada"),
                    L.T("Você recusa as cartas privadas dessa nação: elas voltam sem ser lidas, e a nação fica sabendo. Ultimatos e declarações públicas continuam chegando."));
                B.Tip(row.WriteButton.transform, string.Empty, L.T("Escrever"), L.T("Endereça o painel de escrever, à direita, a essa nação."));
                B.Tip(row.BlockButton.transform, string.Empty, blocked ? L.T("Voltar a aceitar cartas") : L.T("Recusar cartas"),
                    blocked ? L.T("As cartas privadas dessa nação voltam a chegar.") : L.T("As cartas privadas dessa nação passam a voltar sem ser lidas. Ela descobre quando a carta volta."));
            }
        }

        private void SetNationRowCount(int count)
        {
            while (nationRows.Count < count)
            {
                int index = nationRows.Count;
                Transform card = listTable.InstantiateChild(cardSample, "Nation" + index).transform;
                Transform top = card.Find("Table/Top");
                UIButton write = top.Find("LiberateButton").GetComponent<UIButton>();
                UIButton block = top.GetComponent<UITransform>().InstantiateChild(write.transform, "BlockButton").GetComponent<UIButton>();
                var row = new NationRow { Card = card, WriteButton = write, BlockButton = block };
                write.LeftClick += b => ComposeTo(nationRows[index].Empire, string.Empty);
                block.LeftClick += b =>
                {
                    IaModule.Instance?.TogglePlayerBlock(nationRows[index].Empire);
                    nextRefresh = 0;
                };
                nationRows.Add(row);
            }
            for (int i = 0; i < nationRows.Count; i++)
            {
                B.SetVisible(nationRows[i].Card, i < count);
            }
            B.SetVisible(cardSample, false);
        }

        /// <summary>Segundo botão do cartão, à esquerda do primeiro, na mesma linha do título.</summary>
        internal static void SetSecondButton(UIButton button, UIButton primary, string text)
        {
            UITransform ui = button.GetComponent<UITransform>();
            if (!ui.InteractiveSelf)
            {
                ui.InteractiveSelf = true;
            }
            B.SetVisible(button.transform, true);
            UILabel label = button.GetComponent<UILabel>();
            UITransform primaryUi = primary.GetComponent<UITransform>();
            float width = Mathf.Ceil(text.Length * 8.5f + 28f);
            if (label.Text != text || Mathf.Abs(ui.Width - width) > 0.5f)
            {
                label.Text = text;
                ui.BottomAnchor = ui.BottomAnchor.SetAttach(false);
                ui.LeftAnchor = ui.LeftAnchor.SetAttach(false);
                ui.SetTopBorder(8f);
                ui.SetRightBorder(10f + primaryUi.Width + 6f);
                ui.Width = width;
                ui.Height = 26f;
            }
        }

        // ---------------- Desenvolvimento e remoção ----------------

        /// <summary>Comandos: "open", "close", "secao 0..6", "to E#", "responder &lt;id&gt;", "texto ...".</summary>
        internal static string DevCommand(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0] : "open";
            if (Instance == null)
            {
                return "erro: tela do correio não existe (abra uma partida)";
            }
            switch (sub)
            {
                case "close":
                    SetOpen(false);
                    return "ok: fechada";
                case "secao":
                case "tab":
                    SetOpen(true);
                    if (parts.Length > 1 && int.TryParse(parts[1], out int section))
                    {
                        Instance.SelectSection((Section)Mathf.Clamp(section, 0, SectionNames.Length - 1));
                    }
                    return "ok";
                case "to":
                    SetOpen(true);
                    if (parts.Length > 1 && int.TryParse(parts[1].TrimStart('E', 'e'), out int empire))
                    {
                        Instance.ComposeTo(empire, null);
                    }
                    return "ok";
                case "responder":
                    SetOpen(true);
                    if (parts.Length > 1 && int.TryParse(parts[1], out int id))
                    {
                        Instance.StartReply(IaModule.Instance?.LetterById(id));
                    }
                    else if (Instance.letterRows.Count > 0 && Instance.letterRows[0].Letter != null)
                    {
                        // Sem id: a primeira carta da lista que está na tela (o mesmo que clicar em Responder nela).
                        Instance.StartReply(Instance.letterRows[0].Letter);
                    }
                    return "ok";
                case "limpar":
                    // Apaga o rascunho (texto, assunto, exigência) e a ligação de resposta, sem enviar.
                    Instance.bodyField?.ReplaceText(string.Empty);
                    Instance.subjectField?.ReplaceText(string.Empty);
                    Instance.demandField?.ReplaceText(string.Empty);
                    Instance.replyToId = -1;
                    Instance.feedback = null;
                    Instance.nextRefresh = 0;
                    return "ok: rascunho apagado";
                case "texto":
                    // Só preenche o campo (teste visual do texto multilinha); não envia nada.
                    SetOpen(true);
                    Instance.bodyField?.ReplaceText(string.Join(" ", parts.Skip(1)).Replace("\\n", "\n"));
                    return "ok: texto no campo (não enviado)";
                default:
                    SetOpen(true);
                    return "ok: aberta";
            }
        }

        /// <summary>
        /// Remoção segura (recarga do núcleo, saída da partida). Uma tela cheia destruída aberta deixaria o grupo achando
        /// que há tela aberta: o HUD e as janelas da partida sumiriam para sempre. Esconde, limpa o grupo e solta o teclado.
        /// </summary>
        internal static void DestroyWindow()
        {
            MailScreen window = Instance;
            Instance = null;
            if (window == null)
            {
                return;
            }
            try
            {
                if (window.Shown)
                {
                    WindowsUtils.HideWindow(window, instant: true);
                }
                if (window.Group is InGameFullscreenGroup fullscreen && fullscreen.lastOpenedWindow == window)
                {
                    fullscreen.lastOpenedWindow = null;
                }
                if (window.Group is UIWindowsGroup group && group.windows != null)
                {
                    group.windows = group.windows.Where(w => w != window).ToArray();
                }
                UIInteractivityManager.Instance?.SetFocus();
                WindowsManager.Instance?.Dirtyfy();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Remoção da tela do correio: {ex.Message}");
            }
            window.gameObject.SetActive(false);
            Destroy(window.gameObject);
        }
    }
}
