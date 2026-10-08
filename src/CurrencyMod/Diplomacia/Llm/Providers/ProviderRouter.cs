using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace CurrencyMod.Diplomacia.Llm.Providers
{
    /// <summary>Estado de um provedor para a tela e o F10 (nada de chave aqui).</summary>
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
    /// Fila de provedores ([IA.Provedores] Ordem): o primeiro é o principal; se ele der qualquer erro (depois das
    /// tentativas normais de rede), fica de fora por alguns minutos e o próximo assume. Se todos falharem, a chamada
    /// falha e a IA nativa do jogo continua jogando (travas soltas pelo IaModule), sem travar o turno.
    /// </summary>
    internal static class ProviderRouter
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ProviderHealth> Health = new Dictionary<string, ProviderHealth>();
        private static string lastUsed;
        private static bool allDownNotified;

        /// <summary>Avisos para a thread principal (Runtime.Event, aviso na tela).</summary>
        internal static readonly ConcurrentQueue<string> Notices = new ConcurrentQueue<string>();

        /// <summary>A fila configurada, só com provedores conhecidos e visíveis. Vazia = DeepSeek, se houver chave.</summary>
        internal static List<ProviderDef> Chain()
        {
            var chain = new List<ProviderDef>();
            foreach (string id in (IaConfig.ProviderOrder?.Value ?? string.Empty).Split(',', ';', ' '))
            {
                ProviderDef provider = ProviderCatalog.Get(id);
                if (provider != null && provider.Visible && !chain.Contains(provider))
                {
                    chain.Add(provider);
                }
            }
            if (chain.Count == 0 && string.IsNullOrWhiteSpace(IaConfig.ProviderOrder?.Value) && Credentials.Has("deepseek"))
            {
                chain.Add(ProviderCatalog.Get("deepseek"));
            }
            return chain;
        }

        internal static void SetChain(IEnumerable<string> ids)
        {
            IaConfig.ProviderOrder.Value = string.Join(",", ids.Where(id => ProviderCatalog.Get(id) != null).Distinct());
        }

        /// <summary>Provedores da fila que têm chave ou login.</summary>
        internal static List<ProviderDef> Ready() => Chain().Where(HasAccess).ToList();

        /// <summary>Tem chave ou login. O Codex não guarda nada no mod: vale o login feito no próprio Codex.</summary>
        internal static bool HasAccess(ProviderDef provider)
        {
            // O modelo local não tem chave: se o servidor não estiver de pé, a chamada falha e a fila passa ao próximo.
            return provider.Format == WireFormat.CodexCli ? CodexCli.Ready : provider.Local || Credentials.Has(provider.Id);
        }

        internal static bool AnyReady => Ready().Count > 0;

        internal static string ModelFor(ProviderDef provider)
        {
            string model = IaConfig.ProviderModels.TryGetValue(provider.Id, out var entry) ? entry.Value?.Trim() : null;
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

        /// <summary>O provedor que respondeu por último (ou o primeiro pronto da fila).</summary>
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

        /// <summary>Texto curto para o F10 e o "ia status": provedor e modelo em uso.</summary>
        internal static string Describe()
        {
            List<ProviderDef> ready = Ready();
            if (ready.Count == 0)
            {
                return Chain().Count == 0 ? "nenhum provedor configurado (tela Realpolitik)" : "sem chave/login nos provedores da fila";
            }
            ProviderDef current = Current;
            string rest = ready.Count > 1 ? " · reserva: " + string.Join(", ", ready.Where(p => p != current).Select(p => p.Name)) : string.Empty;
            return $"{current.Name} ({ModelFor(current)}){rest}";
        }

        /// <summary>Thread de trabalho: manda pelo primeiro provedor que responder.</summary>
        internal static ChatResult Send(ChatRequest request, int timeoutSeconds)
        {
            // Sem licença, nenhuma decisão, carta ou conselho chama a IA (o "Testar conexão" usa o SendWith e continua livre).
            if (!Licenca.License.AllowsAi)
            {
                return new ChatResult { Ok = false, Kind = ErrorKind.Auth, Error = "sem licença (tela Realpolitik → Licença)" };
            }
            List<ProviderDef> ready = Ready();
            if (ready.Count == 0)
            {
                return new ChatResult { Ok = false, Kind = ErrorKind.Auth, Error = "nenhum provedor com chave ou login (tela Realpolitik)" };
            }
            DateTime now = DateTime.UtcNow;
            // Quem está de fora por erro recente vai para o fim, mas ainda é tentado se todos estiverem de fora.
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
                        health.OutUntilUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, IaConfig.FailoverMinutes.Value));
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

        /// <summary>Um provedor só (o "Testar conexão" e o roteador).</summary>
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
                    // Ainda não sabemos o modelo (nunca listou): procura o servidor e falha esta chamada, a fila segue.
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
                if (allDownNotified)
                {
                    return;
                }
                allDownNotified = true;
                Notices.Enqueue("!" + L.F("Nenhum provedor de IA respondeu ({0}). A IA nativa do jogo joga pelas nações até um deles voltar.",
                    string.Join("; ", failed)));
            }
        }

        /// <summary>Motivo curto e traduzido de um erro.</summary>
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

        /// <summary>Volta tudo ao normal (troca de provedores na tela).</summary>
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
            }
        }
    }
}
