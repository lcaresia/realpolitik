# Estudo: baratear a IA por turno sem perder qualidade

> Fases 1–3 do `prompt-custo-ia.md` (raio-X, pesquisa e relatório). **Nenhuma chamada à API foi feita.** Data: 2026-10-06.
> Dados: 279 decisões reais (413 chamadas) dos **Francos**, T100–T130, 9 nações com IA, `deepseek-flash` com raciocínio `low`,
> mais 28 decisões da **Teste16** (T77–78, 16 impérios), lidas antes de os logs sumirem (ver §6).

## Resumo (uma tela)

**Hoje:** ~US$ 0,12 por turno com 16 impérios fora do pico e ~US$ 0,22 no pico. Nos Francos (9 nações): US$ 0,06 fora do
pico e US$ 0,14 no pico.

**Para onde vai o dinheiro:** **68% é raciocínio.** A resposta que o jogo usa (diário, cartas, ações) custa só **8%**.
- O raciocínio `low` do deepseek-flash gera em média **8.200 tokens** de "pensamento" em inglês para escrever uma resposta de ~700
  tokens. Grande parte disso é o modelo resumindo o dossiê para si mesmo.
- **21% das decisões estouram o teto** (12 mil tokens) ainda pensando e são **jogadas fora inteiras**: **28% do gasto**
  é desperdício puro.
- A segunda tentativa dessas decisões sai com ~700 tokens de raciocínio, e o resultado tem praticamente o mesmo volume de
  cartas e ações (ver §3). É um experimento natural que sugere que o raciocínio longo não está comprando muita coisa, mas
  isso **precisa da bancada** para virar conclusão.

**⚠️ Provável bug (§7):** o mod manda o `reasoning_effort` no lugar errado do corpo. Se a API ignora o campo ali, o `low`
nunca valeu e **tudo rodou em `high`**. A prova custa ~US$ 0,02 e, se o bug se confirmar, a correção é de uma linha.

**Custo possível:** **~US$ 0,02–0,03 por turno fora do pico e ~US$ 0,04–0,05 no pico**, com 16 impérios, sem tirar nenhuma
nação de pensar todo turno.

**As 5 maiores alavancas (em ordem):**
| # | Alavanca | Economia estimada | Risco para a qualidade | Trabalho |
|---|---|---|---|---|
| 1 | **Raciocínio sob controle**: desligado na rotina, curto e com teto só nos turnos difíceis | **−55 a −65%** | médio (medir na bancada) | pequeno |
| 2 | **Acabar com as respostas cortadas** (vem junto da 1; e corrigir a mensagem de nova tentativa, que hoje manda "encurtar as cartas" quando o problema é o raciocínio) | **−28%** (já contido na 1) | nenhum | mínimo |
| 3 | **Cache de verdade**: o dossiê na ordem "o que não muda primeiro, o turno por último"; hoje só o prompt de sistema cai no cache (25% de acerto nos Francos) | −10 a −15% do que sobrar | nenhum (mesmo conteúdo) | pequeno |
| 4 | **Correspondência recente mais curta**: é a maior seção do dossiê (28%), maior que as regras | −10% do que sobrar | baixo–médio | médio |
| 5 | **Novas tentativas mais baratas**: o falso positivo "Teodoro" sozinho causou 39 novas tentativas | −3 a −5% | nenhum | pequeno |

**O que NÃO vale a pena agora:**
- **Pensar só quando há novidade:** nos Francos, **toda** decisão tinha carta nova chegando, e só 7 de 279 terminaram sem
  carta nem ação. O mundo é dinâmico; cortar cadência cortaria vida.
- **Juntar várias nações numa chamada:** quebra a névoa e economiza pouco depois das alavancas acima.
- **API em lote:** o turno não espera horas.

---

## 0. Resultados (fases 4–5, 2026-10-06)

Decisões do lucas: `low` em tudo; nada de raciocínio por camadas por enquanto; só o DeepSeek nos testes, mas cada mudança
tem que baixar o custo em porcentagem com qualquer provedor; gastar à vontade na bancada, com placar.

**A bancada** (`_Modding\tools\ia-bench`, ver o README de lá): 12 cenários difíceis do corpus × 4 repetições por variante,
métricas objetivas, simulação de cache sem custo sobre as 279 decisões e comparação às cegas. Gasto total: ~US$ 0,95.

| Versão (por decisão, no pico) | Raciocínio | Entrada | Cache (simulado) | Custo | Corte |
|---|---:|---:|---:|---:|---:|
| Antes: `reasoning_effort` no lugar errado (= `high`) | ~9.100 | 15.200 | 28% | ~US$ 0,015 | — |
| **Bug corrigido** (`low` de verdade) | 4.258 | 15.227 | 28% | US$ 0,0094 | −38% |
| + "COMO PENSAR" + correspondência antiga em uma linha | 3.085 | 13.650 | 32% | US$ 0,0074 | −51% |
| + dossiê na ordem do cache | 2.977 | 13.650 | 34%* | US$ 0,0073 | −52% |

\* A simulação só reordena o texto do log. No mod, a correspondência e a memória também passam a andar em blocos (o começo
fica igual por vários turnos), então o cache real deve passar disso. Isso vai ser medido no jogo.

**Por turno e por partida (16 impérios, deepseek-flash, estimativa antes da validação no jogo):**
- Com 16 impérios a decisão fica perto de US$ 0,008 no pico (a entrada é ~20 mil tokens, contra 15 mil nos Francos).
- Turno cheio: 15 nações + conselho + novas tentativas = **~US$ 0,12 no pico e ~US$ 0,06 fora do pico**. Antes eram ~US$ 0,22
  e ~US$ 0,12.

| Partida de 300 turnos | Agora | Antes |
|---|---:|---:|
| Tudo fora do pico | ~US$ 12–16 | ~US$ 30–36 |
| Misturado | ~US$ 18–24 | ~US$ 45–50 |
| Tudo no pico | ~US$ 25–32 | ~US$ 60–66 |

O começo da partida é mais barato (poucos contatos, poucas cartas). **A meta de US$ 0,04 por turno no pico ainda não foi
alcançada.** O raciocínio continua sendo ~50% do custo, mesmo no `low` corrigido. Próximas alavancas, decididas com o lucas
para manter o `low`:
1. seção de nações por relevância;
2. o cache em blocos medido no jogo;
3. nações sem novidade pensando menos.

Previsão: ~US$ 0,08 por turno no pico. Passar disso exige mexer no raciocínio (desligado na rotina), o que fica para depois.

### Validação no jogo (Teste16, 16 impérios, 2026-10-06)

Os mesmos 10 turnos (T83–T92), a partir do mesmo save ("Teste16 T82 site2"), com a versão antiga (o backup das 11:08,
com o bug) e com a nova. Os dois fora do pico. Logs em `ia-bench\corpus\b57b0e07_antigo` e `b57b0e07_novo`.

| | Antiga | Nova | |
|---|---:|---:|---:|
| Custo dos 10 turnos | US$ 1,224 | **US$ 0,550** | **−55%** |
| Por turno | US$ 0,122 | US$ 0,055 | |
| Tempo por turno (nações + jogo) | 7,3 min | **2,8 min** | 2,6× mais rápido |
| Tempo por decisão | 92 s | 20 s | |
| Chamadas (decisões) | 236 | 156 | |
| Decisões cortadas e jogadas fora | 63 (44% das decisões, **47% do custo**) | **0** | |
| Novas tentativas | 96 | 16 | |
| Raciocínio por decisão | 9.918 | 2.722 | −73% |
| Erros de conexão do DeepSeek | vários (chamadas de 70–90 s caíam) | nenhum | |

Divisão do custo na nova: entrada sem cache 42%, raciocínio 42%, resposta 12%, cache 1%. Por tipo: decisões 94%, conselho
3%, novas tentativas 2%.

**Raciocínio (versão nova):**
- média de 2.703 tokens, mediana de 2.195;
- 31% das decisões (as acima de 3.000 tokens) consomem 56% do raciocínio;
- ele cresce com o que a nação faz: ~1.400 tokens com 0–1 cartas e ações, ~3.500 com 5 ou mais;
- guerra não muda nada;
- 77% das decisões pensam em inglês.

**Novas tentativas:** 6 por código no texto da carta, 3 por carta repetida, 6 por "nome inventado" (a maioria falso
positivo: "Aksumitas", "Independentes", "Guarda", "Cartago"), 1 por "pano" e 1 por json inválido.

**Cache:**
- O DeepSeek aproveita sim o prefixo da mesma nação de um turno para o outro (o cache bate com o prefixo comum).
- A lista de cartas antigas perdia a primeira linha a cada turno (o teto de 40). Corrigido para cortar em blocos de 20.
- Depois da correção (T93–T97), o cache subiu de 34% para ~36%: o trecho estável depois das regras é curto. O resto do
  dossiê é informação nova de verdade.
- Teto deste formato: ~40%. Separar as notas da memória, que andam em blocos, dos sentimentos daria mais 3–5 pontos.

**Raciocínio desligado (bancada, 12 cenários × 4):**
- US$ 0,0036 por decisão (−46%);
- às cegas, o `low` ganhou 6 a 2 (4 empates), e os 3 erros reais foram todos sem raciocínio: carta que promete uma coisa e
  ação que faz outra, inglês no meio da carta, ação sem proposta;
- **mantido o `low`.**

Um turno passa a custar **~US$ 0,055 fora do pico e ~US$ 0,11 no pico**. Uma partida de 300 turnos com 16 impérios fica em
~US$ 14 (fora do pico) a ~US$ 28 (no pico); antes eram ~US$ 33–66.

**Qualidade (às cegas, 24 pares base × otimizada):** base 7, otimizada 6, empate 11. Na prática, um empate. O único erro de
verdade, um encontro "que já aconteceu" quando ainda estava no futuro, foi da base. Cartas: mesmo tamanho (64–67 palavras) e
mesma quantidade. As ações caíram um pouco com o "pense pouco" (1,79 → 1,52 por decisão) e voltaram com a reordenação (1,69).

**O que entrou no mod** (compilado, ainda não implantado):
- `LlmClient`:
  - o `reasoning_effort` vai no nível de cima do corpo (DeepSeek);
  - OpenAI e Gemini 3 passam a receber o nível. Antes pensavam no padrão deles, mais caro. O Gemini 2.5 Flash-Lite fica sem o
    nível, porque recebê-lo ligaria o raciocínio.
- `Prompts`: seção "COMO PENSAR" no fim das regras, ainda dentro do prefixo que as nações compartilham.
- `DossierBuilder`:
  - ordem do cache: nomes do jogo → correspondência → memória → povos → interceptadas → nações → Congresso → notícias →
    "TURNO N" → sua nação → conselho → cartas novas → pendências → lembretes;
  - correspondência: as cartas dos últimos 4 turnos com o resumo; as mais velhas numa linha, numa janela que anda em blocos
    de 5 turnos;
  - memória: as notas numa janela que anda em blocos de 8; os sentimentos depois delas;
  - grounding: um nome que chegou em carta recebida pode ser citado de volta, como boato.
- `IaJobs`: a mensagem de "cortada" agora pede para pensar menos, não para encurtar as cartas.
- `PlayerCouncil`: o conselho também recebe a linha "pense pouco". O dossiê dele já sai enxuto e reordenado, porque usa o
  mesmo `DossierBuilder`.
- `IaLog.Prune`: deixa de apagar os turnos "do futuro".

## 1. Para onde vai cada dólar (Francos T100–130, US$ 2,60 em 31 turnos)

| Parte | US$ | % | Barra |
|---|---:|---:|---|
| Raciocínio das decisões que deram certo | 1,117 | 43,0% | `██████████████████████` |
| Raciocínio das decisões **cortadas** (perdido) | 0,578 | 22,2% | `███████████` |
| Entrada sem cache (dossiê), decisões ok | 0,454 | 17,5% | `█████████` |
| Entrada sem cache, decisões cortadas (perdida) | 0,146 | 5,6% | `███` |
| Resposta útil (json), primeira tentativa | 0,124 | 4,8% | `██` |
| Novas tentativas (tudo) | 0,168 | 6,6% | `███` |
| Entrada em cache | 0,012 | 0,4% | |

Por tipo: **raciocínio 68%**, entrada sem cache 24%, resposta 8%, cache 0,4%. **Cortadas (perdidas): 28%.** Novas tentativas: 6,6%.

O conselho do jogador custa US$ 0,003 (fora do pico) a US$ 0,007 (no pico) por reunião: ~5% do turno. Ele também raciocina
(4–10 mil caracteres de raciocínio para ~2.400 de resposta).

**Pico × fora do pico:** os turnos 100–122 custaram ~US$ 0,06; os turnos 123–130 (jogados de madrugada, no pico) custaram ~US$
0,14. O mesmo jogo custa o dobro dependendo da hora.

### Por chamada (média, Francos)
| | Chamadas | Entrada | Acerto de cache | Saída | Raciocínio | Custo médio |
|---|---:|---:|---:|---:|---:|---:|
| 1ª tentativa | 279 | 15.021 | **25%** | 8.554 | **7.918** | US$ 0,0087 |
| 2ª tentativa | 113 | 15.478 | 96% | 1.464 | 717 | US$ 0,0013 |
| 3ª tentativa | 21 | 16.368 | 95% | 898 | 254 | US$ 0,0010 |

Na Teste16 (16 impérios): entrada de 20,2 mil tokens (59% em cache), saída de 6,4 mil (5,9 mil de raciocínio) e 1,75 chamada por
decisão.

Raciocínio da 1ª tentativa: p10 = 3.800, **mediana = 8.200**, p90 = 12.000 (o teto).

## 2. A entrada, por seção

Média por decisão nos Francos (~2,9 caracteres por token):

| Seção | Caracteres | ~Tokens | Muda a cada turno? |
|---|---:|---:|---|
| Prompt de sistema (regras + formato + persona) | 12.300 | 4.300 | não (só a persona no fim, por nação) |
| **Correspondência recente (resumo)** | **12.400** | **4.300** | pouco (entra 1–3 cartas, sai a mais velha) |
| Nações que você conhece | 6.600 | 2.300 | sim (tropas, pedágios) |
| Sua memória | 5.000 | 1.700 | pouco (notas novas no fim) |
| Sua nação | 1.600 | 550 | sim |
| Cartas que chegaram | 1.500 | 520 | sim |
| Seu conselho | 950 | 330 | sim |
| Povos independentes | 820 | 280 | pouco |
| Nomes do jogo | 710 | 250 | quase nunca |
| Notícias entre outras nações | 610 | 210 | sim |
| Pendências + lembretes + interceptadas | 590 | 200 | sim |

**Por que o cache rende tão pouco (25%):**
- O prompt de sistema é estável. As 9 nações compartilham os primeiros 14.300 caracteres e só a persona difere no fim. É isso
  que cai no cache, e só isso.
- O dossiê começa com **"TURNO 130 — DOSSIÊ DE..."** e logo depois vem **SUA NAÇÃO** (tesouro, exércitos), que muda todo turno.
  O prefixo quebra no primeiro byte do dossiê, e nada dele aproveita o cache do turno anterior.
- As seções que mudam pouco (correspondência, memória, nomes do jogo, povos) estão **no meio e no fim**, depois das que mudam.
- A persona fica no fim do sistema, o que está certo para compartilhar as regras entre nações.

**Ordem proposta** (o mesmo conteúdo, só reordenado): sistema (regras → formato → persona) → **nomes do jogo** → **memória**
(do mais antigo para o mais novo) → **correspondência** (do mais antigo para o mais novo) → povos → nações → notícias → sua
nação → cartas que chegaram → conselho → pendências → lembretes → **"TURNO N"** no fim. Assim, de um turno para o outro, a mesma
nação reaproveita tudo até a primeira carta nova da correspondência.

A correspondência hoje vai do mais antigo para o mais novo, mas a janela "anda": quando a carta mais velha sai, o prefixo quebra
no começo da seção. Para evitar isso, a janela deve andar **em blocos** (por exemplo, renovar a cada 5 turnos), e não carta a
carta.

## 3. A saída

- Resposta final: ~2.070 caracteres (~700 tokens): diário de 308 caracteres, 1,9 carta de 67 palavras, 1,6 ação, 1,8
  sentimentos e 2,3 notas de memória. Está enxuta e dentro das regras (nenhuma carta passou de 150 palavras).
- **O raciocínio é 92% da saída** e sai em inglês, recontando o dossiê ponto por ponto antes de decidir.
- **O experimento natural** compara as decisões que saíram de primeira (raciocínio longo) com as que foram cortadas e refeitas
  (~700 tokens de raciocínio, com o resto da conversa em cache):

| | n | Cartas | Palavras/carta | Ações | Ações substantivas | Sentimentos | Memória | Diário |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Raciocínio longo | 220 | 1,90 | 68 | 1,61 | 1,08 | 1,85 | 2,42 | 316 |
| Raciocínio curto (refeitas) | 59 | 1,78 | 62 | 1,66 | 0,98 | 1,68 | 1,80 | 281 |

Quase o mesmo volume. Mas a refeita não é um teste limpo: o modelo já tinha pensado bastante antes de ser cortado, embora esse
pensamento não volte na conversa. **Volume não é qualidade: só a bancada responde isso.**

## 4. Novas tentativas

- 134 de 413 chamadas (32%) são novas tentativas, mas elas custam pouco (96% de cache e saída curta): 6,6% do gasto.
- Os motivos:
  - **cortada por tamanho: 72** (todas por causa do raciocínio, não das cartas);
  - **"Teodoro" não existe: 39 (em 41 decisões, T100–T130).** É um falsificador inventado por uma nação. Na última tentativa,
    nome desconhecido vira só aviso, e a carta passou. Desde então, o nome circula nas cartas e na memória de todas as nações
    (ele **está** no dossiê). O validador só aceita nomes do jogo (`KnownWords`), então barra cada nação que o cita;
  - carta repetida: 11;
  - nome inventado (Pérsia, Roma, Cartago, Mongol…): ~10;
  - destinatário desconhecido: 5;
  - "pano": 3;
  - ações em situação errada: ~8.
- **A mensagem de correção está errada para o corte:** ela diz "cartas e diário menores", mas o problema é o raciocínio.

## 5. Alavancas, uma por uma

Estimativas com 16 impérios, deepseek-flash e preço fora do pico (no pico, dobre). Base: ~US$ 0,12 por turno.

### 5.1 Raciocínio (a alavanca de verdade)
- **Opções:**
  - **(a) desligado em todas:** saída ~900 tokens. Custo **~US$ 0,03 por turno**.
  - **(b) desligado na rotina e `low` com teto nos turnos difíceis:** guerra declarada ou sofrida, proposta de rendição ou
    tratado esperando resposta, carta do jogador, Congresso, ultimato vencendo. Pelos Francos, isso dá ~20–30% das decisões.
    Custo **~US$ 0,04 por turno**.
  - **(c) `low` com instrução de pensar curto** ("pense em no máximo 10 linhas, em português, sem recontar o dossiê"). O
    deepseek obedece mal a isso, mas vale medir.
- O teto: `max_tokens` inclui o raciocínio. Hoje, com 12 mil, quem estoura perde tudo. Com o raciocínio controlado, o teto pode
  cair para ~3 mil, e um corte vira raro e barato.
- **Risco:** é a única alavanca grande com risco real para a qualidade (coerência de longo prazo, blefe calculado, uso de
  códigos). Por isso vai para a bancada antes de tudo.
- **Recomendação: (b)**, com (a) como modo "econômico" no `.cfg`.

### 5.2 Respostas cortadas
- Vem de graça com a 5.1.
- Independente dela: trocar a mensagem de correção por "pense menos e responda direto" e, na nova tentativa, **mandar o
  raciocínio desligado**. A 2ª tentativa já mostra que funciona com pouco raciocínio.

### 5.3 Cache
- A reordenação da §2. Nenhum risco: o mesmo texto em outra ordem. Ainda assim, passa pela bancada, porque a ordem pode mudar o
  foco do modelo.
- Ganho estimado: o acerto vai de ~25–59% para ~70–80%. Isso corta a parte "entrada sem cache", que hoje é 24% do gasto, para
  menos da metade.
- Detalhes por provedor (o automático do DeepSeek, OpenAI e Gemini; o `cache_control` do Anthropic e do OpenRouter) estão em
  [§7](#7-provedores-preços-e-regras-de-cache-2026).

### 5.4 Dossiê mais enxuto
- **Correspondência:**
  - as cartas que a nação **já respondeu** viram uma linha ("T118 → E6: nega patrocinar Zavijava");
  - as que ainda importam (promessas, ameaças, ultimatos) ficam inteiras;
  - a janela anda em blocos.
  - Ganho: ~2–3 mil tokens por decisão.
- **Nações:**
  - detalhe completo para vizinhos, inimigos, aliados e quem escreveu;
  - o resto numa linha.
  - Ganho: ~1 mil tokens.
- **Sentimentos na memória:** a seção repete os nomes completos dos líderes a cada linha. Usar o código E# com um glossário
  no início economiza ~30% dela.
- **Grounding:** os códigos e nomes reais continuam. O que sai são repetições e prosa.

### 5.5 Memória em camadas
Hoje a memória tem ~1.700 tokens e cresce com o jogo. Proposta: as últimas 10 notas por nação ficam inteiras; as mais velhas
viram um resumo que a própria decisão reescreve a cada ~10 turnos (um campo opcional no json). O ganho cresce com a partida, e
não é grande no T130. Fica para depois da bancada.

### 5.6 Pensar quando importa
- **Não recomendado agora.** Todas as 279 decisões tinham carta nova chegando, e 272 terminaram com carta ou ação real.
- Uma versão segura: nações **sem nenhum contato** (começo de jogo) pensam a cada 2–3 turnos. O ganho só aparece no início da
  partida.

### 5.7 Modelos em camadas
- Equivale à 5.1(b), só que usando um modelo melhor nos turnos difíceis em vez de mais raciocínio.
- Vale comparar na bancada:
  - deepseek-flash sem raciocínio;
  - GLM-4.7-Flash (grátis);
  - Gemini 2.5 Flash-Lite;
  - gpt-5-nano.
- Preços na §7.

### 5.8 Saída menor
A resposta já é enxuta (8% do gasto). Encurtar chaves do json economizaria <2%, e não vale o risco de confundir o modelo.
**Saída estruturada (json schema)** zera os erros de formato onde o provedor aceita, mas os erros de hoje são de conteúdo
(grounding), não de formato.

### 5.9 Conselho do jogador
- **Raciocínio:** desligado também (ou o mesmo esquema da 5.1). O ganho é de 60–70% de ~5% do turno.
- **Prefixo:** a reunião usa um prompt de sistema próprio, então não cai no cache da decisão da nação. Compartilhar o prefixo
  daria pouco, porque o jogador não tem decisão da nação.

### 5.10 Novas tentativas
- O caso "Teodoro" é uma decisão de design (§9.1), não só de custo. São duas saídas:
  - **(a) mais rígida:** na última tentativa, a carta com nome inventado **cai** em vez de passar com aviso. Assim a invenção
    nunca entra no mundo;
  - **(b) mais livre:** um nome que já circula em cartas recebidas vira "boato conhecido" e pode ser citado.

  Hoje acontece o pior dos dois: a invenção entra e depois cobra nova tentativa de todo mundo, 30 turnos seguidos.
- O corte por tamanho já está coberto pela 5.2.

### 5.11 Outras coisas que os dados mostraram
- **`IaLog.Prune` apagou os logs da Teste16** enquanto eu lia: carregar um save mais antigo apaga os turnos "do futuro". Para a
  bancada, os logs agora são copiados para `_Modding\tools\ia-bench\corpus\`. Sugestão: o Prune só apaga os turnos velhos, não
  os "do futuro".
- **Horário:** jogar fora do pico do DeepSeek já corta pela metade. A tela de provedores poderia mostrar "agora: preço cheio /
  meio preço".

## 6. Projeção (16 impérios, por turno)

| Cenário | Fora do pico | No pico |
|---|---:|---:|
| Hoje | ~US$ 0,12 | ~US$ 0,22 |
| 5.1(b) raciocínio só nos turnos difíceis + 5.2 | ~US$ 0,04 | ~US$ 0,08 |
| + 5.3 cache | ~US$ 0,03 | ~US$ 0,06 |
| + 5.4 dossiê enxuto + 5.10 + conselho | **~US$ 0,02** | **~US$ 0,04** |
| 5.1(a) raciocínio desligado + tudo acima | ~US$ 0,015 | ~US$ 0,03 |

Uma partida de 300 turnos sairia por US$ 6–12 em vez de US$ 36–66.

## 7. Provedores: preços e regras de cache (2026)

Pesquisa completa, com um link para cada afirmação: [`research\cache-raciocinio-2026.md`](../research/cache-raciocinio-2026.md).
Preços em US$ por milhão de tokens (entrada / cache / saída). "(N)" = não confirmado.

| Modelo | Preço | Cache | Raciocínio | Custo/turno estimado (16, fora do pico) |
|---|---|---|---|---:|
| **deepseek-flash** (direto) | 0,30 / 0,006 / 1,20; metade fora do pico (01–04 e 06–10 UTC, seg–sex) | automático, por prefixo, dura "de horas a dias"; granularidade e mínimo não publicados (N) | `none`/`low`/`high` (padrão)/`max`. **Não há orçamento de tokens de raciocínio.** `max_tokens` inclui o raciocínio | hoje ~0,11; sem raciocínio ~0,03 |
| deepseek v4.1 flash (OpenRouter) | igual ao direto; DeepInfra 0,14 / 0,0042 / 0,42 (fp8) | automático | `reasoning.effort` (o `max_tokens` vira esforço) | ~igual |
| gpt-6-luna | 0,10 / 0,01 / 0,50 | automático, mínimo 1.024, `prompt_cache_key` | esforço `none` | ~0,024 |
| gpt-5-nano | 0,05 / 0,005 / 0,40 | idem | `minimal` | ~0,02 (N) |
| Gemini 2.5 Flash-Lite | 0,10 / 0,01 / 0,40 | implícito | `thinkingBudget=0` desliga (já vem desligado) | ~0,02 |
| Gemini 3.1 Flash-Lite | 0,25 / 0,025 / 1,50 | implícito | não desliga de verdade (`minimal`) | ~0,05 |
| GLM-4.7-Flash (Z.ai) | **grátis** (~1 pedido simultâneo, N) | implícito | `thinking.type:"disabled"` | 0 |
| Claude Haiku 4.5 | 1 / 0,10 (escrita 1,25) / 5 | `cache_control`, mínimo 4.096 | — | ~0,15 |

- **Flex e economy:** o Flex do Gemini custa metade, mas demora de 1 a 15 minutos. O Flex da OpenAI também custa metade, com
  latência não publicada e com risco de 429. Nenhum dos dois serve para o turno.
- **Pedir "pense pouco" no prompt** é ignorado ou piora a precisão (arxiv 2511.04108). O controle tem que ser pelo parâmetro.

### ⚠️ Provável bug: o `low` pode nunca ter funcionado
- **O que o mod manda:** `thinking: {type: "enabled", reasoning_effort: "low"}`, com o esforço **dentro** de `thinking`
  ([LlmClient.cs:131-133](../src/CurrencyMod/Diplomacia/Llm/LlmClient.cs#L131)).
- **O que diz o guia oficial do DeepSeek:** o `reasoning_effort` vai **no nível de cima** do corpo, e o padrão, quando ele
  falta, é `high`.
- **A consequência:** se a API ignora o campo aninhado, **todas as chamadas rodaram em `high`**. Isso bate com a mediana de 8.200
  tokens de raciocínio. Há relatos (N) de que, no V4.1 Flash, `low` quase equivale a desligar.
- **A prova:** 2 chamadas iguais, uma com o campo aninhado e outra no nível de cima, comparando `reasoning_tokens`. Custa
  ~US$ 0,02.
- **Se confirmar,** a correção é de uma linha e pode, sozinha, levar a ~US$ 0,04 por turno com o mesmo modelo e o mesmo `low`
  que já foram escolhidos.

## 8. Plano em ordem

0. **Provar o bug do `reasoning_effort`** (~US$ 0,02). Se ele se confirmar, corrigir. A partir daí, "hoje" passa a ser o `low`
   de verdade, e a linha de base é refeita com ele.
1. **Bancada + linha de base** (fase 4): ~25 cenários fixos tirados do corpus (guerra, rendição, Congresso, carta do jogador,
   traição/forjador, pedágio, conselho, rotina), rodados com a configuração de hoje.
2. **5.2 + 5.10** (nenhum risco): mensagem de correção, nova tentativa sem raciocínio, "Teodoro".
3. **5.3 cache** (reordenar o dossiê), medido na bancada.
4. **5.1 raciocínio:** (a) e (b) contra a linha de base, com avaliação às cegas e uma amostra para o lucas.
5. **5.4 dossiê enxuto**, medido na bancada.
6. **Conselho** com o mesmo esquema.
7. Validação no jogo com 16 impérios (10 turnos antes e 10 depois) e a entrega.

## 9. Decisões que ficam com o lucas

0. **Posso gastar ~US$ 0,02 agora** para provar o bug do `reasoning_effort` (2 chamadas pela bancada, fora do jogo)?
1. **A meta:** US$ 0,04 por turno no pico (~US$ 0,02 fora) serve?
2. **O orçamento de teste:** quanto posso gastar em chamadas na bancada? Sugestão: **até US$ 2–3**, com placar. A linha de base
   com 25 cenários × 3 variantes custa ~US$ 0,5.
3. **O raciocínio:** topa testar "desligado na rotina, ligado nos turnos difíceis"? E, se a bancada empatar, prefere
   desligado em tudo (mais barato) ou o misto (mais seguro)?
4. **O modelo padrão:** testo também alternativas (GLM grátis, Gemini Flash-Lite, gpt-nano) ou fico só no deepseek-flash
   nesta rodada?
5. **"Teodoro":** prefere (a) cortar a invenção na origem ou (b) aceitar boato que já circula (§5.10)?
6. **O `IaLog.Prune`:** pode deixar de apagar os turnos "do futuro" ao carregar um save antigo?
