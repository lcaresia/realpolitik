using System.Collections.Generic;
using System.Linq;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy.Blocs
{
    /// <summary>
    /// Joint voice in the Congress: an approved "congresso" agenda item fixes the bloc's position in the open vote
    /// ("lei:A", "lei:B" or "K#:E#" in a crisis). Each member that is not human and has not voted yet gets the same vote
    /// order the language AIs use (CongressFlow.Vote); everyone's weight goes to the same side. The player is only notified.
    /// </summary>
    internal static class BlocCongress
    {
        /// <summary>Votes already sent (bloc:member:session), so they are not repeated every second.</summary>
        private static readonly HashSet<string> sent = new HashSet<string>();

        /// <summary>Key of the open vote the position refers to ("lei:&lt;civic&gt;:&lt;turn&gt;" or "crise:&lt;pool&gt;:&lt;turn&gt;").</summary>
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

        /// <summary>Checks whether the position matches a vote open right now (agenda item validation).</summary>
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

        /// <summary>Main thread, every second: sends the members' missing votes.</summary>
        internal static void Apply(AiWorld world, WorldCapture capture, int turn, System.Func<int, bool> isHuman)
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
                    continue; // the vote the bloc decided on has already closed
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
                    Plugin.Log.LogInfo($"Bloc {bloc.Handle}: E{member} votes {choice} in the Congress with the bloc.");
                }
            }
        }
    }
}
