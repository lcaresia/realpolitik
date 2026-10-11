using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AiBench
{
    /// <summary>Runs a variant on the fixed scenarios and stores the answer, reasoning, usage and objective metrics.</summary>
    internal static class Runner
    {
        internal static string ScenarioFile = "scenarios.txt";

        internal static List<(string file, string label)> Scenarios()
        {
            return File.ReadAllLines(Path.Combine(Bench.Root, ScenarioFile))
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(l => { string[] p = l.Split('·'); return (p[0].Trim(), p.Length > 1 ? p[1].Trim() : ""); })
                .ToList();
        }

        internal static string Id(string file) => Path.GetFileNameWithoutExtension(file);

        internal static int Run(string variant, int reps, string effort = "low")
        {
            // "variant@off" or "variant@high": the reasoning level is part of the name (its own results folder).
            if (variant.Contains("@"))
            {
                effort = variant.Substring(variant.IndexOf('@') + 1);
            }
            else if (effort != "low")
            {
                variant += "@" + effort;
            }
            string folder = Path.Combine(Bench.Root, "results", variant.Replace(':', '_'));
            Directory.CreateDirectory(folder);
            var jobs = (from s in Scenarios() from r in Enumerable.Range(1, reps) select (s.file, rep: r))
                .Where(j => !File.Exists(Path.Combine(folder, $"{Id(j.file)}_r{j.rep}.json")))
                .ToList();
            Console.WriteLine($"{variant}: {jobs.Count} calls to make");
            jobs.AsParallel().WithDegreeOfParallelism(8).ForAll(job =>
            {
                JObject log = JObject.Parse(File.ReadAllText(Path.Combine(Bench.Root, "corpus", job.file)));
                var (system, user) = Variants.Apply(Variants.Spec(variant), (string)log["sistema"], (string)log["dossie"]);
                var call = new Call { System = system, Effort = effort, EffortPlacement = effort == "off" ? "off" : "top" };
                call.Turns.Add(("user", user));
                CallResult r = Client.Send(call);
                JObject metrics = Metrics.Measure(r.Content, r.Finish, (string)log["dossie"], (string)log["sistema"]);
                var record = new JObject
                {
                    ["cenario"] = job.file, ["variante"] = variant, ["rep"] = job.rep, ["uso"] = r.ToJson(), ["metricas"] = metrics,
                    ["resposta"] = r.Content, ["raciocinioDoModelo"] = r.Reasoning, ["entradaChars"] = system.Length + user.Length,
                };
                File.WriteAllText(Path.Combine(folder, $"{Id(job.file)}_r{job.rep}.json"), record.ToString());
                Console.WriteLine($"  {Id(job.file)} r{job.rep}: {(r.Ok ? "ok" : r.Error)} finish={r.Finish} reasoning={r.ReasoningTokens} US$ {r.Cost:0.0000}");
            });
            Console.WriteLine("score: US$ " + ((double)Scoreboard.Read()["gastoUSD"]).ToString("0.0000"));
            return 0;
        }

        /// <summary>Table comparing the variants already run.</summary>
        internal static int Report(string variants)
        {
            Console.WriteLine("variant                  n   ok%    cuts    input  cache%   reasoning    answer  US$/decision(peak) letters     words actions names? codesInLetter");
            foreach (string variant in variants.Split(','))
            {
                string folder = Path.Combine(Bench.Root, "results", variant.Replace(':', '_'));
                if (!Directory.Exists(folder))
                {
                    continue;
                }
                List<JObject> runs = Directory.GetFiles(folder, "*.json").Select(f => JObject.Parse(File.ReadAllText(f))).ToList();
                double Avg(Func<JObject, double> f) => runs.Count == 0 ? 0 : runs.Average(f);
                double ok = Avg(r => (bool)r["metricas"]["jsonOk"] ? 1 : 0);
                double cut = runs.Count(r => (string)r["uso"]["fim"] == "length");
                double input = Avg(r => (int)r["uso"]["entrada"]);
                double reasoning = Avg(r => (int)r["uso"]["raciocinio"]);
                double answer = Avg(r => (int)r["uso"]["saida"] - (int)r["uso"]["raciocinio"]);
                // Cost comparable across variants: at peak, with the game's simulated cache (the server cache on the bench
                // depends on who ran before) and the real output.
                double cacheShare = Simulate.CacheShare(Variants.Spec(variant));
                double cost = Avg(r => ((int)r["uso"]["entrada"] * ((1 - cacheShare) * 0.30 + cacheShare * 0.006) + (int)r["uso"]["saida"] * 1.20) / 1_000_000);
                Console.WriteLine($"{variant,-22} {runs.Count,3} {100 * ok,5:0} {cut,6} {input,8:0} {100 * cacheShare,6:0} {reasoning,10:0} {answer,9:0} {cost,17:0.00000}"
                    + $" {Avg(r => (int)r["metricas"]["cartas"]),7:0.00} {Avg(r => (double)r["metricas"]["palavrasPorCarta"]),9:0} {Avg(r => (int)r["metricas"]["acoes"]),6:0.00}"
                    + $" {runs.Sum(r => (int)r["metricas"]["nomesDesconhecidos"]),7} {runs.Sum(r => (int)r["metricas"]["codigosNaCarta"]),15}");
            }
            return 0;
        }
    }

    /// <summary>Objective metrics of an answer, approximating the mod's validator (DecisionParser/Grounding).</summary>
    internal static class Metrics
    {
        internal static JObject Measure(string content, string finish, string dossier, string system)
        {
            var m = new JObject { ["jsonOk"] = false, ["cartas"] = 0, ["palavrasPorCarta"] = 0.0, ["maxPalavras"] = 0, ["acoes"] = 0,
                ["nomesDesconhecidos"] = 0, ["codigosNaCarta"] = 0, ["codigosInexistentes"] = 0, ["diarioPalavras"] = 0 };
            if (finish == "length" || string.IsNullOrEmpty(content))
            {
                return m;
            }
            JObject json;
            try
            {
                json = JObject.Parse(content);
            }
            catch (Exception)
            {
                return m;
            }
            m["jsonOk"] = true;
            var letters = (json["cartas"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            var words = letters.Select(l => Words((string)l["texto"])).ToList();
            m["cartas"] = letters.Count;
            m["palavrasPorCarta"] = words.Count == 0 ? 0 : words.Average();
            m["maxPalavras"] = words.Count == 0 ? 0 : words.Max();
            m["acoes"] = (json["acoes"] as JArray)?.Count ?? 0;
            m["diarioPalavras"] = Words((string)json["diario"]);
            string known = dossier + "\n" + system;
            var unknown = new List<string>();
            int codesInLetters = 0;
            foreach (JObject letter in letters)
            {
                string text = (string)letter["texto"] ?? "";
                codesInLetters += Regex.Matches(text, @"\b[ACTGMEK]\d{1,3}\b").Count;
                // Proper noun mid-sentence that appears nowhere in the dossier or the system prompt.
                foreach (Match name in Regex.Matches(text, @"(?<=[a-zà-ú,;:] )\p{Lu}[\p{Ll}'’-]{2,}"))
                {
                    if (known.IndexOf(name.Value, StringComparison.Ordinal) < 0)
                    {
                        unknown.Add(name.Value);
                    }
                }
            }
            m["nomesDesconhecidos"] = unknown.Distinct().Count();
            m["listaNomes"] = string.Join(", ", unknown.Distinct());
            m["codigosNaCarta"] = codesInLetters;
            // Codes used in the actions that do not exist in the dossier (army, city, territory, grievance...).
            string actions = json["acoes"]?.ToString() ?? "";
            var missing = Regex.Matches(actions, @"\b[ACTGMEK]\d{1,3}\b").Cast<Match>().Select(x => x.Value).Distinct()
                .Where(code => !Regex.IsMatch(dossier, @"\b" + code + @"\b")).ToList();
            m["codigosInexistentes"] = missing.Count;
            return m;
        }

        private static int Words(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
