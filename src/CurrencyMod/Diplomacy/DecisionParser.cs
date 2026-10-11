using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacy
{
    /// <summary>What the nation decided this turn, already validated.</summary>
    internal sealed class Decision
    {
        public string Diary;
        public List<FeelingUpdate> Feelings = new List<FeelingUpdate>();
        public List<LetterDraft> Letters = new List<LetterDraft>();
        public List<ActionDraft> Actions = new List<ActionDraft>();
        public List<NoteDraft> Notes = new List<NoteDraft>();
        /// <summary>Problems fixed without asking again (out-of-range values, too many letters...).</summary>
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
        /// <summary>Group message (type "grupo"): the conversation id.</summary>
        public int GroupId = -1;
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
    /// What the nation may reference this turn (built together with the dossier, on the main thread).
    /// Validation runs on the worker thread and only consults this, never the game.
    /// </summary>
    internal sealed class ValidationContext
    {
        public int SelfIndex;
        public Dictionary<string, int> KnownEmpires = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OwnArmies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OwnCities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> KnownCities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> KnownTerritories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Territories of its cities and outposts (for ceder_territorio).</summary>
        public HashSet<string> OwnTerritories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Other nations' armies in sight (A codes), targets of ordem_exercito "atacar".</summary>
        public HashSet<string> SeenArmies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Independent peoples the nation knows: M code → minor empire index.</summary>
        public Dictionary<string, int> KnownMinors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public HashSet<int> AtWar = new HashSet<int>();
        public bool PublicDeclarationAllowed = true;
        public int MaxLetters = 3;
        public int MaxLetterWords = 350;
        public int MaxDiaryWords = 120;
        /// <summary>Words of the game's real names (nations, leaders, peoples, cities, territories, resources), in
        /// lowercase. Empty = do not check names.</summary>
        public HashSet<string> KnownWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Words of resource names: all of the game's and only those the nation has access to (offering what it lacks is sent back).</summary>
        public HashSet<string> AllResourceWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OwnResourceWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The nation's chat groups: "GRP#" code → id.</summary>
        public Dictionary<string, int> Groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The nation's bloc (-1 = none or still forming), open invitations ("BLC#"), open agenda items ("P#") and election.</summary>
        public int BlocId = -1;
        public HashSet<string> BlocInvites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> BlocMotions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool BlocElection;
        /// <summary>The nation's last recent letter to each destination (Letter.Everyone = public statement).</summary>
        public Dictionary<int, PreviousLetter> LastLetters = new Dictionary<int, PreviousLetter>();
        /// <summary>Nations with a treaty / deal proposal awaiting this nation's answer.</summary>
        public HashSet<int> TreatyToAnswer = new HashSet<int>();
        public HashSet<int> AgreementToAnswer = new HashSet<int>();
        /// <summary>The nation's grievances shown in the dossier: G code → nation and type (the executor re-checks in the game).</summary>
        public Dictionary<string, GrievanceRef> Grievances = new Dictionary<string, GrievanceRef>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Nations with demands against this nation that it can still answer.</summary>
        public HashSet<int> DemandsToAnswer = new HashSet<int>();
        /// <summary>Nations against which this nation has open demands.</summary>
        public HashSet<int> DemandsMade = new HashSet<int>();
        /// <summary>Surrender: to whom the nation can offer or impose it, and whose surrender awaits its answer.</summary>
        public HashSet<int> CanOfferSurrender = new HashSet<int>();
        public HashSet<int> CanForceSurrender = new HashSet<int>();
        public HashSet<int> SurrenderToAnswer = new HashSet<int>();
        /// <summary>What can go in as a term, per nation: when offering (its own territories) and when imposing (theirs).</summary>
        public Dictionary<int, SurrenderLimits> OfferLimits = new Dictionary<int, SurrenderLimits>();
        public Dictionary<int, SurrenderLimits> ForceLimits = new Dictionary<int, SurrenderLimits>();
        /// <summary>The World Congress exists and already accepts crises (Together We Rule DLC; research\congress.md).</summary>
        public bool CongressCrisisOpen;
        /// <summary>World Congress (filled by the dossier): the match has the Congress; the nation presides now and the laws
        /// it can propose (V code → definition name); open law vote and whether it already voted; crises (K code); possible
        /// bribes ("lei:E4" or "K3:E4" → current maximum); consulate; verdicts it must answer; consensus axes.</summary>
        public bool CongressExists;
        public bool PresideNow;
        public Dictionary<string, string> ProposableLaws = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool LawVoteOpen;
        public bool LawVoted;
        /// <summary>Law being voted (definition name), to store the se_perder stance.</summary>
        public string LawVoteCivic;
        public Dictionary<string, CrisisVoteRef> CrisisVotes = new Dictionary<string, CrisisVoteRef>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> BribeLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public bool HasConsulate;
        /// <summary>Zero weight in the Congress: voting without a bribe is rejected by the game.</summary>
        public bool NoSway;
        public HashSet<int> VerdictAgainstMe = new HashSet<int>();
        public Dictionary<string, int> ConsensusAxes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Consensus axes the nation cannot afford yet (the contribution would be rejected).</summary>
        public HashSet<string> UnaffordableAxes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Crisis vote shown in the dossier (K code).</summary>
    internal sealed class CrisisVoteRef
    {
        public int Pool;
        public int Declarator;
        public int Target;
        public bool Voted;
    }

    /// <summary>Possible terms in one surrender direction (from the game's exact preview).</summary>
    internal sealed class SurrenderLimits
    {
        public HashSet<string> Territories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Free points after the mandatory demands (the rest becomes gold).</summary>
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
        private static readonly HashSet<string> LetterTypes = new HashSet<string> { "privada", "publica", "ultimato", "grupo" };
        private static readonly HashSet<string> Answers = new HashSet<string> { "aceitar", "ignorar" };
        private static readonly HashSet<string> SurrenderAnswers = new HashSet<string> { "aceitar", "recusar" };
        private static readonly HashSet<string> DemandAnswers = new HashSet<string> { "aceitar", "recusar", "enrolar" };
        private static readonly HashSet<string> Investments = new HashSet<string> { "nenhum", "baixo", "medio", "alto" };
        private static readonly HashSet<string> VerdictAnswers = new HashSet<string> { "cumprir", "guerra" };
        private static readonly HashSet<string> TradeModes = new HashSet<string> { "livre", "pedagio", "bloqueio" };
        private static readonly HashSet<string> TollChoices = new HashSet<string> { "pagar", "desviar", "automatico" };
        private const string NoCongress = "não há Congresso mundial nesta partida (ou ele ainda não se formou)";

        /// <summary>Treaties with independent peoples, in the order of the game's MinorTreaty enum.</summary>
        internal static readonly string[] MinorTreatyIds = { "contato", "comercio", "mercenarios", "ciencia", "lucros", "contrato", "intercambio", "vassalo", "anexar" };

        /// <summary>From this point on a letter counts as a repeat of the previous one to the same destination.</summary>
        private const double RepeatThreshold = 0.5;

        private static readonly System.Text.RegularExpressions.Regex PlusNumbers =
            new System.Text.RegularExpressions.Regex(@"([:\[,]\s*)\+(\d)", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Reads and validates the response. Returns null and fills errors when it must ask again.
        /// <paramref name="lenient"/> = last attempt: whatever is still wrong in a letter is resolved here
        /// (drop it or just warn) instead of losing the whole turn.
        /// </summary>
        internal static Decision Parse(string content, ValidationContext context, List<string> errors, bool lenient = false)
        {
            JObject root;
            try
            {
                // "+10" is not valid JSON, but the AI sometimes writes positive sentiments that way.
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
        /// Letter with a problem: asks for another response; on the last attempt (lenient) only the letter is dropped,
        /// with a warning. Before, the error took down the whole decision and the nation's turn was lost over one letter.
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
                    Reject(decision, errors, lenient, $"Carta para '{to}': tipo '{type}' não existe (use privada, publica, ultimato ou grupo).");
                    continue;
                }
                int recipient;
                int groupId = -1;
                if (type == "grupo" || to.StartsWith("GRP", StringComparison.OrdinalIgnoreCase))
                {
                    // Group conversation: only in groups the nation is a member of (SEUS GRUPOS section). The destination
                    // key is negative so the 1-letter-per-recipient rule applies per group.
                    if (!context.Groups.TryGetValue(to.ToUpperInvariant(), out groupId))
                    {
                        Reject(decision, errors, lenient, $"Mensagem para '{to}': você não está nesse grupo. Use o código GRP# de SEUS GRUPOS (ou crie um com criar_grupo).");
                        continue;
                    }
                    type = "grupo";
                    recipient = -100 - groupId;
                }
                else                 if (type == "publica" || to.Equals("todos", StringComparison.OrdinalIgnoreCase))
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
                // A 25% margin avoids asking again over a few words (each retry costs a call).
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

                // Game anchoring (design §9.1) and anti-loop (§9): a repeated letter, or one with something the game
                // does not have, is sent back to be rewritten and, on the last attempt, dropped. An unknown name is also
                // sent back, but on the last attempt it becomes just a warning, so a false alarm does not cost the letter.
                string[] fields = { text, Str(item["assunto"]), Str(item["exigencia"]) };
                List<string> notInGame = Grounding.NotInGameWords(string.Join(" ", fields), context.KnownWords);
                bool repeated = context.LastLetters.TryGetValue(recipient, out PreviousLetter previous)
                    && Grounding.Similarity(text, previous.Text) >= RepeatThreshold;
                // Offering (or calling "mine") a resource the nation has no access to: Māui offered copper without having any resource.
                List<string> notOwned = Grounding.OfferedNotOwned(string.Join(" ", fields), context.OwnResourceWords, context.AllResourceWords);
                if (repeated || notInGame.Count > 0 || notOwned.Count > 0)
                {
                    string why = repeated
                        ? $"repete quase igual a sua carta do turno {previous.Turn} para o mesmo destino. Traga algo novo (um fato, uma oferta, uma consequência) ou não escreva"
                        : notInGame.Count > 0
                        ? $"cita o que o jogo não tem: {string.Join(", ", notInGame)}. Mercadoria é só dinheiro, influência e os recursos de NOMES DO JOGO"
                        : $"oferece recurso que você não tem: {string.Join(", ", notOwned)}. Só ofereça o que está na sua linha \"Recursos:\"; o recurso do outro você pode pedir";
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
                // Each field separately: the subject glued to the end of the text would become "mid-sentence" and each
                // capitalized word in it would look like a name.
                List<string> unknown = context.KnownWords.Count > 0
                    ? fields.SelectMany(f => Grounding.UnknownNames(f, context.KnownWords)).Distinct().ToList()
                    : new List<string>();
                if (unknown.Count > 0)
                {
                    // Game names only (design §9.1): if the AI insists up to the last attempt, the letter is dropped (in
                    // the turn 95 test, an invented envoy survived all three attempts).
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
                    ExpectsReply = type != "grupo" && Bool(item["pede_resposta"], type != "publica"),
                    GroupId = groupId,
                };
                if (type == "grupo")
                {
                    letter.To = Letter.Group;
                }
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
        /// Actions from the closed list. An invalid action asks for another response; on the last attempt (lenient) it is
        /// dropped with a warning, so the turn is not lost over a single action.
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
                            // Zero weight and nothing to bribe: the game would reject it. Drop it without asking again (it would cost a call).
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
                            // No influence: the game would reject it. Drop it without asking again.
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
                    case "rota_pedagio":
                        problem = Empire(item, context, out string tollOwner) ?? OneOf(item, "escolha", TollChoices);
                        summary = $"suas rotas nos postos de {tollOwner}: {Str(item["escolha"])}";
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
                    case "criar_grupo":
                    {
                        string groupName = Str(item["nome"])?.Trim();
                        List<string> members = Handles(item["membros"]);
                        string unknownMember = members.FirstOrDefault(m => !context.KnownEmpires.ContainsKey(m));
                        if (string.IsNullOrWhiteSpace(groupName))
                        {
                            problem = "falta \"nome\" do grupo";
                        }
                        else if (unknownMember != null)
                        {
                            problem = $"membro '{unknownMember}' desconhecido (use códigos E# de nações que você conhece)";
                        }
                        else if (members.Distinct().Count() + 1 < ChatGroup.MinMembers || members.Distinct().Count() + 1 > ChatGroup.MaxMembers)
                        {
                            problem = $"um grupo tem de {ChatGroup.MinMembers} a {ChatGroup.MaxMembers} nações contando você (\"membros\" lista as outras)";
                        }
                        summary = $"criar o grupo '{groupName}' com {string.Join(", ", members)}";
                        break;
                    }
                    case "sair_grupo":
                    {
                        string handle = Str(item["grupo"])?.Trim().ToUpperInvariant();
                        if (handle == null || !context.Groups.ContainsKey(handle))
                        {
                            problem = $"grupo '{handle}' não é seu (use um GRP# de SEUS GRUPOS)";
                        }
                        summary = $"sair do grupo {handle}";
                        break;
                    }
                    case "fundar_bloco":
                    {
                        List<string> guests = Handles(item["convidados"]);
                        string unknownGuest = guests.FirstOrDefault(m => !context.KnownEmpires.ContainsKey(m));
                        if (string.IsNullOrWhiteSpace(Str(item["nome"])))
                        {
                            problem = "falta \"nome\" do bloco";
                        }
                        else if (context.BlocId >= 0)
                        {
                            problem = "você já está num bloco (um por nação); saia dele antes (sair_bloco)";
                        }
                        else if (unknownGuest != null)
                        {
                            problem = $"convidado '{unknownGuest}' desconhecido (use códigos E# de nações que você conhece)";
                        }
                        else if (guests.Distinct().Count() + 1 < Blocs.Bloc.MinMembers)
                        {
                            problem = $"convide pelo menos {Blocs.Bloc.MinMembers - 1} nações (o bloco nasce com {Blocs.Bloc.MinMembers})";
                        }
                        summary = $"fundar o bloco '{Str(item["nome"])}' convidando {string.Join(", ", guests)}";
                        break;
                    }
                    case "responder_bloco":
                    {
                        string handle = Str(item["bloco"])?.Trim().ToUpperInvariant();
                        if (handle == null || !context.BlocInvites.Contains(handle))
                        {
                            problem = $"não há convite do bloco '{handle}' para você (veja CONVITES em BLOCOS)";
                        }
                        summary = $"{(item["aceitar"]?.Type == JTokenType.Boolean && (bool)item["aceitar"] ? "aceitar" : "recusar")} o convite do bloco {handle}";
                        break;
                    }
                    case "pauta_bloco":
                    {
                        string kind = Str(item["tipo"])?.Trim().ToLowerInvariant();
                        if (context.BlocId < 0)
                        {
                            problem = "você não está num bloco formado";
                        }
                        else if (kind == null || !Blocs.BlocMotion.Kinds.Contains(kind))
                        {
                            problem = "\"tipo\" é um de: " + string.Join(", ", Blocs.BlocMotion.Kinds);
                        }
                        else if (kind == "tarifa" || kind == "fim_tarifa" || kind == "admitir" || kind == "expulsar")
                        {
                            problem = Empire(item, context, out _);
                        }
                        summary = $"pauta do bloco: {kind} {Str(item["nacao"])} {Str(item["valor"])}".TrimEnd();
                        break;
                    }
                    case "abrir_pauta":
                    case "votar_pauta":
                    {
                        string handle = Str(item["pauta"])?.Trim().ToUpperInvariant();
                        if (handle == null || !context.BlocMotions.Contains(handle))
                        {
                            problem = $"pauta '{handle}' não está aberta no seu bloco (use um P# de BLOCOS)";
                        }
                        summary = name == "abrir_pauta" ? $"pôr a pauta {handle} em votação" : $"votar {Str(item["voto"])} na pauta {handle}";
                        break;
                    }
                    case "votar_presidente":
                    {
                        if (context.BlocId < 0 || !context.BlocElection)
                        {
                            problem = "não há eleição aberta no seu bloco";
                        }
                        summary = $"votar em {Str(item["candidato"])} para presidente do bloco";
                        break;
                    }
                    case "set_cash_floors":
                    {
                        // Validation messages go back to the LLM, so they stay in the prompt's language.
                        var missing = new[] { "army", "cities", "resources" }.Where(k => item[k] == null || (item[k].Type != JTokenType.Integer && item[k].Type != JTokenType.Float)).ToList();
                        if (missing.Count > 0)
                        {
                            problem = "set_cash_floors precisa de números em " + string.Join(", ", missing.Select(k => "\"" + k + "\""));
                        }
                        summary = $"reservas: exército {Str(item["army"])}, cidades {Str(item["cities"])}, recursos {Str(item["resources"])}";
                        break;
                    }
                    case "sair_bloco":
                    {
                        if (context.BlocId < 0)
                        {
                            problem = "você não está em nenhum bloco";
                        }
                        summary = "sair do bloco";
                        break;
                    }
                    case "nomear_ministro":
                    {
                        string seat = Str(item["pasta"])?.Trim().ToLowerInvariant();
                        string candidate = Str(item["candidato"])?.Trim().ToLowerInvariant();
                        if (seat == null || seat == "mao" || Array.IndexOf(Council.CouncilBank.Portfolios, seat) < 0)
                        {
                            problem = $"\"pasta\" = '{seat}' inválida (use uma das PASTAS VAGAS do seu conselho)";
                        }
                        else if (string.IsNullOrEmpty(candidate))
                        {
                            problem = "falta \"candidato\" (o código p### de um dos candidatos da pasta)";
                        }
                        summary = $"nomear {candidate} para a pasta {seat}";
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
            // A bribe only counts before the vote, and the executor processes in order: bribes always go first.
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
        /// "queixas": a list of the dossier's G codes, a single code, or "todas". codes = null when it is all of them.
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

        /// <summary>List of codes (C#, A#…) in a field that may come as a single string or a list; uppercased.</summary>
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
