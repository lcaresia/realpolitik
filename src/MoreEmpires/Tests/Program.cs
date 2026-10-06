using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace MoreEmpires
{
    /// <summary>Testes offline: equivalência da visibilidade (fecho × recursão do jogo) e gerador de cores.</summary>
    public static class TestProgram
    {
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length > 0 && args[0] == "opcoes")
            {
                // Lê as GameOptionDefinition direto do bundle do jogo (sem abrir o jogo). Útil depois de atualizações.
                string game = args.Length > 1 ? args[1] : @"C:\Program Files (x86)\Steam\steamapps\common\Humankind";
                string bundle = System.IO.Path.Combine(game, @"AssetBundles\MercuryDatabases.AvatarPresentation\mercurydatabases.avatarpresentation.assetbundle");
                string raw = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "moreempires_avatarpresentation.bin");
                string dump = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "moreempires_opcoes.txt");
                Console.WriteLine(UnityFsDump.Run(bundle, raw));
                System.IO.File.WriteAllText(dump, OptionParse.Run(raw, "GameOption_"));
                foreach (string line in System.IO.File.ReadAllLines(dump))
                {
                    Console.WriteLine(line);
                }
                Console.WriteLine("(definições com Default '%...' são UIMappers lidos por engano e podem ser ignoradas)");
                return 0;
            }
            int failures = 0;
            failures += VisibilityEquivalence();
            VisibilityCost();
            failures += Colors();
            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "RESULTADO: todos os testes passaram." : $"RESULTADO: {failures} falha(s).");
            return failures == 0 ? 0 : 1;
        }

        // Combinações realistas de DiplomaticAbility (DiplomaticAgreementHelper.cs) e algumas artificiais.
        private static readonly ulong[] AbilityPalette =
        {
            0x0,                        // nada
            0x4 | 0x8,                  // visão compartilhada (ShareVision | ShareMaps)
            0x8,                        // mapas compartilhados
            0x4,                        // só visão (artificial)
            0x200 | 0x8000000 | 0x400000000, // comércio + travessia por rotas + infraestrutura
            0x100 | 0x8000000 | 0x400000000, // comércio de luxo
            0x400,                      // comércio exclusivo
            0x10,                       // posição da capital
            0x1000,                     // destruir comércio
            0x80000000,                 // visão furtiva
            0x80,                       // atravessar território (NÃO dispara a recursão)
            0x2,                        // escaramuça (NÃO dispara)
        };

        private static int VisibilityEquivalence()
        {
            var random = new Random(12345);
            int cases = 0;
            int skipped = 0;
            int failures = 0;
            var watch = Stopwatch.StartNew();
            for (int trial = 0; trial < 30000; trial++)
            {
                int n = random.Next(2, 17);
                double density = random.NextDouble();
                var matrix = new ulong[n, n];
                for (int u = 0; u < n; u++)
                {
                    for (int w = 0; w < n; w++)
                    {
                        if (u == w || random.NextDouble() > density)
                        {
                            continue;
                        }
                        ulong ab = AbilityPalette[random.Next(AbilityPalette.Length)];
                        if (random.NextDouble() < 0.25)
                        {
                            ab |= AbilityPalette[random.Next(AbilityPalette.Length)];
                        }
                        matrix[u, w] = ab;
                    }
                }
                int[] bits = Enumerable.Range(0, n).Select(i => 1 << i).ToArray();
                int root = random.Next(n);
                int already = 0;
                if (random.NextDouble() < 0.25)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (i != root && random.NextDouble() < 0.3)
                        {
                            already |= bits[i];
                        }
                    }
                }
                ulong Ab(int u, int w) => matrix[u, w];
                long budget = 300000;
                if (!VisibilityCore.Original(n, bits, Ab, root, already, ref budget, out VisibilityCore.Result slow))
                {
                    skipped++;
                    continue;
                }
                VisibilityCore.Result fast = VisibilityCore.Closure(n, bits, Ab, root, already);
                cases++;
                if (!slow.Equals(fast))
                {
                    failures++;
                    if (failures <= 5)
                    {
                        Console.WriteLine($"  DIFERENÇA: n={n} raiz={root} bloqueados={already:X}: original [{slow}] ≠ fecho [{fast}]");
                    }
                }
            }
            Console.WriteLine($"[Visibilidade] {cases} grafos aleatórios (2..16 impérios) comparados em {watch.Elapsed.TotalSeconds:0.0} s: " +
                              $"{cases - failures} idênticos, {failures} diferentes, {skipped} pulados (recursão original > 300 mil chamadas).");

            // Grafos DENSOS (os que a rodada acima pulou), até 10 impérios, sem limite de chamadas.
            int denseCases = 0;
            int denseFailures = 0;
            watch.Restart();
            for (int trial = 0; trial < 1500; trial++)
            {
                int n = random.Next(5, 11);
                double density = 0.7 + 0.3 * random.NextDouble();
                var matrix = new ulong[n, n];
                for (int u = 0; u < n; u++)
                {
                    for (int w = 0; w < n; w++)
                    {
                        if (u != w && random.NextDouble() <= density)
                        {
                            matrix[u, w] = AbilityPalette[random.Next(1, 10)] | (random.NextDouble() < 0.3 ? AbilityPalette[random.Next(AbilityPalette.Length)] : 0UL);
                        }
                    }
                }
                int[] bits = Enumerable.Range(0, n).Select(i => 1 << i).ToArray();
                int root = random.Next(n);
                ulong Ab(int u, int w) => matrix[u, w];
                long budget = long.MaxValue;
                VisibilityCore.Original(n, bits, Ab, root, 0, ref budget, out VisibilityCore.Result slow);
                VisibilityCore.Result fast = VisibilityCore.Closure(n, bits, Ab, root, 0);
                denseCases++;
                if (!slow.Equals(fast))
                {
                    denseFailures++;
                    if (denseFailures <= 5)
                    {
                        Console.WriteLine($"  DIFERENÇA (denso): n={n} raiz={root}: original [{slow}] ≠ fecho [{fast}]");
                    }
                }
            }
            Console.WriteLine($"[Visibilidade] {denseCases} grafos DENSOS (5..10 impérios, 70-100% das ligações) comparados em {watch.Elapsed.TotalSeconds:0.0} s: " +
                              $"{denseCases - denseFailures} idênticos, {denseFailures} diferentes.");
            return failures + denseFailures == 0 ? 0 : 1;
        }

        private static void VisibilityCost()
        {
            Console.WriteLine("[Visibilidade] Custo num bloco em que todos comerciam com todos (o jogo calcula isso para CADA império):");
            for (int n = 6; n <= 11; n++)
            {
                int[] bits = Enumerable.Range(0, n).Select(i => 1 << i).ToArray();
                ulong Ab(int u, int w) => u == w ? 0UL : 0x200UL;
                long budget = long.MaxValue;
                var watch = Stopwatch.StartNew();
                VisibilityCore.Original(n, bits, Ab, 0, 0, ref budget, out _);
                double slowMs = watch.Elapsed.TotalMilliseconds;
                long calls = long.MaxValue - budget;
                watch.Restart();
                for (int r = 0; r < 100; r++)
                {
                    VisibilityCore.Closure(n, bits, Ab, 0, 0);
                }
                double fastMs = watch.Elapsed.TotalMilliseconds / 100;
                Console.WriteLine($"  {n,2} impérios: original {calls,12:N0} chamadas, {slowMs,10:0.0} ms | fecho {fastMs:0.000} ms");
            }
            double e = Math.E;
            for (int n = 12; n <= 16; n++)
            {
                double calls = e * Factorial(n - 1);
                Console.WriteLine($"  {n,2} impérios: original ≈ {calls:E2} chamadas por império (extrapolado e·(n-1)!)");
            }
        }

        private static double Factorial(int k)
        {
            double f = 1;
            for (int i = 2; i <= k; i++)
            {
                f *= i;
            }
            return f;
        }

        // Paleta "Palette_Standard" lida do bundle MercuryDatabases.AvatarPresentation (Primary, Secondary, Tertiary).
        private static readonly float[,] Standard =
        {
            { 0.278f, 0.404f, 0.851f, 0.173f, 0.294f, 0.737f, 0.518f, 0.624f, 1.000f },
            { 0.984f, 0.624f, 0.294f, 0.725f, 0.447f, 0.188f, 0.996f, 0.729f, 0.486f },
            { 0.729f, 0.192f, 0.522f, 0.475f, 0.165f, 0.357f, 0.929f, 0.318f, 0.694f },
            { 0.745f, 0.824f, 0.294f, 0.506f, 0.549f, 0.231f, 0.824f, 0.890f, 0.439f },
            { 0.278f, 0.278f, 0.278f, 0.000f, 0.000f, 0.000f, 0.643f, 0.643f, 0.643f },
            { 0.604f, 0.369f, 0.294f, 0.325f, 0.204f, 0.169f, 0.839f, 0.490f, 0.380f },
            { 0.557f, 0.408f, 0.988f, 0.376f, 0.271f, 0.686f, 0.714f, 0.612f, 1.000f },
            { 1.000f, 0.804f, 0.306f, 0.753f, 0.608f, 0.239f, 1.000f, 0.863f, 0.514f },
            { 0.965f, 0.580f, 0.929f, 0.784f, 0.420f, 0.749f, 1.000f, 0.769f, 0.980f },
            { 0.259f, 0.824f, 0.671f, 0.212f, 0.569f, 0.471f, 0.361f, 0.941f, 0.784f },
            { 0.871f, 0.090f, 0.325f, 0.459f, 0.145f, 0.235f, 1.000f, 0.314f, 0.518f },
            { 0.408f, 0.525f, 0.306f, 0.251f, 0.322f, 0.192f, 0.529f, 0.710f, 0.376f },
        };

        private static int Colors()
        {
            var natives = new List<ColorTriple>();
            for (int i = 0; i < Standard.GetLength(0); i++)
            {
                natives.Add(new ColorTriple
                {
                    Primary = new Rgba(Standard[i, 0], Standard[i, 1], Standard[i, 2]),
                    Secondary = new Rgba(Standard[i, 3], Standard[i, 4], Standard[i, 5]),
                    Tertiary = new Rgba(Standard[i, 6], Standard[i, 7], Standard[i, 8]),
                });
            }
            var minor = new Rgba(0.557f, 0.671f, 0.765f);
            ColorTriple[] extras = ColorMath.Generate(natives, minor, 4);
            ColorTriple[] again = ColorMath.Generate(natives, minor, 4);
            int failures = 0;
            bool deterministic = extras.Select(x => x.Primary.Hex + x.Secondary.Hex + x.Tertiary.Hex).SequenceEqual(again.Select(x => x.Primary.Hex + x.Secondary.Hex + x.Tertiary.Hex));
            if (!deterministic)
            {
                failures++;
                Console.WriteLine("  [Cores] gerador NÃO determinístico");
            }
            double nativeMin = ColorMath.MinDistance(natives.Select(c => c.Primary).ToList(), out int na, out int nb);
            var all = natives.Select(c => c.Primary).Concat(extras.Select(c => c.Primary)).ToList();
            double allMin = ColorMath.MinDistance(all, out int aa, out int ab);
            double vsMinor = extras.Min(x => ColorMath.Distance(ColorMath.ToLab(x.Primary), ColorMath.ToLab(minor)));
            Console.WriteLine($"[Cores] 4 cores novas para Palette_Standard (determinístico: {deterministic}):");
            for (int i = 0; i < extras.Length; i++)
            {
                double nearest = all.Where((c, idx) => idx != 12 + i).Min(c => ColorMath.Distance(ColorMath.ToLab(c), ColorMath.ToLab(extras[i].Primary)));
                Console.WriteLine($"  cor {12 + i}: primária {extras[i].Primary.Hex}, secundária {extras[i].Secondary.Hex}, terciária {extras[i].Tertiary.Hex} (ΔE até a mais próxima: {nearest:0.0})");
            }
            Console.WriteLine($"  menor ΔE entre as 12 nativas: {nativeMin:0.0} (cores {na} e {nb}); entre as 16: {allMin:0.0} (cores {aa} e {ab}); novas × povos independentes: {vsMinor:0.0}");
            if (allMin < nativeMin * 0.6)
            {
                failures++;
                Console.WriteLine("  [Cores] FALHA: as cores novas ficaram parecidas demais com as existentes.");
            }
            return failures;
        }
    }
}
