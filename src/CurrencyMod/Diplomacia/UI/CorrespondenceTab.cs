using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using CurrencyMod.Diplomacia.Capture;
using CurrencyMod.NativeUI;
using HarmonyLib;
using UnityEngine;
using B = CurrencyMod.NativeUI.NativeBankWindow;

namespace CurrencyMod.Diplomacia.UI
{
    /// <summary>
    /// Aba "Cartas" na tela de diplomacia de cada nação (design §8.1): a conversa com aquela nação, em ordem, e o campo
    /// de escrever. As abas do jogo são fixas no código (enum e campos); esta é uma aba "sobreposta": um clone da aba
    /// Relações na fileira de abas e um painel nosso no grupo de painéis. Ativar abre Relações por baixo (aba sempre
    /// válida), esconde o painel dela e mostra o nosso; um patch em DiplomaticScreen.SetCurrentMode desativa quando o
    /// jogador clica numa aba do jogo e reafirma a nossa quando ele só troca de nação.
    /// Pesquisa: research\mail-fullscreen-and-diplomacy-tab.md, parte B.
    /// </summary>
    internal class CorrespondenceTab : MonoBehaviour
    {
        private const string TabName = "CurrencyMod_LettersTab";
        private const string PanelName = "CurrencyMod_LettersPanel";
        private const string SaveNotesDonor = "LoadSavesScreen/Content/Table/SaveGameHeader/SaveNotesInputField";
        private const string SliderDonor = "InGameOptionsWindow/Content/OptionItemsPool/SliderOptionItem";
        private const string TitleDonor = "_RelationsPanel/_DefaultGroup/_MyRelations/Label";
        private const string Red = "E8685E";
        private const string Gold = "E3C88A";
        private const string Green = "8FD18A";
        private const float PanelWidth = 1176f;
        private const float PanelHeight = 790f;
        private const float ListWidth = 744f;
        private const float ListHeight = 712f;
        private const float ComposeWidth = 360f;
        private const float TabSpacing = 190f;
        private const int MaxShown = 40;

        private static readonly string[] TypeIds = { "privada", "ultimato" };
        private static readonly string[] TypeNames = { L.N("Carta privada"), L.N("Ultimato") };

        internal static CorrespondenceTab Instance;
        /// <summary>A aba nossa está ativa (o jogo acha que é Relações).</summary>
        internal static bool Active;
        /// <summary>Chamada nossa de SetCurrentMode (não desativa a aba).</summary>
        internal static bool InternalCall;

        private DiplomaticScreen screen;
        private UIToggle tab;
        private DiplomaticNotificationBlip blip;
        private Transform panel;
        private UILabel titleLabel;
        private UITransform listTable;
        private Transform cardSample;
        private UITransform composeTable;
        private Transform composeSample;
        private Transform leftSpacer;
        private Transform rightSpacer;
        private float leftSpacerWidth = -1f;
        private float rightSpacerWidth = -1f;
        /// <summary>Cantos de cima (esquerdo, direito) de cada aba do jogo antes de a nossa entrar.</summary>
        private readonly Dictionary<SquircleBackgroundWidget, Vector2> originalCorners = new Dictionary<SquircleBackgroundWidget, Vector2>();
        private float nextTabsCheck;
        private bool built;
        private bool screenWasShown;
        private float nextCheck;
        private float nextRefresh;
        private int shownEmpire = -1;
        private int shownLastId = -1;
        private Texture2D letterIcon;
        private Amplitude.Framework.Guid letterIconGuid;

        private readonly List<Row> rows = new List<Row>();
        private Transform emptyCard;
        private Transform typeCard;
        private Transform replyCard;
        private Transform sendCard;
        private UIButton typeButton;
        private UIButton replyButton;
        private UIButton sendButton;
        private Transform subjectItem;
        private Transform demandItem;
        private Transform deadlineItem;
        private Transform bodyItem;
        private UITextField subjectField;
        private UITextField demandField;
        private UITextField bodyField;
        private UISlider deadlineSlider;
        private int composeType;
        private bool replyDetached;
        private int replyEmpire = -1;
        private string feedback;
        private bool feedbackIsError;

        private sealed class Row
        {
            public Transform Card;
            public Transform Body;
            public UIButton Button;
            public Letter Letter;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = Time.unscaledTime + 0.2f;
            try
            {
                if (SavePatches.IsGameOnline() || WindowsUtils.WindowsService == null || !GameAccess.TryGetSession(out _, out _, out _))
                {
                    return;
                }
                DiplomaticScreen current = WindowsUtils.GetWindow<DiplomaticScreen>();
                if (current == null)
                {
                    return;
                }
                if (!built || screen != current)
                {
                    if (!current.Shown)
                    {
                        return;
                    }
                    Build(current);
                }
                bool shown = screen.Shown;
                if (!shown && screenWasShown)
                {
                    // A tela fechou: a próxima abertura começa nas abas do jogo.
                    Deactivate(restoreNative: true);
                }
                screenWasShown = shown;
                if (!shown)
                {
                    return;
                }
                RefreshBlip();
                if (Time.unscaledTime >= nextTabsCheck && tab != null)
                {
                    // O jogo mostra ou esconde abas conforme a nação (Consulado): espaçamento e cantos acompanham.
                    nextTabsCheck = Time.unscaledTime + 1f;
                    FitTabs(tab.transform.parent);
                    ShapeTabs();
                }
                if (Active && Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + 0.5f;
                    RefreshContent();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Aba de cartas da diplomacia: {ex}");
                nextCheck = Time.unscaledTime + 5f;
            }
        }

        // ---------------- Montagem ----------------

        private void Build(DiplomaticScreen target)
        {
            Cleanup();
            screen = target;
            Transform root = screen.transform;
            Transform tabsTable = root.Find("_NegociationGroup/TabsTable");
            Transform relationTab = tabsTable?.Find("TabItem_Relation");
            Transform panelsGroup = root.Find("PanelsGroup");
            AllSettlementsWindow listDonor = Resources.FindObjectsOfTypeAll<AllSettlementsWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            if (relationTab == null || panelsGroup == null || listDonor == null)
            {
                return;
            }

            // Restos de uma geração anterior do mod (recarga a quente).
            Transform oldTab = tabsTable.Find(TabName);
            if (oldTab != null)
            {
                NativeUIKit.Dispose(oldTab);
            }
            Transform oldPanel = panelsGroup.Find(PanelName);
            if (oldPanel != null)
            {
                NativeUIKit.Dispose(oldPanel);
            }

            // Aba: clone de Relações sem o script da aba do jogo, antes do espaçador da direita.
            Transform tabClone = NativeUIKit.Clone(relationTab, tabsTable, TabName, "DiplomaticScreen_TabItem");
            leftSpacer = tabsTable.Find("Left");
            rightSpacer = tabsTable.Find("Right");
            if (rightSpacer != null)
            {
                tabClone.SetSiblingIndex(rightSpacer.GetSiblingIndex());
            }
            tab = tabClone.GetComponent<UIToggle>();
            tab.ClickDoesntSwitchOff = true;
            tab.State = false;
            tab.Switch += (source, state) =>
            {
                if (state && !Active)
                {
                    Activate();
                }
            };
            B.SetLabel(tabClone, "Label", L.T("Cartas"));
            blip = tabClone.GetComponentInChildren<DiplomaticNotificationBlip>(true);
            if (blip != null)
            {
                if (letterIcon == null)
                {
                    letterIcon = SdfIcons.Letter();
                    letterIconGuid = UIRenderingManager.Instance.RegisterTexture(letterIcon);
                }
                // O selo da aba mostra o envelope: cartas novas desta nação.
                if (blip.picto != null)
                {
                    blip.picto.Texture = new UITexture(letterIconGuid, UITextureFlags.AlphaStraight, UITextureColorFormat.Srgb, letterIcon);
                }
                blip.UpdateVisibility(false, instant: true);
            }
            FitTabs(tabsTable);
            ShapeTabs();
            NativeUIKit.Tip(tabClone, L.T("Cartas"), L.T("A correspondência com esta nação, em ordem, e o campo para escrever. A carta leva o tempo de entrega da sua era."));

            // Painel: cópia da lista da janela de cidades para a conversa e outra para escrever. Tudo é montado sob um
            // pai inativo e só depois entra na tela: uma lista clonada direto na tela carrega e se dimensiona sozinha
            // (altura do conteúdo vazio) antes de receber o tamanho dela.
            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            try
            {
                GameObject cities = Instantiate(listDonor.gameObject, stash.transform);
                B.StripGameScripts(cities);
                B.RemoveUnusedParts(cities.transform, keepSorters: 0);
                Transform citiesList = cities.transform.Find("_SettlementsList");

                var panelObject = new GameObject(PanelName, typeof(UITransform));
                panel = panelObject.transform;
                panel.SetParent(stash.transform, false);
                NativeUIKit.Place(panel, 0f, 0f, PanelWidth, PanelHeight);

                Transform titleDonor = panelsGroup.Find(TitleDonor);
                if (titleDonor != null)
                {
                    Transform title = Instantiate(titleDonor.gameObject, panel).transform;
                    title.name = "Title";
                    NativeUIKit.Place(title, 32f, 18f, ListWidth, 30f);
                    titleLabel = title.GetComponent<UILabel>();
                    titleLabel.Color = new Color(1f, 0.875f, 0.584f, 1f);
                    titleLabel.Alignment = new Alignment(HorizontalAlignment.Left, VerticalAlignment.Center);
                }

                Transform conversation = Instantiate(citiesList.gameObject, panel).transform;
                conversation.name = "Conversation";
                MailScreen.PrepareList(conversation, ListWidth, ListHeight, out listTable, out cardSample);
                NativeUIKit.Place(conversation, 32f, 56f, ListWidth, ListHeight);

                Transform compose = Instantiate(citiesList.gameObject, panel).transform;
                compose.name = "Compose";
                MailScreen.PrepareList(compose, ComposeWidth + 8f, ListHeight, out composeTable, out composeSample);
                NativeUIKit.Place(compose, 32f + ListWidth + 16f, 56f, ComposeWidth + 8f, ListHeight);

                panel.SetParent(panelsGroup, false);
            }
            finally
            {
                Destroy(stash);
            }
            BuildCompose();
            emptyCard = listTable.InstantiateChild(cardSample, "EmptyCard").transform;
            B.SetVisible(cardSample, false);
            NativeUIKit.SetVisible(panel, false);
            built = true;
            Active = false;
            Plugin.Log.LogInfo("Aba de cartas da diplomacia montada.");
        }

        /// <summary>Cinco abas em vez de quatro: os espaçadores das pontas encolhem para a fileira caber nos 1176 px.</summary>
        private void FitTabs(Transform tabsTable)
        {
            int visible = 0;
            foreach (Transform child in tabsTable)
            {
                UITransform ui = child.GetComponent<UITransform>();
                if (child != leftSpacer && child != rightSpacer && ui != null && ui.VisibleSelf)
                {
                    visible++;
                }
            }
            float spacer = Mathf.Max(8f, (PanelWidth - visible * TabSpacing) / 2f);
            bool changed = false;
            if (leftSpacer != null)
            {
                UITransform ui = leftSpacer.GetComponent<UITransform>();
                if (leftSpacerWidth < 0f)
                {
                    leftSpacerWidth = ui.Width;
                }
                if (Mathf.Abs(ui.Width - spacer) > 0.5f)
                {
                    ui.Width = spacer;
                    changed = true;
                }
            }
            if (rightSpacer != null)
            {
                UITransform ui = rightSpacer.GetComponent<UITransform>();
                if (rightSpacerWidth < 0f)
                {
                    rightSpacerWidth = ui.Width;
                }
                if (Mathf.Abs(ui.Width - spacer) > 0.5f)
                {
                    ui.Width = spacer;
                    changed = true;
                }
            }
            if (changed)
            {
                tabsTable.GetComponent<Amplitude.UI.Layouts.UILayout>()?.ArrangeChildren();
            }
        }

        /// <summary>
        /// Cantos da fileira de abas: o jogo arredonda o canto de cima à esquerda da primeira aba e o de cima à direita da
        /// última (as do meio são retas). A nossa entra no fim, então fica com o canto da direita, e a última aba visível
        /// do jogo (Crise, ou Consulado quando aparece) perde o dela. Os valores originais voltam na limpeza.
        /// </summary>
        private void ShapeTabs()
        {
            Transform tabsTable = tab != null ? tab.transform.parent : null;
            if (tabsTable == null)
            {
                return;
            }
            var natives = new List<SquircleBackgroundWidget>();
            SquircleBackgroundWidget lastVisible = null;
            float radius = 20f;
            foreach (Transform child in tabsTable)
            {
                if (child == tab.transform || child == leftSpacer || child == rightSpacer)
                {
                    continue;
                }
                SquircleBackgroundWidget background = child.Find("Background")?.GetComponent<SquircleBackgroundWidget>();
                if (background == null)
                {
                    continue;
                }
                if (!originalCorners.TryGetValue(background, out Vector2 original))
                {
                    original = new Vector2(background.cornerRadiusTopLeft, background.cornerRadiusTopRight);
                    originalCorners[background] = original;
                }
                if (original.y > 0f)
                {
                    radius = original.y;
                }
                natives.Add(background);
                UITransform ui = child.GetComponent<UITransform>();
                if (ui != null && ui.VisibleSelf)
                {
                    lastVisible = background;
                }
            }
            foreach (SquircleBackgroundWidget background in natives)
            {
                Vector2 original = originalCorners[background];
                SetTopCorners(background, original.x, background == lastVisible ? 0f : original.y);
            }
            SquircleBackgroundWidget mine = tab.transform.Find("Background")?.GetComponent<SquircleBackgroundWidget>();
            if (mine != null)
            {
                SetTopCorners(mine, 0f, radius);
            }
        }

        private void RestoreTabCorners()
        {
            foreach (KeyValuePair<SquircleBackgroundWidget, Vector2> pair in originalCorners)
            {
                if (pair.Key != null)
                {
                    SetTopCorners(pair.Key, pair.Value.x, pair.Value.y);
                }
            }
            originalCorners.Clear();
        }

        /// <summary>Só mexe quando muda: cada troca refaz o desenho do fundo.</summary>
        private static void SetTopCorners(SquircleBackgroundWidget background, float topLeft, float topRight)
        {
            if (Mathf.Abs(background.cornerRadiusTopLeft - topLeft) > 0.01f)
            {
                background.CornerRadiusTopLeft = topLeft;
            }
            if (Mathf.Abs(background.cornerRadiusTopRight - topRight) > 0.01f)
            {
                background.CornerRadiusTopRight = topRight;
            }
        }

        private void BuildCompose()
        {
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
                replyDetached = true;
                nextRefresh = 0;
            };
            Transform fieldDonor = DevTools.FindByPath(SaveNotesDonor);
            if (fieldDonor != null)
            {
                subjectItem = CloneField(fieldDonor, "SubjectField", 34f, false, 120, L.T("Assunto (opcional)"));
                subjectField = subjectItem.GetComponent<UITextField>();
                demandItem = CloneField(fieldDonor, "DemandField", 34f, false, 200, L.T("O que você exige (ultimato)"));
                demandField = demandItem.GetComponent<UITextField>();
                bodyItem = CloneField(fieldDonor, "BodyField", 300f, true, 4000, L.T("Escreva sua carta…"));
                bodyField = bodyItem.GetComponent<UITextField>();
                bodyField.TextChange += (field, text) => nextRefresh = 0;
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
                UITransform sampleUi = composeSample.GetComponent<UITransform>();
                UITransform ui = deadlineItem.GetComponent<UITransform>();
                ui.Width = sampleUi.Width;
                ui.X = sampleUi.X;
                UILabel label = deadlineItem.GetComponent<UILabel>();
                if (label != null)
                {
                    label.Margins = new RectMargins(14f, label.Margins.Right, label.Margins.Top, label.Margins.Bottom);
                }
                Transform control = deadlineItem.Find("Slider");
                if (control != null)
                {
                    UITransform controlUi = control.GetComponent<UITransform>();
                    controlUi.Width = 160f;
                    controlUi.X = sampleUi.Width - 160f;
                }
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

        private Transform CloneField(Transform donor, string name, float height, bool multiline, int maxChars, string hint)
        {
            Transform item = NativeUIKit.Clone(donor, composeTable.transform, name);
            item.gameObject.SetActive(true);
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
            NativeUIKit.BlockGameShortcuts(field);
            field.actionOnFocus = UITextFieldFocusAction.PlaceCaretAtCursor;
            if (multiline)
            {
                field.multiline = true;
                field.OnMultilineChanged(false, true);
            }
            return item;
        }

        // ---------------- Ativar e desativar ----------------

        private void Activate()
        {
            if (!built || screen == null)
            {
                return;
            }
            try
            {
                // Relações por baixo (sempre válida): o jogo não força troca de aba e desfaz o cursor de comércio.
                InternalCall = true;
                screen.SetCurrentMode(DiplomaticScreenMode.Relations);
            }
            finally
            {
                InternalCall = false;
            }
            Active = true;
            shownEmpire = -1;
            replyDetached = false;
            Reassert();
            nextRefresh = 0;
        }

        /// <summary>Esconde Relações, desliga as abas do jogo e mostra a nossa (depois de cada SetCurrentMode do jogo).</summary>
        internal void Reassert()
        {
            if (!Active || screen == null)
            {
                return;
            }
            screen.relationsPanel?.Hide(instant: true);
            screen.negociationGroup?.SetCurrentMode(DiplomaticScreenMode.None);
            if (tab != null && !tab.State)
            {
                tab.State = true;
            }
            NativeUIKit.SetVisible(panel, true);
            // O fundo com desfoque (e que segura o clique) tem que cobrir o painel inteiro.
            screen.AnimateBackgroundHeight(PanelHeight, instant: false);
        }

        /// <summary>Volta às abas do jogo. restoreNative: devolve Relações (quando a tela fecha ou o mod sai).</summary>
        internal void Deactivate(bool restoreNative)
        {
            if (!Active)
            {
                return;
            }
            Active = false;
            NativeUIKit.SetVisible(panel, false);
            if (tab != null && tab.State)
            {
                tab.State = false;
            }
            UIInteractivityManager.Instance?.SetFocus();
            if (restoreNative && screen != null && screen.currentMode == DiplomaticScreenMode.Relations)
            {
                screen.relationsPanel?.Show(instant: true);
                screen.negociationGroup?.SetCurrentMode(DiplomaticScreenMode.Relations);
                screen.AnimateBackgroundHeight(screen.relationsPanel?.PreferredHeight ?? 0f, instant: true);
            }
        }

        /// <summary>Abre a diplomacia com uma nação já na aba de cartas (botão "Diplomacia" do correio).</summary>
        internal static void OpenFor(int empire)
        {
            var cursors = Amplitude.Mercury.Presentation.Presentation.PresentationCursorController;
            if (cursors == null || !cursors.ChangeToDiplomaticCursor(empire))
            {
                return;
            }
            pendingActivation = true;
            pendingSince = Time.unscaledTime;
        }

        private static bool pendingActivation;
        private static float pendingSince;

        // ---------------- Conteúdo ----------------

        private void RefreshBlip()
        {
            if (pendingActivation && built && screen.Shown && screen.currentMode != DiplomaticScreenMode.None)
            {
                pendingActivation = false;
                if (!Active)
                {
                    Activate();
                }
            }
            else if (pendingActivation && Time.unscaledTime - pendingSince > 5f)
            {
                pendingActivation = false;
            }
            if (blip == null)
            {
                return;
            }
            IaModule module = IaModule.Instance;
            int other = Snapshots.DiplomaticCursorSnapshot?.PresentationData?.OtherEmpireIndex ?? -1;
            int unread = module == null || other < 0 ? 0 : module.PlayerInbox().Count(l => l.From == other && !l.ReadBy.Contains(module.PlayerIndex));
            if (blip.tooltip != null)
            {
                blip.tooltip.Message = unread == 1 ? L.T("1 carta nova desta nação.") : L.F("{0} cartas novas desta nação.", unread);
            }
            blip.UpdateVisibility(unread > 0 && !Active, instant: false);
        }

        private void RefreshContent()
        {
            IaModule module = IaModule.Instance;
            WorldCapture capture = TurnCapture.Latest;
            int other = Snapshots.DiplomaticCursorSnapshot?.PresentationData?.OtherEmpireIndex ?? -1;
            if (module == null || IaModule.World == null || capture == null || other < 0 || module.PlayerIndex < 0)
            {
                return;
            }
            int me = module.PlayerIndex;
            int turn = module.CurrentTurn;
            CapturedEmpire them = capture.Empire(other);
            if (other != shownEmpire)
            {
                // Outra nação: o rascunho fica, mas a ligação de resposta e o aviso recomeçam.
                replyDetached = false;
                feedback = null;
            }
            if (titleLabel != null)
            {
                titleLabel.Text = L.F("Correspondência com {0}", GameText.InlineUi(them?.Culture ?? DossierBuilder.Name(capture, other)));
            }

            List<Letter> letters = IaModule.World.Letters
                // As minhas sempre; as deles só depois de chegar (a interceptada nunca chega).
                .Where(l => l.From == me || l.Arrived(turn))
                .Where(l => (l.From == me && l.To == other) || (l.From == other && (l.To == me || l.IsPublic)))
                .Where(l => !(l.From == other && l.RejectedBy.Contains(me)))
                .OrderBy(l => l.From == me ? l.SentTurn : l.DeliverTurn).ThenBy(l => l.Id)
                .ToList();
            if (letters.Count > MaxShown)
            {
                letters = letters.Skip(letters.Count - MaxShown).ToList();
            }
            FillConversation(module, capture, letters, me, other, turn);
            bool scrollToEnd = other != shownEmpire || (letters.Count > 0 && letters[letters.Count - 1].Id != shownLastId);
            shownEmpire = other;
            shownLastId = letters.Count > 0 ? letters[letters.Count - 1].Id : -1;
            if (scrollToEnd)
            {
                listTable.GetComponentInParent<UIScrollView>()?.ResetVertically(toEnd: true);
            }
            module.MarkReadByPlayer(letters.Where(l => l.From == other));
            FillCompose(module, capture, them, me, other, turn);
        }

        private void FillConversation(IaModule module, WorldCapture capture, List<Letter> letters, int me, int other, int turn)
        {
            B.SetVisible(emptyCard, false);
            SetRowCount(letters.Count);
            if (letters.Count == 0)
            {
                Transform top = emptyCard.Find("Table/Top");
                B.SetLabel(top, "TitleGroup/Title", L.T("Nenhuma carta trocada ainda"));
                B.SetChip(top, "StatsTable/PopCount", L.T("Escreva ao lado"));
                B.Tip(top, "StatsTable/PopCount", L.T("Conversa vazia"), L.T("Vocês ainda não trocaram cartas. Escreva a primeira no painel ao lado."));
                B.HideChild(top, "StatsTable/Fortification");
                B.HideChild(top, "StatsTable/ExtensionsCount");
                B.HideChild(top, "LiberateButton");
                B.HideOutputs(top);
                emptyCard.SetAsLastSibling();
                B.SetVisible(emptyCard, true);
                return;
            }
            CapturedEmpire them = capture.Empire(other);
            for (int i = 0; i < letters.Count; i++)
            {
                Letter letter = letters[i];
                Row row = rows[i];
                row.Letter = letter;
                Transform top = row.Card.Find("Table/Top");
                bool mine = letter.From == me;
                if (mine)
                {
                    B.SetLabel(top, "TitleGroup/Title", L.T("Você"));
                    B.SetChip(top, "StatsTable/PopCount", L.F("Enviada no turno {0}", letter.SentTurn));
                    B.SetChip(top, "StatsTable/Fortification", MailScreen.TypeChip(letter.Type));
                    B.SetVisible(top.Find("StatsTable/ExtensionsCount"), true);
                    B.SetChip(top, "StatsTable/ExtensionsCount", MailScreen.StatusChip(module, letter, turn, out string help));
                    B.Tip(top, "StatsTable/ExtensionsCount", L.T("Situação"), help);
                    B.Tip(top, "StatsTable/PopCount", L.T("Enviada"), L.F("Você escreveu no turno {0}; chega no turno {1}.", letter.SentTurn, letter.DeliverTurn));
                }
                else
                {
                    bool answered = !letter.IsPublic && module.PlayerAnswered(letter);
                    string sender = them?.Leader ?? them?.Culture ?? "?";
                    B.SetLabel(top, "TitleGroup/Title", letter.IsPublic ? L.F("{0} · a todos", sender) : sender);
                    B.SetChip(top, "StatsTable/PopCount", !letter.ReadBy.Contains(me) ? $"<c={Green}>" + L.T("Nova") + "</c> · " + L.F("turno {0}", letter.DeliverTurn) : L.F("Chegou no turno {0}", letter.DeliverTurn));
                    B.Tip(top, "StatsTable/PopCount", L.T("Chegada"), L.F("Escrita no turno {0}, chegou no turno {1}.", letter.SentTurn, letter.DeliverTurn));
                    B.SetChip(top, "StatsTable/Fortification", MailScreen.TypeChip(letter.Type));
                    B.SetVisible(top.Find("StatsTable/ExtensionsCount"), !letter.IsPublic && letter.ExpectsReply);
                    if (!letter.IsPublic && letter.ExpectsReply)
                    {
                        B.SetChip(top, "StatsTable/ExtensionsCount", answered ? $"<c={Green}>" + L.T("Respondida") + "</c>" : letter.Type == "ultimato" ? MailScreen.UltimatumChip(letter, turn) : $"<c={Gold}>" + L.T("Pede resposta") + "</c>");
                        // A linha é reaproveitada: sem isto ficava o tooltip "Situação" de uma carta sua.
                        B.Tip(top, "StatsTable/ExtensionsCount", answered ? L.T("Respondida") : L.T("Pede resposta"),
                            answered ? L.T("Você já respondeu a esta carta.") : L.T("Eles esperam uma resposta sua. Responda no painel ao lado."));
                    }
                }
                B.SetVisible(row.Button.transform, false);
                MailScreen.FitTitle(top, 12f);
                B.HideOutputs(top);
                MailScreen.SetBody(row.Body, MailScreen.LetterBody(module, capture, letter, me));
                B.Tip(top, "StatsTable/Fortification", MailScreen.TypeName(letter.Type), MailScreen.TypeHelp(letter.Type));
            }
        }

        private void SetRowCount(int count)
        {
            while (rows.Count < count)
            {
                Transform card = listTable.InstantiateChild(cardSample, "Letter" + rows.Count).transform;
                rows.Add(new Row
                {
                    Card = card,
                    Body = MailScreen.AddBody(card),
                    Button = card.Find("Table/Top/LiberateButton").GetComponent<UIButton>(),
                });
            }
            for (int i = 0; i < rows.Count; i++)
            {
                B.SetVisible(rows[i].Card, i < count);
            }
            B.SetVisible(cardSample, false);
        }

        private void FillCompose(IaModule module, WorldCapture capture, CapturedEmpire them, int me, int other, int turn)
        {
            bool canWrite = capture.AiEmpires().Contains(other) && (capture.Empire(me)?.RelationWith(other)?.Knows ?? false);
            int era = capture.Empire(me)?.EraIndex ?? 0;
            int arrives = turn + Delivery.TurnsForEra(era);
            string type = TypeIds[composeType];
            Letter answerable = canWrite && !replyDetached ? module.LetterById(IaModule.AnswerableLetter(IaModule.World, me, other, turn)) : null;
            replyEmpire = other;

            B.SetVisible(typeCard, true);
            B.SetVisible(sendCard, true);
            Transform typeTop = typeCard.Find("Table/Top");
            B.SetLabel(typeTop, "TitleGroup/Title", canWrite ? L.T(TypeNames[composeType]) : L.T("Sem correio")); // TypeNames: marcados com L.N
            B.SetChip(typeTop, "StatsTable/PopCount", canWrite ? L.F("Chega no turno {0}", arrives) : L.T("Só nações do computador"));
            B.HideChild(typeTop, "StatsTable/Fortification");
            B.HideChild(typeTop, "StatsTable/ExtensionsCount");
            B.SetVisible(typeButton.transform, canWrite);
            if (canWrite)
            {
                B.SetButtonText(typeButton, L.T("Trocar"));
            }
            MailScreen.FitTitle(typeTop, typeButton.GetComponent<UITransform>().Width + 20f);
            B.HideOutputs(typeTop);
            B.Tip(typeTop, "StatsTable/PopCount", L.T("Chegada"), L.F("Sua carta viaja por {0} e chega no turno {1}. A resposta sai no turno seguinte à chegada.", Delivery.CourierName(era), arrives));
            B.Tip(typeButton.transform, string.Empty, L.T("Trocar tipo"), L.T("Carta privada ↔ Ultimato. Declarações públicas ficam no Correio."));

            B.SetVisible(replyCard, answerable != null);
            if (answerable != null)
            {
                Transform replyTop = replyCard.Find("Table/Top");
                B.SetLabel(replyTop, "TitleGroup/Title", string.IsNullOrWhiteSpace(answerable.Subject) ? L.F("Resposta à carta do turno {0}", answerable.SentTurn) : "Re: " + MailScreen.Escape(answerable.Subject).Replace("Re: ", string.Empty));
                B.SetChip(replyTop, "StatsTable/PopCount", L.F("Carta do turno {0}", answerable.SentTurn));
                B.HideChild(replyTop, "StatsTable/Fortification");
                B.HideChild(replyTop, "StatsTable/ExtensionsCount");
                B.SetButtonText(replyButton, L.T("Soltar"));
                MailScreen.FitTitle(replyTop, replyButton.GetComponent<UITransform>().Width + 20f);
                B.HideOutputs(replyTop);
                B.Tip(replyTop, "StatsTable/PopCount", L.T("Em resposta a"), L.T("A carta nova fica ligada à última carta deles que pedia resposta."));
                B.Tip(replyButton.transform, string.Empty, L.T("Soltar"), L.T("Escreve uma carta nova, sem ligar àquela."));
            }

            B.SetVisible(subjectItem, canWrite);
            B.SetVisible(bodyItem, canWrite);
            B.SetVisible(demandItem, canWrite && type == "ultimato");
            B.SetVisible(deadlineItem, canWrite && type == "ultimato");
            if (deadlineItem != null)
            {
                deadlineItem.GetComponent<UILabel>().Text = L.F("Prazo: {0} turnos", Mathf.RoundToInt(deadlineSlider.CurrentValue));
            }

            Transform sendTop = sendCard.Find("Table/Top");
            int words = bodyField != null ? DecisionParser.Words(bodyField.Text) : 0;
            int max = IaConfig.PlayerLetterMaxWords.Value;
            string title = !canWrite ? L.T("Esta nação não recebe cartas") : feedback ?? (words == 0 ? L.T("Escreva a carta acima") : L.T("Pronta para enviar"));
            B.SetLabel(sendTop, "TitleGroup/Title", feedback != null && feedbackIsError ? $"<c={Red}>{title}</c>" : title);
            B.SetChip(sendTop, "StatsTable/PopCount", words > max ? $"<c={Red}>" + L.F("{0}/{1} palavras", words, max) + "</c>" : L.F("{0}/{1} palavras", words, max));
            B.Tip(sendTop, "StatsTable/PopCount", L.T("Tamanho"), L.F("Palavras da carta. O máximo é {0}.", max));
            B.HideChild(sendTop, "StatsTable/Fortification");
            B.HideChild(sendTop, "StatsTable/ExtensionsCount");
            B.SetVisible(sendButton.transform, canWrite);
            if (canWrite)
            {
                B.SetButtonText(sendButton, L.T("Enviar"));
            }
            MailScreen.FitTitle(sendTop, sendButton.GetComponent<UITransform>().Width + 20f);
            B.HideOutputs(sendTop);
            B.Tip(sendButton.transform, string.Empty, L.T("Enviar"), L.T("Manda a carta. Ela aparece na conversa com a situação da entrega."));

            Transform[] order = { typeCard, replyCard, subjectItem, demandItem, deadlineItem, bodyItem, sendCard };
            foreach (Transform item in order)
            {
                item?.SetAsLastSibling();
            }
        }

        private void Send()
        {
            IaModule module = IaModule.Instance;
            int other = Snapshots.DiplomaticCursorSnapshot?.PresentationData?.OtherEmpireIndex ?? -1;
            if (module == null || bodyField == null || other < 0)
            {
                return;
            }
            string text = bodyField.Text.Trim();
            if (text.Length == 0)
            {
                SetFeedback(L.T("A carta está vazia."), true);
                return;
            }
            string type = TypeIds[composeType];
            string demand = demandField?.Text.Trim();
            if (type == "ultimato" && string.IsNullOrEmpty(demand))
            {
                SetFeedback(L.T("Um ultimato precisa de uma exigência."), true);
                return;
            }
            int deadline = deadlineSlider != null ? Mathf.RoundToInt(deadlineSlider.CurrentValue) : 3;
            int inReplyTo = replyDetached ? -2 : -1; // -1: o módulo liga sozinho à última carta deles que pedia resposta
            string result = module.SendPlayerLetter(other, type, subjectField?.Text.Trim(), text, demand, deadline, inReplyTo: inReplyTo);
            if (result.StartsWith("ok"))
            {
                SetFeedback(L.T("Carta enviada."), false);
                bodyField.ReplaceText(string.Empty);
                subjectField?.ReplaceText(string.Empty);
                demandField?.ReplaceText(string.Empty);
                replyDetached = false;
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

        // ---------------- Remoção ----------------

        private void Cleanup()
        {
            if (!built)
            {
                return;
            }
            try
            {
                Deactivate(restoreNative: true);
                RestoreTabCorners();
                if (leftSpacer != null && leftSpacerWidth >= 0f)
                {
                    leftSpacer.GetComponent<UITransform>().Width = leftSpacerWidth;
                }
                if (rightSpacer != null && rightSpacerWidth >= 0f)
                {
                    rightSpacer.GetComponent<UITransform>().Width = rightSpacerWidth;
                }
                Transform tabsTable = tab != null ? tab.transform.parent : null;
                NativeUIKit.Dispose(tab != null ? tab.transform : null);
                NativeUIKit.Dispose(panel);
                tabsTable?.GetComponent<Amplitude.UI.Layouts.UILayout>()?.ArrangeChildren();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Aba de cartas: limpeza: {ex.Message}");
            }
            built = false;
            tab = null;
            panel = null;
            rows.Clear();
            leftSpacerWidth = -1f;
            rightSpacerWidth = -1f;
        }

        private void OnDestroy()
        {
            Cleanup();
            if (letterIcon != null)
            {
                try
                {
                    UIRenderingManager.Instance?.UnregisterTexture(letterIcon);
                }
                catch (Exception)
                {
                }
                Destroy(letterIcon);
            }
            if (Instance == this)
            {
                Instance = null;
                Active = false;
            }
        }

        /// <summary>
        /// Clique numa aba do jogo: a nossa sai. Troca de nação com a nossa ativa: o jogo reabre Relações e a nossa
        /// volta por cima no mesmo quadro.
        /// </summary>
        [HarmonyPatch(typeof(DiplomaticScreen), nameof(DiplomaticScreen.SetCurrentMode))]
        private static class SetModePatch
        {
            private static void Prefix(DiplomaticScreen __instance, out bool __state)
            {
                __state = false;
                if (!Active || InternalCall || Instance == null)
                {
                    return;
                }
                if (__instance.isSwitchingOtherEmpire)
                {
                    __state = true;
                }
                else
                {
                    Instance.Deactivate(restoreNative: false);
                }
            }

            private static void Postfix(bool __state)
            {
                if (__state && Active)
                {
                    Instance?.Reassert();
                }
            }
        }
    }
}
