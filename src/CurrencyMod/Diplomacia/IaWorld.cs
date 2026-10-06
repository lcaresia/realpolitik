using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Estado da diplomacia com IA de uma partida: personas, diários, sentimentos, memórias, cartas e gasto.
    /// Vai dentro do save (DiplomaciaIA.json). Só a thread principal mexe nele.
    /// </summary>
    internal sealed class IaWorld
    {
        /// <summary>2 = turnos contados como o jogo mostra (a versão 1 contava um a mais); 3 = postura e foco guardados
        /// como estado atual da nação (antes só no histórico de ações); 4 = cartas ligadas à carta que respondem;
        /// 5 = interceptação de cartas por espiões.</summary>
        public int Version = 5;
        public string GameGuid;
        public double TotalCostUsd;
        public int TotalCalls;
        public long TotalPromptTokens;
        public long TotalCacheHitTokens;
        public long TotalCompletionTokens;
        public long TotalReasoningTokens;
        public List<IaNation> Nations = new List<IaNation>();
        public List<Letter> Letters = new List<Letter>();
        public int NextLetterId = 1;
        public Dictionary<string, string> CityHandles = new Dictionary<string, string>();
        public Dictionary<string, string> ArmyHandles = new Dictionary<string, string>();
        public int NextCityHandle = 1;
        public int NextArmyHandle = 1;
        public int NextActionId = 1;
        /// <summary>Último nome bom de cada império (líder e nome completo). O jogo às vezes entrega o líder do jogador
        /// como "0", quando o apelido da Steam ainda não carregou.</summary>
        public Dictionary<int, string> LeaderNames = new Dictionary<int, string>();
        public Dictionary<int, string> FullNames = new Dictionary<int, string>();
        /// <summary>Onde cada espião (chave = GUID do exército) está e desde que turno: o jogo só guarda isso para
        /// infiltrações, e espião recém-chegado ainda não conhece o lugar (peso menor na interceptação).</summary>
        public Dictionary<string, SpyTrack> SpyTracks = new Dictionary<string, SpyTrack>();
        public int SpyTracksTurn = -1;
        /// <summary>Ordens a exércitos dadas pela IA de linguagem (ordem_exercito): destino, situação e até quando a IA
        /// nativa deixa o exército em paz.</summary>
        public List<ArmyMission> ArmyMissions = new List<ArmyMission>();
        /// <summary>Reuniões do conselho do jogador (design §10.6), uma por turno, com as respostas dele.</summary>
        public List<Council.CouncilMeeting> PlayerCouncil = new List<Council.CouncilMeeting>();
        /// <summary>Relações do jogador no turno anterior (guerra, paz, acordos), para ver se ele seguiu o conselho.</summary>
        public Dictionary<int, string> PlayerRelationMemo = new Dictionary<int, string>();
        public int PlayerRelationMemoTurn = -1;
        /// <summary>Resultados do Congresso mundial (leis votadas e crises julgadas), os mais recentes no fim. O jogo apaga a
        /// crise do pool logo depois do resultado, então a notícia fica guardada aqui.</summary>
        public List<CongressNews> CongressNews = new List<CongressNews>();

        /// <summary>A missão em vigor de um exército (a mais recente), ou null.</summary>
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

        public IaNation Get(int empireIndex)
        {
            foreach (IaNation nation in Nations)
            {
                if (nation.EmpireIndex == empireIndex)
                {
                    return nation;
                }
            }
            return null;
        }

        public IaNation Ensure(int empireIndex)
        {
            IaNation nation = Get(empireIndex);
            if (nation == null)
            {
                nation = new IaNation { EmpireIndex = empireIndex };
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

        /// <summary>GUID do jogo por trás de um código C# ou A# (0 se não existir).</summary>
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

        /// <summary>Troca nomes ruins da foto ("Governante Supremo 0") pelos últimos nomes bons e guarda os bons.</summary>
        public void FixNames(Capture.WorldCapture capture)
        {
            foreach (Capture.CapturedEmpire empire in capture.Empires)
            {
                if (empire == null)
                {
                    continue;
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
                    // Sem nome bom guardado: fica só o título ("Governante Supremo (Os Francos)").
                    empire.FullName = empire.FullName.Replace(" " + empire.Leader.Trim() + " ", " ");
                    empire.Leader = null;
                }
            }
        }

        /// <summary>Atualiza estados salvos por versões anteriores do mod.</summary>
        public void Migrate()
        {
            if (Version < 2)
            {
                // Versão 1 usava CurrentTurn + 1; o jogo mostra CurrentTurn. Volta tudo um turno.
                foreach (IaNation nation in Nations)
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
                // Postura e foco viravam só registro: recupera o último valor de cada um no histórico de ações.
                foreach (IaNation nation in Nations)
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
                // Respostas: refaz as cadeias em ordem, com a regra de hoje (cada carta responde a mais recente do
                // destinatário que pedia resposta, já tinha chegado e ainda não tinha resposta).
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
                // Interceptação: nenhuma carta antiga é sorteada (nem as que ainda estão a caminho).
                foreach (Letter letter in Letters)
                {
                    letter.InterceptionChecked = true;
                }
                Version = 5;
            }
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

        public static IaWorld FromJson(string json) => JsonConvert.DeserializeObject<IaWorld>(json);
    }

    internal sealed class IaNation
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
        /// <summary>Postura atual com cada nação (definir_postura); vale até mudar e guia a IA nativa (design §11.1).</summary>
        public Dictionary<int, Stance> Postures = new Dictionary<int, Stance>();
        /// <summary>Foco atual do império (definir_foco); vale até mudar.</summary>
        public Stance Focus;
        /// <summary>Política comercial com cada nação (politica_comercial: livre, pedagio ou bloqueio) em todos os postos; a IA
        /// nativa de comércio (TradeAi) não mexe enquanto a IA de linguagem decide pela nação.</summary>
        public Dictionary<int, Stance> TradeStances = new Dictionary<int, Stance>();
        /// <summary>Patrocínio escolhido em cada povo independente (patrocinar): índice do povo → (dinheiro &lt;&lt; 8) | influência.
        /// A IA nativa não mexe nesses povos enquanto a IA de linguagem decide pela nação.</summary>
        public Dictionary<int, int> MinorPatronage = new Dictionary<int, int>();
        /// <summary>Conselho de ministros (design §10): a Mão e as 11 pastas.</summary>
        public List<Council.Minister> Council = new List<Council.Minister>();
        public int CouncilTurn = -1;
        /// <summary>Personalidades demitidas por esta nação (não voltam para ela).</summary>
        public List<string> FiredPersonalities = new List<string>();
        /// <summary>Congresso: o que os governadores fazem se a lei imposta chegar (cívico → "adotar" ou "recusar"), do
        /// campo se_perder de votar_congresso.</summary>
        public Dictionary<string, string> CongressStances = new Dictionary<string, string>();
        /// <summary>Congresso: a opção que a nação votou em cada lei (cívico → nome da escolha). Votou na vencedora = adota a
        /// lei imposta (quem não tem a lei também recebe a lei imposta, mesmo tendo votado nela).</summary>
        public Dictionary<string, string> CongressVotes = new Dictionary<string, string>();

        /// <summary>
        /// Guarda definir_postura / definir_foco como estado atual. Devolve a descrição do que mudou, ou null se a ação
        /// não é dessas ou veio sem os campos.
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
            // Lista ou objeto no "motivo" faria o cast (string) lançar no meio do Apply.
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

    /// <summary>Postura com uma nação ou foco do império: o valor, desde quando vale e o motivo dado.</summary>
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
        /// <summary>Id da ordem no executor (0 = ação que não vira ordem do jogo).</summary>
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
        /// <summary>Índice do destinatário, ou Everyone para declaração pública.</summary>
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
        /// Carta que esta responde (-1 = nenhuma). Vem do botão Responder do jogador ou, sem ele, da carta mais recente
        /// do destinatário que pedia resposta e ainda não tinha sido respondida (vale também para as IAs).
        /// </summary>
        public int InReplyTo = -1;
        /// <summary>Por quem a carta já foi lida no dossiê (uma pública é lida por vários).</summary>
        public List<int> ReadBy = new List<int>();
        /// <summary>Quem recusou a carta (bloqueou a correspondência privada do remetente).</summary>
        public List<int> RejectedBy = new List<int>();

        // Interceptação (design §8.3.1). Só cartas privadas passam pelo sorteio, uma vez, no turno em que chegariam.
        /// <summary>Já passou pelo sorteio (ultimatos e declarações públicas nascem conferidos).</summary>
        public bool InterceptionChecked;
        /// <summary>Quem interceptou (-1 = ninguém). Carta interceptada nunca chega ao destinatário.</summary>
        public int InterceptedBy = -1;
        public int InterceptedTurn = -1;
        public int InterceptedTerritory = -1;
        /// <summary>Nome e GUID do exército espião que interceptou.</summary>
        public string InterceptedSpy;
        public string InterceptedSpyKey;
        /// <summary>"origem" (território de quem escreveu) ou "destino" (de quem ia receber).</summary>
        public string InterceptedVia;
        /// <summary>Quem interceptou já leu (no dossiê, ou o jogador na aba da espionagem).</summary>
        public bool InterceptorRead;
        /// <summary>Risco na origem, guardado quando a carta foi escrita (os espiões mudam de lugar depois).</summary>
        public List<InterceptRisk> OriginRisks;

        [JsonIgnore]
        public bool IsPublic => To == Everyone;

        [JsonIgnore]
        public bool Intercepted => InterceptedBy >= 0;

        /// <summary>
        /// A carta chega a este império: é para ele (ou pública e de outro), já passou pelo sorteio e não foi
        /// interceptada. O turno de entrega se confere à parte.
        /// </summary>
        public bool IsFor(int empireIndex) => InterceptionChecked && !Intercepted && (To == empireIndex || (To == Everyone && From != empireIndex));

        /// <summary>Já chegou ao destino até este turno (conferida e não interceptada).</summary>
        public bool Arrived(int turn) => DeliverTurn <= turn && InterceptionChecked && !Intercepted;
    }

    /// <summary>
    /// Um resultado do Congresso: lei votada ("lei") ou crise julgada ("crise"). Key evita repetir a mesma notícia; os
    /// votos de cada nação ficam em Votes (índice = nação; lei: 0 = A, 1 = B; crise: a nação apoiada; -1 = não votou).
    /// </summary>
    internal sealed class CongressNews
    {
        public string Key;
        public int Turn;
        public string Kind;
        public string Civic;
        public string ChoiceA;
        public string ChoiceB;
        /// <summary>Lei: 0 = A, 1 = B, -1 = empate. Crise: a nação vencedora (-1 = cancelada).</summary>
        public int Winner = -1;
        public int Declarator = -1;
        public int Target = -1;
        public string State;
        public List<int> Votes = new List<int>();
        public List<double> Sway = new List<double>();
    }

    /// <summary>Chance de um império interceptar uma carta numa ponta, e o espião dele que mais pesa ali.</summary>
    internal sealed class InterceptRisk
    {
        public int Empire;
        public double Chance;
        public string SpyKey;
        public string SpyName;
        public int Territory = -1;
    }

    /// <summary>
    /// Ordem da IA de linguagem a um exército: o objetivo, o destino (território e, se houver, a cidade), quando foi dada,
    /// a situação ("a caminho", "chegou", "falhou: …", "cancelada") e até que turno a IA nativa fica sem mexer nele.
    /// </summary>
    internal sealed class ArmyMission
    {
        public int Id;
        public int Empire;
        public string ArmyKey;
        public string Handle;
        /// <summary>mover, atacar, defender ou parar.</summary>
        public string Goal;
        public int TargetTerritory = -1;
        public string TargetCityKey;
        public string TargetArmyKey;
        public string TargetLabel;
        public int Turn;
        public string Status;
        public int StatusTurn;
        /// <summary>Último turno em que a IA nativa fica sem mexer no exército.</summary>
        public int HoldUntil = -1;
        /// <summary>Ainda em vigor (a caminho, ou chegou e segurando a posição).</summary>
        public bool Active;
    }

    /// <summary>Onde um espião está e desde quando (turno em que chegou ao território).</summary>
    internal sealed class SpyTrack
    {
        public int Territory = -1;
        public int Since;
        public int LastSeen;
    }
}
