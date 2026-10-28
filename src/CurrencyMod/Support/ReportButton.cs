using System;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Tooltips;
using Amplitude.Mercury.UI.Windows;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using CurrencyMod.Diplomacy.UI;
using CurrencyMod.NativeUI;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod.Support
{
    /// <summary>
    /// "Report a problem" button, always on the control bar (last one, after the council): a clone of the "Trade" button
    /// with a speech-bubble icon that opens the Realpolitik screen on the report section. Lucas asked for a permanent,
    /// easy-to-reach button (2026-10-14); it also keeps the bar visible in the Neolithic.
    /// </summary>
    internal class ReportButton : MonoBehaviour
    {
        private const string CloneName = "CurrencyMod_ReportToggle";

        private static ReportButton instance;

        private ControlBannerLayerToggle button;
        private Texture2D iconTexture;
        private Amplitude.Framework.Guid iconGuid;
        private readonly TitleAndDescription tooltipTarget = new TitleAndDescription();
        private ErrorReport.State shownState = (ErrorReport.State)(-1);
        private float nextAttempt;
        private float nextRefresh;

        internal static bool Exists => instance != null && instance.button != null;

        private void Awake()
        {
            instance = this;
        }

        private void Update()
        {
            if (CentralBankWindow.IsInGame)
            {
                bool reportOpen = ProvidersScreen.IsOpen && ProvidersScreen.Instance.CurrentSection == ProvidersScreen.ReportSection;
                if (NativeUIKit.ShortcutPressed(Diplomacy.AiConfig.ReportKey.Value, reportOpen))
                {
                    Toggle(ProvidersScreen.ReportSection, !reportOpen);
                }
                else if (NativeUIKit.ShortcutPressed(Diplomacy.AiConfig.RealpolitikKey.Value, ProvidersScreen.IsOpen))
                {
                    Toggle(null, !ProvidersScreen.IsOpen);
                }
            }
            if (button == null)
            {
                if (Time.unscaledTime >= nextAttempt)
                {
                    nextAttempt = Time.unscaledTime + 1f;
                    TryCreate();
                }
                return;
            }
            bool open = ProvidersScreen.IsOpen && ProvidersScreen.Instance.CurrentSection == ProvidersScreen.ReportSection;
            if (button.Toggle.State != open)
            {
                button.Toggle.State = open;
            }
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.5f;
                RefreshTooltip();
            }
        }

        private void TryCreate()
        {
            if (!CentralBankWindow.IsInGame)
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
                iconTexture = SdfIcons.Report();
                iconGuid = UIRenderingManager.Instance.RegisterTexture(iconTexture);
            }
            button.SetIcon(new UITexture(iconGuid, UITextureFlags.AlphaStraight, UITextureColorFormat.Srgb, iconTexture));
            button.Toggle.State = false;
            button.Toggle.Switch += Toggle_Switch;
            shownState = (ErrorReport.State)(-1);
            RefreshTooltip();
            button.Tooltip.Bind(TooltipUtils.TitleAndDescription, tooltipTarget);
            button.Show(instant: true);
            Plugin.Log.LogInfo("Report button created on the control bar.");
        }

        private void RefreshTooltip()
        {
            ErrorReport.State state = ErrorReport.Current;
            if (state == shownState)
            {
                return;
            }
            shownState = state;
            tooltipTarget.Title = L.T("Relatar um problema") + $" ({Diplomacy.AiConfig.ReportKey.Value})";
            string last = state == ErrorReport.State.Sent ? "\n" + L.F("Último envio: {0}", ErrorReport.Code)
                : state == ErrorReport.State.Failed ? "\n" + L.T("O último envio falhou; a cópia ficou salva neste PC.")
                : state == ErrorReport.State.Sending ? "\n" + L.T("Enviando…") : string.Empty;
            tooltipTarget.Description = L.T("Envia os registros do jogo e do mod para a equipe analisar um erro, um travamento ou algo estranho.") + last;
        }

        private void Toggle_Switch(IUIToggle source, bool state)
        {
            bool showingReport = ProvidersScreen.IsOpen && ProvidersScreen.Instance.CurrentSection == ProvidersScreen.ReportSection;
            if (state != showingReport)
            {
                Toggle(ProvidersScreen.ReportSection, state);
            }
        }

        /// <summary>Opens the Realpolitik screen on a section (null = its default) straight from the map, or closes it.</summary>
        private static void Toggle(string section, bool open)
        {
            if (open)
            {
                ProvidersButtons.OpenInGame(section: section, backToGame: true);
            }
            else
            {
                ProvidersScreen.SetOpen(false);
            }
        }

        internal static void DestroyButton()
        {
            if (instance != null && instance.button != null)
            {
                instance.button.Toggle.Switch -= instance.Toggle_Switch;
                instance.button.Tooltip.Unbind();
                DestroyClone(instance.button.gameObject);
                instance.button = null;
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

        /// <summary>The bar stays visible while this button exists (the game hides it in the Neolithic).</summary>
        [HarmonyPatch(typeof(ControlBanner), nameof(ControlBanner.ShouldBeShown))]
        private static class ShowBarPatch
        {
            private static void Postfix(ref bool __result)
            {
                if (!__result && Exists)
                {
                    __result = true;
                }
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
