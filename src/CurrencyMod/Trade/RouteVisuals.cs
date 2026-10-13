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
    /// Trade routes with color, thickness and value (request from 2026-10-10, docs\melhorias-jogando.md item 8). The game
    /// draws all routes the same (PresentationTradeRoadController, one style per situation: normal, interrupted, destroyed).
    /// Here:
    /// - on the sandbox thread (postfix of TradePathSnapshot.ComputePathRendering), each external line gets its situation
    ///   relative to the local player, the goods it carries, the toll that applies to the player and the mid-path tile;
    /// - in the presentation (postfix of TradePathSnapshot_PresentationChanged), the line switches to a mod style: a copy of
    ///   the game's normal style with another color and another width;
    /// - a "£12/t" label in the middle of each route that crosses the player's land (RouteLabels, only in the trade view).
    /// Colors: gold = pays your toll; white = passes free through your land; orange = your route that pays a toll to
    /// someone; blue = your route; the others stay as the game draws them. Thickness: the more resources, the thicker.
    /// </summary>
    internal static class RouteVisuals
    {
        internal enum Kind { Other, PaysMe, FreeOnMe, MinePays, Mine }

        internal struct Info
        {
            public bool External;
            public Kind Kind;
            public int Goods;
            /// <summary>Per-turn toll that applies to the player: charged (PaysMe) or that would be charged at the current price (FreeOnMe).</summary>
            public double Value;
            public int MidTile;
            /// <summary>Grand route (TradeNetwork): drawn thick and golden, with its name.</summary>
            public bool Grand;
        }

        private sealed class Lines
        {
            public Info[] Items = new Info[0];
            /// <summary>Grand route names and trade hubs the local player can see (built on the sandbox thread).</summary>
            public List<Label> Extra = new List<Label>();
        }

        /// <summary>
        /// The data of each snapshot buffer (the game alternates two Data objects between sandbox and presentation).
        /// A plain reference-keyed cache, not a ConditionalWeakTable: after a core hot reload Mono's weak tables of the
        /// two generations got mixed (InvalidCastException on TryGetValue) and the game crashed right after.
        /// </summary>
        private static readonly object tableLock = new object();
        private static readonly List<KeyValuePair<TradePathSnapshot.Data, Lines>> table = new List<KeyValuePair<TradePathSnapshot.Data, Lines>>();

        private static Lines LinesFor(TradePathSnapshot.Data data, bool create)
        {
            lock (tableLock)
            {
                foreach (KeyValuePair<TradePathSnapshot.Data, Lines> pair in table)
                {
                    if (ReferenceEquals(pair.Key, data))
                    {
                        return pair.Value;
                    }
                }
                if (!create)
                {
                    return null;
                }
                var lines = new Lines();
                table.Add(new KeyValuePair<TradePathSnapshot.Data, Lines>(data, lines));
                if (table.Count > 4)
                {
                    table.RemoveAt(0); // a new game or save: the old buffers are gone
                }
                return lines;
            }
        }

        /// <summary>A value label: the route's middle tile, text, and whether the route already pays (gold) or only would pay.</summary>
        internal sealed class Label
        {
            public int Tile;
            public string Text;
            public bool Paid;
            public LabelKind Kind;
            /// <summary>Second line under the text (grand route: what it is; hub: level and routes).</summary>
            public string Subtitle;
            /// <summary>Hover panel (hubs and grand routes): buffs, debuffs and why.</summary>
            public string Detail;
            /// <summary>Hub: its level. Grand route: the route's middle tile (to know whether the view shows it).</summary>
            public int Level;
        }

        internal enum LabelKind { Toll, Grand, Hub }

        /// <summary>Labels from the last presentation (read by RouteLabels on the main thread).</summary>
        internal static volatile List<Label> Labels = new List<Label>();

        internal static bool Enabled => EconomyConfig.RouteColors.Value && !SavePatches.IsGameOnline();

        /// <summary>Test ("game routes colors E5"): paints as if the player were another empire (-1 = the local player).</summary>
        internal static volatile int ViewAs = -1;

        /// <summary>Test ("game routes colors all"): label on every colored route, to check the position.</summary>
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
                    Plugin.Log.LogWarning($"Route colors: {ex}");
                }
            }
        }

        private static void Compute(TradePathSnapshot.Data data)
        {
            Lines lines = LinesFor(data, create: true);
            int count = data.PathRenderingLines.Length;
            if (lines.Items.Length != count)
            {
                lines.Items = new Info[count];
            }
            Array.Clear(lines.Items, 0, count);
            int local = ViewAs >= 0 ? ViewAs : SandboxManager.Sandbox?.LocalEmpireIndex ?? -1;
            TradeNetwork.Snapshot network = TradeNetwork.Enabled ? TradeNetwork.Refresh() : new TradeNetwork.Snapshot();
            lines.Extra = TradeNetworkLabels.Build(network, local);
            ExternalTradeRelation[] relations = Sandbox.TradeController.ExternalTradeRelations;
            int line = 0;
            for (int r = 0; r < relations.Length && line < count; r++)
            {
                ExternalTradeRelation relation = relations[r];
                if (relation == null)
                {
                    continue; // same order as ComputePathRendering: only existing relations become a line
                }
                lines.Items[line] = Describe(relation, local);
                lines.Items[line].Grand = relation.TradeRoadStatus == TradeRoadStatus.Active
                    && network.GrandPairs.Contains((relation.LeftEmpireIndex << 16) | relation.RightEmpireIndex);
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
                        // The owner charges once per route, at its most expensive post along the path.
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

        // ---------------- Presentation ----------------

        /// <summary>Relative width per goods tier (1-2, 3-5, 6+ resources).</summary>
        private static readonly float[] WidthTiers = { 0.8f, 1.25f, 1.8f };

        private static readonly Color[] KindColors =
        {
            Color.white,                              // Other (unused: the game's style stays)
            new Color(0.95f, 0.78f, 0.36f),           // PaysMe: gold
            new Color(1f, 1f, 1f),                    // FreeOnMe: white
            new Color(0.95f, 0.55f, 0.25f),           // MinePays: orange
            new Color(0.45f, 0.72f, 0.95f),           // Mine: blue
        };

        /// <summary>The controller that already has the mod styles, and the index of the first one (the game's come first).</summary>
        private static PresentationTradeRoadController styledController;
        private static int styledFirst = -1;

        /// <summary>The grand route style comes after all kind × width styles.</summary>
        private static int GrandStyleOffset => (KindColors.Length - 1) * WidthTiers.Length;

        internal static readonly Color GrandColor = new Color(1f, 0.8f, 0.25f);

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
                    Plugin.Log.LogWarning($"Route colors (presentation): {ex}");
                }
            }
        }

        private static void Present(PresentationTradeRoadController controller)
        {
            TradePathSnapshot.Data data = Snapshots.TradePathSnapshot.PresentationData;
            WorldSpaceLinesWithSteps lines = controller.worldSpaceLinesWithSteps;
            Lines computed = data != null ? LinesFor(data, create: false) : null;
            if (lines == null || computed == null)
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
            var grandVisible = new HashSet<int>();
            for (int i = 0; i < lines.LinesCount && i < computed.Items.Length; i++)
            {
                Info info = computed.Items[i];
                WorldSpaceLinesWithSteps.Line line = lines.Lines[i];
                // Only touches lines in the normal style (interrupted and destroyed ones stay as the game shows them).
                if (!info.External || !line.IsVisible || line.StyleIndex != defaultStyle)
                {
                    continue;
                }
                if (info.Grand)
                {
                    // Grand routes stand out above everything: one style, thick and golden, whatever their situation with you.
                    lines.Lines[i].StyleIndex = firstModStyle + GrandStyleOffset;
                    if (info.MidTile >= 0)
                    {
                        grandVisible.Add(info.MidTile);
                    }
                }
                if (info.Kind == Kind.Other)
                {
                    continue;
                }
                if (!info.Grand)
                {
                    lines.Lines[i].StyleIndex = firstModStyle + ((int)info.Kind - 1) * WidthTiers.Length + Tier(info.Goods);
                }
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
            // Grand route names (only routes the current view shows) and the hubs the player knows.
            foreach (Label extra in computed.Extra)
            {
                if (extra.Kind != LabelKind.Grand || grandVisible.Contains(extra.Level))
                {
                    labels.Add(extra);
                }
            }
            Labels = labels;
        }

        /// <summary>
        /// Puts the mod styles right after the game's (always at the same position: reloading the core does not pile up
        /// styles, UpdateStyles only rewrites the ones that already exist). Returns the index of the first.
        /// </summary>
        private static int EnsureStyles(PresentationTradeRoadController controller, WorldSpaceLinesWithSteps lines)
        {
            if (ReferenceEquals(styledController, controller) && styledFirst >= 0)
            {
                return styledFirst;
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
            WorldSpaceLineRenderer.Style grand = template;
            grand.UseCustomColor = true;
            grand.Color = GrandColor;
            grand.UseCustomGlowColor = true;
            grand.GlowColor = new Color(1f, 0.62f, 0.12f, 0.95f);
            grand.Width = Mathf.Clamp(template.Width * 2.8f, 0.5f, 20f);
            all.Add(grand);
            lines.UpdateStyles(all.ToArray());
            styledController = controller;
            styledFirst = firstMod;
            Plugin.Log.LogInfo($"Route colors: {all.Count - firstMod} styles added (base width {template.Width:0.0}, game's own custom color {template.UseCustomColor}).");
            return firstMod;
        }
    }
}
