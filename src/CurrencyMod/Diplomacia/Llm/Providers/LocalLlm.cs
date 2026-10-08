using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace CurrencyMod.Diplomacia.Llm.Providers
{
    /// <summary>
    /// Provedor "Modelo local": qualquer servidor com API chat/completions da OpenAI rodando neste PC ou na rede de casa
    /// (Ollama, LM Studio, llama.cpp, vLLM...). Sem chave, sem custo, e o dossiê não sai da máquina. O endereço vem de
    /// [IA.Provedores] EnderecoLocal; vazio = procura nas portas padrão (Ollama 11434, LM Studio 1234, llama.cpp 8080).
    /// </summary>
    internal static class LocalLlm
    {
        internal const string Id = "local";

        private static readonly string[] DefaultBases =
        {
            "http://localhost:11434/v1",
            "http://localhost:1234/v1",
            "http://localhost:8080/v1",
        };

        private static readonly object Gate = new object();
        private static string found;
        private static DateTime probedUtc = DateTime.MinValue;
        private static volatile bool probing;

        /// <summary>Resultado da última procura: true = achou servidor, false = não achou, null = ainda não procurou.</summary>
        internal static bool? Reachable { get; private set; }

        /// <summary>Endereço base (termina em /v1), do .cfg ou do último servidor achado; null se não houver.</summary>
        internal static string Base
        {
            get
            {
                string configured = Normalize(IaConfig.LocalUrl?.Value);
                if (configured != null)
                {
                    return configured;
                }
                lock (Gate)
                {
                    return found ?? DefaultBases[0];
                }
            }
        }

        /// <summary>Normaliza "localhost:1234", "http://x:8080" ou "http://x/v1/chat/completions" para ".../v1".</summary>
        internal static string Normalize(string text)
        {
            text = (text ?? string.Empty).Trim().TrimEnd('/');
            if (text.Length == 0)
            {
                return null;
            }
            if (!text.Contains("://"))
            {
                text = "http://" + text;
            }
            foreach (string tail in new[] { "/chat/completions", "/models" })
            {
                if (text.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(0, text.Length - tail.Length).TrimEnd('/');
                }
            }
            if (!text.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                text += "/v1";
            }
            return Uri.TryCreate(text, UriKind.Absolute, out _) ? text : null;
        }

        /// <summary>Aponta ChatUrl/ModelsUrl do provedor para o endereço atual (chamar antes de cada uso).</summary>
        internal static void Apply(ProviderDef provider)
        {
            string bas = Base;
            provider.ChatUrl = bas + "/chat/completions";
            provider.ModelsUrl = bas + "/models";
            provider.Host = new Uri(bas).Host;
        }

        /// <summary>
        /// Procura o servidor (se o .cfg não fixou um) e guarda a lista de modelos. Roda numa thread de trabalho e no
        /// máximo uma vez a cada 20 s, para a tela poder chamar à vontade.
        /// </summary>
        internal static void Probe(ProviderDef provider, bool force = false)
        {
            lock (Gate)
            {
                if (probing || (!force && (DateTime.UtcNow - probedUtc).TotalSeconds < 20))
                {
                    return;
                }
                probing = true;
                probedUtc = DateTime.UtcNow;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string configured = Normalize(IaConfig.LocalUrl?.Value);
                    IEnumerable<string> candidates = configured != null ? new[] { configured } : DefaultBases;
                    bool ok = false;
                    foreach (string candidate in candidates)
                    {
                        lock (Gate)
                        {
                            found = candidate;
                        }
                        Apply(provider);
                        if (ProviderTester.FetchModels(provider, quick: true))
                        {
                            ok = true;
                            break;
                        }
                    }
                    if (!ok && configured == null)
                    {
                        lock (Gate)
                        {
                            found = null;
                        }
                        Apply(provider);
                    }
                    Reachable = ok;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogInfo($"[IA] Procura do modelo local falhou: {ex.GetType().Name}");
                    Reachable = false;
                }
                finally
                {
                    probing = false;
                }
            });
        }

        /// <summary>Modelo a usar quando o .cfg não escolheu um: o primeiro que o servidor lista (ou null).</summary>
        internal static string FirstModel(ProviderDef provider)
        {
            return ProviderTester.Models(provider).FirstOrDefault();
        }
    }
}
