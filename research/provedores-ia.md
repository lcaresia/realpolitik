# Provedores de IA: pesquisa (fase 0)

Pesquisado em 2026-10-06 nas documentações oficiais (exceto onde marcado **(N)** = não confirmado / fonte de terceiros).
Base para `docs\prompt-provedores-ia.md`. Preços em US$ por milhão de tokens.

## Resumo

| Provedor | Dá para fazer já? | O que exige |
|---|---|---|
| OpenAI "Sign in with ChatGPT" | **Não** (app pago) | Formulário de interesse; acesso comercial é "trial limitado" para parceiros escolhidos, sem prazo |
| xAI (SuperGrok/X Premium) | **Não** | Não existe programa público; só parceiros anunciados. Pedir por contato |
| OpenRouter (login) | **Sim** | Nada: sem cadastro de app, sem client id |
| Chaves de API (DeepSeek, OpenAI, Anthropic, Gemini, xAI, Z.ai/GLM) | **Sim** | Só o adaptador de formato de cada uma |
| Local (Ollama, LM Studio) | **Sim** | Cuidado com o contexto padrão de 4k do Ollama e o json_schema do LM Studio |

## 1. OpenAI: Sign in with ChatGPT (SIWC)

- Docs: https://developers.openai.com/siwc. Ampliado no DevDay de 29/09/2026 (imprensa: thenewstack.io/sign-in-with-chatgpt). A página de ajuda da OpenAI deu 403 no fetch **(N data oficial)**.
- **Duas trilhas** (https://developers.openai.com/siwc/token-sharing-open-source):
  - apps **open source / locais**: fluxo documentado sem aprovação (cliente registrado dinamicamente por instalação);
  - apps **pagos ou hospedados**: formulário https://openai.com/form/sign-in-with-chatgpt-interest/ → "limited trial" para parceiros comerciais selecionados (https://developers.openai.com/siwc/request-client-id). **O mod pago cai aqui.**
  - Campos do formulário **(N)**: e-mail de trabalho, empresa, site, capacidades desejadas. Prazo, taxa e aceitação de dev individual: não documentados.
- **Fluxo OAuth:** `https://auth.openai.com/api/accounts/authorize`, `response_type=code`, PKCE S256. Escopos `openid profile email offline_access resource.invoke chatgpt.tokens.use.direct`. Retorno em loopback (ex.: `http://127.0.0.1:1455/auth/callback`). Token: `https://auth.openai.com/api/accounts/oauth/token`, sem segredo. **Sem código de dispositivo.**
- **Tokens:** acesso 1 h; refresh 30 dias, rotativo (`grant_type=refresh_token`, `resource=https://api.openai.com/v1`). Sair = revogar.
- **API:** só **Responses** (`POST https://api.openai.com/v1/responses`), com `store:false`, `stream:true`, `input` em array. Sem `temperature`, `max_output_tokens` etc. Modelos via `GET /v1/models` (filtrar `visibility=="list"`). Saída em json/structured outputs: **(N), precisa testar.**
- **Erros:** 429 `subscription_sharing_usage_limit_exceeded` (cota; pode vir no meio do stream), 403 `..._user_not_eligible`, 400 `..._unsupported_capability`, 401 `..._invalid_user` (logar de novo), 503 (backoff). Plus tem limite de 5 h compartilhado entre apps; o usuário pode pôr teto semanal por app.
- **Proibido:** reaproveitar o client do Codex CLI.

## 2. xAI: login do SuperGrok / X Premium

- Só anúncios de parceiros (Hermes, OpenClaw, OpenCode, Kilo Code: https://x.ai/news/grok-openclaw etc.). **Nenhuma página em docs.x.ai** sobre OAuth para terceiros, nem formulário/lista de liberação.
- **(N, terceiros):** device code (RFC 8628) em `auth.x.ai`, cliente público compartilhado ("Grok Build"), token usado como Bearer em `https://api.x.ai/v1`. **Não usar o cliente compartilhado** sem permissão escrita.
- **(N)** 403 depois do login dependendo do plano (issues no GitHub do hermes-agent e openclaw).
- Caminho: pedir um client OAuth próprio pelo contato de https://x.ai/api.

## 3. OpenRouter

- **OAuth PKCE** (https://openrouter.ai/docs/use-cases/oauth-pkce): abrir `https://openrouter.ai/auth?callback_url=...&code_challenge=...&code_challenge_method=S256&state=...`. **Localhost em qualquer porta** é aceito (usar `localhost`; 127.0.0.1 só aparece na referência da API). Modo sem `callback_url`: a página mostra o código para colar (alternativa se o HttpListener falhar). Código expira em 10 min.
- **Troca:** `POST https://openrouter.ai/api/v1/auth/keys` `{code, code_verifier, code_challenge_method}` → `{"key":"sk-or-..."}`. Sem aprovação nem client id. Validade/limite da chave pelo navegador: **(N)** (o usuário gerencia em openrouter.ai/settings/keys).
- **Chamadas:** `https://openrouter.ai/api/v1/chat/completions`, Bearer. Cabeçalhos opcionais `HTTP-Referer` e `X-OpenRouter-Title` (ou `X-Title`).
- **Modelos:** `GET /api/v1/models` (`pricing` por token em string, `supported_parameters` com `response_format`/`structured_outputs`).
- **Crédito:** `GET /api/v1/key` → `limit_remaining`, `usage`, `is_free_tier`, `free_model_daily_requests`.
- **json:** `response_format` é preferência "suave" (pode ser ignorado); `provider.require_parameters=true` força, com risco de 503. O parser tolerante continua necessário.
- **Custo:** `usage.cost` vem na resposta, junto com `cached_tokens` e `reasoning_tokens`.
- **Erros:** 401 chave, **402 sem crédito** (`metadata.limit_source`), 403 moderação, 429, 502/503. Alguns erros vêm em HTTP 200 com `error` no corpo. Modelos `:free`: 20/min e 50/dia (1000/dia com ≥ US$10 comprados).

## 4. APIs por chave

| Provedor | Endpoint | json | Modelo barato: entrada / cache / saída | Erros de crédito |
|---|---|---|---|---|
| DeepSeek | `https://api.deepseek.com/chat/completions` | `json_object` (o prompt precisa dizer "json") | `deepseek-flash` (V4.1-Flash): 0,30 / 0,006 / 1,20 no pico, metade fora. Pico 01–04 e 06–10 UTC seg–sex (bate com o `.cfg`) | 402 |
| OpenAI | `https://api.openai.com/v1/chat/completions` | `json_object` e `json_schema` | `gpt-6-luna` 0,10 / 0,01 / 0,50 · `gpt-5-nano` 0,05 / 0,005 / 0,40 | **429 com `insufficient_quota`** |
| Anthropic | nativo `https://api.anthropic.com/v1/messages` (`x-api-key`, `anthropic-version: 2023-06-01`) | structured outputs `output_config.format` json_schema; **prefill dá 400 no Claude 4.6+**; a camada compatível com OpenAI ignora `response_format` e não faz cache ("não é para produção") | `claude-haiku-4-5` 1 / 0,10 / 5 · `claude-sonnet-5-5` 2 / 0,20 / 10. Cache só com `cache_control` | 402 `billing_error` |
| Gemini (AI Studio) | `https://generativelanguage.googleapis.com/v1beta/openai/chat/completions` | schema via `response_format`; `json_object` **(N)**; parâmetros desconhecidos ignorados em silêncio | `gemini-2.5-flash-lite` 0,10 / 0,01 / 0,40 · `gemini-3.1-flash-lite` 0,25 / 0,025 / 1,50 | 402; 429 `RESOURCE_EXHAUSTED` |
| xAI | `https://api.x.ai/v1/chat/completions` ("legado"; recomendam `/v1/responses`) | `json_schema`; `json_object` **(N)** | `grok-4.3` 1,25 / 0,20 / 2,50 | 403 / 429 |
| Z.ai (GLM, internacional) | `https://api.z.ai/api/paas/v4/chat/completions` | `json_object` (glm-4.5+) | **`GLM-4.7-Flash` grátis**; `GLM-4.7-FlashX` 0,07 / 0,01 / 0,40 | HTTP 429 código 1113 |
| Zhipu (China) | `https://open.bigmodel.cn/api/paas/v4/` **(N)** | igual ao Z.ai **(N)** | preços em CNY **(N)** | — |

- **Gemini grátis:** o Google não publica mais os limites (ver em aistudio.google.com/rate-limit). Terceiros **(N)**: Flash-Lite ~15/min e ~1000/dia. Com 16 chamadas por turno precisa de espaçamento (~4–6 s). **Os dados do nível grátis são usados para treinar**: avisar na tela.
- **Listar modelos:** `GET /models` em todos, exceto Z.ai/Zhipu (lista fixa no código). Anthropic: `GET /v1/models` com `x-api-key`.
- **Repetir** em 429 (exceto falta de crédito), 5xx e 529. **Não repetir** 400/401/402/403.
- **Raciocínio:** o `deepseek-flash` tem raciocínio alto por padrão; Claude 5.x, Grok e Gemini de raciocínio também. Desligar ou usar "low" onde der. O parâmetro `thinking` atual é do DeepSeek; cada adaptador traduz.

### Custo por turno (estimativa relativa)

Hipótese do agente: 16 chamadas × 8k de entrada (metade em cache) + 1k de saída, sem raciocínio. **O gasto real medido no mod com DeepSeek é ~US$0,12/turno** (prompts maiores + raciocínio), cerca de 3× a estimativa; use a tabela só para comparar.

| Modelo | Estimado/turno | Real esperado (×3) |
|---|---|---|
| Z.ai GLM-4.7-Flash / Gemini grátis | 0 | 0 (com limites) |
| GLM-4.7-FlashX | 0,012 | ~0,035 |
| Gemini 2.5 Flash-Lite | 0,014 | ~0,04 |
| OpenAI gpt-6-luna | 0,015 | ~0,045 |
| DeepSeek flash (fora / no pico) | 0,019 / 0,039 | ~0,06 / ~0,12 |
| Gemini 3.1 Flash-Lite | 0,042 | ~0,13 |
| xAI grok-4.3 | 0,133 | ~0,40 |
| Claude Haiku 4.5 | 0,166 | ~0,50 |
| Claude Sonnet 5.5 | 0,333 | ~1,00 |

## 5. Local

- **Ollama:** `http://localhost:11434/v1/chat/completions` (chave ignorada), `response_format` json_object aceito. Modelos: `GET /api/tags`. Detectar: GET curto em `/api/tags`.
  - **Armadilha:** em GPU < 24 GB o **contexto padrão é 4k** e corta o prompt sem erro (https://docs.ollama.com/context-length). Nossos prompts passam disso → usar o endpoint nativo `/api/chat` com `options.num_ctx` (ex.: 16384) e `format:"json"`.
- **LM Studio:** `http://localhost:1234/v1`, servidor ligado à mão (aba Developer ou `lms server start`). Só documenta `json_schema`; `json_object` dá erro (issue #173 do bug tracker) → mandar json_schema genérico ou nada.
- **Modelos (N, guias de terceiros):** Qwen3 8B/14B (melhor para json; desligar o thinking ou tirar `<think>`), Gemma 3 12B (melhor texto multilíngue), Mistral Nemo 12B. ~25–30 tokens/s numa placa de 12 GB; com o Humankind aberto, na prática ~8B.

## 6. Proibidos (confirmado)

- **Anthropic:** terceiros não podem oferecer login claude.ai nem usar crédito Free/Pro/Max "sem aprovação prévia" (https://code.claude.com/docs/en/legal-and-compliance).
- **Google:** OAuth do Gemini CLI/Antigravity em ferramenta de terceiros viola os termos e pode suspender a conta (https://github.com/google-gemini/gemini-cli/blob/main/docs/resources/tos-privacy.md).

## Fontes principais

developers.openai.com/siwc (+ subpáginas sign-in, token-reference, models-and-inference, preview-limitations, errors-and-recovery) ·
x.ai/news/grok-openclaw · openrouter.ai/docs (oauth-pkce, app-attribution, models, limits, errors, provider-selection, usage-accounting) ·
api-docs.deepseek.com (pricing, json_mode, error_codes) · developers.openai.com/api/docs/pricing · platform.claude.com/docs (openai-sdk, pricing, errors) ·
ai.google.dev/gemini-api/docs (pricing, openai, rate-limits) · docs.x.ai (models, structured-outputs) · docs.z.ai (pricing, struct-output, api-code) ·
docs.ollama.com (openai-compatibility, context-length) · lmstudio.ai/docs/developer.

## 7. Testes no jogo (2026-10-06, Teste16, 16 impérios)

| Teste | Resultado |
|---|---|
| Chave errada: DeepSeek, OpenAI, Gemini, GLM, xAI, OpenRouter | Todos com "Chave recusada" (ou "Chave ou login recusado" no OpenRouter). A OpenAI devolve pedaços da chave (`sk-teste****0000`): agora trocados por `[chave]` antes de qualquer log. |
| Sem rede (`.invalid`) | "Sem conexão com o provedor. Confira a internet." |
| Login OpenRouter (sem navegador, retorno simulado) | `state` errado recusado; código falso → "O provedor recusou o login (Invalid code)". A página do navegador só responde depois da troca. |
| Login ChatGPT / Grok | "Aguardando aprovação" (sem identificador no `.cfg`; os dois nem aparecem na tela). |
| Testar conexão DeepSeek (chave real) | `{"ok": true}` em 0,9–1,1 s, ~US$ 0,00001. |
| Fila: OpenAI (chave falsa) → DeepSeek | Respondeu o DeepSeek; a OpenAI ficou 10 min de fora. Só a OpenAI na fila: falha + aviso nativo uma vez. |

**Turnos reais com DeepSeek pela fila nova** (`deepseek-flash`, raciocínio low, fora do pico):

| Turno | Nações | Chamadas | Custo | Tempo | Erros |
|---|---|---|---|---|---|
| T77 | 15/15 ok + conselho | 28 | US$ 0,136 | ~5 min | 7 respostas cortadas por tamanho, refeitas |
| T78 | 15/15 ok | 23 | US$ 0,121 | ~4 min (230 s com a virada) | 6 cortadas por tamanho, 1 carta com códigos (A103), 1 ação sem reclamação; todas refeitas |

- **Respostas cortadas** (`finish_reason: length`, o raciocínio gasta os 12 mil tokens): já aconteciam com o cliente
  antigo (59 de 413 chamadas, 14%, na partida dos Francos); com 16 impérios foram 27%. O corpo do pedido ao DeepSeek
  é o mesmo de antes. Cada corte custa uma chamada a mais. Sugestão para o lucas decidir: subir `MaxTokensResposta`
  para 16000 ou pedir o raciocínio mais curto no prompt.
- Outros provedores com custo: não testados (precisam de permissão e de conta do lucas).

## 8. ChatGPT pelo Codex (decisão do lucas, 2026-10-06)

- Caminho: o `codex exec` do Codex instalado (o mesmo do `@openai/codex-sdk`), com o login do ChatGPT feito no
  Codex. Risco de política aceito pelo lucas: a OpenAI pede aprovação ("Sign in with ChatGPT") para app **pago** usar
  a cota do plano. Fontes: developers.openai.com/cookbook/articles/sign-in-with-chatgpt e
  developers.openai.com/siwc/token-sharing-open-source; SDK: github.com/openai/codex/blob/main/sdk/typescript/README.md.
- Cotas do Codex (learn.chatgpt.com/docs/pricing): no Plus, os Sol dão ~15–160 mensagens a cada 5 h (1–10 turnos de
  16 impérios); o **gpt-6-luna**, 350–3.000 (22–187 turnos). Pro sem limite de 5 h (pode haver limite semanal).
- Base do Codex por chamada: padrão ~20,4 mil tokens e ~15 s; com ferramentas desligadas e instruções próprias,
  ~10,1 mil e ~5–6 s (ping).
- **Turno real T78 (Teste16, gpt-6-luna, fila Codex → DeepSeek):** 15/15 nações ok, 17 chamadas (2 refeitas por
  ação ou código inválido), nenhuma caiu na reserva, **~1 min** do carregamento ao fim das decisões, 14 s por chamada
  em média, ~27 mil tokens de entrada (pouco cache) e ~520 gerados, US$ 0. Cartas e ações coerentes.

## 9. Descartados (2026-10-06)

O lucas decidiu não integrar o login oficial "Sign in with ChatGPT" nem o login da assinatura do Grok. O código, as opções do `.cfg` (`ChatGPTClientId`, `ChatGPTPortaRetorno`, `GrokClientId`, `Modelo_chatgpt`, `Modelo_grok`, apagadas sozinhas) e os pedidos de aprovação saíram. As seções 1 e 2 ficam como registro da pesquisa.
