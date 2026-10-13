using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy.Blocs
{
    /// <summary>BLOCOS section of the dossier: the nation's bloc (motions, election, resolutions and the commission's audits), invites and the other blocs.</summary>
    internal static class BlocDossier
    {
        internal static void Append(StringBuilder text, AiWorld world, int self, int turn, ValidationContext context, Func<int, string> name)
        {
            Bloc mine = BlocEngine.Of(world, self);
            var invites = BlocEngine.InvitesFor(world, self).ToList();
            var others = world.Blocs.Where(b => b != mine && b.Formed).ToList();
            if (mine != null)
            {
                context.BlocId = mine.Id;
                context.BlocElection = mine.ElectionOpened >= 0;
                foreach (BlocMotion motion in mine.Motions.Where(m => m.Status == BlocMotion.Voting || m.Status == BlocMotion.Suggested || m.Status == BlocMotion.Tiebreak))
                {
                    context.BlocMotions.Add("P" + motion.Id);
                }
            }
            foreach (Bloc bloc in invites)
            {
                context.BlocInvites.Add(bloc.Handle);
            }
            if (mine == null && invites.Count == 0 && others.Count == 0)
            {
                return;
            }
            string Who(int e) => e == self ? "você" : $"{name(e)} (E{e})";

            text.AppendLine("== BLOCOS (alianças de várias nações, tipo OTAN; um bloco por nação) ==");
            text.AppendLine("Benefícios entre membros: comércio sem pedágio nem bloqueio e manutenção das rotas 25% mais barata; quem de fora declarar guerra a um membro dá aos outros membros um motivo de guerra contra ele (ninguém é obrigado a lutar). "
                + "PAUTAS SÃO TEXTO LIVRE (propose_motion, até 500 caracteres, de graça): qualquer coisa que sirva aos seus interesses (sanção comercial ou bloqueio contra um rival, votar junto no Congresso, regra interna, defesa conjunta, condenar ou punir um membro, expulsar, convidar, mudar o nome). "
                + "O presidente (mandato de 20 turnos) põe em votação; os outros sugerem, e a sugestão vence em 5 turnos sem decisão. A votação dura 2 turnos; maioria simples. "
                + "O PRESIDENTE NÃO VOTA: ele escolhe o que vai a votação e DESEMPATA (empate, ou expulsão com maioria mas sem 2/3: a decisão é dele, em até 2 turnos, com votar_pauta). "
                + "Expulsar um membro é uma pauta com \"expel\" (propose_motion): passa com 2/3 dos votos e o expulso sai do bloco. "
                + "Cada membro tem 3 PAUTAS ATIVAS no máximo (sugeridas, em votação ou resoluções suas em vigor): mantenha as suas 3 ocupadas com o que mais te interessa. "
                + "Aprovada, a pauta vira RESOLUÇÃO por 10 turnos (depois vence e precisa ser proposta de novo) e obriga todos os membros, inclusive quem votou contra. "
                + "NINGUÉM FISCALIZA POR VOCÊ: o mod não interpreta, não audita e não aplica resolução nenhuma. Cada membro cumpre com as próprias ações (pedágio, bloqueio, guerra, voto, convite) e observa os outros. "
                + "Se te convier, registre acusações no LIVRO DO BLOCO (accuse_breach, com a evidência que você diz ter visto): verdadeiras, exageradas ou inventadas; todos os membros leem e cada um decide se acredita, cobra, pune ou ignora. Também é jogada deixar passar o deslize de um aliado, ou combinar em particular que alguém feche os olhos para o seu.");
            if (mine != null)
            {
                bool president = mine.President == self;
                text.AppendLine($"SEU BLOCO: {mine.Handle} \"{mine.Name}\" · {(mine.Formed ? "formado no turno " + mine.FormedTurn : "em formação (precisa de 3 membros)")} · membros: {string.Join(", ", mine.Members.Select(Who))}");
                text.AppendLine($"  Presidente: {Who(mine.President)}, mandato até o turno {mine.TermEnds}." + (mine.ElectionOpened >= 0
                    ? $" ELEIÇÃO ABERTA até o turno {mine.ElectionOpened + Bloc.VoteTurns}: vote com votar_presidente ({(mine.ElectionVotes.TryGetValue(self, out int chosen) ? "seu voto: E" + chosen : "você ainda não votou")})."
                    : string.Empty));
                if (mine.Invites.Count > 0)
                {
                    text.AppendLine("  Convites pendentes: " + string.Join(", ", mine.Invites.Select(i => $"{Who(i.Empire)} até o turno {i.Turn + Bloc.InviteTurns}")));
                }
                foreach (BlocResolution resolution in mine.InForce)
                {
                    text.AppendLine($"  RESOLUÇÃO EM VIGOR {resolution.Handle} (de {Who(resolution.Author)}, turno {resolution.PassedTurn}, vence no turno {resolution.Expires}): «{InjectionGuard.Inline(resolution.Title)}» — {InjectionGuard.Inline(resolution.Text)}");
                    List<string> against = mine.VotesOf(resolution).Where(v => !v.Value).Select(v => Who(v.Key)).ToList();
                    text.AppendLine($"    Votação: {resolution.Yes} sim, {resolution.No} não" + (against.Count > 0 ? $" (contra: {string.Join(", ", against)})" : string.Empty) + (resolution.ByPresident ? "; desempatada pelo presidente" : string.Empty) + ".");
                    foreach (BlocAccusation accusation in mine.Accusations.Where(a => a.Resolution == resolution.Id))
                    {
                        text.AppendLine($"    Livro: no turno {accusation.Turn}, {Who(accusation.Accuser)} acusou {Who(accusation.Accused)}: {InjectionGuard.Inline(accusation.Evidence)}");
                    }
                }
                foreach (BlocAccusation accusation in mine.Accusations.Where(a => a.Resolution < 0 && turn - a.Turn <= 15))
                {
                    text.AppendLine($"  Livro: no turno {accusation.Turn}, {Who(accusation.Accuser)} acusou {Who(accusation.Accused)} (conduta no bloco): {InjectionGuard.Inline(accusation.Evidence)}");
                }
                int free = Bloc.MotionSlots - mine.SlotsUsed(self);
                text.AppendLine(free > 0 ? $"  Suas pautas ativas: {Bloc.MotionSlots - free} de {Bloc.MotionSlots}. Você tem {free} vaga(s): use-as (propose_motion)." : $"  Suas pautas ativas: {Bloc.MotionSlots} de {Bloc.MotionSlots} (todas ocupadas).");
                foreach (BlocMotion motion in mine.Motions.Where(m => m.Status == BlocMotion.Tiebreak))
                {
                    text.AppendLine($"  AGUARDANDO O DESEMPATE DO PRESIDENTE P{motion.Id}: {Describe(motion)} · sim {motion.Yes}, não {motion.No}"
                        + (president ? $" · VOCÊ decide até o turno {motion.TiebreakTurn + BlocMotion.TiebreakTurns} (votar_pauta: sim aprova, não rejeita)" : $" · {Who(mine.President)} decide"));
                }
                foreach (BlocMotion motion in mine.Motions.Where(m => m.Status == BlocMotion.Voting))
                {
                    string myVote = president ? "você preside: não vota (desempata se der empate)"
                        : motion.Expel == self ? "você é o alvo: não vota"
                        : motion.Votes.TryGetValue(self, out bool yes) ? (yes ? "sim" : "não") : "ainda não votou (votar_pauta)";
                    text.AppendLine($"  EM VOTAÇÃO P{motion.Id}: {Describe(motion)} · proposta por {Who(motion.Author)} · sim {motion.Yes}, não {motion.No} · fecha no turno {motion.Deadline} · seu voto: {myVote}");
                }
                foreach (BlocMotion motion in mine.Suggestions)
                {
                    text.AppendLine($"  SUGESTÃO P{motion.Id}: {Describe(motion)} · de {Who(motion.Author)} · vence no turno {motion.SuggestedTurn + Bloc.SuggestionTurns}{(president ? " · você é o presidente: abrir_pauta para pôr em votação ou descartar" : " · aguarda o presidente")}");
                }
                var recent = mine.History.Where(e => turn - e.Turn <= 10).Skip(Math.Max(0, mine.History.Count(e => turn - e.Turn <= 10) - 5));
                foreach (BlocEvent ev in recent)
                {
                    text.AppendLine($"  T{ev.Turn}: {Event(ev, Who)}");
                }
            }
            else
            {
                int wait = BlocEngine.CooldownLeft(world, self, turn);
                if (wait > 0)
                {
                    text.AppendLine($"Você saiu de um bloco há pouco: pode entrar em outro daqui a {wait} turnos.");
                }
            }
            foreach (Bloc bloc in invites)
            {
                BlocInvite invite = bloc.InviteFor(self);
                text.AppendLine($"CONVITE PARA VOCÊ: {bloc.Handle} \"{bloc.Name}\" de {Who(invite.From)} · membros: {string.Join(", ", bloc.Members.Select(Who))} · responda até o turno {invite.Turn + Bloc.InviteTurns} (responder_bloco)");
            }
            foreach (Bloc bloc in others)
            {
                text.AppendLine($"Outro bloco: {bloc.Handle} \"{bloc.Name}\" · membros: {string.Join(", ", bloc.Members.Select(Who))} · presidente {Who(bloc.President)}");
                foreach (BlocResolution resolution in bloc.InForce.Where(r => (r.Text ?? string.Empty).Contains("E" + self) || (r.Title ?? string.Empty).Contains("E" + self)))
                {
                    text.AppendLine($"  RESOLUÇÃO DELES QUE CITA VOCÊ ({resolution.Handle}): «{InjectionGuard.Inline(resolution.Title)}» — {InjectionGuard.Inline(resolution.Text)}");
                }
            }
            text.AppendLine();
        }

        /// <summary>
        /// CONGRESSO MUNDIAL — PAUTAS LIVRES (melhorias item 40): the same free motions as a bloc, with every nation of the
        /// Congress; approved motions are only recorded.
        /// </summary>
        internal static void AppendCongress(StringBuilder text, AiWorld world, int self, int turn, ValidationContext context, Func<int, string> name)
        {
            Bloc forum = world.CongressForum;
            if (forum == null || !forum.Has(self))
            {
                return;
            }
            context.CongressForum = true;
            foreach (BlocMotion motion in forum.Motions.Where(m => m.Status == BlocMotion.Voting || m.Status == BlocMotion.Suggested || m.Status == BlocMotion.Tiebreak))
            {
                context.BlocMotions.Add("P" + motion.Id);
            }
            string Who(int e) => e == self ? "você" : $"{name(e)} (E{e})";
            bool president = forum.President == self;
            text.AppendLine("== CONGRESSO MUNDIAL — PAUTAS LIVRES (todas as nações do Congresso) ==");
            text.AppendLine("Além das leis do jogo, qualquer nação propõe ao Congresso uma pauta em texto livre (propose_motion com \"forum\": \"congress\", até 500 caracteres, de graça): condenar alguém, pedir sanção, proteger uma rota, reconhecer um povo, propor uma regra para o mundo. "
                + "Mesmas regras de um bloco: quem preside a sessão do Congresso põe em votação (os outros sugerem; a sugestão vence em 5 turnos), a votação dura 2 turnos com 1 voto por nação, maioria simples, e o presidente não vota mas desempata. "
                + "Cada nação tem 3 pautas ativas no Congresso. Aprovada, vira RESOLUÇÃO por 10 turnos. "
                + "O MOD NÃO APLICA NADA: a resolução só fica registrada. Cumprir ou não é escolha de cada nação; descumprir uma resolução do mundo é muito mal visto, como no direito internacional. "
                + "Quem quiser registra a acusação no livro do Congresso (accuse_breach com \"forum\": \"congress\"): todas as nações leem e decidem se acreditam.");
            text.AppendLine($"  Preside: {Who(forum.President)}.");
            foreach (BlocResolution resolution in forum.InForce)
            {
                text.AppendLine($"  RESOLUÇÃO DO CONGRESSO {resolution.Handle} (de {Who(resolution.Author)}, turno {resolution.PassedTurn}, vence no turno {resolution.Expires}): «{InjectionGuard.Inline(resolution.Title)}» — {InjectionGuard.Inline(resolution.Text)}");
                List<string> against = forum.VotesOf(resolution).Where(v => !v.Value).Select(v => Who(v.Key)).ToList();
                text.AppendLine($"    Votação: {resolution.Yes} sim, {resolution.No} não" + (against.Count > 0 ? $" (contra: {string.Join(", ", against)})" : string.Empty) + (resolution.ByPresident ? "; desempatada pelo presidente" : string.Empty) + ".");
                foreach (BlocAccusation accusation in forum.Accusations.Where(a => a.Resolution == resolution.Id))
                {
                    text.AppendLine($"    Livro: no turno {accusation.Turn}, {Who(accusation.Accuser)} acusou {Who(accusation.Accused)}: {InjectionGuard.Inline(accusation.Evidence)}");
                }
            }
            foreach (BlocAccusation accusation in forum.Accusations.Where(a => a.Resolution < 0 && turn - a.Turn <= 15))
            {
                text.AppendLine($"  Livro: no turno {accusation.Turn}, {Who(accusation.Accuser)} acusou {Who(accusation.Accused)}: {InjectionGuard.Inline(accusation.Evidence)}");
            }
            int free = Bloc.MotionSlots - forum.SlotsUsed(self);
            text.AppendLine($"  Suas pautas ativas no Congresso: {Bloc.MotionSlots - free} de {Bloc.MotionSlots}.");
            foreach (BlocMotion motion in forum.Motions.Where(m => m.Status == BlocMotion.Tiebreak))
            {
                text.AppendLine($"  AGUARDANDO O DESEMPATE P{motion.Id}: {Describe(motion)} · sim {motion.Yes}, não {motion.No}"
                    + (president ? $" · VOCÊ decide até o turno {motion.TiebreakTurn + BlocMotion.TiebreakTurns} (votar_pauta: sim aprova, não rejeita)" : $" · {Who(forum.President)} decide"));
            }
            foreach (BlocMotion motion in forum.Motions.Where(m => m.Status == BlocMotion.Voting))
            {
                string myVote = president ? "você preside: não vota (desempata se der empate)"
                    : motion.Votes.TryGetValue(self, out bool yes) ? (yes ? "sim" : "não") : "ainda não votou (votar_pauta)";
                text.AppendLine($"  EM VOTAÇÃO P{motion.Id}: {Describe(motion)} · proposta por {Who(motion.Author)} · sim {motion.Yes}, não {motion.No} · fecha no turno {motion.Deadline} · seu voto: {myVote}");
            }
            foreach (BlocMotion motion in forum.Suggestions)
            {
                text.AppendLine($"  SUGESTÃO P{motion.Id}: {Describe(motion)} · de {Who(motion.Author)} · vence no turno {motion.SuggestedTurn + Bloc.SuggestionTurns}{(president ? " · você preside: abrir_pauta para pôr em votação ou descartar" : " · aguarda quem preside")}");
            }
            text.AppendLine();
        }

        /// <summary>A free motion: «title» — text.</summary>
        internal static string Describe(BlocMotion motion)
        {
            string expel = motion.Expel >= 0 ? $" [EXPULSÃO de E{motion.Expel}: precisa de 2/3]" : string.Empty;
            return motion.Kind == BlocMotion.FreeKind || motion.Kind == null
                ? $"«{InjectionGuard.Inline(motion.Title ?? string.Empty)}»{expel} — {InjectionGuard.Inline(motion.Text ?? string.Empty)}"
                : DescribeLegacy(motion, e => "E" + e);
        }

        /// <summary>A typed motion from an old save.</summary>
        internal static string DescribeLegacy(BlocMotion motion, Func<int, string> who)
        {
            switch (motion.Kind)
            {
                case "tarifa": return $"tarifa comum ({motion.Arg}) contra {who(motion.Target)}" + Why(motion);
                case "fim_tarifa": return $"tirar a tarifa comum contra {who(motion.Target)}" + Why(motion);
                case "admitir": return $"convidar {who(motion.Target)} para o bloco" + Why(motion);
                case "expulsar": return $"expulsar {who(motion.Target)} (exige 2/3)" + Why(motion);
                case "congresso": return $"o bloco vota junto no Congresso: {motion.Arg}" + Why(motion);
                case "renomear": return $"renomear o bloco para \"{motion.Arg}\"" + Why(motion);
                case "dissolver": return "desfazer o bloco" + Why(motion);
            }
            return motion.Kind;
        }

        private static string Why(BlocMotion motion) => string.IsNullOrEmpty(motion.Reason) ? string.Empty : $" (motivo: {InjectionGuard.Inline(motion.Reason)})";

        private static string Event(BlocEvent ev, Func<int, string> who)
        {
            switch (ev.Kind)
            {
                case "entrou": return $"{who(ev.Actor)} entrou";
                case "saiu": return $"{who(ev.Actor)} saiu";
                case "expulso": return $"{who(ev.Actor)} foi expulso";
                case "recusou": return $"{who(ev.Actor)} recusou o convite";
                case "formado": return "o bloco se formou";
                case "presidente": return $"{who(ev.Actor)} é o presidente ({ev.Arg})";
                case "eleicao": return "eleição de presidente aberta";
                case "aprovada": return $"aprovada: {ev.Arg}" + (ev.Target >= 0 ? $" ({who(ev.Target)})" : string.Empty);
                case "rejeitada": return $"rejeitada: {ev.Arg}" + (ev.Target >= 0 ? $" ({who(ev.Target)})" : string.Empty);
                case "pauta": return $"{who(ev.Actor)} pôs em votação: {ev.Arg}" + (ev.Target >= 0 ? $" ({who(ev.Target)})" : string.Empty);
                case "sugeriu": return $"{who(ev.Actor)} sugeriu: {ev.Arg}" + (ev.Target >= 0 ? $" ({who(ev.Target)})" : string.Empty);
                case "expirou": return $"a sugestão de {who(ev.Actor)} venceu sem decisão do presidente: {ev.Arg}";
                case "acusou": return $"{who(ev.Actor)} acusou {who(ev.Target)} de descumprir" + (ev.Arg != null ? $": {ev.Arg}" : " o compromisso do bloco");
                case "revogada": return $"resolução revogada: {ev.Arg}";
                case "resolucao_venceu": return $"resolução de {who(ev.Actor)} venceu (10 turnos): {ev.Arg}";
                case "desempate": return $"empate (ou expulsão sem 2/3) em «{ev.Arg}»: a decisão é do presidente {who(ev.Actor)}";
                case "descartada": return $"{who(ev.Actor)} descartou a sugestão: {ev.Arg}";
                case "fundou": return $"{who(ev.Actor)} fundou o bloco";
                case "convidou": return $"{who(ev.Target)} foi convidado";
                case "convite_venceu": return $"o convite a {who(ev.Target)} venceu";
                case "renomeado": return $"o bloco mudou de nome (antes: {ev.Arg})";
                case "defesa": return $"{who(ev.Actor)} declarou guerra a {who(ev.Target)}; os membros em paz com o agressor ganharam motivo de guerra";
                default: return ev.Kind + (ev.Actor >= 0 ? " " + who(ev.Actor) : string.Empty) + (ev.Target >= 0 ? " → " + who(ev.Target) : string.Empty);
            }
        }
    }
}
