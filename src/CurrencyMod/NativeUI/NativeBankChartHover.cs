using System;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Balão do gráfico: com o mouse em cima, mostra os valores do turno sob o cursor. O gráfico é uma imagem só, então a
    /// posição do mouse (no espaço da interface, o mesmo dos retângulos) vira o índice do turno e o texto do balão nativo é
    /// refeito quando o índice muda.
    /// </summary>
    internal partial class NativeBankWindow
    {
        private int hoverFirst;
        private int hoverCount;
        private Func<int, string[]> hoverText;
        private int hoverShown = -1;

        /// <summary>Registra de onde vêm os textos do balão (turno inicial, quantos turnos, texto de um índice: título e descrição).</summary>
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
                // Um erro aqui rodaria a cada quadro: o balão do gráfico deixa de funcionar, o resto da tela não.
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
            // O desenho vai de uma ponta à outra da imagem: o primeiro turno na borda esquerda, o último na direita.
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
