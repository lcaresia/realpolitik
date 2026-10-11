using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CurrencyMod.Diplomacy.Llm.Providers;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacy.Llm
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
        /// <summary>Filled by the router with the model of the provider in use.</summary>
        public string Model;
        public List<ChatMessage> Messages = new List<ChatMessage>();
        public int MaxTokens = 8000;
        public float Temperature = 1f;
        /// <summary>low/high/max, or null to turn reasoning off.</summary>
        public string ReasoningEffort;
        public bool JsonMode = true;
    }

    /// <summary>Error type, so the screen can explain it and the router can decide.</summary>
    internal enum ErrorKind
    {
        None,
        /// <summary>Wrong key, expired token or no permission (401/403).</summary>
        Auth,
        /// <summary>No credit or subscription quota exhausted (402, 429 insufficient_quota...).</summary>
        Credit,
        /// <summary>Calls-per-minute limit (429).</summary>
        Rate,
        /// <summary>Request rejected (400, nonexistent model, unsupported parameter).</summary>
        Request,
        /// <summary>Provider server having trouble (5xx).</summary>
        Server,
        /// <summary>No internet, timeout, connection dropped.</summary>
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
        /// <summary>Cost reported by the provider itself (OpenRouter), instead of the table.</summary>
        public double? ReportedCostUsd;
        public double Seconds;
    }

    /// <summary>
    /// HTTP transport for the providers: chat/completions (OpenAI format, with each one's variations) and the API
    /// through the queue (ProviderRouter); Codex goes through CodexCli. Synchronous on purpose: it runs on the AI worker
    /// threads, never on Unity's main thread. The key only goes into the Authorization header.
    /// </summary>
    internal static class LlmClient
    {
        private static readonly HashSet<string> ConfiguredHosts = new HashSet<string>();

        internal static ChatResult Send(ChatRequest request, ProviderDef provider, string bearer, int timeoutSeconds, int transportRetries = 2)
        {
            ChatResult result = null;
            if (provider.Local)
            {
                // Connection refused = server down: retrying only delays the queue. A slow model's timeout is not retried either.
                transportRetries = 0;
            }
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

        // ---------------- Request body ----------------

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
                // The effort goes at the top level of the body. Inside "thinking" the API ignores it and uses "high" (proven
                // on the bench on 2026-10-06: nested ~9.1k reasoning tokens, a real "low" ~5.1k).
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
                // Without the field, OpenAI thinks at "medium" and Gemini 3 at "high". Gemini 2.5 Flash-Lite comes without
                // reasoning: sending the level there would turn reasoning on and make it more expensive.
                body["reasoning_effort"] = effort == "max" ? "high" : effort;
            }
            // OpenAI reasoning models reject a temperature other than 1; for the others, only send it if it changed.
            if (effort == null && !provider.OpenAiStyle && (provider.DeepSeekThinking || Math.Abs(request.Temperature - 1f) > 0.001f))
            {
                body["temperature"] = request.Temperature;
            }
            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        // ---------------- Sending ----------------

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
                    // While the request waits in the queue, DeepSeek sends empty lines to keep the connection alive.
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
                // App identification on OpenRouter (optional; shows up on the player's dashboard).
                http.Headers["X-OpenRouter-Title"] = "Realpolitik: Living Nations";
                http.Headers["X-Title"] = "Realpolitik: Living Nations";
            }
            http.UserAgent = "HumankindAIDiplomacy/1.0";
            http.Timeout = timeoutSeconds * 1000;
            http.ReadWriteTimeout = timeoutSeconds * 1000;
            http.ServicePoint.Expect100Continue = false;
            return http;
        }

        // ---------------- Responses ----------------

        private static void ParseChat(string text, ChatResult result)
        {
            JObject root = JObject.Parse(text);
            if (root["error"] is JObject error)
            {
                // OpenRouter sometimes returns HTTP 200 with the error in the body.
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
        /// Keeps only the JSON object: strips reasoning blocks (&lt;think&gt;), code fences and surrounding text. Models
        /// without json_object (xAI, Codex) sometimes write a sentence before the JSON.
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

        /// <summary>Reads the provider error and decides its type. The body may contain the message, never the key.</summary>
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
        /// Strips key fragments from error messages: OpenAI, for example, returns "sk-proj-abc****wxyz". The error goes
        /// to F10, the logs and the screen, and the key (not even part of it) must never go there.
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
                    // .NET Framework opens only 2 connections per host by default; the nations think in parallel.
                    ServicePointManager.FindServicePoint(uri).ConnectionLimit = 16;
                }
            }
        }
    }

    /// <summary>Cost in dollars from the provider/model table, with the off-peak discount (DeepSeek).</summary>
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
            double factor = provider.OffPeak && !IsPeak(utc) ? AiConfig.OffPeakFactor.Value : 1.0;
            return perMillion / 1_000_000.0 * factor;
        }

        /// <summary>Estimated cost of a turn with 16 empires (15 nations + the council), from the spending measured on DeepSeek.</summary>
        internal static double TurnEstimate(ProviderDef provider, string model)
        {
            ModelDef price = ProviderCatalog.Price(provider, model);
            if (price == null)
            {
                return -1;
            }
            // Measured on the bench (estudo-custo-ia.md §0, 2026-10-06), after fixing reasoning_effort, the "think
            // briefly" instruction and the trimmed dossier: per decision ~18k input tokens with 16 empires (≈ 35% cached)
            // and ~3.8k generated (~3k of reasoning); ~17 calls per turn (15 nations, the council and a few retries) →
            // ~US$ 0.12 at DeepSeek peak (half off-peak). It used to be ~US$ 0.22. Models without reasoning generate far
            // less: for them the estimate is a ceiling.
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
            string spec = AiConfig.PeakHoursUtc?.Value ?? string.Empty;
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
