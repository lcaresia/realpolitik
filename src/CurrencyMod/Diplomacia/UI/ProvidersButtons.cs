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

namespace CurrencyMod.Diplomacia.UI
{
    /// <summary>
    /// Entradas da tela Diplomacia IA: um botão no menu principal (clone de "Meu Perfil", depois de "Cenários") e outro
    /// no menu de pausa da partida (clone de "Configurações", logo abaixo dele). Pelo menu de pausa, a coluna do menu
    /// some enquanto a tela está aberta e volta ao fechar, como nas Configurações do jogo.
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
                Plugin.Log.LogError($"Botões da Diplomacia IA: {ex}");
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
            Plugin.Log.LogInfo("Botão Diplomacia IA criado no menu principal.");
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
            Plugin.Log.LogInfo("Botão Diplomacia IA criado no menu de pausa.");
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
        /// Na partida, a tela sempre abre por cima do menu de pausa (que esconde o HUD, como nas Configurações). Se o
        /// menu ainda não estiver aberto (ex.: comando de teste), abre antes.
        /// </summary>
        internal static void OpenInGame(PauseMenuModalWindow menu = null, string section = null)
        {
            menu = menu ?? Resources.FindObjectsOfTypeAll<PauseMenuModalWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            if (menu != null && !menu.Shown)
            {
                WindowsUtils.ShowWindow(menu);
            }
            if (menu != null)
            {
                // Igual às Configurações: a coluna do menu de pausa sai e volta ao fechar.
                menu.leftContentShowable?.Hide(instant: true);
                ProvidersScreen.Closed = () =>
                {
                    if (menu != null && menu.Shown)
                    {
                        menu.leftContentShowable?.Show();
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
                B.SetLabel(button.transform, string.Empty, L.T("Diplomacia IA"));
                B.Tip(button.transform, string.Empty, L.T("Diplomacia IA"),
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
