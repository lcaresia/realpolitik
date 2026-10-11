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
using CurrencyMod.NativeUI;
using UnityEngine;
using B = CurrencyMod.NativeUI.NativeBankWindow;

namespace CurrencyMod.Diplomacy.UI
{
    /// <summary>
    /// Full-screen Diplomatic Mail (design §8.1). The frame is a clone of the game's Settings screen
    /// (left column with title, sections and back button; central panel with blur), registered in the match's
    /// full-screen group: the game hides the HUD, closes on ESC and shows one full screen at a time. The card list
    /// comes from the cities window, as in the Central Bank.
    ///   left: New, To answer, Read, Answered, Sent, Public statements, Nations;
    ///   center: the section's letters, with a filter by nation and by period;
    ///   right: compose (and reply, linked to the original letter).
    /// The letters live in AiModule.World; this screen only reads and calls SendPlayerLetter / TogglePlayerBlock.
    /// </summary>
    internal class MailScreen : UIWindow
    {
        private const string WindowName = "CurrencyMod_MailScreen";
        private const string SaveNotesDonor = "LoadSavesScreen/Content/Table/SaveGameHeader/SaveNotesInputField";
        private const string SliderDonor = "InGameOptionsWindow/Content/OptionItemsPool/SliderOptionItem";
        private const string Red = "E8685E";
        private const string Gold = "E3C88A";
        private const string Green = "8FD18A";

        // Geometry (standardized 1920 × 1080 screen). The HUD that stays on top of the full screen: the control bar
        // at the bottom left (up to x≈370, from y≈913) and the end-turn button at the bottom right.
        private const float CenterX = 420f;
        private const float CenterWidth = 1030f;
        private const float RightX = 1470f;
        private const float RightWidth = 420f;
        private const float RightHeight = 830f;
        private const float ListWidth = 928f; // cards sit 26 px inside the list and are 34 px narrower: they line up with the divider line
        /// <summary>The list ends above the control bar (which stays on top of the full screen up to x≈560).</summary>
        private const float ListHeight = 756f;
        private const float ComposeListWidth = 398f;
        private const float ComposeListHeight = 720f;
        /// <summary>Received letters older than this leave "To answer" (nobody answers a letter from 20 turns ago).</summary>
        private const int AnswerWindowTurns = 20;
        private const int MaxLettersListed = 60;

        internal static MailScreen Instance;

        internal enum Section { New, ToAnswer, Read, Answered, Sent, Public, Nations, Groups }

        // Texts marked with L.N: translated with L.T (or SectionHelpText) at display time.
        private static readonly string[] SectionNames = { L.N("Novas"), L.N("A responder"), L.N("Lidas"), L.N("Respondidas"), L.N("Enviadas"), L.N("Comunicados públicos"), L.N("Nações"), L.N("Grupos") };
        private static readonly string[] SectionHelp =
        {
            L.N("Cartas que chegaram e você ainda não leu."),
            L.N("Cartas privadas e ultimatos que pedem resposta e ainda não têm a sua (até {0} turnos atrás)."),
            L.N("Todas as cartas privadas e ultimatos que você já leu."),
            L.N("Cartas que você já respondeu, com a sua resposta ligada."),
            L.N("Suas cartas e a situação de cada uma: a caminho, entregue, lida, respondida ou recusada."),
            L.N("Declarações públicas: todas as nações que conhecem quem escreveu leem."),
            L.N("Nações que você conhece: escrever para elas ou recusar as cartas privadas delas."),
            L.N("Conversas com várias nações ao mesmo tempo: todos os membros leem cada mensagem."),
        };
        private static readonly string[] TypeIds = { "privada", "ultimato", "publica", "grupo" };
        private static readonly string[] TypeNames = { L.N("Carta privada"), L.N("Ultimato"), L.N("Declaração pública"), L.N("Mensagem de grupo") };
        private static readonly int[] PeriodTurns = { 0, 5, 15, 30 };
        private static readonly string[] PeriodNames = { L.N("Período: tudo"), L.N("Últimos 5 turnos"), L.N("Últimos 15 turnos"), L.N("Últimos 30 turnos") };

        /// <summary>Section help, translated (the "To answer" one takes the turn window in {0}).</summary>
        private static string SectionHelpText(int index) => L.F(SectionHelp[index], AnswerWindowTurns); // SectionHelp: L.N set

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
        /// <summary>Letters shown in "New" during this visit: they stay in the list after being marked as read.</summary>
        private readonly HashSet<int> freshShown = new HashSet<int>();

        private readonly List<LetterRow> letterRows = new List<LetterRow>();
        private readonly List<NationRow> nationRows = new List<NationRow>();
        private Transform emptyCard;
        /// <summary>What the empty card's button does (null = no button).</summary>
        private Action emptyAction;

        // Compose.
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
        /// <summary>Group of the compose panel (type "grupo"); -1 = create a new group with newGroupMembers.</summary>
        private int composeGroup = -1;
        private readonly HashSet<int> newGroupMembers = new HashSet<int>();
        private readonly List<NationRow> groupRows = new List<NationRow>();
        private int composeType;
        private int replyToId = -1;
        /// <summary>"Detach": the letter goes out unlinked (otherwise the module links it by itself to their last letter that asked for a reply).</summary>
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
            /// <summary>Only on group cards: the latest messages.</summary>
            public Transform Body;
        }

        // ---------------- Creation ----------------

        internal static MailScreen Create()
        {
            SystemSettingsScreen donor = Resources.FindObjectsOfTypeAll<SystemSettingsScreen>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            AllSettlementsWindow listDonor = Resources.FindObjectsOfTypeAll<AllSettlementsWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            InGameFullscreenGroup group = WindowsManager.Instance?.GetWindowsGroup<InGameFullscreenGroup>();
            if (donor == null || listDonor == null || group == null || !group.IsReady)
            {
                return null;
            }

            // Everything is built under an inactive parent: nothing loads until the screen is clean and registered.
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
            Plugin.Log.LogInfo("Diplomatic Mail full screen created.");
            return window;
        }

        /// <summary>Removes from the copy the settings panels, the item "pool" and the settings scripts.</summary>
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
            // Settings buttons ("Default", "Unbind"): the settings screen shows or hides them depending on the open
            // group, and the copy inherits the current state. On the session's first opening they were left visible.
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
            // The control bar stays on top of the full screen in the bottom corner: the back button moves up.
            UITransform closeUi = closeButton.GetComponent<UITransform>();
            NativeUIKit.Place(closeButton.transform, closeUi.X, 836f, closeUi.Width, closeUi.Height);

            Transform toggles = left.Find("SettingsTogglesTable");
            var all = new List<Transform>();
            foreach (Transform child in toggles)
            {
                all.Add(child);
            }
            // The settings screen may have fewer buttons than the mail sections: clone the last one until there are enough.
            while (all.Count > 0 && all.Count < SectionNames.Length)
            {
                all.Add(toggles.GetComponent<UITransform>().InstantiateChild(all[all.Count - 1], "Section" + all.Count).transform);
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

            // Center: letter list. Right: compose (copy of the central panel, still empty).
            Transform center = root.Find("Center");
            UITransform centerUi = center.GetComponent<UITransform>();
            Transform rightPanel = Instantiate(center.gameObject, root).transform;
            rightPanel.name = "Compose";
            NativeUIKit.Place(center, CenterX, centerUi.Y, CenterWidth, centerUi.Height);
            NativeUIKit.Place(rightPanel, RightX, 56f, RightWidth, RightHeight);

            // The empire emblem (top corner) sits on top of the left column: title and description move to the
            // central panel.
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

            // Filters (nation and period) on the section title row: the cards' small buttons.
            Transform buttonDonor = cardSample.Find("Table/Top/LiberateButton");
            nationFilterButton = Instantiate(buttonDonor.gameObject, center).GetComponent<UIButton>();
            nationFilterButton.name = "NationFilter";
            periodFilterButton = Instantiate(buttonDonor.gameObject, center).GetComponent<UIButton>();
            periodFilterButton.name = "PeriodFilter";
        }

        /// <summary>
        /// Scrolling list from the cities window at a new size: the list's vertical table is turned off (we own the
        /// size) and the template card gets the list width, minus the scrollbar.
        /// </summary>
        internal static void PrepareList(Transform list, float width, float height, out UITransform table, out Transform sample)
        {
            NativeUIKit.Place(list, 0f, 0f, width, height);
            // The cities list stays hidden until its window opens for the first time in the session, and the copy inherits
            // that (empty list on first opening): always show it and remove the source window's show/hide animation.
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
            // The cities list scroll grows with its content up to 540: here the height is fixed, the screen's.
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
                Plugin.Log.LogError($"Mail: failed to build the compose panel: {ex}");
            }
            emptyCard = listTable.InstantiateChild(cardSample, "EmptyCard").transform;
            emptyCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>().LeftClick += b => emptyAction?.Invoke();
            SelectSection(pendingSection >= 0 ? (Section)pendingSection : Section.New);
            pendingSection = -1;
        }

        /// <summary>Tooltips inherited from the settings and the cities window: clears them all.</summary>
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

        // ---------------- Compose ----------------

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
                bodyItem = CloneField(fieldDonor, "BodyField", 300f, multiline: true, maxChars: AiConfig.PlayerLetterLimit, hint: L.T("Escreva sua carta como o líder do seu povo…"));
                bodyField = bodyItem.GetComponent<UITextField>();
                // TextChange is bound in Update (EnsureTextChange), after the field loads.
            }
            else
            {
                Plugin.Log.LogWarning("Mail: text field donor not found.");
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

        /// <summary>Native text field (the save-notes one) with the width of the panel cards; optionally multiline.</summary>
        private Transform CloneField(Transform donor, string name, float height, bool multiline, int maxChars, string hint)
        {
            Transform item = NativeUIKit.Clone(donor, composeTable.transform, name);
            item.gameObject.SetActive(true); // the donor stays inactive while the saves screen is closed
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
            // Typing must not fire game shortcuts; clicking the text places the cursor there (does not select all).
            NativeUIKit.BlockGameShortcuts(field);
            field.actionOnFocus = UITextFieldFocusAction.PlaceCaretAtCursor;
            if (multiline)
            {
                field.multiline = true;
                field.OnMultilineChanged(false, true); // turns on word wrap, auto height and Enter = new line
            }
            NativeUIKit.Tip(item, name == "BodyField" ? L.T("Texto da carta") : name == "SubjectField" ? L.T("Assunto") : L.T("Exigência"),
                name == "BodyField" ? L.F("Enter quebra a linha. Até {0} caracteres.", AiConfig.PlayerLetterLimit)
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
            if (TypeIds[composeType] == "grupo")
            {
                // The player's groups and, last, "New group" (-1).
                int me = AiModule.Instance?.PlayerIndex ?? -1;
                List<int> options = AiModule.World?.Groups.Where(g => g.Members.Contains(me)).Select(g => g.Id).ToList() ?? new List<int>();
                options.Add(-1);
                composeGroup = options[(options.IndexOf(composeGroup) + 1) % options.Count];
                feedback = null;
                nextRefresh = 0;
                return;
            }
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
            AiModule module = AiModule.Instance;
            if (module == null || bodyField == null)
            {
                return;
            }
            string type = TypeIds[composeType];
            if (type == "grupo")
            {
                SendToGroup(module);
                return;
            }
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
                SetFeedback(result.Replace("error: ", string.Empty), true);
            }
        }

        /// <summary>Message to the chosen group, or creates the new group (name in the subject, members ticked in Nations) and sends.</summary>
        private void SendToGroup(AiModule module)
        {
            string text = AiModule.CleanPlayerText(bodyField.Text);
            if (text.Length == 0)
            {
                SetFeedback(L.T("A mensagem está vazia."), true);
                return;
            }
            string result;
            if (composeGroup < 0)
            {
                string name = subjectField?.Text.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    SetFeedback(L.T("Dê um nome ao grupo no assunto."), true);
                    return;
                }
                result = module.CreatePlayerGroup(name, newGroupMembers.ToList(), text);
                if (result.StartsWith("ok"))
                {
                    newGroupMembers.Clear();
                    composeGroup = AiModule.World.Groups.LastOrDefault()?.Id ?? -1;
                }
            }
            else
            {
                result = module.SendPlayerGroupLetter(composeGroup, subjectField?.Text.Trim(), text);
            }
            if (result.StartsWith("ok"))
            {
                int arrives = ParseArrival(result);
                SetFeedback(arrives >= 0 ? L.F("Enviada. Chega no turno {0}.", arrives) : L.T("Mensagem enviada ao grupo."), false);
                bodyField.ReplaceText(string.Empty);
                subjectField?.ReplaceText(string.Empty);
            }
            else
            {
                SetFeedback(result.Replace("error: ", string.Empty), true);
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

        /// <summary>Addresses the compose panel to a nation (Write, in the Nations section).</summary>
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

        /// <summary>Reply: recipient, "Re:" subject and the link to the original letter.</summary>
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

        // ---------------- Opening, sections and input ----------------

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
                // Freshly created (or recreated on a reload), the screen only accepts opening after PostLoad.
                Instance.pendingOpen = true;
                return;
            }
            Instance.pendingOpen = false;
            // The full-screen group hides itself with a diplomacy, trade or stealth cursor, and the bar menus would stay
            // on top: normal cursor, menus closed and the mod's other windows closed.
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
                pendingSection = (int)section; // still loading
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
            if (section != Section.Nations && section != Section.Groups)
            {
                SetNationRowCount(0);
                SetGroupRowCount(0);
            }
            else
            {
                SetLetterRowCount(0);
                if (section == Section.Nations) SetGroupRowCount(0); else SetNationRowCount(0);
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

        // ---------------- Content ----------------

        private Action<Amplitude.UI.Interactables.IUITextField, string> bodyChanged;

        private void Update()
        {
            NativeUIKit.EnsureTextChange(bodyField, bodyChanged ?? (bodyChanged = (f, t) => nextRefresh = 0));
            NativeUIKit.FixLayoutAfterPaste(bodyField);
            if (wasShown && !Shown)
            {
                NativeUIKit.ReleaseTextFocus(); // the game hid the screen without going through SetOpen
            }
            wasShown = Shown;
            if (pendingOpen && LoadingState == Amplitude.UI.Windows.LoadingState.Loaded)
            {
                SetOpen(true);
            }
            // A bar menu open (Faith, Society...) or something else selected: close, like the game's screens.
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
                Plugin.Log.LogError($"Diplomatic Mail: {ex}");
                nextRefresh = Time.unscaledTime + 5f;
            }
        }

        private void RefreshContent()
        {
            AiModule module = AiModule.Instance;
            WorldCapture capture = TurnCapture.Latest;
            if (module == null || AiModule.World == null || module.PlayerIndex < 0 || capture == null || capture.Guid != AiModule.World.GameGuid)
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
            else if (currentSection == Section.Groups)
            {
                FillGroups(module, capture, me, turn);
            }
            else
            {
                FillLetters(module, capture, me, turn);
            }
            FillCompose(module, capture, me, turn, era, delay);
        }

        private void FillLeft(AiModule module, int me, int turn, int era, int delay)
        {
            List<Letter> inbox = module.PlayerInbox();
            int unread = inbox.Count(l => !l.ReadBy.Contains(me));
            int toAnswer = inbox.Count(l => ToAnswer(module, l, turn));
            int publicUnread = inbox.Count(l => l.IsPublic && !l.ReadBy.Contains(me));
            WorldCapture latest = TurnCapture.Latest;
            int publicCitesMe = inbox.Count(l => l.IsPublic && !l.ReadBy.Contains(me) && Mentions.Cites(l, latest, AiModule.World, me));
            int inTransit = module.PlayerOutbox().Count(l => l.DeliverTurn > turn);
            string courier = Delivery.CourierName(era);
            string arrival = delay == 0 ? L.F("Turno {0}. Suas cartas chegam no mesmo turno ({1}).", turn, courier)
                : delay == 1 ? L.F("Turno {0}. Suas cartas chegam em 1 turno ({1}).", turn, courier)
                : L.F("Turno {0}. Suas cartas chegam em {1} turnos ({2}).", turn, delay, courier);
            descriptionLabel.Text = (Llm.Providers.ProviderRouter.AllDown ? $"<c={Red}>X {L.T("IA desconectada")}</c> · " : string.Empty)
                + arrival + (inTransit > 0 ? " " + L.F("{0} a caminho.", inTransit) : string.Empty);
            int groupUnread = inbox.Count(l => l.IsGroup && !l.ReadBy.Contains(me));
            int[] counts = { unread, toAnswer, -1, -1, -1, publicUnread, -1, groupUnread };
            for (int i = 0; i < sectionToggles.Count; i++)
            {
                string label = counts[i] > 0 ? $"{L.T(SectionNames[i])} ({counts[i]})" : L.T(SectionNames[i]);
                if (i == (int)Section.Public && publicCitesMe > 0)
                {
                    // A new statement that talks about you: the count gets the golden part.
                    label = $"{L.T(SectionNames[i])} ({counts[i]} · <c={Gold}>" + (publicCitesMe == 1 ? L.T("1 cita você") : L.F("{0} citam você", publicCitesMe)) + "</c>)";
                }
                UILabel toggleLabel = sectionToggles[i].GetComponent<UILabel>();
                if (toggleLabel != null && toggleLabel.Text != label)
                {
                    toggleLabel.Text = label;
                }
            }
        }

        private void FillFilters(WorldCapture capture)
        {
            bool letters = currentSection != Section.Nations && currentSection != Section.Groups;
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

        /// <summary>Small button on the section title row, right-aligned at "right".</summary>
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

        private bool ToAnswer(AiModule module, Letter letter, int turn)
        {
            return !letter.IsPublic && letter.ExpectsReply && letter.DeliverTurn >= turn - AnswerWindowTurns && !module.PlayerAnswered(letter)
                && (TurnCapture.Latest?.AiEmpires().Contains(letter.From) ?? false);
        }

        private List<Letter> SectionLetters(AiModule module, int me, int turn)
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
                    // Those that arrived this turn stay in "New" for the whole turn, even once read: at turn change the screen
                    // could mark as read letters the player never saw, and "New" ended up empty.
                    letters = inbox.Where(l => freshShown.Contains(l.Id) || l.DeliverTurn >= turn);
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
                    letters = AiModule.World.Letters.Where(l => l.IsPublic && l.DeliverTurn <= turn && (l.From == me || l.IsFor(me)));
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

        private void FillLetters(AiModule module, WorldCapture capture, int me, int turn)
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
            // Reading is opening the section: what shows up here becomes read (in "New" it stays listed until leaving it).
            // Only with the list actually visible: at turn change the window stays "Shown" with the screens group hidden.
            if (NativeUIKit.IsReallyVisible(listTable))
            {
                module.MarkReadByPlayer(letters.Where(l => l.From != me));
            }
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

        private void FillReceivedRow(AiModule module, WorldCapture capture, LetterRow row, Transform top, Letter letter, int me, int turn, List<int> ai)
        {
            CapturedEmpire sender = capture.Empire(letter.From);
            bool fresh = !letter.ReadBy.Contains(me) || freshShown.Contains(letter.Id) && currentSection == Section.New;
            bool answered = !letter.IsPublic && module.PlayerAnswered(letter);
            // A public statement that talks about you (name, people of any era or city): golden title and a "Mentions you" chip.
            bool citesMe = letter.IsPublic && Mentions.Cites(letter, capture, AiModule.World, me);
            string groupName = letter.IsGroup ? AiModule.World.Groups.FirstOrDefault(g => g.Id == letter.GroupId)?.Name ?? "GRP" + letter.GroupId : null;
            string title = NationAndLeader(capture, letter.From) + (letter.IsPublic ? " · " + L.T("a todos") : letter.IsGroup ? " · " + L.F("no grupo {0}", Escape(groupName)) : string.Empty);
            B.SetLabel(top, "TitleGroup/Title", citesMe ? $"<c={Gold}>{title}</c>" : title);
            B.SetChip(top, "StatsTable/PopCount", fresh ? $"<c={Green}>{L.T("Nova")}</c> · " + L.F("chegou no turno {0}", letter.DeliverTurn) : L.F("Chegou no turno {0}", letter.DeliverTurn));
            B.SetChip(top, "StatsTable/Fortification", TypeChip(letter.Type));
            // The leader is already in the title: the chip only shows the situation, when there is one.
            bool hasState = answered || !letter.IsPublic && letter.ExpectsReply;
            B.SetVisible(top.Find("StatsTable/ExtensionsCount"), hasState || citesMe);
            if (hasState)
            {
                B.SetChip(top, "StatsTable/ExtensionsCount", answered ? $"<c={Green}>{L.T("Respondida")}</c>"
                    : letter.Type == "ultimato" ? UltimatumChip(letter, turn) : $"<c={Gold}>{L.T("Pede resposta")}</c>");
            }
            else if (citesMe)
            {
                B.SetChip(top, "StatsTable/ExtensionsCount", $"<c={Gold}>{L.T("Cita você")}</c>");
            }
            // Only reply to someone who still exists: without the sender in the list, Compose would silently switch the recipient.
            bool canReply = ai.Contains(letter.From);
            B.SetVisible(row.Primary.transform, canReply);
            if (canReply)
            {
                B.SetButtonText(row.Primary, answered ? L.T("Escrever de novo") : L.T("Responder"));
                // Second button: the whole conversation with that nation, on the diplomacy Letters tab.
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
            if (hasState)
            {
                B.Tip(top, "StatsTable/ExtensionsCount", answered ? L.T("Respondida") : L.T("Pede resposta"),
                    answered ? L.T("Você já respondeu esta carta; a sua resposta aparece logo abaixo do texto.")
                    : L.T("Quem escreveu espera uma resposta sua. Ficar em silêncio também é uma resposta."));
            }
            else if (citesMe)
            {
                B.Tip(top, "StatsTable/ExtensionsCount", L.T("Cita você"), L.T("Este comunicado público fala de você: o seu nome, o seu povo (de qualquer era) ou uma das suas cidades."));
            }
            B.Tip(row.Primary.transform, string.Empty, L.T("Responder"), L.T("Abre o painel de escrever, à direita, endereçado a quem mandou esta carta e ligado a ela."));
        }

        private void FillSentRow(AiModule module, WorldCapture capture, LetterRow row, Transform top, Letter letter, int turn)
        {
            string to = letter.IsPublic ? L.T("Você · a todas as nações") : L.F("Para {0}", NationAndLeader(capture, letter.To, inline: true));
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

        internal static string StatusChip(AiModule module, Letter letter, int turn, out string help)
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

        /// <summary>Letter text: subject, demand, body and, when present, the letter it answers and the reply it received.</summary>
        internal static string LetterBody(AiModule module, WorldCapture capture, Letter letter, int me)
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
                    // "Os Bizantinos" → "à carta dos Bizantinos" (Portuguese crasis and contraction, not "a a carta de Os Bizantinos").
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
            // What this means in the game: what the sender did to you on the letter's turn and the rules it cites.
            List<string> context = LetterContext.For(AiModule.World, letter, me);
            if (context.Count > 0)
            {
                parts.Add($"<c={Gold}>{L.T("O que isso significa no jogo:")}</c>\n" + string.Join("\n", context.Select(c => "• " + Escape(c))));
            }
            return string.Join("\n\n", parts);
        }

        /// <summary>AI or player text must not turn into rich-text markup by accident.</summary>
        internal static string Escape(string text) => (text ?? string.Empty).Replace("<", "‹").Replace(">", "›");

        /// <summary>"Os Micênicos · Māui": people and leader, for the card title.</summary>
        internal static string NationAndLeader(WorldCapture capture, int empire, bool inline = false)
        {
            CapturedEmpire nation = capture.Empire(empire);
            string raw = nation?.Culture ?? DossierBuilder.Name(capture, empire);
            // At the start of the title the article is capitalized ("Os Garamantes"); mid-sentence, lowercase ("Para os Garamantes").
            string culture = inline ? GameText.InlineUi(raw) : raw;
            return string.IsNullOrWhiteSpace(nation?.Leader) ? culture : culture + " · " + GameText.InlineUi(nation.Leader);
        }

        internal static string TypeChip(string type)
        {
            switch (type)
            {
                case "ultimato": return $"<c={Red}>{L.T("Ultimato")}</c>";
                case "publica": return L.T("Pública");
                case "grupo": return L.T("Grupo");
                default: return L.T("Privada");
            }
        }

        internal static string TypeName(string type) => type == "ultimato" ? L.T("Ultimato") : type == "publica" ? L.T("Declaração pública") : type == "grupo" ? L.T("Mensagem de grupo") : L.T("Carta privada");

        internal static string TypeHelp(string type)
        {
            switch (type)
            {
                case "ultimato": return L.T("Exigência com prazo. Chega mesmo para quem recusa sua correspondência.");
                case "publica": return L.T("Todas as nações que conhecem quem escreveu leem. Serve de propaganda, aviso ou denúncia.");
                case "grupo": return L.T("Todos os membros do grupo leem. Bom para combinar algo entre várias nações ao mesmo tempo.");
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
                primary.LeftClick += b =>
                {
                    Letter clicked = letterRows[index].Letter;
                    if (clicked != null && clicked.IsGroup)
                    {
                        ComposeToGroup(clicked.GroupId); // reply in the group, to all members
                        return;
                    }
                    StartReply(clicked);
                };
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
        /// The letter's running text inside the card: a label (game font, word wrap, auto height) right
        /// below the title-and-chips row. The card's table stacks and the card grows with it.
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
            // The donor title has resize weight 1; with a "stretchy" child the table does not grow by itself.
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
        /// Title that shares the row with buttons: the title group ends before the buttons, and the label is cut with
        /// an ellipsis, as in the game.
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

        private void ShowEmpty(string title, string chip, string tipTitle, string tipText, string button = null, Action action = null)
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
            emptyAction = action;
            Transform actionButton = top.Find("LiberateButton");
            B.SetVisible(actionButton, button != null);
            if (button != null)
            {
                B.SetButtonText(actionButton.GetComponent<UIButton>(), button);
                B.Tip(actionButton, string.Empty, button, tipText);
            }
            B.HideOutputs(top);
            if (tipTitle != null)
            {
                B.Tip(top, "StatsTable/PopCount", tipTitle, tipText);
            }
            emptyCard.SetAsLastSibling();
            B.SetVisible(emptyCard, true);
        }

        // ---------------- Compose (right panel) ----------------

        private void FillCompose(AiModule module, WorldCapture capture, int me, int turn, int era, int delay)
        {
            List<int> nations = KnownNations(capture);
            string type = TypeIds[composeType];
            if (type != "publica" && (composeTo < 0 || !nations.Contains(composeTo)))
            {
                composeTo = nations.Count > 0 ? nations[0] : -1;
            }
            Letter replying = replyToId >= 0 ? module.LetterById(replyToId) : null;
            if (replying != null && (type == "publica" || type == "grupo" || replying.From != composeTo))
            {
                replyToId = -1;
                replying = null;
            }
            composeTitleLabel.Text = replying != null ? L.T("Responder") : L.T("Escrever");
            // The cards are born hidden, like the game's list template.
            B.SetVisible(recipientCard, true);
            B.SetVisible(typeCard, true);
            B.SetVisible(sendCard, true);

            // Recipient.
            Transform top = recipientCard.Find("Table/Top");
            CapturedEmpire them = capture.Empire(composeTo);
            int arrives = turn + delay;
            if (type == "grupo")
            {
                FillGroupRecipient(top, capture, me, arrives);
            }
            else             if (type == "publica")
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

            // Type.
            Transform typeTop = typeCard.Find("Table/Top");
            B.SetLabel(typeTop, "TitleGroup/Title", L.T(TypeNames[composeType]));
            B.SetChip(typeTop, "StatsTable/PopCount", type == "ultimato" ? L.T("Exigência com prazo") : type == "publica" ? L.T("Todos leem") : type == "grupo" ? L.T("Os membros leem") : L.T("Só ele lê"));
            B.HideChild(typeTop, "StatsTable/Fortification");
            B.HideChild(typeTop, "StatsTable/ExtensionsCount");
            B.SetVisible(typeButton.transform, true); // the sample's button can come hidden: every compose button is shown explicitly
            B.SetButtonText(typeButton, L.T("Trocar"));
            FitTitle(typeTop, typeButton.GetComponent<UITransform>().Width + 20f);
            B.HideOutputs(typeTop);
            B.Tip(typeTop, "StatsTable/PopCount", L.T(TypeNames[composeType]), TypeHelp(type));
            B.Tip(typeButton.transform, string.Empty, L.T("Trocar tipo"), L.T("Carta privada → Ultimato → Declaração pública → Mensagem de grupo."));

            // In reply to.
            B.SetVisible(replyCard, replying != null);
            if (replying != null)
            {
                Transform replyTop = replyCard.Find("Table/Top");
                B.SetLabel(replyTop, "TitleGroup/Title", string.IsNullOrWhiteSpace(replying.Subject) ? L.F("Resposta à carta do turno {0}", replying.SentTurn) : "Re: " + Escape(replying.Subject).Replace("Re: ", string.Empty));
                B.SetChip(replyTop, "StatsTable/PopCount", L.F("Carta do turno {0}", replying.SentTurn));
                B.HideChild(replyTop, "StatsTable/Fortification");
                B.HideChild(replyTop, "StatsTable/ExtensionsCount");
                B.SetVisible(replyButton.transform, true);
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

            // Send.
            Transform sendTop = sendCard.Find("Table/Top");
            int words = bodyField != null ? AiModule.PlayerTextLength(bodyField.Text) : 0;
            int max = AiConfig.PlayerLetterLimit;
            string title = feedback ?? (words == 0 ? L.T("Escreva a carta acima") : L.T("Pronta para enviar"));
            B.SetLabel(sendTop, "TitleGroup/Title", feedback != null && feedbackIsError ? $"<c={Red}>{title}</c>" : title);
            string wordCount = L.F("{0}/{1} caracteres", words, max);
            B.SetChip(sendTop, "StatsTable/PopCount", words > max ? $"<c={Red}>{wordCount}</c>" : wordCount);
            B.HideChild(sendTop, "StatsTable/Fortification");
            B.HideChild(sendTop, "StatsTable/ExtensionsCount");
            B.SetVisible(sendButton.transform, true);
            B.SetButtonText(sendButton, L.T("Enviar"));
            FitTitle(sendTop, sendButton.GetComponent<UITransform>().Width + 20f);
            B.HideOutputs(sendTop);
            B.Tip(sendTop, "StatsTable/PopCount", L.T("Tamanho"), L.F("Cartas acima de {0} caracteres são cortadas. Cartas curtas costumam ser mais bem lidas.", max));
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

        /// <summary>Living computer nations you know (same knowledge rule as the game).</summary>
        internal static List<int> KnownNations(WorldCapture capture)
        {
            AiModule module = AiModule.Instance;
            if (capture == null || module == null)
            {
                return new List<int>();
            }
            CapturedEmpire me = capture.Empire(module.PlayerIndex);
            return capture.AiEmpires().Where(e => me?.RelationWith(e)?.Knows ?? false).ToList();
        }

        // ---------------- Nations ----------------

        private void FillNations(AiModule module, WorldCapture capture, int me)
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
                B.SetVisible(row.WriteButton.transform, true);
                bool picking = TypeIds[composeType] == "grupo" && composeGroup < 0;
                B.SetButtonText(row.WriteButton, !picking ? L.T("Escrever") : newGroupMembers.Contains(empire) ? L.T("No grupo") : L.T("+ Grupo"));
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

        // ---------------- Groups (melhorias-jogando item 15) ----------------

        /// <summary>One card per player group: members, latest messages, Write and Leave. The top explains how to create one.</summary>
        private void FillGroups(AiModule module, WorldCapture capture, int me, int turn)
        {
            B.SetVisible(emptyCard, false);
            List<ChatGroup> groups = AiModule.World.Groups.Where(g => g.Members.Contains(me)).ToList();
            sectionTitleLabel.Text = L.F("Grupos · {0}", groups.Count);
            if (groups.Count == 0)
            {
                SetGroupRowCount(0);
                ShowEmpty(L.T("Você ainda não está em nenhum grupo"), L.T("Sem grupos"), L.T("Sem grupos"),
                    L.T("Abre a seção Nações com o painel de escrever em \"Mensagem de grupo\". Marque os membros com \"+ Grupo\", dê um nome no assunto e escreva a primeira mensagem."),
                    L.T("Criar um grupo"), () =>
                    {
                        ComposeToGroup(-1);
                        SelectSection(Section.Nations);
                    });
                return;
            }
            SetGroupRowCount(groups.Count);
            for (int i = 0; i < groups.Count; i++)
            {
                ChatGroup group = groups[i];
                NationRow row = groupRows[i];
                row.Empire = group.Id;
                Transform top = row.Card.Find("Table/Top");
                List<Letter> messages = AiModule.World.Letters
                    .Where(l => l.IsGroup && l.GroupId == group.Id && (l.From == me || (l.IsFor(me) && l.DeliverTurn <= turn)))
                    .OrderByDescending(l => l.Id).ToList();
                int unread = messages.Count(l => l.From != me && !l.ReadBy.Contains(me));
                B.SetLabel(top, "TitleGroup/Title", Escape(group.Name));
                B.SetChip(top, "StatsTable/PopCount", L.F("{0} membros", group.Members.Count));
                B.SetChip(top, "StatsTable/Fortification", unread > 0 ? $"<c={Green}>" + L.F("{0} nova(s)", unread) + "</c>" : L.F("{0} mensagem(ns)", messages.Count));
                B.SetVisible(top.Find("StatsTable/ExtensionsCount"), true);
                B.SetChip(top, "StatsTable/ExtensionsCount", group.Creator == me ? L.T("Criado por você") : L.F("Criado por {0}", GameText.InlineUi(capture.Empire(group.Creator)?.Culture ?? "E" + group.Creator)));
                B.HideOutputs(top);
                B.SetVisible(row.WriteButton.transform, true);
                B.SetButtonText(row.WriteButton, L.T("Escrever"));
                SetSecondButton(row.BlockButton, row.WriteButton, L.T("Sair"));
                FitTitle(top, row.WriteButton.GetComponent<UITransform>().Width + row.BlockButton.GetComponent<UITransform>().Width + 28f);
                string members = string.Join(", ", group.Members.Select(m => m == me ? L.T("você") : NationAndLeader(capture, m, inline: true)));
                B.Tip(top, "StatsTable/PopCount", L.T("Membros"), Escape(members));
                B.Tip(row.WriteButton.transform, string.Empty, L.T("Escrever ao grupo"), L.T("Endereça o painel de escrever a este grupo: todos os membros leem."));
                B.Tip(row.BlockButton.transform, string.Empty, L.T("Sair do grupo"), L.T("Você deixa de receber as mensagens. Grupo com menos de 2 membros acaba."));
                var lines = new List<string> { $"<c={Gold}>" + L.T("Membros:") + "</c> " + Escape(members) };
                foreach (Letter message in messages.Take(6).Reverse())
                {
                    string who = message.From == me ? L.T("Você") : NationAndLeader(capture, message.From);
                    string text = message.Text ?? string.Empty;
                    lines.Add($"<c={Gold}>T{message.SentTurn} {Escape(who)}:</c> " + Escape(text.Length > 400 ? text.Substring(0, 400) + "…" : text));
                }
                SetBody(row.Body, string.Join("\n", lines));
            }
            // Viewing the group means reading its messages.
            if (NativeUIKit.IsReallyVisible(listTable))
            {
                module.MarkReadByPlayer(AiModule.World.Letters.Where(l => l.IsGroup && l.IsFor(me) && l.DeliverTurn <= turn));
            }
        }

        private void SetGroupRowCount(int count)
        {
            while (groupRows.Count < count)
            {
                int index = groupRows.Count;
                Transform card = listTable.InstantiateChild(cardSample, "Group" + index).transform;
                Transform top = card.Find("Table/Top");
                UIButton write = top.Find("LiberateButton").GetComponent<UIButton>();
                UIButton leave = top.GetComponent<UITransform>().InstantiateChild(write.transform, "LeaveButton").GetComponent<UIButton>();
                var row = new NationRow { Card = card, WriteButton = write, BlockButton = leave, Body = AddBody(card) };
                write.LeftClick += b => ComposeToGroup(groupRows[index].Empire);
                leave.LeftClick += b =>
                {
                    AiModule module = AiModule.Instance;
                    string why = module?.LeaveGroup(module.PlayerIndex, groupRows[index].Empire);
                    SetFeedback(why == null ? L.T("Você saiu do grupo.") : why, why != null);
                };
                groupRows.Add(row);
            }
            for (int i = 0; i < groupRows.Count; i++)
            {
                B.SetVisible(groupRows[i].Card, i < count);
            }
            B.SetVisible(cardSample, false);
        }

        /// <summary>"To" card for the group type: the chosen group or the new group, with the members ticked in Nations.</summary>
        private void FillGroupRecipient(Transform top, WorldCapture capture, int me, int arrives)
        {
            ChatGroup group = composeGroup >= 0 ? AiModule.World.Groups.FirstOrDefault(g => g.Id == composeGroup && g.Members.Contains(me)) : null;
            if (group == null)
            {
                composeGroup = -1;
                newGroupMembers.RemoveWhere(m => !KnownNations(capture).Contains(m));
                int count = newGroupMembers.Count + 1;
                B.SetLabel(top, "TitleGroup/Title", L.T("Para: novo grupo"));
                B.SetChip(top, "StatsTable/PopCount", count < ChatGroup.MinMembers
                    ? $"<c={Red}>" + L.F("{0} de {1} membros", count, ChatGroup.MinMembers) + "</c>"
                    : L.F("{0} membros", count));
                B.SetVisible(top.Find("StatsTable/Fortification"), true);
                B.SetChip(top, "StatsTable/Fortification", L.T("Marque em Nações"));
                B.Tip(top, "StatsTable/Fortification", L.T("Membros"), newGroupMembers.Count == 0
                    ? L.T("Na seção Nações, use \"+ Grupo\" em cada nação que vai participar. O nome do grupo vai no assunto.")
                    : Escape(string.Join(", ", newGroupMembers.Select(m => NationAndLeader(capture, m, inline: true)))));
            }
            else
            {
                B.SetLabel(top, "TitleGroup/Title", L.F("Grupo: {0}", Escape(group.Name)));
                B.SetChip(top, "StatsTable/PopCount", L.F("Chega no turno {0}", arrives));
                B.SetVisible(top.Find("StatsTable/Fortification"), true);
                B.SetChip(top, "StatsTable/Fortification", L.F("{0} membros", group.Members.Count));
                B.Tip(top, "StatsTable/Fortification", L.T("Membros"), Escape(string.Join(", ", group.Members.Select(m => m == me ? L.T("você") : NationAndLeader(capture, m, inline: true)))));
            }
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.SetVisible(recipientButton.transform, true);
            B.SetButtonText(recipientButton, L.T("Trocar"));
        }

        /// <summary>Compose panel addressed to a group (or to a new group, with -1).</summary>
        internal void ComposeToGroup(int groupId)
        {
            composeType = Array.IndexOf(TypeIds, "grupo");
            composeGroup = groupId;
            replyToId = -1;
            feedback = null;
            nextRefresh = 0;
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
                write.LeftClick += b =>
                {
                    // Creating a new group: the button ticks or unticks the nation as a member.
                    if (TypeIds[composeType] == "grupo" && composeGroup < 0)
                    {
                        int empire = nationRows[index].Empire;
                        if (!newGroupMembers.Remove(empire) && newGroupMembers.Count + 1 < ChatGroup.MaxMembers)
                        {
                            newGroupMembers.Add(empire);
                        }
                        nextRefresh = 0;
                        return;
                    }
                    ComposeTo(nationRows[index].Empire, string.Empty);
                };
                block.LeftClick += b =>
                {
                    AiModule.Instance?.TogglePlayerBlock(nationRows[index].Empire);
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

        /// <summary>The card's second button, left of the first, on the same row as the title.</summary>
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

        // ---------------- Development and teardown ----------------

        /// <summary>
        /// "ai mail ..." commands: "open", "close", "section 0..6" ("tab" also works), "to E#", "reply [id]", "clear",
        /// "paste [file]", "group N|new", "member E#", "subject ...", "send", "text ...".
        /// </summary>
        internal static string DevCommand(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0] : "open";
            if (Instance == null)
            {
                return "error: mail screen does not exist (load a game)";
            }
            switch (sub)
            {
                case "close":
                    SetOpen(false);
                    return "ok: closed";
                case "section":
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
                case "reply":
                    SetOpen(true);
                    if (parts.Length > 1 && int.TryParse(parts[1], out int id))
                    {
                        Instance.StartReply(AiModule.Instance?.LetterById(id));
                    }
                    else if (Instance.letterRows.Count > 0 && Instance.letterRows[0].Letter != null)
                    {
                        // No id: the first letter of the list on screen (same as clicking Reply on it).
                        Instance.StartReply(Instance.letterRows[0].Letter);
                    }
                    return "ok";
                case "clear":
                    // Clears the draft (text, subject, demand) and the reply link, without sending.
                    Instance.bodyField?.ReplaceText(string.Empty);
                    Instance.subjectField?.ReplaceText(string.Empty);
                    Instance.demandField?.ReplaceText(string.Empty);
                    Instance.replyToId = -1;
                    Instance.feedback = null;
                    Instance.nextRefresh = 0;
                    return "ok: draft cleared";
                case "paste":
                {
                    // "ai mail paste <file>": pastes the file content (relative to _Modding\dev) into the letter field through
                    // the same path as Ctrl+V (focus + TryInsertString), to reproduce the pasted text that vanished. Does not send.
                    SetOpen(true);
                    UITextField field = Instance.bodyField;
                    if (field == null)
                    {
                        return "error: field not built yet";
                    }
                    string file = System.IO.Path.Combine(DevTools.DevDir, parts.Length > 1 ? parts[1] : "paste.txt");
                    if (!System.IO.File.Exists(file))
                    {
                        return "error: file not found: " + file;
                    }
                    Amplitude.UI.Interactables.UIInteractivityManager.Instance?.SetFocus(field.TextFieldResponder);
                    field.ReplaceText(string.Empty);
                    bool inserted = field.TryInsertString(System.IO.File.ReadAllText(file));
                    return $"ok: inserted={inserted}, {field.Text?.Length ?? 0} characters in the field, editing={field.IsEditing}, label y={field.label?.UITransform.Y:0}";
                }
                case "group":
                    // "group N" writes to group N; "group new" opens creation (members with "member E#", name in the subject).
                    SetOpen(true);
                    Instance.ComposeToGroup(parts.Length > 1 && int.TryParse(parts[1], out int groupId) ? groupId : -1);
                    return "ok";
                case "member":
                    // Same as clicking "+ Group" on the nation's row.
                    if (parts.Length > 1 && int.TryParse(parts[1].TrimStart('E', 'e'), out int member) && Instance.composeGroup < 0
                        && !Instance.newGroupMembers.Remove(member) && Instance.newGroupMembers.Count + 1 < ChatGroup.MaxMembers)
                    {
                        Instance.newGroupMembers.Add(member);
                    }
                    Instance.nextRefresh = 0;
                    return "ok: members " + string.Join(",", Instance.newGroupMembers);
                case "subject":
                    Instance.subjectField?.ReplaceText(string.Join(" ", parts.Skip(1)));
                    return "ok";
                case "send":
                    // Same as the Send button (really sends).
                    Instance.Send();
                    return "ok: " + (Instance.feedback ?? "?");
                case "text":
                    // Only fills the field (visual test of multiline text); sends nothing.
                    SetOpen(true);
                    Instance.bodyField?.ReplaceText(string.Join(" ", parts.Skip(1)).Replace("\\n", "\n"));
                    return "ok: text in the field (not sent)";
                default:
                    SetOpen(true);
                    return "ok: open";
            }
        }

        /// <summary>
        /// Safe teardown (core reload, leaving the match). A full screen destroyed while open would leave the group thinking
        /// a screen is open: the HUD and the match windows would vanish forever. Hides, clears the group and releases the keyboard.
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
                Plugin.Log.LogWarning($"Mail screen teardown: {ex.Message}");
            }
            window.gameObject.SetActive(false);
            Destroy(window.gameObject);
        }
    }
}
