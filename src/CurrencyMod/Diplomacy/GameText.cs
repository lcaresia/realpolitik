namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Fixed Portuguese texts for the dossier and the viewer (EraName, Of, Inline: always Portuguese).
    /// In the UI, use EraNameUi, OfUi and InlineUi: outside Portuguese they return the name as is, and the preposition
    /// lives in the translation ("Carta {0}" with OfUi → "Carta dos Chou" / "Letter from {0}").
    /// </summary>
    internal static class GameText
    {
        private static readonly string[] EraNames =
        {
            L.N("Era Neolítica"), L.N("Era Antiga"), L.N("Era Clássica"), L.N("Era Medieval"), L.N("Era Moderna"), L.N("Era Industrial"), L.N("Era Contemporânea"),
        };

        internal static System.Collections.Generic.IEnumerable<string> AllEraNames => EraNames;

        internal static string EraName(int eraIndex)
        {
            if (eraIndex < 0)
            {
                return "era desconhecida";
            }
            return eraIndex < EraNames.Length ? EraNames[eraIndex] : $"Era {eraIndex}";
        }

        /// <summary>Era name in the UI language.</summary>
        internal static string EraNameUi(int eraIndex)
        {
            if (eraIndex < 0)
            {
                return L.T("era desconhecida");
            }
            return eraIndex < EraNames.Length ? L.T(EraNames[eraIndex]) : L.F("Era {0}", eraIndex);
        }

        /// <summary>"Os Chou" → "dos Chou"; "Roma" → "de Roma".</summary>
        internal static string Of(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "de ?";
            }
            if (name.StartsWith("Os ")) return "dos " + name.Substring(3);
            if (name.StartsWith("As ")) return "das " + name.Substring(3);
            if (name.StartsWith("O ")) return "do " + name.Substring(2);
            if (name.StartsWith("A ")) return "da " + name.Substring(2);
            return "de " + name;
        }

        /// <summary>Of in the UI: in Portuguese "dos Chou"; in other languages just the name.</summary>
        internal static string OfUi(string name) => L.IsPortuguese ? Of(name) : (string.IsNullOrEmpty(name) ? "?" : name);

        /// <summary>Name in mid-sentence: "Os Chou" → "os Chou" ("com os Chou", "para os Chou").</summary>
        internal static string Inline(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "?";
            }
            if (name.StartsWith("Os ") || name.StartsWith("As ")) return char.ToLowerInvariant(name[0]) + name.Substring(1);
            if (name.StartsWith("O ") || name.StartsWith("A ")) return char.ToLowerInvariant(name[0]) + name.Substring(1);
            return name;
        }

        /// <summary>Inline in the UI: in Portuguese "os Chou"; in other languages just the name.</summary>
        internal static string InlineUi(string name) => L.IsPortuguese ? Inline(name) : (string.IsNullOrEmpty(name) ? "?" : name);
    }
}
