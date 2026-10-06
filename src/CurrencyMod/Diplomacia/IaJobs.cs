using System;
using System.Collections.Generic;
using CurrencyMod.Diplomacia.Llm;

namespace CurrencyMod.Diplomacia
{
    /// <summary>Uma nação pensando num turno: tudo que a thread de trabalho precisa, já pronto.</summary>
    internal sealed class DecisionJob
    {
        public int Session;
        public int Turn;
        public int EmpireIndex;
        public string NationName;
        public string System;
        public string User;
        public ValidationContext Context;
        public List<int> DeliveredLetterIds = new List<int>();
        public List<int> RejectedLetterIds = new List<int>();
        /// <summary>Cartas interceptadas pelos espiões desta nação que o dossiê mostrou (ficam lidas ao aplicar).</summary>
        public List<int> InterceptedLetterIds = new List<int>();
        public int SenderEra;

        public string ReasoningEffort;
        public int MaxTokens;
        public float Temperature;
        public int TimeoutSeconds;
        public int MaxRetries;
    }

    internal sealed class DecisionResult
    {
        public DecisionJob Job;
        public Decision Decision;
        public List<CallRecord> Calls = new List<CallRecord>();
        public List<string> Errors = new List<string>();
        public string Raw;
        public string Reasoning;
        public string FatalError;
        public double CostUsd;
        public int PromptTokens;
        public int CacheHitTokens;
        public int CompletionTokens;
        public int ReasoningTokens;
    }

    /// <summary>Roda numa thread de trabalho: chama a API, valida e pede de novo se precisar.</summary>
    internal static class DecisionRunner
    {
        internal static DecisionResult Run(DecisionJob job)
        {
            var result = new DecisionResult { Job = job };
            var messages = new List<ChatMessage>
            {
                new ChatMessage("system", job.System),
                new ChatMessage("user", job.User),
            };
            for (int attempt = 0; attempt <= job.MaxRetries; attempt++)
            {
                var request = new ChatRequest
                {
                    Messages = messages,
                    MaxTokens = job.MaxTokens,
                    ReasoningEffort = job.ReasoningEffort,
                    Temperature = job.Temperature,
                    JsonMode = true,
                };
                ChatResult chat = Llm.Providers.ProviderRouter.Send(request, job.TimeoutSeconds);
                var call = new CallRecord
                {
                    Turn = job.Turn,
                    Attempt = attempt + 1,
                    Ok = chat.Ok,
                    Error = chat.Error,
                    Seconds = chat.Seconds,
                    Prompt = chat.PromptTokens,
                    Hit = chat.CacheHitTokens,
                    Miss = chat.CacheMissTokens,
                    Completion = chat.CompletionTokens,
                    Reasoning = chat.ReasoningTokens,
                    Cost = chat.CostUsd,
                    FinishReason = chat.FinishReason,
                    Provider = chat.ProviderId == null ? null : chat.ProviderId + "/" + chat.Model,
                };
                result.Calls.Add(call);
                result.CostUsd += chat.CostUsd;
                result.PromptTokens += chat.PromptTokens;
                result.CacheHitTokens += chat.CacheHitTokens;
                result.CompletionTokens += chat.CompletionTokens;
                result.ReasoningTokens += chat.ReasoningTokens;
                if (!chat.Ok)
                {
                    result.FatalError = chat.Error;
                    return result;
                }

                result.Raw = chat.Content;
                result.Reasoning = chat.ReasoningContent;
                var errors = new List<string>();
                if (chat.FinishReason == "length")
                {
                    // Quase sempre é o raciocínio que estoura o teto, não as cartas (estudo-custo-ia.md §4).
                    errors.Add("A resposta foi cortada: você pensou demais e o json não chegou a sair. Pense pouco desta vez e escreva o json direto.");
                }
                else
                {
                    result.Decision = DecisionParser.Parse(chat.Content, job.Context, errors, lenient: attempt == job.MaxRetries);
                }
                if (result.Decision != null)
                {
                    result.Errors.Clear();
                    return result;
                }
                call.Errors = string.Join(" | ", errors);
                result.Errors = errors;
                messages.Add(new ChatMessage("assistant", chat.Content ?? string.Empty));
                messages.Add(new ChatMessage("user",
                    "Sua resposta tem problemas:\n- " + string.Join("\n- ", errors) + "\nCorrija e responda de novo só com o json completo."));
            }
            result.FatalError = "resposta inválida depois de todas as tentativas";
            return result;
        }
    }
}
