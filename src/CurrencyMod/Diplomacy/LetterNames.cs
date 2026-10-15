using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Last pass on an AI letter before it is sent (melhorias item 48). The validator asks the AI to rewrite letters
    /// with dossier codes, but after the retries a letter could still go out with "E3" in the text. Here every code
    /// becomes the name the player sees (E3 → the nation's fixed name, C14 → the city, T31 → the territory, A7 → "o
    /// exército de ..."), and the signature "rainha dos Harapeanos" becomes "rainha de Kestria".
    /// </summary>
    internal static class LetterNames
    {
        private static readonly Regex Codes = new Regex(@"\b([ECTA])(\d{1,4})\b", RegexOptions.Compiled);

        internal static string Humanize(string text, AiWorld world, WorldCapture capture, int sender)
        {
            if (string.IsNullOrEmpty(text) || capture == null)
            {
                return text;
            }
            text = Codes.Replace(text, match => Name(match.Groups[1].Value[0], int.Parse(match.Groups[2].Value), match.Value, world, capture) ?? match.Value);
            text = RepeatedName.Replace(text, "$1");
            return FixSignature(text, capture.Empire(sender));
        }

        /// <summary>
        /// Only the code pass, for texts shown to the player that are not letters (bloc motions, council speeches): the
        /// player never sees the dossier codes, so "postos para E15" reads "postos para Valdoria".
        /// </summary>
        internal static string NamesOnly(string text)
        {
            WorldCapture capture = TurnCapture.Latest;
            if (string.IsNullOrEmpty(text) || capture == null)
            {
                return text;
            }
            AiWorld world = AiModule.World;
            string named = Codes.Replace(text, match => Name(match.Groups[1].Value[0], int.Parse(match.Groups[2].Value), match.Value, world, capture) ?? match.Value);
            // "Huayto (T29)" became "Huayto (Huayto)": keep one.
            return named == text ? text : RepeatedName.Replace(named, "$1");
        }

        private static readonly Regex RepeatedName = new Regex(@"(\p{L}[\p{L}'’\-]*(?: \p{L}[\p{L}'’\-]*){0,3}) \(\1\)", RegexOptions.Compiled);

        private static string Name(char kind, int number, string code, AiWorld world, WorldCapture capture)
        {
            switch (kind)
            {
                case 'E':
                    CapturedEmpire empire = capture.Empire(number);
                    return empire?.NationName ?? empire?.Culture;
                case 'T':
                    string territory = number < capture.TerritoryOwner.Length ? capture.TerritoryName(number) : null;
                    return string.IsNullOrWhiteSpace(territory) ? null : territory;
                case 'C':
                {
                    string guid = Guid(world?.CityHandles, code);
                    if (guid == null)
                    {
                        return null;
                    }
                    foreach (CapturedEmpire each in capture.Empires.Where(e => e != null))
                    {
                        string name = each.Cities.FirstOrDefault(c => c.Key == guid)?.Name ?? each.SeenCities.FirstOrDefault(c => c.Key == guid)?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name;
                        }
                    }
                    return null;
                }
                case 'A':
                {
                    string guid = Guid(world?.ArmyHandles, code);
                    if (guid == null)
                    {
                        return null;
                    }
                    foreach (CapturedEmpire each in capture.Empires.Where(e => e != null))
                    {
                        if (each.Armies.Any(a => a.Key == guid))
                        {
                            return ArmyOf() + (each.NationName ?? each.Culture);
                        }
                        SeenArmy seen = each.SeenArmies.FirstOrDefault(a => a.Key == guid);
                        if (seen != null)
                        {
                            CapturedEmpire owner = capture.Empire(seen.Owner);
                            return ArmyOf() + (owner?.NationName ?? owner?.Culture ?? "?");
                        }
                    }
                    return AnArmy();
                }
            }
            return null;
        }

        /// <summary>The letters are written in the AI's language (AiConfig.WritingCode), not the UI's.</summary>
        private static string Of()
        {
            switch (AiConfig.WritingCode)
            {
                case "en": return "of";
                case "de": return "von";
                default: return "de";
            }
        }

        private static string ArmyOf()
        {
            switch (AiConfig.WritingCode)
            {
                case "en": return "the army of ";
                case "es": return "el ejército de ";
                case "fr": return "l'armée de ";
                case "de": return "das Heer von ";
                default: return "o exército de ";
            }
        }

        private static string AnArmy()
        {
            switch (AiConfig.WritingCode)
            {
                case "en": return "an army";
                case "es": return "un ejército";
                case "fr": return "une armée";
                case "de": return "ein Heer";
                default: return "um exército";
            }
        }

        private static string Guid(Dictionary<string, string> handles, string code)
        {
            if (handles == null)
            {
                return null;
            }
            foreach (KeyValuePair<string, string> pair in handles)
            {
                if (pair.Value == code)
                {
                    return pair.Key;
                }
            }
            return null;
        }

        /// <summary>"— Semíramis, rainha dos Harapeanos" → "— Semíramis, rainha de Kestria" (only near the end: the signature).</summary>
        private static string FixSignature(string text, CapturedEmpire sender)
        {
            string people = NationNaming.WithoutArticle(sender?.RealCulture);
            string nation = sender?.NationName;
            if (string.IsNullOrWhiteSpace(people) || string.IsNullOrWhiteSpace(nation) || people == nation)
            {
                return text;
            }
            int from = System.Math.Max(0, text.Length - 160);
            string tail = text.Substring(from);
            string fixedTail = Regex.Replace(tail, @"\b(dos|das|do|da|of the|de los|de las|des|der)\s+" + Regex.Escape(people) + @"\b", Of() + " " + nation, RegexOptions.IgnoreCase);
            return fixedTail == tail ? text : text.Substring(0, from) + fixedTail;
        }
    }
}
