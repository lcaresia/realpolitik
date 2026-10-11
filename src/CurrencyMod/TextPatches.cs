using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.UI.Tooltips;
using Amplitude.UI.Renderers;
using HarmonyLib;

namespace CurrencyMod
{
    /// <summary>
    /// Writes the player's currency name next to amounts ("Costs 500 Reais [coin]").
    /// Generic labels with no number ("Total money") stay as they are.
    /// </summary>
    internal static class TextPatches
    {
        internal static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        // Number (with optional color tags) followed by the money icon, or icon followed by number.
        // The thousands separator can be a dot, comma or thin/non-breaking space (French), depending on the game language.
        private static readonly Regex AmountThenIcon = new Regex(
            @"(?<num>[+\-−]?\d(?:[\d.,]|[\u00A0\u202F](?=\d))*(?:\s?[kKmM](?![a-zA-Z]))?)(?<tags>(?:</?[^<>\[\]]*>)*)(?<space>\s*)(?<icon>\[(?:money|moneycolored)\])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex IconThenAmount = new Regex(
            @"(?<icon>\[(?:money|moneycolored)\])(?<space>\s*)(?<tags>(?:</?[^<>\[\]]*>)*)(?<num>[+\-−]?\d(?:[\d.,]|[\u00A0\u202F](?=\d))*(?:\s?[kKmM](?![a-zA-Z]))?)(?<close>(?:</[^<>\[\]]*>)*)(?!\s*\p{L})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Plural of the local player's currency, updated by the window every frame (main thread).</summary>
        internal static volatile string LocalCurrencyPlural;
        internal static volatile string LocalTooltipLine;

        internal static string Decorate(string text)
        {
            string plural = LocalCurrencyPlural;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(plural) || text.IndexOf("oney", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return text;
            }
            if (text.IndexOf(plural, StringComparison.Ordinal) >= 0)
            {
                return text; // already decorated
            }

            bool matched = false;
            string result = AmountThenIcon.Replace(text, m =>
            {
                matched = true;
                return $"{m.Groups["num"].Value} {plural}{m.Groups["tags"].Value} {m.Groups["icon"].Value}";
            });
            if (!matched)
            {
                result = IconThenAmount.Replace(text, m =>
                    $"{m.Groups["icon"].Value}{m.Groups["space"].Value}{m.Groups["tags"].Value}{m.Groups["num"].Value}{m.Groups["close"].Value} {plural}");
            }
            return result;
        }

        [HarmonyPatch]
        private static class LabelPatch
        {
            private static MethodBase TargetMethod() => AccessTools.Method(typeof(UILabel), "LocalizeIfNecessary");

            private static void Postfix(ref string __result)
            {
                if (!EconomyConfig.EnableTextCurrency.Value)
                {
                    return;
                }
                try
                {
                    __result = Decorate(__result);
                }
                catch (Exception)
                {
                    // Text must never break the game UI.
                }
            }
        }

        [HarmonyPatch(typeof(MoneyTooltipBrick), "Bind")]
        private static class MoneyTooltipPatch
        {
            private static readonly AccessTools.FieldRef<MoneyTooltipBrick, UILabel> NetLabel =
                AccessTools.FieldRefAccess<MoneyTooltipBrick, UILabel>("moneyNetLabel");

            private static void Postfix(MoneyTooltipBrick __instance, bool __result)
            {
                string line = LocalTooltipLine;
                if (!__result || !EconomyConfig.EnableTooltipLine.Value || string.IsNullOrEmpty(line))
                {
                    return;
                }
                try
                {
                    UILabel label = NetLabel(__instance);
                    if (label != null && !label.Text.Contains(line))
                    {
                        label.Text = label.Text + "\n" + line;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Money tooltip: {ex.Message}");
                }
            }
        }

        /// <summary>Updates the cached texts of the local currency. Called on the main thread.</summary>
        internal static void RefreshLocalCache(EmpireCurrency mine)
        {
            if (mine == null || SavePatches.IsGameOnline())
            {
                LocalCurrencyPlural = null;
                LocalTooltipLine = null;
                return;
            }
            LocalCurrencyPlural = mine.PluralOrName;
            // One short line: the income label in the money tooltip has a fixed height (one extra line overlaps the
            // titles) and does not wrap (with currency and exchange rate together the text spilled out of both sides of the bubble).
            LocalTooltipLine = L.F("Inflação {0}  ·  Juros {1}", Format.Percent(mine.InflationRate), Format.Percent(mine.InterestRate));
        }
    }

    /// <summary>UI numbers, in the culture of the mod's language (L.Culture).</summary>
    internal static class Format
    {
        public static string Percent(double rate) => (rate * 100).ToString("0.0#", L.Culture) + "%";

        public static string SignedPercent(double rate) => (rate >= 0 ? "+" : "") + Percent(rate);

        public static string Money(double value) => value.ToString("#,0", L.Culture);

        public static string SignedMoney(double value) => (value >= 0 ? "+" : "−") + Math.Abs(value).ToString("#,0.#", L.Culture);

        public static string Rate(double value) => value.ToString(value >= 100 ? "#,0" : (value >= 10 ? "0.0" : "0.00"), L.Culture);
    }
}
