using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Confere se uma carta fala só do que existe no jogo (design §9.1) e se não repete a carta anterior (§9, regras
    /// anti-laço). Roda na thread de trabalho, só com o ValidationContext.
    /// </summary>
    internal static class Grounding
    {
        /// <summary>Palavras que aparecem com maiúscula no meio da frase sem ser nome inventado: títulos, lugares do
        /// céu e da terra, instituições. Comparação em minúsculas.</summary>
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
            // Cartas em inglês ([IA] IdiomaDasCartas).
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
            // Espanhol.
            "su", "sus", "vuestra", "vuestro", "vuestras", "vuestros", "majestad", "excelencia", "señor", "señora",
            "señores", "rey", "reyes", "reina", "emperador", "emperatriz", "consejo", "imperio", "imperios", "naciones",
            "pueblo", "pueblos", "dios", "dioses", "diosa", "cielo", "cielos", "tierra", "océano", "sur", "este",
            "fe", "iglesia", "asamblea", "congreso", "humanidad", "jefe", "faraón", "sultán", "antigua", "clásica",
            "contemporánea", "atentamente", "saludos", "estimado", "estimada", "querido", "querida",
            // Francês.
            "votre", "vos", "notre", "nos", "sa", "son", "ses", "majesté", "altesse", "seigneur", "sire", "monsieur",
            "madame", "roi", "rois", "reine", "empereur", "impératrice", "conseil", "cour", "trône", "couronne",
            "royaume", "royaumes", "peuple", "dieu", "dieux", "déesse", "ciel", "soleil", "lune", "mer", "océan",
            "nord", "sud", "est", "ouest", "foi", "église", "sénat", "république", "assemblée", "congrès", "humanité",
            "pharaon", "calife", "néolithique", "antique", "classique", "médiévale", "moderne", "industrielle",
            "contemporaine", "cordialement", "salutations", "cher", "chère",
        };

        /// <summary>Deuses e figuras de fé: o tom da cultura pode citá-los (design §9.1, sabor permitido).</summary>
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

        /// <summary>Coisas que o jogo não tem e que as IAs já inventaram (turnos 80–95): mercadorias e mecanismos. Um
        /// recurso de verdade com o mesmo nome no mapa (ex.: "Seda") vale, porque entra no vocabulário conhecido.</summary>
        private static readonly HashSet<string> NotInGame = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pano", "panos", "tecido", "tecidos", "prisioneiro", "prisioneiros", "recibo", "recibos", "carreta", "carretas",
            "sal", "milho", "trigo", "cevada", "arroz", "vinho", "azeite", "seda", "especiarias", "lã",
            // Os mesmos em inglês, espanhol, francês e alemão ([IA] IdiomaDasCartas).
            "cloth", "cloths", "fabric", "fabrics", "prisoner", "prisoners", "receipt", "receipts", "cart", "carts",
            "salt", "corn", "maize", "wheat", "barley", "rice", "wine", "silk", "spices", "wool",
            // Sem palavras que também são português comum ("aceite", "tela", "mais", "reis").
            "paño", "paños", "prisionero", "prisioneros", "maíz", "vino", "especias",
            "tissu", "tissus", "étoffe", "étoffes", "prisonnier", "prisonniers", "reçu", "reçus", "charrette",
            "charrettes", "sel", "maïs", "blé", "orge", "riz", "vin", "soie", "épices", "laine",
            "tuch", "stoff", "stoffe", "gefangene", "gefangener", "quittung", "quittungen", "karren", "salz",
            "weizen", "gerste", "wein", "seide", "gewürze", "wolle",
        };

        /// <summary>Códigos do dossiê (E3, C14, A7, T31): nas cartas vão os nomes.</summary>
        private static readonly System.Text.RegularExpressions.Regex Codes =
            new System.Text.RegularExpressions.Regex(@"\b[ECATG]\d{1,4}\b", System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static List<string> CodesIn(string text) => string.IsNullOrEmpty(text)
            ? new List<string>()
            : Codes.Matches(text).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).Distinct().ToList();

        /// <summary>Palavras de um nome, em minúsculas ("Os Chou" → "os", "chou").</summary>
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

        /// <summary>Junta as palavras de um nome real ao vocabulário permitido.</summary>
        internal static void AddKnown(HashSet<string> known, string name)
        {
            foreach (string word in Words(name))
            {
                known.Add(word);
                // "lcaresia" (L minúsculo) e "Icaresia" (i maiúsculo) são a mesma coisa na tela.
                if (word.StartsWith("l"))
                {
                    known.Add("i" + word.Substring(1));
                }
            }
        }

        /// <summary>Nomes próprios da carta que não estão no jogo nem no dossiê ("Marco Túlio").</summary>
        internal static List<string> UnknownNames(string text, HashSet<string> known)
        {
            var unknown = new List<string>();
            // Em alemão todo substantivo tem maiúscula: a regra de nome próprio não funciona e fica desligada.
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
                    // Pontuação entre palavras quebra a sequência de um nome composto.
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
                return false; // minúscula, curta demais ou TODA EM MAIÚSCULAS (ênfase)
            }
            if (SentenceStart(text, start))
            {
                return false; // primeira palavra da frase: não dá para saber se é nome
            }
            string lower = token.ToLowerInvariant();
            if (known.Contains(lower) || CommonCapitalized.Contains(lower) || Faith.Contains(lower) || Variants(lower).Any(known.Contains))
            {
                return false;
            }
            // Nome composto com hífen ("Amon-Rá"): basta cada parte ser conhecida.
            return !(lower.Contains('-') && lower.Split('-').All(p => p.Length == 0 || known.Contains(p) || Faith.Contains(p) || CommonCapitalized.Contains(p)));
        }

        /// <summary>Singular, plural e feminino de um nome real ("Godo" vale por "Godos", "Bizantina" por "Bizantinos").</summary>
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
                    // Saudação de carta: "Agamenão, rei dos Romanos, Finalmente..." — o corpo começa com maiúscula
                    // depois da vírgula, ainda na primeira frase.
                    return text.IndexOfAny(SentenceEnds, 0, j) < 0;
                }
                return c == '.' || c == '!' || c == '?' || c == ':' || c == ';' || c == '\n' || c == '\r' || c == '"' || c == '“'
                    || c == '«' || c == '(' || c == '—' || c == '–' || c == '…' || c == '\'';
            }
            return true;
        }

        private static readonly char[] SentenceEnds = { '.', '!', '?', '\n', '\r' };

        /// <summary>Mercadorias e mecanismos que o jogo não tem, citados na carta.</summary>
        internal static List<string> NotInGameWords(string text, HashSet<string> known)
        {
            return Words(text).Where(w => NotInGame.Contains(w) && !known.Contains(w)).Distinct().ToList();
        }

        /// <summary>Semelhança entre duas cartas (0 a 1): palavras de 4+ letras em comum sobre o total.</summary>
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
