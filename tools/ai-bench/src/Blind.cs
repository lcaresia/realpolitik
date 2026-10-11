using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AiBench
{
    /// <summary>
    /// Blind comparison: for each scenario and repetition, the answers of two variants side by side as "A" and "B"
    /// (randomly assigned). The key goes into a separate file, so the judge (model or person) only opens it after scoring.
    /// </summary>
    internal static class Blind
    {
        internal static int Build(string variantA, string variantB, string reps, int seed)
        {
            var random = new Random(seed);
            var page = new StringBuilder();
            var key = new JObject();
            page.AppendLine($"# Blind comparison ({DateTime.Now:yyyy-MM-dd})");
            page.AppendLine();
            page.AppendLine("For each pair: which answer is better (A, B or tie) in cultural voice, use of memory, reaction to what arrived, coherence, boldness and validity. The key is in another file.");
            int pair = 0;
            foreach (var (file, label) in Runner.Scenarios())
            {
                string id = Runner.Id(file);
                // Log keys ("sistema", "dossie") and the dossier section titles below are the mod's data, kept as they are.
                JObject log = JObject.Parse(File.ReadAllText(Path.Combine(Bench.Root, "corpus", file)));
                foreach (string rep in reps.Split(','))
                {
                    string pathA = Path.Combine(Bench.Root, "results", variantA.Replace(':', '_'), $"{id}_r{rep}.json");
                    string pathB = Path.Combine(Bench.Root, "results", variantB.Replace(':', '_'), $"{id}_r{rep}.json");
                    if (!File.Exists(pathA) || !File.Exists(pathB))
                    {
                        continue;
                    }
                    pair++;
                    bool swap = random.Next(2) == 1;
                    JObject first = JObject.Parse(File.ReadAllText(swap ? pathB : pathA));
                    JObject second = JObject.Parse(File.ReadAllText(swap ? pathA : pathB));
                    key[$"pair{pair}"] = new JObject { ["A"] = swap ? variantB : variantA, ["B"] = swap ? variantA : variantB, ["scenario"] = id, ["rep"] = rep };
                    page.AppendLine();
                    page.AppendLine($"## Pair {pair} · {id} · {label}");
                    page.AppendLine();
                    page.AppendLine("**Who:** " + Regex.Match((string)log["sistema"], @"Você é ([^\n]+)").Groups[1].Value + " " + Regex.Match((string)log["sistema"], @"Temperamento: ([^\n]+)").Value);
                    page.AppendLine();
                    page.AppendLine("**What arrived this turn (excerpt):**");
                    page.AppendLine("```");
                    page.AppendLine(Trim(Section((string)log["dossie"], "CARTAS QUE CHEGARAM"), 1800));
                    page.AppendLine(Trim(Section((string)log["dossie"], "PENDÊNCIAS"), 600));
                    page.AppendLine("```");
                    AppendAnswer(page, "A", first);
                    AppendAnswer(page, "B", second);
                }
            }
            string name = $"blind_{Safe(variantA)}_vs_{Safe(variantB)}";
            File.WriteAllText(Path.Combine(Bench.Root, "results", name + ".md"), page.ToString());
            File.WriteAllText(Path.Combine(Bench.Root, "results", name + ".key.json"), key.ToString());
            Console.WriteLine($"{pair} pairs in results\\{name}.md (key in {name}.key.json)");
            return 0;
        }

        // Answer keys ("diario", "cartas", "acoes", "memoria"...) are the mod's LLM action schema.
        private static void AppendAnswer(StringBuilder page, string tag, JObject run)
        {
            page.AppendLine();
            page.AppendLine($"### {tag}");
            JObject json;
            try
            {
                json = JObject.Parse((string)run["resposta"]);
            }
            catch (Exception)
            {
                page.AppendLine("(invalid or truncated answer)");
                return;
            }
            page.AppendLine($"- **Diary:** {json["diario"]}");
            foreach (JObject letter in (json["cartas"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                page.AppendLine($"- **Letter to {letter["para"]} ({letter["tipo"]}) — {letter["assunto"]}:** {letter["texto"]}");
            }
            foreach (JToken action in (json["acoes"] as JArray) ?? new JArray())
            {
                page.AppendLine($"- **Action:** `{action.ToString(Newtonsoft.Json.Formatting.None)}`");
            }
            foreach (JToken note in (json["memoria"] as JArray) ?? new JArray())
            {
                page.AppendLine($"- **Memory:** {note["nacao"]}: {note["nota"]}");
            }
        }

        private static string Section(string dossier, string title)
        {
            Match m = Regex.Match(dossier.Replace("\r\n", "\n"), "== " + Regex.Escape(title) + @"[^\n]*==\n([\s\S]*?)(?=\n== |\nDecida o seu turno|$)");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static string Trim(string text, int max) => text.Length <= max ? text : text.Substring(0, max) + " […]";

        private static string Safe(string variant) => variant.Replace(':', '_').Replace('+', '-');
    }
}
