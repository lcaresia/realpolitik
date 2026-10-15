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

        /// <summary>Where a motion lives: the Congress's free agenda when the id is one of its motions, else the empire's bloc.</summary>
        internal static Bloc ForumOf(AiWorld world, int empire, int motionId)
        {
            Bloc congress = world?.CongressForum;
            return congress != null && congress.Has(empire) && congress.MotionBy(motionId) != null ? congress : Of(world, empire);
        }

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
                return WaitProblem(wait);
            }
            if (world.Blocs.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return L.N("já existe um bloco com esse nome");
            }
            List<int> guests = (invitees ?? Enumerable.Empty<int>()).Where(e => e != founder).Distinct().ToList();
            if (guests.Count < Bloc.MinMembers - 1)
            {
                // Counts in the text: already translated here (the caller's L.T leaves an unknown text as it is).
                return L.F("convide pelo menos {0} nações (o bloco nasce com {1})", Bloc.MinMembers - 1, Bloc.MinMembers);
            }
            if (guests.Count > Bloc.MaxMembers - 1)
            {
                return L.F("no máximo {0} convites", Bloc.MaxMembers - 1);
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

        private static string WaitProblem(int wait) => L.P(wait, "ainda falta {0} turno da espera depois de sair de um bloco", "ainda faltam {0} turnos da espera depois de sair de um bloco");

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
                return WaitProblem(wait);
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
        /// Free motion (item 46): a title and a text, interpreted later by the technical commission. A regular member
        /// suggests it; the president puts it straight to a vote.
        /// </summary>
        internal static string Propose(AiWorld world, int empire, string title, string text, int turn, out BlocMotion motion, int expel = -1, Bloc forum = null)
        {
            motion = null;
            Bloc bloc = forum ?? Of(world, empire);
            if (forum != null && !forum.Has(empire))
            {
                return L.N("você não faz parte do Congresso Mundial");
            }
            if (forum != null && expel >= 0)
            {
                return L.N("o Congresso não expulsa ninguém");
            }
            if (bloc == null || !bloc.Formed)
            {
                return L.N("você não está num bloco formado");
            }
            text = Clean(text, Bloc.MotionTextMax);
            title = Clean(title, Bloc.MotionTitleMax);
            if (text.Length == 0)
            {
                return L.N("escreva o texto da pauta");
            }
            if (title.Length == 0)
            {
                title = TitleFrom(text);
            }
            if (bloc.Motions.Any(m => (m.Status == BlocMotion.Voting || m.Status == BlocMotion.Suggested) && m.Author == empire && string.Equals(m.Text, text, StringComparison.OrdinalIgnoreCase)))
            {
                return L.N("essa pauta já está aberta");
            }
            if (expel >= 0 && (!bloc.Has(expel) || expel == empire))
            {
                return L.N("só dá para propor a expulsão de outro membro do bloco");
            }
            if (expel >= 0 && bloc.Motions.Any(m => m.Expel == expel && (m.Status == BlocMotion.Voting || m.Status == BlocMotion.Suggested || m.Status == BlocMotion.Tiebreak)))
            {
                return L.N("já há uma expulsão desse membro em andamento");
            }
            if (bloc.SlotsUsed(empire) >= Bloc.MotionSlots)
            {
                return L.F("você já tem {0} pautas ativas (sugeridas, em votação ou em vigor); espere uma acabar", Bloc.MotionSlots);
            }
            motion = new BlocMotion
            {
                Id = world.NextMotionId++,
                Kind = BlocMotion.FreeKind,
                Title = title,
                Text = text,
                Author = empire,
                SuggestedTurn = turn,
                Expel = expel,
            };
            bloc.Motions.Add(motion);
            if (empire == bloc.President)
            {
                Open(bloc, motion, turn);
            }
            else
            {
                bloc.Log(turn, "sugeriu", empire, -1, title);
            }
            TrimMotions(bloc);
            return null;
        }

        /// <summary>Title from the first sentence of the text (when none was given).</summary>
        internal static string TitleFrom(string text)
        {
            text = (text ?? string.Empty).Trim();
            int end = text.IndexOfAny(new[] { '.', '\n', ';', ':' });
            string first = end > 0 ? text.Substring(0, end) : text;
            if (first.Length > 60)
            {
                int space = first.LastIndexOf(' ', 60);
                first = first.Substring(0, space > 20 ? space : 60) + "…";
            }
            return first;
        }

        /// <summary>The president puts a suggestion to a vote (or discards it).</summary>
        internal static string Schedule(AiWorld world, int empire, int motionId, bool open, int turn)
        {
            Bloc bloc = ForumOf(world, empire, motionId);
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
                bloc.Log(turn, "descartada", empire, -1, motion.Title);
                return null;
            }
            Open(bloc, motion, turn);
            return null;
        }

        internal static string Vote(AiWorld world, int empire, int motionId, bool yes, int turn)
        {
            Bloc bloc = ForumOf(world, empire, motionId);
            BlocMotion motion = bloc?.MotionBy(motionId);
            if (motion != null && motion.Status == BlocMotion.Tiebreak && empire == bloc.President)
            {
                return Decide(world, empire, motionId, yes, turn);
            }
            if (motion == null || motion.Status != BlocMotion.Voting)
            {
                return L.N("não há essa pauta em votação no seu bloco");
            }
            if (empire == bloc.President)
            {
                return L.N("o presidente não vota: ele escolhe o que vai a votação e desempata");
            }
            if (empire == motion.Expel)
            {
                return L.N("quem é alvo da expulsão não vota");
            }
            motion.Votes[empire] = yes;
            CloseIfDone(world, bloc, motion, turn);
            return null;
        }

        /// <summary>The president's decision on a motion waiting for the tie-break.</summary>
        internal static string Decide(AiWorld world, int empire, int motionId, bool yes, int turn)
        {
            Bloc bloc = ForumOf(world, empire, motionId);
            BlocMotion motion = bloc?.MotionBy(motionId);
            if (motion == null || motion.Status != BlocMotion.Tiebreak)
            {
                return L.N("não há essa pauta esperando o desempate");
            }
            if (bloc.President != empire)
            {
                return L.N("só o presidente desempata");
            }
            motion.PresidentDecision = yes;
            Close(world, bloc, motion, yes, turn, byPresident: true);
            return null;
        }

        /// <summary>Who votes on a motion: every member but the president and the member it would expel.</summary>
        internal static List<int> Voters(Bloc bloc, BlocMotion motion) => bloc.Members.Where(m => m != bloc.President && m != motion.Expel).ToList();

        private static void Open(Bloc bloc, BlocMotion motion, int turn)
        {
            motion.Status = BlocMotion.Voting;
            motion.OpenedTurn = turn;
            motion.Votes.Clear();
            bloc.Log(turn, "pauta", bloc.President, -1, motion.Title);
        }

        /// <summary>
        /// Closes the motion when every voter has voted, or when the deadline has passed. Simple majority; a tie (or no
        /// vote at all) goes to the president. An expulsion needs 2/3 of the voters; a majority short of that also goes
        /// to the president.
        /// </summary>
        private static void CloseIfDone(AiWorld world, Bloc bloc, BlocMotion motion, int turn)
        {
            List<int> voters = Voters(bloc, motion);
            motion.Votes.Remove(bloc.President); // votes cast before the rule (2026-10-12) do not count
            bool everyone = voters.All(v => motion.Votes.ContainsKey(v));
            if (!everyone && turn < motion.Deadline)
            {
                return;
            }
            int yes = motion.Votes.Count(v => v.Value && voters.Contains(v.Key));
            int no = motion.Votes.Count(v => !v.Value && voters.Contains(v.Key));
            bool tie = yes == no;
            bool shortExpulsion = motion.Expel >= 0 && yes > no && yes * 3 < Math.Max(1, voters.Count) * 2;
            if (tie || shortExpulsion)
            {
                motion.Status = BlocMotion.Tiebreak;
                motion.TiebreakTurn = turn;
                bloc.Log(turn, "desempate", bloc.President, motion.Expel, motion.Title);
                return;
            }
            Close(world, bloc, motion, yes > no, turn, byPresident: false);
        }

        private static void Close(AiWorld world, Bloc bloc, BlocMotion motion, bool passed, int turn, bool byPresident)
        {
            motion.Status = passed ? BlocMotion.Passed : BlocMotion.Failed;
            motion.ClosedTurn = turn;
            bloc.Log(turn, passed ? "aprovada" : "rejeitada", -1, -1, motion.Title);
            if (passed)
            {
                var resolution = new BlocResolution
                {
                    Id = world.NextResolutionId++,
                    MotionId = motion.Id,
                    Title = motion.Title,
                    Text = motion.Text,
                    Author = motion.Author,
                    PassedTurn = turn,
                    Yes = motion.Yes,
                    No = motion.No,
                    Voters = Voters(bloc, motion).Count,
                    Votes = new Dictionary<int, bool>(motion.Votes),
                    ByPresident = byPresident,
                };
                bloc.Resolutions.Add(resolution);
                if (motion.Expel >= 0 && bloc.Has(motion.Expel))
                {
                    // The only mechanical effect a motion has: the expelled member leaves (the resolution is done).
                    Leave(world, motion.Expel, turn, expelled: true);
                    resolution.Status = BlocResolution.ExpiredStatus;
                    resolution.RevokedTurn = turn;
                }
                if (bloc.Resolutions.Count > 30)
                {
                    BlocResolution oldest = bloc.Resolutions.FirstOrDefault(r => r.Status != BlocResolution.InForceStatus);
                    if (oldest != null)
                    {
                        bloc.Resolutions.Remove(oldest);
                    }
                }
            }
        }

        /// <summary>
        /// A member records that another broke a resolution (or the bloc's word). The mod does not judge it: the
        /// accusation is only the accuser's word, shown to every member with its evidence.
        /// </summary>
        internal static string Accuse(AiWorld world, int accuser, int accused, int resolutionId, string evidence, int turn, bool congress = false)
        {
            Bloc forum = world?.CongressForum;
            bool inCongress = forum != null && forum.Has(accuser) && (congress || resolutionId >= 0 && forum.ResolutionBy(resolutionId) != null);
            if (congress && !inCongress)
            {
                return L.N("você não faz parte do Congresso Mundial");
            }
            Bloc bloc = inCongress ? forum : Of(world, accuser);
            if (bloc == null || !bloc.Formed)
            {
                return L.N("você não está num bloco formado");
            }
            if (accused == accuser || !bloc.Has(accused))
            {
                return L.N("só dá para acusar outro membro do seu bloco");
            }
            if (resolutionId >= 0 && bloc.ResolutionBy(resolutionId) == null)
            {
                return L.N("não há essa resolução no seu bloco");
            }
            evidence = Clean(evidence, 300);
            if (evidence.Length == 0)
            {
                return L.N("diga o que você viu (a evidência)");
            }
            if (bloc.Accusations.Any(a => a.Accuser == accuser && a.Accused == accused && a.Resolution == resolutionId && turn - a.Turn < 3))
            {
                return L.N("você já fez essa acusação há pouco");
            }
            bloc.Accusations.Add(new BlocAccusation { Turn = turn, Accuser = accuser, Accused = accused, Resolution = resolutionId, Evidence = evidence });
            if (bloc.Accusations.Count > 60)
            {
                bloc.Accusations.RemoveRange(0, bloc.Accusations.Count - 60);
            }
            BlocResolution resolution = bloc.ResolutionBy(resolutionId);
            bloc.Log(turn, "acusou", accuser, accused, resolution != null ? resolution.Title : null);
            return null;
        }

        /// <summary>
        /// Moves an old save to the free model once: open typed motions become free motions (title from the type, text
        /// from the reason), common tariffs become resolutions in force, and the joint Congress position is cleared.
        /// </summary>
        internal static void Migrate(AiWorld world, Bloc bloc, int turn)
        {
            if (bloc.Migrated)
            {
                return;
            }
            bloc.Migrated = true;
            foreach (BlocMotion motion in bloc.Motions)
            {
                if (motion.Kind == BlocMotion.FreeKind || motion.Kind == null)
                {
                    continue;
                }
                string legacy = BlocDossier.DescribeLegacy(motion, e => "E" + e);
                if (motion.Status == BlocMotion.Voting || motion.Status == BlocMotion.Suggested)
                {
                    motion.Title = TitleFrom(string.IsNullOrWhiteSpace(motion.Reason) ? legacy : motion.Reason);
                    motion.Text = Clean(string.IsNullOrWhiteSpace(motion.Reason) ? legacy : motion.Reason, Bloc.MotionTextMax);
                    motion.Kind = BlocMotion.FreeKind;
                    motion.SuggestedTurn = Math.Max(motion.SuggestedTurn, turn); // the clock restarts with the new model
                }
                else
                {
                    motion.Title = legacy;
                    motion.Text = motion.Reason;
                }
            }
            foreach (BlocTariff tariff in bloc.Tariffs)
            {
                string mode = tariff.Mode == "bloqueio" ? "bloqueio" : "pedágio";
                bloc.Resolutions.Add(new BlocResolution
                {
                    Id = world.NextResolutionId++,
                    Title = $"Tarifa comum: {mode} contra E{tariff.Target}",
                    Text = $"Todos os membros aplicam {mode} nos seus postos comerciais contra E{tariff.Target}.",
                    Author = bloc.President,
                    PassedTurn = turn,
                    Voters = bloc.Members.Count,
                });
            }
            bloc.Tariffs.Clear();
            bloc.CongressLine = null;
            Plugin.Log.LogInfo($"Bloc {bloc.Handle}: moved to free motions ({bloc.Resolutions.Count} resolution(s)).");
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
            if (world.BlocTickTurn != turn && (world.Blocs.Count > 0 || world.CongressForum != null))
            {
                world.BlocTickTurn = turn;
                Tick(world, turn);
                if (world.CongressForum != null)
                {
                    TickMotions(world, world.CongressForum, turn);
                }
                changed = true;
            }
            return changed;
        }

        /// <summary>The motion part of the turn step, also used by the Congress's free agenda: lapsed resolutions, late
        /// tie-breaks, stale suggestions and motions at their deadline.</summary>
        internal static void TickMotions(AiWorld world, Bloc bloc, int turn)
        {
            foreach (BlocResolution lapsed in bloc.InForce.Where(r => turn >= r.Expires).ToList())
            {
                lapsed.Status = BlocResolution.ExpiredStatus;
                lapsed.RevokedTurn = turn;
                bloc.Log(turn, "resolucao_venceu", lapsed.Author, -1, lapsed.Title);
            }
            foreach (BlocMotion waiting in bloc.Motions.Where(m => m.Status == BlocMotion.Tiebreak && turn - m.TiebreakTurn >= BlocMotion.TiebreakTurns).ToList())
            {
                Close(world, bloc, waiting, false, turn, byPresident: false);
            }
            foreach (BlocMotion stale in bloc.Suggestions.Where(m => turn - m.SuggestedTurn >= Bloc.SuggestionTurns).ToList())
            {
                stale.Status = BlocMotion.Dropped;
                stale.ClosedTurn = turn;
                bloc.Log(turn, "expirou", stale.Author, -1, stale.Title);
            }
            foreach (BlocMotion motion in bloc.OpenMotions.ToList())
            {
                CloseIfDone(world, bloc, motion, turn);
            }
        }

        /// <summary>Per-turn step: expired invites and suggestions, failed formation, motions at their deadline and the election.</summary>
        internal static void Tick(AiWorld world, int turn)
        {
            foreach (Bloc bloc in world.Blocs.ToList())
            {
                bloc.Members.RemoveAll(m => m < 0);
                Migrate(world, bloc, turn);
                foreach (BlocResolution lapsed in bloc.InForce.Where(r => turn >= r.Expires).ToList())
                {
                    lapsed.Status = BlocResolution.ExpiredStatus;
                    lapsed.RevokedTurn = turn;
                    bloc.Log(turn, "resolucao_venceu", lapsed.Author, -1, lapsed.Title);
                }
                foreach (BlocMotion waiting in bloc.Motions.Where(m => m.Status == BlocMotion.Tiebreak && turn - m.TiebreakTurn >= BlocMotion.TiebreakTurns).ToList())
                {
                    // The president did not decide in time: the motion fails.
                    Close(world, bloc, waiting, false, turn, byPresident: false);
                }
                foreach (BlocMotion stale in bloc.Suggestions.Where(m => turn - m.SuggestedTurn >= Bloc.SuggestionTurns).ToList())
                {
                    stale.Status = BlocMotion.Dropped;
                    stale.ClosedTurn = turn;
                    bloc.Log(turn, "expirou", stale.Author, -1, stale.Title);
                }
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
