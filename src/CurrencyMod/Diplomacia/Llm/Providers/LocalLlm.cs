using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace CurrencyMod.Diplomacia.Llm.Providers
{
    /// <summary>
    /// "Local model" provider: any server with an OpenAI chat/completions API running on this PC or on the home network
    /// (Ollama, LM Studio, llama.cpp, vLLM...). No key, no cost, and the dossier never leaves the machine. The address
    /// comes from [IA.Provedores] EnderecoLocal; empty = probe the default ports (Ollama 11434, LM Studio 1234, llama.cpp 8080).
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

        /// <summary>Result of the last probe: true = server found, false = not found, null = not probed yet.</summary>
        internal static bool? Reachable { get; private set; }

        /// <summary>Base address (ends in /v1), from the .cfg or the last server found; null if there is none.</summary>
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

        /// <summary>Normalizes "localhost:1234", "http://x:8080" or "http://x/v1/chat/completions" to ".../v1".</summary>
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

        /// <summary>Points the provider's ChatUrl/ModelsUrl at the current address (call before each use).</summary>
        internal static void Apply(ProviderDef provider)
        {
            string bas = Base;
            provider.ChatUrl = bas + "/chat/completions";
            provider.ModelsUrl = bas + "/models";
            provider.Host = new Uri(bas).Host;
        }

        /// <summary>
        /// Probes for the server (if the .cfg did not pin one) and caches the model list. Runs on a worker thread and at
        /// most once every 20 s, so the screen can call it freely.
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
                    Plugin.Log.LogInfo($"[IA] Local model probe failed: {ex.GetType().Name}");
                    Reachable = false;
                }
                finally
                {
                    probing = false;
                }
            });
        }

        /// <summary>Model to use when the .cfg did not pick one: the first one the server lists (or null).</summary>
        internal static string FirstModel(ProviderDef provider)
        {
            return ProviderTester.Models(provider).FirstOrDefault();
        }
    }
}
