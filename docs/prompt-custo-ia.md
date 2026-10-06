# Prompt: estudo profundo para baratear a IA (custo por turno) sem perder qualidade

> Prompt independente para uma sessão nova do Claude Code, aberta na pasta do Humankind. Cole o conteúdo abaixo da linha.

---

## Missão

O mod **Realpolitik: Living Nations for HUMANKIND** faz cada nação do computador pensar com uma IA de linguagem (dossiê
→ diário, sentimentos, cartas, ações reais no jogo), mais o **conselho de ministros** do jogador e os conselhos das
nações. Ele vai ser vendido para jogadores que pagam a própria IA. **Cada centavo por turno pesa na decisão de compra.**

Objetivo: **baixar o custo por turno o máximo possível, mantendo — ou melhorando — a qualidade.** Qualidade aqui é
inegociável:
- cartas vivas, curtas, na voz de cada cultura, que lembram do passado e reagem ao que aconteceu;
- decisões coerentes e livres (blefar, enrolar, ameaçar, recuar);
- ações reais e válidas no jogo;
- dinamismo: o mundo se mexe todo turno;
- o conselho de ministros do jogador e os conselhos das nações continuam ricos.

**Meta sugerida (ajuste com o lucas):** com 16 impérios e o modelo padrão, cair de ~US$ 0,12 por turno (~US$ 0,22 no pico
do DeepSeek) para **menos de US$ 0,04 por turno**, sem piora medível na qualidade. Uma partida de 300 turnos deveria
custar "menos que um café".

O usuário (lucas) fala português (pt-BR, informal). Responda em pt-BR, curto e claro. Leia a memória do projeto
(MEMORY.md já vem carregada), principalmente `llm-diplomacy-idea.md`, `ai-providers.md`, `feedback-ai-freedom.md`,
`feedback-workflow.md`, `humankind-modding-setup.md` e `installable-goal.md`.

## O que existe hoje (leia antes)

- **Docs:**
  - `_Modding\docs\diplomacia-ia.md`: §1 turno, §3 dossiê, §5 configuração, a seção de custos medidos, §14 conselho,
    §15 provedores;
  - `docs\design-diplomacia-ia.md`: decisões e regras de design, que não podem ser quebradas sem conversa;
  - `research\provedores-ia.md`: provedores, preços, json e cache.
- **Código:** `_Modding\src\CurrencyMod\Diplomacia\`. Os arquivos principais:
  - `DossierBuilder.cs` (dossiê), `Prompts.cs` (regras, formato e persona);
  - `IaJobs.cs` (chamada + validação + novas tentativas), `DecisionParser.cs`;
  - `Llm\LlmClient.cs` + `Llm\Providers\*` (provedores, preços, fila);
  - `Council\PlayerCouncil.cs` (conselho do jogador), `IaModule.cs` (despacho por turno).
- **Números já medidos** (confira nos logs, não confie cegamente):
  - Por turno, com 16 impérios: cerca de **21 chamadas × (20 mil tokens de entrada, ~55% em cache, + 6,3 mil
    gerados)**, com `deepseek-flash` e raciocínio `low`.
  - Uma nação/turno com dossiê completo: 3–5 mil tokens de entrada e ~2.400 de saída, dos quais ~800 são raciocínio.
  - Conselho: ~US$ 0,0035 por reunião e ~US$ 0,003 por resposta.
- **Configuração que mexe no custo:** `[IA] Raciocinio`, `MaxTokensResposta`, `ChamadasParalelas`,
  `MaxNacoesPorTurno`, `CartasPorTurno`, `TetoGastoPartidaUSD`, `PrecosPersonalizados`, além da fila de provedores.
- **Dado de ouro:** `BepInEx\DiplomaciaIA\logs\<partida>\T###_E#.json`. Cada decisão real tem o dossiê, o prompt, a
  resposta crua, os tokens, o cache e o custo. Há centenas, das partidas **Francos** e **Teste16** (16 impérios, com
  Congresso). Use isso para medir **e para testar fora do jogo**.

## O que estudar

Para cada alavanca: quanto economiza (estimado com os dados reais), o risco para a qualidade, o trabalho, e a sua
recomendação. Use fontes atuais (2026) para preços e regras de cache de cada provedor, com links.

### A. Raio-X do gasto (antes de qualquer ideia)
- Some os logs: custo por turno, por nação, por tipo de chamada (decisão, nova tentativa por json inválido, conselho,
  resposta ao conselho, Congresso etc.).
- Quebre a entrada por **seção do dossiê e do prompt** (regras, persona, situação, cartas, memória, relações,
  exércitos, Congresso…), em tokens.
- Mostre a **taxa de cache real** e por que ela não é maior: ordem do prefixo, partes que mudam no meio, provedor.
- Mostre a saída: tokens de raciocínio × resposta; quanto do json é usado de fato; campos gerados e descartados.
- Taxa de **novas tentativas** (json inválido, validação, grounding) e quanto elas custam.
- Quantas nações pensam por turno **sem ter nada novo** para decidir.
- Entregue gráficos simples (HTML ou PNG) e uma tabela "para onde vai cada dólar".

### B. Alavancas para investigar (não se limite a elas)
1. **Cache de prompt ao máximo:**
   - tudo que é fixo primeiro (regras, formato, persona, memória longa), depois o que muda devagar, e o turno por
     último;
   - a mesma ordem byte a byte entre turnos;
   - as regras de cache de cada provedor: automático no DeepSeek, OpenAI e Gemini; `cache_control` no Anthropic e no
     OpenRouter; tamanho mínimo, tempo de vida e desconto de cada um.
2. **Dossiê mais enxuto e mais inteligente:**
   - formato compacto (tabelas e códigos em vez de prosa; nomes curtos com glossário no prefixo fixo);
   - **diferença desde o último turno** em vez do mundo inteiro;
   - cartas antigas resumidas;
   - corte por relevância: vizinhos, inimigos e parceiros em detalhe, o resto em uma linha.
   - Cuidado: o grounding depende dos códigos e nomes reais; não pode piorar.
3. **Memória resumida em camadas:** diário recente na íntegra; resumo comprimido dos turnos antigos, atualizado de
   tempos em tempos (de preferência pela mesma chamada da decisão).
4. **Pensar quando importa (cadência por eventos):**
   - a nação pensa quando algo mudou para ela: carta recebida, guerra, proposta, crise, Congresso, mudança de era,
     ataque, pedágio novo;
   - sem nada novo, pensa a cada N turnos ou faz uma chamada "rápida" barata.
   - **Nunca forçar decisão nem prazo** (`feedback-ai-freedom.md`): a nação pode adiar à vontade, mas o mundo tem que
     continuar dinâmico.
   - Meça quantas chamadas hoje resultam em "nada mudou".
5. **Modelos em camadas (roteamento):**
   - um modelo barato e rápido para turnos de rotina;
   - um mais forte (ou mais raciocínio) só quando a situação pede: guerra, rendição, crise, Congresso, carta do
     jogador;
   - a mesma ideia para o conselho.
   - Compare modelos de 2026 em custo × qualidade: DeepSeek, GLM (inclusive os grátis), Gemini Flash-Lite, GPT mini/nano,
     Grok, modelos abertos no OpenRouter; e locais via Ollama/LM Studio como opção "custo zero".
6. **Raciocínio:** quanto o `low` realmente melhora em relação a `desligado`; raciocínio só nos turnos difíceis; um
   teto de tokens de raciocínio por chamada.
7. **Saída menor:**
   - json mais enxuto (chaves curtas, sem campos que ninguém lê);
   - cartas só quando há motivo;
   - diário e sentimentos mais curtos sem perder a voz;
   - saída estruturada (json schema) para zerar as novas tentativas.
8. **Agrupar chamadas:** juntar o conselho na mesma chamada da decisão quando fizer sentido. Medir se uma chamada para
   várias nações pequenas ou menores (povos, nações sem contato) vale a perda de independência. Provavelmente não para
   as grandes: cada nação só sabe o que ela sabe (névoa), e isso **não pode vazar**.
9. **Horário e preço:** desconto fora do pico do DeepSeek, desconto de cache e APIs em lote. A API em lote
   provavelmente não serve, porque o turno não espera horas. Mas confira se algo como "flex/economy tier" serve.
10. **Novas tentativas mais baratas:** quando o json vem inválido, pedir só a parte errada, não tudo de novo.
11. **Conselho do jogador:** a reunião aproveita o mesmo prefixo da decisão da nação do jogador? Dá para os ministros
    falarem só sobre o que mudou? Fala mais curta sem perder personalidade.
12. Qualquer outra ideia que os dados mostrarem.

### C. Medir a qualidade (obrigatório antes de mudar qualquer coisa)
Monte uma **bancada de testes fora do jogo** que reexecuta decisões reais a partir dos logs (`T###_E#.json`): o mesmo
dossiê em versão atual × versão otimizada, ou modelo A × B. Para cada uma, medir:
- **objetivo:** json válido de primeira, ações válidas, erros de grounding (`Grounding.cs`), tamanho das cartas
  (30–100 palavras, máx. 150), repetição, custo, tokens, latência;
- **subjetivo:** uma rubrica de qualidade — voz da cultura, uso da memória, reação aos fatos do turno, coerência com o
  diário, ousadia/dinamismo, qualidade dos ministros — julgada às cegas por um modelo forte e conferida por amostra
  pelo lucas, com as respostas lado a lado sem dizer qual é qual.
- Um conjunto fixo de **cenários difíceis** tirados dos logs: guerra, rendição, Congresso, carta do jogador, traição,
  pedágio/bloqueio e conselho.

A bancada vira uma ferramenta permanente (`_Modding\tools\ia-bench\` ou parecido), para que toda mudança futura de
prompt ou modelo seja medida antes de ir para o jogo. Fica de fora do pacote de venda.

## Fases (pare e converse nos pontos marcados)

1. **Raio-X (A)**, só lendo logs e código. Nada de chamada à API.
2. **Pesquisa (B)** com preços e regras de cache atuais e links.
3. **Relatório e conversa:** `_Modding\docs\estudo-custo-ia.md`, com:
   - um resumo de uma tela no topo: custo hoje, custo possível e as 5 maiores alavancas;
   - a tabela "para onde vai cada dólar";
   - a lista de alavancas com economia estimada × risco × trabalho;
   - um plano em ordem (primeiro o que economiza muito sem risco: cache, saída, novas tentativas);
   - as decisões que ficam com o lucas.

   → **Pare e mostre ao lucas.** Pergunte também **quanto ele autoriza gastar em chamadas de teste** (sugestão: até
   US$ 2–3, com placar do gasto).
4. **Bancada (C)** e linha de base: a qualidade e o custo de hoje nos cenários fixos.
5. **Implementar as alavancas aprovadas, uma de cada vez**, cada uma medida na bancada contra a linha de base. Uma
   alavanca que piora a qualidade sai, mesmo que economize. O `.cfg` ganha opções novas com padrão bom, documentadas.
6. **Validação dentro do jogo com 16 impérios** (Teste16, via kit de dev: `passturns.ps1`).
   - Pelo menos 10 turnos antes e 10 depois, com o custo por turno medido no F10.
   - Prints das cartas e do conselho para o lucas comparar.
   - Siga a regra de deploy: só implantar com `pensando agora: 0`, no mesmo comando.
7. **Entrega:**
   - atualize `docs\diplomacia-ia.md`: custos novos, opções novas, como usar a bancada;
   - atualize o rótulo de estimativa de custo da tela de provedores;
   - atualize a memória do projeto (`llm-diplomacy-idea.md` ou uma nova);
   - faça um backup (`tools\dev\backup.ps1`).

## Regras que não se quebram

- **Qualidade primeiro.** Nenhuma economia entra sem a bancada mostrar que a qualidade se manteve ou melhorou.
- **Liberdade da IA** (`feedback-ai-freedom.md`): nada de forçar decisão, prazo ou ação para economizar. Pensar menos
  vezes é aceitável; pensar pior não é.
- **Névoa de guerra:** uma nação nunca vê o que não sabe. Agrupar ou resumir não pode vazar informação entre nações.
- **Grounding** (design §9.1): tudo que a IA diz tem que existir no jogo. Dossiê compacto não pode tirar os códigos e
  nomes reais.
- **Gasto de API só com autorização do lucas**, dentro do teto que ele der, com placar. A bancada usa a credencial
  pelo próprio código do mod (`Credentials`/DPAPI). **Nunca** leia, imprima, copie ou registre chaves
  (`deepseek.key`, `BepInEx\config\credenciais\`).
- **Jogo:**
  - testes sempre com 16 impérios;
  - nunca `ia reset`;
  - não rode ao mesmo tempo que outra sessão que esteja mexendo no jogo;
  - não implante durante "pensando".
- Nada sai deste PC sem permissão (contas novas, uploads, compras).
