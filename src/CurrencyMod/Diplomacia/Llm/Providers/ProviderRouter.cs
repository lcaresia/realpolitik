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

        // ---------------- IA desconectada (melhorias-jogando item 19) ----------------
        // Quando a fila inteira falha, a IA fica "desconectada": nações e conselho param de chamar a API e só uma chamada de
        // teste sai de tempos em tempos (2, 4, 8, até 10 min). A primeira resposta boa reconecta. O aviso na tela aparece
        // uma vez por queda (e não de novo se a IA cair outra vez em menos de 15 min); o botão das cartas mostra um X vermelho.
        private static volatile bool allDown;
        private static DateTime allDownSinceUtc;
        private static DateTime nextProbeUtc;
        private static TimeSpan probeInterval = TimeSpan.FromMinutes(2);
        private static DateTime lastDownPopupUtc = DateTime.MinValue;
        private static string allDownReason;

        /// <summary>Todos os provedores da fila falharam na última tentativa e nenhum respondeu desde então.</summary>
        internal static bool AllDown => allDown;

        internal static string AllDownReason => allDownReason;

        internal static DateTime AllDownSinceUtc => allDownSinceUtc;

        /// <summary>Minutos até a próxima chamada de teste (0 = pode testar agora).</summary>
        internal static int MinutesToProbe => allDown ? Math.Max(0, (int)Math.Ceiling((nextProbeUtc - DateTime.UtcNow).TotalMinutes)) : 0;

        /// <summary>
        /// Pode chamar a API agora? Conectada: sempre. Desconectada: só uma chamada de teste quando o prazo vence (quem pega
        /// a vez segura o próximo teste por 3 min, até a resposta chegar e remarcar).
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
                    // Teste que falhou: espera o dobro até o próximo (no máximo 10 min).
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
                // Fila ou credenciais mudaram: tenta de novo na hora.
                nextProbeUtc = DateTime.MinValue;
                probeInterval = TimeSpan.FromMinutes(2);
            }
        }
    }
}
