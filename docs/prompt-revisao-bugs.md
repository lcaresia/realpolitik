# Prompt: revisão de bugs de todo o código dos mods

> Prompt independente para uma sessão nova do Claude Code, aberta na pasta do Humankind. Cole o conteúdo abaixo da linha.

---

## Objetivo

Revisar **todo o código C# que o Claude escreveu** para os mods de Humankind do lucas, achar **bugs reais** e
corrigi-los, testando no jogo. Não é refatoração, não é feature nova e não é rebalanceamento.

O usuário fala português (pt-BR, informal). Responda em pt-BR, com relatórios curtos, e mande os prints importantes
(SendUserFile). A sua memória do projeto (MEMORY.md) já vem carregada: leia os arquivos dela que tocarem cada área.

## O projeto (leia antes de revisar)

- Mapa das pastas, compilação e funcionalidades: `_Modding\README.md`.
- Documentação por área:
  - `docs\diplomacia-ia.md` (técnico da IA de linguagem; §7 tem os comandos de teste; §12 as ações, travas e Congresso);
  - `docs\design-diplomacia-ia.md` (decisões de design);
  - `docs\bloqueio-comercial.md` (pedágio, bloqueio, prévia de desvio);
  - `docs\proposta-ciclo-economico.md` (economia, com a calibração no §10);
  - `docs\guia-telas-nativas.md` (interface nativa, kit de desenvolvimento e armadilhas já resolvidas);
  - `docs\instalacao.md` e `docs\pesquisa-30-jogadores.md` (MoreEmpires).
- Código do jogo descompilado: `_Modding\decompiled\`. É a fonte da verdade para qualquer API do jogo.

Código a revisar (≈ 35 mil linhas):

| Pasta | O que é | Linhas |
|---|---|---|
| `src\CurrencyMod\` (raiz) | núcleo: economia, câmbio, conversão, textos, `NativeEffects`, saves, comandos `jogo`, kit de desenvolvimento | ~5 mil |
| `src\CurrencyMod\Diplomacia\` e subpastas | IA de linguagem (DeepSeek): foto do turno, dossiê, prompt, parser, chamada à API, executor de ordens, travas e viés da IA nativa, Congresso, rendição, exércitos, correio, espionagem, conselho, visualizador | ~20 mil |
| `src\CurrencyMod\NativeUI\` | Banco Central, Posto Comercial, painéis na diplomacia, `NativeUIKit` | ~3,5 mil |
| `src\CurrencyMod\Trade\` | bloqueio comercial, pedágio, IA de regras, prévia de desvio | ~1,4 mil |
| `src\CurrencyMod.Loader\` | carregador fixo com recarga a quente do núcleo | ~200 |
| `src\MoreEmpires\` | plugin separado: até 16 impérios | ~4,4 mil (com testes) |

`src\HelloHumankind\` não está instalado: ignore.

## Compilar, instalar e testar

- .NET SDK 8 em `C:\Program Files\dotnet` (ponha no PATH do terminal).
- **Núcleo:** `dotnet build _Modding\src\CurrencyMod -c Release` compila e instala. Com o jogo aberto, o carregador
  recarrega sozinho em ~15 s (o `BepInEx\LogOutput.log` mostra "Núcleo carregado (geração N)"). Com
  `-p:SkipDeploy=true` só compila.
- **Carregador e MoreEmpires:** só com o jogo fechado (o carregador exige reiniciar o jogo; o MoreEmpires instala com
  `tools\instalar-moreempires.ps1`).
- **Canal de teste com o jogo aberto:** `_Modding\tools\dev\devcmd.ps1 -Commands @("ia status", "jogo estado")`. Ele
  escreve em `dev\cmd.txt` e mostra as respostas novas de `dev\out\result.txt`; respostas longas vão para um arquivo
  em `dev\out`. Os comandos estão em `docs\diplomacia-ia.md` §7 e em `docs\guia-telas-nativas.md` §1: `screenshot`,
  `find`, `tree`, `inspect`, `hover`, `click`, `ia …`, `jogo …`, `trade …`, `nbank …`.
- `tools\dev\passturn.ps1` passa um turno esperando todas as nações decidirem; `passturns.ps1 -Target N` passa
  vários; `backup.ps1` faz o backup.
- **Partida de teste:** "Teste16 T76 congresso" (16 impérios, DLC Together We Rule ligado; `jogo carregar Teste16 T76
  congresso`). É partida de teste: pode passar turnos e fazer ações de teste nela. **Teste sempre com 16 jogadores.**
- Para ver uma tela: `screenshot <nome>` grava `dev\out\<nome>.png`; abra com Read. Mande ao usuário os prints do
  que mudou.

## Regras que não se quebram

- A chave da API fica só em `BepInEx\config\deepseek.key`. Nunca a mostre, logue, copie para backup ou pacote, nem
  a cole no chat.
- **Nunca instale o núcleo com as nações pensando:** antes, `ia status` tem que dizer "pensando agora: 0" (a recarga
  perde as chamadas em voo). Depois de instalar, espere ~18 s antes de mandar comandos.
- Não rode `ia reset`. Não mande cartas em nome do usuário sem perguntar. Não use `DiplomaticAmbassy` para bloqueio.
- Fora do escopo: o pacote de instalação e mais de 16 impérios.
- **Não mude balanceamento nem decisões de design sem perguntar** (valores do `.cfg`, regras do jogo, o que as nações
  podem fazer). Conta errada é bug; "acho que deveria ser outro número" não é.
- Nada de refatoração de estilo, renomeação ou reorganização. Corrija o mínimo, no estilo do código em volta
  (comentários e textos em pt-BR).
- Interface: idêntica à nativa; janelas fecham com ESC e quando outra coisa abre, e nunca se sobrepõem; campos de
  texto não disparam atalhos do jogo.
- Faça backup (`tools\dev\backup.ps1`) antes de começar e no fim.

## Como revisar

1. Antes do código de cada área, leia o documento dela.
2. Revise área por área, cada uma inteira. Pode usar subagentes em paralelo (um por área) para levantar suspeitas;
   **você confirma cada uma** lendo o código e o descompilado antes de mexer.
   - **Economia e moeda:** `EconomySimulation`, `EconomyConfig`, `CurrencyState`, `CurrencyManager`, conversão
     (`TransferConversion`), `TextPatches`, `NativeEffects`, `SavePatches`, `EconomyDiagnosis`.
   - **Bloqueio comercial:** `Trade\*`, `NativeUI\TradePostWindow`, `NativeUI\DiplomacyTradePolicyPanel`.
   - **IA de linguagem:** captura (`Diplomacia\Capture\`), dossiê (`DossierBuilder`, `DossierCongress`), `Prompts`,
     `DecisionParser`, `DecisionRunner`, `IaModule`, executor (`ActionExecutor`, `CongressFlow`, `SurrenderFlow`,
     `ArmyOrders` e os demais fluxos), travas (`NativeAiLocks`, `NativeAiBias`, `ProposalHold`), `Espionage`,
     conselho (`Council\`), visualizador (`Viewer\`), `ExpansionSavePatch`.
   - **Telas nativas:** `NativeUI\*`, `Diplomacia\UI\*`, `ExclusiveWindows`.
   - **Carregador e recarga a quente:** `CurrencyMod.Loader`, `Plugin.cs` (`ModEntry.Start/Stop`).
   - **MoreEmpires.**
3. O que procurar (bug real, com um cenário concreto que quebra):
   - **Threads:** a simulação roda numa thread própria (sandbox), a IA nativa em outras, e a interface e o `IaModule`
     na principal. Procure estado do jogo lido ou escrito na thread errada, coleções compartilhadas sem lock, estado
     estático lido por patches sem troca atômica, e API da Unity (Time, GameObject, componentes) fora da principal.
   - **Recarga a quente:** estado estático que nasce vazio na geração nova (a tabela de travas já foi corrigida com
     `NativeAiLocks.Carry`), patch, componente, evento ou janela que o `ModEntry.Stop` não desfaz, e assinatura de
     evento duplicada.
   - **Save e load:** estado do mod (`CurrencyMod.json`, `DiplomaciaIA.json`) que não volta igual, campo novo sem
     valor padrão para save antigo, e migração (`IaWorld.Version`).
   - **Patches Harmony:** prefixo que pula o original sem querer, pós-fixo que lê ou troca `__result` errado, e patch
     num método que o jogo também chama em outro contexto. Exemplo já corrigido: `DestroyPath` esvazia `TradeNodes`
     antes de procurar o caminho novo. Procure também exceção engolida que desliga um sistema calado.
   - **Contas:** divisão por zero, NaN ou infinito, arredondamento, sinal, unidade (por turno × total, moeda própria
     × moeda do outro), e contagem dupla (já houve: rota contada nas linhas dos dois impérios).
   - **Índices e chaves:** índice de império, território, exército ou evento fora do limite; índice que o jogo
     reaproveita (eventos de osmose); chave de dicionário que colide.
   - **IA de linguagem:** validação que deixa passar ação impossível ou recusa ação válida; executor que diz "feito"
     sem conferir; decisão que a trava não cobre (a IA nativa decidindo por uma nação da IA de linguagem); volta à IA
     nativa que nunca dispara ou dispara cedo; dossiê que mostra o que a nação não deveria saber (névoa de guerra).
   - **Interface:** janela que não fecha, vaza na tela ou some depois da recarga; texto que transborda; botão sem
     clique; tooltip errado.
4. Para cada bug: anote arquivo:linha, o cenário que quebra e a gravidade; confirme no descompilado quando depender
   do jogo; corrija o mínimo; compile.
5. Teste no jogo, na partida de 16, toda correção que muda comportamento (com print quando for visual). Se não der
   para testar ao vivo, diga por quê.
6. Atualize o documento da área (o que mudou e por quê) e a memória quando for uma lição para o futuro.

## O que já se sabe (não reabrir sem motivo)

- Testado e funcionando em 2026-10-05:
  - Congresso: votos, subornos, crises, veredito com guerra, lei proposta pela IA e lei imposta com "adotar";
  - pedágio: preços por posto e por império e a prévia de desvio;
  - economia: curva suave da pressão e peso decrescente da dívida na regra dos juros.
- Ainda sem teste ao vivo: o ramo "recusar" da lei imposta e a volta da primeira sessão do Congresso.
- Bugs já corrigidos nesta fase (procure parecidos em outros lugares):
  - custo do pedágio no recálculo real das rotas (`TradeNodes` vazio);
  - travas da IA nativa abertas durante a recarga a quente;
  - total de rotas contado duas vezes;
  - chave de evento de osmose reaproveitada.

## Entrega

- Relatório em pt-BR, com:
  - os bugs achados (gravidade, arquivo, cenário);
  - os que foram corrigidos e testados;
  - os corrigidos sem teste ao vivo, e por quê;
  - as suspeitas que não se confirmaram e o que precisa de decisão do usuário.
- Backup no fim e prints das telas que mudaram, mandados ao usuário.
