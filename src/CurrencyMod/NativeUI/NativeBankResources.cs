using System;
using System.Collections.Generic;
using System.Linq;
using CurrencyMod.Diplomacy;
using CurrencyMod.Diplomacy.Capture;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// "Resources" section of the Central Bank: the strategic and luxury resources the empire accesses now (one card per
    /// resource: how many accesses and where they come from — own deposit, bought from someone, sold to someone) and a
    /// chart of the accesses over the turns. The history is recorded at the end of each turn (HistoryPoint.Resources,
    /// humans only); older saves start with no history.
    /// </summary>
    internal partial class NativeBankWindow
    {
        /// <summary>Chart lines: the resources with the most accesses now (the rest only count in the total).</summary>
        private const int ResourceChartLines = 5;

        private static readonly Color[] ResourceColors =
        {
            new Color(0.40f, 0.71f, 0.93f), new Color(0.56f, 0.82f, 0.54f), new Color(0.91f, 0.41f, 0.37f),
            new Color(0.78f, 0.56f, 0.91f), new Color(0.96f, 0.66f, 0.33f),
        };
        private static readonly string[] ResourceHex = { "66B5ED", "8FD18A", "E8685E", "C78FE8", "F5A854" };

        /// <summary>Current accesses (type → count): the turn snapshot, otherwise the last history point.</summary>
        private static Dictionary<int, int> CurrentResources(EmpireCurrency mine, int localIndex, out WorldCapture capture)
        {
            capture = TurnCapture.Latest;
            CapturedEmpire me = capture?.Empire(localIndex);
            if (me?.ResourceAccess != null)
            {
                var access = new Dictionary<int, int>();
                for (int t = 0; t < me.ResourceAccess.Length; t++)
                {
                    GameGlossary.ResourceEntry entry = me.ResourceAccess[t] > 0 ? GameGlossary.Resource(t) : null;
                    if (entry != null && (entry.Luxury || entry.Strategic))
                    {
                        access[t] = me.ResourceAccess[t];
                    }
                }
                return access;
            }
            capture = null;
            return mine.History.LastOrDefault(h => h.Resources != null)?.Resources ?? new Dictionary<int, int>();
        }

        private void FillResources(EmpireCurrency mine, int localIndex)
        {
            Dictionary<int, int> current = CurrentResources(mine, localIndex, out WorldCapture capture);
            List<KeyValuePair<int, int>> ordered = current
                .OrderByDescending(p => GameGlossary.Resource(p.Key)?.Strategic ?? false).ThenByDescending(p => p.Value)
                .ThenBy(p => GameGlossary.Resource(p.Key)?.Name).ToList();
            SetRowCount(Math.Max(1, ordered.Count));
            if (ordered.Count == 0)
            {
                Transform empty = rows[0].Find("Table/Top");
                SetLabel(empty, "TitleGroup/Title", L.T("Nenhum recurso estratégico ou de luxo"));
                SetChip(empty, "StatsTable/PopCount", L.T("Sem acesso"));
                Tip(empty, "StatsTable/PopCount", L.T("Recursos"), L.T("Construa extratores nas jazidas do seu território ou compre recursos por rota comercial (acordo econômico)."));
                HideChild(empty, "StatsTable/Fortification");
                HideChild(empty, "StatsTable/ExtensionsCount");
                HideExtraChips(empty);
                HideChild(empty, "LiberateButton");
                HideOutputs(empty);
                return;
            }
            for (int i = 0; i < ordered.Count; i++)
            {
                int type = ordered[i].Key;
                int count = ordered[i].Value;
                GameGlossary.ResourceEntry entry = GameGlossary.Resource(type);
                Transform top = rows[i].Find("Table/Top");
                SetLabel(top, "TitleGroup/Title", entry?.Name ?? L.T("Recurso desconhecido"));
                SetChip(top, "StatsTable/PopCount", count == 1 ? L.T("1 acesso") : L.F("{0} acessos", count));
                SetChip(top, "StatsTable/Fortification", entry != null && entry.Strategic ? L.T("Estratégico") : L.T("Luxo"));
                Tip(top, "StatsTable/PopCount", L.T("Acessos"), L.T("Quantas fontes desse recurso o seu império usa agora (extratores nas suas jazidas mais as compras por rota comercial)."));
                Tip(top, "StatsTable/Fortification", L.T("Tipo"), entry != null && entry.Strategic
                    ? L.T("Estratégico: libera unidades e construções.")
                    : L.T("Luxo: dá bônus às suas cidades enquanto você tiver acesso."));
                string origin = Origin(capture, localIndex, type, out string originHelp);
                SetVisible(top.Find("StatsTable/ExtensionsCount"), origin != null);
                if (origin != null)
                {
                    SetChip(top, "StatsTable/ExtensionsCount", origin);
                    Tip(top, "StatsTable/ExtensionsCount", L.T("De onde vem"), originHelp);
                }
                int before = Before(mine, type);
                string trend = ExtraChip(top, 3);
                SetVisible(top.Find(trend), before >= 0 && before != count);
                if (before >= 0 && before != count)
                {
                    SetChip(top, trend, (count > before ? $"<c={UpColor}>▲</c> " : $"<c={DownColor}>▼</c> ") + L.F("era {0}", before));
                    Tip(top, trend, L.T("Mudança"), L.F("Quantos acessos você tinha há {0} turnos.", TrendTurns));
                }
                HideChild(top, "StatsTable/Chip4");
                HideChild(top, "LiberateButton");
                HideOutputs(top);
            }
        }

        private const int TrendTurns = 5;

        /// <summary>Accesses of the resource TrendTurns points back in the history (-1 with no history).</summary>
        private static int Before(EmpireCurrency mine, int type)
        {
            List<HistoryPoint> points = mine.History.Where(h => h.Resources != null).ToList();
            if (points.Count < 2)
            {
                return -1;
            }
            HistoryPoint old = points[Math.Max(0, points.Count - 1 - TrendTurns)];
            return old.Resources.TryGetValue(type, out int value) ? value : 0;
        }

        /// <summary>"Own deposit", "Bought from X" or "Sold to X" from the turn snapshot (null with no snapshot).</summary>
        private static string Origin(WorldCapture capture, int localIndex, int type, out string help)
        {
            help = null;
            if (capture == null)
            {
                return null;
            }
            List<int> sellers = capture.Trades.Where(t => !t.Suspended && t.Buyer == localIndex && t.Resources.Contains(type)).Select(t => t.Seller).Distinct().ToList();
            List<int> buyers = capture.Trades.Where(t => !t.Suspended && t.Seller == localIndex && t.Resources.Contains(type)).Select(t => t.Buyer).Distinct().ToList();
            if (sellers.Count > 0)
            {
                help = L.F("Você compra esse recurso por rota comercial de: {0}.", string.Join(", ", sellers.Select(ShortEmpireName)));
                return sellers.Count == 1 ? L.F("Comprado {0}", Diplomacy.GameText.OfUi(ShortEmpireName(sellers[0]))) : L.F("Comprado de {0} impérios", sellers.Count);
            }
            help = buyers.Count > 0
                ? L.F("Vem das suas jazidas, e você vende esse recurso por rota comercial para: {0}.", string.Join(", ", buyers.Select(ShortEmpireName)))
                : L.T("Vem das jazidas do seu território.");
            return buyers.Count > 0
                ? (buyers.Count == 1 ? L.F("Seu · vendido para {0}", Diplomacy.GameText.InlineUi(ShortEmpireName(buyers[0]))) : L.F("Seu · vendido para {0} impérios", buyers.Count))
                : L.T("Jazida própria");
        }

        private static void HideExtraChips(Transform top)
        {
            HideChild(top, "StatsTable/Chip3");
            HideChild(top, "StatsTable/Chip4");
        }

        private void FillResourcesChart(EmpireCurrency mine, int localIndex)
        {
            Transform top = chartCard.Find("Table/Top");
            List<HistoryPoint> history = mine.History.Where(h => h.Resources != null).ToList();
            if (history.Count < 2)
            {
                FillChartEmpty(top, L.T("O gráfico dos recursos começa a ser gravado agora; aparece depois de dois turnos."));
                return;
            }
            chartImage.Color = Color.white;
            int first = history[0].Turn;
            int last = history[history.Count - 1].Turn;
            int count = Mathf.Max(2, last - first + 1);
            Dictionary<int, int> latest = history[history.Count - 1].Resources;
            List<int> lines = latest.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(ResourceChartLines).Select(p => p.Key).ToList();

            double[] Line(Func<Dictionary<int, int>, double> pick)
            {
                var values = new double[count];
                for (int i = 0; i < count; i++)
                {
                    values[i] = double.NaN;
                }
                foreach (HistoryPoint p in history)
                {
                    int index = p.Turn - first;
                    if (index >= 0 && index < count)
                    {
                        values[index] = pick(p.Resources);
                    }
                }
                return values;
            }

            var series = new List<BankChart.Series>();
            var names = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                int type = lines[i];
                series.Add(new BankChart.Series { Values = Line(r => r.TryGetValue(type, out int v) ? v : 0), Color = ResourceColors[i % ResourceColors.Length], Thickness = 4.5f, Dot = true });
                names.Add(GameGlossary.Resource(type)?.Name ?? L.T("Recurso desconhecido"));
            }
            double[] total = Line(r => r.Values.Sum());
            series.Add(new BankChart.Series { Values = total, Color = new Color(0.89f, 0.78f, 0.54f), Thickness = 7f, Fill = true, Dot = true });
            names.Add(L.T("Total"));

            var references = new List<BankChart.Reference>();
            BankChart.Range(series, references, out double min, out double max);
            min = 0;
            // Access counts are whole numbers: an even top keeps the middle label whole too (it read "1, 1, 0" with a top of 1).
            max = Math.Max(2, Math.Ceiling(max / 2.0) * 2);
            double checksum = series.SelectMany(s => s.Values).Where(v => !double.IsNaN(v)).Sum();
            string signature = "resources|" + count + "|" + first + "|" + max.ToString("0.00") + "|" + series.Count + "|" + checksum.ToString("0.00");
            if (signature != chartSignature)
            {
                chartSignature = signature;
                pendingChart = new PendingChart { Series = series, References = references, Min = min, Max = max, Count = count };
            }
            chartYLabels[0].Text = max.ToString("0");
            chartYLabels[1].Text = ((min + max) / 2.0).ToString("0");
            chartYLabels[2].Text = min.ToString("0");
            chartXLabels[0].Text = L.F("Turno {0}", first);
            chartXLabels[1].Text = L.F("Turno {0}", last);
            SetLabel(top, "TitleGroup/Title", L.P(history.Count, "Acessos a recursos no último turno", "Acessos a recursos nos últimos {0} turnos"));

            // Money of resources per turn (melhorias-jogando item 23): purchases, sales and route upkeep with other empires,
            // from the bank's trade ledger. Recorded per partner, not per resource, so the value is per turn.
            var moneyByTurn = new Dictionary<int, (double Paid, double Gain)>();
            // Per resource (item 31): purchases record their resource type since 1.3.2; route upkeep stays in the total.
            var moneyByResource = new Dictionary<(int Turn, int Type), (double Paid, double Gain)>();
            CurrencyWorld ledgerWorld = CurrencyManager.Current;
            foreach (TradePoint point in ledgerWorld?.TradeHistory ?? new List<TradePoint>())
            {
                foreach (MoneyFlow flow in point.Money.Where(m => m.Kind == MoneyKind.Resources && m.Resource >= 0 && (m.From == localIndex || m.To == localIndex)))
                {
                    moneyByResource.TryGetValue((point.Turn, flow.Resource), out var sum);
                    moneyByResource[(point.Turn, flow.Resource)] = (sum.Paid + (flow.From == localIndex ? flow.Paid : 0), sum.Gain + (flow.To == localIndex ? flow.Gain : 0));
                }
                double paid = point.Money.Where(m => m.From == localIndex && (m.Kind == MoneyKind.Resources || m.Kind == MoneyKind.Upkeep)).Sum(m => m.Paid);
                double gain = point.Money.Where(m => m.To == localIndex && (m.Kind == MoneyKind.Resources || m.Kind == MoneyKind.Upkeep)).Sum(m => m.Gain);
                moneyByTurn[point.Turn] = (paid, gain);
            }
            string ResourceMoney(int turn, int type)
            {
                if (!moneyByResource.TryGetValue((turn, type), out var money) || (money.Paid < 0.05 && money.Gain < 0.05))
                {
                    return string.Empty;
                }
                var parts = new List<string>();
                if (money.Paid >= 0.05)
                {
                    parts.Add($"<c={TextPatches.NegativeColor}>-{mine.Symbol} {Format.Money(money.Paid)}</c>");
                }
                if (money.Gain >= 0.05)
                {
                    parts.Add($"+{mine.Symbol} {Format.Money(money.Gain)}");
                }
                return " · " + string.Join(" ", parts);
            }
            string MoneyText(int turn) => moneyByTurn.TryGetValue(turn, out var money) && (money.Paid > 0.05 || money.Gain > 0.05)
                ? L.F("pago {0} {1} · recebido {0} {2}", mine.Symbol, Format.Money(money.Paid), Format.Money(money.Gain))
                : null;

            int strategic = latest.Where(p => GameGlossary.Resource(p.Key)?.Strategic ?? false).Sum(p => p.Value);
            int luxury = latest.Where(p => GameGlossary.Resource(p.Key)?.Luxury ?? false).Sum(p => p.Value);
            string legend = string.Join("  ", lines.Select((type, i) => "<c=" + ResourceHex[i % ResourceHex.Length] + ">●</c> " + names[i]));
            string[] chips =
            {
                Dot("E3C88A") + L.F("Total {0}", latest.Values.Sum()) + (MoneyText(last) is string lastMoney ? " · " + lastMoney : string.Empty),
                L.F("Estratégicos {0} · luxo {1}", strategic, luxury),
                legend.Length > 0 ? legend : L.T("Sem recursos"),
            };
            string[][] tips =
            {
                new[] { L.T("Total"), L.T("Soma dos acessos a todos os recursos estratégicos e de luxo, turno a turno. O valor é o dinheiro do último turno fechado: compras e vendas de recursos e manutenção das rotas com outros impérios (passe o mouse no gráfico para ver cada turno).") },
                new[] { L.T("Por tipo"), L.T("Estratégicos liberam unidades e construções; luxos dão bônus às cidades.") },
                new[] { L.T("Linhas"), L.F("Os {0} recursos com mais acessos agora, cada um na sua cor.", ResourceChartLines) },
            };
            for (int c = 0; c < ChipPaths.Length; c++)
            {
                SetVisible(top.Find(ChipPaths[c]), true);
                SetChip(top, ChipPaths[c], chips[c]);
                Tip(top, ChipPaths[c], tips[c][0], tips[c][1]);
            }
            SetVisible(top.Find("StatsTable"), true);
            HideOutputs(top, ChartCardHeight);

            SetChartHover(first, count, index =>
            {
                var hover = new List<string>();
                for (int s = series.Count - 1; s >= 0; s--)
                {
                    double value = index < series[s].Values.Length ? series[s].Values[index] : double.NaN;
                    if (!double.IsNaN(value))
                    {
                        hover.Add(names[s] + L.Colon + value.ToString("0") + (s < lines.Count ? ResourceMoney(first + index, lines[s]) : string.Empty));
                    }
                }
                string money = MoneyText(first + index);
                if (money != null)
                {
                    hover.Insert(0, money);
                }
                return new[] { L.F("Turno {0}", first + index), string.Join("\n", hover) };
            });
        }
    }
}
