using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CurrencyMod.Diplomacia.Llm.Providers
{
    internal enum WireFormat
    {
        /// <summary>chat/completions da OpenAI (DeepSeek, OpenAI, Gemini, GLM, xAI, OpenRouter).</summary>
        ChatCompletions,

        /// <summary>O Codex instalado no PC ("codex exec"), com o login do ChatGPT feito nele (CodexCli).</summary>
        CodexCli,
    }

    internal enum LoginKind
    {
        None,
        /// <summary>Navegador + retorno local com PKCE (OpenRouter).</summary>
        BrowserPkce,
        /// <summary>O login do próprio Codex ("codex login"), que guarda a conta do ChatGPT no Codex.</summary>
        Codex,
    }

    /// <summary>Preço em US$ por milhão de tokens: entrada do cache, entrada fora do cache e saída.</summary>
    internal sealed class ModelDef
    {
        public string Id;
        public bool Recommended;
        public double Hit;
        public double Miss;
        public double Out;

        internal bool Free => Hit == 0 && Miss == 0 && Out == 0;
    }

    internal sealed class ProviderDef
    {
        public string Id;
        public string Name;
        public WireFormat Format = WireFormat.ChatCompletions;
        public LoginKind Login = LoginKind.None;
        /// <summary>Aceita chave colada (o OpenRouter aceita os dois: login ou chave).</summary>
        public bool AcceptsKey = true;
        public string ChatUrl;
        public string ModelsUrl;
        public string KeyUrl;
        /// <summary>Para onde o dossiê vai (nota de privacidade).</summary>
        public string Host;
        public List<ModelDef> Models = new List<ModelDef>();
        /// <summary>Manda response_format json_object (onde a documentação confirma).</summary>
        public bool JsonObject = true;
        /// <summary>Raciocínio no formato do DeepSeek: thinking {type, reasoning_effort}.</summary>
        public bool DeepSeekThinking;
        /// <summary>Raciocínio no formato do GLM: thinking {type} sem nível.</summary>
        public bool GlmThinking;
        /// <summary>Raciocínio no formato do OpenRouter: reasoning {effort | enabled}.</summary>
        public bool OpenRouterReasoning;
        /// <summary>Modelos de raciocínio da OpenAI: max_completion_tokens e sem temperature.</summary>
        public bool OpenAiStyle;
        /// <summary>Preço pela metade fora do horário de pico (DeepSeek).</summary>
        public bool OffPeak;
        /// <summary>Usa a cota da assinatura do jogador: custo em dólar é zero.</summary>
        public bool Subscription;
        /// <summary>Servidor no próprio PC (Ollama, LM Studio...): sem chave, sem custo; o endereço vem de LocalLlm.</summary>
        public bool Local;

        internal bool Visible => true;

        internal ModelDef Recommended => Models.FirstOrDefault(m => m.Recommended) ?? Models.FirstOrDefault();

        internal ModelDef Model(string id) => Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Provedores conhecidos. Endereços e preços conferidos nas páginas oficiais em 2026-10-06
    /// (research\provedores-ia.md). O .cfg pode sobrescrever preços ([IA.Provedores] PrecosPersonalizados).
    /// </summary>
    internal static class ProviderCatalog
    {
        internal static readonly List<ProviderDef> All = new List<ProviderDef>
        {
            new ProviderDef
            {
                Id = "openrouter", Name = "OpenRouter", Login = LoginKind.BrowserPkce,
                ChatUrl = "https://openrouter.ai/api/v1/chat/completions",
                ModelsUrl = "https://openrouter.ai/api/v1/models",
                KeyUrl = "https://openrouter.ai/settings/keys",
                Host = "openrouter.ai",
                OpenRouterReasoning = true,
                Models =
                {
                    M("deepseek/deepseek-v4.1-flash", 0.0165, 0.055, 1.32, recommended: true),
                    M("openai/gpt-6-luna", 0.01, 0.10, 0.50),
                    M("google/gemini-3.1-flash-lite", 0.025, 0.25, 1.50),
                    M("z-ai/glm-5.3-flash", 0.03, 0.15, 0.50),
                    M("x-ai/grok-4.3", 0.20, 1.25, 2.50),
                },
            },
            new ProviderDef
            {
                // Pelo Codex do jogador (@openai/codex-sdk usa o mesmo "codex exec"): cota do Plus/Pro, sem chave.
                Id = "codex", Name = "ChatGPT (Codex)", Format = WireFormat.CodexCli, Login = LoginKind.Codex,
                AcceptsKey = false, Subscription = true, JsonObject = false,
                ChatUrl = "codex exec",
                KeyUrl = "https://developers.openai.com/codex",
                Host = "OpenAI (pelo Codex, com a sua conta do ChatGPT)",
                Models =
                {
                    M("gpt-6-luna", 0, 0, 0, recommended: true),
                    M("gpt-6.1-sol", 0, 0, 0),
                },
            },
            new ProviderDef
            {
                Id = "deepseek", Name = "DeepSeek",
                ChatUrl = "https://api.deepseek.com/chat/completions",
                ModelsUrl = "https://api.deepseek.com/models",
                KeyUrl = "https://platform.deepseek.com/api_keys",
                Host = "api.deepseek.com",
                DeepSeekThinking = true, OffPeak = true,
                Models = { M("deepseek-flash", 0.006, 0.30, 1.20, recommended: true) },
            },
            new ProviderDef
            {
                Id = "openai", Name = "OpenAI",
                ChatUrl = "https://api.openai.com/v1/chat/completions",
                ModelsUrl = "https://api.openai.com/v1/models",
                KeyUrl = "https://platform.openai.com/api-keys",
                Host = "api.openai.com",
                OpenAiStyle = true,
                Models =
                {
                    M("gpt-6-luna", 0.01, 0.10, 0.50, recommended: true),
                    M("gpt-5-nano", 0.005, 0.05, 0.40),
                },
            },
            new ProviderDef
            {
                Id = "gemini", Name = "Google Gemini",
                ChatUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                ModelsUrl = "https://generativelanguage.googleapis.com/v1beta/openai/models",
                KeyUrl = "https://aistudio.google.com/apikey",
                Host = "generativelanguage.googleapis.com",
                Models =
                {
                    M("gemini-2.5-flash-lite", 0.01, 0.10, 0.40, recommended: true),
                    M("gemini-3.1-flash-lite", 0.025, 0.25, 1.50),
                    M("gemini-3.8-flash", 0.075, 0.75, 3.75),
                },
            },
            new ProviderDef
            {
                Id = "zai", Name = "GLM (Z.ai)",
                ChatUrl = "https://api.z.ai/api/paas/v4/chat/completions",
                KeyUrl = "https://z.ai/manage-apikey/apikey-list",
                Host = "api.z.ai",
                GlmThinking = true,
                Models =
                {
                    M("glm-4.7-flash", 0, 0, 0, recommended: true),
                    M("glm-4.7-flashx", 0.01, 0.07, 0.40),
                    M("glm-5.3-flash", 0.03, 0.15, 0.50),
                },
            },
            new ProviderDef
            {
                Id = "xai", Name = "xAI (Grok)",
                ChatUrl = "https://api.x.ai/v1/chat/completions",
                ModelsUrl = "https://api.x.ai/v1/models",
                KeyUrl = "https://console.x.ai",
                Host = "api.x.ai",
                // A documentação da xAI só confirma json_schema: o json vai pedido no prompt e o cliente o extrai.
                JsonObject = false,
                Models =
                {
                    M("grok-4.3", 0.20, 1.25, 2.50, recommended: true),
                    M("grok-4.20-0309-non-reasoning", 0.20, 1.25, 2.50),
                },
            },
            new ProviderDef
            {
                // Qualquer servidor chat/completions local. O endereço é resolvido por LocalLlm.Apply a cada uso.
                Id = LocalLlm.Id, Name = "Ollama / LM Studio", Local = true, AcceptsKey = false, JsonObject = false,
                ChatUrl = "http://localhost:11434/v1/chat/completions",
                ModelsUrl = "http://localhost:11434/v1/models",
                KeyUrl = "https://ollama.com/download",
                Host = "localhost",
            },
        };

        /// <summary>Sugerido para quem acabou de instalar.</summary>
        internal const string Suggested = "openrouter";

        internal static ProviderDef Get(string id) => All.FirstOrDefault(p => string.Equals(p.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Preço de um modelo: o do .cfg se houver, senão o da tabela, senão null (desconhecido).</summary>
        internal static ModelDef Price(ProviderDef provider, string model)
        {
            ModelDef custom = CustomPrice(provider.Id, model);
            if (custom != null)
            {
                return custom;
            }
            if (provider.Subscription || provider.Local)
            {
                return new ModelDef { Id = model };
            }
            return provider.Model(model) ?? Live(provider.Id, model);
        }

        // ---------------- Preços personalizados (.cfg) ----------------

        /// <summary>Formato: "provedor/modelo=cache/entrada/saída; ..." (US$ por milhão de tokens).</summary>
        private static ModelDef CustomPrice(string provider, string model)
        {
            string spec = IaConfig.CustomPrices?.Value;
            if (string.IsNullOrWhiteSpace(spec))
            {
                return null;
            }
            foreach (string entry in spec.Split(';'))
            {
                string[] pair = entry.Split('=');
                if (pair.Length != 2)
                {
                    continue;
                }
                string key = pair[0].Trim();
                if (!string.Equals(key, provider + "/" + model, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(key, model, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string[] prices = pair[1].Split('/');
                if (prices.Length == 3
                    && double.TryParse(prices[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double hit)
                    && double.TryParse(prices[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double miss)
                    && double.TryParse(prices[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double output))
                {
                    return new ModelDef { Id = model, Hit = hit, Miss = miss, Out = output };
                }
            }
            return null;
        }

        // ---------------- Preços da lista de modelos (OpenRouter) ----------------

        private static readonly Dictionary<string, ModelDef> LivePrices = new Dictionary<string, ModelDef>();

        internal static void SetLive(string provider, ModelDef model)
        {
            lock (LivePrices)
            {
                LivePrices[provider + "/" + model.Id] = model;
            }
        }

        private static ModelDef Live(string provider, string model)
        {
            lock (LivePrices)
            {
                return LivePrices.TryGetValue(provider + "/" + model, out ModelDef def) ? def : null;
            }
        }

        private static ModelDef M(string id, double hit, double miss, double output, bool recommended = false)
            => new ModelDef { Id = id, Hit = hit, Miss = miss, Out = output, Recommended = recommended };
    }
}
