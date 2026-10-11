using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia.Blocs
{
    /// <summary>
    /// Bloc actions of the language-model AIs (applied by the mod, no game order): fundar_bloco, responder_bloco,
    /// pauta_bloco (the president puts it straight to a vote; the others suggest), abrir_pauta (president), votar_pauta,
    /// votar_presidente and sair_bloco. Shape validation lives in DecisionParser; the rules, in BlocEngine.
    /// </summary>
    internal static class BlocActions
    {
        private static readonly HashSet<string> Names = new HashSet<string>
        {
            "fundar_bloco", "responder_bloco", "pauta_bloco", "abrir_pauta", "votar_pauta", "votar_presidente", "sair_bloco",
        };

        internal static bool Handles(string name) => Names.Contains(name);

        internal static string Apply(IaWorld world, int empire, string name, string json, ValidationContext context, int turn)
        {
            JObject item = JObject.Parse(json);
            string problem;
            string done;
            switch (name)
            {
                case "fundar_bloco":
                {
                    var guests = Codes(item["convidados"]).Select(h => context.KnownEmpires.TryGetValue(h, out int e) ? e : -1).Where(e => e >= 0);
                    problem = BlocEngine.Found(world, empire, (string)item["nome"], guests, turn, out Bloc bloc);
                    done = bloc != null ? $"bloco {bloc.Handle} '{bloc.Name}' fundado; convites enviados" : null;
                    break;
                }
                case "responder_bloco":
                {
                    bool accept = item["aceitar"]?.Type == JTokenType.Boolean && (bool)item["aceitar"];
                    problem = BlocEngine.Respond(world, empire, BlocEngine.ByHandle(world, (string)item["bloco"]), accept, turn);
                    done = accept ? "entrou no bloco" : "recusou o convite";
                    break;
                }
                case "pauta_bloco":
                {
                    string handle = (string)item["nacao"];
                    int target = handle != null && context.KnownEmpires.TryGetValue(handle, out int e) ? e : -1;
                    problem = BlocEngine.Propose(world, empire, (string)item["tipo"], target, (string)item["valor"], (string)item["motivo"], turn, out BlocMotion motion);
                    done = motion != null ? $"pauta P{motion.Id} {(motion.Status == BlocMotion.Voting ? "em votação" : "sugerida ao presidente")}" : null;
                    break;
                }
                case "abrir_pauta":
                {
                    bool open = item["abrir"]?.Type != JTokenType.Boolean || (bool)item["abrir"];
                    problem = BlocEngine.Schedule(world, empire, MotionId((string)item["pauta"]), open, turn);
                    done = open ? "pauta em votação" : "sugestão descartada";
                    break;
                }
                case "votar_pauta":
                {
                    string vote = ((string)item["voto"] ?? string.Empty).Trim().ToLowerInvariant();
                    problem = BlocEngine.Vote(world, empire, MotionId((string)item["pauta"]), vote.StartsWith("s") || vote == "true", turn);
                    done = "voto registrado";
                    break;
                }
                case "votar_presidente":
                {
                    string handle = (string)item["candidato"];
                    int candidate = handle != null && context.KnownEmpires.TryGetValue(handle, out int e) ? e : handle == "E" + empire ? empire : -1;
                    problem = BlocEngine.VotePresident(world, empire, candidate, turn);
                    done = "voto registrado";
                    break;
                }
                case "sair_bloco":
                    problem = BlocEngine.Leave(world, empire, turn);
                    done = "saiu do bloco";
                    break;
                default:
                    return "não aplicada: ação de bloco desconhecida";
            }
            return problem == null ? "feito: " + done : "não aplicada: " + problem;
        }

        internal static int MotionId(string handle) => int.TryParse((handle ?? string.Empty).Trim().TrimStart('P', 'p'), out int id) ? id : -1;

        private static List<string> Codes(JToken token)
        {
            if (token is JArray array)
            {
                return array.Select(t => ((string)t ?? string.Empty).Trim().ToUpperInvariant()).Where(t => t.Length > 0).ToList();
            }
            string text = (string)token;
            return (text ?? string.Empty).Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().ToUpperInvariant()).ToList();
        }
    }
}
