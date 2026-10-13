using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// "Your Trade Posts" block in the diplomacy Trade tab: shortcut to set the selected empire to Free, Toll or
    /// Blocked at all your posts at once. Built from pieces of the tab itself: the "Your Resources" title, the
    /// rounded resource box, small-caps labels and the price buttons of the trade table (UIToggle with a rounded
    /// background).
    /// </summary>
    internal class DiplomacyTradePolicyPanel : MonoBehaviour
    {
        private const string PanelName = "CurrencyMod_TradePolicyPanel";
        private const float Left = 24f;      // relative to Content (x 372 on screen): aligns with "Your Resources"
        private const float Width = 540f;
        private const float TitleTop = 180f;
        private const float BoxTop = 212f;
        private const float BoxHeight = 122f;
        private const float PriceTop = 68f;
        private const float PriceButtonWidth = 30f;
        private const float ButtonWidth = 160f;
        private const float ButtonHeight = 30f;
        private const float ButtonGap = 14f;

        private static readonly TradeMode[] Modes = { TradeMode.Free, TradeMode.Toll, TradeMode.Block };
        private static readonly string[] ModeLabels = { L.N("Livre"), L.N("Taxar"), L.N("Bloquear") };

        private Transform root;
        private Transform title;
        private Transform info;
        private Transform footer;
        private Transform priceLabel;
        private UIToggle priceDown;
        private UIToggle priceUp;
        private readonly UIToggle[] buttons = new UIToggle[3];
        private bool updatingButtons;
        private int otherEmpire = -1;
        private float nextUpdate;
        private bool failed;

        private void Update()
        {
            if (Time.unscaledTime < nextUpdate)
            {
                return;
            }
            nextUpdate = Time.unscaledTime + 0.5f;
            try
            {
                // At game start the window service does not exist yet (see DiplomacyEconomyPanel).
                if (WindowsUtils.WindowsService == null || !Diplomacy.GameAccess.TryGetSession(out _, out _, out _))
                {
                    return;
                }
                DiplomaticScreen screen = WindowsUtils.GetWindow<DiplomaticScreen>();
                if (screen == null || !screen.Shown || SavePatches.IsGameOnline() || !TradeBlockade.Enabled)
                {
                    return;
                }
                if (root == null)
                {
                    // The donor search scans the whole screen: only with the Trade tab open, and if it fails, wait 2 s.
                    if (screen.currentMode != DiplomaticScreenMode.Trade)
                    {
                        return;
                    }
                    bool built;
                    try
                    {
                        built = Build(screen.transform);
                    }
                    catch
                    {
                        foreach (Transform part in parts)
                        {
                            NativeUIKit.Dispose(part);
                        }
                        parts.Clear();
                        root = null;
                        throw;
                    }
                    if (!built)
                    {
                        nextUpdate = Time.unscaledTime + 2f;
                        return;
                    }
                }
                Refresh();
                failed = false;
            }
            catch (Exception ex)
            {
                // A transient error (diplomacy screen changing state) does not disable the panel until reload:
                // retry in 5 s and only log the first of consecutive failures.
                if (!failed)
                {
                    Plugin.Log.LogError($"Trade posts in diplomacy: {ex}");
                }
                failed = true;
                nextUpdate = Time.unscaledTime + 5f;
            }
        }

        private bool Build(Transform screen)
        {
            Transform content = screen.Find("PanelsGroup/_TradePanel/Content");
            Transform mySide = content?.Find("MySide");
            Transform toggleDonor = FindPurchaseToggle(screen);
            if (mySide == null || toggleDonor == null)
            {
                return false; // the table has no items yet to serve as a template; try again later
            }

            // Leftovers from a previous generation of the mod (hot reload).
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                if (content.GetChild(i).name.StartsWith(PanelName))
                {
                    NativeUIKit.Dispose(content.GetChild(i));
                }
            }
            root = content;

            title = Part(mySide.Find("MyResourcesLabel"), "Title");
            NativeUIKit.Place(title, Left, TitleTop, Width, 32);

            Transform box = Part(mySide.Find("Background"), "Box");
            NativeUIKit.Place(box, Left, BoxTop, Width, BoxHeight);

            Transform smallCaps = mySide.Find("MyStrategicsLabel");
            info = Part(smallCaps, "Info");
            NativeUIKit.Place(info, Left, BoxTop + 2, Width, 30);
            NativeUIKit.Align(info, HorizontalAlignment.Center);
            NoGrow(info);
            Brighten(info);

            float buttonsLeft = Left + (Width - (3 * ButtonWidth + 2 * ButtonGap)) / 2f;
            for (int i = 0; i < 3; i++)
            {
                Transform button = Part(toggleDonor, "Mode" + i);
                NativeUIKit.Place(button, buttonsLeft + i * (ButtonWidth + ButtonGap), BoxTop + 34, ButtonWidth, ButtonHeight);
                UILabel label = NativeUIKit.Label(button);
                label.AutoAdjustWidth = false;
                label.Alignment = new Alignment(HorizontalAlignment.Center, VerticalAlignment.Center);
                label.Text = L.T(ModeLabels[i]); // ModeLabels: marked with L.N
                // The label used to fit the button width to the text: with fitting off, reposition it.
                NativeUIKit.Place(button, buttonsLeft + i * (ButtonWidth + ButtonGap), BoxTop + 34, ButtonWidth, ButtonHeight);
                FitLabel(label, ButtonWidth);
                buttons[i] = button.GetComponent<UIToggle>();
                int index = i;
                buttons[i].Switch += (source, state) => Button_Switch(index);
                BindTooltip(button, i);
            }

            // General toll price for this empire: text in the middle, − and + at the right end of the row.
            float upLeft = Left + Width - 16f - PriceButtonWidth;
            float downLeft = upLeft - 6f - PriceButtonWidth;
            priceLabel = Part(smallCaps, "Price");
            // Fixed width: with auto-fit the text grew under the buttons and out of the card.
            UILabel priceText = NativeUIKit.Label(priceLabel);
            if (priceText != null)
            {
                priceText.AutoAdjustWidth = false;
            }
            NativeUIKit.Place(priceLabel, Left + 16f, BoxTop + PriceTop, downLeft - Left - 24f, 30);
            NativeUIKit.Align(priceLabel, HorizontalAlignment.Center);
            priceDown = PriceButton(toggleDonor, "PriceDown", "-", downLeft, -1);
            priceUp = PriceButton(toggleDonor, "PriceUp", "+", upLeft, +1);

            footer = Part(smallCaps, "Footer");
            NativeUIKit.Place(footer, Left, BoxTop + 92, Width, 26);
            NativeUIKit.Align(footer, HorizontalAlignment.Center);
            NoGrow(footer);
            Brighten(footer);
            Brighten(priceLabel);

            Plugin.Log.LogInfo("Trade posts block created in the diplomacy Trade tab.");
            return true;
        }

        /// <summary>Fixed-width text line that truncates with an ellipsis (the donor grew to both sides and left the box).</summary>
        /// <summary>The block's rows sit over the map: the donor comes at 50% opacity and becomes unreadable.</summary>
        private static void Brighten(Transform target)
        {
            UILabel label = NativeUIKit.Label(target);
            if (label != null)
            {
                label.Color = new Color(1f, 1f, 1f, 0.92f);
            }
        }

        private static void NoGrow(Transform target)
        {
            UILabel label = NativeUIKit.Label(target);
            if (label != null)
            {
                label.AutoAdjustWidth = false;
                label.AutoTruncate = true;
            }
        }

        /// <summary>
        /// The donor button has an automatic width from 65 to 80: the text stayed centered in that range (and not on the
        /// actual button), so "+" fell outside the button and "Livre" sat to the left. Range = button width, and text again.
        /// </summary>
        private static void FitLabel(UILabel label, float width)
        {
            label.AutoAdjustWidthMin = width;
            label.AutoAdjustWidthMax = width;
            label.AutoAdjustWidth = false;
            string text = label.Text;
            label.Text = string.Empty;
            label.Text = text;
        }

        /// <summary>− or + of the general price: the same button as the trade table, used as a button (turns back off on click).</summary>
        private UIToggle PriceButton(Transform donor, string name, string text, float left, int direction)
        {
            Transform button = Part(donor, name);
            NativeUIKit.Place(button, left, BoxTop + PriceTop + 2f, PriceButtonWidth, 26f);
            UILabel label = NativeUIKit.Label(button);
            label.AutoAdjustWidth = false;
            label.Alignment = new Alignment(HorizontalAlignment.Center, VerticalAlignment.Center);
            label.Text = text;
            NativeUIKit.Place(button, left, BoxTop + PriceTop + 2f, PriceButtonWidth, 26f);
            FitLabel(label, PriceButtonWidth);
            UIToggle toggle = button.GetComponent<UIToggle>();
            toggle.Switch += (source, state) =>
            {
                if (updatingButtons || !state)
                {
                    return;
                }
                ChangeGeneralPrice(direction);
                updatingButtons = true;
                toggle.State = false;
                updatingButtons = false;
            };
            UITooltip tooltip = button.GetComponent<UITooltip>();
            if (tooltip != null)
            {
                tooltip.Unbind(preserveTooltipClass: false);
                tooltip.Bind(TooltipUtils.TitleAndDescription, new Amplitude.Mercury.UI.Tooltips.TitleAndDescription(
                    direction < 0 ? L.T("Baixar o pedágio deles") : L.T("Subir o pedágio deles"),
                    L.T("Preço por recurso transportado, por turno, que esse império paga em todos os seus postos com pedágio. Vale também para os postos em que você tinha posto outro preço.") + " "
                    + (direction < 0 ? L.T("Mais barato, menos rotas desviam.") : L.T("Se pagar sair mais caro que desviar, a rota desvia e você não recebe nada."))));
            }
            return toggle;
        }

        private void ChangeGeneralPrice(int direction)
        {
            if (otherEmpire < 0 || !CentralBankWindow.TryGetGameData(out GameSnapshot.Data game))
            {
                return;
            }
            int me = game.LocalEmpireInfo.EmpireIndex;
            int era = Math.Max(1, (int)game.EmpireInfo[me].EraIndex);
            CurrencyWorld world = CurrencyManager.Ensure(game.GameID, game.NumberOfMajorEmpires);
            lock (CurrencyManager.Lock)
            {
                double current = TradePolicy.GeneralPriceOrDefault(me, otherEmpire, era);
                double next = TradePolicy.StepPrice(current, direction);
                TradePolicy.SetGeneralPrice(world, me, otherEmpire, next);
                Plugin.Log.LogInfo($"Diplomacy: general toll for empire {otherEmpire} → {next} per resource.");
            }
            nextUpdate = 0;
        }

        /// <summary>Price button of a trade table item (only exists when the other side sells something).</summary>
        private static Transform FindPurchaseToggle(Transform screen)
        {
            var item = screen.GetComponentsInChildren<TradeTableItem>(true).FirstOrDefault()
                ?? Resources.FindObjectsOfTypeAll<TradeTableItem>().FirstOrDefault(t => t.gameObject.scene.IsValid());
            return item != null ? item.transform.Find("PurchaseToggle") : null;
        }

        private static void BindTooltip(Transform button, int index)
        {
            string[] titles =
            {
                L.T("Livre em todos os seus postos"),
                L.T("Taxar em todos os seus postos"),
                L.T("Bloquear em todos os seus postos"),
            };
            string[] descriptions =
            {
                L.T("As rotas desse império passam livremente por todos os seus territórios."),
                L.T("Em todos os seus territórios, as rotas desse império pagam pedágio por turno (por recurso transportado), ou desviam se a volta sair mais barata."),
                L.T("As rotas desse império não podem atravessar nenhum território seu; sem outro caminho, são destruídas."),
            };
            string extra = index == 0 ? string.Empty : "\n" + L.T("Ele ganha uma reclamação \"Bloqueio comercial\" contra você.");
            UITooltip tooltip = button.GetComponent<UITooltip>();
            if (tooltip != null)
            {
                tooltip.Unbind(preserveTooltipClass: false);
                tooltip.Bind(TooltipUtils.TitleAndDescription,
                    new Amplitude.Mercury.UI.Tooltips.TitleAndDescription(titles[index], descriptions[index] + extra));
            }
        }

        private void Button_Switch(int index)
        {
            if (updatingButtons || otherEmpire < 0)
            {
                return;
            }
            if (!CentralBankWindow.TryGetGameData(out GameSnapshot.Data game))
            {
                return;
            }
            int me = game.LocalEmpireInfo.EmpireIndex;
            CurrencyWorld world = CurrencyManager.Ensure(game.GameID, game.NumberOfMajorEmpires);
            List<int> territories = TradePostWindow.MyTerritories(me);
            int changed = 0;
            lock (CurrencyManager.Lock)
            {
                foreach (int territory in territories)
                {
                    if (TradePolicy.SetRule(world, me, territory, otherEmpire, Modes[index]))
                    {
                        changed++;
                    }
                }
            }
            Plugin.Log.LogInfo($"Diplomacy: empire {otherEmpire} → {TradePolicy.ModeName(Modes[index])} in {territories.Count} territory(ies) ({changed} change(s)).");
            nextUpdate = 0;
        }

        private void Refresh()
        {
            if (!CentralBankWindow.TryGetGameData(out GameSnapshot.Data game))
            {
                return;
            }
            int me = game.LocalEmpireInfo.EmpireIndex;
            int other = Snapshots.DiplomaticCursorSnapshot.PresentationData.OtherEmpireIndex;
            bool valid = other >= 0 && other < game.NumberOfMajorEmpires && other != me;
            foreach (Transform part in parts)
            {
                NativeUIKit.SetVisible(part, valid);
            }
            if (!valid)
            {
                return;
            }
            otherEmpire = other;

            if (!TradePolicy.TransitPublished && Amplitude.Mercury.Sandbox.Sandbox.TradeController != null)
            {
                TradeBlockade.PublishTransit(Amplitude.Mercury.Sandbox.Sandbox.TradeController);
            }
            List<int> territories = TradePostWindow.MyTerritories(me);
            int routes = territories
                .SelectMany(t => TradePolicy.RoutesThrough(t))
                .Where(r => (r.Left == other || r.Right == other) && r.Left != me && r.Right != me)
                .Select(r => r.Left * 256 + r.Right)
                .Distinct()
                .Count();

            CurrencyWorld world = CurrencyManager.Ensure(game.GameID, game.NumberOfMajorEmpires);
            TradeMode? uniform;
            int taxed;
            int blocked;
            double paid;
            string symbol;
            double general;
            bool ownPrices;
            int era = Math.Max(1, (int)game.EmpireInfo[me].EraIndex);
            lock (CurrencyManager.Lock)
            {
                List<TradeMode> modes = territories.Select(t => TradePolicy.StoredMode(world, me, t, other)).ToList();
                taxed = modes.Count(m => m == TradeMode.Toll);
                blocked = modes.Count(m => m == TradeMode.Block);
                uniform = modes.Count > 0 && modes.All(m => m == modes[0]) ? modes[0] : (TradeMode?)null;
                paid = world.LastTolls.Where(t => t.Owner == me && t.Payer == other).Sum(t => t.ReceivedByOwner);
                symbol = world.Get(me)?.Symbol ?? "$";
                general = TradePolicy.StoredGeneralPrice(world, me, other);
                ownPrices = world.TradeRules.Any(r => r.Owner == me && r.Target == other && r.Mode == TradeMode.Toll && r.Price > 0);
            }
            double price = general > 0 ? general : TradePolicy.DefaultPrice(era);
            NativeUIKit.SetText(priceLabel, general > 0
                ? L.F("Pedágio: {0} {1} por recurso", symbol, Format.Money(price))
                : L.F("Pedágio: {0} {1} por recurso (padrão)", symbol, Format.Money(price)));
            NativeUIKit.Tip(priceLabel, L.T("Preço do pedágio deles"),
                L.T("Quanto esse império paga, por recurso transportado e por turno, para passar pelos seus postos com pedágio. Os botões mudam o preço em todos os postos; na janela do Posto Comercial (visão Comercializar) dá para pôr um preço só naquele posto."));

            NativeUIKit.SetText(title, L.T("Seus Postos Comerciais"));
            // With toll or blockade at all posts: detour preview through the game's own pathing (how many of their routes
            // pay and how many detour or get blocked), which updates instantly with the price buttons.
            if (uniform.HasValue && uniform.Value != TradeMode.Free)
            {
                // With posts that have their own price, the preview shows today's situation (each post with its price);
                // without them, the general price at all posts, which is what the buttons apply.
                var ask = new TollPreview.Ask
                {
                    Owner = me,
                    Territory = -1,
                    Target = other,
                    Mode = uniform.Value,
                    Price = uniform.Value != TradeMode.Toll ? 0 : ownPrices ? -1 : price,
                };
                TollPreview.Request(ask);
                TollPreview.Answer answer = TollPreview.Get(ask);
                NativeUIKit.SetText(info, answer == null ? L.T("Calculando o efeito nas rotas deles…")
                    : answer.Routes == 0 ? L.T("Nenhuma rota deles passaria pelos seus territórios")
                    : uniform.Value == TradeMode.Block ? (answer.Routes == 1 ? L.T("O bloqueio barra 1 rota deles") : L.F("O bloqueio barra {0} rotas deles", answer.Routes))
                    : answer.Away == 0 ? (answer.Routes == 1 ? L.T("A rota deles paga o pedágio · nenhuma desvia") : L.F("As {0} rotas deles pagam · nenhuma desvia", answer.Routes))
                    : answer.Stay == 0 ? "<c=F2A65A>" + (answer.Routes == 1 ? L.T("A rota deles desvia dos seus postos") : L.F("As {0} rotas deles desviam dos seus postos", answer.Routes)) + "</c>"
                    : (answer.Stay == 1 ? L.F("1 de {0} rotas deles paga", answer.Routes) : L.F("{0} de {1} rotas deles pagam", answer.Stay, answer.Routes))
                        + " · <c=F2A65A>" + (answer.Away == 1 ? L.T("1 desvia dos seus postos") : L.F("{0} desviam dos seus postos", answer.Away)) + "</c>");
                NativeUIKit.Tip(info, L.T("Rotas deles e o pedágio"),
                    L.T("Das rotas desse império que passariam pelos seus territórios sem pedágio nem bloqueio, quantas continuam passando (e pagam) e quantas vão por outro caminho, mais barato. Calculado pelo caminho do próprio jogo; atualiza na hora quando você muda o preço. As rotas mudam de verdade na sua próxima ação."));
            }
            else
            {
                NativeUIKit.SetText(info, routes == 0
                    ? L.T("Nenhuma rota deles passa pelos seus territórios")
                    : routes == 1 ? L.T("1 rota deles passa pelos seus territórios") : L.F("{0} rotas deles passam pelos seus territórios", routes));
                NativeUIKit.Tip(info, L.T("Rotas deles"), L.T("Rotas comerciais desse império com terceiros que atravessam algum território seu. Só essas podem ser taxadas ou bloqueadas."));
            }

            string state = !uniform.HasValue ? L.F("Misto: {0} com pedágio, {1} bloqueados", taxed, blocked)
                : uniform.Value == TradeMode.Toll ? L.F("Pedágio em todos os {0} postos", territories.Count)
                : uniform.Value == TradeMode.Block ? L.F("Bloqueado em todos os {0} postos", territories.Count)
                : L.F("Livre em todos os {0} postos", territories.Count);
            if (ownPrices)
            {
                state += " · " + L.T("há postos com preço próprio");
            }
            if (paid > 0)
            {
                state += " · " + L.F("pagaram {0} {1}", symbol, Format.Money(paid));
            }
            // Their choice in the face of your tolls (rota_pedagio of the language-model AI).
            TollChoiceKind theirChoice = TradePolicy.GetTollChoice(other, me);
            if (theirChoice == TollChoiceKind.Pay)
            {
                state += " · <c=8FD18A>" + L.T("eles escolheram pagar") + "</c>";
            }
            else if (theirChoice == TollChoiceKind.Detour)
            {
                state += " · <c=F2A65A>" + L.T("eles escolheram desviar") + "</c>";
            }
            NativeUIKit.SetText(footer, state);
            NativeUIKit.Tip(footer, L.T("Situação nos seus postos"),
                L.T("Como esse império está tratado nos seus postos hoje. \"Misto\" quando você ajustou postos diferentes pela janela do Posto Comercial (visão Comercializar). \"Preço próprio\": um posto em que você pôs outro preço só para ele; os botões - e + daqui trocam o preço de todos os postos de uma vez."));

            updatingButtons = true;
            for (int i = 0; i < 3; i++)
            {
                bool on = uniform.HasValue && uniform.Value == Modes[i];
                if (buttons[i] != null && buttons[i].State != on)
                {
                    buttons[i].State = on;
                }
                // The toggle's own "on" look is too faint: the chosen mode also gets a gold check (melhorias item 52).
                UILabel label = buttons[i] != null ? NativeUIKit.Label(buttons[i].transform) : null;
                string text = on ? "<c=F2C46D>✓ " + L.T(ModeLabels[i]) + "</c>" : L.T(ModeLabels[i]);
                if (label != null && label.Text != text)
                {
                    label.Text = text;
                }
            }
            updatingButtons = false;
        }

        private readonly List<Transform> parts = new List<Transform>();

        /// <summary>Piece cloned directly into the tab (shows and hides together with it).</summary>
        private Transform Part(Transform donor, string name)
        {
            Transform part = NativeUIKit.Clone(donor, root, PanelName + "_" + name);
            parts.Add(part);
            return part;
        }

        private void OnDestroy()
        {
            foreach (Transform part in parts)
            {
                NativeUIKit.Dispose(part);
            }
            parts.Clear();
            root = null;
        }
    }
}
