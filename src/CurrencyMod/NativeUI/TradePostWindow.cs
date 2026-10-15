using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Windows;
using Amplitude.UI;
using Amplitude.UI.Animations.Scene;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using Amplitude.UI.Windows;
using UnityEngine;
using static CurrencyMod.NativeUI.NativeBankWindow;
using Utils = Amplitude.Mercury.UI.Utils;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// "Trade Post" window: opened by clicking one of your territories in the trade view. Lists the
    /// foreign empires, their routes passing there, and lets you choose Free / Toll / Blocked.
    /// Same native base as the Central Bank (clone of "Cities and Outposts").
    /// </summary>
    internal class TradePostWindow : UIWindow
    {
        private const string WindowName = "CurrencyMod_TradePostWindow";
        private const string TollColor = "FFDF95";
        private const string BlockColor = "E8685E";

        internal static TradePostWindow Instance;

        private UILabel headerLabel;
        private UIButton closeButton;
        private UILabel sectionTitle;
        private Transform tabsTable;
        private Transform overviewCard;
        private UIButton applyAllButton;
        private UITransform listTable;
        private Transform cardSample;
        private Transform noteCard;
        private readonly List<Transform> rows = new List<Transform>();
        private readonly List<int> rowEmpires = new List<int>();
        /// <summary>− and + buttons for each row's toll price (only shown when the empire is on Toll).</summary>
        private readonly List<UIButton> priceDownButtons = new List<UIButton>();
        private readonly List<UIButton> priceUpButtons = new List<UIButton>();
        private int territoryIndex = -1;
        private int usersCount;
        private float nextRefresh;
        /// <summary>Empire whose "→ Bloquear" was clicked once, and when that expires; the second click blocks.</summary>
        private int confirmBlock = -1;
        private float confirmBlockUntil;
        /// <summary>Until when "Copiar para todos" waits for the confirming click.</summary>
        private float confirmApplyAllUntil;
        /// <summary>Sums of the detour preview across rows (for the top card): routes that detour around the toll and barred routes.</summary>
        // Routes (per pair of empires) that detour / get barred: the same route shows up in the rows of both sides.
        private readonly HashSet<string> awayRoutes = new HashSet<string>();
        private readonly HashSet<string> barredRoutes = new HashSet<string>();
        private bool previewPending;
        private const string AwayColor = "F2A65A";

        internal static bool IsOpen => Instance != null && Instance.Shown;

        // ---------------- Creation ----------------

        internal static TradePostWindow Create()
        {
            AllSettlementsWindow donor = Resources.FindObjectsOfTypeAll<AllSettlementsWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            // In the trade view the selection group is hidden; the "International Trade" panel lives in the
            // overlays group, which does not touch the visibility of windows it does not know.
            InGameOverlaysGroup group = WindowsManager.Instance?.GetWindowsGroup<InGameOverlaysGroup>();
            if (donor == null || group == null || !group.IsReady)
            {
                return null;
            }

            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            GameObject clone = Instantiate(donor.gameObject, stash.transform);
            clone.name = WindowName;

            var donorWindow = clone.GetComponent<AllSettlementsWindow>();
            UIAnimatorComponent showAnimator = donorWindow.showAnimator;
            UIAnimatorComponent hideAnimator = donorWindow.hideAnimator;

            TradePostWindow window;
            try
            {
                StripGameScripts(clone);
                RemoveUnusedParts(clone.transform);
                window = clone.AddComponent<TradePostWindow>();
                window.showAnimator = showAnimator;
                window.hideAnimator = hideAnimator;
                window.Bind(clone.transform);
            }
            catch
            {
                Destroy(stash);
                throw;
            }

            group.AddDebugWindowImplementation(window);
            Destroy(stash);
            Instance = window;
            Plugin.Log.LogInfo("Trade post window created.");
            return window;
        }

        private void Bind(Transform root)
        {
            Transform header = root.Find("Header");
            headerLabel = header.GetComponent<UILabel>();
            closeButton = header.Find("CloseButton").GetComponent<UIButton>();

            Transform culture = root.Find("_CultureAndWondersPanel");
            sectionTitle = culture.Find("CultureAndWondersTitle").GetComponent<UILabel>();
            tabsTable = root.Find("TabsTable");

            listTable = root.Find("_SettlementsList/Scrollview/Viewport/SettlementItemsTable").GetComponent<UITransform>();
            cardSample = listTable.transform.Find("_SettlementItemSample");
            // The cities window hides the list when the empire has no cities yet; the copy inherits that state.
            NativeBankWindow.SetVisible(root.Find("_SettlementsList"), true);

            UITransform cultureTransform = culture.GetComponent<UITransform>();
            cultureTransform.Height = sectionTitle.GetComponent<UITransform>().Height;
            UITransform card = root.GetComponent<UITransform>().InstantiateChild(cardSample, "OverviewCard");
            card.transform.SetSiblingIndex(culture.GetSiblingIndex() + 1);
            card.SetLeftBorder(0f);
            card.SetRightBorder(0f);
            overviewCard = card.transform;
        }

        protected override System.Collections.IEnumerator PostLoad()
        {
            yield return base.PostLoad();
            closeButton.LeftClick += CloseButton_LeftClick;
            // "Apply to all" sits in a card at the end of the list, after the choices it copies.
            noteCard = listTable.InstantiateChild(cardSample, "ApplyAllCard").transform;
            applyAllButton = noteCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            applyAllButton.LeftClick += ApplyAll_LeftClick;
            ClearInheritedTooltips();
            BindTooltip(applyAllButton.transform, L.T("Copiar para todos os postos"),
                L.T("Copia as escolhas deste posto (livre, pedágio ou bloqueio para cada império) para todos os seus territórios. Pede confirmação."));
            headerLabel.Text = L.T("Posto Comercial");
        }

        private void ClearInheritedTooltips()
        {
            foreach (UITooltip tooltip in GetComponentsInChildren<UITooltip>(true))
            {
                if (tooltip == null || tooltip.gameObject == closeButton.gameObject)
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

        protected override void PreUnload()
        {
            if (closeButton != null)
            {
                closeButton.LeftClick -= CloseButton_LeftClick;
            }
            if (applyAllButton != null)
            {
                applyAllButton.LeftClick -= ApplyAll_LeftClick;
            }
            base.PreUnload();
        }

        private void CloseButton_LeftClick(IUIButton button) => SetOpen(-1);

        public override bool CatchInputEvent(ref InputEvent inputEvent)
        {
            if (base.CatchInputEvent(ref inputEvent))
            {
                return true;
            }
            if (Shown && inputEvent.IsExitEvent())
            {
                SetOpen(-1);
                return true;
            }
            return false;
        }

        /// <summary>Opens the window on a territory (or closes it, with -1).</summary>
        internal static void SetOpen(int territory)
        {
            if (territory < 0 && Instance == null)
            {
                return; // closing never creates the window
            }
            if (Instance == null)
            {
                Create();
            }
            if (Instance == null)
            {
                return;
            }
            if (territory >= 0)
            {
                Instance.territoryIndex = territory;
                Instance.nextRefresh = 0;
                // Freshly created, the window only loads on the next frame: open it in Update.
                Instance.pendingOpen = true;
                return;
            }
            Instance.pendingOpen = false;
            WindowsUtils.UpdateWindowVisibility(Instance, false);
        }

        private bool pendingOpen;
        private float openedAt;

        private static bool InTradeView()
        {
            return Amplitude.Mercury.Presentation.Presentation.PresentationCursorController?.CurrentCursor is Amplitude.Mercury.Presentation.TradeViewCursor;
        }

        // ---------------- Actions ----------------

        private static int LocalEmpire()
        {
            return CentralBankWindow.TryGetGameData(out GameSnapshot.Data game) ? game.LocalEmpireInfo.EmpireIndex : -1;
        }

        private static TradeMode Next(TradeMode mode)
        {
            switch (mode)
            {
                case TradeMode.Free: return TradeMode.Toll;
                case TradeMode.Toll: return TradeMode.Block;
                default: return TradeMode.Free;
            }
        }

        private void CycleMode(int target)
        {
            int me = LocalEmpire();
            CurrencyWorld world = CurrencyManager.Current;
            if (me < 0 || world == null || territoryIndex < 0)
            {
                return;
            }
            lock (CurrencyManager.Lock)
            {
                TradeMode next = Next(TradePolicy.StoredMode(world, me, territoryIndex, target));
                // Blocking can destroy routes: it waits for a second click within 5 s.
                if (next == TradeMode.Block && !(confirmBlock == target && Time.unscaledTime < confirmBlockUntil))
                {
                    confirmBlock = target;
                    confirmBlockUntil = Time.unscaledTime + 5f;
                    nextRefresh = 0;
                    return;
                }
                confirmBlock = -1;
                TradePolicy.SetRule(world, me, territoryIndex, target, next);
                Plugin.Log.LogInfo($"Post {territoryIndex}: empire {target} → {TradePolicy.ModeName(next)}.");
            }
            nextRefresh = 0;
        }

        /// <summary>− and + of an empire's toll at this post: the price becomes the post's own (per resource, per turn).</summary>
        private void ChangePrice(int target, int direction)
        {
            int me = LocalEmpire();
            CurrencyWorld world = CurrencyManager.Current;
            if (me < 0 || target < 0 || world == null || territoryIndex < 0 || !CentralBankWindow.TryGetGameData(out GameSnapshot.Data game))
            {
                return;
            }
            int era = Math.Max(1, (int)game.EmpireInfo[me].EraIndex);
            lock (CurrencyManager.Lock)
            {
                double current = TradePolicy.GetPrice(territoryIndex, target, me, era);
                double next = TradePolicy.StepPrice(current, direction);
                if (TradePolicy.SetPostPrice(world, me, territoryIndex, target, next))
                {
                    Plugin.Log.LogInfo($"Post {territoryIndex}: toll for empire {target} → {next} per resource.");
                }
            }
            nextRefresh = 0;
        }

        private void ApplyAll_LeftClick(IUIButton button)
        {
            int me = LocalEmpire();
            CurrencyWorld world = CurrencyManager.Current;
            if (me < 0 || world == null || territoryIndex < 0 || !CentralBankWindow.TryGetGameData(out GameSnapshot.Data game))
            {
                return;
            }
            // It rewrites every post: the first click only arms it.
            if (Time.unscaledTime >= confirmApplyAllUntil)
            {
                confirmApplyAllUntil = Time.unscaledTime + 5f;
                nextRefresh = 0;
                return;
            }
            confirmApplyAllUntil = 0;
            List<int> mine = MyTerritories(me);
            int changed = 0;
            lock (CurrencyManager.Lock)
            {
                for (int target = 0; target < game.NumberOfMajorEmpires; target++)
                {
                    if (target == me)
                    {
                        continue;
                    }
                    TradeMode mode = TradePolicy.StoredMode(world, me, territoryIndex, target);
                    double price = TradePolicy.StoredPostPrice(world, me, territoryIndex, target);
                    foreach (int territory in mine)
                    {
                        if (TradePolicy.SetRule(world, me, territory, target, mode))
                        {
                            changed++;
                        }
                        // The post's price goes along (0 = the empire's general price).
                        if (mode == TradeMode.Toll && TradePolicy.CopyPostPrice(world, me, territory, target, price))
                        {
                            changed++;
                        }
                    }
                }
            }
            Plugin.Log.LogInfo($"Post {territoryIndex}: rules copied to {mine.Count} territory(ies) ({changed} change(s)).");
            nextRefresh = 0;
        }

        internal static List<int> MyTerritories(int me)
        {
            var result = new List<int>();
            var territories = Snapshots.GameSnapshot.PresentationData.TerritoryInfo;
            for (int t = 0; t < territories.Length; t++)
            {
                if (territories.Data[t].EmpireIndex == me)
                {
                    result.Add(t);
                }
            }
            return result;
        }

        // ---------------- Content ----------------

        private void Update()
        {
            // "Loaded" becomes true before PostLoad finishes; until then the manager ignores the request.
            if (LoadingState != Amplitude.UI.Windows.LoadingState.Loaded)
            {
                return;
            }
            if (pendingOpen)
            {
                pendingOpen = false;
                // One window at a time (the Central Bank closes); the trade view stays, it is where the post lives.
                ExclusiveWindows.BeforeOpen(this, keepTradeView: true);
                openedAt = Time.unscaledTime;
                WindowsUtils.UpdateWindowVisibility(this, true);
            }
            if (Shown && (!InTradeView() || ExclusiveWindows.Interrupted(this, openedAt, tradeView: true)))
            {
                WindowsUtils.UpdateWindowVisibility(this, false); // left the trade view or went to do something else
            }
            // While the post is open, the "International Trade" panel (which sits in the same spot) is hidden.
            SetTradePanelHidden(Shown);
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
                Plugin.Log.LogError($"Trade post: {ex}");
                nextRefresh = Time.unscaledTime + 5f;
            }
        }

        private bool tradePanelHidden;

        private void SetTradePanelHidden(bool hidden)
        {
            if (hidden == tradePanelHidden)
            {
                return;
            }
            TradeViewWindow tradeWindow = WindowsUtils.GetWindow<TradeViewWindow>();
            UITransform ui = tradeWindow != null ? tradeWindow.GetComponent<UITransform>() : null;
            if (ui == null)
            {
                return;
            }
            tradePanelHidden = hidden;
            ui.VisibleSelf = !hidden && tradeWindow.Shown;
        }

        private void RefreshContent()
        {
            if (!CentralBankWindow.TryGetGameData(out GameSnapshot.Data game) || territoryIndex < 0)
            {
                return;
            }
            int me = game.LocalEmpireInfo.EmpireIndex;
            if (Snapshots.GameSnapshot.PresentationData.TerritoryInfo.Data[territoryIndex].EmpireIndex != me)
            {
                SetOpen(-1); // the territory is no longer yours
                return;
            }
            CurrencyWorld world = CurrencyManager.Ensure(game.GameID, game.NumberOfMajorEmpires);
            if (!TradePolicy.TransitPublished && Amplitude.Mercury.Sandbox.Sandbox.TradeController != null)
            {
                TradeBlockade.PublishTransit(Amplitude.Mercury.Sandbox.Sandbox.TradeController);
            }
            List<TransitRoute> routes = TradePolicy.RoutesThrough(territoryIndex)
                .Where(r => r.Left != me && r.Right != me)
                .ToList();

            SetVisible(tabsTable, false);
            sectionTitle.Text = Utils.GameUtils.GetTerritoryName(territoryIndex);

            lock (CurrencyManager.Lock)
            {
                EmpireCurrency mine = world.Get(me);
                string symbol = mine?.Symbol ?? "$";
                int era = Math.Max(1, (int)game.EmpireInfo[me].EraIndex);
                double tollPerResource = TradePolicy.DefaultPrice(era);
                double tollIncome = world.LastTolls.Where(t => t.Owner == me).Sum(t => t.ReceivedByOwner);
                int blockedHere = world.TradeRules.Count(r => r.Owner == me && r.Territory == territoryIndex && r.Mode == TradeMode.Block);

                Transform top = overviewCard.Find("Table/Top");
                SetLabel(top, "TitleGroup/Title", L.F("Pedágio padrão: {0} {1}/recurso", symbol, Format.Money(tollPerResource)));
                SetChip(top, "StatsTable/PopCount", routes.Count == 1 ? L.T("1 rota aqui") : L.F("{0} rotas aqui", routes.Count));
                SetChip(top, "StatsTable/ExtensionsCount", L.F("{0} {1}/turno", Format.SignedMoney(tollIncome), symbol));
                HideChild(top, "LiberateButton");
                HideOutputs(top);
                SetVisible(overviewCard, true);
                Tip(top, "TitleGroup/Title", L.T("Pedágio padrão"),
                    L.T("O que cada rota paga por recurso transportado, por turno, quando você não definiu outro preço. Mude o preço de um império neste posto com − e + na linha dele, ou o preço dele em todos os postos na aba Comércio da diplomacia."));
                Tip(top, "StatsTable/PopCount", L.T("Rotas estrangeiras"),
                    blockedHere > 0 ? L.P(blockedHere, "Rotas comerciais entre outros impérios que atravessam este território agora. {0} império bloqueado aqui.", "Rotas comerciais entre outros impérios que atravessam este território agora. {0} impérios bloqueados aqui.")
                    : L.T("Rotas comerciais entre outros impérios que atravessam este território agora."));
                Tip(top, "StatsTable/ExtensionsCount", L.T("Pedágios recebidos"),
                    L.T("Total que você arrecadou de pedágio no último turno, em todos os postos. Se a volta sair mais barata que o pedágio, a rota desvia."));

                var empires = new List<int>();
                for (int i = 0; i < game.NumberOfMajorEmpires; i++)
                {
                    if (i == me || i >= game.EmpireInfo.Length || !game.EmpireInfo[i].IsAlive || !IsKnown(i))
                    {
                        continue;
                    }
                    // Only those who use this post; anyone already tolled or blocked here stays on the list
                    // (the blocked route disappears, but you must be able to free it).
                    int empire = i;
                    if (routes.Any(r => r.Left == empire || r.Right == empire)
                        || TradePolicy.StoredMode(world, me, territoryIndex, empire) != TradeMode.Free)
                    {
                        empires.Add(i);
                    }
                }
                usersCount = empires.Count;
                // Those who pass through here first.
                empires = empires.OrderByDescending(e => routes.Count(r => r.Left == e || r.Right == e)).ThenBy(e => e).ToList();

                SetRowCount(empires.Count);
                awayRoutes.Clear();
                barredRoutes.Clear();
                previewPending = false;
                for (int i = 0; i < empires.Count; i++)
                {
                    FillRow(i, empires[i], world, me, era, routes);
                    rowEmpires[i] = empires[i];
                }

                // Detours caused by the toll, merging the rows without counting twice a route between two tolled empires
                // (preview computed by the game's own pathfinding). Blockade wins: a barred route does not count as a detour.
                awayRoutes.ExceptWith(barredRoutes);
                int awayTotal = awayRoutes.Count;
                int barredTotal = barredRoutes.Count;
                string away = previewPending && awayTotal == 0 && barredTotal == 0 ? L.T("calculando…")
                    : awayTotal == 0 && barredTotal == 0 ? L.T("0 desviam")
                    : string.Join(" · ", new[]
                    {
                        awayTotal > 0 ? "<c=" + AwayColor + ">" + (awayTotal == 1 ? L.T("1 desvia") : L.F("{0} desviam", awayTotal)) + "</c>" : null,
                        barredTotal > 0 ? "<c=" + BlockColor + ">" + (barredTotal == 1 ? L.T("1 barrada") : L.F("{0} barradas", barredTotal)) + "</c>" : null,
                    }.Where(s => s != null));
                SetChip(top, "StatsTable/Fortification", away);
                Tip(top, "StatsTable/Fortification", L.T("Rotas que fogem deste posto"),
                    L.T("Das rotas estrangeiras que passariam por aqui sem pedágio nem bloqueio, quantas vão por outro caminho por causa das suas regras: desviam do pedágio (a volta sai mais barata que pagar) ou ficam barradas pelo bloqueio. Calculado pelo próprio caminho do jogo e atualizado na hora quando você muda o preço; as rotas mudam de verdade na sua próxima ação."));
            }

            Transform note = noteCard.Find("Table/Top");
            if (usersCount == 0)
            {
                SetLabel(note, "TitleGroup/Title", L.T("Nenhum império estrangeiro usa este posto"));
                HideChild(note, "StatsTable");
                HideChild(note, "LiberateButton");
            }
            else
            {
                int territories = MyTerritories(me).Count;
                SetLabel(note, "TitleGroup/Title", L.T("Todos os seus postos"));
                SetVisible(note.Find("StatsTable"), true);
                SetChip(note, "StatsTable/PopCount", territories == 1 ? L.T("1 território") : L.F("{0} territórios", territories));
                SetChip(note, "StatsTable/Fortification", L.T("Vale na próxima ação"));
                HideChild(note, "StatsTable/ExtensionsCount");
                SetVisible(note.Find("LiberateButton"), true);
                SetButtonText(applyAllButton, Time.unscaledTime < confirmApplyAllUntil ? L.T("Confirmar") : L.T("Copiar para todos"));
                Tip(note, "StatsTable/PopCount", L.T("Seus territórios"), L.T("Quantos postos recebem as escolhas deste ao clicar em Copiar para todos."));
                Tip(note, "StatsTable/Fortification", L.T("Quando vale"), L.T("As rotas são recalculadas na sua próxima ação ou no fim do turno."));
            }
            HideOutputs(note);
            noteCard.SetAsLastSibling();
            SetVisible(noteCard, true);
            MatchTradePanelHeight();
        }

        /// <summary>
        /// The window sits over the "International Trade" panel and, with few rows, would shrink and
        /// let the panel show underneath: the height follows the native panel's.
        /// </summary>
        private void MatchTradePanelHeight()
        {
            UITransform mine = GetComponent<UITransform>();
            if (mine == null)
            {
                return;
            }
            // Visible bottom edge of the "International Trade" panel (its window covers the whole screen).
            const float TradePanelBottom = 866f;
            float target = TradePanelBottom - mine.GlobalRect.y;
            var table = GetComponent<Amplitude.UI.Layouts.UITable1D>();
            if (table != null && table.autoResize)
            {
                table.autoResize = false;
            }
            if (target > 200f && Mathf.Abs(mine.Height - target) > 0.5f)
            {
                mine.Height = target;
            }
        }

        private void FillRow(int index, int empire, CurrencyWorld world, int me, int era, List<TransitRoute> routes)
        {
            Transform row = rows[index];
            TradeMode mode = TradePolicy.StoredMode(world, me, territoryIndex, empire);
            List<TransitRoute> theirs = routes.Where(r => r.Left == empire || r.Right == empire).ToList();
            int buys = theirs.Sum(r => r.Left == empire ? r.LeftBuys : r.RightBuys);
            int sells = theirs.Sum(r => r.Left == empire ? r.RightBuys : r.LeftBuys);
            TollRecord paid = world.LastTolls.FirstOrDefault(t => t.Owner == me && t.Payer == empire);
            string symbol = world.Get(me)?.Symbol ?? "$";
            double price = TradePolicy.GetPrice(territoryIndex, empire, me, era);
            bool ownPrice = TradePolicy.StoredPostPrice(world, me, territoryIndex, empire) > 0;
            bool generalPrice = TradePolicy.StoredGeneralPrice(world, me, empire) > 0;

            // The current state stands out with a dot (melhorias item 52); the button says what it switches to.
            string modeText = mode == TradeMode.Toll
                ? "<c=" + TollColor + ">● " + L.F("Pedágio {0} {1}", symbol, Format.Money(price)) + "</c>"
                : mode == TradeMode.Block ? "<c=" + BlockColor + ">● " + L.T("Bloqueado") + "</c>" : "<c=8FD18A>● " + L.T("Livre") + "</c>";
            Transform top = row.Find("Table/Top");
            SetLabel(top, "TitleGroup/Title", ShortEmpireName(empire));
            SetChip(top, "StatsTable/PopCount", modeText);
            SetChip(top, "StatsTable/Fortification", RoutesChip(mode, empire, me, price, theirs.Count, out string partners));
            Tip(top, "StatsTable/Fortification", L.T("Rotas aqui"),
                (mode == TradeMode.Toll
                    ? L.T("Das rotas desse império que passariam por este posto sem pedágio, quantas continuam passando (e pagam) e quantas desviam por um caminho mais barato. Muda na hora quando você mexe no preço. Uma rota entre dois impérios que você taxa paga os dois pedágios.")
                    : mode == TradeMode.Block ? L.T("Rotas desse império que passariam por aqui e o bloqueio barra (desviam ou são destruídas).")
                    : L.T("Rotas desse império com outros que atravessam este território agora."))
                + (partners != null ? "\n" + partners : string.Empty));
            bool toll = mode == TradeMode.Toll;
            SetVisible(priceDownButtons[index].transform, toll);
            SetVisible(priceUpButtons[index].transform, toll);
            // With a toll, the − and + buttons take the end of the row: the third chip goes away and what they paid moves to the price tooltip.
            string detail = toll ? null
                : paid != null ? L.F("Pagou {0} {1}", symbol, Format.Money(paid.ReceivedByOwner))
                : theirs.Count > 0 ? L.F("Compra {0} · Vende {1}", buys, sells) : null;
            if (detail != null)
            {
                SetVisible(top.Find("StatsTable/ExtensionsCount"), true);
                SetChip(top, "StatsTable/ExtensionsCount", detail);
            }
            else
            {
                HideChild(top, "StatsTable/ExtensionsCount");
            }
            HideOutputs(top);
            Tip(top, "StatsTable/PopCount", L.T("Situação neste posto"),
                mode == TradeMode.Toll
                    ? L.F("Paga {0} {1} por recurso transportado, por turno, para passar aqui (ou desvia, se a volta sair mais barata).", symbol, Format.Money(price)) + " "
                        + (ownPrice ? L.T("Preço deste posto.") : generalPrice ? L.T("Preço geral desse império (aba Comércio da diplomacia).") : L.T("Preço padrão."))
                        + " " + L.T("Mude com - e +.")
                        + (paid != null ? " " + L.F("No último turno esse império pagou {0} {1} somando todos os seus postos.", symbol, Format.Money(paid.ReceivedByOwner)) : string.Empty)
                        + (theirs.Count > 0 ? " " + L.P(sells, "Nas rotas daqui esse império compra {1} e vende {0} recurso.", "Nas rotas daqui esse império compra {1} e vende {0} recursos.", buys) : string.Empty)
                : mode == TradeMode.Block ? L.T("Proibido de passar por aqui. Sem outro caminho, a rota é destruída.")
                : L.T("Passa livremente por aqui."));
            Tip(top, "StatsTable/ExtensionsCount", paid != null ? L.T("Pedágio pago") : L.T("Comércio nessas rotas"),
                paid != null ? L.T("Quanto esse império pagou a você no último turno, somando todos os seus postos.")
                : L.T("Recursos que esse império compra e vende nas rotas que passam por aqui. Mais recursos, pedágio maior."));

            UIButton button = top.Find("LiberateButton").GetComponent<UIButton>();
            bool confirmingBlock = mode == TradeMode.Toll && confirmBlock == empire && Time.unscaledTime < confirmBlockUntil;
            SetButtonText(button, confirmingBlock ? L.T("Confirmar")
                : "→ " + (mode == TradeMode.Free ? L.T("Cobrar pedágio") : mode == TradeMode.Toll ? L.T("Bloquear") : L.T("Liberar")));
        }

        /// <summary>
        /// The row's routes chip. With a toll or blockade, shows the detour preview (TollPreview): of the routes that would pass
        /// here without the rule, how many pay and how many detour (or get barred). Requests the computation and sums for the top card.
        /// </summary>
        private string RoutesChip(TradeMode mode, int empire, int me, double price, int current, out string partners)
        {
            partners = null;
            if (mode == TradeMode.Free)
            {
                return current == 0 ? L.T("Sem rotas aqui") : current == 1 ? L.T("1 rota aqui") : L.F("{0} rotas aqui", current);
            }
            var ask = new TollPreview.Ask { Owner = me, Territory = territoryIndex, Target = empire, Mode = mode, Price = mode == TradeMode.Toll ? price : 0 };
            TollPreview.Request(ask);
            TollPreview.Answer answer = TollPreview.Get(ask);
            if (answer == null)
            {
                previewPending = true;
                return L.T("calculando…");
            }
            if (answer.Routes == 0)
            {
                return L.T("Sem rotas aqui");
            }
            if (mode == TradeMode.Block)
            {
                foreach (int other in answer.AwayPartners)
                {
                    barredRoutes.Add(TollPreview.Answer.RouteKey(empire, other));
                }
                partners = PartnerList(L.T("Barradas"), answer.AwayPartners);
                return "<c=" + BlockColor + ">" + (answer.Routes == 1 ? L.T("1 barrada") : L.F("{0} barradas", answer.Routes)) + "</c>";
            }
            foreach (int other in answer.AwayPartners)
            {
                awayRoutes.Add(TollPreview.Answer.RouteKey(empire, other));
            }
            partners = string.Join("\n", new[] { PartnerList(L.T("Pagam"), answer.StayPartners), AwayList(answer) }.Where(s => s != null));
            string stay = answer.Stay == 1 ? L.T("1 paga") : L.F("{0} pagam", answer.Stay);
            string away = answer.Away == 1 ? L.T("1 desvia") : L.F("{0} desviam", answer.Away);
            return answer.Away == 0 ? $"{stay} · {away}" : $"{stay} · <c={AwayColor}>{away}</c>";
        }

        /// <summary>"Pagam: rota com Os Polinésios." for the row tooltip (null if there are none).</summary>
        private static string PartnerList(string label, List<int> partners)
        {
            return partners.Count == 0 ? null
                : $"{label}: " + string.Join(", ", partners.Select(p => L.F("rota com {0}", ShortEmpireName(p)))) + ".";
        }

        /// <summary>"Desviam: rota com Os Axumitas (passa pelo seu posto de X)." — detouring to another of your posts does not escape you.</summary>
        private static string AwayList(TollPreview.Answer answer)
        {
            if (answer.AwayPartners.Count == 0)
            {
                return null;
            }
            var parts = new List<string>();
            for (int i = 0; i < answer.AwayPartners.Count; i++)
            {
                int via = i < answer.AwayVia.Count ? answer.AwayVia[i] : -1;
                parts.Add(via >= 0
                    ? L.F("rota com {0} (passa pelo seu posto de {1})", ShortEmpireName(answer.AwayPartners[i]), Utils.GameUtils.GetTerritoryName(via))
                    : L.F("rota com {0} (sai das suas terras)", ShortEmpireName(answer.AwayPartners[i])));
            }
            return L.T("Desviam") + ": " + string.Join(", ", parts) + ".";
        }

        /// <summary>Small square button on the chips line, pinned to the right of the card.</summary>
        private static void PlacePriceButton(UIButton button, string text, float rightBorder)
        {
            UILabel label = button.GetComponent<UILabel>();
            if (label != null)
            {
                label.AutoAdjustWidth = false;
                label.Alignment = new Alignment(HorizontalAlignment.Center, VerticalAlignment.Center);
                label.Text = text;
            }
            UITransform ui = button.GetComponent<UITransform>();
            ui.InteractiveSelf = true;
            ui.BottomAnchor = ui.BottomAnchor.SetAttach(false);
            ui.LeftAnchor = ui.LeftAnchor.SetAttach(false);
            ui.SetTopBorder(40f);
            ui.SetRightBorder(rightBorder);
            ui.Width = 26f;
            ui.Height = 24f;
        }

        /// <summary>Only empires you already know (the others show as "Unknown").</summary>
        internal static bool IsKnown(int empire)
        {
            try
            {
                var summaries = Snapshots.DiplomaticSnapshot.PresentationData.LocalEmpireDiplomaticSummary.RelationSummaries;
                if (summaries == null)
                {
                    return true;
                }
                foreach (DiplomaticRelationSummaryInfo summary in summaries)
                {
                    if (summary.OtherEmpireIndex == empire)
                    {
                        return summary.IsKnownByOwner();
                    }
                }
            }
            catch (Exception)
            {
            }
            return true;
        }

        private void SetRowCount(int count)
        {
            while (rows.Count < count)
            {
                int index = rows.Count;
                Transform row = listTable.InstantiateChild(cardSample, "Row" + index).transform;
                rows.Add(row);
                rowEmpires.Add(-1);
                UIButton button = row.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
                // Price − and +: copies of the row button, at chip height, pinned to the right.
                UIButton down = UnityEngine.Object.Instantiate(button.gameObject, button.transform.parent).GetComponent<UIButton>();
                down.name = "PriceDown";
                UIButton up = UnityEngine.Object.Instantiate(button.gameObject, button.transform.parent).GetComponent<UIButton>();
                up.name = "PriceUp";
                // Plain hyphen: the game's font lacks the minus sign (U+2212) and would show "?".
                PlacePriceButton(down, "-", 40f);
                PlacePriceButton(up, "+", 10f);
                down.LeftClick += b => ChangePrice(rowEmpires[index], -1);
                up.LeftClick += b => ChangePrice(rowEmpires[index], +1);
                BindTooltip(down.transform, L.T("Baixar o pedágio"), L.T("Cobra menos desse império neste posto, por recurso transportado. Pedágio mais barato faz menos rotas desviarem."));
                BindTooltip(up.transform, L.T("Subir o pedágio"), L.T("Cobra mais desse império neste posto, por recurso transportado. Se pagar sair mais caro que desviar, a rota desvia e você não recebe nada."));
                priceDownButtons.Add(down);
                priceUpButtons.Add(up);
                button.LeftClick += b => CycleMode(rowEmpires[index]);
                BindTooltip(button.transform, L.T("Livre → Pedágio → Bloqueado"),
                    L.T("Pedágio: as rotas desse império continuam passando aqui, mas esse império paga por turno, por recurso transportado, na sua moeda. Rotas podem desviar se houver outro caminho.") + "\n" +
                    L.T("Bloqueado: as rotas desse império não podem atravessar este território; sem outro caminho, a rota é destruída.") + "\n" +
                    L.T("Bloquear ou cobrar pedágio dá a esse império uma reclamação \"Bloqueio comercial\" contra você. Bloquear pede confirmação."));
            }
            for (int i = 0; i < rows.Count; i++)
            {
                SetVisible(rows[i], i < count);
            }
            SetVisible(cardSample, false);
        }

        // ---------------- Map click (trade view) ----------------

        /// <summary>In the trade view a click did nothing: on one of your territories, it opens the post.</summary>
        [HarmonyLib.HarmonyPatch(typeof(Amplitude.Mercury.Presentation.TradeViewCursor), nameof(Amplitude.Mercury.Presentation.TradeViewCursor.OnClick))]
        private static class TradeViewClickPatch
        {
            private static void Postfix(Amplitude.Mercury.Presentation.MouseButton mouseButton)
            {
                if (mouseButton != Amplitude.Mercury.Presentation.MouseButton.Left || !TradeBlockade.Enabled)
                {
                    return;
                }
                try
                {
                    int tile = Amplitude.Mercury.Presentation.Presentation.PresentationCursorController.CurrentHighlightedPosition.ToTileIndex();
                    int territory = Snapshots.GameSnapshot.PresentationData.GetTerritoryIndexAt(tile);
                    if (territory >= 0 && Snapshots.GameSnapshot.PresentationData.TerritoryInfo.Data[territory].EmpireIndex == LocalEmpire())
                    {
                        SetOpen(territory);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Trade post (click): {ex}");
                }
            }
        }

        // ---------------- Removal ----------------

        internal static void DestroyWindow()
        {
            TradePostWindow window = Instance;
            Instance = null;
            if (window == null)
            {
                return;
            }
            window.SetTradePanelHidden(false); // restores the native panel (mod reload / leaving the game)
            try
            {
                if (window.Group is UIWindowsGroup group && group.windows != null)
                {
                    group.windows = group.windows.Where(w => w != window).ToArray();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Trade post window removal: {ex.Message}");
            }
            window.gameObject.SetActive(false);
            Destroy(window.gameObject);
        }

        /// <summary>
        /// Commands: "info" (default), "open &lt;territory&gt;", "close",
        /// "preview &lt;owner&gt; &lt;territory|-1&gt; &lt;target&gt; &lt;price&gt; [block]", "price &lt;empire&gt; up|down",
        /// "mine", "costs", "set &lt;territory&gt; &lt;empire&gt; free|toll|block".
        /// </summary>
        internal static string DevCommand(string args)
        {
            string[] parts = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0] : "info";
            CurrencyWorld world = CurrencyManager.Current;
            switch (sub)
            {
                case "open":
                    SetOpen(int.Parse(parts[1]));
                    return Instance != null ? "ok" : "error: window not created";
                case "close":
                    SetOpen(-1);
                    return "ok";
                case "preview":
                {
                    // "trade preview <owner> <territory|-1> <target> <price> [block]": detour preview (requests and shows the
                    // already computed answer; run again to read the result of a new request).
                    if (parts.Length < 5 || !int.TryParse(parts[1], out int owner) || !int.TryParse(parts[2], out int territory)
                        || !int.TryParse(parts[3], out int target) || !double.TryParse(parts[4], out double asked))
                    {
                        return "usage: trade preview <owner> <territory|-1> <target> <price> [block]";
                    }
                    var ask = new TollPreview.Ask
                    {
                        Owner = owner,
                        Territory = territory,
                        Target = target,
                        Mode = parts.Length > 5 && parts[5].StartsWith("block") ? TradeMode.Block : TradeMode.Toll,
                        Price = asked,
                    };
                    TollPreview.Request(ask, maxAgeSeconds: 0.5f);
                    TollPreview.Answer answer = TollPreview.Get(ask);
                    return answer == null ? "request sent; run again to read it"
                        : $"routes of E{target} that would cross {(territory < 0 ? $"territories of E{owner}" : $"T{territory}")} without a rule: {answer.Routes} · with {ask.Mode} {asked}: {answer.Stay} stay, {answer.Away} detour\n{answer.Detail}";
                }
                case "price":
                {
                    // "trade price <empire> up|down": same as the row's + and − buttons (window open).
                    if (Instance == null || !Instance.Shown || parts.Length < 3 || !int.TryParse(parts[1], out int empire))
                    {
                        return "usage: trade price <empire> up|down (with the trade post window open)";
                    }
                    Instance.ChangePrice(empire, parts[2] == "down" ? -1 : +1);
                    return "ok";
                }
                case "mine":
                {
                    int me = LocalEmpire();
                    if (!TradePolicy.TransitPublished && Amplitude.Mercury.Sandbox.Sandbox.TradeController != null)
                    {
                        TradeBlockade.PublishTransit(Amplitude.Mercury.Sandbox.Sandbox.TradeController);
                    }
                    var lines = MyTerritories(me).Select(t => $"{t}: {Utils.GameUtils.GetTerritoryName(t)} — " +
                        string.Join(", ", TradePolicy.RoutesThrough(t).Select(r => $"{r.Left}↔{r.Right} (buys {r.LeftBuys}/{r.RightBuys}{(r.Suspended ? ", suspended" : "")})")));
                    return string.Join("\n", lines);
                }
                case "costs":
                {
                    // Per-territory upkeep of each route (read directly from the simulation, diagnostics only).
                    var text = new System.Text.StringBuilder();
                    foreach (var relation in Amplitude.Mercury.Sandbox.Sandbox.TradeController.ExternalTradeRelations)
                    {
                        if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active)
                        {
                            continue;
                        }
                        var l = relation.LeftTradeExchange.Entity;
                        var r = relation.RightTradeExchange.Entity;
                        double upkeep = (float)l.TradeUpkeepAfterResourceDiversification.Value + (float)r.TradeUpkeepAfterResourceDiversification.Value;
                        int nodes = Math.Max(l.TradeNodes.Count, r.TradeNodes.Count);
                        int goods = (int)l.NumberOfDifferentResourceTraded.Value + (int)r.NumberOfDifferentResourceTraded.Value;
                        text.AppendLine($"{relation.LeftEmpireIndex}↔{relation.RightEmpireIndex}: territories={relation.PathAsTerritories.Count} nodes={nodes} resources={goods} upkeep={upkeep:0.#} perNode={(nodes > 0 ? upkeep / nodes : 0):0.##}");
                    }
                    return text.ToString();
                }
                case "set":
                {
                    int territory = int.Parse(parts[1]);
                    int target = int.Parse(parts[2]);
                    TradeMode mode = parts[3] == "toll" ? TradeMode.Toll : parts[3] == "block" ? TradeMode.Block : TradeMode.Free;
                    int majors = Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires;
                    var territories = Snapshots.GameSnapshot.PresentationData.TerritoryInfo.Data;
                    if (territories == null || territory < 0 || territory >= territories.Length || target < 0 || target >= majors)
                    {
                        return "error: invalid territory or empire";
                    }
                    int owner = territories[territory].EmpireIndex;
                    if (owner < 0 || owner >= majors)
                    {
                        return "error: territory without a major owner";
                    }
                    lock (CurrencyManager.Lock)
                    {
                        TradePolicy.SetRule(world, owner, territory, target, mode);
                    }
                    return $"ok: owner {owner}";
                }
                default:
                {
                    if (world == null)
                    {
                        return "no state";
                    }
                    lock (CurrencyManager.Lock)
                    {
                        var text = new System.Text.StringBuilder();
                        text.AppendLine($"Rules ({world.TradeRules.Count}):");
                        foreach (TradeRule rule in world.TradeRules)
                        {
                            text.AppendLine($"  owner {rule.Owner} territory {rule.Territory} target {rule.Target}: {rule.Mode}");
                        }
                        text.AppendLine("Incidents: " + string.Join(", ", world.TradeIncidents.Select(i => $"{i.Owner}→{i.Victim}")));
                        text.AppendLine("Tolls: " + string.Join(", ", world.LastTolls.Select(t => $"{t.Payer} paid {t.PaidByPayer} to {t.Owner}")));
                        text.AppendLine("General prices: " + string.Join(", ", world.TollPrices.Select(p => $"{p.Owner}→{p.Target}: {p.Price}")));
                        text.AppendLine("Prices per post: " + string.Join(", ", world.TradeRules.Where(r => r.Price > 0).Select(r => $"{r.Owner} T{r.Territory}→{r.Target}: {r.Price}")));
                        text.AppendLine($"Route recompute pending: {TradePolicy.PathsDirty}");
                        text.AppendLine($"Window: exists={Instance != null} state={Instance?.Visibility} shown={Instance?.Shown} visible={Instance?.GetComponent<UITransform>().VisibleSelf} pending={Instance?.pendingOpen} loaded={Instance?.Loaded} tradeView={InTradeView()} cursor={Amplitude.Mercury.Presentation.Presentation.PresentationCursorController?.CurrentCursor?.GetType().Name}");
                        return text.ToString();
                    }
                }
            }
        }
    }
}
