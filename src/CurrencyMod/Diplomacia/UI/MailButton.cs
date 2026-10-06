using System;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Tooltips;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using CurrencyMod.NativeUI;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod.Diplomacia.UI
{
    /// <summary>
    /// Botão do Correio Diplomático na barra de controle inferior, ao lado do Banco Central: clone do botão
    /// "Comercializar" do jogo, com ícone de envelope (SDF) e um selo com o número de cartas novas.
    /// </summary>
    internal class MailButton : MonoBehaviour
    {
        private const string CloneName = "CurrencyMod_MailToggle";

        private static MailButton instance;

        private ControlBannerLayerToggle button;
        private Texture2D iconTexture;
        private Amplitude.Framework.Guid iconGuid;
        private readonly TitleAndDescription tooltipTarget = new TitleAndDescription();
        private Transform badge;
        private int shownCount = -1;
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
            if (MailScreen.Instance == null)
            {
                try
                {
                    MailScreen.Create();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Falha ao criar a tela do correio: {ex}");
                    enabled = false;
                    return;
                }
            }
            bool open = MailScreen.IsOpen;
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
                iconTexture = SdfIcons.Letter();
                iconGuid = UIRenderingManager.Instance.RegisterTexture(iconTexture);
            }
            button.SetIcon(new UITexture(iconGuid, UITextureFlags.AlphaStraight, UITextureColorFormat.Srgb, iconTexture));
            button.Toggle.State = false;
            button.Toggle.Switch += Toggle_Switch;
            RefreshTooltip(0);
            button.Tooltip.Bind(TooltipUtils.TitleAndDescription, tooltipTarget);
            badge = NativeUIKit.CreateBadge(cloneTransform.transform, "MailBadge");
            button.Show(instant: true);
            Plugin.Log.LogInfo("Botão do Correio Diplomático criado na barra de controle.");
        }

        private void RefreshBadge()
        {
            int count = IaModule.Instance?.PlayerUnreadCount() ?? 0;
            if (count == shownCount)
            {
                return;
            }
            shownCount = count;
            RefreshTooltip(count);
            if (badge == null)
            {
                return;
            }
            NativeUIKit.SetBadge(badge, count <= 0 ? null : count > 9 ? "9+" : count.ToString());
        }

        private void RefreshTooltip(int unread)
        {
            tooltipTarget.Title = L.T("Correio Diplomático");
            string news = unread == 0 ? L.T("Nenhuma carta nova.") : unread == 1 ? L.T("1 carta nova.") : L.F("{0} cartas novas.", unread);
            tooltipTarget.Description = news + "\n" + L.T("Leia e responda as cartas das nações, escreva para elas ou recuse a correspondência de alguém.");
        }

        private void Toggle_Switch(IUIToggle source, bool state)
        {
            MailScreen.SetOpen(state);
        }

        internal static void DestroyButton()
        {
            MailScreen.DestroyWindow();
            if (instance != null && instance.button != null)
            {
                instance.button.Toggle.Switch -= instance.Toggle_Switch;
                instance.button.Tooltip.Unbind();
                DestroyClone(instance.button.gameObject);
                instance.button = null;
                instance.badge = null;
                instance.shownCount = -1;
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

        /// <summary>Ao sair da partida, o jogo reconstrói a barra; o botão e a janela saem junto.</summary>
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
