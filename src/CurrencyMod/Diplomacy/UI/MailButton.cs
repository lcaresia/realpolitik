using System;
using System.Linq;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Tooltips;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using CurrencyMod.NativeUI;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod.Diplomacy.UI
{
    /// <summary>
    /// Diplomatic Mail button on the bottom control bar, next to the Central Bank: a clone of the game's "Trade"
    /// button, with an envelope icon (SDF) and a badge with the number of new letters.
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
            if (MailScreen.Instance != null && NativeUIKit.ShortcutPressed(AiConfig.MailKey.Value, MailScreen.IsOpen))
            {
                MailScreen.SetOpen(!MailScreen.IsOpen);
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
            if (MailScreen.Instance == null)
            {
                try
                {
                    MailScreen.Create();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to create the mail screen: {ex}");
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
            Plugin.Log.LogInfo("Diplomatic Mail button created on the control bar.");
        }

        private bool shownDown;

        private void RefreshBadge()
        {
            int count = AiModule.Instance?.PlayerUnreadCount() ?? 0;
            // AI disconnected (the whole queue failing): the badge becomes a red X until it comes back.
            bool down = Llm.Providers.ProviderRouter.AllDown;
            if (count == shownCount && down == shownDown && !down)
            {
                return;
            }
            shownCount = count;
            shownDown = down;
            RefreshTooltip(count);
            if (badge == null)
            {
                return;
            }
            NativeUIKit.SetBadge(badge, down ? "X" : count <= 0 ? null : count > 9 ? "9+" : count.ToString());
        }

        private void RefreshTooltip(int unread)
        {
            tooltipTarget.Title = L.T("Correio Diplomático") + $" ({AiConfig.MailKey.Value})";
            string news = unread == 0 ? L.T("Nenhuma carta nova.") : unread == 1 ? L.T("1 carta nova.") : L.F("{0} cartas novas.", unread);
            AiModule module = AiModule.Instance;
            int cites = module == null ? 0 : module.PlayerInbox().Count(l => l.IsPublic && !l.ReadBy.Contains(module.PlayerIndex)
                && Mentions.Cites(l, Capture.TurnCapture.Latest, AiModule.World, module.PlayerIndex));
            if (cites > 0)
            {
                news += " " + (cites == 1 ? L.T("Um comunicado público cita você.") : L.F("{0} comunicados públicos citam você.", cites));
            }
            if (Llm.Providers.ProviderRouter.AllDown)
            {
                int minutes = Llm.Providers.ProviderRouter.MinutesToProbe;
                news = "<c=E8685E>" + L.T("IA desconectada") + "</c> — " + L.F("nenhum provedor respondeu ({0}).", Llm.Providers.ProviderRouter.AllDownReason ?? "?") + " "
                    + (minutes <= 0 ? L.T("Testando de novo agora.") : L.F("Nova tentativa em {0} min.", minutes)) + " "
                    + L.T("Enquanto isso, a IA nativa do jogo joga pelas nações e as cartas ficam sem resposta.") + "\n" + news;
            }
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

        /// <summary>When leaving the match, the game rebuilds the bar; the button and the window go with it.</summary>
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
