using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Amplitude.Mercury;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Presentation;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using Amplitude.Mercury.Terrain;
using HarmonyLib;
using UnityEngine;
using Math = System.Math;
using Territory = Amplitude.Mercury.Simulation.Territory;

namespace CurrencyMod
{
    /// <summary>
    /// Rotas comerciais com cor, espessura e valor (pedido de 2026-10-10, docs\melhorias-jogando.md item 8). O jogo desenha
    /// todas as rotas iguais (PresentationTradeRoadController, um estilo por situação: normal, interrompida, destruída).
    /// Aqui:
    /// - na thread do sandbox (pós-fixo de TradePathSnapshot.ComputePathRendering), cada linha externa ganha a situação em
    ///   relação ao jogador local, a mercadoria que passa, o pedágio que vale para ele e o tile do meio do caminho;
    /// - na apresentação (pós-fixo de TradePathSnapshot_PresentationChanged), a linha troca para um estilo do mod: cópia do
    ///   estilo normal do jogo com outra cor e outra largura;
    /// - um rótulo "£12/t" no meio de cada rota que passa pelas terras do jogador (RouteLabels, só na visão de comércio).
    /// Cores: dourado = paga o seu pedágio; branco = passa livre pelas suas terras; laranja = rota sua que paga pedágio a
    /// alguém; azul = rota sua; as outras ficam como o jogo desenha. Espessura: quanto mais recursos, mais grossa.
    /// </summary>
    internal static class RouteVisuals
    {
        internal enum Kind { Other, PaysMe, FreeOnMe, MinePays, Mine }

        internal struct Info
        {
            public bool External;
            public Kind Kind;
            public int Goods;
            /// <summary>Pedágio por turno que vale para o jogador: cobrado (PaysMe) ou que cobraria no preço atual (FreeOnMe).</summary>
            public double Value;
            public int MidTile;
        }

        private sealed class Lines
        {
            public Info[] Items = new Info[0];
        }

        /// <summary>Os dados de cada buffer do snapshot (o jogo alterna dois objetos Data entre sandbox e apresentação).</summary>
        private static readonly ConditionalWeakTable<TradePathSnapshot.Data, Lines> table = new ConditionalWeakTable<TradePathSnapshot.Data, Lines>();

        /// <summary>Um rótulo de valor: tile do meio da rota, texto e se a rota já paga (dourado) ou só pagaria.</summary>
        internal sealed class Label
        {
            public int Tile;
            public string Text;
            public bool Paid;
        }

        /// <summary>Rótulos da última apresentação (lidos pelo RouteLabels na thread principal).</summary>
        internal static volatile List<Label> Labels = new List<Label>();

        internal static bool Enabled => EconomyConfig.RouteColors.Value && !SavePatches.IsGameOnline();

        /// <summary>Teste ("jogo rotas cores E5"): pinta como se o jogador fosse outro império (-1 = o jogador local).</summary>
        internal static volatile int ViewAs = -1;

        /// <summary>Teste ("jogo rotas cores todas"): rótulo em toda rota colorida, para conferir a posição.</summary>
        internal static volatile bool LabelAll;

        // ---------------- Sandbox ----------------

        [HarmonyPatch(typeof(TradePathSnapshot), nameof(TradePathSnapshot.ComputePathRendering))]
        private static class ComputePatch
        {
            private static void Postfix(TradePathSnapshot.Data simulationData)
            {
                if (!Enabled || simulationData?.PathRenderingLines == null)
                {
                    return;
                }
                try
                {
                    Compute(simulationData);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Cores das rotas: {ex}");
                }
            }
        }

        private static void Compute(TradePathSnapshot.Data data)
        {
            Lines lines = table.GetValue(data, _ => new Lines());
            int count = data.PathRenderingLines.Length;
            if (lines.Items.Length != count)
            {
                lines.Items = new Info[count];
            }
            Array.Clear(lines.Items, 0, count);
            int local = ViewAs >= 0 ? ViewAs : SandboxManager.Sandbox?.LocalEmpireIndex ?? -1;
            ExternalTradeRelation[] relations = Sandbox.TradeController.ExternalTradeRelations;
            int line = 0;
            for (int r = 0; r < relations.Length && line < count; r++)
            {
                ExternalTradeRelation relation = relations[r];
                if (relation == null)
                {
                    continue; // mesma ordem do ComputePathRendering: só as relações que existem viram linha
                }
                lines.Items[line] = Describe(relation, local);
                line++;
            }
        }

        private static Info Describe(ExternalTradeRelation relation, int local)
        {
            var info = new Info { External = true, Kind = Kind.Other, MidTile = -1 };
            if (relation.TradeRoadStatus != TradeRoadStatus.Active || local < 0)
            {
                return info;
            }
            int left = relation.LeftEmpireIndex;
            int right = relation.RightEmpireIndex;
            info.Goods = TradeBlockade.ResourcesBought(relation.LeftTradeExchange.Entity) + TradeBlockade.ResourcesBought(relation.RightTradeExchange.Entity);
            int nodes = relation.PathAsPathNode.Length;
            if (nodes > 0)
            {
                int territoryNode = relation.PathAsPathNode.Data[nodes / 2].TerritoryNodeIndex;
                info.MidTile = Sandbox.TerritoryPathfindManager.TerritoryNodes[territoryNode].GetCurrentTradeTileIndex();
            }
            bool mine = left == local || right == local;
            double paysMe = 0, wouldPay = 0;
            bool crossesMe = false, iPay = false;
            for (int i = 0; i < relation.PathAsTerritories.Count; i++)
            {
                Territory territory = relation.PathAsTerritories[i];
                MajorEmpire owner = territory != null ? TradeBlockade.OwnerOf(territory.Index) : null;
                if (owner == null || owner.Index == left || owner.Index == right)
                {
                    continue;
                }
                int era = Math.Max(1, owner.DepartmentOfDevelopment.CurrentEraIndex);
                TradeMode mode = TradeBlockade.ModeFor(territory.Index, owner.Index, left, right);
                if (owner.Index == local)
                {
                    crossesMe = true;
                    foreach (int payer in new[] { left, right })
                    {
                        if (payer < 0 || payer >= Sandbox.NumberOfMajorEmpires)
                        {
                            continue;
                        }
                        // O dono cobra uma vez por rota, pelo posto mais caro dele no caminho.
                        double price = TradePolicy.GetPrice(territory.Index, payer, local, era) * info.Goods;
                        if (TradePolicy.GetMode(territory.Index, payer, local) == TradeMode.Toll)
                        {
                            paysMe = Math.Max(paysMe, price);
                        }
                        else
                        {
                            wouldPay = Math.Max(wouldPay, TradePolicy.GeneralPriceOrDefault(local, payer, era) * info.Goods);
                        }
                    }
                }
                else if (mine && mode == TradeMode.Toll && TradePolicy.GetMode(territory.Index, local, owner.Index) == TradeMode.Toll)
                {
                    iPay = true;
                }
            }
            if (mine)
            {
                info.Kind = iPay ? Kind.MinePays : Kind.Mine;
            }
            else if (crossesMe)
            {
                info.Kind = paysMe > 0 ? Kind.PaysMe : Kind.FreeOnMe;
                info.Value = paysMe > 0 ? paysMe : wouldPay;
            }
            return info;
        }

        // ---------------- Apresentação ----------------

        /// <summary>Largura relativa por faixa de mercadoria (1-2, 3-5, 6+ recursos).</summary>
        private static readonly float[] WidthTiers = { 0.8f, 1.25f, 1.8f };

        private static readonly Color[] KindColors =
        {
            Color.white,                              // Other (não usado: fica o estilo do jogo)
            new Color(0.95f, 0.78f, 0.36f),           // PaysMe: dourado
            new Color(1f, 1f, 1f),                    // FreeOnMe: branco
            new Color(0.95f, 0.55f, 0.25f),           // MinePays: laranja
            new Color(0.45f, 0.72f, 0.95f),           // Mine: azul
        };

        /// <summary>Índice do primeiro estilo do mod no controlador (os do jogo vêm antes).</summary>
        private static readonly ConditionalWeakTable<PresentationTradeRoadController, object> styled = new ConditionalWeakTable<PresentationTradeRoadController, object>();

        private static int Tier(int goods) => goods >= 6 ? 2 : goods >= 3 ? 1 : 0;

        [HarmonyPatch(typeof(PresentationTradeRoadController), nameof(PresentationTradeRoadController.TradePathSnapshot_PresentationChanged))]
        private static class PresentPatch
        {
            private static void Postfix(PresentationTradeRoadController __instance)
            {
                if (!Enabled)
                {
                    Labels = new List<Label>();
                    return;
                }
                try
                {
                    Present(__instance);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Cores das rotas (apresentação): {ex}");
                }
            }
        }

        private static void Present(PresentationTradeRoadController controller)
        {
            TradePathSnapshot.Data data = Snapshots.TradePathSnapshot.PresentationData;
            WorldSpaceLinesWithSteps lines = controller.worldSpaceLinesWithSteps;
            if (data == null || lines == null || !table.TryGetValue(data, out Lines computed))
            {
                return;
            }
            int firstModStyle = EnsureStyles(controller, lines);
            if (firstModStyle < 0)
            {
                return;
            }
            int defaultStyle = controller.tradeRoadStyleCriteriaToStyleIndex[TradeRoadSettings.TradeRoadStyle.StyleCriteria.Default];
            var labels = new List<Label>();
            string symbol = CurrencyManager.Current?.Get(SandboxManager.Sandbox?.LocalEmpireIndex ?? -1)?.Symbol ?? "$";
            for (int i = 0; i < lines.LinesCount && i < computed.Items.Length; i++)
            {
                Info info = computed.Items[i];
                WorldSpaceLinesWithSteps.Line line = lines.Lines[i];
                // Só mexe nas linhas no estilo normal (interrompidas e destruídas ficam como o jogo mostra).
                if (!info.External || info.Kind == Kind.Other || !line.IsVisible || line.StyleIndex != defaultStyle)
                {
                    continue;
                }
                lines.Lines[i].StyleIndex = firstModStyle + ((int)info.Kind - 1) * WidthTiers.Length + Tier(info.Goods);
                if (LabelAll && info.MidTile >= 0)
                {
                    labels.Add(new Label { Tile = info.MidTile, Text = info.Kind + " · " + info.Goods, Paid = info.Kind == Kind.PaysMe });
                }
                else if ((info.Kind == Kind.PaysMe || info.Kind == Kind.FreeOnMe) && info.MidTile >= 0 && info.Value > 0)
                {
                    string text = symbol + " " + Format.Money(info.Value) + "/t" + (info.Kind == Kind.FreeOnMe ? "?" : string.Empty);
                    labels.Add(new Label { Tile = info.MidTile, Text = text, Paid = info.Kind == Kind.PaysMe });
                }
            }
            lines.Apply();
            Labels = labels;
        }

        /// <summary>
        /// Põe os estilos do mod logo depois dos do jogo (sempre na mesma posição: recarregar o núcleo não acumula estilos,
        /// o UpdateStyles só reescreve os que já existem). Devolve o índice do primeiro.
        /// </summary>
        private static int EnsureStyles(PresentationTradeRoadController controller, WorldSpaceLinesWithSteps lines)
        {
            if (styled.TryGetValue(controller, out object first) && first is int known)
            {
                return known;
            }
            TradeRoadSettings.TradeRoadStyle[] gameStyles = controller.loadedTradeRoadSettings?.GetStyles();
            if (gameStyles == null || gameStyles.Length == 0)
            {
                return -1;
            }
            int baseIndex = controller.tradeRoadStyleCriteriaToStyleIndex[TradeRoadSettings.TradeRoadStyle.StyleCriteria.Default];
            WorldSpaceLineRenderer.Style template = gameStyles[baseIndex].LineStyle;
            var all = new List<WorldSpaceLineRenderer.Style>();
            foreach (TradeRoadSettings.TradeRoadStyle gameStyle in gameStyles)
            {
                all.Add(gameStyle.LineStyle);
            }
            int firstMod = all.Count;
            for (int kind = 1; kind < KindColors.Length; kind++)
            {
                foreach (float width in WidthTiers)
                {
                    WorldSpaceLineRenderer.Style style = template;
                    style.UseCustomColor = true;
                    style.Color = KindColors[kind];
                    style.UseCustomGlowColor = true;
                    style.GlowColor = new Color(KindColors[kind].r, KindColors[kind].g, KindColors[kind].b, 0.6f);
                    style.Width = Mathf.Clamp(template.Width * width, 0.5f, 20f);
                    all.Add(style);
                }
            }
            lines.UpdateStyles(all.ToArray());
            styled.Add(controller, firstMod);
            Plugin.Log.LogInfo($"Cores das rotas: {all.Count - firstMod} estilos acrescentados (largura base {template.Width:0.0}, cor própria do jogo {template.UseCustomColor}).");
            return firstMod;
        }
    }
}
