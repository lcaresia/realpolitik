using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacy.Blocs
{
    /// <summary>
    /// Bloc actions of the language-model AIs (applied by the mod, no game order): fundar_bloco, responder_bloco,
    /// propose_motion (free text; the president puts it straight to a vote, the others suggest; pauta_bloco is the
    /// legacy alias), abrir_pauta (president), votar_pauta, votar_presidente and sair_bloco. Shape validation lives in
    /// DecisionParser; the rules, in BlocEngine.
    /// </summary>
    internal static class BlocActions
    {
        private static readonly HashSet<string> Names = new HashSet<string>
        {
            "fundar_bloco", "responder_bloco", "propose_motion", "pauta_bloco", "accuse_breach", "abrir_pauta", "votar_pauta", "votar_presidente", "sair_bloco",
        };

        internal static bool Handles(string name) => Names.Contains(name);

        internal static string Apply(AiWorld world, int empire, string name, string json, ValidationContext context, int turn)
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
                case "propose_motion":
                case "pauta_bloco":
                {
                    string title = (item["title"] as JValue)?.Value?.ToString();
                    string text = (item["text"] as JValue)?.Value?.ToString() ?? (item["motivo"] as JValue)?.Value?.ToString();
                    string expelHandle = (item["expel"] as JValue)?.Value?.ToString();
                    int expel = expelHandle != null && context.KnownEmpires.TryGetValue(expelHandle.Trim(), out int expelled) ? expelled : -1;
                    bool congress = IsCongress(item);
                    BlocMotion motion = null;
                    problem = congress && world.CongressForum == null ? L.N("não há Congresso Mundial nesta partida")
                        : BlocEngine.Propose(world, empire, title, text, turn, out motion, expel, congress ? world.CongressForum : null);
                    done = motion != null ? $"pauta P{motion.Id} {(congress ? "do Congresso " : string.Empty)}{(motion.Status == BlocMotion.Voting ? "em votação" : "sugerida ao presidente")}" : null;
                    break;
                }
                case "accuse_breach":
                {
                    string accused = (item["nation"] as JValue)?.Value?.ToString();
                    int target = accused != null && context.KnownEmpires.TryGetValue(accused.Trim(), out int e) ? e : -1;
                    string handle = ((item["resolution"] as JValue)?.Value?.ToString() ?? string.Empty).Trim().TrimStart('R', 'r');
                    int resolution = int.TryParse(handle, out int id) ? id : -1;
                    problem = BlocEngine.Accuse(world, empire, target, resolution, (item["evidence"] as JValue)?.Value?.ToString(), turn, IsCongress(item));
                    done = "acusação registrada no livro";
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

        /// <summary>"forum": "congress" sends a motion or an accusation to the World Congress's free agenda.</summary>
        internal static bool IsCongress(JObject item) => string.Equals(((item["forum"] as JValue)?.Value?.ToString() ?? string.Empty).Trim(), "congress", System.StringComparison.OrdinalIgnoreCase);

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
