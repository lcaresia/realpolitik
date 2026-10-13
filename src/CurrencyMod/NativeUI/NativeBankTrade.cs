using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Interop;
using Amplitude.UI;
using Amplitude.UI.Renderers;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Central Bank "Trade" section: the money you earn from and pay to each empire per turn, in a line chart
    /// (totals, 5-turn moving average) and in one card per country with two bars (paid and earned, average of the last 10 turns).
    /// The numbers come from the ledger recorded by the mod (CurrencyWorld.TradeHistory → MoneyFlow): resource purchases (the
    /// buyer pays upfront and the seller earns a share, only once), route upkeep (per turn, only the buyer pays),
    /// post tolls (per turn) and other transfers (gifts, demands). All in your currency.
    /// </summary>
    internal partial class NativeBankWindow
    {
        private const float TradeCardHeight = 142f;
        private const float TradeBarTop = 82f;
        private const float TradeBarStep = 28f;
        private const string PaidColor = "E8685E";
        private const string GainColor = "8FD18A";
        /// <summary>How many turns the per-country card average covers.</summary>
        private const int TradeAverageTurns = 10;
        /// <summary>Moving-average window of the chart lines.</summary>
        private const int TradeSmoothTurns = 5;

        private sealed class TradeRow
        {
            public Transform Card;
            public Transform PaidBar;
            public Transform GainBar;
        }

        /// <summary>What an empire received from you and what you received from it, by kind, summed over a window of turns.</summary>
        private sealed class PartnerTotals
        {
            public int Empire;
            public readonly double[] Paid = new double[5];
            public readonly double[] Gain = new double[5];
            public int BoughtGoods;
            public int SoldGoods;
            public double PaidTotal => Paid.Sum();
            public double GainTotal => Gain.Sum();
        }

        private readonly List<TradeRow> tradeRows = new List<TradeRow>();
        private Transform tradeEmptyCard;
        private UILabel tradeEmptyText;
        private bool tradeEmptyVisible;
        private int tradeRowsShown;

        private void BuildTrade()
        {
            tradeEmptyCard = MakeTextCard(listTable, cardSample, "TradeEmptyCard", out tradeEmptyText);
        }

        private void SetTradeVisible(bool visible)
        {
            SetVisible(tradeEmptyCard, visible && tradeEmptyVisible);
            for (int i = 0; i < tradeRows.Count; i++)
            {
                SetVisible(tradeRows[i].Card, visible && i < tradeRowsShown);
            }
        }

        // ---------------- Numbers ----------------

        private static string Signed(double value) => (value < 0 ? "−" : "+") + Format.Money(Math.Abs(value));

        /// <summary>Sum of what flowed between you and each empire in turns [from, to].</summary>
        private static Dictionary<int, PartnerTotals> TotalsByPartner(List<TradePoint> history, int me, int from, int to)
        {
            var result = new Dictionary<int, PartnerTotals>();
            PartnerTotals Of(int empire)
            {
                if (!result.TryGetValue(empire, out PartnerTotals totals))
                {
                    totals = new PartnerTotals { Empire = empire };
                    result[empire] = totals;
                }
                return totals;
            }
            foreach (TradePoint point in history)
            {
                if (point.Turn < from || point.Turn > to)
                {
                    continue;
                }
                foreach (MoneyFlow flow in point.Money)
                {
                    int kind = Mathf.Clamp(flow.Kind, 0, MoneyKind.Transit);
                    if (flow.From == me && flow.To != me && flow.To >= 0)
                    {
                        Of(flow.To).Paid[kind] += flow.Paid;
                    }
                    else if (flow.To == me && flow.From != me && flow.From >= 0)
                    {
                        Of(flow.From).Gain[kind] += flow.Gain;
                    }
                }
                if (point.Turn == to)
                {
                    foreach (TradeFlow flow in point.Flows)
                    {
                        if (flow.Buyer == me && flow.Seller != me)
                        {
                            Of(flow.Seller).BoughtGoods += flow.Goods;
                        }
                        else if (flow.Seller == me && flow.Buyer != me)
                        {
                            Of(flow.Buyer).SoldGoods += flow.Goods;
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Only turns already closed: the point of the turn in progress has the money events (recorded immediately), but
        /// upkeep and the resource count only come in at the end of the turn, and the average would fluctuate.
        /// </summary>
        private static List<TradePoint> CompleteHistory(CurrencyWorld world)
        {
            List<TradePoint> all = world.TradeHistory ?? new List<TradePoint>();
            return world.LastProcessedTurn < 0 ? all : all.Where(p => p.Turn <= world.LastProcessedTurn).ToList();
        }

        private static string headerKey;
        private static int headerWindow;
        private static Dictionary<int, PartnerTotals> headerTotals;

        /// <summary>
        /// The trade totals by partner for the headers, computed once per turn: each of the trade view's empire rows used to
        /// copy and sum the whole history again (~120 ms on a late game, melhorias item 42).
        /// </summary>
        private static bool HeaderTotals(CurrencyWorld world, int me, out int window, out Dictionary<int, PartnerTotals> byPartner)
        {
            List<TradePoint> all = world.TradeHistory;
            string key = $"{me}:{world.LastProcessedTurn}:{all?.Count ?? 0}:{(all != null && all.Count > 0 ? all[all.Count - 1].Turn : -1)}";
            if (key != headerKey)
            {
                List<TradePoint> history = CompleteHistory(world);
                headerKey = key;
                headerTotals = null;
                if (history.Count > 0)
                {
                    int last = history[history.Count - 1].Turn;
                    headerWindow = WindowTurns(history, last, TradeAverageTurns);
                    headerTotals = TotalsByPartner(history, me, last - headerWindow + 1, last);
                }
            }
            window = headerWindow;
            byPartner = headerTotals;
            return byPartner != null;
        }

        /// <summary>
        /// Another empire's trade header (trade view market, melhorias-jogando item 22): a short line (relation,
        /// exchange rate, trade balance per turn) and the details for its tooltip (what you buy and sell, averaged like
        /// the bank's Trade tab). False when there is no game or currency data.
        /// </summary>
        internal static bool TradeHeader(int partner, out string line, out string details)
        {
            line = details = null;
            CurrencyWorld world = CurrencyManager.Current;
            var sandbox = Amplitude.Mercury.Sandbox.SandboxManager.Sandbox;
            if (world == null || sandbox == null)
            {
                return false;
            }
            int me = sandbox.LocalEmpireIndex;
            if (me < 0 || partner < 0 || me >= world.Empires.Count || partner >= world.Empires.Count || me == partner)
            {
                return false;
            }
            var shortParts = new List<string>();
            var lines = new List<string>();
            Diplomacy.Capture.WorldCapture capture = Diplomacy.Capture.TurnCapture.Latest;
            Diplomacy.Capture.CapturedRelation relation = capture?.Empire(me)?.RelationWith(partner);
            if (relation != null)
            {
                string chip = Diplomacy.UI.MailScreen.RelationChip(relation);
                shortParts.Add(chip);
                lines.Add(L.F("Relação: {0}", chip));
            }
            EmpireCurrency mine = world.Get(me);
            EmpireCurrency other = world.Get(partner);
            if (mine != null && other != null && other.ExchangeValue > 1e-6)
            {
                string rate = Format.Rate(mine.ExchangeValue / other.ExchangeValue);
                shortParts.Add(L.F("1 {0} = {1} {2}", mine.Symbol, rate, other.Symbol));
                lines.Add(L.F("Câmbio: 1 {0} vale {1} {2} ({3}).", mine.PluralOrName, rate, other.PluralOrName, other.Symbol));
            }
            if (HeaderTotals(world, me, out int window, out Dictionary<int, PartnerTotals> byPartner))
            {
                if (byPartner.TryGetValue(partner, out PartnerTotals totals)
                    && (totals.PaidTotal > 0.05 || totals.GainTotal > 0.05 || totals.BoughtGoods > 0 || totals.SoldGoods > 0))
                {
                    double balance = (totals.GainTotal - totals.PaidTotal) / window;
                    string color = balance > 0.05 ? GainColor : balance < -0.05 ? PaidColor : null;
                    shortParts.Add(Colored(L.F("saldo {0}/t", Signed(balance)), color));
                    lines.Add(L.F("Você compra {0} recurso(s) dele: {1} {2}/turno.", totals.BoughtGoods, mine?.Symbol, Format.Money(totals.PaidTotal / window)));
                    lines.Add(L.F("Você vende {0} recurso(s) a ele: {1} {2}/turno.", totals.SoldGoods, mine?.Symbol, Format.Money(totals.GainTotal / window)));
                    lines.Add(L.F("Média dos últimos {0} turno(s), com a manutenção das rotas. Detalhes no Banco Central, aba Comércio.", window));
                }
            }
            if (shortParts.Count == 0)
            {
                return false;
            }
            line = string.Join("  ·  ", shortParts);
            details = string.Join("\n", lines);
            return true;
        }

        /// <summary>How many turns the average window actually covers (the history may be shorter).</summary>
        private static int WindowTurns(List<TradePoint> history, int last, int wanted)
        {
            int first = history.Count > 0 ? history[0].Turn : last;
            return Mathf.Max(1, Mathf.Min(wanted, last - first + 1));
        }

        // ---------------- Per-country cards ----------------

        private void FillTrade(GameSnapshot.Data game, CurrencyWorld world, EmpireCurrency mine, int localIndex)
        {
            List<TradePoint> history = CompleteHistory(world);
            int last = history.Count > 0 ? history[history.Count - 1].Turn : 0;
            int window = WindowTurns(history, last, TradeAverageTurns);
            List<PartnerTotals> partners = TotalsByPartner(history, localIndex, last - window + 1, last).Values
                .Where(p => p.Empire < game.NumberOfMajorEmpires && p.Empire < game.EmpireInfo.Length && game.EmpireInfo[p.Empire].IsAlive
                    && p.Empire < world.Empires.Count && TradePostWindow.IsKnown(p.Empire) && (p.PaidTotal > 0 || p.GainTotal > 0))
                .OrderByDescending(p => p.PaidTotal + p.GainTotal)
                .ToList();

            tradeEmptyVisible = partners.Count == 0;
            tradeRowsShown = partners.Count;
            if (partners.Count == 0)
            {
                FillTextCard(tradeEmptyCard, tradeEmptyText,
                    history.Count == 0 ? L.T("Comércio") : L.T("Nenhum dinheiro trocado com outros impérios"),
                    history.Count == 0
                        ? L.T("Os números aparecem depois do próximo fim de turno.")
                        : L.T("Você não pagou nem ganhou nada de outros impérios nos últimos turnos. Compre ou venda recursos na diplomacia, aba Comércio, para começar."));
                SetTradeVisible(true);
                return;
            }

            while (tradeRows.Count < partners.Count)
            {
                tradeRows.Add(CreateTradeRow(tradeRows.Count));
            }
            double maxValue = Math.Max(1e-6, partners.Max(p => Math.Max(p.PaidTotal, p.GainTotal)));
            float cardWidth = cardSample.GetComponent<UITransform>().Width;
            for (int i = 0; i < partners.Count; i++)
            {
                PartnerTotals partner = partners[i];
                EmpireCurrency other = world.Get(partner.Empire);
                TradeRow row = tradeRows[i];
                Transform top = row.Card.Find("Table/Top");
                double paid = partner.PaidTotal / window;
                double gain = partner.GainTotal / window;
                double balance = gain - paid;
                string balanceColor = balance > 0.05 ? GainColor : balance < -0.05 ? PaidColor : null;
                FillTextCardHeader(top,
                    NationAndLeaderName(partner.Empire) + " · " + other.Name,
                    Colored(L.F("Saldo {0}/turno", Signed(balance)), balanceColor),
                    L.F("Recursos: você compra {0} · vende {1}", partner.BoughtGoods, partner.SoldGoods));
                Tip(top, "StatsTable/PopCount", L.T("Saldo com esse império"),
                    L.F("O que você ganhou com ele menos o que pagou a ele (manutenção das rotas incluída), por turno, em {1} (média de {0} turno(s)). Verde: você ganha mais do que paga; vermelho: você paga mais do que ganha.", window, mine.PluralOrName));
                Tip(top, "StatsTable/Fortification", L.T("Recursos negociados"),
                    L.T("Quantos tipos de recurso você compra dele e quantos ele compra de você pelas rotas comerciais."));

                FillTradeBar(row.PaidBar, TradeBarTop, cardWidth, paid, maxValue / window, PaidColor,
                    L.F("Você paga {0} {1}/turno", mine.Symbol, Format.Money(paid)), L.T("O que você paga"),
                    L.F("Média de {0} turno(s), em {1}.", window, mine.PluralOrName) + "\n" + Breakdown(partner.Paid, window, true));
                FillTradeBar(row.GainBar, TradeBarTop + TradeBarStep, cardWidth, gain, maxValue / window, GainColor,
                    L.F("Você ganha {0} {1}/turno", mine.Symbol, Format.Money(gain)), L.T("O que você ganha"),
                    L.F("Média de {0} turno(s), em {1}.", window, mine.PluralOrName) + "\n" + Breakdown(partner.Gain, window, false));
                HideOutputs(top, TradeCardHeight);
            }
            SetTradeVisible(true);
        }

        /// <summary>Tooltip lines of a bar: how much comes from each kind of flow (per turn).</summary>
        private static string Breakdown(double[] byKind, int window, bool paid)
        {
            var lines = new List<string>();
            void Add(string label, double value)
            {
                if (value > 1e-9)
                {
                    lines.Add(label + L.Colon + Format.Money(value / window));
                }
            }
            Add(paid ? L.T("Compra de recursos") : L.T("Venda de recursos"), byKind[MoneyKind.Resources]);
            if (paid)
            {
                Add(L.T("Manutenção das rotas"), byKind[MoneyKind.Upkeep]);
            }
            Add(L.T("Pedágios"), byKind[MoneyKind.Toll]);
            Add(L.T("Passagem das rotas"), byKind[MoneyKind.Transit]);
            Add(L.T("Presentes e outras transferências"), byKind[MoneyKind.Other]);
            return lines.Count > 0 ? string.Join("\n", lines) : L.T("Nada nesse período.");
        }

        /// <summary>Title and two chips (the third stays hidden) of a country's card.</summary>
        private static void FillTextCardHeader(Transform top, string title, string chipA, string chipB)
        {
            SetLabel(top, "TitleGroup/Title", title);
            SetVisible(top.Find("StatsTable"), true);
            SetVisible(top.Find("StatsTable/PopCount"), true);
            SetChip(top, "StatsTable/PopCount", chipA);
            SetVisible(top.Find("StatsTable/Fortification"), true);
            SetChip(top, "StatsTable/Fortification", chipB);
            SetVisible(top.Find("StatsTable/ExtensionsCount"), false);
            HideChild(top, "LiberateButton");
        }

        private TradeRow CreateTradeRow(int index)
        {
            Transform card = listTable.InstantiateChild(cardSample, "TradeRow" + index).transform;
            Transform top = card.Find("Table/Top");
            HideChild(top, "LiberateButton");
            UITransform topUi = top.GetComponent<UITransform>();
            Transform chip = top.Find("StatsTable/PopCount");
            var row = new TradeRow { Card = card };
            row.PaidBar = topUi.InstantiateChild(chip, "PaidBar").transform;
            row.GainBar = topUi.InstantiateChild(chip, "GainBar").transform;
            return row;
        }

        /// <summary>Horizontal bar made from the game's "chip" piece: the width follows the value and the text goes inside.</summary>
        private static void FillTradeBar(Transform bar, float y, float cardWidth, double value, double max, string colorHex, string text, string tipTitle, string tipText)
        {
            UILabel label = NativeUIKit.Label(bar);
            if (label == null)
            {
                return;
            }
            HideChild(bar, "Picto");
            label.AutoAdjustWidth = false;
            label.Margins = new RectMargins(10f, 10f, label.Margins.Top, label.Margins.Bottom);
            label.Alignment = new Alignment(HorizontalAlignment.Left, VerticalAlignment.Center);
            float full = cardWidth - 24f;
            float minimum = Mathf.Max(230f, text.Length * 8.5f + 28f); // the text goes inside the bar: the minimum width follows the text
            float width = value <= 1e-9 ? minimum : Mathf.Lerp(minimum, full, Mathf.Clamp01((float)(value / Math.Max(1e-9, max))));
            UITransform ui = bar.GetComponent<UITransform>();
            if (Mathf.Abs(ui.X - 8f) > 0.5f || Mathf.Abs(ui.Y - y) > 0.5f || Mathf.Abs(ui.Width - width) > 0.5f || Mathf.Abs(ui.Height - 24f) > 0.5f)
            {
                NativeUIKit.Place(bar, 8f, y, width, 24f);
            }
            SetVisible(bar, true);
            var background = bar.GetComponent<UISquircleImage>();
            if (background != null)
            {
                Color color = ParseColor(colorHex);
                color.a = value <= 1e-9 ? 0.18f : 0.55f;
                if (background.Color != color)
                {
                    background.Color = color;
                }
            }
            if (label.Text != text)
            {
                label.Text = text;
            }
            NativeUIKit.Tip(bar, tipTitle, tipText);
        }

        private static Color ParseColor(string hex)
        {
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        // ---------------- Chart ----------------

        /// <summary>Lines of what you pay and what you earn per turn (sum over all empires), 5-turn moving average.</summary>
        private void FillTradeChart(CurrencyWorld world, EmpireCurrency mine, int localIndex)
        {
            Transform top = chartCard.Find("Table/Top");
            List<TradePoint> history = CompleteHistory(world);
            if (history.Count < 2)
            {
                FillChartEmpty(top);
                return;
            }
            chartImage.Color = Color.white;
            int first = history[0].Turn;
            int last = history[history.Count - 1].Turn;
            int count = Mathf.Max(2, last - first + 1);
            var paid = new double[count];
            var gain = new double[count];
            var present = new bool[count];
            foreach (TradePoint point in history)
            {
                int index = point.Turn - first;
                if (index < 0 || index >= count)
                {
                    continue;
                }
                present[index] = true;
                foreach (MoneyFlow flow in point.Money)
                {
                    if (flow.From == localIndex && flow.To != localIndex && flow.To >= 0)
                    {
                        paid[index] += flow.Paid;
                    }
                    else if (flow.To == localIndex && flow.From != localIndex && flow.From >= 0)
                    {
                        gain[index] += flow.Gain;
                    }
                }
            }
            double[] paidLine = Smooth(paid, present);
            double[] gainLine = Smooth(gain, present);
            var series = new List<BankChart.Series>
            {
                new BankChart.Series { Values = paidLine, Color = new Color(0.91f, 0.41f, 0.37f), Thickness = 7f, Fill = true, Dot = true },
                new BankChart.Series { Values = gainLine, Color = new Color(0.56f, 0.82f, 0.54f), Thickness = 7f, Fill = true, Dot = true },
            };
            var references = new List<BankChart.Reference>();
            BankChart.Range(series, references, out double min, out double max);
            min = 0; // money flowing is never negative: the axis starts at zero
            double checksum = paidLine.Concat(gainLine).Where(v => !double.IsNaN(v)).Sum();
            string signature = "trade|" + count + "|" + first + "|" + max.ToString("0.0000") + "|" + checksum.ToString("0.0000");
            if (signature != chartSignature)
            {
                chartSignature = signature;
                pendingChart = new PendingChart { Series = series, References = references, Min = min, Max = max, Count = count };
            }

            int window = WindowTurns(history, last, TradeAverageTurns);
            double averagePaid = 0, averageGain = 0;
            for (int i = Math.Max(0, count - window); i < count; i++)
            {
                averagePaid += paid[i];
                averageGain += gain[i];
            }
            averagePaid /= window;
            averageGain /= window;
            chartYLabels[0].Text = Format.Money(max);
            chartYLabels[1].Text = Format.Money((min + max) / 2.0);
            chartYLabels[2].Text = Format.Money(min);
            chartXLabels[0].Text = L.F("Turno {0}", first);
            chartXLabels[1].Text = L.F("Turno {0}", last);
            SetLabel(top, "TitleGroup/Title", L.F("Dinheiro com outros impérios, últimos {0} turnos", history.Count));
            string[] chips =
            {
                Dot(PaidColor) + L.F("Você paga {0}/turno", Format.Money(averagePaid)),
                Dot(GainColor) + L.F("Você ganha {0}/turno", Format.Money(averageGain)),
                Dot("FFFFFF") + L.F("Saldo {0}/turno", Signed(averageGain - averagePaid)),
            };
            string[][] tips =
            {
                new[] { L.T("O que você paga"), L.F("Compras de recursos, manutenção das rotas, pedágios e presentes que saíram do seu caixa, por turno, em {0}. A linha é a média móvel de {1} turnos; o número, a média dos últimos {2}.", mine.PluralOrName, TradeSmoothTurns, window) },
                new[] { L.T("O que você ganha"), L.F("Vendas de recursos (o vendedor ganha uma parte quando a compra é feita), pedágios e presentes que entraram no seu caixa, por turno, em {0}. A linha é a média móvel de {1} turnos; o número, a média dos últimos {2}.", mine.PluralOrName, TradeSmoothTurns, window) },
                new[] { L.T("Saldo"), L.T("O que você ganha menos o que você paga, por turno. Positivo: entra mais dinheiro do que sai.") },
            };
            for (int c = 0; c < ChipPaths.Length; c++)
            {
                SetVisible(top.Find(ChipPaths[c]), true);
                SetChip(top, ChipPaths[c], chips[c]);
                Tip(top, ChipPaths[c], tips[c][0], tips[c][1]);
            }
            SetVisible(top.Find("StatsTable"), true);
            HideOutputs(top, ChartCardHeight);

            string symbol = mine.Symbol;
            SetChartHover(first, count, index => new[]
            {
                L.F("Turno {0}", first + index),
                Dot(PaidColor) + L.F("Você pagou {0} {1}", symbol, Format.Money(paid[index])) + "  (" + L.F("média {0}", Format.Money(double.IsNaN(paidLine[index]) ? 0 : paidLine[index])) + ")\n"
                + Dot(GainColor) + L.F("Você ganhou {0} {1}", symbol, Format.Money(gain[index])) + "  (" + L.F("média {0}", Format.Money(double.IsNaN(gainLine[index]) ? 0 : gainLine[index])) + ")\n"
                + L.F("Saldo {0}", Signed(gain[index] - paid[index])),
            });
        }

        /// <summary>Moving average of the last TradeSmoothTurns turns (only those present in the history).</summary>
        private static double[] Smooth(double[] values, bool[] present)
        {
            var result = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                double sum = 0;
                int n = 0;
                for (int k = Math.Max(0, i - TradeSmoothTurns + 1); k <= i; k++)
                {
                    if (present[k])
                    {
                        sum += values[k];
                        n++;
                    }
                }
                result[i] = n > 0 ? sum / n : double.NaN;
            }
            return result;
        }

        // ---------------- Test (development tool) ----------------

        /// <summary>
        /// "nbank fake": fills the player's ledger with made-up data (occasional purchases, upkeep and tolls every
        /// turn), to check the screen without waiting dozens of turns. Only meant for test games.
        /// </summary>
        internal static string FakeTradeHistory()
        {
            if (!CentralBankWindow.TryGetGameData(out GameSnapshot.Data game))
            {
                return "error: no game";
            }
            int me = game.LocalEmpireInfo.EmpireIndex;
            CurrencyWorld world = CurrencyManager.Ensure(game.GameID, game.NumberOfMajorEmpires);
            var random = new System.Random(7);
            int[] partners = Enumerable.Range(0, game.NumberOfMajorEmpires).Where(i => i != me && i < game.EmpireInfo.Length && game.EmpireInfo[i].IsAlive && TradePostWindow.IsKnown(i)).Take(4).ToArray();
            lock (CurrencyManager.Lock)
            {
                int endTurn = Math.Max(world.LastProcessedTurn, 64);
                world.TradeHistory = new List<TradePoint>();
                for (int turn = endTurn - 44; turn <= endTurn; turn++)
                {
                    TradePoint point = world.PointFor(turn);
                    foreach (int partner in partners)
                    {
                        double rate = world.Rate(me, partner);
                        point.Flows.Add(new TradeFlow { Buyer = me, Seller = partner, Value = 8 + partner % 5, Goods = 1 + partner % 4 });
                        point.Flows.Add(new TradeFlow { Buyer = partner, Seller = me, Value = 5, Goods = 1 + partner % 3 });
                        point.Money.Add(new MoneyFlow { From = me, To = partner, Paid = 8 + partner % 5 + random.NextDouble() * 2, Gain = 0, Kind = MoneyKind.Upkeep });
                        if (random.NextDouble() < 0.25)
                        {
                            point.Money.Add(new MoneyFlow { From = me, To = partner, Paid = 40 + random.NextDouble() * 80, Gain = 0, Kind = MoneyKind.Resources });
                        }
                        if (random.NextDouble() < 0.2)
                        {
                            point.Money.Add(new MoneyFlow { From = partner, To = me, Paid = 30 * rate, Gain = 25 + random.NextDouble() * 70, Kind = MoneyKind.Resources });
                        }
                        if (partner % 2 == 0)
                        {
                            point.Money.Add(new MoneyFlow { From = partner, To = me, Paid = 6 * rate, Gain = 6, Kind = MoneyKind.Toll });
                        }
                    }
                }
            }
            return $"ok: fake money ledger with {string.Join(", ", partners.Select(p => "E" + p))}";
        }
    }
}
