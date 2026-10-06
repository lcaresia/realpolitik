using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using CurrencyMod.Diplomacia.Capture;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Congresso mundial pelas ordens do jogo (research\congress.md §7.3). As ações rodam na thread do sandbox, só no
    /// turno principal (as ordens internacionais são recusadas no fim do turno):
    /// - propor_votacao: OrderStartCivicVote (só o presidente da sessão);
    /// - votar_congresso: VoteForCivicChoiceIndex (lei) ou SideWithCrisisDeclarator/Targeted (crise); abster = não votar;
    /// - subornar: BribeFor*Vote, com o número ajustado ao que resta e ao que a alavancagem paga (a validação do jogo
    ///   estoura exceção com alvo fora da faixa, então tudo é conferido antes);
    /// - responder_congresso: AcceptDemands (cumprir o veredito) ou DeclareSurpriseWar;
    /// - contribuir_consenso: ContributeToIdeologicalConsensus;
    /// - crise_internacional: DeclareInternationalCrisis com o motivo certo quando o Congresso recusa.
    /// TrackFallbacks (thread principal) devolve à IA nativa o veredito e a primeira sessão que a IA de linguagem deixou
    /// parados por mais de [IA] TurnosParaResponderPropostas, e publica a postura sobre a lei imposta (se_perder).
    /// </summary>
    internal static class CongressFlow
    {
        // ---------------- Propor lei ----------------

        internal static void Propose(ActionIntent intent, MajorEmpire empire)
        {
            InternationalAncillary congress = Open(intent);
            if (congress == null)
            {
                return;
            }
            if (!congress.IsCivicVoteUnlocked)
            {
                ActionExecutor.Report(intent, false, "o Congresso ainda não se formou");
                return;
            }
            var civic = new StaticString(intent.CivicName ?? string.Empty);
            InternationalFailureFlags flags = congress.GetStartCivicVoteFailureFlags(intent.Empire, civic);
            if (flags != InternationalFailureFlags.None)
            {
                string leader = string.Empty;
                bool[] working = null;
                if (congress.TryComputeNextCivicVoteSessionInfo(out int next, out int turn, out _, ref working) && next != intent.Empire)
                {
                    leader = $" (preside a próxima sessão: E{next}, a partir do turno {turn})";
                }
                ActionExecutor.Report(intent, false, "o Congresso não aceitou a proposta: " + Explain(flags) + leader);
                return;
            }
            string label = LawLabel(intent.CivicName);
            ActionExecutor.Post(new OrderStartCivicVote { CivicName = civic }, intent, null, next =>
            {
                if (congress.TryGetActiveCivicVote(ref congress.CivicVoteInfos, out InternationalCivicVoteInfo vote) && vote.CivicName == civic)
                {
                    ActionExecutor.Report(next, true, $"lei {label} em votação no Congresso até a virada para o turno {vote.TurnEnd} (último turno para votar: {vote.TurnEnd - 1})");
                }
                else
                {
                    ActionExecutor.Report(next, true, $"lei {label} proposta ao Congresso");
                }
            });
        }

        // ---------------- Votar ----------------

        internal static void Vote(ActionIntent intent, MajorEmpire empire)
        {
            InternationalAncillary congress = Open(intent);
            if (congress == null)
            {
                return;
            }
            if (intent.LawVote)
            {
                if (!congress.TryGetActiveCivicVote(ref congress.CivicVoteInfos, out InternationalCivicVoteInfo vote))
                {
                    ActionExecutor.Report(intent, false, "não há votação de lei aberta (a sessão ainda não abriu ou a votação já fechou)");
                    return;
                }
                string label = LawLabel(vote.CivicName.ToString());
                if (intent.Choice < 0)
                {
                    ActionExecutor.Report(intent, true, $"abstenção na lei {label}: você não votou (só conta o peso de quem vota)");
                    return;
                }
                var order = new OrderInternationalAction { InternationalAction = InternationalAction.VoteForCivicChoiceIndex, CivicChoiceIndex = intent.Choice };
                InternationalFailureFlags flags = congress.GetInternationalActionFailureFlags(intent.Empire, order);
                if (flags != InternationalFailureFlags.None)
                {
                    ActionExecutor.Report(intent, false, "o Congresso não aceitou o voto: " + Explain(flags));
                    return;
                }
                ActionExecutor.Post(order, intent, null, next =>
                {
                    if (congress.TryGetActiveCivicVote(ref congress.CivicVoteInfos, out InternationalCivicVoteInfo after))
                    {
                        double mine = (float)after.MajorEmpireBallots[next.Empire].SwayContribution;
                        ActionExecutor.Report(next, true, $"votou {(next.Choice == 0 ? "A" : "B")} na lei {label} com peso {N(mine)} (placar agora: {LawScore(after)})");
                    }
                    else
                    {
                        ActionExecutor.Report(next, true, $"voto registrado na lei {label}");
                    }
                });
                return;
            }

            if (!Crisis(intent, congress, out int declarator, out int target))
            {
                return;
            }
            string crisis = $"K{intent.CrisisVote} (E{declarator} contra E{target})";
            if (intent.Choice < 0)
            {
                ActionExecutor.Report(intent, true, $"abstenção na crise {crisis}: você não tomou lado");
                return;
            }
            InternationalAction side = intent.Choice == declarator ? InternationalAction.SideWithCrisisDeclarator
                : intent.Choice == target ? InternationalAction.SideWithCrisisTargeted : InternationalAction.None;
            if (side == InternationalAction.None)
            {
                ActionExecutor.Report(intent, false, $"na crise {crisis} só dá para apoiar E{declarator} ou E{target}");
                return;
            }
            var sideOrder = new OrderInternationalAction { InternationalAction = side, CrisisVoteIndex = intent.CrisisVote };
            InternationalFailureFlags sideFlags = congress.GetInternationalActionFailureFlags(intent.Empire, sideOrder);
            if (sideFlags != InternationalFailureFlags.None)
            {
                ActionExecutor.Report(intent, false, "o Congresso não aceitou o voto: " + Explain(sideFlags));
                return;
            }
            ActionExecutor.Post(sideOrder, intent, null, next =>
            {
                ref InternationalCrisisVoteInfo after = ref congress.CrisisVoteInfos.GetReferenceAt(next.CrisisVote);
                double mine = (float)after.MajorEmpireBallots[next.Empire].SwayContribution;
                ActionExecutor.Report(next, true, $"apoiou E{next.Choice} na crise {crisis} com peso {N(mine)} (placar agora: {CrisisScore(ref after)})");
            });
        }

        // ---------------- Subornar ----------------

        internal static void Bribe(ActionIntent intent, MajorEmpire empire)
        {
            InternationalAncillary congress = Open(intent);
            if (congress == null)
            {
                return;
            }
            int target = intent.BribeTarget;
            if (target < 0 || target >= Sandbox.NumberOfMajorEmpires || target == intent.Empire || !(Sandbox.MajorEmpires[target]?.IsAlive ?? false))
            {
                ActionExecutor.Report(intent, false, "nação inválida para suborno");
                return;
            }
            InternationalBribeInfo bribe;
            string where;
            if (intent.LawVote)
            {
                if (!congress.TryGetActiveCivicVote(ref congress.CivicVoteInfos, out InternationalCivicVoteInfo vote))
                {
                    ActionExecutor.Report(intent, false, "não há votação de lei aberta");
                    return;
                }
                if (vote.MajorEmpireBallots[intent.Empire].HasVoted())
                {
                    ActionExecutor.Report(intent, false, "você já votou nesta lei: suborno só vale antes do voto");
                    return;
                }
                bribe = vote.MajorEmpireBallots[intent.Empire].BribeInfos[target];
                where = "na lei " + LawLabel(vote.CivicName.ToString());
            }
            else
            {
                if (!Crisis(intent, congress, out int declarator, out int other))
                {
                    return;
                }
                ref InternationalCrisisVoteInfo crisis = ref congress.CrisisVoteInfos.GetReferenceAt(intent.CrisisVote);
                if (crisis.MajorEmpireBallots[intent.Empire].HasVoted())
                {
                    ActionExecutor.Report(intent, false, "você já votou nessa crise: suborno só vale antes do voto");
                    return;
                }
                bribe = crisis.MajorEmpireBallots[intent.Empire].BribeInfos[target];
                where = $"na crise K{intent.CrisisVote} (E{declarator} contra E{other})";
            }
            if (empire.Consulat.Entity == null || empire.Consulat.Entity.DistrictStatus != DistrictStatus.None)
            {
                ActionExecutor.Report(intent, false, "subornar exige o seu consulado funcionando");
                return;
            }
            double leverage = (float)empire.DiplomaticRelationByOtherEmpireIndex[target].GetEmpireEmbassy(intent.Empire).LeverageActionPointStock.Value;
            double cost = (float)bribe.LeverageCostToBribe;
            int remaining = bribe.MaximumBribeActionCount - bribe.PurchasedBribeActionCount;
            int affordable = cost > 0 ? (int)Math.Floor(leverage / cost + 1e-6) : 0;
            int count = Math.Min(Math.Max(1, intent.BribeCount), Math.Min(remaining, affordable));
            if (count < 1)
            {
                ActionExecutor.Report(intent, false, remaining <= 0
                    ? $"não há mais suborno possível contra E{target} {where} (o peso deles já foi todo comprado ou é zero)"
                    : $"alavancagem insuficiente contra E{target}: {N(leverage)}, e cada suborno custa {N(cost)}");
                return;
            }
            var order = new OrderInternationalAction
            {
                InternationalAction = intent.LawVote ? InternationalAction.BribeForCivicVote : InternationalAction.BribeForCrisisVote,
                CrisisVoteIndex = intent.LawVote ? -1 : intent.CrisisVote,
                TargetedEmpireIndex = target,
                NumberOfBribeActions = count,
            };
            InternationalFailureFlags flags = congress.GetInternationalActionFailureFlags(intent.Empire, order);
            if (flags != InternationalFailureFlags.None)
            {
                ActionExecutor.Report(intent, false, "o Congresso não aceitou o suborno: " + Explain(flags));
                return;
            }
            int before = bribe.PurchasedBribeActionCount;
            double swayBefore = (float)bribe.PurchasedBonusSway;
            string adjusted = count < intent.BribeCount ? $" (pedidos {intent.BribeCount}, ajustados para {count}: é o que resta ou o que a alavancagem paga)" : string.Empty;
            ActionExecutor.Post(order, intent, null, next =>
            {
                InternationalBribeInfo after = CurrentBribe(congress, next, target);
                if (after.PurchasedBribeActionCount == before)
                {
                    Plugin.Log?.LogWarning($"[IA] Suborno de E{next.Empire} contra E{target} aceito pelo jogo sem efeito (NumberOfBribeActions perdido?)");
                    ActionExecutor.Report(next, false, "o jogo aceitou a ordem, mas o suborno não entrou");
                    return;
                }
                double gained = (float)after.PurchasedBonusSway - swayBefore;
                ActionExecutor.Report(next, true, $"{count} suborno(s) contra E{target} {where}: +{N(gained)} de peso no seu voto agora e +{N(count * cost)} no começo do próximo turno se você ainda não tiver votado;"
                    + $" alavancagem contra E{target}: {N(leverage)} → {N(leverage - count * cost)}{adjusted}");
            });
        }

        private static InternationalBribeInfo CurrentBribe(InternationalAncillary congress, ActionIntent intent, int target)
        {
            if (intent.LawVote)
            {
                int last = congress.CivicVoteInfos.Length - 1;
                return last >= 0 ? congress.CivicVoteInfos.Data[last].MajorEmpireBallots[intent.Empire].BribeInfos[target] : default(InternationalBribeInfo);
            }
            return congress.CrisisVoteInfos.GetReferenceAt(intent.CrisisVote).MajorEmpireBallots[intent.Empire].BribeInfos[target];
        }

        // ---------------- Veredito ----------------

        internal static void AnswerVerdict(ActionIntent intent, MajorEmpire empire)
        {
            BaseDiplomaticState state = Sandbox.DiplomaticAncillary.GetRelationFor(intent.Empire, intent.Other)?.DiplomaticState;
            if (state == null || !state.Crisis.HasLostInternationalCrisisVote(intent.Empire) || state.Crisis.InternationalCrisisWinnerEmpireIndex != intent.Other)
            {
                ActionExecutor.Report(intent, false, "não há veredito do Congresso contra você nessa disputa (já foi respondido ou foi cancelado)");
                return;
            }
            DiplomaticActionFailureFlags flags = empire.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(intent.Other, intent.Diplomatic);
            if (flags != DiplomaticActionFailureFlags.None)
            {
                ActionExecutor.Report(intent, false, "o jogo não permite agora: " + ActionExecutor.Explain(flags));
                return;
            }
            bool comply = intent.Diplomatic == DiplomaticAction.AcceptDemands;
            ActionExecutor.Post(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = intent.Diplomatic }, intent,
                comply ? "veredito cumprido: as exigências deles foram entregues e a crise acabou"
                    : "guerra surpresa declarada contra o veredito do Congresso (não sancionada: as nações que não são suas aliadas ganham alavancagem e uma reclamação contra você)");
        }

        // ---------------- Levar a disputa ao Congresso ----------------

        internal static void DeclareCrisis(ActionIntent intent, MajorEmpire empire)
        {
            DiplomaticActionFailureFlags flags = empire.DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags(intent.Other, DiplomaticAction.DeclareInternationalCrisis);
            if (flags != DiplomaticActionFailureFlags.None)
            {
                var reasons = new List<string>();
                InternationalAncillary congress = Sandbox.InternationalAncillary;
                if ((flags & DiplomaticActionFailureFlags.Locked) != 0 && congress != null)
                {
                    InternationalFailureFlags why = congress.GetStartCrisisVoteFailureFlags(intent.Empire, intent.Other);
                    if (why != InternationalFailureFlags.None)
                    {
                        reasons.Add(Explain(why));
                    }
                    flags &= ~DiplomaticActionFailureFlags.Locked;
                }
                if (flags != DiplomaticActionFailureFlags.None)
                {
                    reasons.Add(ActionExecutor.Explain(flags));
                }
                ActionExecutor.Report(intent, false, "o jogo não deixa levar a disputa ao Congresso agora: " + (reasons.Count > 0 ? string.Join("; ", reasons) : "travado no momento"));
                return;
            }
            ActionExecutor.Post(new OrderDiplomaticAction { OtherEmpireIndex = intent.Other, DiplomaticAction = DiplomaticAction.DeclareInternationalCrisis }, intent, null, next =>
            {
                InternationalAncillary congress = Sandbox.InternationalAncillary;
                if (congress != null && congress.TryGetActiveCrisisVoteBetween(next.Empire, next.Other, out InternationalCrisisVoteInfo vote))
                {
                    ActionExecutor.Report(next, true, $"disputa levada ao Congresso: votação K{vote.PoolAllocationIndex} até a virada para o turno {vote.TurnEnd} (último turno para votar: {vote.TurnEnd - 1});"
                        + " empate ou falta de votos favorecem o alvo, então vote em você mesmo");
                }
                else
                {
                    ActionExecutor.Report(next, true, "disputa levada ao Congresso");
                }
            });
        }

        // ---------------- Consenso ideológico ----------------

        internal static void Contribute(ActionIntent intent, MajorEmpire empire)
        {
            InternationalAncillary congress = Open(intent);
            if (congress == null)
            {
                return;
            }
            int axis = intent.ConsensusAxis;
            if (axis < 0 || axis >= congress.IdeologicalConsensusInfos.Length)
            {
                ActionExecutor.Report(intent, false, "eixo do consenso inválido");
                return;
            }
            var order = new OrderInternationalAction { InternationalAction = InternationalAction.ContributeToIdeologicalConsensus, IdeologicalConsensusIndex = axis };
            InternationalFailureFlags flags = congress.GetInternationalActionFailureFlags(intent.Empire, order);
            if (flags != InternationalFailureFlags.None)
            {
                ActionExecutor.Report(intent, false, "o Congresso não aceitou a contribuição: " + Explain(flags));
                return;
            }
            ActionExecutor.Post(order, intent, null, next =>
            {
                InternationalIdeologicalConsensusInfo info = congress.IdeologicalConsensusInfos.Data[axis];
                ActionExecutor.Report(next, true, info.IsUnlocked
                    ? $"contribuição feita: o eixo I{axis} foi destravado e o consenso do mundo passa a valer"
                    : $"contribuição feita no eixo I{axis}: {(int)info.ContributionCount} de {(int)info.ContributionRequiredCount}");
            });
        }

        // ---------------- Voltas à IA nativa e postura sobre a lei imposta (thread principal) ----------------

        private static Dictionary<int, int> verdictSeen = new Dictionary<int, int>();
        private static int firstSessionSeen = -1;
        private static int firstSessionLeader = -1;

        /// <summary>
        /// A cada segundo (IaModule.UpdateLocks): o veredito que a IA de linguagem deixou sem resposta por
        /// TurnosParaResponderPropostas turnos volta para a IA nativa (que costuma cumprir); a primeira sessão do Congresso,
        /// que espera o primeiro presidente para sempre, também; e cada lei imposta a uma nação da IA de linguagem ganha a
        /// resposta que ela escolheu (votou na opção vencedora = adota; senão, a postura se_perder do voto).
        /// </summary>
        /// <summary>
        /// Um passo da espera da primeira sessão: true quando o presidente já esperou o prazo e a IA nativa propõe por ele.
        /// A espera conta por presidente: um presidente novo (o anterior morreu ou ficou sem lei para propor) não herda os
        /// turnos do outro, e sem presidente nada conta.
        /// </summary>
        internal static bool FirstSessionStep(int turn, int leader, int maxTurns, ref int seen, ref int seenLeader)
        {
            if (leader < 0 || leader != seenLeader || seen < 0 || seen > turn)
            {
                seen = leader < 0 ? -1 : turn;
                seenLeader = leader;
            }
            return leader >= 0 && turn - seen >= maxTurns;
        }

        /// <summary>Teste ("ia teste congresso"): a espera da 1ª sessão numa sequência de turnos e presidentes.</summary>
        internal static string SelfTestFirstSession()
        {
            int seen = -1, seenLeader = -1;
            var steps = new[] { (10, 3), (11, 3), (12, 3), (13, 3), (13, 5), (14, 5), (15, -1), (16, 5), (18, 5), (19, 5) };
            var text = new System.Text.StringBuilder("prazo 3 turnos; (turno, presidente) → a IA nativa propõe?\n");
            foreach ((int turn, int leader) in steps)
            {
                bool fallback = FirstSessionStep(turn, leader, 3, ref seen, ref seenLeader);
                text.AppendLine($"T{turn} E{leader}: {(fallback ? "SIM" : "não")} (esperando desde T{seen})");
            }
            return text.ToString();
        }

        internal static void TrackFallbacks(int turn, IaWorld world, WorldCapture capture)
        {
            InternationalAncillary congress = Sandbox.InternationalAncillary;
            DiplomaticAncillary diplomacy = Sandbox.DiplomaticAncillary;
            if (congress == null || diplomacy?.DiplomaticRelations == null || !CongressCapture.ExpansionActive)
            {
                NativeAiLocks.SetCongressFallback(null, null, null);
                return;
            }
            int maxTurns = Math.Max(1, IaConfig.ProposalTurns.Value);
            var seen = new Dictionary<int, int>();
            var verdicts = new HashSet<int>();
            foreach (DiplomaticRelation relation in diplomacy.DiplomaticRelations)
            {
                BaseDiplomaticState state = relation?.DiplomaticState;
                if (state == null || state.Crisis.CrisisStatus != CrisisInfo.Status.InternationalCrisisVoteEnded)
                {
                    continue;
                }
                int winner = state.Crisis.InternationalCrisisWinnerEmpireIndex;
                int loser = winner == relation.LeftEmpireIndex ? relation.RightEmpireIndex : relation.LeftEmpireIndex;
                int key = (loser << 16) | winner;
                int first = verdictSeen.TryGetValue(key, out int known) ? known : turn;
                seen[key] = first;
                if (turn - first >= maxTurns)
                {
                    verdicts.Add(key);
                }
            }
            verdictSeen = seen;

            var leaders = new HashSet<int>();
            if (congress.IsCivicVoteUnlocked && congress.CivicVoteInfos.Length == 0)
            {
                int leader = capture?.Congress?.NextLeader ?? -1;
                if (FirstSessionStep(turn, leader, maxTurns, ref firstSessionSeen, ref firstSessionLeader))
                {
                    leaders.Add(leader);
                }
            }
            else
            {
                firstSessionSeen = -1;
            }

            // Voto e postura de cada nação em cada lei, publicados antes de a votação fechar: a decisão sobre a lei imposta
            // sai na hora da ordem da IA nativa (NativeAiLocks.ImposedLawPatch).
            var stances = new Dictionary<string, NativeAiLocks.LawStance>();
            foreach (IaNation nation in world?.Nations ?? new List<IaNation>())
            {
                foreach (string civic in nation.CongressVotes.Keys.Union(nation.CongressStances.Keys))
                {
                    nation.CongressVotes.TryGetValue(civic, out string voted);
                    nation.CongressStances.TryGetValue(civic, out string ifLost);
                    stances[NativeAiLocks.LawStanceKey(nation.EmpireIndex, civic)] = new NativeAiLocks.LawStance { Voted = voted, IfLost = ifLost };
                }
            }
            NativeAiLocks.SetCongressFallback(verdicts, leaders, stances);
            CongressLawVote last = capture?.Congress?.LawHistory.Count > 0 ? capture.Congress.LawHistory[0] : null;
            NativeAiLocks.SetLastLawResult(last?.Law.Civic, last == null || last.Winner < 0 ? null : last.Winner == 0 ? last.Law.ChoiceA : last.Law.ChoiceB, last?.Leader ?? -1);
        }

        // ---------------- Notícias (thread principal) ----------------

        /// <summary>
        /// Guarda no IaWorld os resultados que aparecem na foto do turno: leis votadas (ficam no histórico do jogo) e crises
        /// encerradas (o jogo libera o lugar delas logo depois). Devolve true se entrou notícia nova.
        /// </summary>
        internal static bool RecordNews(IaWorld world, WorldCapture capture)
        {
            CapturedCongress congress = capture?.Congress;
            if (world == null || congress == null)
            {
                return false;
            }
            bool added = false;
            foreach (CongressLawVote vote in congress.LawHistory)
            {
                string key = $"lei:{vote.Law.Civic}:{vote.TurnEnd}";
                if (world.CongressNews.Any(n => n.Key == key))
                {
                    continue;
                }
                var news = new CongressNews
                {
                    Key = key,
                    Turn = vote.TurnEnd,
                    Kind = "lei",
                    Civic = vote.Law.Civic,
                    ChoiceA = vote.Law.ChoiceA,
                    ChoiceB = vote.Law.ChoiceB,
                    Winner = vote.Winner,
                    State = vote.State,
                };
                foreach (CongressBallot ballot in vote.Ballots)
                {
                    news.Votes.Add(ballot?.Choice ?? -1);
                    news.Sway.Add(ballot?.Sway ?? 0);
                }
                world.CongressNews.Add(news);
                added = true;
            }
            foreach (CongressCrisis crisis in congress.Crises.Where(c => !c.Active))
            {
                string key = $"crise:{crisis.Declarator}:{crisis.Target}:{crisis.TurnBegin}";
                if (world.CongressNews.Any(n => n.Key == key))
                {
                    continue;
                }
                bool cancelled = crisis.State.StartsWith("Cancelled");
                var news = new CongressNews
                {
                    Key = key,
                    Turn = Math.Min(crisis.TurnEnd, capture.Turn),
                    Kind = "crise",
                    Declarator = crisis.Declarator,
                    Target = crisis.Target,
                    Winner = cancelled ? -1 : crisis.Winner,
                    State = crisis.State,
                };
                foreach (CongressBallot ballot in crisis.Ballots)
                {
                    news.Votes.Add(ballot?.Choice ?? -1);
                    news.Sway.Add(ballot?.Sway ?? 0);
                }
                world.CongressNews.Add(news);
                added = true;
            }
            if (world.CongressNews.Count > 12)
            {
                world.CongressNews.RemoveRange(0, world.CongressNews.Count - 12);
            }
            return added;
        }

        // ---------------- Comandos de teste (thread principal) ----------------

        /// <summary>"jogo congresso liberar E#": as três ordens de depuração do próprio jogo; quem posta preside a 1ª sessão.</summary>
        internal static string ForceUnlock(int empire)
        {
            InternationalAncillary congress = Sandbox.InternationalAncillary;
            if (congress == null || !CongressCapture.ExpansionActive || congress.IsInternationalDisabled)
            {
                return "erro: a partida não tem o Congresso (expansão desligada ou opção da partida desligada); forçar criaria votações que nunca terminam";
            }
            if (!GameAccess.IsTurnMain())
            {
                return "erro: só no turno principal";
            }
            SandboxManager.PostOrder(new OrderInternationalAction { InternationalAction = InternationalAction.ForceUnlockCivicVote }, empire);
            SandboxManager.PostOrder(new OrderInternationalAction { InternationalAction = InternationalAction.ForceUnlockCrisisVote }, empire);
            SandboxManager.PostOrder(new OrderInternationalAction { InternationalAction = InternationalAction.ForceUnlockIdeologicalConsensus }, empire);
            return $"ok: Congresso forçado por E{empire} (ela preside a 1ª sessão). Todas as nações passam a se conhecer. Salve e recarregue para o consenso mostrar os valores certos.";
        }

        /// <summary>"jogo congresso encerrar lei|K#": encerra a votação na hora (ordem de depuração do jogo).</summary>
        internal static string ForceEnd(string what)
        {
            InternationalAncillary congress = Sandbox.InternationalAncillary;
            Sandbox sandbox = SandboxManager.Sandbox;
            if (congress == null || sandbox == null)
            {
                return "erro: sem partida";
            }
            if ((what ?? string.Empty).StartsWith("lei", StringComparison.OrdinalIgnoreCase))
            {
                SandboxManager.PostOrder(new OrderInternationalAction { InternationalAction = InternationalAction.ForceEndCivicVote }, sandbox.LocalEmpireIndex);
                return "ok: votação de lei encerrada (o resultado sai agora)";
            }
            if (what != null && what.StartsWith("K", StringComparison.OrdinalIgnoreCase) && int.TryParse(what.Substring(1), out int pool))
            {
                SandboxManager.PostOrder(new OrderInternationalAction { InternationalAction = InternationalAction.ForceEndCrisisVote, CrisisVoteIndex = pool }, sandbox.LocalEmpireIndex);
                return $"ok: votação de crise K{pool} encerrada (o resultado sai agora)";
            }
            return "uso: jogo congresso encerrar lei|K#";
        }

        /// <summary>"jogo congresso consulado E#": cria o distrito Consulado numa casa livre do território da capital (ordem de editor).</summary>
        internal static string CreateConsulate(int empire)
        {
            MajorEmpire major = Sandbox.MajorEmpires[empire];
            Settlement capital = null;
            for (int s = 0; s < major.Settlements.Count; s++)
            {
                if (major.Settlements[s] != null && major.Settlements[s].IsCapital)
                {
                    capital = major.Settlements[s];
                }
            }
            if (capital == null)
            {
                return $"erro: E{empire} não tem capital";
            }
            string definition = null;
            foreach (ConstructibleDefinition item in Amplitude.Framework.Databases.GetDatabase<ConstructibleDefinition>())
            {
                if (item is EmpireWideConsulatDefinition)
                {
                    definition = item.Name.ToString();
                    break;
                }
            }
            if (definition == null)
            {
                return "erro: definição do consulado não encontrada";
            }
            Amplitude.Mercury.Simulation.World world = Sandbox.World;
            int center = world.TileInfo.Data[capital.WorldPosition.ToTileIndex()].TerritoryIndex;
            var terrains = Amplitude.Mercury.Simulation.World.Tables.TerrainTypeDefinitions;
            for (int tile = 0; tile < world.TileInfo.Data.Length; tile++)
            {
                if (world.TileInfo.Data[tile].TerritoryIndex != center)
                {
                    continue;
                }
                var terrain = terrains[world.TileInfo.Data[tile].TerrainType];
                if (terrain == null || terrain.IsWater() || !terrain.AllowCityConstruction || Amplitude.Mercury.Simulation.World.Maps.DistrictMap[tile].IsValid || Amplitude.Mercury.Simulation.World.Maps.ArmyMap[tile].IsValid)
                {
                    continue;
                }
                SandboxManager.PostOrder(new EditorOrderCreateExtensionDistrictAt { DistrictDefinitionName = new StaticString(definition), TileIndex = tile });
                return $"ok: consulado ({definition}) pedido no tile {tile} de T{center} (capital de E{empire})";
            }
            return $"erro: nenhuma casa livre no território da capital de E{empire}";
        }

        /// <summary>"jogo congresso alavancagem E# E# n": dá (ou tira) alavancagem do primeiro contra o segundo (ordem de editor).</summary>
        internal static string AddLeverage(int empire, int other, int delta)
        {
            SandboxManager.PostOrder(new EditorOrderAddOrRemoveLeveragePointStock { EmpireIndex = empire, OtherEmpireIndex = other, PointDelta = delta }, empire);
            return $"ok: {delta:+0;-0} de alavancagem para E{empire} contra E{other}";
        }

        /// <summary>"jogo congresso lei E# NomeDoCivico": deixa uma lei disponível para a nação (ordem de editor).</summary>
        internal static string UnlockLaw(int empire, string civic)
        {
            SandboxManager.PostOrder(new EditorOrderUnlockCivic { EmpireIndex = empire, CivicName = new StaticString(civic) });
            return $"ok: lei {civic} liberada para E{empire}";
        }

        /// <summary>"ia congresso": diagnóstico completo a partir da foto do turno (e da simulação, para o que muda no turno).</summary>
        internal static string Describe(WorldCapture capture)
        {
            InternationalAncillary live = Sandbox.InternationalAncillary;
            var text = new System.Text.StringBuilder();
            text.AppendLine($"Expansão Together We Rule na partida: {(CongressCapture.ExpansionActive ? "sim" : "NÃO")} · Congresso na partida: {(live == null ? "?" : live.IsInternationalDisabled ? "DESLIGADO" : "ligado")}"
                + $" · {ExpansionSaveHint()}");
            CapturedCongress congress = capture?.Congress;
            if (congress == null)
            {
                text.AppendLine("Sem Congresso na foto do turno (sem a expansão ou desligado; ou foto de antes de ligar).");
                return text.ToString().TrimEnd();
            }
            text.AppendLine($"lei liberada: {congress.CivicUnlocked} · crise liberada: {congress.CrisisUnlocked} · consenso liberado: {congress.ConsensusUnlocked} · quem formou: E{congress.Unlocker}"
                + $" · era mínima {congress.MinEra} ({GameText.EraName(congress.MinEra)}) · fração conhecida {congress.MinMetFraction:0.##}");
            if (congress.LeaderOrder.Length > 0)
            {
                text.AppendLine("ordem da presidência: " + string.Join(" → ", congress.LeaderOrder.Select(e => "E" + e)));
            }
            text.AppendLine($"sessões até agora: {congress.SessionCount} · próximo presidente: E{congress.NextLeader} a partir do turno {congress.NextSessionTurn} · leis propuníveis: {congress.Proposable.Count}"
                + (congress.Proposable.Count > 0 ? " (" + string.Join(", ", congress.Proposable.Select(l => $"V{l.Code} {l.Civic}")) + ")" : string.Empty));
            var rows = new List<string>();
            for (int i = 0; i < congress.Sway.Length; i++)
            {
                CapturedEmpire empire = capture.Empire(i);
                if (empire == null || !empire.Alive)
                {
                    continue;
                }
                double leverage = i < congress.Leverage.Length ? congress.Leverage[i].Sum() : 0;
                string blocked = i < congress.UnlockBlocked.Length && !string.IsNullOrEmpty(congress.UnlockBlocked[i]) ? $" falta[{congress.UnlockBlocked[i]}]" : string.Empty;
                rows.Add($"E{i}{(empire.Human ? "*" : string.Empty)} peso {congress.Sway[i]:0.#} consulado {(congress.Consulate[i] ? "sim" : "não")} alavancagem {leverage:0.#} {empire.EraName}{blocked}");
            }
            text.AppendLine(string.Join("\n", rows));
            if (congress.LawVote != null)
            {
                CongressLawVote vote = congress.LawVote;
                text.AppendLine($"VOTAÇÃO DE LEI: V{vote.Law.Code} {vote.Law.Civic} (A {vote.Law.ChoiceA} × B {vote.Law.ChoiceB}) · presidente E{vote.Leader} · turnos {vote.TurnBegin}–{vote.TurnEnd} · estado {vote.State}");
                text.AppendLine("   cédulas: " + string.Join(" ", vote.Ballots.Select((b, i) => b == null ? null : $"E{i}:{(b.Choice == 0 ? "A" : b.Choice == 1 ? "B" : "-")}({b.Sway:0.#}{(b.BribeBonus > 0 ? $"+sub {b.BribeBonus:0.#}" : string.Empty)}{(b.BribePending > 0 ? $"+pend {b.BribePending:0.#}" : string.Empty)})").Where(s => s != null)));
            }
            foreach (CongressLawVote vote in congress.LawHistory)
            {
                text.AppendLine($"lei encerrada: {vote.Law.Civic} · turnos {vote.TurnBegin}–{vote.TurnEnd} · {vote.State} · vencedor {(vote.Winner == 0 ? "A" : vote.Winner == 1 ? "B" : "empate")}");
            }
            foreach (CongressCrisis crisis in congress.Crises)
            {
                text.AppendLine($"CRISE K{crisis.Pool}: E{crisis.Declarator} contra E{crisis.Target} · turnos {crisis.TurnBegin}–{crisis.TurnEnd} · {crisis.State}"
                    + (crisis.Active ? string.Empty : $" · vencedor E{crisis.Winner}{(crisis.WaitingLoser ? $" · espera E{crisis.Loser}" : string.Empty)}"));
                text.AppendLine("   cédulas: " + string.Join(" ", crisis.Ballots.Select((b, i) => b == null || b.Choice < 0 ? null : $"E{i}→E{b.Choice}({b.Sway:0.#})").Where(s => s != null)));
            }
            foreach (CongressAxis axis in congress.Consensus)
            {
                text.AppendLine($"consenso I{axis.Index} {axis.Definition}: {(axis.Unlocked ? $"em vigor desde {axis.UnlockTurn} ({axis.Orientation})" : $"{axis.Count}/{axis.Required}")}");
            }
            foreach (CongressImposedLaw law in congress.ImposedLaws)
            {
                text.AppendLine($"lei imposta: E{law.Empire} recebe {law.Civic} → {law.Choice} (evento {law.EventIndex}, recusar custa {law.DiscardCost:0.#}{(law.CanDiscard ? string.Empty : ", sem influência")})");
            }
            text.AppendLine(NativeAiLocks.DescribeCongress());
            return text.ToString().TrimEnd();
        }

        /// <summary>Se a expansão foi ligada à força neste save (patch de teste em saves criados sem ela).</summary>
        internal static string ExpansionSaveHint() => ExpansionSavePatch.Forced || ExpansionSavePatch.ForcedBySession()
            ? "expansão LIGADA À FORÇA neste save ([IA] ExpansaoEmSaveAntigo)" : "expansão como o save foi criado";

        // ---------------- Códigos do dossiê ----------------

        /// <summary>Lei pelo código V# (índice do cívico, igual em todas as nações) ou pelo nome interno: o nome da definição.</summary>
        internal static string CivicForCode(string code)
        {
            if (string.IsNullOrEmpty(code) || Sandbox.MajorEmpires == null || Sandbox.MajorEmpires.Length == 0)
            {
                return null;
            }
            var civics = Sandbox.MajorEmpires[0].DepartmentOfDevelopment.Civics.Data;
            if (code.StartsWith("V", StringComparison.OrdinalIgnoreCase) && int.TryParse(code.Substring(1), out int index))
            {
                return index >= 0 && index < civics.Length && civics[index].CivicDefinition != null && civics[index].CivicDefinition.IsInternational
                    ? civics[index].CivicDefinition.Name.ToString()
                    : null;
            }
            foreach (var civic in civics)
            {
                if (civic.CivicDefinition != null && civic.CivicDefinition.IsInternational && civic.CivicDefinition.Name.ToString().Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    return civic.CivicDefinition.Name.ToString();
                }
            }
            return null;
        }

        /// <summary>Votação de crise pelo código K# do dossiê (ou só o número, nos testes).</summary>
        internal static bool TryCrisisCode(string code, ValidationContext context, out int pool)
        {
            pool = -1;
            if (string.IsNullOrEmpty(code))
            {
                return false;
            }
            if (context != null && context.CrisisVotes.TryGetValue(code.Trim(), out CrisisVoteRef known))
            {
                pool = known.Pool;
                return true;
            }
            string digits = code.Trim().TrimStart('K', 'k');
            return int.TryParse(digits, out pool) && pool >= 0;
        }

        // ---------------- Utilidades ----------------

        private static InternationalAncillary Open(ActionIntent intent)
        {
            InternationalAncillary congress = Sandbox.InternationalAncillary;
            if (congress == null || congress.IsInternationalDisabled || !CongressCapture.ExpansionActive)
            {
                ActionExecutor.Report(intent, false, "não há Congresso mundial nesta partida");
                return null;
            }
            return congress;
        }

        private static bool Crisis(ActionIntent intent, InternationalAncillary congress, out int declarator, out int target)
        {
            declarator = target = -1;
            if (!congress.IsCrisisVoteUnlocked || intent.CrisisVote < 0 || intent.CrisisVote >= congress.CrisisVoteInfos.Capacity)
            {
                ActionExecutor.Report(intent, false, "votação de crise inválida");
                return false;
            }
            ref InternationalCrisisVoteInfo crisis = ref congress.CrisisVoteInfos.GetReferenceAt(intent.CrisisVote);
            if (crisis.PoolAllocationIndex < 0 || !crisis.IsActive())
            {
                ActionExecutor.Report(intent, false, $"a votação da crise K{intent.CrisisVote} já fechou");
                return false;
            }
            declarator = crisis.DeclaratorEmpireIndex;
            target = crisis.TargetedEmpireIndex;
            return true;
        }

        private static string LawScore(InternationalCivicVoteInfo vote)
        {
            double a = 0, b = 0;
            foreach (InternationalCivicVoteInfo.BallotInfo ballot in vote.MajorEmpireBallots)
            {
                if (ballot.ChoiceIndex == 0) a += (float)ballot.SwayContribution;
                else if (ballot.ChoiceIndex == 1) b += (float)ballot.SwayContribution;
            }
            return $"A {N(a)} × B {N(b)}";
        }

        private static string CrisisScore(ref InternationalCrisisVoteInfo crisis)
        {
            double declarator = 0, target = 0;
            foreach (InternationalCrisisVoteInfo.BallotInfo ballot in crisis.MajorEmpireBallots)
            {
                if (ballot.SideWithEmpireIndex == crisis.DeclaratorEmpireIndex) declarator += (float)ballot.SwayContribution;
                else if (ballot.SideWithEmpireIndex == crisis.TargetedEmpireIndex) target += (float)ballot.SwayContribution;
            }
            return $"E{crisis.DeclaratorEmpireIndex} {N(declarator)} × E{crisis.TargetedEmpireIndex} {N(target)}";
        }

        /// <summary>Nome traduzido da lei (ou o nome interno, se a tradução não carregou).</summary>
        internal static string LawLabel(string civic) => "«" + (GameGlossary.Title(civic) ?? civic ?? "?") + "»";

        private static string N(double value) => DossierBuilder.N(value);

        /// <summary>Motivos de recusa do Congresso em português (research\congress.md §7.7).</summary>
        internal static string Explain(InternationalFailureFlags flags)
        {
            var reasons = new List<string>();
            void Add(InternationalFailureFlags flag, string text)
            {
                if ((flags & flag) != 0)
                {
                    reasons.Add(text);
                }
            }
            Add(InternationalFailureFlags.InternationalDisabled, "o Congresso está desligado nesta partida");
            Add(InternationalFailureFlags.Locked, "o Congresso ainda não se formou (ou essa parte dele ainda está fechada)");
            Add(InternationalFailureFlags.WrongSessionLeader, "você não preside esta sessão");
            Add(InternationalFailureFlags.SessionHasNotStarted, "a sessão ainda não abriu");
            Add(InternationalFailureFlags.VoteInactive, "não há votação aberta (a sessão ainda não abriu ou a votação já fechou)");
            Add(InternationalFailureFlags.VoteActive, "já há votação de lei em andamento");
            Add(InternationalFailureFlags.NoCrisisVote | InternationalFailureFlags.NoCivicVote, "não há votação desse tipo");
            Add(InternationalFailureFlags.WrongCivicProposal | InternationalFailureFlags.NoProposableCivic, "essa lei não pode ser proposta por você (não é internacional ou não está disponível para você)");
            Add(InternationalFailureFlags.WrongVoteChoice, "opção inválida: use A ou B");
            Add(InternationalFailureFlags.AlreadyVoted, "você já votou nesta votação (o voto não muda; suborno só antes de votar)");
            Add(InternationalFailureFlags.NotEnoughSway, "seu peso no Congresso é zero");
            Add(InternationalFailureFlags.NoConsulatBuilt, "você não tem consulado funcionando");
            Add(InternationalFailureFlags.NotEnoughLeverage, "alavancagem insuficiente contra essa nação");
            Add(InternationalFailureFlags.NotEnoughAction, "número de subornos inválido ou acima do que resta contra essa nação");
            Add(InternationalFailureFlags.WrongEmpire, "nação inválida (ou você mesmo)");
            Add(InternationalFailureFlags.WrongOrder, "votação ou alvo inválido");
            Add(InternationalFailureFlags.AlreadyInActiveCrisis, "já há crise entre vocês no Congresso (ou a anterior ainda não foi encerrada)");
            Add(InternationalFailureFlags.NotEnoughInfluence, "influência insuficiente");
            Add(InternationalFailureFlags.AlreadyUnlocked, "já destravado");
            Add(InternationalFailureFlags.NotInCorrectEra, "era insuficiente");
            Add(InternationalFailureFlags.NotEnoughEmpiresMet, "você conhece poucas nações");
            return reasons.Count > 0 ? string.Join("; ", reasons) : flags.ToString();
        }
    }
}
