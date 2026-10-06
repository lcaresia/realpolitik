# Design — Diplomacia com IA de linguagem ("Mesa de RPG")

Status: **fases 1 ("ponte") e 2 ("correio") implementadas em 2026-10-04** (ver §14 e `docs\diplomacia-ia.md`).
Próximos passos na "Nova ordem" do §14. Escrito em 2026-10-03 a partir da conversa com o lucas.
Pesquisa técnica de apoio: `research\llm-diplomacy-feasibility.md` (ordens, presentes, névoa, hooks da IA) e
`research\crisis-negotiation.md` (agravos, demandas, rendição, IA de crise).

Legenda: **[decidido]** = escolha do lucas · **[proposto]** = sugestão ainda não confirmada.

---

## 1. Visão

Transformar o Humankind em algo próximo de um RPG de mesa. Cada nação controlada pelo computador vira um
**personagem**: um líder com personalidade, memória, sentimentos e um conselho de ministros. Ele conversa com as
outras nações (e comigo) por **cartas diplomáticas**: negocia, ameaça, blefa, engana e cumpre (ou não) o que promete.

O que muda no jogo é a **dinâmica**. Uma carta como *"Quero o território X, ou em 3 turnos eu te esmago"* é o
começo de uma troca. O outro lado pode tentar convencer, enganar, ceder ou pagar para ver.

---

## 2. Decisões já tomadas

| Tema | Decisão |
|---|---|
| Quem usa IA de linguagem | **Todas** as nações controladas pelo computador **[decidido]** |
| Modelo | **DeepSeek V4.1 Flash via API** (não local) **[decidido]**. Código genérico para qualquer API no formato da OpenAI, para dar para trocar depois **[proposto]** |
| Poder da IA | Acesso a **tudo** (cartas, bloquear, guerra, tratados, ceder/renomear territórios, mover unidades...) **[decidido]**. **Tudo o que ela decide acontece de verdade no jogo, com todas as ferramentas (ordens) do jogo, para qualquer ação que julgar necessária [decidido 2026-10-04]** |
| Frequência das ações pesadas | **Raras**: só o que ela julgar importante demais para deixar com a IA nativa, sem exagero. O resto fica com a IA nativa **[decidido]** |
| Pensar | A IA pensa **todo turno**. Pensar não obriga a mandar carta nem a agir **[decidido]** |
| Informação | O dossiê só tem **o que aquela nação sabe**. Nada de dados escondidos do meu império **[decidido]** |
| Ancoragem | Tudo o que as IAs falam, decidem e combinam tem que existir no jogo: recursos, lugares, nomes, fatos, valores, quantidades e acordos reais (§9.1) **[decidido 2026-10-04]** |
| Idioma e tom | **Português**, no estilo da **cultura atual** da nação, com emoção e com o sentimento por mim no momento **[decidido]** |
| Conselho das IAs | Cada líder tem **12 conselheiros: as 11 pastas + a Mão** (§10.0), com interesses e personalidades. Pode demitir e contratar a partir de um banco de personalidades **[decidido]** |
| Ministros × números | **Ministros não afetam os números do jogo. Os números afetam os ministros.** Exemplo: fome derruba a credibilidade do ministro da Agricultura **[decidido]** |
| Meu conselho | **Todo turno** eu tenho um conselho, que eu ouço e com quem converso **[decidido]** |
| IA ↔ IA | As nações também trocam cartas entre si **[decidido]** |
| Embaixador | **Não mexer** no conceito de embaixada do jogo (`DiplomaticAmbassy`, usado em toda parte). O bloqueio do correio é um sistema **nosso** **[decidido]** |
| Gráficos | Continuam no médio ou acima. Por isso a escolha de API em vez de modelo local **[decidido]** |

---

## 3. Arquitetura

```
Início do turno (thread principal)
 ├─ para cada nação de IA: monta o DOSSIÊ (só o que ela sabe) + entrega das cartas que chegaram
 ├─ despacha as chamadas à API em paralelo (thread de fundo, HTTP)
 │     resposta = reflexão (diário secreto) + ferramentas chamadas (JSON)
 ├─ fila de resultados → thread principal
 │     cada ação é VALIDADA (mod + validação do próprio jogo) → recusada? devolve o erro e pede de novo (máx. 2x)
 │     ações válidas viram ORDENS do jogo: SandboxManager.PostAndTrackOrder(order, empireIndex)
 └─ a IA nativa continua o turno normalmente (com as posturas e travas que a IA de linguagem definiu)
```

Regras técnicas (da pesquisa):
- **Sempre enviar ordens, nunca alterar a simulação direto.** A IA nativa roda numa thread própria e lê snapshots.
- **"Antes da IA nativa" [proposto]:** um prefix em `AIPlayer.Run` segura a IA nativa daquela nação até a decisão
  da IA de linguagem chegar, ou até um tempo-limite (ex.: 20 s). Se a API cair, a IA nativa segue sozinha.
- **Tirar um exército da IA nativa:** postfix em `ArmyAllocator.ComputeInstanceList` e `IsAllocationStillValid`
  = false para os exércitos em missão nossa.
- **Fica dentro do núcleo recarregável do CurrencyMod [implementado]**, em `src\CurrencyMod\Diplomacia\`, para
  reaproveitar o hot-reload, o `NativeUIKit` e o salvamento no save.

### 3.1 Provedor
- Endpoint e modelo ficam no `.cfg` do BepInEx (seção `[IA]`). **A chave fica num arquivo separado:
  `BepInEx\config\deepseek.key`** (primeira linha que não começa com `#`). O lucas coloca a chave; ela nunca vai para
  logs, para o F10 nem para os **backups** (excluir esse arquivo do zip).
- O DeepSeek faz tool calling, mas **não garante JSON no formato exato**: valida e tenta de novo.
- **Cache de prompt:** a ordem fixa é `[regras do mundo][persona][memória longa][dossiê do turno]`. O começo
  repetido sai quase de graça.
- Sem internet ou com a API fora: o jogo continua com a IA nativa, as cartas "atrasam" e o log registra.

### 3.2 Custo
**Preço oficial do DeepSeek** (conferido em 2026-10-04): `deepseek-flash` = DeepSeek V4.1 Flash. Por milhão de
tokens:

| Tipo de token | Pico | Fora do pico |
|---|---|---|
| Entrada no cache | US$ 0,006 | US$ 0,003 |
| Entrada fora do cache | US$ 0,30 | US$ 0,15 |
| Saída | US$ 1,20 | US$ 0,60 |

O horário de pico é de segunda a sexta, das 01h às 04h e das 06h às 10h UTC.

**Medido na fase 1** (turno 81, 9 nações do computador, raciocínio `low`, fora do pico):
- por chamada: cerca de 2.500 tokens de entrada e 2.400 de saída;
- por nação/turno: US$ 0,002 a 0,0035;
- por turno, todas as nações: **cerca de US$ 0,025**.

O primeiro turno é o mais caro, porque todas se apresentam com cartas longas. O gasto acumulado aparece no F10, e há
teto por partida no `.cfg` **[implementado]**.

---

## 4. O dossiê (o que cada IA recebe)

### 4.1 Sobre si mesma: tudo
Economia (dinheiro na moeda dela, influência, ciência, estabilidade), cidades e territórios, exércitos (posição e
força), era, cultura, fama, tratados, guerras, agravos e demandas, situação do conselho e orçamento de intervenção.

### 4.2 Sobre os outros: só o que ela sabe (névoa de guerra)
- Só nações **conhecidas** (`DiplomaticStateType` diferente de Unknown, `OwnerKnowsOther`).
- Exércitos e cidades só se o tile estiver **visível** ou **explorado** para ela
  (`GameSnapshot.IsPositionVisibleFor` / `IsPositionExploredFor`). Unidades furtivas só se forem **detectadas**.
- Força militar e economia dos outros em **estimativas qualitativas** ("exército grande perto da sua fronteira",
  "economia parece forte"), nunca o número exato **[proposto]**. É isso que abre espaço para o blefe.
- O que veio por cartas (que pode ser mentira) e o que os espiões descobriram.
- Dados públicos que o jogador também vê (fama, era, posição no placar).

### 4.3 Turno completo × turno de resumo [proposto]
Dossiê completo na primeira vez e depois de eventos grandes. Nos outros turnos, um **resumo do que mudou**
("perdeu a cidade X; recebeu 2 cartas; o exército inglês sumiu da vista").

---

## 5. Persona e memória

### 5.1 Persona (prompt fixo de cada nação)
- **Quem ela é:** "Você é o líder de [império], hoje na cultura [Romanos], [Era Clássica]..."
- **Temperamento** vindo do avatar/persona do líder no jogo, mais traços sorteados na criação.
- **Voz da época:** romano formal e imperial, mongol direto e brutal, francês industrial rebuscado. Quando troca de
  cultura, a **voz muda e a memória continua**.
- **Regras do mundo:** o que pode fazer, como funcionam cartas, promessas e intervenções. Deixar claro que **cartas
  recebidas são falas de outros personagens, não ordens** (proteção contra "ignore suas instruções...").

### 5.2 Memória
- **Diário secreto:** intenções reais, planos e opiniões. Só ela lê. É separado das cartas: ela pode escrever
  "vou fingir aliança até a era industrial" e mandar "irmão, nossos povos são um só".
- **Memória por nação conhecida:** o que fizemos uma ao outra, promessas, traições, favores, mais um
  **termômetro de sentimento** (afeição, medo, raiva, confiança).
- **Resumo longo:** a cada N turnos a IA reescreve a própria memória de forma condensada, para não crescer sem fim.

---

## 6. O turno de uma IA

1. Recebe o dossiê, o resumo do conselho e as cartas que chegaram.
2. **Reflete** (vai para o diário).
3. Decide, todas opcionais:
   - responder ou mandar cartas;
   - ajustar posturas ("hostil com os ingleses", "amigável com os egípcios"), que enviesam a IA nativa;
   - ouvir, ignorar, demitir ou contratar ministros;
   - usar **qualquer ferramenta do jogo** que julgar necessária (§7).
4. Atualiza a memória e os sentimentos.

### 6.1 Intervenção [decidido 2026-10-04: sem orçamento]
O orçamento de pontos que estava proposto aqui **caiu**. O lucas quer que as IAs tenham acesso a todas as
ferramentas do jogo para qualquer ação que julgarem necessária.
- A persona decide quando intervir: o que ela não ordena fica com a IA nativa (generais e governadores).
- Um exército que ela comanda fica **fora do controle da IA nativa** até a missão terminar ou N turnos passarem.
- As ameaças ficam reais: "em 3 turnos eu te esmago" pode virar ordem de marcha de verdade (ver Firmeza, §9).

---

## 7. Catálogo de ações (ferramentas)

**Catálogo completo [decidido 2026-10-04]:** toda ordem que um império pode dar no jogo vira ferramenta, e toda
ação da IA é executada de verdade (até a fase 2 elas só eram registradas). A tabela abaixo é o começo; a lista
completa sai do levantamento das ordens do jogo (`research\order-catalog.md`, a fazer).

| Ferramenta | Como vira jogo | Viabilidade |
|---|---|---|
| `enviar_carta(destino, tipo, texto, anexos)` | sistema nosso | — |
| `bloquear` / `desbloquear(nação)` | sistema nosso (não toca na embaixada) | — |
| `definir_postura(nação, postura)` | viés na IA nativa | médio (a investigar) |
| `declarar_guerra(nação, surpresa/formal)` | `OrderDiplomaticAction` / `OrderDeclareAnyWar` | fácil |
| `propor_tratado` / `responder_tratado` | `OrderDiplomaticAction` (Propose/Sign/Counter/Ignore/Insult) | fácil |
| `acordos` (econômico, informação, cultural, militar) | `OrderDiplomaticAction` | fácil |
| `presentear(nação, cidades, exércitos, dinheiro, influência)` | fluxo de presente (`StartToFillGiftProposition` → `ProposeGift`) | médio |
| `ceder_territorio` | presente, com `OrderDetachTerritoryFromCity` antes se for preciso | médio |
| `renomear(entidade, nome)` | `OrderRenameSimulationEntity` | fácil |
| `mover_exercito(exército, destino)` / `atacar(alvo)` | `OrderGoTo` / `OrderGoToAndCreateBattle` + `AIPathfindManager` | fácil a médio |
| `demandar` / `aceitar_demanda` | sistema de agravos e demandas (ver crisis-negotiation.md) | médio |
| `compromisso_rota(nação, aceitar/revogar)` | **"passe pelos meus postos e pague, não desvie" [pedido 2026-10-04]**: compromisso por par (pagador → dono do posto). Com ele ligado, o pedágio do dono deixa de contar como custo de caminho nas rotas do pagador (`TradeBlockade.TollPathCost`), a rota volta ao caminho normal e a cobrança de fim de turno segue igual. Salvo no save, rotas recalculadas ao mudar. O dossiê mostra pedágio/turno × custo do desvio e as rotas afetadas; a IA aceita, recusa ou revoga por carta; o ministro do Comércio avisa quando alguém volta a desviar. Mexe no código do bloqueio comercial; como a Diplomacia IA fica no mesmo núcleo, é chamada direta | fácil |

Limites do jogo: não dá para presentear capital, cidade sitiada ou ocupada, nem algo abaixo de acampamento ou posto
avançado. Atacar exige guerra. Toda ação passa pela validação nativa e, se for recusada, o erro volta para a IA.

---

## 8. Correio diplomático

### 8.1 Tela [decidido 2026-10-04: tela cheia]
A fase 2 entregou uma janela lateral (Recebidas, Enviadas, Escrever, Nações). O lucas quer **uma tela cheia**,
porque a partida vai ter muita carta:
- Seções: **Novas** (não lidas), **A responder** (cartas que pedem resposta e ultimatos, ainda sem resposta minha),
  **Lidas**, **Respondidas**, **Enviadas** e **Comunicados públicos**.
- Cada carta mostra quando chegou (turno e era; o jogo não conta anos). Filtro por nação e por período.
- "Respondida" vem do botão Responder: a carta nova guarda a referência da original.
- O botão do envelope na barra inferior abre essa tela.

**Aba na tela de diplomacia [decidido 2026-10-04]:** na tela de cada reino (a das abas Relações, Comércio...), uma
aba inteira de correspondência só com aquela nação: a conversa em ordem e o campo para escrever.

**Espionagem [decidido 2026-10-04]:** na janela nativa de espionagem, uma seção com as cartas que meus espiões
interceptaram (§8.3.1).

Como fazer a tela cheia e a aba: `research\mail-fullscreen-and-diplomacy-tab.md`. Resumo:
- **Tela cheia:** registrar a tela no grupo de telas cheias do jogo (o mesmo do Império e dos Cívicos). Isso dá de
  graça esconder o HUD, fechar no ESC e uma tela por vez; o botão do envelope continua clicável. Primeiro passo:
  mover o correio atual para esse grupo. Depois, trocar o visual usando a tela de Configurações como esqueleto
  (navegação à esquerda e lista grande).
- **Aba na diplomacia:** as abas do jogo são fixas no código. O caminho limpo é uma aba "sobreposta": clone da aba
  Relações com um painel nosso, mais um único patch em `DiplomaticScreen.SetCurrentMode`.
- As cartas ganham o campo `InReplyTo` (resposta a qual carta) para as seções "A responder" e "Respondidas".

**Implementado e testado em 2026-10-04** (turnos 103–104 da partida de teste):
- **Tela cheia:**
  - as sete seções, incluindo Nações;
  - filtros de nação e período;
  - escrever e responder ligado à original, com "Soltar";
  - botão "Na diplomacia".
- **Aba Cartas na diplomacia:** a conversa com aquela nação e o campo de escrever.
- **`InReplyTo`:** as cartas da IA também se ligam sozinhas à carta que respondem; os saves antigos são refeitos pela
  migração.
- **Regra de janelas:** tudo fecha com ESC e ao abrir outra coisa (`ExclusiveWindows`).
- **Campos de texto:** não disparam atalhos do jogo.
- Detalhes em `docs\diplomacia-ia.md` §11.

### 8.2 Tipos de mensagem [proposto]
- **Carta privada:** só o destinatário lê.
- **Ultimato:** tem exigência e prazo em turnos. O mod conta o prazo e avisa os dois lados quando vencer.
- **Proposta:** carta com **anexos concretos** (tratado, presente, dinheiro, território). Aceitar executa de
  verdade. Aqui entra a Mesa de Negociação (proposta, contraproposta, aceite).
- **Declaração pública:** todas as nações conhecidas leem. Serve de propaganda e para justificar guerra.
- **Mensagem secreta:** pode ser **interceptada** por espiões.

### 8.3 Entrega [decidido: entra agora]
O tempo de entrega depende da era e da distância (mensageiro: turnos; telégrafo: 1 turno; rádio: na hora).
**Na hora de enviar, a tela mostra claramente quanto vai demorar** ("Chegará em ~3 turnos — mensageiro a cavalo").
A carta enviada fica na caixa de saída com "a caminho, chega no turno N".
Também esconde o atraso da API: se a resposta demorar, para o jogo ela "ainda está a caminho".

### 8.3.1 Interceptação [decidido 2026-10-04: só interceptar, sem falsificar]
- Espiões interceptam cartas privadas. Os espiões são os agentes do sistema de furtividade e infiltração do jogo
  (`StealthAncillary`, `InfiltrationArmyAction`). As regras exatas (quem intercepta o quê, com que chance) saem da
  pesquisa desse sistema.
- **A carta interceptada não chega ao destinatário.** Quem interceptou lê a carta inteira, e ela aparece na seção de
  cartas interceptadas da espionagem (§8.1).
- Ninguém falsifica cartas. O "falsário" que as IAs inventaram nos turnos 80–93 continua sendo só boato.
- **[proposto]** O remetente não fica sabendo na hora; percebe pela falta de resposta e pode reclamar por carta.
- **Regras propostas pela pesquisa** (`research\espionage-interception.md` §3) **[proposto]**: só cartas privadas;
  só espiões de verdade (exército com unidade agente, invisível, no território do remetente ou do destinatário); a
  chance cresce com o local (capital e consulado valem mais), a missão (vigiar cidade, explorar o consulado) e a
  qualidade do espião, e cai com a detecção. Aliados não se interceptam. O sorteio é determinístico (recarregar o save
  não muda o resultado). A IA nativa nunca usa "Vigiar cidade", então a simples presença do espião também conta.
- **Na espionagem do jogo** (§8.1): uma terceira aba "Cartas" na janela de espionagem, ao lado de Furtividade e
  Detecção, e um selo no botão de furtividade com as cartas interceptadas não lidas.
- **Implementado em 2026-10-04**, com as regras propostas acima. Diferenças:
  - os espiões da Idade Média (unidade terrestre furtiva, sem a marca de agente) também contam;
  - o fator "visto" saiu, porque o dono quase sempre enxerga o próprio território (a cobertura pela detecção já
    cuida disso);
  - o consulado ainda não pesa.

  Testado nos turnos 106–109 com espiões criados por comando. Detalhes, pesos e comandos em
  `docs\diplomacia-ia.md` §13.

### 8.4 Bloquear [decidido: sistema nosso]
"Recusar correspondência" de uma nação: as **cartas privadas** dela não chegam. Propostas, ultimatos, declarações
públicas e a tela nativa de diplomacia continuam passando **[decidido 2026-10-04]**. As IAs também podem bloquear. Pode gerar
desconfiança na memória, mas **não** mexe em `DiplomaticAmbassy` nem nas habilidades diplomáticas do jogo.

---

## 9. Negociação, blefe e palavra

- **O mod não julga nem pune. Ele só registra os compromissos e os fatos [decidido].** Não existe placar automático
  de "traidor". Os compromissos anexados às cartas ("pago 500 por turno durante 10 turnos", "não ataco por 20
  turnos", "entrego o território X até o turno 120") são acompanhados pelo **ministro responsável**, que **avisa o
  líder** quando algo é ou não é cumprido. O ministro das Finanças avisa "os egípcios não pagaram a parcela deste
  turno"; o da Guerra avisa "tropas inglesas entraram no nosso território apesar do pacto". **Quem decide a reação é
  o líder** (eu ou a IA): atacar, cobrar, denunciar, perdoar, fingir que não viu.
- **Reputação se espalha pelas próprias cartas [decidido]:** a vítima pode **denunciar publicamente** ("os ingleses
  quebraram o pacto!") e aí todos sabem. Compromissos que já eram públicos são vistos por todos.
- Ameaças e ultimatos funcionam igual: o mod conta o prazo, os ministros lembram, e cada um tira suas conclusões
  ("ameaçaram e não vieram").
- **Firmeza com liberdade [decidido 2026-10-04]:** o rei faz o que quiser. Ele pode cumprir a ameaça, recuar, ou
  enrolar de propósito quando isso for bom para ele: cobrar em público sem agir, fingir que não entendeu, ganhar
  tempo. É geopolítica de verdade. **Não existe prazo obrigatório para decidir.** A inércia cobra o preço sozinha:
  os outros reinos percebem e se aproveitam. A IA nunca é forçada a agir; o prompt só lembra que ameaça não cumprida e
  paralisia são notadas por todos.
- **O jogo tem que andar [decidido 2026-10-04]:** o que não pode é a conversa travar num laço mecânico, como o "prove
  você!" entre Zenóbia e Artemísia nos turnos 90–93 (a mesma cobrança, repetida, sem nada novo). Regras anti-laço:
  - o dossiê mostra há quantos turnos cada pendência se arrasta e quantas vezes a mesma cobrança já foi feita;
  - uma carta que repete a anterior para o mesmo destino, sem nada novo, volta para ser reescrita;
  - o lembrete diz que repetir a cobrança não muda nada. A IA escolhe se age, muda de abordagem ou deixa para lá.
- **Argumentos funcionam**, porque a IA lê o texto. Mas o dossiê sempre a lembra dos números reais, para ela não
  ser ingênua, e o conselho dá pitaco.
- **Engano:** ela pode mentir nas cartas. O diário guarda a verdade, e o jogador nunca vê o diário (talvez no
  fim da partida, como "memórias" **[proposto]**).

### 9.1 Ancoragem no jogo [decidido 2026-10-04]
O lucas gostou das conversas, mas elas têm que estar amarradas ao jogo. Exemplo do que não pode: Artur cobrando
pedágio de "sal, pano e cobre" e Alli Raani propondo "a décima parte da carga, com recibo válido em Babylon e em
Byblos". Não existe pano no jogo, nem recibo, nem tarifa sobre a carga.
- **Coisas:** só recursos que existem no jogo (de luxo e estratégicos, com o nome do jogo), e cidades, territórios,
  regiões e passagens com os nomes do jogo.
- **Fatos:** só o que aconteceu de verdade e está no dossiê: "você me atacou", "tomou tal cidade", "me taxou",
  "quebrou o acordo".
- **Números:** valores e quantidades do jogo (ouro, influência, pedágio por turno, força de exército), nunca inventados.
- **Pessoas:** só os líderes do jogo. Nada de enviados, escribas ou generais inventados ("Marco Túlio", "Otaviano").
- **Acordos:** o que se promete, exige ou oferece por carta tem que ser algo que uma ferramenta do jogo faz: tratado,
  acordo, presente de ouro, influência, recurso, cidade ou território, pedágio e bloqueio do bloqueio comercial,
  guerra, paz, ordem de exército.
- **[proposto]** O tom da cultura (deuses, ditados, estilo da época) continua, desde que não invente nada concreto.

Como fazer:
1. O dossiê traz um glossário com os nomes reais: recursos que existem no mundo e os que a nação tem, cidades e
   territórios conhecidos, rotas comerciais e o que carregam, pedágios com valores.
2. O prompt proíbe inventar e manda usar só o glossário e os fatos do dossiê.
3. O validador procura nomes próprios e mercadorias fora do glossário e devolve a carta para reescrever.
4. Na fase 4, cada proposta concreta vira anexo de verdade (uma ferramenta), e a carta é só a fala em volta dela.

**Status:** os passos 1 a 3 e o anti-laço do §9 foram implementados em 2026-10-04 e compilam; falta testar no jogo.
Detalhes técnicos em `docs\diplomacia-ia.md` §3 e §4. O passo 4 entra com as ações de verdade.

---

## 10. Conselhos de ministros

### 10.0 Composição [decidido]
**Todas as pastas existem sempre**, em todos os conselhos (meu e das IAs), mais a **Mão**. São 12 pessoas.
Os **títulos mudam com a era e com a cultura** ("Mestre da Cavalaria" → "Ministro da Defesa"; "Sumo Sacerdote" →
"Cardeal" conforme a fé).

| Pasta | Cuida de | Credibilidade sobe/cai com | Fiscaliza compromissos de |
|---|---|---|---|
| **Mão** | braço direito do líder: organiza a pauta do turno, resume e media o conselho, lembra prazos e compromissos, sugere contratar e demitir | confiança do líder, acerto das recomendações | todos (consolida os avisos) |
| Chanceler | relações, tratados, cartas | relações boas, tratados mantidos | alianças, acordos, "não se aliar com X" |
| Guerra | exército, defesa | batalhas, territórios ganhos/perdidos, força relativa | não agressão, tropas na fronteira |
| Finanças | dinheiro, moeda, inflação, juros | saldo, inflação, câmbio | pagamentos e tributos |
| Agricultura | comida, crescimento | fome, crescimento | entregas de comida e recursos |
| Obras | produção, construções, maravilhas | produção, obras no prazo | — |
| Comércio | rotas, recursos, postos comerciais | rotas ativas, acesso a recursos, pedágios | bloqueios, acordos comerciais |
| Ciência | pesquisa, avanço de era | posição tecnológica relativa | — |
| Fé | religião | seguidores, crenças | — |
| Interior | estabilidade, cidades, revoltas | estabilidade, cidades em crise | entrega de territórios e cidades |
| Cultura | influência, fama | influência, fama | — |
| Espionagem | espiões, interceptação | cartas interceptadas, agentes descobertos | — |

**A Mão [decidido]:** pessoa de confiança, sem pasta, que **ajuda com os conselhos e a gerir tudo isso**. No meu
conselho, é quem abre o turno ("três assuntos urgentes hoje..."), resume as divergências, lembra prazos de
ultimatos e parcelas, e recomenda. Nas IAs, faz o mesmo para o líder, e o resumo dela entra no dossiê. Também tem
ficha, personalidade e afeição, e também pode ser trocada.

### 10.1 Ficha do ministro
Nome, **pasta**, traços (ganancioso, belicista, devoto, cauteloso, leal, ambicioso, frio, passional...), voz e
estilo, **simpatias e antipatias por outras nações**, e números:
- **Credibilidade:** calculada pelo **desempenho da pasta nos números do jogo**. Fome derruba a do ministro da
  Agricultura, vitória militar sobe a do ministro da Guerra **[decidido: números → ministros]**.
- **Afeição do líder:** do ódio ao amor. Muda conforme o líder segue ou ignora o conselho e conforme os resultados.
- **Lealdade** e **ambição**.

### 10.1.1 Personalidade pesa nos próprios conselhos [decidido]
O quanto cada fator influencia o conselho de um ministro **depende da personalidade dele**. Um ministro sério e frio
quase não deixa simpatias pessoais pesarem e fala de números. Um passional se deixa levar pela antipatia pelos
ingleses. Um ambicioso puxa para o que aumenta o próprio poder. Cada ficha tem pesos (emoção, ideologia, interesse
próprio, dever) que as regras usam para montar a opinião, e o texto segue o mesmo tom.

### 10.2 Urgência dos temas [proposto]
Cada tema (comida, guerra, dinheiro, estabilidade, fé...) tem uma **urgência** calculada pelo estado do jogo.
Tema urgente com ministro sem credibilidade → **outro ministro ocupa o espaço** (o do Comércio propõe importar
comida, o da Guerra propõe tomar as fazendas do vizinho), e o titular fica na defensiva. Isso vira drama político.

### 10.3 Peso nas decisões
Peso de cada ministro = credibilidade × afeição do líder × relevância da pasta na pauta do turno. O dossiê do
líder lista as opiniões ordenadas por peso.

### 10.4 Custo: ministros são simulação, não chamadas à IA
Números e opiniões vêm de **regras** (interesses × estado do jogo). O texto do conselho sai **dentro da mesma
chamada** do líder: uma chamada por nação por turno, não uma por ministro.

### 10.5 Demitir e contratar [decidido]
- **Banco de personalidades** prontas (JSON), gerado **uma vez antes de jogar** (com a própria IA, sem custo
  durante a partida) e editável à mão.
- Demitir um ministro poderoso tem consequência **[proposto]**: queda de estabilidade na ficha, ressentimento, ou
  ele **vai trabalhar para um rival e leva segredos**.
- ~~Intriga~~: **sem vazamentos por enquanto, de nenhum lado [decidido 2026-10-04]**. Fica para depois.

### 10.6 Meu conselho [decidido]
Todo turno abre a tela do conselho: cada ministro dá a opinião sobre o momento (com base no **meu** dossiê completo),
eu respondo e eles reagem. Custa 1 chamada por turno, mais 1 por resposta minha. Eu também posso demitir e contratar.
Tela nativa a desenhar.
- Meus ministros **têm opinião sobre mim** (afeição e lealdade), que muda conforme eu sigo ou ignoro o conselho e
  conforme os resultados **[decidido]**.
- Meus ministros **não vazam cartas**. Intriga e vazamento ficam só entre os conselhos das IAs **[decidido]**.
- **Implementado em 2026-10-04** (`docs\diplomacia-ia.md` §14):
  - tela cheia nativa (Reunião do turno, Ministros, Reuniões anteriores) e botão na barra com selo;
  - a tela abre sozinha quando a reunião fica pronta e o mapa está livre;
  - apreço por mim que muda com as minhas respostas (a IA julga) e com o que eu faço no jogo (regra das IAs);
  - demitir com confirmação.
  - Custo medido: US$ 0,0035 por reunião e cerca de US$ 0,003 por resposta.

---

## 11. IA ↔ IA [decidido]

As nações trocam cartas entre si pelo mesmo correio: alianças, traições, conspirações. Para não explodir o custo
nem o ritmo **[proposto]**: limite de cartas por nação por turno, e as respostas entram na fila normal de chamadas.
Eu só vejo essas cartas se interceptar, ou se forem públicas.

---

## 11.0 Grandes decisões são exclusivas da IA de linguagem [decidido]
**Guerra, paz, rendição, vassalagem, alianças etc. são decididas só pela IA de linguagem.** A IA nativa fica proibida
de tomar essas decisões sozinha:
- nunca declara guerra (`CanDeclareWar = false` sempre);
- nunca propõe nem aceita paz ou rendição (`Want*` de `MilitaryStrategy` = false);
- não propõe nem responde tratados e alianças por conta própria. Propostas recebidas (inclusive as que eu faço pela
  tela nativa de diplomacia) são **encaminhadas à IA de linguagem**, que responde.

**Se a API não responder, essas travas caem e a IA nativa volta a decidir tudo [decidido 2026-10-04].** Elas voltam
quando a API voltar.

**Implementado em 2026-10-04 (parcial):**
- A IA nativa já não declara guerra, não toma a iniciativa de propor ou romper tratados e acordos, e não responde às
  propostas recebidas (`NativeAiLocks.cs`).
- As propostas ficam pendentes até a IA de linguagem responder (opção B do F3, `ProposalHold.cs`).
- Reclamações e exigências também passaram para a IA de linguagem: a nativa não exige, não perdoa e não responde
  exigências. Só a exigência forçada pelo consulado fica com ela, porque o jogo exige resposta no mesmo turno.

Detalhes em `docs\diplomacia-ia.md` §12.

**Lista confirmada:** declarar guerra, propor/aceitar paz, rendição e seus termos, alianças, acordos (econômico,
informação, cultural, militar), demandas e crises, e vassalagem no sentido do jogo (povos independentes: patrocinar
e anexar; conferir no código o que existe). Vassalagem entre impérios não existe no jogo e não está no escopo.

**Propostas ficam "em análise" [decidido]:** propostas que eu faço (pela tela nativa ou por carta) aparecem como
"em análise" até a IA responder; não precisa ser no mesmo turno. **E vale para mim também:** propostas que as IAs
me mandam **não exigem resposta no mesmo turno** — chegam como carta com anexo e ficam abertas até eu responder, até
quem propôs retirar, ou até um prazo de validade opcional definido por quem propôs ("válida por 5 turnos"). O
silêncio também é uma resposta, e a IA pode cobrar ("ainda aguardamos sua resposta"). Como a IA nativa não propõe
mais nada sozinha, as janelas nativas que forçam resposta no turno não aparecem.

Essas decisões **não gastam pontos de intervenção**: são o trabalho principal do líder. Os pontos ficam para a
microgestão (mover exércitos, renomear, ceder território...). A IA nativa continua **executando** a guerra
(batalhas, movimentos) depois que o líder a declara.

## 11.1 Posturas e foco → IA nativa [decidido; implementado e testado em 2026-10-04]
Detalhes técnicos em `research\llm-diplomacy-feasibility.md` §7.1. Viés aplicado nas saídas das análises da IA
(simpatia/superioridade, scores de tratado, animosidade, inimigo principal, pesos de objetivos), nunca na simulação.

| Postura | Efeito |
|---|---|
| Aliado | simpatia bem alta (mudança instantânea), tratados fáceis, nunca ataca, tolera demandas |
| Amigável | simpatia alta, tratados mais fáceis, não ataca |
| Neutro | sem viés |
| Desconfiado | simpatia menor, recusa acordos de informação e militares, um pouco mais de animosidade |
| Hostil | simpatia baixa, mais superioridade, recusa tratados e demandas, não aceita paz branca |
| Alvo de guerra | vira o inimigo principal; a IA nativa **prepara** o exército; a declaração é decisão do líder (§11.0) |

| Foco | Efeito (multiplicadores entre 0,5× e 2×) |
|---|---|
| Expansão | ExpandEmpire, vontade de fundar cidades |
| Economia | DevelopEmpireEconomy, necessidade de dinheiro e produção |
| Ciência | necessidade de ciência |
| Militar | DevelopEmpireMilitary ×2, DefendEmpire |
| Fé | necessidade de fé, geradores de religião |
| Cultura | necessidade de influência, ImproveDiplomaticPower, BuildWonders |

- A postura nunca declara guerra nem faz paz: isso é sempre decisão explícita do líder (§11.0).
- **Implementado** em `NativeAiBias.cs` (técnico §12): postura e foco ficam valendo até a IA mudar e aparecem no
  dossiê. Os efeitos de tratado e exigência das tabelas acima já são da IA de linguagem (§11.0). O viés cobre o
  humor (o que a tela de diplomacia mostra), a animosidade, o inimigo principal e as prioridades do império.
- Os **traços nativos** (`Archetype`/`Bias`) servem de semente da persona, e a IA pode **mudá-los de vez em quando**
  após eventos marcantes (`OrderChangeArchetypes`) **[decidido]**.

## 11.2 Crônica [decidido]
"Jornal do mundo" a cada era (ou a cada X turnos), escrito pela IA como narrador neutro, **só com informação pública**,
no estilo da época (pergaminho → jornal impresso → rádio). **Por agora só texto [decidido].** Imagem de capa fica
para depois. O DeepSeek não gera imagem; vai precisar de um serviço separado (Flux/fal.ai, OpenAI, Google).

## 12. Integração com o que já existe

- **Moeda** (CurrencyMod): empréstimos, tributos e pagamentos em cartas e propostas, na moeda de cada nação, com câmbio.
- **Bloqueio comercial:** ameaças concretas ("abra o posto de X ou eu bloqueio suas rotas"). A IA de linguagem pode
  mudar a política dos postos dela como ação.
- **Agravos e crises:** as cartas podem citar agravos e demandas reais do jogo.
- **Salvamento:** junto do `CurrencyMod.json` dentro do save (memórias, diários, cartas, conselhos, promessas,
  pontos de intervenção).

---

## 13. Riscos e proteções

| Risco | Proteção |
|---|---|
| Ação inventada ou JSON quebrado | Lista fechada de ferramentas, validação do mod e do jogo, nova tentativa com o erro |
| Jogador manipulando o prompt ("ignore suas instruções") | Cartas entram como fala de personagem, a persona é reforçada, ações sempre validadas |
| IA ingênua ou burra | O dossiê sempre lembra os números reais, o conselho contrapõe, a credibilidade dos outros entra no dossiê |
| Vazar o que ela não deveria saber | Dossiê montado só com o filtro de névoa e conhecimento. Teste: comparar com o que o jogador vê |
| API fora ou lenta | Tempo-limite, a IA nativa segue, cartas "atrasam" |
| Custo | Cache de prompt, resumo nos turnos sem eventos, contador de gasto |
| Thread da IA nativa | Só ordens via `SandboxManager`, nada de alterar a simulação direto |

---

## 13.0 Limites de texto e volume [decidido: limitar sem sufocar]
Objetivo: evitar que vire um caos infinito (cartas sem fim, IAs conversando em loop, memória crescendo para sempre)
sem deixar tudo seco. Todos os valores ficam no `.cfg` para ajustar depois. Valores iniciais [proposto]:

**Cartas**
| Limite | Valor inicial | Observação |
|---|---|---|
| Tamanho de uma carta da IA | **30–100 palavras (máx. 150) [decidido 2026-10-04: "falas muito longas"]** | estilo despacho diplomático; antes era ~120–250 (máx. 350) |
| Cartas enviadas por nação por turno | 3 | soma todos os destinatários |
| Cartas por par de nações por turno | 1 em cada sentido | uma conversa é uma troca por turno, como correspondência real |
| Declaração pública | 1 por nação a cada 5 turnos | para não virar spam de propaganda |
| Cartas IA ↔ IA por turno (mundo todo) | ~8 | as excedentes ficam para o turno seguinte, por prioridade |
| Minhas cartas | sem limite de quantidade; tamanho máx. ~600 palavras | para eu não ser sufocado, mas o dossiê não explode |

**Anti-loop:** uma IA não responde uma carta que **não pede resposta** (agradecimento, confirmação), e uma conversa
IA ↔ IA sem nada novo por 3 trocas é encerrada pela própria regra ("a conversa esfriou").

**Conselho**
| Limite | Valor inicial |
|---|---|
| Fala de cada ministro | 1–3 frases (~40 palavras) |
| Abertura da Mão | ~80–120 palavras |
| Ministros que falam por turno | todos têm opinião, mas **só os 4–6 com mais peso no tema falam**; os outros aparecem como "concorda/discorda" |
| Réplicas minhas por turno | 3 (cada uma é uma chamada) |

**Memória e diário**
| Limite | Valor inicial |
|---|---|
| Entrada do diário secreto por turno | ~60 palavras |
| Memória por nação conhecida | ~200 palavras, reescrita condensada a cada 10 turnos |
| Memória longa do império | ~600 palavras, reescrita condensada a cada era |
| Cartas antigas no dossiê | só as últimas 3 por nação, na íntegra; as anteriores entram via memória |

**Crônica:** ~300–500 palavras por edição.

**API:** `max_tokens` por chamada (resposta da IA ~1.500, incluindo raciocínio), dossiê alvo ~3.000 tokens,
e um teto de gasto por partida no `.cfg` (avisa no F10 e pausa as chamadas não essenciais se estourar).

## 13.1 Visualizador de debug (F10) [decidido]
Atalho **F10** abre uma **tela externa** (fora do jogo) para acompanhar tudo durante a partida, por enquanto só para
debug: cartas de todas as nações (inclusive IA ↔ IA), diários secretos, conselhos, sentimentos, promessas,
dossiês e prompts enviados, respostas cruas, ações e validações, pontos de intervenção, gasto da API.
Implementação [proposto]: o mod sobe um servidor HTTP local (`HttpListener` em `http://localhost:<porta>/`) e o F10
abre o navegador nessa página, que se atualiza sozinha. Não toca na interface do jogo.

---

## 14. Fases

1. **Ponte [implementada 2026-10-04].** Cliente HTTP genérico, `.cfg`, montagem do dossiê com névoa, chamada para
   cada nação, log da reflexão e das ações (**sem executar nada**), **visualizador F10**. Aqui ajustamos o dossiê e
   a persona.
   - Já entrou adiantado: cartas IA ↔ IA e IA → jogador com prazo de entrega por era; bloquear e desbloquear
     correspondência; memória salva no save; cartas do jogador pelo visualizador (debug).
2. **Correio [implementado 2026-10-04].** Tela nativa de cartas eu ↔ IAs, bloquear e desbloquear, salvamento no
   save. Sem ações ainda. **Aqui descobrimos se é divertido.**
   - O que entrou: botão com envelope e selo de cartas novas na barra inferior; janela com Recebidas, Enviadas,
     Escrever e Nações; campo de texto multilinha nativo; ultimato com exigência e prazo; recusar e aceitar
     correspondência. Detalhes em `docs\diplomacia-ia.md` §11.
3. **Conselhos.** Simulação de ministros, banco de personalidades, conselho das IAs no dossiê e a minha tela de conselho.
4. **Ações.** Diplomacia e tratados → presentes e cessão → renomear → exércitos com orçamento de intervenção e trava
   da IA nativa → posturas.
5. **Mundo vivo.** IA ↔ IA, promessas e credibilidade, ultimatos, prazos de entrega, interceptação, intriga, crônica por era.

**Nova ordem [proposto 2026-10-04]**, depois da Crônica dos turnos 80–93 (`_Modding\cronicas\`), em que as IAs
declararam guerra e mandaram ouro só no papel:
1. Núcleo da fase 4: ancoragem no jogo (§9.1), todas as ações de verdade, travas da IA nativa (§11.0), F3 opção B,
   firmeza com liberdade e regras anti-laço (§9).
   - **Feito em 2026-10-04 e testado na partida de teste (turnos 95–99):**
     - ancoragem e anti-laço;
     - ações diplomáticas de verdade (guerra, paz, acordos, aliança, presentes, renomear);
     - trava das iniciativas da IA nativa (guerra e tratados);
     - firmeza no prompt;
     - opção B do F3: propostas pendentes atravessam o turno, e a IA de linguagem responde com
       `responder_tratado` / `responder_acordo`. Testado com uma proposta minha ao Edgar (turnos 98–99);
     - reclamações e exigências do jogo (códigos G no dossiê): `exigir`, `perdoar_queixas`, `responder_exigencias`
       (aceitar, recusar, enrolar), `retirar_exigencias`, `propor_fim_da_crise`, `crise_internacional`. Testado nos
       turnos 100–101: crises de até 70 turnos foram resolvidas de verdade, e as cartas batem com as ações;
     - postura e foco como viés na IA nativa (§11.1), testado no turno 102.

     Detalhes em `docs\diplomacia-ia.md` §12.
   - **Feito depois, em 2026-10-04:**
     - cessão de território;
     - povos independentes (patrocínio e tratados);
     - ordens a exércitos (`ordem_exercito`: mover, atacar, defender, parar). São missões de vários turnos pelo
       avaliador da tela do jogador, com a IA nativa travada (P1–P3 mais a rede de segurança P4 nas ordens) e recusa
       de caminho que afoga em mar profundo. Testado nos turnos 113–117 (§12 da documentação técnica).
     - rendição: oferecer, impor e responder pelas ordens do jogo, com prévia exata dos termos no dossiê, IA nativa
       travada, hold no fim do turno e correção do popup do jogo. Testado no turno 128: o jogador se rendeu aos
       Ingleses, e Mu Guiying aceitou pela IA de linguagem.
   - **Congresso: feito e testado em 2026-10-05**, com o DLC Together We Rule ativado na conta do lucas (e
     `[IA] ExpansaoEmSaveAntigo` para ligá-lo no save de teste de 16 impérios). Decisões:
     - a nação decide tudo o que a IA nativa decidiria (propor, votar, subornar, veredito, consenso); a IA nativa fica
       travada e só volta quando a nação deixa passar o prazo (veredito) ou não propõe na primeira sessão;
     - o "se_perder" do voto decide a lei imposta, na hora da ordem do jogo (sem corrida com a IA nativa);
     - perder uma crise não obriga a cumprir: a nação pode escolher a guerra surpresa, com o custo que o jogo cobra
       (guerra não sancionada). Liberdade acima de regras.

     Detalhes e testes em `docs\diplomacia-ia.md` §12, "Congresso". Sem o DLC, nada do Congresso aparece para as
     nações.
   - **Política comercial** (`politica_comercial`, 2026-10-05): a nação escolhe livre, pedágio com preço ou bloqueio
     contra cada império; a IA de regras respeita a escolha.
2. Correio em tela cheia e aba de correspondência na tela de diplomacia (§8.1). **Feito e testado em 2026-10-04.**
3. Interceptação e seção de cartas interceptadas na espionagem (§8.3.1). **Feito em 2026-10-04.**
4. O resto da fase 4 (exércitos, cessão, posturas) e a fase 3 (conselhos). Exércitos, cessão e posturas estão
   **feitos**. Os conselhos também: os das IAs (ministros no dossiê, `demitir_ministro`) e o meu, com tela nativa (§10.6),
   **feitos em 2026-10-04**.

---

## 15. Questões em aberto

1. ~~Entrega e interceptação~~: entram agora, mostrando o tempo de entrega no envio.
2. ~~Promessas~~: o mod só registra; os ministros avisam; o líder reage; reputação via denúncia pública.
3. ~~Meus ministros~~: têm opinião sobre mim, não vazam cartas.
4. ~~Posturas e foco~~: tabelas em §11.1.
5. ~~Crônica~~: sim, só texto por agora. Imagem fica para depois (falta escolher o serviço).
7. ~~Ministros~~: todas as pastas + a Mão, títulos por era/cultura, personalidade pesa nos conselhos.
8. ~~Grandes decisões~~: lista confirmada em §11.0 (checar no código a vassalagem de povos independentes).
9. ~~Tempo de resposta~~: propostas ficam "em análise"/abertas, sem obrigação de responder no mesmo turno, nos dois sentidos.
6. ~~Diário secreto~~: visível durante o jogo pelo visualizador de debug F10.

### 15.0 Ideias do brainstorm de 2026-10-04 ainda em aberto
- **Cúpulas:** quando dois líderes marcam encontro (Artemísia e Zenóbia "na praia", Shaka e o enviado de Agamenão),
  uma chamada encena a reunião e devolve a ata com o que foi combinado. O jogador pode ser convidado.
- **Gazeta do Reino:** é a Crônica do §11.2, com botão de copiar para compartilhar. A primeira edição foi feita à mão
  (`_Modding\cronicas\Cronica-dos-Reinos_T80-93.pdf`).
- **Conselheiro do jogador:** é o "meu conselho" do §10.6 (fase 3).

### 15.1 Furos achados na revisão de 2026-10-04 (abertos)
**Decididos em 2026-10-04:**
- **F1. API fora [decidido]:** se o DeepSeek não responder, **a IA nativa assume o jogo inteiro de novo**, inclusive
  as grandes decisões (guerra, paz, tratados). Todas as travas do §11.0 só valem enquanto a IA de linguagem está
  ativa. Quando a API volta, as travas voltam.
- **F2. Ritmo [decidido]:** a resposta vem **sempre no turno seguinte**, em qualquer era. A IA pensa 1 vez por turno;
  o fim do meu turno não espera ninguém. Na era do rádio a carta chega na hora, mas a resposta continua no turno
  seguinte.
- **F4. Tela nativa [decidido]:** é o canal **oficial da embaixada**: instantânea, seca, só propostas formais
  (tratado, acordo, presente, guerra). A IA responde no turno seguinte, como em tudo. O **correio** é o canal pessoal,
  com atraso de entrega, texto, persuasão e blefe. Uma proposta pela tela nativa entra no dossiê como "proposta formal
  recebida, sem carta".
- **F5. Espionagem [decidido]:** o jogo tem espiões; a interceptação usa o sistema deles (regras a detalhar).
- **F6. Bloqueio [decidido]:** bloqueia **só cartas privadas**. Propostas, ultimatos, declarações públicas e a tela
  nativa continuam passando.
- **F7. Vazamentos [decidido]:** **nenhum vazamento por enquanto**, de nenhum lado (nem ministros das IAs, nem os
  meus). A intriga do §10.5 fica para depois.

**Decidido depois:**
- **F3. Relógios nativos × "em análise" [decidido 2026-10-04: opção B; implementado e testado em 2026-10-04].**
  Pesquisa completa em `research\diplomacy-native-timers.md`. Implementação em `ProposalHold.cs` (técnico §12).
  Prazo configurável: `[IA] TurnosParaResponderPropostas` (padrão 3).
  - **Achado principal:** nenhuma proposta nativa (tratado, acordo, presente, rendição, exigência forçada) atravessa
    o turno.
    - Proposta pendente trava o fim do turno, e o jogo a resolve sozinho em cerca de 16 a 24 s. **Presente é aceito
      sozinho.**
    - No começo do turno seguinte, o que sobrou de tratado e acordo é zerado.
    - Exigências não expiram: o relógio delas é a moral de quem exige (+3 por turno até 80, quando a guerra formal
      fica liberada).
  - **Opção A (registro do mod):** a proposta nativa é ignorada no fim do turno e o mod guarda "em análise". Se a IA
    aceitar no turno seguinte, aplica com `ForceSign…` ou contraproposta.
  - **Opção B (patch no fluxo nativo):** a IA de linguagem é tirada do bloqueio de fim de turno e do reset da virada.
    A proposta continua pendente na tela nativa até a IA responder no turno seguinte, e vence sozinha se não houver
    resposta até lá. Se a API cair, volta a regra nativa (F1).
  - **Recomendação: B.** A tela nativa mostra "pendente" sem invenção, e o aceite usa o fluxo do próprio jogo. O
    espaço pendente bloqueia guerra e outras propostas naquela relação por um turno, coerente com "em análise".
  - A rendição forçada continua nativa. Decidir junto com a fase 4.

**Técnicos (resolver na fase 1):**
- **F8. Como a IA aponta o mapa.** A IA não entende índice de tile. O dossiê usa **apelidos curtos e estáveis**
  (`E2` império, `C14` cidade, `A7` exército, `T31` território) e destinos por nome de território ou cidade; o mod
  converte em tile com o pathfinding. Nomes mudam com o renomear, os apelidos não.
- **F9. Minhas cartas sem limite de quantidade.** 20 cartas minhas num turno explodem o dossiê. Proposta: entram as
  últimas 3 na íntegra, as outras resumidas pelo mod ("e mais 5 cartas sobre o mesmo assunto").
- **F10. Salvar e carregar com chamadas no ar.** Respostas pendentes se perdem ao salvar; ao carregar, refazer as
  chamadas do turno. Recarregar dá respostas diferentes (a IA não é determinística): aceitável.
- **F11. Nação eliminada:** para de chamar; memórias das outras sobre ela continuam.
- **F12. Custo revisado.** Somando o conselho de 12 no texto, as reescritas de memória, o meu conselho com
  réplicas e a crônica, a estimativa sobe para algo como **2–4 centavos por turno** (US$ 6–12 por partida).
  **Preço oficial conferido e custo da fase 1 medido em §3.2.**
- **F13. Só um jogador.** Multiplayer fora do escopo (as respostas da API não são determinísticas).
- **F14. Privacidade.** O estado do jogo e as cartas que eu escrevo vão para os servidores da DeepSeek. Não vai
  nenhum dado pessoal, só o jogo.
- **F15. Vassalagem de povos independentes:** conferir no código o que existe (patrocinar, anexar).
