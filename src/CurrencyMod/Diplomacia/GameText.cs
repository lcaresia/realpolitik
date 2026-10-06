namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Textos fixos em português para o dossiê e o visualizador (EraName, Of, Inline: sempre português).
    /// Na interface, use EraNameUi, OfUi e InlineUi: fora do português eles devolvem o nome como está, e a preposição
    /// fica na tradução ("Carta {0}" com OfUi → "Carta dos Chou" / "Letter from {0}").
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

        /// <summary>Nome da era no idioma da interface.</summary>
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

        /// <summary>Of na interface: em português "dos Chou"; nos outros idiomas só o nome.</summary>
        internal static string OfUi(string name) => L.IsPortuguese ? Of(name) : (string.IsNullOrEmpty(name) ? "?" : name);

        /// <summary>Nome no meio da frase: "Os Chou" → "os Chou" ("com os Chou", "para os Chou").</summary>
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

        /// <summary>Inline na interface: em português "os Chou"; nos outros idiomas só o nome.</summary>
        internal static string InlineUi(string name) => L.IsPortuguese ? Inline(name) : (string.IsNullOrEmpty(name) ? "?" : name);
    }
}
