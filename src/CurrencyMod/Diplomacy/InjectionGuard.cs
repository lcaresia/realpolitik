using System.Linq;
using System.Text.RegularExpressions;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Prompt-injection guard for free text that other characters wrote (letters, subjects, demands), melhorias-jogando
    /// item 25. Two layers, both without an AI call:
    /// - a fence: the text goes between markers the rules explain, and lines that imitate the dossier's own structure
    ///   ("== SECTION ==", code fences, json actions) are neutralized;
    /// - a detector: phrases that talk to a language model instead of a leader mark the letter in the dossier. The nation
    ///   is told, never blocked: it may take offence, distrust or denounce it, as the AI-freedom rule asks.
    /// </summary>
    internal static class InjectionGuard
    {
        /// <summary>Opening and closing marks of a fenced text (the rules in Prompts.WorldRules name them).</summary>
        internal const string Open = "<<<";
        internal const string Close = ">>>";

        // Phrases aimed at a model, in the five interface languages. They need an instruction word next to them, so a
        // leader writing "ignore Shaka's provocations" is not flagged.
        private static readonly Regex Suspicious = new Regex(
            // "ignore your/the previous instructions": instruction words always count; rules only when they are the
            // reader's or the game's ("forget your rules", "esqueça as regras do jogo"), not a general's.
            @"\b(ignor\w*|esque[cç]\w*|desconsider\w*|forget|disregard|oubli\w*|vergiss\w*)\b[^.\n]{0,40}\b(instru[cç]\w*|prompts?|anweisung\w*)\b"
            + @"|\b(ignor\w*|esque[cç]\w*|desconsider\w*|forget|disregard|oubli\w*|vergiss\w*)\b[^.\n]{0,20}\b(suas|tuas|your|tus|sus|vos|tes|deine|ihre|anteriores|previous|prior|above)\s+(regras|rules|reglas|r[eè]gles|regeln)\b"
            + @"|\b(ignor\w*|esque[cç]\w*|desconsider\w*|forget|disregard|oubli\w*|vergiss\w*)\b[^.\n]{0,20}\b(regras|rules|reglas|r[eè]gles|regeln)\s+(do|of\s+the|del|du|des)\s+(jogo|game|juego|jeu|spiels)\b"
            + @"|(?m)^\s*==\s*[A-ZÁÉÍÓÚÂÊÔÃÕÇ][^=\n]{2,60}==\s*$"
            + @"|\bsystem\s*prompt\b|\bprompt\s+(do\s+)?sistema\b|\bdeveloper\s+mode\b|\bmodo\s+(desenvolvedor|dev)\b|\bjailbreak\b"
            + @"|\b(voc[eê]|you|t[uú]|vous|du)\s+(agora\s+|now\s+|ahora\s+|maintenant\s+|jetzt\s+)?(é|e|are|eres|es|êtes|bist)\s+(um|uma|an?|un|una|une|ein|eine)\s+(modelo|ia|ai|assistente|assistant|asistente|chatbot|llm|language\s+model|modèle|sprachmodell)\b"
            + @"|\b(responda|respond|answer|responde|répondez|antworte)\w*\s+(apenas|s[oó]|only|solo|seulement|nur)\s+(com|with|con|avec|mit)\b"
            + @"|""\s*(acao|acoes|cartas|diario)\s*""\s*:",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>True when the text reads like instructions to a language model.</summary>
        internal static bool LooksLikeInjection(string text) => !string.IsNullOrEmpty(text) && Suspicious.IsMatch(text);

        /// <summary>The text with the dossier's structural marks neutralized (one line or many).</summary>
        internal static string Neutralize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }
            string clean = text.Replace(Open, "‹‹‹").Replace(Close, "›››").Replace("```", "'''");
            return string.Join("\n", clean.Split('\n').Select(line => line.TrimStart().StartsWith("==") ? "= =" + line.TrimStart().Substring(2) : line));
        }

        /// <summary>A one-line field (subject, demand): neutralized and kept on one line.</summary>
        internal static string Inline(string text) => Neutralize(text).Replace('\n', ' ').Replace('\r', ' ');

        /// <summary>The fenced body of a letter.</summary>
        internal static string Fence(string text) => Open + "\n" + Neutralize(text) + "\n" + Close;
    }
}
