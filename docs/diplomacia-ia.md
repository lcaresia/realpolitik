# Diplomacia IA — documentação técnica

Cada nação controlada pelo computador vira um **personagem** guiado por um modelo de linguagem (DeepSeek, OpenRouter,
OpenAI, Gemini, GLM ou xAI, à escolha do jogador na tela Diplomacia IA, §15).
Uma vez por turno ela lê um **dossiê** com o que sabe do mundo, as cartas que chegaram e a própria memória, e responde
com um diário secreto, sentimentos, cartas e ordens.

- Design completo e decisões: `docs\design-diplomacia-ia.md`.
- Instalação em outra máquina: `docs\instalacao.md`.
- Pesquisas no código do jogo: `research\llm-diplomacy-feasibility.md`, `research\llm-dossier-api.md`,
  `research\diplomacy-native-timers.md`.

**Fase atual: 4 ("ações de verdade"), em andamento.**
- O que já tem efeito:
  - dossiê, chamada à API, diário, sentimentos e memória;
  - cartas entre nações, inclusive para você, com interceptação por espiões (§13);
  - bloquear e desbloquear correspondência (para as IAs e para você);
  - **Correio Diplomático nativo** dentro do jogo (§11);
  - visualizador F10;
  - **ações como ordens do jogo** (§12): guerra, paz, rendição, acordos, presentes, cessão de território, exigências
    e crises, povos independentes, postura e foco (que puxam a IA nativa), ordens a exércitos, política comercial
    (pedágio com preço e bloqueio) e conselho de ministros;
  - **Congresso mundial** (§12, "Congresso"), com o DLC Together We Rule: propor leis, votar, subornar, crises
    internacionais, vereditos e consenso ideológico. Testado numa partida de 16 impérios (2026-10-05);
  - **o seu conselho** (§14), com tela nativa.
- Fora do escopo: o pacote de instalação novo (próxima sessão) e a negociação de verdade na aba Crise (em conversa).

---

## 1. Como funciona um turno

```
Começo do turno — thread do jogo (sandbox)
 └─ TurnCapture: postfix em SandboxState_TurnMain.Begin → WorldCapture (foto só com tipos simples)
      logo depois do processamento de início de turno e antes de a IA nativa agir; nenhuma ordem roda junto

Thread principal (IaModule.Tick, a cada 1 s)
 ├─ detecta a partida (GUID) e o turno (o mesmo número que o jogador vê, GameSnapshot.CurrentTurn)
 ├─ pega a foto do turno; se não houver (núcleo recarregado no meio do turno), tira uma na thread principal (plano B)
 ├─ para cada nação do computador que ainda não pensou neste turno (até [IA] ChamadasParalelas de cada vez):
 │     DossierBuilder.Build(foto) → texto do dossiê + contexto de validação (códigos que ela pode citar)
 │     Prompts                    → [regras do mundo + formato] + [persona]  (mensagem de sistema, fica no cache)
 │     ThreadPool                 → DecisionRunner.Run: chama a API, valida o json, pede de novo com os erros (até 2x)
 ├─ respostas voltam por uma fila → IaModule.Apply (thread principal)
 │     diário, sentimentos, notas, cartas (com prazo de entrega), bloqueios, ações (registradas)
 └─ ViewerModel publica tudo no visualizador; IaLog grava o log em disco; a memória vai junto no próximo save
```

- **Leitura segura do jogo.** A simulação roda numa thread própria e muda o tempo todo (ordens, movimentos,
  batalhas). Por isso o mod a lê **uma vez por turno, dentro da própria thread do jogo**, e copia tudo para objetos
  simples (`Capture\WorldCapture.cs`). Dossiê, visualizador e validação usam só essa foto.
- O plano B (foto pela thread principal) só roda no turno principal (`SandboxState_TurnMain`) e no máximo a cada
  10 s. É quase sempre seguro, mas não garantido; serve para não perder o turno depois de uma recarga a quente.
- A rede roda no `ThreadPool`. Nada de HTTP na thread do jogo.
- **O fim do turno não espera a IA (F2).** Uma resposta que chega depois vale para o turno em que foi pedida. As
  cartas dela têm prazo de entrega a partir desse turno.
- **Falhas:** cada nação pode falhar até 3 vezes por turno. Entre as tentativas o mod espera 30 s × número de
  falhas; depois desiste daquele turno. Três falhas seguidas no geral mudam a situação para "API com erro".
- **Teto de gasto:** quando o gasto da partida passa de `[IA] TetoGastoPartidaUSD`, as chamadas param.

## 2. Arquivos (`src\CurrencyMod\Diplomacia\`)

| Arquivo | O que faz |
|---|---|
| `IaConfig.cs` | Seção `[IA]` do `.cfg` (ver §5). |
| `Llm\ApiKey.cs` | Lê o `BepInEx\config\deepseek.key` antigo (só para importar para as credenciais, §15). |
| `Llm\LlmClient.cs` | Transporte HTTP: `chat/completions` com as variações de cada provedor (o Codex vai por `Providers\CodexCli.cs`). Modo json, raciocínio, novas tentativas em erro de rede/429/5xx, tipo do erro (`ErrorKind`), extração do json, custo (`Pricing`) e limpeza de pedaços de chave nas mensagens de erro (`Redact`). |
| `Llm\Providers\*.cs` | Provedores (§15): catálogo, credenciais com DPAPI, fila com troca automática, logins OAuth, "Testar conexão" e comandos `ia provedor`. |
| `UI\ProvidersScreen.cs` / `UI\ProvidersButtons.cs` | Tela Diplomacia IA (menu principal e menu de pausa) e os botões de entrada (§15). |
| `IaModule.cs` | Orquestrador (MonoBehaviour): partida, turno, despacho, aplicação das respostas, cartas do jogador, publicação. |
| `IaJobs.cs` | `DecisionJob` (tudo que a thread de trabalho precisa) e `DecisionRunner` (chamada + validação + novas tentativas). |
| `Capture\TurnCapture.cs` | Foto do turno: gancho em `SandboxState_TurnMain.Begin` (thread do jogo) e plano B pela thread principal. Lê impérios, cidades, exércitos, relações, névoa e furtividade, recursos de cada império, jazidas do mapa, rotas comerciais (quem compra o quê de quem), e do bloqueio comercial do mod: pedágios do último turno, postos com pedágio ou bloqueio e o nome da moeda de cada império. |
| `Capture\WorldCapture.cs` | Os tipos simples da foto (império, cidade, exército, relação, exército e cidade vistos, compra de rota, pedágio, regra de posto). |
| `DossierBuilder.cs` | Monta o dossiê a partir da foto (§3) e o vocabulário real que o validador usa. |
| `ActionExecutor.cs` | Executa as ações como ordens do jogo (§12): fila da thread principal, bomba na thread do sandbox (prefix em `PostOrderController.Update`), checagem com `GetDiplomaticActionFailureFlags`, resultado de volta para a memória da nação. |
| `NativeAiLocks.cs` | Travas da IA nativa (design §11.0): sem guerra, sem tratados e acordos (propor, romper, responder), sem exigir ou perdoar reclamações e sem responder exigências enquanto a IA de linguagem responde pela nação. |
| `NativeAiBias.cs` | Postura e foco viram viés na IA nativa: simpatia, superioridade, animosidade, inimigo principal, motivação dos objetivos e necessidades (§12). |
| `ProposalHold.cs` | Opção B do F3: propostas para uma nação travada atravessam o fim do turno e a virada até ela responder (§12). |
| `GameGlossary.cs` | Nomes oficiais do jogo, traduzidos pelo próprio jogo: recursos (`DepartmentOfResources.ResourceDefinitionByResourceType` + `Utils.DataUtils.TryGetLocalizedTitle`) e povos (`FactionDefinition`). Lido na thread principal e guardado. |
| `Grounding.cs` | Confere as cartas: nomes próprios fora do jogo, mercadorias e mecanismos inexistentes, carta repetida (§4). |
| `GameAccess.cs` / `GameText.cs` | Partida, turno e estado do jogo pelos snapshots da interface; nomes das eras; limpeza de marcações de texto. |
| `Prompts.cs` | Regras do mundo, formato da resposta, persona (traços nativos + um traço sorteado estável). |
| `DecisionParser.cs` | Lê e valida o json da resposta contra o que a nação pode citar. |
| `IaWorld.cs` | Estado salvo no save: personas, diários, sentimentos, notas, ações, cartas, bloqueios, gasto. |
| `IaRuntime.cs` | Dados só do visualizador (últimos dossiês, prompts, respostas cruas, chamadas, eventos). |
| `Delivery.cs` | Prazo de entrega das cartas pela era de quem envia. |
| `IaSavePatches.cs` | Grava/lê `DiplomaciaIA.json` dentro do container do save. |
| `IaLog.cs` | Log por decisão em `BepInEx\DiplomaciaIA\logs\<partida>\T###_E#.json`. |
| `IaCommands.cs` | Comandos de desenvolvimento (`ia ...`, ver §7). |
| `Viewer\ViewerServer.cs` | Servidor HTTP mínimo em `127.0.0.1` (TcpListener). |
| `Viewer\ViewerModel.cs` | Monta os jsons do visualizador. |
| `Viewer\viewer.html` | A página do visualizador (embutida na DLL). |

Pontos de contato com o resto do CurrencyMod:
- `Plugin.BindConfig` chama `IaConfig.Bind`.
- `ModEntry.Start` adiciona o `IaModule`.
- `ModEntry.Stop` chama `IaModule.Shutdown()`, que solta a porta e guarda a memória para a recarga a quente.
- `DevTools.Execute` repassa `ia ...`.

## 3. O dossiê

Montado em `DossierBuilder.Build(world, foto, império)`. Fica com cerca de 3.000 a 5.000 tokens, a maior parte em
cartas. Seções:

1. **Sua nação:**
   - povo, era, estrelas de era, fama e posição;
   - tesouro (na moeda dela, do CurrencyMod) e saldo por turno, influência, ciência, estabilidade, população,
     territórios e tecnologias;
   - **recursos** estratégicos e de luxo a que tem acesso, com o nome do jogo e a quantidade;
   - quem faz fronteira com ela;
   - **cidades e postos** (código `C#`): população, ordem pública, territórios, capital, sitiada, tomada de quem;
   - **exércitos** (código `A#`): local, unidades, força, saúde, naval, em marcha, sitiando; e quantos agentes
     secretos tem.
2. **Nações que você conhece**, cada uma com:
   - era, posição em fama, se faz fronteira;
   - relação e acordos (comércio, fronteiras abertas, mapas compartilhados, não agressão);
   - apoio à guerra dos dois lados e placar de guerra;
   - **reclamações dela** contra eles, uma por linha com código `G#`: o que o jogo daria se ela exigir (pagar X na
     moeda dela, entregar o território T#, adotar ou revogar uma lei, converter-se, romper aliança etc.), em quantos
     turnos expira e, se não der para exigir agora, o motivo do jogo (até 6; o resto vai em "todas");
   - reclamações deles contra ela, por tipo (ex.: "bloqueio, pedágio ou rota comercial destruída");
   - **exigências em aberto** nos dois sentidos, com o que cada uma pede e desde quando. As que esperam a resposta
     dela vêm como "EXIGÊNCIAS CONTRA VOCÊ", com o que aceitar, recusar e enrolar fazem. Também: quem já enrolou,
     quem recusou (e quem ganhou pretexto para guerra formal), exigência forçada pelo consulado e votação no
     Congresso;
   - **comércio** entre as duas: o que cada uma compra da outra pela rota comercial, e se a rota está suspensa;
   - **pedágio do último turno** nos dois sentidos, com o valor na moeda dela e o número de rotas;
   - **postos comerciais**: em quantos postos uma cobra pedágio ou bloqueia as rotas da outra;
   - **tropas deles à vista** (força visível e % da sua força total, quantas estão dentro do seu território);
   - **cidades deles que ela conhece**;
   - **histórico diplomático recente** (últimos 4 atos: propostas, acordos, rupturas).
3. **Notícias entre outras nações:** guerras e alianças entre duas nações que ela conhece (regra do jogo).
4. **Nomes do jogo** (design §9.1): os recursos estratégicos e de luxo que existem neste mapa (jazidas > 0; os de
   eras futuras vêm marcados), o que é mercadoria entre nações e o lembrete de só citar o que está no dossiê.
5. **Cartas que chegaram:** cartas com `DeliverTurn <= turno` ainda não lidas, com "pede resposta" ou "não pede
   resposta". Cartas privadas de quem ela bloqueou são recusadas e voltam ao remetente.
6. **Correspondência recente (resumo):** só cartas que já chegaram, cortadas em 60 palavras:
   - até 3 privadas por nação nos últimos 15 turnos;
   - as 2 últimas declarações públicas dela;
   - a última declaração de cada nação conhecida.
7. **Sua memória:**
   - sentimentos atuais;
   - últimas 12 notas;
   - últimas 3 entradas do diário;
   - últimas 5 ordens, resumidas, com a situação de cada uma.
8. **Pendências** (design §9, anti-laço): ultimatos dos últimos 20 turnos, dela e contra ela, com a exigência, se
   vence ou há quantos turnos venceu e quantas cartas cada lado mandou depois. Termina lembrando que agir é escolha
   dela e que os outros reinos notam ameaça não cumprida e quem fica parado.
9. **Lembretes:** guerras em curso, limites de cartas, prazo de entrega, se a declaração pública está liberada,
   bloqueios e cartas devolvidas.

**Regra de conhecimento (névoa)** — é a mesma regra do jogo:
- **Conhecer uma nação:** a relação está em paz ou acima. Em "contato parcial" (`PartialyKnown`), só conhece o lado
  que descobriu o outro (`LeftKnowRight`).
- **Tropas:** só exércitos em tiles visíveis para ela agora
  (`VisibilityController.IsWorldPositionVisibleFor`). Unidades furtivas só aparecem se o tile estiver detectado;
  senão entra só a parte visível, marcada "parte oculta".
- **Cidades de outros:** só as que estão em tiles já explorados por ela. População e cerco, só se estiverem à vista
  agora.
- **Guerras e alianças entre outros:** só entre duas nações que ela conhece.
- **Nomes:** vêm de `Sandbox.EmpireNamesRepository` (nome verdadeiro). A interface mostra "Desconhecido" porque
  filtra pelo que o **jogador** conhece.

**Códigos estáveis** (sobrevivem a renomear; guardados no save):
- `E#` = índice do império;
- `C#` = cidade/posto e `A#` = exército, numerados na primeira vez que aparecem;
- `T#` = índice do território;
- `G#` = reclamação: índice no `GrievanceAllocator` do dono (o jogo reaproveita índices; o executor confere o tipo de
  novo antes de agir).

## 4. Resposta da IA

Objeto json (modo json da API). Chaves:
- `diario`: texto.
- `sentimentos`: `nacao`, `afeicao`, `confianca`, `medo`, `raiva`, `motivo`.
- `cartas`: `para`, `tipo`, `assunto`, `texto`, `pede_resposta`, e `exigencia`/`prazo_turnos` no ultimato.
- `acoes`: `acao` + parâmetros, de uma lista fechada.
- `memoria`: `nacao`, `nota`.

O formato completo e as regras estão em `Prompts.WorldRules()`.

**Validação (`DecisionParser`):**
- **Pede de novo, com a lista de erros:** json quebrado, sem diário, destinatário ou ação desconhecidos, carta
  longa demais, ultimato sem exigência, declaração pública antes da hora, parâmetros fora da lista.
- **Corrige sem pedir de novo, com aviso:** valores fora da faixa, segunda carta para o mesmo destinatário, cartas
  acima do limite do turno, sentimento sobre nação desconhecida.
- **Na última tentativa**, uma ação inválida é descartada com aviso em vez de derrubar o turno inteiro (turno 101:
  Agamenão insistiu em `propor_fim_da_crise` sem exigência dos dois lados e perdia o turno). O erro aponta a ação
  certa (`retirar_exigencias` ou `responder_exigencias`).

**Ancoragem e anti-laço (`Grounding`, design §9 e §9.1):** cada carta (texto, assunto e exigência) é conferida
contra o vocabulário real da partida, montado em `DossierBuilder.FillGrounding`: nomes de todas as nações, líderes,
povos (inclusive culturas antigas, pela lista de povos do jogo), moedas, cidades, exércitos, territórios, recursos e
eras.
- **Nome próprio desconhecido** (palavra com maiúscula no meio da frase que não está no vocabulário, nem nos títulos
  comuns, nem na lista de deuses e figuras de fé): pede de novo. Na última tentativa, só avisa e a carta segue.
  - O começo de frase e a saudação ("Agamenão, rei dos Romanos, Finalmente...") não contam.
  - "lcaresia" (L minúsculo) e "Icaresia" valem igual.
- **Mercadoria ou mecanismo que o jogo não tem** (pano, tecido, prisioneiros, recibo, carreta): pede de novo; na
  última tentativa a carta é descartada.
- **Carta repetida:** semelhança de 50% ou mais (palavras de 4+ letras em comum) com a última carta da nação para o
  mesmo destino nos últimos 6 turnos. Pede de novo; na última tentativa a carta é descartada.
- Calibragem com as 184 cartas das IAs dos turnos 80–93 da partida de teste: 53 tinham algo inventado ("prisioneiros"
  15×, "panos" 10×, "Marco Túlio" 7×, "recibo" 5×, os nomes trocados do começo, intendentes "Liang" e "Shen", "Tiro",
  "Egeu"). Alarme falso: 1 (uma palavra comum escrita com maiúscula no meio da frase).

**Entrega das cartas (`[IA] AtrasoCartasPorEra`):**
- Padrão `2,2,2,1,1,1,0`: dois turnos até a Clássica, um da Medieval à Industrial, e na Contemporânea chega no mesmo
  turno (rádio).
- A resposta sempre sai no turno seguinte à chegada, porque a nação lê no começo do turno.

## 5. Configuração (`BepInEx\config\lucas.humankind.currency.cfg`, seção `[IA]`)

| Opção | Padrão | O que faz |
|---|---|---|
| `Ativo` | true | Liga a IA de linguagem. |
| `PosturaEFocoNaIANativa` | true | Postura e foco da IA de linguagem enviesam a IA nativa (§12). Desligado: só ficam registrados. |
| `ExecutarAcoes` | true | As ações decididas viram ordens do jogo (§12). Desligado: só ficam registradas e a IA nativa não é travada. |
| `Modelo` | `deepseek-flash` | Antigo. O modelo agora é por provedor, em `[IA.Provedores]` (§15); um valor diferente do padrão é levado uma vez para `Modelo_deepseek`. |
| `Raciocinio` | `low` | `desligado`, `low`, `high`, `max`. Mais raciocínio = mais caro e mais lento. Vale no DeepSeek, no GLM (liga/desliga) e no OpenRouter; nos outros, o modelo usa o padrão dele. |
| `MaxTokensResposta` | 12000 | Teto de tokens gerados por chamada (raciocínio + resposta). Com 8000, no teste do turno 95 o raciocínio gastou tudo em 4 de 9 nações e a resposta saiu cortada. |
| `Temperatura` | 1.0 | Só vale com o raciocínio desligado. |
| `TempoLimiteSegundos` | 180 | Espera máxima por resposta. |
| `ChamadasParalelas` | 4 | Nações pensando ao mesmo tempo. |
| `TentativasExtras` | 2 | Novas tentativas quando a resposta vem inválida. |
| `MaxNacoesPorTurno` | 0 | Limite de nações por turno (0 = todas). Com limite, quem pensou há mais tempo vai primeiro. Com 16 impérios (MoreEmpires), sugestão: `ChamadasParalelas` 6, `MaxNacoesPorTurno` 8 e um `TetoGastoPartidaUSD` maior (o custo cresce mais rápido que o número de nações). |
| `TetoGastoPartidaUSD` | 5.0 | Gasto máximo por partida; ao passar, as chamadas param. |
| `FatorForaDoPico` | 0.5 | Só DeepSeek: fora do pico ele cobra metade. Os preços agora vêm da tabela por provedor e modelo (§15); `PrecoEntrada*`/`PrecoSaidaUSD` saíram. |
| `HorariosPicoUTC` | `01-04,06-10` | Só DeepSeek: faixas de pico em UTC, de segunda a sexta. |
| `CartasPorTurno` | 3 | Cartas por nação por turno. |
| `PalavrasPorCarta` | 150 | Tamanho máximo de uma carta da IA. O prompt pede de 30 a ~2/3 disso, como despacho diplomático; acima de 1,25× a resposta volta para encurtar. Era 350 até 2026-10-04 (o lucas achou as falas longas). |
| `PalavrasCartaJogador` | 600 | Tamanho máximo das suas cartas (o excesso é cortado). |
| `PalavrasDiario` | 60 | Tamanho alvo da entrada do diário (era 120). |
| `TurnosEntreDeclaracoes` | 5 | Uma declaração pública por nação a cada N turnos. |
| `AtrasoCartasPorEra` | `2,2,2,1,1,1,0` | Turnos de entrega por era de quem envia. |
| `Conselhos` | true | Conselho de ministros das nações da IA (por regras, sem chamada extra). |
| `ConselhoDoJogador` | true | O seu conselho se reúne a cada turno (1 chamada) e responde às suas falas (1 chamada cada) (§14). |
| `ConselhoAbreSozinho` | true | A tela do conselho abre sozinha quando a reunião fica pronta, se o mapa estiver livre. |
| `ConselhoFalasPorTurno` | 6 | Quantos ministros falam na reunião, além da Mão. |
| `ExpansaoEmSaveAntigo` | false | Liga o DLC Together We Rule (Congresso) ao carregar um save criado sem ele, se o DLC estiver comprado e ativo (`ExpansionSavePatch`: postfix em `Sandbox.Load` que acende o bit do `DiplomacyExpansionPack` em `Sandbox.DownloadableContents`). Vale no próximo carregamento. `jogo expansao` mostra se a sessão atual foi forçada. |
| `AtalhoVisualizador` | F10 | Abre o visualizador no navegador. |
| `PortaVisualizador` | 8765 | Porta local do visualizador. |
| `SalvarLogs` | true | Grava os logs por decisão. |
| `TurnosDeLogGuardados` | 30 | Turnos de log mantidos por partida. |

O `.cfg` é relido a cada recarga do núcleo. Mudanças com o jogo aberto valem depois de `reload` ou de uma nova
compilação. A tela Diplomacia IA (§15) grava direto no `.cfg` e vale na hora.

**Seção `[IA.Provedores]`** (editada pela tela; chave e token nunca ficam aqui):

| Opção | Padrão | O que faz |
|---|---|---|
| `Ordem` | vazio | Fila de provedores, ex.: `openrouter,deepseek`. Vazio = DeepSeek, se houver chave dele (instalações antigas). `nenhum` = fila vazia de propósito. |
| `Modelo_<provedor>` | o recomendado | Modelo de cada provedor (`Modelo_openrouter`, `Modelo_deepseek`, `Modelo_openai`, `Modelo_gemini`, `Modelo_zai`, `Modelo_xai`, `Modelo_codex`). |
| `PrecosPersonalizados` | vazio | Passa por cima da tabela: `provedor/modelo=cache/entrada/saída; ...` em US$ por milhão de tokens. |
| `MinutosForaAposErro` | 10 | Depois de um erro, o provedor fica de fora esse tempo e o próximo da fila assume. |
| `CodexCaminho` | vazio | Caminho do `codex.exe` do provedor ChatGPT (Codex). Vazio = procura sozinho (app do Codex e PATH). |

## 6. Visualizador (F10)

`http://localhost:8765/`, aberto pelo F10 no navegador padrão.

**O que mostra:**
- No topo: situação da IA, turno, gasto, chamadas, taxa de cache e quantas nações estão pensando.
- **Sua correspondência**, a tela inicial:
  - **Recebidas:** só as cartas que já chegaram para você, com marca de "nova" no turno da chegada e botão
    **Responder**.
  - **Enviadas:** situação de cada carta sua (a caminho / entregue / lida / recusada).
- **Escrever carta:** você escreve para uma nação, e a carta chega pela regra de entrega. É o correio da fase 1, até
  existir a tela nativa (fase 2).
- **Bastidores**, uma chave no canto. Desligue para jogar sem spoiler. Ligada, mostra também:
  - **Correio do mundo:** todas as cartas, inclusive entre IAs e as que ainda estão a caminho.
  - **Eventos:** o que o mod fez.
  - **Por nação:** Diário, Cartas, Ações, Sentimentos, Memória, Persona, Dossiê (exatamente o que ela sabe), Prompt,
    Resposta (json e raciocínio do modelo) e Chamadas (tempo, tokens, cache, custo).

**Segurança:**
- Só aceita conexões desta máquina (`127.0.0.1`) e com cabeçalho `Host` de localhost, o que bloqueia DNS rebinding.
- Pedidos POST precisam do token da própria página.
- A chave da API nunca aparece no visualizador.

## 7. Comandos de desenvolvimento

Escreva uma linha em `_Modding\dev\cmd.txt` com o jogo aberto. O resultado sai em `_Modding\dev\out\result.txt`.

| Comando | O que faz |
|---|---|
| `ia status` | Situação, estado do jogo, foto do turno, gasto, nações e última decisão de cada uma. |
| `ia foto` | Tira a foto do turno agora, pela thread principal (testes). |
| `ia dossie <n\|todas>` | Monta o dossiê agora, sem chamar a API, e grava em `dev\out\ia_dossie_E<n>.txt`. |
| `ia prompt <n>` | Grava o prompt completo (sistema + dossiê). |
| `ia agora [n]` | Faz a nação `n` (ou todas) pensar agora, mesmo já tendo pensado no turno. |
| `ia carta <n> <texto>` | Carta privada sua para a nação `n` (chega pela regra de entrega). |
| `ia carta! <n> <texto>` | O mesmo, entregue no turno atual. Junto com `ia agora <n>`, testa o ciclo carta → resposta. |
| `ia on` / `ia off` | Liga ou desliga a IA (grava no `.cfg`). |
| `ia viewer` | Abre o visualizador. |
| `ia reset` | Apaga a memória da IA desta partida (testes). |
| `ia acoes [turnos]` | Ações das nações nos últimos turnos (padrão 3) e o que o jogo fez com cada uma. |
| `ia correio …` | Controla a tela cheia do correio (§11.5). |
| `ia cartas E#` | Abre a diplomacia com a nação já na aba Cartas. |
| `ia espionagem …` / `ia interceptar …` | Espiões e interceptação de cartas (§13). |
| `ia congresso [agora]` | O Congresso visto pela foto do turno (ou tirada agora): DLC, liberação, peso de cada nação, consulados, alavancagem, presidência, leis, votações com votos e subornos, crises, consenso, leis impostas e notícias. Grava `dev\out\congresso.txt`. |
| `ia acao E# {json}` | Uma ação qualquer de uma nação da IA (o mesmo json das decisões), pelo mesmo caminho do executor, sem a validação do dossiê. Fica na memória da nação. |
| `ia conselho [ver\|abrir\|fechar\|secao 0..2\|falar <texto>\|demitir <pasta>\|reunir]` | O seu conselho (§14): mostra a reunião do turno em texto, abre a tela, fala com os ministros, demite e convoca de novo depois de uma falha. |
| `ia teste carta-de E# [texto] \| parser E# \| congresso \| conselho-json` | Cenários da revisão de bugs: carta de uma nação para você (vale com nação eliminada); decisão com cartas erradas na 1ª e na última tentativa; a espera da 1ª sessão do Congresso numa sequência de presidentes; resposta do conselho fora do formato pelo `ApplyMeeting` de verdade. |
| `ia exercitos E#` | Exércitos da nação com o código A (da foto do turno), em marcha ou não, e a missão de cada um (§12). |
| `ia exercito E# A# mover\|atacar\|defender\|parar <T#\|C#\|A#\|-> [turnos]` | A mesma ordem que a IA de linguagem daria, registrada na memória da nação (o resultado aparece no `ia acoes`). |

Comandos do jogo (`GameCommands.cs`), para testar sem clicar:

| Comando | O que faz |
|---|---|
| `jogo estado` | Estado do jogo, turno e se dá para passar o turno. |
| `jogo saves` | Lista os saves. |
| `jogo carregar [título]` | Carrega o save pelo título (sem título: o mais recente). Funciona no menu principal. |
| `jogo salvar <título>` | Salva a partida. |
| `jogo turno` | Passa o turno (mesma ordem do botão). Avisa se uma proposta espera o jogador: o jogo segura o fim do turno até ele responder. |
| `jogo propostas` | Tratados e acordos pendentes entre o jogador e cada nação. |
| `jogo responder <E#> aceitar\|ignorar` | Responde pelo jogador. |
| `jogo propor <E#> <tipo>` | Proposta formal do jogador (economico, informacao, cultural, militar, alianca, paz). |
| `jogo crises [E#]` | Reclamações (com o índice G), exigências e crise de todas as relações, ou só das de uma nação. |
| `jogo exigir <E#> todas\|G#…` | O jogador transforma reclamações em exigências (para testar as respostas das IAs). |
| `jogo exigencias <E#> aceitar\|recusar\|enrolar\|retirar` | O jogador responde às exigências de uma nação, ou retira as suas. |
| `jogo travado [n]` | Por que o fim do turno não termina: pendências diplomáticas, batalhas, ações, decisão obrigatória do jogador e as últimas n ordens (o TurnFinish só sai depois de 8 s sem ordem nova). |
| `jogo economia` | Auditoria da economia de todos os impérios (jogo + CurrencyMod), com alertas de valores fora da curva; grava `dev\out\economia_T<turno>.csv`. |
| `jogo juros <E#> <%>\|auto` | Fixa os juros de uma nação (também da IA) para testar estratégias; `auto` devolve a regra automática. |
| `jogo espectador on\|off` | A IA nativa joga pelo império local e os turnos andam sozinhos (testes de desempenho e balanceamento). |
| `jogo menu` | Volta ao menu principal sem salvar. |
| `jogo novo <jogadores> [Tiny\|Small\|Normal\|Large\|Huge]` | Partida nova pelo início rápido, com essas opções; `jogo novo restaurar <jogadores> <tamanho>` grava as opções do lobby. |
| `jogo sair` | Fecha o jogo. A Steam ainda acusa AppError_16 depois: reabra com `steam -shutdown`, `steam -silent` e o link do jogo. |
| `jogo detalhe industria\|estabilidade\|dinheiro [n]` | Detalhamento nativo (o mesmo do tooltip) da n-ésima cidade do jogador ou do dinheiro do império, com as linhas da economia (crédito, inflação, desemprego, juros). |
| `jogo rpn [nome]` | Fórmula de dados do jogo (`RpnDefinition`); sem nome, lista todas. |
| `jogo humor [E#]` | Humor da IA nativa de cada nação (com simpatia) ao lado da postura e do foco definidos pela IA de linguagem. O jogo só preenche o humor em relação ao jogador humano. |
| `jogo batalha [auto\|recuar]` | Batalhas em andamento; responde pelo jogador às que esperam a confirmação dele, como os botões de resolução instantânea e de recuo. `jogo estado` avisa quando há uma. |
| `jogo interacao esc\|tropa\|cidade\|fe\|sociedade\|comercio\|diplomacia E#\|aba <relacoes\|comercio\|tratados\|crise\|normal>\|estado` | Simula o que o jogador faz para testar se as janelas do mod fecham (ESC, clicar numa tropa ou cidade, abrir fé, sociedade ou comércio, abrir a diplomacia, trocar de aba). `estado` mostra o cursor, o estado da barra, as janelas do mod abertas, a aba Cartas e a nação e o modo da diplomacia. |
| `jogo foco <caminho>\|off` | Põe o cursor num campo de texto (como o clique) ou solta. `jogo interacao estado` mostra "foco do teclado" e "digitando". |
| `jogo comprar E# luxo\|estrategico [comprador E#]` | Compra todos os recursos da categoria (botão do comércio); sem comprador, é o jogador. Cria rota nova: o log mostra "Rota E…: StartTrade pelo caminho …". O comércio exige acordo econômico. |
| `jogo posto E# T#` | Funda um posto avançado num território livre de terra (`EditorOrderCreateCampAt`). |
| `jogo rotas detalhe` | Caminho inteiro de cada rota, território a território, com o dono. |
| `jogo pedagio teste E# T_livre T_taxado E_esq E_dir [preço]` | Custo de passo do mod para o posto interno, com o contexto de uma rota real (use uma rota com mercadoria). |
| `jogo alerta <texto\|off>` | Força o selo de alerta do Banco Central (teste visual). |
| `jogo rendicao oferecer\|impor\|aceitar\|recusar\|cancelar E#` | O jogador age numa guerra como pela aba Crise (oferecer e impor com os termos padrão do jogo). |
| `jogo camera T#\|tile` | Centraliza a câmera num território (centro administrativo ou visual) ou num tile, para prints de onde as coisas acontecem. |
| `jogo congresso liberar E# \| encerrar lei\|K# \| consulado E# \| alavancagem E# E# n \| lei E# Nome \| propor V# \| votar lei A\|B \| votar K# E# \| subornar lei\|K# E# n \| cumprir\|guerra E#` | Testes do Congresso: libera as votações (como se essa nação tivesse cumprido o requisito), encerra uma votação agora, cria consulado, dá alavancagem, libera uma lei para uma nação, e o jogador propõe, vota, suborna e responde a um veredito pelas ordens do jogo. |
| `jogo expansao [on\|off]` | Situação do DLC Together We Rule nesta sessão e a opção `ExpansaoEmSaveAntigo` (vale no próximo carregamento). |
| `jogo civico E# NomeDoCivico` | A lei de um império agora: situação (`Enacted`, `Frozen`…), escolha ativa e turno da última troca. Confere o que aconteceu com uma lei imposta pelo Congresso. |
| `jogo rotas` | Rotas entre impérios que atravessam um terceiro, com o que esse dono faz com cada lado, regras por dono e pedágios pagos no último fim de turno. |
| `jogo pedagio E# E# livre\|pedagio\|bloqueio \| preco E# E# n \| peso n` | O primeiro império aplica o modo às rotas do segundo em todos os territórios dele; fixa o preço geral (0 = padrão); muda `PesoPedagioNasRotas`. |

Comandos do Posto Comercial (`trade …`, mesmo canal):

| Comando | O que faz |
|---|---|
| `trade` | Regras, incidentes, pedágios pagos, preços gerais e por posto, recálculo pendente e situação da janela. |
| `trade open <T#>` / `trade price <E#> up\|down` | Abre o Posto Comercial de um território seu; o mesmo que os botões `+`/`-` da linha do império. |
| `trade previa <dono> <território\|-1> <alvo> <preço> [bloqueio]` | Prévia de desvio pelo caminho do jogo: das rotas do alvo que passariam por ali sem regra do dono, quantas passam e quantas desviam com esse preço, e o caminho de cada uma com e sem a regra. Rode duas vezes: a primeira pede, a segunda lê. |
| `trade costs` / `trade mine` | Manutenção por nó de cada rota (a base do custo do pedágio) e as rotas que passam pelos seus territórios. |

## 8. Salvamento

- `DiplomaciaIA.json` vai dentro do container do save, ao lado do `CurrencyMod.json`. O jogo ignora arquivos que não
  conhece, então o save continua abrindo sem o mod.
- Ao carregar um save, a memória dele substitui a atual, mesmo voltando no tempo. Respostas que estavam a caminho
  são descartadas e as nações sem decisão no turno pensam de novo.
- Na recarga a quente do núcleo, a memória passa de uma geração para a outra (`AppDomain.SetData`).
- `IaWorld.Version` + `Migrate()` atualizam estados antigos. A versão 1 contava os turnos um a mais; ao carregar,
  tudo volta um turno.

## 9. Custos medidos

Turno 81 de uma partida com 10 impérios, 9 nações do computador, `deepseek-flash`, raciocínio `low`, fora do pico.

| Medida | Valor |
|---|---|
| Entrada por chamada | cerca de 2.500 tokens |
| Saída por chamada | cerca de 2.400 tokens (cerca de 800 de raciocínio) |
| Custo por nação/turno | US$ 0,002 a 0,0035 |
| Custo por turno (todas) | cerca de US$ 0,025 |

O primeiro turno é o mais caro: todas se apresentam com cartas longas.

Com o dossiê completo (cidades, exércitos, relações, cartas), a entrada sobe para 3.000 a 5.000 tokens. O custo
fica em cerca de **US$ 0,003 a 0,004 por nação/turno**, ou ~US$ 0,03 por turno com 9 nações fora do pico. Perto de
metade da entrada vem do cache: as regras são iguais para todas as nações, e a persona é fixa por nação.

### 9.1 Custos reais e o corte de 2026-10-06

Os números acima ficaram velhos: com o mundo inteiro no dossiê e o raciocínio, o custo real medido era ~US$ 0,12 por turno
fora do pico com 16 impérios (~US$ 0,22 no pico). O estudo completo, com tabelas, simulação e a comparação às cegas, está em
`docs\estudo-custo-ia.md`. O resumo:

- **Bug do raciocínio (corrigido):** o mod mandava `reasoning_effort` dentro de `thinking`. O DeepSeek ignora o campo ali e
  pensa em `high`. Agora o campo vai no nível de cima do corpo (`LlmClient.BuildBody`). OpenAI e Gemini 3 também passam a
  receber o nível do `.cfg`; o Gemini 2.5 Flash-Lite fica sem ele, porque recebê-lo ligaria o raciocínio.
- **"COMO PENSAR":** fim das regras (`Prompts.WorldRules`): pensar pouco, não resumir o dossiê, não reabrir decisão. O
  conselho do jogador tem uma linha igual.
- **Ordem do dossiê para o cache** (`DossierBuilder.Build`): o que muda pouco primeiro (nomes do jogo, correspondência,
  memória, povos, interceptadas, nações, Congresso, notícias), depois "TURNO N", a sua nação, o conselho, as cartas novas, as
  pendências e os lembretes. O provedor reaproveita o prefixo igual ao do pedido anterior da mesma nação.
- **Correspondência:** as cartas dos últimos 4 turnos com o resumo de 60 palavras; as mais velhas numa linha
  (`AppendLetterLine`), numa janela que anda em blocos de 5 turnos (até 40 linhas).
- **Memória:** as notas numa janela que anda em blocos de 8 (12 a 19 notas), antes dos sentimentos, que mudam todo turno.
- **Novas tentativas:**
  - a mensagem de "cortada" pede para pensar menos;
  - um nome que chegou em carta recebida (últimos 30 turnos) vale como boato no grounding, como no caso "Teodoro".
- **Logs:** o `IaLog.Prune` não apaga mais os turnos "do futuro" ao carregar um save antigo.

Medido na bancada, por decisão no pico: ~US$ 0,015 → **US$ 0,0073 (−52%)**, sem perda de qualidade na comparação às cegas.
Estimativa com 16 impérios: ~US$ 0,12 por turno no pico e ~US$ 0,06 fora; uma partida de 300 turnos, ~US$ 12–32 conforme o
horário. Ainda falta a validação no jogo.

### 9.2 A bancada (`_Modding\tools\ia-bench`)

Ferramenta fora do jogo que reexecuta decisões reais dos logs (copiadas para `corpus\`) com variantes de prompt e mede custo,
cache simulado, validade e qualidade às cegas. **Toda mudança de prompt, dossiê ou modelo passa por ela antes de ir para o
jogo.** Comandos e cuidados estão no `README.md` de lá. Ela usa a credencial do mod (DPAPI) e nunca mostra a chave.

## 10. Limitações conhecidas

- Ações não são executadas (fase 4).
- O prazo de entrega ainda não leva em conta a distância, só a era.
- Conselho de ministros: fase 3.

## 11. Correio Diplomático (tela cheia + aba na diplomacia, 2026-10-04)

**Como abrir:**
- Botão com **envelope** na barra inferior esquerda, ao lado do Banco Central. O selo vermelho mostra quantas cartas
  novas chegaram (9+ acima de nove).
- Na diplomacia de cada nação, a aba **Cartas** (depois de Crise) mostra só a conversa com ela.

### 11.1 Tela cheia (`Diplomacia\UI\MailScreen.cs`)

**Como é feita:**
- A moldura é um clone da tela de Configurações do jogo: coluna da esquerda com as seções e o botão de voltar, e
  painel central com desfoque.
- Fica registrada no grupo das telas cheias da partida (`InGameFullscreenGroup`). O jogo esconde o HUD, fecha no ESC
  e mostra uma tela cheia por vez.
- As listas de cartões vêm da janela de cidades, como no Banco Central.

| Parte | O que mostra |
|---|---|
| **Seções (esquerda)** | Cada uma com a contagem. **Novas**: chegaram e você não leu; continuam listadas enquanto você não sai da seção. **A responder**: privadas e ultimatos que pedem resposta, sem a sua, dos últimos 20 turnos. **Lidas**. **Respondidas**: com a sua resposta ligada. **Enviadas**: "Chega no turno N", "Entregue", "Lida", "Respondida" ou "Recusada". **Comunicados públicos**: os que você escreveu e os que chegaram até você. **Nações**: escrever ou recusar/aceitar as cartas privadas de cada uma. |
| **Centro** | As cartas da seção (até 60), da mais nova para a mais antiga. Botões **Nação** (cicla as nações conhecidas) e **Período** (tudo, 5, 15 ou 30 turnos). Cada cartão: quem escreveu, chips (turno, "Nova", tipo, situação, prazo do ultimato) e o texto inteiro, com o assunto em dourado e a exigência em vermelho. **Responder** prepara a resposta; **Na diplomacia** abre a aba Cartas daquela nação. |
| **Escrever (direita)** | Destinatário (**Trocar**), tipo (privada → ultimato → pública), cartão **"Em resposta a"** com **Soltar**, assunto, exigência e prazo (só no ultimato), texto multilinha (300 px) e **Enviar** com o contador de palavras (máx. `[IA] PalavrasCartaJogador`). |

### 11.2 Aba "Cartas" na diplomacia (`Diplomacia\UI\CorrespondenceTab.cs`)

- **Conversa:** as cartas entre você e aquela nação, mais as públicas dela. São as últimas 40, em ordem de chegada,
  com a rolagem no fim. Abrir marca como lidas.
- **Escrever:** igual ao da tela cheia, sem a declaração pública: carta privada ou ultimato. Se a última carta dela
  pede resposta, a sua já sai ligada ("Re: …"). **Soltar** tira a ligação.
- **Como funciona:** as abas do jogo são fixas no código (enum e campos), então esta é uma aba "sobreposta".
  - Na fileira de abas entra um clone da aba Relações, e os espaçadores das pontas encolhem para caber.
  - No grupo de painéis entra um painel nosso.
  - Ativar abre Relações por baixo (aba sempre válida), esconde o painel dela e mostra o nosso.
  - Um patch em `DiplomaticScreen.SetCurrentMode` desativa a aba quando o jogador clica numa aba do jogo e a reafirma
    quando ele só troca de nação.
  - A receita está em `docs\guia-telas-nativas.md`.

### 11.3 Respostas ligadas (`Letter.InReplyTo`, `IaWorld` versão 4)

- **Ligação de toda carta:** cada carta guarda a que ela responde (−1 = nenhuma).
- **Cartas da IA:** ligadas sozinhas à última carta sua que já chegou, pede resposta, não foi recusada e ainda não
  tem resposta dela (`IaModule.AnswerableLetter`).
- **Suas cartas:**
  - **Responder** liga à carta escolhida;
  - sem escolher, vale a mesma regra automática;
  - **Soltar** manda sem ligação.
- **Saves antigos:** a migração para a versão 4 refaz as ligações em ordem cronológica.

### 11.4 Regras

- **Recusar uma nação:** as cartas privadas dela que chegarem voltam sem ser lidas (`Letter.RejectedBy`), e ela vê
  "sua carta foi devolvida" no próximo dossiê. Ultimatos e declarações públicas sempre chegam.
- **Destinatários:** você só escreve para nações do computador vivas que conhece.
- **Texto:** é guardado sem `< > { }`. Na tela, o texto das cartas é mostrado sem interpretar marcação.
- **Teclado:** os campos de texto bloqueiam os atalhos do jogo enquanto você digita (`NativeUIKit.BlockGameShortcuts`).
- **Uma janela por vez:** o correio fecha com ESC e quando você abre outra coisa, como Banco Central, fé, sociedade,
  uma tropa ou uma cidade (`NativeUI\ExclusiveWindows.cs`, regra no guia de telas nativas).

### 11.5 Arquivos e comandos

**Arquivos:**
- `Diplomacia\UI\MailScreen.cs` (tela cheia);
- `Diplomacia\UI\CorrespondenceTab.cs` (aba da diplomacia + patch do `SetCurrentMode`);
- `Diplomacia\UI\MailButton.cs` (botão + selo);
- `NativeUI\SdfIcons.Letter()` (ícone);
- `IaModule`: `PlayerInbox`, `PlayerOutbox`, `PlayerUnreadCount`, `MarkReadByPlayer`, `TogglePlayerBlock`,
  `ProcessPlayerInbox`, `AnswerableLetter`, `PlayerAnswered`, `ReplyTo`, `LetterById` e
  `SendPlayerLetter(…, inReplyTo)` (−1 automático, −2 sem ligação, ≥ 0 a carta).

**Comandos de teste:**
- `ia correio open|close|secao 0..6|to E#`;
- `ia correio responder [id]` (sem id: a primeira carta da lista);
- `ia correio limpar` (apaga o rascunho e a ligação);
- `ia correio texto <texto>` (só preenche o campo, com `\n` para quebrar linha; não envia);
- `ia cartas E#` (abre a diplomacia daquela nação já na aba Cartas);
- `jogo interacao …` (testes de fechar janelas: ESC, tropa, cidade, fé, abas da diplomacia, estado; veja §7).

## 12. Ações de verdade e travas da IA nativa (fase 4, núcleo — 2026-10-04)

**Ações que viram ordens do jogo** (`[IA] ExecutarAcoes`, padrão ligado):

| Ação da IA | Ordem do jogo |
|---|---|
| `declarar_guerra` (formal/surpresa) | `OrderDiplomaticAction` DeclareFormalWar / DeclareSurpriseWar |
| `propor_paz` | ProposeEndWarTreaty (os termos ficam na carta) |
| `oferecer_rendicao` / `impor_rendicao` (territórios, vassalagem) | StartToFill / AllowToForce → OrderChangeSurrenderTerritoryState e …SubmissionState → ProposeToSurrender / DeclareSurrender; se falhar, CancelSurrenderProposition (abaixo) |
| `responder_rendicao` (aceitar, recusar) | AcceptSurrender / RefuseSurrender, conferindo quem responde |
| `propor_acordo` (economico, informacao, cultural, militar, alianca) | Propose…Agreement / ProposeAllianceTreaty |
| `romper_acordo` (os mesmos) | Break…Agreement / DeclareEndOfAlliance |
| `presentear` (ouro, influencia, cidades, exercitos) | StartToFillGiftProposition → OrderUpdateGiftFimsInfo (dinheiro e/ou influência) e OrderUpdateGiftInfo (cada cidade, posto ou exército) → ProposeGift; se falhar, CancelGiftProposition. Nunca mais do que a nação tem. Cada item passa antes por `GetEntityFailureFlags`: o que o jogo não deixa dar (capital, cercada, tomada de outra nação, mercenário, limite de agentes) fica de fora com o motivo. |
| `ceder_territorio` (T#) | Território central de posto ou cidade: o assentamento inteiro vai de presente. Anexo de uma cidade: `CanDetachTerritoryFromCity` → OrderDetachTerritoryFromCity (vira posto) → o posto novo vai de presente. O dossiê lista os anexos de cada cidade. |
| `patrocinar` (M#, dinheiro, influencia) | OrderSetPatronageInvestmentLevels (nível que faltar fica igual). Sem contato aberto, OrderEnactTreatyMinorEmpire(OpenContact) antes. A escolha fica guardada: um postfix no atuador `PatronizeMinor` da IA nativa troca os níveis da ordem dela pelos da IA de linguagem. |
| `tratado_povo` (M#, tratado) | OrderEnactTreatyMinorEmpire, depois de `FillEnactMinorEmpireTreatyFailures` e da regra "um tratado por grupo" (`PatronageTreatyChosen`). Vassalo (Puppet) e anexar (Assimilate) ficam travados na IA nativa (postfix em `IsTreatyValid` dos geradores). |
| `renomear` (C# ou A#) | OrderRenameSimulationEntity |
| `exigir` (queixas: G#… ou "todas") | `OrderExecuteGrievanceAction` CreateDemand, uma por reclamação; "todas" = `OrderExecuteGrievanceActionBatch` |
| `perdoar_queixas` (as mesmas) | o mesmo, com RenounceGrievance |
| `responder_exigencias` (aceitar, recusar, enrolar) | AcceptDemands / RefuseDemands / StallForTime |
| `retirar_exigencias` | WithdrawDemands |
| `propor_fim_da_crise` | ProposeEndCrisisTreaty (só com exigências dos dois lados; a resposta é `responder_tratado`) |
| `crise_internacional` | DeclareInternationalCrisis |
| `ordem_exercito` (A#, mover/atacar/defender/parar, destino T#/C#/A#, turnos) | Missão de vários turnos (`ArmyOrders.cs`, abaixo): o avaliador do clique direito do jogador escolhe o caminho, que vira OrderGoTo, OrderCreateBattle, OrderGoToAndCreateBattle, OrderGoToAndJoinBattle ou OrderCancelArmyMovement. |

**Ordens a exércitos** (`Diplomacia\ArmyOrders.cs`; pesquisa em `research\army-orders.md`; 2026-10-04):
- **Objetivos:**
  - `mover`: vai a um território (T) ou cidade (C) e segura o lugar até o fim do turno seguinte.
  - `defender`: vai a uma cidade ou território seu e segura até o prazo.
  - `atacar`: uma cidade inimiga (vira cerco; com cerco já montado, entra como reforço) ou um exército inimigo à vista
    (A da seção da nação). Exige guerra declarada.
  - `parar`: cancela o movimento e segura até o prazo.
- **Destino:**
  - Território: a âncora (centro administrativo ou centro visual) e até 11 tiles vizinhos dentro do território. Os
    tiles precisam estar explorados, sem exército e sem centro de cidade estrangeira, e na água só para navio. Com
    `mover`, tile dentro da área de uma batalha em curso fica de fora (`BattleRepository.TryGetBattleAt`), porque lá a
    ação seria entrar na batalha.
  - Cidade já cercada (atacar), ou exército que defende uma cidade cercada:
    - o reforço entra pelo lado de fora (`JoinSiege`), como a IA nativa faz (`Nodes.FindJoinBattlePosition`);
    - o alvo é um tile livre da área da batalha (`Siege.Battle.Arena.Area.Positions`), fora dos distritos da cidade
      cercada e o mais perto do exército;
    - a ordem é `OrderGoToAndJoinBattle`, com o GUID da batalha que o avaliador devolve;
    - mirar o centro da cidade dava "CannotJoinInnerAreaOfBattle", e mirar o tile de um sitiante dava
      "CannotJoinBattle".
  - Cidade sua: o centro dela.
  - Exército que já está no território (em terra) conta como chegada e segura sem andar. Se tiver sobrado movimento
    de antes, ele é cancelado.
- **Avaliação:**
  - Usa o mesmo `SimulationEvaluator.Evaluate(RequestArmyActionAt)` da tela do jogador. O pedido é zerado a cada uso,
    porque o avaliador só acumula flags.
  - Os critérios de aceite são os mesmos do `BaseArmyCursor`.
  - Recusas: caminho com falha; precisa de guerra (3 tentativas, para dar tempo ao `declarar_guerra` da mesma
    decisão); condições que a tela pergunta ao jogador (romper um cerco seu, entrar na batalha de outras nações,
    perder um patrocínio); caminho que afoga.
- **Mar profundo:**
  - O avaliador tenta primeiro um caminho que não mata e, se não acha, devolve o que mata (a tela mostra a caveira e
    o jogador decide). A IA não vê a tela.
  - `Deadly()` conta os fins de turno em mar profundo do caminho contra `Unit.ComputeTurnsBeforeDyingInDeepWater` e
    recusa o caminho mortal. Parar em mar profundo também conta.
  - Navio de verdade é a unidade `UnitSpawnType.Maritime`. Tropa de terra embarcada (`AtSea`) tem destino em terra.
  - Antes dessa regra, dois exércitos morreram afogados no teste: E5 A100 e E3 A126.
- **Turno a turno:**
  - O movimento do jogo só anda dentro do turno (o próprio GoTo posta `OrderContinueGotoAction`).
  - O laço `ArmyOrders.Tick` roda no prefix do `PostOrderController.Update`, só no turno principal, com até 4 ordens
    por volta e nunca durante o ciclo da IA nativa (`aiDecisionInProgress`).
  - A cada turno novo, a missão é reavaliada e repostada até chegar.
- **Prazo:** `turnos` vai de 1 a 10 (padrão 6, ou 5 para defender e parar). A viagem que o jogo calcula estende o
  prazo até 12 turnos.
- **Fim da missão:**
  - Chegou e segurou o lugar.
  - O prazo acabou.
  - O caminho ficou bloqueado 3 vezes.
  - O exército sumiu. Se as unidades estão em outro exército da nação, a missão diz "fundido". Se não estão em lugar
    nenhum, diz dispensado, destruído ou afogado (`Fate`).
  - Com `atacar`, o exército entrou em combate e a batalha segue com os generais.
- **Trava da IA nativa:**
  - P1, `ArmyAllocator.ComputeInstanceList`: tira o exército do pool, então ele fica sem tarefa nativa.
  - P2, `ArmySpecificMission.GetContexts`: impede reagrupar e dispersar com o exército, e reagrupar dentro dele.
  - P3, `SplitArmies.IsArmyValid`: impede dividir o exército.
  - P4, rede de segurança: um prefix em `Brain.PostAndTrackOrder` marca a ordem que o cérebro nativo da própria nação
    der a um exército sob ordem. Batalha em curso e melhoria de unidades passam. Um postfix em
    `OrderPolicyController.GetPolicy` (cadeia Post) recusa a ordem marcada, pelo mesmo caminho de qualquer ordem
    recusada pela política do jogo.
  - O `ia status` mostra "exércitos sob ordem", "tarefas nativas tiradas" e "ordens nativas barradas (última)".
- **Memória:**
  - A missão fica no IaWorld (`ArmyMissions`: objetivo, alvo, situação, último turno em `HoldUntil`, ativa).
  - Ao carregar um save ou recarregar o núcleo, `ResumeMissions` reenvia as missões ativas com o prazo salvo.
  - O dossiê mostra a missão na lista de exércitos da nação e o resultado na memória de ações.
- **Teste (turnos 113 a 117):**
  - As nações deram ordens sozinhas. Zenóbia mandou A171 defender Ḫattuša (chegou em 1 turno e segura até o turno
    119), A165 para Ḫattuša (chegou e segurou) e A177 defender Mizar. Alli Raani segurou Babilônia com A60, e Zenóbia
    mandou 5 unidades para Furud.
  - `parar` (E5 A163) segurou o lugar e liberou no fim do prazo.
  - O "já está lá" funcionou com E5 A82.
  - Um navio costeiro (E5 A162) foi aceito.
  - As missões voltaram depois de cada recarga do núcleo.
  - "tarefas nativas tiradas" passou de 1.200; "ordens nativas barradas" ficou em 0, ou seja, P1 a P3 já seguram a
    IA nativa.
- **Teste do `atacar` (turnos 119 a 122):**
  - Os Ingleses declararam guerra ao jogador no turno 118, depois de um ultimato vencido.
  - No turno 119, Mu Guiying mandou A179 atacar São Paulo (C4), a capital do jogador. O caminho previsto era de 4
    turnos.
  - A missão foi repostada a cada turno e, no turno 122, o exército chegou e montou o cerco (`jogo batalha` mostra
    "E0 ataca E1 · Siege").
  - A missão terminou com "chegou e atacou São Paulo (C4); a batalha segue com seus generais".
  - No mesmo trajeto, o log mostrou a regra do mar profundo: um destino que parava em mar profundo foi recusado, e um
    caminho que só cruzava o mar profundo dentro do turno foi aceito.
  - No turno 123, duas ordens para reforçar o cerco foram recusadas pelo jogo ("CannotJoinInnerAreaOfBattle"). Com o
    `JoinSiege`, a mesma ordem (A207 atacar C4) foi aceita: o exército saiu a caminho de entrar no cerco, com chegada
    prevista em 3 turnos.
  - Um "mover" de tropa embarcada (A164, de Kerma até São Paulo) foi recusado pela regra do mar profundo: 28 passos
    em alto-mar.

**Povos independentes no dossiê** (`TurnCapture.CaptureMinors`, 2026-10-04). Para cada povo vivo com cidade que a nação
já viu, o dossiê mostra:
- código M (o índice do império menor), cidade, território e situação (jovem, no auge, em declínio);
- patrocínio da nação e a parte dela no total, quem é o maior patrono e o investimento atual;
- tratados assinados, os que dá para assinar agora (com o custo em influência), os que pedem mais patrocínio e os
  bloqueados por outro motivo;
- os nomes dos tratados vêm da tradução do jogo (`MinorPatronageTreaty`).

**Congresso** (pesquisa em `research\congress.md`; implementado e testado em 2026-10-05):
- **Pré-requisitos:** o Congresso mundial (votações de lei e de crise, subornos, consenso ideológico) exige o DLC
  Together We Rule (`DiplomacyExpansionPack`) ativo na partida. Sem o DLC, o jogo nem registra a passagem que fecha
  as votações: forçar o Congresso criaria votações que nunca terminam. Por isso, sem DLC, nada do Congresso aparece
  no prompt, no dossiê nem no parser. Saves antigos: `[IA] ExpansaoEmSaveAntigo` (§5).
- **Foto** (`Capture\CongressCapture.cs`, thread do jogo): peso de cada nação (sway), consulados, alavancagem entre
  pares, se as votações já foram liberadas (requisitos de era e de nações conhecidas lidos das fórmulas RPN), a
  sessão atual e o próximo presidente (`TryComputeNextCivicVoteSessionInfo` na thread do jogo; fora dela, a foto da IA
  nativa), as leis que dá para propor (código V), a votação de lei aberta e o histórico, com votos e subornos
  possíveis, as crises (código K), os eixos do consenso ideológico (código I, com custo) e as leis impostas. Lei
  imposta conta só se for efeito do Congresso ou bater lei, opção e presidente da última votação (a osmose cultural
  comum não entra).
- **Dossiê** (`DossierCongress.cs`, seção "CONGRESSO MUNDIAL"): antes de existir, como ele se forma; depois, o peso
  de todos, consulados e alavancagem, a nota de que guerra fora do Congresso é guerra não sancionada, a presidência
  ("VOCÊ PRESIDE" e as leis V que dá para propor), a votação de lei (placar, quem falta votar, como votar, subornos ou
  "você não vota: peso zero"), as crises, os vereditos ("O CONGRESSO DECIDIU CONTRA VOCÊ"), a lei imposta, o consenso
  (com o que dá para pagar) e as notícias recentes (`IaWorld.CongressNews`, até 12).
- **Ferramentas** (`Prompts.CongressTools`, só nas partidas com Congresso; executor `CongressFlow.cs`, na thread do
  sandbox):
  - `propor_votacao {lei: V#}` → `OrderStartCivicVote`, só quando preside;
  - `votar_congresso {votacao: lei, opcao: A|B|abster, se_perder: adotar|recusar}` ou `{votacao: K#, apoiar: E#|abster}`
    → `OrderInternationalAction` (um voto por votação);
  - `subornar {votacao, nacao, vezes}` → o mesmo pedido com `NumberOfBribeActions`; confere consulado e alavancagem,
    ajusta a quantidade ao que dá e relê o resultado. O parser põe os subornos antes do voto;
  - `responder_congresso {nacao, resposta: cumprir|guerra}` → `AcceptDemands` ou `DeclareSurpriseWar` depois de perder
    uma crise;
  - `contribuir_consenso {eixo: I#}`;
  - `crise_internacional {nacao}` → exige consulado e nem guerra nem aliança com o alvo; quando o jogo recusa, explica
    o motivo pelas flags de `GetStartCrisisVoteFailureFlags`.
  - Recusas brandas (aviso, sem nova tentativa): votar com peso zero e sem suborno, e eixo de consenso sem influência.
- **Travas na IA nativa** (`NativeAiLocks`, região Congresso): prefixos nos quatro `GetContexts`
  (`StartInternationalCivicVote`, `ManageInternationalCivicVote`, `ManageInternationalCrisisVote`,
  `ContributeToInternationalIdeologicalConsensus`) e em `ResolveInternationalCrisisVote.GenerateDesires`. Para nações
  da IA de linguagem a IA nativa não propõe, não vota, não contribui e não responde ao veredito sozinha.
- **Voltas à IA nativa** (`CongressFlow.TrackFallbacks`): o veredito volta à IA nativa depois do prazo de proposta
  sem resposta da nação; na primeira sessão, se a presidente não propuser a tempo, a IA nativa propõe por ela.
- **Lei imposta** (`NativeAiLocks.ImposedLawPatch`, postfix em `DoOsmosisAction.CreateOrder`): a decisão é tomada na
  hora da ordem, pela tabela publicada a cada turno (`LawStance`: o que a nação votou e o "se_perder"). A IA nativa
  responde em menos de um segundo, então decidir antes dava corrida. Votou na opção imposta → adota; "recusar" →
  descarta (se tiver influência); "adotar" → adota.
- **Comandos:** `ia congresso [agora]` e `jogo congresso …` (§7).
- **Testado** na partida de 16 impérios "Teste16" (T64 → T68, save "Teste16 TWR T64", DLC ligado por
  `ExpansaoEmSaveAntigo`):
  - liberação das votações por E3; votos das nações com o relatório certo do executor; recusas de voto com peso zero;
  - subornos de E3 e E8 na crise K0, por decisão própria;
  - crise K0: E8, o alvo, ganhou por 200 × 115, e as exigências caíram;
  - crise K1 (forçada): E3 ganhou; o veredito de E1 ficou segurado ("vereditos segurados 2") e a IA de linguagem de E1
    escolheu guerra ("guerra surpresa declarada contra o veredito");
  - lei V31 encerrada, opção A venceu.
- **Testado depois (T68 → T76, mesma partida, turnos passados com todas as nações decidindo):**
  - crise levada ao Congresso por decisão própria: E4 contra E6 (T71); o alvo venceu e as exigências caíram;
  - **lei proposta por uma nação da IA de linguagem:** E8 presidiu no T75 e propôs V31 «Tolerância Religiosa», com o
    motivo "minha opção vence hoje 10 a 3 e me protege da exigência de conversão de Alexandre"; votação até o T84;
  - votos e subornos nas crises, com o placar certo em cada relatório do executor;
  - **lei imposta com "se_perder":** E15 (votou B, "adotar") recebeu a lei A quando a votação foi encerrada à força; a
    IA nativa ia recusar e a trava fez adotar (`jogo civico E15 Civics_Religion02`: escolha A, trocada no T76). O ramo
    "recusar" é o mesmo código com a outra ordem e ainda não aconteceu ao vivo; a volta da primeira sessão também não
    (a primeira sessão desta partida já passou).
- **Furo achado e corrigido (recarga a quente):** a tabela de nações travadas (`NativeAiLocks.llmActive`) nascia vazia
  em cada geração nova do núcleo e só era preenchida no primeiro segundo do `IaModule`. Nesse intervalo, no T75, a IA
  nativa soltou de uma vez o que a trava vinha segurando: 7 crises no Congresso (cinco contra E1) e 12 votos na lei
  V31 que nenhuma nação da IA de linguagem decidiu. Agora o `ModEntry.Stop` guarda a tabela (`NativeAiLocks.Carry`) e o
  `Start` a devolve antes de aplicar os patches (log: "Travas da IA nativa vindas da geração anterior: 14 nação(ões)").
  Só acontecia em desenvolvimento (o jogador comum não recarrega o núcleo), mas deixou marcas na partida de teste.
- **Registro:** cada lei imposta decidida pela posição da nação sai no log ("Lei imposta a E15 (…): adotada, como a
  IA de linguagem quis (a IA nativa ia recusar)"), uma vez por evento, e o contador do `ia status` passou a contar
  também os casos em que a IA nativa já faria o mesmo.

**Política comercial** (`politica_comercial`, `IaModule.ApplyTradeStance`; 2026-10-05):
- `{"nacao": "E2", "modo": "livre|pedagio|bloqueio", "preco": 6}`: vale em todos os territórios da nação contra as
  rotas daquela nação. "preco" é opcional (sem ele fica o preço atual); vira o preço geral (`SetGeneralPrice`).
- A postura fica guardada (`IaNation.TradeStances`) e é publicada para a IA de regras (`TradeAi.SetLlmStances`), que a
  reaplica a cada turno e não mexe nesses pares. A IA de regras só cria pedágio contra quem usa os postos dela (ou
  onde já havia regra), e a regra "mais rico" pede hostilidade ≥ 1.
- O dossiê mostra os pedágios com preço ("de X a Y <moeda> por recurso").

**Rendição** (`Diplomacia\SurrenderFlow.cs`; pesquisa em `research\surrender.md`; 2026-10-04):
- **Ferramentas:**
  - `oferecer_rendicao`: a nação se rende.
  - `impor_rendicao`: só com o apoio à guerra do outro em 0 e o seu acima de 0.
  - `responder_rendicao`: aceitar ou recusar.
  - Termos: `{"territorios": ["T21"], "submissao": false}`.
- **Orçamento:**
  - O preço é o placar de guerra do vencedor, que tem de ser gasto quase todo (sobra máxima de 5).
  - As exigências do vencedor entram sempre (10 pontos cada).
  - Cada território custa 25 e só entra se encostar na fronteira do vencedor, for cidade ocupada por ele ou for vizinho
    de um já incluído.
  - A vassalagem tem custo próprio. O que sobra o jogo põe em ouro sozinho.
- **Captura** (`TurnCapture.CaptureSurrender`):
  - Flags de impor e oferecer dos dois lados, proposta pendente (vencedor, perdedor, forçada, quem responde, termos e
    custo) e rascunhos abertos.
  - **Prévia exata** nas duas direções: `DiplomaticAncillary.InitializeSurrenderProposition` numa struct local, só na
    thread do jogo (usa RPN e uma fila estática). Ela dá os territórios que dá para pedir (e os que só entram junto com
    um vizinho), a vassalagem e o valor do ponto em ouro.
- **Dossiê:**
  - Seção "Rendição" em cada guerra, com "RENDIÇÃO ESPERANDO VOCÊ", "eles podem IMPOR", a prévia com preços e o aviso
    de proposta de paz aberta.
  - O `ValidationContext` guarda o que pode entrar (`OfferLimits` / `ForceLimits`), e o parser recusa território fora
    da lista ou termo que estoura o placar.
- **Executor:**
  - Oferta: StartToFill → territórios em ordem de vizinhança → vassalagem → ProposeToSurrender.
  - Imposição: AllowToForce (ou o rascunho forçado já aberto) → termos → DeclareSurrender.
  - Resposta: confere o papel (forçada: responde o perdedor; oferta: o vencedor) e posta Accept ou Refuse.
  - Se o envio falha, o rascunho é cancelado (rascunho aberto bloqueia paz e rendição e nunca expira).
  - Uma faxina a cada 3 s cancela rascunhos órfãos das nações da IA de linguagem. O rascunho forçado que o jogo dá
    pela variante "ao aliado" fica.
- **Travas da IA nativa** (`NativeAiLocks`):
  - prefix em `Surrender.GetParents`, `ForceSurrender.GetParents` e `FillSurrenderTerms.GetContexts`;
  - postfix em `EndSurrender.TryComputeAction`, por relação.
  - Quando a nação da IA de linguagem não responde em `TurnosParaResponderPropostas` turnos, a relação entra na tabela
    de volta ao nativo (`ProposalHold.TrackSurrenders`), e a IA nativa responde.
- **Hold:** no fim do turno, a rendição que a nação da IA de linguagem vai responder fica escondida
  (`ProposedSurrenderIndex = -1` no prefix de `ReadyForTurnFinishCompletion`, devolvida no postfix e no finalizer).
  Assim o jogo não recusa sozinho por impaciência (~16 s).
- **Correção do jogo (UI):**
  - Quem impõe rendição ficava preso no popup obrigatório até o perdedor responder, porque
    `IsOtherSurrenderPropositionPending` só olha "vencedor = eu".
  - Um postfix em `MandatoryDiplomaticPropositionPending.RefreshData` limpa o bit quando a rendição é forçada, o
    vencedor é o jogador e ele não tem rascunho aberto.
- **Teste (turno 128, guerras dos Ingleses e dos Bizantinos contra o jogador):**
  - `ia acao E7 {"acao":"oferecer_rendicao","nacao":"E1"}`: o executor abriu o rascunho, enviou e reportou "rendição
    oferecida a E1: sem termos = 0 pontos". A tela nativa mostrou "A Rainha Zenóbia está se rendendo!" com Recusar e
    Aceitar, e o jogador recusou (`jogo rendicao recusar E7`).
  - `jogo rendicao oferecer E0`: o jogador ofereceu rendição aos Ingleses. O dossiê deles mostrou "RENDIÇÃO ESPERANDO
    VOCÊ … 1 exigência(s) — 10 de 10 pontos".
  - Com `ia agora E0`, Mu Guiying aceitou (`responder_rendicao`): a guerra virou paz, o cerco de São Paulo acabou, e
    ela escreveu ao jogador e fez uma declaração pública. Os Bizantinos ganharam a reclamação "SurrenderedToOurEnemy",
    regra nativa para os aliados do vencedor.
  - **Hold (turnos 129–130):** o jogador ofereceu rendição aos Bizantinos depois que Zenóbia já tinha decidido o turno
    129. A proposta atravessou o fim do turno sem a recusa automática do jogo, e o turno não travou. No turno 130, o
    dossiê dela mostrou "RENDIÇÃO ESPERANDO VOCÊ … 2 exigência(s) — 20 de 20 pontos", e ela aceitou.
  - Ainda não testado ao vivo: `impor_rendicao` (falta um lado com apoio à guerra em 0). O código é o mesmo da oferta,
    com AllowToForce e DeclareSurrender.

**Postura e foco → IA nativa** (`NativeAiBias.cs`, design §11.1, 2026-10-04). `definir_postura` e `definir_foco` viram
estado da nação (`IaNation.Postures` / `Focus`, com o turno desde quando valem; a versão 3 do `IaWorld` recupera os
valores do histórico de ações). O dossiê mostra "Sua postura com eles" e "Foco do império". O IaModule publica a
tabela a cada segundo (só nações travadas, `[IA] PosturaEFocoNaIANativa`), e os patches rodam na thread da IA nativa:
- `ComputeDiplomaticScore.ProcessGenericModifiers` (postfix): soma na simpatia e na superioridade
  (aliado +0,35; amigável +0,2; desconfiado −0,15 e +0,05; hostil −0,3 e +0,15; alvo de guerra −0,4 e +0,25). Postura
  nova (deste turno ou do anterior) liga `TriggerInstantTransitions`, e o humor muda sem esperar 1–2 turnos.
- `AnimosityAndThreat.Process` (postfix): animosidade −0,3 / −0,15 / +0,05 / +0,2 / +0,4, limitada a 0–1.
- `ComputeNemesis.Process` (postfix): alvo de guerra vira o inimigo principal (o de maior animosidade, se houver
  mais de um). A IA nativa prepara o exército; a guerra continua travada até `declarar_guerra`.
- Foco: postfix no `ComputeMotivationPropagation` dos geradores ExpandEmpire, DevelopEmpireEconomy,
  DevelopEmpireMilitary, DefendEmpire, BuildWonders, ImproveDiplomaticPower e CreateReligion (multiplicadores de 0,85
  a 2), em `ComputeFIMSNeeds.Process` (comida, produção, dinheiro, ciência e influência; até 1,6×, limitado a 0–1) e
  em `FaithNeed.Process` (fé 1,6× por território).
- O humor da IA nativa só é preenchido no jogo em relação ao jogador humano; entre IAs, a conferência é pelos
  contadores do `ia status`.
- **Teste (turno 102):** humor ajustado 2.548×, animosidade 1.274×, objetivos 924×, necessidades 198×. A simpatia com
  o jogador caiu exatamente 0,15 nas cinco nações que o marcaram como "desconfiado" (ex.: Chou 0,33 → 0,18) e ficou
  igual nas outras. Edgar marcou Agamenão como alvo de guerra depois de recusar as exigências dele, e a IA nativa
  passou a tratá-lo como inimigo principal ("inimigo principal forçado: E4 → E5").

**Reclamações e exigências** (2026-10-04):
- **Captura** (`TurnCapture.Grievances` / `Demands` / `Gain`): para cada relação, as reclamações disponíveis do dono
  (sem as já exigidas, perdoadas ou repetidas), com `CheckPrerequisitesFor(CreateDemand, eu, índice)` para saber se dá
  para exigir agora; as exigências em aberto dos dois lados (`OnGoingDemands` + `DemandsAllocator`); quem enrolou
  (`HasStalled`), quem recusou por último (`Crisis.LastRefuserEmpireIndex`) e a exigência forçada
  (`EmpireWhoNeedToAnswerEnforcedDemand`).
- **Ganho** (`DemandGainType`): dinheiro (`GainParam` × `GrievanceCount`), território (`GainGuid` → T#), lei
  (`GainParamName` + escolha, traduzidos por `GameGlossary.Title`), religião, ação diplomática contra terceiro
  (libertar vassalo, romper aliança, entrar na guerra, render-se, assinar acordo). Se o jogo já não consegue entregar,
  o dossiê avisa que vira dinheiro.
- **Validador:** `exigir`/`perdoar_queixas` só com códigos G daquela nação; `responder_exigencias` só quando há
  exigências dela esperando a resposta (não as já recusadas nem as forçadas); `retirar_exigencias` e
  `crise_internacional` só com exigências suas; `propor_fim_da_crise` só com exigências dos dois lados.
- **Executor** (`ActionExecutor.ExecuteGrievances`): confere de novo dono, alvo e tipo da reclamação, pré-checa cada
  uma e posta as ordens; um resultado só quando todos os tickets voltam ("2 exigência(s) feita(s)").
- **Travas novas:** postfix em `ComputeGrievanceActions.Process` (a IA nativa não exige nem perdoa) e em
  `ComputeDemandsAction.Process` (não aceita, recusa, enrola, retira nem leva ao Congresso). A **exigência forçada pelo
  consulado** continua com a IA nativa: o jogo segura o fim do turno até a resposta, no mesmo turno.
- Exigência comum **não segura o fim do turno** e não expira: fica na mesa até alguém ceder. Sem guerra, quem tem mais
  exigências ganha +3 de apoio à guerra por turno (`NoWarAndWeHaveMoreOngoingDemands`); o dossiê diz isso.
- **Teste (turnos 100–101):**
  - Agamenão formalizou o ultimato vencido contra Edgar (`exigir` G1 e G2: duas exigências de pagamento por tropas
    invadindo).
  - Alli Raani respondeu às três crises que se arrastavam desde os turnos 27, 64 e 97: pagou 100 a Artur, aceitou o
    acordo de não agressão exigido por Agamenão e enrolou as de Shaka. As cartas dela dizem exatamente isso.
  - Artur enrolou a exigência territorial de Byblos e perdoou uma reclamação contra os Godos (`perdoar_queixas`).
  - Mu Guiying fez uma exigência contra o jogador pela rota comercial bloqueada.
  - O `retirar_exigencias` de Artur no turno 100 foi recusado pelo jogo com o motivo certo: Alli Raani tinha pagado
    antes.

**Como roda:**
1. A thread principal aplica a decisão (`IaModule.Apply` → `BuildIntent`), cria um `ActionIntent` com id
   sequencial (`IaWorld.NextActionId`) e põe na fila. O `ActionRecord` da nação fica "enviada ao jogo".
2. Na thread do sandbox, um prefix em `PostOrderController.Update` esvazia a fila, só no turno principal (fora dele as
   ordens esperam). Confere antes com `DepartmentOfForeignAffairs.GetDiplomaticActionFailureFlags`; se o jogo não
   permitir, devolve o motivo em português (`ActionExecutor.Explain`). Senão posta com `PostAndTrackOrder`.
3. O resultado do ticket volta pela fila de saída; a thread principal atualiza o `ActionRecord` para
   "executada: …" ou "recusada pelo jogo: …". A nação vê isso no próximo dossiê, em "Sua ordem".

**Travas da IA nativa** (`NativeAiLocks.cs`, design §11.0). Valem para as nações que a IA de linguagem decidiu neste
turno ou no anterior, com a IA ligada, executando ações, a API respondendo e o teto de gasto respeitado. Se a API
cair, a nação sai da trava depois de um turno e a IA nativa volta a decidir tudo (F1).
- Postfix em `ComputeCanDeclareWar.Process`: `CanDeclareWar = false` (a IA nativa não declara guerra).
- Postfix em `ComputeTreatyAction.Process`: se a escolha do turno for uma iniciativa (propor paz, aliança, fim de
  crise ou acordo; romper acordo ou aliança), vira `DiplomaticAction.Count` ("nenhuma ação").
- Postfix em `ComputeTreatyAction.Process` também para **respostas** (assinar, contrapropor, ignorar): quem responde é
  a IA de linguagem.
- Reclamações e exigências também são dela (ver "Reclamações e exigências" acima), menos a exigência forçada pelo
  consulado.

**Propostas em análise — opção B do F3** (`ProposalHold.cs`). Uma proposta de tratado ou acordo que uma nação travada
precisa responder fica pendente de verdade, na tela nativa, até a resposta dela no turno seguinte:
- **Fim do turno:** em `DiplomaticAncillary.ReadyForTurnFinishCompletion`, um prefix esconde essas propostas só
  durante a chamada (Status = None), e o postfix e o finalizer devolvem. Assim o fim do turno não espera por elas e a
  "impaciência" do jogo não as ignora.
- **Virada:** a mesma coisa em `NewTurnBegin_ResetTreatyStatus`, que zeraria tudo.
- **Prazo:** depois de `[IA] TurnosParaResponderPropostas` (padrão 3) turnos sem resposta, ou se a IA de linguagem
  parar (F1), a regra do jogo volta.
- **Resumo da IA:** a captura guarda tipo, quem propôs, quem responde e o turno (`CapturedRelation.Treaty` /
  `Agreement`). O dossiê mostra "PROPOSTA ESPERANDO VOCÊ…" ou "Sua proposta de … aguarda a resposta deles".
- **Ações novas:** `responder_tratado` e `responder_acordo` (aceitar ou ignorar). O validador só aceita quando há
  proposta daquela nação esperando.
- **Teste (turnos 98–99):** o jogador propôs acordo de mapas a Edgar (`jogo propor E4 informacao`). A IA nativa não
  respondeu, o turno passou sem travar e a proposta sobreviveu à virada. No turno 99 o dossiê do Edgar mostrou a
  proposta, ele respondeu "aceitar", e o acordo foi assinado.
- O `ia status` mostra as nações travadas e quanto a trava segurou.

**Teste na partida de teste (turnos 95–98):**
- Turno 97: Edgar propôs paz a Agamenão, os Teutões aceitaram e a guerra Gregos × Teutões acabou de verdade
  ("turno 97, paz proposta por você; tratado assinado por eles"). Quatro propostas de acordo foram executadas. Uma foi
  recusada pelo jogo, por exigência pendente, e o motivo voltou para a nação.
- Turno 98: a trava segurou 254 iniciativas de tratado e 24 vontades de guerra da IA nativa.
- Cuidado ao passar o turno por comando: propostas da IA nativa feitas antes da trava ficaram esperando o jogador, e o
  jogo parou em `SandboxState_TurnFinish` até `jogo responder`.
- Turnos 99 e 100: o fim do turno parou porque um povo independente atacou o jogador e a batalha esperava a
  confirmação dele (`jogo travado` mostrou "Battle stuck … State=Confirmation" e "travado por decisão obrigatória").
  É regra do jogo, não do mod: resolvido com `jogo batalha auto`.

### 12.x Revisão de bugs (2026-10-05)
- **Load com chamada no ar:** `Apply` tirava a nação do `inFlight` antes de conferir a sessão e apagava a entrada da
  chamada nova. A nação pensava duas vezes no mesmo turno (diário, cartas e ações em dobro). Agora a resposta de outra
  sessão volta antes de mexer no `inFlight`.
- **Travas soltas por uma nação teimosa:** `consecutiveFailures` contava também respostas que chegaram e não
  passaram na validação. Três falhas de uma nação só soltavam a IA nativa de **todas** (F1 é para a API fora do ar).
  Agora só falha de transporte conta.
- **Carta errada na última tentativa:** tipo inválido, destinatário desconhecido, carta vazia ou longa demais,
  declaração pública fora da vez e ultimato sem exigência ainda derrubavam a decisão inteira no modo `lenient`. Agora
  só a carta cai, com aviso (`DecisionParser.Reject`).
- **Dossiê:** "Fazem fronteira com você" só lista nações já contatadas (névoa de guerra).
- **`motivo` e conselho:** lista ou objeto no lugar de texto fazia o cast `(string)` lançar. No `Apply` isso cortava
  a decisão no meio; no conselho, a reunião ficava "pensando" e repetia a chamada paga a cada frame. Leitura segura, e
  o conselho espera 60 s depois de uma falha.
- **Respostas com maiúscula:** `responder_tratado`, `responder_acordo` e o tipo de `declarar_guerra` comparavam o JSON
  cru: "Aceitar" virava recusa e "Surpresa" virava guerra formal. Agora normalizam como o parser.
- **Primeira sessão do Congresso:** a espera da volta à IA nativa conta por presidente (`firstSessionLeader`). Um
  presidente novo não herda os turnos do anterior, e sem presidente nada conta.
- **Patrocínio de povo que sumiu:** o jogo reaproveita o índice do povo morto. A escolha dele é apagada assim que a foto
  do turno não o mostra mais, para não travar o povo novo.
- **Load do mesmo jogo:** `OnSaveLoaded` esvazia `ActionExecutor.Pending` e `Outcomes`. Ações da linha do tempo
  abandonada não executam no save carregado, nem marcam outras ações com o mesmo id.
- **Missão de exército na fila:** `ResumeMissions` não reenvia uma missão cuja ordem original ainda está na fila (fim
  de turno longo). Antes a cópia substituía a original sem o ajuste de prazo pela viagem.
- **`ProposalHold`:** se o esconder lança exceção no meio, o que já foi escondido volta (antes a proposta sumia).
- **Consulado (decidido 2026-10-05):** a IA nativa não usa a alavancagem por uma nação da IA de linguagem
  (`ConsulateLeveragePatch` em `DoDiplomaticLeverageAction.GetContexts`) e não propõe nem cancela acordos de consulado
  (`ConsulateAgreementPatch` em `ManageConsulateAgreements.TryContinue`). Responder a um acordo que outro propôs fica
  com ela, como a exigência forçada (a IA de linguagem não tem essa ação). Contador no `ia status`.
- **Exércitos com a API fora (decidido 2026-10-05: "volta tudo para a IA nativa, 100%"):** sem a trava da nação, o
  `ArmyOrders.Tick` encerra a missão ("o exército volta à IA nativa") e o `ResumeMissions` não reenvia. Testado com
  `ia off`: as 24 missões caíram na hora e as travas viraram "nenhuma"; com `ia on` as travas voltaram.
- **Testado no jogo (partida de 16):** carta de nação eliminada sem "Responder"; " Aceitar " assinou o acordo
  de verdade; parser, Congresso e conselho com `ia teste`; F8 e ESC com o teclado pelo `PostMessage` (ver o guia de
  telas).
- **Fora (baixo risco, sem mudança):** voto ou suborno atrasado que cai numa crise nova com o mesmo K#; tabelas de
  patrocínio, leis e exércitos que nascem vazias na recarga a quente (só no desenvolvimento).

## 13. Interceptação de cartas por espiões (design §8.3.1, 2026-10-04)

**A regra.** Só cartas **privadas**: ultimatos vão por enviado formal e declarações públicas todos leem.
- **Quem intercepta:** um império C que não é nenhuma das pontas da carta.
- **Com o quê:** espiões de verdade num território de quem escreveu (a origem) ou de quem ia receber (o destino).
- **Carta interceptada:** nunca chega, e quem interceptou lê inteira.
- **Quem escreveu:** não fica sabendo. A carta aparece como "Entregue" e nunca vira "Lida".
- **Quem ia receber:** nunca a vê.

**O que é espião** (`TurnCapture.SpyUnits`). Exército furtivo que tenha pelo menos uma destas unidades:
- **agente** (`UnitTagAsAbility.Agent`: agentes secretos e mestres espiões, da era moderna);
- **unidade terrestre furtiva**: os espiões da Idade Média (`LandUnit_Era3_Common_Spies`) **não** têm a marca de
  agente.

Ficam de fora os enviados (agentes sem furtividade, visíveis) e os navios furtivos (Lembos, Fusta).

**Para contar contra o dono do território E:**
- o espião precisa estar oculto dele (`IsCollectionInvisible`);
- e poder agir (`IsStealthAgentActionAvailable`): aliados com operações furtivas compartilhadas não agem um contra o
  outro.

**Peso de cada espião** (`Espionage.Weigh`) = local × missão × qualidade × cobertura × tempo:

| Fator | Valores |
|---|---|
| local | região da capital 1; região de outra cidade 0,5; posto ou terra solta 0,25 |
| missão | vigiando a capital 2,5; vigiando outra cidade 2; outra infiltração concluída 1,3, em curso 1,1; só presença 1 |
| qualidade | (1 + 0,25 × (unidades espiãs − 1)) × (1 + 0,1 × veterania), no máximo 2 |
| cobertura | sob detecção hostil: min(1, turnos até ser revelado / 4); dividido por (1 + 0,15 × detecção do dono ali) |
| tempo no lugar | chegou neste turno 0,6; há 1–2 turnos 0,8; 3 ou mais 1 (`IaWorld.SpyTracks`) |

**Chance:**
- **Numa ponta:** Pmax × (1 − e^(−k × soma dos pesos)), com padrão Pmax 0,5 e k 0,7. Se o dono caça espiões, a
  exposição vale 0,85×.
- **Exemplos:** um espião só presente na capital, há 3 turnos e sem detecção, dá 0,25; vigiando a capital dá 0,41.
- **Na carta:** 1 − (1 − p_origem)(1 − p_destino), com teto `[IA] InterceptacaoTeto` (0,75).
- **Limite:** cada espião em ação intercepta no máximo `[IA] CartasInterceptadasPorEspiao` (1) carta por turno.

**Quando** (`Espionage.StampOrigin` / `Resolve`):
1. **Ao escrever:** o risco na origem é guardado na carta (`Letter.OriginRisks`), porque os espiões de lá podem sair
   antes da entrega.
2. **No turno da chegada:** no começo do turno, antes do correio e dos dossiês, `IaModule.Tick` chama `Resolve` com a
   foto do turno. Ele calcula o risco no destino, combina com o da origem e sorteia por império, em ordem de chance.
3. **Até lá:** carta privada ainda não sorteada fica segura (`Letter.IsFor` / `Arrived` exigem `InterceptionChecked`).
4. **Sorteio:** determinístico, um FNV-1a de 64 bits da partida, da carta e do império. Recarregar o save dá o mesmo
   resultado se os espiões não se moveram.

**Quem sabe o quê:**
- **IA que interceptou:** seção "CARTAS INTERCEPTADAS PELOS SEUS ESPIÕES (secreto)" no dossiê. Mostra até 3 novas
  inteiras por turno (as outras ficam para o próximo) e as últimas lidas em resumo. Ficam lidas ao aplicar a decisão
  (`InterceptedLetterIds`).
- **IA dona de espiões:** em "Sua nação", o dossiê lista "Seus espiões": onde estão, se estão observando, se foram
  descobertos e quando seriam revelados. Também mostra a chance qualitativa (alta, média, baixa) de interceptar as
  cartas de cada nação conhecida.
- **IA vítima:** só o aviso que o jogo já dá. "Seus guardas notaram atividade de espiões estrangeiros em…"
  (`Territory.IsUnderStealthActivity`), sem dizer de quem.
- **Regras no prompt:** espiões, carta que não chega, usar o que leu pode revelar a rede, e os generais movem os espiões.
- **Jogador:** aba **Cartas** na janela de espionagem, mais um selo no botão de espionagem com as cartas interceptadas
  não lidas.

**Aba Cartas na espionagem** (`Diplomacia\UI\InterceptedLettersTab.cs`):
- **A aba:** terceira aba de modo, ao lado de Furtividade e Detecção. É um clone da aba do jogo, com o componente dela,
  para os cantos de primeira e última aba virem do estilo (`RefreshPosition`).
- **Ativa:** esconde as abas de listagem, a ordenação e as listas, e mostra o painel. Um postfix em `Refresh` mantém
  isso; um prefix em `SetStatMode` a desativa quando o jogador clica numa aba do jogo.
- **Ao reabrir:** a aba continua ativa, como o jogo faz com o modo dele.
- **Painel:** cartão "Sua rede de espiões" (cada espião fora e a chance de ler as cartas de cada nação) e as
  cartas interceptadas, da mais nova para a mais antiga (até 30): de → para, turno, "Nova", "Na saída"/"Na chegada",
  espião e lugar.
- **Altura:** acompanha o conteúdo até 520 px.

**Configuração** (`[IA]`): `Interceptacao`, `InterceptacaoChanceMax`, `InterceptacaoCurva`, `InterceptacaoTeto`,
`CartasInterceptadasPorEspiao`.

**Comandos:**
- `ia espionagem`: espiões, pesos, chance por ponta, cartas a caminho com o risco na origem, últimos sorteios (chance e
  número tirado), detecção nas capitais e tráfego de cartas. Grava em `dev\out\espionagem.txt`.
- `ia espionagem unidades`: unidades com marca de agente ou espiãs e os exércitos furtivos em campo.
- `ia espionagem simular E# T# [vigiar] [fixar]`: peso e chance de um espião de teste; com `fixar`, ele vale no sorteio
  até `ia espionagem limpar`.
- `ia espionagem aba [furtividade|deteccao|estado]`: abre a espionagem na aba Cartas, simula o clique numa aba do jogo
  ou mostra o estado.
- `ia interceptar <id> E#`: força a interceptação de uma carta privada que o destinatário ainda não leu.
- `jogo espiao E# T# [unidade]`: cria um espião (ordem de depuração do jogo `OrderSpawnArmy`) num tile de terra livre
  do território.
- `jogo interacao espionagem`: abre a visão de espionagem.

**Teste na partida de teste (turnos 106–109):**
- **Antes:** a partida foi salva como "Antes espiao T106".
- **Espiões criados:** um espião medieval do jogador em Mykênê (capital dos Teutões, E5) e um dos Teutões em Ḫattuša
  (capital dos Bizantinos, E7).
- **Os dois:** nasceram ocultos (furtividade 8/8).
- **IA nativa dos Teutões:** levou sozinha um espião para Byblos (capital dos Axumitas) e o outro para outro território
  dos Bizantinos.
- **Turno 109:** 6 sorteios, de 0,15 a 0,25, e nenhuma carta pega. Uma passou com 0,18 contra uma chance de 0,17.

## 14. Conselho do jogador (design §10.6, 2026-10-04)

O seu conselho de ministros se reúne a cada turno. A Mão abre a pauta, e os ministros das pastas mais urgentes falam,
cada um na própria voz e com os números do seu reino. Você responde, eles reagem, e o apreço de cada um por você muda.

**Arquivos:**
- `Diplomacia\Council\PlayerCouncil.cs`: a reunião, as respostas, o apreço e o log.
- `Diplomacia\UI\CouncilScreen.cs`: a tela cheia, na mesma moldura do correio.
- `Diplomacia\UI\CouncilButton.cs`: o botão na barra, ao lado do correio, com o ícone de três bustos
  (`SdfIcons.Council`), o selo "!" e a abertura automática.

**Os ministros:**
- São os mesmos 12 das IAs (`CouncilEngine`): o banco de personalidades, os títulos por era e a credibilidade pelos
  números da pasta.
- Cada um tem uma tendência de conselho por regras, como "paz:E3" ou "foco:economia".
- No seu conselho, a afeição do ministro é o apreço dele por você, de −100 a 100.

**A reunião (1 chamada por turno):**
- `PlayerCouncil.Tick` roda no `IaModule.Tick`, depois do despacho das nações (com a IA ligada, a chave em ordem e
  abaixo do teto de gasto).
- A entrada tem três partes:
  - o dossiê do jogador (`DossierBuilder.Build` com o índice do jogador), sem a seção do conselho e sem o pedido de
    decisão das IAs;
  - as fichas dos 12 ministros: traços, voz, credibilidade, apreço, lealdade, simpatias, os números da pasta e a
    tendência;
  - o pedido da reunião.
- O sistema traz o papel de roteirista do conselho e a ancoragem: só fatos do dossiê, ferramentas como ações da tela
  do jogo e sem os códigos G e M. O tom de cada ministro segue o apreço.
- A resposta é um json `{"mao", "falas": [{"pasta", "texto", "conselho"}]}`, com até `ConselhoFalasPorTurno` falas.
  Pasta desconhecida ou repetida é descartada, e a apresentação no começo da fala ("Fulano, Marechal.") é cortada,
  porque a tela já mostra o nome.
- Uma falha tenta de novo até 3 vezes, com intervalo crescente. Depois disso, o botão "Convocar de novo" chama outra
  vez.

**As suas respostas (1 chamada cada, até 8 por turno, até 120 palavras):**
- A conversa vai inteira: o sistema, o dossiê e as fichas (o mesmo prefixo, que o cache da API aproveita), a reunião,
  as falas anteriores e a nova.
- Respondem de 1 a 4 ministros: quem a fala toca, quem discorda e quem você chamou pelo nome ou pela pasta.
- Volta `"apreco": {"pasta": -8..8}`, que muda o apreço de cada um. Um ministro orgulhoso ou rancoroso que cai 5 ou
  mais perde lealdade.
- A Mão não decide por você: se você só perguntou, ela resume as opções.

**O que você faz no jogo também conta:**
- No começo de cada turno, `FollowUp` compara as suas relações com as do turno anterior: guerra nova, paz, acordo
  novo.
- Quem tinha aconselhado isso ganha apreço (+4 e lealdade). Quem tinha aconselhado o contrário perde (−3), pela mesma
  regra das IAs (`CouncilEngine.ApplyReaction`).

**Demitir:**
- Na aba Ministros, "Demitir" pede confirmação ("Confirmar", 5 s).
- Outra pessoa do banco assume a pasta. O resto do conselho sente o golpe (os leais perdem lealdade, os ambiciosos
  ganham ambição).
- A troca entra na próxima resposta como "mudanças no conselho".

**A tela:**
- Seções:
  - **Reunião do turno:** cartões com título, nome, pasta, credibilidade e apreço; a fala e o conselho em dourado; as
    suas falas em itálico e as reações com a mudança de apreço. Quando chega resposta, a lista rola até a sua última
    fala (`ScrollToTop`).
  - **Ministros:** as fichas, com o botão de demitir.
  - **Reuniões anteriores:** as últimas 12.
- À direita fica o campo de texto, que bloqueia os atalhos do jogo. Ao lado aparecem o botão "Falar", a situação da
  reunião e o custo.
- **Selo "!":** a reunião está pronta e você ainda não viu.
- **Abertura automática** (`ConselhoAbreSozinho`):
  - Acontece uma vez por turno, nos 2 minutos depois de a reunião ficar pronta, e só com o mapa livre: turno
    principal, cursor normal, nenhum menu da barra e nenhuma janela aberta.
  - Fecha como as outras janelas do mod (ESC, tropa, cidade, menu da barra).
- **Sem vazamento:** os seus ministros não vazam cartas (design §10.6).

**Memória:**
- `IaWorld.PlayerCouncil` guarda as reuniões, as falas, as respostas, as mudanças e o custo.
- `IaWorld.PlayerRelationMemo` guarda as relações do turno anterior.
- Os dois vão no save.
- O log de cada chamada fica em `logs\<partida>\T<turno>_E<jogador>_conselho_<reuniao|respostaN>.json`.

**Custo medido:** a reunião sai por US$ 0,0034 a 0,0035, e cada resposta por cerca de US$ 0,003 (deepseek-flash,
raciocínio low).

**Teste (turnos 117 e 118):**
- **Reunião do turno 117:**
  - A Mão apontou o ultimato inglês de 900 vencendo, as cobranças dos Bizantinos e a cidade sem comida.
  - A Tesoureira usou o cofre de verdade (5.390 Reais, +187 por turno).
  - O Mestre dos Sussurros citou as três cartas interceptadas e o espião em Mykênê a 5 turnos de ser descoberto.
- **Pergunta sobre a fome e o pagamento:** a Tesoureira fez a conta certa (5.390 − 900 = 4.490). A Intendente admitiu
  que o dossiê não dizia qual cidade. Daí veio a correção: as regras da Agricultura agora nomeiam as cidades com fome,
  e no turno 118 ela citou Santa Catarina.
- **Turno 118:** a tela abriu sozinha, sem apresentações no começo das falas e sem códigos G.
- **Demissão:** demitir a Fé trocou Ravi Iyer por Xochitl Tecuani.

## 15. Provedores de IA e a tela Diplomacia IA (2026-10-06)

O jogador liga a IA **sem editar arquivo nenhum**, numa tela nativa no menu principal e no menu de pausa. Pesquisa de
cada provedor (endereços, preços, erros, regras): `research\provedores-ia.md`.

**Descartados (2026-10-06, decisão do lucas):** o login oficial "Sign in with ChatGPT" e o login da assinatura do
Grok (SuperGrok/X Premium). Os dois dependiam de aprovação da OpenAI e da xAI e saíram do código, do `.cfg` e da
tela. O ChatGPT do jogador entra pelo Codex (§15.8).

### 15.1 Provedores

| Id | Provedor | Acesso | Endereço | Modelo recomendado | Observações |
|---|---|---|---|---|---|
| `openrouter` | OpenRouter | login (PKCE) ou chave | `openrouter.ai/api/v1/chat/completions` | `deepseek/deepseek-v4.1-flash` | Sugerido para quem acabou de comprar. Custo real vem em `usage.cost`. |
| `codex` | ChatGPT (Codex) | login do próprio Codex (`codex login`) | `codex exec` (o Codex instalado no PC) | `gpt-6-luna` | Cota do Plus/Pro do jogador, sem chave. Mesmo motor do `@openai/codex-sdk`, chamado direto do C# (sem Node). Ver §15.8. |
| `deepseek` | DeepSeek | chave | `api.deepseek.com/chat/completions` | `deepseek-flash` | O calibrado. `thinking` + desconto fora do pico. |
| `openai` | OpenAI | chave | `api.openai.com/v1/chat/completions` | `gpt-6-luna` | `max_completion_tokens`, sem `temperature`. |
| `gemini` | Google Gemini | chave (AI Studio) | `generativelanguage.googleapis.com/v1beta/openai/chat/completions` | `gemini-2.5-flash-lite` | Nível grátis treina com os textos (aviso na tela). |
| `zai` | GLM (Z.ai) | chave | `api.z.ai/api/paas/v4/chat/completions` | `glm-4.7-flash` (grátis) | `thinking` liga/desliga. |
| `xai` | xAI (Grok) | chave | `api.x.ai/v1/chat/completions` | `grok-4.3` | Sem `json_object` confirmado: o json vem pelo prompt e é extraído. |

Proibido (não existe nem como opção escondida): login com assinatura Claude Pro/Max, Google AI Pro/Ultra ou Gemini
CLI, e o mod fingir ser outro app com o identificador dele. (O provedor Codex não finge: quem roda é o próprio Codex do
jogador.)

### 15.2 Como funciona

- **Fila (`ProviderRouter`):** as nações e o conselho chamam `ProviderRouter.Send` no lugar do cliente direto. A
  ordem vem de `[IA.Provedores] Ordem`, só com provedores visíveis e com credencial.
  - O primeiro que responder vale.
  - **Qualquer erro** (depois das tentativas normais de rede) tira o provedor por `MinutosForaAposErro` e passa para o
    próximo. Erro de json não é erro do provedor: esse continua no `DecisionRunner`, como antes.
  - Se **todos** falharem, a chamada falha e as travas da IA nativa se soltam como já acontecia com a API fora do ar.
    O jogador recebe **um** aviso nativo (`MessageModalWindow`) até um provedor voltar. Trocas de provedor vão para os
    eventos do F10.
- **Adaptadores (`LlmClient`):** `chat/completions` com as variações (`response_format`, formato do raciocínio,
  `max_tokens` × `max_completion_tokens`, temperatura); o Codex vai por `CodexCli` (§15.8). Com json pedido, a
  resposta passa por `ExtractJson`: tira `<think>`, cercas de código e texto em volta. O `DecisionParser` não mudou.
- **Erros:** `ErrorKind` = Auth, Credit, Rate, Request, Server, Network. 402, `insufficient_quota` (OpenAI), código
  1113 (Z.ai) e o limite de uso do Codex contam como falta de crédito. As mensagens passam por `Redact`, que troca
  pedaços de chave por `[chave]` (a OpenAI devolve `sk-proj-abc****wxyz`).
- **Preços (`ProviderCatalog` + `Pricing`):** tabela por provedor e modelo (US$ por milhão: cache, entrada, saída), que
  `PrecosPersonalizados` sobrescreve. No OpenRouter vale o `usage.cost` da resposta, e a lista de modelos traz o
  preço de cada um. Por assinatura, custo zero. O teto de gasto usa o custo do modelo em uso.
- **Estimativa por turno na tela:** 21 chamadas × (20 mil de entrada, 55% em cache, + 6,3 mil gerados), medido nos
  logs do Teste16 e da partida dos Francos com `deepseek-flash` e raciocínio "low" (~US$ 0,22 no pico do DeepSeek).
  Para modelos sem raciocínio é um teto.

### 15.3 Credenciais (`Credentials`)

- Um arquivo por provedor em `BepInEx\config\credenciais\<id>.dat`, criptografado com **DPAPI, usuário atual**
  (`CryptProtectData` direto do `crypt32.dll`, com entropia própria). Só abre no mesmo usuário do mesmo PC; em outro, o
  provedor aparece como "Sem chave".
- **Migração:** o `deepseek.key` é importado na primeira leitura e de novo se for editado depois (data mais nova que
  o `.dat`). Ele continua valendo. "Apagar a chave" do DeepSeek também esvazia o `deepseek.key`.
- **Nunca expor:** a tela mostra só os 4 últimos caracteres; o campo é modo senha (`*`). Nada de chave em log, F10,
  save, backup ou pacote (`backup.ps1` e `empacotar.ps1` recusam a pasta `credenciais` e procuram chaves dentro dos
  arquivos). Os comandos de desenvolvimento nunca recebem chave (o canal grava tudo no `result.txt`).

### 15.4 Logins (`OAuthLogin`)

- **OpenRouter (navegador):** PKCE S256 + `state` aleatório; retorno só no próprio PC (`TcpListener` em 127.0.0.1 e
  ::1, porta livre); 5 minutos; cancelável.
  - `openrouter.ai/auth?callback_url=http://localhost:<porta>/callback...` → `POST /api/v1/auth/keys` → chave do
    jogador, salva nas credenciais.
  - A página do navegador só responde **depois** da troca do código, com "Pronto! Pode voltar ao jogo" ou "O login não
    deu certo" (traduzidas).
- **Codex:** o "Entrar" roda o `codex login` do próprio Codex (§15.8).

### 15.5 A tela (`UI\ProvidersScreen.cs`)

- **Doadora:** `SystemSettingsScreen` (Configurações), registrada no grupo `SystemFullscreenGroup` (o mesmo das
  Configurações). Abre igual no menu principal e na partida, e o ESC fecha pelo grupo. Detalhes de montagem no guia de
  telas nativas, receita "Tela do sistema (menu principal e pausa)".
- **Esquerda:** "Em uso" + um botão por provedor visível, com o estado colorido embaixo do nome (Conectado · 1º, Sem
  chave, Não conectado, Com erro).
- **Centro, "Em uso":** provedor e modelo atuais, fila, gasto da partida; listas para ligar/desligar, teto de gasto,
  nações por turno, idioma das cartas e raciocínio; nota de privacidade.
- **Centro, um provedor:** posição na fila (lista), Entrar/Cancelar (login), campo da chave + Colar, modelo (lista com
  ★ e custo estimado por turno), Testar, Abrir site, Apagar/Sair (dois cliques), nota do que sai do PC.
  - Conectar o primeiro provedor já o põe na fila. Fila vazia de propósito vira `Ordem = nenhum`.
  - "Testar" faz uma chamada mínima (json `{"ok": true}`) e busca a lista de modelos da conta (no OpenRouter, também o
    crédito que sobra).
- **Entradas (`ProvidersButtons`):** botão "Diplomacia IA" no menu principal (depois de Cenários) e no menu de pausa
  (depois de Configurações). Na partida, a tela sempre abre por cima do menu de pausa, que esconde o HUD.

### 15.6 Comandos

| Comando | O que faz |
|---|---|
| `ia provedor` | Provedores, fila, credencial (só os 4 últimos), modelo, chamadas, gasto e último erro. |
| `ia provedor ordem a,b` | Muda a fila (sem nada = padrão). |
| `ia provedor modelo <id> <modelo>` | Muda o modelo de um provedor. |
| `ia provedor testar <id>` | "Testar conexão" (espera até 60 s). |
| `ia provedor falso <id>` | Testa com uma chave falsa na memória (mensagem de chave errada). |
| `ia provedor semrede <id>` | Testa um endereço que não existe (mensagem de sem internet). |
| `ia provedor chave-falsa <id>` / `apagar <id>` | Grava/apaga uma chave falsa (testes da fila). |
| `ia provedor rota` | Uma chamada mínima pela fila inteira, como as nações. |
| `ia provedor login <id>` / `login` / `cancelar` | Começa, mostra ou cancela um login. |
| `ia provedor login-teste <id>` | Login sem abrir o navegador; o teste faz o papel dele com um GET no retorno. |
| `ia provedor tela abrir [id\|uso]` / `fechar` / `secao <id>` / `lista N` / `escolher N I` / `enter` | Controla a tela. |

### 15.7 Testes (2026-10-06, Teste16, 16 impérios)

- Chave errada em DeepSeek, OpenAI, Gemini, GLM, xAI e OpenRouter: mensagem clara em todos.
- Sem rede: "Sem conexão com o provedor".
- Login do OpenRouter até a troca do código (retorno com `state` errado recusado; código falso → "O provedor recusou
  o login").
- Fila: OpenAI com chave falsa em 1º → DeepSeek respondeu; com só a OpenAI, aviso nativo uma vez.
- Turnos reais com DeepSeek pela fila: ver `research\provedores-ia.md` (custo, tempo, erros de json).
- ChatGPT pelo Codex (gpt-6-luna, conta do lucas): Testar conexão em 6,8 s; turno T78 com 15/15 nações em ~1 min, US$ 0 (§15.8).
- Campo da chave: digitação mascarada e validação pelo Enter do campo. O Enter mandado por `PostMessage` não chega
  ao jogo fora da partida (nem nas Configurações nativas), então ele foi testado pelo caminho do próprio campo
  (`ia provedor tela enter`).

### 15.8 ChatGPT pelo Codex (`Llm\Providers\CodexCli.cs`, 2026-10-06)

Decisão do lucas: usar a conta do ChatGPT do jogador pelo Codex (o mesmo `codex exec` que o `@openai/codex-sdk`
roda), no pacote vendido, assumindo o risco de política: a OpenAI pede aprovação para app pago usar a cota do plano
("Sign in with ChatGPT"), e esse caminho não passa por ela. A tela avisa que o uso segue os termos da OpenAI.

- **Instalação e login são do Codex.** O mod acha o `codex.exe` (app do Codex em `%LOCALAPPDATA%\OpenAI\Codex\bin\<versão>`,
  o PATH ou `[IA.Provedores] CodexCaminho`), confere `codex login status` (em segundo plano, a cada 2 min ou ao abrir a
  tela) e, no "Entrar", roda `codex login`, que abre o navegador. **O mod nunca lê o `auth.json` do Codex** e não tem
  "Sair" (o jogador sai pelo Codex).
- **Cada chamada:** `codex exec --json --skip-git-repo-check --ephemeral --ignore-user-config --ignore-rules
  --sandbox read-only -C <pasta vazia> -m <modelo> -c model_reasoning_effort=...` + instruções próprias curtas
  (`model_instructions_file`), sem permissões nem ambiente (`include_permissions_instructions=false`,
  `include_environment_context=false`, `include_apps_instructions=false`), sem busca na web e com as ferramentas
  desligadas (`--disable shell_tool unified_exec apps browser_use ... `; `code_mode_host` fica ligado, senão o Codex
  reclama). O prompt (sistema + mensagem + conversa das novas tentativas) vai pela entrada padrão.
- **Medido (ping `{"ok":true}`):** padrão do Codex ~20,4 mil tokens de entrada e ~15 s; com os cortes, ~10,1 mil e
  ~5–6 s. Esses ~10 mil vêm em toda chamada, somados ao dossiê.
- **Resposta:** eventos JSONL; vale o último `agent_message`, e o uso vem no `turn.completed`. Avisos do próprio
  Codex ("Codex is ignoring...", "Code Mode is unavailable", "Reconnecting...") não contam como erro. Limite de uso →
  Credit; 401/sem login → Auth (força conferir o login de novo); tempo esgotado mata o processo.
- **Custo em dólar:** zero (cota do plano). Plus: o gpt-6-luna tem cota bem maior que os Sol (ver
  `research\provedores-ia.md`).
## 16. Licença (removida em 2026-10-08)

O mod passou a ser gratuito e de código aberto (MIT). A chave de licença, o servidor de licenças e a loja foram
removidos do código: a Diplomacia IA funciona para todos que configurarem um provedor na tela Realpolitik.
