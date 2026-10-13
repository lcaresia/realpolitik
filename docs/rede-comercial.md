# Rede comercial: entrepostos, grandes rotas e carga das rotas

Pedido do Lucas em 2026-10-12 (item 36 de `melhorias-jogando.md`). O código fica em `src\CurrencyMod\Trade\`:

| Arquivo | O que faz |
|---|---|
| `TradeNetwork.cs` | Lê as rotas ativas do jogo e monta a rede: entrepostos, grandes rotas e a carga por território. |
| `TradeHubs.cs` | Efeitos dos entrepostos: buffs, debuffs, espiões, epidemia, crise de abastecimento e inflação da dívida. |
| `TradeCargo.cs` | Fé, cultura e tecnologia que viajam com a rota. |
| `TradeNetworkLabels.cs` | Textos do mapa de rotas. |
| `RouteVisuals.cs`, `RouteLabels.cs` | Desenho da linha e dos rótulos. |

O dossiê das IAs está em `Diplomacy\DossierTrade.cs`. As configurações ficam na seção `[TradeNetwork]` do `.cfg`.

## 1. Mapa de dependência (só leitura)

Para cada nação e recurso, o dossiê calcula a parte que vem por rota e de quem. Ele só mostra a dependência a partir de 40% do que a nação tem.

- **Na própria nação:**
  - "Você depende do comércio para: Cobre 70% de X, passando pelas terras de E3";
  - "Dependem de você: E11: Porcelana 100%, Ferro 60%";
  - "Rotas vitais de outros que passam pelas suas terras" (60% ou mais).
- **Em cada nação próxima:** "Dependência comercial: você depende deles: …; eles dependem de você: …; de terceiros: …".
- O prompt de `politica_comercial` lembra que cortar a rota de que um rival depende aperta a economia dele.

## 2. Entrepostos (cidades por onde passam muitas rotas)

**Nível.** Cada rota ativa com mercadoria conta uma vez para cada cidade de cujos territórios ela passa (ou onde termina). Grande rota conta 2. Os níveis vêm de `HubLevelRoutes = 2,4,7,11`:

| Nível | Nome |
|---|---|
| 1 | Mercado |
| 2 | Entreposto |
| 3 | Grande entreposto |
| 4 | Metrópole comercial |

**Buffs** (aplicados no fim do turno, como os veteranos do conselho):

| Nível | Ganho |
|---|---|
| 1+ | +1 de dinheiro por rota × era |
| 2+ | +5% de crescimento por nível acima de Mercado |
| 3+ | +influência (nível × era) |
| 4 | +15% da ciência da cidade |

As duas pontas de cada grande rota ganham fama por turno (2 × era).

**Debuffs: exposição × vulnerabilidade** (regra do Lucas: ser rico tem que valer a pena, o debuff só freia). O nível define quais debuffs podem aparecer, e o estado da cidade decide quanto deles entra de fato:

| Debuff | Abre no nível | Some quando |
|---|---|---|
| Custo de vida: a inflação da dívida sobe até +50% | 2 | o tesouro não tem dívida |
| Espiões estrangeiros perdem menos disfarce por turno (até 50%) | 3 | estabilidade 80 ou mais |
| Crime: até -6 de estabilidade | 4 | estabilidade 80 ou mais |
| Minorias: até -4 de estabilidade | 4 | todos os territórios têm a fé e a cultura do dono |
| Epidemia: até 4% por turno, -1 de população (-2 em cidade com 14+), uma a cada 15 turnos | 4 | estabilidade alta e comida sobrando |
| Crise de abastecimento: estabilidade em todas as cidades por 5 turnos | qualquer | não cortam rota de que a nação dependia (60% ou mais de um recurso caindo para menos de 20%) |

- A soma dos debuffs de estabilidade numa cidade nunca passa de `HubMaxStabilityLoss` (8).
- O sorteio da epidemia é determinístico por cidade e turno: o mesmo save repete o mesmo resultado.
- Os debuffs de estabilidade aparecem na explicação nativa da estabilidade ("Crime nas rotas de comércio", "Minorias estrangeiras trazidas pelo comércio", "Crise de abastecimento (rota cortada)").
- O dinheiro aparece na explicação da renda ("Entrepostos comerciais") e na renda da barra do topo.

**Como cada efeito entra no jogo:**
- **Estabilidade:** pelo `NativeEffects.StabilityLoss`, o mesmo caminho da inflação do mod.
- **Espiões:** prefixo em `StealthAncillary.NewTurnBegin_UpdateStealth`, que devolve parte do desgaste antes do jogo descontar.
- **Inflação:** `EconomySimulation.TargetInflation` multiplica o termo da dívida.

## 3. Grandes rotas

**Quais são.** As 10% mais longas entre as rotas ativas com mercadoria, contadas em territórios, depois em terras cruzadas, depois em mercadoria. São sempre pelo menos uma, e todas cruzam no mínimo 5 territórios (`GrandRouteTopShare`, `GrandRouteMinTerritories`; decisão do Lucas: porcentagem, não tamanho fixo).

**Nome.** O formato é "Rota da Seda Unug–Kerma": um título da lista ("Estrada Real", "Rota do Incenso", "Via Imperial"…) seguido das duas capitais. Duas grandes rotas não repetem o título enquanto houver títulos sobrando.

**No mapa de rotas:**
- linha dourada, grossa e brilhante, por cima da cor normal;
- o nome em letras grandes douradas, com "Grande rota · N territórios · N nações";
- o nome fica no trecho explorado mais perto do meio do caminho, nunca na névoa nem em cima das cidades das pontas.

**Selos e painel.** Os entrepostos ganham um selo embaixo do estandarte da cidade. Os das outras nações só aparecem a partir de Entreposto e só em área explorada. Passando o mouse sobre um selo ou sobre o nome de uma grande rota, abre um painel:
- ganhos por turno;
- riscos com o motivo (só nas suas cidades);
- quem a rota liga, o que leva e os entrepostos no caminho.

## 4. Carga das rotas

Cada lado da rota leva a fé e a cultura de quem vende:
- força total (1,5 por recurso) nos territórios do comprador por onde passa;
- metade nas terras de terceiros;
- nada em território sem dono (regra do jogo).

Entra como "força de crença" do território (`ComputeTerritoryBeliefStrengths` da religião e da cultura). Ela compete com a fé local, aparece nas dicas do jogo e converte no ritmo do próprio jogo.

**Medida no T83 do teste:** a religião dominante de um território tem força em torno de 50, e as rotas somam de 1 a 8 por religião. A fé de quem vende pesa, mas não atropela a local.

**Tecnologia:** uma tecnologia que um parceiro comercial já tem custa 10% menos. Isso entra pelo multiplicador nativo de "impérios que contribuem", então a tela de pesquisa mostra o parceiro. Os custos são recalculados quando os parceiros mudam.

## 5. Testado (2026-10-12, partida de teste "Teste16 T82 site2", turnos 82 a 85, com a IA de linguagem ligada)

- **Rede:** 12 entrepostos e 3 grandes rotas com os limites normais. Os efeitos foram aplicados no fim do turno em 11 nações (log "Trade hubs: …").
- **Mapa:** linha dourada grossa, nomes, selos, empilhamento quando há vários nomes no mesmo lugar e o painel ao passar o mouse, que troca de lado para não cobrir o painel de comércio. Prints em `dev\out\trade7.png` e `dev\out\hover2.png`.
- **Freio:** com limites baixos só para o teste, cidades com estabilidade 98 a 100 ficaram sem debuff. Babylon (74) teve -0,7 de crime. Harappa (10) teve crime -6, espiões +50% e epidemia 2%.
- **Epidemia forçada:** Harappa perdeu 1 de população.
- **Espião:** o espião da E0 em Harappa foi protegido (contador 1).
- **Crise de abastecimento:** os bloqueios cortaram as rotas da E11, que entrou em crise (10 recursos perdidos).
- **IA:**
  - 5 escolhas de cívico pelas nações (`choose_civic`), todas adotadas pelo jogo;
  - a E6 escreveu no diário "Gilgamés … depende do meu cobre; … fecharei essa alavanca".

## Comandos de teste

| Comando | O que mostra ou faz |
|---|---|
| `game trade [refresh]` | Entrepostos (territórios, nível, rotas, estado, efeitos), grandes rotas, crises e inflação da dívida. |
| `game trade routes` | O tamanho de todas as rotas ativas, para calibrar. |
| `game trade cargo T#` | Fé e cultura que as rotas somam no território. |
| `game trade epidemic <cidade>` | O próximo fim de turno causa epidemia nesse entreposto. |
| `game trade labels` | Os rótulos do mapa e quantos foram desenhados. |
| `game trade hover <texto>` / `off` | Abre o painel do rótulo que contém o texto, sem usar o mouse. |
