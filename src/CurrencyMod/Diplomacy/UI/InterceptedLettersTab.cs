using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Tooltips;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using CurrencyMod.Diplomacy.Capture;
using CurrencyMod.NativeUI;
using HarmonyLib;
using UnityEngine;
using B = CurrencyMod.NativeUI.NativeBankWindow;

namespace CurrencyMod.Diplomacy.UI
{
    /// <summary>
    /// "Letters" tab in the native espionage (Intelligence) window, next to Stealth and Detection (design §8.1 and
    /// §8.3.1): the player's spy network, the chance of intercepting each nation's letters and the intercepted letters.
    /// The game's mode tabs come from an enum; this one is a clone of the mode tab with the same component (first and
    /// last tab corners come from the style), with no link to the window. When active, it hides the listing tabs, the
    /// sorting and the lists and shows a panel of ours; a postfix on the window's Refresh keeps that while it is
    /// active, and a prefix on SetStatMode deactivates it when the player clicks one of the game's tabs.
    /// Badge on the bar's espionage button: intercepted letters the player has not read yet.
    /// </summary>
    internal class InterceptedLettersTab : MonoBehaviour
    {
        private const string TabName = "CurrencyMod_LettersStatTab";
        private const string PanelName = "CurrencyMod_InterceptedPanel";
        private const string BadgeName = "CurrencyMod_SpyBadge";
        private const string TabsPath = "_ArmyFilters/StatModeGroup/StatModeTabsTable";
        private const string ListingPath = "_ArmyFilters/ListingModeAndSorters";
        private const string Gold = "E3C88A";
        private const string Green = "8FD18A";
        private const float PanelWidth = 460f;
        private const float PanelHeight = 520f;
        private const int MaxShown = 30;

        internal static InterceptedLettersTab Instance;
        /// <summary>Our tab is active (the window stays in one of the game's modes underneath).</summary>
        internal static bool Active;

        private AllMilitaryForcesStealthLayerWindow window;
        private AllMilitaryForcesStealthLayerWindow_StatModeTabItem tab;
        private readonly TitleAndDescription tabTooltip = new TitleAndDescription();
        private Transform panel;
        private UITransform listTable;
        private Transform cardSample;
        private Transform summaryCard;
        private Transform summaryBody;
        private Transform emptyCard;
        private Transform emptyBody;
        private readonly List<Row> rows = new List<Row>();
        /// <summary>Letters that were unread when the tab opened: they stay "Nova" until the player leaves it.</summary>
        private readonly HashSet<int> freshShown = new HashSet<int>();
        private bool built;
        private float nextCheck;
        private float nextRefresh;
        private int shownUnread = -1;

        private ControlBannerLayerToggle stealthToggle;
        private Transform badge;
        private int shownBadge = -1;

        private sealed class Row
        {
            public Transform Card;
            public Transform Body;
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
            nextCheck = Time.unscaledTime + 0.25f;
            try
            {
                if (SavePatches.IsGameOnline() || WindowsUtils.WindowsService == null || !GameAccess.TryGetSession(out _, out _, out _))
                {
                    return;
                }
                RefreshBadge();
                AllMilitaryForcesStealthLayerWindow current = WindowsUtils.GetWindow<AllMilitaryForcesStealthLayerWindow>();
                if (current == null)
                {
                    return;
                }
                if (!built || window != current)
                {
                    if (!current.Shown || current.allStatModeItems == null || current.allStatModeItems.Length < 2)
                    {
                        return;
                    }
                    Build(current);
                }
                if (!window.Shown)
                {
                    // Closed: on the next visit, only what arrives afterwards shows as "Nova".
                    freshShown.Clear();
                    return;
                }
                if (Active)
                {
                    HideNative();
                    if (Time.unscaledTime >= nextRefresh)
                    {
                        nextRefresh = Time.unscaledTime + 0.5f;
                        RefreshContent();
                    }
                }
                RefreshTabTitle();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Espionage letters tab: {ex}");
                nextCheck = Time.unscaledTime + 5f;
            }
        }

        // ---------------- Building ----------------

        private void Build(AllMilitaryForcesStealthLayerWindow target)
        {
            Cleanup();
            window = target;
            Transform root = window.transform;
            Transform tabs = root.Find(TabsPath);
            Transform sample = tabs?.Find("_StatModeTabSample");
            Transform filters = root.Find("_ArmyFilters");
            AllSettlementsWindow listDonor = Resources.FindObjectsOfTypeAll<AllSettlementsWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            if (tabs == null || sample == null || filters == null || listDonor == null)
            {
                return;
            }
            Transform oldTab = tabs.Find(TabName);
            if (oldTab != null)
            {
                NativeUIKit.Dispose(oldTab);
            }
            Transform oldPanel = root.Find(PanelName);
            if (oldPanel != null)
            {
                NativeUIKit.Dispose(oldPanel);
            }

            // Tab: clone of the mode tab, with its component (corners from the style) but no link to the window.
            UITransform clone = tabs.GetComponent<UITransform>().InstantiateChild(sample, TabName);
            clone.transform.SetAsLastSibling();
            clone.VisibleSelf = true;
            tab = clone.GetComponent<AllMilitaryForcesStealthLayerWindow_StatModeTabItem>();
            // The tab's Switch event is ambiguous with the Publicizer (event and field with the same name): we listen to the toggle.
            tab.toggle.Switch += Toggle_Switch;
            tab.SetState(false);
            tab.title.Text = L.T("Cartas");
            tabTooltip.Title = L.T("Cartas interceptadas");
            tabTooltip.Description = L.T("Cartas privadas que os seus espiões leram no caminho, e a chance de ler as de cada nação. Espião oculto no território de outra nação, de preferência na capital, pega parte das cartas que ela escreve ou recebe.");
            tab.tooltip.Bind(TooltipUtils.TitleAndDescription, tabTooltip);
            ShapeTabs(ours: true);

            // Panel: list from the cities window built under an inactive parent and only then put into the window, right
            // after the filters (the window is a vertical table and grows with it).
            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            try
            {
                GameObject cities = Instantiate(listDonor.gameObject, stash.transform);
                B.StripGameScripts(cities);
                B.RemoveUnusedParts(cities.transform, keepSorters: 0);
                Transform list = Instantiate(cities.transform.Find("_SettlementsList").gameObject, stash.transform).transform;
                list.name = PanelName;
                MailScreen.PrepareList(list, PanelWidth, PanelHeight, out listTable, out cardSample);
                panel = list;
                panel.SetParent(root, false);
                panel.SetSiblingIndex(filters.GetSiblingIndex() + 1);
            }
            finally
            {
                Destroy(stash);
            }
            summaryCard = listTable.InstantiateChild(cardSample, "Summary").transform;
            summaryBody = MailScreen.AddBody(summaryCard);
            emptyCard = listTable.InstantiateChild(cardSample, "Empty").transform;
            emptyBody = MailScreen.AddBody(emptyCard);
            B.SetVisible(cardSample, false);
            NativeUIKit.SetVisible(panel, false);
            built = true;
            Active = false;
            Plugin.Log.LogInfo("Espionage letters tab built.");
        }

        /// <summary>
        /// Row corners: the game marks the first and last tab in the style (First/Last). With ours at the end, the
        /// Detection tab becomes a middle tab; without it, it is the last one again.
        /// </summary>
        private void ShapeTabs(bool ours)
        {
            AllMilitaryForcesStealthLayerWindow_StatModeTabItem[] items = window?.allStatModeItems;
            if (items == null || items.Length == 0)
            {
                return;
            }
            for (int i = 0; i < items.Length; i++)
            {
                items[i]?.RefreshPosition(i == 0, !ours && i == items.Length - 1);
            }
            if (ours && tab != null)
            {
                tab.RefreshPosition(false, true);
            }
        }

        // ---------------- Activate and deactivate ----------------

        private void Toggle_Switch(IUIToggle source, bool state)
        {
            if (state && !Active)
            {
                Activate();
            }
        }

        internal void Activate()
        {
            if (!built || window == null)
            {
                return;
            }
            Active = true;
            tab.SetState(true);
            foreach (AllMilitaryForcesStealthLayerWindow_StatModeTabItem item in window.allStatModeItems)
            {
                item?.SetState(false);
            }
            HideNative();
            NativeUIKit.SetVisible(panel, true);
            nextRefresh = 0f;
            RefreshContent();
        }

        /// <summary>Goes back to the game's tabs: the current mode's tab lights up and the window redraws.</summary>
        internal void Deactivate(bool restoreNative)
        {
            if (!Active)
            {
                return;
            }
            Active = false;
            freshShown.Clear();
            tab?.SetState(false);
            NativeUIKit.SetVisible(panel, false);
            if (window == null)
            {
                return;
            }
            Transform listing = window.transform.Find(ListingPath);
            if (listing != null)
            {
                NativeUIKit.SetVisible(listing, true);
            }
            if (restoreNative)
            {
                int mode = (int)window.currentLayerStatMode;
                AllMilitaryForcesStealthLayerWindow_StatModeTabItem[] items = window.allStatModeItems;
                if (items != null && mode >= 0 && mode < items.Length)
                {
                    items[mode]?.SetState(true);
                }
            }
            // Forces the window to pick the listing again and show the mode's list.
            window.currentListingMode = AllMilitaryForcesStealthLayerListingMode.Count;
            window.Dirtyfy();
        }

        /// <summary>Hides listing tabs, sorting, lists and the "no forces" notice (the game's Refresh shows them again).</summary>
        internal void HideNative()
        {
            if (window == null)
            {
                return;
            }
            Transform listing = window.transform.Find(ListingPath);
            if (listing != null && listing.GetComponent<UITransform>().VisibleSelf)
            {
                NativeUIKit.SetVisible(listing, false);
            }
            if (window.noMilitaryForceGroup != null && window.noMilitaryForceGroup.VisibleSelf)
            {
                window.noMilitaryForceGroup.VisibleSelf = false;
            }
            HideList(window.armiesList);
            HideList(window.squadronsList);
            HideList(window.militaryForcesList);
            HideList(window.agentsList);
        }

        private static void HideList(AllMilitaryForcesStealthLayerWindow_MilitaryForcesList list)
        {
            if (list != null && list.GetComponent<UITransform>().VisibleSelf)
            {
                list.UpdateVisibility(false, instant: true);
            }
        }

        // ---------------- Content ----------------

        private void RefreshTabTitle()
        {
            int unread = AiModule.Instance?.PlayerInterceptedUnread() ?? 0;
            if (tab == null || unread == shownUnread)
            {
                return;
            }
            shownUnread = unread;
            tab.title.Text = unread > 0 ? L.F("Cartas ({0})", unread) : L.T("Cartas");
        }

        private void RefreshContent()
        {
            AiModule module = AiModule.Instance;
            WorldCapture capture = TurnCapture.Latest;
            if (module == null || AiModule.World == null || capture == null || module.PlayerIndex < 0 || capture.Guid != AiModule.World.GameGuid)
            {
                return;
            }
            int me = module.PlayerIndex;
            FillSummary(module, capture, me);
            List<Letter> letters = module.PlayerIntercepted().OrderByDescending(l => l.InterceptedTurn).ThenByDescending(l => l.Id).Take(MaxShown).ToList();
            SetRowCount(letters.Count);
            B.SetVisible(emptyCard, letters.Count == 0);
            if (letters.Count == 0)
            {
                Transform top = emptyCard.Find("Table/Top");
                B.SetLabel(top, "TitleGroup/Title", L.T("Nenhuma carta interceptada"));
                B.SetChip(top, "StatsTable/PopCount", L.T("Só cartas privadas"));
                B.HideChild(top, "StatsTable/Fortification");
                B.HideChild(top, "StatsTable/ExtensionsCount");
                B.HideChild(top, "LiberateButton");
                B.HideOutputs(top);
                MailScreen.SetBody(emptyBody, L.T("Mande um espião para o território de outra nação e deixe-o oculto, de preferência na região da capital. A cada turno ele pode ler uma carta privada que essa nação escreve ou recebe. Carta lida pelos seus espiões nunca chega ao destino, e ninguém fica sabendo."));
                B.Tip(top, "StatsTable/PopCount", L.T("Só cartas privadas"), L.T("Ultimatos vão por enviado formal e declarações públicas todos leem: espião não intercepta."));
                emptyCard.SetAsLastSibling();
            }
            for (int i = 0; i < letters.Count; i++)
            {
                FillLetter(module, capture, rows[i], letters[i], me);
            }
            module.MarkInterceptedRead(letters);
            FitHeight();
        }

        /// <summary>
        /// The panel follows the content up to PanelHeight (then it scrolls): the game window is a vertical table and grows
        /// along, without leftover empty background below the cards. The size is from the table's last layout (0.5 s delay).
        /// </summary>
        private void FitHeight()
        {
            if (panel == null || listTable == null)
            {
                return;
            }
            float height = Mathf.Clamp(listTable.Height + 12f, 120f, PanelHeight);
            // Scrollbar only when the content exceeds the panel (when it fit, it showed up as a stroke on the edge).
            Transform scrollbar = panel.Find("Scrollview/Scrollbar");
            if (scrollbar != null)
            {
                bool needed = listTable.Height + 12f > PanelHeight;
                if (scrollbar.GetComponent<UITransform>().VisibleSelf != needed)
                {
                    NativeUIKit.SetVisible(scrollbar, needed);
                }
            }
            UITransform panelUi = panel.GetComponent<UITransform>();
            if (Mathf.Abs(panelUi.Height - height) < 1f)
            {
                return;
            }
            panelUi.Height = height;
            Transform scroll = panel.Find("Scrollview");
            if (scroll != null)
            {
                scroll.GetComponent<UITransform>().Height = height;
                UIScrollView view = scroll.GetComponent<UIScrollView>();
                if (view != null)
                {
                    view.maxHeight = height;
                }
            }
            window?.GetComponent<Amplitude.UI.Layouts.UILayout>()?.ArrangeChildren();
        }

        private void FillSummary(AiModule module, WorldCapture capture, int me)
        {
            Transform top = summaryCard.Find("Table/Top");
            List<CapturedSpy> spies = Espionage.AllSpies(capture).Where(s => s.Owner == me).ToList();
            List<CapturedSpy> abroad = spies.Where(s => s.TerritoryOwner >= 0 && s.TerritoryOwner != me).ToList();
            int total = module.PlayerIntercepted().Count;
            B.SetLabel(top, "TitleGroup/Title", L.T("Sua rede de espiões"));
            B.SetChip(top, "StatsTable/PopCount", abroad.Count == 1 ? L.T("1 espião fora") : L.F("{0} espiões fora", abroad.Count));
            B.SetVisible(top.Find("StatsTable/Fortification"), true);
            B.SetChip(top, "StatsTable/Fortification", total == 1 ? L.T("1 carta lida") : L.F("{0} cartas lidas", total));
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.HideChild(top, "LiberateButton");
            B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", L.T("Espiões fora"), L.T("Espiões seus em território de outra nação. Só eles leem cartas, e só enquanto ficam ocultos."));
            B.Tip(top, "StatsTable/Fortification", L.T("Cartas lidas"), L.T("Todas as cartas que os seus espiões já interceptaram nesta partida."));

            var lines = new List<string>();
            foreach (CapturedSpy spy in abroad.Take(6))
            {
                Espionage.SpyWeight weight = Espionage.Weigh(capture, AiModule.World, spy);
                string nation = capture.Empire(spy.TerritoryOwner)?.Culture ?? DossierBuilder.Name(capture, spy.TerritoryOwner);
                string state = weight != null ? (weight.Mission == "presença" ? L.T("observando") : MissionUi(weight.Mission))
                    : spy.Hidden ? L.T("sem poder agir (acordo com eles)") : L.T("descoberto");
                string danger = spy.HostileDetection > 0 && spy.TurnsBeforeRevealed != int.MaxValue ? L.F(", revelado em ~{0} turno(s)", spy.TurnsBeforeRevealed) : string.Empty;
                lines.Add(L.F("{0} em {1}, terra {2}: {3}{4}.", MailScreen.Escape(spy.Name ?? L.T("Espião")), MailScreen.Escape(capture.TerritoryName(spy.Territory)), MailScreen.Escape(GameText.OfUi(nation)), state, danger));
            }
            var reach = new List<string>();
            foreach (int end in capture.AliveEmpires())
            {
                if (end == me)
                {
                    continue;
                }
                InterceptRisk risk = Espionage.Risks(capture, AiModule.World, end, -1, -1).FirstOrDefault(r => r.Empire == me);
                if (risk != null)
                {
                    string nation = capture.Empire(end)?.Culture ?? DossierBuilder.Name(capture, end);
                    reach.Add($"{MailScreen.Escape(nation)} <c={Gold}>" + LevelUi(risk.Chance) + "</c> " + L.F("({0:0}% por carta)", risk.Chance * 100));
                }
            }
            string text = lines.Count > 0 ? string.Join("\n", lines) : L.T("Nenhum espião seu em território estrangeiro.");
            text += reach.Count > 0
                ? "\n\n" + L.T("Chance de ler as cartas privadas que escrevem ou recebem:") + "\n" + string.Join("\n", reach)
                : "\n\n" + L.T("Sem espião oculto em território estrangeiro, nenhuma carta é interceptada.");
            MailScreen.SetBody(summaryBody, text);
            summaryCard.SetAsFirstSibling();
        }

        /// <summary>Spy mission in the UI (Espionage.SpyWeight.Mission stays in Portuguese: it also goes to the dossier).</summary>
        private static string MissionUi(string mission)
        {
            switch (mission)
            {
                case "vigiando a capital": return L.T("vigiando a capital");
                case "vigiando uma cidade": return L.T("vigiando uma cidade");
                case "infiltrado": return L.T("infiltrado");
                case "infiltrando": return L.T("infiltrando");
                default: return mission;
            }
        }

        /// <summary>Espionage.Level in the UI (the original stays in Portuguese: it also goes to the dossier).</summary>
        private static string LevelUi(double chance)
        {
            switch (Espionage.Level(chance))
            {
                case "alta": return L.T("alta");
                case "média": return L.T("média");
                case "baixa": return L.T("baixa");
                default: return L.T("nenhuma");
            }
        }

        private void FillLetter(AiModule module, WorldCapture capture, Row row, Letter letter, int me)
        {
            row.Letter = letter;
            Transform top = row.Card.Find("Table/Top");
            string from = capture.Empire(letter.From)?.Culture ?? DossierBuilder.Name(capture, letter.From);
            string to = capture.Empire(letter.To)?.Culture ?? DossierBuilder.Name(capture, letter.To);
            B.SetLabel(top, "TitleGroup/Title", $"{from} → {to}");
            if (!letter.InterceptorRead)
            {
                freshShown.Add(letter.Id);
            }
            bool fresh = freshShown.Contains(letter.Id);
            B.SetChip(top, "StatsTable/PopCount", fresh ? $"<c={Green}>" + L.T("Nova") + "</c> · " + L.F("turno {0}", letter.InterceptedTurn) : L.F("Turno {0}", letter.InterceptedTurn));
            B.SetVisible(top.Find("StatsTable/Fortification"), true);
            B.SetChip(top, "StatsTable/Fortification", letter.InterceptedVia == "origem" ? L.T("Na saída") : L.T("Na chegada"));
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.HideChild(top, "LiberateButton");
            MailScreen.FitTitle(top, 12f);
            B.HideOutputs(top);
            string where = letter.InterceptedTerritory >= 0 ? capture.TerritoryName(letter.InterceptedTerritory) : "?";
            string spy = letter.InterceptedSpy ?? L.T("Seu espião");
            B.Tip(top, "StatsTable/PopCount", L.T("Interceptada"), L.F("Escrita no turno {0}; seu espião pegou no turno {1}. Ela não chegou a quem era endereçada.", letter.SentTurn, letter.InterceptedTurn));
            B.Tip(top, "StatsTable/Fortification", letter.InterceptedVia == "origem" ? L.T("Na saída") : L.T("Na chegada"),
                letter.InterceptedVia == "origem" ? L.F("{0}, em {1}, pegou a carta quando saía de quem escreveu.", spy, where) : L.F("{0}, em {1}, pegou a carta antes de chegar a quem ia receber.", spy, where));
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(letter.Subject))
            {
                parts.Add($"<c={Gold}>{MailScreen.Escape(letter.Subject)}</c>");
            }
            parts.Add("<i>" + L.F("Escrita no turno {0} · {1} em {2}.", letter.SentTurn, MailScreen.Escape(spy), MailScreen.Escape(where)) + "</i>");
            parts.Add(MailScreen.Escape(letter.Text));
            MailScreen.SetBody(row.Body, string.Join("\n\n", parts));
        }

        private void SetRowCount(int count)
        {
            while (rows.Count < count)
            {
                Transform card = listTable.InstantiateChild(cardSample, "Letter" + rows.Count).transform;
                rows.Add(new Row { Card = card, Body = MailScreen.AddBody(card) });
            }
            for (int i = 0; i < rows.Count; i++)
            {
                B.SetVisible(rows[i].Card, i < count);
            }
            B.SetVisible(summaryCard, true);
            B.SetVisible(cardSample, false);
        }

        // ---------------- Badge on the espionage button ----------------

        private void RefreshBadge()
        {
            if (stealthToggle == null)
            {
                ControlBanner banner = WindowsUtils.GetWindow<ControlBanner>();
                if (banner == null || banner.stealthToggle == null)
                {
                    return;
                }
                stealthToggle = banner.stealthToggle;
                Transform leftover = stealthToggle.transform.Find(BadgeName);
                if (leftover != null)
                {
                    NativeUIKit.Dispose(leftover);
                }
                badge = NativeUIKit.CreateBadge(stealthToggle.transform, BadgeName);
                shownBadge = -1;
            }
            int unread = AiModule.Instance?.PlayerInterceptedUnread() ?? 0;
            if (unread == shownBadge || badge == null)
            {
                return;
            }
            shownBadge = unread;
            NativeUIKit.SetBadge(badge, unread <= 0 ? null : unread > 9 ? "9+" : unread.ToString());
        }

        // ---------------- Cleanup ----------------

        private void Cleanup()
        {
            try
            {
                Deactivate(restoreNative: true);
                if (window != null)
                {
                    ShapeTabs(ours: false);
                }
                if (tab != null)
                {
                    tab.toggle.Switch -= Toggle_Switch;
                    tab.tooltip?.Unbind();
                    NativeUIKit.Dispose(tab.transform);
                }
                NativeUIKit.Dispose(panel);
                window?.transform.Find(TabsPath)?.GetComponent<Amplitude.UI.Layouts.UILayout>()?.ArrangeChildren();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Espionage letters tab: cleanup: {ex.Message}");
            }
            built = false;
            tab = null;
            panel = null;
            rows.Clear();
            shownUnread = -1;
        }

        private void RemoveBadge()
        {
            if (badge != null)
            {
                NativeUIKit.Dispose(badge);
            }
            badge = null;
            stealthToggle = null;
            shownBadge = -1;
        }

        private void OnDestroy()
        {
            Cleanup();
            RemoveBadge();
            if (Instance == this)
            {
                Instance = null;
                Active = false;
            }
        }

        // ---------------- Patches ----------------

        /// <summary>The window's Refresh shows lists and listing tabs again: with our tab active, they are hidden again.</summary>
        [HarmonyPatch(typeof(AllMilitaryForcesStealthLayerWindow), "Refresh")]
        private static class RefreshPatch
        {
            private static void Postfix()
            {
                if (Active)
                {
                    Instance?.HideNative();
                }
            }
        }

        /// <summary>Click on one of the game's mode tabs (Stealth, Detection): ours is deactivated.</summary>
        [HarmonyPatch(typeof(AllMilitaryForcesStealthLayerWindow), "SetStatMode")]
        private static class SetStatModePatch
        {
            private static void Prefix()
            {
                // The current mode's tab lights up again: if the click was on it, SetStatMode does not touch the states.
                if (Active)
                {
                    Instance?.Deactivate(restoreNative: true);
                }
            }
        }

        /// <summary>Leaving the game: the window and the bar are rebuilt; tab, panel and badge are removed first.</summary>
        [HarmonyPatch(typeof(AllMilitaryForcesStealthLayerWindow), nameof(AllMilitaryForcesStealthLayerWindow.OnPresentationShuttingDown))]
        private static class ShutdownPatch
        {
            private static void Prefix()
            {
                Instance?.Cleanup();
            }
        }

        [HarmonyPatch(typeof(ControlBanner), nameof(ControlBanner.OnPresentationShuttingDown))]
        private static class BannerShutdownPatch
        {
            private static void Prefix()
            {
                Instance?.RemoveBadge();
            }
        }

        /// <summary>
        /// "ai espionage tab": opens the espionage view already on the Letters tab. "ai espionage tab stealth|detection":
        /// the same as clicking the game's tab (StatModeTab_Switch). "ai espionage tab state": which tab is active.
        /// </summary>
        internal static string DevOpen(string which)
        {
            AllMilitaryForcesStealthLayerWindow target = WindowsUtils.GetWindow<AllMilitaryForcesStealthLayerWindow>();
            if (which == "stealth" || which == "detection")
            {
                if (target == null || !target.Shown)
                {
                    return "error: the espionage window is not open";
                }
                target.StatModeTab_Switch(which == "stealth" ? AllMilitaryForcesStealthLayerStatMode.Stealth : AllMilitaryForcesStealthLayerStatMode.Detection);
                return $"ok: clicked the {which} tab";
            }
            if (which == "state")
            {
                Transform listing = target?.transform.Find(ListingPath);
                return $"window {(target != null && target.Shown ? "open" : "closed")} · game mode {target?.currentLayerStatMode} · Letters tab {(Active ? "active" : "inactive")}"
                    + $" · listing {(listing != null && listing.GetComponent<UITransform>().VisibleSelf ? "visible" : "hidden")}"
                    + $" · panel {(Instance?.panel != null && Instance.panel.GetComponent<UITransform>().VisibleSelf ? "visible" : "hidden")}"
                    + $" · panel height {Instance?.panel?.GetComponent<UITransform>().Height:0} · content {Instance?.listTable?.Height:0}";
            }
            ControlBanner banner = WindowsUtils.GetWindow<ControlBanner>();
            if (banner == null)
            {
                return "error: control banner does not exist";
            }
            banner.RequestStealthState();
            pendingOpen = true;
            pendingSince = Time.unscaledTime;
            return "ok: espionage opened; the Letters tab activates when the window appears";
        }

        private static bool pendingOpen;
        private static float pendingSince;

        private void LateUpdate()
        {
            if (!pendingOpen)
            {
                return;
            }
            if (Time.unscaledTime - pendingSince > 5f)
            {
                pendingOpen = false;
                return;
            }
            if (built && window != null && window.Shown && !Active)
            {
                pendingOpen = false;
                Activate();
            }
        }
    }
}
