using System.Collections.Generic;
using System.Linq;
using CurrencyMod.Diplomacia.Capture;

namespace CurrencyMod.Diplomacia.Blocs
{
    /// <summary>
    /// Voz conjunta no Congresso: a pauta "congresso" aprovada fixa a posição do bloco na votação aberta ("lei:A",
    /// "lei:B" ou "K#:E#" numa crise). Cada membro que não é humano e ainda não votou recebe a mesma ordem de voto que
    /// as IAs de linguagem usam (CongressFlow.Vote); o peso de todos vai para o mesmo lado. O jogador só é avisado.
    /// </summary>
    internal static class BlocCongress
    {
        /// <summary>Votos já mandados (bloco:membro:sessão), para não repetir a cada segundo.</summary>
        private static readonly HashSet<string> sent = new HashSet<string>();

        /// <summary>Chave da votação aberta a que a posição se refere ("lei:&lt;civic&gt;:&lt;turno&gt;" ou "crise:&lt;pool&gt;:&lt;turno&gt;").</summary>
        internal static string SessionKeyFor(string choice, WorldCapture capture)
        {
            CapturedCongress congress = capture?.Congress;
            if (congress == null || string.IsNullOrEmpty(choice))
            {
                return null;
            }
            if (choice.StartsWith("lei:"))
            {
                CongressLawVote vote = congress.LawVote;
                return vote != null && vote.Active && vote.Law != null ? $"lei:{vote.Law.Civic}:{vote.TurnBegin}" : null;
            }
            if (TryCrisis(choice, out int pool, out _))
            {
                CongressCrisis crisis = congress.Crises.FirstOrDefault(c => c.Pool == pool && c.Active);
                return crisis != null ? $"crise:{pool}:{crisis.TurnBegin}" : null;
            }
            return null;
        }

        /// <summary>Confere se a posição combina com uma votação aberta agora (validação da pauta).</summary>
        internal static string Check(string choice, WorldCapture capture)
        {
            choice = (choice ?? string.Empty).Trim();
            if (choice != "lei:A" && choice != "lei:B" && !TryCrisis(choice, out _, out _))
            {
                return "a posição é \"lei:A\", \"lei:B\" ou \"K#:E#\" (crise K#, apoiando E#)";
            }
            return SessionKeyFor(choice, capture) == null ? L.N("não há essa votação aberta no Congresso") : null;
        }

        private static bool TryCrisis(string choice, out int pool, out int side)
        {
            pool = side = -1;
            string[] parts = (choice ?? string.Empty).Split(':');
            return parts.Length == 2 && int.TryParse(parts[0].TrimStart('K', 'k'), out pool)
                && int.TryParse(parts[1].TrimStart('E', 'e'), out side) && parts[0].StartsWith("K", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Thread principal, a cada segundo: manda os votos que faltam dos membros.</summary>
        internal static void Apply(IaWorld world, WorldCapture capture, int turn, System.Func<int, bool> isHuman)
        {
            if (world == null || capture?.Congress == null)
            {
                return;
            }
            foreach (Bloc bloc in world.Blocs.Where(b => b.Formed && b.CongressLine != null))
            {
                string choice = bloc.CongressLine.Choice;
                string key = SessionKeyFor(choice, capture);
                if (key == null || key != bloc.CongressLine.SessionKey)
                {
                    continue; // a votação para a qual o bloco decidiu já fechou
                }
                CongressBallot[] ballots;
                var intentTemplate = new ActionIntent { Guid = world.GameGuid, Turn = turn, Name = "votar_congresso", Summary = "voto do bloco " + bloc.Name };
                if (choice.StartsWith("lei:"))
                {
                    ballots = capture.Congress.LawVote.Ballots;
                    intentTemplate.LawVote = true;
                    intentTemplate.Choice = choice == "lei:A" ? 0 : 1;
                }
                else
                {
                    TryCrisis(choice, out int pool, out int side);
                    ballots = capture.Congress.Crises.First(c => c.Pool == pool && c.Active).Ballots;
                    intentTemplate.LawVote = false;
                    intentTemplate.CrisisVote = pool;
                    intentTemplate.Choice = side;
                }
                foreach (int member in bloc.Members)
                {
                    if (isHuman(member) || member >= ballots.Length || ballots[member] == null || ballots[member].Choice >= 0)
                    {
                        continue;
                    }
                    if (!sent.Add($"{bloc.Id}:{member}:{key}"))
                    {
                        continue;
                    }
                    ActionExecutor.Pending.Enqueue(new ActionIntent
                    {
                        Guid = intentTemplate.Guid,
                        Turn = turn,
                        Empire = member,
                        Name = intentTemplate.Name,
                        Summary = intentTemplate.Summary,
                        LawVote = intentTemplate.LawVote,
                        CrisisVote = intentTemplate.CrisisVote,
                        Choice = intentTemplate.Choice,
                    });
                    Plugin.Log.LogInfo($"Bloco {bloc.Handle}: E{member} vota {choice} no Congresso com o bloco.");
                }
            }
        }
    }
}
