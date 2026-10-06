# Pesquisa: cache de prompt e corte de raciocínio (2026-10-06)

Complementa `_Modding\research\provedores-ia.md`. Preços em US$ por milhão de tokens. **(N)** = não confirmado.
Obs.: as páginas foram lidas por um extrator automático; números conferidos em mais de uma leitura quando houve dúvida.

## 0. ACHADO PRINCIPAL: o "low" provavelmente não está chegando ao DeepSeek

- O guia oficial mostra `reasoning_effort` como **parâmetro de nível superior** do corpo, e `thinking` só com `type`:
  `reasoning_effort="high", extra_body={"thinking": {"type": "enabled"}}` — https://api-docs.deepseek.com/guides/thinking_mode
- O mod manda `"thinking": {"type": "enabled", "reasoning_effort": "low"}` (aninhado) em
  `_Modding\src\CurrencyMod\Diplomacia\Llm\LlmClient.cs` linhas 131–133.
- O padrão quando o esforço não é informado é **"high"** ("The default effort is 'high'") — https://api-docs.deepseek.com/api/create-chat-completion
- Usuários do V4.1 Flash relatam que **"low" é quase igual a desligar o raciocínio** e que "high" ≈ "max" e "takes forever" — https://hn.nuxt.dev/item/49624603 (terceiros, **(N)** oficial)
- Conclusão provável: o pedido está rodando em **high**, o que explica a mediana de ~8.200 tokens de raciocínio.
  **Teste barato:** 2 chamadas iguais, uma com `reasoning_effort` no topo e outra aninhada, comparando
  `usage.completion_tokens_details.reasoning_tokens`. (A referência da API é ambígua sobre o aninhamento **(N)**; o exemplo do guia é no topo.)
- O mesmo vale para o OpenRouter: o mod já manda `reasoning.effort` (formato certo).

## 1. DeepSeek (API direta)

Fonte de preços: https://api-docs.deepseek.com/quick_start/pricing

| Modelo | Cache hit | Cache miss | Saída | Fora do pico (−50%) |
|---|---|---|---|---|
| `deepseek-flash` (V4.1 Flash; aceita os nomes antigos `deepseek-v4-flash`) | 0,006 | 0,30 | 1,20 | 0,003 / 0,15 / 0,60 |
| `deepseek-v4-pro` | 0,044 | 1,32 | 3,96 | 0,022 / 0,66 / 1,98 |

- **Preços da nota antiga continuam válidos.** Pico: 01:00–04:00 e 06:00–10:00 UTC, seg–sex (= 22h–01h e 03h–07h em Brasília); resto é metade do preço. Mesma fonte. Imprensa confirma "rates doubling during weekday peak windows" — https://siliconangle.com/deepseek-releases-v4-1-flash-says-it-outperforms-flagship-v4-pro
- **Modo sem raciocínio tem o mesmo preço por token**; a economia vem só de gerar menos tokens. Contexto 1M, saída máx. 384K. Fonte: pricing.

### Cache de contexto (https://api-docs.deepseek.com/guides/kv_cache)
- **Automático**, ligado para todos, sem mudar código.
- Unidades de prefixo são gravadas: (a) no fim da entrada do usuário e no fim da saída de cada pedido; (b) quando o sistema detecta **prefixo comum entre vários pedidos**; (c) em intervalos fixos de tokens em entradas longas. **O tamanho do intervalo não é publicado (N)** (o "64 tokens" das versões antigas não aparece mais).
- Acerto só se o pedido novo reusar **por inteiro** uma unidade de prefixo. Logo: o system prompt de 5k idêntico nas 16 nações vira prefixo comum e deve dar acerto da 2ª nação em diante; o dossiê que muda todo turno sempre será miss.
- **Mínimo de prefixo: não publicado (N).** **Duração:** limpo quando sem uso, "usually within a few hours to a few days". Melhor esforço, sem garantia de 100%.
- Escopo: mesma conta **(N explícito; implícito no texto)**. Medir com `usage.prompt_cache_hit_tokens` / `prompt_cache_miss_tokens`.
- Dica prática: nada variável (turno, data, nome da nação) dentro dos primeiros 5k; o system prompt deve ser byte a byte igual.

### Raciocínio (https://api-docs.deepseek.com/guides/thinking_mode e /api/create-chat-completion)
- Liga/desliga por pedido: `thinking: {"type": "enabled"|"disabled"}` ou `reasoning_effort: "none"`.
- Valores: `none`, `low`, `high` (padrão), `max`. Mapeamento: minimal→low, low→low, medium→high, high→high, max→max (xhigh: uma leitura diz high, outra max **(N)**).
- **Não existe orçamento de tokens de raciocínio** (nenhum `budget_tokens`) — não documentado.
- `max_tokens` = todos os tokens gerados (o raciocínio entra; por isso o corte por `length` com 12.000). Padrão 8K sem raciocínio, 64K com raciocínio, 128K com `max`; teto 384K.
- Com raciocínio: sem `temperature`/`presence_penalty`/`frequency_penalty`; `top_p` só 0,95–1,0. Sem `tools`, não precisa devolver `reasoning_content`.
- Qualidade: num benchmark de terceiros o V4 Flash marcou 78,0 sem raciocínio vs 83,0 em high/max — https://aiagentstore.ai/ai-models/reasoning-effort/deepseek-v4-flash **(N)**.

## 2. OpenRouter

- **Cache** (https://openrouter.ai/docs/guides/best-practices/prompt-caching):
  - DeepSeek: automático, leitura ~0,1×, escrita sem custo extra, roteamento "sticky" automático após acerto.
  - OpenAI: automático a partir de 1.024 tokens; leitura 0,25–0,5× (0,1× nos novos); escrita 1,25× no GPT-5.6+.
  - Gemini 2.5+: implícito (mín. 1.024–4.096 conforme o modelo), leitura 0,25×; `cache_control` explícito com TTL 5 min.
  - Anthropic: precisa de `cache_control` (topo ou por bloco, máx. 4); leitura 0,1×, escrita 1,25× (5 min) ou 2× (1 h).
  - Z.ai/GLM: automático, leitura ~0,2×, escrita grátis (promoção).
- **`reasoning.max_tokens`** (https://openrouter.ai/docs/guides/best-practices/reasoning-tokens): orçamento real só em Gemini (`thinkingBudget`), Anthropic e alguns Qwen. Em modelos "só esforço" o valor vira um nível de esforço. **DeepSeek não aparece na lista → não há orçamento real (N para o comportamento exato).** `max_tokens` precisa ser maior que o orçamento. Raciocínio é cobrado como saída, mesmo com `exclude: true`. Esforço: minimal ≈10%, low ≈20%, medium 50%, high 80%, xhigh/max 95% de max_tokens (para modelos de orçamento); `none` desliga.
- **Preço do `deepseek/deepseek-v4.1-flash`** (https://openrouter.ai/api/v1/models/deepseek/deepseek-v4.1-flash/endpoints, lido em 2026-10-06, num horário fora do pico): o endpoint DeepSeek aparecia a 0,15 / 0,003 (cache) / 0,60. Outros: DeepInfra 0,14 / 0,0042 / 0,42 (fp8, saída máx. 131K); Decart 0,09 / 0,018 / 0,18 (fp4); InferenceNet 0,07 / 0,006 / 0,30; Together/Fireworks 0,30 / 0,006 / 1,20. Provedores fp4/fp8 podem ter qualidade menor; filtrar com `provider.quantizations`, `provider.order`, `max_price` (https://openrouter.ai/docs/guides/routing/provider-selection). Se o OpenRouter repassa o pico do DeepSeek: **(N)**.

## 3. Google Gemini (https://ai.google.dev/gemini-api/docs/pricing)

| Modelo | Standard entrada / cache / saída | Flex e Batch (−50%) |
|---|---|---|
| `gemini-2.5-flash-lite` | 0,10 / 0,01 / 0,40 | 0,05 / 0,01 / 0,20 |
| `gemini-3.1-flash-lite` | 0,25 / 0,025 / 1,50 | 0,125 / 0,0125 / 0,75 |
| `gemini-3.5-flash-lite` | 0,30 / 0,03 / 2,50 **(N: o extrator leu isso duas vezes, mas é estranho)** | 0,15 / 0,02 / 1,25 |
| `gemini-2.5-flash` | 0,30 / 0,03 / 2,50 | 0,15 / 0,03 / 1,25 |
| `gemini-3.7/3.8-flash` | 0,75 / 0,075 / 3,75 até 31/12/2026; dobra em 2027 | metade |

Preços dos 2.5/3.1 Flash-Lite iguais aos da nota antiga. Armazenamento de cache explícito: US$1/M tokens/hora.

- **Cache implícito** (https://ai.google.dev/gemini-api/docs/caching): ligado em todos os 2.5+. Mínimos: 2.048 (2.5 Flash/Pro), 4.096 (3.5–3.8 Flash, 3.1 Pro). Flash-Lite: **(N)**. Dicas oficiais: conteúdo grande e comum no início; mandar pedidos com prefixo parecido em pouco tempo. Desconto = preço de "context caching" da tabela (~90%). TTL do implícito não publicado **(N)**.
- **Raciocínio** (https://ai.google.dev/gemini-api/docs/generate-content/thinking):
  - 2.5 Flash: `thinkingBudget` 0–24.576, **0 desliga**; 2.5 Flash-Lite: 512–24.576, **0 desliga, e já vem desligado por padrão**; −1 = dinâmico.
  - 3.x: usar `thinkingLevel`. `minimal` existe em 3.5/3.6 Flash e 3.5/3.1 Flash-Lite ("matches no thinking for most queries", mas não garante zero). 3.7/3.8 Flash: só low/medium/high.
  - Camada OpenAI (https://ai.google.dev/gemini-api/docs/openai): `reasoning_effort` minimal/low/medium/high; no 2.5, low=1.024 tokens, medium 8.192, high 24.576; `none` só desliga no 2.5 não-Pro ("cannot be turned off for Gemini 2.5 Pro or 3 models"). `service_tier: "flex"` também funciona nessa camada.
- **Flex** (https://ai.google.dev/gemini-api/docs/generate-content/flex-inference): −50%, latência alvo **1–15 min**, erro 429/503 sem fallback automático, timeout ≥10 min, conta nos limites normais. Lento demais para 16 nações por turno, salvo se o turno aceitar espera.

## 4. OpenAI (https://developers.openai.com/api/docs/pricing)

| Modelo | Entrada / cache / saída | Flex/Batch |
|---|---|---|
| `gpt-5-nano` | 0,05 / 0,005 / 0,40 | 0,025 / 0,0025 / 0,20 |
| `gpt-6-luna` | 0,10 / 0,01 / 0,50 (escrita de cache 0,125) | 0,05 / 0,005 / 0,25 |
| `gpt-5-mini` | 0,25 / 0,025 / 2,00 | 0,125 / 0,0125 / 1,00 |

- `gpt-6-luna`: esforço `none`/low/medium (padrão)/high/xhigh/max; contexto 1,05M; saída 128K — https://developers.openai.com/api/docs/models/gpt-6-luna. `gpt-5-nano`: esforço suportado não listado na página **(N; historicamente minimal–high)** — https://developers.openai.com/api/docs/models/gpt-5-nano
- `max_output_tokens` inclui raciocínio; sem orçamento de tokens, só esforço — https://developers.openai.com/api/docs/guides/reasoning
- **Cache** (https://developers.openai.com/api/docs/guides/prompt-caching): automático, mínimo 1.024 tokens; até −95% (0,1× no GPT-5.6+); **escrita 1,25× no GPT-5.6+**; `prompt_cache_key` estável (≈15 pedidos/min por chave); `prompt_cache_retention`: `in_memory` (5–10 min de inatividade, até 1 h) ou `24h`. Modelos antigos arredondam acerto para múltiplos de 128.
- **Flex** (https://developers.openai.com/api/docs/guides/flex-processing): `service_tier: "flex"`, preço de Batch + desconto de cache, beta; mais lento, 429 "resource unavailable" sem cobrança (repetir ou mandar `service_tier: "auto"`); timeout sugerido 15 min. Latência típica: **(N)**.

## 5. Z.ai GLM (https://docs.z.ai/guides/overview/pricing)

- Grátis: `GLM-4.7-Flash`, `GLM-4.5-Flash`. Pagos: `GLM-4.7-FlashX` 0,07 / 0,01 / 0,40; **novo `GLM-5.3-Flash` 0,15 / 0,03 / 0,50**; `GLM-5.3-FlashX` 0,37 / 0,075 / 1,25. Armazenamento de cache "limited-time free".
- Limite do grátis: **1 pedido simultâneo** (terceiros: https://toolnavs.com/en/article/1100-zai-released-glm-47-flash-weights-and-api-free-tier-1-concurrency-and-launched-f) **(N oficial)**; RPM/dia não publicados **(N)**.
- Cache implícito automático em GLM-4.5+/5; `usage.prompt_tokens_details.cached_tokens`; TTL e mínimo não publicados — https://docs.z.ai/guides/capabilities/cache
- Raciocínio: `thinking: {"type": "disabled"}` desliga, **exceto GLM-5.3 e GLM-5.3-Flash (raciocínio forçado)**; ligado por padrão no 4.7/5.x; sem orçamento — https://docs.z.ai/guides/capabilities/thinking-mode

## 6. Anthropic Claude Haiku 4.5 (https://platform.claude.com/docs/en/about-claude/pricing)

- Entrada 1,00; escrita de cache 5 min 1,25 (1,25×); 1 h 2,00 (2×); leitura 0,10 (0,1×); saída 5,00. Batch: 0,50 / 2,50.
- **Mínimo cacheável do Haiku 4.5: 4.096 tokens**; TTL 5 min renovado de graça a cada acerto; até 4 breakpoints; olha 20 blocos para trás; cache automático com `cache_control` no topo — https://platform.claude.com/docs/en/build-with-claude/prompt-caching
- Para o mod: system de 5k passa do mínimo; 16 nações em < 5 min → 1 escrita + 15 leituras por turno; vale usar TTL de 1 h se o turno do jogador demorar > 5 min. Ainda assim é o mais caro da lista (saída 5,00).

## 7. Camadas "flex" que respondem em segundos/minutos

| Oferta | Desconto | Latência | Fonte |
|---|---|---|---|
| DeepSeek fora do pico | −50% | normal (segundos) | https://api-docs.deepseek.com/quick_start/pricing |
| OpenAI Flex | preço de Batch (−50%) | "slower", sem número; 429 possível | https://developers.openai.com/api/docs/guides/flex-processing |
| Gemini Flex | −50% | 1–15 min | https://ai.google.dev/gemini-api/docs/generate-content/flex-inference |
| xAI | só Batch (20–50%) e Priority (2×); flex não encontrado **(N)** | — | https://www.morphllm.com/grok-api-pricing (terceiros) |
| Anthropic | só Batch (−50%, assíncrono) | — | pricing acima |

## 8. Como cortar raciocínio sem perder qualidade

1. **Corrigir o parâmetro** (seção 0) e medir de novo antes de qualquer outra coisa.
2. **Pedir "pense pouco" no prompt funciona mal**: instruções do tipo "use no máximo 100 tokens pensando" são ignoradas ou derrubam a acurácia — https://arxiv.org/html/2511.04108v4
3. **Agrupar pedidos** reduziu raciocínio em 76% (2.950→710) mantendo a acurácia em R1/o1 (mesmo artigo). Para o mod: avaliar 2–4 nações por chamada (cuidado: JSON maior e risco de cortar).
4. **Orçamento real** só em Gemini (`thinkingBudget`) e Anthropic (`budget_tokens`); no DeepSeek/OpenAI só nível de esforço. Corte por `max_tokens` só gera `length` e nova chamada.
5. **Rota em dois níveis:** sem raciocínio (DeepSeek `none`, `gpt-6-luna` `none`, Gemini 2.5 Flash-Lite budget 0) para a rotina; raciocínio só em crises/guerra/congresso. Sugestão de projeto, sem fonte de benchmark específica **(N)**.
6. **Tirar trabalho do raciocínio:** dossiê já com números calculados, opções enumeradas e JSON curto; o modelo pensa menos quando não precisa calcular. Recomendação geral **(N)**.

## Custo por chamada estimado (5k cache + 10k miss + 700 de JSON), fora do pico do DeepSeek

| Cenário | Raciocínio | Custo/chamada | ×16 |
|---|---|---|---|
| DeepSeek hoje (medido ~8.200) | 8.200 | ~US$0,0069 | ~US$0,11 |
| DeepSeek `low` de verdade (~1.000 suposto, **(N)**) | 1.000 | ~0,0026 | ~0,04 |
| DeepSeek `none` | 0 | ~0,0020 | ~0,03 |
| `gpt-6-luna` `none` | 0 | ~0,0015 | ~0,024 |
| Gemini 2.5 Flash-Lite, budget 0 | 0 | ~0,0013 | ~0,02 |
| Gemini 3.1 Flash-Lite `minimal` | ~0 | ~0,0037 | ~0,06 |

No pico, DeepSeek dobra. Cálculo: tokens × preço das tabelas acima.
