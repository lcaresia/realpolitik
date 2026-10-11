using System;
using System.Linq;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Tooltips;
using Amplitude.Mercury.UI.Windows;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using CurrencyMod.Diplomacy.Council;
using CurrencyMod.NativeUI;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod.Diplomacy.UI
{
    /// <summary>
    /// Council button in the control bar, next to the mail: clone of the "Trade" button with the three-busts icon and a
    /// "!" badge when the turn's meeting is ready and you have not seen it yet. When the meeting becomes ready, the
    /// screen opens by itself if the map is free ([IA] ConselhoAbreSozinho), once per turn and only within the next 2
    /// minutes (after that, only the badge remains).
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
                    Plugin.Log.LogError($"Failed to create the council screen: {ex}");
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
            if (!AiConfig.PlayerCouncil.Value || !CentralBankWindow.IsInGame || SavePatches.IsGameOnline())
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
            Plugin.Log.LogInfo("Council button created in the control bar.");
        }

        private void RefreshBadge()
        {
            AiModule module = AiModule.Instance;
            CouncilMeeting meeting = PlayerCouncil.Meeting(AiModule.World, module?.CurrentTurn ?? -1);
            string mark = PlayerCouncil.HasNews(AiModule.World, module?.CurrentTurn ?? -1) ? "!" : null;
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
            string state = meeting == null ? L.F("O conselho está dispensado. Ele se reúne sozinho no turno {0}, ou quando você convocar uma audiência.", PlayerCouncil.NextAutoTurn(AiModule.World))
                : meeting.Status == "pronta" ? (meeting.Seen ? L.T("A reunião deste turno já aconteceu.") : L.T("A reunião deste turno está pronta."))
                : meeting.Status == "erro" ? L.T("A reunião deste turno falhou; abra para convocar de novo.")
                : L.T("O conselho está se reunindo.");
            tooltipTarget.Description = state + "\n" + L.T("Ouça a Mão e os ministros, responda a eles e demita quem não serve mais.");
        }

        /// <summary>Opens the screen when the meeting becomes ready, if the player is not busy with anything.</summary>
        private void TryAutoOpen()
        {
            if (!PlayerCouncil.AutoOpenPending)
            {
                return;
            }
            AiModule module = AiModule.Instance;
            CouncilMeeting meeting = PlayerCouncil.Meeting(AiModule.World, module?.CurrentTurn ?? -1);
            if (!AiConfig.PlayerCouncilAutoOpen.Value || meeting == null || meeting.Seen || meeting.Status != "pronta"
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

        /// <summary>Free map: main turn, default cursor, no bar menu and no window open.</summary>
        private static bool MapIsFree()
        {
            try
            {
                if (!GameAccess.IsTurnMain() || CouncilScreen.IsOpen || AlliancesScreen.IsOpen || MailScreen.IsOpen || NativeBankWindow.IsOpen || CentralBankWindow.IsOpen
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

        /// <summary>When leaving the game, the game rebuilds the bar; the button and the screen go along with it.</summary>
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
