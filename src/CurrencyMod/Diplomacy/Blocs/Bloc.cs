using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CurrencyMod.Diplomacy.Blocs
{
    /// <summary>
    /// Bloc of nations (melhorias-jogando item 16, "like NATO, BRICS"). It is born with 3 members after the invitations,
    /// has a president with a term, voted agenda items and four benefits: free internal trade, mutual defense, a joint
    /// voice in the Congress and a common tariff. The screen calls it "Alliances"; the entity is a "bloc" so it is not
    /// confused with the game's Alliance agreement.
    /// </summary>
    internal sealed class Bloc
    {
        public const int MinMembers = 3;
        public const int MaxMembers = 10;
        /// <summary>Turns an invitation stays open.</summary>
        public const int InviteTurns = 5;
        /// <summary>Turns an agenda item or election stays open for voting.</summary>
        public const int VoteTurns = 3;
        public const int TermTurns = 20;
        /// <summary>Waiting turns after leaving (or being expelled) before joining another bloc.</summary>
        public const int LeaveCooldown = 10;

        public int Id;
        public string Name;
        public int Founder;
        public int CreatedTurn;
        /// <summary>Turn the bloc reached 3 members; -1 = still forming (invitations only).</summary>
        public int FormedTurn = -1;
        public List<int> Members = new List<int>();
        public int President = -1;
        public int TermStart;
        /// <summary>Members' chat group (ChatGroup), created together with the bloc.</summary>
        public int GroupId = -1;
        public List<BlocInvite> Invites = new List<BlocInvite>();
        public List<BlocMotion> Motions = new List<BlocMotion>();
        public List<BlocEvent> History = new List<BlocEvent>();
        /// <summary>Common tariff: toll or blockade against an outside nation at every member trade post.</summary>
        public List<BlocTariff> Tariffs = new List<BlocTariff>();
        /// <summary>Bloc position in the Congress, voted as an agenda item: all members vote together.</summary>
        public BlocCongressLine CongressLine;
        /// <summary>Open election: opening turn (-1 = none) and votes (voter → candidate).</summary>
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
    /// Bloc agenda item. A regular member suggests it (Suggested); only the president puts it to a vote (Voting). It closes
    /// when everyone has voted or the deadline ends: simple majority of the votes (tie: the president's vote decides);
    /// expulsion requires 2/3 of the members who can vote.
    /// </summary>
    internal sealed class BlocMotion
    {
        public const string Suggested = "sugerida";
        public const string Voting = "votacao";
        public const string Passed = "aprovada";
        public const string Failed = "rejeitada";
        public const string Dropped = "descartada";

        /// <summary>Agenda item types.</summary>
        public static readonly string[] Kinds = { "tarifa", "fim_tarifa", "admitir", "expulsar", "congresso", "renomear", "dissolver" };

        public int Id;
        public string Kind;
        /// <summary>Target nation (tarifa, fim_tarifa, admitir, expulsar); -1 for the others.</summary>
        public int Target = -1;
        /// <summary>Tariff: "pedagio" or "bloqueio". Congress: the position ("a", "b" or the supported nation "E#"). Rename: the name.</summary>
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
        /// <summary>Event kinds: fundou, convidou, entrou, recusou, saiu, expulso, presidente, pauta, aprovada, rejeitada,
        /// tarifa, fim_tarifa, renomeado, desfeito, formado, falhou.</summary>
        public string Kind;
        public int Actor = -1;
        public int Target = -1;
        public string Arg;
    }

    internal sealed class BlocTariff
    {
        public int Target;
        /// <summary>"pedagio" or "bloqueio".</summary>
        public string Mode;
        public int Since;
    }

    internal sealed class BlocCongressLine
    {
        /// <summary>Voted position: "a"/"b" on a law, "E#" on a crisis; applies to the session open when it was voted.</summary>
        public string Choice;
        public string SessionKey;
        public int Turn;
    }
}
