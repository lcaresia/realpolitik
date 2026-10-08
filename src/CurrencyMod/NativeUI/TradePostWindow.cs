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
    /// Janela "Posto Comercial": aberta ao clicar num território seu na visão de comércio. Lista os
    /// impérios estrangeiros, as rotas deles que passam ali e deixa escolher Livre / Pedágio / Bloqueado.
    /// Mesma base nativa do Banco Central (clone de "Cidades e Postos Avançados").
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
        /// <summary>Botões − e + do preço do pedágio de cada linha (só aparecem com o império em Pedágio).</summary>
        private readonly List<UIButton> priceDownButtons = new List<UIButton>();
        private readonly List<UIButton> priceUpButtons = new List<UIButton>();
        private int territoryIndex = -1;
        private int usersCount;
        private float nextRefresh;
        /// <summary>Somas da prévia de desvio nas linhas (para o cartão de cima): rotas que desviam do pedágio e rotas barradas.</summary>
        // Rotas (por par de impérios) que desviam / ficam barradas: a mesma rota aparece nas linhas dos dois lados.
        private readonly HashSet<string> awayRoutes = new HashSet<string>();
        private readonly HashSet<string> barredRoutes = new HashSet<string>();
        private bool previewPending;
        private const string AwayColor = "F2A65A";

        internal static bool IsOpen => Instance != null && Instance.Shown;

        // ---------------- Criação ----------------

        internal static TradePostWindow Create()
        {
            AllSettlementsWindow donor = Resources.FindObjectsOfTypeAll<AllSettlementsWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            // Na visão de comércio o grupo de seleção fica escondido; o painel "Comércio Internacional"
            // mora no grupo de sobreposições, que não mexe na visibilidade de janelas que não conhece.
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
            Plugin.Log.LogInfo("Janela do posto comercial criada.");
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
            // A janela de cidades esconde a lista quando o império ainda não tem cidades; a cópia herda esse estado.
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
            // "Aplicar a todos" fica num cartão no fim da lista, depois das escolhas que ele copia.
            noteCard = listTable.InstantiateChild(cardSample, "ApplyAllCard").transform;
            applyAllButton = noteCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            applyAllButton.LeftClick += ApplyAll_LeftClick;
            ClearInheritedTooltips();
            BindTooltip(applyAllButton.transform, L.T("Aplicar a todos os postos"),
                L.T("Copia as escolhas deste posto (livre, pedágio ou bloqueio para cada império) para todos os seus territórios."));
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

        /// <summary>Abre a janela num território (ou fecha, com -1).</summary>
        internal static void SetOpen(int territory)
        {
            if (territory < 0 && Instance == null)
            {
                return; // fechar nunca cria a janela
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
                // Recém-criada, a janela só carrega no próximo quadro: abre no Update.
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

        // ---------------- Ações ----------------

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
                TradePolicy.SetRule(world, me, territoryIndex, target, next);
                Plugin.Log.LogInfo($"Posto {territoryIndex}: império {target} → {TradePolicy.ModeName(next)}.");
            }
            nextRefresh = 0;
        }

        /// <summary>− e + do pedágio de um império neste posto: o preço passa a ser do posto (por recurso, por turno).</summary>
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
                    Plugin.Log.LogInfo($"Posto {territoryIndex}: pedágio do império {target} → {next} por recurso.");
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
                        // O preço do posto vai junto (0 = o preço geral do império).
                        if (mode == TradeMode.Toll && TradePolicy.CopyPostPrice(world, me, territory, target, price))
                        {
                            changed++;
                        }
                    }
                }
            }
            Plugin.Log.LogInfo($"Posto {territoryIndex}: regras copiadas para {mine.Count} território(s) ({changed} mudança(s)).");
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

        // ---------------- Conteúdo ----------------

        private void Update()
        {
            // "Loaded" fica verdadeiro antes do PostLoad terminar; até lá o gerenciador ignora o pedido.
            if (LoadingState != Amplitude.UI.Windows.LoadingState.Loaded)
            {
                return;
            }
            if (pendingOpen)
            {
                pendingOpen = false;
                // Uma janela de cada vez (o Banco Central fecha); a visão de comércio continua, é o lugar do posto.
                ExclusiveWindows.BeforeOpen(this, keepTradeView: true);
                openedAt = Time.unscaledTime;
                WindowsUtils.UpdateWindowVisibility(this, true);
            }
            if (Shown && (!InTradeView() || ExclusiveWindows.Interrupted(this, openedAt, tradeView: true)))
            {
                WindowsUtils.UpdateWindowVisibility(this, false); // saiu da visão de comércio ou foi mexer em outra coisa
            }
            // Enquanto o posto está aberto, o painel "Comércio Internacional" (que fica no mesmo lugar) some.
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
                Plugin.Log.LogError($"Posto comercial: {ex}");
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
                SetOpen(-1); // o território deixou de ser seu
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
                    blockedHere > 0 ? L.F("Rotas comerciais entre outros impérios que atravessam este território agora. {0} império(s) bloqueado(s) aqui.", blockedHere)
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
                    // Só quem usa este posto; quem já foi taxado ou bloqueado aqui continua na lista
                    // (a rota bloqueada some, mas é preciso poder liberar).
                    int empire = i;
                    if (routes.Any(r => r.Left == empire || r.Right == empire)
                        || TradePolicy.StoredMode(world, me, territoryIndex, empire) != TradeMode.Free)
                    {
                        empires.Add(i);
                    }
                }
                usersCount = empires.Count;
                // Quem passa por aqui primeiro.
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

                // Desvio por causa do pedágio, juntando as linhas sem contar duas vezes a rota entre dois impérios taxados
                // (prévia calculada pelo caminho do próprio jogo). Bloqueio vence: rota barrada não conta como desvio.
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
                SetButtonText(applyAllButton, L.T("Aplicar"));
                Tip(note, "StatsTable/PopCount", L.T("Seus territórios"), L.T("Quantos postos recebem as escolhas deste ao clicar em Aplicar."));
                Tip(note, "StatsTable/Fortification", L.T("Quando vale"), L.T("As rotas são recalculadas na sua próxima ação ou no fim do turno."));
            }
            HideOutputs(note);
            noteCard.SetAsLastSibling();
            SetVisible(noteCard, true);
            MatchTradePanelHeight();
        }

        /// <summary>
        /// A janela fica por cima do painel "Comércio Internacional" e, com poucas linhas, encolheria e
        /// deixaria o painel aparecer por baixo: a altura acompanha a do painel nativo.
        /// </summary>
        private void MatchTradePanelHeight()
        {
            UITransform mine = GetComponent<UITransform>();
            if (mine == null)
            {
                return;
            }
            // Borda de baixo visível do painel "Comércio Internacional" (a janela dele ocupa a tela toda).
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

            string modeText = mode == TradeMode.Toll
                ? "<c=" + TollColor + ">" + L.F("Pedágio {0} {1}", symbol, Format.Money(price)) + "</c>"
                : mode == TradeMode.Block ? "<c=" + BlockColor + ">" + L.T("Bloqueado") + "</c>" : L.T("Livre");
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
            // Com pedágio, os botões − e + ocupam a ponta da linha: o terceiro chip sai e o que ele pagou vai para a dica do preço.
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
                        + (paid != null ? " " + L.F("No último turno ele pagou {0} {1} somando todos os seus postos.", symbol, Format.Money(paid.ReceivedByOwner)) : string.Empty)
                        + (theirs.Count > 0 ? " " + L.F("Nas rotas daqui ele compra {0} e vende {1} recurso(s).", buys, sells) : string.Empty)
                : mode == TradeMode.Block ? L.T("Proibido de passar por aqui. Sem outro caminho, a rota é destruída.")
                : L.T("Passa livremente por aqui."));
            Tip(top, "StatsTable/ExtensionsCount", paid != null ? L.T("Pedágio pago") : L.T("Comércio nessas rotas"),
                paid != null ? L.T("Quanto ele pagou a você no último turno, somando todos os seus postos.")
                : L.T("Recursos que ele compra e vende nas rotas que passam por aqui. Mais recursos, pedágio maior."));

            UIButton button = top.Find("LiberateButton").GetComponent<UIButton>();
            SetButtonText(button, mode == TradeMode.Free ? L.T("Taxar") : mode == TradeMode.Toll ? L.T("Bloquear") : L.T("Liberar"));
        }

        /// <summary>
        /// Chip das rotas da linha. Com pedágio ou bloqueio, mostra a prévia de desvio (TollPreview): das rotas que passariam
        /// aqui sem a regra, quantas pagam e quantas desviam (ou ficam barradas). Pede o cálculo e soma para o cartão de cima.
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

        /// <summary>"Pagam: rota com Os Polinésios." para a dica da linha (null se não houver).</summary>
        private static string PartnerList(string label, List<int> partners)
        {
            return partners.Count == 0 ? null
                : $"{label}: " + string.Join(", ", partners.Select(p => L.F("rota com {0}", ShortEmpireName(p)))) + ".";
        }

        /// <summary>"Desviam: rota com Os Axumitas (passa pelo seu posto de X)." — desviar para outro posto seu não foge de você.</summary>
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

        /// <summary>Botãozinho quadrado na linha dos chips, preso à direita do cartão.</summary>
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

        /// <summary>Só impérios que você já conhece (os outros aparecem como "Desconhecido").</summary>
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
                // − e + do preço: cópias do botão da linha, na altura dos chips, presas à direita.
                UIButton down = UnityEngine.Object.Instantiate(button.gameObject, button.transform.parent).GetComponent<UIButton>();
                down.name = "PriceDown";
                UIButton up = UnityEngine.Object.Instantiate(button.gameObject, button.transform.parent).GetComponent<UIButton>();
                up.name = "PriceUp";
                // Hífen comum: a fonte do jogo não tem o sinal de menos (U+2212) e mostraria "?".
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
                    L.T("Pedágio: as rotas desse império continuam passando aqui, mas ele paga por turno, por recurso transportado, na sua moeda. Rotas podem desviar se houver outro caminho.") + "\n" +
                    L.T("Bloqueado: as rotas desse império não podem atravessar este território; sem outro caminho, a rota é destruída.") + "\n" +
                    L.T("Bloquear ou cobrar pedágio dá a ele uma reclamação \"Bloqueio comercial\" contra você."));
            }
            for (int i = 0; i < rows.Count; i++)
            {
                SetVisible(rows[i], i < count);
            }
            SetVisible(cardSample, false);
        }

        // ---------------- Clique no mapa (visão de comércio) ----------------

        /// <summary>Na visão de comércio o clique não fazia nada: num território seu, abre o posto.</summary>
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
                    Plugin.Log.LogError($"Posto comercial (clique): {ex}");
                }
            }
        }

        // ---------------- Remoção ----------------

        internal static void DestroyWindow()
        {
            TradePostWindow window = Instance;
            Instance = null;
            if (window == null)
            {
                return;
            }
            window.SetTradePanelHidden(false); // devolve o painel nativo (recarga do mod / saída da partida)
            try
            {
                if (window.Group is UIWindowsGroup group && group.windows != null)
                {
                    group.windows = group.windows.Where(w => w != window).ToArray();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Remoção da janela do posto: {ex.Message}");
            }
            window.gameObject.SetActive(false);
            Destroy(window.gameObject);
        }

        /// <summary>Comandos: "open &lt;território&gt;", "close", "info", "set &lt;território&gt; &lt;império&gt; free|toll|block", "mine".</summary>
        internal static string DevCommand(string args)
        {
            string[] parts = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0] : "info";
            CurrencyWorld world = CurrencyManager.Current;
            switch (sub)
            {
                case "open":
                    SetOpen(int.Parse(parts[1]));
                    return Instance != null ? "ok" : "erro: janela não criada";
                case "close":
                    SetOpen(-1);
                    return "ok";
                case "previa":
                {
                    // "trade previa <dono> <território|-1> <alvo> <preço> [bloqueio]": prévia de desvio (pede e mostra a
                    // resposta já calculada; rode de novo para ler o resultado de um pedido novo).
                    if (parts.Length < 5 || !int.TryParse(parts[1], out int owner) || !int.TryParse(parts[2], out int territory)
                        || !int.TryParse(parts[3], out int target) || !double.TryParse(parts[4], out double asked))
                    {
                        return "uso: trade previa <dono> <território|-1> <alvo> <preço> [bloqueio]";
                    }
                    var ask = new TollPreview.Ask
                    {
                        Owner = owner,
                        Territory = territory,
                        Target = target,
                        Mode = parts.Length > 5 && parts[5].StartsWith("bloq") ? TradeMode.Block : TradeMode.Toll,
                        Price = asked,
                    };
                    TollPreview.Request(ask, maxAgeSeconds: 0.5f);
                    TollPreview.Answer answer = TollPreview.Get(ask);
                    return answer == null ? "pedido feito; rode de novo para ler"
                        : $"rotas de E{target} que passariam por {(territory < 0 ? $"territórios de E{owner}" : $"T{territory}")} sem regra: {answer.Routes} · com {ask.Mode} {asked}: passam {answer.Stay}, desviam {answer.Away}\n{answer.Detail}";
                }
                case "price":
                {
                    // "trade price <império> up|down": o mesmo que os botões + e − da linha (janela aberta).
                    if (Instance == null || !Instance.Shown || parts.Length < 3 || !int.TryParse(parts[1], out int empire))
                    {
                        return "uso: trade price <império> up|down (com a janela do posto aberta)";
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
                        string.Join(", ", TradePolicy.RoutesThrough(t).Select(r => $"{r.Left}↔{r.Right} (compra {r.LeftBuys}/{r.RightBuys}{(r.Suspended ? ", suspensa" : "")})")));
                    return string.Join("\n", lines);
                }
                case "costs":
                {
                    // Manutenção por território de cada rota (leitura direta da simulação, só diagnóstico).
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
                        text.AppendLine($"{relation.LeftEmpireIndex}↔{relation.RightEmpireIndex}: territórios={relation.PathAsTerritories.Count} nós={nodes} recursos={goods} manutenção={upkeep:0.#} por nó={(nodes > 0 ? upkeep / nodes : 0):0.##}");
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
                        return "erro: território ou império inválido";
                    }
                    int owner = territories[territory].EmpireIndex;
                    if (owner < 0 || owner >= majors)
                    {
                        return "erro: território sem dono maior";
                    }
                    lock (CurrencyManager.Lock)
                    {
                        TradePolicy.SetRule(world, owner, territory, target, mode);
                    }
                    return $"ok: dono {owner}";
                }
                default:
                {
                    if (world == null)
                    {
                        return "sem estado";
                    }
                    lock (CurrencyManager.Lock)
                    {
                        var text = new System.Text.StringBuilder();
                        text.AppendLine($"Regras ({world.TradeRules.Count}):");
                        foreach (TradeRule rule in world.TradeRules)
                        {
                            text.AppendLine($"  dono {rule.Owner} território {rule.Territory} alvo {rule.Target}: {rule.Mode}");
                        }
                        text.AppendLine("Incidentes: " + string.Join(", ", world.TradeIncidents.Select(i => $"{i.Owner}→{i.Victim}")));
                        text.AppendLine("Pedágios: " + string.Join(", ", world.LastTolls.Select(t => $"{t.Payer} pagou {t.PaidByPayer} a {t.Owner}")));
                        text.AppendLine("Preços gerais: " + string.Join(", ", world.TollPrices.Select(p => $"{p.Owner}→{p.Target}: {p.Price}")));
                        text.AppendLine("Preços por posto: " + string.Join(", ", world.TradeRules.Where(r => r.Price > 0).Select(r => $"{r.Owner} T{r.Territory}→{r.Target}: {r.Price}")));
                        text.AppendLine($"Recalcular rotas pendente: {TradePolicy.PathsDirty}");
                        text.AppendLine($"Janela: existe={Instance != null} estado={Instance?.Visibility} shown={Instance?.Shown} visible={Instance?.GetComponent<UITransform>().VisibleSelf} pendente={Instance?.pendingOpen} loaded={Instance?.Loaded} visaoComercio={InTradeView()} cursor={Amplitude.Mercury.Presentation.Presentation.PresentationCursorController?.CurrentCursor?.GetType().Name}");
                        return text.ToString();
                    }
                }
            }
        }
    }
}
