using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacia.Capture;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Interceptação de cartas por espiões (design §8.3.1; pesquisa research\espionage-interception.md §3).
    /// - Só cartas privadas. Ultimatos e declarações públicas vão por enviado formal ou praça pública.
    /// - Quem intercepta: um império C que não é nenhuma das pontas, com espiões de verdade (exército com unidade agente,
    ///   oculto e capaz de agir; aliados com operações furtivas compartilhadas não agem um contra o outro) num território
    ///   de quem escreveu (risco guardado quando a carta foi escrita) ou de quem ia receber (no turno da chegada).
    /// - A carta interceptada nunca chega; quem interceptou lê inteira. O remetente não fica sabendo.
    /// - Sorteio determinístico (partida, carta, império): recarregar o save dá o mesmo resultado se os espiões não se
    ///   moveram. Cada espião intercepta no máximo [IA] CartasInterceptadasPorEspiao cartas por turno.
    /// </summary>
    internal static class Espionage
    {
        /// <summary>
        /// Espiões simulados (comando "ia espionagem simular ... fixar"): só na memória do mod, para testar o sorteio
        /// sem mexer no jogo. Somem na recarga do núcleo ou com "ia espionagem limpar".
        /// </summary>
        internal static readonly List<CapturedSpy> TestSpies = new List<CapturedSpy>();

        /// <summary>Últimos sorteios (para o comando "ia espionagem"): carta, império, chance e número tirado.</summary>
        internal static readonly List<string> LastRolls = new List<string>();

        /// <summary>Espiões da foto do turno mais os simulados.</summary>
        internal static IEnumerable<CapturedSpy> AllSpies(WorldCapture capture)
        {
            return TestSpies.Count == 0 ? capture.Spies : capture.Spies.Concat(TestSpies);
        }

        /// <summary>Peso de um espião contra o dono do território onde ele está, e por quê.</summary>
        internal sealed class SpyWeight
        {
            public CapturedSpy Spy;
            public double Weight;
            /// <summary>"capital", "cidade" ou "território".</summary>
            public string Place;
            /// <summary>"vigiando a capital", "vigiando uma cidade", "infiltrado", "infiltrando" ou "presença".</summary>
            public string Mission;
        }

        // ---------------- Pesos ----------------

        /// <summary>
        /// peso = local × missão × qualidade × cobertura × tempo (tabela da pesquisa, §3). Null quando o espião não conta:
        /// fora de território de império maior, no próprio território, revelado ou impedido de agir por acordo.
        /// </summary>
        internal static SpyWeight Weigh(WorldCapture capture, IaWorld world, CapturedSpy spy)
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

            // Local: região da capital 1; região de outra cidade 0,5; posto avançado ou território solto 0,25.
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

            // Missão: vigiar a cidade é o que mais rende (a correspondência passa pelo palácio).
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

            // Qualidade: mais agentes e veteranos.
            double quality = Math.Min(2.0, (1 + 0.25 * Math.Max(0, spy.Agents - 1)) * (1 + 0.1 * Math.Max(0.0, spy.Veterancy)));

            // Cobertura: sob detecção vale menos quanto mais perto de ser revelado; detecção alta do dono atrapalha.
            double coverage = spy.HostileDetection > 0 ? Math.Min(1.0, spy.TurnsBeforeRevealed / 4.0) : 1.0;
            coverage /= 1 + 0.15 * Math.Max(0.0, spy.OwnerDetection);

            // Tempo no lugar: quem acabou de chegar ainda não conhece os mensageiros.
            int age = SpyAge(world, spy, capture.Turn);
            double time = age <= 0 ? 0.6 : age <= 2 ? 0.8 : 1.0;

            double weight = local * mission * quality * coverage * time;
            return weight > 0 ? new SpyWeight { Spy = spy, Weight = weight, Place = place, Mission = missionName } : null;
        }

        /// <summary>Exposição → chance numa ponta: Pmax × (1 − e^(−k × exposição)).</summary>
        internal static double Chance(double exposure)
        {
            double max = Clamp01(IaConfig.InterceptionMaxChance.Value);
            double k = Math.Max(0.01, IaConfig.InterceptionCurve.Value);
            return exposure <= 0 ? 0 : max * (1 - Math.Exp(-k * exposure));
        }

        /// <summary>
        /// Risco de cada império interceptar uma carta na ponta "end" (os territórios desse império), tirando as duas
        /// pontas da carta. Só impérios com chance acima de zero.
        /// </summary>
        internal static List<InterceptRisk> Risks(WorldCapture capture, IaWorld world, int end, int from, int to)
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

        /// <summary>Guarda o risco na origem quando a carta é escrita (os espiões de lá podem sair antes da entrega).</summary>
        internal static void StampOrigin(Letter letter, WorldCapture capture, IaWorld world)
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
                Plugin.Log.LogWarning($"[IA] Risco de interceptação da carta {letter.Id} não calculado: {ex.Message}");
            }
        }

        // ---------------- Sorteio ----------------

        /// <summary>
        /// Confere as cartas privadas que chegam neste turno (em ordem de id) e decide quais foram interceptadas.
        /// Precisa da foto deste turno. Devolve as interceptadas agora.
        /// </summary>
        internal static List<Letter> Resolve(IaWorld world, WorldCapture capture, int turn)
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
            double cap = Clamp01(IaConfig.InterceptionCap.Value);
            foreach (Letter letter in pending)
            {
                letter.InterceptionChecked = true;
                if (!IaConfig.Interception.Value || letter.IsPublic || letter.Type != "privada")
                {
                    continue;
                }
                List<InterceptRisk> origin = letter.OriginRisks ?? new List<InterceptRisk>();
                List<InterceptRisk> destination = Risks(capture, world, letter.To, letter.From, letter.To);
                foreach (Candidate candidate in Combine(origin, destination, cap))
                {
                    if (!budget.TryGetValue(candidate.Empire, out int left) || left <= 0)
                    {
                        Remember($"T{turn} carta {letter.Id} E{letter.From}→E{letter.To}: E{candidate.Empire} sem espião livre neste turno");
                        continue;
                    }
                    double roll = Roll(world.GameGuid, letter.Id, candidate.Empire);
                    Remember($"T{turn} carta {letter.Id} E{letter.From}→E{letter.To}: E{candidate.Empire} chance {candidate.Chance:0.00} (origem {candidate.Origin?.Chance ?? 0:0.00}, destino {candidate.Destination?.Chance ?? 0:0.00}) · tirou {roll:0.00} → {(roll < candidate.Chance ? "INTERCEPTADA" : "passou")}");
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

        /// <summary>p = 1 − (1 − p_origem)(1 − p_destino), com teto; em ordem de chance (empate: menor índice).</summary>
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

        /// <summary>Cartas que cada império ainda pode interceptar neste turno: espiões em ação × limite por espião.</summary>
        private static Dictionary<int, int> Budgets(IaWorld world, WorldCapture capture, int turn)
        {
            int perSpy = Math.Max(1, IaConfig.InterceptionPerSpy.Value);
            var budget = new Dictionary<int, int>();
            foreach (CapturedSpy spy in AllSpies(capture))
            {
                if (Weigh(capture, world, spy) != null)
                {
                    budget[spy.Owner] = (budget.TryGetValue(spy.Owner, out int value) ? value : 0) + perSpy;
                }
            }
            // Espiões que saíram da origem depois que a carta foi escrita ainda contam pela carta que viram sair.
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

        /// <summary>Número em [0, 1) fixo para (partida, carta, império): FNV-1a de 64 bits.</summary>
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

        // ---------------- Onde estão os espiões ----------------

        /// <summary>Atualiza, uma vez por turno, há quanto tempo cada espião está no território atual.</summary>
        internal static void TrackSpies(IaWorld world, WorldCapture capture)
        {
            if (world == null || capture == null || world.SpyTracksTurn == capture.Turn)
            {
                return;
            }
            // Primeira vez (partida antiga): quem já estava em campo conta como instalado.
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

        private static int SpyAge(IaWorld world, CapturedSpy spy, int turn)
        {
            if (world != null && spy.Key != null && world.SpyTracks.TryGetValue(spy.Key, out SpyTrack track) && track.Territory == spy.Territory)
            {
                return turn - track.Since;
            }
            return 0;
        }

        // ---------------- Textos ----------------

        /// <summary>Linha do registro de eventos (visualizador e log).</summary>
        internal static string Describe(WorldCapture capture, Letter letter)
        {
            string where = letter.InterceptedTerritory >= 0 ? capture?.TerritoryName(letter.InterceptedTerritory) ?? "T" + letter.InterceptedTerritory : "?";
            return $"Carta {letter.Id} de E{letter.From} para E{letter.To} interceptada por E{letter.InterceptedBy} ({letter.InterceptedVia}: {letter.InterceptedSpy ?? "espião"} em {where}).";
        }

        /// <summary>Cobertura qualitativa de uma chance (para o dossiê e a interface).</summary>
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

        /// <summary>Tabela para o comando "ia espionagem": espiões, pesos e chance de cada império em cada ponta.</summary>
        internal static string Report(IaWorld world, WorldCapture capture)
        {
            var text = new StringBuilder();
            text.AppendLine($"turno {capture.Turn} · {capture.Spies.Count} espião(ões){(TestSpies.Count > 0 ? $" + {TestSpies.Count} simulado(s)" : string.Empty)} · interceptação {(IaConfig.Interception.Value ? "ligada" : "desligada")} · Pmax {IaConfig.InterceptionMaxChance.Value:0.00} k {IaConfig.InterceptionCurve.Value:0.00} teto {IaConfig.InterceptionCap.Value:0.00}");
            foreach (CapturedSpy spy in AllSpies(capture).OrderBy(s => s.Owner))
            {
                SpyWeight weight = Weigh(capture, world, spy);
                string owner = spy.TerritoryOwner >= 0 ? "E" + spy.TerritoryOwner : "sem dono";
                string infiltration = spy.Infiltration != null ? $" · {spy.Infiltration} contra E{spy.InfiltrationTarget}{(spy.Infiltrated ? " (concluída)" : $" ({spy.InfiltrationTurnsLeft} turnos)")}" : string.Empty;
                string revealed = spy.TurnsBeforeRevealed == int.MaxValue ? "sem detecção" : $"revelado em {spy.TurnsBeforeRevealed}";
                text.AppendLine($"E{spy.Owner} {spy.Name ?? spy.Key} em {capture.TerritoryName(spy.Territory)} (T{spy.Territory}, {owner}) · agentes {spy.Agents} vet {spy.Veterancy:0.#} · furtividade {spy.Stealth:0.#}/{spy.StealthMax:0.#} · oculto {(spy.Hidden ? "sim" : "não")} age {(spy.CanAct ? "sim" : "não")} · detecção dono {spy.OwnerDetection:0.##} hostil {spy.HostileDetection:0.##} ({revealed}){(spy.WatchingCity ? " · vigiando" : string.Empty)}{infiltration} → {(weight != null ? $"peso {weight.Weight:0.###} ({weight.Place}, {weight.Mission})" : "não conta")}");
            }
            text.AppendLine();
            text.AppendLine("Chance por ponta (quem intercepta → território de quem):");
            foreach (int end in capture.AliveEmpires())
            {
                List<InterceptRisk> risks = Risks(capture, world, end, -1, -1);
                if (risks.Count > 0)
                {
                    text.AppendLine($"  em E{end}: " + string.Join(", ", risks.Select(r => $"E{r.Empire} {r.Chance:0.00} ({r.SpyName ?? r.SpyKey})")));
                }
            }
            if (capture.StealthActivity.Count > 0)
            {
                text.AppendLine("Atividade furtiva marcada pelo jogo: " + string.Join(", ", capture.StealthActivity.Select(t => $"{capture.TerritoryName(t)} (E{capture.OwnerOf(t)})")));
            }
            List<Letter> recent = world.Letters.Where(l => l.Intercepted && l.InterceptedTurn >= capture.Turn - 10).OrderBy(l => l.Id).ToList();
            text.AppendLine();
            text.AppendLine($"Interceptadas nos últimos 10 turnos: {recent.Count}");
            foreach (Letter letter in recent)
            {
                text.AppendLine($"  T{letter.InterceptedTurn} " + Describe(capture, letter) + (letter.InterceptorRead ? " (lida)" : " (não lida)"));
            }
            List<Letter> waiting = world.Letters.Where(l => !l.InterceptionChecked).OrderBy(l => l.Id).ToList();
            text.AppendLine($"Cartas privadas ainda a caminho (sorteio na chegada): {waiting.Count}");
            foreach (Letter letter in waiting)
            {
                string risks = letter.OriginRisks != null && letter.OriginRisks.Count > 0
                    ? string.Join(", ", letter.OriginRisks.Select(r => $"E{r.Empire} {r.Chance:0.00}"))
                    : "nenhum";
                text.AppendLine($"  carta {letter.Id} E{letter.From}→E{letter.To} · escrita T{letter.SentTurn} · chega T{letter.DeliverTurn} · risco na origem: {risks}");
            }
            if (LastRolls.Count > 0)
            {
                text.AppendLine("Últimos sorteios:");
                foreach (string line in LastRolls.Skip(Math.Max(0, LastRolls.Count - 20)))
                {
                    text.AppendLine("  " + line);
                }
            }
            text.AppendLine();
            text.AppendLine("Detecção nas capitais: " + string.Join(", ", capture.AliveEmpires()
                .Select(e => capture.Empire(e)?.Cities.FirstOrDefault(c => c.Capital))
                .Where(c => c != null && c.CenterTerritory >= 0 && c.CenterTerritory < capture.OwnerDetection.Length)
                .Select(c => $"{c.Name} (E{capture.OwnerOf(c.CenterTerritory)}, T{c.CenterTerritory}) {capture.OwnerDetection[c.CenterTerritory]:0.##}")));
            var traffic = world.Letters.Where(l => !l.IsPublic && l.Type == "privada" && l.SentTurn >= capture.Turn - 10)
                .SelectMany(l => new[] { l.From, l.To }).GroupBy(e => e).OrderByDescending(g => g.Count()).Select(g => $"E{g.Key} {g.Count()}");
            text.AppendLine("Cartas privadas nos últimos 10 turnos (escritas + recebidas): " + string.Join(", ", traffic));
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// "ia espionagem simular E# T# [vigiar] [fixar]": peso e chance de um espião de teste (1 agente, furtividade 8)
        /// num território estrangeiro, com a detecção real do dono ali. "fixar" deixa o espião valendo no sorteio.
        /// </summary>
        internal static string Simulate(IaWorld world, WorldCapture capture, int owner, int territory, bool watch, bool keep)
        {
            int end = capture.OwnerOf(territory);
            if (end < 0)
            {
                return "erro: território sem dono (ou de povo independente)";
            }
            if (end == owner)
            {
                return "erro: o espião precisa estar em território estrangeiro";
            }
            CapturedCity capital = capture.Empire(end)?.Cities.FirstOrDefault(c => c.Capital);
            float detection = territory < capture.OwnerDetection.Length ? capture.OwnerDetection[territory] : 0f;
            var spy = new CapturedSpy
            {
                Owner = owner,
                Key = $"teste-{owner}-{territory}",
                Name = "Espião de teste",
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
            return $"espião de teste de E{owner} em {capture.TerritoryName(territory)} (T{territory}, de E{end}){(watch && !watching ? " (vigiar só vale na região da capital)" : string.Empty)}: detecção do dono {detection:0.##}"
                + $" → peso {(weight != null ? $"{weight.Weight:0.###} ({weight.Place}, {weight.Mission})" : "0")} → chance numa ponta {chance:0.00}"
                + (keep ? " · fixado: vale no sorteio até 'ia espionagem limpar' ou a recarga do núcleo" : string.Empty);
        }
    }
}
