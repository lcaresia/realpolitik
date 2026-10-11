using System;
using System.Collections.Generic;
using System.Linq;

namespace CurrencyMod.Diplomacy.Blocs
{
    /// <summary>
    /// "ai bloc ..." on the dev channel. By default acts as the player; "as E#" at the end acts as another nation
    /// (to test invites, votes and elections without waiting for the AIs).
    /// </summary>
    internal static class BlocCommands
    {
        internal const string Usage = "usage: ai bloc show | found <name_with_underscores> E1 E2 [...] | accept BLC# | decline BLC# | leave"
            + " | propose <kind> [E#] [arg] | open <motion#> | discard <motion#> | vote <motion#> yes|no | president E# | turn [+N]   (+ \"as E#\" at the end)";

        internal static string Run(AiModule module, string rest)
        {
            AiWorld world = AiModule.World;
            if (world == null || module == null)
            {
                return "error: no game";
            }
            List<string> bits = (rest ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            int actor = module.PlayerIndex;
            int asIndex = bits.FindIndex(b => b == "as");
            if (asIndex >= 0 && asIndex + 1 < bits.Count)
            {
                actor = Empire(bits[asIndex + 1]);
                bits.RemoveRange(asIndex, 2);
            }
            int turn = module.CurrentTurn;
            string sub = bits.Count > 0 ? bits[0] : "show";
            string problem;
            switch (sub)
            {
                case "show":
                    return Describe(world, turn);
                case "found" when bits.Count >= 2:
                    problem = BlocEngine.Found(world, actor, bits[1], bits.Skip(2).Select(Empire).Where(e => e >= 0), turn, out Bloc made);
                    return Done(module, problem, made != null ? $"ok: {made.Handle} '{made.Name}' founded by E{actor}; invites: {string.Join(", ", made.Invites.Select(i => "E" + i.Empire))}" : null);
                case "accept" when bits.Count >= 2:
                case "decline" when bits.Count >= 2:
                    problem = BlocEngine.Respond(world, actor, BlocEngine.ByHandle(world, bits[1]), sub == "accept", turn);
                    return Done(module, problem, $"ok: E{actor} {(sub == "accept" ? "accepted" : "declined")}");
                case "leave":
                    return Done(module, BlocEngine.Leave(world, actor, turn), $"ok: E{actor} left");
                case "propose" when bits.Count >= 2:
                {
                    int target = bits.Count >= 3 ? Empire(bits[2]) : -1;
                    string arg = target >= 0 ? string.Join(" ", bits.Skip(3)) : string.Join(" ", bits.Skip(2));
                    problem = BlocEngine.Propose(world, actor, bits[1], target, arg.Length > 0 ? arg.Replace('_', ' ') : null, "(teste)", turn, out BlocMotion motion);
                    return Done(module, problem, motion != null ? $"ok: motion {motion.Id} ({motion.Kind}) {motion.Status}" : null);
                }
                case "open" when bits.Count >= 2:
                case "discard" when bits.Count >= 2:
                    problem = BlocEngine.Schedule(world, actor, int.TryParse(bits[1], out int id) ? id : -1, sub == "open", turn);
                    return Done(module, problem, "ok");
                case "vote" when bits.Count >= 3:
                    problem = BlocEngine.Vote(world, actor, int.TryParse(bits[1], out int voteId) ? voteId : -1, bits[2] == "yes", turn);
                    return Done(module, problem, "ok: vote by E" + actor);
                case "president" when bits.Count >= 2:
                    return Done(module, BlocEngine.VotePresident(world, actor, Empire(bits[1]), turn), "ok: vote by E" + actor);
                case "turn":
                    // Runs the turn step now, as if N turns had passed (for deadlines): "turn [+N]".
                    int ahead = bits.Count >= 2 && int.TryParse(bits[1].TrimStart('+'), out int n) ? n : 0;
                    BlocEngine.Tick(world, turn + ahead);
                    module.MarkDirty();
                    return Describe(world, turn + ahead);
            }
            return Usage;
        }

        private static string Done(AiModule module, string problem, string ok)
        {
            if (problem != null)
            {
                return "error: " + problem;
            }
            module.MarkDirty();
            return ok ?? "ok";
        }

        private static int Empire(string text) => int.TryParse((text ?? string.Empty).TrimStart('E', 'e'), out int e) ? e : -1;

        internal static string Describe(AiWorld world, int turn)
        {
            if (world.Blocs.Count == 0)
            {
                return "(no blocs)";
            }
            var lines = new List<string>();
            foreach (Bloc b in world.Blocs)
            {
                lines.Add($"{b.Handle} '{b.Name}' {(b.Formed ? "formed T" + b.FormedTurn : "forming")} · members {string.Join(", ", b.Members.Select(m => "E" + m))}"
                    + $" · president E{b.President} (term until T{b.TermEnds}){(b.ElectionOpened >= 0 ? $" · ELECTION open T{b.ElectionOpened}: {string.Join(", ", b.ElectionVotes.Select(v => $"E{v.Key}→E{v.Value}"))}" : string.Empty)}"
                    + $" · group GRP{b.GroupId}");
                if (b.Invites.Count > 0)
                {
                    lines.Add("  invites: " + string.Join(", ", b.Invites.Select(i => $"E{i.Empire} (T{i.Turn}, expires T{i.Turn + Bloc.InviteTurns})")));
                }
                foreach (BlocMotion m in b.Motions.Where(m => m.Status == BlocMotion.Voting || m.Status == BlocMotion.Suggested || turn - m.ClosedTurn <= 3))
                {
                    lines.Add($"  motion {m.Id} {m.Kind}{(m.Target >= 0 ? " E" + m.Target : string.Empty)}{(m.Arg != null ? " " + m.Arg : string.Empty)} · {m.Status}"
                        + $" · yes {m.Yes} no {m.No}{(m.Status == BlocMotion.Voting ? " · deadline T" + m.Deadline : string.Empty)} · by E{m.Author}");
                }
                if (b.Tariffs.Count > 0)
                {
                    lines.Add("  common tariff: " + string.Join(", ", b.Tariffs.Select(t => $"{t.Mode} against E{t.Target} (since T{t.Since})")));
                }
                if (b.CongressLine != null)
                {
                    lines.Add($"  Congress: {b.CongressLine.Choice} (T{b.CongressLine.Turn}, session {b.CongressLine.SessionKey ?? "?"})");
                }
                lines.Add("  history: " + string.Join(" | ", b.History.Skip(Math.Max(0, b.History.Count - 6)).Select(e => $"T{e.Turn} {e.Kind}{(e.Actor >= 0 ? " E" + e.Actor : string.Empty)}{(e.Target >= 0 ? "→E" + e.Target : string.Empty)}{(e.Arg != null ? " " + e.Arg : string.Empty)}")));
            }
            return string.Join("\n", lines);
        }
    }
}
