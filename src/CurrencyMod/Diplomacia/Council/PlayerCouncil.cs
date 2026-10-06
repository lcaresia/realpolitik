using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CurrencyMod.Diplomacia.Capture;
using CurrencyMod.Diplomacia.Llm;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CurrencyMod.Diplomacia.Council
{
    /// <summary>Uma fala no conselho do jogador.</summary>
    internal sealed class CouncilSpeech
    {
        public string Portfolio;
        public string Text;
        /// <summary>O conselho em uma linha (falas da reunião).</summary>
        public string Advice;
        /// <summary>Quanto o apreço do ministro pelo líder mudou com a resposta dele (reações).</summary>
        public int AffectionChange;
    }

    /// <summary>Uma resposta do jogador ao conselho e as reações dos ministros.</summary>
    internal sealed class CouncilExchange
    {
        public int Turn;
        public string PlayerText;
        /// <summary>pensando, pronta, erro.</summary>
        public string Status = "pensando";
        public string Error;
        public string Raw;
        public List<CouncilSpeech> Replies = new List<CouncilSpeech>();
    }

    /// <summary>A reunião do conselho do jogador num turno.</summary>
    internal sealed class CouncilMeeting
    {
        public int Turn;
        /// <summary>pensando, pronta, erro.</summary>
        public string Status = "pensando";
        public string Error;
        public int Attempts;
        public string Raw;
        public List<CouncilSpeech> Speeches = new List<CouncilSpeech>();
        public List<CouncilExchange> Exchanges = new List<CouncilExchange>();
        /// <summary>Demissões e contratações feitas durante a reunião (entram na próxima resposta).</summary>
        public List<string> Notes = new List<string>();
        public bool Seen;
        public double CostUsd;
    }

    /// <summary>
    /// O conselho do jogador (design §10.6). A cada turno o conselho se reúne: os números e as tendências de cada
    /// pasta vêm das regras do CouncilEngine (as mesmas das IAs) e uma chamada à API escreve a reunião na voz de cada
    /// ministro, a partir do dossiê completo do jogador. O jogador responde na tela do conselho e os ministros reagem
    /// (uma chamada por resposta, com a reunião como contexto, que o cache da API aproveita). O apreço de cada ministro
    /// pelo líder muda com as respostas (a IA julga) e com o que o líder fez no jogo (guerra, paz, acordos contra o
    /// conselho do turno anterior). Os ministros do jogador não vazam cartas.
    /// Tudo na thread principal, menos a chamada à API (ThreadPool, uma de cada vez).
    /// </summary>
    internal static class PlayerCouncil
    {
        private const int MaxMeetingsKept = 12;
        private const int MaxExchangesPerTurn = 8;
        internal const int MaxPlayerWords = 120;
        private const int MaxSpeechChars = 700;

        private sealed class Job
        {
            public IaWorld World;
            public int Turn;
            public int Player;
            /// <summary>-1 = a reunião; senão, o índice da resposta.</summary>
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

        /// <summary>A reunião ficou pronta e a tela pode abrir sozinha (CouncilButton decide quando).</summary>
        internal static bool AutoOpenPending;
        internal static float ReadyAt;

        internal static bool Thinking => inFlight;

        internal static CouncilMeeting Meeting(IaWorld world, int turn) => world?.PlayerCouncil.LastOrDefault(m => m.Turn == turn);

        /// <summary>Reunião do turno pronta e ainda não vista: o selo do botão.</summary>
        internal static bool HasNews(IaWorld world, int turn)
        {
            CouncilMeeting meeting = Meeting(world, turn);
            return meeting != null && meeting.Status == "pronta" && !meeting.Seen;
        }

        // ---------------- Turno ----------------

        /// <summary>Uma vez por segundo, pelo IaModule.Tick (IA ligada, chave e teto de gasto em ordem).</summary>
        internal static void Tick(IaWorld world, WorldCapture capture, int turn, int player)
        {
            if (world == null || capture == null || player < 0 || !IaConfig.PlayerCouncil.Value || !IaConfig.Councils.Value)
            {
                return;
            }
            CapturedEmpire me = capture.Empire(player);
            if (me == null || !me.Alive || capture.Turn != turn)
            {
                return;
            }
            IaNation nation = world.Ensure(player);
            FollowUp(world, nation, capture, turn, player);
            CouncilMeeting meeting = Meeting(world, turn);
            if (meeting == null)
            {
                meeting = new CouncilMeeting { Turn = turn };
                world.PlayerCouncil.Add(meeting);
                while (world.PlayerCouncil.Count > MaxMeetingsKept)
                {
                    world.PlayerCouncil.RemoveAt(0);
                }
                IaModule.Instance?.MarkDirty();
            }
            if (inFlight || Time.unscaledTime < retryAt)
            {
                return;
            }
            if (meeting.Status == "pensando" || (meeting.Status == "erro" && meeting.Attempts < 3))
            {
                DispatchMeeting(world, capture, meeting, player);
                return;
            }
            // Resposta que ficou sem chamada (núcleo recarregado no meio): manda de novo.
            int waiting = meeting.Exchanges.FindIndex(e => e.Status == "pensando");
            if (waiting >= 0 && meeting.Status == "pronta")
            {
                DispatchExchange(world, capture, meeting, waiting, player);
            }
        }

        /// <summary>
        /// O que o líder fez desde o turno anterior (guerra, paz, acordos novos), comparando as relações: quem tinha
        /// aconselhado isso ganha apreço; quem tinha aconselhado o contrário perde.
        /// </summary>
        private static void FollowUp(IaWorld world, IaNation nation, WorldCapture capture, int turn, int player)
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
                    Plugin.Log?.LogInfo($"[IA] Conselho do jogador: desde o turno {world.PlayerRelationMemoTurn}, o líder fez {string.Join(", ", done)}");
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

        // ---------------- Jogador ----------------

        /// <summary>O jogador fala ao conselho (tela do conselho ou "ia conselho falar").</summary>
        internal static string Speak(string text)
        {
            IaModule module = IaModule.Instance;
            IaWorld world = IaModule.World;
            WorldCapture capture = TurnCapture.Latest;
            if (module == null || world == null || capture == null || module.PlayerIndex < 0)
            {
                return "erro: " + L.T("sem partida");
            }
            CouncilMeeting meeting = Meeting(world, module.CurrentTurn);
            text = (text ?? string.Empty).Replace("<", "‹").Replace(">", "›").Trim();
            if (meeting == null || meeting.Status != "pronta")
            {
                return "erro: " + L.T("o conselho ainda não se reuniu neste turno");
            }
            if (text.Length == 0)
            {
                return "erro: " + L.T("diga alguma coisa ao conselho");
            }
            if (inFlight || meeting.Exchanges.Any(e => e.Status == "pensando"))
            {
                return "erro: " + L.T("o conselho ainda está respondendo");
            }
            if (meeting.Exchanges.Count >= MaxExchangesPerTurn)
            {
                return "erro: " + L.F("o conselho já ouviu {0} falas suas neste turno; continue no próximo", MaxExchangesPerTurn);
            }
            string[] words = text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > MaxPlayerWords)
            {
                text = string.Join(" ", words.Take(MaxPlayerWords)) + "…";
            }
            meeting.Exchanges.Add(new CouncilExchange { Turn = module.CurrentTurn, PlayerText = text });
            DispatchExchange(world, capture, meeting, meeting.Exchanges.Count - 1, module.PlayerIndex);
            module.MarkDirty();
            return "ok: " + L.T("o conselho está ouvindo");
        }

        /// <summary>Demite um ministro do jogador e põe outra pessoa do banco no lugar.</summary>
        internal static string Fire(string portfolio)
        {
            IaModule module = IaModule.Instance;
            IaWorld world = IaModule.World;
            WorldCapture capture = TurnCapture.Latest;
            if (module == null || world == null || capture == null || module.PlayerIndex < 0)
            {
                return "erro: " + L.T("sem partida");
            }
            portfolio = (portfolio ?? string.Empty).Trim().ToLowerInvariant();
            if (!CouncilBank.Portfolios.Contains(portfolio))
            {
                return "erro: " + L.F("pasta desconhecida ({0})", string.Join(", ", CouncilBank.Portfolios));
            }
            IaNation nation = world.Ensure(module.PlayerIndex);
            string result = CouncilEngine.Fire(world, nation, capture, portfolio, module.CurrentTurn);
            if (result == null)
            {
                return "erro: " + L.T("ninguém nessa pasta");
            }
            Minister newcomer = nation.Council.FirstOrDefault(m => m.Portfolio == portfolio);
            int era = capture.Empire(module.PlayerIndex)?.EraIndex ?? 0;
            Meeting(world, module.CurrentTurn)?.Notes.Add(result + (newcomer != null ? $" (ficha do novo: {Card(newcomer, era, capture)})" : string.Empty));
            IaModule.Runtime.Event("Conselho do jogador: " + result + ".");
            module.MarkDirty();
            return "ok: " + result;
        }

        /// <summary>Convoca de novo a reunião que falhou.</summary>
        internal static string Retry()
        {
            IaModule module = IaModule.Instance;
            CouncilMeeting meeting = Meeting(IaModule.World, module?.CurrentTurn ?? -1);
            if (meeting == null)
            {
                return "erro: " + L.T("sem reunião neste turno");
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

        // ---------------- Chamadas ----------------

        private static void DispatchMeeting(IaWorld world, WorldCapture capture, CouncilMeeting meeting, int player)
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

        private static void DispatchExchange(IaWorld world, WorldCapture capture, CouncilMeeting meeting, int index, int player)
        {
            if (inFlight)
            {
                return; // o Tick manda quando a chamada atual voltar
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

        private static void Launch(IaWorld world, int turn, int player, int exchange, string label, List<ChatMessage> messages)
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
                Reasoning = IaConfig.ReasoningEffort,
                MaxTokens = Math.Max(1500, IaConfig.MaxTokens.Value),
                Temperature = IaConfig.Temperature.Value,
                Timeout = Math.Max(20, IaConfig.TimeoutSeconds.Value),
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

        /// <summary>Thread de trabalho: a chamada, e mais uma se o json vier quebrado.</summary>
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

        /// <summary>Thread principal, a cada frame (IaModule.Update): aplica o que voltou da API.</summary>
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
                    Plugin.Log?.LogError($"[IA] Conselho do jogador: falha ao aplicar a resposta: {ex}");
                    // Sem isto a reunião segue "pensando" e o Tick manda a mesma chamada paga de novo no frame seguinte.
                    retryAt = Time.unscaledTime + 60f;
                }
            }
        }

        private static void Apply(Result result)
        {
            Job job = result.Job;
            IaWorld world = IaModule.World;
            if (world == null || !ReferenceEquals(world, job.World))
            {
                return; // outra partida ou save carregado no meio
            }
            world.TotalCostUsd += result.Cost;
            world.TotalCalls += result.Calls;
            world.TotalPromptTokens += result.Prompt;
            world.TotalCacheHitTokens += result.Hit;
            world.TotalCompletionTokens += result.Completion;
            world.TotalReasoningTokens += result.ReasoningTokens;
            IaLog.WriteCouncil(world.GameGuid, job.Turn, job.Player, job.Label, job.System, job.User, result.Raw, result.Reasoning, result.Error, result.Cost);
            IaModule.Instance?.MarkDirty();
            CouncilMeeting meeting = Meeting(world, job.Turn);
            if (meeting == null)
            {
                return;
            }
            meeting.CostUsd += result.Cost;
            IaNation nation = world.Ensure(job.Player);
            if (job.Exchange < 0)
            {
                ApplyMeeting(meeting, nation, result);
            }
            else if (job.Exchange < meeting.Exchanges.Count)
            {
                ApplyExchange(meeting.Exchanges[job.Exchange], nation, result);
            }
        }

        private static void ApplyMeeting(CouncilMeeting meeting, IaNation nation, Result result)
        {
            if (result.Json == null || result.Error != null)
            {
                meeting.Status = "erro";
                meeting.Error = result.Error ?? "resposta vazia";
                retryAt = Time.unscaledTime + 20f * Math.Max(1, meeting.Attempts);
                Plugin.Log?.LogWarning($"[IA] Conselho do jogador, turno {meeting.Turn}: {meeting.Error}");
                return;
            }
            var speeches = new List<CouncilSpeech>
            {
                new CouncilSpeech { Portfolio = "mao", Text = WithoutIntro(Clean((string)result.Json["mao"]), nation, "mao") },
            };
            var seen = new HashSet<string> { "mao" };
            int max = Math.Max(1, IaConfig.PlayerCouncilSpeakers.Value);
            foreach (JObject item in (result.Json["falas"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string portfolio = Portfolio(Text(item["pasta"]));
                string text = Clean(Text(item["texto"]));
                if (portfolio == null || seen.Contains(portfolio) || string.IsNullOrEmpty(text) || !nation.Council.Any(m => m.Portfolio == portfolio))
                {
                    continue;
                }
                seen.Add(portfolio);
                speeches.Add(new CouncilSpeech { Portfolio = portfolio, Text = WithoutIntro(text, nation, portfolio), Advice = Clean(Text(item["conselho"])) });
                if (speeches.Count > max)
                {
                    break;
                }
            }
            meeting.Speeches = speeches;
            meeting.Raw = result.Raw;
            meeting.Status = "pronta";
            meeting.Error = null;
            meeting.Seen = false;
            AutoOpenPending = true;
            ReadyAt = Time.unscaledTime;
            IaModule.Runtime.Event($"Seu conselho se reuniu no turno {meeting.Turn}: {speeches.Count - 1} ministro(s) falaram (US$ {result.Cost:0.0000}).");
        }

        private static void ApplyExchange(CouncilExchange exchange, IaNation nation, Result result)
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
                string text = Clean(Text(item["texto"]));
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

        /// <summary>
        /// Teste ("ia teste conselho-json"): uma resposta fora do formato (fala como texto solto, pasta e conselho como
        /// lista) passa pelo ApplyMeeting e pelo ApplyExchange de verdade, numa reunião descartável. Antes lançava exceção
        /// e a reunião ficava "pensando", repetindo a chamada paga.
        /// </summary>
        internal static string SelfTestMalformed(IaWorld world, int player)
        {
            IaNation nation = world.Ensure(player);
            string portfolio = nation.Council.FirstOrDefault(m => m.Portfolio != "mao")?.Portfolio ?? "guerra";
            JObject json = JObject.Parse("{\"mao\":\"Teste da Mão.\",\"falas\":[\"fala solta\",42,{\"pasta\":[\"x\"],\"texto\":\"pasta em lista\"},"
                + "{\"pasta\":\"" + portfolio + "\",\"texto\":\"Fala válida.\",\"conselho\":[\"conselho\",\"em lista\"]}]}");
            bool autoOpen = AutoOpenPending;
            float readyAt = ReadyAt;
            var text = new StringBuilder();
            try
            {
                var meeting = new CouncilMeeting { Turn = -1, Status = "pensando" };
                ApplyMeeting(meeting, nation, new Result { Json = json, Raw = json.ToString() });
                text.AppendLine($"reunião: {meeting.Status}; falas aproveitadas: {string.Join(", ", meeting.Speeches.Select(s => s.Portfolio + (s.Advice != null ? " (conselho: " + s.Advice + ")" : string.Empty)))}");
                var exchange = new CouncilExchange { Turn = -1, Status = "pensando" };
                ApplyExchange(exchange, nation, new Result { Json = json, Raw = json.ToString() });
                text.AppendLine($"resposta: {exchange.Status}; réplicas aproveitadas: {exchange.Replies.Count}");
            }
            catch (Exception ex)
            {
                text.AppendLine("FALHOU com exceção: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                AutoOpenPending = autoOpen;
                ReadyAt = readyAt;
            }
            return text.ToString();
        }

        /// <summary>Campo de texto do json, ou null: lista ou objeto no lugar do texto fariam o cast (string) lançar.</summary>
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
        /// Tira a apresentação do começo da fala ("Hamilcar Barca, Marechal. Sem ferro…" → "Sem ferro…"): a tela já
        /// mostra nome e cargo. Só quando a primeira frase é curta (até 5 palavras) e começa pelo nome.
        /// </summary>
        private static string WithoutIntro(string text, IaNation nation, string portfolio)
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

        private static string Clean(string text)
        {
            text = (text ?? string.Empty).Replace("<", "‹").Replace(">", "›").Trim();
            return text.Length > MaxSpeechChars ? text.Substring(0, MaxSpeechChars).TrimEnd() + "…" : text;
        }

        // ---------------- Prompt ----------------

        /// <summary>Dossiê do jogador, fichas dos ministros e o pedido da reunião (o mesmo prefixo nas respostas: cache).</summary>
        private static bool PreparePrompt(IaWorld world, WorldCapture capture, int player, int turn, out string error)
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
                Plugin.Log?.LogError($"[IA] Conselho do jogador: {error}");
                return false;
            }
            IaNation nation = world.Ensure(player);
            system = SystemPrompt(me);
            user = CleanDossier(build.Text) + "\n\n" + Roster(nation, capture, me) + "\n" + MeetingRequest(Math.Max(1, IaConfig.PlayerCouncilSpeakers.Value));
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
            text.AppendLine(IaConfig.WritingCode == "pt"
                ? "- Cada ministro tem nome, pasta, traços e voz (fichas no fim do dossiê). Fale como ele falaria, no tom da era, em português do Brasil. Nada de jargão técnico (\"IA\", \"json\", \"token\")."
                : $"- Cada ministro tem nome, pasta, traços e voz (fichas no fim do dossiê). Fale como ele falaria, no tom da era. Nada de jargão técnico (\"IA\", \"json\", \"token\").\n- IDIOMA: o rei fala {L.EnglishName(IaConfig.WritingCode)}. Escreva todas as falas e textos livres em {L.EnglishName(IaConfig.WritingCode)}, mesmo com o dossiê em português; nomes do jogo como estão no dossiê; chaves do json e códigos exatamente como especificados.");
            text.AppendLine("- O apreço de cada um pelo líder vai de -100 a 100: baixo deixa a fala fria, seca ou ressentida; alto, calorosa e franca. A lealdade (0 a 100) mede o quanto ele aceita ser contrariado.");
            text.AppendLine("- Ministros podem discordar entre si e do líder; a Mão media e resume. Ninguém trai, ameaça o líder ou vaza segredos.");
            text.AppendLine("- A tela já mostra o nome e o cargo de quem fala: não comece a fala se apresentando.");
            text.AppendLine("- As fichas trazem os números de cada pasta e a tendência de cada ministro: use como base, sem repetir a ficha.");
            text.AppendLine();
            text.AppendLine("Ancoragem (obrigatória):");
            text.AppendLine("- Só fatos do dossiê: números, nações, líderes, cidades, territórios, exércitos, cartas e exigências que aparecem lá. Nada inventado: sem enviados, boatos, recursos ou acontecimentos que não estão no dossiê. Se a pasta não tem nada a dizer, o ministro fica calado.");
            text.AppendLine("- \"Você\" no dossiê é o líder. O dossiê foi escrito para outro sistema: nomes de ferramentas (como responder_exigencias, ordem_exercito ou definir_foco) não existem para o conselho. Os ministros falam do que o líder faz na tela do jogo: aceitar ou recusar exigências, propor um acordo, declarar guerra ou propor paz, mover tropas, mudar a produção de uma cidade, escrever uma carta, patrocinar um povo independente.");
            text.AppendLine("- Cada conselho é uma ação concreta que o líder pode fazer agora ou nos próximos turnos.");
            text.AppendLine("- Os códigos de nação, cidade, exército e território (E3, C14, A7, T31) ajudam o líder a achar as coisas: use com o nome, como \"Ḫattuša (T106)\". "
                + "Os códigos de queixa (G1, G2...) e de povo independente (M4) não aparecem na tela do jogo: descreva a queixa (\"a queixa pelo ataque sem provocação\") ou diga o nome do povo.");
            text.AppendLine();
            text.AppendLine("Pense pouco: o que mudou, o que mais pesa, quem fala. Não releia nem resuma o dossiê no raciocínio; depois escreva o json.");
            return text.ToString();
        }

        private static string MeetingRequest(int speakers)
        {
            return "== A REUNIÃO DESTE TURNO ==\n"
                + $"Escreva a reunião. A Mão abre em 2 a 4 frases: o que mais pesa hoje e o que vence logo. Depois falam até {speakers} ministros, os de pasta mais urgente hoje; "
                + "quem não tem nada relevante fica calado. Cada fala tem 1 a 3 frases, até 60 palavras, na voz do ministro, e um conselho concreto em uma linha.\n"
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
            text.AppendLine("Reajam. Falam de 1 a 4 ministros: os que a resposta toca, os que discordam e quem o líder chamou pelo nome ou pela pasta. "
                + "Cada um em 1 a 3 frases, até 60 palavras, na própria voz: podem concordar, insistir, pedir detalhe ou se ofender, sempre com os fatos do dossiê. "
                + "A Mão (pasta \"mao\") pode fechar: com o que o líder decidiu, se ele decidiu; se ele só perguntou, com as opções que ficaram na mesa. Ninguém decide pelo líder.");
            text.AppendLine("Diga também como o apreço de cada um pelo líder mudou com essa resposta, de -8 a 8 (só quem mudou).");
            text.Append("Responda só com json: {\"falas\": [{\"pasta\": \"guerra\", \"texto\": \"...\"}], \"apreco\": {\"guerra\": 2}}");
            return text.ToString();
        }

        /// <summary>O dossiê sem o pedido de decisão da IA e sem a seção do conselho (as fichas vêm completas depois).</summary>
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

        private static string Roster(IaNation nation, WorldCapture capture, CapturedEmpire me)
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
            return text.ToString();
        }

        /// <summary>Ficha curta de um ministro (título, nome, traços, voz e números).</summary>
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

        /// <summary>Etiqueta de conselho das regras em português ("paz:E3" → "paz com os Persas (E3)").</summary>
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

        // ---------------- Estado para a tela e os comandos ----------------

        internal static string Describe(IaWorld world, int turn)
        {
            if (!IaConfig.PlayerCouncil.Value)
            {
                return "conselho do jogador: desligado ([IA] ConselhoDoJogador)";
            }
            CouncilMeeting meeting = Meeting(world, turn);
            if (meeting == null)
            {
                return "conselho do jogador: sem reunião neste turno";
            }
            string state = meeting.Status == "pronta" ? $"pronta, {meeting.Speeches.Count - 1} fala(s)" : meeting.Status == "erro" ? "erro: " + meeting.Error : "reunindo";
            return $"conselho do jogador: reunião do turno {meeting.Turn} {state} · {meeting.Exchanges.Count} resposta(s) sua(s) · US$ {meeting.CostUsd:0.0000}"
                + (meeting.Seen ? " · vista" : string.Empty) + (inFlight ? " · chamada em curso" : string.Empty);
        }
    }
}
