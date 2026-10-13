using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Presentation;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using UnityEngine;

namespace CurrencyMod
{
    /// <summary>
    /// Labels of the trade view, drawn over the route map:
    /// - toll value in the middle of each route that crosses the player's land: "£ 12/t" in gold when the route already
    ///   pays the player's toll, "£ 12/t?" in white when it passes free (what it would pay at the current price);
    /// - grand routes: the route's name in large gold letters with a line saying what it is (TradeNetwork);
    /// - trade hubs: a badge with the level over the city, gold for the player's own; hovering shows what the hub gains
    ///   and risks (TradeNetworkLabels).
    /// The position uses the same math as the game's markers (tile world position → camera). Drawn with IMGUI: the game's
    /// lines are painted on the GPU, with no marker of their own to hang text on. Also shows the trade network's notices
    /// to the player (epidemics) on the main thread.
    /// </summary>
    internal sealed class RouteLabels : MonoBehaviour
    {
        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.36f);
        private static readonly Color Light = new Color(0.93f, 0.93f, 0.93f);
        private static readonly Color Muted = new Color(0.78f, 0.80f, 0.84f);

        private GUIStyle style;
        private GUIStyle grandTitle;
        private GUIStyle grandSubtitle;
        private GUIStyle hubTitle;
        private GUIStyle hubSubtitle;
        private GUIStyle hubMark;
        private GUIStyle legend;
        private GUIStyle panel;
        private Texture2D background;
        private Texture2D grandBackground;
        private Texture2D border;
        private Texture2D panelBackground;
        private float scale = -1f;
        private readonly List<Rect> taken = new List<Rect>();
        private Font serif;
        private Font sans;

        /// <summary>Last frame (dev "game trade labels"): in the trade view, labels drawn, labels off screen or under a panel.</summary>
        internal static volatile bool InView;
        /// <summary>Test ("game trade hover &lt;text&gt;"): shows the hover panel of the label containing this text, without the mouse.</summary>
        internal static volatile string ForcedHover;
        internal static volatile int Drawn;
        internal static volatile int Skipped;

        private static bool InTradeView()
        {
            return Presentation.PresentationCursorController?.CurrentCursor is TradeViewCursor;
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void EnsureStyles()
        {
            float now = Screen.height / 1080f;
            if (style != null && Mathf.Approximately(now, scale))
            {
                return;
            }
            scale = now;
            if (serif == null)
            {
                // The game draws its text with its own renderer: a serif close to its titles, and its plain sans for details.
                serif = Font.CreateDynamicFontFromOSFont(new[] { "Palatino Linotype", "Georgia", "Times New Roman" }, 16);
                sans = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 14);
            }
            if (background == null)
            {
                background = Solid(new Color(0.07f, 0.09f, 0.13f, 0.82f));
                grandBackground = Solid(new Color(0.10f, 0.07f, 0.02f, 0.88f));
                border = Solid(Gold);
                panelBackground = Solid(new Color(0.06f, 0.08f, 0.12f, 0.97f));
            }
            GUIStyle Make(int size, FontStyle font, RectOffset padding, Texture2D back, Font face = null)
            {
                var made = new GUIStyle(GUI.skin.label)
                {
                    font = face ?? sans,
                    fontSize = Mathf.RoundToInt(size * now),
                    fontStyle = font,
                    alignment = TextAnchor.MiddleCenter,
                    padding = padding,
                    wordWrap = false,
                    richText = false,
                };
                made.normal.background = back;
                return made;
            }
            style = Make(15, FontStyle.Bold, new RectOffset(8, 8, 3, 3), background);
            // Smaller than before (melhorias items 37 and 49): the grand route names covered cities and posts.
            grandTitle = Make(15, FontStyle.Bold, new RectOffset(9, 9, 3, 2), null, serif);
            grandSubtitle = Make(11, FontStyle.Normal, new RectOffset(9, 9, 0, 3), null);
            hubTitle = Make(12, FontStyle.Bold, new RectOffset(7, 7, 2, 0), null, serif);
            hubSubtitle = Make(10, FontStyle.Normal, new RectOffset(7, 7, 0, 2), null);
            hubMark = Make(11, FontStyle.Normal, new RectOffset(5, 5, 1, 1), background);
            legend = Make(12, FontStyle.Normal, new RectOffset(10, 10, 6, 6), null);
            legend.alignment = TextAnchor.UpperLeft;
            legend.wordWrap = true;
            legend.normal.textColor = Light;
            panel = Make(13, FontStyle.Normal, new RectOffset(12, 12, 9, 9), null);
            panel.alignment = TextAnchor.UpperLeft;
            panel.wordWrap = true;
            panel.normal.textColor = Light;
        }

        private void Update()
        {
            // Trade network notices (epidemics) for the player, one at a time.
            if (TradeHubs.PendingNotices.TryDequeue(out string notice))
            {
                Diplomacy.AiModule.ShowNotice(notice);
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !RouteVisuals.Enabled)
            {
                return;
            }
            List<RouteVisuals.Label> labels = RouteVisuals.Labels;
            InView = InTradeView();
            if (labels == null || labels.Count == 0 || !InView)
            {
                return;
            }
            Camera camera = Presentation.PresentationTradeRoadController?.cameraGraphicService?.Camera;
            if (camera == null || Presentation.PresentationWorldController == null)
            {
                return;
            }
            try
            {
                EnsureStyles();
                RefreshPanels();
                Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                int drawn = 0, skipped = 0;
                taken.Clear();
                string hovered = null;
                Rect hoveredRect = default;
                // Priority (melhorias item 37): your tolls, then grand routes, then your hubs, then the others' marks. A label
                // that would overlap one already drawn is dropped instead of stacking a column of boxes.
                var order = new List<RouteVisuals.Label>();
                order.AddRange(labels.Where(l => l.Kind == RouteVisuals.LabelKind.Toll));
                order.AddRange(labels.Where(l => l.Kind == RouteVisuals.LabelKind.Grand));
                order.AddRange(labels.Where(l => l.Kind == RouteVisuals.LabelKind.Hub && l.Paid));
                order.AddRange(labels.Where(l => l.Kind == RouteVisuals.LabelKind.Hub && !l.Paid));
                var legendLines = new List<string>();
                {
                    foreach (RouteVisuals.Label label in order)
                    {
                        if (label.Kind == RouteVisuals.LabelKind.Grand && label.Subtitle != null && !legendLines.Contains(label.Subtitle))
                        {
                            legendLines.Add(label.Subtitle);
                        }
                        if (!TryScreen(camera, label.Tile, out Vector2 center))
                        {
                            continue;
                        }
                        Rect rect = Draw(label, center);
                        drawn += rect.width > 0 ? 1 : 0;
                        skipped += rect.width > 0 ? 0 : 1;
                        string forced = ForcedHover;
                        bool hover = forced != null ? (label.Text + " " + label.Subtitle).IndexOf(forced, StringComparison.OrdinalIgnoreCase) >= 0 : rect.Contains(mouse);
                        if (rect.width > 0 && label.Detail != null && hover)
                        {
                            hovered = label.Detail;
                            hoveredRect = rect;
                        }
                    }
                }
                DrawLegend(legendLines);
                if (hovered != null)
                {
                    DrawPanel(hovered, hoveredRect);
                }
                Drawn = drawn;
                Skipped = skipped;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Route labels: {ex.Message}");
                enabled = false;
            }
        }

        private static bool TryScreen(Camera camera, int tile, out Vector2 center)
        {
            center = default;
            if (tile < 0)
            {
                return false;
            }
            Vector3 world = Presentation.PresentationWorldController.GetWorldPositionInUnityWorld(tile, needElevation: true);
            Vector3 viewport = camera.WorldToViewportPoint(world);
            if (viewport.z <= 0f || viewport.x < -0.05f || viewport.x > 1.05f || viewport.y < -0.05f || viewport.y > 1.05f)
            {
                return false;
            }
            center = new Vector2(viewport.x * Screen.width, (1f - viewport.y) * Screen.height);
            return true;
        }

        /// <summary>Draws one label centered on the point; returns its rect (empty when skipped).</summary>
        private Rect Draw(RouteVisuals.Label label, Vector2 center)
        {
            switch (label.Kind)
            {
                case RouteVisuals.LabelKind.Grand:
                    return DrawTwoLines(label, center, grandTitle, grandSubtitle, grandBackground, Gold, new Color(0.98f, 0.88f, 0.62f), framed: true, withSubtitle: false);
                case RouteVisuals.LabelKind.Hub:
                    // Hub badges hang under the city's banner (shield on the tile, name to its right), never over them.
                    if (!label.Paid)
                    {
                        return DrawMark(label, center + new Vector2(60f * scale, 40f * scale));
                    }
                    return DrawTwoLines(label, center + new Vector2(95f * scale, 52f * scale), hubTitle, hubSubtitle, background,
                        Gold, Muted, framed: label.Level >= TradeNetwork.MaxLevel);
                default:
                {
                    var content = new GUIContent(label.Text);
                    Vector2 size = style.CalcSize(content);
                    var rect = new Rect(center.x - size.x / 2f, center.y - size.y / 2f, size.x, size.y);
                    if (taken.Exists(t => t.Overlaps(rect)))
                    {
                        return default;
                    }
                    taken.Add(rect);
                    // IMGUI draws over every game panel: a label that would land on one is skipped.
                    if (OverPanel(rect))
                    {
                        return default;
                    }
                    style.normal.textColor = label.Paid ? Gold : Light;
                    GUI.Label(rect, content, style);
                    return rect;
                }
            }
        }

        /// <summary>Other nations' hubs: a small level mark (◆ per level), the details in the hover.</summary>
        private Rect DrawMark(RouteVisuals.Label label, Vector2 center)
        {
            var content = new GUIContent(label.Text);
            Vector2 size = hubMark.CalcSize(content);
            var rect = new Rect(center.x - size.x / 2f, center.y - size.y / 2f, size.x, size.y);
            if (taken.Exists(t => t.Overlaps(rect)) || OverPanel(rect))
            {
                return default;
            }
            taken.Add(rect);
            hubMark.normal.textColor = label.Level >= TradeNetwork.MaxLevel ? Gold : Muted;
            GUI.Label(rect, content, hubMark);
            return rect;
        }

        /// <summary>Legend of the grand routes in the view (full names), in the top-left corner under the empire banner.</summary>
        private void DrawLegend(List<string> lines)
        {
            if (lines.Count == 0)
            {
                return;
            }
            var content = new GUIContent(L.T("Grandes rotas") + "\n" + string.Join("\n", lines.Take(6).Select(l => "• " + l)));
            float width = 330f * scale;
            float height = legend.CalcHeight(content, width);
            var rect = new Rect(20f * scale, 290f * scale, width, height);
            if (OverPanel(rect))
            {
                return;
            }
            GUI.DrawTexture(rect, grandBackground);
            GUI.DrawTexture(new Rect(rect.x, rect.y, Mathf.Max(1f, 2f * scale), rect.height), border);
            GUI.Label(rect, content, legend);
        }

        private Rect DrawTwoLines(RouteVisuals.Label label, Vector2 center, GUIStyle title, GUIStyle subtitle, Texture2D back, Color titleColor, Color subtitleColor, bool framed, bool withSubtitle = true)
        {
            var top = new GUIContent(label.Text);
            bool second = withSubtitle && label.Subtitle != null;
            var bottom = new GUIContent(second ? label.Subtitle : string.Empty);
            Vector2 a = title.CalcSize(top);
            Vector2 b = second ? subtitle.CalcSize(bottom) : Vector2.zero;
            a.y += 5f * scale; // CalcSize leaves the serif's descenders (g, p) outside the box
            float width = Mathf.Max(a.x, b.x);
            float height = a.y + b.y;
            var rect = new Rect(center.x - width / 2f, center.y - height / 2f, width, height);
            if (taken.Exists(t => t.Overlaps(rect)))
            {
                return default;
            }
            taken.Add(rect);
            if (OverPanel(rect))
            {
                return default;
            }
            GUI.DrawTexture(rect, back);
            if (framed)
            {
                float line = Mathf.Max(1f, 2f * scale);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, line), border);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - line, rect.width, line), border);
            }
            title.normal.textColor = titleColor;
            GUI.Label(new Rect(rect.x, rect.y, width, a.y), top, title);
            if (second)
            {
                subtitle.normal.textColor = subtitleColor;
                GUI.Label(new Rect(rect.x, rect.y + a.y, width, b.y), bottom, subtitle);
            }
            return rect;
        }

        /// <summary>The hover panel: next to the label, kept inside the screen.</summary>
        private void DrawPanel(string text, Rect anchor)
        {
            var content = new GUIContent(text);
            float width = Mathf.Min(460f * scale, Screen.width * 0.4f);
            float height = panel.CalcHeight(content, width);
            float gap = 12f * scale;
            float y = Mathf.Clamp(anchor.y, 8f, Screen.height - height - 8f);
            // Right of the label, else left, else below, else above: the first place that covers no game panel.
            var places = new[]
            {
                new Rect(anchor.xMax + gap, y, width, height),
                new Rect(anchor.x - width - gap, y, width, height),
                new Rect(anchor.center.x - width / 2f, anchor.yMax + gap, width, height),
                new Rect(anchor.center.x - width / 2f, anchor.y - height - gap, width, height),
            };
            Rect rect = places[0];
            foreach (Rect place in places)
            {
                if (place.x >= 8f && place.xMax <= Screen.width - 8f && place.y >= 8f && place.yMax <= Screen.height - 8f && !OverPanel(place))
                {
                    rect = place;
                    break;
                }
            }
            GUI.DrawTexture(rect, panelBackground);
            GUI.Label(rect, content, panel);
            float line = Mathf.Max(1f, 2f * scale);
            GUI.DrawTexture(new Rect(rect.x, rect.y, line, rect.height), border);
        }

        // Game panels the labels must not cover (top banners, bottom bar, trade view side panel, notifications).
        private readonly List<Amplitude.UI.UITransform> panels = new List<Amplitude.UI.UITransform>();
        private float panelsFoundAt = -100f;

        /// <summary>
        /// Asks the window service for each panel. It used to resolve the types by name and scan every object of the scene
        /// (FindObjectsOfType): ~900 ms every 2 s while the trade view was open, the stutter of melhorias item 42.
        /// </summary>
        private void RefreshPanels()
        {
            if (Time.unscaledTime - panelsFoundAt < 2f)
            {
                return;
            }
            panelsFoundAt = Time.unscaledTime;
            panels.Clear();
            AddPanel(WindowsUtils.GetWindow<ManagementBanner>());
            AddPanel(WindowsUtils.GetWindow<EmpireBanner>());
            AddPanel(WindowsUtils.GetWindow<MenuBanner>());
            AddPanel(WindowsUtils.GetWindow<ControlBanner>());
            AddPanel(WindowsUtils.GetWindow<NotificationBanner>());
            AddPanel(WindowsUtils.GetWindow<EndTurnWindow>());
            AddPanel(WindowsUtils.GetWindow<EraStarCompletionBanner>());
            AddPanel(WindowsUtils.GetWindow<TradeViewWindow>()?.GetComponentInChildren<TradeViewMarketTab>(true));
        }

        private void AddPanel(Component panel)
        {
            if (panel != null && panel.GetComponent<Amplitude.UI.UITransform>() is Amplitude.UI.UITransform ui)
            {
                panels.Add(ui);
            }
        }

        /// <summary>The trade view's own header (title and route filters) and the top bar, in the 1920x1080 UI space.</summary>
        private static readonly Rect[] Reserved = { new Rect(0, 0, 1920, 135), new Rect(680, 135, 560, 140) };

        /// <summary>True when the screen rect overlaps a visible game panel (panel rects are in the 1920x1080 UI space).</summary>
        private bool OverPanel(Rect rect)
        {
            float scaleX = 1920f / Screen.width;
            float scaleY = 1080f / Screen.height;
            var ui = new Rect(rect.x * scaleX, rect.y * scaleY, rect.width * scaleX, rect.height * scaleY);
            foreach (Rect reserved in Reserved)
            {
                if (reserved.Overlaps(ui))
                {
                    return true;
                }
            }
            // The mod's trade post window (right side): its root is full screen, so the panel area is reserved by hand.
            if (NativeUI.TradePostWindow.IsOpen && new Rect(1440, 135, 480, 900).Overlaps(ui))
            {
                return true;
            }
            foreach (Amplitude.UI.UITransform item in panels)
            {
                // Some banners are full-screen containers: only real panels (smaller than the screen) hide a label.
                if (item != null && item.VisibleGlobally && !(item.GlobalRect.width > 1500f && item.GlobalRect.height > 700f) && item.GlobalRect.Overlaps(ui))
                {
                    return true;
                }
            }
            return false;
        }

        private void OnDestroy()
        {
            foreach (UnityEngine.Object texture in new UnityEngine.Object[] { background, grandBackground, border, panelBackground, serif, sans })
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }
    }
}
