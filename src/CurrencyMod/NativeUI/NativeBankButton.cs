using System;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Tooltips;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Central Bank button in the bottom-left control bar, next to "Trade".
    /// It is a clone of the game's own trade button (same styles, animations and sounds),
    /// with its own SDF icon and a native tooltip.
    /// </summary>
    internal class NativeBankButton : MonoBehaviour
    {
        private const string CloneName = "CurrencyMod_CentralBankToggle";

        private static NativeBankButton instance;

        private ControlBannerLayerToggle button;
        private Texture2D iconTexture;
        private Amplitude.Framework.Guid iconGuid;
        private readonly TitleAndDescription tooltipTarget = new TitleAndDescription();
        private float nextAttempt;
        /// <summary>"!" badge when the economy is in a severe state (debt, high inflation, heavy unemployment).</summary>
        private Transform badge;
        private string alert;
        private float nextAlert;

        public static event Action<bool> Clicked;

        /// <summary>Alert forced by a test command ("game alert"), to check the badge without waiting for a crisis.</summary>
        internal static string TestAlert;

        public static bool Exists => instance != null && instance.button != null;

        private float nextWindowTry;

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

            // The lit button follows the open window.
            if (NativeBankWindow.Instance == null && Time.unscaledTime >= nextWindowTry)
            {
                // Tries once per second (the donor or the group may not be ready yet) and, if building throws
                // (a game update changing the Settings screen), waits 30 s instead of disabling the button.
                nextWindowTry = Time.unscaledTime + 1f;
                try
                {
                    NativeBankWindow.Create();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to create the Central Bank screen: {ex}");
                    nextWindowTry = Time.unscaledTime + 30f;
                }
            }

            bool open = NativeBankWindow.IsOpen || CentralBankWindow.IsOpen;
            if (button.Toggle.State != open)
            {
                button.Toggle.State = open;
            }
            if (Time.unscaledTime >= nextAlert)
            {
                nextAlert = Time.unscaledTime + 1f;
                RefreshAlert();
            }
            RefreshTooltip();
        }

        /// <summary>The same diagnosis as the Cycle tab; only severe cases light the badge.</summary>
        private void RefreshAlert()
        {
            string current = null;
            try
            {
                CurrencyWorld world = CurrencyManager.Current;
                if (world != null && CentralBankWindow.TryGetGameData(out Amplitude.Mercury.Interop.GameSnapshot.Data game))
                {
                    int local = game.LocalEmpireInfo.EmpireIndex;
                    double stock = (float)game.EmpireInfo[local].MoneyStock;
                    EconomyDiagnosis diagnosis = null;
                    lock (CurrencyManager.Lock)
                    {
                        EmpireCurrency mine = world.Get(local);
                        if (mine != null)
                        {
                            diagnosis = EconomyDiagnosis.For(mine, stock);
                        }
                    }
                    if (diagnosis != null && diagnosis.Severity == EconomyDiagnosis.Level.Bad)
                    {
                        current = diagnosis.Headline;
                    }
                }
            }
            catch (Exception)
            {
                current = null;
            }
            if (TestAlert != null)
            {
                current = TestAlert;
            }
            if (current == alert)
            {
                return;
            }
            alert = current;
            NativeUIKit.SetBadge(badge, alert != null ? "!" : null);
        }

        private void TryCreate()
        {
            if (!CentralBankWindow.IsInGame || SavePatches.IsGameOnline())
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

            // Removes leftovers from a previous generation of the mod (hot reload).
            Transform leftover = banner.layerTogglesTable.transform.Find(CloneName);
            if (leftover != null)
            {
                DestroyClone(leftover.gameObject);
            }

            UITransform cloneTransform = banner.layerTogglesTable.InstantiateChild(banner.tradeToggle.transform, CloneName);
            button = cloneTransform.GetComponent<ControlBannerLayerToggle>();
            if (button == null)
            {
                Plugin.Log.LogError("Trade button clone without ControlBannerLayerToggle.");
                DestroyClone(cloneTransform.gameObject);
                return;
            }
            cloneTransform.transform.SetAsLastSibling();

            if (button.Stamp.IsLoaded)
            {
                button.Stamp.ClearTags(); // do not inherit tutorial tags
            }

            if (iconTexture == null)
            {
                iconTexture = SdfIcons.CentralBank();
                iconGuid = UIRenderingManager.Instance.RegisterTexture(iconTexture);
            }
            button.SetIcon(new UITexture(iconGuid, UITextureFlags.AlphaStraight, UITextureColorFormat.Srgb, iconTexture));

            button.Toggle.State = false;
            button.Toggle.Switch += Toggle_Switch;
            RefreshTooltip();
            button.Tooltip.Bind(TooltipUtils.TitleAndDescription, tooltipTarget);
            badge = NativeUIKit.CreateBadge(cloneTransform.transform, "BankBadge");
            alert = null;
            button.Show(instant: true);
            Plugin.Log.LogInfo("Central Bank button created in the control bar.");
        }

        private void RefreshTooltip()
        {
            tooltipTarget.Title = L.T("Banco Central") + $" ({Plugin.ToggleWindowKey.Value})";
            string description = TextPatches.LocalTooltipLine != null
                ? L.T("Moeda, câmbio, inflação e juros do seu império.") + "\n" + TextPatches.LocalTooltipLine
                : L.T("Moeda, câmbio, inflação e juros do seu império.");
            if (alert != null)
            {
                description += "\n" + L.F("<c=E8685E>Alerta: {0}.</c> Veja o diagnóstico na aba Ciclo.", alert);
            }
            tooltipTarget.Description = description;
        }

        private void Toggle_Switch(IUIToggle source, bool state)
        {
            if (NativeBankWindow.Instance != null)
            {
                NativeBankWindow.SetOpen(state);
            }
            else
            {
                CentralBankWindow.SetOpen(state);
            }
            Clicked?.Invoke(state);
        }

        internal static void DestroyButton()
        {
            NativeBankWindow.DestroyWindow();
            TradePostWindow.DestroyWindow();
            if (instance != null && instance.button != null)
            {
                instance.button.Toggle.Switch -= instance.Toggle_Switch;
                instance.button.Tooltip.Unbind();
                DestroyClone(instance.button.gameObject);
                instance.button = null;
                instance.badge = null;
                instance.alert = null;
            }
        }

        private static void DestroyClone(GameObject clone)
        {
            // UI components must unload (deactivate) before being destroyed.
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

        /// <summary>When leaving the game, the game rebuilds the bar; the clone goes with it.</summary>
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
