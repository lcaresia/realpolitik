using System;
using System.Collections.Generic;
using Amplitude.Mercury.Presentation;
using UnityEngine;

namespace CurrencyMod
{
    /// <summary>
    /// Valor no meio de cada rota que passa pelas terras do jogador, só na visão de comércio: "£ 12/t" em dourado quando a
    /// rota já paga o pedágio dele, "£ 12/t?" em branco quando passa livre (quanto pagaria no preço atual). A posição usa a
    /// mesma conta dos marcadores do jogo (posição do tile no mundo → câmera). Desenhado com IMGUI: as linhas do jogo são
    /// pintadas na GPU, sem marcador próprio para pendurar texto.
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
                Plugin.Log.LogWarning($"Rótulos das rotas: {ex.Message}");
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
