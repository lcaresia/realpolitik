using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CurrencyMod.Diplomacia.Blocs
{
    /// <summary>
    /// Bloco de nações (melhorias-jogando item 16, "tipo OTAN, BRICS"). Nasce com 3 membros depois dos convites,
    /// tem presidente com mandato, pautas votadas e quatro benefícios: comércio interno livre, defesa mútua, voz
    /// conjunta no Congresso e tarifa comum. A tela chama "Alianças"; a entidade é "bloco" para não confundir com o
    /// acordo de Aliança do jogo.
    /// </summary>
    internal sealed class Bloc
    {
        public const int MinMembers = 3;
        public const int MaxMembers = 10;
        /// <summary>Turnos que um convite fica aberto.</summary>
        public const int InviteTurns = 5;
        /// <summary>Turnos que uma pauta ou eleição fica em votação.</summary>
        public const int VoteTurns = 3;
        public const int TermTurns = 20;
        /// <summary>Turnos de espera depois de sair (ou ser expulso) para entrar em outro bloco.</summary>
        public const int LeaveCooldown = 10;

        public int Id;
        public string Name;
        public int Founder;
        public int CreatedTurn;
        /// <summary>Turno em que o bloco atingiu 3 membros; -1 = ainda em formação (só convites).</summary>
        public int FormedTurn = -1;
        public List<int> Members = new List<int>();
        public int President = -1;
        public int TermStart;
        /// <summary>Grupo de conversa dos membros (ChatGroup), criado junto com o bloco.</summary>
        public int GroupId = -1;
        public List<BlocInvite> Invites = new List<BlocInvite>();
        public List<BlocMotion> Motions = new List<BlocMotion>();
        public List<BlocEvent> History = new List<BlocEvent>();
        /// <summary>Tarifa comum: pedágio ou bloqueio contra uma nação de fora em todos os postos dos membros.</summary>
        public List<BlocTariff> Tariffs = new List<BlocTariff>();
        /// <summary>Posição do bloco no Congresso, votada em pauta: todos os membros votam juntos.</summary>
        public BlocCongressLine CongressLine;
        /// <summary>Eleição aberta: turno de abertura (-1 = nenhuma) e votos (eleitor → candidato).</summary>
        public int ElectionOpened = -1;
        public Dictionary<int, int> ElectionVotes = new Dictionary<int, int>();

        [JsonIgnore]
        public bool Formed => FormedTurn >= 0;

        [JsonIgnore]
        public string Handle => "BLC" + Id;

        public bool Has(int empire) => Members.Contains(empire);

        public BlocInvite InviteFor(int empire) => Invites.FirstOrDefault(i => i.Empire == empire);

        public BlocMotion MotionBy(int id) => Motions.FirstOrDefault(m => m.Id == id);

        public IEnumerable<BlocMotion> OpenMotions => Motions.Where(m => m.Status == BlocMotion.Voting);

        public IEnumerable<BlocMotion> Suggestions => Motions.Where(m => m.Status == BlocMotion.Suggested);

        public int TermEnds => TermStart + TermTurns;

        public void Log(int turn, string kind, int actor = -1, int target = -1, string arg = null)
        {
            History.Add(new BlocEvent { Turn = turn, Kind = kind, Actor = actor, Target = target, Arg = arg });
            if (History.Count > 60)
            {
                History.RemoveRange(0, History.Count - 60);
            }
        }
    }

    internal sealed class BlocInvite
    {
        public int Empire;
        public int From;
        public int Turn;
    }

    /// <summary>
    /// Pauta do bloco. Membro comum sugere (Suggested); só o presidente põe em votação (Voting). Fecha quando todos
    /// votam ou o prazo acaba: maioria simples dos votos (empate: vale o voto do presidente); expulsão exige 2/3 dos
    /// membros que podem votar.
    /// </summary>
    internal sealed class BlocMotion
    {
        public const string Suggested = "sugerida";
        public const string Voting = "votacao";
        public const string Passed = "aprovada";
        public const string Failed = "rejeitada";
        public const string Dropped = "descartada";

        /// <summary>Tipos de pauta.</summary>
        public static readonly string[] Kinds = { "tarifa", "fim_tarifa", "admitir", "expulsar", "congresso", "renomear", "dissolver" };

        public int Id;
        public string Kind;
        /// <summary>Nação alvo (tarifa, fim_tarifa, admitir, expulsar); -1 nas outras.</summary>
        public int Target = -1;
        /// <summary>Tarifa: "pedagio" ou "bloqueio". Congresso: a posição ("a", "b" ou a nação apoiada "E#"). Renomear: o nome.</summary>
        public string Arg;
        public string Reason;
        public int Author;
        public int SuggestedTurn;
        public int OpenedTurn = -1;
        public int ClosedTurn = -1;
        public string Status = Suggested;
        public Dictionary<int, bool> Votes = new Dictionary<int, bool>();

        [JsonIgnore]
        public int Deadline => OpenedTurn + Bloc.VoteTurns;

        public int Yes => Votes.Count(v => v.Value);

        public int No => Votes.Count(v => !v.Value);
    }

    internal sealed class BlocEvent
    {
        public int Turn;
        /// <summary>fundou, convidou, entrou, recusou, saiu, expulso, presidente, pauta, aprovada, rejeitada,
        /// tarifa, fim_tarifa, renomeado, desfeito, formado, falhou.</summary>
        public string Kind;
        public int Actor = -1;
        public int Target = -1;
        public string Arg;
    }

    internal sealed class BlocTariff
    {
        public int Target;
        /// <summary>"pedagio" ou "bloqueio".</summary>
        public string Mode;
        public int Since;
    }

    internal sealed class BlocCongressLine
    {
        /// <summary>Posição votada: "a"/"b" numa lei, "E#" numa crise; vale para a sessão aberta quando foi votada.</summary>
        public string Choice;
        public string SessionKey;
        public int Turn;
    }
}
