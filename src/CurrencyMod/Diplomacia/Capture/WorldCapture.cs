using System;
using System.Collections.Generic;

namespace CurrencyMod.Diplomacia.Capture
{
    /// <summary>
    /// Snapshot of the world taken at the start of the turn, on the game thread (TurnCapture). Simple types only: once
    /// ready, it can be read on any thread without touching the simulation.
    /// </summary>
    internal sealed class WorldCapture
    {
        public string Guid;
        public int Turn;
        public int LocalEmpire;
        /// <summary>true = taken on the main thread (hot reload mid-turn): best effort.</summary>
        public bool FromMainThread;
        /// <summary>World Congress crisis possible: Together We Rule DLC, game option and crisis vote unlocked.</summary>
        public bool CongressCrisisOpen;
        /// <summary>World Congress (research\congress.md). Null when the game does not have the expansion or the Congress
        /// is turned off in the options.</summary>
        public CapturedCongress Congress;
        public DateTime CapturedAtUtc;
        public CapturedEmpire[] Empires = new CapturedEmpire[0];
        public string[] TerritoryNames = new string[0];
        /// <summary>Owner of each territory: major empire index, or -1 (no owner / independent people).</summary>
        public int[] TerritoryOwner = new int[0];
        public bool[] TerritoryOcean = new bool[0];
        public int[][] Adjacent = new int[0][];
        /// <summary>Deposits of each resource on the map (index = ResourceType; 0 = the resource does not exist in this world).</summary>
        public int[] ResourceDeposits = new int[0];
        /// <summary>Trade route purchases between major empires: who buys which resources from whom.</summary>
        public List<CapturedTrade> Trades = new List<CapturedTrade>();
        /// <summary>Tolls charged at the end of the previous turn by the mod's trade blockade.</summary>
        public List<CapturedToll> Tolls = new List<CapturedToll>();
        /// <summary>Posts with a toll or blockade, summed per owner and affected empire.</summary>
        public List<CapturedTradeRule> TradeRules = new List<CapturedTradeRule>();
        /// <summary>Spies (armies with an agent unit) of all empires, with what letter interception uses.</summary>
        public List<CapturedSpy> Spies = new List<CapturedSpy>();
        /// <summary>Territories where the game flagged foreign spy activity this turn (the owner got the notice).</summary>
        public List<int> StealthActivity = new List<int>();
        /// <summary>The owner's stealth detection in each territory (0 with no owner).</summary>
        public float[] OwnerDetection = new float[0];
        /// <summary>Living independent peoples with a settlement, and each major empire's relation with them.</summary>
        public List<CapturedMinor> Minors = new List<CapturedMinor>();

        public CapturedMinor Minor(int index) => Minors.Find(m => m.Index == index);

        public CapturedEmpire Empire(int index) => index >= 0 && index < Empires.Length ? Empires[index] : null;

        public int OwnerOf(int territory) => territory >= 0 && territory < TerritoryOwner.Length ? TerritoryOwner[territory] : -1;

        public string TerritoryName(int territory) =>
            territory >= 0 && territory < TerritoryNames.Length && !string.IsNullOrEmpty(TerritoryNames[territory]) ? TerritoryNames[territory] : $"território {territory}";

        public IEnumerable<int> AiEmpires()
        {
            foreach (CapturedEmpire empire in Empires)
            {
                if (empire != null && empire.Alive && !empire.Human)
                {
                    yield return empire.Index;
                }
            }
        }

        public IEnumerable<int> AliveEmpires()
        {
            foreach (CapturedEmpire empire in Empires)
            {
                if (empire != null && empire.Alive)
                {
                    yield return empire.Index;
                }
            }
        }
    }

    internal sealed class CapturedEmpire
    {
        public int Index;
        public bool Alive;
        public bool Human;
        public string FullName;
        public string Leader;
        public string Culture;
        public int EraIndex;
        public string EraName;
        public uint Archetypes;
        public uint Biases;

        public double Money;
        public double MoneyNet;
        public double Influence;
        public double InfluenceNet;
        public double Science;
        public double Stability;
        public double Fame;
        public int FameRank;
        public int EraStars;
        public int Population;
        public int Technologies;
        public int TerritoryCount;
        public double MilitaryStrength;
        public int UnitCount;
        /// <summary>Access to each resource (index = ResourceType): exploited deposits plus what it buys.</summary>
        public int[] ResourceAccess = new int[0];
        /// <summary>The empire's currency in CurrencyMod (plural, e.g. "Reais"); null if the currency mod has no data.</summary>
        public string CurrencyName;
        public string CurrencySymbol;

        public List<CapturedCity> Cities = new List<CapturedCity>();
        public List<CapturedArmy> Armies = new List<CapturedArmy>();
        /// <summary>Relation with each other major empire (null at its own index).</summary>
        public CapturedRelation[] Relations = new CapturedRelation[0];
        /// <summary>Armies of other empires that this nation sees now (fog and stealth applied).</summary>
        public List<SeenArmy> SeenArmies = new List<SeenArmy>();
        /// <summary>Settlements of other empires on tiles this nation has already explored.</summary>
        public List<SeenCity> SeenCities = new List<SeenCity>();
        /// <summary>Major empires with a territory adjacent to a territory of this nation.</summary>
        public List<int> Neighbors = new List<int>();

        public CapturedRelation RelationWith(int other) => other >= 0 && other < Relations.Length ? Relations[other] : null;
    }

    internal sealed class CapturedCity
    {
        public string Key;
        public string Name;
        public bool IsCity;
        public bool Capital;
        public int Population;
        public double PublicOrder;
        /// <summary>Net industry and growth (surplus food) per turn.</summary>
        public double Production;
        public double Growth;
        public int CenterTerritory = -1;
        public List<int> Territories = new List<int>();
        public bool Besieged;
        public bool Captured;
        /// <summary>Has a harbor district: only then can the nation talk about "opening the port", a trade fleet, etc.</summary>
        public bool HasHarbour;
        public int OriginalOwner = -1;
    }

    internal sealed class CapturedArmy
    {
        public string Key;
        public string Name;
        public int Units;
        public double Strength;
        public double Health;
        public int Territory = -1;
        public string State;
        public bool Moving;
        public bool Naval;
        public bool Spy;
        public bool Besieging;
    }

    /// <summary>
    /// Spy: an army with at least one agent unit (UnitTagAsAbility.Agent). The stealth fields are relative to the owner
    /// of the territory it is in (research: research\espionage-interception.md).
    /// </summary>
    internal sealed class CapturedSpy
    {
        public int Owner;
        public string Key;
        public string Name;
        public int Territory = -1;
        /// <summary>Owner of the territory the spy is in (-1 = no owner or independent people).</summary>
        public int TerritoryOwner = -1;
        public int Agents;
        /// <summary>Lowest veterancy among the units (the game uses the minimum for the army).</summary>
        public double Veterancy;
        public double Stealth;
        public double StealthMax;
        /// <summary>Hidden from the territory owner (stealthy and without shared alliance vision).</summary>
        public bool Hidden;
        /// <summary>Can act against the territory owner: hidden and with no agreement forbidding agent actions.</summary>
        public bool CanAct;
        /// <summary>The territory owner's detection there.</summary>
        public double OwnerDetection;
        /// <summary>Highest hostile detection against the spy there.</summary>
        public double HostileDetection;
        /// <summary>Turns until stealth runs out with the current detection (int.MaxValue = nobody detects there).</summary>
        public int TurnsBeforeRevealed = int.MaxValue;
        /// <summary>The territory owner hunts revealed spies (game ability).</summary>
        public bool OwnerHuntsSpies;
        /// <summary>"Watch city" mission active.</summary>
        public bool WatchingCity;
        /// <summary>Infiltration in progress or completed (WatchCity, ExploitDistrict...); null = none.</summary>
        public string Infiltration;
        public int InfiltrationTarget = -1;
        public string InfiltrationSettlement;
        public bool Infiltrated;
        public int InfiltrationTurnsLeft;
    }

    /// <summary>Independent people (minor empire). The index is reused when one people disappears and another appears.</summary>
    internal sealed class CapturedMinor
    {
        public int Index;
        public string Name;
        /// <summary>Young, Zenith, InDecline or Dying.</summary>
        public string Status;
        public bool Peaceful;
        public int CenterTerritory = -1;
        public string CityName;
        public List<int> Territories = new List<int>();
        /// <summary>Major empire with the most patronage (-1 = nobody).</summary>
        public int TopPatron = -1;
        /// <summary>Relation with each major empire (index = major empire; null if not known).</summary>
        public CapturedMinorRelation[] Relations = new CapturedMinorRelation[0];
    }

    internal sealed class CapturedMinorRelation
    {
        public double Patronage;
        /// <summary>Share of the total patronage (0 to 1).</summary>
        public double Share;
        public string MoneyInvestment;
        public string InfluenceInvestment;
        /// <summary>The game's reason for not being able to patronize now (null = can).</summary>
        public string PatronizeBlocked;
        public List<CapturedMinorTreaty> Treaties = new List<CapturedMinorTreaty>();
    }

    internal sealed class CapturedMinorTreaty
    {
        /// <summary>MinorTreaty value.</summary>
        public int Treaty;
        /// <summary>Game definition name (translated in the dossier by GameGlossary).</summary>
        public string Definition;
        public bool Enacted;
        public bool Available;
        public double Cost;
        /// <summary>Why it is not possible now ("falta patrocínio", "falta influência", "outro tratado do grupo já escolhido").</summary>
        public string Blocked;
    }

    internal sealed class SeenArmy
    {
        /// <summary>Army GUID (hex), for the A# attack target code.</summary>
        public string Key;
        public int Owner;
        public int Units;
        public double Strength;
        public int Territory = -1;
        public bool Naval;
        /// <summary>Part of the army is stealthy and undetected: the numbers are only for the visible part.</summary>
        public bool Partial;
    }

    internal sealed class SeenCity
    {
        public int Owner;
        public string Key;
        public string Name;
        public bool IsCity;
        public bool Capital;
        public int CenterTerritory = -1;
        /// <summary>The tile is visible now (population and siege are valid only in that case).</summary>
        public bool Visible;
        public int Population;
        public bool Besieged;
    }

    internal sealed class CapturedRelation
    {
        public int Other;
        public string State;
        public bool Knows;
        public bool AtWar;
        public bool AllOutWar;
        public bool Alliance;
        public bool OpenBorders;
        public bool Trade;
        public bool SharedMaps;
        public bool NonAggression;
        public double MyMoral;
        public double TheirMoral;
        public double MyWarScore;
        public double TheirWarScore;
        /// <summary>This nation's grievances against the other that have not become demands yet.</summary>
        public List<CapturedGrievance> MyGrievances = new List<CapturedGrievance>();
        /// <summary>Types of the other's grievances against this nation.</summary>
        public List<string> TheirGrievances = new List<string>();
        public bool DemandsAgainstThem;
        public bool DemandsAgainstMe;
        /// <summary>Open demands of this nation against the other, and of the other against this one.</summary>
        public List<CapturedDemand> MyDemands = new List<CapturedDemand>();
        public List<CapturedDemand> TheirDemands = new List<CapturedDemand>();
        /// <summary>Who already asked for more time ("enrolar") in this crisis: only once per crisis.</summary>
        public bool IStalled;
        public bool TheyStalled;
        /// <summary>Demand forced by the consulate: who must answer on the same turn (-1 = none). Left to the native AI.</summary>
        public int EnforcedDemandOn = -1;
        public string Crisis;
        /// <summary>Who refused the demands last (the other side gains a pretext for formal war).</summary>
        public int CrisisRefuser = -1;
        public List<LogItem> Log = new List<LogItem>();
        /// <summary>Pending treaty proposal ("paz", "aliança", "fim da crise"): who proposed, who answers and when.</summary>
        public PendingProposal Treaty;
        /// <summary>Pending agreement proposal ("acordo econômico (comércio)" etc.).</summary>
        public PendingProposal Agreement;
        /// <summary>Surrender in this war (research\surrender.md): only with AtWar.</summary>
        public CapturedSurrender Surrender;
        /// <summary>Congress verdict on the dispute (crisis with status InternationalCrisisVoteEnded): who won (-1 = none)
        /// and, if this nation lost, why it cannot comply or go to surprise war now (null = can).</summary>
        public int CongressWinner = -1;
        public string VerdictAcceptBlocked;
        public string VerdictWarBlocked;
    }

    /// <summary>Surrender in a war, from one nation's point of view ("mine" is the owner of the relation).</summary>
    internal sealed class CapturedSurrender
    {
        /// <summary>I can force surrender (AllowToForceOtherToSurrender without failure: their support at 0, mine above 0).</summary>
        public bool CanForce;
        public string ForceBlocked;
        /// <summary>They can force surrender on me now.</summary>
        public bool CanBeForced;
        /// <summary>I can offer surrender (StartToFillSurrenderProposition without failure).</summary>
        public bool CanOffer;
        public string OfferBlocked;
        /// <summary>My open drafts (offer and forced, the latter also via the "to the ally" variant).</summary>
        public bool MyOfferDraft;
        public bool MyForcedDraft;
        /// <summary>Proposal sent in this war, awaiting an answer.</summary>
        public CapturedSurrenderPending Pending;
        /// <summary>Exact preview (InitializeSurrenderProposition) if I surrender to them / if I force it on them.</summary>
        public CapturedSurrenderPreview IfIOffer;
        public CapturedSurrenderPreview IfIForce;
    }

    internal sealed class CapturedSurrenderPending
    {
        public int Winner;
        public int Loser;
        public bool Forced;
        /// <summary>Who answers: the loser in a forced one, the winner in an offer.</summary>
        public int Responder;
        public int Score;
        public int Cost;
        public int Demands;
        public List<int> Territories = new List<int>();
        public bool Submission;
        public int Money;
    }

    internal sealed class CapturedSurrenderPreview
    {
        /// <summary>Winner's score: the price the terms must spend (maximum leftover of 5).</summary>
        public int Budget;
        public int DemandCount;
        public int DemandCost;
        /// <summary>Score above the cost of the demands: all demands are included and the rest is free.</summary>
        public bool DemandsFixed;
        public List<CapturedSurrenderTerritory> Territories = new List<CapturedSurrenderTerritory>();
        public bool SubmissionAvailable;
        public int SubmissionCost;
        public string SubmissionBlocked;
        public int MoneyPerPoint;
    }

    internal sealed class CapturedSurrenderTerritory
    {
        public int Territory;
        /// <summary>"centro de São Paulo (ocupada)", "anexo de Santa Catarina", "posto".</summary>
        public string Detail;
        /// <summary>Can be included right away (touches the winner's border or is an occupied city); otherwise only together with Via.</summary>
        public bool Ready;
        public int Via = -1;
    }

    /// <summary>
    /// Game grievance (DiplomaticGrievanceInfo). Pool = index in the owner's GrievanceAllocator; the code in the dossier is
    /// "G" + Pool. The executor checks the type again before acting, because the game reuses indices.
    /// </summary>
    internal sealed class CapturedGrievance
    {
        public int Pool;
        public string Type;
        public int TurnsLeft;
        public CapturedGain Gain;
        /// <summary>The game's reason for not being able to demand now (null = can).</summary>
        public string Blocked;
    }

    /// <summary>Open demand (DiplomaticDemandInfo).</summary>
    internal sealed class CapturedDemand
    {
        public string Type;
        public int Turn;
        public CapturedGain Gain;
    }

    /// <summary>What the demand delivers to the demander (DemandGainType + DemandGain).</summary>
    internal sealed class CapturedGain
    {
        /// <summary>Money, Territory, ForceCivic, ForceReligion or DiplomaticAction.</summary>
        public string Kind;
        public int Amount;
        public int Territory = -1;
        /// <summary>Definition names of the civic and the choice (null = revoke the civic); translated in the dossier.</summary>
        public string Civic;
        public string CivicChoice;
        /// <summary>Demanded diplomatic action (DiplomaticAction) against the Third nation.</summary>
        public string Action;
        public int Third = -1;
        /// <summary>false = the game can no longer deliver this (it becomes money).</summary>
        public bool Valid = true;
    }

    internal sealed class PendingProposal
    {
        public string Kind;
        public int From;
        public int Answerer;
        public int Turn;
    }

    internal sealed class LogItem
    {
        public int Turn;
        public int Initiator;
        public string Action;
    }

    /// <summary>One side of a trade route: the buyer and the resources (ResourceType) it buys from the seller.</summary>
    internal sealed class CapturedTrade
    {
        public int Buyer;
        public int Seller;
        public bool Suspended;
        public List<int> Resources = new List<int>();
    }

    /// <summary>One turn's toll: each side in its own currency.</summary>
    internal sealed class CapturedToll
    {
        public int Owner;
        public int Payer;
        public double Received;
        public double Paid;
        public int Routes;
    }

    // ---------------- World Congress (research\congress.md) ----------------

    internal sealed class CapturedCongress
    {
        public bool CivicUnlocked;
        public bool CrisisUnlocked;
        public bool ConsensusUnlocked;
        /// <summary>Who formed the Congress (presides over the first session).</summary>
        public int Unlocker = -1;
        /// <summary>Fixed order of the session presidency.</summary>
        public int[] LeaderOrder = new int[0];
        /// <summary>Congress weight of each major nation (the vote weight).</summary>
        public double[] Sway = new double[0];
        /// <summary>Consulate operating (required to bribe and to declare a crisis).</summary>
        public bool[] Consulate = new bool[0];
        /// <summary>Leverage of each nation against each other: Leverage[i][j] = that of i's embassy with j.</summary>
        public double[][] Leverage = new double[0][];
        /// <summary>Before the Congress exists: what each nation still lacks to form it ("era", "consulado", "contatos").</summary>
        public string[] UnlockBlocked = new string[0];
        /// <summary>Minimum era and required fraction of known nations (RPN; -1 when the snapshot came from the main thread).</summary>
        public int MinEra = -1;
        public double MinMetFraction = -1;
        /// <summary>Law sessions so far (0 = the first has not opened yet and awaits the first president).</summary>
        public int SessionCount;
        public int NextLeader = -1;
        public int NextSessionTurn = -1;
        /// <summary>Laws the next president can propose.</summary>
        public List<CongressLaw> Proposable = new List<CongressLaw>();
        public CongressLawVote LawVote;
        /// <summary>Latest closed law votes (most recent first).</summary>
        public List<CongressLawVote> LawHistory = new List<CongressLawVote>();
        public List<CongressCrisis> Crises = new List<CongressCrisis>();
        public List<CongressAxis> Consensus = new List<CongressAxis>();
        /// <summary>Laws imposed by the Congress awaiting adoption or refusal (CivicsShakedown osmosis events).</summary>
        public List<CongressImposedLaw> ImposedLaws = new List<CongressImposedLaw>();
    }

    /// <summary>International law (civic with IsInternational). The code in the dossier is "V" + Code (civic index).</summary>
    internal sealed class CongressLaw
    {
        public int Code;
        public string Civic;
        public string ChoiceA;
        public string ChoiceB;
        /// <summary>Option in force in each major nation: 0 = A, 1 = B, -1 = without the law.</summary>
        public int[] Current = new int[0];
    }

    internal sealed class CongressLawVote
    {
        public CongressLaw Law;
        public int Leader = -1;
        public int TurnBegin;
        public int TurnEnd;
        public string State;
        public bool Active;
        /// <summary>For closed ones: 0 = A won, 1 = B won, -1 = tie (nothing changes).</summary>
        public int Winner = -1;
        public CongressBallot[] Ballots = new CongressBallot[0];
    }

    internal sealed class CongressBallot
    {
        /// <summary>Law: 0 = A, 1 = B. Crisis: the supported nation. -1 = did not vote.</summary>
        public int Choice = -1;
        /// <summary>Weight recorded in the vote (only for those who voted).</summary>
        public double Sway;
        /// <summary>Extra weight already bought with bribes (counts in the vote) and the bonus that applies at the start of next turn.</summary>
        public double BribeBonus;
        public double BribePending;
        /// <summary>Bribes possible now against each target (index = target; null = none). Only for computer nations.</summary>
        public CongressBribe[] Bribes;
    }

    internal sealed class CongressBribe
    {
        /// <summary>How many bribes can still be bought against this target in this vote.</summary>
        public int Remaining;
        public int Purchased;
        /// <summary>Leverage per bribe and weight gained per bribe.</summary>
        public double Cost;
        public double Bonus;
        /// <summary>How many the current leverage pays for (limited to what remains).</summary>
        public int Affordable;
    }

    /// <summary>Crisis vote in the Congress. The code in the dossier is "K" + Pool.</summary>
    internal sealed class CongressCrisis
    {
        public int Pool;
        public int Declarator;
        public int Target;
        public int TurnBegin;
        public int TurnEnd;
        public string State;
        public bool Active;
        /// <summary>Closed, waiting for the loser to comply or go to surprise war.</summary>
        public bool WaitingLoser;
        public int Winner = -1;
        public int Loser = -1;
        public CongressBallot[] Ballots = new CongressBallot[0];
    }

    /// <summary>Ideological consensus axis. The code in the dossier is "I" + Index.</summary>
    internal sealed class CongressAxis
    {
        public int Index;
        public string Definition;
        public bool Unlocked;
        public int UnlockTurn = -1;
        public int Count;
        public int Required;
        public string Orientation;
        /// <summary>Influence cost of each major nation's next contribution (-1 = not read) and how many it has made.</summary>
        public double[] Cost = new double[0];
        public int[] Contributions = new int[0];
    }

    internal sealed class CongressImposedLaw
    {
        public int Empire;
        public int EventIndex;
        public string Civic;
        public string Choice;
        public int Proposer = -1;
        public double DiscardCost;
        public bool CanDiscard;
    }

    /// <summary>How many of the owner's posts charge a toll on (or block) the affected empire.</summary>
    internal sealed class CapturedTradeRule
    {
        public int Owner;
        public int Target;
        public bool Block;
        public int Posts;
        /// <summary>Toll per resource per turn, in the owner's currency: lowest and highest among the posts (toll only).</summary>
        public double MinPrice;
        public double MaxPrice;
    }
}
