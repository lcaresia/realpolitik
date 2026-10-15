using System;
using System.Linq;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.UI.Layouts;
using CurrencyMod.NativeUI;
using UnityEngine;
using B = CurrencyMod.NativeUI.NativeBankWindow;

namespace CurrencyMod.Diplomacy.UI
{
    /// <summary>
    /// Entry points of the Realpolitik screen: a button in the main menu (clone of "My Profile", after "Scenarios") and
    /// another in the in-game pause menu (clone of "Settings", right below it). From the pause menu, the menu column
    /// disappears while the screen is open and comes back on close, as in the game's Settings.
    /// </summary>
    internal class ProvidersButtons : MonoBehaviour
    {
        private const string MainName = "CurrencyMod_AiDiplomacyButton";
        private const string PauseName = "CurrencyMod_AiDiplomacyPauseButton";

        private UIButton mainButton;
        private UIButton pauseButton;
        private PauseMenuModalWindow pauseMenu;
        private float nextAttempt;

        private void Update()
        {
            if (Time.unscaledTime < nextAttempt)
            {
                return;
            }
            nextAttempt = Time.unscaledTime + 1f;
            try
            {
                if (mainButton == null)
                {
                    mainButton = CreateMain();
                }
                if (pauseButton == null)
                {
                    pauseButton = CreatePause();
                }
                RefreshTexts();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"AI Diplomacy buttons: {ex}");
                nextAttempt = Time.unscaledTime + 30f;
            }
        }

        private UIButton CreateMain()
        {
            MainMenuScreen menu = Resources.FindObjectsOfTypeAll<MainMenuScreen>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            if (menu == null || menu.LoadingState != Amplitude.UI.Windows.LoadingState.Loaded)
            {
                return null;
            }
            Transform buttons = menu.transform.Find("Buttons");
            Transform donor = buttons?.Find("MyProfileButton");
            Transform after = buttons?.Find("ScenarioButton") ?? donor;
            if (donor == null)
            {
                return null;
            }
            Transform leftover = buttons.Find(MainName);
            if (leftover != null)
            {
                NativeUIKit.Dispose(leftover);
            }
            UIButton button = Clone(donor, buttons, MainName, after.GetSiblingIndex() + 1);
            button.LeftClick += b => ProvidersScreen.SetOpen(true);
            Plugin.Log.LogInfo("AI Diplomacy button created in the main menu.");
            return button;
        }

        private UIButton CreatePause()
        {
            PauseMenuModalWindow menu = Resources.FindObjectsOfTypeAll<PauseMenuModalWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            if (menu == null || menu.LoadingState != Amplitude.UI.Windows.LoadingState.Loaded || menu.settingsButton == null)
            {
                return null;
            }
            Transform donor = menu.settingsButton.transform;
            Transform parent = donor.parent;
            Transform leftover = parent.Find(PauseName);
            if (leftover != null)
            {
                NativeUIKit.Dispose(leftover);
            }
            pauseMenu = menu;
            UIButton button = Clone(donor, parent, PauseName, donor.GetSiblingIndex() + 1);
            button.LeftClick += b => OpenFromPause();
            Plugin.Log.LogInfo("AI Diplomacy button created in the pause menu.");
            return button;
        }

        private static UIButton Clone(Transform donor, Transform parent, string name, int index)
        {
            Transform clone = NativeUIKit.Clone(donor, parent, name);
            clone.SetSiblingIndex(Math.Min(index, parent.childCount - 1));
            UIButton button = clone.GetComponent<UIButton>();
            UITooltip tooltip = clone.GetComponent<UITooltip>();
            if (tooltip != null)
            {
                tooltip.Unbind(preserveTooltipClass: false);
                tooltip.Message = string.Empty;
            }
            Arrange(parent);
            return button;
        }

        private static void Arrange(Transform parent)
        {
            UILayout layout = parent.GetComponent<UILayout>();
            if (layout is UITable1D table)
            {
                table.ArrangeChildren();
            }
        }

        private void OpenFromPause() => OpenInGame(pauseMenu);

        /// <summary>
        /// In game, the screen always opens on top of the pause menu (which hides the HUD, as in Settings). If the menu
        /// is not open yet (e.g. a test command), it opens it first.
        /// </summary>
        /// <param name="backToGame">Opened from the control bar: closing goes straight back to the map instead of
        /// leaving the player on a pause menu they never opened.</param>
        internal static void OpenInGame(PauseMenuModalWindow menu = null, string section = null, bool backToGame = false)
        {
            menu = menu ?? Resources.FindObjectsOfTypeAll<PauseMenuModalWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            if (backToGame)
            {
                // Same cleanup as the mod's other screens (bank, mail, council...): never two on top of each other.
                NativeUI.ExclusiveWindows.BeforeOpen(null);
            }
            if (menu != null && !menu.Shown)
            {
                WindowsUtils.ShowWindow(menu);
            }
            ProvidersScreen.BackToGame = backToGame;
            if (menu != null)
            {
                // Same as Settings: the pause menu column goes away and comes back on close.
                menu.leftContentShowable?.Hide(instant: true);
                ProvidersScreen.Closed = () =>
                {
                    ProvidersScreen.BackToGame = false;
                    if (menu == null || !menu.Shown)
                    {
                        return;
                    }
                    menu.leftContentShowable?.Show(instant: backToGame);
                    if (backToGame)
                    {
                        WindowsUtils.HideWindow(menu);
                    }
                };
            }
            ProvidersScreen.SetOpen(true, section);
        }

        private void RefreshTexts()
        {
            foreach (UIButton button in new[] { mainButton, pauseButton })
            {
                if (button == null)
                {
                    continue;
                }
                B.SetLabel(button.transform, string.Empty, L.T("Realpolitik"));
                B.Tip(button.transform, string.Empty, L.T("Realpolitik") + (button == pauseButton ? $" ({AiConfig.RealpolitikKey.Value})" : string.Empty),
                    L.T("Conecte a IA que faz as nações do computador pensarem, escreverem cartas e negociarem: login ou chave, modelo e limites."));
            }
            if (mainButton != null)
            {
                Arrange(mainButton.transform.parent);
            }
        }

        private void OnDestroy()
        {
            ProvidersScreen.DestroyWindow();
            foreach (UIButton button in new[] { mainButton, pauseButton })
            {
                if (button != null)
                {
                    Transform parent = button.transform.parent;
                    NativeUIKit.Dispose(button.transform);
                    if (parent != null)
                    {
                        Arrange(parent);
                    }
                }
            }
            mainButton = null;
            pauseButton = null;
        }
    }
}
