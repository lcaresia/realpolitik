using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod
{
    /// <summary>
    /// Texts the route map shows for the trade network (sandbox thread, inside RouteVisuals.Compute): the name of each
    /// grand route and a badge on each trade hub, with a hover panel that explains what the hub gains and what it risks.
    /// Fog of war: only what the local player has explored; the stability-driven risks only for the player's own cities.
    /// </summary>
    internal static class TradeNetworkLabels
    {
        internal static List<RouteVisuals.Label> Build(TradeNetwork.Snapshot network, int local)
        {
            var labels = new List<RouteVisuals.Label>();
            if (local < 0 || !TradeNetwork.Enabled)
            {
                return labels;
            }
            VisibilityController visibility = Sandbox.VisibilityController;
            bool Explored(int tile) => tile >= 0 && (visibility == null || visibility.IsWorldPositionExploredFor(tile, local));
            foreach (TradeNetwork.GrandRoute route in network.GrandRoutes)
            {
                // The name sits on the explored stretch of the route nearest its middle (never in the fog).
                // The two ends are cities: the name goes on the road between them whenever any of it is explored.
                int middle = route.PathTiles.Count / 2;
                int last = route.PathTiles.Count - 1;
                int tile = route.PathTiles.Select((t, i) => (t, i)).Where(p => Explored(p.t))
                    .OrderBy(p => p.i == 0 || p.i == last ? 1 : 0).ThenBy(p => Math.Abs(p.i - middle)).Select(p => p.t).DefaultIfEmpty(-1).First();
                if (tile < 0)
                {
                    continue;
                }
                labels.Add(new RouteVisuals.Label
                {
                    Kind = RouteVisuals.LabelKind.Grand,
                    Tile = tile,
                    Level = route.MidTile,
                    // Only the title on the map (melhorias item 49); the two ends and the numbers in the legend and the hover.
                    Text = route.Title ?? route.Name,
                    Subtitle = route.Name,
                    Detail = GrandDetail(route, local),
                });
            }
            foreach (TradeNetwork.Hub hub in network.Hubs)
            {
                bool mine = hub.Empire == local;
                if ((!mine && hub.Level < 2) || !Explored(hub.Tile))
                {
                    continue;
                }
                // Your hubs carry text; other nations' only a small level mark, with everything in the hover (item 37).
                labels.Add(new RouteVisuals.Label
                {
                    Kind = RouteVisuals.LabelKind.Hub,
                    Tile = hub.Tile,
                    Level = hub.Level,
                    Paid = mine,
                    Text = mine ? TradeNetwork.LevelName(hub.Level) : new string('◆', Math.Max(1, hub.Level)),
                    Subtitle = mine ? L.P(hub.Routes, "{1} · {0} rota", "{1} · {0} rotas", hub.Name) : null,
                    Detail = HubDetail(hub, mine),
                });
            }
            return labels;
        }

        private static string Name(int empire)
        {
            try
            {
                if (empire >= 0 && empire < Sandbox.NumberOfMajorEmpires)
                {
                    return NativeUI.NativeBankWindow.ShortEmpireName(empire);
                }
            }
            catch (Exception)
            {
            }
            return "E" + empire;
        }

        private static string GrandDetail(TradeNetwork.GrandRoute route, int local)
        {
            var lines = new List<string>
            {
                route.Name,
                L.F("Liga {0} a {1}, cruzando {2} territórios de {3} nações e povos.", Name(route.Left), Name(route.Right), route.Territories, route.Nations),
            };
            string goods = string.Join(", ", route.GoodsTypes.Select(t => Diplomacy.GameGlossary.Resource(t)?.Name).Where(n => n != null));
            if (goods.Length > 0)
            {
                lines.Add(L.F("Leva: {0}.", goods));
            }
            if (route.HubNames.Count > 0)
            {
                lines.Add(L.F("Entrepostos no caminho: {0}.", string.Join(", ", route.HubNames)));
            }
            lines.Add(L.T("As duas pontas ganham fama todo turno, e as cidades no caminho contam esta rota em dobro."));
            if (route.Left == local || route.Right == local)
            {
                lines.Add(L.T("Você está numa das pontas desta rota."));
            }
            else if (route.Transit.Contains(local))
            {
                lines.Add(L.T("Ela passa pelas suas terras: você pode cobrar pedágio ou bloqueá-la."));
            }
            return string.Join("\n", lines);
        }

        private static string Percent(double value) => Math.Round(value * 100) + "%";

        private static string HubDetail(TradeNetwork.Hub hub, bool mine)
        {
            TradeHubs.Effects effects = hub.Effects;
            var lines = new List<string>
            {
                $"{hub.Name} · {TradeNetwork.LevelName(hub.Level)}",
                hub.GrandRoutes > 0
                    ? (hub.Routes == 1 ? L.T("1 rota passa por aqui: é uma grande rota.")
                        : L.P(hub.GrandRoutes, "{1} rotas passam por aqui, {0} delas grande rota.", "{1} rotas passam por aqui, {0} delas grandes rotas.", hub.Routes))
                    : L.P(hub.Routes, "{0} rota passa por aqui.", "{0} rotas passam por aqui."),
            };
            var gains = new List<string> { L.F("+{0} dinheiro", Format.Money(effects.Money)) };
            if (effects.GrowthPercent > 0)
            {
                gains.Add(L.F("+{0} crescimento", Percent(effects.GrowthPercent)));
            }
            if (effects.Influence > 0)
            {
                gains.Add(L.F("+{0} influência", Format.Money(effects.Influence)));
            }
            if (effects.SciencePercent > 0)
            {
                gains.Add(L.F("+{0} ciência", Percent(effects.SciencePercent)));
            }
            lines.Add(L.T("Ganhos por turno:") + " " + string.Join(" · ", gains));
            if (!mine)
            {
                return string.Join("\n", lines);
            }

            // Risks: what the level exposes, and how much the city's own state lets in.
            var risks = new List<string>();
            MajorEmpire empire = hub.Empire < Sandbox.NumberOfMajorEmpires ? Sandbox.MajorEmpires[hub.Empire] : null;
            if (hub.Level >= 2)
            {
                bool debt = empire != null && (float)empire.MoneyStock.Value < 0;
                double factor = TradeHubs.DebtInflationFactor(hub.Empire) - 1;
                risks.Add(debt
                    ? L.F("Custo de vida: com dívida, a inflação da dívida sobe {0} pelos seus entrepostos.", Percent(factor))
                    : L.T("Custo de vida: nenhum (tesouro sem dívida)."));
            }
            if (hub.Level >= 3)
            {
                risks.Add(effects.SpyShield > 0.005
                    ? L.F("Espiões: os estrangeiros aqui gastam {0} menos do disfarce por turno (estabilidade {1}; a partir de 80, nada).", Percent(effects.SpyShield), Math.Round(hub.Stability))
                    : L.T("Espiões: nenhuma vantagem para eles (cidade estável)."));
            }
            if (hub.Level >= 4)
            {
                risks.Add(effects.CrimeLoss > 0.05
                    ? L.F("Crime: -{0} de estabilidade (estabilidade {1}; a partir de 80, nada).", effects.CrimeLoss.ToString("0.#"), Math.Round(hub.Stability))
                    : L.T("Crime: nenhum (cidade estável)."));
                risks.Add(effects.MinorityLoss > 0.05
                    ? L.F("Minorias estrangeiras: -{0} de estabilidade ({1} dos territórios seguem outra fé ou cultura).", effects.MinorityLoss.ToString("0.#"), Percent(hub.ForeignShare))
                    : L.T("Minorias: nenhuma tensão (territórios da sua fé e cultura)."));
                risks.Add(effects.EpidemicChance > 0.0005
                    ? L.F("Epidemia: {0} de chance por turno (cidade instável ou com fome).", (effects.EpidemicChance * 100).ToString("0.#") + "%")
                    : L.T("Epidemia: quase impossível (cidade estável e bem alimentada)."));
            }
            if (TradeHubs.SupplyCrisis(hub.Empire, out string what))
            {
                risks.Add(L.F("Crise de abastecimento: cortaram a rota do {0}.", what));
            }
            if (risks.Count > 0)
            {
                lines.Add(L.T("Riscos:"));
                lines.AddRange(risks.Select(r => "· " + r));
            }
            return string.Join("\n", lines);
        }
    }
}
