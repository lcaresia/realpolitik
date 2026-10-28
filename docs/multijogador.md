# Realpolitik no multijogador — pesquisa e desenho

Estado: desenho **aprovado** (2026-10-28); **fase 1 (economia + comércio) escrita e compilada, sem commit**, testada só
em parte (ver §7).
Pesquisa completa (em inglês, com caminhos do código decompilado): nota `mp-sync-research.md` da sessão; o resumo
está abaixo.

## 1. Como o multijogador do Humankind funciona

- **Lockstep determinístico com o anfitrião ordenando as ordens.** Todo jogador roda a simulação inteira (`Sandbox`).
  Cada ação vira uma *ordem*; o anfitrião (`SandboxRole.Authority`) valida, numera (`Serial`) e manda para todos
  (`ProcessOrderMessage`); cada máquina aplica na mesma ordem. Nenhum estado é copiado, só as ordens.
  O single-player usa o mesmo caminho com um "anfitrião local".
- **A IA nativa roda só no anfitrião** e age postando ordens (que se replicam).
- **Não dá para criar tipos de ordem novos**: o tipo vai como número de um enum fixo (`OrderIdentifier`); um número
  desconhecido quebra quem recebe.
- **Detecção de dessincronização:** a cada turno, depois do autosave, cada máquina tira um MD5 do Sandbox serializado
  e manda ao anfitrião. Se diferir, a luz fica vermelha e o anfitrião escolhe "Continuar / Ressincronizar"
  (recarrega o último autosave para todos). Nada é corrigido sozinho.
- **Turnos simultâneos.** O fim do turno (incluindo `TurnEndPass_CollectMoney`, onde a economia do mod roda) é
  executado em **todas** as máquinas. O anfitrião espera a IA (`IsAIBusy`) antes de fechar o turno.
- **Saves:** cada máquina grava o próprio autosave. Quem entra no meio recebe do anfitrião só descritor + sandbox + IA
  e repete as ordens desde o último autosave. **Os arquivos extras do mod (`CurrencyMod.json`, `DiplomaciaIA.json`)
  não chegam a quem entra.**
- **Aleatoriedade:** sempre semeada por `WorldSeed + Turn + GUID`; números do jogo em `FixedPoint`.

### Canais que o mod pode usar

| Canal | Ordenado com o jogo? | Uso |
|---|---|---|
| **`OrderChangeGameOption`** (ordem do jogo com nome + texto livre; `GameOptionsController` grava em `Sandbox.Metadata`) | **Sim** — validado pelo anfitrião, aplicado por todos na mesma ordem, repetido para quem entra | **Tudo que muda o jogo**: entradas dos jogadores e decisões da IA. Patch Harmony na validação/processamento para nomes com prefixo `rp.` |
| Mensagem de rede própria (`Message` registrada por reflexão) | Não | Dados grandes sem efeito no jogo (memória da IA para quem entra) |
| Chat do lobby com protocolo próprio (`rp://...`) | Não | Aperto de mão de versão do mod |
| Metadados da sessão (anfitrião → clientes) | Não | Versão/config do anfitrião no lobby |

Ordens só são aceitas na fase principal do turno (TurnMain). Para o anfitrião segurar o turno enquanto a IA do LLM
pensa, o caminho é manter um império de IA "não pronto" durante o TurnMain.

## 2. O que o mod faz hoje no multijogador

Desliga tudo: ~27 arquivos checam `SavePatches.IsGameOnline()`. Botões somem, a economia não roda, a IA do LLM não
age, nada é salvo. (Pesquisa aponta que `SpendingFloors` e partes de `NativeAiBias` talvez não estejam protegidos —
conferir.)

## 3. Regra geral do desenho

O mod tem três tipos de coisa, e cada uma tem um jeito de sincronizar:

1. **Simulação determinística** (mexe em números do jogo): economia, câmbio, crédito, estabilidade, pedágio/bloqueio,
   centros comerciais, efeitos de veteranos. Roda **em todas as máquinas**, calculada só a partir do estado do jogo e
   de entradas já sincronizadas. Nada de `System.Random()`, relógio, config local ou texto do LLM aqui dentro.
2. **Entradas** (alguém decidiu algo): o jogador mexe nos juros, muda pedágio, nomeia ministro, vota no bloco; a IA do
   LLM manda uma carta, propõe moção, muda postura comercial. Viram **ordens do mod** (`OrderChangeGameOption` com
   nome `rp.<ação>` e JSON), aplicadas por todos na mesma ordem. Ações da IA no jogo (guerra, tratados, exércitos) já
   são ordens nativas e continuam assim.
3. **Coisas locais** (só tela/dinheiro de quem usa): telas, idioma, chave de API, relatório de erro. Ficam em cada
   máquina e nunca entram na simulação.

Consequências:
- **Todos os jogadores precisam do mod, na mesma versão.** Não é escolha: os patches mudam a simulação; quem não tem
  o mod calcula outros números e dessincroniza no primeiro turno. O mod confere a versão no lobby e, se alguém não
  tiver ou tiver outra versão, avisa e fica desligado na partida (como hoje).
- **A config que mexe na simulação é a do anfitrião** (`EconomyConfig` etc.), mandada a todos no início da partida e
  gravada junto do estado. A config local dos clientes vale só para telas e atalhos.
- **Matemática:** a economia usa `double`. Em Windows x64 (SSE2) soma/multiplicação são iguais em todas as máquinas;
  `Math.Pow/Exp/Log` dependem da biblioteca C. Proposta: manter `double` e trocar só as funções transcendentais por
  versões próprias determinísticas; o hash do estado do mod (abaixo) denuncia qualquer diferença. (Mac/Linux: o jogo
  tem versão Mac; tratar como risco a testar.)
- **Hash do mod por turno:** cada máquina calcula um hash do estado determinístico do mod no fim do turno; se alguém
  divergir, o mod avisa e grava log (o hash do jogo só pega o efeito, não a causa).

## 4. Funcionalidade por funcionalidade

| Funcionalidade | Onde roda | Como sincroniza | Estado a replicar / salvar |
|---|---|---|---|
| **Moeda, inflação, juros, câmbio, crédito** (`EconomySimulation`, `NativeEffects`, `TransferConversion`) | todas as máquinas | determinístico; juros manuais e nome da moeda viram ordens `rp.bank.*` do dono do império | `CurrencyWorld` (pequeno) |
| **Banco Central (F8)** | tela local de cada jogador | lê o estado local (igual em todos) | — |
| **Bloqueio/pedágio, rede comercial, centros, rotas** (`Trade/*`) | todas | política por posto vira ordem `rp.trade.policy`; a prévia (TollPreview) é local | tabela de políticas e preços |
| **IA do LLM: nações, diários, posturas** (`AiModule`, `ActionExecutor`, `ArmyOrders`) | **só no anfitrião** (chamadas e decisão) | ações no jogo = ordens nativas (já replicam); o que muda o estado do mod (posturas comerciais, travas, cartas) vira ordem `rp.ai.*` | memória da IA (grande) |
| **Travas/viés da IA nativa** (`NativeAiLocks`, `NativeAiBias`, `SpendingFloors`) | só no anfitrião (onde a IA nativa roda) | não precisa: o resultado sai como ordem | — |
| **Congresso** (`CongressFlow`) | anfitrião decide pela IA | votos de IA = ordens nativas; as `EditorOrder*` usadas hoje precisam ser conferidas (o anfitrião pode não aceitá-las no MP) | — |
| **Cartas / Correio (F2)** | anfitrião gera as cartas da IA; cada jogador escreve as suas | carta = ordem `rp.mail.send` (texto em pedaços se for grande); resposta da IA chega como outra ordem | cartas |
| **Conselho/ministros (F7)** | **na máquina de cada humano, com a chave dele** (D1); sem chave, o conselho fica desligado só para ele | o texto do LLM fica local; tudo que mexe no jogo vira ordem daquele jogador: nomear/demitir (`rp.council.*`), custo em influência (`OrderGainInfluence`), efeitos de veteranos (os números entram no estado replicado pela ordem, nunca calculados a partir do texto). O conselho da IA (ministros das nações) roda no anfitrião | ministros e efeitos: replicados; falas e reuniões: só no save de quem é dono |
| **Blocos e Alianças (F3)** | lógica em todas as máquinas, aplicada por ordem | convite, voto, moção, eleição = ordens `rp.bloc.*` de humanos e da IA; defesa mútua dispara ordens nativas | blocos, moções, livro de acusações, tratos |
| **Espionagem / cartas interceptadas** | idem cartas | determinística a partir das cartas replicadas (sorteio com semente do jogo) | — |
| **Nomes nativos** (`NativeNames`) | todas | derivados do estado replicado | — |
| **Tela "Diplomacia IA" (provedores)** | em cada máquina: no anfitrião configura as nações; nos clientes, só o conselho do próprio jogador | — | credenciais nunca saem da máquina |
| **Traduções** | local | cada jogador vê a interface no próprio idioma | — |
| **Relatar um problema (F4)** | local | cada jogador envia os próprios logs; o anfitrião anexa o log da IA | — |

### Save e quem entra no meio

- Cada máquina continua gravando `CurrencyMod.json` + `DiplomaciaIA.json` no próprio save (o estado é igual em todas).
- **Quem entra no meio / ressincronização:** os arquivos do mod não vêm junto. Duas opções a validar num teste:
  (a) guardar o estado **determinístico** pequeno em `Sandbox.Metadata` (onde `OrderChangeGameOption` grava), que já
  vai junto no sandbox e entra no hash do jogo; (b) o anfitrião manda o estado por mensagem de rede própria logo depois
  do carregamento. Memória da IA (grande) sempre pela opção (b) — ou o cliente não precisa dela inteira: só as cartas
  e conselho dele.
- Carregar um save de MP: todos carregam o mesmo arquivo do anfitrião? Não — cada um tem o seu; o anfitrião manda o
  sandbox. Por isso o anfitrião manda também o estado do mod (b) ao começar.

### Tempo do turno

O LLM leva segundos. No MP isso trava todo mundo. Proposta: o anfitrião dispara as chamadas no começo do turno (como
hoje), segura o fim do turno (império de IA "não pronto") até no máximo **N segundos** (config, padrão 60); passou disso,
a nação age no próximo turno. O cronômetro de turno do jogo continua valendo.

## 5. Decisões

Perguntas para o Lucas (uma por vez); as outras seguem o padrão abaixo salvo se ele mudar.

| # | Decisão | Recomendação | Estado |
|---|---|---|---|
| D1 | Quem paga e roda as chamadas do LLM | anfitrião paga as nações da IA (e as respostas delas às cartas dos humanos); **cada humano usa a própria chave para o próprio conselho** | **decidido 2026-10-28 (Lucas)** |
| D2 | Humanos participam de Congresso e blocos | sim, votam e propõem como as nações da IA | padrão |
| D3 | Cartas entre humanos pelo Correio | sim (sem LLM, custo zero) | padrão |
| D4 | Idioma do texto da IA | idioma do anfitrião para todos (uma chamada só) | padrão |
| D5 | Mod obrigatório e mesma versão em todos | sim (exigência técnica) | fixo |
| D6 | Espera máxima pelo LLM no fim do turno | 60 s, configurável pelo anfitrião | padrão |
| D7 | Ordem de entrega | fase 1 economia + comércio; fase 2 IA do LLM, cartas, Congresso; fase 3 conselho, blocos, entrar no meio | padrão |

## 6. Riscos e testes necessários

- `OrderChangeGameOption` com nome desconhecido: conferir num jogo MP real (dois PCs ou duas contas) que o patch
  aceita, replica e não quebra o painel de opções nem o cronômetro de turno.
- Limite de tamanho do texto da ordem (cartas longas): medir; se preciso, pedaços.
- `EditorOrder*` usados pelo Congresso e comandos de dev: provavelmente recusados no MP; trocar por ordens normais.
- Teste de MP precisa de duas instâncias do jogo (dois PCs ou Steam + segunda conta); definir como testar antes da fase 1.

## 7. Fase 1 — o que foi feito (2026-10-28)

Código novo em `src\CurrencyMod\Multiplayer\`:
- `MpChannel.cs` — ordens do mod `rp.<ação>` em cima de `OrderChangeGameOption` (patches em
  `GameOptionsController.Validate/ProcessOrderChangeGameOption`). Anfitrião sem o mod recusa; jogador sem o mod só
  guarda o texto nos metadados.
- `MpSession.cs` — no começo de cada turno online, cada jogador manda `rp.hello` (império, versão `1.5.5/mp1`, hash do
  estado no início do turno, se está sincronizado). Quando todos os humanos estão com a versão do anfitrião e alguém
  está fora de passo, o anfitrião manda `rp.state` (estado + config de simulação dele, JSON → gzip → base64, pedaços de
  3000 caracteres). Todos aplicam o mesmo texto; só então a economia liga (`MpSession.EconomyBlocked`). Jogador sem
  o mod ou com outra versão: o mod fica desligado para todos e aparece um aviso na tela. A config do anfitrião vale só
  na partida (não grava no .cfg do cliente; volta ao sair).
- `MpInputs.cs` — juros (automático/manual/taxa), nome da moeda e regras/preços de pedágio. Single-player: aplica na
  hora, como antes. Multiplayer: vira ordem `rp.bank.*` / `rp.trade.edit`. O slider de juros manda uma ordem só, quando
  o jogador para de mexer.
- `DetMath.cs` — `Log/Exp/Pow/Tanh` determinísticos (só + − × ÷), usados pela economia em todos os modos.

Mudanças que também valem para o single-player:
- Memória da rede comercial que vivia em variáveis estáticas (crises de abastecimento, epidemias, parceiros) agora fica
  no estado salvo (`CurrencyWorld.SupplyCrises/SupplyShares/LastEpidemics/TradePartners`). A crise guarda o tipo do
  recurso, não o nome traduzido.
- A rede comercial que a simulação lê só é atualizada pelos passes da simulação; o desenho do mapa calcula a sua sem
  publicar (`TradeNetwork.Refresh(publish: false)`). Antes o mapa atualizava a da simulação no meio do turno.
- Save: no multijogador cada jogador grava `CurrencyMod.json` depois que o estado do anfitrião chegou.
- Relatar um problema (F4) aparece também no multijogador.

Ainda desligado no multijogador (fases 2 e 3): IA do LLM, cartas, Congresso, conselho, blocos, Alianças, nomes
nativos e a janela antiga do banco (IMGUI).

Testes feitos (2026-10-28):
- Compila; traduções en/es/fr/de completas (validador: 0 vazios, 0 com problema).
- `mp roundtrip` no Teste16 T68: estado de 154 mil caracteres vira 49,6 mil empacotados (17 ordens); cópia idêntica.
- **Partida online de verdade, eu como anfitrião** (`game host` + `game lobby ready`, sala privada, 4 impérios, Tiny;
  os outros 3 são IA):
  - o "olá" sai, o anfitrião manda o estado e a economia liga (`active True`); o hash bate em todo turno até o 37;
  - ordens `rp.ping` de 5, 3000 e 60 000 caracteres passam pelo pipeline do jogo;
  - juros, nome da moeda ("Lira") e bloqueio/pedágio via ordens chegam ao estado; o botão "Mudar para automático" do
    Banco Central (clique de verdade na interface) vira ordem e muda o modo;
  - o Banco Central abre e mostra tudo online; Correio, Conselho e Alianças continuam escondidos; F4 aparece;
  - aviso de versão diferente aparece na tela (com `mp hello 9.9.9/mp1`) e some quando as versões voltam a bater;
  - hot-reload do núcleo no meio da partida online: o estado é reenviado e reaplicado sozinho, mesmo hash;
  - **salvar e recarregar online**: `CurrencyMod.json` vai no save; recarregado como anfitrião, o estado volta com o
    mesmo hash do momento do save e é reenviado para todos.
- Regressão no single-player (Teste16 T68, 16 impérios): ordens, juros e pedágio aplicados na hora; turnos 68 e 69
  passaram sem erro do mod.

Defeitos achados e corrigidos nos testes:
- os painéis de economia e de política comercial da diplomacia nunca apareciam online (o teste de "partida carregada"
  recusava partidas online) — `GameAccess.TryGetSession(..., allowOnline: true)`;
- o aviso de versão ficava preso no status depois que as versões voltavam a bater;
- "olás" e hash de uma partida anterior apareciam no `mp` depois de carregar outra.

O que **não** dá para testar com um PC só (precisa de dois PCs ou duas contas Steam):
- um cliente de verdade recebendo as ordens pela rede (tamanho máximo real de mensagem, ordem de chegada);
- dois computadores calculando a economia e o hash dando igual (determinismo entre máquinas; Mac/Linux);
- jogador sem o mod (o aviso só aparece depois de 30 s sem o "olá" dele);
- jogador entrando no meio da partida.
Roteiro para esse teste: anfitrião e cliente com a mesma versão → `mp` nos dois mostra `active True` e o mesmo hash;
mexer em juros e pedágio de um lado e ver do outro; passar 5+ turnos sem luz vermelha; cliente sem o mod → aviso;
salvar e recarregar.

Observação: carregar um save com o jogo em segundo plano pode travar na tela de carregamento; em primeiro plano carrega.

## 8. Fase 2 — nações da IA e cartas (2026-10-28, versão 1.7.0)

Pedido do Lucas: "O IA Nations tem que entrar já e letter também". Código em `src\CurrencyMod\Multiplayer\MpAi.cs` +
ajustes no `AiModule`.

Como funciona:
- **A IA de linguagem roda só no anfitrião** (provedor e custo dele, D1). Ela decide pelas nações do computador e age
  com as ordens normais do jogo (guerra, tratados, exércitos, Congresso), que já chegam a todos.
- **O que a simulação lê em todas as máquinas vai como ordem `rp.ai.control`**: quais nações a IA de linguagem comanda
  (travas da IA nativa), as posturas comerciais (bloqueio/pedágio das nações) e as regras de propostas pendentes
  (quantos turnos a nação tem para responder). Pedágio e bloqueio decididos pela IA vão como `rp.trade.edit`.
- **Cartas**: o anfitrião guarda o único mundo da IA. A cada 2 s ele manda (`rp.mail.sync`) as cartas que envolvem um
  humano (enviadas ou recebidas por ele, públicas, de grupo com humano), os grupos, os tratos, as recusas de cada humano
  e os nomes. Os outros jogadores guardam isso num espelho, que o Correio (F2) e a aba Cartas da diplomacia leem.
- **O que um jogador que não é o anfitrião faz no Correio** (escrever, responder, ler, recusar cartas de uma nação,
  criar grupo, mensagem ao grupo, sair do grupo, trato) vai ao anfitrião como ordem `rp.mail.*`; o anfitrião aplica e a
  próxima sincronização devolve para todos.
- O "IA desconectada" do Correio dos outros jogadores mostra o estado dos provedores do anfitrião.
- Recarregar, ressincronizar ou alguém entrando: o anfitrião manda de novo o controle e todas as cartas.

Ainda fora do multijogador (fase 3): conselho (F7), blocos e Alianças (F3), cartas interceptadas (espionagem), nomes
que a IA dá às nações, rendição pendente segurada para a IA responder, cartas entre humanos.

Testado (anfitrião sozinho, partida online "RP MP teste", 4 impérios):
- a IA das nações decide online no anfitrião; a ordem de controle chega (3 nações comandadas); ações viram ordens do jogo;
- carta pelo caminho dos outros jogadores (`mp mail E1 …`): vira ordem, o anfitrião grava, a sincronização sai;
- `mp mirror`: o espelho de um jogador fica igual ao mundo do anfitrião (caixa de entrada e de saída);
- hot-reload no meio: estado, controle e cartas reenviados sozinhos.
