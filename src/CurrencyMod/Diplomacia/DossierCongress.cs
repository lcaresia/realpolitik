using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacia.Capture;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Seção "CONGRESSO MUNDIAL" do dossiê (research\congress.md §7.6): como ele se forma, o peso de cada nação, a
    /// presidência e as leis que dá para propor, a votação de lei aberta (placar, quem falta, subornos), as crises em
    /// votação, os vereditos, a lei imposta, o consenso ideológico e as notícias. Preenche o ValidationContext com os
    /// códigos V (leis), K (crises) e I (eixos) que as ferramentas usam. Só aparece nas partidas com o Congresso.
    /// </summary>
    internal static class DossierCongress
    {
        internal static void Append(StringBuilder text, IaWorld world, WorldCapture capture, CapturedEmpire me, DossierBuild build)
        {
            CapturedCongress congress = capture.Congress;
            if (congress == null)
            {
                return;
            }
            ValidationContext context = build.Context;
            int self = me.Index;
            context.HasConsulate = self < congress.Consulate.Length && congress.Consulate[self];
            context.NoSway = self >= congress.Sway.Length || congress.Sway[self] <= 0;
            text.AppendLine("== CONGRESSO MUNDIAL ==");
            if (!congress.CivicUnlocked && !congress.CrisisUnlocked && !congress.ConsensusUnlocked)
            {
                AppendNotFormed(text, capture, congress, me);
                text.AppendLine();
                return;
            }
            context.CongressExists = true;
            AppendGeneral(text, capture, congress, me);
            if (congress.CivicUnlocked)
            {
                AppendPresidency(text, capture, congress, me, context);
                AppendLawVote(text, capture, congress, me, context);
            }
            if (congress.CrisisUnlocked)
            {
                AppendCrises(text, capture, congress, me, context);
            }
            AppendVerdicts(text, capture, me, context);
            AppendImposedLaw(text, world, congress, me);
            if (congress.ConsensusUnlocked)
            {
                AppendConsensus(text, congress, me, context);
            }
            AppendNews(text, world, capture);
            text.AppendLine();
        }

        private static void AppendNotFormed(StringBuilder text, WorldCapture capture, CapturedCongress congress, CapturedEmpire me)
        {
            string era = congress.MinEra >= 0 ? $"na {GameText.EraName(congress.MinEra)} ou depois" : "na era exigida";
            string met = congress.MinMetFraction > 0 ? $"pelo menos {DossierBuilder.N(congress.MinMetFraction * 100)}% das nações vivas" : "a maior parte das nações vivas";
            text.AppendLine($"Ainda não se formou. Forma-se sozinho no começo do turno em que alguma nação estiver {era}, tiver o distrito Consulado funcionando e conhecer {met}."
                + " Quando ele se formar, todas as nações passam a se conhecer, e ele vota leis para o mundo inteiro e julga disputas.");
            int alive = capture.AliveEmpires().Count() - 1;
            int known = me.Relations.Count(r => r != null && r.Knows);
            string blocked = me.Index < congress.UnlockBlocked.Length ? congress.UnlockBlocked[me.Index] : null;
            var mine = new List<string> { me.EraName };
            if (blocked != null)
            {
                if (blocked.Contains("era")) mine.Add("falta a era");
                mine.Add(blocked.Contains("consulado") ? "sem consulado" : "consulado funcionando");
                mine.Add($"conhece {known} de {alive} nações{(blocked.Contains("contatos") ? " (poucas)" : string.Empty)}");
            }
            else
            {
                mine.Add(congress.Consulate[me.Index] ? "consulado funcionando" : "sem consulado");
                mine.Add($"conhece {known} de {alive} nações");
            }
            text.AppendLine("Você: " + string.Join(", ", mine) + ".");
        }

        private static void AppendGeneral(StringBuilder text, WorldCapture capture, CapturedCongress congress, CapturedEmpire me)
        {
            int self = me.Index;
            double total = 0;
            var weights = new List<KeyValuePair<int, double>>();
            foreach (int empire in capture.AliveEmpires())
            {
                double sway = empire < congress.Sway.Length ? congress.Sway[empire] : 0;
                total += Math.Max(0, sway);
                if (empire != self)
                {
                    weights.Add(new KeyValuePair<int, double>(empire, sway));
                }
            }
            double mySway = self < congress.Sway.Length ? congress.Sway[self] : 0;
            string share = total > 0 ? $" ({DossierBuilder.N(100 * Math.Max(0, mySway) / total)}% do total)" : string.Empty;
            string formed = congress.Unlocker >= 0 ? (congress.Unlocker == self ? "Formado por você." : $"Formado por E{congress.Unlocker} {DossierBuilder.Name(capture, congress.Unlocker)}.") : "Formado.";
            text.AppendLine($"{formed} Peso no Congresso (o peso do voto de cada nação; vem de vassalos, povos e influência): você {DossierBuilder.N(mySway)}{share} · "
                + string.Join(" · ", weights.OrderByDescending(w => w.Value).Select(w => $"E{w.Key} {DossierBuilder.N(w.Value)}")) + ".");
            if (mySway <= 0)
            {
                text.AppendLine("Seu peso é zero: seu voto não conta até ele crescer.");
            }
            bool consulate = self < congress.Consulate.Length && congress.Consulate[self];
            var leverage = new List<string>();
            if (self < congress.Leverage.Length)
            {
                for (int j = 0; j < congress.Leverage[self].Length; j++)
                {
                    if (j != self && congress.Leverage[self][j] >= 1)
                    {
                        leverage.Add($"contra E{j} {DossierBuilder.N(congress.Leverage[self][j])}");
                    }
                }
            }
            text.AppendLine($"Seu consulado: {(consulate ? "funcionando" : "NÃO construído (sem ele você não suborna nem leva disputas ao Congresso)")}."
                + (leverage.Count > 0 ? $" Sua alavancagem (gasta em subornos): {string.Join(" · ", leverage.Take(8))}." : " Você não tem alavancagem contra ninguém (sem ela, não há suborno)."));
            if (congress.CrisisUnlocked)
            {
                text.AppendLine("Guerra surpresa agora é \"não sancionada\": todas as nações que não são aliadas do agressor ganham alavancagem e uma reclamação contra ele.");
            }
        }

        private static void AppendPresidency(StringBuilder text, WorldCapture capture, CapturedCongress congress, CapturedEmpire me, ValidationContext context)
        {
            int self = me.Index;
            bool voteOpen = congress.LawVote != null;
            if (!voteOpen && congress.NextLeader == self && capture.Turn >= congress.NextSessionTurn)
            {
                context.PresideNow = true;
                bool first = congress.SessionCount == 0;
                int waitTurns = Math.Max(1, IaConfig.ProposalTurns.Value);
                text.AppendLine("VOCÊ PRESIDE A SESSÃO DO CONGRESSO NESTE TURNO: escolha a lei a votar com propor_votacao."
                    + (first ? $" Esta é a primeira sessão: ela espera por você. Se você não propuser em {waitTurns} turno(s), seus diplomatas propõem por você."
                        : " Se não propuser neste turno, a presidência passa para a próxima nação."));
                if (congress.Proposable.Count == 0)
                {
                    text.AppendLine("   (nenhuma lei disponível para você propor agora)");
                }
                else
                {
                    text.AppendLine("   Leis que você pode propor (opção A × opção B · nações em cada opção hoje · a sua):");
                    foreach (CongressLaw law in congress.Proposable)
                    {
                        string code = "V" + law.Code;
                        context.ProposableLaws[code] = law.Civic;
                        text.AppendLine($"   - {code} {LawLine(law, self)}.");
                    }
                }
                return;
            }
            if (congress.NextLeader >= 0)
            {
                string who = congress.NextLeader == self ? "você" : $"E{congress.NextLeader} {DossierBuilder.Name(capture, congress.NextLeader)}";
                string when = voteOpen ? "depois desta votação" : congress.NextSessionTurn > capture.Turn ? $"no turno {congress.NextSessionTurn}" : "agora";
                text.AppendLine($"Próxima sessão de lei: {when}, presidida por {who}. Quem preside escolhe a lei a votar; sem proposta, a presidência passa adiante.");
            }
        }

        private static string LawLine(CongressLaw law, int self)
        {
            int a = law.Current.Count(c => c == 0);
            int b = law.Current.Count(c => c == 1);
            int none = law.Current.Length - a - b;
            int mine = self < law.Current.Length ? law.Current[self] : -1;
            return $"{CongressFlow.LawLabel(law.Civic)}: A {Choice(law.ChoiceA)} × B {Choice(law.ChoiceB)} — A {a}, B {b}, sem a lei {none} · a sua: {(mine == 0 ? "A" : mine == 1 ? "B" : "nenhuma")}";
        }

        private static string Choice(string choice) => "«" + (GameGlossary.Title(choice) ?? choice ?? "?") + "»";

        private static void AppendLawVote(StringBuilder text, WorldCapture capture, CapturedCongress congress, CapturedEmpire me, ValidationContext context)
        {
            CongressLawVote vote = congress.LawVote;
            if (vote == null)
            {
                return;
            }
            int self = me.Index;
            CongressLaw law = vote.Law;
            context.LawVoteOpen = true;
            context.LawVoteCivic = law.Civic;
            CongressBallot mine = self < vote.Ballots.Length ? vote.Ballots[self] : null;
            context.LawVoted = mine != null && mine.Choice >= 0;
            string leader = vote.Leader == self ? "presidida por você" : $"presidida por E{vote.Leader} {DossierBuilder.Name(capture, vote.Leader)}";
            int left = vote.TurnEnd - 1 - capture.Turn;
            text.AppendLine($"Votação de lei em andamento ({leader}): V{law.Code} {CongressFlow.LawLabel(law.Civic)} — A {Choice(law.ChoiceA)} × B {Choice(law.ChoiceB)}.");
            text.AppendLine($"   Fecha na virada para o turno {vote.TurnEnd}: o último turno para votar é o {vote.TurnEnd - 1}{(left > 0 ? $" (faltam {left})" : " (É ESTE)")}. Empate: nada muda.");
            text.AppendLine("   Placar: " + Score(vote.Ballots, 0, "A", 1, "B") + "." + Missing(capture, vote.Ballots, congress, self));
            int current = self < law.Current.Length ? law.Current[self] : -1;
            text.AppendLine(current >= 0
                ? $"   Sua lei hoje: {(current == 0 ? "A " + Choice(law.ChoiceA) : "B " + Choice(law.ChoiceB))}. Se a outra opção vencer, você recebe a lei imposta: adotar troca a sua lei; recusar custa influência."
                : "   Você não tem essa lei hoje: se a votação tiver vencedor, você recebe a lei imposta (adotar ou pagar influência para recusar).");
            if (context.LawVoted)
            {
                text.AppendLine($"   Você já votou {(mine.Choice == 0 ? "A" : "B")} com peso {DossierBuilder.N(mine.Sway)} (o voto não muda).");
                return;
            }
            if (context.NoSway && (mine?.BribeBonus ?? 0) <= 0 && !(HasBribes(mine) && context.HasConsulate))
            {
                // Sem peso e sem como comprar peso: o voto seria recusado pelo jogo.
                context.LawVoteOpen = false;
                text.AppendLine("   Você não vota: seu peso no Congresso é zero. Se a outra opção vencer, você recebe a lei imposta mesmo assim.");
                return;
            }
            if (context.NoSway && (mine?.BribeBonus ?? 0) <= 0)
            {
                text.AppendLine("   Seu peso no Congresso é zero: só com suborno (antes do voto, no mesmo turno) o seu voto conta.");
            }
            text.AppendLine("   Votar: votar_congresso {\"votacao\":\"lei\",\"opcao\":\"A|B|abster\",\"se_perder\":\"adotar|recusar\"}. Seu peso entra como está no momento do voto. Votar no último turno é arriscado: a resposta pode chegar depois do fechamento.");
            AppendBribes(text, mine, "lei", context);
        }

        private static bool HasBribes(CongressBallot mine) => mine?.Bribes != null && mine.Bribes.Any(b => b != null && b.Affordable >= 1);

        private static void AppendBribes(StringBuilder text, CongressBallot mine, string vote, ValidationContext context)
        {
            if (mine?.Bribes == null)
            {
                return;
            }
            var options = new List<string>();
            int example = -1;
            for (int j = 0; j < mine.Bribes.Length; j++)
            {
                CongressBribe bribe = mine.Bribes[j];
                if (bribe == null || bribe.Affordable < 1)
                {
                    continue;
                }
                context.BribeLimits[$"{vote}:E{j}"] = bribe.Affordable;
                example = example < 0 ? j : example;
                options.Add($"E{j} até {bribe.Affordable} vez(es), {DossierBuilder.N(bribe.Cost)} de alavancagem cada, +{DossierBuilder.N(bribe.Bonus)} de peso cada");
            }
            if (mine.BribeBonus > 0)
            {
                text.AppendLine($"   Peso já comprado com suborno nesta votação: +{DossierBuilder.N(mine.BribeBonus)}{(mine.BribePending > 0 ? $" (+{DossierBuilder.N(mine.BribePending)} no começo do próximo turno, se você ainda não tiver votado)" : string.Empty)}.");
            }
            if (options.Count > 0)
            {
                text.AppendLine(context.HasConsulate
                    ? $"   Subornar antes de votar (subornar {{\"votacao\":\"{vote}\",\"nacao\":\"E{example}\",\"vezes\":1}}): {string.Join("; ", options)}."
                    : "   Suborno: só com o seu consulado funcionando.");
                if (!context.HasConsulate)
                {
                    foreach (string key in context.BribeLimits.Keys.Where(k => k.StartsWith(vote + ":")).ToList())
                    {
                        context.BribeLimits.Remove(key);
                    }
                }
            }
        }

        /// <summary>"A 210 (E4, E7) · B 95 (E1)" (lei) ou "E2 150 (E2, E8) × E5 120 (E5)" (crise).</summary>
        private static string Score(CongressBallot[] ballots, int first, string firstLabel, int second, string secondLabel)
        {
            string Side(int choice, string label)
            {
                var voters = new List<string>();
                double sum = 0;
                for (int i = 0; i < ballots.Length; i++)
                {
                    if (ballots[i] != null && ballots[i].Choice == choice)
                    {
                        voters.Add("E" + i);
                        sum += ballots[i].Sway;
                    }
                }
                return $"{label} {DossierBuilder.N(sum)}{(voters.Count > 0 ? " (" + string.Join(", ", voters) + ")" : string.Empty)}";
            }
            return Side(first, firstLabel) + " × " + Side(second, secondLabel);
        }

        private static string Missing(WorldCapture capture, CongressBallot[] ballots, CapturedCongress congress, int self)
        {
            var missing = new List<string>();
            foreach (int empire in capture.AliveEmpires())
            {
                if (empire < ballots.Length && (ballots[empire] == null || ballots[empire].Choice < 0))
                {
                    double sway = empire < congress.Sway.Length ? congress.Sway[empire] : 0;
                    missing.Add($"{(empire == self ? "você" : "E" + empire)} ({DossierBuilder.N(sway)})");
                }
            }
            return missing.Count > 0 ? " Ainda não votaram: " + string.Join(", ", missing) + "." : " Todos já votaram.";
        }

        private static void AppendCrises(StringBuilder text, WorldCapture capture, CapturedCongress congress, CapturedEmpire me, ValidationContext context)
        {
            int self = me.Index;
            List<CongressCrisis> crises = congress.Crises.Where(c => c.Active || c.WaitingLoser).ToList();
            if (crises.Count == 0)
            {
                return;
            }
            text.AppendLine("Crises no Congresso (disputas de exigências que uma nação levou ao voto do mundo):");
            foreach (CongressCrisis crisis in crises)
            {
                string code = "K" + crisis.Pool;
                string declarator = crisis.Declarator == self ? "você" : $"E{crisis.Declarator} {DossierBuilder.Name(capture, crisis.Declarator)}";
                string target = crisis.Target == self ? "você" : $"E{crisis.Target} {DossierBuilder.Name(capture, crisis.Target)}";
                bool mineCrisis = crisis.Declarator == self || crisis.Target == self;
                if (!crisis.Active)
                {
                    string winner = crisis.Winner == self ? "você" : "E" + crisis.Winner;
                    string loser = crisis.Loser == self ? "você precisa" : $"E{crisis.Loser} precisa";
                    text.AppendLine($"- {code}: {declarator} contra {target} — ENCERRADA: venceu {winner}; {loser} cumprir as exigências ou declarar guerra surpresa.");
                    continue;
                }
                CongressBallot mine = self < crisis.Ballots.Length ? crisis.Ballots[self] : null;
                bool voted = mine != null && mine.Choice >= 0;
                context.CrisisVotes[code] = new CrisisVoteRef { Pool = crisis.Pool, Declarator = crisis.Declarator, Target = crisis.Target, Voted = voted };
                int left = crisis.TurnEnd - 1 - capture.Turn;
                text.AppendLine($"- {code}{(mineCrisis ? " (SUA CRISE)" : string.Empty)}: {declarator} contra {target}. Fecha na virada para o turno {crisis.TurnEnd} (último turno para votar: {crisis.TurnEnd - 1}{(left > 0 ? $", faltam {left}" : ", É ESTE")}).");
                text.AppendLine($"   Placar: {Score(crisis.Ballots, crisis.Declarator, "E" + crisis.Declarator, crisis.Target, "E" + crisis.Target)}. Empate ou falta de votos favorecem E{crisis.Target}, o alvo.{Missing(capture, crisis.Ballots, congress, self)}");
                text.AppendLine($"   Se E{crisis.Declarator} vencer, E{crisis.Target} terá de cumprir as exigências dele ou declarar guerra surpresa. Se E{crisis.Target} vencer, as exigências de E{crisis.Declarator} caem.");
                if (mineCrisis)
                {
                    text.AppendLine("   Enquanto a votação corre, ninguém aceita, recusa ou retira exigências entre vocês, e guerra formal está proibida.");
                }
                if (voted)
                {
                    text.AppendLine($"   Você já apoiou E{mine.Choice} com peso {DossierBuilder.N(mine.Sway)}.");
                    continue;
                }
                if (context.NoSway && (mine?.BribeBonus ?? 0) <= 0 && !(HasBribes(mine) && context.HasConsulate))
                {
                    text.AppendLine("   Você não vota nesta crise: seu peso no Congresso é zero.");
                    continue;
                }
                text.AppendLine($"   Votar: votar_congresso {{\"votacao\":\"{code}\",\"apoiar\":\"E{crisis.Declarator}|E{crisis.Target}|abster\"}}. Quem você apoiar lembra por muito tempo; o outro lado também."
                    + (mineCrisis ? " VOCÊ AINDA NÃO VOTOU: na sua própria crise, o seu peso só conta se você votar em você." : string.Empty));
                AppendBribes(text, mine, code, context);
            }
        }

        private static void AppendVerdicts(StringBuilder text, WorldCapture capture, CapturedEmpire me, ValidationContext context)
        {
            int waitTurns = Math.Max(1, IaConfig.ProposalTurns.Value);
            foreach (CapturedRelation relation in me.Relations)
            {
                if (relation == null || relation.CongressWinner < 0)
                {
                    continue;
                }
                int other = relation.Other;
                string them = $"E{other} {DossierBuilder.Name(capture, other)}";
                if (relation.CongressWinner == me.Index)
                {
                    text.AppendLine($"O Congresso decidiu A SEU FAVOR na disputa com {them}: eles têm de cumprir as suas exigências ou declarar guerra surpresa. Esperando a resposta deles.");
                    continue;
                }
                context.VerdictAgainstMe.Add(other);
                var blocked = new List<string>();
                if (relation.VerdictAcceptBlocked != null) blocked.Add("cumprir indisponível agora: " + relation.VerdictAcceptBlocked);
                if (relation.VerdictWarBlocked != null) blocked.Add("guerra indisponível agora: " + relation.VerdictWarBlocked);
                text.AppendLine($"O CONGRESSO DECIDIU CONTRA VOCÊ na disputa com {them}: cumpra as exigências deles (veja a seção da nação) com responder_congresso {{\"nacao\":\"E{other}\",\"resposta\":\"cumprir\"}},"
                    + $" ou declare guerra surpresa ({{\"resposta\":\"guerra\"}}; guerra não sancionada: alavancagem e reclamação contra você para as nações que não são suas aliadas)."
                    + (blocked.Count > 0 ? " [" + string.Join("; ", blocked) + "]" : string.Empty)
                    + $" Até você responder, a disputa fica parada. Se não responder em {waitTurns} turno(s), seus diplomatas decidem.");
            }
        }

        private static void AppendImposedLaw(StringBuilder text, IaWorld world, CapturedCongress congress, CapturedEmpire me)
        {
            CongressLawVote last = congress.LawHistory.Count > 0 ? congress.LawHistory[0] : null;
            foreach (CongressImposedLaw law in congress.ImposedLaws.Where(l => l.Empire == me.Index))
            {
                IaNation nation = world.Get(me.Index);
                string voted = null;
                string stance = null;
                nation?.CongressVotes.TryGetValue(law.Civic, out voted);
                nation?.CongressStances.TryGetValue(law.Civic, out stance);
                bool votedWinner = voted == law.Choice
                    || (last != null && last.Law.Civic == law.Civic && last.Winner >= 0 && me.Index < last.Ballots.Length && last.Ballots[me.Index]?.Choice == last.Winner);
                string what = votedWinner || stance == "adotar" ? "adotam (você votou nela ou escolheu adotar)"
                    : stance == "recusar" ? (law.CanDiscard ? $"recusam, pagando {DossierBuilder.N(law.DiscardCost)} de influência (a sua postura)" : "adotam: falta influência para recusar")
                    : $"decidem pelo que for melhor (recusar custa {DossierBuilder.N(law.DiscardCost)} de influência)";
                text.AppendLine($"Lei imposta pelo Congresso a você: {CongressFlow.LawLabel(law.Civic)} → {Choice(law.Choice)}. Seus governadores {what} neste turno.");
            }
        }

        private static void AppendConsensus(StringBuilder text, CapturedCongress congress, CapturedEmpire me, ValidationContext context)
        {
            if (congress.Consensus.Count == 0)
            {
                return;
            }
            text.AppendLine("Consenso ideológico (o mundo destrava eixos pagando influência; o efeito vem da média ideológica de todas as nações, não de voto):");
            foreach (CongressAxis axis in congress.Consensus)
            {
                string code = "I" + axis.Index;
                string name = "«" + (GameGlossary.Title(axis.Definition) ?? axis.Definition) + "»";
                if (axis.Unlocked)
                {
                    string orientation = axis.Orientation != null ? " → «" + (GameGlossary.Title(axis.Orientation) ?? axis.Orientation) + "»" : string.Empty;
                    text.AppendLine($"- {code} {name}: em vigor desde o turno {axis.UnlockTurn}{orientation}.");
                    continue;
                }
                double cost = me.Index < axis.Cost.Length ? axis.Cost[me.Index] : -1;
                int mine = me.Index < axis.Contributions.Length ? axis.Contributions[me.Index] : 0;
                bool affordable = cost < 0 || me.Influence >= cost;
                if (affordable)
                {
                    context.ConsensusAxes[code] = axis.Index;
                }
                else
                {
                    context.UnaffordableAxes.Add(code);
                }
                text.AppendLine($"- {code} {name}: travado — {axis.Count} de {axis.Required} contribuições"
                    + (cost >= 0 ? $"; contribuir custa a você {DossierBuilder.N(cost)} de influência" + (affordable ? string.Empty : $" (você tem {DossierBuilder.N(me.Influence)}: ainda não dá)") : string.Empty)
                    + (mine > 0 ? $" (você já contribuiu {mine} vez(es))" : string.Empty) + ".");
            }
        }

        private static void AppendNews(StringBuilder text, IaWorld world, WorldCapture capture)
        {
            List<CongressNews> news = world.CongressNews.Where(n => capture.Turn - n.Turn <= 10).ToList();
            if (news.Count == 0)
            {
                return;
            }
            var items = new List<string>();
            foreach (CongressNews item in news.Skip(Math.Max(0, news.Count - 4)))
            {
                if (item.Kind == "lei")
                {
                    double a = 0, b = 0;
                    var votersA = new List<string>();
                    var votersB = new List<string>();
                    for (int i = 0; i < item.Votes.Count; i++)
                    {
                        if (item.Votes[i] == 0) { a += item.Sway[i]; votersA.Add("E" + i); }
                        else if (item.Votes[i] == 1) { b += item.Sway[i]; votersB.Add("E" + i); }
                    }
                    string result = item.Winner == 0 ? $"venceu A {Choice(item.ChoiceA)}" : item.Winner == 1 ? $"venceu B {Choice(item.ChoiceB)}" : "empate, nada mudou";
                    items.Add($"turno {item.Turn}, lei {CongressFlow.LawLabel(item.Civic)}: {result}, {DossierBuilder.N(a)} × {DossierBuilder.N(b)} (A: {(votersA.Count > 0 ? string.Join(", ", votersA) : "ninguém")}; B: {(votersB.Count > 0 ? string.Join(", ", votersB) : "ninguém")})");
                }
                else
                {
                    var forDeclarator = new List<string>();
                    var forTarget = new List<string>();
                    for (int i = 0; i < item.Votes.Count; i++)
                    {
                        if (item.Votes[i] == item.Declarator) forDeclarator.Add("E" + i);
                        else if (item.Votes[i] == item.Target) forTarget.Add("E" + i);
                    }
                    string result = item.Winner >= 0 ? $"venceu E{item.Winner}" : "cancelada (guerra ou vassalagem)";
                    items.Add($"turno {item.Turn}, crise E{item.Declarator} × E{item.Target}: {result} (apoiaram E{item.Declarator}: {(forDeclarator.Count > 0 ? string.Join(", ", forDeclarator) : "ninguém")}; E{item.Target}: {(forTarget.Count > 0 ? string.Join(", ", forTarget) : "ninguém")})");
                }
            }
            text.AppendLine("Notícias do Congresso: " + string.Join(". ", items) + ".");
        }
    }
}
