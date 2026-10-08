using System;
using System.Collections.Generic;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.UI;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Painel "Economia" na aba Relações da tela de diplomacia: compara a sua economia com a do
    /// império selecionado (PIB, renda, força da moeda, inflação e juros) e mostra a cotação.
    /// Montado com peças da própria tela: título de "Minhas Relações", caixa fosca da lista de
    /// relações, barras de "Apoio de Guerra" e o texto rico da frase de estado.
    /// </summary>
    internal class DiplomacyEconomyPanel : MonoBehaviour
    {
        private const string PanelName = "CurrencyMod_EconomyPanel";
        private const float PanelWidth = 756f;
        private const float GaugeWidth = 256f;
        private const float GaugeInnerWidth = 252f;
        private const float RowsTop = 120f;

        private class Row
        {
            public Transform Name;
            public Transform Gauge;
            public Transform ValueGroup;
            public Transform Left;
            public Transform Right;
        }

        private Transform panel;
        private Transform title;
        private Transform subtitle;
        private Transform headerMine;
        private Transform headerTheirs;
        private readonly List<Row> rows = new List<Row>();
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
                // No começo do jogo o serviço de janelas ainda não existe: a busca daria erro e o painel se
                // desligaria até a próxima recarga.
                if (WindowsUtils.WindowsService == null || !Diplomacia.GameAccess.TryGetSession(out _, out _, out _))
                {
                    return;
                }
                DiplomaticScreen screen = WindowsUtils.GetWindow<DiplomaticScreen>();
                if (screen == null || !screen.Shown || SavePatches.IsGameOnline())
                {
                    return;
                }
                if (panel == null)
                {
                    rows.Clear();
                    Build(screen.transform);
                }
                Refresh();
                failed = false;
            }
            catch (Exception ex)
            {
                if (!failed)
                {
                    Plugin.Log.LogError($"Painel de economia na diplomacia: {ex}");
                }
                failed = true;
                nextUpdate = Time.unscaledTime + 5f;
            }
        }

        private void Build(Transform screen)
        {
            rows.Clear();
            Transform group = screen.Find("PanelsGroup/_RelationsPanel/_DefaultGroup");
            Transform relations = group.Find("_MyRelations");
            Transform boxDonor = relations.Find("Scrollview");
            Transform titleDonor = relations.Find("Label");
            Transform sentenceDonor = group.Find("Content/Label");
            Transform gaugeDonor = screen.Find("_NegociationGroup/_MyMoral/Gauge");
            Transform smallCapsDonor = screen.Find("_NegociationGroup/_MyMoral/Label");
            Transform valueDonor = screen.Find("_NegociationGroup/MoneyGroup/Labels/Stock");

            Transform leftover = group.Find(PanelName);
            if (leftover != null)
            {
                NativeUIKit.Dispose(leftover);
            }

            // Caixa fosca (a mesma da lista de relações), sem a rolagem.
            panel = NativeUIKit.Clone(boxDonor, group, PanelName, "UIScrollView", "ScrollingDragArea");
            Transform viewport = panel.Find("Viewport");
            if (viewport != null)
            {
                DestroyImmediate(viewport.gameObject);
            }
            UITransform groupUi = group.GetComponent<UITransform>();
            float left = 612f - groupUi.GlobalRect.x;
            float top = 344f - groupUi.GlobalRect.y;
            const int metricCount = 5;
            float height = RowsTop + metricCount * 50f + 10f;
            NativeUIKit.Place(panel, left, top, PanelWidth, height);
            foreach (Transform child in panel)
            {
                NativeUIKit.Place(child, 0, 0, PanelWidth, height); // o desfoque acompanha a caixa
            }

            title = NativeUIKit.Clone(titleDonor, panel, "Title");
            NativeUIKit.Place(title, 0, 4, PanelWidth, 32);
            NativeUIKit.Align(title, HorizontalAlignment.Center);
            NativeUIKit.Label(title).Color = new Color(1f, 0.875f, 0.584f); // dourado dos títulos de seção

            // A frase de estado ajusta a própria altura; aqui a altura é fixa.
            subtitle = NativeUIKit.Clone(sentenceDonor, panel, "Subtitle");
            NativeUIKit.Label(subtitle).AutoAdjustHeight = false;
            NativeUIKit.Place(subtitle, 20, 44, PanelWidth - 40, 44); // cabe 2 linhas (a frase de câmbio é longa)
            NativeUIKit.Align(subtitle, HorizontalAlignment.Center);

            // Cabeçalhos das colunas, alinhados aos valores de cada lado.
            float columnWidth = (PanelWidth - GaugeWidth) / 2f - 34f;
            headerMine = NativeUIKit.Clone(smallCapsDonor, panel, "HeaderMine");
            NativeUIKit.Place(headerMine, 20, 94, columnWidth, 18);
            NativeUIKit.Align(headerMine, HorizontalAlignment.Right);
            headerTheirs = NativeUIKit.Clone(smallCapsDonor, panel, "HeaderTheirs");
            NativeUIKit.Place(headerTheirs, PanelWidth - 20 - columnWidth, 94, columnWidth, 18);
            NativeUIKit.Align(headerTheirs, HorizontalAlignment.Left);

            for (int i = 0; i < metricCount; i++)
            {
                float y = RowsTop + i * 50f;
                var row = new Row
                {
                    Name = NativeUIKit.Clone(smallCapsDonor, panel, "Name" + i),
                    Gauge = NativeUIKit.Clone(gaugeDonor, panel, "Gauge" + i),
                    Left = NativeUIKit.Clone(valueDonor, panel, "Mine" + i),
                    Right = NativeUIKit.Clone(valueDonor, panel, "Theirs" + i),
                };
                float gaugeLeft = (PanelWidth - GaugeWidth) / 2f;
                NativeUIKit.Place(row.Name, gaugeLeft - 40, y, GaugeWidth + 80, 18);
                NativeUIKit.Align(row.Name, HorizontalAlignment.Center);
                NativeUIKit.Place(row.Gauge, gaugeLeft, y + 20, GaugeWidth, 22);
                NativeUIKit.Place(row.Left, 20, y + 17, gaugeLeft - 34, 28);
                NativeUIKit.Align(row.Left, HorizontalAlignment.Right);
                NativeUIKit.Place(row.Right, gaugeLeft + GaugeWidth + 14, y + 17, gaugeLeft - 34, 28);
                NativeUIKit.Align(row.Right, HorizontalAlignment.Left);
                // O rótulo doador ajusta a largura ao texto, o que anula o alinhamento: largura fixa.
                NativeUIKit.Label(row.Left).AutoAdjustWidth = false;
                NativeUIKit.Label(row.Right).AutoAdjustWidth = false;
                NativeUIKit.Place(row.Left, 20, y + 17, gaugeLeft - 34, 28);
                NativeUIKit.Place(row.Right, gaugeLeft + GaugeWidth + 14, y + 17, gaugeLeft - 34, 28);
                row.ValueGroup = row.Gauge.Find("ValueGroup");
                NativeUIKit.SetVisible(row.Gauge.Find("LockGroup"), false);
                rows.Add(row);
            }
            Plugin.Log.LogInfo("Painel de economia criado na tela de diplomacia.");
        }

        private void Refresh()
        {
            if (!CentralBankWindow.TryGetGameData(out GameSnapshot.Data game))
            {
                return;
            }
            int me = game.LocalEmpireInfo.EmpireIndex;
            int other = Snapshots.DiplomaticCursorSnapshot.PresentationData.OtherEmpireIndex;
            bool valid = other >= 0 && other < game.NumberOfMajorEmpires && other < game.EmpireInfo.Length && me < game.EmpireInfo.Length && other != me;
            NativeUIKit.SetVisible(panel, valid);
            if (!valid)
            {
                return;
            }

            CurrencyWorld world = CurrencyManager.Ensure(game.GameID, game.NumberOfMajorEmpires);
            lock (CurrencyManager.Lock)
            {
                EmpireCurrency mine = world.Get(me);
                EmpireCurrency theirs = world.Get(other);
                if (mine == null || theirs == null)
                {
                    return;
                }
                double rate = world.Rate(me, other);
                double myNet = (float)game.EmpireInfo[me].MoneyNet;
                double theirNet = (float)game.EmpireInfo[other].MoneyNet;

                NativeUIKit.SetText(title, L.T("Economia"));
                NativeUIKit.SetText(headerMine, L.T("Você"));
                NativeUIKit.SetText(headerTheirs, ShortEmpireName(other));
                string verdict = rate >= 1.02
                    ? L.T("Sua moeda vale mais: importar deles sai <b>barato</b>")
                    : rate <= 0.98
                        ? L.T("A moeda deles vale mais: importar deles sai <b>caro</b>")
                        : L.T("Moedas equivalentes");
                NativeUIKit.SetText(subtitle, $"<b>{theirs.Name} ({theirs.Symbol})</b>  ·  1 {mine.Symbol} = {Format.Rate(rate)} {theirs.Symbol}  ·  {verdict}");

                // Renda comparada em valor real (convertida pelo câmbio), exibida na moeda de cada um.
                SetRow(0, L.T("PIB estimado"), Format.Money(mine.Gdp), Format.Money(theirs.Gdp), Share(mine.Gdp, theirs.Gdp));
                SetRow(1, L.T("Renda por turno"), $"{Format.SignedMoney(myNet)} {mine.Symbol}", $"{Format.SignedMoney(theirNet)} {theirs.Symbol}",
                    Share(Math.Max(0, myNet) * mine.ExchangeValue, Math.Max(0, theirNet) * theirs.ExchangeValue));
                SetRow(2, L.T("Força da moeda"), $"{Format.Rate(mine.ExchangeValue)}×", $"{Format.Rate(theirs.ExchangeValue)}×", Share(mine.ExchangeValue, theirs.ExchangeValue));
                // Inflação e juros: barra mostra quem tem a economia mais estável / o dinheiro mais caro.
                SetRow(3, L.T("Inflação por turno"), Format.Percent(mine.InflationRate), Format.Percent(theirs.InflationRate),
                    Share(1.0 / (1.0 + Math.Max(0, mine.InflationRate) * 100), 1.0 / (1.0 + Math.Max(0, theirs.InflationRate) * 100)));
                SetRow(4, L.T("Juros por turno"), Format.Percent(mine.InterestRate), Format.Percent(theirs.InterestRate),
                    Share(mine.InterestRate, theirs.InterestRate));

                string[][] help =
                {
                    new[] { L.T("PIB estimado"), L.T("Tamanho da economia: renda, produção, ciência e influência somadas por turno. A barra mostra a sua fatia.") },
                    new[] { L.T("Renda por turno"), L.T("Dinheiro ganho por turno, cada um na sua moeda. A barra compara em valor real, já convertido pelo câmbio.") },
                    new[] { L.T("Força da moeda"), L.T("Valor da moeda comparado à média do mundo (1,00×). A mais forte compra mais da outra no comércio.") },
                    new[] { L.T("Inflação por turno"), L.T("Quanto os preços sobem por turno. Aqui a barra favorece quem tem a inflação mais baixa.") },
                    new[] { L.T("Juros por turno"), L.T("Juros do banco central. Juros altos seguram a inflação, mas esfriam a renda.") },
                };
                for (int i = 0; i < rows.Count && i < help.Length; i++)
                {
                    NativeUIKit.Tip(rows[i].Name, help[i][0], help[i][1]);
                    NativeUIKit.Tip(rows[i].Gauge, help[i][0], help[i][1]);
                }
                NativeUIKit.Tip(subtitle, L.T("Cotação"), L.F("Quanto 1 {0} compra de {1}. Moeda mais forte deixa as importações mais baratas.", mine.Name, theirs.PluralOrName));
            }
        }

        private static string ShortEmpireName(int empireIndex)
        {
            string name = CentralBankWindow.EmpireName(empireIndex);
            int open = name.LastIndexOf('(');
            int close = name.LastIndexOf(')');
            return open >= 0 && close > open ? name.Substring(open + 1, close - open - 1) : name;
        }

        private static double Share(double mine, double theirs)
        {
            double total = Math.Max(0, mine) + Math.Max(0, theirs);
            return total <= 1e-9 ? 0.5 : Math.Max(0, mine) / total;
        }

        private void SetRow(int index, string name, string mineText, string theirsText, double share)
        {
            Row row = rows[index];
            NativeUIKit.SetText(row.Name, name);
            NativeUIKit.SetText(row.Left, mineText);
            NativeUIKit.SetText(row.Right, theirsText);
            if (row.ValueGroup != null)
            {
                NativeUIKit.StyleGauge(row.ValueGroup, new Color(0.89f, 0.78f, 0.54f, 1f));
                // A parte preenchida vinha ancorada a 50% da barra: solta e define a largura.
                UITransform ui = row.ValueGroup.GetComponent<UITransform>();
                float width = Mathf.Clamp((float)share, 0.02f, 0.98f) * GaugeInnerWidth;
                if (Mathf.Abs(ui.Width - width) > 0.5f)
                {
                    NativeUIKit.Place(row.ValueGroup, 2f, 2f, width, 18f);
                }
            }
        }

        private void OnDestroy()
        {
            NativeUIKit.Dispose(panel);
            panel = null;
        }
    }
}
