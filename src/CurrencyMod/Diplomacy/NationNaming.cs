using System;
using System.Collections.Generic;
using System.Linq;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Fixed nation names (melhorias item 39). In Humankind the empire is named after its culture, which changes every
    /// era, so "the Romans" of today had another name before. Each nation gets one name for the whole game: the player
    /// keeps the first people they played (the name they already use), the AI nations draw an invented country name.
    /// The mod (dossier, letters, council, mod screens) shows the fixed name; the current culture becomes a detail.
    /// </summary>
    internal static class NationNaming
    {
        /// <summary>Invented, plausible country names with no accents (they read the same in every UI language) and no
        /// clash with the game's real cultures.</summary>
        private static readonly string[] Pool =
        {
            "Valdoria", "Kessaria", "Belmara", "Dravenia", "Estmar", "Falkrest", "Galdria", "Halvoria", "Istrenia",
            "Jorvalia", "Karsten", "Lusmera", "Morvania", "Norvessa", "Pellaria", "Quessar", "Rovenna", "Sardania",
            "Tavrenia", "Ulmora", "Westmark", "Zarkhan", "Brevonia", "Calderra", "Ebrania", "Ferrona", "Gorvane",
            "Kestria", "Lorvenia", "Marvessa", "Nordhal", "Ostmaria", "Revania", "Selvara", "Torvaldia", "Vastria",
            "Yllaria", "Zennor", "Arvelia", "Corvanta",
        };

        private static readonly HashSet<string> Articles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "os", "as", "o", "a", "the", "les", "los", "las", "die", "der", "das", "le", "la", "il", "i", "gli",
        };

        /// <summary>
        /// Puts the fixed name on a captured empire: Culture becomes the fixed name (every screen and prompt that names
        /// the nation by its people now shows it), the game's culture moves to RealCulture, and the full name ends with
        /// the fixed name ("Rei Ancestral (Valdoria)"). Safe to call again on the same snapshot.
        /// </summary>
        internal static void Apply(AiWorld world, CapturedEmpire empire)
        {
            if (empire.RealCulture == null)
            {
                empire.RealCulture = empire.Culture;
            }
            string name = NameFor(world, empire);
            empire.NationName = name;
            empire.Culture = name;
            string full = empire.FullName ?? string.Empty;
            int open = full.LastIndexOf('(');
            string title = open > 0 ? full.Substring(0, open).TrimEnd() : full.Trim();
            empire.FullName = string.IsNullOrEmpty(title) ? name : $"{title} ({name})";
        }

        /// <summary>The nation's fixed name, drawn (or taken from the player's first people) the first time it is needed.</summary>
        internal static string NameFor(AiWorld world, CapturedEmpire empire)
        {
            if (world.NationNames.TryGetValue(empire.Index, out string name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
            if (empire.Human)
            {
                string first = world.CultureNames.TryGetValue(empire.Index, out List<string> seen) && seen.Count > 0 ? seen[0] : empire.RealCulture;
                name = WithoutArticle(first);
            }
            if (string.IsNullOrWhiteSpace(name) || name == "cultura desconhecida")
            {
                name = Draw(world.GameGuid, empire.Index, world.NationNames.Values);
            }
            world.NationNames[empire.Index] = name;
            return name;
        }

        /// <summary>"Os Máuria" → "Máuria".</summary>
        internal static string WithoutArticle(string people)
        {
            if (string.IsNullOrWhiteSpace(people))
            {
                return null;
            }
            string trimmed = people.Trim();
            int space = trimmed.IndexOf(' ');
            return space > 0 && Articles.Contains(trimmed.Substring(0, space)) ? trimmed.Substring(space + 1).Trim() : trimmed;
        }

        private static string Draw(string guid, int index, IEnumerable<string> taken)
        {
            var used = new HashSet<string>(taken.Where(t => t != null), StringComparer.OrdinalIgnoreCase);
            int start = (int)(Fnv((guid ?? string.Empty) + "#" + index) % (uint)Pool.Length);
            for (int step = 0; step < Pool.Length; step++)
            {
                string candidate = Pool[(start + step) % Pool.Length];
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
            }
            return Pool[start] + " " + (index + 1);
        }

        /// <summary>Stable across runtimes (string.GetHashCode is not guaranteed to be).</summary>
        private static uint Fnv(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }
}
