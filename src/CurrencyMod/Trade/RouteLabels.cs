using System;
using System.Collections.Generic;
using Amplitude.Mercury.Presentation;
using UnityEngine;

namespace CurrencyMod
{
    /// <summary>
    /// Value in the middle of each route that crosses the player's land, only in the trade view: "£ 12/t" in gold when the
    /// route already pays the player's toll, "£ 12/t?" in white when it passes free (what it would pay at the current price).
    /// The position uses the same math as the game's markers (tile world position → camera). Drawn with IMGUI: the game's
    /// lines are painted on the GPU, with no marker of their own to hang text on.
    /// </summary>
    internal sealed class RouteLabels : MonoBehaviour
    {
        private GUIStyle style;
        private GUIStyle shadow;
        private Texture2D background;

        private static bool InTradeView()
        {
            return Presentation.PresentationCursorController?.CurrentCursor is TradeViewCursor;
        }

        private void EnsureStyles()
        {
            if (style != null)
            {
                return;
            }
            background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0.07f, 0.09f, 0.13f, 0.82f));
            background.Apply();
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(15f * Screen.height / 1080f),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 3, 3),
            };
            style.normal.background = background;
            shadow = new GUIStyle(style);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !RouteVisuals.Enabled)
            {
                return;
            }
            List<RouteVisuals.Label> labels = RouteVisuals.Labels;
            if (labels == null || labels.Count == 0 || !InTradeView())
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
                foreach (RouteVisuals.Label label in labels)
                {
                    Vector3 world = Presentation.PresentationWorldController.GetWorldPositionInUnityWorld(label.Tile, needElevation: true);
                    Vector3 viewport = camera.WorldToViewportPoint(world);
                    if (viewport.z <= 0f || viewport.x < -0.05f || viewport.x > 1.05f || viewport.y < -0.05f || viewport.y > 1.05f)
                    {
                        continue;
                    }
                    var content = new GUIContent(label.Text);
                    Vector2 size = style.CalcSize(content);
                    var rect = new Rect(viewport.x * Screen.width - size.x / 2f, (1f - viewport.y) * Screen.height - size.y / 2f, size.x, size.y);
                    style.normal.textColor = label.Paid ? new Color(0.95f, 0.78f, 0.36f) : new Color(0.93f, 0.93f, 0.93f);
                    GUI.Label(rect, content, style);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Route labels: {ex.Message}");
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (background != null)
            {
                Destroy(background);
            }
        }
    }
}
