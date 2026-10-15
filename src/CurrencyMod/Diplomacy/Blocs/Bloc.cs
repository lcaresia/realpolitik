using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CurrencyMod.Diplomacy.Blocs
{
    /// <summary>
    /// Bloc of nations (melhorias-jogando item 16, "like NATO, BRICS"). It is born with 3 members after the invitations,
    /// has a president with a term and free-text motions (item 46): a motion that passes becomes a resolution for 10
    /// turns. The mod never interprets, audits or enforces a resolution: the members do, and whoever thinks another
    /// member broke one records an accusation in the bloc's book, which everyone sees (Lucas, 2026-10-12). Mechanical
    /// benefits: free internal trade and mutual defense. The screen calls it "Alliances"; the entity is a "bloc" so it is
    /// not confused with the game's Alliance agreement.
    /// </summary>
    internal sealed class Bloc
    {
        public const int MinMembers = 3;
        public const int MaxMembers = 10;
        /// <summary>Turns an invitation stays open.</summary>
        public const int InviteTurns = 5;
        /// <summary>Turns an agenda item or election stays open for voting (Lucas, 2026-10-12: 2).</summary>
        public const int VoteTurns = 2;
        /// <summary>Turns a suggestion waits for the president before it expires.</summary>
        public const int SuggestionTurns = 5;
        /// <summary>Turns a resolution stays in force; after that it lapses and must be proposed again.</summary>
        public const int ResolutionTurns = 10;
        /// <summary>Motions a member may have at once: its suggestions, motions in a vote and resolutions in force.</summary>
        public const int MotionSlots = 3;
        public const int MotionTitleMax = 80;
        public const int MotionTextMax = 500;
        public const int TermTurns = 20;
        /// <summary>Waiting turns after leaving (or being expelled) before joining another bloc.</summary>
        public const int LeaveCooldown = 10;
        /// <summary>Turns an accusation counts against a member (and stays listed in the book).</summary>
        public const int AccusationTurns = 30;

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
        /// <summary>Legacy common tariff (typed motions, before item 46); moved into resolutions by BlocEngine.Migrate.</summary>
        public List<BlocTariff> Tariffs = new List<BlocTariff>();
        /// <summary>Legacy joint Congress position (typed motions); cleared by BlocEngine.Migrate.</summary>
        public BlocCongressLine CongressLine;
        /// <summary>Free motions that passed, with the commission's reading and audits.</summary>
        public List<BlocResolution> Resolutions = new List<BlocResolution>();
        /// <summary>The book of accusations: what a member says another did against a resolution (only their word).</summary>
        public List<BlocAccusation> Accusations = new List<BlocAccusation>();
        /// <summary>Typed motions and tariffs already converted to the free model.</summary>
        public bool Migrated;
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

        public IEnumerable<BlocResolution> InForce => Resolutions.Where(r => r.Status == BlocResolution.InForceStatus);

        public BlocResolution ResolutionBy(int id) => Resolutions.FirstOrDefault(r => r.Id == id);

        /// <summary>The resolution's votes; for one approved before they were stored, the votes of its motion (if still kept).</summary>
        public Dictionary<int, bool> VotesOf(BlocResolution resolution)
        {
            if (resolution.Votes.Count > 0)
            {
                return resolution.Votes;
            }
            BlocMotion motion = MotionBy(resolution.MotionId);
            return motion != null ? motion.Votes : resolution.Votes;
        }

        /// <summary>Motion slots in use by a member (suggested, in a vote, or resolutions in force it wrote).</summary>
        public int SlotsUsed(int member) => Motions.Count(m => m.Author == member && (m.Status == BlocMotion.Voting || m.Status == BlocMotion.Suggested || m.Status == BlocMotion.Tiebreak))
            + Resolutions.Count(r => r.Author == member && r.Status == BlocResolution.InForceStatus);

        /// <summary>Accusations against a member in the last <paramref name="turns"/> turns.</summary>
        public int AccusedCount(int member, int turn, int turns = AccusationTurns) => Accusations.Count(a => a.Accused == member && turn - a.Turn <= turns);

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
        /// <summary>Waiting for the president: a tie, or an expulsion with a majority short of 2/3 (Lucas, 2026-10-12).</summary>
        public const string Tiebreak = "desempate";
        /// <summary>Turns the president has to decide a tie before the motion fails.</summary>
        public const int TiebreakTurns = 2;

        /// <summary>Free-text motion (item 46): the only kind proposed now.</summary>
        public const string FreeKind = "free";

        /// <summary>Legacy agenda item types (typed motions in old saves).</summary>
        public static readonly string[] Kinds = { "tarifa", "fim_tarifa", "admitir", "expulsar", "congresso", "renomear", "dissolver" };

        public int Id;
        public string Kind = FreeKind;
        /// <summary>Free motion: short title and the text the members vote on.</summary>
        public string Title;
        public string Text;
        /// <summary>Expulsion motion: the member to expel (-1 = an ordinary motion). Passes with 2/3; short of that, the president decides.</summary>
        public int Expel = -1;
        /// <summary>Turn the motion went to the president's tie-break (-1 = it did not).</summary>
        public int TiebreakTurn = -1;
        /// <summary>The president's decision on a tie: null = none, true = approved, false = rejected.</summary>
        public bool? PresidentDecision;
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

    /// <summary>A free motion that passed: binding on every member until revoked.</summary>
    internal sealed class BlocResolution
    {
        public const string InForceStatus = "in_force";
        public const string RevokedStatus = "revoked";
        public const string ExpiredStatus = "expired";

        public int Id;
        public int MotionId;
        public string Title;
        public string Text;
        public int Author;
        public int PassedTurn;
        public int Yes;
        public int No;
        /// <summary>Members who could vote (the president and an expelled member do not).</summary>
        public int Voters;
        /// <summary>Each member's vote (melhorias item 53): copied from the motion, which is pruned later.</summary>
        public Dictionary<int, bool> Votes = new Dictionary<int, bool>();
        /// <summary>The president broke a tie (or decided an expulsion short of 2/3).</summary>
        public bool ByPresident;
        public string Status = InForceStatus;
        public int RevokedTurn = -1;

        [JsonIgnore]
        public int Expires => PassedTurn + Bloc.ResolutionTurns;

        [JsonIgnore]
        public string Handle => "R" + Id;
    }

    /// <summary>A member's accusation that another broke a resolution: only the accuser's word, with its evidence.</summary>
    internal sealed class BlocAccusation
    {
        public int Turn;
        public int Accuser;
        public int Accused;
        /// <summary>Resolution id, or -1 for the bloc's spirit in general.</summary>
        public int Resolution = -1;
        public string Evidence;
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
