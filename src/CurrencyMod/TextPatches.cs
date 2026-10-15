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

            /// <summary>Height of the native income label (one line, as the prefab ships it).</summary>
            private const float NativeNetHeight = 48f;

            /// <summary>One line of the label's font (size 22).</summary>
            private const float LineHeight = 23f;

            /// <summary>Average glyph width of that font and the usable width of the 360 px bubble.</summary>
            private const float CharWidth = 11f;
            private const float UsableWidth = 340f;

            private static readonly Regex Markup = new Regex(@"<[^<>]*>|\[[^\[\]]*\]", RegexOptions.Compiled);

            /// <summary>Lines the text takes once wrapped (estimated: the label only measures itself once visible).</summary>
            private static int VisualLines(string text)
            {
                int lines = 0;
                foreach (string part in text.Split('\n'))
                {
                    int chars = Markup.Replace(part, "##").Length;
                    lines += Math.Max(1, (int)Math.Ceiling(chars * CharWidth / UsableWidth));
                }
                return lines;
            }

            private static void Postfix(MoneyTooltipBrick __instance, bool __result)
            {
                string line = LocalTooltipLine;
                if (!__result || !EconomyConfig.EnableTooltipLine.Value || string.IsNullOrEmpty(line))
                {
                    // Without the mod's lines (online game, line off), the label goes back to its native one-line box.
                    UILabel native = __result ? NetLabel(__instance) : null;
                    if (native != null)
                    {
                        native.WordWrap = false;
                        FitHeight(__instance, native, NativeNetHeight);
                    }
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
                        string interestPart = Math.Abs(interest) >= 0.05 ? "\n" + L.F("{0}/turno de juros", Format.SignedMoney(interest)) : string.Empty;
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
                                transfers = "\n" + L.T("Último turno (não se repete):") + "\n" + string.Join("\n", parts);
                            }
                        }
                        label.Text = text + "\n" + line + interestPart + transfers;
                        // One line height per value: the native label has a fixed one-line height. Wrap is on for the
                        // long gift lines (German), so each value counts the lines it wraps into.
                        label.AutoAdjustHeight = false;
                        label.WordWrap = true;
                        FitHeight(__instance, label, Math.Max(NativeNetHeight, VisualLines(label.Text) * LineHeight + 4f));
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Money tooltip: {ex.Message}");
                }
            }

            /// <summary>
            /// Sets the income label's height and grows the brick to match. The brick's table layout ships disabled (the
            /// rows are placed once in the prefab), so it would not grow by itself and the next section was drawn over the
            /// extra lines; the income label is the last row, so the brick ends where it ends. The bricks table above it
            /// re-stacks the bricks when the brick's size changes.
            /// </summary>
            private static void FitHeight(MoneyTooltipBrick brick, UILabel label, float height)
            {
                Amplitude.UI.UITransform box = label?.GetComponent<Amplitude.UI.UITransform>();
                Amplitude.UI.UITransform frame = brick.GetComponent<Amplitude.UI.UITransform>();
                if (box == null || frame == null)
                {
                    return;
                }
                if (Math.Abs(box.Height - height) > 0.5f)
                {
                    box.Height = height;
                }
                float bottom = box.Y + height;
                if (Math.Abs(frame.Height - bottom) > 0.5f)
                {
                    frame.Height = bottom;
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
            // One value per line: the native income label does not wrap, so values side by side spilled out of the
            // bubble (Discord report, English UI); the tooltip patch lets the label grow to fit the lines.
            LocalTooltipLine = L.F("Inflação {0}", Format.Percent(mine.InflationRate)) + "\n" + L.F("Juros {0}", Format.Percent(mine.InterestRate));
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
