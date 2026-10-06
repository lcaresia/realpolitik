# Proposta: ciclo produção → inflação → juros (para discutir antes de implementar)

Pedido do usuário (2026-10-04):

> Produção empurra inflação pra baixo · Inflação empurra juros pra cima · Juros empurra produção pra baixo

A meta é uma simplificação crível da realidade, em que ninguém escala ao infinito. Focar em finanças (juros altos)
ou em indústria (juros baixos) deve cobrar um preço do outro lado. E o jogador precisa enxergar se está indo bem ou
mal, e o que fazer.

## 1. Como o modelo é hoje (`EconomySimulation.cs`, `EconomyConfig.cs`)

| Elo | Hoje | Situação |
|---|---|---|
| Produção → inflação | não existe: a produção só entra no "PIB", que mexe no câmbio | **falta** |
| Inflação → juros | regra de Taylor nos juros automáticos (IA e jogador no automático): 1% + 1,5 × (inflação − 0,5%), de 0% a 5% | existe |
| Juros → produção | não existe: juros altos só cortam a renda em dinheiro (até −25%) e derrubam a inflação | **falta** |

Outras peças:
- **Inflação-alvo:** base 0,4% por turno, mais dívida (+1% e +0,3% por turno seguido), mais dinheiro parado (mais
  de 10 turnos de renda: até +1%), mais estabilidade baixa, menos juros acima do neutro (× 0,6).
- **Juros sobre o saldo:** saldo × (juros − inflação) por turno, até ±8%.
- **Câmbio:** força econômica relativa^0,5 dividida pelos preços relativos.

### Brecha encontrada: o caminho financeiro escala sem limite

Com juros travados em 5%, a inflação-alvo fica negativa (0,4% − 4 × 0,6 = −2%, ou −1% com o entesouramento). Os
juros reais ficam perto de 6% por turno, e o saldo **dobra a cada ~12 turnos, para sempre**. As IAs não caem nisso
porque usam a regra automática; o jogador no modo manual pode cair.

## 2. Auditoria da partida de 16 impérios (turno 64, só IA nativa)

`jogo economia` (grava `dev\out\economia_T64.csv`):
- nenhum valor inválido, câmbio, inflação ou juros no limite, nem salto de câmbio;
- câmbio de 0,32 a 2,44; inflação de 0,21% a 1,69% por turno; índice de preços de 1,26 a 1,95;
- juros do saldo desprezíveis (de −5 a +2 por turno): as IAs guardam pouco dinheiro;
- 3 impérios ainda são tribos do Neolítico no turno 64 (estabilidade 0%). Falta espaço no mapa Normal com 16
  impérios: é assunto do MoreEmpires, não da economia;
- os Romanos têm 5× a ciência mediana: forte, não absurdo.

## 3. Proposta

Taxas por turno; neutro = 1%.

1. **Juros → produção (crédito).** Produção das cidades × (1 − 5 × (juros − 1%)): −5% por ponto acima do neutro
   (−20% com juros de 5%) e até +5% com juros a 0%. Substitui o corte direto na renda em dinheiro.
2. **Produção → inflação (cobertura).** Compara a produção com o dinheiro novo de cada turno (produção ÷ renda) e
   com a média do mundo: inflação −0,4% × ln(cobertura relativa). Quem produz o dobro da média por moeda ganha
   −0,28%; quem produz metade, +0,28%. Indústria forte segura a inflação; dinheiro sem produção atrás vira inflação.
3. **Inflação → juros.** Continua a regra de Taylor. Para a IA, entra também o hiato de produção (corta juros quando
   a produção cai), para ela não afundar a própria economia.
4. **Trava contra o infinito.** Os juros do saldo rendem no máximo 25% da produção do império por turno. Capital
   precisa de economia real para render.
5. **Juros → inflação direto.** O peso cai de 0,6 para 0,3: o efeito principal passa a ser pela produção.

### Simulação (60 turnos, economia crescendo 1,5% por turno, mesma partida nas 3 estratégias)

| Modelo | Estratégia | Produção no T60 | Produção acumulada | Saldo | Inflação | Preços | Juros do saldo por turno |
|---|---|---|---|---|---|---|---|
| atual | automático | 366 | 14.649 | 8.108 | 0,83% | 1,45 | 50 |
| atual | financeiro (5%) | 366 | 14.649 | **49.502** | −1% | 0,46 | **2.794** |
| atual | industrial (0%) | 366 | 14.649 | 5.376 | 1,41% | 2,03 | −74 |
| proposta | automático | 355 | 14.458 | 8.218 | 0,94% | 1,49 | 53 |
| proposta | financeiro (5%) | 293 | 11.719 | 9.909 | 0,19% | 0,87 | 73 (no teto) |
| proposta | industrial (0%) | 385 | 15.381 | 5.509 | 1,10% | 1,69 | −60 |

- Hoje o financeiro ganha 6× mais dinheiro sem custo nenhum, e o industrial só perde.
- Na proposta, o financeiro tem +20% de saldo, moeda forte e preços baixos, mas 19% menos produção. O industrial tem
  +6% de produção, mas moeda mais fraca e menos poupança. O automático fica no meio.

## 4. Como deixar claro no jogo

- **Aba "Ciclo" no Banco Central:** os três elos com o efeito atual de cada um, setas de tendência e um diagnóstico
  de uma frase com o que fazer. Exemplos:
  - "Produção 385/turno, 1,2× a média por moeda: inflação −0,07%."
  - "Inflação 1,1% (meta 0,5%): os juros automáticos subiriam para 1,9%."
  - "Juros 0%: produção +5% nas cidades."
- **Inflação decomposta** (base, dívida, dinheiro parado, cobertura, estabilidade, juros), como nos tooltips do
  próprio jogo.
- **Linha no tooltip de produção das cidades:** "Crédito (juros 0%): +5%".
- **Avisos quando passar de um limite:** inflação acima de 2% por turno, juros no teto, produção −10% por juros.

## 5. Decisões do usuário (2026-10-04) e pesquisa

- **Juros → produção:** "testa aí". **Inflação → estabilidade:** sim. **Juros altos → menos produção (crédito):**
  sim. **Teto do rendimento e oferta × demanda:** pesquisar.
- **Pesquisa (agente, com fontes):**
  - **Modelo de 3 equações** (Carlin & Soskice) e **teoria quantitativa:** inflação ≈ crescimento do dinheiro −
    crescimento real. O juro age forte e rápido na **demanda** e devagar na **oferta** (investimento).
  - **Achado crítico:** se o juro derrubar só a produção e a produção for o que segura a inflação, subir juros sobe
    a inflação (espiral de estagflação). O efeito do juro na demanda precisa ser maior que o efeito na oferta.
  - **Juros compostos não explodem na vida real:** retorno decrescente do capital (r = αY/K), excesso de poupança
    derruba os juros e há capacidade de absorção. Victoria 3 usa teto *suave* na reserva.
  - **Inflação → instabilidade:** existe e tem limiar. O desemprego pesa mais no bem-estar que a inflação (1,7× a
    5×).
  - **UI dos jogos:** tooltips aninhados com cada termo (Victoria 3), entradas ponderadas (Democracy 4), barras de
    oferta × demanda (Port Royale).

## 6. Modelo v2 (implementado, em teste — `EconomySimulation.cs`)

1. **Oferta × demanda:** pressão = (renda ÷ produção, suavizadas) ÷ referência do próprio império − 1. A
   referência acompanha a razão em ~15 turnos (`AdaptacaoDemanda` 0,15). Inflação + 3% × pressão (±0,6%). A vantagem
   é de ritmo (produção crescendo mais que o dinheiro), não de tamanho.
2. **Inflação → juros:** Taylor 1,5 na inflação + 0,05 × pressão, andando 25% por turno.
3. **Juros → demanda (direto):** −0,3% de inflação por ponto acima do neutro.
4. **Juros → produção (investimento):** alvo = 1 − 5 × (juro real − juro real neutro), de 0,75 a 1,05. A produção
   anda 15% por turno até o alvo (`VelocidadeInvestimento`).
5. **Estabilidade por cidade e por turno:**
   - inflação: −4 por ponto acima de 0,75% e mais −4 por ponto acima de 1,5% (1% → −1; 2% → −7; 3% → −15);
   - desemprego: −20 × (1 − produção do crédito); produção ×0,75 dá −5.
6. **Rendimento do saldo:** juros reais × saldo × C/(saldo + C), com C = 15 turnos de renda. O saldo cresce em linha,
   não em exponencial.

**Correções da revisão de bugs (2026-10-05):**
- **Ordem no fim do turno:** o jogo roda todos os passes de um império antes do próximo (`SandboxState_TurnEnd`). Com
  a conta global só no `CollectMoney` do primeiro império, o império 0 investia a produção e calculava a ordem pública
  com o crédito e a meta do turno anterior, e os outros com os do turno atual. Agora a conta global roda num prefixo do
  passe dos ancilares (`Ancillary.Passes`, antes de todos os impérios). O `CollectMoney` continua como reserva, e o
  `LastProcessedTurn` impede rodar duas vezes.
- **Cidade em modo ciência:** a indústria dela vira ciência (o jogo devolve 0 em `ComputeProductionIncome`) e já entra
  no `ResearchNet`. Somar a produção dela também contava duas vezes (PIB, câmbio e pressão).
- **Juros mudados na janela:** o botão Automático e o controle deslizante agora refazem a tabela de efeitos na hora,
  como o comando `jogo juros`. Antes, a renda da barra do topo e as linhas de juros e crédito mostravam a taxa velha
  até o fim do turno, embora o pagamento já usasse a nova. A tabela também passou a ser publicada dentro do lock.
- **Dinheiro parado com renda zero ou negativa (decidido 2026-10-05):** a parcela é zero. Quem gasta a reserva não está
  entesourando; antes, o piso de renda 5 dava a inflação máxima dessa parcela (+1%/turno). Testado: os Tribo Nômade
  (tesouro 102, renda 0) ficaram com 0,00% (antes ~0,2%). `jogo economia` mostra a parcela na coluna "parado%".

### Testes (2026-10-04, `jogo economia`, `jogo juros`, `jogo espectador`)

- **v1 (cobertura contra a média do mundo), mapa Tiny com 16 impérios, turnos 21–82:**
  - industrial (0%) e financeiro (5%) viraram as maiores potências;
  - o industrial não pagava nada pelo juro zero;
  - a cobertura oscilava quando a renda do turno quase zerava.
- **v2, mapa Huge com 16 impérios, turnos 1–76:**
  - **Primeiro defeito:** a pressão ficou negativa para quase todos, porque no começo do jogo a produção cresce mais
    que o dinheiro para todo mundo. A inflação foi a ~0 e os juros automáticos a 0%.
  - **Correção:** a pressão passou a contar só o desvio contra a mediana do mundo. Depois disso, juros automáticos de
    0,1% a 3,2% e crédito de ×0,90 a ×1,05.
  - **Financeiro (Khmers, 5%):** 2º maior PIB, mesmo com produção ×0,75. Deflação de −0,6%, preços em 0,53, a moeda
    mais forte (3,79) e −5 de estabilidade por turno por desemprego.
  - **Industrial (Celtas, 0%):** produção ×1,05, preços mais altos entre os estados (1,59), meio da tabela.
  - **Ciclo funcionando:** dinheiro 40% acima do ritmo da produção levou os juros automáticos a 3% e cortou a
    produção para ×0,90–0,92 (Assírios, Nascas). Inflação de 2% num endividado tirou −7,6 de estabilidade por turno
    (Micênicos).
  - Nenhum valor absurdo: juros do saldo de no máximo 9 por turno, câmbio de 0,30 a 3,79.
- **Pendente:**
  - ~~a barra da cidade ainda lê a produção sem o crédito~~: conferido em 2026-10-05, a barra e a lista de cidades já
    mostram o crédito (`SettlementInfo.ProductionNet` vem de `ComputeProductionIncome`, que tem o fator). Só a
    estimativa de tempo de construção da IA nativa ignora o crédito (efeito pequeno);
  - o jogador escolher uma "postura" em vez de travar o juro (sugestão da pesquisa).

## 7. Situação das decisões

1. **Juros → produção:** −5% por ponto de juro *real* acima do neutro, de ×0,75 a ×1,05, ajustando 15% por turno.
   Testado; o custo do caminho industrial ainda é pequeno (ajustar se o usuário achar fraco).
2. **Teto do rendimento:** trocado pelo teto suave da pesquisa (C = 15 turnos de renda), não 25% da produção.
3. **Oferta × demanda:** pressão do dinheiro contra a produção, relativa ao próprio império e à mediana do mundo
   (a "cobertura" da v1 foi abandonada).
4. **Inflação → estabilidade:** sim, em faixas (0,75% e 1,5%), mais o desemprego pelo crédito caro.
5. **Interface:** aba "Ciclo" no Banco Central feita (seção 8); tooltip nas cidades e avisos depois.

Técnico (pesquisar ao implementar): aplicar o efeito na produção como modificador do próprio jogo (descriptor/
property), para aparecer nos tooltips nativos, em vez de mexer no número por fora.

## 8. Aba "Ciclo" do Banco Central (implementada em 2026-10-04)

`NativeUI\NativeBankWindow.cs` (`BuildCycleTab`, `FillCycle`). A janela tem agora 4 abas: Câmbio, Política, Ciclo e
Sua moeda. Na aba Ciclo, cada cartão tem título, chips e um texto curto que explica o número.

| Cartão | Título | Chips | Texto |
|---|---|---|---|
| Diagnóstico | o problema mais urgente, colorido (vermelho, laranja ou verde) | — | o que está acontecendo e o que fazer |
| 1 · Oferta × demanda | pressão (+ = dinheiro à frente da produção) | produção, renda, efeito na inflação | ritmo contra o resto do mundo |
| 2 · Inflação → juros | inflação e meta | juros, o que a regra pede, modo | para onde os juros vão ou o que a regra pediria no manual |
| 3 · Juros → produção | multiplicador do crédito | juro real, neutro, alvo | se a produção vai subir ou cair e quanto vale cada ponto |
| Estabilidade | perda por cidade/turno | inflação, desemprego | as faixas de 0,75% e 1,5% |
| Rendimento do saldo (ou custo da dívida) | juros do último turno | juro real e o teto suave | por que rende ou perde |
| Rumo da inflação | para onde a inflação anda | atual e meta | cada parcela com sinal e cor: base, oferta × demanda, dinheiro parado, dívida, estabilidade e juros |

**Ordem do diagnóstico** (vale o primeiro que bater):
1. dívida;
2. inflação ≥ 1,5% (vermelho);
3. inflação > 0,75%;
4. crédito caro (produção ou alvo abaixo de ×0,95);
5. pressão acima de +10%;
6. deflação;
7. pressão abaixo de −10% (verde);
8. equilibrada.

No modo manual, o diagnóstico diz o que a regra automática pediria.

**Origem dos dados:**
- **Perdas de estabilidade:** calculadas da inflação e do crédito atuais (`EconomySimulation.InflationStabilityLoss` e
  `UnemploymentStabilityLoss`, as mesmas funções que a simulação usa).
- **Juros pedidos:** `EconomySimulation.TaylorRate`.
- **Rumo da inflação:** gravado na simulação a cada fim de turno (`EmpireCurrency.InflationTarget` e as parcelas
  `InflationFrom*`).
- **Saves antigos:** oferta × demanda e o rumo aparecem a partir do primeiro fim de turno.

**Também mudou:**
- na aba Política, o chip "Renda" (efeito antigo dos juros na renda, hoje desligado) virou "Produção ×" (crédito);
- a janela reserva (IMGUI) também mostra a produção pelo crédito.

**Teste:** jogo dos Francos, turno 103, juros fixos em 0,2% e inflação de 1,1%.
- O diagnóstico disse "Inflação acima do tolerável", com −1,4 de estabilidade por cidade, e que a regra pediria 1,9%.
- O rumo deu 0,54%: base +0,4%, oferta × demanda 0, estabilidade alta −0,11% e juros abaixo do neutro +0,24%.
- Crédito ×1,01 subindo até ×1,05.

## 9. Efeitos nos números e no resumo nativo do jogo (2026-10-04)

Pedido do usuário: "todos esses bônus devem aparecer bem discriminados no resumo de buff e debuff nativo".

**Por que não são efeitos de dados do jogo.** Os efeitos do Humankind (descritores) são compilados: a lista
`CompiledDescriptor` é fixa e não aceita descritores novos em tempo de execução. Valores diferentes por império
exigiriam vários descritores. Por isso cada efeito entra onde o jogo calcula e onde ele mostra, em `NativeEffects.cs`.

| Efeito | No cálculo | Na tela | No resumo nativo |
|---|---|---|---|
| Crédito (juros → produção) | `ComputeProductionIncome` × crédito: construção, lista de cidades e IA. | Tela da cidade (`SettlementCursorSnapshot.Synchronize`) | "+2 Indústria de Crédito barato (juros de 0,2%)" |
| Inflação e desemprego | Baixam a **meta** de estabilidade de cada cidade. A estabilidade anda até ela no ritmo do jogo (`GetSettlementPublicOrderGain`, mesma fórmula da RPN `PublicOrderGain`: ±5 por turno). | Meta mostrada já descontada (`Settlement.OnBeforeSynchronization` → `SettlementInfo`). A IA vê a meta descontada. | "−1 Estabilidade de Inflação de 0,96% por turno" e "Desemprego pelo crédito caro" |
| Juros do saldo | Pagos no fim do turno, como antes | Renda por turno da barra do topo, já com os juros projetados (transpiler em `GameSnapshot.SynchronizeEmpireInfo`) | "−14 Reais Dinheiro de Inflação corroendo o saldo" (ou "Juros do saldo", "Juros da dívida") |

**Como as linhas entram.** Um postfix em `PropertyBreakdownWorker.Evaluate` acrescenta uma `PropertyBreakdownPart` com
`LocalizedSourceName` próprio e reordena as partes. O jogo escreve a linha no formato dele: valor, ícone, cor e
ganhos antes das perdas. Partes menores que meio ponto somem, como as do jogo.

**Mudança de regra na estabilidade.** Antes, a perda era tirada da estabilidade atual a cada turno: era invisível
no jogo e, acima de 5 por turno, derrubava a cidade sem limite. Agora é uma redução da meta, como as outras fontes do
jogo. Chaves novas na configuração:
- `EstabilidadeMetaPorPonto1` e `EstabilidadeMetaPorPonto2` (6 cada): 1% → −1,5; 2% → −10,5; 3% → −22,5;
  4% → −34,5;
- `DesempregoNaMeta` (40): ×0,90 → −4; ×0,75 → −10.

**Teste (Francos, turnos 103-104):**
- A linha de crédito apareceu no tooltip de indústria de São Paulo (161 com o crédito).
- A estabilidade da capital andou de 79,6 para 69,6, rumo à meta de 63,8 (meta do jogo 65, inflação −1,25).
- O tooltip de Santa Catarina passou a dizer "até alcançar 9%" (meta do jogo 10).
- A renda do topo foi de +226,8 para +211 com os juros, e a linha −14 entrou no detalhamento de dinheiro.

**Conferência sem mouse:** `jogo detalhe industria|estabilidade|dinheiro [n]` pede ao jogo o mesmo detalhamento do
tooltip, e `hover <caminho>` abre um tooltip nativo para capturas (ver guia-telas-nativas.md).

## 10. Calibração (2026-10-05)

Relatório completo, explicado de forma simples: `docs\relatorios\Economia-Calibracao-ELI5.pdf` (gerado só com os dados
já coletados: saves, auditorias `jogo economia` dos turnos 35, 49, 54, 76, 82 e da partida longa, e este documento).

**O que está bom:** em 64 turnos da partida longa nada disparou. Inflação, juros e câmbio ficaram longe dos tetos,
ninguém viveu de juros, e o caminho financeiro deixou de escalar (guarda 20% a mais, mas produz 19% menos).

**Pontos de atenção e o que o usuário decidiu (2026-10-05):**
1. **Pressão nervosa:** em 58% dos retratos a parcela da oferta × demanda estava no limite de ±0,6% (basta ±20% de
   pressão). Os Hunos foram de +50% a −50% em 9 turnos. **Decidido: curva suave no lugar do corte seco. Feito:**
   parcela = `PressaoLimite` × tanh(`PressaoDemanda` × pressão / `PressaoLimite`). Perto de zero é o mesmo linear; longe,
   encosta no limite aos poucos (10% → 0,28%; 20% → 0,46%; 50% → 0,59%). Conferido no T77 da partida de 16 impérios:
   as 15 parcelas batem com a curva, e nenhuma ficou colada no limite (com o corte antigo seriam 5).
2. **Dívida da IA:** em 40% dos retratos havia dívida empurrando a inflação (até +2,5% por turno). Os Persas ficaram
   presos: dívida → inflação de 1,92% → juros de 4,25% → crédito ×0,90 → −13,7 de meta de estabilidade (T129). A
   regra automática sobe os juros por uma inflação que vem da dívida, e isso aperta a dívida. **Decidido: não ignorar
   a dívida; o peso dela na regra diminui quanto maior ela for, mas nunca zera ("a dívida interna ainda tem seu
   peso"). Feito:** `EmpireCurrency.InflationRateFromDebt` guarda quanto da inflação atual veio da dívida (anda no ritmo
   da inflação) e a regra de Taylor conta essa parte por `DividaNaRegra` × tanh(dívida / `DividaNaRegra`), com
   `[Juros] DividaNaRegra` = 0,015 (0,5% → conta 0,48%; 1% → 0,87%; 2,5% → 1,40%; 0 = conta tudo, como antes). A aba
   Ciclo diz quanto a regra está contando. Conferido no T77: os 5 impérios endividados já têm a parte da dívida
   registrada; o desconto cresce nos turnos seguintes (dívida longa como a de E2, ~2,2%: a regra pede ~1,3 ponto de
   juros a menos).
3. **Câmbio no teto:** a moeda do caminho financeiro (Khmers, Huge) chegou a 3,79 e encostou no teto de 4,0.
   **Decidido: fica como está** (o teto faz o papel dele).
4. **Custo do caminho industrial:** leve (+6% de produção, moeda mais fraca). **Decidido: fica como está** até o
   usuário achar fraco jogando.

`jogo economia` ganhou as colunas `dívInf%` (parte da inflação vinda da dívida) e `regra%` (juros que a regra pede).

**Limpeza feita:**
- 12 chaves antigas que não faziam mais nada saem do `.cfg` sozinhas (`EconomyConfig.RemoveObsoleteKeys`, lista
  fechada: `Cobertura`, `CoberturaLimite`, `EfeitoJuros`, `LimiteEstabilidade`, `PerdaEstabilidade`,
  `EstabilidadePorPonto1/2`, `EfeitoRenda`, `EfeitoProducao`, `TetoRendimento`, `DesempregoEstabilidade`,
  `CustoDesvioPedagio`). Cópia do arquivo antes da limpeza em `Documentos\HumankindModding\backups\`.
- A descrição de `VelocidadeInvestimento` dizia "~6 turnos para metade do efeito"; com 0,15 por turno são ~4 (e ~14
  para 90%). A de `JurosNaRenda` apontava para uma chave que não existe mais.
