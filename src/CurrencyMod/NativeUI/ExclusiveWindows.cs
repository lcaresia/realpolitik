using System;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Windows;
using Amplitude.UI.Windows;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// The mod's windows (Central Bank, trade post, mail) behave like the game's: one at a time, and they close when the
    /// player goes to do something else (user request on 2026-10-04: ESC closes; opening Faith or another menu closes;
    /// clicking a unit closes; never two open on top of each other).
    /// The game decides the side windows from the cursor (city, unit, diplomacy...) and from the control bar menus;
    /// the mod's windows are not part of that, so this class handles both ends:
    /// - on open: closes the other mod windows, the fullscreens, the bar menus and goes back to the default cursor (which
    ///   closes the selected unit's or city's window) and hides other open side windows;
    /// - while open: if the cursor stops being the default one, a bar menu opens or another side window appears, it
    ///   closes.
    /// </summary>
    internal static class ExclusiveWindows
    {
        /// <summary>Time after opening during which the other windows may still be leaving (animation).</summary>
        private const float Grace = 0.5f;

        /// <summary>Prepares the opening of a mod window. keepTradeView: the trade post lives in the trade view.</summary>
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
                if (opening != Diplomacia.UI.AlliancesScreen.Instance)
                {
                    Diplomacia.UI.AlliancesScreen.SetOpen(false);
                }
                if (opening != Diplomacia.UI.MailScreen.Instance)
                {
                    Diplomacia.UI.MailScreen.SetOpen(false);
                    // A side window does not show while a fullscreen is open (its group hides everything).
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
                Plugin.Log.LogWarning($"Mod windows: prepare opening: {ex.Message}");
            }
        }

        /// <summary>
        /// Whether the player went to do something else after the window opened: selected a unit, city or nation (the
        /// cursor changed), opened a control bar menu, or another game side window appeared.
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
