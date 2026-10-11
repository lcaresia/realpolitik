using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Checks that a letter only talks about what exists in the game (design §9.1) and does not repeat the previous
    /// letter (§9, anti-loop rules). Runs on the worker thread, using only the ValidationContext.
    /// </summary>
    internal static class Grounding
    {
        /// <summary>Words that appear capitalized mid-sentence without being invented names: titles, places in the
        /// heavens and on earth, institutions. Compared in lowercase.</summary>
        private static readonly HashSet<string> CommonCapitalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rei", "reis", "rainha", "rainhas", "senhor", "senhora", "senhores", "senhoras", "sua", "suas", "seu", "seus",
            "vossa", "vossas", "vosso", "vossos", "majestade", "majestades", "alteza", "excelência", "líder", "líderes",
            "governante", "governantes", "supremo", "suprema", "soberano", "soberana", "imperador", "imperatriz",
            "príncipe", "princesa", "general", "generais", "conselho", "corte", "trono", "coroa", "império", "impérios",
            "reino", "reinos", "nação", "nações", "povo", "povos", "era", "eras", "deus", "deuses", "deusa", "deusas",
            "céu", "céus", "sol", "lua", "terra", "mar", "mares", "oceano", "norte", "sul", "leste", "oeste", "oriente",
            "ocidente", "fé", "templo", "igreja", "senado", "república", "assembleia", "congresso", "humanidade",
            "autocrata", "chefe", "cacique", "faraó", "sultão", "califa", "xá", "cã", "khan", "imperial", "real",
            "todos", "todas", "neolítica", "antiga", "clássica", "medieval", "moderna", "industrial", "contemporânea",
            // Letters in English ([IA] IdiomaDasCartas).
            "your", "my", "our", "his", "her", "their", "you", "we", "the", "majesty", "majesties", "highness",
            "excellency", "lord", "lords", "lady", "ladies", "sir", "king", "kings", "queen", "queens", "emperor",
            "empress", "princess", "council", "court", "throne", "crown", "empire", "empires", "kingdom", "kingdoms",
            "nation", "nations", "people", "peoples", "god", "gods", "goddess", "goddesses", "heaven", "heavens", "sun",
            "moon", "earth", "sea", "seas", "ocean", "north", "south", "east", "west", "orient", "occident", "faith",
            "temple", "church", "senate", "republic", "assembly", "congress", "humanity", "chief", "pharaoh", "sultan",
            "caliph", "shah", "ruler", "rulers", "sovereign", "neolithic", "ancient", "classical", "early", "contemporary",
            "age", "dear", "yours", "sincerely", "regards", "greetings", "respectfully", "january", "february", "march",
            "april", "may", "june", "july", "august", "september", "october", "november", "december", "monday",
            "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
            // Spanish.
            "su", "sus", "vuestra", "vuestro", "vuestras", "vuestros", "majestad", "excelencia", "señor", "señora",
            "señores", "rey", "reyes", "reina", "emperador", "emperatriz", "consejo", "imperio", "imperios", "naciones",
            "pueblo", "pueblos", "dios", "dioses", "diosa", "cielo", "cielos", "tierra", "océano", "sur", "este",
            "fe", "iglesia", "asamblea", "congreso", "humanidad", "jefe", "faraón", "sultán", "antigua", "clásica",
            "contemporánea", "atentamente", "saludos", "estimado", "estimada", "querido", "querida",
            // French.
            "votre", "vos", "notre", "nos", "sa", "son", "ses", "majesté", "altesse", "seigneur", "sire", "monsieur",
            "madame", "roi", "rois", "reine", "empereur", "impératrice", "conseil", "cour", "trône", "couronne",
            "royaume", "royaumes", "peuple", "dieu", "dieux", "déesse", "ciel", "soleil", "lune", "mer", "océan",
            "nord", "sud", "est", "ouest", "foi", "église", "sénat", "république", "assemblée", "congrès", "humanité",
            "pharaon", "calife", "néolithique", "antique", "classique", "médiévale", "moderne", "industrielle",
            "contemporaine", "cordialement", "salutations", "cher", "chère",
        };

        /// <summary>Gods and faith figures: the culture's tone may cite them (design §9.1, allowed flavor).</summary>
        private static readonly HashSet<string> Faith = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "zeus", "júpiter", "jupiter", "atena", "athena", "ares", "apolo", "hera", "poseidon", "netuno", "marte",
            "minerva", "vênus", "juno", "hermes", "mercúrio", "tanit", "baal", "melqart", "osíris", "ísis", "rá", "amon",
            "hórus", "anúbis", "ptah", "aton", "odin", "thor", "freyja", "frigg", "wotan", "tengri", "ahura", "mazda",
            "mitra", "shiva", "vishnu", "brahma", "buda", "confúcio", "tian", "itzamná", "kukulcán", "quetzalcóatl",
            "huitzilopochtli", "tláloc", "inti", "viracocha", "pachamama", "olorum", "olodumaré", "ogum", "xangô",
            "nyame", "alá", "allah", "yahweh", "javé", "jeová", "cristo", "jesus", "maria", "marduk", "ishtar", "enlil",
            "assur", "nabu", "teshub", "amaterasu", "perun", "svarog", "dagda", "lugh", "brigid", "anahita", "sol",
        };

        /// <summary>Things the game does not have and that the AIs have already invented (turns 80–95): goods and
        /// mechanics. A real resource with the same name on the map (e.g. "Seda") is fine, since it enters the known vocabulary.</summary>
        private static readonly HashSet<string> NotInGame = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pano", "panos", "tecido", "tecidos", "prisioneiro", "prisioneiros", "recibo", "recibos", "carreta", "carretas",
            "sal", "milho", "trigo", "cevada", "arroz", "vinho", "azeite", "seda", "especiarias", "lã",
            // The same in English, Spanish, French and German ([IA] IdiomaDasCartas).
            "cloth", "cloths", "fabric", "fabrics", "prisoner", "prisoners", "receipt", "receipts", "cart", "carts",
            "salt", "corn", "maize", "wheat", "barley", "rice", "wine", "silk", "spices", "wool",
            // Excluding words that are also common Portuguese ("aceite", "tela", "mais", "reis").
            "paño", "paños", "prisionero", "prisioneros", "maíz", "vino", "especias",
            "tissu", "tissus", "étoffe", "étoffes", "prisonnier", "prisonniers", "reçu", "reçus", "charrette",
            "charrettes", "sel", "maïs", "blé", "orge", "riz", "vin", "soie", "épices", "laine",
            "tuch", "stoff", "stoffe", "gefangene", "gefangener", "quittung", "quittungen", "karren", "salz",
            "weizen", "gerste", "wein", "seide", "gewürze", "wolle",
        };

        /// <summary>Dossier codes (E3, C14, A7, T31): letters must use the names instead.</summary>
        private static readonly System.Text.RegularExpressions.Regex Codes =
            new System.Text.RegularExpressions.Regex(@"\b[ECATG]\d{1,4}\b", System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static List<string> CodesIn(string text) => string.IsNullOrEmpty(text)
            ? new List<string>()
            : Codes.Matches(text).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).Distinct().ToList();

        /// <summary>Words of a name, lowercased ("Os Chou" → "os", "chou").</summary>
        internal static IEnumerable<string> Words(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                yield break;
            }
            var word = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    word.Append(char.ToLowerInvariant(c));
                }
                else if (word.Length > 0)
                {
                    yield return word.ToString();
                    word.Clear();
                }
            }
            if (word.Length > 0)
            {
                yield return word.ToString();
            }
        }

        /// <summary>Adds the words of a real name to the allowed vocabulary.</summary>
        internal static void AddKnown(HashSet<string> known, string name)
        {
            foreach (string word in Words(name))
            {
                known.Add(word);
                // "lcaresia" (lowercase L) and "Icaresia" (uppercase i) look the same on screen.
                if (word.StartsWith("l"))
                {
                    known.Add("i" + word.Substring(1));
                }
            }
        }

        /// <summary>Proper names in the letter that are neither in the game nor in the dossier ("Marco Túlio").</summary>
        internal static List<string> UnknownNames(string text, HashSet<string> known)
        {
            var unknown = new List<string>();
            // In German every noun is capitalized: the proper-name rule does not work and stays off.
            if (string.IsNullOrWhiteSpace(text) || IaConfig.WritingCode == "de")
            {
                return unknown;
            }
            var current = new List<string>();
            void Flush()
            {
                if (current.Count > 0)
                {
                    string name = string.Join(" ", current);
                    if (!unknown.Contains(name))
                    {
                        unknown.Add(name);
                    }
                    current.Clear();
                }
            }

            int i = 0;
            while (i < text.Length)
            {
                if (!char.IsLetter(text[i]))
                {
                    // Punctuation between words breaks a compound name sequence.
                    if (!char.IsWhiteSpace(text[i]))
                    {
                        Flush();
                    }
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && (char.IsLetter(text[i]) || text[i] == '\'' || text[i] == '-'))
                {
                    i++;
                }
                string token = text.Substring(start, i - start).Trim('\'', '-');
                if (!IsSuspect(text, start, token, known))
                {
                    Flush();
                    continue;
                }
                current.Add(token);
            }
            Flush();
            return unknown;
        }

        private static bool IsSuspect(string text, int start, string token, HashSet<string> known)
        {
            if (token.Length < 3 || !char.IsUpper(token[0]) || token.All(c => !char.IsLetter(c) || char.IsUpper(c)))
            {
                return false; // lowercase, too short or ALL CAPS (emphasis)
            }
            if (SentenceStart(text, start))
            {
                return false; // first word of the sentence: no way to tell whether it is a name
            }
            string lower = token.ToLowerInvariant();
            if (known.Contains(lower) || CommonCapitalized.Contains(lower) || Faith.Contains(lower) || Variants(lower).Any(known.Contains))
            {
                return false;
            }
            // Hyphenated compound name ("Amon-Rá"): each part being known is enough.
            return !(lower.Contains('-') && lower.Split('-').All(p => p.Length == 0 || known.Contains(p) || Faith.Contains(p) || CommonCapitalized.Contains(p)));
        }

        /// <summary>Singular, plural and feminine of a real name ("Godo" counts for "Godos", "Bizantina" for "Bizantinos").</summary>
        private static IEnumerable<string> Variants(string word)
        {
            yield return word + "s";
            yield return word + "es";
            if (word.EndsWith("es"))
            {
                yield return word.Substring(0, word.Length - 2);
            }
            if (word.EndsWith("s"))
            {
                yield return word.Substring(0, word.Length - 1);
            }
            string stem = word.TrimEnd('s');
            if (stem.Length > 3 && (stem.EndsWith("a") || stem.EndsWith("o")))
            {
                string root = stem.Substring(0, stem.Length - 1);
                yield return root + "o";
                yield return root + "a";
                yield return root + "os";
                yield return root + "as";
            }
        }

        private static bool SentenceStart(string text, int start)
        {
            for (int j = start - 1; j >= 0; j--)
            {
                char c = text[j];
                if (char.IsWhiteSpace(c) && c != '\n' && c != '\r')
                {
                    continue;
                }
                if (c == ',')
                {
                    // Letter greeting: "Agamenão, rei dos Romanos, Finalmente..." — the body starts capitalized
                    // after the comma, still within the first sentence.
                    return text.IndexOfAny(SentenceEnds, 0, j) < 0;
                }
                return c == '.' || c == '!' || c == '?' || c == ':' || c == ';' || c == '\n' || c == '\r' || c == '"' || c == '“'
                    || c == '«' || c == '(' || c == '—' || c == '–' || c == '…' || c == '\'';
            }
            return true;
        }

        private static readonly char[] SentenceEnds = { '.', '!', '?', '\n', '\r' };

        /// <summary>Goods and mechanics the game does not have, mentioned in the letter.</summary>
        internal static List<string> NotInGameWords(string text, HashSet<string> known)
        {
            return Words(text).Where(w => NotInGame.Contains(w) && !known.Contains(w)).Distinct().ToList();
        }

        /// <summary>Offer verbs and the writer's possessives, in the 5 letter languages: "ofereço cobre", "meu cobre".</summary>
        private static readonly HashSet<string> OfferWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ofereço", "ofereco", "oferto", "dou", "darei", "envio", "enviarei", "mando", "mandarei", "vendo", "venderei", "entrego",
            "entregarei", "abro", "abrirei", "cedo", "cederei", "levo", "levarei", "meu", "minha", "meus", "minhas", "nosso", "nossa", "nossos", "nossas",
            "offer", "give", "send", "sell", "deliver", "open", "my", "our",
            "ofrezco", "doy", "daré", "envío", "mandaré", "venderé", "entrego", "abro", "mi", "mis", "nuestro", "nuestra", "nuestros", "nuestras",
            "offre", "donne", "envoie", "vends", "livre", "ouvre", "mon", "ma", "mes", "notre", "nos",
            "biete", "gebe", "schicke", "sende", "verkaufe", "liefere", "öffne", "mein", "meine", "meinen", "unser", "unsere",
        };

        /// <summary>The other party's possessives: "teu cobre" is asking, not offering.</summary>
        private static readonly HashSet<string> TheirWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "teu", "tua", "teus", "tuas", "vosso", "vossa", "vossos", "vossas", "seu", "sua", "seus", "suas",
            "your", "their", "tu", "tus", "vuestro", "vuestra", "su", "sus", "ton", "ta", "tes", "votre", "vos", "leur", "leurs",
            "dein", "deine", "deinen", "euer", "eure", "ihr", "ihre",
        };

        /// <summary>
        /// Resources the letter offers or claims to have without having them: a map resource word up to 6 words after an
        /// offer verb or "meu/nosso", with no "teu/vosso" in between, and not among the writer's resources.
        /// </summary>
        internal static List<string> OfferedNotOwned(string text, HashSet<string> ownResources, HashSet<string> allResources)
        {
            var found = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || allResources == null || allResources.Count == 0)
            {
                return found;
            }
            List<string> words = Words(text).ToList();
            for (int i = 0; i < words.Count; i++)
            {
                if (!OfferWords.Contains(words[i]))
                {
                    continue;
                }
                for (int j = i + 1; j < words.Count && j <= i + 6; j++)
                {
                    if (TheirWords.Contains(words[j]))
                    {
                        break;
                    }
                    if (allResources.Contains(words[j]) && (ownResources == null || !ownResources.Contains(words[j])))
                    {
                        found.Add(words[j]);
                    }
                }
            }
            return found.Distinct().ToList();
        }

        /// <summary>Words that identify a resource by name ("Cobre" → "cobre"; compound names: each word of 4+ letters).
        /// "Ouro" is left out: in letters it almost always means money ("meu ouro e minha palavra são teus").</summary>
        internal static IEnumerable<string> ResourceWords(string name) => Words(name).Where(w => w.Length >= 4 && !MoneyLikeWords.Contains(w));

        private static readonly HashSet<string> MoneyLikeWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ouro", "gold" };

        /// <summary>Similarity between two letters (0 to 1): shared words of 4+ letters over the total.</summary>
        internal static double Similarity(string a, string b)
        {
            var left = new HashSet<string>(Words(a).Where(w => w.Length >= 4));
            var right = new HashSet<string>(Words(b).Where(w => w.Length >= 4));
            if (left.Count == 0 || right.Count == 0)
            {
                return 0;
            }
            int common = left.Count(right.Contains);
            return (double)common / (left.Count + right.Count - common);
        }
    }
}
