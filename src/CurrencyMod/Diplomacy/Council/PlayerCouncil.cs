using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CurrencyMod.Diplomacy.Capture;
using CurrencyMod.Diplomacy.Llm;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CurrencyMod.Diplomacy.Council
{
    /// <summary>One speech in the player's council.</summary>
    internal sealed class CouncilSpeech
    {
        public string Portfolio;
        public string Text;
        /// <summary>The advice in one line (meeting speeches).</summary>
        public string Advice;
        /// <summary>How much the minister's regard for the leader changed with the leader's answer (reactions).</summary>
        public int AffectionChange;
    }

    /// <summary>One player answer to the council and the ministers' reactions.</summary>
    internal sealed class CouncilExchange
    {
        public int Turn;
        public string PlayerText;
        /// <summary>pensando, pronta, erro (status values).</summary>
        public string Status = "pensando";
        public string Error;
        public string Raw;
        public List<CouncilSpeech> Replies = new List<CouncilSpeech>();
    }

    /// <summary>A standing order of the leader: the council plans around it until the leader revokes it.</summary>
    internal sealed class LeaderOrder
    {
        public int Turn;
        public string Text;
    }

    /// <summary>The player's council meeting in one turn.</summary>
    internal sealed class CouncilMeeting
    {
        public int Turn;
        /// <summary>pensando, pronta, erro (status values).</summary>
        public string Status = "pensando";
        public string Error;
        public int Attempts;
        public string Raw;
        public List<CouncilSpeech> Speeches = new List<CouncilSpeech>();
        public List<CouncilExchange> Exchanges = new List<CouncilExchange>();
        /// <summary>Dismissals and hirings made during the meeting (included in the next answer).</summary>
        public List<string> Notes = new List<string>();
        /// <summary>The same notes in the UI language (the ones above go to the AI in Portuguese).</summary>
        public List<string> NotesUi = new List<string>();
        public bool Seen;
        /// <summary>Minister replies to the leader's answers that arrived while the meeting was not on screen.</summary>
        public int UnseenReplies;
        public double CostUsd;
        /// <summary>Audience requested by the leader (otherwise, the automatic interval meeting).</summary>
        public bool Requested;
    }

    /// <summary>
    /// The player's council (design §10.6). Every turn the council meets: the numbers and trends of each portfolio come
    /// from the CouncilEngine rules (the same as the AIs') and one API call writes the meeting in each minister's voice,
    /// from the player's full dossier. The player answers on the council screen and the ministers react (one call per
    /// answer, with the meeting as context, which the API cache reuses). Each minister's regard for the leader changes
    /// with the answers (the AI judges) and with what the leader did in the game (war, peace, agreements against the
    /// previous turn's advice). The player's ministers do not leak letters.
    /// Everything on the main thread, except the API call (ThreadPool, one at a time).
    /// </summary>
    internal static class PlayerCouncil
    {
        private const int MaxMeetingsKept = 12;
        private const int MaxExchangesPerTurn = 8;
        private const int MaxLeaderOrders = 6;
        internal const int MaxPlayerWords = 120;
        private const int MaxSpeechChars = 700;

        private sealed class Job
        {
            public AiWorld World;
            public int Turn;
            public int Player;
            /// <summary>-1 = the meeting; otherwise, the answer index.</summary>
            public int Exchange = -1;
            public string Label;
            public List<ChatMessage> Messages;
            public string System;
            public string User;
            public string Reasoning;
            public int MaxTokens;
            public float Temperature;
            public int Timeout;
        }

        private sealed class Result
        {
            public Job Job;
            public string Raw;
            public JObject Json;
            public string Reasoning;
            public string Error;
            public double Cost;
            public int Calls;
            public int Prompt;
            public int Hit;
            public int Completion;
            public int ReasoningTokens;
        }

        private static readonly ConcurrentQueue<Result> results = new ConcurrentQueue<Result>();
        private static bool inFlight;
        private static float retryAt;
        private static int promptTurn = -1;
        private static string promptGuid;
        private static string system;
        private static string user;

        /// <summary>The meeting is ready and the screen may open by itself (CouncilButton decides when).</summary>
        internal static bool AutoOpenPending;
        internal static float ReadyAt;

        internal static bool Thinking => inFlight;

        internal static CouncilMeeting Meeting(AiWorld world, int turn) => world?.PlayerCouncil.LastOrDefault(m => m.Turn == turn);

        /// <summary>The turn's meeting is ready and not seen yet: the button badge.</summary>
        internal static bool HasNews(AiWorld world, int turn)
        {
            CouncilMeeting meeting = Meeting(world, turn);
            return meeting != null && meeting.Status == "pronta" && !meeting.Seen;
        }

        /// <summary>Turn in which the player opened the council and saw the urgent topics (the badge stops asking).</summary>
        internal static int UrgentSeenTurn = -1;

        /// <summary>
        /// What the council would want to talk about right now even without a meeting (melhorias-jogando item 29), read
        /// from the turn capture, no AI call: demands against the player, wars, a treasury about to run dry.
        /// </summary>
        internal static List<string> UrgentTopics(WorldCapture capture, int player)
        {
            var topics = new List<string>();
            CapturedEmpire me = capture?.Empire(player);
            if (me == null)
            {
                return topics;
            }
            foreach (CapturedRelation relation in me.Relations)
            {
                if (relation == null || relation.Other == player)
                {
                    continue;
                }
                string name = capture.Empire(relation.Other)?.FullName ?? "E" + relation.Other;
                if (relation.TheirDemands.Count > 0 || relation.DemandsAgainstMe)
                {
                    topics.Add(L.F("Exigências de {0} esperam resposta.", name));
                }
                if (relation.AtWar && relation.TheirWarScore > relation.MyWarScore)
                {
                    topics.Add(L.F("A guerra com {0} está pendendo para o lado deles.", name));
                }
            }
            if (me.Money < 0 || (me.MoneyNet < 0 && me.Money + me.MoneyNet * 3 < 0))
            {
                topics.Add(L.T("O tesouro está no vermelho ou acaba em poucos turnos."));
            }
            return topics;
        }

        /// <summary>
        /// The council button badge: the number of new speeches of an unseen meeting, the number of unseen replies, or "!"
        /// for urgent topics the player has not seen this turn. Null when there is nothing new.
        /// </summary>
        internal static string Badge(AiWorld world, WorldCapture capture, int player, int turn, out List<string> urgent)
        {
            urgent = UrgentTopics(capture, player);
            CouncilMeeting meeting = Meeting(world, turn);
            if (meeting != null && meeting.Status == "pronta" && !meeting.Seen)
            {
                return Math.Max(1, meeting.Speeches.Count).ToString();
            }
            if (meeting != null && meeting.UnseenReplies > 0)
            {
                return meeting.UnseenReplies.ToString();
            }
            return urgent.Count > 0 && UrgentSeenTurn != turn ? "!" : null;
        }

        // ---------------- Turn ----------------

        /// <summary>Once per second, via AiModule.Tick (AI on, key and spending cap in order).</summary>
        internal static void Tick(AiWorld world, WorldCapture capture, int turn, int player)
        {
            if (world == null || capture == null || player < 0 || !AiConfig.PlayerCouncil.Value || !AiConfig.Councils.Value)
            {
                return;
            }
            CapturedEmpire me = capture.Empire(player);
            if (me == null || !me.Alive || capture.Turn != turn)
            {
                return;
            }
            AiNation nation = world.Ensure(player);
            FollowUp(world, nation, capture, turn, player);
            CouncilMeeting meeting = Meeting(world, turn);
            if (meeting == null)
            {
                // Automatic meeting only every ConselhoIntervaloTurnos; in between, only by audience (Convene).
                if (turn < NextAutoTurn(world))
                {
                    return;
                }
                meeting = Open(world, turn, requested: false);
            }
            if (inFlight || Time.unscaledTime < retryAt)
            {
                return;
            }
            bool pending = meeting.Status == "pensando" || (meeting.Status == "erro" && meeting.Attempts < 3) || meeting.Exchanges.Any(e => e.Status == "pensando");
            // AI disconnected: the meeting waits (without spending attempts) until the queue's test call.
            if (pending && Llm.Providers.ProviderRouter.AllDown && !Llm.Providers.ProviderRouter.MayCall())
            {
                return;
            }
            if (meeting.Status == "pensando" || (meeting.Status == "erro" && meeting.Attempts < 3))
            {
                DispatchMeeting(world, capture, meeting, player);
                return;
            }
            // Answer left without a call (core reloaded midway): send it again.
            int waiting = meeting.Exchanges.FindIndex(e => e.Status == "pensando");
            if (waiting >= 0 && meeting.Status == "pronta")
            {
                DispatchExchange(world, capture, meeting, waiting, player);
            }
        }

        /// <summary>Latest meeting (of any turn), or null if the council never met.</summary>
        internal static CouncilMeeting LastMeeting(AiWorld world) => world?.PlayerCouncil.LastOrDefault();

        /// <summary>Turn of the next automatic meeting: the latest meeting (automatic or audience) + the interval.</summary>
        internal static int NextAutoTurn(AiWorld world)
        {
            CouncilMeeting last = LastMeeting(world);
            return last == null ? 0 : last.Turn + Math.Max(1, AiConfig.PlayerCouncilInterval.Value);
        }

        private static CouncilMeeting Open(AiWorld world, int turn, bool requested)
        {
            var meeting = new CouncilMeeting { Turn = turn, Requested = requested };
            world.PlayerCouncil.Add(meeting);
            while (world.PlayerCouncil.Count > MaxMeetingsKept)
            {
                world.PlayerCouncil.RemoveAt(0);
            }
            AiModule.Instance?.MarkDirty();
            return meeting;
        }

        /// <summary>The leader requests an audience: the council meets this turn (1 call) and the count restarts.</summary>
        internal static string Convene(string speech = null)
        {
            AiModule module = AiModule.Instance;
            AiWorld world = AiModule.World;
            if (module == null || world == null || module.PlayerIndex < 0 || module.CurrentTurn < 0)
            {
                return "error: " + L.T("sem partida");
            }
            if (!AiConfig.PlayerCouncil.Value || !AiConfig.Councils.Value)
            {
                return "error: " + L.T("o conselho do jogador está desligado no .cfg");
            }
            CouncilMeeting meeting = Meeting(world, module.CurrentTurn);
            if (meeting != null)
            {
                return meeting.Status == "erro" ? Retry() : "error: " + L.T("o conselho já está reunido neste turno");
            }
            CouncilMeeting opened = Open(world, module.CurrentTurn, requested: true);
            retryAt = 0;
            // Words written before convening are not lost: Tick sends them as soon as the meeting is ready.
            string text = Sanitize(speech);
            if (text.Length > 0)
            {
                opened.Exchanges.Add(new CouncilExchange { Turn = module.CurrentTurn, PlayerText = text });
                return "ok: " + L.T("o conselho foi convocado e vai ouvir o que você escreveu");
            }
            return "ok: " + L.T("o conselho foi convocado e já está se reunindo");
        }

        /// <summary>
        /// What the leader did since the previous turn (war, peace, new agreements), comparing the relations: whoever had
        /// advised it gains regard; whoever had advised the opposite loses it.
        /// </summary>
        private static void FollowUp(AiWorld world, AiNation nation, WorldCapture capture, int turn, int player)
        {
            if (world.PlayerRelationMemoTurn == turn)
            {
                return;
            }
            CapturedEmpire me = capture.Empire(player);
            var now = new Dictionary<int, string>();
            foreach (CapturedRelation relation in me.Relations)
            {
                if (relation != null && relation.Knows)
                {
                    now[relation.Other] = Flags(relation);
                }
            }
            if (world.PlayerRelationMemoTurn >= 0 && world.PlayerRelationMemoTurn < turn)
            {
                var done = new List<string>();
                foreach (KeyValuePair<int, string> pair in now)
                {
                    string before = world.PlayerRelationMemo.TryGetValue(pair.Key, out string memo) ? memo : string.Empty;
                    string other = "E" + pair.Key;
                    bool warBefore = before.Contains("W");
                    bool warNow = pair.Value.Contains("W");
                    if (!warBefore && warNow)
                    {
                        done.Add("guerra:" + other);
                        done.Add("atacar:" + other);
                    }
                    if (warBefore && !warNow)
                    {
                        done.Add("paz:" + other);
                    }
                    if ("ATONS".Any(flag => pair.Value.Contains(flag.ToString()) && !before.Contains(flag.ToString())))
                    {
                        done.Add("acordo:" + other);
                        done.Add("acordo");
                    }
                }
                if (done.Count > 0)
                {
                    CouncilEngine.ApplyReaction(nation, done, world.PlayerRelationMemoTurn);
                    Plugin.Log?.LogInfo($"[IA] Player council: since turn {world.PlayerRelationMemoTurn}, the leader did {string.Join(", ", done)}");
                }
            }
            world.PlayerRelationMemo = now;
            world.PlayerRelationMemoTurn = turn;
        }

        private static string Flags(CapturedRelation relation)
        {
            return (relation.AtWar ? "W" : string.Empty) + (relation.Alliance ? "A" : string.Empty) + (relation.Trade ? "T" : string.Empty)
                + (relation.OpenBorders ? "O" : string.Empty) + (relation.NonAggression ? "N" : string.Empty) + (relation.SharedMaps ? "S" : string.Empty);
        }

        // ---------------- Player ----------------

        /// <summary>The player speaks to the council (council screen or "ai council speak").</summary>
        internal static string Speak(string text)
        {
            AiModule module = AiModule.Instance;
            AiWorld world = AiModule.World;
            WorldCapture capture = TurnCapture.Latest;
            if (module == null || world == null || capture == null || module.PlayerIndex < 0)
            {
                return "error: " + L.T("sem partida");
            }
            CouncilMeeting meeting = Meeting(world, module.CurrentTurn);
            text = Sanitize(text);
            if (meeting == null)
            {
                return "error: " + L.T("o conselho não está reunido; convoque uma audiência");
            }
            if (meeting.Status != "pronta")
            {
                return "error: " + L.T("o conselho ainda não se reuniu neste turno");
            }
            if (text.Length == 0)
            {
                return "error: " + L.T("diga alguma coisa ao conselho");
            }
            if (inFlight || meeting.Exchanges.Any(e => e.Status == "pensando"))
            {
                return "error: " + L.T("o conselho ainda está respondendo");
            }
            if (meeting.Exchanges.Count >= MaxExchangesPerTurn)
            {
                return "error: " + L.F("o conselho já ouviu {0} falas suas neste turno; continue no próximo", MaxExchangesPerTurn);
            }
            meeting.Exchanges.Add(new CouncilExchange { Turn = module.CurrentTurn, PlayerText = text });
            DispatchExchange(world, capture, meeting, meeting.Exchanges.Count - 1, module.PlayerIndex);
            module.MarkDirty();
            return "ok: " + L.T("o conselho está ouvindo");
        }

        /// <summary>Dismisses one of the player's ministers and puts another person from the pool in their place.</summary>
        internal static string Fire(string portfolio)
        {
            AiModule module = AiModule.Instance;
            AiWorld world = AiModule.World;
            WorldCapture capture = TurnCapture.Latest;
            if (module == null || world == null || capture == null || module.PlayerIndex < 0)
            {
                return "error: " + L.T("sem partida");
            }
            portfolio = (portfolio ?? string.Empty).Trim().ToLowerInvariant();
            if (!CouncilBank.Portfolios.Contains(portfolio))
            {
                return "error: " + L.F("pasta desconhecida ({0})", string.Join(", ", CouncilBank.Portfolios));
            }
            AiNation nation = world.Ensure(module.PlayerIndex);
            Minister former = nation.Council.FirstOrDefault(m => m.Portfolio == portfolio);
            int formerEra = capture.Empire(module.PlayerIndex)?.EraIndex ?? 0;
            string formerUi = former != null ? CouncilBank.TitleUi(former.Portfolio, formerEra, former.Gender) + " " + former.Name : "?";
            string result = CouncilEngine.Fire(world, nation, capture, portfolio, module.CurrentTurn);
            if (result == null)
            {
                return "error: " + L.T("ninguém nessa pasta");
            }
            // Only the Hand is replaced on the spot; the other portfolios stay vacant until the player appoints someone.
            Minister newcomer = portfolio == "mao" ? nation.Council.FirstOrDefault(m => m.Portfolio == portfolio) : null;
            int era = capture.Empire(module.PlayerIndex)?.EraIndex ?? 0;
            CouncilMeeting noted = Meeting(world, module.CurrentTurn);
            if (noted != null)
            {
                noted.Notes.Add(result + (newcomer != null ? $" (ficha do novo: {Card(newcomer, era, capture)})" : string.Empty));
                noted.NotesUi.Add(newcomer != null
                    ? L.F("Demissão de {0}; no lugar entra {1}", formerUi, newcomer.Name) + " (" + L.F("ficha do novo: {0}", CardUi(newcomer, era)) + ")"
                    : L.F("Demissão de {0}; a pasta fica vaga", formerUi));
            }
            AiModule.Runtime.Event("Player council: " + result + ".");
            module.MarkDirty();
            return "ok: " + result;
        }

        /// <summary>
        /// The player appoints one of the 3 candidates for a vacant portfolio (council screen or "ai council appoint portfolio
        /// id"). Costs influence (CouncilEngine.AppointCost), deducted through the game's own order.
        /// </summary>
        internal static string Appoint(string portfolio, string personalityId)
        {
            AiModule module = AiModule.Instance;
            AiWorld world = AiModule.World;
            WorldCapture capture = TurnCapture.Latest;
            if (module == null || world == null || capture == null || module.PlayerIndex < 0)
            {
                return "error: " + L.T("sem partida");
            }
            portfolio = (portfolio ?? string.Empty).Trim().ToLowerInvariant();
            CapturedEmpire me = capture.Empire(module.PlayerIndex);
            int cost = CouncilEngine.AppointCost(me?.EraIndex ?? 0);
            if ((me?.Influence ?? 0) < cost)
            {
                return "error: " + L.F("influência insuficiente: nomear custa {0}", cost);
            }
            AiNation nation = world.Ensure(module.PlayerIndex);
            Minister minister = CouncilEngine.Appoint(world, nation, capture, portfolio, personalityId, module.CurrentTurn, out string problem);
            if (minister == null)
            {
                return "error: " + CouncilBank.ErrorUi(problem);
            }
            ChargeInfluence(module.PlayerIndex, cost);
            int era = me?.EraIndex ?? 0;
            string who = CouncilBank.TitleUi(minister.Portfolio, era, minister.Gender) + " " + minister.Name;
            CouncilMeeting noted = Meeting(world, module.CurrentTurn);
            if (noted != null)
            {
                noted.Notes.Add($"o líder nomeou {CouncilEngine.Title(minister, era)} {minister.Name} (ficha: {Card(minister, era, capture)})");
                noted.NotesUi.Add(L.F("Nomeação: {0}", who) + " (" + CardUi(minister, era) + ")");
            }
            AiModule.Runtime.Event($"Player council: appointed {minister.Name} ({minister.Portfolio}) for {cost} influence.");
            module.MarkDirty();
            return "ok: " + L.F("{0} nomeado(a) por {1} de influência", who, cost);
        }

        /// <summary>Deducts influence through the game's order (the same as the cheats screen, with a negative value).</summary>
        internal static void ChargeInfluence(int empire, int amount)
        {
            if (amount > 0)
            {
                Amplitude.Mercury.Sandbox.SandboxManager.PostOrder(new Amplitude.Mercury.Interop.OrderGainInfluence { Gain = -amount }, empire);
            }
        }

        /// <summary>Convenes the failed meeting again.</summary>
        internal static string Retry()
        {
            AiModule module = AiModule.Instance;
            CouncilMeeting meeting = Meeting(AiModule.World, module?.CurrentTurn ?? -1);
            if (meeting == null)
            {
                return "error: " + L.T("sem reunião neste turno");
            }
            if (meeting.Status == "erro")
            {
                meeting.Status = "pensando";
                meeting.Attempts = 0;
                meeting.Error = null;
            }
            foreach (CouncilExchange exchange in meeting.Exchanges.Where(e => e.Status == "erro"))
            {
                exchange.Status = "pensando";
                exchange.Error = null;
            }
            retryAt = 0;
            module?.MarkDirty();
            return "ok: " + L.T("o conselho vai se reunir de novo");
        }

        /// <summary>The leader's words without markup, cut at MaxPlayerWords.</summary>
        private static string Sanitize(string text)
        {
            text = (text ?? string.Empty).Replace("<", "‹").Replace(">", "›").Trim();
            string[] words = text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length > MaxPlayerWords ? string.Join(" ", words.Take(MaxPlayerWords)) + "…" : text;
        }

        // ---------------- Calls ----------------

        private static void DispatchMeeting(AiWorld world, WorldCapture capture, CouncilMeeting meeting, int player)
        {
            meeting.Attempts++;
            if (!PreparePrompt(world, capture, player, meeting.Turn, out string error))
            {
                meeting.Status = "erro";
                meeting.Error = error;
                retryAt = Time.unscaledTime + 30f;
                return;
            }
            meeting.Status = "pensando";
            meeting.Error = null;
            Launch(world, meeting.Turn, player, -1, "reuniao", new List<ChatMessage>
            {
                new ChatMessage("system", system),
                new ChatMessage("user", user),
            });
        }

        private static void DispatchExchange(AiWorld world, WorldCapture capture, CouncilMeeting meeting, int index, int player)
        {
            if (inFlight)
            {
                return; // Tick sends it when the current call returns
            }
            if (!PreparePrompt(world, capture, player, meeting.Turn, out string error))
            {
                CouncilExchange failed = meeting.Exchanges[index];
                failed.Status = "erro";
                failed.Error = error;
                return;
            }
            var messages = new List<ChatMessage>
            {
                new ChatMessage("system", system),
                new ChatMessage("user", user),
                new ChatMessage("assistant", meeting.Raw ?? "{}"),
            };
            for (int i = 0; i < index; i++)
            {
                CouncilExchange before = meeting.Exchanges[i];
                if (before.Status == "pronta" && before.Raw != null)
                {
                    messages.Add(new ChatMessage("user", ExchangeRequest(before.PlayerText, null)));
                    messages.Add(new ChatMessage("assistant", before.Raw));
                }
            }
            messages.Add(new ChatMessage("user", ExchangeRequest(meeting.Exchanges[index].PlayerText, meeting.Notes)));
            meeting.Exchanges[index].Status = "pensando";
            meeting.Exchanges[index].Error = null;
            Launch(world, meeting.Turn, player, index, "resposta" + (index + 1), messages);
        }

        private static void Launch(AiWorld world, int turn, int player, int exchange, string label, List<ChatMessage> messages)
        {
            var job = new Job
            {
                World = world,
                Turn = turn,
                Player = player,
                Exchange = exchange,
                Label = label,
                Messages = messages,
                System = system,
                User = messages.Count > 3 ? messages[messages.Count - 1].Content : user,
                Reasoning = AiConfig.ReasoningEffort,
                MaxTokens = Math.Max(1500, AiConfig.MaxTokens.Value),
                Temperature = AiConfig.Temperature.Value,
                Timeout = Math.Max(20, AiConfig.TimeoutSeconds.Value),
            };
            inFlight = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Result result;
                try
                {
                    result = Run(job);
                }
                catch (Exception ex)
                {
                    result = new Result { Job = job, Error = ex.GetType().Name + ": " + ex.Message };
                }
                results.Enqueue(result);
            });
        }

        /// <summary>Worker thread: the call, plus one more if the json comes back broken.</summary>
        private static Result Run(Job job)
        {
            var result = new Result { Job = job };
            var messages = new List<ChatMessage>(job.Messages);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var request = new ChatRequest
                {
                    Messages = messages,
                    MaxTokens = job.MaxTokens,
                    ReasoningEffort = job.Reasoning,
                    Temperature = job.Temperature,
                    JsonMode = true,
                };
                ChatResult chat = Llm.Providers.ProviderRouter.Send(request, job.Timeout);
                result.Calls++;
                result.Cost += chat.CostUsd;
                result.Prompt += chat.PromptTokens;
                result.Hit += chat.CacheHitTokens;
                result.Completion += chat.CompletionTokens;
                result.ReasoningTokens += chat.ReasoningTokens;
                if (!chat.Ok)
                {
                    result.Error = chat.Error;
                    return result;
                }
                result.Raw = chat.Content;
                result.Reasoning = chat.ReasoningContent;
                string problem;
                try
                {
                    result.Json = JObject.Parse(chat.Content ?? string.Empty);
                    problem = job.Exchange < 0
                        ? (string.IsNullOrWhiteSpace((string)result.Json["mao"]) ? "falta a fala da Mão (\"mao\")" : null)
                        : (!(result.Json["falas"] is JArray falas) || falas.Count == 0 ? "faltam as falas (\"falas\")" : null);
                }
                catch (Exception ex)
                {
                    problem = "json inválido: " + ex.Message;
                    result.Json = null;
                }
                if (problem == null && chat.FinishReason != "length")
                {
                    result.Error = null;
                    return result;
                }
                result.Error = problem ?? "resposta cortada por ficar longa demais";
                messages = new List<ChatMessage>(job.Messages)
                {
                    new ChatMessage("assistant", chat.Content ?? string.Empty),
                    new ChatMessage("user", "Sua resposta tem um problema: " + result.Error + ". Responda de novo só com o json completo, mais curto."),
                };
            }
            return result;
        }

        /// <summary>Main thread, every frame (AiModule.Update): applies what came back from the API.</summary>
        internal static void Pump()
        {
            while (results.TryDequeue(out Result result))
            {
                inFlight = false;
                try
                {
                    Apply(result);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"[IA] Player council: failed to apply the response: {ex}");
                    // Without this the meeting stays "pensando" and Tick sends the same paid call again on the next frame.
                    retryAt = Time.unscaledTime + 60f;
                }
            }
        }

        private static void Apply(Result result)
        {
            Job job = result.Job;
            AiWorld world = AiModule.World;
            if (world == null || !ReferenceEquals(world, job.World))
            {
                return; // another game or a save loaded midway
            }
            world.TotalCostUsd += result.Cost;
            world.TotalCalls += result.Calls;
            world.TotalPromptTokens += result.Prompt;
            world.TotalCacheHitTokens += result.Hit;
            world.TotalCompletionTokens += result.Completion;
            world.TotalReasoningTokens += result.ReasoningTokens;
            AiLog.WriteCouncil(world.GameGuid, job.Turn, job.Player, job.Label, job.System, job.User, result.Raw, result.Reasoning, result.Error, result.Cost);
            AiModule.Instance?.MarkDirty();
            CouncilMeeting meeting = Meeting(world, job.Turn);
            if (meeting == null)
            {
                return;
            }
            meeting.CostUsd += result.Cost;
            AiNation nation = world.Ensure(job.Player);
            if (job.Exchange < 0)
            {
                ApplyMeeting(meeting, nation, result);
            }
            else if (job.Exchange < meeting.Exchanges.Count)
            {
                ApplyExchange(meeting.Exchanges[job.Exchange], nation, result);
                RecordOrders(world, job.Turn, result.Json);
                meeting.UnseenReplies += meeting.Exchanges[job.Exchange].Replies.Count;
            }
        }

        private static void ApplyMeeting(CouncilMeeting meeting, AiNation nation, Result result)
        {
            if (result.Json == null || result.Error != null)
            {
                meeting.Status = "erro";
                meeting.Error = result.Error ?? "resposta vazia";
                retryAt = Time.unscaledTime + 20f * Math.Max(1, meeting.Attempts);
                Plugin.Log?.LogWarning($"[IA] Player council, turn {meeting.Turn}: {meeting.Error}");
                return;
            }
            // Sentences citing a mechanism the game does not have are removed (redoing the meeting would cost another call).
            var dropped = new List<string>();
            var speeches = new List<CouncilSpeech>
            {
                new CouncilSpeech { Portfolio = "mao", Text = WithoutIntro(Ground(Clean((string)result.Json["mao"]), dropped), nation, "mao") },
            };
            var seen = new HashSet<string> { "mao" };
            int max = Math.Max(1, AiConfig.PlayerCouncilSpeakers.Value);
            foreach (JObject item in (result.Json["falas"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string portfolio = Portfolio(Text(item["pasta"]));
                string text = Ground(Clean(Text(item["texto"])), dropped);
                if (portfolio == null || seen.Contains(portfolio) || string.IsNullOrEmpty(text) || !nation.Council.Any(m => m.Portfolio == portfolio))
                {
                    continue;
                }
                seen.Add(portfolio);
                speeches.Add(new CouncilSpeech { Portfolio = portfolio, Text = WithoutIntro(text, nation, portfolio), Advice = Ground(Clean(Text(item["conselho"])), dropped) });
                if (speeches.Count > max)
                {
                    break;
                }
            }
            if (dropped.Count > 0)
            {
                Plugin.Log?.LogInfo($"[IA] Player council, turn {meeting.Turn}: {dropped.Count} sentence(s) with an invented mechanism removed: {string.Join(" | ", dropped)}");
            }
            meeting.Speeches = speeches;
            meeting.Raw = result.Raw;
            meeting.Status = "pronta";
            meeting.Error = null;
            meeting.Seen = false;
            AutoOpenPending = true;
            ReadyAt = Time.unscaledTime;
            AiModule.Runtime.Event($"Your council met on turn {meeting.Turn}: {speeches.Count - 1} minister(s) spoke (US$ {result.Cost:0.0000}).");
        }

        private static void ApplyExchange(CouncilExchange exchange, AiNation nation, Result result)
        {
            if (result.Json == null || result.Error != null)
            {
                exchange.Status = "erro";
                exchange.Error = result.Error ?? "resposta vazia";
                return;
            }
            var replies = new List<CouncilSpeech>();
            foreach (JObject item in (result.Json["falas"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string portfolio = Portfolio(Text(item["pasta"]));
                string text = Ground(Clean(Text(item["texto"])), new List<string>());
                if (portfolio == null || string.IsNullOrEmpty(text) || !nation.Council.Any(m => m.Portfolio == portfolio))
                {
                    continue;
                }
                replies.Add(new CouncilSpeech { Portfolio = portfolio, Text = WithoutIntro(text, nation, portfolio) });
                if (replies.Count >= 5)
                {
                    break;
                }
            }
            if (result.Json["apreco"] is JObject changes)
            {
                foreach (JProperty change in changes.Properties())
                {
                    string portfolio = Portfolio(change.Name);
                    Minister minister = nation.Council.FirstOrDefault(m => m.Portfolio == portfolio);
                    if (minister == null || !int.TryParse(change.Value.ToString(), out int delta))
                    {
                        continue;
                    }
                    delta = Math.Max(-8, Math.Min(8, delta));
                    minister.Affection = Math.Max(-100, Math.Min(100, minister.Affection + delta));
                    if (delta <= -5 && (minister.Has("orgulhoso") || minister.Has("rancoroso")))
                    {
                        minister.Loyalty = Math.Max(0, minister.Loyalty - 2);
                    }
                    CouncilSpeech reply = replies.FirstOrDefault(r => r.Portfolio == portfolio);
                    if (reply != null)
                    {
                        reply.AffectionChange += delta;
                    }
                }
            }
            exchange.Replies = replies;
            exchange.Raw = result.Raw;
            exchange.Status = "pronta";
            exchange.Error = null;
        }

        /// <summary>Stores the order the leader just gave ("order") and drops the ones revoked ("revoke": [1, 2]).</summary>
        private static void RecordOrders(AiWorld world, int turn, JObject json)
        {
            if (json == null || world == null)
            {
                return;
            }
            var revoked = new HashSet<int>();
            if (json["revoke"] is JArray numbers)
            {
                foreach (JToken number in numbers)
                {
                    string code = number.ToString().TrimStart('O', 'o');
                    if (int.TryParse(code, out int index))
                    {
                        revoked.Add(index - 1);
                    }
                }
            }
            for (int i = world.LeaderOrders.Count - 1; i >= 0; i--)
            {
                if (revoked.Contains(i))
                {
                    world.LeaderOrders.RemoveAt(i);
                }
            }
            string order = Clean(Text(json["order"]));
            if (!string.IsNullOrWhiteSpace(order) && !world.LeaderOrders.Any(o => o.Text == order))
            {
                world.LeaderOrders.Add(new LeaderOrder { Turn = turn, Text = order });
                while (world.LeaderOrders.Count > MaxLeaderOrders)
                {
                    world.LeaderOrders.RemoveAt(0);
                }
            }
            if (revoked.Count > 0 || !string.IsNullOrWhiteSpace(order))
            {
                Plugin.Log?.LogInfo($"[IA] Player council, turn {turn}: standing orders now {world.LeaderOrders.Count} (new: {order ?? "-"}, revoked: {revoked.Count}).");
            }
        }

        /// <summary>The leader's standing orders for the prompt, numbered O1, O2… (null when there are none).</summary>
        private static string OrdersText(AiWorld world)
        {
            if (world.LeaderOrders.Count == 0)
            {
                return null;
            }
            var text = new StringBuilder();
            text.AppendLine("== ORDENS EM VIGOR DO LÍDER (decididas; o conselho trabalha para cumpri-las) ==");
            for (int i = 0; i < world.LeaderOrders.Count; i++)
            {
                text.AppendLine($"- O{i + 1} (turno {world.LeaderOrders[i].Turn}): {world.LeaderOrders[i].Text}");
            }
            return text.ToString();
        }

        /// <summary>
        /// Test ("ai test council-json"): a response out of format (speech as loose text, portfolio and advice as a list)
        /// goes through the real ApplyMeeting and ApplyExchange, in a throwaway meeting. It used to throw an exception and
        /// the meeting stayed "pensando", repeating the paid call.
        /// </summary>
        internal static string SelfTestMalformed(AiWorld world, int player)
        {
            AiNation nation = world.Ensure(player);
            string portfolio = nation.Council.FirstOrDefault(m => m.Portfolio != "mao")?.Portfolio ?? "guerra";
            JObject json = JObject.Parse("{\"mao\":\"Hand test.\",\"falas\":[\"loose speech\",42,{\"pasta\":[\"x\"],\"texto\":\"portfolio as a list\"},"
                + "{\"pasta\":\"" + portfolio + "\",\"texto\":\"Valid speech.\",\"conselho\":[\"advice\",\"as a list\"]}]}");
            bool autoOpen = AutoOpenPending;
            float readyAt = ReadyAt;
            var text = new StringBuilder();
            try
            {
                var meeting = new CouncilMeeting { Turn = -1, Status = "pensando" };
                ApplyMeeting(meeting, nation, new Result { Json = json, Raw = json.ToString() });
                text.AppendLine($"meeting: {meeting.Status}; speeches kept: {string.Join(", ", meeting.Speeches.Select(s => s.Portfolio + (s.Advice != null ? " (advice: " + s.Advice + ")" : string.Empty)))}");
                var exchange = new CouncilExchange { Turn = -1, Status = "pensando" };
                ApplyExchange(exchange, nation, new Result { Json = json, Raw = json.ToString() });
                text.AppendLine($"answer: {exchange.Status}; replies kept: {exchange.Replies.Count}");
            }
            catch (Exception ex)
            {
                text.AppendLine("FAILED with exception: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                AutoOpenPending = autoOpen;
                ReadyAt = readyAt;
            }
            return text.ToString();
        }

        /// <summary>Text field of the json, or null: a list or object in place of the text would make the (string) cast throw.</summary>
        private static string Text(JToken token) => (token as JValue)?.Value?.ToString();

        private static string Portfolio(string value)
        {
            string key = (value ?? string.Empty).Trim().ToLowerInvariant()
                .Replace("ç", "c").Replace("ã", "a").Replace("á", "a").Replace("é", "e").Replace("ê", "e").Replace("í", "i").Replace("ó", "o").Replace("ô", "o").Replace("ú", "u");
            if (key == "fe" || key == "fé")
            {
                return "fe";
            }
            if (key.StartsWith("mao") || key == "a mao")
            {
                return "mao";
            }
            return CouncilBank.Portfolios.FirstOrDefault(p => p == key || key.StartsWith(p));
        }

        /// <summary>
        /// Strips the self-introduction from the start of the speech ("Hamilcar Barca, Marechal. Sem ferro…" → "Sem
        /// ferro…"): the screen already shows name and office. Only when the first sentence is short (up to 5 words) and
        /// starts with the name.
        /// </summary>
        private static string WithoutIntro(string text, AiNation nation, string portfolio)
        {
            Minister minister = nation.Council.FirstOrDefault(m => m.Portfolio == portfolio);
            string first = minister?.Name?.Split(' ').FirstOrDefault();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(first) || !text.StartsWith(first, StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
            int end = text.IndexOf(". ", StringComparison.Ordinal);
            if (end < 0 || end > 80)
            {
                return text;
            }
            string intro = text.Substring(0, end);
            if (intro.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 5)
            {
                return text;
            }
            string rest = text.Substring(end + 2).TrimStart();
            return rest.Length > 0 ? char.ToUpperInvariant(rest[0]) + rest.Substring(1) : text;
        }

        /// <summary>
        /// Mechanisms the council has already invented (turn 32: "só com reciprocidade plena de escribas e costumes"). Added
        /// to the goods and mechanisms of Grounding.NotInGame, which apply to the nations' letters.
        /// </summary>
        private static readonly HashSet<string> CouncilInvented = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "reciprocidade", "escribas", "alfândega", "alfândegas", "alfandega", "aduana", "aduanas", "embaixada", "embaixadas",
            "refém", "reféns", "tribunal", "tribunais", "casamento", "dote",
            "reciprocity", "scribes", "customs", "embassy", "embassies", "hostage", "hostages", "tribunal", "marriage", "dowry",
            "reciprocidad", "escribas", "embajada", "embajadas", "rehén", "rehenes",
            "réciprocité", "scribes", "douane", "douanes", "ambassade", "ambassades", "otage", "otages", "mariage", "dot",
            "gegenseitigkeit", "schreiber", "zoll", "zölle", "botschaft", "botschaften", "geisel", "geiseln", "heirat", "mitgift",
        };

        /// <summary>Real vocabulary from the last meeting's dossier (game names, map resources): "Seda" on the map is valid.</summary>
        private static HashSet<string> knownWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Removes sentences citing an invented mechanism. Returns the text without them (empty if nothing is left).</summary>
        internal static string Ground(string text, List<string> dropped)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
            var kept = new List<string>();
            foreach (string sentence in System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?…])\s+"))
            {
                List<string> bad = Grounding.Words(sentence).Where(w => CouncilInvented.Contains(w) && !knownWords.Contains(w)).ToList();
                bad.AddRange(Grounding.NotInGameWords(sentence, knownWords));
                if (bad.Count > 0)
                {
                    dropped.Add($"\"{sentence.Trim()}\" ({string.Join(", ", bad.Distinct())})");
                    continue;
                }
                kept.Add(sentence);
            }
            return string.Join(" ", kept).Trim();
        }

        private static string Clean(string text)
        {
            text = (text ?? string.Empty).Replace("<", "‹").Replace(">", "›").Trim();
            return text.Length > MaxSpeechChars ? text.Substring(0, MaxSpeechChars).TrimEnd() + "…" : text;
        }

        // ---------------- Prompt ----------------

        /// <summary>Player dossier, minister cards and the meeting request (the same prefix in the answers: cache).</summary>
        private static bool PreparePrompt(AiWorld world, WorldCapture capture, int player, int turn, out string error)
        {
            error = null;
            if (promptTurn == turn && promptGuid == world.GameGuid && system != null && user != null)
            {
                return true;
            }
            CapturedEmpire me = capture.Empire(player);
            if (me == null || capture.Turn != turn)
            {
                error = "sem a foto do turno";
                return false;
            }
            DossierBuild build;
            try
            {
                build = DossierBuilder.Build(world, capture, player);
            }
            catch (Exception ex)
            {
                error = "falha ao montar o dossiê: " + ex.Message;
                Plugin.Log?.LogError($"[IA] Player council: {error}");
                return false;
            }
            AiNation nation = world.Ensure(player);
            knownWords = build.Context?.KnownWords ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            system = SystemPrompt(me);
            string orders = OrdersText(world);
            user = CleanDossier(build.Text) + "\n\n" + Roster(nation, capture, me) + "\n" + (orders != null ? orders + "\n" : string.Empty) + MeetingRequest(Math.Max(1, AiConfig.PlayerCouncilSpeakers.Value));
            promptTurn = turn;
            promptGuid = world.GameGuid;
            return true;
        }

        private static string SystemPrompt(CapturedEmpire me)
        {
            string leader = string.IsNullOrWhiteSpace(me.Leader) ? "o líder" : me.Leader;
            var text = new StringBuilder();
            text.AppendLine($"Você escreve as falas do conselho de ministros de {leader}, que governa {me.FullName} numa partida de Humankind ({me.EraName}). "
                + "O líder é o jogador humano. A cada turno o conselho se reúne antes de ele jogar: a Mão abre a pauta e os ministros opinam pela própria pasta. "
                + "Depois o líder pode responder, e eles reagem.");
            text.AppendLine();
            text.AppendLine("O conselho é de gente:");
            text.AppendLine(AiConfig.WritingCode == "pt"
                ? "- Cada ministro tem nome, pasta, traços e voz (fichas no fim do dossiê). Fale como ele falaria, no tom da era, em português do Brasil. Nada de jargão técnico (\"IA\", \"json\", \"token\")."
                : $"- Cada ministro tem nome, pasta, traços e voz (fichas no fim do dossiê). Fale como ele falaria, no tom da era. Nada de jargão técnico (\"IA\", \"json\", \"token\").\n- IDIOMA: o rei fala {L.EnglishName(AiConfig.WritingCode)}. Escreva todas as falas e textos livres em {L.EnglishName(AiConfig.WritingCode)}, mesmo com o dossiê em português; nomes do jogo como estão no dossiê; chaves do json e códigos exatamente como especificados.");
            text.AppendLine("- O apreço de cada um pelo líder vai de -100 a 100: baixo deixa a fala fria, seca ou ressentida; alto, calorosa e franca. A lealdade (0 a 100) muda só o tom com que ele aceita ser contrariado, nunca se ele obedece.");
            text.AppendLine("- O LÍDER TEM A ÚLTIMA PALAVRA. Os ministros aconselham; quem decide é ele. Podem discordar entre si e do líder quando ele pergunta ou pede opinião; a Mão media e resume. Ninguém trai, ameaça o líder ou vaza segredos.");
            text.AppendLine("- ORDEM DO LÍDER: quando o líder manda ou pede um plano (\"pensem em como cercar os X e me tragam sugestões\", \"quero guerra com Y\", \"cortem o comércio com Z\"), a decisão já está tomada. "
                + "Os ministros obedecem e trabalham a favor dela: cada um da pasta envolvida ENTREGA a sua parte do plano, com os passos concretos na tela do jogo (quais postos taxar ou bloquear, quais acordos cortar, quem chamar como aliado, que tropas mover), a ordem e o custo em dinheiro e em relações. "
                + "Quem discorda pode, DEPOIS de entregar o plano, apontar o risco numa frase (\"cumpro, mas saiba que…\"), no tom da lealdade e do apreço. "
                + "Ninguém recusa, adia, pede para repensar, troca a ordem por outra sugestão nem repete a objeção depois. Ambição alta pode tentar tirar vantagem pessoal do plano, sem sabotá-lo.");
            text.AppendLine("- ORDENS EM VIGOR (lista no fim do dossiê, quando houver) continuam valendo nos turnos seguintes: nenhum conselho as contradiz. Os conselhos das reuniões seguintes ajudam a cumpri-las ou relatam o andamento; quem achava ruim já disse e não volta ao assunto.");
            text.AppendLine("- A tela já mostra o nome e o cargo de quem fala: não comece a fala se apresentando.");
            text.AppendLine("- As fichas trazem os números de cada pasta e a tendência de cada ministro: use como base, sem repetir a ficha.");
            text.AppendLine();
            text.AppendLine("Ancoragem (obrigatória):");
            text.AppendLine("- Só fatos do dossiê: números, nações, líderes, cidades, territórios, exércitos, cartas e exigências que aparecem lá. Nada inventado: sem enviados, boatos, recursos ou acontecimentos que não estão no dossiê. Se a pasta não tem nada a dizer, o ministro fica calado.");
            text.AppendLine("- \"Você\" no dossiê é o líder. O dossiê foi escrito para outro sistema: nomes de ferramentas (como responder_exigencias, ordem_exercito ou definir_foco) não existem para o conselho. Os ministros falam do que o líder faz na tela do jogo: aceitar ou recusar exigências, propor um acordo, declarar guerra ou propor paz, mover tropas, mudar a produção de uma cidade, escrever uma carta, patrocinar um povo independente.");
            text.AppendLine("- Cada conselho é uma ação concreta que o líder pode fazer agora ou nos próximos turnos.");
            text.AppendLine("- Ação concreta é só o que o jogo tem: os acordos do jogo (econômico, informação, cultural, militar, aliança), paz, guerra, presente de dinheiro, influência, cidade ou território, "
                + "aceitar ou recusar exigências e propostas, mover ou recolher exército, mudar a produção ou o foco, pedágio ou bloqueio nos postos comerciais, recurso pela rota comercial, carta. "
                + "Acordo do jogo não tem cláusula: é proposto, aceito ou recusado, sem condição extra (nada de \"só com reciprocidade de…\").");
            text.AppendLine("- Não invente pessoas, ofícios, lugares, mercadorias, cerimônias, regras nem mecanismos que o jogo não tem (escribas enviados, embaixadas, alfândegas, reciprocidade de costumes, troca de prisioneiros, reféns, tribunais, casamentos). "
                + "O título de um ministro (Escriba-Mor, Mestre dos Ritos...) é só o cargo dele: não vira mecanismo do jogo.");
            text.AppendLine("- Os códigos de nação, cidade, exército e território (E3, C14, A7, T31) ajudam o líder a achar as coisas: use com o nome, como \"Ḫattuša (T106)\". "
                + "Os códigos de queixa (G1, G2...), de povo independente (M4) e do Congresso (K6, V2, I3, e o E da nação no voto) não aparecem na tela do jogo: descreva a queixa (\"a queixa pelo ataque sem provocação\"), diga o nome do povo ou descreva a votação (\"vote a seu favor na disputa com os Nórdicos\").");
            text.AppendLine();
            text.AppendLine("Pense pouco: o que mudou, o que mais pesa, quem fala. Não releia nem resuma o dossiê no raciocínio; depois escreva o json.");
            return text.ToString();
        }

        private static string MeetingRequest(int speakers)
        {
            return "== A REUNIÃO DESTE TURNO ==\n"
                + $"Escreva a reunião. A Mão abre em 2 a 4 frases: o que mais pesa hoje e o que vence logo. Depois falam até {speakers} ministros, os de pasta mais urgente hoje; "
                + "quem não tem nada relevante fica calado. Cada fala tem 1 a 3 frases, até 60 palavras, na voz do ministro, e um conselho concreto em uma linha. "
                + "Se há ordens em vigor do líder, nenhum conselho vai contra elas: os ministros relatam o andamento e o próximo passo para cumpri-las.\n"
                + "Responda só com json, sem texto fora dele:\n"
                + "{\"mao\": \"...\", \"falas\": [{\"pasta\": \"guerra\", \"texto\": \"...\", \"conselho\": \"...\"}]}\n"
                + "Pastas: " + string.Join(", ", CouncilBank.Portfolios.Where(p => p != "mao")) + ".";
        }

        private static string ExchangeRequest(string playerText, List<string> notes)
        {
            var text = new StringBuilder();
            text.AppendLine($"O líder responde ao conselho: «{playerText}»");
            if (notes != null && notes.Count > 0)
            {
                text.AppendLine("Mudanças no conselho desde a reunião: " + string.Join(" ", notes));
            }
            text.AppendLine("Reajam. Falam de 1 a 4 ministros: os que a resposta toca e quem o líder chamou pelo nome ou pela pasta. "
                + "Cada um em 1 a 3 frases, até 60 palavras, na própria voz: se ele só perguntou ou conversou, podem concordar, discordar, pedir detalhe ou se ofender, sempre com os fatos do dossiê. "
                + "A Mão (pasta \"mao\") pode fechar: com o que o líder decidiu, se ele decidiu; se ele só perguntou, com as opções que ficaram na mesa. Ninguém decide pelo líder.");
            text.AppendLine("Se a resposta é uma ORDEM ou um pedido de plano (o líder manda fazer ou pede sugestões de COMO fazer), a decisão é dele e ninguém a discute: "
                + "cada ministro envolvido entrega primeiro a parte dele do plano, com passos concretos na tela do jogo, nomes e códigos do dossiê, ordem e custo (até 90 palavras nesse caso), "
                + "e só no fim, se discordar, aponta o risco numa frase. Ninguém propõe fazer outra coisa no lugar. A Mão fecha com o plano em ordem: o que fazer neste turno e nos próximos.");
            text.AppendLine("Diga também como o apreço de cada um pelo líder mudou com essa resposta, de -8 a 8 (só quem mudou).");
            text.AppendLine("Se a resposta é uma ordem nova, resuma-a em \"order\" (uma linha, com os nomes, como \"Cercar economicamente e bloquear os Carais (E5)\"); senão, deixe \"order\" vazio. "
                + "Se o líder cancelou ou mudou uma ordem em vigor, ponha o número dela em \"revoke\" (O2 → 2).");
            text.Append("Responda só com json: {\"falas\": [{\"pasta\": \"guerra\", \"texto\": \"...\"}], \"apreco\": {\"guerra\": 2}, \"order\": \"\", \"revoke\": []}");
            return text.ToString();
        }

        /// <summary>The dossier without the AI decision request and without the council section (the full cards come later).</summary>
        private static string CleanDossier(string text)
        {
            var output = new StringBuilder();
            bool skipping = false;
            foreach (string raw in (text ?? string.Empty).Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("== ", StringComparison.Ordinal))
                {
                    skipping = line.StartsWith("== SEU CONSELHO", StringComparison.Ordinal);
                }
                if (skipping || line.StartsWith("Decida o seu turno", StringComparison.Ordinal))
                {
                    continue;
                }
                output.AppendLine(line);
            }
            return output.ToString().TrimEnd();
        }

        private static string Roster(AiNation nation, WorldCapture capture, CapturedEmpire me)
        {
            int era = me.EraIndex;
            var text = new StringBuilder();
            text.AppendLine("== SEUS MINISTROS (fichas para escrever as falas) ==");
            foreach (Minister minister in nation.Council)
            {
                text.AppendLine("- " + minister.Portfolio + " · " + Card(minister, era, capture));
                if (!string.IsNullOrEmpty(minister.Opinion))
                {
                    text.AppendLine("  Números da pasta: " + minister.Opinion + (minister.Advice != null ? " Tende a aconselhar: " + AdviceText(minister.Advice, capture) + "." : string.Empty));
                }
            }
            // Vacant portfolios: nobody speaks for them; the Hand may suggest appointing someone (the leader picks on the council screen).
            List<string> vacant = CouncilBank.Portfolios.Where(p => p != "mao" && !nation.Council.Any(m => m.Portfolio == p)).ToList();
            if (vacant.Count > 0)
            {
                text.AppendLine($"Pastas vagas (o líder nomeia na tela do conselho, por {CouncilEngine.AppointCost(era)} de influência): "
                    + string.Join(", ", vacant.Select(p => CouncilBank.PortfolioNames.TryGetValue(p, out string name) ? name : p)) + ".");
            }
            return text.ToString();
        }

        /// <summary>The short card in the UI language (Card, in Portuguese, goes to the AI).</summary>
        internal static string CardUi(Minister minister, int era)
        {
            var parts = new List<string>
            {
                CouncilBank.TitleUi(minister.Portfolio, era, minister.Gender) + " " + minister.Name,
                L.F("Traços: {0}", string.Join(", ", minister.Traits.Select(CouncilBank.TraitUi))),
            };
            if (!string.IsNullOrWhiteSpace(minister.Voice))
            {
                parts.Add(L.F("Voz: {0}", L.T(minister.Voice))); // voice: from the default pool, marked with L.N
            }
            parts.Add(L.F("Credibilidade {0}", minister.Credibility));
            return string.Join(" · ", parts);
        }

        /// <summary>Short card of a minister (title, name, traits, voice and numbers).</summary>
        internal static string Card(Minister minister, int era, WorldCapture capture)
        {
            var parts = new List<string>
            {
                $"{CouncilEngine.Title(minister, era)} {minister.Name}",
                "traços: " + string.Join(", ", minister.Traits),
            };
            if (!string.IsNullOrWhiteSpace(minister.Voice))
            {
                parts.Add("voz: " + minister.Voice);
            }
            parts.Add($"credibilidade {minister.Credibility}, apreço pelo líder {(minister.Affection > 0 ? "+" : string.Empty)}{minister.Affection}, lealdade {minister.Loyalty}, ambição {minister.Ambition}");
            var likes = minister.Sympathies.Where(p => p.Value > 0).Select(p => Who(capture, p.Key)).ToList();
            var dislikes = minister.Sympathies.Where(p => p.Value < 0).Select(p => Who(capture, p.Key)).ToList();
            if (likes.Count > 0)
            {
                parts.Add("simpatiza com " + string.Join(" e ", likes));
            }
            if (dislikes.Count > 0)
            {
                parts.Add("desconfia de " + string.Join(" e ", dislikes));
            }
            return string.Join(" · ", parts);
        }

        /// <summary>Advice tag from the rules, in Portuguese ("paz:E3" → "paz com os Persas (E3)").</summary>
        internal static string AdviceText(string advice, WorldCapture capture)
        {
            string[] pair = (advice ?? string.Empty).Split(':');
            string target = pair.Length > 1 && pair[1].StartsWith("E") && int.TryParse(pair[1].Substring(1), out int empire) ? Who(capture, empire) : null;
            switch (pair[0])
            {
                case "paz": return "paz com " + target;
                case "guerra": return "guerra contra " + target;
                case "atacar": return "atacar " + target;
                case "acordo": return target != null ? "um acordo com " + target : "um acordo comercial";
                case "aceitar": return "aceitar a proposta de " + target;
                case "recusar": return target != null ? "recusar " + target : "recusar";
                case "ceder": return "ceder às exigências de " + target;
                case "resistir": return "resistir às exigências de " + target;
                case "foco": return "dar prioridade a " + (pair.Length > 1 ? pair[1] : "algo");
                case "economizar": return "economizar";
                case "presentear": return "dar presentes ou patrocinar um povo independente";
                case "defender": return "reforçar a defesa";
                case "cautela": return "cautela com o que se escreve em cartas";
                default: return advice;
            }
        }

        private static string Who(WorldCapture capture, int empire)
        {
            CapturedEmpire them = capture?.Empire(empire);
            return them != null ? $"{them.Culture} (E{empire})" : $"E{empire}";
        }

        // ---------------- State for the screen and the commands ----------------

        internal static string Describe(AiWorld world, int turn)
        {
            if (!AiConfig.PlayerCouncil.Value)
            {
                return "player council: off ([IA] ConselhoDoJogador)";
            }
            CouncilMeeting meeting = Meeting(world, turn);
            if (meeting == null)
            {
                return "player council: no meeting this turn";
            }
            string state = meeting.Status == "pronta" ? $"ready, {meeting.Speeches.Count - 1} speech(es)" : meeting.Status == "erro" ? "error: " + meeting.Error : "meeting";
            return $"player council: turn {meeting.Turn} meeting {state} · {meeting.Exchanges.Count} answer(s) of yours · US$ {meeting.CostUsd:0.0000}"
                + (meeting.Seen ? " · seen" : string.Empty) + (inFlight ? " · call in flight" : string.Empty);
        }
    }
}
