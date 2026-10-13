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
                        // The balance interest of the next end of turn, even when it is below the half point the game's
                        // breakdown drops (melhorias-jogando item 21), and the per-turn income in red when negative.
                        var sandbox = Amplitude.Mercury.Sandbox.SandboxManager.Sandbox;
                        Amplitude.Mercury.Simulation.MajorEmpire me = sandbox != null ? Amplitude.Mercury.Sandbox.Sandbox.MajorEmpires[sandbox.LocalEmpireIndex] : null;
                        string text = label.Text;
                        if (me != null && (float)NativeEffects.DisplayedMoneyNet(me) < 0 && !text.StartsWith("<c="))
                        {
                            text = $"<c={NegativeColor}>{text}</c>";
                        }
                        double interest = me != null ? NativeEffects.ProjectedInterest(me) : 0;
                        string interestPart = Math.Abs(interest) >= 0.05 ? "  ·  " + L.F("{0}/turno de juros", Format.SignedMoney(interest)) : string.Empty;
                        string transfers = string.Empty;
                        if (me != null)
                        {
                            NativeEffects.LastTurnTransfers(me, Amplitude.Mercury.Sandbox.SandboxManager.Sandbox.Turn, out double received, out double given);
                            var parts = new System.Collections.Generic.List<string>();
                            if (received >= 0.5)
                            {
                                parts.Add(L.F("{0} recebidos em presentes e tributos", Format.SignedMoney(received)));
                            }
                            if (given >= 0.5)
                            {
                                parts.Add($"<c={NegativeColor}>" + L.F("{0} dados em presentes e tributos", Format.SignedMoney(-given)) + "</c>");
                            }
                            if (parts.Count > 0)
                            {
                                transfers = "\n" + L.T("Último turno (não se repete):") + " " + string.Join("  ·  ", parts);
                            }
                        }
                        label.Text = text + "\n" + line + interestPart + transfers;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Money tooltip: {ex.Message}");
                }
            }
        }

        /// <summary>Negative money in the native tooltips (the mod's usual red).</summary>
        internal const string NegativeColor = "E8685E";

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
