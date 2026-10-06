using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CurrencyMod.Diplomacia.Llm.Providers;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia.Llm
{
    internal sealed class ChatMessage
    {
        public string Role;
        public string Content;

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    internal sealed class ChatRequest
    {
        /// <summary>Preenchido pelo roteador com o modelo do provedor em uso.</summary>
        public string Model;
        public List<ChatMessage> Messages = new List<ChatMessage>();
        public int MaxTokens = 8000;
        public float Temperature = 1f;
        /// <summary>low/high/max, ou null para desligar o raciocínio.</summary>
        public string ReasoningEffort;
        public bool JsonMode = true;
    }

    /// <summary>Tipo do erro, para a tela explicar e o roteador decidir.</summary>
    internal enum ErrorKind
    {
        None,
        /// <summary>Chave errada, token vencido ou sem permissão (401/403).</summary>
        Auth,
        /// <summary>Sem crédito ou cota da assinatura esgotada (402, 429 insufficient_quota...).</summary>
        Credit,
        /// <summary>Limite de chamadas por minuto (429).</summary>
        Rate,
        /// <summary>Pedido recusado (400, modelo inexistente, parâmetro não aceito).</summary>
        Request,
        /// <summary>Servidor do provedor com problema (5xx).</summary>
        Server,
        /// <summary>Sem internet, tempo esgotado, conexão caiu.</summary>
        Network,
        Other,
    }

    internal sealed class ChatResult
    {
        public bool Ok;
        public string Error;
        public ErrorKind Kind;
        public int HttpStatus;
        public bool Transient;
        public string ProviderId;
        public string Model;
        public string Content;
        public string ReasoningContent;
        public string FinishReason;
        public int PromptTokens;
        public int CacheHitTokens;
        public int CacheMissTokens;
        public int CompletionTokens;
        public int ReasoningTokens;
        public double CostUsd;
        /// <summary>Custo informado pelo próprio provedor (OpenRouter), em vez da tabela.</summary>
        public double? ReportedCostUsd;
        public double Seconds;
    }

    /// <summary>
    /// Transporte HTTP para os provedores: chat/completions (formato da OpenAI, com as variações de cada um) e a API
    /// pela fila (ProviderRouter); o Codex vai por CodexCli. Síncrono de propósito: roda nas threads de trabalho da IA, nunca na thread principal
    /// da Unity. A chave só vai para o cabeçalho Authorization.
    /// </summary>
    internal static class LlmClient
    {
        private static readonly HashSet<string> ConfiguredHosts = new HashSet<string>();

        internal static ChatResult Send(ChatRequest request, ProviderDef provider, string bearer, int timeoutSeconds, int transportRetries = 2)
        {
            ChatResult result = null;
            for (int attempt = 0; attempt <= transportRetries; attempt++)
            {
                result = SendOnce(request, provider, bearer, timeoutSeconds);
                if (result.Ok || !result.Transient)
                {
                    return result;
                }
                Thread.Sleep(2000 * (attempt + 1));
            }
            return result;
        }

        // ---------------- Corpo do pedido ----------------

        internal static string BuildBody(ChatRequest request, ProviderDef provider)
        {

            var messages = new JArray();
            foreach (ChatMessage message in request.Messages)
            {
                messages.Add(new JObject { ["role"] = message.Role, ["content"] = message.Content });
            }
            var body = new JObject
            {
                ["model"] = request.Model,
                ["messages"] = messages,
                ["stream"] = false,
            };
            body[provider.OpenAiStyle ? "max_completion_tokens" : "max_tokens"] = request.MaxTokens;
            if (request.JsonMode && provider.JsonObject)
            {
                body["response_format"] = new JObject { ["type"] = "json_object" };
            }
            string effort = request.ReasoningEffort;
            if (provider.DeepSeekThinking)
            {
                // O esforço vai no nível de cima do corpo. Dentro de "thinking" a API ignora e usa "high" (provado na
                // bancada em 2026-10-06: aninhado ~9,1 mil tokens de raciocínio, "low" de verdade ~5,1 mil).
                body["thinking"] = new JObject { ["type"] = effort == null ? "disabled" : "enabled" };
                if (effort != null)
                {
                    body["reasoning_effort"] = effort;
                }
            }
            else if (provider.GlmThinking)
            {
                body["thinking"] = new JObject { ["type"] = effort == null ? "disabled" : "enabled" };
            }
            else if (provider.OpenRouterReasoning)
            {
                body["reasoning"] = effort == null
                    ? new JObject { ["enabled"] = false }
                    : new JObject { ["effort"] = effort == "max" ? "high" : effort };
            }
            else if (effort != null && (provider.OpenAiStyle
                || (provider.Id == "gemini" && request.Model != null && !request.Model.Contains("2.5"))))
            {
                // Sem o campo, a OpenAI pensa em "medium" e o Gemini 3 em "high". O Gemini 2.5 Flash-Lite já vem sem
                // raciocínio: mandar o nível lá ligaria o raciocínio e encareceria.
                body["reasoning_effort"] = effort == "max" ? "high" : effort;
            }
            // Modelos de raciocínio da OpenAI recusam temperatura diferente de 1; nos outros, só manda se mudou.
            if (effort == null && !provider.OpenAiStyle && (provider.DeepSeekThinking || Math.Abs(request.Temperature - 1f) > 0.001f))
            {
                body["temperature"] = request.Temperature;
            }
            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        // ---------------- Envio ----------------

        private static ChatResult SendOnce(ChatRequest request, ProviderDef provider, string bearer, int timeoutSeconds)
        {
            var result = new ChatResult { ProviderId = provider.Id, Model = request.Model };
            var watch = Stopwatch.StartNew();
            try
            {
                EnsureTransport(provider.ChatUrl);
                byte[] payload = Encoding.UTF8.GetBytes(BuildBody(request, provider));

                HttpWebRequest http = CreateRequest(provider, provider.ChatUrl, "POST", bearer, timeoutSeconds);
                http.ContentType = "application/json; charset=utf-8";
                http.Accept = "application/json";
                http.ContentLength = payload.Length;
                using (Stream stream = http.GetRequestStream())
                {
                    stream.Write(payload, 0, payload.Length);
                }

                using (var response = (HttpWebResponse)http.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    result.HttpStatus = (int)response.StatusCode;
                    // Enquanto a requisição espera na fila, o DeepSeek manda linhas vazias para manter a conexão.
                    ParseChat(reader.ReadToEnd().Trim(), result);
                }
                if (result.Ok && request.JsonMode)
                {
                    result.Content = ExtractJson(result.Content);
                }
            }
            catch (WebException ex)
            {
                result.Ok = false;
                if (ex.Response is HttpWebResponse response)
                {
                    result.HttpStatus = (int)response.StatusCode;
                    string body = string.Empty;
                    try
                    {
                        using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        {
                            body = reader.ReadToEnd();
                        }
                    }
                    catch (Exception)
                    {
                    }
                    Classify(result, result.HttpStatus, body);
                }
                else
                {
                    result.Error = $"{ex.Status}: {ex.Message}";
                    result.Kind = ErrorKind.Network;
                    result.Transient = ex.Status == WebExceptionStatus.Timeout || ex.Status == WebExceptionStatus.ConnectFailure
                        || ex.Status == WebExceptionStatus.ConnectionClosed || ex.Status == WebExceptionStatus.ReceiveFailure
                        || ex.Status == WebExceptionStatus.SendFailure || ex.Status == WebExceptionStatus.KeepAliveFailure;
                }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Kind = ErrorKind.Other;
                result.Error = ex.GetType().Name + ": " + ex.Message;
            }
            result.Seconds = watch.Elapsed.TotalSeconds;
            if (result.Ok)
            {
                result.CostUsd = result.ReportedCostUsd ?? Pricing.Cost(provider, request.Model, result.CacheHitTokens, result.CacheMissTokens,
                    result.CompletionTokens, DateTime.UtcNow);
            }
            return result;
        }

        internal static HttpWebRequest CreateRequest(ProviderDef provider, string url, string method, string bearer, int timeoutSeconds)
        {
            EnsureTransport(url);
            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = method;
            if (!string.IsNullOrEmpty(bearer))
            {
                http.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
            }
            if (provider?.Id == "openrouter")
            {
                // Identificação do app no OpenRouter (opcional; aparece no painel do jogador).
                http.Headers["X-OpenRouter-Title"] = "Humankind AI Diplomacy";
                http.Headers["X-Title"] = "Humankind AI Diplomacy";
            }
            http.UserAgent = "HumankindAIDiplomacy/1.0";
            http.Timeout = timeoutSeconds * 1000;
            http.ReadWriteTimeout = timeoutSeconds * 1000;
            http.ServicePoint.Expect100Continue = false;
            return http;
        }

        // ---------------- Respostas ----------------

        private static void ParseChat(string text, ChatResult result)
        {
            JObject root = JObject.Parse(text);
            if (root["error"] is JObject error)
            {
                // O OpenRouter às vezes devolve HTTP 200 com o erro no corpo.
                Classify(result, (int?)error["code"] ?? 500, text);
                return;
            }
            var choice = root["choices"]?[0] as JObject;
            JObject message = choice?["message"] as JObject;
            if (message == null)
            {
                result.Ok = false;
                result.Kind = ErrorKind.Other;
                result.Error = "resposta sem choices[0].message";
                return;
            }
            result.Content = (string)message["content"];
            result.ReasoningContent = (string)message["reasoning_content"] ?? (string)message["reasoning"];
            result.FinishReason = (string)choice["finish_reason"];

            if (root["usage"] is JObject usage)
            {
                result.PromptTokens = (int?)usage["prompt_tokens"] ?? 0;
                result.CompletionTokens = (int?)usage["completion_tokens"] ?? 0;
                result.CacheHitTokens = (int?)usage["prompt_cache_hit_tokens"] ?? (int?)usage["prompt_tokens_details"]?["cached_tokens"] ?? 0;
                result.CacheMissTokens = (int?)usage["prompt_cache_miss_tokens"] ?? Math.Max(0, result.PromptTokens - result.CacheHitTokens);
                result.ReasoningTokens = (int?)usage["completion_tokens_details"]?["reasoning_tokens"] ?? 0;
                result.ReportedCostUsd = (double?)usage["cost"];
            }
            result.Ok = true;
        }

        /// <summary>
        /// Deixa só o objeto json: tira blocos de raciocínio (&lt;think&gt;), cercas de código e texto em volta. Os modelos
        /// sem json_object (xAI, Codex) às vezes escrevem uma frase antes do json.
        /// </summary>
        internal static string ExtractJson(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }
            string text = Regex.Replace(content, "<think>.*?</think>", string.Empty, RegexOptions.Singleline).Trim();
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text.Substring(start, end - start + 1) : text;
        }

        /// <summary>Lê o erro do provedor e decide o tipo. O corpo pode ter a mensagem, nunca a chave.</summary>
        internal static void Classify(ChatResult result, int status, string body)
        {
            result.Ok = false;
            result.HttpStatus = status;
            string message = ErrorMessage(body, out string code);
            string lower = (code + " " + message).ToLowerInvariant();
            if (status == 402 || lower.Contains("insufficient_quota") || lower.Contains("insufficient balance")
                || lower.Contains("usage_limit") || lower.Contains("billing") || code == "1113" || lower.Contains("credit"))
            {
                result.Kind = ErrorKind.Credit;
            }
            else if (status == 401 || status == 403 || lower.Contains("api key") || lower.Contains("invalid_api_key"))
            {
                result.Kind = ErrorKind.Auth;
            }
            else if (status == 429)
            {
                result.Kind = ErrorKind.Rate;
            }
            else if (status >= 500)
            {
                result.Kind = ErrorKind.Server;
            }
            else if (status >= 400)
            {
                result.Kind = ErrorKind.Request;
            }
            else
            {
                result.Kind = ErrorKind.Other;
            }
            result.Transient = result.Kind == ErrorKind.Rate || result.Kind == ErrorKind.Server;
            result.Error = $"HTTP {status}: {Redact(message)}";
        }

        /// <summary>
        /// Tira pedaços de chave das mensagens de erro: a OpenAI, por exemplo, devolve "sk-proj-abc****wxyz". O erro vai
        /// para o F10, os logs e a tela, e chave (nem pedaço dela) não pode ir.
        /// </summary>
        internal static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            text = Regex.Replace(text, @"\b(sk|rk|pk|xai|gsk|AIza)[-_A-Za-z0-9\*\.]{4,}", "[chave]");
            return Regex.Replace(text, @"[A-Za-z0-9_\-]*\*{3,}[A-Za-z0-9_\-]*", "[chave]");
        }

        private static string ErrorMessage(string body, out string code)
        {
            code = string.Empty;
            if (string.IsNullOrEmpty(body))
            {
                return "(sem corpo)";
            }
            try
            {
                JToken root = JToken.Parse(body.Trim());
                if (root is JArray array && array.Count > 0)
                {
                    root = array[0];
                }
                JToken error = root["error"] ?? root;
                code = (string)error["code"] ?? (string)error["type"] ?? (string)error["status"] ?? string.Empty;
                string message = (string)error["message"] ?? (string)root["message"] ?? (string)root["msg"];
                if (!string.IsNullOrEmpty(message))
                {
                    return message.Length > 300 ? message.Substring(0, 300) : message;
                }
            }
            catch (Exception)
            {
            }
            return body.Length > 300 ? body.Substring(0, 300) : body;
        }

        private static void EnsureTransport(string endpoint)
        {
            if ((ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls12) == 0)
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            var uri = new Uri(endpoint);
            lock (ConfiguredHosts)
            {
                if (ConfiguredHosts.Add(uri.Host))
                {
                    // O .NET Framework abre só 2 conexões por host por padrão; as nações pensam em paralelo.
                    ServicePointManager.FindServicePoint(uri).ConnectionLimit = 16;
                }
            }
        }
    }

    /// <summary>Custo em dólares pela tabela do provedor/modelo, com desconto fora do horário de pico (DeepSeek).</summary>
    internal static class Pricing
    {
        internal static double Cost(ProviderDef provider, string model, int cacheHit, int cacheMiss, int output, DateTime utc)
        {
            ModelDef price = ProviderCatalog.Price(provider, model);
            if (price == null)
            {
                return 0;
            }
            double perMillion = cacheHit * price.Hit + cacheMiss * price.Miss + output * price.Out;
            double factor = provider.OffPeak && !IsPeak(utc) ? IaConfig.OffPeakFactor.Value : 1.0;
            return perMillion / 1_000_000.0 * factor;
        }

        /// <summary>Custo estimado de um turno com 16 impérios (15 nações + o conselho), pelo gasto medido no DeepSeek.</summary>
        internal static double TurnEstimate(ProviderDef provider, string model)
        {
            ModelDef price = ProviderCatalog.Price(provider, model);
            if (price == null)
            {
                return -1;
            }
            // Medido na bancada (estudo-custo-ia.md §0, 2026-10-06), depois da correção do reasoning_effort, do "pense
            // pouco" e do dossiê enxuto: por decisão ~18 mil tokens de entrada com 16 impérios (≈ 35% do cache) e ~3,8
            // mil gerados (~3 mil de raciocínio); ~17 chamadas por turno (15 nações, o conselho e poucas repetições) →
            // ~US$ 0,12 no pico do DeepSeek (metade fora dele). Antes eram ~US$ 0,22. Modelos sem raciocínio geram bem
            // menos: para eles a estimativa é um teto.
            const double calls = 17, input = 18000, hitShare = 0.35, output = 3800;
            double perCall = input * hitShare * price.Hit + input * (1 - hitShare) * price.Miss + output * price.Out;
            return calls * perCall / 1_000_000.0;
        }

        internal static bool IsPeak(DateTime utc)
        {
            if (utc.DayOfWeek == DayOfWeek.Saturday || utc.DayOfWeek == DayOfWeek.Sunday)
            {
                return false;
            }
            string spec = IaConfig.PeakHoursUtc?.Value ?? string.Empty;
            foreach (string range in spec.Split(','))
            {
                string[] parts = range.Trim().Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out int from) && int.TryParse(parts[1], out int to)
                    && utc.Hour >= from && utc.Hour < to)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
