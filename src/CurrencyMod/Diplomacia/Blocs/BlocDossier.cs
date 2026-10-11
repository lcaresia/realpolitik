using System;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacia.Capture;

namespace CurrencyMod.Diplomacia.Blocs
{
    /// <summary>Seção BLOCOS do dossiê: o bloco da nação (pautas, eleição, tarifa), convites e os outros blocos.</summary>
    internal static class BlocDossier
    {
        internal static void Append(StringBuilder text, IaWorld world, int self, int turn, ValidationContext context, Func<int, string> name)
        {
            Bloc mine = BlocEngine.Of(world, self);
            var invites = BlocEngine.InvitesFor(world, self).ToList();
            var others = world.Blocs.Where(b => b != mine && b.Formed).ToList();
            if (mine != null)
            {
                context.BlocId = mine.Id;
                context.BlocElection = mine.ElectionOpened >= 0;
                foreach (BlocMotion motion in mine.Motions.Where(m => m.Status == BlocMotion.Voting || m.Status == BlocMotion.Suggested))
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
            text.AppendLine("Benefícios entre membros: comércio sem pedágio nem bloqueio e manutenção das rotas 25% mais barata; quem de fora declarar guerra a um membro dá aos outros membros um motivo de guerra contra ele (ninguém é obrigado a lutar); o bloco pode votar junto no Congresso e impor uma tarifa comum contra quem é de fora. O presidente (mandato de 20 turnos) põe pautas em votação; os outros sugerem. Maioria simples; expulsar exige 2/3.");
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
                if (mine.Tariffs.Count > 0)
                {
                    text.AppendLine("  Tarifa comum: " + string.Join(", ", mine.Tariffs.Select(t => $"{(t.Mode == "bloqueio" ? "bloqueio" : "pedágio")} contra {Who(t.Target)}")));
                }
                if (mine.CongressLine != null)
                {
                    text.AppendLine($"  Posição do bloco no Congresso: {mine.CongressLine.Choice} (os membros votam juntos).");
                }
                foreach (BlocMotion motion in mine.Motions.Where(m => m.Status == BlocMotion.Voting))
                {
                    string myVote = motion.Votes.TryGetValue(self, out bool yes) ? (yes ? "sim" : "não") : "ainda não votou (votar_pauta)";
                    text.AppendLine($"  EM VOTAÇÃO P{motion.Id}: {Describe(motion, Who)} · proposta por {Who(motion.Author)} · sim {motion.Yes}, não {motion.No} · fecha no turno {motion.Deadline} · seu voto: {myVote}");
                }
                foreach (BlocMotion motion in mine.Suggestions)
                {
                    text.AppendLine($"  SUGESTÃO P{motion.Id}: {Describe(motion, Who)} · de {Who(motion.Author)}{(president ? " · você é o presidente: abrir_pauta para pôr em votação ou descartar" : " · aguarda o presidente")}");
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
                text.AppendLine($"Outro bloco: {bloc.Handle} \"{bloc.Name}\" · membros: {string.Join(", ", bloc.Members.Select(Who))} · presidente {Who(bloc.President)}"
                    + (bloc.Tariffs.Any(t => t.Target == self) ? " · TEM TARIFA COMUM CONTRA VOCÊ" : string.Empty));
            }
            text.AppendLine();
        }

        internal static string Describe(BlocMotion motion, Func<int, string> who)
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

        private static string Why(BlocMotion motion) => string.IsNullOrEmpty(motion.Reason) ? string.Empty : $" (motivo: {motion.Reason})";

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
