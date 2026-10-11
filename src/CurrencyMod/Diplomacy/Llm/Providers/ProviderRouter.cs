using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace CurrencyMod.Diplomacy.Llm.Providers
{
    /// <summary>State of a provider for the screen and F10 (no key here).</summary>
    internal sealed class ProviderHealth
    {
        public DateTime LastOkUtc;
        public DateTime LastErrorUtc;
        public string LastError;
        public ErrorKind LastKind;
        public DateTime OutUntilUtc;
        public int Calls;
        public double CostUsd;
    }

    /// <summary>
    /// Provider queue ([IA.Provedores] Ordem): the first one is the primary; if it returns any error (after the normal
    /// network retries), it sits out for a few minutes and the next one takes over. If all fail, the call fails and the
    /// game's native AI keeps playing (locks released by AiModule), without stalling the turn.
    /// </summary>
    internal static class ProviderRouter
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ProviderHealth> Health = new Dictionary<string, ProviderHealth>();
        private static string lastUsed;
        private static bool allDownNotified;

        // ---------------- AI disconnected (melhorias-jogando item 19) ----------------
        // When the whole queue fails, the AI becomes "disconnected": nations and council stop calling the API and only a
        // probe call goes out from time to time (2, 4, 8, up to 10 min). The first good response reconnects. The on-screen
        // notice appears once per outage (and not again if the AI drops again within 15 min); the letters button shows a red X.
        private static volatile bool allDown;
        private static DateTime allDownSinceUtc;
        private static DateTime nextProbeUtc;
        private static TimeSpan probeInterval = TimeSpan.FromMinutes(2);
        private static DateTime lastDownPopupUtc = DateTime.MinValue;
        private static string allDownReason;

        /// <summary>All providers in the queue failed on the last attempt and none has responded since.</summary>
        internal static bool AllDown => allDown;

        internal static string AllDownReason => allDownReason;

        internal static DateTime AllDownSinceUtc => allDownSinceUtc;

        /// <summary>Minutes until the next probe call (0 = can probe now).</summary>
        internal static int MinutesToProbe => allDown ? Math.Max(0, (int)Math.Ceiling((nextProbeUtc - DateTime.UtcNow).TotalMinutes)) : 0;

        /// <summary>
        /// Can the API be called now? Connected: always. Disconnected: only one probe call when the deadline expires (whoever
        /// takes the turn holds off the next probe for 3 min, until the response arrives and reschedules it).
        /// </summary>
        internal static bool MayCall()
        {
            if (!allDown)
            {
                return true;
            }
            lock (Gate)
            {
                if (DateTime.UtcNow < nextProbeUtc)
                {
                    return false;
                }
                nextProbeUtc = DateTime.UtcNow.AddMinutes(3);
                return true;
            }
        }

        /// <summary>Notices for the main thread (Runtime.Event, on-screen notice).</summary>
        internal static readonly ConcurrentQueue<string> Notices = new ConcurrentQueue<string>();

        /// <summary>The configured queue, only with known and visible providers. Empty = DeepSeek, if there is a key.</summary>
        internal static List<ProviderDef> Chain()
        {
            var chain = new List<ProviderDef>();
            foreach (string id in (AiConfig.ProviderOrder?.Value ?? string.Empty).Split(',', ';', ' '))
            {
                ProviderDef provider = ProviderCatalog.Get(id);
                if (provider != null && provider.Visible && !chain.Contains(provider))
                {
                    chain.Add(provider);
                }
            }
            if (chain.Count == 0 && string.IsNullOrWhiteSpace(AiConfig.ProviderOrder?.Value) && Credentials.Has("deepseek"))
            {
                chain.Add(ProviderCatalog.Get("deepseek"));
            }
            return chain;
        }

        internal static void SetChain(IEnumerable<string> ids)
        {
            AiConfig.ProviderOrder.Value = string.Join(",", ids.Where(id => ProviderCatalog.Get(id) != null).Distinct());
        }

        /// <summary>Providers in the queue that have a key or login.</summary>
        internal static List<ProviderDef> Ready() => Chain().Where(HasAccess).ToList();

        /// <summary>Has a key or login. Codex stores nothing in the mod: the login done in Codex itself is what counts.</summary>
        internal static bool HasAccess(ProviderDef provider)
        {
            // The local model has no key: if the server is not up, the call fails and the queue moves to the next one.
            return provider.Format == WireFormat.CodexCli ? CodexCli.Ready : provider.Local || Credentials.Has(provider.Id);
        }

        internal static bool AnyReady => Ready().Count > 0;

        internal static string ModelFor(ProviderDef provider)
        {
            string model = AiConfig.ProviderModels.TryGetValue(provider.Id, out var entry) ? entry.Value?.Trim() : null;
            if (string.IsNullOrEmpty(model) && provider.Local)
            {
                return LocalLlm.FirstModel(provider) ?? string.Empty;
            }
            return string.IsNullOrEmpty(model) ? provider.Recommended?.Id : model;
        }

        internal static ProviderHealth HealthOf(string provider)
        {
            lock (Gate)
            {
                if (!Health.TryGetValue(provider, out ProviderHealth health))
                {
                    health = new ProviderHealth();
                    Health[provider] = health;
                }
                return health;
            }
        }

        /// <summary>The provider that responded last (or the first ready one in the queue).</summary>
        internal static ProviderDef Current
        {
            get
            {
                List<ProviderDef> ready = Ready();
                lock (Gate)
                {
                    ProviderDef used = ready.FirstOrDefault(p => p.Id == lastUsed);
                    return used ?? ready.FirstOrDefault(p => HealthOf(p.Id).OutUntilUtc <= DateTime.UtcNow) ?? ready.FirstOrDefault();
                }
            }
        }

        /// <summary>Short text for F10 and "ai status": provider and model in use.</summary>
        internal static string Describe()
        {
            List<ProviderDef> ready = Ready();
            if (ready.Count == 0)
            {
                return Chain().Count == 0 ? "no provider configured (Realpolitik screen)" : "no key/login on the queued providers";
            }
            ProviderDef current = Current;
            string rest = ready.Count > 1 ? " · fallback: " +string.Join(", ", ready.Where(p => p != current).Select(p => p.Name)) : string.Empty;
            return $"{current.Name} ({ModelFor(current)}){rest}";
        }

        /// <summary>Worker thread: sends through the first provider that responds.</summary>
        internal static ChatResult Send(ChatRequest request, int timeoutSeconds)
        {
            List<ProviderDef> ready = Ready();
            if (ready.Count == 0)
            {
                return new ChatResult { Ok = false, Kind = ErrorKind.Auth, Error = "nenhum provedor com chave ou login (tela Realpolitik)" };
            }
            DateTime now = DateTime.UtcNow;
            // Providers sitting out after a recent error go to the end, but are still tried if all are sitting out.
            List<ProviderDef> order = ready.Where(p => HealthOf(p.Id).OutUntilUtc <= now)
                .Concat(ready.Where(p => HealthOf(p.Id).OutUntilUtc > now)).ToList();

            ChatResult last = null;
            var failed = new List<string>();
            foreach (ProviderDef provider in order)
            {
                ChatResult result = SendWith(provider, request, timeoutSeconds);
                ProviderHealth health = HealthOf(provider.Id);
                lock (Gate)
                {
                    health.Calls++;
                    if (result.Ok)
                    {
                        health.LastOkUtc = DateTime.UtcNow;
                        health.OutUntilUtc = DateTime.MinValue;
                        health.CostUsd += result.CostUsd;
                    }
                    else
                    {
                        health.LastErrorUtc = DateTime.UtcNow;
                        health.LastError = result.Error;
                        health.LastKind = result.Kind;
                        // A rejected key (401/403) will not fix itself in a few minutes: sit out for an hour, so a dead
                        // fallback key does not take the turns that the working provider would answer.
                        health.OutUntilUtc = DateTime.UtcNow.AddMinutes(result.Kind == ErrorKind.Auth ? 60 : Math.Max(1, AiConfig.FailoverMinutes.Value));
                    }
                }
                if (result.Ok)
                {
                    NoteSuccess(provider, failed);
                    return result;
                }
                failed.Add($"{provider.Name}: {Short(result)}");
                last = result;
            }
            NoteAllDown(failed);
            return last;
        }

        /// <summary>A single provider ("Test connection" and the router).</summary>
        internal static ChatResult SendWith(ProviderDef provider, ChatRequest request, int timeoutSeconds, Credential credential = null)
        {
            if (provider.Format == WireFormat.CodexCli)
            {
                return CodexCli.Send(new ChatRequest
                {
                    Model = string.IsNullOrEmpty(request.Model) ? ModelFor(provider) : request.Model,
                    Messages = request.Messages,
                    MaxTokens = request.MaxTokens,
                    Temperature = request.Temperature,
                    ReasoningEffort = request.ReasoningEffort,
                    JsonMode = request.JsonMode,
                }, provider, timeoutSeconds);
            }
            if (provider.Local)
            {
                LocalLlm.Apply(provider);
                if (string.IsNullOrEmpty(request.Model) && string.IsNullOrEmpty(ModelFor(provider)))
                {
                    // We do not know the model yet (never listed): probe the server and fail this call, the queue moves on.
                    LocalLlm.Probe(provider, force: true);
                    return new ChatResult { Ok = false, Kind = ErrorKind.Network, ProviderId = provider.Id, Error = "servidor local sem modelo (ou fora do ar)" };
                }
                credential = new Credential();
            }
            credential = credential ?? Credentials.Get(provider.Id);
            if (credential == null)
            {
                return new ChatResult { Ok = false, Kind = ErrorKind.Auth, ProviderId = provider.Id, Error = "sem chave ou login" };
            }

            var copy = new ChatRequest
            {
                Model = ModelFor(provider),
                Messages = request.Messages,
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                ReasoningEffort = request.ReasoningEffort,
                JsonMode = request.JsonMode,
            };
            if (!string.IsNullOrEmpty(request.Model))
            {
                copy.Model = request.Model;
            }
            return LlmClient.Send(copy, provider, credential.Bearer, timeoutSeconds);
        }

        private static void NoteSuccess(ProviderDef provider, List<string> failed)
        {
            lock (Gate)
            {
                allDownNotified = false;
                if (allDown)
                {
                    allDown = false;
                    probeInterval = TimeSpan.FromMinutes(2);
                    lastUsed = provider.Id;
                    Notices.Enqueue(L.F("IA reconectada: usando {0}.", provider.Name));
                    return;
                }
                if (lastUsed == provider.Id)
                {
                    return;
                }
                bool firstUse = lastUsed == null && failed.Count == 0;
                lastUsed = provider.Id;
                if (!firstUse)
                {
                    Notices.Enqueue(failed.Count > 0
                        ? L.F("IA: usando {0} ({1}).", provider.Name, string.Join("; ", failed))
                        : L.F("IA: voltou a usar {0}.", provider.Name));
                }
            }
        }

        private static void NoteAllDown(List<string> failed)
        {
            lock (Gate)
            {
                lastUsed = null;
                allDownReason = string.Join("; ", failed);
                if (allDown)
                {
                    // Failed probe: wait twice as long until the next one (at most 10 min).
                    probeInterval = TimeSpan.FromMinutes(Math.Min(10, probeInterval.TotalMinutes * 2));
                    nextProbeUtc = DateTime.UtcNow + probeInterval;
                    return;
                }
                allDown = true;
                allDownSinceUtc = DateTime.UtcNow;
                probeInterval = TimeSpan.FromMinutes(2);
                nextProbeUtc = DateTime.UtcNow + probeInterval;
                if (allDownNotified || DateTime.UtcNow - lastDownPopupUtc < TimeSpan.FromMinutes(15))
                {
                    return;
                }
                allDownNotified = true;
                lastDownPopupUtc = DateTime.UtcNow;
                Notices.Enqueue("!" + L.F("A IA está desconectada: nenhum provedor respondeu ({0}). A IA nativa do jogo joga pelas nações; o mod testa de novo sozinho de tempos em tempos. O X vermelho no botão das cartas some quando ela voltar.",
                    allDownReason));
            }
        }

        /// <summary>Short, translated reason for an error.</summary>
        internal static string Short(ChatResult result)
        {
            switch (result.Kind)
            {
                case ErrorKind.Auth: return L.T("chave ou login recusado");
                case ErrorKind.Credit: return L.T("sem crédito ou cota");
                case ErrorKind.Rate: return L.T("limite de chamadas por minuto");
                case ErrorKind.Request: return L.T("pedido recusado (modelo?)");
                case ErrorKind.Server: return L.T("servidor do provedor com erro");
                case ErrorKind.Network: return L.T("sem conexão");
                default: return L.T("erro");
            }
        }

        /// <summary>Resets everything to normal (providers changed on the screen).</summary>
        internal static void ResetHealth()
        {
            lock (Gate)
            {
                foreach (ProviderHealth health in Health.Values)
                {
                    health.OutUntilUtc = DateTime.MinValue;
                }
                lastUsed = null;
                allDownNotified = false;
                // Queue or credentials changed: retry right away.
                nextProbeUtc = DateTime.MinValue;
                probeInterval = TimeSpan.FromMinutes(2);
            }
        }
    }
}
