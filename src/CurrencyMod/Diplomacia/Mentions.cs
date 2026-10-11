using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacia.Capture;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Se uma carta cita um império: o nome do líder, o nome do povo (atual ou de eras anteriores) ou uma cidade dele.
    /// Sem acento e sem caixa; nomes de 5+ letras aceitam um erro de digitação ("Icaresia" por "lcaresia"). Usado para
    /// destacar os comunicados públicos que falam do jogador. Sem custo de API.
    /// </summary>
    internal static class Mentions
    {
        /// <summary>Palavras dos nomes de povo que não identificam ninguém ("Os Chou" → só "chou").</summary>
        private static readonly HashSet<string> Articles = new HashSet<string>
        {
            "os", "as", "o", "a", "de", "do", "da", "dos", "das", "e", "the", "of", "les", "le", "la", "los", "las", "el", "die", "der", "des",
            "povo", "imperio", "reino", "tribo", "nomade", "nomades", "lider", "governante", "supremo",
        };

        private static readonly Dictionary<string, bool> cache = new Dictionary<string, bool>();

        internal static bool Cites(Letter letter, WorldCapture capture, IaWorld world, int empire)
        {
            if (letter == null || capture == null || letter.From == empire)
            {
                return false;
            }
            CapturedEmpire target = capture.Empire(empire);
            List<string> cities = target?.Cities.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();
            List<string> cultures = world?.CultureNames != null && world.CultureNames.TryGetValue(empire, out List<string> seen) ? seen : new List<string>();
            string leader = target?.Leader;
            string key = letter.Id + ":" + empire + ":" + leader + ":" + cultures.Count + ":" + cities.Count;
            if (cache.TryGetValue(key, out bool known))
            {
                return known;
            }
            List<string> words = Words(letter.Subject + " " + letter.Text);
            bool cites = MatchesName(words, leader)
                || MatchesName(words, target?.Culture)
                || cultures.Any(c => MatchesName(words, c))
                || cities.Any(c => MatchesPhrase(words, c));
            cache[key] = cites;
            return cites;
        }

        /// <summary>Alguma palavra significativa do nome aparece no texto (com tolerância de uma letra nas longas).</summary>
        private static bool MatchesName(List<string> words, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            foreach (string part in Words(name).Where(p => p.Length >= 3 && !Articles.Contains(p)))
            {
                if (words.Any(w => w == part || part.Length >= 5 && Math.Abs(w.Length - part.Length) <= 1 && Distance(w, part) <= 1))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>O nome inteiro (todas as palavras, em sequência) aparece no texto: cidades como "São Paulo".</summary>
        private static bool MatchesPhrase(List<string> words, string phrase)
        {
            List<string> parts = Words(phrase);
            if (parts.Count == 0 || parts.Sum(p => p.Length) < 4)
            {
                return false;
            }
            for (int i = 0; i + parts.Count <= words.Count; i++)
            {
                bool all = true;
                for (int j = 0; j < parts.Count && all; j++)
                {
                    all = words[i + j] == parts[j];
                }
                if (all)
                {
                    return true;
                }
            }
            return false;
        }

        internal static List<string> Words(string text)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return words;
            }
            var current = new StringBuilder();
            foreach (char c in text.Normalize(NormalizationForm.FormD))
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue; // acento
                }
                if (char.IsLetterOrDigit(c))
                {
                    current.Append(char.ToLowerInvariant(c));
                }
                else if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Length = 0;
                }
            }
            if (current.Length > 0)
            {
                words.Add(current.ToString());
            }
            return words;
        }

        /// <summary>Distância de edição (Levenshtein) entre duas palavras curtas.</summary>
        private static int Distance(string a, string b)
        {
            int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
            int[] current = new int[b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                int[] swap = previous;
                previous = current;
                current = swap;
            }
            return previous[b.Length];
        }
    }
}
