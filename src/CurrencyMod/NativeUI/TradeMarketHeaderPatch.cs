using System;
using Amplitude.Mercury.UI;
using Amplitude.UI.Renderers;
using HarmonyLib;
using Utils = Amplitude.Mercury.UI.Utils;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Trade view, market tab: under each empire's name the game only says whether resources can be traded. The line
    /// now shows the relation, the exchange rate and the trade balance; the tooltip keeps the licence and the details
    /// (melhorias-jogando item 22). The licence picto next to it stays.
    /// </summary>
    [HarmonyPatch(typeof(TradeViewMarketEmpireItem), nameof(TradeViewMarketEmpireItem.Refresh))]
    internal static class TradeMarketHeaderPatch
    {
        private static readonly AccessTools.FieldRef<TradeViewMarketEmpireItem, UILabel> LicenseLabel =
            AccessTools.FieldRefAccess<TradeViewMarketEmpireItem, UILabel>("tradeLicenseLabel");

        private static readonly System.Collections.Generic.Dictionary<UILabel, string> licenseTexts = new System.Collections.Generic.Dictionary<UILabel, string>();

        private static bool loggedError;

        private static void Postfix(TradeViewMarketEmpireItem __instance)
        {
            try
            {
                UILabel label = LicenseLabel(__instance);
                if (label == null || !NativeBankWindow.TradeHeader(__instance.EmpireIndex, out string line, out string details))
                {
                    return;
                }
                // The game rewrites the licence text only when the licence changes: remember it (a %key, localized here).
                string text = label.Text ?? string.Empty;
                if (text != line)
                {
                    int cut = text.IndexOf("  ·  ", StringComparison.Ordinal);
                    text = cut >= 0 ? text.Substring(0, cut) : text; // leftover of an older version of this patch
                    licenseTexts[label] = text.StartsWith("%") ? Utils.TextUtils.Localize(text) : text;
                }
                licenseTexts.TryGetValue(label, out string license);
                label.Text = line;
                NativeUIKit.Tip(label.transform, license ?? L.T("Comércio"), details);
            }
            catch (Exception ex)
            {
                if (!loggedError)
                {
                    loggedError = true;
                    Plugin.Log.LogError($"Trade market header: {ex}");
                }
            }
        }
    }
}
