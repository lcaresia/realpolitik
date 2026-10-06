using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia.Llm.Providers
{
    /// <summary>Resultado do "Testar conexão" para a tela.</summary>
    internal sealed class TestOutcome
    {
        public volatile bool Finished;
        public bool Ok;
        public string Message;
        public double Seconds;
        /// <summary>Crédito que sobra (OpenRouter), ou null.</summary>
        public double? CreditLeftUsd;
    }

    /// <summary>
    /// "Testar conexão": uma chamada mínima (resposta em json de poucos tokens, custo de frações de centavo) e,
    /// quando o provedor tem lista de modelos, a lista. Roda numa thread de trabalho.
    /// </summary>
    internal static class ProviderTester
    {
        private static readonly Dictionary<string, List<string>> FetchedModels = new Dictionary<string, List<string>>();

        internal static TestOutcome Start(ProviderDef provider)
        {
            var outcome = new TestOutcome();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Run(provider, outcome);
                }
                catch (Exception ex)
                {
                    outcome.Ok = false;
                    outcome.Message = ex.GetType().Name + ": " + ex.Message;
                }
                outcome.Finished = true;
            });
            return outcome;
        }

        private static void Run(ProviderDef provider, TestOutcome outcome)
        {
            if (!ProviderRouter.HasAccess(provider))
            {
                outcome.Message = provider.AcceptsKey ? L.T("Cole a chave primeiro.") : L.T("Entre com a conta primeiro.");
                return;
            }
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new ChatMessage("system", "You are a connection test. Reply with JSON only."),
                    new ChatMessage("user", "Reply exactly with this json: {\"ok\": true}"),
                },
                MaxTokens = 1000,
                Temperature = 1f,
                ReasoningEffort = null,
                JsonMode = true,
            };
            ChatResult chat = ProviderRouter.SendWith(provider, request, 60);
            outcome.Seconds = chat.Seconds;
            if (!chat.Ok)
            {
                outcome.Message = Explain(provider, chat);
                return;
            }
            bool json;
            try
            {
                json = (bool?)JObject.Parse(chat.Content ?? string.Empty)["ok"] == true;
            }
            catch (Exception)
            {
                json = false;
            }
            if (provider.Id == "openrouter")
            {
                outcome.CreditLeftUsd = OpenRouterCredit();
            }
            outcome.Ok = json;
            outcome.Message = json
                ? L.F("Conectado: {0} respondeu em {1:0.0} s.", ProviderRouter.ModelFor(provider), chat.Seconds)
                : L.F("Respondeu, mas sem o json pedido. Escolha outro modelo. ({0})", Clip(chat.Content, 60));
            FetchModels(provider);
        }

        /// <summary>Mensagem clara para cada tipo de erro.</summary>
        internal static string Explain(ProviderDef provider, ChatResult chat)
        {
            switch (chat.Kind)
            {
                case ErrorKind.Auth:
                    return !provider.AcceptsKey ? L.T("Login recusado ou vencido. Entre de novo.")
                        : provider.Login != LoginKind.None ? L.T("Chave ou login recusado. Entre de novo ou cole a chave outra vez.")
                        : L.T("Chave recusada. Confira se colou a chave inteira e se ela está ativa no site do provedor.");
                case ErrorKind.Credit:
                    return provider.Subscription
                        ? L.T("A cota da assinatura acabou por agora. Ela volta sozinha; enquanto isso, o próximo provedor da lista assume.")
                        : L.T("Sem crédito na conta. Adicione crédito no site do provedor.");
                case ErrorKind.Rate:
                    return L.T("Limite de chamadas por minuto. Espere um pouco e teste de novo.");
                case ErrorKind.Request:
                    return L.F("O provedor recusou o pedido. O modelo \"{0}\" existe na sua conta? ({1})", ProviderRouter.ModelFor(provider), Clip(chat.Error, 90));
                case ErrorKind.Server:
                    return L.T("O servidor do provedor está com problema. Tente mais tarde.");
                case ErrorKind.Network:
                    return L.T("Sem conexão com o provedor. Confira a internet.");
                default:
                    return Clip(chat.Error, 120);
            }
        }

        /// <summary>Modelos que a conta enxerga (para a lista da tela). Vazio se o provedor não lista.</summary>
        internal static List<string> Models(ProviderDef provider)
        {
            lock (FetchedModels)
            {
                return FetchedModels.TryGetValue(provider.Id, out List<string> list) ? new List<string>(list) : new List<string>();
            }
        }

        /// <summary>Busca a lista de modelos (GET, sem custo). No OpenRouter, também guarda o preço de cada um.</summary>
        internal static void FetchModels(ProviderDef provider)
        {
            if (string.IsNullOrEmpty(provider.ModelsUrl))
            {
                return;
            }
            try
            {
                string bearer = provider.Id == "openrouter" ? null : Credentials.Get(provider.Id)?.Bearer;
                HttpWebRequest http = LlmClient.CreateRequest(provider, provider.ModelsUrl, "GET", bearer, 30);
                http.Accept = "application/json";
                JObject root;
                using (var response = (HttpWebResponse)http.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    root = JObject.Parse(reader.ReadToEnd());
                }
                var ids = new List<string>();
                foreach (JToken item in root["data"] as JArray ?? new JArray())
                {
                    string id = (string)item["id"] ?? (string)item["slug"];
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }
                    if (provider.Id == "openrouter")
                    {
                        // Só modelos que aceitam json e cabem no nosso uso (sem imagem, sem lote).
                        var parameters = item["supported_parameters"] as JArray;
                        if (parameters == null || !parameters.Any(p => (string)p == "response_format") || id.EndsWith(":batch"))
                        {
                            continue;
                        }
                        JToken pricing = item["pricing"];
                        double miss = PerMillion(pricing?["prompt"]);
                        double output = PerMillion(pricing?["completion"]);
                        double hit = pricing?["input_cache_read"] != null ? PerMillion(pricing["input_cache_read"]) : miss;
                        ProviderCatalog.SetLive(provider.Id, new ModelDef { Id = id, Hit = hit, Miss = miss, Out = output });
                    }

                    ids.Add(id);
                }
                ids.Sort(StringComparer.OrdinalIgnoreCase);
                lock (FetchedModels)
                {
                    FetchedModels[provider.Id] = ids;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogInfo($"[IA] Lista de modelos de {provider.Name} indisponível: {ex.GetType().Name}");
            }
        }

        private static double? OpenRouterCredit()
        {
            try
            {
                HttpWebRequest http = LlmClient.CreateRequest(ProviderCatalog.Get("openrouter"), "https://openrouter.ai/api/v1/key", "GET",
                    Credentials.Get("openrouter")?.Bearer, 30);
                using (var response = (HttpWebResponse)http.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    JToken data = JObject.Parse(reader.ReadToEnd())["data"];
                    return (double?)data?["limit_remaining"];
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static double PerMillion(JToken perToken)
        {
            return double.TryParse((string)perToken ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value * 1_000_000.0 : 0;
        }

        private static string Clip(string text, int max)
        {
            text = (text ?? string.Empty).Replace('\n', ' ').Trim();
            return text.Length > max ? text.Substring(0, max) + "…" : text;
        }
    }
}
