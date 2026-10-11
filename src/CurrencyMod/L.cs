using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Amplitude.Framework.Localization;
using Path = System.IO.Path;
using Services = Amplitude.Framework.Services;
using BepInEx.Configuration;
using Newtonsoft.Json;

namespace CurrencyMod
{
    /// <summary>
    /// Translation of the mod's UI. The Portuguese text in the code is the key itself:
    ///   L.T("Banco Central")            → "Central Bank" in English;
    ///   L.F("Juros de {0}%", taxa)      → string.Format with the translated text and the language's culture;
    ///   L.N("Ministros")                → only marks the text for extraction (static lists); translate with L.T when showing it.
    /// The tables live in the core itself (Lang\en.json, es.json, fr.json, de.json: {"text in pt": "translation"}).
    /// A file at BepInEx\CurrencyModCore\lang\&lt;code&gt;.json, if present, overrides them (community translations).
    /// Untranslated text falls back to Portuguese and goes into the missing list (command "language missing").
    /// </summary>
    internal static class L
    {
        internal static readonly string[] Supported = { "pt", "en", "es", "fr", "de" };

        internal static ConfigEntry<string> Language;

        private static volatile Dictionary<string, string> table;
        private static volatile string code = "pt";
        private static volatile CultureInfo culture = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly HashSet<string> missing = new HashSet<string>();
        private static float nextCheck;

        /// <summary>Language in use: pt, en, es, fr or de.</summary>
        internal static string Code => code;

        /// <summary>The language's colon: French takes a space before it (" : ").</summary>
        internal static string Colon => code == "fr" ? " : " : ": ";

        /// <summary>Culture for numbers and dates in the language in use (decimal comma in pt, es, fr, de; dot in en).</summary>
        internal static CultureInfo Culture => culture;

        internal static bool IsPortuguese => code == "pt";

        /// <summary>Language name in English, to ask the AI nations to write in it.</summary>
        internal static string EnglishName(string language)
        {
            switch (language)
            {
                case "en": return "English";
                case "es": return "Spanish";
                case "fr": return "French";
                case "de": return "German";
                default: return "Brazilian Portuguese";
            }
        }

        internal static void Bind(ConfigFile config)
        {
            Language = config.Bind("Interface", "Idioma", "Auto",
                new ConfigDescription(
                    "Language of the mod / Idioma do mod: Auto (same as the game / o mesmo do jogo), pt, en, es, fr, de.",
                    new AcceptableValueList<string>("Auto", "pt", "en", "es", "fr", "de")));
            lock (missing)
            {
                missing.Clear();
            }
            table = null;
            nextCheck = 0f;
            Refresh(force: true);
        }

        /// <summary>Checks the game's language every so often (main thread; called by DevTools.Update).</summary>
        internal static void Tick(float now)
        {
            if (now < nextCheck)
            {
                return;
            }
            nextCheck = now + 2f;
            Refresh(force: false);
        }

        internal static void Refresh(bool force)
        {
            string wanted = Resolve();
            if (!force && wanted == code && (table != null || wanted == "pt"))
            {
                return;
            }
            if (wanted != code)
            {
                lock (missing)
                {
                    missing.Clear();
                }
            }
            table = wanted == "pt" ? null : Load(wanted);
            culture = CultureInfo.GetCultureInfo(CultureName(wanted));
            code = wanted;
        }

        private static string Resolve()
        {
            string configured = Language?.Value ?? "Auto";
            if (!string.Equals(configured, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                string fixedCode = configured.Trim().ToLowerInvariant();
                return Array.IndexOf(Supported, fixedCode) >= 0 ? fixedCode : "en";
            }
            try
            {
                string game = Services.GetService<ILocalizationService>()?.CurrentLanguage;
                if (string.IsNullOrEmpty(game))
                {
                    return code;
                }
                string prefix = game.Substring(0, Math.Min(2, game.Length)).ToLowerInvariant();
                // Game language the mod does not have yet: English.
                return Array.IndexOf(Supported, prefix) >= 0 ? prefix : "en";
            }
            catch (Exception)
            {
                return code;
            }
        }

        private static string CultureName(string language)
        {
            switch (language)
            {
                case "en": return "en-US";
                case "es": return "es-ES";
                case "fr": return "fr-FR";
                case "de": return "de-DE";
                default: return "pt-BR";
            }
        }

        private static Dictionary<string, string> Load(string language)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("lang." + language + ".json"))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            Merge(result, reader.ReadToEnd());
                        }
                    }
                }
                string overridePath = Path.Combine(Path.Combine(Path.Combine(BepInEx.Paths.BepInExRootPath, "CurrencyModCore"), "lang"), language + ".json");
                if (File.Exists(overridePath))
                {
                    Merge(result, File.ReadAllText(overridePath));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"Language {language}: table not loaded ({ex.Message}). Texts stay in Portuguese.");
            }
            return result;
        }

        private static void Merge(Dictionary<string, string> into, string json)
        {
            var entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (entries == null)
            {
                return;
            }
            foreach (KeyValuePair<string, string> entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.Value))
                {
                    into[entry.Key] = entry.Value;
                }
            }
        }

        /// <summary>Translates a UI text (the Portuguese is the key).</summary>
        internal static string T(string pt)
        {
            Dictionary<string, string> current = table;
            if (current == null || string.IsNullOrEmpty(pt))
            {
                return pt;
            }
            if (current.TryGetValue(pt, out string translated))
            {
                return translated;
            }
            lock (missing)
            {
                missing.Add(pt);
            }
            return pt;
        }

        /// <summary>Translates and formats: L.F("{0} cartas novas", n). Numbers come out in the language's culture.</summary>
        internal static string F(string pt, params object[] args)
        {
            string format = T(pt);
            try
            {
                return string.Format(culture, format, args);
            }
            catch (FormatException)
            {
                // Translation with wrong placeholders: use the Portuguese, which always matches the arguments.
                return string.Format(culture, pt, args);
            }
        }

        /// <summary>Only marks the text for extraction (static lists); returns the Portuguese itself.</summary>
        internal static string N(string pt) => pt;

        /// <summary>Requested texts that have no translation in the current language (for the command "language missing").</summary>
        internal static List<string> Missing()
        {
            lock (missing)
            {
                return new List<string>(missing);
            }
        }

        internal static int TableSize => table?.Count ?? 0;
    }
}
