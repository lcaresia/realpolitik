using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace IaBench
{
    /// <summary>
    /// Comparação às cegas: para cada cenário e repetição, as respostas de duas variantes lado a lado como "A" e "B"
    /// (sorteado). A chave vai num arquivo separado, para o juiz (modelo ou pessoa) só abrir depois de dar as notas.
    /// </summary>
    internal static class Blind
    {
        internal static int Build(string variantA, string variantB, string reps, int seed)
        {
            var random = new Random(seed);
            var page = new StringBuilder();
            var key = new JObject();
            page.AppendLine($"# Comparação às cegas ({DateTime.Now:yyyy-MM-dd})");
            page.AppendLine();
            page.AppendLine("Para cada par: qual resposta é melhor (A, B ou empate) em voz da cultura, uso da memória, reação ao que chegou, coerência, ousadia e validade. A chave está em outro arquivo.");
            int pair = 0;
            foreach (var (file, label) in Runner.Scenarios())
            {
                string id = Runner.Id(file);
                JObject log = JObject.Parse(File.ReadAllText(Path.Combine(Bench.Root, "corpus", file)));
                foreach (string rep in reps.Split(','))
                {
                    string pathA = Path.Combine(Bench.Root, "resultados", variantA.Replace(':', '_'), $"{id}_r{rep}.json");
                    string pathB = Path.Combine(Bench.Root, "resultados", variantB.Replace(':', '_'), $"{id}_r{rep}.json");
                    if (!File.Exists(pathA) || !File.Exists(pathB))
                    {
                        continue;
                    }
                    pair++;
                    bool swap = random.Next(2) == 1;
                    JObject first = JObject.Parse(File.ReadAllText(swap ? pathB : pathA));
                    JObject second = JObject.Parse(File.ReadAllText(swap ? pathA : pathB));
                    key[$"par{pair}"] = new JObject { ["A"] = swap ? variantB : variantA, ["B"] = swap ? variantA : variantB, ["cenario"] = id, ["rep"] = rep };
                    page.AppendLine();
                    page.AppendLine($"## Par {pair} · {id} · {label}");
                    page.AppendLine();
                    page.AppendLine("**Quem é:** " + Regex.Match((string)log["sistema"], @"Você é ([^\n]+)").Groups[1].Value + " " + Regex.Match((string)log["sistema"], @"Temperamento: ([^\n]+)").Value);
                    page.AppendLine();
                    page.AppendLine("**O que chegou neste turno (trecho):**");
                    page.AppendLine("```");
                    page.AppendLine(Trim(Section((string)log["dossie"], "CARTAS QUE CHEGARAM"), 1800));
                    page.AppendLine(Trim(Section((string)log["dossie"], "PENDÊNCIAS"), 600));
                    page.AppendLine("```");
                    AppendAnswer(page, "A", first);
                    AppendAnswer(page, "B", second);
                }
            }
            string name = $"cego_{Safe(variantA)}_vs_{Safe(variantB)}";
            File.WriteAllText(Path.Combine(Bench.Root, "resultados", name + ".md"), page.ToString());
            File.WriteAllText(Path.Combine(Bench.Root, "resultados", name + ".chave.json"), key.ToString());
            Console.WriteLine($"{pair} pares em resultados\\{name}.md (chave em {name}.chave.json)");
            return 0;
        }

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
                page.AppendLine("(resposta inválida ou cortada)");
                return;
            }
            page.AppendLine($"- **Diário:** {json["diario"]}");
            foreach (JObject letter in (json["cartas"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                page.AppendLine($"- **Carta para {letter["para"]} ({letter["tipo"]}) — {letter["assunto"]}:** {letter["texto"]}");
            }
            foreach (JToken action in (json["acoes"] as JArray) ?? new JArray())
            {
                page.AppendLine($"- **Ação:** `{action.ToString(Newtonsoft.Json.Formatting.None)}`");
            }
            foreach (JToken note in (json["memoria"] as JArray) ?? new JArray())
            {
                page.AppendLine($"- **Memória:** {note["nacao"]}: {note["nota"]}");
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
