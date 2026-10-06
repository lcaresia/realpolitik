using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using CurrencyMod.Diplomacia.Llm.Providers;
using Newtonsoft.Json.Linq;

namespace IaBench
{
    /// <summary>Uma chamada ao DeepSeek, com o corpo montado pela bancada (para testar variações do corpo do mod).</summary>
    internal sealed class Call
    {
        public string System;
        public List<(string role, string content)> Turns = new List<(string, string)>();
        public int MaxTokens = 12000;
        /// <summary>"nested" (como o mod faz hoje), "top" (como a documentação pede) ou "off".</summary>
        public string EffortPlacement = "top";
        public string Effort = "low";
        public string Model = "deepseek-flash";
    }

    internal sealed class CallResult
    {
        public bool Ok;
        public string Error;
        public string Content;
        public string Reasoning;
        public string Finish;
        public int Prompt, Hit, Miss, Completion, ReasoningTokens;
        public double Cost;
        public double Seconds;

        public JObject ToJson() => new JObject
        {
            ["ok"] = Ok, ["erro"] = Error, ["fim"] = Finish, ["entrada"] = Prompt, ["cache"] = Hit, ["semCache"] = Miss,
            ["saida"] = Completion, ["raciocinio"] = ReasoningTokens, ["custo"] = Cost, ["segundos"] = Math.Round(Seconds, 1),
        };
    }

    internal static class Client
    {
        private const string Url = "https://api.deepseek.com/chat/completions";
        private const double Hit = 0.006, Miss = 0.30, Out = 1.20;

        internal static string BuildBody(Call call)
        {
            var messages = new JArray { new JObject { ["role"] = "system", ["content"] = call.System } };
            foreach (var (role, content) in call.Turns)
            {
                messages.Add(new JObject { ["role"] = role, ["content"] = content });
            }
            var body = new JObject
            {
                ["model"] = call.Model,
                ["messages"] = messages,
                ["stream"] = false,
                ["max_tokens"] = call.MaxTokens,
                ["response_format"] = new JObject { ["type"] = "json_object" },
            };
            switch (call.EffortPlacement)
            {
                case "nested":
                    body["thinking"] = new JObject { ["type"] = "enabled", ["reasoning_effort"] = call.Effort };
                    break;
                case "top":
                    body["thinking"] = new JObject { ["type"] = "enabled" };
                    body["reasoning_effort"] = call.Effort;
                    break;
                default:
                    body["thinking"] = new JObject { ["type"] = "disabled" };
                    body["temperature"] = 1.0;
                    break;
            }
            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        internal static CallResult Send(Call call)
        {
            var result = new CallResult();
            var watch = Stopwatch.StartNew();
            string bearer = Credentials.Get("deepseek")?.Bearer;
            if (string.IsNullOrEmpty(bearer))
            {
                result.Error = "sem credencial do DeepSeek";
                return result;
            }
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = 16;
                byte[] payload = Encoding.UTF8.GetBytes(BuildBody(call));
                var http = (HttpWebRequest)WebRequest.Create(Url);
                http.Method = "POST";
                http.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
                http.ContentType = "application/json; charset=utf-8";
                http.Timeout = http.ReadWriteTimeout = 300_000;
                http.ServicePoint.Expect100Continue = false;
                using (Stream stream = http.GetRequestStream())
                {
                    stream.Write(payload, 0, payload.Length);
                }
                using (var response = (HttpWebResponse)http.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    JObject root = JObject.Parse(reader.ReadToEnd().Trim());
                    var choice = (JObject)root["choices"][0];
                    result.Content = (string)choice["message"]["content"];
                    result.Reasoning = (string)choice["message"]["reasoning_content"];
                    result.Finish = (string)choice["finish_reason"];
                    JObject usage = (JObject)root["usage"];
                    result.Prompt = (int?)usage["prompt_tokens"] ?? 0;
                    result.Completion = (int?)usage["completion_tokens"] ?? 0;
                    result.Hit = (int?)usage["prompt_cache_hit_tokens"] ?? 0;
                    result.Miss = (int?)usage["prompt_cache_miss_tokens"] ?? result.Prompt - result.Hit;
                    result.ReasoningTokens = (int?)usage["completion_tokens_details"]?["reasoning_tokens"] ?? 0;
                    result.Ok = true;
                }
            }
            catch (WebException ex)
            {
                string body = string.Empty;
                try
                {
                    using (var reader = new StreamReader(ex.Response.GetResponseStream()))
                    {
                        body = reader.ReadToEnd();
                    }
                }
                catch (Exception)
                {
                }
                result.Error = $"{ex.Status} {(int?)(ex.Response as HttpWebResponse)?.StatusCode}: {(body.Length > 300 ? body.Substring(0, 300) : body)}";
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().Name + ": " + ex.Message;
            }
            result.Seconds = watch.Elapsed.TotalSeconds;
            if (result.Ok)
            {
                double factor = IsPeak(DateTime.UtcNow) ? 1.0 : 0.5;
                result.Cost = (result.Hit * Hit + result.Miss * Miss + result.Completion * Out) / 1_000_000.0 * factor;
            }
            Scoreboard.Add(result.Cost);
            return result;
        }

        /// <summary>Pico do DeepSeek: 01–04 e 06–10 UTC, de segunda a sexta (o mesmo padrão do .cfg).</summary>
        internal static bool IsPeak(DateTime utc)
        {
            if (utc.DayOfWeek == DayOfWeek.Saturday || utc.DayOfWeek == DayOfWeek.Sunday)
            {
                return false;
            }
            return (utc.Hour >= 1 && utc.Hour < 4) || (utc.Hour >= 6 && utc.Hour < 10);
        }
    }

    /// <summary>Placar do gasto da bancada (placar.json): toda chamada soma, inclusive as que falham.</summary>
    internal static class Scoreboard
    {
        private static readonly object Gate = new object();
        private static string FilePath => Path.Combine(Bench.Root, "placar.json");

        internal static void Add(double cost)
        {
            lock (Gate)
            {
                JObject board = Read();
                board["gastoUSD"] = (double)board["gastoUSD"] + cost;
                board["chamadas"] = (int)board["chamadas"] + 1;
                board["atualizado"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.WriteAllText(FilePath, board.ToString());
            }
        }

        internal static JObject Read()
        {
            return File.Exists(FilePath)
                ? JObject.Parse(File.ReadAllText(FilePath))
                : new JObject { ["gastoUSD"] = 0.0, ["chamadas"] = 0 };
        }
    }
}
