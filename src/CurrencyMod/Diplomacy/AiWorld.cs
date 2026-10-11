using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// AI diplomacy state of a game: personas, diaries, feelings, memories, letters and spending.
    /// Goes inside the save (DiplomaciaIA.json). Only the main thread touches it.
    /// </summary>
    internal sealed class AiWorld
    {
        /// <summary>2 = turns counted as the game shows them (version 1 counted one extra); 3 = posture and focus stored
        /// as the nation's current state (previously only in the action history); 4 = letters linked to the letter they
        /// answer; 5 = letter interception by spies.</summary>
        public int Version = 5;
        public string GameGuid;
        public double TotalCostUsd;
        public int TotalCalls;
        public long TotalPromptTokens;
        public long TotalCacheHitTokens;
        public long TotalCompletionTokens;
        public long TotalReasoningTokens;
        public List<AiNation> Nations = new List<AiNation>();
        public List<Letter> Letters = new List<Letter>();
        public int NextLetterId = 1;
        public Dictionary<string, string> CityHandles = new Dictionary<string, string>();
        public Dictionary<string, string> ArmyHandles = new Dictionary<string, string>();
        public int NextCityHandle = 1;
        public int NextArmyHandle = 1;
        public int NextActionId = 1;
        /// <summary>Last good name of each empire (leader and full name). The game sometimes delivers the player's leader
        /// as "0", when the Steam nickname has not loaded yet.</summary>
        public Dictionary<int, string> LeaderNames = new Dictionary<int, string>();
        public Dictionary<int, string> FullNames = new Dictionary<int, string>();
        /// <summary>Group chats (melhorias-jogando item 15): any nation creates one; messages are letters of type "grupo".</summary>
        public List<ChatGroup> Groups = new List<ChatGroup>();
        public int NextGroupId = 1;
        /// <summary>Nation blocs (item 16), with agenda items and a president; rules in Blocs.BlocEngine.</summary>
        public List<Blocs.Bloc> Blocs = new List<Blocs.Bloc>();
        public int NextBlocId = 1;
        public int NextMotionId = 1;
        /// <summary>Turn in which each nation left (or was expelled from) a bloc: waits Bloc.LeaveCooldown to join another.</summary>
        public Dictionary<int, int> BlocLeftTurn = new Dictionary<int, int>();
        /// <summary>Last turn in which the bloc step ran (BlocEngine.Step runs once per turn).</summary>
        public int BlocTickTurn = -1;

        /// <summary>Group by the "GRP#" code or by id.</summary>
        public ChatGroup GroupBy(string handle)
        {
            string digits = (handle ?? string.Empty).Trim().ToUpperInvariant().Replace("GRP", string.Empty);
            return int.TryParse(digits, out int id) ? Groups.FirstOrDefault(g => g.Id == id) : null;
        }

        /// <summary>All people names each empire has had (changes every era): letters still mention the old ones.</summary>
        public Dictionary<int, List<string>> CultureNames = new Dictionary<int, List<string>>();
        /// <summary>Where each spy (key = army GUID) is and since which turn: the game only stores this for
        /// infiltrations, and a newly arrived spy does not know the place yet (lower weight in interception).</summary>
        public Dictionary<string, SpyTrack> SpyTracks = new Dictionary<string, SpyTrack>();
        public int SpyTracksTurn = -1;
        /// <summary>Army orders given by the language AI (ordem_exercito): destination, status and until when the native
        /// AI leaves the army alone.</summary>
        public List<ArmyMission> ArmyMissions = new List<ArmyMission>();
        /// <summary>The player's council meetings (design §10.6), one per turn, with the player's answers.</summary>
        public List<Council.CouncilMeeting> PlayerCouncil = new List<Council.CouncilMeeting>();
        /// <summary>The player's relations on the previous turn (war, peace, agreements), to see whether the advice was followed.</summary>
        public Dictionary<int, string> PlayerRelationMemo = new Dictionary<int, string>();
        public int PlayerRelationMemoTurn = -1;
        /// <summary>World Congress results (voted laws and judged crises), most recent at the end. The game deletes the
        /// crisis from the pool right after the result, so the news is kept here.</summary>
        public List<CongressNews> CongressNews = new List<CongressNews>();

        /// <summary>An army's mission in force (the most recent one), or null.</summary>
        public ArmyMission MissionFor(string armyKey)
        {
            for (int i = ArmyMissions.Count - 1; i >= 0; i--)
            {
                if (ArmyMissions[i].ArmyKey == armyKey)
                {
                    return ArmyMissions[i];
                }
            }
            return null;
        }

        public AiNation Get(int empireIndex)
        {
            foreach (AiNation nation in Nations)
            {
                if (nation.EmpireIndex == empireIndex)
                {
                    return nation;
                }
            }
            return null;
        }

        public AiNation Ensure(int empireIndex)
        {
            AiNation nation = Get(empireIndex);
            if (nation == null)
            {
                nation = new AiNation { EmpireIndex = empireIndex };
                Nations.Add(nation);
                Nations.Sort((a, b) => a.EmpireIndex.CompareTo(b.EmpireIndex));
            }
            return nation;
        }

        public string CityHandle(string guid)
        {
            if (!CityHandles.TryGetValue(guid, out string handle))
            {
                handle = "C" + NextCityHandle++;
                CityHandles[guid] = handle;
            }
            return handle;
        }

        public string ArmyHandle(string guid)
        {
            if (!ArmyHandles.TryGetValue(guid, out string handle))
            {
                handle = "A" + NextArmyHandle++;
                ArmyHandles[guid] = handle;
            }
            return handle;
        }

        /// <summary>Game GUID behind a C# or A# code (0 if it does not exist).</summary>
        public ulong GuidForHandle(string handle)
        {
            Dictionary<string, string> table = handle != null && handle.StartsWith("C") ? CityHandles : ArmyHandles;
            foreach (KeyValuePair<string, string> pair in table)
            {
                if (string.Equals(pair.Value, handle, StringComparison.OrdinalIgnoreCase)
                    && ulong.TryParse(pair.Key, System.Globalization.NumberStyles.HexNumber, null, out ulong guid))
                {
                    return guid;
                }
            }
            return 0;
        }

        /// <summary>Replaces bad names in the snapshot ("Governante Supremo 0") with the last good names and stores the good ones.</summary>
        public void FixNames(Capture.WorldCapture capture)
        {
            foreach (Capture.CapturedEmpire empire in capture.Empires)
            {
                if (empire == null)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(empire.Culture))
                {
                    if (!CultureNames.TryGetValue(empire.Index, out List<string> cultures))
                    {
                        cultures = new List<string>();
                        CultureNames[empire.Index] = cultures;
                    }
                    if (!cultures.Contains(empire.Culture))
                    {
                        cultures.Add(empire.Culture);
                    }
                }
                bool bad = string.IsNullOrWhiteSpace(empire.Leader) || empire.Leader.Trim().All(char.IsDigit);
                if (!bad)
                {
                    LeaderNames[empire.Index] = empire.Leader;
                    FullNames[empire.Index] = empire.FullName;
                }
                else if (LeaderNames.TryGetValue(empire.Index, out string leader) && FullNames.TryGetValue(empire.Index, out string full))
                {
                    empire.Leader = leader;
                    empire.FullName = full;
                }
                else if (!string.IsNullOrWhiteSpace(empire.Leader) && empire.FullName != null)
                {
                    // No good name stored: only the title remains ("Governante Supremo (Os Francos)").
                    empire.FullName = empire.FullName.Replace(" " + empire.Leader.Trim() + " ", " ");
                    empire.Leader = null;
                }
            }
        }

        /// <summary>Upgrades states saved by earlier versions of the mod.</summary>
        public void Migrate()
        {
            if (Version < 2)
            {
                // Version 1 used CurrentTurn + 1; the game shows CurrentTurn. Moves everything back one turn.
                foreach (AiNation nation in Nations)
                {
                    if (nation.LastDecisionTurn > 0) nation.LastDecisionTurn--;
                    if (nation.FailedTurn > 0) nation.FailedTurn--;
                    if (nation.LastPublicDeclarationTurn > 0) nation.LastPublicDeclarationTurn--;
                    nation.Diary.ForEach(d => d.Turn--);
                    nation.Notes.ForEach(n => n.Turn--);
                    nation.Actions.ForEach(a => a.Turn--);
                    foreach (Feelings feelings in nation.Feelings.Values)
                    {
                        if (feelings.Turn > 0) feelings.Turn--;
                    }
                }
                foreach (Letter letter in Letters)
                {
                    letter.SentTurn--;
                    letter.DeliverTurn--;
                }
                Version = 2;
            }
            if (Version < 3)
            {
                // Posture and focus used to be just a record: recovers the last value of each from the action history.
                foreach (AiNation nation in Nations)
                {
                    foreach (ActionRecord action in nation.Actions.OrderBy(a => a.Turn))
                    {
                        nation.RememberStance(action.Name, action.Json, action.Turn);
                    }
                }
                Version = 3;
            }
            if (Version < 4)
            {
                // Replies: rebuilds the chains in order, with today's rule (each letter answers the recipient's most
                // recent letter that asked for a reply, had already arrived and had no reply yet).
                var answered = new HashSet<int>();
                foreach (Letter letter in Letters.OrderBy(l => l.Id))
                {
                    if (letter.IsPublic || letter.InReplyTo >= 0)
                    {
                        continue;
                    }
                    Letter original = Letters
                        .Where(l => l.Id < letter.Id && l.From == letter.To && l.To == letter.From && l.ExpectsReply
                            && l.DeliverTurn <= letter.SentTurn && !l.RejectedBy.Contains(letter.From) && !answered.Contains(l.Id))
                        .OrderByDescending(l => l.Id)
                        .FirstOrDefault();
                    if (original != null)
                    {
                        letter.InReplyTo = original.Id;
                        answered.Add(original.Id);
                    }
                }
                Version = 4;
            }
            if (Version < 5)
            {
                // Interception: no old letter is rolled (not even those still on the way).
                foreach (Letter letter in Letters)
                {
                    letter.InterceptionChecked = true;
                }
                Version = 5;
            }
            // New council (melhorias-jogando item 3): everyone goes back to having only the Hand on load, all at once
            // (before, each nation was only reset when its dossier was built, and the 12 old veterans gave bonuses until then).
            foreach (AiNation nation in Nations)
            {
                if (nation.CouncilModel < 1)
                {
                    nation.Council.RemoveAll(m => m.Portfolio != "mao");
                    nation.CouncilCandidates = new Dictionary<string, List<string>>();
                    nation.CouncilCandidatesEra = -1;
                    nation.CouncilModel = 1;
                }
            }
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

        public static AiWorld FromJson(string json) => JsonConvert.DeserializeObject<AiWorld>(json);
    }

    internal sealed class AiNation
    {
        public int EmpireIndex;
        public Persona Persona;
        public int LastDecisionTurn = -1;
        public int FailedTurn = -1;
        public int FailuresThisTurn;
        public int LastPublicDeclarationTurn = -999;
        public List<DiaryEntry> Diary = new List<DiaryEntry>();
        public Dictionary<int, Feelings> Feelings = new Dictionary<int, Feelings>();
        public List<MemoryNote> Notes = new List<MemoryNote>();
        public List<ActionRecord> Actions = new List<ActionRecord>();
        public List<int> BlockedEmpires = new List<int>();
        /// <summary>Current posture toward each nation (definir_postura); holds until changed and guides the native AI (design §11.1).</summary>
        public Dictionary<int, Stance> Postures = new Dictionary<int, Stance>();
        /// <summary>The empire's current focus (definir_foco); holds until changed.</summary>
        public Stance Focus;
        /// <summary>Trade policy toward each nation (politica_comercial: livre, pedagio or bloqueio) at all posts; the
        /// native trade AI (TradeAi) does not touch it while the language AI decides for the nation.</summary>
        public Dictionary<int, Stance> TradeStances = new Dictionary<int, Stance>();
        /// <summary>Patronage chosen for each independent people (patrocinar): people index → (money &lt;&lt; 8) | influence.
        /// The native AI does not touch these peoples while the language AI decides for the nation.</summary>
        public Dictionary<int, int> MinorPatronage = new Dictionary<int, int>();
        /// <summary>Council of ministers (design §10): the Hand and the portfolios the nation has already appointed.</summary>
        public List<Council.Minister> Council = new List<Council.Minister>();
        public int CouncilTurn = -1;
        /// <summary>
        /// Council model: 0 = the old one (12 seats always filled); 1 = starts with only the Hand and portfolios are
        /// appointed from candidates (melhorias-jogando item 3). When loading an old save, the council goes back to only the Hand.
        /// </summary>
        public int CouncilModel;
        /// <summary>The 3 candidates for each vacant portfolio (bank ids). They change in a new era or when someone is appointed.</summary>
        public Dictionary<string, List<string>> CouncilCandidates = new Dictionary<string, List<string>>();
        /// <summary>Era in which the candidates were drawn.</summary>
        public int CouncilCandidatesEra = -1;
        /// <summary>
        /// Cash floors set by the language AI on each decision (set_cash_floors): the native AI does not spend money in a
        /// category if the treasury would end below that category's floor. Floors may be negative (planned debt).
        /// Null = never set (the default floor applies).
        /// </summary>
        public CashFloors CashFloors;
        /// <summary>Personalities fired by this nation (they do not come back to it).</summary>
        public List<string> FiredPersonalities = new List<string>();
        /// <summary>Congress: what the governors do if the imposed law arrives (civic → "adotar" or "recusar"), from the
        /// se_perder field of votar_congresso.</summary>
        public Dictionary<string, string> CongressStances = new Dictionary<string, string>();
        /// <summary>Congress: the option the nation voted for on each law (civic → choice name). Voted for the winner =
        /// adopts the imposed law (whoever lacks the law also receives the imposed law, even having voted for it).</summary>
        public Dictionary<string, string> CongressVotes = new Dictionary<string, string>();

        /// <summary>
        /// Stores definir_postura / definir_foco as the current state. Returns the description of what changed, or null if
        /// the action is not one of these or came without the fields.
        /// </summary>
        public string RememberStance(string actionName, string json, int turn)
        {
            if (actionName != "definir_postura" && actionName != "definir_foco")
            {
                return null;
            }
            Newtonsoft.Json.Linq.JObject item;
            try
            {
                item = Newtonsoft.Json.Linq.JObject.Parse(json ?? "{}");
            }
            catch (Exception)
            {
                return null;
            }
            // A list or object in "motivo" would make the (string) cast throw in the middle of Apply.
            string reason = (item["motivo"] as Newtonsoft.Json.Linq.JValue)?.Value?.ToString();
            if (actionName == "definir_foco")
            {
                string focus = ((string)item["foco"])?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(focus))
                {
                    return null;
                }
                Focus = new Stance { Value = focus, Turn = Focus != null && Focus.Value == focus ? Focus.Turn : turn, Reason = reason };
                return "foco " + focus;
            }
            string handle = (string)item["nacao"];
            string posture = ((string)item["postura"])?.Trim().ToLowerInvariant();
            if (handle == null || string.IsNullOrEmpty(posture) || !int.TryParse(handle.Trim().TrimStart('E', 'e'), out int other))
            {
                return null;
            }
            int since = Postures.TryGetValue(other, out Stance current) && current.Value == posture ? current.Turn : turn;
            Postures[other] = new Stance { Value = posture, Turn = since, Reason = reason };
            return $"postura {posture} com E{other}";
        }

        public Feelings FeelingsFor(int other)
        {
            if (!Feelings.TryGetValue(other, out Feelings feelings))
            {
                feelings = new Feelings();
                Feelings[other] = feelings;
            }
            return feelings;
        }
    }

    internal sealed class Persona
    {
        public string LeaderName;
        public string Gender;
        public List<string> Traits = new List<string>();
        public string Quirk;
        public string Seed;
    }

    internal sealed class Feelings
    {
        public int Affection;
        public int Trust;
        public int Fear;
        public int Anger;
        public string Reason;
        public int Turn = -1;
    }

    /// <summary>Posture toward a nation or the empire's focus: the value, since when it holds and the reason given.</summary>
    internal sealed class Stance
    {
        public string Value;
        public int Turn;
        public string Reason;
    }

    internal sealed class DiaryEntry
    {
        public int Turn;
        public string Text;
    }

    internal sealed class MemoryNote
    {
        public int Turn;
        public int About;
        public string Text;
    }

    internal sealed class ActionRecord
    {
        /// <summary>Order id in the executor (0 = action that does not become a game order).</summary>
        public int Id;
        public int Turn;
        public string Name;
        public string Json;
        public string Status;
    }

    internal sealed class Letter
    {
        public const int Everyone = -1;

        public int Id;
        public int From;
        /// <summary>Recipient index, or Everyone for a public declaration.</summary>
        public int To;
        public string Type;
        public string Subject;
        public string Text;
        public string Demand;
        public int DeadlineTurns;
        public bool ExpectsReply;
        public int SentTurn;
        public int DeliverTurn;
        /// <summary>
        /// Letter this one answers (-1 = none). Comes from the player's Reply button or, without it, from the recipient's
        /// most recent letter that asked for a reply and had not been answered yet (also applies to the AIs).
        /// </summary>
        public int InReplyTo = -1;
        /// <summary>Who has already read the letter in the dossier (a public one is read by many).</summary>
        public List<int> ReadBy = new List<int>();
        /// <summary>Who refused the letter (blocked the sender's private correspondence).</summary>
        public List<int> RejectedBy = new List<int>();

        // Interception (design §8.3.1). Only private letters go through the roll, once, on the turn they would arrive.
        /// <summary>Already went through the roll (ultimatums and public declarations are born checked).</summary>
        public bool InterceptionChecked;
        /// <summary>Who intercepted (-1 = nobody). An intercepted letter never reaches the recipient.</summary>
        public int InterceptedBy = -1;
        public int InterceptedTurn = -1;
        public int InterceptedTerritory = -1;
        /// <summary>Name and GUID of the spy army that intercepted.</summary>
        public string InterceptedSpy;
        public string InterceptedSpyKey;
        /// <summary>"origem" (territory of the writer) or "destino" (of the intended recipient).</summary>
        public string InterceptedVia;
        /// <summary>The interceptor has already read it (in the dossier, or the player in the espionage tab).</summary>
        public bool InterceptorRead;
        /// <summary>Risk at the origin, stored when the letter was written (spies move afterwards).</summary>
        public List<InterceptRisk> OriginRisks;

        /// <summary>Group message destination (Members says who reads; GroupId says which conversation).</summary>
        public const int Group = -2;

        /// <summary>Group chat (type "grupo"): the group id and the members at the time of sending.</summary>
        public int GroupId = -1;
        public List<int> Members;

        [JsonIgnore]
        public bool IsPublic => To == Everyone;

        [JsonIgnore]
        public bool IsGroup => To == Group;

        [JsonIgnore]
        public bool Intercepted => InterceptedBy >= 0;

        /// <summary>
        /// The letter reaches this empire: it is for it (or public and from someone else, or from its group and another
        /// member), has gone through the roll and was not intercepted. The delivery turn is checked separately.
        /// </summary>
        public bool IsFor(int empireIndex) => InterceptionChecked && !Intercepted && From != empireIndex
            && (To == empireIndex || To == Everyone || (To == Group && Members != null && Members.Contains(empireIndex)));

        /// <summary>Has reached its destination by this turn (checked and not intercepted).</summary>
        public bool Arrived(int turn) => DeliverTurn <= turn && InterceptionChecked && !Intercepted;
    }

    /// <summary>A group chat: name, creator and current members (whoever leaves stops receiving).</summary>
    internal sealed class ChatGroup
    {
        public const int MinMembers = 3;
        public const int MaxMembers = 8;

        public int Id;
        public string Name;
        public int Creator;
        public int CreatedTurn;
        public List<int> Members = new List<int>();
        /// <summary>A bloc's group (-1 = ordinary group): members follow the bloc, and one only leaves by leaving the bloc.</summary>
        public int BlocId = -1;

        [JsonIgnore]
        public string Handle => "GRP" + Id;
    }

    /// <summary>
    /// A Congress result: voted law ("lei") or judged crisis ("crise"). Key avoids repeating the same news; each nation's
    /// votes go in Votes (index = nation; law: 0 = A, 1 = B; crisis: the supported nation; -1 = did not vote).
    /// </summary>
    internal sealed class CongressNews
    {
        public string Key;
        public int Turn;
        public string Kind;
        public string Civic;
        public string ChoiceA;
        public string ChoiceB;
        /// <summary>Law: 0 = A, 1 = B, -1 = tie. Crisis: the winning nation (-1 = cancelled).</summary>
        public int Winner = -1;
        public int Declarator = -1;
        public int Target = -1;
        public string State;
        public List<int> Votes = new List<int>();
        public List<double> Sway = new List<double>();
    }

    /// <summary>Chance of an empire intercepting a letter at one end, and its spy that weighs most there.</summary>
    internal sealed class InterceptRisk
    {
        public int Empire;
        public double Chance;
        public string SpyKey;
        public string SpyName;
        public int Territory = -1;
    }

    /// <summary>
    /// Language AI order to an army: the goal, the destination (territory and, if any, the city), when it was given, the
    /// status ("a caminho", "chegou", "falhou: …", "cancelada") and until which turn the native AI leaves it alone.
    /// </summary>
    internal sealed class ArmyMission
    {
        public int Id;
        public int Empire;
        public string ArmyKey;
        public string Handle;
        /// <summary>mover, atacar, defender or parar.</summary>
        public string Goal;
        public int TargetTerritory = -1;
        public string TargetCityKey;
        public string TargetArmyKey;
        public string TargetLabel;
        public int Turn;
        public string Status;
        public int StatusTurn;
        /// <summary>Last turn in which the native AI leaves the army alone.</summary>
        public int HoldUntil = -1;
        /// <summary>Still in force (on the way, or arrived and holding the position).</summary>
        public bool Active;
    }

    /// <summary>Where a spy is and since when (turn it arrived in the territory).</summary>
    internal sealed class SpyTrack
    {
        public int Territory = -1;
        public int Since;
        public int LastSeen;
    }
}
