using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacy.Capture;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacy.Council
{
    /// <summary>A nation's minister: who they are (from the bank), the portfolio, the numbers and the latest opinion.</summary>
    internal sealed class Minister
    {
        public string Portfolio;
        public string PersonalityId;
        public string Name;
        public string Gender;
        public List<string> Traits = new List<string>();
        public string Voice;
        /// <summary>0 to 100: goes up and down with the portfolio's numbers.</summary>
        public int Credibility = 50;
        /// <summary>-100 to 100: how much the leader likes them (follows or ignores the advice).</summary>
        public int Affection;
        public int Loyalty = 60;
        public int Ambition = 40;
        public int Since;
        /// <summary>Which of the portfolio's portraits (Portraits.Get): set at appointment so candidates never share a face.</summary>
        public int PortraitVariant;
        /// <summary>Sympathy (+) or antipathy (-) toward other nations, from -50 to 50.</summary>
        public Dictionary<int, int> Sympathies = new Dictionary<int, int>();
        public string Opinion;
        /// <summary>The same opinion in the UI language (screen only; the dossier uses Opinion, always in Portuguese).</summary>
        public string OpinionUi;
        /// <summary>The advice as a tag, to know whether the leader followed it: "paz:E3", "foco:ciencia", "economizar"…</summary>
        public string Advice;
        public double Urgency;
        public int OpinionTurn = -1;
        /// <summary>Portfolio metric value at the last update (for credibility).</summary>
        public double LastMetric = double.NaN;

        internal bool Has(string trait) => Traits.Any(t => CouncilBank.TraitKey(t) == trait);
    }

    /// <summary>
    /// Councils of ministers of the AI nations (design §10). All rule-based, no AI call: every turn each minister reads
    /// their own portfolio's numbers (only what the nation knows), gains or loses credibility and gives short advice,
    /// shaped by their traits and their sympathies toward other nations. The Hand summarizes the agenda. The leader's
    /// dossier lists the opinions by weight (credibility × leader's affection × urgency), and the leader can fire.
    /// </summary>
    internal static class CouncilEngine
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        // ---------------- Formation ----------------

        // Model from 2026-10-10 (melhorias-jogando item 3): the council starts with only the Hand; each vacant portfolio
        // has 3 candidates (the same until the era changes or someone is appointed) and appointing costs influence, once.
        // Firing opens the seat.

        /// <summary>Candidates per vacant portfolio.</summary>
        internal const int CandidatesPerSeat = 3;

        /// <summary>Influence to appoint a minister in the era (0 = Neolithic): 25, 50 in the Ancient… 175 in the Contemporary.</summary>
        internal static int AppointCost(int era) => 25 * (Math.Max(0, era) + 1);

        /// <summary>Veteran bonus, in %, in the portfolio's area: +1% every 5 turns in office, up to +10%. The Hand has none.</summary>
        internal static int VeteranBonus(Minister minister, int turn) =>
            minister == null || minister.Portfolio == "mao" ? 0 : Math.Max(0, Math.Min(10, (turn - minister.Since) / 5));

        /// <summary>
        /// The Hand always present and the candidates of each vacant portfolio up to date. Old save (12 seats): the council
        /// goes back to only the Hand, once.
        /// </summary>
        internal static void Ensure(AiWorld world, AiNation nation, WorldCapture capture, int turn)
        {
            if (nation.CouncilModel < 1)
            {
                nation.Council.RemoveAll(m => m.Portfolio != "mao");
                nation.CouncilCandidates = new Dictionary<string, List<string>>();
                nation.CouncilCandidatesEra = -1;
                nation.CouncilModel = 1;
            }
            nation.CouncilCandidates = nation.CouncilCandidates ?? new Dictionary<string, List<string>>();
            if (!nation.Council.Any(m => m.Portfolio == "mao"))
            {
                Personality hand = Pick(world, nation, "mao", turn, 0, new HashSet<string>());
                if (hand != null)
                {
                    nation.Council.Add(Make(world, nation, capture, hand, "mao", turn));
                }
            }
            nation.CouncilCandidatesSince = nation.CouncilCandidatesSince ?? new Dictionary<string, int>();
            nation.CouncilCandidateLeft = nation.CouncilCandidateLeft ?? new Dictionary<string, string>();
            int era = capture?.Empire(nation.EmpireIndex)?.EraIndex ?? 0;
            if (era != nation.CouncilCandidatesEra)
            {
                nation.CouncilCandidates.Clear();
                nation.CouncilCandidatesSince.Clear();
                nation.CouncilCandidateLeft.Clear();
                nation.CouncilCandidatesEra = era;
            }
            var taken = new HashSet<string>(nation.CouncilCandidates.Values.SelectMany(v => v));
            foreach (string portfolio in CouncilBank.Portfolios)
            {
                if (portfolio == "mao" || nation.Council.Any(m => m.Portfolio == portfolio))
                {
                    nation.CouncilCandidates.Remove(portfolio);
                    nation.CouncilCandidatesSince.Remove(portfolio);
                    nation.CouncilCandidateLeft.Remove(portfolio);
                    continue;
                }
                if (nation.CouncilCandidates.TryGetValue(portfolio, out List<string> current) && current.Count > 0)
                {
                    RotateCandidate(world, nation, portfolio, current, era, turn, taken);
                    continue;
                }
                if (turn >= 0)
                {
                    nation.CouncilCandidatesSince[portfolio] = turn;
                }
                var picks = new List<string>();
                for (int attempt = 0; picks.Count < CandidatesPerSeat && attempt < CandidatesPerSeat * 4; attempt++)
                {
                    Personality person = Pick(world, nation, portfolio, era, attempt, taken);
                    if (person == null)
                    {
                        break;
                    }
                    // At most 2 of each gender per seat, so each candidate has a different portrait.
                    if (picks.Count(id => CouncilBank.ById(id)?.Gender == person.Gender) >= 2)
                    {
                        continue;
                    }
                    taken.Add(person.Id);
                    picks.Add(person.Id);
                }
                nation.CouncilCandidates[portfolio] = picks;
                // A new draw goes into the save right away (otherwise a save/load or a core reload draws others).
                AiModule.Instance?.MarkDirty();
            }
            nation.Council.Sort((a, b) => Array.IndexOf(CouncilBank.Portfolios, a.Portfolio).CompareTo(Array.IndexOf(CouncilBank.Portfolios, b.Portfolio)));
        }

        /// <summary>Turns a candidate waits before one of the seat gives up: 10 on Normal speed, scaled like the meetings.</summary>
        internal static int CandidatePatience() => Math.Max(3, (int)Math.Round(10 * PlayerCouncil.SpeedMultiplier()));

        /// <summary>
        /// Every <see cref="CandidatePatience"/> turns one of a vacant seat's candidates gets tired of waiting and leaves;
        /// a new one takes the place (Discord feedback 2026-10-14: the same 3 people waited the whole era). Only one at a
        /// time, so the player never loses all the names they were weighing at once.
        /// </summary>
        private static void RotateCandidate(AiWorld world, AiNation nation, string portfolio, List<string> current, int era, int turn, HashSet<string> taken)
        {
            if (turn < 0)
            {
                return; // turn not known yet (right after a load or a core reload): never count from -1
            }
            if (!nation.CouncilCandidatesSince.TryGetValue(portfolio, out int since) || since > turn || since < 0)
            {
                // Lists drawn before this rule (old saves) start counting now.
                if (turn >= 0)
                {
                    nation.CouncilCandidatesSince[portfolio] = turn;
                }
                AiModule.Instance?.MarkDirty();
                return;
            }
            if (turn - since < CandidatePatience())
            {
                return;
            }
            int index = (int)(Hash($"{world.GameGuid}|{nation.EmpireIndex}|{portfolio}|{turn}|leaves") % (uint)current.Count);
            string leaving = current[index];
            var others = current.Where(id => id != leaving).ToList();
            Personality newcomer = null;
            for (int attempt = 0; attempt < CandidatesPerSeat * 4 && newcomer == null; attempt++)
            {
                Personality person = Pick(world, nation, portfolio, era * 1000 + turn, attempt, new HashSet<string>(taken) { leaving });
                if (person != null && others.Count(id => CouncilBank.ById(id)?.Gender == person.Gender) < 2)
                {
                    newcomer = person;
                }
            }
            nation.CouncilCandidatesSince[portfolio] = turn;
            if (newcomer == null)
            {
                return; // nobody free in the bank: the same people keep waiting
            }
            current[index] = newcomer.Id;
            taken.Add(newcomer.Id);
            nation.CouncilCandidateLeft[portfolio] = $"{leaving}|{newcomer.Id}|{turn}";
            AiModule.Instance?.MarkDirty();
            PlayerCouncil.NoteCandidateLeft(world, nation, portfolio, leaving, newcomer.Id, era, turn);
        }

        /// <summary>
        /// Appoints one of the portfolio's candidates (the influence cost is charged by the caller). Returns null and the
        /// reason in <paramref name="problem"/> when it cannot.
        /// </summary>
        internal static Minister Appoint(AiWorld world, AiNation nation, WorldCapture capture, string portfolio, string personalityId, int turn, out string problem)
        {
            problem = null;
            Ensure(world, nation, capture, turn);
            if (portfolio == "mao" || !CouncilBank.Portfolios.Contains(portfolio))
            {
                problem = "pasta inválida";
                return null;
            }
            if (nation.Council.Any(m => m.Portfolio == portfolio))
            {
                problem = "a pasta já tem ministro (demita antes)";
                return null;
            }
            if (!nation.CouncilCandidates.TryGetValue(portfolio, out List<string> candidates) || !candidates.Contains(personalityId))
            {
                problem = "não é um dos candidatos desta pasta";
                return null;
            }
            Personality person = CouncilBank.ById(personalityId);
            if (person == null)
            {
                problem = "candidato não existe mais no banco";
                return null;
            }
            // The bank is small: the same person may be a candidate in two nations. Whoever already serves another leaves the list.
            if (world.Nations.Any(n => n != nation && n.Council.Any(m => m.PersonalityId == personalityId)))
            {
                candidates.Remove(personalityId);
                problem = "esse candidato já serve a outra nação";
                return null;
            }
            Minister minister = Make(world, nation, capture, person, portfolio, turn);
            minister.PortraitVariant = PortraitVariant(candidates, personalityId);
            nation.Council.Add(minister);
            nation.CouncilCandidates.Remove(portfolio);
            nation.Council.Sort((a, b) => Array.IndexOf(CouncilBank.Portfolios, a.Portfolio).CompareTo(Array.IndexOf(CouncilBank.Portfolios, b.Portfolio)));
            return minister;
        }

        /// <summary>A candidate's portrait: their position among the seat's candidates of the same gender (kept after appointment).</summary>
        internal static int PortraitVariant(List<string> candidates, string personalityId)
        {
            string gender = CouncilBank.ById(personalityId)?.Gender;
            return Math.Max(0, candidates.Where(id => CouncilBank.ById(id)?.Gender == gender).ToList().IndexOf(personalityId));
        }

        /// <summary>
        /// The minister this candidate would become if appointed now (numbers and sympathies are fixed per person and
        /// nation, so the preview matches the appointment).
        /// </summary>
        internal static Minister Preview(AiWorld world, AiNation nation, WorldCapture capture, Personality person, string portfolio, int turn)
        {
            return Make(world, nation, capture, person, portfolio, turn);
        }

        /// <summary>How the traits change the advice and the loyalty, in the UI language (the rules in Opine* and Follow).</summary>
        internal static List<string> TendenciesUi(Minister m)
        {
            var lines = new List<string>();
            if (m.Has("belicista") || m.Has("ousado") || m.Has("rancoroso"))
                lines.Add(L.T("Na guerra, empurra para o ataque e para reforçar o exército."));
            else if (m.Has("cauteloso") || m.Has("prudente") || m.Has("frio") || m.Has("pragmático"))
                lines.Add(L.T("Na guerra, prefere segurar posição e negociar."));
            if (m.Has("orgulhoso") || m.Has("belicista") || m.Has("rancoroso") || m.Has("desconfiado"))
                lines.Add(L.T("Diante de exigências estrangeiras, tende a recusar."));
            else if (m.Has("cauteloso") || m.Has("prudente") || m.Has("idealista") || m.Has("pragmático") || m.Has("generoso"))
                lines.Add(L.T("Diante de exigências estrangeiras, tende a ceder para evitar conflito."));
            if (m.Has("ganancioso") || m.Has("astuto"))
                lines.Add(L.T("Com o caixa no vermelho, manda cobrar dívidas e pedágios em vez de cortar gastos."));
            if (m.Has("generoso") || m.Has("ousado") || m.Has("otimista"))
                lines.Add(L.T("Com sobra de dinheiro, sugere presentes e patrocínio de povos independentes."));
            if (m.Has("devoto"))
                lines.Add(L.T("Puxa o reino para a fé."));
            if (m.Has("ambicioso") || m.Has("vaidoso"))
                lines.Add(L.T("Gosta de maravilhas e de cultura que levem o nome do reino."));
            if (m.Has("vaidoso"))
                lines.Add(L.T("Gosta de lembrar que já tinha previsto."));
            if (m.Has("orgulhoso") || m.Has("rancoroso"))
                lines.Add(L.T("Perde lealdade quando você contraria os conselhos."));
            if (m.Has("leal"))
                lines.Add(L.T("Começa com lealdade mais alta."));
            if (m.Has("ambicioso"))
                lines.Add(L.T("Começa com ambição mais alta, que cresce quando um colega é demitido."));
            return lines;
        }

        /// <summary>Someone from the bank who serves no nation, was not fired by this one and is not in <paramref name="taken"/>.</summary>
        private static Personality Pick(AiWorld world, AiNation nation, string portfolio, int salt, int attempt, HashSet<string> taken)
        {
            var used = new HashSet<string>(world.Nations.SelectMany(n => n.Council).Select(m => m.PersonalityId));
            used.UnionWith(nation.FiredPersonalities);
            used.UnionWith(taken);
            List<Personality> free = CouncilBank.All().Where(p => !used.Contains(p.Id)).ToList();
            if (free.Count == 0)
            {
                free = CouncilBank.All().Where(p => !nation.Council.Any(m => m.PersonalityId == p.Id) && !taken.Contains(p.Id)).ToList();
            }
            if (free.Count == 0)
            {
                return null;
            }
            uint seed = Hash($"{world.GameGuid}|{nation.EmpireIndex}|{portfolio}|{salt}|{attempt}|{nation.FiredPersonalities.Count}");
            return free[(int)(seed % (uint)free.Count)];
        }

        private static Minister Make(AiWorld world, AiNation nation, WorldCapture capture, Personality person, string portfolio, int turn)
        {
            // No turn in the seed: the candidate card shows these numbers before the appointment (Preview).
            uint seed = Hash($"{world.GameGuid}|{nation.EmpireIndex}|{portfolio}|{person.Id}");
            var minister = new Minister
            {
                Portfolio = portfolio,
                PersonalityId = person.Id,
                Name = person.Name,
                Gender = person.Gender,
                Traits = new List<string>(person.Traits),
                Voice = person.Voice,
                Credibility = 45 + (int)(seed % 21),
                Loyalty = 50 + (int)((seed >> 5) % 31),
                Ambition = 30 + (int)((seed >> 10) % 41),
                Since = turn,
            };
            if (minister.Has("leal")) minister.Loyalty = Math.Min(100, minister.Loyalty + 15);
            if (minister.Has("ambicioso")) minister.Ambition = Math.Min(100, minister.Ambition + 25);
            // One or two known nations toward which they feel sympathy or antipathy.
            List<int> known = capture?.AliveEmpires().Where(e => e != nation.EmpireIndex && (capture.Empire(nation.EmpireIndex)?.RelationWith(e)?.Knows ?? false)).ToList() ?? new List<int>();
            for (int i = 0; i < Math.Min(2, known.Count); i++)
            {
                uint pick = Hash($"{seed}|{i}");
                int other = known[(int)(pick % (uint)known.Count)];
                int value = 15 + (int)((pick >> 4) % 26);
                minister.Sympathies[other] = (pick & 1) == 0 ? value : -value;
            }
            return minister;
        }

        /// <summary>
        /// Fires: the portfolio becomes vacant, with new candidates (the Hand does not leave: it is replaced right away by
        /// another person from the bank). Returns the description, or null if the portfolio has no minister.
        /// </summary>
        internal static string Fire(AiWorld world, AiNation nation, WorldCapture capture, string portfolio, int turn)
        {
            Minister old = nation.Council.FirstOrDefault(m => m.Portfolio == portfolio);
            if (old == null)
            {
                return null;
            }
            nation.Council.Remove(old);
            nation.FiredPersonalities.Add(old.PersonalityId);
            nation.CouncilCandidates?.Remove(portfolio);
            Ensure(world, nation, capture, turn);
            Minister newHand = portfolio == "mao" ? nation.Council.FirstOrDefault(m => m.Portfolio == "mao") : null;
            // The rest of the council feels the blow: the loyal ones fear, the ambitious ones see room.
            foreach (Minister other in nation.Council.Where(m => m != newHand))
            {
                if (other.Has("ambicioso")) other.Ambition = Math.Min(100, other.Ambition + 5);
                else other.Loyalty = Math.Max(0, other.Loyalty - 3);
            }
            int era = capture?.Empire(nation.EmpireIndex)?.EraIndex ?? 0;
            return newHand != null
                ? $"{Title(old, era)} {old.Name} demitido; no lugar entra {newHand.Name}"
                : $"{Title(old, era)} {old.Name} demitido; a pasta fica vaga até você nomear alguém";
        }

        // ---------------- Turn ----------------

        /// <summary>Once per turn: credibility from the portfolio's numbers and each one's advice.</summary>
        internal static void Update(AiWorld world, AiNation nation, WorldCapture capture, ValidationContext context, int turn)
        {
            CapturedEmpire me = capture?.Empire(nation.EmpireIndex);
            if (me == null || !me.Alive)
            {
                return;
            }
            Ensure(world, nation, capture, turn);
            bool newTurn = nation.CouncilTurn != turn;
            foreach (Minister minister in nation.Council)
            {
                if (minister.Portfolio == "mao")
                {
                    continue;
                }
                double metric = Metric(minister.Portfolio, world, capture, me, turn);
                if (newTurn)
                {
                    UpdateCredibility(minister, metric);
                }
                minister.LastMetric = metric;
                Opine(minister, world, capture, me, context, turn);
                minister.OpinionTurn = turn;
            }
            Minister hand = nation.Council.FirstOrDefault(m => m.Portfolio == "mao");
            if (hand != null)
            {
                List<Minister> others = nation.Council.Where(m => m != hand).ToList();
                if (newTurn && others.Count > 0)
                {
                    // The Hand is worth what the council it coordinates is worth.
                    int average = (int)Math.Round(others.Average(m => m.Credibility));
                    hand.Credibility = Clamp(hand.Credibility + Math.Sign(average - hand.Credibility), 5, 95);
                }
                OpineHand(hand, world, capture, me, others, turn);
                hand.OpinionTurn = turn;
            }
            nation.CouncilTurn = turn;
        }

        private static void UpdateCredibility(Minister minister, double metric)
        {
            int delta = 0;
            double before = minister.LastMetric;
            switch (minister.Portfolio)
            {
                case "financas":
                case "agricultura":
                    // Level: positive goes up, negative goes down.
                    delta = metric > 0 ? 1 : metric < 0 ? -2 : 0;
                    break;
                case "interior":
                    delta = metric >= 60 ? 1 : metric < 40 ? -2 : 0;
                    break;
                case "cultura":
                    // Position in the fame ranking: lower is better.
                    if (!double.IsNaN(before)) delta = metric < before ? 2 : metric > before ? -2 : 0;
                    break;
                default:
                    if (!double.IsNaN(before) && before != 0)
                    {
                        double change = (metric - before) / Math.Abs(before);
                        delta = change > 0.05 ? 1 : change < -0.1 ? -2 : 0;
                    }
                    else if (!double.IsNaN(before))
                    {
                        delta = metric > before ? 1 : metric < before ? -1 : 0;
                    }
                    break;
            }
            // With nothing new, credibility slowly drifts back to the middle.
            if (delta == 0 && Math.Abs(minister.Credibility - 50) > 20)
            {
                delta = minister.Credibility > 50 ? -1 : 1;
            }
            minister.Credibility = Clamp(minister.Credibility + delta, 5, 95);
        }

        /// <summary>Each portfolio's metric, using only what the nation knows about itself.</summary>
        private static double Metric(string portfolio, AiWorld world, WorldCapture capture, CapturedEmpire me, int turn)
        {
            switch (portfolio)
            {
                case "financas": return me.MoneyNet;
                case "guerra": return me.MilitaryStrength;
                case "chanceler":
                    return me.Relations.Where(r => r != null && r.Knows).Sum(r => (r.Alliance ? 3 : 0) + (r.Trade ? 1 : 0) + (r.OpenBorders ? 1 : 0)
                        + (r.NonAggression ? 1 : 0) - (r.AtWar ? 4 : 0) - r.TheirDemands.Count);
                case "agricultura": return me.Cities.Sum(c => c.Growth);
                case "obras": return me.Cities.Sum(c => c.Production);
                case "comercio": return capture.Trades.Count(t => t.Buyer == me.Index || t.Seller == me.Index) + capture.Tolls.Where(t => t.Owner == me.Index).Sum(t => t.Received) / 10.0;
                case "ciencia": return me.Technologies;
                case "interior": return me.Stability;
                case "cultura": return me.FameRank > 0 ? me.FameRank : 10;
                case "espionagem": return world.Letters.Count(l => l.InterceptedBy == me.Index && l.InterceptedTurn >= turn - 5) + capture.Spies.Count(s => s.Owner == me.Index && s.CanAct);
                default: return 0;
            }
        }

        // ---------------- Opinions ----------------

        private static void Opine(Minister m, AiWorld world, WorldCapture capture, CapturedEmpire me, ValidationContext context, int turn)
        {
            string money = me.CurrencyName ?? "de dinheiro";
            string moneyUi = me.CurrencyName ?? L.T("de dinheiro");
            m.Advice = null;
            // Opinion goes to the dossier (always Portuguese); OpinionUi is the same thing in the screen language.
            m.OpinionUi = null;
            switch (m.Portfolio)
            {
                case "financas":
                {
                    string facts = $"Tesouro em {N(me.Money)} {money}, {Signed(me.MoneyNet)} por turno.";
                    string factsUi = L.F("Tesouro em {0} {1}, {2} por turno.", NUi(me.Money), moneyUi, SignedUi(me.MoneyNet));
                    if (me.MoneyNet < 0)
                    {
                        m.Urgency = 0.8;
                        m.Advice = "economizar";
                        m.Opinion = facts + (m.Has("ganancioso") || m.Has("astuto")
                            ? " Gastamos mais do que entra: cobre de quem nos deve, por exigência ou pedágio, e nada de presentes agora."
                            : " Gastamos mais do que entra: corte despesas e não mande presentes enquanto isso.");
                        m.OpinionUi = factsUi + " " + (m.Has("ganancioso") || m.Has("astuto")
                            ? L.T("Gastamos mais do que entra: cobre de quem nos deve, por exigência ou pedágio, e nada de presentes agora.")
                            : L.T("Gastamos mais do que entra: corte despesas e não mande presentes enquanto isso."));
                    }
                    else if (me.MoneyNet > 0 && me.Money > 25 * me.MoneyNet)
                    {
                        m.Urgency = 0.3;
                        if (m.Has("generoso") || m.Has("ousado") || m.Has("otimista"))
                        {
                            m.Advice = "presentear";
                            m.Opinion = facts + " Sobra moeda: um presente bem dado ou o patrocínio de um povo independente compra amizade.";
                            m.OpinionUi = factsUi + " " + L.T("Sobra moeda: um presente bem dado ou o patrocínio de um povo independente compra amizade.");
                        }
                        else
                        {
                            m.Advice = "economizar";
                            m.Opinion = facts + " Cofre cheio é força na próxima crise: guarde.";
                            m.OpinionUi = factsUi + " " + L.T("Cofre cheio é força na próxima crise: guarde.");
                        }
                    }
                    else
                    {
                        m.Urgency = 0.2;
                        m.Opinion = facts + " Contas no azul; nada urgente.";
                        m.OpinionUi = factsUi + " " + L.T("Contas no azul; nada urgente.");
                    }
                    break;
                }
                case "guerra":
                    OpineWar(m, world, capture, me, context);
                    break;
                case "chanceler":
                    OpineDiplomacy(m, capture, me, context);
                    break;
                case "agricultura":
                {
                    double growth = me.Cities.Sum(c => c.Growth);
                    List<CapturedCity> starving = me.Cities.Where(c => c.IsCity && c.Growth < 0).ToList();
                    if (starving.Count > 0)
                    {
                        m.Urgency = 0.75;
                        m.Advice = "foco:economia";
                        string names = string.Join(", ", starving.Take(3).Select(c => c.Name)) + (starving.Count > 3 ? $" e mais {starving.Count - 3}" : string.Empty);
                        m.Opinion = (starving.Count == 1 ? $"{names} está perdendo população" : $"{names} estão perdendo população")
                            + " por falta de comida. Cuide dos campos antes de pensar em guerra.";
                        string namesUi = string.Join(", ", starving.Take(3).Select(c => c.Name));
                        m.OpinionUi = starving.Count == 1 ? L.F("{0} está perdendo população por falta de comida. Cuide dos campos antes de pensar em guerra.", namesUi)
                            : starving.Count > 3 ? L.F("{0} e mais {1} estão perdendo população por falta de comida. Cuide dos campos antes de pensar em guerra.", namesUi, starving.Count - 3)
                            : L.F("{0} estão perdendo população por falta de comida. Cuide dos campos antes de pensar em guerra.", namesUi);
                    }
                    else
                    {
                        m.Urgency = growth > 0 ? 0.15 : 0.45;
                        m.Advice = growth > 0 ? null : "foco:expansao";
                        m.Opinion = growth > 0 ? $"Os celeiros dão conta: crescimento de {N(growth)} por turno somando as cidades."
                            : "As cidades pararam de crescer: precisamos de mais terra boa (expansão).";
                        m.OpinionUi = growth > 0 ? L.F("Os celeiros dão conta: crescimento de {0} por turno somando as cidades.", NUi(growth))
                            : L.T("As cidades pararam de crescer: precisamos de mais terra boa (expansão).");
                    }
                    break;
                }
                case "obras":
                {
                    double production = me.Cities.Sum(c => c.Production);
                    m.Urgency = 0.2;
                    m.Opinion = $"As cidades produzem {N(production)} de indústria por turno.";
                    m.OpinionUi = L.F("As cidades produzem {0} de indústria por turno.", NUi(production));
                    if (m.Has("ambicioso") || m.Has("vaidoso"))
                    {
                        m.Advice = "foco:cultura";
                        m.Opinion += " Uma maravilha levaria o seu nome pelos séculos.";
                        m.OpinionUi += " " + L.T("Uma maravilha levaria o seu nome pelos séculos.");
                    }
                    else if (production < me.Cities.Count(c => c.IsCity) * 15)
                    {
                        m.Urgency = 0.4;
                        m.Advice = "foco:economia";
                        m.Opinion += " É pouco para o tamanho do reino.";
                        m.OpinionUi += " " + L.T("É pouco para o tamanho do reino.");
                    }
                    break;
                }
                case "comercio":
                {
                    int partners = capture.Trades.Where(t => t.Buyer == me.Index || t.Seller == me.Index).Select(t => t.Buyer == me.Index ? t.Seller : t.Buyer).Distinct().Count();
                    CapturedToll toll = capture.Tolls.Where(t => t.Payer == me.Index && t.Paid > 0).OrderByDescending(t => t.Paid).FirstOrDefault();
                    if (toll != null && context.KnownEmpires.ContainsValue(toll.Owner))
                    {
                        m.Urgency = 0.5;
                        m.Advice = m.Has("orgulhoso") || m.Has("rancoroso") ? $"recusar:E{toll.Owner}" : $"acordo:E{toll.Owner}";
                        m.Opinion = $"Pagamos {N(toll.Paid)} {money} de pedágio por turno a {Who(capture, toll.Owner)}. "
                            + (m.Advice.StartsWith("recusar") ? "Isso é tributo disfarçado: exija o fim ou feche nossas rotas." : "Negocie: um acordo vale mais que o pedágio.");
                        m.OpinionUi = L.F("Pagamos {0} {1} de pedágio por turno a {2}.", NUi(toll.Paid), moneyUi, WhoUi(capture, toll.Owner)) + " "
                            + (m.Advice.StartsWith("recusar") ? L.T("Isso é tributo disfarçado: exija o fim ou feche nossas rotas.") : L.T("Negocie: um acordo vale mais que o pedágio."));
                    }
                    else
                    {
                        m.Urgency = partners == 0 ? 0.4 : 0.15;
                        m.Opinion = partners == 0 ? "Não temos rotas de comércio com ninguém: um acordo econômico abriria mercados."
                            : $"Comerciamos com {partners} nação(ões); as rotas rendem.";
                        m.OpinionUi = partners == 0 ? L.T("Não temos rotas de comércio com ninguém: um acordo econômico abriria mercados.")
                            : L.P(partners, "Comerciamos com {0} nação; as rotas rendem.", "Comerciamos com {0} nações; as rotas rendem.");
                        if (partners == 0)
                        {
                            m.Advice = "acordo";
                        }
                    }
                    break;
                }
                case "ciencia":
                    m.Urgency = m.Has("idealista") || m.Has("prudente") ? 0.3 : 0.2;
                    m.Advice = m.Urgency >= 0.3 ? "foco:ciencia" : null;
                    m.Opinion = $"Dominamos {me.Technologies} tecnologias e produzimos {N(me.Science)} de ciência por turno."
                        + (m.Advice != null ? " Saber é poder que não se rouba: invista nos sábios." : string.Empty);
                    m.OpinionUi = L.F("Dominamos {0} tecnologias e produzimos {1} de ciência por turno.", me.Technologies, NUi(me.Science))
                        + (m.Advice != null ? " " + L.T("Saber é poder que não se rouba: invista nos sábios.") : string.Empty);
                    break;
                case "fe":
                    m.Urgency = m.Has("devoto") ? 0.35 : 0.1;
                    m.Advice = m.Has("devoto") ? "foco:fe" : null;
                    m.Opinion = m.Has("devoto") ? "Os templos estão vazios de oferendas; um reino sem fé perde o rumo." : "A fé do povo está tranquila.";
                    m.OpinionUi = m.Has("devoto") ? L.T("Os templos estão vazios de oferendas; um reino sem fé perde o rumo.") : L.T("A fé do povo está tranquila.");
                    break;
                case "interior":
                {
                    int besieged = me.Cities.Count(c => c.Besieged);
                    if (besieged > 0)
                    {
                        m.Urgency = 0.95;
                        m.Advice = "defender";
                        m.Opinion = $"{besieged} cidade(s) nossa(s) sob cerco: isso vem antes de qualquer outra coisa.";
                        m.OpinionUi = L.P(besieged, "{0} cidade nossa sob cerco: isso vem antes de qualquer outra coisa.", "{0} cidades nossas sob cerco: isso vem antes de qualquer outra coisa.");
                    }
                    else if (me.Stability < 40)
                    {
                        m.Urgency = 0.8;
                        m.Advice = "foco:economia";
                        m.Opinion = $"Estabilidade em {N(me.Stability)}%: o povo está inquieto. Evite guerras novas e cuide das cidades.";
                        m.OpinionUi = L.F("Estabilidade em {0}%: o povo está inquieto. Evite guerras novas e cuide das cidades.", NUi(me.Stability));
                    }
                    else
                    {
                        m.Urgency = 0.15;
                        m.Opinion = $"Estabilidade em {N(me.Stability)}%: as cidades estão em ordem.";
                        m.OpinionUi = L.F("Estabilidade em {0}%: as cidades estão em ordem.", NUi(me.Stability));
                    }
                    break;
                }
                case "cultura":
                    m.Urgency = me.InfluenceNet < 0 ? 0.5 : 0.2;
                    m.Advice = me.InfluenceNet < 0 ? "economizar" : m.Has("vaidoso") || m.Has("orgulhoso") ? "foco:cultura" : null;
                    m.Opinion = (me.FameRank > 0 ? $"Somos o {me.FameRank}º reino em fama. " : string.Empty)
                        + $"Influência {N(me.Influence)} ({Signed(me.InfluenceNet)} por turno)."
                        + (me.InfluenceNet < 0 ? " Estamos gastando a influência mais rápido do que ela entra." : m.Advice != null ? " A glória pede obras e festas." : string.Empty);
                    m.OpinionUi = (me.FameRank > 0 ? L.F("Somos o {0}º reino em fama.", me.FameRank) + " " : string.Empty)
                        + L.F("Influência {0} ({1} por turno).", NUi(me.Influence), SignedUi(me.InfluenceNet))
                        + (me.InfluenceNet < 0 ? " " + L.T("Estamos gastando a influência mais rápido do que ela entra.") : m.Advice != null ? " " + L.T("A glória pede obras e festas.") : string.Empty);
                    break;
                case "espionagem":
                {
                    List<int> watched = capture.StealthActivity.Where(t => capture.OwnerOf(t) == me.Index).ToList();
                    int read = world.Letters.Count(l => l.InterceptedBy == me.Index && l.InterceptedTurn >= turn - 5);
                    int abroad = capture.Spies.Count(s => s.Owner == me.Index && s.TerritoryOwner >= 0 && s.TerritoryOwner != me.Index);
                    if (watched.Count > 0)
                    {
                        m.Urgency = 0.6;
                        m.Advice = "cautela";
                        m.Opinion = $"Há espiões estrangeiros em {string.Join(", ", watched.Take(3).Select(t => capture.TerritoryName(t)))}: o que se escreve em carta privada pode estar sendo lido.";
                        m.OpinionUi = L.F("Há espiões estrangeiros em {0}: o que se escreve em carta privada pode estar sendo lido.", string.Join(", ", watched.Take(3).Select(t => capture.TerritoryName(t))));
                    }
                    else if (read > 0)
                    {
                        m.Urgency = 0.45;
                        m.Opinion = $"Nossos espiões leram {read} carta(s) alheia(s) nos últimos turnos. Use o que sabemos, mas sem revelar como sabemos.";
                        m.OpinionUi = L.P(read, "Nossos espiões leram {0} carta alheia nos últimos turnos. Use o que sabemos, mas sem revelar como sabemos.", "Nossos espiões leram {0} cartas alheias nos últimos turnos. Use o que sabemos, mas sem revelar como sabemos.");
                    }
                    else
                    {
                        m.Urgency = abroad == 0 ? 0.25 : 0.1;
                        m.Opinion = abroad == 0 ? "Não temos olhos em terra estrangeira: um espião na capital de um rival leria parte das cartas deles."
                            : $"Temos {abroad} espião(ões) em terra estrangeira, quietos por enquanto.";
                        m.OpinionUi = abroad == 0 ? L.T("Não temos olhos em terra estrangeira: um espião na capital de um rival leria parte das cartas deles.")
                            : L.P(abroad, "Temos {0} espião em terra estrangeira, quieto por enquanto.", "Temos {0} espiões em terra estrangeira, quietos por enquanto.");
                    }
                    break;
                }
            }
            if (m.Has("vaidoso") && m.Opinion != null)
            {
                m.Opinion = "Como sempre previ: " + Lower(m.Opinion);
                if (m.OpinionUi != null)
                {
                    // In German nouns start with a capital letter: do not lowercase the first letter.
                    m.OpinionUi = L.F("Como sempre previ: {0}", L.Code == "de" ? m.OpinionUi : Lower(m.OpinionUi));
                }
            }
        }

        private static void OpineWar(Minister m, AiWorld world, WorldCapture capture, CapturedEmpire me, ValidationContext context)
        {
            bool bold = m.Has("belicista") || m.Has("ousado") || m.Has("rancoroso");
            bool careful = m.Has("cauteloso") || m.Has("prudente") || m.Has("frio") || m.Has("pragmático");
            foreach (int other in context.AtWar)
            {
                CapturedRelation relation = me.RelationWith(other);
                if (relation == null)
                {
                    continue;
                }
                m.Urgency = 0.9;
                bool losing = relation.MyWarScore < relation.TheirWarScore;
                if (bold && !losing)
                {
                    m.Advice = $"atacar:E{other}";
                    m.Opinion = $"Guerra com {Who(capture, other)}: placar {N(relation.MyWarScore)} contra {N(relation.TheirWarScore)}. Pressione agora, antes que se reergam.";
                    m.OpinionUi = L.F("Guerra com {0}: placar {1} contra {2}. Pressione agora, antes que se reergam.", WhoUi(capture, other), NUi(relation.MyWarScore), NUi(relation.TheirWarScore));
                }
                else if (losing || careful)
                {
                    m.Advice = $"paz:E{other}";
                    m.Opinion = $"Guerra com {Who(capture, other)}: placar {N(relation.MyWarScore)} contra {N(relation.TheirWarScore)}, apoio à guerra {N(relation.MyMoral)} contra {N(relation.TheirMoral)}. "
                        + (losing ? "Estamos perdendo: proponha paz enquanto há o que salvar." : "Já mostramos força; uma paz agora guarda o que ganhamos.");
                    m.OpinionUi = L.F("Guerra com {0}: placar {1} contra {2}, apoio à guerra {3} contra {4}.", WhoUi(capture, other), NUi(relation.MyWarScore), NUi(relation.TheirWarScore), NUi(relation.MyMoral), NUi(relation.TheirMoral)) + " "
                        + (losing ? L.T("Estamos perdendo: proponha paz enquanto há o que salvar.") : L.T("Já mostramos força; uma paz agora guarda o que ganhamos."));
                }
                else
                {
                    m.Advice = "defender";
                    m.Opinion = $"Guerra com {Who(capture, other)}: placar {N(relation.MyWarScore)} contra {N(relation.TheirWarScore)}. Segure as fronteiras e espere o erro deles.";
                    m.OpinionUi = L.F("Guerra com {0}: placar {1} contra {2}. Segure as fronteiras e espere o erro deles.", WhoUi(capture, other), NUi(relation.MyWarScore), NUi(relation.TheirWarScore));
                }
                return;
            }
            SeenArmy intruder = me.SeenArmies.Where(a => a.Territory >= 0 && capture.OwnerOf(a.Territory) == me.Index).OrderByDescending(a => a.Strength).FirstOrDefault();
            if (intruder != null && context.KnownEmpires.ContainsValue(intruder.Owner))
            {
                m.Urgency = 0.7;
                m.Advice = bold ? $"guerra:E{intruder.Owner}" : "defender";
                m.Opinion = $"Tropas de {Who(capture, intruder.Owner)} dentro do nosso território ({intruder.Units} unidade(s) em {capture.TerritoryName(intruder.Territory)}). "
                    + (bold ? "Isso é provocação: temos pretexto." : "Reforce as guarnições e exija explicação por carta.");
                int intruderUnits = intruder.Units;
                m.OpinionUi = L.P(intruderUnits, "Tropas de {1} dentro do nosso território ({0} unidade em {2}).", "Tropas de {1} dentro do nosso território ({0} unidades em {2}).", WhoUi(capture, intruder.Owner), capture.TerritoryName(intruder.Territory)) + " "
                    + (bold ? L.T("Isso é provocação: temos pretexto.") : L.T("Reforce as guarnições e exija explicação por carta."));
                return;
            }
            // Sworn enemy from the minister's own antipathy, or the leader's posture.
            KeyValuePair<int, int> hated = m.Sympathies.Where(p => p.Value <= -20 && context.KnownEmpires.ContainsValue(p.Key)).OrderBy(p => p.Value).FirstOrDefault();
            if (bold && hated.Value < 0)
            {
                m.Urgency = 0.4;
                m.Advice = $"guerra:E{hated.Key}";
                m.Opinion = $"Nossa força total é {N(me.MilitaryStrength)}. {Who(capture, hated.Key)} não merece confiança: prepare as lanças contra eles.";
                m.OpinionUi = L.F("Nossa força total é {0}. {1} não merece confiança: prepare as lanças contra eles.", NUi(me.MilitaryStrength), WhoUi(capture, hated.Key));
                return;
            }
            m.Urgency = 0.2;
            m.Advice = bold ? "foco:militar" : null;
            m.Opinion = $"Fronteiras calmas. Força total {N(me.MilitaryStrength)} em {me.UnitCount} unidade(s)."
                + (bold ? " Exército parado enferruja: reforce-o." : string.Empty);
            int unitCount = me.UnitCount;
            m.OpinionUi = L.P(unitCount, "Fronteiras calmas. Força total {1} em {0} unidade.", "Fronteiras calmas. Força total {1} em {0} unidades.", NUi(me.MilitaryStrength))
                + (bold ? " " + L.T("Exército parado enferruja: reforce-o.") : string.Empty);
        }

        private static void OpineDiplomacy(Minister m, WorldCapture capture, CapturedEmpire me, ValidationContext context)
        {
            bool soft = m.Has("cauteloso") || m.Has("prudente") || m.Has("idealista") || m.Has("pragmático") || m.Has("generoso");
            bool hard = m.Has("orgulhoso") || m.Has("belicista") || m.Has("rancoroso") || m.Has("desconfiado");
            foreach (int other in context.DemandsToAnswer)
            {
                int sympathy = m.Sympathies.TryGetValue(other, out int s) ? s : 0;
                m.Urgency = 0.85;
                bool cede = (soft && sympathy > -20) || (!hard && sympathy >= 20);
                m.Advice = cede ? $"ceder:E{other}" : $"resistir:E{other}";
                m.Opinion = $"{Who(capture, other)} tem exigências contra nós esperando resposta. "
                    + (cede ? "Ceder custa menos que uma guerra; aceite e cobre gratidão depois." : "Ceder agora convida mais exigências: recuse ou enrole.");
                m.OpinionUi = L.F("{0} tem exigências contra nós esperando resposta.", WhoUi(capture, other)) + " "
                    + (cede ? L.T("Ceder custa menos que uma guerra; aceite e cobre gratidão depois.") : L.T("Ceder agora convida mais exigências: recuse ou enrole."));
                return;
            }
            int proposer = context.TreatyToAnswer.Concat(context.AgreementToAnswer).Select(e => (int?)e).FirstOrDefault() ?? -1;
            if (proposer >= 0)
            {
                int sympathy = m.Sympathies.TryGetValue(proposer, out int s) ? s : 0;
                m.Urgency = 0.75;
                bool accept = sympathy >= 0 && !(hard && sympathy < 10);
                m.Advice = accept ? $"aceitar:E{proposer}" : $"recusar:E{proposer}";
                m.Opinion = $"Há proposta formal de {Who(capture, proposer)} esperando você. " + (accept ? "Aceite: vale mais um amigo na fronteira." : "Desconfio da pressa deles: deixe esperar.");
                m.OpinionUi = L.F("Há proposta formal de {0} esperando você.", WhoUi(capture, proposer)) + " " + (accept ? L.T("Aceite: vale mais um amigo na fronteira.") : L.T("Desconfio da pressa deles: deixe esperar."));
                return;
            }
            // Independent peoples weigh almost like nations, but below them: a vassal or annexation within reach beats a
            // friendly overture; anything else about them only comes up when no nation needs attention.
            MinorTopic minorTopic = MinorTopicFor(m, capture, me, hard);
            if (minorTopic != null && minorTopic.Urgency >= 0.4)
            {
                ApplyMinorTopic(m, minorTopic);
                return;
            }
            // No urgency: lean toward whoever the minister likes, if there is no agreement yet.
            KeyValuePair<int, int> friend = m.Sympathies.Where(p => p.Value >= 20 && context.KnownEmpires.ContainsValue(p.Key) && !context.AtWar.Contains(p.Key)).OrderByDescending(p => p.Value).FirstOrDefault();
            if (friend.Value > 0)
            {
                CapturedRelation relation = me.RelationWith(friend.Key);
                if (relation != null && !relation.Alliance)
                {
                    m.Urgency = 0.35;
                    m.Advice = $"acordo:E{friend.Key}";
                    m.Opinion = $"{Who(capture, friend.Key)} é gente de palavra. " + (relation.Trade && relation.OpenBorders ? "Já comerciamos: hora de pensar em aliança." : "Um acordo com eles abriria caminho.");
                    m.OpinionUi = L.F("{0} é gente de palavra.", WhoUi(capture, friend.Key)) + " " + (relation.Trade && relation.OpenBorders ? L.T("Já comerciamos: hora de pensar em aliança.") : L.T("Um acordo com eles abriria caminho."));
                    return;
                }
            }
            if (minorTopic != null)
            {
                ApplyMinorTopic(m, minorTopic);
                return;
            }
            m.Urgency = 0.2;
            m.Opinion = $"Conhecemos {context.KnownEmpires.Count} nação(ões); nenhuma pendência diplomática urgente.";
            m.OpinionUi = L.P(context.KnownEmpires.Count, "Conhecemos {0} nação; nenhuma pendência diplomática urgente.", "Conhecemos {0} nações; nenhuma pendência diplomática urgente.");
        }

        private sealed class MinorTopic
        {
            public double Urgency;
            public string Advice;
            public string Opinion;
            public string OpinionUi;
        }

        private static void ApplyMinorTopic(Minister m, MinorTopic topic)
        {
            m.Urgency = topic.Urgency;
            m.Advice = topic.Advice;
            m.Opinion = topic.Opinion;
            m.OpinionUi = topic.OpinionUi;
        }

        /// <summary>
        /// The Foreign Affairs topic about independent peoples (Discord feedback: the minister only looked at the major
        /// nations): a vassal or annexation treaty within reach (0.45), a rival taking over a people we have courted or
        /// that is up for grabs (0.3), another treaty we can sign (0.28), or keeping our hold on a people (0.22). Always
        /// below a nation's demand or proposal, and below a friendly nation unless it is a vassal or annexation.
        /// </summary>
        private static MinorTopic MinorTopicFor(Minister m, WorldCapture capture, CapturedEmpire me, bool hard)
        {
            List<CapturedMinor> known = capture.Minors
                .Where(x => x.Index >= 0 && me.Index < x.Relations.Length && x.Relations[me.Index] != null && x.Status != "Dying")
                .ToList();
            if (known.Count == 0)
            {
                return null;
            }
            string Label(CapturedMinor x) => $"{x.Name ?? "povo independente"} (M{x.Index}, cidade {x.CityName ?? "?"})";
            string LabelUi(CapturedMinor x) => x.CityName != null ? L.F("{0} ({1})", x.Name ?? L.T("povo independente"), x.CityName) : x.Name ?? L.T("povo independente");

            // 1. Vassal or annexation available now.
            foreach (string id in new[] { "anexar", "vassalo" })
            {
                int treaty = Array.IndexOf(DecisionParser.MinorTreatyIds, id);
                CapturedMinor target = known
                    .Where(x => x.Relations[me.Index].Treaties.Any(t => t.Treaty == treaty && t.Available))
                    .OrderByDescending(x => x.Relations[me.Index].Share).FirstOrDefault();
                if (target == null)
                {
                    continue;
                }
                double cost = target.Relations[me.Index].Treaties.First(t => t.Treaty == treaty).Cost;
                bool annex = id == "anexar";
                return new MinorTopic
                {
                    Urgency = 0.45,
                    Advice = $"tratado_povo:M{target.Index}",
                    Opinion = (annex ? $"{Label(target)} já aceita ser anexado: a cidade deles vira nossa por {N(cost)} de influência."
                            : $"{Label(target)} já aceita ser nosso vassalo, por {N(cost)} de influência.")
                        + (hard ? " Povo pequeno se toma antes que outro tome." : " Gente pequena, mas é terra e gente a mais sem guerra."),
                    OpinionUi = (annex ? L.F("{0} já aceita ser anexado: a cidade deles vira nossa por {1} de influência.", LabelUi(target), NUi(cost))
                            : L.F("{0} já aceita ser nosso vassalo, por {1} de influência.", LabelUi(target), NUi(cost)))
                        + " " + (hard ? L.T("Povo pequeno se toma antes que outro tome.") : L.T("Gente pequena, mas é terra e gente a mais sem guerra.")),
                };
            }

            // 2. A rival is taking a people over (most patronage, half of the total) while ours is small.
            CapturedMinor contested = known
                .Where(x => x.TopPatron >= 0 && x.TopPatron != me.Index && x.TopPatron < x.Relations.Length
                    && x.Relations[x.TopPatron] != null && x.Relations[x.TopPatron].Share >= 0.5
                    && x.Relations[me.Index].Share < 0.25 && x.Relations[me.Index].PatronizeBlocked == null)
                .OrderByDescending(x => x.Relations[x.TopPatron].Share).FirstOrDefault();
            if (contested != null)
            {
                int rival = contested.TopPatron;
                double share = contested.Relations[rival].Share * 100;
                return new MinorTopic
                {
                    Urgency = 0.3,
                    Advice = $"patrocinar:M{contested.Index}",
                    Opinion = $"{Who(capture, rival)} já tem {N(share)}% do patrocínio de {Label(contested)}; daqui a pouco é vassalo deles. Um patrocínio modesto nosso segura o povo, ou ao menos encarece para eles.",
                    OpinionUi = L.F("{0} já tem {1}% do patrocínio de {2}; daqui a pouco é vassalo deles. Um patrocínio modesto nosso segura o povo, ou ao menos encarece para eles.", WhoUi(capture, rival), NUi(share), LabelUi(contested)),
                };
            }

            // 3. Another treaty we can sign now (trade, science, mercenaries...).
            CapturedMinor signable = known
                .Where(x => x.Relations[me.Index].Treaties.Any(t => t.Available))
                .OrderByDescending(x => x.Relations[me.Index].Share).FirstOrDefault();
            if (signable != null)
            {
                CapturedMinorTreaty treaty = signable.Relations[me.Index].Treaties.Where(t => t.Available).OrderBy(t => t.Cost).First();
                string name = DossierBuilder.MinorTreatyName(treaty);
                return new MinorTopic
                {
                    Urgency = 0.28,
                    Advice = $"tratado_povo:M{signable.Index}",
                    Opinion = $"{Label(signable)} assinaria «{name}» por {N(treaty.Cost)} de influência. Coisa menor, mas barata.",
                    OpinionUi = L.F("{0} assinaria «{1}» por {2} de influência. Coisa menor, mas barata.", LabelUi(signable), name, NUi(treaty.Cost)),
                };
            }

            // 4. Our own clients: keep them, or start courting the one nobody holds.
            CapturedMinor ours = known.Where(x => x.TopPatron == me.Index).OrderByDescending(x => x.Relations[me.Index].Share).FirstOrDefault();
            if (ours != null)
            {
                return new MinorTopic
                {
                    Urgency = 0.22,
                    Opinion = $"Somos o maior patrono de {Label(ours)} ({N(ours.Relations[me.Index].Share * 100)}% do patrocínio). Pouco peso no mundo, mas é um amigo que não nos custa guerra; mantenha o investimento.",
                    OpinionUi = L.F("Somos o maior patrono de {0} ({1}% do patrocínio). Pouco peso no mundo, mas é um amigo que não nos custa guerra; mantenha o investimento.", LabelUi(ours), NUi(ours.Relations[me.Index].Share * 100)),
                };
            }
            CapturedMinor free = known.Where(x => x.TopPatron < 0 && x.Peaceful && x.Relations[me.Index].PatronizeBlocked == null).FirstOrDefault();
            if (free != null && (m.Has("generoso") || m.Has("ambicioso") || m.Has("astuto") || m.Has("pragmático")))
            {
                return new MinorTopic
                {
                    Urgency = 0.21,
                    Advice = $"patrocinar:M{free.Index}",
                    Opinion = $"Ninguém patrocina {Label(free)} ainda. É um povo pequeno, mas quem chega primeiro leva os tratados.",
                    OpinionUi = L.F("Ninguém patrocina {0} ainda. É um povo pequeno, mas quem chega primeiro leva os tratados.", LabelUi(free)),
                };
            }
            return null;
        }

        private static void OpineHand(Minister hand, AiWorld world, WorldCapture capture, CapturedEmpire me, List<Minister> others, int turn)
        {
            List<Minister> agenda = others.Where(o => o.Opinion != null).OrderByDescending(o => o.Urgency).Take(3).ToList();
            var parts = new List<string>();
            var partsUi = new List<string>();
            for (int i = 0; i < agenda.Count; i++)
            {
                parts.Add($"{i + 1}) {CouncilBank.PortfolioNames[agenda[i].Portfolio]}: {Shorten(agenda[i].Opinion, 18)}");
                // Portfolio names marked with L.N in CouncilBank.
                partsUi.Add($"{i + 1}) {L.T(CouncilBank.PortfolioNames[agenda[i].Portfolio])}: {Shorten(agenda[i].OpinionUi ?? LetterNames.NamesOnly(agenda[i].Opinion), 18)}");
            }
            // Ultimatum deadlines that expire now.
            Letter due = world.Letters.Where(l => l.Type == "ultimato" && (l.From == me.Index || (l.To == me.Index && l.Arrived(turn)))
                && l.SentTurn + l.DeadlineTurns <= turn && l.SentTurn + l.DeadlineTurns >= turn - 1).OrderByDescending(l => l.Id).FirstOrDefault();
            string deadline = due == null ? string.Empty
                : due.From == me.Index ? $" Seu ultimato a {Who(capture, due.To)} venceu: cumpra ou recue." : $" O ultimato de {Who(capture, due.From)} contra nós venceu.";
            string deadlineUi = due == null ? string.Empty
                : due.From == me.Index ? " " + L.F("Seu ultimato a {0} venceu: cumpra ou recue.", WhoUi(capture, due.To)) : " " + L.F("O ultimato de {0} contra nós venceu.", WhoUi(capture, due.From));
            hand.Urgency = 1;
            hand.Opinion = (parts.Count > 0 ? "Pauta de hoje: " + string.Join(" ", parts) : "Nada urgente na pauta.") + deadline;
            hand.OpinionUi = (partsUi.Count > 0 ? L.F("Pauta de hoje: {0}", string.Join(" ", partsUi)) : L.T("Nada urgente na pauta.")) + deadlineUi;
        }

        // ---------------- Reaction to the leader's decisions ----------------

        /// <summary>
        /// After the turn's decision: whoever saw their advice followed gains the leader's affection; whoever saw the
        /// opposite loses it.
        /// </summary>
        internal static void React(AiNation nation, IEnumerable<ActionDraft> actions, int turn)
        {
            var done = new List<string>();
            foreach (ActionDraft action in actions)
            {
                JObject item;
                try
                {
                    item = JObject.Parse(action.Json ?? "{}");
                }
                catch (Exception)
                {
                    continue;
                }
                string target = ((string)item["nacao"])?.Trim().ToUpperInvariant();
                switch (action.Name)
                {
                    case "propor_paz": done.Add("paz:" + target); break;
                    case "declarar_guerra": done.Add("guerra:" + target); done.Add("atacar:" + target); break;
                    case "propor_acordo": done.Add("acordo:" + target); done.Add("acordo"); break;
                    case "responder_tratado":
                    case "responder_acordo": done.Add(((string)item["resposta"])?.Trim().ToLowerInvariant() == "aceitar" ? "aceitar:" + target : "recusar:" + target); break;
                    case "responder_exigencias":
                        string answer = (string)item["resposta"];
                        done.Add(answer == "aceitar" ? "ceder:" + target : "resistir:" + target);
                        break;
                    case "definir_foco": done.Add("foco:" + ((string)item["foco"])?.Trim().ToLowerInvariant()); break;
                    case "presentear": done.Add("presentear"); break;
                    case "patrocinar": done.Add("patrocinar"); done.Add("presentear"); done.Add("patrocinar:" + ((string)item["povo"])?.Trim().ToUpperInvariant()); break;
                    case "tratado_povo": done.Add("tratado_povo:" + ((string)item["povo"])?.Trim().ToUpperInvariant()); break;
                    case "ordem_exercito": done.Add("defender"); break;
                }
            }
            ApplyReaction(nation, done, turn);
        }

        /// <summary>
        /// Affection and loyalty after what the leader did (tags like "paz:E3", "acordo", "foco:ciencia"), against the
        /// advice given on turn <paramref name="turn"/>. In the player's council, affection is the minister's regard for
        /// the leader; the effect is the same.
        /// </summary>
        internal static void ApplyReaction(AiNation nation, List<string> done, int turn)
        {
            foreach (Minister minister in nation.Council)
            {
                if (minister.Advice == null || minister.OpinionTurn != turn)
                {
                    continue;
                }
                if (done.Contains(minister.Advice))
                {
                    minister.Affection = Clamp(minister.Affection + 4, -100, 100);
                    minister.Loyalty = Clamp(minister.Loyalty + 1, 0, 100);
                }
                else if (Contradicts(minister.Advice, done))
                {
                    minister.Affection = Clamp(minister.Affection - 3, -100, 100);
                    if (minister.Has("orgulhoso") || minister.Has("rancoroso"))
                    {
                        minister.Loyalty = Clamp(minister.Loyalty - 2, 0, 100);
                    }
                }
            }
        }

        private static bool Contradicts(string advice, List<string> done)
        {
            string[] pair = advice.Split(':');
            string target = pair.Length > 1 ? pair[1] : null;
            switch (pair[0])
            {
                case "paz": return done.Contains("guerra:" + target);
                case "guerra":
                case "atacar": return done.Contains("paz:" + target);
                case "aceitar": return done.Contains("recusar:" + target);
                case "recusar": return done.Contains("aceitar:" + target);
                case "ceder": return done.Contains("resistir:" + target);
                case "resistir": return done.Contains("ceder:" + target);
                case "foco": return done.Any(d => d.StartsWith("foco:") && d != advice);
                case "economizar": return done.Contains("presentear");
                default: return false;
            }
        }

        // ---------------- Dossier ----------------

        /// <summary>Opinion weight: credibility × leader's affection × urgency.</summary>
        internal static double Weight(Minister m) => m.Credibility * (1 + m.Affection / 200.0) * (0.3 + m.Urgency);

        internal static string Title(Minister m, int era) => CouncilBank.Title(m.Portfolio, era, m.Gender);

        /// <summary>The dossier's "SEU CONSELHO" section: the Hand and the weightiest opinions.</summary>
        internal static string DossierText(AiNation nation, CapturedEmpire me)
        {
            if (nation.Council.Count == 0)
            {
                return null;
            }
            int era = me?.EraIndex ?? 0;
            var text = new StringBuilder();
            text.AppendLine("== SEU CONSELHO (cada ministro opina pela própria pasta; quem decide é você) ==");
            Minister hand = nation.Council.FirstOrDefault(m => m.Portfolio == "mao");
            if (hand != null && hand.Opinion != null)
            {
                text.AppendLine($"{Title(hand, era)} {hand.Name} ({string.Join(", ", hand.Traits)} · credibilidade {hand.Credibility}): {hand.Opinion}");
            }
            foreach (Minister m in nation.Council.Where(x => x.Portfolio != "mao" && x.Opinion != null).OrderByDescending(Weight).Take(6))
            {
                string affection = m.Affection != 0 ? $" · sua afeição {(m.Affection > 0 ? "+" : string.Empty)}{m.Affection}" : string.Empty;
                text.AppendLine($"- {Title(m, era)} {m.Name} ({string.Join(", ", m.Traits)} · credibilidade {m.Credibility}{affection}): {m.Opinion}");
            }
            text.AppendLine("Ministros com credibilidade baixa e sem a sua afeição pesam menos. Seguir um conselho aumenta a sua afeição por quem o deu. Demitir (demitir_ministro com a pasta) deixa a pasta vaga e zera o bônus de veterano dela.");
            // Veterans: time in office pays off in the portfolio's area (up to +10%).
            List<string> veterans = nation.Council.Where(m => VeteranBonus(m, nation.CouncilTurn) > 0)
                .Select(m => $"{Title(m, era)} {m.Name} +{VeteranBonus(m, nation.CouncilTurn)}%").ToList();
            if (veterans.Count > 0)
            {
                text.AppendLine("Bônus de veterano (rende na área da pasta enquanto ele fica no cargo, +1% a cada 5 turnos até +10%): " + string.Join(", ", veterans) + ".");
            }
            // Vacant portfolios and the candidates: nomear_ministro with the portfolio and the candidate code (costs influence).
            if (nation.CouncilCandidates != null && nation.CouncilCandidates.Count > 0)
            {
                text.AppendLine($"PASTAS VAGAS (nomear_ministro custa {AppointCost(era)} de influência; vaga não opina nem rende bônus):");
                foreach (KeyValuePair<string, List<string>> seat in nation.CouncilCandidates.OrderBy(s => Array.IndexOf(CouncilBank.Portfolios, s.Key)))
                {
                    var people = seat.Value.Select(CouncilBank.ById).Where(p => p != null)
                        .Select(p => $"{p.Id} {p.Name} ({string.Join(", ", p.Traits)})").ToList();
                    if (people.Count > 0)
                    {
                        text.AppendLine($"- {seat.Key}: " + string.Join(" · ", people));
                    }
                }
            }
            return text.ToString();
        }

        // ---------------- Utilities ----------------

        private static string Who(WorldCapture capture, int empire)
        {
            CapturedEmpire them = capture.Empire(empire);
            return them?.Leader != null ? $"{them.Leader} ({them.Culture}, E{empire})" : $"{DossierBuilder.Name(capture, empire)} (E{empire})";
        }

        /// <summary>Who without the dossier code, for OpinionUi (the player never sees "E7").</summary>
        private static string WhoUi(WorldCapture capture, int empire)
        {
            CapturedEmpire them = capture.Empire(empire);
            return them?.Leader != null ? $"{them.Leader} ({them.Culture})" : DossierBuilder.Name(capture, empire);
        }

        private static string N(double value) => Math.Round(value).ToString("#,0", PtBr);

        private static string Signed(double value) => (value >= 0 ? "+" : string.Empty) + N(value);

        /// <summary>N and Signed in the UI culture (OpinionUi only).</summary>
        private static string NUi(double value) => Math.Round(value).ToString("#,0", L.Culture);

        private static string SignedUi(double value) => (value >= 0 ? "+" : string.Empty) + NUi(value);

        private static string Lower(string text) => string.IsNullOrEmpty(text) ? text : char.ToLowerInvariant(text[0]) + text.Substring(1);

        private static string Shorten(string text, int words)
        {
            string[] parts = (text ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= words ? text : string.Join(" ", parts.Take(words)) + "…";
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static uint Hash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }
}
