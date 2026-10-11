# Economia do save "NAO DELETE PELO AMOR DE DEUS" (Máuria, turno 81)

Análise só de leitura, feita numa cópia do save (md5 fb7625a552690c31ebe04f5d9d25be4f, o mesmo do original).

**Fontes**
- `CurrencyMod.json`, de dentro do .ctr (que é um zip comum): câmbio, inflação e juros de cada moeda dos turnos 21 a 80, e o livro de dinheiro do jogador (vendas e compras de recursos, manutenção de rotas e pedágios) dos turnos 22 a 81.
- `DiplomaciaIA.json`: alianças, ações e cartas.
- Logs da IA em `BepInEx\DiplomaciaIA\logs\2f44534a\` (pasta com o mesmo GUID do save): o dossiê "SUA NAÇÃO" de cada império em cada turno, de 51 a 81. Cada um traz tesouro, renda por turno, influência, ciência, estabilidade, população, tecnologias e era. É a única série de **tesouro e renda** que existe, porque o JSON do mod não guarda o saldo de dinheiro por turno.
- As tabelas extraídas estão em `series_*.csv`, na mesma pasta deste relatório.

Legenda: **[DADO]** quer dizer confirmado pelos números; **[CÓDIGO]** quer dizer confirmado lendo o código; **[HIPÓTESE]** é inferência que ainda não foi verificada.

---

## 1. Linha do tempo

### 1.1 Tesouro do jogador (E0, Máuria, moeda Real) [DADO]

| Turno | Tesouro (R$) | Renda/turno | Ciência | Pop. | Tecnologias | Influência (estoque) |
|---|---|---|---|---|---|---|
| 51 | 36 | −40 | 125 | 13 | 13 | 46 |
| 58 | 429 | −16 | 160 | 16 | 18 | 229 |
| 63 | **−248** | −60 | 231 | 15 | 21 | 227 |
| 66 | 188 | −64 | 212 | 13 | 24 | 505 |
| 72 | 193 | −38 | 248 | 14 | 27 | 985 |
| 73 | 324 | −45 | 250 | 16 | 28 | 1.081 |
| 74 | 740 | −38 | 251 | 15 | 28 | 1.205 |
| **75** | **1.867** | −22 | 265 | 17 | 29 | 1.333 |
| 80 | 2.613 | −4 | 285 | 23 | 32 | 2.148 |
| 81 | 2.598 | −14 | 300 | 25 | 32 | 2.296 |

- **A aceleração começa no turno 73.** De 193 R$ (T72) o tesouro vai a 1.867 R$ (T75): 9,7 vezes em 3 turnos. Só no T74→T75 entraram +1.127 R$ de uma vez. Depois o ritmo cai para uns +7% por turno até 2.613 R$ (T80).
- **A renda por turno ficou negativa o tempo todo** (de −68 a +5). O tesouro não cresceu por renda: cresceu por **pagamentos de uma vez só**.
- No mesmo período, a influência do jogador sobe sem parar (+148/turno no T80) e a ciência vai de 125 para 300.

### 1.2 Câmbio, inflação e juros do Real [DADO]

| Turno | Câmbio do Real | Inflação/turno | Juros/turno | Mediana do câmbio das outras moedas |
|---|---|---|---|---|
| 21 | 1,69 | −0,14% | 0,32% | 0,77 |
| 37 | 1,42 (mínimo) | −0,05% | 0,13% | 1,17 |
| 50 | 1,78 | 0,35% | 0,20% | 1,11 |
| 63 | 2,36 | **1,70%** | 0,30% | 0,94 |
| 73 | 2,80 | 0,06% | 0,02% | 0,88 |
| 80 | **3,14** | 0,27% | 0,33% | 0,92 |

- O Real sobe +121% do T37 ao T80, cerca de +1,9% por turno. Os trechos mais rápidos são T49–58 (+3,6% por turno) e T73–80. O teto do câmbio é 4,0 (`EconomyConfig.cs:113`).
- A única alta de inflação do Real (1,70% no T63) coincide com o tesouro negativo nos T59–64. O mod puniu o jogador quando ele ficou endividado.
- Juros quase zero e CreditFactor de 1,03 dão ao jogador +3% de produção pelo crédito. É um efeito pequeno.

### 1.3 O jogador contra as IAs [DADO]

No T80:

| | Jogador | Mediana das IAs | Melhor IA |
|---|---|---|---|
| Era | **Clássica** | Antiga (13 de 15) | E10 e E13 entraram na Clássica |
| PIB do mod | 707 | ~157 | E10: 592 |
| Força | 818 | ~143 | E10: 617 |
| Ciência/turno | 285 | 21 | 82 |
| População | 23 | 4 | 14 |
| Tecnologias | 32 | 14 | 19 |
| Tesouro | 2.613 R$ | 78 | 670 (E10) |
| Índice de preços | 1,35 | ~2,05 | — |

- Das 15 IAs, 6 a 8 ficam **endividadas** (tesouro negativo) em cada turno. Somando todas as IAs, o tesouro ficou entre −700 e +900 quase o turno todo.
- A inflação de dívida está no máximo (2,5% por turno) para E1 e E11, em 1,6% para E13 e em 1,3% para E8. A inflação média dos T60–80 fica acima de 1,3% para E1, E2, E5, E6, E7, E11, E13 e E15, contra 0,5% do jogador.
- A estabilidade de várias IAs é muito baixa: E5 16%, E6 20%, E13 13%, E15 15%.
- Quatro IAs têm influência negativa em algum momento: E1 chegou a −889 com −170 por turno; E6 a −563, E13 a −457 e E15 a −411.

---

## 2. Causas: de onde veio o dinheiro

### 2.1 O tesouro fecha com a venda de licenças de recursos [DADO]

Comparei, turno a turno, a variação do tesouro menos a renda com o que o livro do mod registra:

| Turno | Δ tesouro − renda | Vendas de recursos registradas (R$) | Compras do jogador |
|---|---|---|---|
| 52 | +421 | +421 | — |
| 58 | −469 | — | −429 (comprou 5 licenças) |
| 64 | +320 | +309 | — |
| 65 | +176 | +186 | — |
| 73 | +461 | +448 | — |
| **74** | **+1.165** | **+1.150** (16 vendas) | — |
| 75 | +290 | +272 | — |
| 78 | +221 | +245 | — |
| 79 | +202 | +193 | — |

- **O que sobra depois da renda é praticamente igual às vendas de licenças de recursos** (setup cost pago pelas IAs), com diferença de até ~3% por turno.
- Do T51 ao T81 as vendas somam ≈ 3.840 R$. A renda acumulada foi ≈ −1.150 R$ e as compras −429 R$.
- No T74, quem comprou foi E8 (8 compras), E11 (4), E1, E6 e E3.
- Quatro dos cinco maiores compradores (E8, E11, E6, E3) são **aliados** do jogador. O jogador tem 6 alianças: E3, E6, E7, E8, E10 e E11.

O que **não** pesou:

| Fonte | Valor | Situação |
|---|---|---|
| Juros sobre o saldo | `LastInterestFlow` = **0,17 R$/turno** | [DADO] |
| Pedágios | 3 a 6 R$/turno, só até o T75 | [DADO] |
| Presentes e transferências (Kind 3) | Nenhum registrado | [DADO] |

Os juros são pequenos por causa da conta (`EconomySimulation.cs:532-547`). O juro real é 0,33% − 0,27% = 0,06%, e o teto suave de 15 turnos × renda de no mínimo 20 deixa só ~269 R$ rendendo.

**Não há juros compostos nem contagem em dobro.** As entradas repetidas no livro (por exemplo, E8 pagando 151 quatro vezes no T74) batem com a queda real do tesouro de E8 (1.598 → 175). São compras separadas, não um bug de registro. [DADO]

### 2.2 Por que tantas compras de uma vez (T73–75)

- Várias IAs receberam dinheiro de repente **no mesmo momento** [DADO]:
  - E14: de 78 para 3.471 no T74 e de volta para 91 no T75.
  - E8: de 311 para 1.598 no T74 e para 175 no T75.
  - E1 teve renda de 394 no T70.
  - E15 teve renda de 233 no T77.
- A IA nativa gasta essas sobras comprando acesso a recursos. O jogador é o maior fornecedor de luxo do mundo: no T80 ele tem 13 tipos de luxo, e Café, Pedra Preciosa, Sal, Obsidiana, Pérolas e Âmbar Cinza aparecem como "eles compram de você" para quase todos os parceiros.
- **[HIPÓTESE]** De onde vêm essas sobras: guerra E7×E14, saque, eventos ou pagamentos entre IAs. O livro do mod só grava movimentos com humano (`EconomySimulation.cs:392-404`), então não dá para confirmar.
- **[HIPÓTESE] Revenda.** No T80 o jogador compra Pérolas, Cobre e Ébano de E8, E10 e E5 e também vende Pérolas, Cobre e Ébano para E3, E5, E8 e E11. Se esses itens forem acessos comprados e não jazidas próprias, há uma **arbitragem**: o jogador compra barato pelo Real forte e revende pelo preço cheio. O save não guarda a origem dos acessos (o campo `History[].Resources` não existe neste save). Dá para conferir na aba Recursos do Banco Central (jazida, comprado ou vendido).

### 2.3 Como o mod converte a venda [CÓDIGO + DADO]

**Preço que a IA paga** (`TransferConversion.cs:235-237`): preço do jogo × câmbio do vendedor ÷ câmbio do comprador.

**O que o vendedor recebe** (`TransferConversion.cs:181,189`): o valor pago × câmbio do comprador ÷ câmbio do vendedor.

Conferido no livro: Ganho ÷ Pago é igual ao câmbio do comprador ÷ câmbio do jogador, com 3 casas, em todos os turnos verificados.

Resultado:
- **O jogador recebe exatamente o preço que receberia sem o mod.** A conversão não engordou as vendas dele.
- **As IAs pagam, em moedas delas, 2,3 vezes mais na média**: 11.171 unidades pagas por 4.789 R$ de preço do jogo.

| Comprador | Quanto pagou a mais |
|---|---|
| E8 | 1,7× |
| E2, E11, E6 | 2,5× |
| E1 | 3,0× |
| E15 | **4,0×** |

Em valor de mundo a troca é justa. Mas a renda das IAs é em moedas "nominais" do jogo, que não acompanham o câmbio. Na prática, cada licença custa para elas 2 a 4 turnos de renda a mais.

- O mesmo vale ao contrário: **as importações do jogador ficam baratas**. Ele paga o preço do jogo × câmbio do vendedor ÷ 3,1 (por exemplo, 0,3× diante do Marco).
- `LastConversionGain` = −162,6 no T80 é exatamente o ajuste da compra de E6 no T81 (paga 215, o jogador recebe 52,4), o que confirma o mecanismo. [DADO]

### 2.4 Por que o Real só sobe [CÓDIGO + DADO]

1. **PIB** = renda + produção + ciência + influência (`EconomySimulation.cs:261`). O jogador tem 707 contra uma mediana de ~157, puxado por ciência (285) e influência (148). O dinheiro entra como zero, porque a renda dele é negativa.
2. **Câmbio-alvo** = (força relativa)^0,5 ÷ preço relativo (`EconomySimulation.cs:360-364`). Com o jogador 4× acima da força média, o primeiro fator dá ×2,0.
3. **Índice de preços:** o do jogador é 1,35. O das IAs endividadas sobe 1–2,5% por turno pela inflação de dívida (`EconomySimulation.cs:496-500`) e chega a 2,0–2,7. Isso empurra o Real para cima e as moedas delas para baixo.
4. **Normalização pela média geométrica** (`EconomySimulation.cs:369-377`): quanto mais moedas afundam, mais o Real sobe em termos relativos.

### 2.5 Mecanismos do mod que alimentam a disparada (sem bug de conta)

1. **A IA nativa não reage ao preço.** Comprar de quem tem moeda forte custa 2–4× mais, e ela compra assim mesmo sempre que tem dinheiro. Exemplo: E15 pagou 680 Marcos (4×) no T78 vindo de dívida. [DADO]
   - Na economia real, moeda forte deixa a exportação mais cara e o exportador perde clientes. **Aqui o jogador não perde nada**: recebe o preço cheio e os clientes continuam. A moeda forte não tem lado ruim.
2. **Armadilha de pobreza das IAs.** O caminho é sempre o mesmo:
   - tesouro negativo;
   - +1% de inflação, mais 0,3% por turno seguido em déficit (até 2,5%);
   - preços sobem;
   - a moeda cai e tudo de fora fica mais caro;
   - a estabilidade cai: inflação acima de 0,75% baixa a meta das cidades (`EconomySimulation.cs:588-593`). Com 2,3% de inflação, E15 perde uns −14 de meta; com 2,5%, E11 perde uns −16;
   - mais déficit, e o ciclo recomeça.

   Em 43% dos turnos de IA o tesouro estava negativo. [DADO + CÓDIGO]
3. **Brecha do entesouramento.** A inflação por dinheiro parado só vale se a renda for positiva (`EconomySimulation.cs:501`). O jogador tem 2.600 R$ parados com renda de −4/turno, então `InflationFromHoarding` = 0. Com renda positiva, seriam 520 turnos de renda e a inflação bateria no teto de 1% por turno. Em vez disso, a inflação do jogador ficou em 0,27%, e é ela que segura o câmbio lá em cima. [DADO + CÓDIGO]
4. **[HIPÓTESE, menor]** A manutenção por turno das rotas (Kind 1) não passa pela conversão, porque `TransferMethods` não inclui a cobrança de manutenção. Se o jogo realmente paga essa manutenção ao vendedor, como diz o comentário em `CurrencyState.cs:19`, ela troca de mãos 1:1 entre moedas de valores diferentes. Para o jogador o saldo é quase nulo: recebe ~240 e paga ~232 por turno.

---

## 3. Por que as IAs não seguraram o jogador

1. **Elas já começaram muito menores** [DADO]. No T51 a mediana das IAs tinha população 4 e 7 tecnologias, contra 13 e 13 do jogador. No T81 era população 4 e 14 tecnologias, contra 25 e 32, e só 2 IAs saíram da Era Antiga.
   - Nos outros jogos com log a IA era bem mais forte: pop. média 15–19, estabilidade média 67–75 e 22–38% de turnos em dívida. Neste save: pop. 5, estabilidade 54 e 43% de turnos em dívida.
   - **[HIPÓTESE]** O mundo fraco vem das configurações da partida (mapa pequeno para 16 impérios, dificuldade), que eu não consegui ler no binário. As penalidades do mod (inflação de dívida, estabilidade) agravam.
2. **Ninguém tentou conter, e os maiores compradores eram aliados** [DADO]. O jogador estava em paz com todos e aliado a 6.
   - Regras de comércio contra o jogador: só pedágio de E1 em 2 postos e bloqueio de E14 em 1. O jogador cobra pedágio de E14 em 8 postos.
   - As IAs de linguagem reagiram só com desconfiança (E4, E5 e E13 nos T77–78, por causa do "bloco de seis"). Nenhuma embargou nem deixou de comprar.
3. **O mod não tem nenhum mecanismo contra um líder disparado** [CÓDIGO].
   - A regra de pedágio da IA (`Trade/TradeAi.cs:145-146`) só taxa o "rival muito mais rico" se já houver hostilidade, e nunca bloqueia compras de recursos.
   - Não há elasticidade de preço, nem limite de gasto da IA, nem reação de coalizão.
   - O dossiê da IA de linguagem não diz quanto ela paga a mais por importar do jogador.
4. **O mod deixou as IAs mais pobres** [DADO + CÓDIGO]: elas pagaram 2,3× por licença, e a dívida trouxe inflação e menos estabilidade.

---

## 4. Veredito

- **Não é bug de contabilidade** [DADO]. O tesouro fecha turno a turno com o livro, os juros são desprezíveis e não há registro em dobro.
- **A maior parte é dinâmica normal de Humankind**:
  - um império 3–6× maior que todos;
  - monopólio de luxos;
  - IAs que gastam toda sobra comprando acesso a recursos;
  - nos outros saves também apareceram líderes disparados, mas eram IAs, com renda de +1.039 e +1.371 por turno.

  Respondendo direto: "só clicar e explodir" aconteceu porque **as IAs vieram comprar de você**. Você não fez nada de especial; foi o mercado que veio até você.
- **Há, sim, problema de balanceamento do mod.** Ele amplifica a diferença e não tem freio para nenhum dos dois lados:
  - moeda forte só traz vantagem;
  - a dívida das IAs vira uma armadilha;
  - a brecha do entesouramento poupa o jogador rico de inflação.

### Correções sugeridas (por impacto)

1. **Repasse parcial do câmbio no preço dos recursos** (`TransferConversion.cs:237`). Usar câmbio^0,5 em vez do câmbio inteiro, por exemplo, mantendo a conversão cheia no recebimento.
   - Quem tem moeda forte passa a receber menos que o preço do jogo, e a IA paga ~√ do que paga hoje (1,5× em vez de 2,3×).
   - Isso cria o lado ruim natural da moeda forte e corta pela metade o dreno das IAs.
   - Alternativa simples: limitar o câmbio usado no preço entre 0,5 e 2.
2. **Freio de gasto na IA nativa** (patch na compra de acesso):
   - não comprar se o tesouro depois da compra ficar negativo;
   - não comprar se o preço convertido passar de N turnos de renda (por exemplo, 3).

   Ataca direto a compra a 4× vinda de dívida.
3. **Fechar a brecha do entesouramento** (`EconomySimulation.cs:501-511`). Medir os "turnos de renda" contra uma base que não some quando a renda fica negativa, por exemplo `max(5, SmoothedIncome, 0,1×PIB)`.
   - Para o jogador seriam ~37 turnos, o que dá +0,5% de inflação por turno e esfria o Real.
4. **Suavizar a armadilha da dívida para IAs**:
   - `DeficitPorTurno` de 0,003 para 0,0015, ou teto de 1,5%;
   - ou um "socorro": IA endividada há mais de 5 turnos tem os juros automáticos limitados e a perda de estabilidade pela inflação reduzida à metade.
5. **Câmbio menos explosivo**:
   - `Cambio.Elasticidade` de 0,5 para 0,35;
   - `Cambio.Maximo` de 4,0 para 2,5;
   - opcional: normalizar pela mediana em vez da média geométrica.

   Reduz o quanto as IAs pagam a mais. Não muda as vendas do jogador.
6. **Reação ao líder disparado**:
   - em `TradeAi`, quando a força de alguém passar de 3× a média, liberar pedágio sem precisar de hostilidade;
   - no dossiê da IA de linguagem, mostrar "você paga ×N para importar de E0" e o tamanho relativo do líder.

   É liberdade para a IA decidir, não regra forçada.
7. **[Verificar antes]** Se a revenda de acessos comprados for permitida e estiver gerando arbitragem, impedir a venda de acesso que foi comprado.
8. **Para diagnosticar melhor da próxima vez**: gravar no `HistoryPoint` o tesouro, a renda e a soma de vendas e compras de cada império a cada turno. Hoje só os logs da IA têm isso, e só para os últimos 30 turnos.

---

## 5. Outros jogos com log (resumo) [DADO]

| Pasta | Turnos | Jogador | IAs |
|---|---|---|---|
| `b57b0e07` | 83–102 | Pequeno (pop. 5–7, ciência 8–17); tesouro 664 → 1.695 com renda de +27 a +52 | Fortes. Líderes: E12 com +1.039/turno e E3 (Poloneses, Era Moderna) com +1.371/turno |
| `b44f3e76` | 100–130 | Tesouro negativo (−275 a −189) | Líder disparado E1: 4.818 de tesouro, pop. 90 |
| `ae4a6353` | 1–4 | Sem dados úteis | — |

Nesses jogos o líder disparado é uma IA, e a disparada vem de **renda por turno**. No save do Lucas, a disparada vem de **vendas de licenças com renda negativa**.
