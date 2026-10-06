using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace IaBench
{
    /// <summary>
    /// Simulação sem custo, sobre o corpus inteiro: quanto do prompt cairia no cache do DeepSeek (prefixo igual ao do
    /// pedido anterior da mesma nação, ou ao de outra nação no mesmo turno) e o tamanho estimado da entrada.
    /// </summary>
    internal static class Simulate
    {
        internal const double CharsPerToken = 2.87;
        /// <summary>O DeepSeek guarda o prefixo em blocos; o que sobra no fim do bloco não conta.</summary>
        private const int CacheUnitTokens = 64;

        internal sealed class Sample
        {
            public string File;
            public int Turn;
            public int Empire;
            public string System;
            public string Dossier;
        }

        internal static List<Sample> Load(string corpus)
        {
            var list = new List<Sample>();
            foreach (string path in Directory.GetFiles(corpus, "T*_E*.json"))
            {
                string name = Path.GetFileName(path);
                if (name.Contains("conselho"))
                {
                    continue;
                }
                JObject log = JObject.Parse(System.IO.File.ReadAllText(path));
                list.Add(new Sample
                {
                    File = name, Turn = (int)log["turno"], Empire = (int)log["imperio"],
                    System = (string)log["sistema"], Dossier = (string)log["dossie"],
                });
            }
            return list.OrderBy(s => s.Turn).ThenBy(s => s.Empire).ToList();
        }

        internal static int Run(string corpus, string variantSpec)
        {
            List<Sample> samples = Load(corpus);
            Console.WriteLine($"{samples.Count} decisões");
            Stability(samples);
            foreach (string variant in variantSpec.Split(','))
            {
                Report(samples, variant);
            }
            return 0;
        }

        /// <summary>Em quantos pares (mesma nação, turno seguinte) cada seção ficou idêntica.</summary>
        private static void Stability(List<Sample> samples)
        {
            var same = new Dictionary<string, int>();
            var total = new Dictionary<string, int>();
            var size = new Dictionary<string, long>();
            foreach (Sample s in samples)
            {
                Sample prev = samples.FirstOrDefault(p => p.Empire == s.Empire && p.Turn == s.Turn - 1);
                Dictionary<string, string> now = Variants.Sections(s.Dossier).ToDictionary(x => x.key, x => x.text);
                foreach (var pair in now)
                {
                    size[pair.Key] = size.TryGetValue(pair.Key, out long v) ? v + pair.Value.Length : pair.Value.Length;
                }
                if (prev == null)
                {
                    continue;
                }
                Dictionary<string, string> before = Variants.Sections(prev.Dossier).ToDictionary(x => x.key, x => x.text);
                foreach (var pair in now)
                {
                    total[pair.Key] = total.TryGetValue(pair.Key, out int t) ? t + 1 : 1;
                    if (before.TryGetValue(pair.Key, out string old) && old == pair.Value)
                    {
                        same[pair.Key] = same.TryGetValue(pair.Key, out int c) ? c + 1 : 1;
                    }
                }
            }
            Console.WriteLine("seção · igual ao turno anterior · tamanho médio");
            foreach (var pair in total.OrderByDescending(p => size[p.Key]))
            {
                same.TryGetValue(pair.Key, out int c);
                Console.WriteLine($"  {pair.Key,-34} {100.0 * c / pair.Value,5:0}%  {size[pair.Key] / samples.Count,6} chars");
            }
        }

        private static readonly Dictionary<string, double> ShareCache = new Dictionary<string, double>();
        private static List<Sample> corpusSamples;

        /// <summary>Fração da entrada que cairia no cache no jogo, simulada no corpus inteiro (memorizada por variante).</summary>
        internal static double CacheShare(string variant)
        {
            if (!ShareCache.TryGetValue(variant, out double share))
            {
                corpusSamples = corpusSamples ?? Load(Path.Combine(Bench.Root, "corpus", "b44f3e76"));
                share = Report(corpusSamples, variant, quiet: true);
                ShareCache[variant] = share;
            }
            return share;
        }

        private static double Report(List<Sample> samples, string variant, bool quiet = false)
        {
            var prompts = samples.Select(s =>
            {
                var (system, user) = Variants.Apply(variant, s.System, s.Dossier);
                return (s, text: system + "\n" + user, systemLen: system.Length);
            }).ToList();
            double inputTokens = 0, cachedTokens = 0;
            foreach (var p in prompts)
            {
                // O melhor prefixo disponível: o pedido anterior da mesma nação ou qualquer pedido anterior deste turno
                // (as nações pensam em paralelo, mas na prática o cache já tem o turno de quem começou antes).
                int best = 0;
                foreach (var q in prompts.Where(q => (q.s.Empire == p.s.Empire && q.s.Turn == p.s.Turn - 1)
                    || (q.s.Turn == p.s.Turn && q.s.Empire < p.s.Empire)))
                {
                    best = Math.Max(best, CommonPrefix(p.text, q.text));
                }
                double tokens = p.text.Length / CharsPerToken;
                double hit = Math.Floor(best / CharsPerToken / CacheUnitTokens) * CacheUnitTokens;
                inputTokens += tokens;
                cachedTokens += Math.Min(hit, tokens);
            }
            int n = prompts.Count;
            double miss = (inputTokens - cachedTokens) / n, hitAvg = cachedTokens / n;
            double costPeak = (miss * 0.30 + hitAvg * 0.006) / 1_000_000;
            if (!quiet)
            {
                Console.WriteLine($"{variant,-24} entrada ~{inputTokens / n,6:0} tokens · cache {100 * cachedTokens / inputTokens,4:0}% · sem cache ~{miss,6:0} · custo da entrada no pico US$ {costPeak:0.00000}/decisão");
            }
            return cachedTokens / inputTokens;
        }

        internal static int CommonPrefix(string a, string b)
        {
            int max = Math.Min(a.Length, b.Length), i = 0;
            while (i < max && a[i] == b[i])
            {
                i++;
            }
            return i;
        }
    }
}
