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
using CurrencyMod.Diplomacy.Capture;
using CurrencyMod.Diplomacy.Council;
using CurrencyMod.NativeUI;
using UnityEngine;
using B = CurrencyMod.NativeUI.NativeBankWindow;

namespace CurrencyMod.Diplomacy.UI
{
    /// <summary>
    /// The player's council screen (design §10.6), in the same frame as the Mail screen (clone of the Settings screen, in
    /// the game's full-screen group):
    ///   left: Meeting of the turn, Ministers, Previous meetings;
    ///   center: the speeches (the Hand opens, each minister with title, portfolio, credibility and regard for you), your
    ///           replies and the reactions; or the ministers' profiles, with the dismiss button;
    ///   right: speak to the council.
    /// The content comes from AiModule.World.PlayerCouncil; the API calls live in PlayerCouncil.
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
        private const float ListWidth = 928f; // the cards sit 26 px inside the list and 34 px narrower: they line up with the divider line
        /// <summary>The list ends above the control bar (which sits over the full screen up to x≈560).</summary>
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
        private Transform scheduleCard;
        private UIButton scheduleButton;
        private Transform fieldItem;
        private UITextField field;
        private UIButton sendButton;
        private UIButton retryButton;
        private string feedback;
        private bool feedbackIsError;
        /// <summary>Turn the speak/summon feedback belongs to: it never carries over to the next turn.</summary>
        private int feedbackTurn;
        /// <summary>Appoint/fire result, shown for a few seconds in the ministers title (not in the speak/summon cards).</summary>
        private string cabinetFeedback;
        private bool cabinetFeedbackIsError;
        private float cabinetFeedbackUntil;
        private string confirmFire;
        private float confirmUntil;
        /// <summary>Your speeches and replies already shown: when this changes, the list scrolls to your last speech.</summary>
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
            /// <summary>Card of a vacant portfolio (the button opens the candidates) or of a candidate (the button appoints).</summary>
            public string Vacant;
            public string Candidate;
        }

        // ---------------- Creation ----------------

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
            Plugin.Log.LogInfo("Council full screen created.");
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
                Plugin.Log.LogError($"Council: failed to build the speak panel: {ex}");
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

        // ---------------- Right panel: speak ----------------

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
                // TextChange is hooked in Update (EnsureTextChange), after the field loads.
                NativeUIKit.Tip(fieldItem, L.T("Falar ao conselho"), L.F("Enter quebra a linha. Até {0} palavras. Cada fala sua custa uma chamada à API.", PlayerCouncil.MaxPlayerWords));
            }
            else
            {
                Plugin.Log.LogWarning("Council: text field donor not found.");
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
            // Automatic meetings: how often the council meets on its own (Xenouzik's feedback, 2026-10-14).
            scheduleCard = sideTable.InstantiateChild(sideSample, "ScheduleCard").transform;
            scheduleButton = scheduleCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            scheduleButton.LeftClick += b =>
            {
                PlayerCouncil.CycleInterval();
                nextRefresh = 0;
            };
            B.SetVisible(sideSample, false);
        }

        private void FillSchedule()
        {
            B.SetVisible(scheduleCard, true);
            Transform top = scheduleCard.Find("Table/Top");
            B.SetLabel(top, "TitleGroup/Title", L.T("Reuniões automáticas"));
            B.SetChip(top, "StatsTable/PopCount", PlayerCouncil.DescribeInterval());
            B.HideChild(top, "StatsTable/Fortification");
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.SetVisible(scheduleButton.transform, true);
            B.SetButtonText(scheduleButton, L.T("Mudar"));
            MailScreen.FitTitle(top, scheduleButton.GetComponent<UITransform>().Width + 20f);
            B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", L.T("Reuniões automáticas"), L.T("De quantos em quantos turnos o conselho se reúne sozinho. No automático, segue a velocidade do jogo (7 turnos no Normal). Cada reunião é uma chamada à API."));
            B.Tip(scheduleButton.transform, string.Empty, L.T("Próximo intervalo"), L.T("Passa para o próximo intervalo: automático, 3, 5, 7, 10, 15, 20 turnos ou só por audiência."));
        }

        private void Speak()
        {
            if (field == null)
            {
                return;
            }
            if (PlayerCouncil.Meeting(AiModule.World, AiModule.Instance?.CurrentTurn ?? -1) == null)
            {
                // Council dismissed: the button summons an audience, and what is written goes with it (answered after the meeting).
                string convened = PlayerCouncil.Convene(field.Text);
                if (convened.StartsWith("ok"))
                {
                    field.ReplaceText(string.Empty);
                }
                SetFeedback(convened.Replace("ok: ", string.Empty).Replace("error: ", string.Empty), convened.StartsWith("error"));
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
                SetFeedback(result.Replace("error: ", string.Empty), true);
            }
        }

        private void SetFeedback(string message, bool isError)
        {
            feedback = message;
            feedbackIsError = isError;
            feedbackTurn = AiModule.Instance?.CurrentTurn ?? 0;
            nextRefresh = 0;
        }

        private void SetCabinetFeedback(string message, bool isError)
        {
            cabinetFeedback = message;
            cabinetFeedbackIsError = isError;
            cabinetFeedbackUntil = Time.unscaledTime + 8f;
            nextRefresh = 0;
        }

        private void FillSide(CouncilMeeting meeting, int turn)
        {
            sideTitleLabel.Text = L.T("Falar ao conselho");
            if (feedback != null && feedbackTurn != turn)
            {
                feedback = null;
            }
            B.SetVisible(speakCard, true);
            B.SetVisible(sendCard, true);
            Transform top = speakCard.Find("Table/Top");
            B.SetLabel(top, "TitleGroup/Title", L.T("Você fala ao conselho"));
            B.SetChip(top, "StatsTable/PopCount", L.F("Turno {0}", turn));
            int used = meeting?.Exchanges.Count ?? 0;
            B.SetChip(top, "StatsTable/Fortification", L.P(used, "{0} fala sua", "{0} falas suas"));
            B.HideChild(top, "StatsTable/ExtensionsCount");
            MailScreen.FitTitle(top, 12f);
            B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", L.T("Reunião do turno"), L.T("Você responde à reunião deste turno. Depois o conselho só volta a se reunir sozinho no intervalo, ou quando você convocar uma audiência."));
            B.Tip(top, "StatsTable/Fortification", L.T("Suas falas"), L.T("Até 8 por turno. Os ministros respondem com o que sabem do reino e mudam o apreço por você conforme o que ouvem."));

            if (meeting == null)
            {
                // Council dismissed this turn: the send card becomes the audience request.
                B.SetLabel(top, "TitleGroup/Title", L.T("O conselho está dispensado"));
                B.SetChip(top, "StatsTable/Fortification", PlayerCouncil.NextAutoLabel(AiModule.World, turn));
                B.Tip(top, "StatsTable/Fortification", L.T("Próxima reunião"), PlayerCouncil.DescribeInterval() + ". " + L.T("Uma audiência agora recomeça a contagem. Mude o intervalo no cartão Reuniões automáticas, abaixo."));
                Transform idleTop = sendCard.Find("Table/Top");
                string idleTitle = feedback ?? L.T("Peça uma audiência");
                B.SetLabel(idleTop, "TitleGroup/Title", feedback != null && feedbackIsError ? $"<c={Red}>{idleTitle}</c>" : idleTitle);
                B.SetChip(idleTop, "StatsTable/PopCount", L.T("1 chamada à API"));
                B.HideChild(idleTop, "StatsTable/Fortification");
                B.HideChild(idleTop, "StatsTable/ExtensionsCount");
                B.SetVisible(sendButton.transform, true);
                B.SetButtonText(sendButton, L.T("Convocar"));
                MailScreen.FitTitle(idleTop, sendButton.GetComponent<UITransform>().Width + 20f);
                B.HideOutputs(idleTop);
                B.Tip(idleTop, "StatsTable/PopCount", L.T("Custo"), L.T("A audiência é uma reunião completa do conselho: uma chamada à API."));
                B.Tip(sendButton.transform, string.Empty, L.T("Convocar"), L.T("Reúne o conselho agora. Depois que a reunião sair, você pode falar com os ministros."));
                B.SetVisible(stateCard, false);
                FillSchedule();
                foreach (Transform item in new[] { speakCard, fieldItem, sendCard, stateCard, scheduleCard })
                {
                    item?.SetAsLastSibling();
                }
                return;
            }

            bool ready = meeting.Status == "pronta";
            bool listening = PlayerCouncil.Thinking && ready || meeting.Exchanges.Any(e => e.Status == "pensando");
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

            // Meeting status (and summon again, if the call failed).
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

            FillSchedule();
            Transform[] order = { speakCard, fieldItem, sendCard, stateCard, scheduleCard };
            foreach (Transform item in order)
            {
                item?.SetAsLastSibling();
            }
        }

        // ---------------- Opening and sections ----------------

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
            NativeUIKit.FixLayoutAfterPaste(field);
            if (wasShown && !Shown)
            {
                NativeUIKit.ReleaseTextFocus(); // the game hid the screen without going through SetOpen
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
                Plugin.Log.LogError($"Council: {ex}");
                nextRefresh = Time.unscaledTime + 5f;
            }
        }

        /// <summary>Scrolls the list until the card is at the top (UIScrollView.ScrollTo only guarantees it is visible).</summary>
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
                Plugin.Log.LogWarning($"Council: scrolling the list: {ex.Message}");
            }
        }

        // ---------------- Content ----------------

        private void RefreshContent()
        {
            AiModule module = AiModule.Instance;
            WorldCapture capture = TurnCapture.Latest;
            AiWorld world = AiModule.World;
            if (module == null || world == null || module.PlayerIndex < 0 || capture == null || capture.Guid != world.GameGuid)
            {
                descriptionLabel.Text = L.T("O conselho se reúne quando a partida estiver carregada.");
                SetRowCount(1);
                FillNotice(rows[0], L.T("Aguardando o turno"), L.T("Conselho fechado"), null);
                return;
            }
            int me = module.PlayerIndex;
            int turn = module.CurrentTurn;
            AiNation nation = world.Ensure(me);
            CapturedEmpire empire = capture.Empire(me);
            int era = empire?.EraIndex ?? 0;
            CouncilMeeting meeting = PlayerCouncil.Meeting(world, turn);
            // With no meeting this turn, the section shows the last one (the council meets every N turns or by audience).
            CouncilMeeting shown = meeting ?? PlayerCouncil.LastMeeting(world);
            if (meeting != null && meeting.Status == "pronta" && currentSection == Section.Meeting && (!meeting.Seen || meeting.UnseenReplies > 0))
            {
                meeting.Seen = true;
                meeting.UnseenReplies = 0;
                module.MarkDirty();
            }
            PlayerCouncil.UrgentSeenTurn = turn;
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
                    FillMeeting(shown, nation, capture, era, turn);
                    break;
            }
            FillSide(meeting, turn);
        }

        private static string DescribeCouncil(AiNation nation, int turn)
        {
            if (!AiConfig.PlayerCouncil.Value)
            {
                return L.T("O conselho está desligado nas configurações do mod.");
            }
            if (nation.Council.Count == 0)
            {
                return L.F("Turno {0}. O conselho se forma na primeira reunião.", turn);
            }
            int loyal = nation.Council.Count(m => m.Affection >= 20);
            int cold = nation.Council.Count(m => m.Affection <= -20);
            return L.P(nation.Council.Count, "Turno {1}. {0} conselheiro", "Turno {1}. {0} conselheiros", turn)
                + (loyal > 0 ? " · " + L.F("{0} com apreço por você", loyal) : string.Empty)
                + (cold > 0 ? " · " + L.P(cold, "{0} ressentido", "{0} ressentidos") : string.Empty) + ".";
        }

        private void FillMeeting(CouncilMeeting meeting, AiNation nation, WorldCapture capture, int era, int turn)
        {
            // Urgent topics behind the "!" badge go on top, so opening the council always shows why it called you.
            List<string> urgent = PlayerCouncil.UrgentTopics(capture, AiModule.Instance?.PlayerIndex ?? -1);
            int urgentRows = urgent.Count > 0 ? 1 : 0;
            if (meeting == null)
            {
                sectionTitleLabel.Text = L.T("Reunião do conselho");
                SetRowCount(1 + urgentRows);
                if (urgentRows > 0)
                {
                    FillUrgent(rows[0], urgent);
                }
                FillNotice(rows[urgentRows], L.T("O conselho ainda não se reuniu"), PlayerCouncil.NextAutoLabel(AiModule.World, turn),
                    L.T("O conselho se reúne sozinho de tempos em tempos. Se quiser ouvi-lo antes, convoque uma audiência à direita."));
                return;
            }
            if (meeting.Status != "pronta")
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
            // First row: the summary (each minister's advice on one line, built from the response itself, no extra call).
            List<CouncilSpeech> advised = meeting.Speeches.Where(s => s.Portfolio != "mao" && !string.IsNullOrWhiteSpace(s.Advice ?? s.Text)).ToList();
            int summary = advised.Count > 0 ? 1 : 0;
            int count = summary + meeting.Speeches.Count + meeting.Exchanges.Sum(e => 1 + e.Replies.Count + (e.Status == "pronta" ? 0 : 1)) + (meeting.Notes.Count > 0 ? 1 : 0);
            int spoke = meeting.Speeches.Count - 1;
            sectionTitleLabel.Text = meeting.Turn == turn
                ? L.P(spoke, "Reunião do turno {1} · {0} ministro falou", "Reunião do turno {1} · {0} ministros falaram", meeting.Turn)
                : PlayerCouncil.NextAutoTurn(AiModule.World) == int.MaxValue
                    ? L.F("Última reunião: turno {0} · a próxima só quando você convocar", meeting.Turn)
                    : L.F("Última reunião: turno {0} · a próxima é no turno {1}, ou quando você convocar", meeting.Turn, PlayerCouncil.NextAutoTurn(AiModule.World));
            SetRowCount(count + urgentRows);
            int index = 0;
            if (urgentRows > 0)
            {
                FillUrgent(rows[index++], urgent);
            }
            // Newest first (melhorias item 38): your latest speech and its replies at the top, then the earlier ones,
            // and the turn's meeting at the end. Inside each exchange the order stays natural (you speak, they answer).
            string exchangesKey = $"{meeting.Turn}:{meeting.Exchanges.Count}:{meeting.Exchanges.Count(e => e.Status == "pronta")}";
            if (shownExchanges != null && exchangesKey != shownExchanges && meeting.Exchanges.Count > 0)
            {
                scrollTarget = rows[0].Card; // the newest exchange is the first row
                scrollAt = Time.unscaledTime + 0.3f;
            }
            shownExchanges = exchangesKey;
            for (int e = meeting.Exchanges.Count - 1; e >= 0; e--)
            {
                CouncilExchange exchange = meeting.Exchanges[e];
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
            if (summary > 0)
            {
                FillSummary(rows[index++], advised, nation, era);
            }
            foreach (CouncilSpeech speech in meeting.Speeches)
            {
                FillSpeech(rows[index++], speech, nation, capture, era, reply: false);
            }
            if (meeting.Notes.Count > 0)
            {
                FillNotice(rows[index++], L.T("Mudanças no conselho"), $"{meeting.Notes.Count}", string.Join("\n", (meeting.NotesUi != null && meeting.NotesUi.Count == meeting.Notes.Count ? meeting.NotesUi : meeting.Notes).Select(n => "• " + MailScreen.Escape(n))));
            }
        }

        private void FillSpeech(Row row, CouncilSpeech speech, AiNation nation, WorldCapture capture, int era, bool reply)
        {
            row.Portfolio = null;
            Minister minister = nation.Council.FirstOrDefault(m => m.Portfolio == speech.Portfolio);
            Transform top = row.Card.Find("Table/Top");
            B.HideChild(top, "StatsTable/Chip3");
            row.Vacant = null;
            row.Candidate = null;
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
                B.Tip(top, "StatsTable/Fortification", L.T("Credibilidade"), L.T("Sobe e desce com os números da pasta no jogo. Ministro com credibilidade baixa costuma errar."));
                B.Tip(top, "StatsTable/ExtensionsCount", L.T("Apreço por você"), reply && speech.AffectionChange != 0
                    ? L.F("Reação à sua fala: {0:+0;-0}. Agora: {1:+0;-0} (de -100 a 100).", speech.AffectionChange, minister.Affection)
                    : L.F("{0:+0;-0}, de -100 a 100. Sobe quando você segue os conselhos desta pasta ou trata bem quem a ocupa; desce quando faz o contrário.", minister.Affection));
            }
            else
            {
                B.HideChild(top, "StatsTable/Fortification");
                B.HideChild(top, "StatsTable/ExtensionsCount");
            }
            B.SetVisible(row.Button.transform, false);
            MailScreen.FitTitle(top, 12f);
            Texture2D speakerPortrait = minister != null ? Portraits.Get(minister.Portfolio, era, minister.Gender, minister.PortraitVariant) : null;
            SetPortrait(row, speakerPortrait);
            if (speakerPortrait != null) B.HideOutputs(top, PortraitCardHeight); else B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", PortfolioName(speech.Portfolio), minister != null ? MailScreen.Escape(TraitsUi(minister)) + (string.IsNullOrWhiteSpace(minister.Voice) ? string.Empty : "\n" + MailScreen.Escape(L.T(minister.Voice))) : string.Empty); // voice: from the default bank, marked with L.N
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
            B.HideChild(top, "StatsTable/Chip3");
            row.Vacant = null;
            row.Candidate = null;
            B.SetLabel(top, "TitleGroup/Title", L.T("Você"));
            B.SetChip(top, "StatsTable/PopCount", L.F("Turno {0}", exchange.Turn));
            B.HideChild(top, "StatsTable/Fortification");
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.SetVisible(row.Button.transform, false);
            MailScreen.FitTitle(top, 12f);
            SetPortrait(row, null);
            B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", L.T("Sua fala"), L.T("O que você disse ao conselho. As reações vêm logo abaixo."));
            MailScreen.SetBody(row.Body, $"<i>{MailScreen.Escape(exchange.PlayerText)}</i>");
        }

        /// <summary>Meeting summary: one line per minister, with their portfolio and advice.</summary>
        private void FillSummary(Row row, List<CouncilSpeech> advised, AiNation nation, int era)
        {
            var lines = new List<string>();
            foreach (CouncilSpeech speech in advised)
            {
                Minister minister = nation.Council.FirstOrDefault(m => m.Portfolio == speech.Portfolio);
                string who = minister != null ? $"{PortfolioName(speech.Portfolio)} ({minister.Name})" : PortfolioName(speech.Portfolio);
                string advice = !string.IsNullOrWhiteSpace(speech.Advice) ? speech.Advice : FirstSentence(speech.Text);
                lines.Add($"• <c={Gold}>{MailScreen.Escape(who)}</c>: {MailScreen.Escape(advice)}");
            }
            FillNotice(row, L.T("Resumo do conselho"), L.P(advised.Count, "{0} conselho", "{0} conselhos"), string.Join("\n", lines));
            B.Tip(row.Card.Find("Table/Top"), "StatsTable/PopCount", L.T("Resumo do conselho"), L.T("O conselho de cada ministro em uma linha. As falas completas vêm logo abaixo."));
        }

        private static string FirstSentence(string text)
        {
            text = (text ?? string.Empty).Trim();
            int end = text.IndexOfAny(new[] { '.', '!', '?' });
            return end > 0 && end < text.Length - 1 ? text.Substring(0, end + 1) : text;
        }

        private void FillUrgent(Row row, List<string> urgent)
        {
            FillNotice(row, L.T("O conselho quer falar com você:"), $"<c={Red}>!</c> {urgent.Count}",
                string.Join("\n", urgent.Select(t => "• " + MailScreen.Escape(t))) + "\n" + L.T("Convoque uma audiência à direita para ouvir o conselho sobre isso."));
        }

        private void FillNotice(Row row, string title, string chip, string text)
        {
            row.Portfolio = null;
            Transform top = row.Card.Find("Table/Top");
            B.HideChild(top, "StatsTable/Chip3");
            row.Vacant = null;
            row.Candidate = null;
            B.SetLabel(top, "TitleGroup/Title", title);
            B.SetChip(top, "StatsTable/PopCount", chip ?? string.Empty);
            B.Tip(top, "StatsTable/PopCount", title, text ?? string.Empty);
            B.HideChild(top, "StatsTable/Fortification");
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.SetVisible(row.Button.transform, false);
            MailScreen.FitTitle(top, 12f);
            SetPortrait(row, null);
            B.HideOutputs(top);
            MailScreen.SetBody(row.Body, text ?? string.Empty);
            B.SetVisible(row.Body, !string.IsNullOrEmpty(text));
        }

        private void FillMinisters(AiNation nation, WorldCapture capture, int era)
        {
            AiModule module = AiModule.Instance;
            int turn = module?.CurrentTurn ?? 0;
            if (module != null)
            {
                CouncilEngine.Ensure(AiModule.World, nation, capture, turn);
            }
            List<Minister> council = nation.Council.ToList();
            List<string> vacant = CouncilBank.Portfolios.Where(p => p != "mao" && !council.Any(m => m.Portfolio == p)).ToList();
            List<string> candidates = expandedSeat != null && nation.CouncilCandidates.TryGetValue(expandedSeat, out List<string> ids) ? ids : new List<string>();
            int cost = CouncilEngine.AppointCost(era);
            double influence = module != null ? capture.Empire(module.PlayerIndex)?.Influence ?? 0 : 0;
            sectionTitleLabel.Text = cabinetFeedback != null && Time.unscaledTime < cabinetFeedbackUntil
                ? (cabinetFeedbackIsError ? $"<c={Red}>{cabinetFeedback}</c>" : cabinetFeedback)
                : L.P(vacant.Count, "Ministros · {1} no cargo · {0} pasta vaga", "Ministros · {1} no cargo · {0} pastas vagas", council.Count);
            SetRowCount(council.Count + vacant.Count + candidates.Count);
            if (confirmFire != null && Time.unscaledTime > confirmUntil)
            {
                confirmFire = null;
            }
            int index = council.Count;
            foreach (string seat in vacant)
            {
                FillVacant(rows[index++], nation, seat, era, cost, turn, seat == expandedSeat);
                if (seat == expandedSeat)
                {
                    foreach (string id in candidates)
                    {
                        Personality person = CouncilBank.ById(id);
                        Minister preview = person != null && module != null ? CouncilEngine.Preview(AiModule.World, nation, capture, person, seat, turn) : null;
                        FillCandidate(rows[index++], seat, person, preview, CouncilEngine.PortraitVariant(candidates, id), capture, era, cost, influence);
                    }
                }
            }
            for (int i = 0; i < council.Count; i++)
            {
                Minister minister = council[i];
                Row row = rows[i];
                row.Portfolio = minister.Portfolio;
                row.Vacant = null;
                row.Candidate = null;
                Transform top = row.Card.Find("Table/Top");
                B.SetLabel(top, "TitleGroup/Title", MailScreen.Escape($"{CouncilBank.TitleUi(minister.Portfolio, era, minister.Gender)} {minister.Name}"));
                B.SetChip(top, "StatsTable/PopCount", PortfolioName(minister.Portfolio));
                B.SetVisible(top.Find("StatsTable/Fortification"), true);
                B.SetChip(top, "StatsTable/Fortification", L.F("Credibilidade {0}", minister.Credibility));
                B.SetVisible(top.Find("StatsTable/ExtensionsCount"), true);
                B.SetChip(top, "StatsTable/ExtensionsCount", AffectionChip(minister.Affection));
                // Veteran: time in office pays off in the portfolio's area (the Hand has none).
                int veteran = CouncilEngine.VeteranBonus(minister, turn);
                string veteranChip = B.ExtraChip(top, 3);
                B.SetVisible(top.Find(veteranChip), minister.Portfolio != "mao");
                if (minister.Portfolio != "mao")
                {
                    B.SetChip(top, veteranChip, veteran > 0 ? $"<c={Gold}>" + L.F("Veterano +{0}%", veteran) + "</c>" : L.T("Novato"));
                    B.Tip(top, veteranChip, L.T("Bônus de veterano"), L.F("Rende +1% a cada 5 turnos no cargo na área da pasta ({0}), até +10%. Demitir zera. Agora: +{1}%.", VeteranArea(minister.Portfolio), veteran));
                }
                bool confirming = confirmFire == minister.Portfolio;
                B.SetVisible(row.Button.transform, true);
                B.SetButtonText(row.Button, confirming ? L.T("Confirmar") : L.T("Demitir"));
                MailScreen.FitTitle(top, row.Button.GetComponent<UITransform>().Width + 20f);
                Texture2D ministerPortrait = Portraits.Get(minister.Portfolio, era, minister.Gender, minister.PortraitVariant);
                SetPortrait(row, ministerPortrait);
                if (ministerPortrait != null) B.HideOutputs(top, PortraitCardHeight); else B.HideOutputs(top);
                B.Tip(top, "StatsTable/PopCount", PortfolioName(minister.Portfolio), MailScreen.Escape(TraitsUi(minister)));
                B.Tip(top, "StatsTable/Fortification", L.T("Credibilidade"), L.T("Sobe e desce com os números da pasta no jogo."));
                B.Tip(top, "StatsTable/ExtensionsCount", L.T("Apreço por você"), L.F("{0:+0;-0}, de -100 a 100. Lealdade {1}, ambição {2}.", minister.Affection, minister.Loyalty, minister.Ambition));
                B.Tip(row.Button.transform, string.Empty, confirming ? L.T("Confirmar a demissão") : L.T("Demitir"),
                    confirming ? L.T("Clique de novo para demitir. A pasta fica vaga e o bônus de veterano zera; o resto do conselho sente o golpe.")
                    : L.T("Tira o ministro do cargo; a pasta fica vaga até você nomear outro. Pede confirmação."));
                var lines = new List<string>
                {
                    L.F("Traços: {0}", MailScreen.Escape(TraitsUi(minister))),
                };
                if (!string.IsNullOrWhiteSpace(minister.Voice))
                {
                    lines.Add(L.F("Voz: {0}", MailScreen.Escape(L.T(minister.Voice)))); // voice: from the default bank, marked with L.N
                }
                lines.Add(L.F("Lealdade {0} · ambição {1} · no cargo desde o turno {2}", minister.Loyalty, minister.Ambition, minister.Since));
                string sympathies = SympathyLine(minister, capture);
                if (sympathies != null)
                {
                    lines.Add(sympathies);
                }
                if (!string.IsNullOrWhiteSpace(minister.Opinion))
                {
                    lines.Add($"<c={Gold}>" + L.T("Números da pasta:") + "</c> " + MailScreen.Escape(minister.OpinionUi ?? minister.Opinion));
                }
                MailScreen.SetBody(row.Body, string.Join("\n", lines));
            }
        }

        private void FillHistory(AiWorld world, AiNation nation, WorldCapture capture, int era, int turn)
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
                // Rows are reused from the Ministers section: without this, a minister's portrait stayed on the card.
                SetPortrait(row, null);
                B.SetLabel(top, "TitleGroup/Title", L.F("Reunião do turno {0}", meeting.Turn));
                B.SetChip(top, "StatsTable/PopCount", L.P(meeting.Speeches.Count - 1, "{0} fala", "{0} falas"));
                B.SetVisible(top.Find("StatsTable/Fortification"), true);
                B.SetChip(top, "StatsTable/Fortification", L.P(meeting.Exchanges.Count, "{0} resposta sua", "{0} respostas suas"));
                // Rows are reused across sections: without swapping the tooltip, the previous speech's one stayed.
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
            return portfolio == "mao" ? L.T("A Mão") : CouncilBank.PortfolioNames.TryGetValue(portfolio ?? string.Empty, out string name) ? L.T(name) : portfolio; // names marked with L.N in CouncilBank
        }

        /// <summary>Minister's traits for the screen (those from the default bank are marked with L.N).</summary>
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
                button.LeftClick += b => RowClicked(rows[index]);
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

        /// <summary>Vacant portfolio open in the list (shows the 3 candidates right below).</summary>
        private string expandedSeat;

        // ---------------- Portraits ----------------

        private const float PortraitSize = 104f;
        private const float PortraitMargin = 10f;
        private static readonly Dictionary<Texture2D, Amplitude.Framework.Guid> portraitGuids = new Dictionary<Texture2D, Amplitude.Framework.Guid>();
        /// <summary>Original left anchor of each card's title and chips (to remove the portrait without breaking the layout).</summary>
        private readonly Dictionary<UITransform, UIBorderAnchor> originalLeft = new Dictionary<UITransform, UIBorderAnchor>();

        /// <summary>
        /// Portrait on the left of the card's top (or none, with null): the title and chips move right and the top grows
        /// to fit the image. The image is a clone of the chip icon with a plain material and the portrait texture.
        /// </summary>
        private void SetPortrait(Row row, Texture2D texture)
        {
            Transform top = row.Card.Find("Table/Top");
            Transform image = top.Find("Portrait");
            bool show = texture != null;
            if (image == null && show)
            {
                Transform picto = cardSample.Find("Table/Top/StatsTable/PopCount/Picto");
                if (picto == null)
                {
                    return;
                }
                UITransform clone = top.GetComponent<UITransform>().InstantiateChild(picto, "Portrait");
                image = clone.transform;
                UIImage ui = image.GetComponent<UIImage>();
                ui.Material = new UIMaterialId("Default"); // the icon uses a distance field; here it is a plain image
                ui.Color = Color.white;
                NativeUIKit.Place(image, PortraitMargin, PortraitMargin, PortraitSize, PortraitSize);
            }
            if (image != null)
            {
                B.SetVisible(image, show);
                if (show)
                {
                    clonePortraitTexture(image.GetComponent<UIImage>(), texture);
                }
            }
            foreach (string path in new[] { "TitleGroup", "StatsTable" })
            {
                UITransform part = top.Find(path)?.GetComponent<UITransform>();
                if (part == null)
                {
                    continue;
                }
                if (!originalLeft.ContainsKey(part))
                {
                    originalLeft[part] = part.LeftAnchor;
                }
                part.LeftAnchor = show ? new UIBorderAnchor(true, 0f, PortraitMargin * 2 + PortraitSize + 6f, 0f) : originalLeft[part];
            }
        }

        private static void clonePortraitTexture(UIImage image, Texture2D texture)
        {
            if (!portraitGuids.TryGetValue(texture, out Amplitude.Framework.Guid guid))
            {
                guid = UIRenderingManager.Instance.RegisterTexture(texture);
                portraitGuids[texture] = guid;
            }
            image.Texture = new UITexture(guid, UITextureFlags.AlphaStraight, UITextureColorFormat.Srgb, texture);
        }

        /// <summary>Height of the top of a card with a portrait.</summary>
        private const float PortraitCardHeight = PortraitSize + PortraitMargin * 2;

        /// <summary>Where each portfolio's veteran bonus pays off (melhorias-jogando item 3).</summary>
        internal static string VeteranArea(string portfolio)
        {
            switch (portfolio)
            {
                case "agricultura": return L.T("comida");
                case "financas": return L.T("dinheiro");
                case "guerra": return L.T("força de combate");
                case "ciencia": return L.T("ciência");
                case "obras": return L.T("indústria");
                case "comercio": return L.T("dinheiro das rotas comerciais");
                case "interior": return L.T("estabilidade");
                case "fe": return L.T("força da sua religião nos seus territórios");
                case "cultura":
                case "chanceler": return L.T("influência");
                case "espionagem": return L.T("chance de interceptar cartas");
                default: return "—";
            }
        }

        private void FillVacant(Row row, AiNation nation, string seat, int era, int cost, int turn, bool open)
        {
            row.Portfolio = seat;
            row.Vacant = seat;
            row.Candidate = null;
            Transform top = row.Card.Find("Table/Top");
            B.SetLabel(top, "TitleGroup/Title", L.F("{0} · vaga", PortfolioName(seat)));
            B.SetChip(top, "StatsTable/PopCount", L.F("Nomear custa {0} de influência", cost));
            B.HideChild(top, "StatsTable/Fortification");
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.HideChild(top, "StatsTable/Chip3");
            B.SetVisible(row.Button.transform, true);
            B.SetButtonText(row.Button, open ? L.T("Fechar") : L.T("Escolher"));
            MailScreen.FitTitle(top, row.Button.GetComponent<UITransform>().Width + 20f);
            SetPortrait(row, null);
            B.HideOutputs(top);
            int patience = CouncilEngine.CandidatePatience();
            B.Tip(top, "StatsTable/PopCount", L.T("Pasta vaga"), L.F("Ninguém opina por esta pasta nem rende bônus de veterano. Os candidatos mudam na próxima era; até lá, a cada {0} turnos um deles cansa de esperar e outro entra no lugar.", patience));
            B.Tip(row.Button.transform, string.Empty, L.T("Escolher"), L.T("Mostra os 3 candidatos desta pasta, logo abaixo."));
            string body = L.F("Bônus de quem ficar no cargo: {0}, +1% a cada 5 turnos, até +10%.", VeteranArea(seat));
            // The last candidate who gave up (CouncilEngine.RotateCandidate), while it is recent.
            if (nation.CouncilCandidateLeft != null && nation.CouncilCandidateLeft.TryGetValue(seat, out string left))
            {
                string[] parts = left.Split('|');
                if (parts.Length == 3 && int.TryParse(parts[2], out int leftTurn) && turn - leftTurn < patience)
                {
                    string gone = CouncilBank.ById(parts[0])?.Name ?? "?";
                    string came = CouncilBank.ById(parts[1])?.Name ?? "?";
                    body += $"\n<c={Gold}>" + L.F("Turno {0}: {1} cansou de esperar e foi embora; {2} entrou na lista.", leftTurn, MailScreen.Escape(gone), MailScreen.Escape(came)) + "</c>";
                }
            }
            MailScreen.SetBody(row.Body, body);
        }

        /// <summary>"Likes X; distrusts Y." from the minister's sympathies, or null when there are none.</summary>
        private static string SympathyLine(Minister minister, WorldCapture capture)
        {
            var likes = minister.Sympathies.Where(p => p.Value > 0).Select(p => MailScreen.NationAndLeader(capture, p.Key, inline: true)).ToList();
            var dislikes = minister.Sympathies.Where(p => p.Value < 0).Select(p => MailScreen.NationAndLeader(capture, p.Key, inline: true)).ToList();
            if (likes.Count == 0 && dislikes.Count == 0)
            {
                return null;
            }
            string joiner = " " + L.T("e") + " ";
            string liked = MailScreen.Escape(string.Join(joiner, likes));
            string disliked = MailScreen.Escape(string.Join(joiner, dislikes));
            return likes.Count > 0 && dislikes.Count > 0 ? L.F("Simpatiza com {0}; desconfia de {1}.", liked, disliked)
                : likes.Count > 0 ? L.F("Simpatiza com {0}.", liked)
                : L.F("Desconfia de {0}.", disliked);
        }

        /// <summary>
        /// A candidate: traits, the numbers they would start with (credibility, loyalty, ambition), the sympathies, the voice
        /// and how the traits shape the advice, so the choice is informed. The portrait is the seat's variant for them.
        /// </summary>
        private void FillCandidate(Row row, string seat, Personality person, Minister preview, int variant, WorldCapture capture, int era, int cost, double influence)
        {
            row.Portfolio = seat;
            row.Vacant = null;
            row.Candidate = person?.Id;
            Transform top = row.Card.Find("Table/Top");
            string title = person != null ? CouncilBank.TitleUi(seat, era, person.Gender) + " " + person.Name : "?";
            B.SetLabel(top, "TitleGroup/Title", $"<c={Gold}>›</c> " + MailScreen.Escape(title));
            string traits = person != null ? string.Join(", ", person.Traits.Select(t => L.T(t))) : string.Empty;
            B.SetChip(top, "StatsTable/PopCount", traits.Length > 0 ? MailScreen.Escape(traits) : L.T("Sem traços marcantes"));
            B.SetVisible(top.Find("StatsTable/Fortification"), preview != null);
            B.SetVisible(top.Find("StatsTable/ExtensionsCount"), preview != null);
            string ambitionChip = B.ExtraChip(top, 3);
            B.SetVisible(top.Find(ambitionChip), preview != null);
            if (preview != null)
            {
                B.SetChip(top, "StatsTable/Fortification", L.F("Credibilidade {0}", preview.Credibility));
                B.SetChip(top, "StatsTable/ExtensionsCount", L.F("Lealdade {0}", preview.Loyalty));
                B.SetChip(top, ambitionChip, L.F("Ambição {0}", preview.Ambition));
                B.Tip(top, "StatsTable/Fortification", L.T("Credibilidade"), L.T("De 0 a 100, com que começa no cargo. Pesa quanto a opinião conta no conselho; depois sobe e desce com os números da pasta."));
                B.Tip(top, "StatsTable/ExtensionsCount", L.T("Lealdade"), L.T("De 0 a 100. Sobe quando você segue os conselhos; ministros orgulhosos ou rancorosos perdem lealdade quando são contrariados."));
                B.Tip(top, ambitionChip, L.T("Ambição"), L.T("De 0 a 100. Quanto quer subir: pesa no tom das falas e cresce quando um colega é demitido."));
            }
            bool affordable = influence >= cost;
            B.SetVisible(row.Button.transform, person != null);
            B.SetButtonText(row.Button, L.F("Nomear · {0}", cost));
            MailScreen.FitTitle(top, row.Button.GetComponent<UITransform>().Width + 20f);
            Texture2D candidatePortrait = person != null ? Portraits.Get(seat, era, person.Gender, variant) : null;
            SetPortrait(row, candidatePortrait);
            if (candidatePortrait != null) B.HideOutputs(top, PortraitCardHeight); else B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", L.T("Traços"), L.T("Os traços puxam o jeito de aconselhar: um prudente segura gastos, um ousado empurra para a frente."));
            B.Tip(row.Button.transform, string.Empty, L.T("Nomear"), affordable
                ? L.F("Nomeia para a pasta por {0} de influência. Os outros candidatos saem.", cost)
                : L.F("Influência insuficiente: custa {0}, você tem {1}.", cost, Math.Floor(influence)));
            var lines = new List<string>();
            if (person != null && !string.IsNullOrWhiteSpace(person.Voice))
            {
                lines.Add(L.F("Voz: {0}", MailScreen.Escape(L.T(person.Voice)))); // voice: from the default bank, marked with L.N
            }
            if (preview != null)
            {
                string sympathies = SympathyLine(preview, capture);
                if (sympathies != null)
                {
                    lines.Add(sympathies);
                }
                foreach (string tendency in CouncilEngine.TendenciesUi(preview))
                {
                    lines.Add("· " + tendency);
                }
            }
            MailScreen.SetBody(row.Body, string.Join("\n", lines));
        }

        private void RowClicked(Row row)
        {
            if (row.Candidate != null)
            {
                string result = PlayerCouncil.Appoint(row.Portfolio, row.Candidate);
                SetCabinetFeedback(result.Replace("ok: ", string.Empty).Replace("error: ", string.Empty), result.StartsWith("error"));
                expandedSeat = null;
                nextRefresh = 0;
                return;
            }
            if (row.Vacant != null)
            {
                expandedSeat = expandedSeat == row.Vacant ? null : row.Vacant;
                nextRefresh = 0;
                return;
            }
            FireClicked(row.Portfolio);
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
            // Fire's result (in Portuguese) also goes to the AI; the screen builds its own sentence, translated.
            AiModule module = AiModule.Instance;
            AiNation nation = module != null && module.PlayerIndex >= 0 ? AiModule.World?.Ensure(module.PlayerIndex) : null;
            Minister old = nation?.Council.FirstOrDefault(m => m.Portfolio == portfolio);
            int era = module != null ? TurnCapture.Latest?.Empire(module.PlayerIndex)?.EraIndex ?? 0 : 0;
            string oldName = old != null ? $"{CouncilBank.TitleUi(old.Portfolio, era, old.Gender)} {old.Name}" : null;
            string result = PlayerCouncil.Fire(portfolio);
            if (result.StartsWith("ok") && oldName != null)
            {
                Minister newcomer = nation.Council.FirstOrDefault(m => m.Portfolio == portfolio);
                SetCabinetFeedback(L.F("Demissão de {0}; no lugar entra {1}", oldName, newcomer?.Name ?? L.T("ninguém")), false);
                return;
            }
            SetCabinetFeedback(result.Replace("ok: ", string.Empty).Replace("error: ", string.Empty), !result.StartsWith("ok"));
        }

        // ---------------- Development and removal ----------------

        /// <summary>"ai council open|close|section 0..2" commands (the rest of "ai council" is in AiCommands).</summary>
        internal static string DevCommand(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0] : "open";
            if (Instance == null)
            {
                return "error: council screen does not exist (load a game)";
            }
            switch (sub)
            {
                case "close":
                    SetOpen(false);
                    return "ok: closed";
                case "section":
                    SetOpen(true);
                    if (parts.Length > 1 && int.TryParse(parts[1], out int section))
                    {
                        Instance.SelectSection((Section)Mathf.Clamp(section, 0, SectionNames.Length - 1));
                    }
                    return "ok";
                default:
                    SetOpen(true);
                    return "ok: open";
            }
        }

        /// <summary>Safe removal (core reload, leaving the game): same as the mail screen's.</summary>
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
                Plugin.Log.LogWarning($"Council screen removal: {ex.Message}");
            }
            window.gameObject.SetActive(false);
            Destroy(window.gameObject);
        }
    }
}
