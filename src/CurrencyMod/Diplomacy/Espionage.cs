using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Letter interception by spies (design §8.3.1; research research\espionage-interception.md §3).
    /// - Private letters only. Ultimatums and public declarations go by formal envoy or the public square.
    /// - Who intercepts: an empire C that is neither end, with real spies (an army with an agent unit, hidden and able to
    ///   act; allies with shared covert operations do not act against each other) in a territory of the writer (risk
    ///   stored when the letter was written) or of the intended recipient (on the arrival turn).
    /// - The intercepted letter never arrives; the interceptor reads it whole. The sender never finds out.
    /// - Deterministic roll (game, letter, empire): reloading the save gives the same result if the spies did not move.
    ///   Each spy intercepts at most [IA] CartasInterceptadasPorEspiao letters per turn.
    /// </summary>
    internal static class Espionage
    {
        /// <summary>
        /// Simulated spies (command "ai espionage simulate ... keep"): only in the mod's memory, to test the roll
        /// without touching the game. They vanish on core reload or with "ai espionage clear".
        /// </summary>
        internal static readonly List<CapturedSpy> TestSpies = new List<CapturedSpy>();

        /// <summary>Latest rolls (for the "ai espionage" command): letter, empire, chance and number drawn.</summary>
        internal static readonly List<string> LastRolls = new List<string>();

        /// <summary>Spies from the turn snapshot plus the simulated ones.</summary>
        internal static IEnumerable<CapturedSpy> AllSpies(WorldCapture capture)
        {
            return TestSpies.Count == 0 ? capture.Spies : capture.Spies.Concat(TestSpies);
        }

        /// <summary>Weight of a spy against the owner of the territory it is in, and why.</summary>
        internal sealed class SpyWeight
        {
            public CapturedSpy Spy;
            public double Weight;
            /// <summary>"capital", "cidade" or "território".</summary>
            public string Place;
            /// <summary>"vigiando a capital", "vigiando uma cidade", "infiltrado", "infiltrando" or "presença".</summary>
            public string Mission;
        }

        // ---------------- Weights ----------------

        /// <summary>
        /// weight = place × mission × quality × cover × time (research table, §3). Null when the spy does not count:
        /// outside a major empire's territory, in its own territory, revealed or prevented from acting by an agreement.
        /// </summary>
        internal static SpyWeight Weigh(WorldCapture capture, AiWorld world, CapturedSpy spy)
        {
            int end = spy.TerritoryOwner;
            if (end < 0 || end == spy.Owner || !spy.CanAct)
            {
                return null;
            }
            CapturedEmpire target = capture.Empire(end);
            if (target == null || !target.Alive)
            {
                return null;
            }

            // Place: capital region 1; another city's region 0.5; outpost or loose territory 0.25.
            double local = 0.25;
            string place = "território";
            CapturedCity capital = null;
            foreach (CapturedCity city in target.Cities)
            {
                if (city.Capital)
                {
                    capital = city;
                }
                if (city.CenterTerritory != spy.Territory && !city.Territories.Contains(spy.Territory))
                {
                    continue;
                }
                double value = city.Capital ? 1.0 : city.IsCity ? 0.5 : 0.25;
                if (value > local)
                {
                    local = value;
                    place = city.Capital ? "capital" : "cidade";
                }
            }

            // Mission: watching the city yields the most (correspondence passes through the palace).
            double mission = 1.0;
            string missionName = "presença";
            bool againstEnd = spy.InfiltrationTarget == end;
            if (spy.WatchingCity && againstEnd)
            {
                bool watchingCapital = capital != null && spy.InfiltrationSettlement == capital.Key;
                mission = watchingCapital ? 2.5 : 2.0;
                missionName = watchingCapital ? "vigiando a capital" : "vigiando uma cidade";
            }
            else if (spy.Infiltration != null && againstEnd)
            {
                mission = spy.Infiltrated ? 1.3 : 1.1;
                missionName = spy.Infiltrated ? "infiltrado" : "infiltrando";
            }

            // Quality: more agents and veterans.
            double quality = Math.Min(2.0, (1 + 0.25 * Math.Max(0, spy.Agents - 1)) * (1 + 0.1 * Math.Max(0.0, spy.Veterancy)));

            // Cover: under detection it is worth less the closer it is to being revealed; the owner's high detection hinders.
            double coverage = spy.HostileDetection > 0 ? Math.Min(1.0, spy.TurnsBeforeRevealed / 4.0) : 1.0;
            coverage /= 1 + 0.15 * Math.Max(0.0, spy.OwnerDetection);

            // Time in place: a spy that just arrived does not know the messengers yet.
            int age = SpyAge(world, spy, capture.Turn);
            double time = age <= 0 ? 0.6 : age <= 2 ? 0.8 : 1.0;

            // Veteran Master of Whispers: the nation's spies weigh more (melhorias-jogando item 3).
            double weight = local * mission * quality * coverage * time * Council.VeteranEffects.Factor(spy.Owner, Council.VeteranEffects.Area.Spy);
            return weight > 0 ? new SpyWeight { Spy = spy, Weight = weight, Place = place, Mission = missionName } : null;
        }

        /// <summary>Exposure → chance at one end: Pmax × (1 − e^(−k × exposure)).</summary>
        internal static double Chance(double exposure)
        {
            double max = Clamp01(AiConfig.InterceptionMaxChance.Value);
            double k = Math.Max(0.01, AiConfig.InterceptionCurve.Value);
            return exposure <= 0 ? 0 : max * (1 - Math.Exp(-k * exposure));
        }

        /// <summary>
        /// Risk of each empire intercepting a letter at the "end" side (that empire's territories), excluding the letter's
        /// two ends. Only empires with a chance above zero.
        /// </summary>
        internal static List<InterceptRisk> Risks(WorldCapture capture, AiWorld world, int end, int from, int to)
        {
            var exposure = new Dictionary<int, double>();
            var best = new Dictionary<int, SpyWeight>();
            bool hunts = false;
            foreach (CapturedSpy spy in AllSpies(capture))
            {
                if (spy.TerritoryOwner != end || spy.Owner == from || spy.Owner == to)
                {
                    continue;
                }
                SpyWeight weight = Weigh(capture, world, spy);
                if (weight == null)
                {
                    continue;
                }
                hunts |= spy.OwnerHuntsSpies;
                exposure[spy.Owner] = (exposure.TryGetValue(spy.Owner, out double sum) ? sum : 0) + weight.Weight;
                if (!best.TryGetValue(spy.Owner, out SpyWeight current) || weight.Weight > current.Weight)
                {
                    best[spy.Owner] = weight;
                }
            }
            var risks = new List<InterceptRisk>();
            foreach (KeyValuePair<int, double> pair in exposure.OrderBy(p => p.Key))
            {
                double chance = Chance(pair.Value * (hunts ? 0.85 : 1.0));
                if (chance <= 0)
                {
                    continue;
                }
                SpyWeight spy = best[pair.Key];
                risks.Add(new InterceptRisk
                {
                    Empire = pair.Key,
                    Chance = chance,
                    SpyKey = spy.Spy.Key,
                    SpyName = spy.Spy.Name,
                    Territory = spy.Spy.Territory,
                });
            }
            return risks;
        }

        /// <summary>Stores the risk at the origin when the letter is written (the spies there may leave before delivery).</summary>
        internal static void StampOrigin(Letter letter, WorldCapture capture, AiWorld world)
        {
            letter.InterceptionChecked = letter.Type != "privada" || letter.IsPublic;
            if (letter.InterceptionChecked || capture == null || world == null || capture.Guid != world.GameGuid)
            {
                return;
            }
            try
            {
                TrackSpies(world, capture);
                List<InterceptRisk> risks = Risks(capture, world, letter.From, letter.From, letter.To);
                letter.OriginRisks = risks.Count > 0 ? risks : null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Interception risk for letter {letter.Id} not computed: {ex.Message}");
            }
        }

        // ---------------- Roll ----------------

        /// <summary>
        /// Checks the private letters arriving this turn (in id order) and decides which were intercepted.
        /// Needs this turn's snapshot. Returns the ones intercepted now.
        /// </summary>
        internal static List<Letter> Resolve(AiWorld world, WorldCapture capture, int turn)
        {
            var caught = new List<Letter>();
            if (world == null || capture == null || capture.Turn != turn || capture.Guid != world.GameGuid)
            {
                return caught;
            }
            List<Letter> pending = world.Letters.Where(l => !l.InterceptionChecked && l.DeliverTurn <= turn).OrderBy(l => l.Id).ToList();
            if (pending.Count == 0)
            {
                return caught;
            }
            TrackSpies(world, capture);
            Dictionary<int, int> budget = Budgets(world, capture, turn);
            double cap = Clamp01(AiConfig.InterceptionCap.Value);
            foreach (Letter letter in pending)
            {
                letter.InterceptionChecked = true;
                if (!AiConfig.Interception.Value || letter.IsPublic || letter.Type != "privada")
                {
                    continue;
                }
                List<InterceptRisk> origin = letter.OriginRisks ?? new List<InterceptRisk>();
                List<InterceptRisk> destination = Risks(capture, world, letter.To, letter.From, letter.To);
                foreach (Candidate candidate in Combine(origin, destination, cap))
                {
                    if (!budget.TryGetValue(candidate.Empire, out int left) || left <= 0)
                    {
                        Remember($"T{turn} letter {letter.Id} E{letter.From}→E{letter.To}: E{candidate.Empire} has no free spy this turn");
                        continue;
                    }
                    double roll = Roll(world.GameGuid, letter.Id, candidate.Empire);
                    Remember($"T{turn} letter {letter.Id} E{letter.From}→E{letter.To}: E{candidate.Empire} chance {candidate.Chance:0.00} (origin {candidate.Origin?.Chance ?? 0:0.00}, destination {candidate.Destination?.Chance ?? 0:0.00}) · rolled {roll:0.00} → {(roll < candidate.Chance ? "INTERCEPTED" : "passed")}");
                    if (roll >= candidate.Chance)
                    {
                        continue;
                    }
                    InterceptRisk via = candidate.ViaDestination ? candidate.Destination : candidate.Origin;
                    MarkIntercepted(letter, candidate.Empire, turn, via, candidate.ViaDestination ? "destino" : "origem");
                    budget[candidate.Empire] = left - 1;
                    caught.Add(letter);
                    break;
                }
            }
            return caught;
        }

        internal static void MarkIntercepted(Letter letter, int empire, int turn, InterceptRisk via, string end)
        {
            letter.InterceptionChecked = true;
            letter.InterceptedBy = empire;
            letter.InterceptedTurn = turn;
            letter.InterceptedTerritory = via?.Territory ?? -1;
            letter.InterceptedSpy = via?.SpyName;
            letter.InterceptedSpyKey = via?.SpyKey;
            letter.InterceptedVia = end;
            letter.InterceptorRead = false;
        }

        private sealed class Candidate
        {
            public int Empire;
            public double Chance;
            public InterceptRisk Origin;
            public InterceptRisk Destination;
            public bool ViaDestination;
        }

        /// <summary>p = 1 − (1 − p_origin)(1 − p_destination), capped; in chance order (tie: lowest index).</summary>
        private static IEnumerable<Candidate> Combine(List<InterceptRisk> origin, List<InterceptRisk> destination, double cap)
        {
            var all = new Dictionary<int, Candidate>();
            foreach (InterceptRisk risk in origin)
            {
                all[risk.Empire] = new Candidate { Empire = risk.Empire, Origin = risk };
            }
            foreach (InterceptRisk risk in destination)
            {
                if (!all.TryGetValue(risk.Empire, out Candidate candidate))
                {
                    candidate = new Candidate { Empire = risk.Empire };
                    all[risk.Empire] = candidate;
                }
                candidate.Destination = risk;
            }
            foreach (Candidate candidate in all.Values)
            {
                double a = candidate.Origin?.Chance ?? 0;
                double b = candidate.Destination?.Chance ?? 0;
                candidate.Chance = Math.Min(cap, 1 - (1 - a) * (1 - b));
                candidate.ViaDestination = b > a;
            }
            return all.Values.Where(c => c.Chance > 0).OrderByDescending(c => c.Chance).ThenBy(c => c.Empire).ToList();
        }

        /// <summary>Letters each empire can still intercept this turn: active spies × limit per spy.</summary>
        private static Dictionary<int, int> Budgets(AiWorld world, WorldCapture capture, int turn)
        {
            int perSpy = Math.Max(1, AiConfig.InterceptionPerSpy.Value);
            var budget = new Dictionary<int, int>();
            foreach (CapturedSpy spy in AllSpies(capture))
            {
                if (Weigh(capture, world, spy) != null)
                {
                    budget[spy.Owner] = (budget.TryGetValue(spy.Owner, out int value) ? value : 0) + perSpy;
                }
            }
            // Spies that left the origin after the letter was written still count for the letter they saw leave.
            foreach (Letter letter in world.Letters.Where(l => !l.InterceptionChecked && l.DeliverTurn <= turn && l.OriginRisks != null))
            {
                foreach (InterceptRisk risk in letter.OriginRisks)
                {
                    if (!budget.ContainsKey(risk.Empire))
                    {
                        budget[risk.Empire] = perSpy;
                    }
                }
            }
            foreach (Letter letter in world.Letters.Where(l => l.InterceptedTurn == turn && l.Intercepted))
            {
                if (budget.ContainsKey(letter.InterceptedBy))
                {
                    budget[letter.InterceptedBy]--;
                }
            }
            return budget;
        }

        /// <summary>Number in [0, 1) fixed for (game, letter, empire): 64-bit FNV-1a.</summary>
        internal static double Roll(string guid, int letterId, int empire)
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes($"{guid}|{letterId}|{empire}|interceptacao"))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
            return (hash >> 11) / (double)(1UL << 53);
        }

        // ---------------- Where the spies are ----------------

        /// <summary>Updates, once per turn, how long each spy has been in its current territory.</summary>
        internal static void TrackSpies(AiWorld world, WorldCapture capture)
        {
            if (world == null || capture == null || world.SpyTracksTurn == capture.Turn)
            {
                return;
            }
            // First time (old game): spies already in the field count as settled.
            bool first = world.SpyTracksTurn < 0;
            foreach (CapturedSpy spy in AllSpies(capture))
            {
                if (spy.Key == null)
                {
                    continue;
                }
                if (!world.SpyTracks.TryGetValue(spy.Key, out SpyTrack track) || track.Territory != spy.Territory)
                {
                    world.SpyTracks[spy.Key] = new SpyTrack { Territory = spy.Territory, Since = first ? capture.Turn - 3 : capture.Turn, LastSeen = capture.Turn };
                }
                else
                {
                    track.LastSeen = capture.Turn;
                }
            }
            foreach (string gone in world.SpyTracks.Where(p => p.Value.LastSeen < capture.Turn - 1).Select(p => p.Key).ToList())
            {
                world.SpyTracks.Remove(gone);
            }
            world.SpyTracksTurn = capture.Turn;
        }

        private static int SpyAge(AiWorld world, CapturedSpy spy, int turn)
        {
            if (world != null && spy.Key != null && world.SpyTracks.TryGetValue(spy.Key, out SpyTrack track) && track.Territory == spy.Territory)
            {
                return turn - track.Since;
            }
            return 0;
        }

        // ---------------- Texts ----------------

        /// <summary>Event log line (viewer and log).</summary>
        internal static string Describe(WorldCapture capture, Letter letter)
        {
            string where = letter.InterceptedTerritory >= 0 ? capture?.TerritoryName(letter.InterceptedTerritory) ?? "T" + letter.InterceptedTerritory : "?";
            return $"Letter {letter.Id} from E{letter.From} to E{letter.To} intercepted by E{letter.InterceptedBy} ({letter.InterceptedVia}: {letter.InterceptedSpy ?? "spy"} in {where}).";
        }

        /// <summary>Qualitative coverage of a chance (for the dossier and the UI).</summary>
        internal static string Level(double chance)
        {
            return chance >= 0.3 ? "alta" : chance >= 0.15 ? "média" : chance > 0 ? "baixa" : "nenhuma";
        }

        private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

        private static void Remember(string line)
        {
            LastRolls.Add(line);
            if (LastRolls.Count > 60)
            {
                LastRolls.RemoveRange(0, LastRolls.Count - 60);
            }
        }

        /// <summary>Table for the "ai espionage" command: spies, weights and each empire's chance at each end.</summary>
        internal static string Report(AiWorld world, WorldCapture capture)
        {
            var text = new StringBuilder();
            text.AppendLine($"turn {capture.Turn} · {capture.Spies.Count} spy(ies){(TestSpies.Count > 0 ? $" + {TestSpies.Count} simulated" : string.Empty)} · interception {(AiConfig.Interception.Value ? "on" : "off")} · Pmax {AiConfig.InterceptionMaxChance.Value:0.00} k {AiConfig.InterceptionCurve.Value:0.00} cap {AiConfig.InterceptionCap.Value:0.00}");
            foreach (CapturedSpy spy in AllSpies(capture).OrderBy(s => s.Owner))
            {
                SpyWeight weight = Weigh(capture, world, spy);
                string owner = spy.TerritoryOwner >= 0 ? "E" + spy.TerritoryOwner : "no owner";
                string infiltration = spy.Infiltration != null ? $" · {spy.Infiltration} against E{spy.InfiltrationTarget}{(spy.Infiltrated ? " (done)" : $" ({spy.InfiltrationTurnsLeft} turns)")}" : string.Empty;
                string revealed = spy.TurnsBeforeRevealed == int.MaxValue ? "not detected" : $"revealed in {spy.TurnsBeforeRevealed}";
                text.AppendLine($"E{spy.Owner} {spy.Name ?? spy.Key} in {capture.TerritoryName(spy.Territory)} (T{spy.Territory}, {owner}) · agents {spy.Agents} vet {spy.Veterancy:0.#} · stealth {spy.Stealth:0.#}/{spy.StealthMax:0.#} · hidden {(spy.Hidden ? "yes" : "no")} acts {(spy.CanAct ? "yes" : "no")} · detection owner {spy.OwnerDetection:0.##} hostile {spy.HostileDetection:0.##} ({revealed}){(spy.WatchingCity ? " · watching" : string.Empty)}{infiltration} → {(weight != null ? $"weight {weight.Weight:0.###} ({weight.Place}, {weight.Mission})" : "does not count")}");
            }
            text.AppendLine();
            text.AppendLine("Chance per end (who intercepts → whose territory):");
            foreach (int end in capture.AliveEmpires())
            {
                List<InterceptRisk> risks = Risks(capture, world, end, -1, -1);
                if (risks.Count > 0)
                {
                    text.AppendLine($"  in E{end}: " + string.Join(", ", risks.Select(r => $"E{r.Empire} {r.Chance:0.00} ({r.SpyName ?? r.SpyKey})")));
                }
            }
            if (capture.StealthActivity.Count > 0)
            {
                text.AppendLine("Stealth activity flagged by the game: " + string.Join(", ", capture.StealthActivity.Select(t => $"{capture.TerritoryName(t)} (E{capture.OwnerOf(t)})")));
            }
            List<Letter> recent = world.Letters.Where(l => l.Intercepted && l.InterceptedTurn >= capture.Turn - 10).OrderBy(l => l.Id).ToList();
            text.AppendLine();
            text.AppendLine($"Intercepted in the last 10 turns: {recent.Count}");
            foreach (Letter letter in recent)
            {
                text.AppendLine($"  T{letter.InterceptedTurn} " + Describe(capture, letter) + (letter.InterceptorRead ? " (read)" : " (unread)"));
            }
            List<Letter> waiting = world.Letters.Where(l => !l.InterceptionChecked).OrderBy(l => l.Id).ToList();
            text.AppendLine($"Private letters still on the way (rolled on arrival): {waiting.Count}");
            foreach (Letter letter in waiting)
            {
                string risks = letter.OriginRisks != null && letter.OriginRisks.Count > 0
                    ? string.Join(", ", letter.OriginRisks.Select(r => $"E{r.Empire} {r.Chance:0.00}"))
                    : "none";
                text.AppendLine($"  letter {letter.Id} E{letter.From}→E{letter.To} · written T{letter.SentTurn} · arrives T{letter.DeliverTurn} · risk at origin: {risks}");
            }
            if (LastRolls.Count > 0)
            {
                text.AppendLine("Last rolls:");
                foreach (string line in LastRolls.Skip(Math.Max(0, LastRolls.Count - 20)))
                {
                    text.AppendLine("  " + line);
                }
            }
            text.AppendLine();
            text.AppendLine("Detection in the capitals: " +string.Join(", ", capture.AliveEmpires()
                .Select(e => capture.Empire(e)?.Cities.FirstOrDefault(c => c.Capital))
                .Where(c => c != null && c.CenterTerritory >= 0 && c.CenterTerritory < capture.OwnerDetection.Length)
                .Select(c => $"{c.Name} (E{capture.OwnerOf(c.CenterTerritory)}, T{c.CenterTerritory}) {capture.OwnerDetection[c.CenterTerritory]:0.##}")));
            var traffic = world.Letters.Where(l => !l.IsPublic && l.Type == "privada" && l.SentTurn >= capture.Turn - 10)
                .SelectMany(l => new[] { l.From, l.To }).GroupBy(e => e).OrderByDescending(g => g.Count()).Select(g => $"E{g.Key} {g.Count()}");
            text.AppendLine("Private letters in the last 10 turns (written + received): " +string.Join(", ", traffic));
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// "ai espionage simulate E# T# [watch] [keep]": weight and chance of a test spy (1 agent, stealth 8) in a
        /// foreign territory, with the owner's real detection there. "keep" keeps the spy active in the roll.
        /// </summary>
        internal static string Simulate(AiWorld world, WorldCapture capture, int owner, int territory, bool watch, bool keep)
        {
            int end = capture.OwnerOf(territory);
            if (end < 0)
            {
                return "error: territory has no owner (or belongs to an independent people)";
            }
            if (end == owner)
            {
                return "error: the spy must be in a foreign territory";
            }
            CapturedCity capital = capture.Empire(end)?.Cities.FirstOrDefault(c => c.Capital);
            float detection = territory < capture.OwnerDetection.Length ? capture.OwnerDetection[territory] : 0f;
            var spy = new CapturedSpy
            {
                Owner = owner,
                Key = $"test-{owner}-{territory}",
                Name = "Test spy",
                Territory = territory,
                TerritoryOwner = end,
                Agents = 1,
                Stealth = 8,
                StealthMax = 8,
                Hidden = true,
                CanAct = true,
                OwnerDetection = detection,
                HostileDetection = detection,
                TurnsBeforeRevealed = detection > 0 ? (int)Math.Ceiling(8 / detection) : int.MaxValue,
            };
            bool watching = watch && capital != null && (capital.CenterTerritory == territory || capital.Territories.Contains(territory));
            if (watching)
            {
                spy.WatchingCity = true;
                spy.Infiltration = "WatchCity";
                spy.InfiltrationTarget = end;
                spy.InfiltrationSettlement = capital.Key;
                spy.Infiltrated = true;
            }
            world.SpyTracks[spy.Key] = new SpyTrack { Territory = territory, Since = capture.Turn - 3, LastSeen = capture.Turn };
            SpyWeight weight = Weigh(capture, world, spy);
            double chance = Chance((weight?.Weight ?? 0) * (spy.OwnerHuntsSpies ? 0.85 : 1.0));
            if (keep)
            {
                TestSpies.RemoveAll(s => s.Key == spy.Key);
                TestSpies.Add(spy);
            }
            return $"test spy of E{owner} in {capture.TerritoryName(territory)} (T{territory}, of E{end}){(watch && !watching ? " (watch only counts in the capital's region)" : string.Empty)}: owner detection {detection:0.##}"
                + $" → weight {(weight != null ? $"{weight.Weight:0.###} ({weight.Place}, {weight.Mission})" : "0")} → chance at one end {chance:0.00}"
                + (keep ? " · kept: counts in the roll until 'ai espionage clear' or the core reload" : string.Empty);
        }
    }
}
