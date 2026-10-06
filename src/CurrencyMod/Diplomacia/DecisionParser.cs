using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia
{
    /// <summary>O que a nação decidiu no turno, já validado.</summary>
    internal sealed class Decision
    {
        public string Diary;
        public List<FeelingUpdate> Feelings = new List<FeelingUpdate>();
        public List<LetterDraft> Letters = new List<LetterDraft>();
        public List<ActionDraft> Actions = new List<ActionDraft>();
        public List<NoteDraft> Notes = new List<NoteDraft>();
        /// <summary>Problemas corrigidos sem pedir de novo (valores fora da faixa, excesso de cartas...).</summary>
        public List<string> Warnings = new List<string>();
    }

    internal sealed class FeelingUpdate
    {
        public int Empire;
        public int Affection;
        public int Trust;
        public int Fear;
        public int Anger;
        public string Reason;
    }

    internal sealed class LetterDraft
    {
        public int To;
        public string Type;
        public string Subject;
        public string Text;
        public bool ExpectsReply;
        public string Demand;
        public int DeadlineTurns;
    }

    internal sealed class ActionDraft
    {
        public string Name;
        public string Json;
        public string Summary;
    }

    internal sealed class NoteDraft
    {
        public int About;
        public string Text;
    }

    /// <summary>
    /// O que a nação pode citar neste turno (montado junto com o dossiê, na thread principal).
    /// A validação roda na thread de trabalho e só consulta isto, nunca o jogo.
    /// </summary>
    internal sealed class ValidationContext
    {
        public int SelfIndex;
        public Dictionary<string, int> KnownEmpires = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OwnArmies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OwnCities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> KnownCities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> KnownTerritories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Territórios das suas cidades e postos (para ceder_territorio).</summary>
        public HashSet<string> OwnTerritories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Exércitos de outras nações à sua vista (códigos A), alvos de ordem_exercito "atacar".</summary>
        public HashSet<string> SeenArmies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Povos independentes que você conhece: código M → índice do império menor.</summary>
        public Dictionary<string, int> KnownMinors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public HashSet<int> AtWar = new HashSet<int>();
        public bool PublicDeclarationAllowed = true;
        public int MaxLetters = 3;
        public int MaxLetterWords = 350;
        public int MaxDiaryWords = 120;
        /// <summary>Palavras dos nomes reais do jogo (nações, líderes, povos, cidades, territórios, recursos), em
        /// minúsculas. Vazio = não conferir nomes.</summary>
        public HashSet<string> KnownWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Sua última carta recente para cada destino (Letter.Everyone = declaração pública).</summary>
        public Dictionary<int, PreviousLetter> LastLetters = new Dictionary<int, PreviousLetter>();
        /// <summary>Nações com proposta de tratado / de acordo esperando a sua resposta.</summary>
        public HashSet<int> TreatyToAnswer = new HashSet<int>();
        public HashSet<int> AgreementToAnswer = new HashSet<int>();
        /// <summary>Reclamações suas mostradas no dossiê: código G → nação e tipo (o executor confere de novo no jogo).</summary>
        public Dictionary<string, GrievanceRef> Grievances = new Dictionary<string, GrievanceRef>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Nações com exigências contra você que você ainda pode responder.</summary>
        public HashSet<int> DemandsToAnswer = new HashSet<int>();
        /// <summary>Nações contra as quais você tem exigências em aberto.</summary>
        public HashSet<int> DemandsMade = new HashSet<int>();
        /// <summary>Rendição: a quem você pode oferecer ou impor, e de quem há rendição esperando a sua resposta.</summary>
        public HashSet<int> CanOfferSurrender = new HashSet<int>();
        public HashSet<int> CanForceSurrender = new HashSet<int>();
        public HashSet<int> SurrenderToAnswer = new HashSet<int>();
        /// <summary>O que pode entrar como termo, por nação: se você oferecer (seus territórios) e se impuser (os deles).</summary>
        public Dictionary<int, SurrenderLimits> OfferLimits = new Dictionary<int, SurrenderLimits>();
        public Dictionary<int, SurrenderLimits> ForceLimits = new Dictionary<int, SurrenderLimits>();
        /// <summary>O Congresso mundial existe e já aceita crises (DLC Together We Rule; research\congress.md).</summary>
        public bool CongressCrisisOpen;
        /// <summary>Congresso mundial (preenchido pelo dossiê): a partida tem o Congresso; a nação preside agora e as leis
        /// que pode propor (código V → nome da definição); votação de lei aberta e se já votou; crises (código K); subornos
        /// possíveis ("lei:E4" ou "K3:E4" → máximo agora); consulado; vereditos que ela precisa responder; eixos do consenso.</summary>
        public bool CongressExists;
        public bool PresideNow;
        public Dictionary<string, string> ProposableLaws = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool LawVoteOpen;
        public bool LawVoted;
        /// <summary>Lei em votação (nome da definição), para guardar a postura se_perder.</summary>
        public string LawVoteCivic;
        public Dictionary<string, CrisisVoteRef> CrisisVotes = new Dictionary<string, CrisisVoteRef>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> BribeLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public bool HasConsulate;
        /// <summary>Peso zero no Congresso: votar sem suborno é recusado pelo jogo.</summary>
        public bool NoSway;
        public HashSet<int> VerdictAgainstMe = new HashSet<int>();
        public Dictionary<string, int> ConsensusAxes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Eixos do consenso que a nação ainda não pode pagar (a contribuição seria recusada).</summary>
        public HashSet<string> UnaffordableAxes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Votação de crise mostrada no dossiê (código K).</summary>
    internal sealed class CrisisVoteRef
    {
        public int Pool;
        public int Declarator;
        public int Target;
        public bool Voted;
    }

    /// <summary>Termos possíveis numa direção de rendição (da prévia exata do jogo).</summary>
    internal sealed class SurrenderLimits
    {
        public HashSet<string> Territories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Pontos livres depois das exigências obrigatórias (o resto vira ouro).</summary>
        public int Room;
        public bool Submission;
        public int SubmissionCost;
    }

    internal sealed class PreviousLetter
    {
        public int Turn;
        public string Text;
    }

    internal sealed class GrievanceRef
    {
        public int Other;
        public int Pool;
        public string Type;
    }

    internal static class DecisionParser
    {
        private static readonly HashSet<string> Postures = new HashSet<string> { "aliado", "amigavel", "neutro", "desconfiado", "hostil", "alvo_de_guerra" };
        private static readonly HashSet<string> Focuses = new HashSet<string> { "expansao", "economia", "ciencia", "militar", "fe", "cultura" };
        private static readonly HashSet<string> Agreements = new HashSet<string> { "economico", "informacao", "cultural", "militar", "alianca" };
        private static readonly HashSet<string> WarTypes = new HashSet<string> { "formal", "surpresa" };
        private static readonly HashSet<string> ArmyGoals = new HashSet<string> { "mover", "atacar", "defender", "parar" };
        private static readonly HashSet<string> LetterTypes = new HashSet<string> { "privada", "publica", "ultimato" };
        private static readonly HashSet<string> Answers = new HashSet<string> { "aceitar", "ignorar" };
        private static readonly HashSet<string> SurrenderAnswers = new HashSet<string> { "aceitar", "recusar" };
        private static readonly HashSet<string> DemandAnswers = new HashSet<string> { "aceitar", "recusar", "enrolar" };
        private static readonly HashSet<string> Investments = new HashSet<string> { "nenhum", "baixo", "medio", "alto" };
        private static readonly HashSet<string> VerdictAnswers = new HashSet<string> { "cumprir", "guerra" };
        private static readonly HashSet<string> TradeModes = new HashSet<string> { "livre", "pedagio", "bloqueio" };
        private const string NoCongress = "não há Congresso mundial nesta partida (ou ele ainda não se formou)";

        /// <summary>Tratados com povos independentes, na ordem do enum MinorTreaty do jogo.</summary>
        internal static readonly string[] MinorTreatyIds = { "contato", "comercio", "mercenarios", "ciencia", "lucros", "contrato", "intercambio", "vassalo", "anexar" };

        /// <summary>A partir daqui uma carta conta como repetição da anterior para o mesmo destino.</summary>
        private const double RepeatThreshold = 0.5;

        private static readonly System.Text.RegularExpressions.Regex PlusNumbers =
            new System.Text.RegularExpressions.Regex(@"([:\[,]\s*)\+(\d)", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Lê e valida a resposta. Devolve null e preenche errors quando é preciso pedir de novo.
        /// <paramref name="lenient"/> = última tentativa: o que ainda estiver errado numa carta é resolvido aqui
        /// (descartar ou só avisar) em vez de perder o turno inteiro.
        /// </summary>
        internal static Decision Parse(string content, ValidationContext context, List<string> errors, bool lenient = false)
        {
            JObject root;
            try
            {
                // "+10" não é json válido, mas a IA às vezes escreve assim os sentimentos positivos.
                root = JObject.Parse(PlusNumbers.Replace(StripFences(content ?? string.Empty), "$1$2"));
            }
            catch (Exception ex)
            {
                errors.Add($"A resposta não é um json válido ({ex.Message}). Responda só com o objeto json.");
                return null;
            }

            var decision = new Decision();
            decision.Diary = Str(root["diario"]);
            if (string.IsNullOrWhiteSpace(decision.Diary))
            {
                errors.Add("Falta o campo \"diario\" (sua reflexão secreta do turno).");
            }
            else if (Words(decision.Diary) > context.MaxDiaryWords * 3 / 2)
            {
                decision.Warnings.Add($"diário longo ({Words(decision.Diary)} palavras)");
            }

            ParseFeelings(root["sentimentos"] as JArray, context, decision);
            ParseLetters(root["cartas"] as JArray, context, decision, errors, lenient);
            ParseActions(root["acoes"] as JArray, context, decision, errors, lenient);
            ParseNotes(root["memoria"] as JArray, context, decision);
            return errors.Count == 0 ? decision : null;
        }

        private static void ParseFeelings(JArray array, ValidationContext context, Decision decision)
        {
            if (array == null)
            {
                return;
            }
            foreach (JObject item in array.OfType<JObject>())
            {
                string handle = Str(item["nacao"]);
                if (!context.KnownEmpires.TryGetValue(handle ?? string.Empty, out int empire))
                {
                    decision.Warnings.Add($"sentimento sobre nação desconhecida '{handle}' ignorado");
                    continue;
                }
                decision.Feelings.Add(new FeelingUpdate
                {
                    Empire = empire,
                    Affection = Clamp(Int(item["afeicao"]), -100, 100),
                    Trust = Clamp(Int(item["confianca"]), -100, 100),
                    Fear = Clamp(Int(item["medo"]), 0, 100),
                    Anger = Clamp(Int(item["raiva"]), 0, 100),
                    Reason = Str(item["motivo"]),
                });
            }
        }

        /// <summary>
        /// Carta com problema: pede outra resposta; na última tentativa (lenient) só a carta cai, com aviso. Antes o
        /// erro derrubava a decisão inteira e o turno da nação se perdia por uma carta.
        /// </summary>
        private static void Reject(Decision decision, List<string> errors, bool lenient, string message)
        {
            if (lenient)
            {
                decision.Warnings.Add("carta descartada: " + message);
            }
            else
            {
                errors.Add(message);
            }
        }

        private static void ParseLetters(JArray array, ValidationContext context, Decision decision, List<string> errors, bool lenient)
        {
            if (array == null)
            {
                return;
            }
            var recipients = new HashSet<int>();
            foreach (JObject item in array.OfType<JObject>())
            {
                string to = (Str(item["para"]) ?? string.Empty).Trim();
                string type = (Str(item["tipo"]) ?? "privada").Trim().ToLowerInvariant();
                string text = Str(item["texto"]);
                if (!LetterTypes.Contains(type))
                {
                    Reject(decision, errors, lenient, $"Carta para '{to}': tipo '{type}' não existe (use privada, publica ou ultimato).");
                    continue;
                }
                int recipient;
                if (type == "publica" || to.Equals("todos", StringComparison.OrdinalIgnoreCase))
                {
                    if (!context.PublicDeclarationAllowed)
                    {
                        Reject(decision, errors, lenient, "Você já fez uma declaração pública recentemente; ainda não pode fazer outra. Troque por cartas privadas ou remova.");
                        continue;
                    }
                    type = "publica";
                    recipient = Letter.Everyone;
                }
                else if (!context.KnownEmpires.TryGetValue(to, out recipient))
                {
                    Reject(decision, errors, lenient, $"Carta para '{to}': destinatário desconhecido. Use o código de uma nação do dossiê (ex.: E2) ou \"todos\" para declaração pública.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(text))
                {
                    Reject(decision, errors, lenient, $"Carta para '{to}' sem texto.");
                    continue;
                }
                // Uma folga de 25% evita pedir de novo por poucas palavras (cada nova tentativa custa uma chamada).
                int words = Words(text);
                if (words > context.MaxLetterWords * 5 / 4)
                {
                    Reject(decision, errors, lenient, $"Carta para '{to}' tem {words} palavras; o máximo é {context.MaxLetterWords}. Encurte para o essencial.");
                    continue;
                }
                if (words > context.MaxLetterWords)
                {
                    decision.Warnings.Add($"carta para '{to}' um pouco longa ({words} palavras)");
                }

                // Ancoragem no jogo (design §9.1) e anti-laço (§9): carta repetida ou com coisa que o jogo não tem
                // volta para ser reescrita e, na última tentativa, é descartada. Nome desconhecido também volta, mas
                // na última tentativa vira só aviso, para um falso alarme não custar a carta.
                string[] fields = { text, Str(item["assunto"]), Str(item["exigencia"]) };
                List<string> notInGame = Grounding.NotInGameWords(string.Join(" ", fields), context.KnownWords);
                bool repeated = context.LastLetters.TryGetValue(recipient, out PreviousLetter previous)
                    && Grounding.Similarity(text, previous.Text) >= RepeatThreshold;
                if (repeated || notInGame.Count > 0)
                {
                    string why = repeated
                        ? $"repete quase igual a sua carta do turno {previous.Turn} para o mesmo destino. Traga algo novo (um fato, uma oferta, uma consequência) ou não escreva"
                        : $"cita o que o jogo não tem: {string.Join(", ", notInGame)}. Mercadoria é só dinheiro, influência e os recursos de NOMES DO JOGO";
                    if (lenient)
                    {
                        decision.Warnings.Add($"carta para '{to}' descartada: {why}");
                    }
                    else
                    {
                        errors.Add($"Carta para '{to}' {why}.");
                    }
                    continue;
                }
                // Cada campo à parte: o assunto colado no fim do texto viraria "meio de frase" e cada palavra com
                // maiúscula dele pareceria um nome.
                List<string> unknown = context.KnownWords.Count > 0
                    ? fields.SelectMany(f => Grounding.UnknownNames(f, context.KnownWords)).Distinct().ToList()
                    : new List<string>();
                if (unknown.Count > 0)
                {
                    // Só nomes do jogo (design §9.1): se a IA insistir até a última tentativa, a carta cai (no teste
                    // do turno 95, um enviado inventado sobreviveu às três tentativas).
                    if (lenient)
                    {
                        decision.Warnings.Add($"carta para '{to}' descartada: nomes fora do jogo ({string.Join(", ", unknown)})");
                    }
                    else
                    {
                        errors.Add($"Carta para '{to}' cita nomes que não estão no jogo nem no seu dossiê: {string.Join(", ", unknown)}. Use só nomes do dossiê; não invente pessoas, lugares, cargos nem mercadorias.");
                    }
                    continue;
                }
                List<string> codes = Grounding.CodesIn(text);
                if (codes.Count > 0)
                {
                    if (!lenient)
                    {
                        errors.Add($"Carta para '{to}' usa códigos do dossiê no texto ({string.Join(", ", codes)}). Na carta, escreva o nome da cidade, do território ou da nação.");
                        continue;
                    }
                    decision.Warnings.Add($"carta para '{to}' com código no texto: {string.Join(", ", codes)}");
                }

                if (!recipients.Add(recipient))
                {
                    decision.Warnings.Add($"segunda carta para '{to}' no mesmo turno descartada");
                    continue;
                }
                if (decision.Letters.Count >= context.MaxLetters)
                {
                    decision.Warnings.Add($"carta para '{to}' acima do limite de {context.MaxLetters} por turno descartada");
                    continue;
                }
                var letter = new LetterDraft
                {
                    To = recipient,
                    Type = type,
                    Subject = Str(item["assunto"]),
                    Text = text.Trim(),
                    ExpectsReply = Bool(item["pede_resposta"], type != "publica"),
                };
                if (type == "ultimato")
                {
                    letter.Demand = Str(item["exigencia"]);
                    letter.DeadlineTurns = Clamp(Int(item["prazo_turnos"], 3), 1, 30);
                    if (string.IsNullOrWhiteSpace(letter.Demand))
                    {
                        Reject(decision, errors, lenient, $"Ultimato para '{to}' sem \"exigencia\".");
                        continue;
                    }
                }
                decision.Letters.Add(letter);
            }
        }

        /// <summary>
        /// Ações da lista fechada. Ação inválida pede outra resposta; na última tentativa (lenient) ela é descartada com
        /// aviso, para o turno não se perder por uma ação só.
        /// </summary>
        private static void ParseActions(JArray array, ValidationContext context, Decision decision, List<string> errors, bool lenient)
        {
            if (array == null)
            {
                return;
            }
            foreach (JObject item in array.OfType<JObject>())
            {
                string name = (Str(item["acao"]) ?? string.Empty).Trim().ToLowerInvariant();
                string problem = null;
                string summary;
                switch (name)
                {
                    case "definir_postura":
                        problem = Empire(item, context, out string postureTarget) ?? OneOf(item, "postura", Postures);
                        summary = $"postura {Str(item["postura"])} com {postureTarget}";
                        break;
                    case "definir_foco":
                        problem = OneOf(item, "foco", Focuses);
                        summary = $"foco em {Str(item["foco"])}";
                        break;
                    case "declarar_guerra":
                        problem = Empire(item, context, out string warTarget) ?? OneOf(item, "tipo", WarTypes);
                        if (problem == null && context.AtWar.Contains(context.KnownEmpires[warTarget]))
                        {
                            problem = $"vocês já estão em guerra com {warTarget}";
                        }
                        summary = $"guerra {Str(item["tipo"])} contra {warTarget}";
                        break;
                    case "propor_paz":
                        problem = Empire(item, context, out string peaceTarget);
                        if (problem == null && !context.AtWar.Contains(context.KnownEmpires[peaceTarget]))
                        {
                            problem = $"vocês não estão em guerra com {peaceTarget} (paz só se propõe em guerra; para aproximação use carta ou propor_acordo)";
                        }
                        summary = $"paz com {peaceTarget}";
                        break;
                    case "propor_acordo":
                    case "romper_acordo":
                        problem = Empire(item, context, out string agreementTarget) ?? OneOf(item, "acordo", Agreements);
                        summary = $"{(name == "propor_acordo" ? "propor" : "romper")} acordo {Str(item["acordo"])} com {agreementTarget}";
                        break;
                    case "exigir":
                    case "perdoar_queixas":
                    {
                        problem = Empire(item, context, out string grievanceTarget);
                        List<string> codes = null;
                        if (problem == null)
                        {
                            problem = GrievanceCodes(item, context, context.KnownEmpires[grievanceTarget], out codes);
                        }
                        string which = codes == null ? "todas as reclamações" : string.Join(", ", codes);
                        summary = name == "exigir" ? $"exigir de {grievanceTarget}: {which}" : $"perdoar {grievanceTarget}: {which}";
                        break;
                    }
                    case "responder_exigencias":
                        problem = Empire(item, context, out string answerDemands) ?? OneOf(item, "resposta", DemandAnswers);
                        if (problem == null && !context.DemandsToAnswer.Contains(context.KnownEmpires[answerDemands]))
                        {
                            problem = $"não há exigências de {answerDemands} esperando a sua resposta"
                                + (context.DemandsMade.Contains(context.KnownEmpires[answerDemands]) ? " (as exigências entre vocês são suas: para desistir delas, use retirar_exigencias)" : string.Empty);
                        }
                        summary = $"{Str(item["resposta"])} as exigências de {answerDemands}";
                        break;
                    case "retirar_exigencias":
                    case "crise_internacional":
                        problem = Empire(item, context, out string demandTarget);
                        if (problem == null && name == "crise_internacional" && !context.CongressCrisisOpen)
                        {
                            problem = "não há Congresso mundial nesta partida (ou ele ainda não se formou): leve a disputa por carta, ultimato, exigência ou guerra";
                        }
                        else if (problem == null && name == "crise_internacional" && !context.HasConsulate)
                        {
                            problem = "levar uma disputa ao Congresso exige o seu consulado funcionando";
                        }
                        else if (problem == null && !context.DemandsMade.Contains(context.KnownEmpires[demandTarget]))
                        {
                            problem = $"você não tem exigências em aberto contra {demandTarget}"
                                + (context.DemandsToAnswer.Contains(context.KnownEmpires[demandTarget]) ? " (as exigências entre vocês são deles: responda com responder_exigencias)" : string.Empty);
                        }
                        summary = name == "retirar_exigencias" ? $"retirar as exigências contra {demandTarget}" : $"levar a disputa com {demandTarget} ao Congresso";
                        break;
                    case "propor_fim_da_crise":
                    {
                        problem = Empire(item, context, out string crisisTarget);
                        int crisisOther = problem == null ? context.KnownEmpires[crisisTarget] : -1;
                        if (problem == null && !(context.DemandsMade.Contains(crisisOther) && context.DemandsToAnswer.Contains(crisisOther)))
                        {
                            problem = $"fim da crise só se propõe quando os dois lados têm exigências em aberto (não é o caso com {crisisTarget})"
                                + (context.DemandsMade.Contains(crisisOther) ? "; para encerrar do seu lado, use retirar_exigencias"
                                    : context.DemandsToAnswer.Contains(crisisOther) ? "; para encerrar a crise, use responder_exigencias" : string.Empty);
                        }
                        summary = $"propor fim da crise a {crisisTarget}";
                        break;
                    }
                    case "responder_tratado":
                    case "responder_acordo":
                    {
                        bool treaty = name == "responder_tratado";
                        problem = Empire(item, context, out string answerTarget) ?? OneOf(item, "resposta", Answers);
                        if (problem == null && !(treaty ? context.TreatyToAnswer : context.AgreementToAnswer).Contains(context.KnownEmpires[answerTarget]))
                        {
                            problem = $"não há proposta de {(treaty ? "tratado" : "acordo")} de {answerTarget} esperando você";
                        }
                        summary = $"{Str(item["resposta"])} a proposta de {(treaty ? "tratado" : "acordo")} de {answerTarget}";
                        break;
                    }
                    case "oferecer_rendicao":
                    case "impor_rendicao":
                    {
                        bool force = name == "impor_rendicao";
                        problem = Empire(item, context, out string surrenderTarget);
                        int surrenderOther = problem == null ? context.KnownEmpires[surrenderTarget] : -1;
                        JObject terms = item["termos"] as JObject;
                        List<string> surrenderTerritories = Handles(terms?["territorios"]);
                        bool submission = Bool(terms?["submissao"], false);
                        if (problem == null && !context.AtWar.Contains(surrenderOther))
                        {
                            problem = $"vocês não estão em guerra com {surrenderTarget}";
                        }
                        else if (problem == null && !(force ? context.CanForceSurrender : context.CanOfferSurrender).Contains(surrenderOther))
                        {
                            problem = force
                                ? $"você não pode impor rendição a {surrenderTarget} agora (só com o apoio à guerra deles em 0 e o seu acima de 0; veja a seção Rendição do dossiê)"
                                : $"não dá para oferecer rendição a {surrenderTarget} agora (veja o motivo na seção Rendição do dossiê)";
                        }
                        else if (problem == null && (force ? context.ForceLimits : context.OfferLimits).TryGetValue(surrenderOther, out SurrenderLimits limits))
                        {
                            string bad = surrenderTerritories.FirstOrDefault(t => !limits.Territories.Contains(t));
                            int cost = surrenderTerritories.Count * 25 + (submission ? limits.SubmissionCost : 0);
                            if (bad != null)
                            {
                                problem = $"território '{bad}' não pode entrar nessa rendição (use só os códigos T listados na seção Rendição de {surrenderTarget})";
                            }
                            else if (submission && !limits.Submission)
                            {
                                problem = "vassalagem não está disponível nessa rendição (veja a seção Rendição)";
                            }
                            else if (limits.Room != int.MaxValue && cost > limits.Room)
                            {
                                problem = $"os termos pedidos custam {cost} pontos e só sobram {limits.Room} depois das exigências: tire territórios ou a vassalagem (o que sobrar vira ouro)";
                            }
                        }
                        summary = $"{(force ? "impor" : "oferecer")} rendição: {(force ? surrenderTarget + " se rende a você" : "você se rende a " + surrenderTarget)}"
                            + (surrenderTerritories.Count > 0 ? $", territórios {string.Join(", ", surrenderTerritories)}" : string.Empty)
                            + (submission ? ", vassalagem" : string.Empty);
                        break;
                    }
                    case "responder_rendicao":
                        problem = Empire(item, context, out string surrenderAnswer) ?? OneOf(item, "resposta", SurrenderAnswers);
                        if (problem == null && !context.SurrenderToAnswer.Contains(context.KnownEmpires[surrenderAnswer]))
                        {
                            problem = $"não há rendição de {surrenderAnswer} esperando a sua resposta";
                        }
                        summary = $"{Str(item["resposta"])} a rendição na guerra com {surrenderAnswer}";
                        break;
                    case "propor_votacao":
                    {
                        string law = Str(item["lei"])?.Trim().ToUpperInvariant();
                        if (!context.CongressExists)
                        {
                            problem = NoCongress;
                        }
                        else if (!context.PresideNow)
                        {
                            problem = "você não preside a sessão do Congresso agora (a seção do Congresso diz quem preside e quando)";
                        }
                        else if (law == null || !context.ProposableLaws.ContainsKey(law))
                        {
                            problem = $"lei '{law}' não está entre as que você pode propor (use um código V da seção do Congresso)";
                        }
                        summary = $"propor a lei {law} ao Congresso";
                        break;
                    }
                    case "votar_congresso":
                    {
                        string vote = Str(item["votacao"])?.Trim();
                        string bribePrefix = (vote != null && vote.Equals("lei", StringComparison.OrdinalIgnoreCase) ? "lei" : vote?.ToUpperInvariant()) + ":";
                        if (context.CongressExists && context.NoSway && !context.BribeLimits.Keys.Any(k => k.StartsWith(bribePrefix, StringComparison.OrdinalIgnoreCase)))
                        {
                            // Peso zero e nada a subornar: o jogo recusaria. Sai sem pedir outra resposta (custaria uma chamada).
                            decision.Warnings.Add("voto no Congresso descartado: seu peso é zero");
                            continue;
                        }
                        if (!context.CongressExists)
                        {
                            problem = NoCongress;
                            summary = "votar no Congresso";
                        }
                        else if (vote != null && vote.Equals("lei", StringComparison.OrdinalIgnoreCase))
                        {
                            string option = (Str(item["opcao"]) ?? string.Empty).Trim().ToUpperInvariant();
                            string ifLost = Str(item["se_perder"])?.Trim().ToLowerInvariant();
                            if (!context.LawVoteOpen)
                            {
                                problem = "não há votação de lei aberta no Congresso";
                            }
                            else if (context.LawVoted)
                            {
                                problem = "você já votou nesta lei (o voto não muda)";
                            }
                            else if (option != "A" && option != "B" && option != "ABSTER")
                            {
                                problem = "\"opcao\" = A, B ou abster";
                            }
                            else if (ifLost != null && ifLost != "adotar" && ifLost != "recusar")
                            {
                                problem = "\"se_perder\" = adotar ou recusar";
                            }
                            summary = option == "ABSTER" ? "abster-se na votação de lei do Congresso" : $"votar {option} na lei do Congresso" + (ifLost != null ? $" (se perder: {ifLost})" : string.Empty);
                        }
                        else
                        {
                            string code = vote?.ToUpperInvariant();
                            string side = (Str(item["apoiar"]) ?? string.Empty).Trim().ToUpperInvariant();
                            if (code == null || !context.CrisisVotes.TryGetValue(code, out CrisisVoteRef crisis))
                            {
                                problem = $"votação '{vote}' desconhecida (use \"lei\" ou um código K da seção do Congresso)";
                            }
                            else if (crisis.Voted)
                            {
                                problem = $"você já votou na crise {code}";
                            }
                            else if (side != "ABSTER" && side != "E" + crisis.Declarator && side != "E" + crisis.Target)
                            {
                                problem = $"\"apoiar\" = E{crisis.Declarator}, E{crisis.Target} ou abster";
                            }
                            summary = side == "ABSTER" ? $"abster-se na crise {code}" : $"apoiar {side} na crise {code}";
                        }
                        break;
                    }
                    case "subornar":
                    {
                        string vote = Str(item["votacao"])?.Trim();
                        problem = Empire(item, context, out string bribeTarget);
                        int times = Int(item["vezes"], 1);
                        string key = (vote != null && vote.Equals("lei", StringComparison.OrdinalIgnoreCase) ? "lei" : vote?.ToUpperInvariant()) + ":" + bribeTarget?.Trim().ToUpperInvariant();
                        if (problem == null && !context.CongressExists)
                        {
                            problem = NoCongress;
                        }
                        else if (problem == null && !context.HasConsulate)
                        {
                            problem = "subornar exige o seu consulado funcionando";
                        }
                        else if (problem == null && !context.BribeLimits.ContainsKey(key))
                        {
                            problem = $"não há suborno possível contra {bribeTarget} em '{vote}' agora (veja os subornos listados na seção do Congresso; só antes do seu voto)";
                        }
                        else if (problem == null && times < 1)
                        {
                            problem = "\"vezes\" precisa ser 1 ou mais";
                        }
                        summary = $"subornar {bribeTarget} {times}× na votação {vote}";
                        break;
                    }
                    case "responder_congresso":
                        problem = Empire(item, context, out string verdictTarget) ?? OneOf(item, "resposta", VerdictAnswers);
                        if (problem == null && !context.VerdictAgainstMe.Contains(context.KnownEmpires[verdictTarget]))
                        {
                            problem = $"não há veredito do Congresso contra você na disputa com {verdictTarget}";
                        }
                        summary = $"{Str(item["resposta"])}: veredito do Congresso na disputa com {verdictTarget}";
                        break;
                    case "contribuir_consenso":
                    {
                        string axis = Str(item["eixo"])?.Trim().ToUpperInvariant();
                        if (axis != null && context.UnaffordableAxes.Contains(axis))
                        {
                            // Sem influência: o jogo recusaria. Sai sem pedir outra resposta.
                            decision.Warnings.Add($"contribuição ao consenso {axis} descartada: influência insuficiente");
                            continue;
                        }
                        if (!context.CongressExists)
                        {
                            problem = NoCongress;
                        }
                        else if (axis == null || !context.ConsensusAxes.ContainsKey(axis))
                        {
                            problem = $"eixo '{axis}' desconhecido (use um código I da seção do Congresso)";
                        }
                        summary = $"contribuir ao consenso mundial {axis}";
                        break;
                    }
                    case "politica_comercial":
                        problem = Empire(item, context, out string tradeTarget) ?? OneOf(item, "modo", TradeModes);
                        summary = $"comércio com {tradeTarget}: {Str(item["modo"])} nos seus postos";
                        break;
                    case "bloquear_correspondencia":
                    case "desbloquear_correspondencia":
                        problem = Empire(item, context, out string mailTarget);
                        summary = $"{(name.StartsWith("bloquear") ? "bloquear" : "desbloquear")} cartas de {mailTarget}";
                        break;
                    case "presentear":
                    {
                        problem = Empire(item, context, out string giftTarget);
                        List<string> giftCities = Handles(item["cidades"]);
                        List<string> giftArmies = Handles(item["exercitos"]);
                        if (problem == null)
                        {
                            string bad = giftCities.FirstOrDefault(c => !context.OwnCities.Contains(c));
                            string badArmy = giftArmies.FirstOrDefault(a => !context.OwnArmies.Contains(a));
                            if (bad != null)
                            {
                                problem = $"'{bad}' não é cidade ou posto seu (use um código C da sua lista)";
                            }
                            else if (badArmy != null)
                            {
                                problem = $"'{badArmy}' não é exército seu (use um código A da sua lista)";
                            }
                            else if (Int(item["ouro"]) <= 0 && Int(item["influencia"]) <= 0 && giftCities.Count == 0 && giftArmies.Count == 0)
                            {
                                problem = "presente sem nada (\"ouro\" e/ou \"influencia\" maiores que zero, ou \"cidades\" / \"exercitos\" com códigos seus)";
                            }
                        }
                        var giftParts = new List<string>();
                        if (Int(item["ouro"]) > 0) giftParts.Add($"{Int(item["ouro"])} de dinheiro");
                        if (Int(item["influencia"]) > 0) giftParts.Add($"{Int(item["influencia"])} de influência");
                        giftParts.AddRange(giftCities);
                        giftParts.AddRange(giftArmies);
                        summary = $"presente para {giftTarget}: {string.Join(", ", giftParts)}";
                        break;
                    }
                    case "demitir_ministro":
                    {
                        string portfolio = Str(item["pasta"])?.Trim().ToLowerInvariant();
                        if (portfolio == null || Array.IndexOf(Council.CouncilBank.Portfolios, portfolio) < 0)
                        {
                            problem = $"\"pasta\" = '{portfolio}' inválida (use {string.Join(", ", Council.CouncilBank.Portfolios)})";
                        }
                        summary = $"demitir o ministro da pasta {portfolio}";
                        break;
                    }
                    case "patrocinar":
                    {
                        problem = Minor(item, context, out string patronTarget);
                        string money = Str(item["dinheiro"])?.Trim().ToLowerInvariant();
                        string influence = Str(item["influencia"])?.Trim().ToLowerInvariant();
                        if (problem == null && money == null && influence == null)
                        {
                            problem = "falta \"dinheiro\" e/ou \"influencia\" (nenhum, baixo, medio ou alto)";
                        }
                        else if (problem == null && ((money != null && !Investments.Contains(money)) || (influence != null && !Investments.Contains(influence))))
                        {
                            problem = "investimento inválido (use nenhum, baixo, medio ou alto)";
                        }
                        summary = $"patrocinar {patronTarget}: dinheiro {money ?? "igual"}, influência {influence ?? "igual"}";
                        break;
                    }
                    case "tratado_povo":
                    {
                        problem = Minor(item, context, out string treatyTarget);
                        string treaty = Str(item["tratado"])?.Trim().ToLowerInvariant();
                        if (problem == null && Array.IndexOf(MinorTreatyIds, treaty) < 0)
                        {
                            problem = $"\"tratado\" = '{treaty}' inválido (use {string.Join(", ", MinorTreatyIds)})";
                        }
                        summary = $"tratado {treaty} com {treatyTarget}";
                        break;
                    }
                    case "ceder_territorio":
                    {
                        problem = Empire(item, context, out string cedeTarget);
                        string territory = Str(item["territorio"])?.Trim().ToUpperInvariant();
                        if (problem == null && (territory == null || !context.OwnTerritories.Contains(territory)))
                        {
                            problem = $"território '{territory}' não é seu (use um código T das suas cidades e postos)";
                        }
                        summary = $"ceder {territory} a {cedeTarget}";
                        break;
                    }
                    case "ordem_exercito":
                    {
                        string army = Str(item["exercito"])?.Trim().ToUpperInvariant();
                        string destination = Str(item["destino"] ?? item["alvo"])?.Trim().ToUpperInvariant();
                        string goal = (Str(item["objetivo"]) ?? string.Empty).Trim().ToLowerInvariant();
                        problem = OneOf(item, "objetivo", ArmyGoals);
                        bool enemyArmy = destination != null && destination.StartsWith("A") && context.SeenArmies.Contains(destination);
                        if (problem == null && (army == null || !context.OwnArmies.Contains(army)))
                        {
                            problem = $"exército '{army}' não é seu (use um código A da sua lista de exércitos)";
                        }
                        else if (problem == null && enemyArmy && goal != "atacar")
                        {
                            problem = $"{destination} é exército de outra nação: só serve de alvo para \"atacar\"";
                        }
                        else if (problem == null && goal != "parar" && !enemyArmy && (destination == null || !(context.KnownTerritories.Contains(destination) || context.KnownCities.Contains(destination))))
                        {
                            problem = $"destino '{destination}' desconhecido (use um código T ou C do dossiê, ou A de exército inimigo à vista para atacar)";
                        }
                        else if (problem == null && goal == "atacar" && destination != null && destination.StartsWith("C") && context.OwnCities.Contains(destination))
                        {
                            problem = $"{destination} é cidade sua: para guardá-la use \"defender\"";
                        }
                        summary = goal == "parar" ? $"parar {army}" : $"{goal} {army} → {destination}";
                        break;
                    }
                    case "renomear":
                    {
                        string target = Str(item["alvo"]);
                        if (target == null || !(context.OwnCities.Contains(target) || context.OwnArmies.Contains(target)))
                        {
                            problem = $"'{target}' não é uma cidade ou exército seu";
                        }
                        else if (string.IsNullOrWhiteSpace(Str(item["nome"])))
                        {
                            problem = "falta \"nome\"";
                        }
                        summary = $"renomear {target} para {Str(item["nome"])}";
                        break;
                    }
                    default:
                        if (lenient)
                        {
                            decision.Warnings.Add($"ação '{name}' descartada: não existe");
                        }
                        else
                        {
                            errors.Add($"Ação '{name}' não existe. Use só as ações da lista.");
                        }
                        continue;
                }
                if (problem != null)
                {
                    if (lenient)
                    {
                        decision.Warnings.Add($"ação {name} descartada: {problem}");
                    }
                    else
                    {
                        errors.Add($"Ação {name}: {problem}.");
                    }
                    continue;
                }
                decision.Actions.Add(new ActionDraft { Name = name, Json = item.ToString(Newtonsoft.Json.Formatting.None), Summary = summary });
            }
            // Suborno só vale antes do voto, e o executor processa na ordem: subornos sempre na frente.
            if (decision.Actions.Any(a => a.Name == "subornar"))
            {
                decision.Actions = decision.Actions.Where(a => a.Name == "subornar").Concat(decision.Actions.Where(a => a.Name != "subornar")).ToList();
            }
        }

        private static void ParseNotes(JArray array, ValidationContext context, Decision decision)
        {
            if (array == null)
            {
                return;
            }
            foreach (JObject item in array.OfType<JObject>())
            {
                string text = Str(item["nota"]);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }
                string handle = Str(item["nacao"]);
                int about = -1;
                if (handle != null && !context.KnownEmpires.TryGetValue(handle, out about))
                {
                    about = -1;
                }
                decision.Notes.Add(new NoteDraft { About = about, Text = text.Trim() });
            }
        }

        private static string Minor(JObject item, ValidationContext context, out string handle)
        {
            handle = Str(item["povo"])?.Trim().ToUpperInvariant();
            return handle != null && context.KnownMinors.ContainsKey(handle)
                ? null
                : $"povo '{handle}' desconhecido (use um código M da seção de povos independentes)";
        }

        private static string Empire(JObject item, ValidationContext context, out string handle)
        {
            handle = Str(item["nacao"]);
            return handle != null && context.KnownEmpires.ContainsKey(handle)
                ? null
                : $"nação '{handle}' desconhecida (use um código E do dossiê)";
        }

        /// <summary>
        /// "queixas": lista de códigos G do dossiê, um código só, ou "todas". codes = null quando são todas.
        /// </summary>
        internal static string GrievanceCodes(JObject item, ValidationContext context, int other, out List<string> codes)
        {
            codes = new List<string>();
            if (!context.Grievances.Values.Any(g => g.Other == other))
            {
                return "você não tem reclamações contra essa nação no dossiê (o que não é reclamação do jogo vai por carta, como ultimato)";
            }
            JToken token = item["queixas"];
            if (token == null || token.Type == JTokenType.Null)
            {
                return "falta \"queixas\" (lista de códigos G do dossiê, ou \"todas\")";
            }
            if (token.Type == JTokenType.String)
            {
                string value = token.ToString().Trim();
                if (IsAll(value))
                {
                    codes = null;
                    return null;
                }
                token = new JArray(value);
            }
            if (!(token is JArray array) || array.Count == 0)
            {
                return "\"queixas\" precisa ser uma lista de códigos G do dossiê ou \"todas\"";
            }
            foreach (JToken entry in array)
            {
                string code = (Str(entry) ?? string.Empty).Trim().ToUpperInvariant();
                if (!context.Grievances.TryGetValue(code, out GrievanceRef reference))
                {
                    return $"reclamação '{code}' desconhecida (use os códigos G do dossiê)";
                }
                if (reference.Other != other)
                {
                    return $"a reclamação {code} não é contra essa nação";
                }
                if (!codes.Contains(code))
                {
                    codes.Add(code);
                }
            }
            return null;
        }

        /// <summary>Lista de códigos (C#, A#…) num campo que pode vir como texto único ou lista; em maiúsculas.</summary>
        internal static List<string> Handles(JToken token)
        {
            var list = new List<string>();
            if (token == null || token.Type == JTokenType.Null)
            {
                return list;
            }
            IEnumerable<JToken> items = token is JArray array ? (IEnumerable<JToken>)array : new[] { token };
            foreach (JToken entry in items)
            {
                string code = (Str(entry) ?? string.Empty).Trim().ToUpperInvariant();
                if (code.Length > 0 && !list.Contains(code))
                {
                    list.Add(code);
                }
            }
            return list;
        }

        internal static bool IsAll(string value) =>
            value != null && (value.Trim().Equals("todas", StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("todos", StringComparison.OrdinalIgnoreCase));

        private static string OneOf(JObject item, string field, HashSet<string> allowed)
        {
            string value = (Str(item[field]) ?? string.Empty).Trim().ToLowerInvariant();
            return allowed.Contains(value) ? null : $"\"{field}\" = '{value}' inválido (use {string.Join(", ", allowed)})";
        }

        private static string StripFences(string text)
        {
            text = text.Trim();
            if (text.StartsWith("```"))
            {
                int firstNewline = text.IndexOf('\n');
                int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline > 0 && lastFence > firstNewline)
                {
                    text = text.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                }
            }
            return text;
        }

        internal static int Words(string text) => string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

        private static string Str(JToken token) => token == null || token.Type == JTokenType.Null ? null : token.ToString();

        private static int Int(JToken token, int fallback = 0)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }
            return double.TryParse(token.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
                ? (int)Math.Round(value)
                : fallback;
        }

        private static bool Bool(JToken token, bool fallback)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }
            return token.Type == JTokenType.Boolean ? (bool)token : bool.TryParse(token.ToString(), out bool value) ? value : fallback;
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);
    }
}
