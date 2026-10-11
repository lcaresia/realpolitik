using System;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Tooltips;
using Amplitude.Mercury.UI.Windows;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using CurrencyMod.NativeUI;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod.Diplomacy.UI
{
    /// <summary>
    /// Alliances (blocs) button on the control bar, next to the council: a clone of the "Trade" button with the
    /// three-rings icon and a "!" badge when something needs your action (invite, missing vote, election or suggestion to
    /// the president).
    /// </summary>
    internal class AlliancesButton : MonoBehaviour
    {
        private const string CloneName = "CurrencyMod_AlliancesToggle";

        private static AlliancesButton instance;

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
            if (AlliancesScreen.Instance == null)
            {
                try
                {
                    AlliancesScreen.Create();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to create the alliances screen: {ex}");
                    enabled = false;
                    return;
                }
            }
            bool open = AlliancesScreen.IsOpen;
            if (button.Toggle.State != open)
            {
                button.Toggle.State = open;
            }
            if (Time.unscaledTime >= nextBadge)
            {
                nextBadge = Time.unscaledTime + 0.5f;
                RefreshBadge();
            }
        }

        private void TryCreate()
        {
            if (!AiConfig.Enabled.Value || !CentralBankWindow.IsInGame || SavePatches.IsGameOnline())
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
                iconTexture = SdfIcons.Alliance();
                iconGuid = UIRenderingManager.Instance.RegisterTexture(iconTexture);
            }
            button.SetIcon(new UITexture(iconGuid, UITextureFlags.AlphaStraight, UITextureColorFormat.Srgb, iconTexture));
            button.Toggle.State = false;
            button.Toggle.Switch += Toggle_Switch;
            RefreshTooltip(null);
            button.Tooltip.Bind(TooltipUtils.TitleAndDescription, tooltipTarget);
            badge = NativeUIKit.CreateBadge(cloneTransform.transform, "AlliancesBadge");
            button.Show(instant: true);
            Plugin.Log.LogInfo("Alliances button created on the control bar.");
        }

        private void RefreshBadge()
        {
            string mark = AlliancesScreen.Badge();
            if (mark == shownBadge)
            {
                return;
            }
            shownBadge = mark;
            RefreshTooltip(mark);
            if (badge != null)
            {
                NativeUIKit.SetBadge(badge, mark);
            }
        }

        private void RefreshTooltip(string mark)
        {
            tooltipTarget.Title = L.T("Alianças");
            tooltipTarget.Description = (mark != null ? L.T("Há algo esperando por você: convite, voto ou eleição.") + "\n" : string.Empty)
                + L.T("Blocos de nações: comércio livre entre membros, defesa mútua, voto conjunto no Congresso e tarifa comum.");
        }

        private void Toggle_Switch(IUIToggle source, bool state)
        {
            AlliancesScreen.SetOpen(state);
        }

        internal static void DestroyButton()
        {
            AlliancesScreen.DestroyWindow();
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
