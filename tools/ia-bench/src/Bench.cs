using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace IaBench
{
    internal static class Bench
    {
        internal static string Root;
        internal static string GameDir;

        private static int Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Root = FindRoot();
            GameDir = Path.GetFullPath(Path.Combine(Root, "..", "..", ".."));
            if (args.Length == 0)
            {
                Console.WriteLine("uso: IaBench prova <log.json> | placar");
                return 1;
            }
            switch (args[0])
            {
                case "prova":
                    return args.Length > 2 ? Proof(args[1], args[2]) : Proof(args[1]);
                case "simular":
                    return Simulate.Run(Path.Combine(Root, "corpus", args.Length > 2 ? args[2] : "b44f3e76"), args.Length > 1 ? args[1] : "base");
                case "rodar":
                    if (args.Length > 4)
                    {
                        Runner.ScenarioFile = args[4];
                    }
                    return Runner.Run(args[1], args.Length > 2 ? int.Parse(args[2]) : 2, args.Length > 3 ? args[3] : "low");
                case "cego":
                    return Blind.Build(args[1], args[2], args.Length > 3 ? args[3] : "1,2", args.Length > 4 ? int.Parse(args[4]) : Environment.TickCount);
                case "tabela":
                    return Runner.Report(args[1]);
                case "placar":
                    Console.WriteLine(Scoreboard.Read());
                    return 0;
                default:
                    Console.WriteLine("comando desconhecido: " + args[0]);
                    return 1;
            }
        }

        private static string FindRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "IaBench.csproj")))
            {
                dir = Path.GetDirectoryName(dir.TrimEnd('\\'));
            }
            return dir ?? Directory.GetCurrentDirectory();
        }

        /// <summary>A mesma decisão real, com o reasoning_effort aninhado (como o mod faz) e no nível de cima.</summary>
        private static int Proof(string logPaths, string variantSpec = "nested:low,top:low")
        {
            var jobs = (from path in logPaths.Split(',')
                        from variant in variantSpec.Split(',')
                        select (path, variant)).ToList();
            var results = jobs.AsParallel().WithDegreeOfParallelism(8).Select(job =>
            {
                JObject log = JObject.Parse(File.ReadAllText(job.path));
                string[] v = job.variant.Split(':');
                var call = new Call { System = (string)log["sistema"], EffortPlacement = v[0], Effort = v.Length > 1 ? v[1] : "low" };
                call.Turns.Add(("user", (string)log["dossie"]));
                return (job.path, job.variant, r: Client.Send(call));
            }).ToList();
            foreach (var (path, variant, r) in results.OrderBy(x => x.variant).ThenBy(x => x.path))
            {
                Console.WriteLine($"{variant,-11} {Path.GetFileName(path),-14} ok={r.Ok} fim={r.Finish} entrada={r.Prompt} cache={r.Hit} saida={r.Completion} raciocinio={r.ReasoningTokens} US$ {r.Cost:0.00000} {r.Seconds:0}s {r.Error}");
            }
            foreach (var group in results.GroupBy(x => x.variant))
            {
                Console.WriteLine($"MÉDIA {group.Key,-11} raciocinio={group.Average(x => x.r.ReasoningTokens):0} saida={group.Average(x => x.r.Completion):0} US$ {group.Average(x => x.r.Cost):0.00000}");
            }
            Console.WriteLine("placar: " + Scoreboard.Read()["gastoUSD"]);
            return 0;
        }
    }
}
