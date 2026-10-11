using System;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Chart tooltip: on mouse hover, shows the values of the turn under the cursor. The chart is a single image, so the
    /// mouse position (in UI space, the same as the rects) becomes the turn index and the native tooltip text is
    /// rebuilt when the index changes.
    /// </summary>
    internal partial class NativeBankWindow
    {
        private int hoverFirst;
        private int hoverCount;
        private Func<int, string[]> hoverText;
        private int hoverShown = -1;

        /// <summary>Registers where the tooltip texts come from (first turn, turn count, text for an index: title and description).</summary>
        private void SetChartHover(int first, int count, Func<int, string[]> describe)
        {
            if (first != hoverFirst || count != hoverCount)
            {
                hoverShown = -1;
            }
            hoverFirst = first;
            hoverCount = count;
            hoverText = describe;
            if (chartImage != null)
            {
                chartImage.GetComponent<UITransform>().InteractiveSelf = true;
            }
        }

        private void ClearChartHover()
        {
            hoverText = null;
            hoverShown = -1;
        }

        private void UpdateChartHover()
        {
            try
            {
                UpdateChartHoverCore();
            }
            catch (Exception)
            {
                // An error here would run every frame: the chart tooltip stops working, the rest of the screen does not.
                hoverText = null;
            }
        }

        private void UpdateChartHoverCore()
        {
            if (hoverText == null || chartImage == null || chartCard == null || hoverCount < 2)
            {
                return;
            }
            UITransform ui = chartImage.GetComponent<UITransform>();
            if (!ui.VisibleSelf || !chartCard.GetComponent<UITransform>().VisibleSelf)
            {
                return;
            }
            Vector2 mouse = UIInteractivityManager.Instance.GetMousePosition(UIHierarchyManager.Instance.MainFullscreenView);
            Rect rect = ui.GlobalRect;
            if (!rect.Contains(mouse))
            {
                hoverShown = -1;
                return;
            }
            // The drawing spans the whole image: the first turn at the left edge, the last at the right.
            int index = Mathf.Clamp(Mathf.RoundToInt((mouse.x - rect.x) / Mathf.Max(1f, rect.width) * (hoverCount - 1)), 0, hoverCount - 1);
            if (index == hoverShown)
            {
                return;
            }
            hoverShown = index;
            string[] text = hoverText(index);
            if (text != null && text.Length >= 2)
            {
                Tip(chartImage.transform, string.Empty, text[0], text[1]);
            }
        }
    }
}
