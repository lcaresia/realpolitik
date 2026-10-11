using System;
using System.Collections.Generic;
using System.Linq;

namespace CurrencyMod.Diplomacy.Blocs
{
    /// <summary>
    /// "ia bloco ..." on the dev channel. By default acts as the player; "como E#" at the end acts as another nation
    /// (to test invites, votes and elections without waiting for the AIs).
    /// </summary>
    internal static class BlocCommands
    {
        internal const string Usage = "uso: ia bloco ver | fundar <nome_com_underscores> E1 E2 [...] | aceitar BLC# | recusar BLC# | sair"
            + " | pautar <tipo> [E#] [arg] | abrir <pauta#> | descartar <pauta#> | votar <pauta#> sim|nao | presidente E# | turno   (+ \"como E#\" no fim)";

        internal static string Run(AiModule module, string rest)
        {
            AiWorld world = AiModule.World;
            if (world == null || module == null)
            {
                return "erro: sem partida";
            }
            List<string> bits = (rest ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            int actor = module.PlayerIndex;
            int como = bits.FindIndex(b => b == "como");
            if (como >= 0 && como + 1 < bits.Count)
            {
                actor = Empire(bits[como + 1]);
                bits.RemoveRange(como, 2);
            }
            int turn = module.CurrentTurn;
            string sub = bits.Count > 0 ? bits[0] : "ver";
            string problem;
            switch (sub)
            {
                case "ver":
                    return Describe(world, turn);
                case "fundar" when bits.Count >= 2:
                    problem = BlocEngine.Found(world, actor, bits[1], bits.Skip(2).Select(Empire).Where(e => e >= 0), turn, out Bloc made);
                    return Done(module, problem, made != null ? $"ok: {made.Handle} '{made.Name}' fundado por E{actor}; convites: {string.Join(", ", made.Invites.Select(i => "E" + i.Empire))}" : null);
                case "aceitar" when bits.Count >= 2:
                case "recusar" when bits.Count >= 2:
                    problem = BlocEngine.Respond(world, actor, BlocEngine.ByHandle(world, bits[1]), sub == "aceitar", turn);
                    return Done(module, problem, $"ok: E{actor} {(sub == "aceitar" ? "aceitou" : "recusou")}");
                case "sair":
                    return Done(module, BlocEngine.Leave(world, actor, turn), $"ok: E{actor} saiu");
                case "pautar" when bits.Count >= 2:
                {
                    int target = bits.Count >= 3 ? Empire(bits[2]) : -1;
                    string arg = target >= 0 ? string.Join(" ", bits.Skip(3)) : string.Join(" ", bits.Skip(2));
                    problem = BlocEngine.Propose(world, actor, bits[1], target, arg.Length > 0 ? arg.Replace('_', ' ') : null, "(teste)", turn, out BlocMotion motion);
                    return Done(module, problem, motion != null ? $"ok: pauta {motion.Id} ({motion.Kind}) {motion.Status}" : null);
                }
                case "abrir" when bits.Count >= 2:
                case "descartar" when bits.Count >= 2:
                    problem = BlocEngine.Schedule(world, actor, int.TryParse(bits[1], out int id) ? id : -1, sub == "abrir", turn);
                    return Done(module, problem, "ok");
                case "votar" when bits.Count >= 3:
                    problem = BlocEngine.Vote(world, actor, int.TryParse(bits[1], out int voteId) ? voteId : -1, bits[2].StartsWith("s"), turn);
                    return Done(module, problem, "ok: voto de E" + actor);
                case "presidente" when bits.Count >= 2:
                    return Done(module, BlocEngine.VotePresident(world, actor, Empire(bits[1]), turn), "ok: voto de E" + actor);
                case "turno":
                    // Runs the turn step now, as if N turns had passed (for deadlines): "turno [+N]".
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
                return "erro: " + problem;
            }
            module.MarkDirty();
            return ok ?? "ok";
        }

        private static int Empire(string text) => int.TryParse((text ?? string.Empty).TrimStart('E', 'e'), out int e) ? e : -1;

        internal static string Describe(AiWorld world, int turn)
        {
            if (world.Blocs.Count == 0)
            {
                return "(nenhum bloco)";
            }
            var lines = new List<string>();
            foreach (Bloc b in world.Blocs)
            {
                lines.Add($"{b.Handle} '{b.Name}' {(b.Formed ? "formado T" + b.FormedTurn : "em formação")} · membros {string.Join(", ", b.Members.Select(m => "E" + m))}"
                    + $" · presidente E{b.President} (mandato até T{b.TermEnds}){(b.ElectionOpened >= 0 ? $" · ELEIÇÃO aberta T{b.ElectionOpened}: {string.Join(", ", b.ElectionVotes.Select(v => $"E{v.Key}→E{v.Value}"))}" : string.Empty)}"
                    + $" · grupo GRP{b.GroupId}");
                if (b.Invites.Count > 0)
                {
                    lines.Add("  convites: " + string.Join(", ", b.Invites.Select(i => $"E{i.Empire} (T{i.Turn}, vence T{i.Turn + Bloc.InviteTurns})")));
                }
                foreach (BlocMotion m in b.Motions.Where(m => m.Status == BlocMotion.Voting || m.Status == BlocMotion.Suggested || turn - m.ClosedTurn <= 3))
                {
                    lines.Add($"  pauta {m.Id} {m.Kind}{(m.Target >= 0 ? " E" + m.Target : string.Empty)}{(m.Arg != null ? " " + m.Arg : string.Empty)} · {m.Status}"
                        + $" · sim {m.Yes} não {m.No}{(m.Status == BlocMotion.Voting ? " · prazo T" + m.Deadline : string.Empty)} · por E{m.Author}");
                }
                if (b.Tariffs.Count > 0)
                {
                    lines.Add("  tarifa comum: " + string.Join(", ", b.Tariffs.Select(t => $"{t.Mode} contra E{t.Target} (desde T{t.Since})")));
                }
                if (b.CongressLine != null)
                {
                    lines.Add($"  Congresso: {b.CongressLine.Choice} (T{b.CongressLine.Turn}, sessão {b.CongressLine.SessionKey ?? "?"})");
                }
                lines.Add("  histórico: " + string.Join(" | ", b.History.Skip(Math.Max(0, b.History.Count - 6)).Select(e => $"T{e.Turn} {e.Kind}{(e.Actor >= 0 ? " E" + e.Actor : string.Empty)}{(e.Target >= 0 ? "→E" + e.Target : string.Empty)}{(e.Arg != null ? " " + e.Arg : string.Empty)}")));
            }
            return string.Join("\n", lines);
        }
    }
}
