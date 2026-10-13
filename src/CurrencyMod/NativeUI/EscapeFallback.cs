using Amplitude.Framework.Input;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.Mercury.UI.Windows;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Safety net for ESC. Sometimes the game stops turning the physical key into its "exit window" action (the pause
    /// menu does not open and no window closes), while the same action raised by code works. When ESC is pressed and
    /// the game did not run WindowsManager.ExitWindow by the next frame, this raises it, and logs what was focused so
    /// the root cause can be found.
    /// </summary>
    internal sealed class EscapeFallback : MonoBehaviour
    {
        /// <summary>Last frame in which the game (or this fallback) ran ExitWindow.</summary>
        internal static int LastExitFrame = -1;

        private int pressedFrame = -1;
        private int fallbacks;

        private void Update()
        {
            if (pressedFrame >= 0 && Time.frameCount > pressedFrame + 1)
            {
                int pressed = pressedFrame;
                pressedFrame = -1;
                if (LastExitFrame < pressed)
                {
                    Fallback();
                }
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                pressedFrame = Time.frameCount;
            }
        }

        private void Fallback()
        {
            WindowsManager manager = WindowsManager.Instance;
            if (manager == null || GUIUtility.keyboardControl != 0)
            {
                return; // not in game, or an IMGUI text field of the mod is typing
            }
            UIControlResponder focused = UIInteractivityManager.Instance?.FocusedResponder as UIControlResponder;
            string focus = focused == null ? "none" : focused.GetType().Name;
            if (focused is UITextFieldResponder)
            {
                Component field = focused.Interactable as Component;
                UITransform transform = field != null ? field.GetComponent<UITransform>() : null;
                if (transform != null && transform.VisibleGlobally)
                {
                    return; // the player is typing in a visible field: ESC belongs to it
                }
                // A field of a hidden window kept the keyboard: release it, then exit as the key should have.
                UIInteractivityManager.Instance.SetFocus();
                focus += " (hidden field " + (field != null ? field.name : "?") + ")";
            }
            fallbacks++;
            Plugin.Log?.LogWarning($"ESC was not handled by the game; raising ExitWindow (fallback #{fallbacks}, focus: {focus}).");
            manager.ExitWindow(null, ActionState.Pressed);
        }

        [HarmonyPatch(typeof(WindowsManager), "ExitWindow")]
        private static class ExitWindowPatch
        {
            private static void Prefix()
            {
                LastExitFrame = Time.frameCount;
            }
        }
    }
}
