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
    /// "Economy" panel in the Relations tab of the diplomacy screen: compares your economy with the
    /// selected empire's (GDP, income, currency strength, inflation and interest) and shows the exchange rate.
    /// Built from pieces of the screen itself: the "My Relations" title, the frosted box of the relations
    /// list, the "War Support" gauges and the rich text of the status sentence.
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
        /// <summary>Second box under the economy: the leader's doctrine, how they read you and the blocs (items 41, 47).</summary>
        private Transform policyPanel;
        private Transform policyTitle;
        private Transform policyText;
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
                // At the start of the game the windows service does not exist yet: the lookup would throw and the panel
                // would switch itself off until the next reload.
                if (WindowsUtils.WindowsService == null || !Diplomacy.GameAccess.TryGetSession(out _, out _, out _))
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
                    Plugin.Log.LogError($"Economy panel in diplomacy: {ex}");
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

            // Frosted box (the same as the relations list), without the scrolling.
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
                NativeUIKit.Place(child, 0, 0, PanelWidth, height); // the blur follows the box
            }

            title = NativeUIKit.Clone(titleDonor, panel, "Title");
            NativeUIKit.Place(title, 0, 4, PanelWidth, 32);
            NativeUIKit.Align(title, HorizontalAlignment.Center);
            NativeUIKit.Label(title).Color = new Color(1f, 0.875f, 0.584f); // gold of the section titles

            // The status sentence adjusts its own height; here the height is fixed.
            subtitle = NativeUIKit.Clone(sentenceDonor, panel, "Subtitle");
            NativeUIKit.Label(subtitle).AutoAdjustHeight = false;
            NativeUIKit.Place(subtitle, 20, 44, PanelWidth - 40, 44); // fits 2 lines (the exchange sentence is long)
            NativeUIKit.Align(subtitle, HorizontalAlignment.Center);

            // Column headers, aligned to the values on each side.
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
                // The donor label fits its width to the text, which cancels the alignment: fixed width.
                NativeUIKit.Label(row.Left).AutoAdjustWidth = false;
                NativeUIKit.Label(row.Right).AutoAdjustWidth = false;
                NativeUIKit.Place(row.Left, 20, y + 17, gaugeLeft - 34, 28);
                NativeUIKit.Place(row.Right, gaugeLeft + GaugeWidth + 14, y + 17, gaugeLeft - 34, 28);
                row.ValueGroup = row.Gauge.Find("ValueGroup");
                NativeUIKit.SetVisible(row.Gauge.Find("LockGroup"), false);
                rows.Add(row);
            }
            // Foreign policy box, right under the economy one.
            Transform leftoverPolicy = group.Find(PanelName + "_Policy");
            if (leftoverPolicy != null)
            {
                NativeUIKit.Dispose(leftoverPolicy);
            }
            const float policyHeight = 150f;
            policyPanel = NativeUIKit.Clone(boxDonor, group, PanelName + "_Policy", "UIScrollView", "ScrollingDragArea");
            Transform policyViewport = policyPanel.Find("Viewport");
            if (policyViewport != null)
            {
                DestroyImmediate(policyViewport.gameObject);
            }
            NativeUIKit.Place(policyPanel, left, top + height + 12f, PanelWidth, policyHeight);
            foreach (Transform child in policyPanel)
            {
                NativeUIKit.Place(child, 0, 0, PanelWidth, policyHeight);
            }
            policyTitle = NativeUIKit.Clone(titleDonor, policyPanel, "Title");
            NativeUIKit.Place(policyTitle, 0, 4, PanelWidth, 32);
            NativeUIKit.Align(policyTitle, HorizontalAlignment.Center);
            NativeUIKit.Label(policyTitle).Color = new Color(1f, 0.875f, 0.584f);
            policyText = NativeUIKit.Clone(sentenceDonor, policyPanel, "Text");
            NativeUIKit.Label(policyText).AutoAdjustHeight = false;
            NativeUIKit.Place(policyText, 24, 42, PanelWidth - 48, policyHeight - 50);
            NativeUIKit.Align(policyText, HorizontalAlignment.Left);
            Plugin.Log.LogInfo("Economy panel created on the diplomacy screen.");
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
            NativeUIKit.SetVisible(policyPanel, valid);
            if (!valid)
            {
                return;
            }
            RefreshPolicy(me, other);

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

                // Income compared in real value (converted by the exchange rate), shown in each one's currency.
                SetRow(0, L.T("PIB estimado"), Format.Money(mine.Gdp), Format.Money(theirs.Gdp), Share(mine.Gdp, theirs.Gdp));
                SetRow(1, L.T("Renda por turno"), $"{Format.SignedMoney(myNet)} {mine.Symbol}", $"{Format.SignedMoney(theirNet)} {theirs.Symbol}",
                    Share(Math.Max(0, myNet) * mine.ExchangeValue, Math.Max(0, theirNet) * theirs.ExchangeValue));
                SetRow(2, L.T("Força da moeda"), $"{Format.Rate(mine.ExchangeValue)}×", $"{Format.Rate(theirs.ExchangeValue)}×", Share(mine.ExchangeValue, theirs.ExchangeValue));
                // Inflation and interest: the gauge shows who has the more stable economy / the more expensive money.
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

        /// <summary>The leader's doctrine, how that AI reads you (its own view) and the blocs of both.</summary>
        private void RefreshPolicy(int me, int other)
        {
            if (policyPanel == null)
            {
                return;
            }
            NativeUIKit.SetText(policyTitle, L.T("Política externa"));
            Diplomacy.AiWorld ai = Diplomacy.AiModule.World;
            var lines = new List<string>();
            Diplomacy.NationDoctrines.Doctrine doctrine = ai != null && ai.Doctrines.TryGetValue(other, out string key) ? Diplomacy.NationDoctrines.ByKey(key) : null;
            lines.Add(doctrine != null
                ? L.F("<b>Doutrina dele:</b> {0}", L.T(doctrine.Name)) + " — " + L.T(DoctrineEssence(doctrine.Key))
                : L.T("<b>Doutrina dele:</b> ainda não definida (a IA decide no primeiro turno dela)."));
            Diplomacy.NationPerception view = null;
            ai?.Get(other)?.Perceptions.TryGetValue(me, out view);
            Diplomacy.NationDoctrines.Doctrine seen = Diplomacy.NationDoctrines.ByKey(view?.Doctrine);
            lines.Add(seen != null
                ? L.F("<b>Como ele te vê:</b> {0}", L.T(seen.Name)) + (string.IsNullOrEmpty(view.Note) ? string.Empty : " — <i>" + view.Note + "</i>")
                : L.T("<b>Como ele te vê:</b> ainda sem opinião formada."));
            Diplomacy.Blocs.Bloc mine = Diplomacy.Blocs.BlocEngine.Of(ai, me);
            Diplomacy.Blocs.Bloc theirs = Diplomacy.Blocs.BlocEngine.Of(ai, other);
            string blocs = theirs == null ? L.T("sem bloco")
                : mine != null && mine == theirs ? L.F("ambos em {0}", theirs.Name)
                : L.F("em {0}", theirs.Name) + (mine != null ? " · " + L.F("você em {0}", mine.Name) : string.Empty);
            lines.Add(L.F("<b>Blocos:</b> {0}", blocs));
            NativeUIKit.SetText(policyText, string.Join("\n", lines));
            NativeUIKit.Tip(policyText, L.T("Política externa"), L.T("A doutrina é o papel que esta nação busca no mundo (sorteada no começo da partida, ligada ao temperamento do líder). \"Como ele te vê\" é a leitura que a IA dele faz de você: só a opinião dela, pode estar errada, e cada nação te vê de um jeito."));
        }

        /// <summary>One-line essence of each doctrine, for the profile.</summary>
        private static string DoctrineEssence(string key)
        {
            switch (key)
            {
                case "isolationist": return L.N("o mundo é perigoso; prefere ficar fora de blocos e um mundo dividido");
                case "coalition": return L.N("segurança coletiva: funda blocos e cobra as regras de todos");
                case "regional": return L.N("quer mandar na própria vizinhança e não admite intrusos");
                case "faith": return L.N("a fé decide quem é amigo");
                case "kin": return L.N("só confia nos de cultura parecida");
                case "imperialist": return L.N("expandir é o destino; os fracos são alvo");
                case "mercantile": return L.N("lucro acima de bandeiras; odeia bloqueios");
                case "opportunist": return L.N("sem amigos permanentes, só interesses");
                case "satellite": return L.N("busca a proteção de um vizinho forte");
                case "neutral": return L.N("neutra e útil a todos, sem alianças militares");
                case "crusade": return L.N("quer espalhar o próprio modelo de governo");
                case "honor": return L.N("respeita a força; ofensa se paga");
                case "suzerain": return L.N("quer vassalos que paguem tributo");
            }
            return string.Empty;
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
                // The filled part came anchored at 50% of the gauge: unanchor it and set the width.
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
            NativeUIKit.Dispose(policyPanel);
            policyPanel = null;
        }
    }
}
