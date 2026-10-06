using System;
using System.Linq;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Tooltips;
using Amplitude.Mercury.UI.Windows;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using CurrencyMod.Diplomacia.Council;
using CurrencyMod.NativeUI;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod.Diplomacia.UI
{
    /// <summary>
    /// Botão do conselho na barra de controle, ao lado do correio: clone do botão "Comercializar" com o ícone de três
    /// bustos e um selo "!" quando a reunião do turno está pronta e você ainda não viu. Quando a reunião fica pronta, a
    /// tela abre sozinha se o mapa estiver livre ([IA] ConselhoAbreSozinho), uma vez por turno e só nos 2 minutos
    /// seguintes (depois disso, fica só o selo).
    /// </summary>
    internal class CouncilButton : MonoBehaviour
    {
        private const string CloneName = "CurrencyMod_CouncilToggle";
        private const float AutoOpenWindowSeconds = 120f;

        private static CouncilButton instance;

        private ControlBannerLayerToggle button;
        private Texture2D iconTexture;
        private Amplitude.Framework.Guid iconGuid;
        private readonly TitleAndDescription tooltipTarget = new TitleAndDescription();
        private Transform badge;
        private string shownBadge = "?";
        private float nextAttempt;
        private float nextBadge;

        private void Awake()
        {
            instance = this;
        }

        private void Update()
        {
            if (button == null)
            {
                if (Time.unscaledTime >= nextAttempt)
                {
                    nextAttempt = Time.unscaledTime + 1f;
                    TryCreate();
                }
                return;
            }
            if (CouncilScreen.Instance == null)
            {
                try
                {
                    CouncilScreen.Create();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Falha ao criar a tela do conselho: {ex}");
                    enabled = false;
                    return;
                }
            }
            bool open = CouncilScreen.IsOpen;
            if (button.Toggle.State != open)
            {
                button.Toggle.State = open;
            }
            if (Time.unscaledTime >= nextBadge)
            {
                nextBadge = Time.unscaledTime + 0.5f;
                RefreshBadge();
                TryAutoOpen();
            }
        }

        private void TryCreate()
        {
            if (!IaConfig.PlayerCouncil.Value || !CentralBankWindow.IsInGame || SavePatches.IsGameOnline())
            {
                return;
            }
            ControlBanner banner;
            try
            {
                banner = WindowsUtils.GetWindow<ControlBanner>();
            }
            catch (Exception)
            {
                return;
            }
            if (banner == null || banner.tradeToggle == null || banner.layerTogglesTable == null)
            {
                return;
            }
            Transform leftover = banner.layerTogglesTable.transform.Find(CloneName);
            if (leftover != null)
            {
                DestroyClone(leftover.gameObject);
            }

            UITransform cloneTransform = banner.layerTogglesTable.InstantiateChild(banner.tradeToggle.transform, CloneName);
            button = cloneTransform.GetComponent<ControlBannerLayerToggle>();
            if (button == null)
            {
                DestroyClone(cloneTransform.gameObject);
                return;
            }
            cloneTransform.transform.SetAsLastSibling();
            if (button.Stamp.IsLoaded)
            {
                button.Stamp.ClearTags();
            }
            if (iconTexture == null)
            {
                iconTexture = SdfIcons.Council();
                iconGuid = UIRenderingManager.Instance.RegisterTexture(iconTexture);
            }
            button.SetIcon(new UITexture(iconGuid, UITextureFlags.AlphaStraight, UITextureColorFormat.Srgb, iconTexture));
            button.Toggle.State = false;
            button.Toggle.Switch += Toggle_Switch;
            RefreshTooltip(null);
            button.Tooltip.Bind(TooltipUtils.TitleAndDescription, tooltipTarget);
            badge = NativeUIKit.CreateBadge(cloneTransform.transform, "CouncilBadge");
            button.Show(instant: true);
            Plugin.Log.LogInfo("Botão do conselho criado na barra de controle.");
        }

        private void RefreshBadge()
        {
            IaModule module = IaModule.Instance;
            CouncilMeeting meeting = PlayerCouncil.Meeting(IaModule.World, module?.CurrentTurn ?? -1);
            string mark = meeting != null && meeting.Status == "pronta" && !meeting.Seen ? "!" : null;
            if (mark == shownBadge)
            {
                return;
            }
            shownBadge = mark;
            RefreshTooltip(meeting);
            if (badge != null)
            {
                NativeUIKit.SetBadge(badge, mark);
            }
        }

        private void RefreshTooltip(CouncilMeeting meeting)
        {
            tooltipTarget.Title = L.T("Conselho");
            string state = meeting == null ? L.T("O conselho se reúne no começo de cada turno.")
                : meeting.Status == "pronta" ? (meeting.Seen ? L.T("A reunião deste turno já aconteceu.") : L.T("A reunião deste turno está pronta."))
                : meeting.Status == "erro" ? L.T("A reunião deste turno falhou; abra para convocar de novo.")
                : L.T("O conselho está se reunindo.");
            tooltipTarget.Description = state + "\n" + L.T("Ouça a Mão e os ministros, responda a eles e demita quem não serve mais.");
        }

        /// <summary>Abre a tela quando a reunião fica pronta, se o jogador não estiver mexendo em nada.</summary>
        private void TryAutoOpen()
        {
            if (!PlayerCouncil.AutoOpenPending)
            {
                return;
            }
            IaModule module = IaModule.Instance;
            CouncilMeeting meeting = PlayerCouncil.Meeting(IaModule.World, module?.CurrentTurn ?? -1);
            if (!IaConfig.PlayerCouncilAutoOpen.Value || meeting == null || meeting.Seen || meeting.Status != "pronta"
                || Time.unscaledTime - PlayerCouncil.ReadyAt > AutoOpenWindowSeconds)
            {
                PlayerCouncil.AutoOpenPending = false;
                return;
            }
            if (!MapIsFree())
            {
                return;
            }
            PlayerCouncil.AutoOpenPending = false;
            CouncilScreen.SetOpen(true);
        }

        /// <summary>Mapa livre: turno principal, cursor normal, nenhum menu da barra e nenhuma janela aberta.</summary>
        private static bool MapIsFree()
        {
            try
            {
                if (!GameAccess.IsTurnMain() || CouncilScreen.IsOpen || MailScreen.IsOpen || NativeBankWindow.IsOpen || CentralBankWindow.IsOpen
                    || TradePostWindow.Instance?.Shown == true)
                {
                    return false;
                }
                object cursor = Amplitude.Mercury.Presentation.Presentation.PresentationCursorController?.CurrentCursor;
                if (cursor != null && !(cursor is Amplitude.Mercury.Presentation.DefaultCursor))
                {
                    return false;
                }
                ControlBanner banner = WindowsUtils.GetWindow<ControlBanner>();
                if (banner != null && banner.State != ControlBanner.ControlBannerState.None)
                {
                    return false;
                }
                InGameFullscreenGroup fullscreens = WindowsManager.Instance?.GetWindowsGroup<InGameFullscreenGroup>();
                if (fullscreens?.windows != null && fullscreens.windows.Any(w => w != null && w.Shown))
                {
                    return false;
                }
                InGameSelectionGroup selection = WindowsManager.Instance?.GetWindowsGroup<InGameSelectionGroup>();
                if (selection?.windows != null && selection.windows.Any(w => w != null && w.Shown))
                {
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void Toggle_Switch(IUIToggle source, bool state)
        {
            CouncilScreen.SetOpen(state);
        }

        internal static void DestroyButton()
        {
            CouncilScreen.DestroyWindow();
            if (instance != null && instance.button != null)
            {
                instance.button.Toggle.Switch -= instance.Toggle_Switch;
                instance.button.Tooltip.Unbind();
                DestroyClone(instance.button.gameObject);
                instance.button = null;
                instance.badge = null;
                instance.shownBadge = "?";
            }
        }

        private static void DestroyClone(GameObject clone)
        {
            clone.SetActive(false);
            Destroy(clone);
        }

        private void OnDestroy()
        {
            DestroyButton();
            if (iconTexture != null)
            {
                try
                {
                    UIRenderingManager.Instance?.UnregisterTexture(iconTexture);
                }
                catch (Exception)
                {
                }
                Destroy(iconTexture);
            }
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>Ao sair da partida, o jogo reconstrói a barra; o botão e a tela saem junto.</summary>
        [HarmonyPatch(typeof(ControlBanner), nameof(ControlBanner.OnPresentationShuttingDown))]
        private static class ShutdownPatch
        {
            private static void Prefix()
            {
                DestroyButton();
            }
        }
    }
}
