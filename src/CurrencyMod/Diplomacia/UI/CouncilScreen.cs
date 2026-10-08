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
using CurrencyMod.Diplomacia.Council;
using CurrencyMod.NativeUI;
using UnityEngine;
using B = CurrencyMod.NativeUI.NativeBankWindow;

namespace CurrencyMod.Diplomacia.UI
{
    /// <summary>
    /// Tela do conselho do jogador (design §10.6), na mesma moldura do Correio (clone da tela de Configurações, no
    /// grupo das telas cheias da partida):
    ///   esquerda: Reunião do turno, Ministros, Reuniões anteriores;
    ///   centro: as falas (a Mão abre, cada ministro com título, pasta, credibilidade e apreço por você), as suas
    ///           respostas e as reações; ou as fichas dos ministros, com o botão de demitir;
    ///   direita: falar ao conselho.
    /// O conteúdo vem de IaModule.World.PlayerCouncil; as chamadas à API ficam no PlayerCouncil.
    /// </summary>
    internal class CouncilScreen : UIWindow
    {
        private const string WindowName = "CurrencyMod_CouncilScreen";
        private const string FieldDonor = "LoadSavesScreen/Content/Table/SaveGameHeader/SaveNotesInputField";
        private const string Red = "E8685E";
        private const string Gold = "E3C88A";
        private const string Green = "8FD18A";

        private const float CenterX = 420f;
        private const float CenterWidth = 1030f;
        private const float RightX = 1470f;
        private const float RightWidth = 420f;
        private const float RightHeight = 830f;
        private const float ListWidth = 928f; // os cartões ficam 26 px dentro da lista e 34 px mais estreitos: alinham com a linha divisória
        /// <summary>A lista termina acima da barra de controle (que fica por cima da tela cheia até x≈560).</summary>
        private const float ListHeight = 756f;
        private const float SideListWidth = 398f;
        private const float SideListHeight = 720f;

        internal static CouncilScreen Instance;

        internal enum Section { Meeting, Ministers, History }

        private static readonly string[] SectionNames = { L.N("Reunião do turno"), L.N("Ministros"), L.N("Reuniões anteriores") };
        private static readonly string[] SectionHelp =
        {
            L.N("O conselho se reúne a cada turno: a Mão abre a pauta e os ministros das pastas mais urgentes falam. Responda à direita; eles reagem."),
            L.N("Quem são os seus ministros: traços, voz, credibilidade (os números da pasta) e o apreço por você. Aqui você demite quem não serve mais."),
            L.N("As reuniões dos turnos anteriores, com o que cada um aconselhou e o que você respondeu."),
        };

        private UILabel titleLabel;
        private UILabel descriptionLabel;
        private UILabel sectionTitleLabel;
        private UILabel sideTitleLabel;
        private UIButton closeButton;
        private readonly List<UIToggle> sectionToggles = new List<UIToggle>();
        private UITransform listTable;
        private Transform cardSample;
        private UITransform sideTable;
        private Transform sideSample;
        private Section currentSection = Section.Meeting;
        private int pendingSection = -1;
        private float nextRefresh;
        private bool pendingOpen;
        private float openedAt;
        private bool wasShown;

        private readonly List<Row> rows = new List<Row>();
        private Transform speakCard;
        private Transform sendCard;
        private Transform stateCard;
        private Transform fieldItem;
        private UITextField field;
        private UIButton sendButton;
        private UIButton retryButton;
        private string feedback;
        private bool feedbackIsError;
        private string confirmFire;
        private float confirmUntil;
        /// <summary>Falas suas e respostas já mostradas: quando muda, a lista rola até a sua última fala.</summary>
        private string shownExchanges;
        private Transform scrollTarget;
        private float scrollAt;

        internal static bool IsOpen => Instance != null && Instance.Shown;

        private sealed class Row
        {
            public Transform Card;
            public Transform Body;
            public UIButton Button;
            public string Portfolio;
        }

        // ---------------- Criação ----------------

        internal static CouncilScreen Create()
        {
            SystemSettingsScreen donor = Resources.FindObjectsOfTypeAll<SystemSettingsScreen>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            AllSettlementsWindow listDonor = Resources.FindObjectsOfTypeAll<AllSettlementsWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            InGameFullscreenGroup group = WindowsManager.Instance?.GetWindowsGroup<InGameFullscreenGroup>();
            if (donor == null || listDonor == null || group == null || !group.IsReady)
            {
                return null;
            }
            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            CouncilScreen window;
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

                MailScreen.StripSettings(clone.transform);
                window = clone.AddComponent<CouncilScreen>();
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
            Plugin.Log.LogInfo("Tela cheia do conselho criada.");
            return window;
        }

        private void Bind(Transform root, Transform citiesList)
        {
            Transform left = root.Find("Left");
            titleLabel = left.Find("Title").GetComponent<UILabel>();
            descriptionLabel = left.Find("ScreenDescription").GetComponent<UILabel>();
            closeButton = left.Find("CloseMenuButton").GetComponent<UIButton>();
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

            Transform center = root.Find("Center");
            UITransform centerUi = center.GetComponent<UITransform>();
            Transform rightPanel = Instantiate(center.gameObject, root).transform;
            rightPanel.name = "Speak";
            NativeUIKit.Place(center, CenterX, centerUi.Y, CenterWidth, centerUi.Height);
            NativeUIKit.Place(rightPanel, RightX, 56f, RightWidth, RightHeight);

            titleLabel.transform.SetParent(center, false);
            NativeUIKit.Place(titleLabel.transform, 64f, 26f, 700f, 60f);
            descriptionLabel.transform.SetParent(center, false);
            NativeUIKit.Place(descriptionLabel.transform, 64f, 84f, CenterWidth - 128f, 24f);
            sectionTitleLabel = center.Find("SettingsTitle").GetComponent<UILabel>();
            NativeUIKit.Place(sectionTitleLabel.transform, 64f, 132f, 560f, 18f);
            NativeUIKit.Place(center.Find("Divider"), 64f, 158f, CenterWidth - 128f, 1f);
            Transform container = center.Find("GroupsContainer");
            NativeUIKit.Place(container, 38f, 170f, ListWidth, ListHeight);
            Transform list = Instantiate(citiesList.gameObject, container).transform;
            list.name = "Speeches";
            MailScreen.PrepareList(list, ListWidth, ListHeight, out listTable, out cardSample);

            sideTitleLabel = rightPanel.Find("SettingsTitle").GetComponent<UILabel>();
            NativeUIKit.Place(sideTitleLabel.transform, 24f, 30f, RightWidth - 48f, 18f);
            NativeUIKit.Place(rightPanel.Find("Divider"), 24f, 56f, RightWidth - 48f, 1f);
            Transform sideContainer = rightPanel.Find("GroupsContainer");
            NativeUIKit.Place(sideContainer, -2f, 70f, SideListWidth + 8f, SideListHeight);
            Transform side = Instantiate(citiesList.gameObject, sideContainer).transform;
            side.name = "SpeakList";
            MailScreen.PrepareList(side, SideListWidth + 8f, SideListHeight, out sideTable, out sideSample);
        }

        protected override IEnumerator PostLoad()
        {
            yield return base.PostLoad();
            closeButton.LeftClick += CloseButton_LeftClick;
            B.SetLabel(closeButton.transform, string.Empty, L.T("Voltar ao jogo"));
            titleLabel.Text = L.T("Conselho");
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
                UILabel label = toggle.GetComponent<UILabel>();
                if (label != null)
                {
                    label.Text = L.T(SectionNames[i]);
                }
            }
            ClearInheritedTooltips();
            for (int i = 0; i < sectionToggles.Count; i++)
            {
                BindTooltip(sectionToggles[i].transform, L.T(SectionNames[i]), L.T(SectionHelp[i]));
            }
            BindTooltip(closeButton.transform, L.T("Voltar ao jogo"), L.T("Fecha o conselho. O ESC e o botão do conselho também fecham."));
            try
            {
                BuildSide();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Conselho: falha ao montar o painel de falar: {ex}");
            }
            SelectSection(pendingSection >= 0 ? (Section)pendingSection : Section.Meeting);
            pendingSection = -1;
        }

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

        // ---------------- Painel da direita: falar ----------------

        private void BuildSide()
        {
            speakCard = sideTable.InstantiateChild(sideSample, "SpeakCard").transform;
            B.HideChild(speakCard.Find("Table/Top"), "LiberateButton");

            Transform donor = DevTools.FindByPath(FieldDonor);
            if (donor != null)
            {
                fieldItem = NativeUIKit.Clone(donor, sideTable.transform, "CouncilField");
                fieldItem.gameObject.SetActive(true);
                UITransform sampleUi = sideSample.GetComponent<UITransform>();
                UITransform ui = fieldItem.GetComponent<UITransform>();
                ui.Width = sampleUi.Width;
                ui.X = sampleUi.X;
                ui.Height = 260f;
                field = fieldItem.GetComponent<UITextField>();
                field.maximumChars = 900;
                field.whiteList = string.Empty;
                field.BlackList = "<>{}";
                field.InstructionText = "<c=F5EBE166><i>" + L.T("Diga ao conselho o que pensa, pergunte a um ministro pelo nome ou pela pasta, ou dê uma ordem…") + "</i></c>";
                NativeUIKit.BlockGameShortcuts(field);
                field.actionOnFocus = UITextFieldFocusAction.PlaceCaretAtCursor;
                field.multiline = true;
                field.OnMultilineChanged(false, true);
                // TextChange é ligado em Update (EnsureTextChange), depois que o campo carrega.
                NativeUIKit.Tip(fieldItem, L.T("Falar ao conselho"), L.F("Enter quebra a linha. Até {0} palavras. Cada fala sua custa uma chamada à API.", PlayerCouncil.MaxPlayerWords));
            }
            else
            {
                Plugin.Log.LogWarning("Conselho: doador do campo de texto não encontrado.");
            }

            sendCard = sideTable.InstantiateChild(sideSample, "SendCard").transform;
            sendButton = sendCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            sendButton.LeftClick += b => Speak();

            stateCard = sideTable.InstantiateChild(sideSample, "StateCard").transform;
            retryButton = stateCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            retryButton.LeftClick += b =>
            {
                SetFeedback(PlayerCouncil.Retry().Replace("ok: ", string.Empty), false);
            };
            B.SetVisible(sideSample, false);
        }

        private void Speak()
        {
            if (field == null)
            {
                return;
            }
            string result = PlayerCouncil.Speak(field.Text);
            if (result.StartsWith("ok"))
            {
                field.ReplaceText(string.Empty);
                SetFeedback(L.T("O conselho está ouvindo…"), false);
            }
            else
            {
                SetFeedback(result.Replace("erro: ", string.Empty), true);
            }
        }

        private void SetFeedback(string message, bool isError)
        {
            feedback = message;
            feedbackIsError = isError;
            nextRefresh = 0;
        }

        private void FillSide(CouncilMeeting meeting, int turn)
        {
            sideTitleLabel.Text = L.T("Falar ao conselho");
            B.SetVisible(speakCard, true);
            B.SetVisible(sendCard, true);
            Transform top = speakCard.Find("Table/Top");
            B.SetLabel(top, "TitleGroup/Title", L.T("Você fala ao conselho"));
            B.SetChip(top, "StatsTable/PopCount", L.F("Turno {0}", turn));
            int used = meeting?.Exchanges.Count ?? 0;
            B.SetChip(top, "StatsTable/Fortification", L.F("{0} fala(s) sua(s)", used));
            B.HideChild(top, "StatsTable/ExtensionsCount");
            MailScreen.FitTitle(top, 12f);
            B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", L.T("Reunião do turno"), L.T("Você responde à reunião deste turno. No próximo turno o conselho se reúne de novo."));
            B.Tip(top, "StatsTable/Fortification", L.T("Suas falas"), L.T("Até 8 por turno. Os ministros respondem com o que sabem do reino e mudam o apreço por você conforme o que ouvem."));

            bool ready = meeting != null && meeting.Status == "pronta";
            bool listening = PlayerCouncil.Thinking && ready || (meeting?.Exchanges.Any(e => e.Status == "pensando") ?? false);
            Transform sendTop = sendCard.Find("Table/Top");
            int words = field != null ? DecisionParser.Words(field.Text) : 0;
            string title = feedback ?? (listening ? L.T("O conselho está ouvindo…") : !ready ? L.T("Aguarde a reunião") : words == 0 ? L.T("Escreva acima") : L.T("Pronto para falar"));
            if (!listening && feedback == L.T("O conselho está ouvindo…"))
            {
                feedback = null;
                title = words == 0 ? L.T("Escreva acima") : L.T("Pronto para falar");
            }
            B.SetLabel(sendTop, "TitleGroup/Title", feedback != null && feedbackIsError ? $"<c={Red}>{title}</c>" : title);
            string wordCount = L.F("{0}/{1} palavras", words, PlayerCouncil.MaxPlayerWords);
            B.SetChip(sendTop, "StatsTable/PopCount", words > PlayerCouncil.MaxPlayerWords ? $"<c={Red}>{wordCount}</c>" : wordCount);
            B.HideChild(sendTop, "StatsTable/Fortification");
            B.HideChild(sendTop, "StatsTable/ExtensionsCount");
            B.SetVisible(sendButton.transform, ready && !listening);
            B.SetButtonText(sendButton, L.T("Falar"));
            MailScreen.FitTitle(sendTop, sendButton.GetComponent<UITransform>().Width + 20f);
            B.HideOutputs(sendTop);
            B.Tip(sendTop, "StatsTable/PopCount", L.T("Tamanho"), L.F("Falas acima de {0} palavras são cortadas.", PlayerCouncil.MaxPlayerWords));
            B.Tip(sendButton.transform, string.Empty, L.T("Falar"), L.T("Manda a sua fala ao conselho. As reações aparecem na reunião, logo abaixo da sua fala."));

            // Situação da reunião (e convocar de novo, se a chamada falhou).
            bool failed = meeting != null && (meeting.Status == "erro" || meeting.Exchanges.Any(e => e.Status == "erro"));
            B.SetVisible(stateCard, meeting != null);
            if (meeting != null)
            {
                Transform stateTop = stateCard.Find("Table/Top");
                string state = meeting.Status == "pronta" ? $"<c={Green}>" + L.T("Reunião feita") + "</c>" : meeting.Status == "erro" ? $"<c={Red}>" + L.T("A reunião falhou") + "</c>" : L.T("O conselho está se reunindo…");
                B.SetLabel(stateTop, "TitleGroup/Title", state);
                B.SetChip(stateTop, "StatsTable/PopCount", "US$ " + meeting.CostUsd.ToString("0.0000", L.Culture));
                B.HideChild(stateTop, "StatsTable/Fortification");
                B.HideChild(stateTop, "StatsTable/ExtensionsCount");
                B.SetVisible(retryButton.transform, failed);
                B.SetButtonText(retryButton, L.T("Convocar de novo"));
                MailScreen.FitTitle(stateTop, failed ? retryButton.GetComponent<UITransform>().Width + 20f : 12f);
                B.HideOutputs(stateTop);
                string error = meeting.Error ?? meeting.Exchanges.Select(e => e.Error).LastOrDefault(e => e != null);
                B.Tip(stateTop, "StatsTable/PopCount", L.T("Custo"), L.T("Quanto a reunião deste turno e as suas falas custaram na API até agora."));
                B.Tip(stateTop, "TitleGroup/Title", L.T("Situação"), failed ? L.F("A chamada à API falhou: {0}", error != null ? CouncilBank.ErrorUi(error) : "?") : L.T("A reunião é escrita uma vez por turno, a partir do seu dossiê (só o que o seu reino sabe)."));
                B.Tip(retryButton.transform, string.Empty, L.T("Convocar de novo"), L.T("Tenta a reunião (ou a sua última fala) de novo."));
            }

            Transform[] order = { speakCard, fieldItem, sendCard, stateCard };
            foreach (Transform item in order)
            {
                item?.SetAsLastSibling();
            }
        }

        // ---------------- Abertura e seções ----------------

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
                Instance.pendingOpen = true;
                return;
            }
            Instance.pendingOpen = false;
            ExclusiveWindows.BeforeOpen(Instance);
            Instance.openedAt = Time.unscaledTime;
            WindowsUtils.ShowWindow(Instance);
            Instance.nextRefresh = 0;
        }

        internal void SelectSection(Section section)
        {
            if (sectionToggles.Count < SectionNames.Length || speakCard == null)
            {
                pendingSection = (int)section;
                return;
            }
            currentSection = section;
            for (int i = 0; i < sectionToggles.Count; i++)
            {
                sectionToggles[i].State = i == (int)section;
            }
            SetRowCount(0);
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

        private Action<Amplitude.UI.Interactables.IUITextField, string> fieldChanged;

        private void Update()
        {
            NativeUIKit.EnsureTextChange(field, fieldChanged ?? (fieldChanged = (f, t) => { if (feedbackIsError) { feedback = null; } nextRefresh = 0; }));
            if (wasShown && !Shown)
            {
                NativeUIKit.ReleaseTextFocus(); // o jogo escondeu a tela sem passar por SetOpen
            }
            wasShown = Shown;
            if (pendingOpen && LoadingState == Amplitude.UI.Windows.LoadingState.Loaded)
            {
                SetOpen(true);
            }
            if (Shown && ExclusiveWindows.Interrupted(this, openedAt))
            {
                SetOpen(false);
                return;
            }
            if (Shown && scrollTarget != null && Time.unscaledTime >= scrollAt)
            {
                ScrollToTop(scrollTarget);
                scrollTarget = null;
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
                Plugin.Log.LogError($"Conselho: {ex}");
                nextRefresh = Time.unscaledTime + 5f;
            }
        }

        /// <summary>Rola a lista até o cartão ficar no topo (o UIScrollView.ScrollTo só garante que ele apareça).</summary>
        private void ScrollToTop(Transform card)
        {
            try
            {
                UIScrollView view = listTable.transform.parent?.parent?.GetComponent<UIScrollView>();
                UITransform target = card.GetComponent<UITransform>();
                if (view == null || target == null || view.viewport == null || view.content == null)
                {
                    return;
                }
                float offset = target.GlobalRect.yMin - view.viewport.GlobalRect.yMin;
                float scrollable = Mathf.Max(0f, view.content.Height - view.viewport.Height);
                view.content.Y = Mathf.Clamp(view.content.Y - offset, -scrollable, 0f);
                view.ScrollViewResponder?.Refresh();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Conselho: rolar a lista: {ex.Message}");
            }
        }

        // ---------------- Conteúdo ----------------

        private void RefreshContent()
        {
            IaModule module = IaModule.Instance;
            WorldCapture capture = TurnCapture.Latest;
            IaWorld world = IaModule.World;
            if (module == null || world == null || module.PlayerIndex < 0 || capture == null || capture.Guid != world.GameGuid)
            {
                descriptionLabel.Text = L.T("O conselho se reúne quando a partida estiver carregada.");
                SetRowCount(1);
                FillNotice(rows[0], L.T("Aguardando o turno"), L.T("Conselho fechado"), null);
                return;
            }
            int me = module.PlayerIndex;
            int turn = module.CurrentTurn;
            IaNation nation = world.Ensure(me);
            CapturedEmpire empire = capture.Empire(me);
            int era = empire?.EraIndex ?? 0;
            CouncilMeeting meeting = PlayerCouncil.Meeting(world, turn);
            if (meeting != null && meeting.Status == "pronta" && currentSection == Section.Meeting && !meeting.Seen)
            {
                meeting.Seen = true;
                module.MarkDirty();
            }
            descriptionLabel.Text = DescribeCouncil(nation, turn);
            switch (currentSection)
            {
                case Section.Ministers:
                    FillMinisters(nation, capture, era);
                    break;
                case Section.History:
                    FillHistory(world, nation, capture, era, turn);
                    break;
                default:
                    FillMeeting(meeting, nation, capture, era, turn);
                    break;
            }
            FillSide(meeting, turn);
        }

        private static string DescribeCouncil(IaNation nation, int turn)
        {
            if (!IaConfig.PlayerCouncil.Value)
            {
                return L.T("O conselho do jogador está desligado no .cfg ([IA] ConselhoDoJogador).");
            }
            if (nation.Council.Count == 0)
            {
                return L.F("Turno {0}. O conselho se forma na primeira reunião.", turn);
            }
            int loyal = nation.Council.Count(m => m.Affection >= 20);
            int cold = nation.Council.Count(m => m.Affection <= -20);
            return L.F("Turno {0}. {1} conselheiros", turn, nation.Council.Count)
                + (loyal > 0 ? " · " + L.F("{0} com apreço por você", loyal) : string.Empty)
                + (cold > 0 ? " · " + L.F("{0} ressentido(s)", cold) : string.Empty) + ".";
        }

        private void FillMeeting(CouncilMeeting meeting, IaNation nation, WorldCapture capture, int era, int turn)
        {
            if (meeting == null || meeting.Status != "pronta")
            {
                sectionTitleLabel.Text = L.F("Reunião do turno {0}", turn);
                SetRowCount(1);
                string chip = meeting == null || meeting.Status == "pensando" ? L.T("Reunindo") : $"<c={Red}>" + L.T("Falhou") + "</c>";
                string text = meeting == null || meeting.Status == "pensando"
                    ? L.T("Os ministros estão lendo os despachos do turno. A reunião aparece aqui em instantes.")
                    : L.F("A reunião não saiu: {0}. Use \"Convocar de novo\", à direita.", MailScreen.Escape(meeting.Error != null ? CouncilBank.ErrorUi(meeting.Error) : L.T("erro desconhecido")));
                FillNotice(rows[0], meeting == null || meeting.Status == "pensando" ? L.T("O conselho está se reunindo…") : L.T("A reunião falhou"), chip, text);
                return;
            }
            int count = meeting.Speeches.Count + meeting.Exchanges.Sum(e => 1 + e.Replies.Count + (e.Status == "pronta" ? 0 : 1)) + (meeting.Notes.Count > 0 ? 1 : 0);
            sectionTitleLabel.Text = L.F("Reunião do turno {0} · {1} ministro(s) falaram", meeting.Turn, meeting.Speeches.Count - 1);
            SetRowCount(count);
            int index = 0;
            foreach (CouncilSpeech speech in meeting.Speeches)
            {
                FillSpeech(rows[index++], speech, nation, capture, era, reply: false);
            }
            string exchangesKey = $"{meeting.Turn}:{meeting.Exchanges.Count}:{meeting.Exchanges.Count(e => e.Status == "pronta")}";
            if (shownExchanges != null && exchangesKey != shownExchanges && meeting.Exchanges.Count > 0)
            {
                // Fala nova ou resposta nova: a sua última fala sobe para o topo da lista (com as reações logo abaixo).
                int last = meeting.Speeches.Count + meeting.Exchanges.Take(meeting.Exchanges.Count - 1).Sum(e => 1 + e.Replies.Count + (e.Status == "pronta" ? 0 : 1));
                scrollTarget = rows[last].Card;
                scrollAt = Time.unscaledTime + 0.3f;
            }
            shownExchanges = exchangesKey;
            foreach (CouncilExchange exchange in meeting.Exchanges)
            {
                FillPlayer(rows[index++], exchange);
                foreach (CouncilSpeech reply in exchange.Replies)
                {
                    FillSpeech(rows[index++], reply, nation, capture, era, reply: true);
                }
                if (exchange.Status != "pronta")
                {
                    FillNotice(rows[index++], exchange.Status == "erro" ? L.T("Ninguém respondeu") : L.T("O conselho está ouvindo…"),
                        exchange.Status == "erro" ? $"<c={Red}>" + L.T("Falhou") + "</c>" : L.T("Pensando"),
                        exchange.Status == "erro" ? L.F("A chamada falhou: {0}. Use \"Convocar de novo\", à direita.", MailScreen.Escape(exchange.Error != null ? CouncilBank.ErrorUi(exchange.Error) : "?")) : null);
                }
            }
            if (meeting.Notes.Count > 0)
            {
                FillNotice(rows[index++], L.T("Mudanças no conselho"), $"{meeting.Notes.Count}", string.Join("\n", (meeting.NotesUi != null && meeting.NotesUi.Count == meeting.Notes.Count ? meeting.NotesUi : meeting.Notes).Select(n => "• " + MailScreen.Escape(n))));
            }
        }

        private void FillSpeech(Row row, CouncilSpeech speech, IaNation nation, WorldCapture capture, int era, bool reply)
        {
            row.Portfolio = null;
            Minister minister = nation.Council.FirstOrDefault(m => m.Portfolio == speech.Portfolio);
            Transform top = row.Card.Find("Table/Top");
            string name = minister != null ? $"{CouncilBank.TitleUi(minister.Portfolio, era, minister.Gender)} {minister.Name}" : CouncilBank.PortfolioNames.TryGetValue(speech.Portfolio, out string portfolioName) ? L.T(portfolioName) : speech.Portfolio;
            B.SetLabel(top, "TitleGroup/Title", MailScreen.Escape(name));
            B.SetChip(top, "StatsTable/PopCount", PortfolioName(speech.Portfolio));
            if (minister != null)
            {
                B.SetVisible(top.Find("StatsTable/Fortification"), true);
                B.SetChip(top, "StatsTable/Fortification", L.F("Credibilidade {0}", minister.Credibility));
                B.SetVisible(top.Find("StatsTable/ExtensionsCount"), true);
                B.SetChip(top, "StatsTable/ExtensionsCount", reply && speech.AffectionChange != 0
                    ? (speech.AffectionChange > 0 ? $"<c={Green}>" + L.F("Apreço +{0}", speech.AffectionChange) + "</c>" : $"<c={Red}>" + L.F("Apreço {0}", speech.AffectionChange) + "</c>")
                    : AffectionChip(minister.Affection));
                B.Tip(top, "StatsTable/Fortification", L.T("Credibilidade"), L.T("Sobe e desce com os números da pasta dele no jogo. Ministro com credibilidade baixa costuma errar."));
                B.Tip(top, "StatsTable/ExtensionsCount", L.T("Apreço por você"), reply && speech.AffectionChange != 0
                    ? L.F("O que ele achou da sua fala: {0:+0;-0}. Agora: {1:+0;-0} (de -100 a 100).", speech.AffectionChange, minister.Affection)
                    : L.F("{0:+0;-0}, de -100 a 100. Sobe quando você segue os conselhos dele ou o trata bem; desce quando faz o contrário.", minister.Affection));
            }
            else
            {
                B.HideChild(top, "StatsTable/Fortification");
                B.HideChild(top, "StatsTable/ExtensionsCount");
            }
            B.SetVisible(row.Button.transform, false);
            MailScreen.FitTitle(top, 12f);
            B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", PortfolioName(speech.Portfolio), minister != null ? MailScreen.Escape(TraitsUi(minister)) + (string.IsNullOrWhiteSpace(minister.Voice) ? string.Empty : "\n" + MailScreen.Escape(L.T(minister.Voice))) : string.Empty); // voz: do banco padrão, marcado com L.N
            string body = MailScreen.Escape(speech.Text);
            if (!string.IsNullOrWhiteSpace(speech.Advice))
            {
                body += $"\n\n<c={Gold}>" + L.T("Conselho:") + $"</c> {MailScreen.Escape(speech.Advice)}";
            }
            MailScreen.SetBody(row.Body, body);
        }

        private void FillPlayer(Row row, CouncilExchange exchange)
        {
            row.Portfolio = null;
            Transform top = row.Card.Find("Table/Top");
            B.SetLabel(top, "TitleGroup/Title", L.T("Você"));
            B.SetChip(top, "StatsTable/PopCount", L.F("Turno {0}", exchange.Turn));
            B.HideChild(top, "StatsTable/Fortification");
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.SetVisible(row.Button.transform, false);
            MailScreen.FitTitle(top, 12f);
            B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", L.T("Sua fala"), L.T("O que você disse ao conselho. As reações vêm logo abaixo."));
            MailScreen.SetBody(row.Body, $"<i>{MailScreen.Escape(exchange.PlayerText)}</i>");
        }

        private void FillNotice(Row row, string title, string chip, string text)
        {
            row.Portfolio = null;
            Transform top = row.Card.Find("Table/Top");
            B.SetLabel(top, "TitleGroup/Title", title);
            B.SetChip(top, "StatsTable/PopCount", chip ?? string.Empty);
            B.Tip(top, "StatsTable/PopCount", title, text ?? string.Empty);
            B.HideChild(top, "StatsTable/Fortification");
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.SetVisible(row.Button.transform, false);
            MailScreen.FitTitle(top, 12f);
            B.HideOutputs(top);
            MailScreen.SetBody(row.Body, text ?? string.Empty);
            B.SetVisible(row.Body, !string.IsNullOrEmpty(text));
        }

        private void FillMinisters(IaNation nation, WorldCapture capture, int era)
        {
            List<Minister> council = nation.Council.ToList();
            sectionTitleLabel.Text = L.F("Ministros · {0}", council.Count);
            if (council.Count == 0)
            {
                SetRowCount(1);
                FillNotice(rows[0], L.T("O conselho ainda não se formou"), L.T("Espere a reunião"), L.T("Os ministros chegam com a primeira reunião do turno."));
                return;
            }
            SetRowCount(council.Count);
            if (confirmFire != null && Time.unscaledTime > confirmUntil)
            {
                confirmFire = null;
            }
            for (int i = 0; i < council.Count; i++)
            {
                Minister minister = council[i];
                Row row = rows[i];
                row.Portfolio = minister.Portfolio;
                Transform top = row.Card.Find("Table/Top");
                B.SetLabel(top, "TitleGroup/Title", MailScreen.Escape($"{CouncilBank.TitleUi(minister.Portfolio, era, minister.Gender)} {minister.Name}"));
                B.SetChip(top, "StatsTable/PopCount", PortfolioName(minister.Portfolio));
                B.SetVisible(top.Find("StatsTable/Fortification"), true);
                B.SetChip(top, "StatsTable/Fortification", L.F("Credibilidade {0}", minister.Credibility));
                B.SetVisible(top.Find("StatsTable/ExtensionsCount"), true);
                B.SetChip(top, "StatsTable/ExtensionsCount", AffectionChip(minister.Affection));
                bool confirming = confirmFire == minister.Portfolio;
                B.SetVisible(row.Button.transform, true);
                B.SetButtonText(row.Button, confirming ? L.T("Confirmar") : L.T("Demitir"));
                MailScreen.FitTitle(top, row.Button.GetComponent<UITransform>().Width + 20f);
                B.HideOutputs(top);
                B.Tip(top, "StatsTable/PopCount", PortfolioName(minister.Portfolio), MailScreen.Escape(TraitsUi(minister)));
                B.Tip(top, "StatsTable/Fortification", L.T("Credibilidade"), L.T("Sobe e desce com os números da pasta dele no jogo."));
                B.Tip(top, "StatsTable/ExtensionsCount", L.T("Apreço por você"), L.F("{0:+0;-0}, de -100 a 100. Lealdade {1}, ambição {2}.", minister.Affection, minister.Loyalty, minister.Ambition));
                B.Tip(row.Button.transform, string.Empty, confirming ? L.T("Confirmar a demissão") : L.T("Demitir"),
                    confirming ? L.T("Clique de novo para demitir. Outra pessoa assume a pasta; o resto do conselho sente o golpe.")
                    : L.T("Tira o ministro do cargo e põe outra pessoa no lugar. Pede confirmação."));
                var lines = new List<string>
                {
                    L.F("Traços: {0}", MailScreen.Escape(TraitsUi(minister))),
                };
                if (!string.IsNullOrWhiteSpace(minister.Voice))
                {
                    lines.Add(L.F("Voz: {0}", MailScreen.Escape(L.T(minister.Voice)))); // voz: do banco padrão, marcado com L.N
                }
                lines.Add(L.F("Lealdade {0} · ambição {1} · no cargo desde o turno {2}", minister.Loyalty, minister.Ambition, minister.Since));
                var likes = minister.Sympathies.Where(p => p.Value > 0).Select(p => capture.Empire(p.Key)?.Culture ?? "E" + p.Key).ToList();
                var dislikes = minister.Sympathies.Where(p => p.Value < 0).Select(p => capture.Empire(p.Key)?.Culture ?? "E" + p.Key).ToList();
                if (likes.Count > 0 || dislikes.Count > 0)
                {
                    string joiner = " " + L.T("e") + " ";
                    string liked = MailScreen.Escape(string.Join(joiner, likes));
                    string disliked = MailScreen.Escape(string.Join(joiner, dislikes));
                    lines.Add(likes.Count > 0 && dislikes.Count > 0 ? L.F("Simpatiza com {0}; desconfia de {1}.", liked, disliked)
                        : likes.Count > 0 ? L.F("Simpatiza com {0}.", liked)
                        : L.F("desconfia de {0}.", disliked));
                }
                if (!string.IsNullOrWhiteSpace(minister.Opinion))
                {
                    lines.Add($"<c={Gold}>" + L.T("Números da pasta:") + "</c> " + MailScreen.Escape(minister.OpinionUi ?? minister.Opinion));
                }
                MailScreen.SetBody(row.Body, string.Join("\n", lines));
            }
        }

        private void FillHistory(IaWorld world, IaNation nation, WorldCapture capture, int era, int turn)
        {
            List<CouncilMeeting> meetings = world.PlayerCouncil.Where(m => m.Turn < turn && m.Status == "pronta").OrderByDescending(m => m.Turn).ToList();
            sectionTitleLabel.Text = L.F("Reuniões anteriores · {0}", meetings.Count);
            if (meetings.Count == 0)
            {
                SetRowCount(1);
                FillNotice(rows[0], L.T("Nenhuma reunião anterior"), "—", L.T("As reuniões dos turnos passados aparecem aqui (as últimas 12)."));
                return;
            }
            SetRowCount(meetings.Count);
            for (int i = 0; i < meetings.Count; i++)
            {
                CouncilMeeting meeting = meetings[i];
                Row row = rows[i];
                row.Portfolio = null;
                Transform top = row.Card.Find("Table/Top");
                B.SetLabel(top, "TitleGroup/Title", L.F("Reunião do turno {0}", meeting.Turn));
                B.SetChip(top, "StatsTable/PopCount", L.F("{0} fala(s)", meeting.Speeches.Count - 1));
                B.SetVisible(top.Find("StatsTable/Fortification"), true);
                B.SetChip(top, "StatsTable/Fortification", L.F("{0} resposta(s) sua(s)", meeting.Exchanges.Count));
                // As linhas são reaproveitadas entre as seções: sem trocar o tooltip, ficava o da fala anterior.
                B.Tip(top, "StatsTable/PopCount", L.T("Falas"), L.T("Quantos ministros falaram nessa reunião, fora a Mão."));
                B.Tip(top, "StatsTable/Fortification", L.T("Suas respostas"), L.T("Quantas vezes você respondeu ao conselho nessa reunião."));
                B.HideChild(top, "StatsTable/ExtensionsCount");
                B.SetVisible(row.Button.transform, false);
                MailScreen.FitTitle(top, 12f);
                B.HideOutputs(top);
                var lines = new List<string>();
                CouncilSpeech hand = meeting.Speeches.FirstOrDefault(s => s.Portfolio == "mao");
                if (hand != null)
                {
                    lines.Add(MailScreen.Escape(hand.Text));
                }
                foreach (CouncilSpeech speech in meeting.Speeches.Where(s => s.Portfolio != "mao"))
                {
                    lines.Add($"<c={Gold}>{PortfolioName(speech.Portfolio)}:</c> " + MailScreen.Escape(string.IsNullOrWhiteSpace(speech.Advice) ? speech.Text : speech.Advice));
                }
                foreach (CouncilExchange exchange in meeting.Exchanges)
                {
                    lines.Add("<i>" + L.F("Você: {0}", MailScreen.Escape(exchange.PlayerText)) + "</i>");
                }
                MailScreen.SetBody(row.Body, string.Join("\n", lines));
            }
        }

        private static string PortfolioName(string portfolio)
        {
            return portfolio == "mao" ? L.T("A Mão") : CouncilBank.PortfolioNames.TryGetValue(portfolio ?? string.Empty, out string name) ? L.T(name) : portfolio; // nomes marcados com L.N no CouncilBank
        }

        /// <summary>Traços do ministro para a tela (os do banco padrão estão marcados com L.N).</summary>
        private static string TraitsUi(Minister minister)
        {
            return string.Join(", ", minister.Traits.Select(t => CouncilBank.TraitUi(t)));
        }

        private static string AffectionChip(int affection)
        {
            if (affection >= 20) return $"<c={Green}>" + L.F("Apreço +{0}", affection) + "</c>";
            if (affection <= -20) return $"<c={Red}>" + L.F("Apreço {0}", affection) + "</c>";
            return affection == 0 ? L.T("Apreço 0") : L.F("Apreço {0:+0;-0}", affection);
        }

        private void SetRowCount(int count)
        {
            while (rows.Count < count)
            {
                int index = rows.Count;
                Transform card = listTable.InstantiateChild(cardSample, "Row" + index).transform;
                Transform top = card.Find("Table/Top");
                UIButton button = top.Find("LiberateButton").GetComponent<UIButton>();
                var row = new Row { Card = card, Body = MailScreen.AddBody(card), Button = button };
                button.LeftClick += b => FireClicked(rows[index].Portfolio);
                rows.Add(row);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                B.SetVisible(rows[i].Card, i < count);
                if (i < count)
                {
                    B.SetVisible(rows[i].Body, true);
                }
            }
            B.SetVisible(cardSample, false);
        }

        private void FireClicked(string portfolio)
        {
            if (portfolio == null)
            {
                return;
            }
            if (confirmFire != portfolio || Time.unscaledTime > confirmUntil)
            {
                confirmFire = portfolio;
                confirmUntil = Time.unscaledTime + 5f;
                nextRefresh = 0;
                return;
            }
            confirmFire = null;
            // O resultado do Fire (em português) também vai para a IA; a tela monta a própria frase, traduzida.
            IaModule module = IaModule.Instance;
            IaNation nation = module != null && module.PlayerIndex >= 0 ? IaModule.World?.Ensure(module.PlayerIndex) : null;
            Minister old = nation?.Council.FirstOrDefault(m => m.Portfolio == portfolio);
            int era = module != null ? TurnCapture.Latest?.Empire(module.PlayerIndex)?.EraIndex ?? 0 : 0;
            string oldName = old != null ? $"{CouncilBank.TitleUi(old.Portfolio, era, old.Gender)} {old.Name}" : null;
            string result = PlayerCouncil.Fire(portfolio);
            if (result.StartsWith("ok") && oldName != null)
            {
                Minister newcomer = nation.Council.FirstOrDefault(m => m.Portfolio == portfolio);
                SetFeedback(L.F("Demissão de {0}; no lugar entra {1}", oldName, newcomer?.Name ?? L.T("ninguém")), false);
                return;
            }
            SetFeedback(result.Replace("ok: ", string.Empty).Replace("erro: ", string.Empty), !result.StartsWith("ok"));
        }

        // ---------------- Desenvolvimento e remoção ----------------

        /// <summary>Comandos: "abrir", "fechar", "secao 0..2".</summary>
        internal static string DevCommand(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0] : "abrir";
            if (Instance == null)
            {
                return "erro: tela do conselho não existe (abra uma partida)";
            }
            switch (sub)
            {
                case "fechar":
                    SetOpen(false);
                    return "ok: fechada";
                case "secao":
                    SetOpen(true);
                    if (parts.Length > 1 && int.TryParse(parts[1], out int section))
                    {
                        Instance.SelectSection((Section)Mathf.Clamp(section, 0, SectionNames.Length - 1));
                    }
                    return "ok";
                default:
                    SetOpen(true);
                    return "ok: aberta";
            }
        }

        /// <summary>Remoção segura (recarga do núcleo, saída da partida): igual à do correio.</summary>
        internal static void DestroyWindow()
        {
            CouncilScreen window = Instance;
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
                Plugin.Log.LogWarning($"Remoção da tela do conselho: {ex.Message}");
            }
            window.gameObject.SetActive(false);
            Destroy(window.gameObject);
        }
    }
}
