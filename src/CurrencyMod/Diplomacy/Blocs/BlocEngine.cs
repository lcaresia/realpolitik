using System;
using System.Collections.Generic;
using System.Linq;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy.Blocs
{
    /// <summary>
    /// Bloc rules, without touching the game: found and invite, accept, leave, suggest and table motions, vote, elect the
    /// president and the per-turn step (expired invites, closed motions, election, dissolved bloc). The in-game effects
    /// (toll, grievances, Congress) read the state from here. Every function returns null on success, or the problem.
    /// </summary>
    internal static class BlocEngine
    {
        // ---------------- Queries ----------------

        /// <summary>Formed (or forming) bloc the nation is a member of.</summary>
        internal static Bloc Of(AiWorld world, int empire) => world?.Blocs.FirstOrDefault(b => b.Has(empire));

        internal static Bloc ById(AiWorld world, int id) => world?.Blocs.FirstOrDefault(b => b.Id == id);

        /// <summary>Bloc by its "BLC#" code or by id.</summary>
        internal static Bloc ByHandle(AiWorld world, string handle)
        {
            string digits = (handle ?? string.Empty).Trim().ToUpperInvariant().Replace("BLC", string.Empty);
            return int.TryParse(digits, out int id) ? ById(world, id) : null;
        }

        /// <summary>Both nations are in the same already-formed bloc (free internal trade).</summary>
        internal static bool SameBloc(AiWorld world, int a, int b)
        {
            if (a == b || world == null)
            {
                return false;
            }
            Bloc bloc = Of(world, a);
            return bloc != null && bloc.Formed && bloc.Has(b);
        }

        /// <summary>Open invites for the nation.</summary>
        internal static IEnumerable<Bloc> InvitesFor(AiWorld world, int empire) => world?.Blocs.Where(b => b.InviteFor(empire) != null) ?? Enumerable.Empty<Bloc>();

        /// <summary>Turns still left in the cooldown after leaving a bloc (0 = may join).</summary>
        internal static int CooldownLeft(AiWorld world, int empire, int turn)
        {
            return world.BlocLeftTurn.TryGetValue(empire, out int left) ? Math.Max(0, left + Bloc.LeaveCooldown - turn) : 0;
        }

        // ---------------- Found, invite, respond, leave ----------------

        internal static string Found(AiWorld world, int founder, string name, IEnumerable<int> invitees, int turn, out Bloc bloc)
        {
            bloc = null;
            name = Clean(name, 40);
            if (name.Length == 0)
            {
                return L.N("o bloco precisa de um nome");
            }
            if (Of(world, founder) != null)
            {
                return L.N("você já está num bloco (um bloco por nação)");
            }
            int wait = CooldownLeft(world, founder, turn);
            if (wait > 0)
            {
                return $"ainda faltam {wait} turnos da espera depois de sair de um bloco";
            }
            if (world.Blocs.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return L.N("já existe um bloco com esse nome");
            }
            List<int> guests = (invitees ?? Enumerable.Empty<int>()).Where(e => e != founder).Distinct().ToList();
            if (guests.Count < Bloc.MinMembers - 1)
            {
                return $"convide pelo menos {Bloc.MinMembers - 1} nações (o bloco nasce com {Bloc.MinMembers})";
            }
            if (guests.Count > Bloc.MaxMembers - 1)
            {
                return $"no máximo {Bloc.MaxMembers - 1} convites";
            }
            bloc = new Bloc
            {
                Id = world.NextBlocId++,
                Name = name,
                Founder = founder,
                CreatedTurn = turn,
                Members = new List<int> { founder },
                President = founder,
                TermStart = turn,
            };
            foreach (int guest in guests)
            {
                bloc.Invites.Add(new BlocInvite { Empire = guest, From = founder, Turn = turn });
            }
            bloc.Log(turn, "fundou", founder, -1, string.Join(",", guests));
            world.Blocs.Add(bloc);
            return null;
        }

        /// <summary>Accepts or declines an invite. On accepting, drops the other invites; the bloc forms with 3 members.</summary>
        internal static string Respond(AiWorld world, int empire, Bloc bloc, bool accept, int turn)
        {
            BlocInvite invite = bloc?.InviteFor(empire);
            if (invite == null)
            {
                return L.N("não há convite desse bloco");
            }
            bloc.Invites.Remove(invite);
            if (!accept)
            {
                bloc.Log(turn, "recusou", empire);
                return null;
            }
            if (Of(world, empire) != null)
            {
                return L.N("você já está num bloco (um bloco por nação); saia dele antes");
            }
            int wait = CooldownLeft(world, empire, turn);
            if (wait > 0)
            {
                bloc.Invites.Add(invite);
                return $"ainda faltam {wait} turnos da espera depois de sair de um bloco";
            }
            if (bloc.Members.Count >= Bloc.MaxMembers)
            {
                return L.N("o bloco está cheio");
            }
            bloc.Members.Add(empire);
            foreach (Bloc other in world.Blocs)
            {
                other.Invites.RemoveAll(i => i.Empire == empire);
            }
            bloc.Log(turn, "entrou", empire);
            if (!bloc.Formed && bloc.Members.Count >= Bloc.MinMembers)
            {
                bloc.FormedTurn = turn;
                bloc.TermStart = turn;
                bloc.Log(turn, "formado");
            }
            SyncGroup(world, bloc, turn);
            return null;
        }

        internal static string Leave(AiWorld world, int empire, int turn, bool expelled = false)
        {
            Bloc bloc = Of(world, empire);
            if (bloc == null)
            {
                return L.N("você não está em nenhum bloco");
            }
            bloc.Members.Remove(empire);
            world.BlocLeftTurn[empire] = turn;
            bloc.ElectionVotes.Remove(empire);
            foreach (BlocMotion motion in bloc.OpenMotions)
            {
                motion.Votes.Remove(empire);
            }
            bloc.Log(turn, expelled ? "expulso" : "saiu", empire);
            if (bloc.President == empire && bloc.Members.Count > 0)
            {
                // Interim: the longest-standing member, with a fresh term.
                bloc.President = bloc.Members[0];
                bloc.TermStart = turn;
                bloc.ElectionOpened = -1;
                bloc.ElectionVotes.Clear();
                bloc.Log(turn, "presidente", bloc.President, -1, "interino");
            }
            if (bloc.Formed && bloc.Members.Count < Bloc.MinMembers || !bloc.Formed && bloc.Members.Count == 0)
            {
                Dissolve(world, bloc, turn);
            }
            else
            {
                SyncGroup(world, bloc, turn);
            }
            return null;
        }

        internal static void Dissolve(AiWorld world, Bloc bloc, int turn)
        {
            bloc.Log(turn, "desfeito");
            world.Blocs.Remove(bloc);
            ChatGroup group = world.Groups.FirstOrDefault(g => g.Id == bloc.GroupId);
            if (group != null)
            {
                world.Groups.Remove(group);
            }
        }

        // ---------------- Motions ----------------

        /// <summary>
        /// Suggests a motion (regular member) or puts it straight to a vote (president). tarifa: target + "pedagio"|"bloqueio";
        /// fim_tarifa, admitir, expulsar: target; congresso: arg = position; renomear: arg = name; dissolver.
        /// </summary>
        internal static string Propose(AiWorld world, int empire, string kind, int target, string arg, string reason, int turn, out BlocMotion motion)
        {
            motion = null;
            Bloc bloc = Of(world, empire);
            if (bloc == null || !bloc.Formed)
            {
                return L.N("você não está num bloco formado");
            }
            kind = (kind ?? string.Empty).Trim().ToLowerInvariant();
            if (!BlocMotion.Kinds.Contains(kind))
            {
                return "pauta desconhecida (use: " + string.Join(", ", BlocMotion.Kinds) + ")";
            }
            string problem = CheckMotion(world, bloc, kind, target, ref arg);
            if (problem != null)
            {
                return problem;
            }
            if (bloc.Motions.Any(m => (m.Status == BlocMotion.Voting || m.Status == BlocMotion.Suggested) && m.Kind == kind && m.Target == target && m.Arg == arg))
            {
                return L.N("essa pauta já está aberta");
            }
            motion = new BlocMotion
            {
                Id = world.NextMotionId++,
                Kind = kind,
                Target = target,
                Arg = arg,
                Reason = Clean(reason, 200),
                Author = empire,
                SuggestedTurn = turn,
            };
            bloc.Motions.Add(motion);
            if (empire == bloc.President)
            {
                Open(bloc, motion, turn);
            }
            else
            {
                bloc.Log(turn, "sugeriu", empire, target, kind + (motion.Arg != null ? ":" + motion.Arg : string.Empty));
            }
            TrimMotions(bloc);
            return null;
        }

        /// <summary>The president puts a suggestion to a vote (or discards it).</summary>
        internal static string Schedule(AiWorld world, int empire, int motionId, bool open, int turn)
        {
            Bloc bloc = Of(world, empire);
            BlocMotion motion = bloc?.MotionBy(motionId);
            if (motion == null || motion.Status != BlocMotion.Suggested)
            {
                return L.N("não há essa sugestão no seu bloco");
            }
            if (bloc.President != empire)
            {
                return L.N("só o presidente põe pautas em votação");
            }
            if (!open)
            {
                motion.Status = BlocMotion.Dropped;
                motion.ClosedTurn = turn;
                bloc.Log(turn, "descartada", empire, motion.Target, motion.Kind);
                return null;
            }
            string arg = motion.Arg;
            string problem = CheckMotion(world, bloc, motion.Kind, motion.Target, ref arg);
            if (problem != null)
            {
                return problem;
            }
            Open(bloc, motion, turn);
            return null;
        }

        internal static string Vote(AiWorld world, int empire, int motionId, bool yes, int turn)
        {
            Bloc bloc = Of(world, empire);
            BlocMotion motion = bloc?.MotionBy(motionId);
            if (motion == null || motion.Status != BlocMotion.Voting)
            {
                return L.N("não há essa pauta em votação no seu bloco");
            }
            if (motion.Kind == "expulsar" && motion.Target == empire)
            {
                return L.N("quem é alvo da expulsão não vota");
            }
            motion.Votes[empire] = yes;
            CloseIfDone(world, bloc, motion, turn);
            return null;
        }

        private static void Open(Bloc bloc, BlocMotion motion, int turn)
        {
            motion.Status = BlocMotion.Voting;
            motion.OpenedTurn = turn;
            motion.Votes.Clear();
            bloc.Log(turn, "pauta", bloc.President, motion.Target, motion.Kind + (motion.Arg != null ? ":" + motion.Arg : string.Empty));
        }

        private static string CheckMotion(AiWorld world, Bloc bloc, string kind, int target, ref string arg)
        {
            switch (kind)
            {
                case "tarifa":
                    arg = (arg ?? "pedagio").Trim().ToLowerInvariant().Replace("pedágio", "pedagio");
                    if (arg != "pedagio" && arg != "bloqueio")
                    {
                        return "a tarifa é \"pedagio\" ou \"bloqueio\"";
                    }
                    if (target < 0 || bloc.Has(target))
                    {
                        return L.N("a tarifa comum é contra uma nação de fora do bloco");
                    }
                    return null;
                case "fim_tarifa":
                    arg = null;
                    return bloc.Tariffs.Any(t => t.Target == target) ? null : L.N("não há tarifa comum contra essa nação");
                case "admitir":
                    arg = null;
                    if (target < 0 || bloc.Has(target))
                    {
                        return L.N("essa nação já é membro");
                    }
                    if (bloc.Members.Count + bloc.Invites.Count >= Bloc.MaxMembers)
                    {
                        return L.N("o bloco está cheio");
                    }
                    return bloc.InviteFor(target) != null ? L.N("essa nação já tem um convite aberto") : null;
                case "expulsar":
                    arg = null;
                    return bloc.Has(target) ? null : L.N("essa nação não é membro");
                case "congresso":
                    arg = (arg ?? string.Empty).Trim();
                    return BlocCongress.Check(arg, TurnCapture.Latest);
                case "renomear":
                    arg = Clean(arg, 40);
                    if (arg.Length == 0)
                    {
                        return L.N("diga o nome novo");
                    }
                    string name = arg;
                    return world.Blocs.Any(b => b != bloc && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) ? L.N("já existe um bloco com esse nome") : null;
                case "dissolver":
                    arg = null;
                    return null;
            }
            return L.N("pauta desconhecida");
        }

        /// <summary>Closes the motion when everyone eligible has voted, or when the deadline has passed.</summary>
        private static void CloseIfDone(AiWorld world, Bloc bloc, BlocMotion motion, int turn)
        {
            List<int> voters = Voters(bloc, motion);
            bool everyone = voters.All(v => motion.Votes.ContainsKey(v));
            if (!everyone && turn < motion.Deadline)
            {
                return;
            }
            bool passed;
            if (motion.Kind == "expulsar")
            {
                passed = motion.Votes.Count(v => v.Value && voters.Contains(v.Key)) * 3 >= voters.Count * 2;
            }
            else if (motion.Yes != motion.No)
            {
                passed = motion.Yes > motion.No;
            }
            else
            {
                // Tie (including nobody voted): the president's vote decides; without it, the motion fails.
                passed = motion.Votes.TryGetValue(bloc.President, out bool presidentYes) && presidentYes && motion.Yes > 0;
            }
            motion.Status = passed ? BlocMotion.Passed : BlocMotion.Failed;
            motion.ClosedTurn = turn;
            bloc.Log(turn, passed ? "aprovada" : "rejeitada", -1, motion.Target, motion.Kind + (motion.Arg != null ? ":" + motion.Arg : string.Empty));
            if (passed)
            {
                Apply(world, bloc, motion, turn);
            }
        }

        private static List<int> Voters(Bloc bloc, BlocMotion motion)
        {
            return bloc.Members.Where(m => !(motion.Kind == "expulsar" && m == motion.Target)).ToList();
        }

        private static void Apply(AiWorld world, Bloc bloc, BlocMotion motion, int turn)
        {
            switch (motion.Kind)
            {
                case "tarifa":
                    bloc.Tariffs.RemoveAll(t => t.Target == motion.Target);
                    bloc.Tariffs.Add(new BlocTariff { Target = motion.Target, Mode = motion.Arg, Since = turn });
                    break;
                case "fim_tarifa":
                    bloc.Tariffs.RemoveAll(t => t.Target == motion.Target);
                    break;
                case "admitir":
                    if (!bloc.Has(motion.Target) && bloc.InviteFor(motion.Target) == null)
                    {
                        bloc.Invites.Add(new BlocInvite { Empire = motion.Target, From = bloc.President, Turn = turn });
                        bloc.Log(turn, "convidou", bloc.President, motion.Target);
                    }
                    break;
                case "expulsar":
                    Leave(world, motion.Target, turn, expelled: true);
                    break;
                case "congresso":
                    bloc.CongressLine = new BlocCongressLine { Choice = motion.Arg, Turn = turn, SessionKey = BlocCongress.SessionKeyFor(motion.Arg, TurnCapture.Latest) };
                    break;
                case "renomear":
                    bloc.Log(turn, "renomeado", -1, -1, bloc.Name);
                    bloc.Name = motion.Arg;
                    ChatGroup group = world.Groups.FirstOrDefault(g => g.Id == bloc.GroupId);
                    if (group != null)
                    {
                        group.Name = bloc.Name;
                    }
                    break;
                case "dissolver":
                    Dissolve(world, bloc, turn);
                    break;
            }
        }

        // ---------------- President ----------------

        internal static string VotePresident(AiWorld world, int empire, int candidate, int turn)
        {
            Bloc bloc = Of(world, empire);
            if (bloc == null || bloc.ElectionOpened < 0)
            {
                return L.N("não há eleição aberta no seu bloco");
            }
            if (!bloc.Has(candidate))
            {
                return L.N("o candidato tem de ser membro do bloco");
            }
            bloc.ElectionVotes[empire] = candidate;
            if (bloc.Members.All(m => bloc.ElectionVotes.ContainsKey(m)))
            {
                CloseElection(bloc, turn);
            }
            return null;
        }

        private static void CloseElection(Bloc bloc, int turn)
        {
            var tally = bloc.ElectionVotes.Where(v => bloc.Has(v.Key) && bloc.Has(v.Value)).GroupBy(v => v.Value)
                .Select(g => new { Candidate = g.Key, Votes = g.Count() }).OrderByDescending(t => t.Votes).ToList();
            int winner = bloc.President;
            if (tally.Count > 0 && (tally.Count == 1 || tally[0].Votes > tally[1].Votes))
            {
                winner = tally[0].Candidate;
            }
            bloc.Log(turn, "presidente", winner, bloc.President, winner == bloc.President ? "reeleito" : "eleito");
            bloc.President = winner;
            bloc.TermStart = turn;
            bloc.ElectionOpened = -1;
            bloc.ElectionVotes.Clear();
        }

        // ---------------- Turn ----------------

        /// <summary>
        /// Called every second by the main thread: records the mutual defenses triggered in the simulation and runs
        /// Tick once per turn. Returns true if anything changed (the world needs to go into the save).
        /// </summary>
        internal static bool Step(AiWorld world, int turn)
        {
            bool changed = false;
            while (BlocDefense.Calls.TryDequeue(out Tuple<int, int, int[]> call))
            {
                Bloc bloc = Of(world, call.Item2);
                if (bloc != null)
                {
                    bloc.Log(turn, "defesa", call.Item1, call.Item2, string.Join(",", call.Item3));
                    changed = true;
                }
            }
            if (world.BlocTickTurn != turn && world.Blocs.Count > 0)
            {
                world.BlocTickTurn = turn;
                Tick(world, turn);
                changed = true;
            }
            return changed;
        }

        /// <summary>Per-turn step: expired invites, failed formation, motions at their deadline and the election.</summary>
        internal static void Tick(AiWorld world, int turn)
        {
            foreach (Bloc bloc in world.Blocs.ToList())
            {
                bloc.Members.RemoveAll(m => m < 0);
                foreach (BlocInvite invite in bloc.Invites.Where(i => turn - i.Turn >= Bloc.InviteTurns).ToList())
                {
                    bloc.Invites.Remove(invite);
                    bloc.Log(turn, "convite_venceu", -1, invite.Empire);
                }
                if (!bloc.Formed && bloc.Invites.Count == 0 && bloc.Members.Count < Bloc.MinMembers)
                {
                    bloc.Log(turn, "falhou");
                    Dissolve(world, bloc, turn);
                    continue;
                }
                foreach (BlocMotion motion in bloc.OpenMotions.ToList())
                {
                    CloseIfDone(world, bloc, motion, turn);
                    if (!world.Blocs.Contains(bloc))
                    {
                        break;
                    }
                }
                if (!world.Blocs.Contains(bloc) || !bloc.Formed)
                {
                    continue;
                }
                if (bloc.ElectionOpened < 0 && turn >= bloc.TermEnds)
                {
                    bloc.ElectionOpened = turn;
                    bloc.ElectionVotes.Clear();
                    bloc.Log(turn, "eleicao");
                }
                else if (bloc.ElectionOpened >= 0 && turn >= bloc.ElectionOpened + Bloc.VoteTurns)
                {
                    CloseElection(bloc, turn);
                }
            }
        }

        // ---------------- Helpers ----------------

        /// <summary>The bloc's chat group: created when the bloc forms, with the same members.</summary>
        private static void SyncGroup(AiWorld world, Bloc bloc, int turn)
        {
            if (!bloc.Formed)
            {
                return;
            }
            ChatGroup group = world.Groups.FirstOrDefault(g => g.Id == bloc.GroupId);
            if (group == null)
            {
                group = new ChatGroup { Id = world.NextGroupId++, Name = bloc.Name, Creator = bloc.Founder, CreatedTurn = turn, BlocId = bloc.Id };
                world.Groups.Add(group);
                bloc.GroupId = group.Id;
            }
            group.Members = new List<int>(bloc.Members);
        }

        private static void TrimMotions(Bloc bloc)
        {
            List<BlocMotion> closed = bloc.Motions.Where(m => m.Status != BlocMotion.Voting && m.Status != BlocMotion.Suggested).ToList();
            if (closed.Count > 30)
            {
                foreach (BlocMotion old in closed.Take(closed.Count - 30))
                {
                    bloc.Motions.Remove(old);
                }
            }
        }

        private static string Clean(string text, int max)
        {
            text = (text ?? string.Empty).Replace("<", "‹").Replace(">", "›").Replace('_', ' ').Trim();
            return text.Length > max ? text.Substring(0, max).TrimEnd() : text;
        }
    }
}
