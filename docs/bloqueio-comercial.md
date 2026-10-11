# Bloqueio comercial — documentação técnica

Cada dono de território decide o que as rotas comerciais estrangeiras fazem ao atravessar as terras dele: passam
livres, pagam pedágio ou são barradas. Vale para o jogador, para a IA de regras e para as nações da IA de linguagem.

Código: `src\CurrencyMod\Trade\` (`TradePolicy.cs`, `TradeBlockade.cs`, `TradeAi.cs`, `TradeGrievances.cs`,
`TollPreview.cs`) e as telas `NativeUI\TradePostWindow.cs` (Posto Comercial) e
`NativeUI\DiplomacyTradePolicyPanel.cs` (bloco "Seus Postos Comerciais" na aba Comércio da diplomacia).

## 1. Regras e preços (`TradePolicy`)

- **Regra:** dono × território × alvo → `Free`, `Toll` ou `Block` (`CurrencyWorld.TradeRules`, salvo no
  `CurrencyMod.json` do save).
- **Preço do pedágio**, por recurso transportado e por turno, na moeda do dono. Vale o primeiro que existir:
  1. preço do posto (`TradeRule.Price`, botões `-`/`+` da linha no Posto Comercial);
  2. preço geral do dono para aquele império (`CurrencyWorld.TollPrices`, botões da diplomacia ou
     `politica_comercial` com "preco"). Mudar o preço geral apaga os preços de posto daquele império;
  3. padrão: `[BloqueioComercial] PedagioPorRecurso` × era do dono, arredondado a 0,1.
- Preço de 1 a 500. Os botões andam de 1 em 1 até 10, de 2 em 2 até 20, de 5 em 5 até 100 e de 25 em 25 acima.
- Qualquer mudança marca `TradePolicy.PathsDirty`: na próxima ordem processada, as rotas são recalculadas.

## 2. Caminho das rotas (`TradeBlockade`)

O jogo calcula o caminho de cada rota por territórios (`TerritoryPathfindManager` com `TradePathfindContext`). O mod
entra no custo de cada passo (postfix em `TradePathfindContext.GetAdjacentTransitionCost`):
- **Bloqueio** de qualquer um dos lados da rota: custo infinito. Sem outro caminho, a rota é destruída em nome de quem
  bloqueou (a notificação do jogo diz quem foi).
- **Pedágio:** custo extra ao **entrar** nas terras do dono e, dentro delas, só o **aumento** de preço de um posto
  para o seguinte. Num caminho de preços crescentes, o total bate com a cobrança (o posto mais caro do dono, §3):

  ```
  pedágio por turno = (preço para o lado esquerdo + preço para o lado direito, se taxados) × recursos da rota
  manutenção por território = manutenção comercial das duas trocas / nós do caminho atual da rota  (mínimo 0,5)
  custo extra = pedágio / manutenção por território × 2 × PesoPedagioNasRotas
  ```

  Entrar num território estrangeiro custa 2 no cálculo do jogo; então o pedágio vira "territórios a mais" pela
  manutenção que a rota paga por território. Se pagar sai mais barato que a volta, a rota passa e paga; senão, desvia.
  Rota sem mercadoria não paga e não desvia.
- **Correção de 2026-10-05:** a contagem de nós vinha das listas `TradeNodes` das trocas, que o jogo esvazia
  (`DestroyPath` → `UnregisterFromTradeNodes`) **antes** de procurar o caminho novo. No recálculo de verdade a conta caía
  sempre no piso (0,5): o pedágio pesava várias vezes mais que o previsto e as rotas fugiam de qualquer preço. Agora a
  conta usa `PathAsPathNode.Length`, que só muda depois. O recálculo do jogo e a prévia (§4) passaram a concordar
  (testado: a rota Axumitas↔Polinésios foi para onde a prévia disse).
- **Revisão de bugs de 2026-10-05:**
  - **Posto interno:** antes só a entrada nas terras do dono contava. Um posto de dentro (ou mais caro que o da
    entrada) nunca fazia a rota desviar, a prévia mostrava "N pagam · 0 desviam" em qualquer preço, e mesmo assim a
    cobrança usava o preço dele. Agora o caminho soma o aumento de preço entre territórios do mesmo dono.
  - **Rota nova:** no `StartTrade` o jogo limpa `PathAsPathNode` antes do `CreatePath`, e as trocas ainda não têm
    `TradeNodes`. Por isso o primeiro caminho saía com o piso da manutenção, e nada pedia recálculo. O pós-fixo
    `StartTradePatch` marca `NeedPathRefresh`, e o `OnOrderProcessedOrTurnBeginOrTurnEnd` do jogo refaz o caminho
    com os valores reais. Vale também quando a rota ganha mercadoria.
  - **Testes no jogo:** `jogo pedagio teste E2 T55 T56 E7 E12 50` (contexto da rota E7↔E12) dá +49,53 tanto entrando
    de fora quanto vindo do posto livre do mesmo dono (antes: +0 nesse passo). Rotas novas criadas com
    `jogo comprar` aparecem no log ("StartTrade … marcada para recalcular") e seguem ativas depois do recálculo.
  - **Índice das regras:** o `Rebuild` agora lê e publica dentro do `CurrencyManager.Lock`, e baixa o `stale` antes de
    ler. Antes, uma regra mudada no meio de um `Rebuild` era apagada pelo índice velho até a próxima mudança.

## 3. Fim do turno

Prefixo em `DepartmentOfTheTreasury.TurnEndPass_CollectMoney`, uma vez por turno:
- **Regras órfãs** (`DropLostTerritoryRules`, desde 2026-10-05): regra de território que mudou de dono é apagada.
  Ela já não valia no caminho, mas mantinha a reclamação da vítima e a retaliação da IA, e ninguém conseguia limpá-la
  pela interface. No primeiro turno com a correção, a partida de teste tinha uma.
- **Cobrança:** cada lado taxado paga, por rota, o preço do posto mais caro daquele dono no caminho × recursos da
  rota. Quem paga desembolsa o equivalente na moeda dele (câmbio do mod). Registro em `CurrencyWorld.LastTolls`.
- **Reclamação:** quem paga pedágio ou é barrado ganha "Bloqueio comercial" contra o dono enquanto a regra existir
  (`TradeGrievances`). Aceitar a exigência tira a regra; recusar dá pretexto de guerra. Se um dos dois é vassalo, o
  jogo mandaria a reclamação para o suserano (sem a marca do mod), então ela não é criada.
- **IA de regras** (`TradeAi.Review`, a cada `IntervaloIA` turnos): taxa ou bloqueia rivais hostis, mas só cria pedágio
  contra quem usa os postos dela (ou onde já havia regra), e a regra "taxar o mais rico" pede hostilidade ≥ 1. As
  posturas escolhidas pelas nações da IA de linguagem (`politica_comercial`) são reaplicadas todo turno e a IA de
  regras não mexe nesses pares.

## 4. Prévia de desvio (`TollPreview`)

Pedido do usuário: ao definir um preço novo, mostrar **na hora** quantas rotas deixam de passar por ali.
- A tela pede (`TollPreview.Request`, thread principal): dono, território (ou -1 = todos os territórios do dono),
  alvo, modo e preço. A resposta vale 4 s.
- O cálculo roda na thread do sandbox, entre as ordens (bomba do `ActionExecutor`, só no `SandboxState_TurnMain`).
  Para cada rota ativa ou suspensa do alvo que não é do dono:
  1. **sem regra nenhuma do dono** (para nenhum dos lados): o caminho passaria por ali? Se sim, conta em "rotas";
  2. **com as regras de hoje e o modo e preço pedidos** para o alvo: continua passando? Se sim, "paga"; senão,
     "desvia" (e, para um posto, por onde vai: sai das terras do dono ou passa por outro posto dele).
- O caminho é o do próprio jogo (`BaseTradeRelation.TryFindPath`, que não muda nada) com um cenário "e se"
  (`TradePolicy.WhatIf`, `[ThreadStatic]`) que `GetMode` e `GetPrice` consultam primeiro.
- **Posto Comercial:** cada linha taxada mostra "N pagam · M desviam" (bloqueio: "N barradas"); o cartão de cima
  soma sem contar duas vezes a rota entre dois impérios taxados. A dica da linha diz qual rota paga e qual desvia, e
  para onde.
- **Diplomacia:** com o mesmo modo em todos os postos, a linha de cima mostra a prévia do preço geral ("A rota deles
  desvia dos seus postos").
- Uma rota entre dois impérios que o dono taxa paga os dois pedágios: baixar só um preço pode não bastar.

## 5. Comandos de teste

`jogo rotas`, `jogo pedagio …`, `trade`, `trade open <T#>`, `trade price <E#> up|down`,
`trade previa <dono> <território|-1> <alvo> <preço> [bloqueio]`, `trade costs`, `trade mine`. Descrição em
`docs\diplomacia-ia.md` §7.

## 6. Configuração (`[BloqueioComercial]`)

| Chave | Padrão | O que faz |
|---|---|---|
| `Ativo` | true | Liga o bloqueio comercial (desliga sozinho em partida online). |
| `PedagioPorRecurso` | 2 | Preço padrão por recurso, × era de quem cobra. |
| `PesoPedagioNasRotas` | 1 | 1 = compara pagar com desviar; maior = foge mais; 0 = sempre passa e paga. |
| `IA` | true | A IA de regras também taxa e bloqueia. |
| `IntervaloIA` | 5 | De quantos em quantos turnos a IA de regras revê. |
| `CoresDasRotas` | true | Cores, espessura e valor das rotas na visão de comércio (§8). |

## 7. Pagar ou desviar: a escolha de quem paga (`rota_pedagio`, 2026-10-10)

Antes, a decisão de pagar o pedágio ou desviar era só da conta de custo do §2. Agora quem paga também pode escolher,
para todos os postos de um dono:
- **Modelo:** `CurrencyWorld.TollChoices` (`TollChoice { Payer, Owner, Choice, Turn }`, salvo no save) e
  `TollChoiceKind { Auto, Pay, Detour }`. O índice `TradePolicy.GetTollChoice` é lido no cálculo de rotas, e
  `SetTollChoice` muda o valor (com o lock) e marca `PathsDirty`.
- **No caminho** (`TradeBlockade.TollPathCost`): se a escolha for `Pay`, o preço daquele pagador não pesa (a rota passa e
  paga). Se for `Detour`, o passo custa `DetourCost = 1000`, que é finito: sem outro caminho, a rota passa e paga. `Auto`
  segue a regra de sempre. A prévia (`TollPreview`) usa o mesmo cálculo e já mostra o efeito.
- **IA de linguagem:** ação `rota_pedagio {"nacao":"E2","escolha":"pagar|desviar|automatico","motivo":"..."}`
  (`Prompts.cs`, `DecisionParser`, `IaModule.ApplyTollChoice`). O dossiê mostra a escolha atual em cada linha "eles
  cobram pedágio das suas rotas".
- **Para o jogador:** o bloco "Seus Postos Comerciais" da diplomacia diz "eles escolheram pagar" ou "eles escolheram desviar".

## 8. Cores, espessura e valor das rotas (`RouteVisuals`, `RouteLabels`, 2026-10-10)

O jogo desenha todas as rotas iguais. Ele usa o `PresentationTradeRoadController` com três estilos (normal, interrompida
e destruída) e desenha as linhas na GPU (`WorldSpaceLinesWithSteps`).
- **Sandbox:** um pós-fixo em `TradePathSnapshot.ComputePathRendering` guarda, por linha, a situação em relação ao
  jogador local, os recursos que passam, o pedágio por turno e o tile do meio. Os dados ficam num
  `ConditionalWeakTable` por buffer `Data`. A ordem das linhas é a do jogo: só as relações externas não nulas, na ordem
  do array.
- **Apresentação:** um pós-fixo em `TradePathSnapshot_PresentationChanged` troca o estilo das linhas normais por estilos
  do mod. Esses estilos são cópias do estilo normal do jogo com `UseCustomColor`, outra cor e outra largura, acrescentados
  uma vez por controlador com `UpdateStyles`. São 4 situações × 3 larguras:
  - **dourado:** paga o seu pedágio;
  - **branco:** passa livre pelas suas terras;
  - **laranja:** rota sua que paga pedágio;
  - **azul:** rota sua.
  As larguras são 1–2, 3–5 e 6+ recursos. As outras rotas e as interrompidas ou destruídas ficam como o jogo desenha.
- **Rótulo:** "£ 12/t" em dourado (já paga) ou "£ 12/t?" em branco (pagaria no preço atual), no meio das rotas que passam
  pelas suas terras, só na visão de comércio. É IMGUI (`RouteLabels`), posicionado com a câmera do controlador e
  `PresentationWorldController.GetWorldPositionInUnityWorld`, a mesma conta dos pinos do jogo.
- **Ainda não testado no jogo.** Falta conferir:
  1. se a cor do estilo aparece (`UseCustomColor`);
  2. se a largura maior fica boa;
  3. se o rótulo combina com o visual. Se destoar, o passo seguinte é usar pinos nativos (`*PinsSubset`).
