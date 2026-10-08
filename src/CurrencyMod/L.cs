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
    /// Tradução da interface do mod. O texto em português do código é a própria chave:
    ///   L.T("Banco Central")            → "Central Bank" em inglês;
    ///   L.F("Juros de {0}%", taxa)      → string.Format com o texto traduzido e a cultura do idioma;
    ///   L.N("Ministros")                → só marca o texto para a extração (listas estáticas); traduza com L.T na hora de mostrar.
    /// As tabelas ficam no próprio núcleo (Lang\en.json, es.json, fr.json, de.json: {"texto em pt": "tradução"}).
    /// Um arquivo em BepInEx\CurrencyModCore\lang\&lt;código&gt;.json, se existir, passa por cima (traduções da comunidade).
    /// Texto sem tradução cai no português e entra na lista de faltando (comando "idioma faltando").
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

        /// <summary>Idioma em uso: pt, en, es, fr ou de.</summary>
        internal static string Code => code;

        /// <summary>Dois-pontos do idioma: o francês leva espaço antes (" : ").</summary>
        internal static string Colon => code == "fr" ? " : " : ": ";

        /// <summary>Cultura dos números e datas no idioma em uso (vírgula decimal em pt, es, fr, de; ponto em en).</summary>
        internal static CultureInfo Culture => culture;

        internal static bool IsPortuguese => code == "pt";

        /// <summary>Nome do idioma em inglês, para pedir às nações da IA que escrevam nele.</summary>
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

        /// <summary>Confere o idioma do jogo de tempos em tempos (thread principal; chamado pelo DevTools.Update).</summary>
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
                // Idioma do jogo que o mod ainda não tem: inglês.
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
                Plugin.Log?.LogWarning($"Idioma {language}: tabela não carregada ({ex.Message}). Os textos ficam em português.");
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

        /// <summary>Traduz um texto da interface (o português é a chave).</summary>
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

        /// <summary>Traduz e formata: L.F("{0} cartas novas", n). Os números saem na cultura do idioma.</summary>
        internal static string F(string pt, params object[] args)
        {
            string format = T(pt);
            try
            {
                return string.Format(culture, format, args);
            }
            catch (FormatException)
            {
                // Tradução com marcadores errados: usa o português, que sempre bate com os argumentos.
                return string.Format(culture, pt, args);
            }
        }

        /// <summary>Só marca o texto para a extração (listas estáticas); devolve o próprio português.</summary>
        internal static string N(string pt) => pt;

        /// <summary>Textos pedidos que não têm tradução no idioma atual (para o comando "idioma faltando").</summary>
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
