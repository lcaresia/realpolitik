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
    /// Escreve o nome da moeda do jogador ao lado de valores ("Custa 500 Reais [moeda]").
    /// Rótulos genéricos sem número ("Dinheiro total") ficam como estão.
    /// </summary>
    internal static class TextPatches
    {
        internal static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        // Número (com tags de cor opcionais) seguido do ícone de dinheiro, ou ícone seguido de número.
        // O separador de milhar pode ser ponto, vírgula ou espaço fino/inseparável (francês), conforme o idioma do jogo.
        private static readonly Regex AmountThenIcon = new Regex(
            @"(?<num>[+\-−]?\d(?:[\d.,]|[\u00A0\u202F](?=\d))*(?:\s?[kKmM](?![a-zA-Z]))?)(?<tags>(?:</?[^<>\[\]]*>)*)(?<space>\s*)(?<icon>\[(?:money|moneycolored)\])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex IconThenAmount = new Regex(
            @"(?<icon>\[(?:money|moneycolored)\])(?<space>\s*)(?<tags>(?:</?[^<>\[\]]*>)*)(?<num>[+\-−]?\d(?:[\d.,]|[\u00A0\u202F](?=\d))*(?:\s?[kKmM](?![a-zA-Z]))?)(?<close>(?:</[^<>\[\]]*>)*)(?!\s*\p{L})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Plural da moeda do jogador local, atualizado pela janela a cada frame (thread principal).</summary>
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
                return text; // já decorado
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
                    // Texto nunca pode quebrar a interface do jogo.
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
                    Plugin.Log.LogWarning($"Tooltip de dinheiro: {ex.Message}");
                }
            }
        }

        /// <summary>Atualiza os textos cacheados da moeda local. Chamado na thread principal.</summary>
        internal static void RefreshLocalCache(EmpireCurrency mine)
        {
            if (mine == null || SavePatches.IsGameOnline())
            {
                LocalCurrencyPlural = null;
                LocalTooltipLine = null;
                return;
            }
            LocalCurrencyPlural = mine.PluralOrName;
            // Uma linha curta: o rótulo da renda no tooltip de dinheiro tem altura fixa (uma linha a mais invade os
            // títulos) e não quebra linha (com moeda e câmbio juntos o texto vazava dos dois lados do balão).
            LocalTooltipLine = L.F("Inflação {0}  ·  Juros {1}", Format.Percent(mine.InflationRate), Format.Percent(mine.InterestRate));
        }
    }

    /// <summary>Números da interface, na cultura do idioma do mod (L.Culture).</summary>
    internal static class Format
    {
        public static string Percent(double rate) => (rate * 100).ToString("0.0#", L.Culture) + "%";

        public static string SignedPercent(double rate) => (rate >= 0 ? "+" : "") + Percent(rate);

        public static string Money(double value) => value.ToString("#,0", L.Culture);

        public static string SignedMoney(double value) => (value >= 0 ? "+" : "−") + Math.Abs(value).ToString("#,0.#", L.Culture);

        public static string Rate(double value) => value.ToString(value >= 100 ? "#,0" : (value >= 10 ? "0.0" : "0.00"), L.Culture);
    }
}
