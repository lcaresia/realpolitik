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
using CurrencyMod.Diplomacy.Blocs;
using CurrencyMod.Diplomacy.Capture;
using CurrencyMod.NativeUI;
using UnityEngine;
using B = CurrencyMod.NativeUI.NativeBankWindow;

namespace CurrencyMod.Diplomacy.UI
{
    /// <summary>
    /// "Alianças" screen (melhorias-jogando item 16), in the same frame as the Mail and the Council:
    ///   left: My bloc, Agenda and election, Invitations, World blocs, History;
    ///   center: cards (the bloc and the benefits with numbers, members with fame and strength, agenda items with tally
    ///   and deadline);
    ///   right: found a bloc (name + invitees) or propose an agenda item to your bloc, and leave.
    /// The entity is "bloco" so as not to confuse it with the game's Alliance agreement. Rules in Blocs.BlocEngine.
    /// </summary>
    internal class AlliancesScreen : UIWindow
    {
        private const string WindowName = "CurrencyMod_AlliancesScreen";
        private const string FieldDonor = "LoadSavesScreen/Content/Table/SaveGameHeader/SaveNotesInputField";
        private const string Red = "E8685E";
        private const string Gold = "E3C88A";
        private const string Green = "8FD18A";
        private const string Dim = "B8AE9C";

        private const float CenterX = 420f;
        private const float CenterWidth = 1030f;
        private const float RightX = 1470f;
        private const float RightWidth = 420f;
        private const float RightHeight = 830f;
        private const float ListWidth = 928f;
        private const float ListHeight = 756f;
        private const float SideListWidth = 398f;
        private const float SideListHeight = 720f;

        internal static AlliancesScreen Instance;

        internal enum Section { Bloc, Agenda, Invites, World, History }

        private static readonly string[] SectionNames = { L.N("Meu bloco"), L.N("Pautas e eleição"), L.N("Convites"), L.N("Blocos do mundo"), L.N("Histórico") };
        private static readonly string[] SectionHelp =
        {
            L.N("O seu bloco: membros, presidente, mandato e os benefícios que estão valendo, com os números."),
            L.N("O que está em votação, as sugestões ao presidente e a eleição. Cada membro tem 1 voto."),
            L.N("Convites para você entrar num bloco e os convites que o seu bloco mandou."),
            L.N("Todos os blocos da partida, com os membros, a força somada e as tarifas."),
            L.N("Tudo o que aconteceu no seu bloco, turno a turno."),
        };

        /// <summary>Agenda item types in the order of the Switch button.</summary>
        private static readonly string[] MotionKinds = { "tarifa", "fim_tarifa", "admitir", "expulsar", "congresso", "renomear", "dissolver" };

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
        private Transform fieldItem;
        private UITextField field;
        private Section currentSection = Section.Bloc;
        private int pendingSection = -1;
        private float nextRefresh;
        private bool pendingOpen;
        private float openedAt;
        private bool wasShown;
        private bool built;

        private readonly List<Row> rows = new List<Row>();
        private readonly List<Row> sideRows = new List<Row>();

        // Right panel.
        private readonly HashSet<int> invitees = new HashSet<int>();
        private int kindIndex;
        private int target = -1;
        private int valueIndex;
        private string feedback;
        private bool feedbackIsError;
        private float confirmLeaveUntil;

        internal static bool IsOpen => Instance != null && Instance.Shown;

        private sealed class Row
        {
            public Transform Card;
            public Transform Body;
            public UIButton Button;
            public UIButton Second;
            public Action Primary;
            public Action SecondAction;
        }

        // ---------------- Creation ----------------

        internal static AlliancesScreen Create()
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
            AlliancesScreen window;
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
                window = clone.AddComponent<AlliancesScreen>();
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
            Plugin.Log.LogInfo("Alliances full screen created.");
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
            // The donor may have fewer section buttons than the five here: clones the last one.
            while (all.Count < SectionNames.Length && all.Count > 0)
            {
                Transform extra = Instantiate(all[all.Count - 1].gameObject, toggles).transform;
                extra.name = "Section" + all.Count;
                all.Add(extra);
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
            rightPanel.name = "Actions";
            NativeUIKit.Place(center, CenterX, centerUi.Y, CenterWidth, centerUi.Height);
            NativeUIKit.Place(rightPanel, RightX, 56f, RightWidth, RightHeight);

            titleLabel.transform.SetParent(center, false);
            NativeUIKit.Place(titleLabel.transform, 64f, 26f, 700f, 60f);
            descriptionLabel.transform.SetParent(center, false);
            NativeUIKit.Place(descriptionLabel.transform, 64f, 84f, CenterWidth - 128f, 24f);
            sectionTitleLabel = center.Find("SettingsTitle").GetComponent<UILabel>();
            NativeUIKit.Place(sectionTitleLabel.transform, 64f, 132f, 700f, 18f);
            NativeUIKit.Place(center.Find("Divider"), 64f, 158f, CenterWidth - 128f, 1f);
            Transform container = center.Find("GroupsContainer");
            NativeUIKit.Place(container, 38f, 170f, ListWidth, ListHeight);
            Transform list = Instantiate(citiesList.gameObject, container).transform;
            list.name = "Cards";
            MailScreen.PrepareList(list, ListWidth, ListHeight, out listTable, out cardSample);

            sideTitleLabel = rightPanel.Find("SettingsTitle").GetComponent<UILabel>();
            NativeUIKit.Place(sideTitleLabel.transform, 24f, 30f, RightWidth - 48f, 18f);
            NativeUIKit.Place(rightPanel.Find("Divider"), 24f, 56f, RightWidth - 48f, 1f);
            Transform sideContainer = rightPanel.Find("GroupsContainer");
            NativeUIKit.Place(sideContainer, -2f, 70f, SideListWidth + 8f, SideListHeight);
            Transform side = Instantiate(citiesList.gameObject, sideContainer).transform;
            side.name = "ActionList";
            MailScreen.PrepareList(side, SideListWidth + 8f, SideListHeight, out sideTable, out sideSample);
        }

        protected override IEnumerator PostLoad()
        {
            yield return base.PostLoad();
            closeButton.LeftClick += CloseButton_LeftClick;
            B.SetLabel(closeButton.transform, string.Empty, L.T("Voltar ao jogo"));
            titleLabel.Text = L.T("Alianças");
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
            foreach (UITooltip tooltip in GetComponentsInChildren<UITooltip>(true))
            {
                try
                {
                    tooltip?.Unbind(preserveTooltipClass: false);
                    if (tooltip != null)
                    {
                        tooltip.Message = string.Empty;
                    }
                }
                catch (Exception)
                {
                }
            }
            for (int i = 0; i < sectionToggles.Count; i++)
            {
                BindTooltip(sectionToggles[i].transform, L.T(SectionNames[i]), L.T(SectionHelp[i]));
            }
            BindTooltip(closeButton.transform, L.T("Voltar ao jogo"), L.T("Fecha as alianças. O ESC e o botão da barra também fecham."));
            try
            {
                BuildField();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Alliances: failed to build the text field: {ex}");
            }
            built = true;
            SelectSection(pendingSection >= 0 ? (Section)pendingSection : Section.Bloc);
            pendingSection = -1;
        }

        private static void BindTooltip(Transform target, string title, string description)
        {
            UITooltip tooltip = target != null ? target.GetComponent<UITooltip>() : null;
            tooltip?.Bind(TooltipUtils.TitleAndDescription, new Amplitude.Mercury.UI.Tooltips.TitleAndDescription(title, description));
        }

        private void BuildField()
        {
            Transform donor = DevTools.FindByPath(FieldDonor);
            if (donor == null)
            {
                Plugin.Log.LogWarning("Alliances: text field donor not found.");
                return;
            }
            fieldItem = NativeUIKit.Clone(donor, sideTable.transform, "AllianceField");
            fieldItem.gameObject.SetActive(true);
            UITransform sampleUi = sideSample.GetComponent<UITransform>();
            UITransform ui = fieldItem.GetComponent<UITransform>();
            ui.Width = sampleUi.Width;
            ui.X = sampleUi.X;
            ui.Height = 90f;
            field = fieldItem.GetComponent<UITextField>();
            field.maximumChars = 200;
            field.whiteList = string.Empty;
            field.BlackList = "<>{}";
            NativeUIKit.BlockGameShortcuts(field);
            field.actionOnFocus = UITextFieldFocusAction.PlaceCaretAtCursor;
            field.multiline = true;
            field.OnMultilineChanged(false, true);
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
            if (!built || sectionToggles.Count < SectionNames.Length)
            {
                pendingSection = (int)section;
                return;
            }
            currentSection = section;
            for (int i = 0; i < sectionToggles.Count; i++)
            {
                sectionToggles[i].State = i == (int)section;
            }
            SetRowCount(rows, listTable, cardSample, 0);
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

        private Action<IUITextField, string> fieldChanged;

        private void Update()
        {
            NativeUIKit.EnsureTextChange(field, fieldChanged ?? (fieldChanged = (f, t) => { if (feedbackIsError) { feedback = null; } nextRefresh = 0; }));
            NativeUIKit.FixLayoutAfterPaste(field);
            if (wasShown && !Shown)
            {
                NativeUIKit.ReleaseTextFocus();
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
            if (!Shown || !built || Time.unscaledTime < nextRefresh)
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
                Plugin.Log.LogError($"Alliances: {ex}");
                nextRefresh = Time.unscaledTime + 5f;
            }
        }

        // ---------------- Content ----------------

        private sealed class View
        {
            public AiModule Module;
            public AiWorld World;
            public WorldCapture Capture;
            public int Me;
            public int Turn;
            public Bloc Mine;
        }

        private View Current()
        {
            AiModule module = AiModule.Instance;
            WorldCapture capture = TurnCapture.Latest;
            AiWorld world = AiModule.World;
            if (module == null || world == null || module.PlayerIndex < 0 || capture == null || capture.Guid != world.GameGuid)
            {
                return null;
            }
            return new View { Module = module, World = world, Capture = capture, Me = module.PlayerIndex, Turn = module.CurrentTurn, Mine = BlocEngine.Of(world, module.PlayerIndex) };
        }

        private void RefreshContent()
        {
            View view = Current();
            if (view == null)
            {
                descriptionLabel.Text = L.T("As alianças aparecem quando a partida estiver carregada.");
                SetRowCount(rows, listTable, cardSample, 1);
                Notice(rows[0], L.T("Aguardando o turno"), null);
                SetRowCount(sideRows, sideTable, sideSample, 0);
                B.SetVisible(fieldItem, false);
                return;
            }
            descriptionLabel.Text = Describe(view);
            switch (currentSection)
            {
                case Section.Agenda:
                    FillAgenda(view);
                    break;
                case Section.Invites:
                    FillInvites(view);
                    break;
                case Section.World:
                    FillWorld(view);
                    break;
                case Section.History:
                    FillHistory(view);
                    break;
                default:
                    FillBloc(view);
                    break;
            }
            FillSide(view);
        }

        private static string Describe(View view)
        {
            int blocs = view.World.Blocs.Count(b => b.Formed);
            if (view.Mine == null)
            {
                int invites = BlocEngine.InvitesFor(view.World, view.Me).Count();
                return L.F("Turno {0}. Você não está em nenhum bloco · {1} bloco(s) no mundo", view.Turn, blocs)
                    + (invites > 0 ? " · " + L.F("{0} convite(s) para você", invites) : string.Empty) + ".";
            }
            return L.F("Turno {0}. {1} · {2} membros · presidente: {3}", view.Turn, Esc(view.Mine.Name), view.Mine.Members.Count, Who(view, view.Mine.President)) + ".";
        }

        // ---- My bloc ----

        private void FillBloc(View view)
        {
            Bloc bloc = view.Mine;
            if (bloc == null)
            {
                sectionTitleLabel.Text = L.T("Meu bloco");
                int wait = BlocEngine.CooldownLeft(view.World, view.Me, view.Turn);
                SetRowCount(rows, listTable, cardSample, 5);
                Notice(rows[0], L.T("Você não está em nenhum bloco"), wait > 0 ? $"<c={Red}>" + L.F("Espera: {0} turnos", wait) + "</c>" : L.T("Funde um à direita"),
                    L.T("Um bloco é uma aliança de várias nações (como a OTAN ou o BRICS). Funde o seu à direita, com um nome e pelo menos 2 convidados, ou aceite um convite na seção Convites. Cada nação fica em um bloco só."));
                FillBenefits(view, null, 1);
                return;
            }
            var members = bloc.Members.ToList();
            sectionTitleLabel.Text = L.F("{0} · {1} membros", Esc(bloc.Name), members.Count);
            SetRowCount(rows, listTable, cardSample, 1 + 4 + members.Count);
            FillHeader(view, rows[0], bloc);
            FillBenefits(view, bloc, 1);
            int index = 5;
            foreach (int member in members.OrderByDescending(m => m == bloc.President).ThenBy(m => m == view.Me ? 0 : 1))
            {
                FillMember(view, rows[index++], bloc, member);
            }
        }

        private void FillHeader(View view, Row row, Bloc bloc)
        {
            Transform top = Top(row);
            Title(row, Esc(bloc.Name));
            Chip(top, 0, bloc.Formed ? L.F("Formado no turno {0}", bloc.FormedTurn) : $"<c={Gold}>" + L.T("Em formação") + "</c>",
                L.T("Formação"), L.F("O bloco nasce com {0} membros. Convites valem {1} turnos.", Bloc.MinMembers, Bloc.InviteTurns));
            Chip(top, 1, L.F("Presidente: {0}", Short(view, bloc.President)), L.T("Presidente"),
                L.F("Só o presidente põe pautas em votação. Mandato de {0} turnos; no fim há eleição, com 1 voto por membro (empate mantém o atual).", Bloc.TermTurns));
            Chip(top, 2, bloc.ElectionOpened >= 0 ? $"<c={Gold}>" + L.T("Eleição aberta") + "</c>" : L.F("Mandato: {0} turno(s)", Math.Max(0, bloc.TermEnds - view.Turn)),
                L.T("Mandato"), L.F("O mandato atual vai até o turno {0}.", bloc.TermEnds));
            int open = bloc.OpenMotions.Count();
            Chip(top, 3, open > 0 ? L.F("{0} pauta(s) em votação", open) : null, L.T("Pautas"), L.T("Veja e vote na seção Pautas e eleição."));
            SetButtons(row, bloc.GroupId >= 0 ? L.T("Conversa do bloco") : null, () => OpenGroup(bloc), null, null);
            B.Tip(row.Button.transform, string.Empty, L.T("Conversa do bloco"), L.T("Abre o grupo do bloco no Correio: todos os membros leem cada mensagem."));
            var lines = new List<string>
            {
                L.F("Membros: {0}", string.Join(", ", bloc.Members.Select(m => Who(view, m)))),
            };
            if (bloc.Invites.Count > 0)
            {
                lines.Add(L.F("Convites pendentes: {0}", string.Join(", ", bloc.Invites.Select(i => Who(view, i.Empire)))));
            }
            lines.Add(L.F("Fundado por {0} no turno {1}.", Who(view, bloc.Founder), bloc.CreatedTurn));
            Body(row, string.Join("\n", lines));
        }

        /// <summary>The four benefits, each on a card, with the state and the numbers (no bloc: only the explanation).</summary>
        private void FillBenefits(View view, Bloc bloc, int first)
        {
            bool on = bloc != null && bloc.Formed;
            string state = on ? $"<c={Green}>" + L.T("Ativo") + "</c>" : $"<c={Dim}>" + L.T("Sem bloco") + "</c>";

            // Internal trade.
            Row trade = rows[first];
            Transform top = Top(trade);
            Title(trade, L.T("Comércio interno"));
            BlocTrade.LastRefund.TryGetValue(view.Me, out double refund);
            int routes = on ? view.Capture.Trades.Count(t => bloc.Has(t.Buyer) && bloc.Has(t.Seller) && (t.Buyer == view.Me || t.Seller == view.Me)) : 0;
            Chip(top, 0, state, L.T("Comércio interno"), L.T("Entre membros não há pedágio nem bloqueio, e a manutenção das rotas sai 25% mais barata."));
            Chip(top, 1, on ? L.F("{0} rota(s) com membros", routes) : null, L.T("Rotas"), L.T("Rotas comerciais suas com outros membros do bloco."));
            Chip(top, 2, on ? L.F("Manutenção devolvida: {0}", refund.ToString("0.#", L.Culture)) : null, L.T("Manutenção"),
                L.T("Quanto você recebeu de volta no último turno: 25% da manutenção que pagou nas rotas com membros."));
            Chip(top, 3, null, null, null);
            SetButtons(trade, null, null, null, null);
            Body(trade, L.T("Sem pedágio nem bloqueio entre membros (o posto deixa passar sozinho) e 25% de desconto na manutenção das rotas entre vocês."));

            // Mutual defense.
            Row defense = rows[first + 1];
            top = Top(defense);
            Title(defense, L.T("Defesa mútua"));
            int calls = on ? bloc.History.Count(e => e.Kind == "defesa") : 0;
            Chip(top, 0, state, L.T("Defesa mútua"), L.T("Quem é de fora e declara guerra a um membro dá aos outros membros (em paz com ele) a reclamação \"Em guerra com aliado\": um motivo de guerra. Ninguém é obrigado a lutar."));
            Chip(top, 1, on ? L.F("Acionada {0} vez(es)", calls) : null, L.T("Acionamentos"), L.T("Quantas vezes alguém de fora declarou guerra a um membro."));
            Chip(top, 2, null, null, null);
            Chip(top, 3, null, null, null);
            SetButtons(defense, null, null, null, null);
            BlocEvent lastCall = on ? bloc.History.LastOrDefault(e => e.Kind == "defesa") : null;
            Body(defense, lastCall != null
                ? L.F("Último: no turno {0}, {1} declarou guerra a {2}.", lastCall.Turn, Who(view, lastCall.Actor), Who(view, lastCall.Target))
                : L.T("Guerra entre membros não expulsa ninguém, mas também não aciona a defesa."));

            // Congress.
            Row congress = rows[first + 2];
            top = Top(congress);
            Title(congress, L.T("Voz no Congresso"));
            string line = on && bloc.CongressLine != null && BlocCongress.SessionKeyFor(bloc.CongressLine.Choice, view.Capture) == bloc.CongressLine.SessionKey
                ? CongressChoiceUi(view, bloc.CongressLine.Choice) : null;
            Chip(top, 0, state, L.T("Voz no Congresso"), L.T("Com a pauta \"Votar junto no Congresso\" aprovada, todos os membros controlados pela IA votam no mesmo lado; o peso de cada um soma. Você vota pela tela do Congresso."));
            Chip(top, 1, on ? (line != null ? L.F("Posição: {0}", line) : L.T("Sem posição nesta votação")) : null, L.T("Posição"), L.T("A posição vale para a votação aberta quando a pauta foi aprovada."));
            Chip(top, 2, null, null, null);
            Chip(top, 3, null, null, null);
            SetButtons(congress, null, null, null, null);
            Body(congress, L.T("Uma votação do Congresso aberta pode virar pauta do bloco (à direita: tipo \"Votar junto no Congresso\")."));

            // Common tariff.
            Row tariff = rows[first + 3];
            top = Top(tariff);
            Title(tariff, L.T("Tarifa comum"));
            Chip(top, 0, state, L.T("Tarifa comum"), L.T("Pedágio ou bloqueio contra uma nação de fora, em todos os postos de todos os membros. Vale o mais duro entre a tarifa e a regra do próprio posto."));
            Chip(top, 1, on ? L.F("{0} tarifa(s)", bloc.Tariffs.Count) : null, L.T("Tarifas"), L.T("Tarifas comuns em vigor."));
            Chip(top, 2, null, null, null);
            Chip(top, 3, null, null, null);
            SetButtons(tariff, null, null, null, null);
            Body(tariff, on && bloc.Tariffs.Count > 0
                ? string.Join("\n", bloc.Tariffs.Select(t => L.F("{0} contra {1}, desde o turno {2}", t.Mode == "bloqueio" ? L.T("Bloqueio") : L.T("Pedágio"), Who(view, t.Target), t.Since)))
                : L.T("Nenhuma tarifa comum em vigor."));
        }

        private void FillMember(View view, Row row, Bloc bloc, int member)
        {
            CapturedEmpire empire = view.Capture.Empire(member);
            Transform top = Top(row);
            Title(row, member == view.Me ? L.F("{0} (você)", Esc(MailScreen.NationAndLeader(view.Capture, member))) : Esc(MailScreen.NationAndLeader(view.Capture, member)));
            string role = member == bloc.President ? $"<c={Gold}>" + L.T("Presidente") + "</c>" : member == bloc.Founder ? L.T("Fundador") : L.T("Membro");
            Chip(top, 0, role, L.T("Papel"), L.T("O presidente põe pautas em votação; os outros sugerem."));
            Chip(top, 1, empire != null ? L.F("Fama {0}", Math.Round(empire.Fame)) : null, L.T("Fama"), L.F("{0}º lugar em fama.", empire?.FameRank ?? 0));
            Chip(top, 2, empire != null ? L.F("Força {0}", Math.Round(empire.MilitaryStrength)) : null, L.T("Força militar"), L.F("{0} unidades.", empire?.UnitCount ?? 0));
            Chip(top, 3, empire != null ? (empire.EraName ?? L.F("Era {0}", empire.EraIndex + 1)) : null, L.T("Era"), L.F("{0} cidade(s), {1} território(s).", empire?.Cities.Count ?? 0, empire?.TerritoryCount ?? 0));
            bool canExpel = member != view.Me && bloc.Formed;
            bool president = bloc.President == view.Me;
            SetButtons(row, canExpel ? (president ? L.T("Pôr expulsão em votação") : L.T("Sugerir expulsão")) : null,
                () => Propose(view, "expulsar", member, null, null), null, null);
            if (canExpel)
            {
                B.Tip(row.Button.transform, string.Empty, L.T("Expulsão"), L.T("Expulsar exige 2/3 dos membros (quem é alvo não vota). Quem sai espera 10 turnos para entrar em outro bloco."));
            }
            CapturedRelation relation = view.Capture.Empire(view.Me)?.RelationWith(member);
            var lines = new List<string>();
            if (member != view.Me && relation != null)
            {
                lines.Add(relation.AtWar ? $"<c={Red}>" + L.T("Em guerra com você") + "</c>" : relation.Alliance ? L.T("Aliança do jogo com você") : L.T("Em paz com você"));
            }
            if (empire != null)
            {
                lines.Add(L.F("Dinheiro {0} · ciência {1} · população {2}", Math.Round(empire.Money), Math.Round(empire.Science), empire.Population));
            }
            Body(row, string.Join("\n", lines));
        }

        // ---- Agenda and election ----

        private void FillAgenda(View view)
        {
            Bloc bloc = view.Mine;
            if (bloc == null || !bloc.Formed)
            {
                sectionTitleLabel.Text = L.T("Pautas e eleição");
                SetRowCount(rows, listTable, cardSample, 1);
                Notice(rows[0], L.T("Sem pautas"), null, L.T("As pautas aparecem quando você está num bloco formado."));
                return;
            }
            var open = bloc.OpenMotions.ToList();
            var suggestions = bloc.Suggestions.ToList();
            var closed = bloc.Motions.Where(m => m.ClosedTurn >= 0).OrderByDescending(m => m.ClosedTurn).ThenByDescending(m => m.Id).Take(6).ToList();
            bool election = bloc.ElectionOpened >= 0;
            int count = (election ? 1 + bloc.Members.Count : 0) + open.Count + suggestions.Count + closed.Count;
            sectionTitleLabel.Text = L.F("Pautas · {0} em votação · {1} sugestão(ões)", open.Count, suggestions.Count);
            if (count == 0)
            {
                SetRowCount(rows, listTable, cardSample, 1);
                Notice(rows[0], L.T("Nenhuma pauta"), null, bloc.President == view.Me
                    ? L.T("Você é o presidente: proponha uma pauta à direita e ela vai direto à votação.")
                    : L.T("Proponha uma pauta à direita: ela vira sugestão, e o presidente decide se põe em votação."));
                return;
            }
            SetRowCount(rows, listTable, cardSample, count);
            int index = 0;
            if (election)
            {
                FillElection(view, rows[index++], bloc);
                foreach (int candidate in bloc.Members)
                {
                    FillCandidate(view, rows[index++], bloc, candidate);
                }
            }
            foreach (BlocMotion motion in open)
            {
                FillMotion(view, rows[index++], bloc, motion);
            }
            foreach (BlocMotion motion in suggestions)
            {
                FillSuggestion(view, rows[index++], bloc, motion);
            }
            foreach (BlocMotion motion in closed)
            {
                FillClosed(view, rows[index++], motion);
            }
        }

        private void FillElection(View view, Row row, Bloc bloc)
        {
            Transform top = Top(row);
            Title(row, $"<c={Gold}>" + L.T("Eleição de presidente") + "</c>");
            Chip(top, 0, L.F("Fecha no turno {0}", bloc.ElectionOpened + Bloc.VoteTurns), L.T("Prazo"), L.T("Fecha antes se todos votarem."));
            Chip(top, 1, L.F("{0}/{1} votaram", bloc.ElectionVotes.Count, bloc.Members.Count), L.T("Votos"), L.T("1 voto por membro. Ganha quem tiver mais votos; empate mantém o atual."));
            Chip(top, 2, bloc.ElectionVotes.TryGetValue(view.Me, out int mine) ? L.F("Seu voto: {0}", Short(view, mine)) : $"<c={Gold}>" + L.T("Você ainda não votou") + "</c>", L.T("Seu voto"), L.T("Você pode mudar o voto até a eleição fechar."));
            Chip(top, 3, null, null, null);
            SetButtons(row, null, null, null, null);
            Body(row, L.F("O mandato de {0} acabou. Escolha abaixo quem preside o bloco pelos próximos {1} turnos.", Who(view, bloc.President), Bloc.TermTurns));
        }

        private void FillCandidate(View view, Row row, Bloc bloc, int candidate)
        {
            Transform top = Top(row);
            int votes = bloc.ElectionVotes.Count(v => v.Value == candidate);
            bool chosen = bloc.ElectionVotes.TryGetValue(view.Me, out int mine) && mine == candidate;
            Title(row, $"<c={Gold}>›</c> " + Esc(MailScreen.NationAndLeader(view.Capture, candidate)) + (candidate == view.Me ? " " + L.T("(você)") : string.Empty));
            Chip(top, 0, L.F("{0} voto(s)", votes), L.T("Votos"), L.T("Votos recebidos até agora."));
            Chip(top, 1, candidate == bloc.President ? L.T("Presidente atual") : null, L.T("Presidente atual"), L.T("Fica no cargo se houver empate."));
            Chip(top, 2, chosen ? $"<c={Green}>" + L.T("Seu voto") + "</c>" : null, L.T("Seu voto"), string.Empty);
            Chip(top, 3, null, null, null);
            SetButtons(row, chosen ? null : L.T("Votar"), () => Act(BlocEngine.VotePresident(view.World, view.Me, candidate, view.Turn), L.T("Voto registrado.")), null, null);
            Body(row, string.Empty);
        }

        private void FillMotion(View view, Row row, Bloc bloc, BlocMotion motion)
        {
            Transform top = Top(row);
            Title(row, MotionTitle(view, motion));
            Chip(top, 0, L.F("Sim {0} · Não {1}", motion.Yes, motion.No), L.T("Placar"),
                motion.Kind == "expulsar" ? L.T("Expulsar exige 2/3 dos membros que votam (o alvo não vota).") : L.T("Maioria simples dos votos; no empate vale o voto do presidente. Sem voto = abstenção."));
            Chip(top, 1, L.F("Fecha no turno {0}", motion.Deadline), L.T("Prazo"), L.T("Fecha antes se todos votarem."));
            Chip(top, 2, L.F("Por {0}", Short(view, motion.Author)), L.T("Autor"), string.Empty);
            bool voted = motion.Votes.TryGetValue(view.Me, out bool yes);
            bool target = motion.Kind == "expulsar" && motion.Target == view.Me;
            Chip(top, 3, target ? $"<c={Red}>" + L.T("Você é o alvo") + "</c>" : voted ? L.F("Seu voto: {0}", yes ? L.T("sim") : L.T("não")) : $"<c={Gold}>" + L.T("Falta seu voto") + "</c>",
                L.T("Seu voto"), L.T("Você pode mudar o voto até a pauta fechar."));
            if (target)
            {
                SetButtons(row, null, null, null, null);
            }
            else
            {
                SetButtons(row, L.T("Sim"), () => Act(BlocEngine.Vote(view.World, view.Me, motion.Id, true, view.Turn), L.T("Voto registrado.")),
                    L.T("Não"), () => Act(BlocEngine.Vote(view.World, view.Me, motion.Id, false, view.Turn), L.T("Voto registrado.")));
            }
            Body(row, MotionBody(view, motion));
        }

        private void FillSuggestion(View view, Row row, Bloc bloc, BlocMotion motion)
        {
            Transform top = Top(row);
            bool president = bloc.President == view.Me;
            Title(row, L.F("Sugestão: {0}", MotionTitle(view, motion)));
            Chip(top, 0, L.F("De {0}", Short(view, motion.Author)), L.T("Autor"), string.Empty);
            Chip(top, 1, L.F("Turno {0}", motion.SuggestedTurn), L.T("Sugerida"), string.Empty);
            Chip(top, 2, president ? $"<c={Gold}>" + L.T("Você decide") + "</c>" : L.T("Aguarda o presidente"), L.T("Sugestão"), L.T("Só o presidente põe sugestões em votação."));
            Chip(top, 3, null, null, null);
            if (president)
            {
                SetButtons(row, L.T("Pôr em votação"), () => Act(BlocEngine.Schedule(view.World, view.Me, motion.Id, true, view.Turn), L.T("Pauta em votação.")),
                    L.T("Descartar"), () => Act(BlocEngine.Schedule(view.World, view.Me, motion.Id, false, view.Turn), L.T("Sugestão descartada.")));
            }
            else
            {
                SetButtons(row, null, null, null, null);
            }
            Body(row, MotionBody(view, motion));
        }

        private void FillClosed(View view, Row row, BlocMotion motion)
        {
            Transform top = Top(row);
            Title(row, $"<c={Dim}>" + MotionTitle(view, motion) + "</c>");
            string status = motion.Status == BlocMotion.Passed ? $"<c={Green}>" + L.T("Aprovada") + "</c>"
                : motion.Status == BlocMotion.Failed ? $"<c={Red}>" + L.T("Rejeitada") + "</c>" : L.T("Descartada");
            Chip(top, 0, status, L.T("Resultado"), string.Empty);
            Chip(top, 1, motion.Status == BlocMotion.Dropped ? null : L.F("Sim {0} · Não {1}", motion.Yes, motion.No), L.T("Placar"), string.Empty);
            Chip(top, 2, L.F("Turno {0}", motion.ClosedTurn), L.T("Fechada"), string.Empty);
            Chip(top, 3, null, null, null);
            SetButtons(row, null, null, null, null);
            Body(row, string.Empty);
        }

        private string MotionTitle(View view, BlocMotion motion)
        {
            switch (motion.Kind)
            {
                case "tarifa": return L.F("{0} contra {1}", motion.Arg == "bloqueio" ? L.T("Bloqueio comum") : L.T("Pedágio comum"), Short(view, motion.Target));
                case "fim_tarifa": return L.F("Tirar a tarifa contra {0}", Short(view, motion.Target));
                case "admitir": return L.F("Convidar {0}", Short(view, motion.Target));
                case "expulsar": return L.F("Expulsar {0}", Short(view, motion.Target));
                case "congresso": return L.F("Votar junto no Congresso: {0}", CongressChoiceUi(view, motion.Arg));
                case "renomear": return L.F("Renomear para \"{0}\"", Esc(motion.Arg));
                case "dissolver": return L.T("Desfazer o bloco");
            }
            return motion.Kind;
        }

        private string MotionBody(View view, BlocMotion motion)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(motion.Reason))
            {
                lines.Add(L.F("Motivo: {0}", Esc(motion.Reason)));
            }
            if (motion.Votes.Count > 0)
            {
                lines.Add(L.F("Votos: {0}", string.Join(", ", motion.Votes.Select(v => $"{Short(view, v.Key)} {(v.Value ? L.T("sim") : L.T("não"))}"))));
            }
            return string.Join("\n", lines);
        }

        // ---- Invitations ----

        private void FillInvites(View view)
        {
            var mine = BlocEngine.InvitesFor(view.World, view.Me).ToList();
            var sent = view.Mine?.Invites.ToList() ?? new List<BlocInvite>();
            sectionTitleLabel.Text = L.F("Convites · {0} para você · {1} enviado(s)", mine.Count, sent.Count);
            if (mine.Count + sent.Count == 0)
            {
                SetRowCount(rows, listTable, cardSample, 1);
                Notice(rows[0], L.T("Nenhum convite"), null, L.T("Quando uma nação convidar você para um bloco, o convite aparece aqui por 5 turnos."));
                return;
            }
            SetRowCount(rows, listTable, cardSample, mine.Count + sent.Count);
            int index = 0;
            int wait = BlocEngine.CooldownLeft(view.World, view.Me, view.Turn);
            foreach (Bloc bloc in mine)
            {
                BlocInvite invite = bloc.InviteFor(view.Me);
                Row row = rows[index++];
                Transform top = Top(row);
                Title(row, L.F("Convite: {0}", Esc(bloc.Name)));
                Chip(top, 0, L.F("De {0}", Short(view, invite.From)), L.T("Quem convidou"), string.Empty);
                Chip(top, 1, L.F("{0} membro(s)", bloc.Members.Count), L.T("Membros"), string.Join(", ", bloc.Members.Select(m => Who(view, m))));
                Chip(top, 2, L.F("Vence no turno {0}", invite.Turn + Bloc.InviteTurns), L.T("Prazo"), L.T("Sem resposta, o convite vence."));
                Chip(top, 3, view.Mine != null ? $"<c={Red}>" + L.T("Você já tem bloco") + "</c>" : wait > 0 ? $"<c={Red}>" + L.F("Espera: {0} turnos", wait) + "</c>" : null,
                    L.T("Um bloco por nação"), L.T("Para aceitar, saia do seu bloco antes (e espere 10 turnos)."));
                SetButtons(row, L.T("Aceitar"), () => Act(BlocEngine.Respond(view.World, view.Me, bloc, true, view.Turn), L.F("Você entrou em {0}.", Esc(bloc.Name))),
                    L.T("Recusar"), () => Act(BlocEngine.Respond(view.World, view.Me, bloc, false, view.Turn), L.T("Convite recusado.")));
                Body(row, L.F("Membros: {0}", string.Join(", ", bloc.Members.Select(m => Who(view, m))))
                    + "\n" + L.F("Presidente: {0}. {1}", Who(view, bloc.President), bloc.Formed ? L.F("Formado no turno {0}.", bloc.FormedTurn) : L.T("Ainda em formação.")));
            }
            foreach (BlocInvite invite in sent)
            {
                Row row = rows[index++];
                Transform top = Top(row);
                Title(row, L.F("Enviado a {0}", Esc(MailScreen.NationAndLeader(view.Capture, invite.Empire))));
                Chip(top, 0, L.F("Turno {0}", invite.Turn), L.T("Enviado"), string.Empty);
                Chip(top, 1, L.F("Vence no turno {0}", invite.Turn + Bloc.InviteTurns), L.T("Prazo"), string.Empty);
                Chip(top, 2, L.T("Aguardando resposta"), L.T("Situação"), string.Empty);
                Chip(top, 3, null, null, null);
                SetButtons(row, null, null, null, null);
                Body(row, string.Empty);
            }
        }

        // ---- World blocs ----

        private void FillWorld(View view)
        {
            var blocs = view.World.Blocs.OrderByDescending(b => b == view.Mine).ThenByDescending(b => b.Members.Count).ToList();
            sectionTitleLabel.Text = L.F("Blocos do mundo · {0}", blocs.Count);
            if (blocs.Count == 0)
            {
                SetRowCount(rows, listTable, cardSample, 1);
                Notice(rows[0], L.T("Nenhum bloco ainda"), null, L.T("Ninguém fundou um bloco nesta partida. As IAs também podem fundar e convidar."));
                return;
            }
            SetRowCount(rows, listTable, cardSample, blocs.Count);
            for (int i = 0; i < blocs.Count; i++)
            {
                Bloc bloc = blocs[i];
                Row row = rows[i];
                Transform top = Top(row);
                Title(row, Esc(bloc.Name) + (bloc == view.Mine ? $" <c={Gold}>· " + L.T("seu bloco") + "</c>" : string.Empty));
                double fame = bloc.Members.Sum(m => view.Capture.Empire(m)?.Fame ?? 0);
                double strength = bloc.Members.Sum(m => view.Capture.Empire(m)?.MilitaryStrength ?? 0);
                Chip(top, 0, bloc.Formed ? L.F("{0} membros", bloc.Members.Count) : $"<c={Gold}>" + L.T("Em formação") + "</c>", L.T("Membros"), string.Empty);
                Chip(top, 1, L.F("Presidente: {0}", Short(view, bloc.President)), L.T("Presidente"), string.Empty);
                Chip(top, 2, L.F("Fama somada {0}", Math.Round(fame)), L.T("Fama somada"), L.T("Soma da fama dos membros."));
                Chip(top, 3, L.F("Força somada {0}", Math.Round(strength)), L.T("Força somada"), L.T("Soma da força militar dos membros."));
                SetButtons(row, null, null, null, null);
                var lines = new List<string> { L.F("Membros: {0}", string.Join(", ", bloc.Members.Select(m => Who(view, m)))) };
                foreach (BlocTariff tariff in bloc.Tariffs)
                {
                    string text = L.F("{0} contra {1}", tariff.Mode == "bloqueio" ? L.T("Bloqueio comum") : L.T("Pedágio comum"), Who(view, tariff.Target));
                    lines.Add(tariff.Target == view.Me ? $"<c={Red}>{text}</c>" : text);
                }
                Body(row, string.Join("\n", lines));
            }
        }

        // ---- History ----

        private void FillHistory(View view)
        {
            Bloc bloc = view.Mine;
            if (bloc == null || bloc.History.Count == 0)
            {
                sectionTitleLabel.Text = L.T("Histórico");
                SetRowCount(rows, listTable, cardSample, 1);
                Notice(rows[0], L.T("Nada ainda"), null, L.T("O histórico do seu bloco aparece aqui: entradas, saídas, pautas e eleições."));
                return;
            }
            var turns = bloc.History.GroupBy(e => e.Turn).OrderByDescending(g => g.Key).ToList();
            sectionTitleLabel.Text = L.F("Histórico · {0} acontecimento(s)", bloc.History.Count);
            SetRowCount(rows, listTable, cardSample, turns.Count);
            for (int i = 0; i < turns.Count; i++)
            {
                Row row = rows[i];
                Transform top = Top(row);
                Title(row, L.F("Turno {0}", turns[i].Key));
                Chip(top, 0, L.F("{0} acontecimento(s)", turns[i].Count()), L.T("Acontecimentos"), string.Empty);
                Chip(top, 1, null, null, null);
                Chip(top, 2, null, null, null);
                Chip(top, 3, null, null, null);
                SetButtons(row, null, null, null, null);
                Body(row, string.Join("\n", turns[i].Select(e => Capitalize(EventUi(view, e)))));
            }
        }

        private string EventUi(View view, BlocEvent ev)
        {
            switch (ev.Kind)
            {
                case "fundou": return L.F("{0} fundou o bloco.", Who(view, ev.Actor));
                case "entrou": return L.F("{0} entrou.", Who(view, ev.Actor));
                case "saiu": return L.F("{0} saiu.", Who(view, ev.Actor));
                case "expulso": return $"<c={Red}>" + L.F("{0} foi expulso.", Who(view, ev.Actor)) + "</c>";
                case "recusou": return L.F("{0} recusou o convite.", Who(view, ev.Actor));
                case "convidou": return L.F("{0} foi convidado.", Who(view, ev.Target));
                case "convite_venceu": return L.F("O convite a {0} venceu.", Who(view, ev.Target));
                case "formado": return $"<c={Green}>" + L.T("O bloco se formou.") + "</c>";
                case "falhou": return L.T("O bloco não se formou a tempo.");
                case "desfeito": return $"<c={Red}>" + L.T("O bloco se desfez.") + "</c>";
                case "presidente": return $"<c={Gold}>" + L.F("{0} é o presidente.", Who(view, ev.Actor)) + "</c>";
                case "eleicao": return L.T("Eleição de presidente aberta.");
                case "pauta": return L.F("{0} pôs em votação: {1}.", Who(view, ev.Actor), KindUi(ev.Arg) + Target(view, ev));
                case "sugeriu": return L.F("{0} sugeriu: {1}.", Who(view, ev.Actor), KindUi(ev.Arg) + Target(view, ev));
                case "descartada": return L.F("{0} descartou a sugestão: {1}.", Who(view, ev.Actor), KindUi(ev.Arg) + Target(view, ev));
                case "aprovada": return $"<c={Green}>" + L.F("Aprovada: {0}.", KindUi(ev.Arg) + Target(view, ev)) + "</c>";
                case "rejeitada": return $"<c={Red}>" + L.F("Rejeitada: {0}.", KindUi(ev.Arg) + Target(view, ev)) + "</c>";
                case "renomeado": return L.F("O bloco mudou de nome (antes: {0}).", Esc(ev.Arg));
                case "defesa": return $"<c={Red}>" + L.F("{0} declarou guerra a {1}; os membros em paz com o agressor ganharam um motivo de guerra.", Who(view, ev.Actor), Who(view, ev.Target)) + "</c>";
            }
            return ev.Kind;
        }

        /// <summary>First visible letter in uppercase (skips the leading color tags).</summary>
        private static string Capitalize(string text)
        {
            int i = 0;
            while (i < text.Length && text[i] == '<')
            {
                int end = text.IndexOf('>', i);
                if (end < 0)
                {
                    break;
                }
                i = end + 1;
            }
            return i < text.Length ? text.Substring(0, i) + char.ToUpper(text[i]) + text.Substring(i + 1) : text;
        }

        private string Target(View view, BlocEvent ev) => ev.Target >= 0 ? " (" + Short(view, ev.Target) + ")" : string.Empty;

        /// <summary>Name of the agenda item type (the history stores "type" or "type:value").</summary>
        private static string KindUi(string kind)
        {
            string value = null;
            if (kind != null && kind.Contains(":"))
            {
                value = kind.Substring(kind.IndexOf(':') + 1);
                kind = kind.Substring(0, kind.IndexOf(':'));
            }
            string name;
            switch (kind)
            {
                case "tarifa": name = value == "bloqueio" ? L.T("bloqueio comum") : L.T("pedágio comum"); break;
                case "fim_tarifa": name = L.T("tirar a tarifa comum"); break;
                case "admitir": name = L.T("convidar"); break;
                case "expulsar": name = L.T("expulsar"); break;
                case "congresso": name = L.T("votar junto no Congresso"); break;
                case "renomear": name = value != null ? L.F("renomear para \"{0}\"", Esc(value)) : L.T("renomear"); break;
                case "dissolver": name = L.T("desfazer o bloco"); break;
                default: name = kind ?? "?"; break;
            }
            return name;
        }

        private static string KindName(string kind)
        {
            switch (kind)
            {
                case "tarifa": return L.T("Tarifa comum");
                case "fim_tarifa": return L.T("Tirar tarifa comum");
                case "admitir": return L.T("Convidar nação");
                case "expulsar": return L.T("Expulsar membro");
                case "congresso": return L.T("Votar junto no Congresso");
                case "renomear": return L.T("Renomear o bloco");
                case "dissolver": return L.T("Desfazer o bloco");
            }
            return kind;
        }

        private string CongressChoiceUi(View view, string choice)
        {
            if (choice == "lei:A" || choice == "lei:B")
            {
                CongressLaw law = view.Capture.Congress?.LawVote?.Law;
                string option = law == null ? choice.Substring(4) : choice == "lei:A" ? law.ChoiceA : law.ChoiceB;
                return L.F("lei, opção {0}", Esc(GameGlossary.Title(option) ?? option));
            }
            string[] parts = (choice ?? string.Empty).Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1].TrimStart('E'), out int side))
            {
                return L.F("apoiar {0} na crise", Short(view, side));
            }
            return Esc(choice);
        }

        // ---------------- Right panel ----------------

        private void FillSide(View view)
        {
            Bloc bloc = view.Mine;
            var order = new List<Transform>();
            if (bloc == null)
            {
                FillFoundForm(view, order);
            }
            else if (!bloc.Formed)
            {
                FillForming(view, bloc, order);
            }
            else
            {
                FillProposeForm(view, bloc, order);
            }
            foreach (Transform item in order)
            {
                item?.SetAsLastSibling();
            }
        }

        private void FillFoundForm(View view, List<Transform> order)
        {
            sideTitleLabel.Text = L.T("Fundar um bloco");
            List<int> known = MailScreen.KnownNations(view.Capture);
            invitees.RemoveWhere(e => !known.Contains(e));
            int wait = BlocEngine.CooldownLeft(view.World, view.Me, view.Turn);
            SetRowCount(sideRows, sideTable, sideSample, 2 + known.Count);

            Row head = sideRows[0];
            Transform top = Top(head);
            Title(head, L.T("Novo bloco"));
            Chip(top, 0, L.F("{0} convidado(s)", invitees.Count), L.T("Convidados"), L.F("Marque abaixo. O bloco nasce quando {0} nações (com você) estiverem dentro.", Bloc.MinMembers));
            Chip(top, 1, wait > 0 ? $"<c={Red}>" + L.F("Espera: {0} turnos", wait) + "</c>" : L.F("Mínimo {0}", Bloc.MinMembers - 1), L.T("Mínimo"),
                L.F("Convide pelo menos {0} nações.", Bloc.MinMembers - 1));
            Chip(top, 2, null, null, null);
            Chip(top, 3, null, null, null);
            SetButtons(head, null, null, null, null);
            Body(head, L.T("Dê um nome ao bloco no campo abaixo e marque os convidados."));
            order.Add(head.Card);

            ShowField(L.T("Nome do bloco (ex.: Liga do Mar)"), L.T("Nome do bloco"), L.T("Até 40 letras. Os membros podem renomear depois, por pauta."));
            order.Add(fieldItem);

            Row send = sideRows[1];
            top = Top(send);
            string ready = invitees.Count < Bloc.MinMembers - 1 ? L.F("Marque mais {0}", Bloc.MinMembers - 1 - invitees.Count) : L.T("Pronto para fundar");
            Title(send, FeedbackTitle(ready));
            Chip(top, 0, L.F("Convites valem {0} turnos", Bloc.InviteTurns), L.T("Prazo"), L.T("Quem não responder a tempo fica de fora."));
            Chip(top, 1, null, null, null);
            Chip(top, 2, null, null, null);
            Chip(top, 3, null, null, null);
            SetButtons(send, L.T("Fundar"), () => Found(view), null, null);
            Body(send, string.Empty);
            B.SetVisible(send.Body, false);
            order.Add(send.Card);

            for (int i = 0; i < known.Count; i++)
            {
                int empire = known[i];
                Row row = sideRows[2 + i];
                top = Top(row);
                Bloc other = BlocEngine.Of(view.World, empire);
                Title(row, Esc(view.Capture.Empire(empire)?.Culture ?? "E" + empire));
                Chip(top, 0, other != null ? L.F("Em {0}", Esc(other.Name)) : view.Capture.Empire(view.Me)?.RelationWith(empire)?.AtWar == true ? $"<c={Red}>" + L.T("Em guerra") + "</c>" : L.T("Livre"),
                    L.T("Situação"), L.T("Quem já está num bloco não pode aceitar (um bloco por nação)."));
                Chip(top, 1, null, null, null);
                Chip(top, 2, null, null, null);
                Chip(top, 3, null, null, null);
                bool on = invitees.Contains(empire);
                SetButtons(row, other == null ? (on ? L.T("Convidado") : L.T("+ Convidar")) : null, () =>
                {
                    if (!invitees.Remove(empire))
                    {
                        invitees.Add(empire);
                    }
                    nextRefresh = 0;
                }, null, null);
                Body(row, string.Empty);
                B.SetVisible(row.Body, false);
                order.Add(row.Card);
            }
        }

        private void FillForming(View view, Bloc bloc, List<Transform> order)
        {
            sideTitleLabel.Text = L.T("Bloco em formação");
            B.SetVisible(fieldItem, false);
            SetRowCount(sideRows, sideTable, sideSample, 1);
            Row row = sideRows[0];
            Transform top = Top(row);
            Title(row, FeedbackTitle(Esc(bloc.Name)));
            Chip(top, 0, L.F("{0}/{1} membros", bloc.Members.Count, Bloc.MinMembers), L.T("Formação"), L.T("O bloco nasce quando 3 nações estiverem dentro."));
            Chip(top, 1, L.F("{0} convite(s) abertos", bloc.Invites.Count), L.T("Convites"), string.Join(", ", bloc.Invites.Select(i => Who(view, i.Empire))));
            Chip(top, 2, null, null, null);
            Chip(top, 3, null, null, null);
            bool confirming = Time.unscaledTime < confirmLeaveUntil;
            SetButtons(row, confirming ? L.T("Confirmar") : L.T("Desistir"), () => Leave(view), null, null);
            Body(row, L.T("Aguardando as respostas. Se ninguém aceitar a tempo, o bloco não se forma."));
            order.Add(row.Card);
        }

        private void FillProposeForm(View view, Bloc bloc, List<Transform> order)
        {
            bool president = bloc.President == view.Me;
            string kind = MotionKinds[Mathf.Clamp(kindIndex, 0, MotionKinds.Length - 1)];
            List<int> targets = Targets(view, bloc, kind);
            if (targets.Count > 0 && !targets.Contains(target))
            {
                target = targets[0];
            }
            List<string> values = Values(view, kind);
            string value = values.Count > 0 ? values[Mathf.Clamp(valueIndex, 0, values.Count - 1)] : null;
            bool needsTarget = kind == "tarifa" || kind == "fim_tarifa" || kind == "admitir" || kind == "expulsar";
            bool needsValue = kind == "tarifa" || kind == "congresso";

            sideTitleLabel.Text = president ? L.T("Pôr em votação") : L.T("Sugerir ao presidente");
            SetRowCount(sideRows, sideTable, sideSample, 5);

            Row typeRow = sideRows[0];
            Transform top = Top(typeRow);
            Title(typeRow, KindName(kind));
            Chip(top, 0, president ? $"<c={Gold}>" + L.T("Vai direto à votação") + "</c>" : L.T("Vira sugestão"), L.T("Como funciona"),
                president ? L.T("Você é o presidente: a pauta entra em votação na hora, por 3 turnos.") : L.T("O presidente decide se põe a sua sugestão em votação."));
            Chip(top, 1, null, null, null);
            Chip(top, 2, null, null, null);
            Chip(top, 3, null, null, null);
            SetButtons(typeRow, L.T("Trocar"), () => { kindIndex = (kindIndex + 1) % MotionKinds.Length; valueIndex = 0; feedback = null; nextRefresh = 0; }, null, null);
            B.Tip(typeRow.Button.transform, string.Empty, L.T("Trocar o tipo"), L.T("Tarifa comum, tirar tarifa, convidar, expulsar, votar junto no Congresso, renomear ou desfazer."));
            Body(typeRow, KindHelp(kind));
            order.Add(typeRow.Card);

            Row targetRow = sideRows[1];
            B.SetVisible(targetRow.Card, needsTarget);
            if (needsTarget)
            {
                top = Top(targetRow);
                Title(targetRow, targets.Count > 0 ? L.F("Nação: {0}", Esc(Short(view, target))) : L.T("Nenhuma nação possível"));
                Chip(top, 0, targets.Count > 0 ? L.F("{0} opção(ões)", targets.Count) : null, L.T("Opções"), string.Empty);
                Chip(top, 1, null, null, null);
                Chip(top, 2, null, null, null);
                Chip(top, 3, null, null, null);
                SetButtons(targetRow, targets.Count > 1 ? L.T("Trocar") : null, () => { target = targets[(targets.IndexOf(target) + 1) % targets.Count]; feedback = null; nextRefresh = 0; }, null, null);
                Body(targetRow, string.Empty);
                B.SetVisible(targetRow.Body, false);
                order.Add(targetRow.Card);
            }

            Row valueRow = sideRows[2];
            B.SetVisible(valueRow.Card, needsValue);
            if (needsValue)
            {
                top = Top(valueRow);
                string valueUi = value == null ? L.T("Nenhuma votação aberta no Congresso")
                    : kind == "tarifa" ? (value == "bloqueio" ? L.T("Bloqueio") : L.T("Pedágio")) : CongressChoiceUi(view, value);
                Title(valueRow, Capitalize(valueUi));
                Chip(top, 0, kind == "tarifa" ? L.T("Modo") : L.T("Posição"), kind == "tarifa" ? L.T("Modo") : L.T("Posição"),
                    kind == "tarifa" ? L.T("Pedágio cobra a passagem; bloqueio fecha a passagem.") : L.T("Opção da lei em votação, ou o lado numa crise."));
                Chip(top, 1, null, null, null);
                Chip(top, 2, null, null, null);
                Chip(top, 3, null, null, null);
                SetButtons(valueRow, values.Count > 1 ? L.T("Trocar") : null, () => { valueIndex = (valueIndex + 1) % values.Count; feedback = null; nextRefresh = 0; }, null, null);
                Body(valueRow, string.Empty);
                B.SetVisible(valueRow.Body, false);
                order.Add(valueRow.Card);
            }

            ShowField(kind == "renomear" ? L.T("Nome novo do bloco") : L.T("Motivo (opcional): os membros leem"),
                kind == "renomear" ? L.T("Nome novo") : L.T("Motivo"), L.T("Até 200 letras."));
            order.Add(fieldItem);

            Row send = sideRows[3];
            top = Top(send);
            Title(send, FeedbackTitle(L.T("Pronta")));
            Chip(top, 0, L.F("Votação de {0} turnos", Bloc.VoteTurns), L.T("Prazo"), L.T("Maioria simples; expulsar exige 2/3."));
            Chip(top, 1, null, null, null);
            Chip(top, 2, null, null, null);
            Chip(top, 3, null, null, null);
            SetButtons(send, president ? L.T("Pôr em votação") : L.T("Sugerir"),
                () => Propose(view, kind, needsTarget ? target : -1, kind == "renomear" ? field?.Text : value, kind == "renomear" ? null : field?.Text), null, null);
            Body(send, string.Empty);
            B.SetVisible(send.Body, false);
            order.Add(send.Card);

            Row leave = sideRows[4];
            top = Top(leave);
            Title(leave, L.T("Sair do bloco"));
            Chip(top, 0, L.F("Espera de {0} turnos", Bloc.LeaveCooldown), L.T("Saída"), L.T("Sair é livre, mas você espera 10 turnos para entrar em outro bloco. Com menos de 3 membros, o bloco se desfaz."));
            Chip(top, 1, null, null, null);
            Chip(top, 2, null, null, null);
            Chip(top, 3, null, null, null);
            bool confirming = Time.unscaledTime < confirmLeaveUntil;
            SetButtons(leave, confirming ? L.T("Confirmar") : L.T("Sair"), () => Leave(view), null, null);
            Body(leave, string.Empty);
            B.SetVisible(leave.Body, false);
            order.Add(leave.Card);
        }

        private static string KindHelp(string kind)
        {
            switch (kind)
            {
                case "tarifa": return L.T("Pedágio ou bloqueio contra uma nação de fora, em todos os postos dos membros.");
                case "fim_tarifa": return L.T("Tira uma tarifa comum em vigor.");
                case "admitir": return L.T("Aprovada, a nação recebe um convite para entrar.");
                case "expulsar": return L.T("Exige 2/3 dos membros. O expulso espera 10 turnos para entrar em outro bloco.");
                case "congresso": return L.T("Todos os membros da IA votam juntos na votação aberta do Congresso.");
                case "renomear": return L.T("Escreva o nome novo no campo abaixo.");
                case "dissolver": return L.T("Aprovada, o bloco acaba para todos.");
            }
            return string.Empty;
        }

        private List<int> Targets(View view, Bloc bloc, string kind)
        {
            switch (kind)
            {
                case "tarifa": return MailScreen.KnownNations(view.Capture).Where(e => !bloc.Has(e) && !bloc.Tariffs.Any(t => t.Target == e)).ToList();
                case "fim_tarifa": return bloc.Tariffs.Select(t => t.Target).ToList();
                case "admitir": return MailScreen.KnownNations(view.Capture).Where(e => !bloc.Has(e) && bloc.InviteFor(e) == null).ToList();
                case "expulsar": return bloc.Members.Where(m => m != view.Me).ToList();
            }
            return new List<int>();
        }

        private static List<string> Values(View view, string kind)
        {
            if (kind == "tarifa")
            {
                return new List<string> { "pedagio", "bloqueio" };
            }
            if (kind != "congresso" || view.Capture.Congress == null)
            {
                return new List<string>();
            }
            var values = new List<string>();
            if (view.Capture.Congress.LawVote?.Active == true)
            {
                values.Add("lei:A");
                values.Add("lei:B");
            }
            foreach (CongressCrisis crisis in view.Capture.Congress.Crises.Where(c => c.Active))
            {
                values.Add($"K{crisis.Pool}:E{crisis.Declarator}");
                values.Add($"K{crisis.Pool}:E{crisis.Target}");
            }
            return values;
        }

        private void ShowField(string instruction, string tipTitle, string tip)
        {
            if (fieldItem == null)
            {
                return;
            }
            B.SetVisible(fieldItem, true);
            string text = "<c=F5EBE166><i>" + instruction + "</i></c>";
            if (field.InstructionText != text)
            {
                field.InstructionText = text;
            }
            NativeUIKit.Tip(fieldItem, tipTitle, tip);
        }

        private string FeedbackTitle(string normal)
        {
            string title = feedback ?? normal;
            return feedback != null && feedbackIsError ? $"<c={Red}>{title}</c>" : title;
        }

        // ---------------- Actions ----------------

        private void Found(View view)
        {
            string problem = BlocEngine.Found(view.World, view.Me, field?.Text, invitees, view.Turn, out Bloc bloc);
            if (Act(problem, bloc != null ? L.F("{0} fundado. Os convites foram enviados.", Esc(bloc.Name)) : null))
            {
                invitees.Clear();
                field?.ReplaceText(string.Empty);
            }
        }

        private void Propose(View view, string kind, int who, string value, string reason)
        {
            string problem = BlocEngine.Propose(view.World, view.Me, kind, who, value, reason, view.Turn, out BlocMotion motion);
            if (Act(problem, motion == null ? null : motion.Status == BlocMotion.Voting ? L.T("Pauta em votação.") : L.T("Sugestão enviada ao presidente.")))
            {
                field?.ReplaceText(string.Empty);
                if (currentSection != Section.Agenda)
                {
                    SelectSection(Section.Agenda);
                }
            }
        }

        private void Leave(View view)
        {
            if (Time.unscaledTime >= confirmLeaveUntil)
            {
                confirmLeaveUntil = Time.unscaledTime + 5f;
                nextRefresh = 0;
                return;
            }
            confirmLeaveUntil = 0;
            Act(BlocEngine.Leave(view.World, view.Me, view.Turn), L.T("Você saiu do bloco."));
        }

        /// <summary>Shows the result (the problem comes from the engine, in Portuguese; the screen only strips the prefix) and saves.</summary>
        private bool Act(string problem, string ok)
        {
            if (problem != null)
            {
                SetFeedback(L.T(problem), true);
                return false;
            }
            AiModule.Instance?.MarkDirty();
            Blocs.BlocIndex.Publish(AiModule.World);
            SetFeedback(ok ?? L.T("Feito."), false);
            return true;
        }

        private void SetFeedback(string message, bool isError)
        {
            feedback = message;
            feedbackIsError = isError;
            nextRefresh = 0;
        }

        private static void OpenGroup(Bloc bloc)
        {
            SetOpen(false);
            MailScreen.SetOpen(true);
            MailScreen.Instance?.ComposeToGroup(bloc.GroupId);
            MailScreen.Instance?.SelectSection(MailScreen.Section.Groups);
        }

        // ---------------- Cards ----------------

        private void SetRowCount(List<Row> pool, UITransform table, Transform sample, int count)
        {
            while (pool.Count < count)
            {
                int index = pool.Count;
                Transform card = table.InstantiateChild(sample, (pool == rows ? "Row" : "Side") + index).transform;
                Transform top = card.Find("Table/Top");
                UIButton button = top.Find("LiberateButton").GetComponent<UIButton>();
                UIButton second = top.GetComponent<UITransform>().InstantiateChild(button.transform, "SecondButton").GetComponent<UIButton>();
                var row = new Row { Card = card, Body = MailScreen.AddBody(card), Button = button, Second = second };
                button.LeftClick += b => row.Primary?.Invoke();
                second.LeftClick += b => row.SecondAction?.Invoke();
                pool.Add(row);
            }
            for (int i = 0; i < pool.Count; i++)
            {
                B.SetVisible(pool[i].Card, i < count);
                if (i < count)
                {
                    B.SetVisible(pool[i].Body, true);
                }
            }
            B.SetVisible(sample, false);
        }

        private static Transform Top(Row row) => row.Card.Find("Table/Top");

        private static void Title(Row row, string title) => B.SetLabel(Top(row), "TitleGroup/Title", title);

        /// <summary>Card top chip 0..3 (null hides it), with tooltip.</summary>
        private static void Chip(Transform top, int index, string text, string tipTitle, string tip)
        {
            string path = index == 0 ? "StatsTable/PopCount" : index == 1 ? "StatsTable/Fortification" : index == 2 ? "StatsTable/ExtensionsCount" : B.ExtraChip(top, 3);
            Transform chip = top.Find(path);
            B.SetVisible(chip, text != null);
            if (text == null)
            {
                return;
            }
            B.SetChip(top, path, text);
            B.Tip(top, path, tipTitle ?? string.Empty, tip ?? string.Empty);
        }

        /// <summary>Card buttons (null hides them) and the title space; the top stays at the default height.</summary>
        private static void SetButtons(Row row, string primary, Action onPrimary, string second, Action onSecond)
        {
            Transform top = Top(row);
            row.Primary = primary != null ? onPrimary : null;
            row.SecondAction = second != null ? onSecond : null;
            B.SetVisible(row.Button.transform, primary != null);
            if (primary != null)
            {
                B.SetButtonText(row.Button, primary);
            }
            if (second != null && primary != null)
            {
                MailScreen.SetSecondButton(row.Second, row.Button, second);
            }
            else
            {
                B.SetVisible(row.Second.transform, false);
            }
            float reserved = 12f;
            if (primary != null)
            {
                reserved = row.Button.GetComponent<UITransform>().Width + 20f;
                if (second != null)
                {
                    reserved += row.Second.GetComponent<UITransform>().Width + 8f;
                }
            }
            MailScreen.FitTitle(top, reserved);
            B.HideOutputs(top);
        }

        private static void Body(Row row, string text)
        {
            MailScreen.SetBody(row.Body, text ?? string.Empty);
            B.SetVisible(row.Body, !string.IsNullOrEmpty(text));
        }

        private void Notice(Row row, string title, string chip, string text = null)
        {
            Transform top = Top(row);
            Title(row, title);
            Chip(top, 0, chip, title, text);
            Chip(top, 1, null, null, null);
            Chip(top, 2, null, null, null);
            Chip(top, 3, null, null, null);
            SetButtons(row, null, null, null, null);
            Body(row, text);
        }

        private static string Esc(string text) => MailScreen.Escape(text);

        /// <summary>"Os Han · Walinong Sari", or "você".</summary>
        private static string Who(View view, int empire) => empire == view.Me ? L.T("você") : Esc(MailScreen.NationAndLeader(view.Capture, empire, inline: true));

        /// <summary>Only the people ("Os Han"), or "você".</summary>
        private static string Short(View view, int empire) => empire == view.Me ? L.T("você") : Esc(GameText.InlineUi(view.Capture.Empire(empire)?.Culture ?? "E" + empire));

        // ---------------- Button badge ----------------

        /// <summary>"!" when something needs your action: invitation, missing vote, election or suggestion for the president.</summary>
        internal static string Badge()
        {
            AiModule module = AiModule.Instance;
            AiWorld world = AiModule.World;
            if (module == null || world == null || module.PlayerIndex < 0)
            {
                return null;
            }
            int me = module.PlayerIndex;
            Bloc bloc = BlocEngine.Of(world, me);
            bool pending = BlocEngine.InvitesFor(world, me).Any()
                || bloc != null && (bloc.OpenMotions.Any(m => !m.Votes.ContainsKey(me) && !(m.Kind == "expulsar" && m.Target == me))
                    || bloc.ElectionOpened >= 0 && !bloc.ElectionVotes.ContainsKey(me)
                    || bloc.President == me && bloc.Suggestions.Any());
            return pending ? "!" : null;
        }

        // ---------------- Development and removal ----------------

        /// <summary>"ai alliances ..." commands: "open", "close", "section 0..4", "kind N", "target E#", "value N", "text ...", "invite E#", "send".</summary>
        internal static string DevCommand(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0] : "open";
            if (Instance == null)
            {
                return "error: alliances screen does not exist (load a game)";
            }
            View view = Instance.Current();
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
                case "kind":
                    Instance.kindIndex = parts.Length > 1 && int.TryParse(parts[1], out int kind) ? Mathf.Clamp(kind, 0, MotionKinds.Length - 1) : 0;
                    Instance.nextRefresh = 0;
                    return "ok: " + MotionKinds[Instance.kindIndex];
                case "target":
                    Instance.target = parts.Length > 1 && int.TryParse(parts[1].TrimStart('E', 'e'), out int who) ? who : -1;
                    Instance.nextRefresh = 0;
                    return "ok";
                case "value":
                    Instance.valueIndex = parts.Length > 1 && int.TryParse(parts[1], out int value) ? value : 0;
                    Instance.nextRefresh = 0;
                    return "ok";
                case "text":
                    Instance.field?.ReplaceText(string.Join(" ", parts.Skip(1)));
                    return "ok";
                case "invite":
                    if (parts.Length > 1 && int.TryParse(parts[1].TrimStart('E', 'e'), out int guest) && !Instance.invitees.Remove(guest))
                    {
                        Instance.invitees.Add(guest);
                    }
                    Instance.nextRefresh = 0;
                    return "ok: " + string.Join(",", Instance.invitees);
                case "send":
                    // The same as the panel's send button (found, or propose the assembled agenda item).
                    if (view == null)
                    {
                        return "error: no game";
                    }
                    if (view.Mine == null)
                    {
                        Instance.Found(view);
                    }
                    else
                    {
                        Instance.sideRows.ElementAtOrDefault(3)?.Primary?.Invoke();
                    }
                    return "ok: " + (Instance.feedback ?? "?");
                default:
                    SetOpen(true);
                    return "ok: open";
            }
        }

        /// <summary>Safe removal (core reload, leaving the game): same as the council's.</summary>
        internal static void DestroyWindow()
        {
            AlliancesScreen window = Instance;
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
                Plugin.Log.LogWarning($"Alliances screen removal: {ex.Message}");
            }
            window.gameObject.SetActive(false);
            Destroy(window.gameObject);
        }
    }
}
