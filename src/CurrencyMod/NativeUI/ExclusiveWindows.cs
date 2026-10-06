using System;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Windows;
using Amplitude.UI.Windows;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// As janelas do mod (Banco Central, posto comercial, correio) se comportam como as do jogo: uma de cada vez, e
    /// fecham quando o jogador vai mexer em outra coisa (pedido do usuário em 2026-10-04: ESC fecha; abrir Fé ou outro
    /// menu fecha; clicar numa tropa fecha; nunca duas abertas uma sobre a outra).
    /// O jogo decide as janelas laterais pelo cursor (cidade, tropa, diplomacia...) e pelos menus da barra de controle;
    /// as do mod não entram nessa conta, então esta classe faz as duas pontas:
    /// - ao abrir: fecha as outras janelas do mod, as telas cheias, os menus da barra e volta ao cursor normal (o que
    ///   fecha a janela da tropa ou da cidade selecionada) e esconde outras janelas laterais abertas;
    /// - enquanto aberta: se o cursor deixa de ser o normal, um menu da barra abre ou outra janela lateral aparece, ela
    ///   fecha.
    /// </summary>
    internal static class ExclusiveWindows
    {
        /// <summary>Tempo depois de abrir em que as outras janelas ainda podem estar saindo (animação).</summary>
        private const float Grace = 0.5f;

        /// <summary>Prepara a abertura de uma janela do mod. keepTradeView: o posto comercial vive na visão de comércio.</summary>
        internal static void BeforeOpen(UIWindow opening, bool keepTradeView = false)
        {
            try
            {
                if (opening != NativeBankWindow.Instance)
                {
                    NativeBankWindow.SetOpen(false);
                }
                if (CentralBankWindow.IsOpen)
                {
                    CentralBankWindow.SetOpen(false);
                }
                if (opening != TradePostWindow.Instance)
                {
                    TradePostWindow.SetOpen(-1);
                }
                if (opening != Diplomacia.UI.CouncilScreen.Instance)
                {
                    Diplomacia.UI.CouncilScreen.SetOpen(false);
                }
                if (opening != Diplomacia.UI.MailScreen.Instance)
                {
                    Diplomacia.UI.MailScreen.SetOpen(false);
                    // Uma janela lateral não aparece com tela cheia aberta (o grupo dela esconde tudo).
                    WindowsManager.Instance?.GetWindowsGroup<InGameFullscreenGroup>()?.HideAllFullscreens();
                }
                if (!keepTradeView)
                {
                    var cursors = Amplitude.Mercury.Presentation.Presentation.PresentationCursorController;
                    if (cursors != null && !(cursors.CurrentCursor is Amplitude.Mercury.Presentation.DefaultCursor))
                    {
                        cursors.ChangeToDefaultCursor();
                    }
                    WindowsUtils.GetWindow<ControlBanner>()?.RequestNoneState();
                }
                InGameSelectionGroup selection = WindowsManager.Instance?.GetWindowsGroup<InGameSelectionGroup>();
                if (selection?.windows != null)
                {
                    foreach (UIWindow window in selection.windows)
                    {
                        if (window != null && window != opening && window.Shown && !IsModWindow(window) && !(keepTradeView && window is TradeViewWindow))
                        {
                            WindowsUtils.HideWindow(window);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Janelas do mod: preparar a abertura: {ex.Message}");
            }
        }

        /// <summary>
        /// Se o jogador foi mexer em outra coisa depois que a janela abriu: selecionou tropa, cidade ou nação (o cursor
        /// mudou), abriu um menu da barra de controle ou outra janela lateral do jogo apareceu.
        /// </summary>
        internal static bool Interrupted(UIWindow window, float openedAt, bool tradeView = false)
        {
            if (window == null || !window.Shown || Time.unscaledTime - openedAt < Grace)
            {
                return false;
            }
            try
            {
                object cursor = Amplitude.Mercury.Presentation.Presentation.PresentationCursorController?.CurrentCursor;
                bool cursorOk = tradeView
                    ? cursor is Amplitude.Mercury.Presentation.TradeViewCursor
                    : cursor == null || cursor is Amplitude.Mercury.Presentation.DefaultCursor;
                if (!cursorOk)
                {
                    return true;
                }
                ControlBanner banner = WindowsUtils.GetWindow<ControlBanner>();
                if (banner != null && banner.State != ControlBanner.ControlBannerState.None && !(tradeView && banner.State == ControlBanner.ControlBannerState.Trade))
                {
                    return true;
                }
                InGameSelectionGroup selection = WindowsManager.Instance?.GetWindowsGroup<InGameSelectionGroup>();
                if (selection?.windows != null)
                {
                    foreach (UIWindow other in selection.windows)
                    {
                        if (other != null && other != window && other.Shown && !IsModWindow(other) && !(tradeView && other is TradeViewWindow))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        private static bool IsModWindow(UIWindow window)
        {
            return window == NativeBankWindow.Instance || window == TradePostWindow.Instance
                || window.name.StartsWith("CurrencyMod_", StringComparison.Ordinal);
        }
    }
}
