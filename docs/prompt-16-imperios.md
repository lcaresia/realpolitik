# Prompt: Humankind com 16 impérios em qualquer tamanho de mapa

> Prompt independente para uma sessão nova do Claude Code. Cole o conteúdo abaixo da linha.

---

## Objetivo

Fazer o Humankind aceitar partidas com **até 16 impérios maiores** (jogador + IA, sem multiplayer online), **em qualquer tamanho de mapa**, do menor ao maior.

Hoje o jogo tem dois limites:
- no máximo 10 impérios;
- o tamanho do mapa limita ainda mais (mapas pequenos não aceitam 10).

Tudo o que hoje vale "até 10" deve valer "até 16":
- lobby;
- limite por tamanho de mapa;
- cores;
- personas;
- geração de mapa;
- interface dentro da partida.

**Fora do escopo:** passar de 16. Acima disso o motor quebra com máscaras de 16 bits (`ushort`); isso fica para outro trabalho.

O usuário fala português (pt-BR, informal). Responda em pt-BR. Toda interface nova ou ajustada tem que ser idêntica à nativa do jogo.

## Ambiente (Windows, sem git)

- **Jogo:** `C:\Program Files (x86)\Steam\steamapps\common\Humankind` (Unity 2021.3, Mono).
- **Mod loader:** BepInEx 5.4.23.5 x64 na raiz do jogo, com HarmonyX. O backup das DLLs originais está em `Humankind_Data\Managed_backup`. **Nunca altere as DLLs do jogo no disco.**
- **Projeto:** `_Modding\`.
  - Leia primeiro `_Modding\README.md`.
  - O guia de UI nativa está em `_Modding\docs\guia-telas-nativas.md`.
  - A pesquisa de contexto é `_Modding\docs\pesquisa-30-jogadores.md`.
- **Fontes decompiladas:** `_Modding\decompiled\<Assembly>\`.
  - Os assemblies `Amplitude.Mercury.Terrain`, `Fx` e `Graphics` não estão lá. Decompile-os com `ilspycmd` se precisar.
- **.NET SDK 8:** fica em `C:\Program Files\dotnet` e **não está no PATH**. Prefixe o caminho nos comandos. O ilspycmd instalado é a versão 9.1.
- **Mod existente (CurrencyMod):**
  - `src\CurrencyMod.Loader` é um plugin fixo que faz hot-reload do núcleo.
  - `src\CurrencyMod` é o núcleo, recarregado a quente. Usa Krafs.Publicizer sobre Firstpass, Framework, Amplitude.UI e Assembly-CSharp.
  - Canal de comandos de desenvolvimento: escreva em `_Modding\dev\cmd.txt` e leia a resposta em `_Modding\dev\out\result.txt`.
  - Leia o README para entender o padrão.
- **Backup:** antes de começar, gere um zip em `C:\Users\lucas\Documents\HumankindModding\backups\HumankindModding_<data>.zip`.
  - Inclua src (sem bin/obj), docs e config.
  - **Nunca inclua `BepInEx\config\deepseek.key`.**
- **Limitações do PowerShell:**
  - Não use `robocopy /E`; use `Copy-Item`.
  - Não remova arquivos de dentro de Program Files com `Remove-Item`. Use o scratchpad para arquivos temporários.

## Arquitetura

Crie um **plugin Harmony separado**: `src\MoreEmpires` → `BepInEx\plugins\MoreEmpires\MoreEmpires.dll`. Use Krafs.Publicizer como o CurrencyMod.

- Config BepInEx: `[Geral] MaxImperios = 16`. Valide o valor no intervalo 2..16.
- Toda constante nova deriva desse valor.
- Mudanças no plugin exigem **reiniciar o jogo**. Agrupe as mudanças e avise o usuário antes de pedir um reinício.
- Se for útil para depurar, adicione comandos de desenvolvimento no mesmo padrão `cmd.txt` / `result.txt`, ou registre no `BepInEx\LogOutput.log`.
- **Antes de patchar cada ponto, confira na fonte decompilada que o código é o descrito.** As linhas são aproximadas.

## O que já suporta 16 (não mexer)

- `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Sandbox\Sandbox.cs:3521` só recusa **mais de 16** impérios maiores. Com 16, não precisa de patch.
- Todas as máscaras de bits por império (`ushort` e `int`) comportam os índices 0..15.

## Tarefas

### 1. Lobby (`Assembly-CSharp\Amplitude.Mercury.UI\`)

- `LobbyScreen.cs:224-233`: `AllowedMaxLobbySlots` devolve um **10 literal** quando não há mapa customizado, ou `currentTerrainSaveDescriptor.EmpiresCount` quando há. Faça devolver `MaxImperios` no primeiro caso. A linha 44, `MaxLobbySlots = 10`, quase não é usada, mas troque também.
- `LobbyScreen_LobbySlotsPanel.cs`:
  - linha 87, `ReserveChildren(10, lobbySlotSample)`;
  - linhas 127 e 142, laços `for (i < 10)`;
  - linha 134, `allLobbySlots[j]` com `j < Slots.Count`, que estoura o índice com mais de 10 slots.

  Troque 10 por `MaxImperios` com transpiler.
- `LobbyScreen_SessionPanel.cs` (181, 420, 428, 434): os botões +/- e o campo de texto travam em `[2, AllowedMaxLobbySlots]`. Confirme que respeitam o novo valor.
- **Layout:** com 16 slots, a lista provavelmente não cabe na tela. Use a rolagem nativa do jogo (veja o guia de UI) ou compacte as linhas no estilo nativo.
  - Nada pode ficar cortado ou sobreposto.
  - O botão de adicionar slot tem que continuar visível.
- **Opção de dados `GameOption_SlotCount`:** confira se os estados (`States`) da definição só vão até 10. Se for o caso, acrescente em tempo de execução os estados que faltam, até `MaxImperios`, copiando o formato dos existentes.
  - Fluxo: `SessionState_LobbyOwner.cs:97-119` → `Session.cs:1158` → `Slots.SetSlotCount` em `Session.cs:1673-1753`.
  - Atenção também a `SessionState_LobbyOwner.cs` linhas ~45 e ~71: laços `< 16`. Confirme que não atrapalham.

### 2. Limite de jogadores por tamanho de mapa (qualquer mapa aceita 16)

- O limite por tamanho de mapa vem de **dados**, não de código: são `OptionConstraint` dentro das `GameOptionDefinition`.
  - Classes: `Amplitude.Framework\Amplitude.Framework.Options\OptionDefinition.cs` (`Constraints`) e `OptionConstraint.cs` (`If OtherOptionName/Value/Not` → `Then Value/Visibility`).
  - Esses constraints marcam estados de `GameOption_WorldSize` e/ou `GameOption_SlotCount` como `OptionVisibilityState.Invalid` ou ocultos, conforme o valor da outra opção.
- **Passo 1:** com um comando de desenvolvimento ou log, despeje todas as `GameOptionDefinition` cujos constraints citem `GameOption_SlotCount` ou `GameOption_WorldSize`. Anote o que elas dizem.
  - Para achar a coleção, veja `Amplitude.Mercury.Data\Amplitude.Mercury.Data.GameOptions\GameOptionDefinition(Collection).cs` e os databases do framework.
- **Passo 2:** depois que os dados carregarem e antes de o lobby abrir, remova ou neutralize **só** os constraints que ligam o número de jogadores ao tamanho do mapa. Os demais ficam intactos.
- Quem lê os constraints:
  - `Amplitude.Framework.Options\Option.cs` (98-102 e 350-353);
  - `Assembly-CSharp\Amplitude.Mercury.Options\GameOptionsManager.cs` (745-790, sorteio de opções aleatórias);
  - `Amplitude.Mercury.UI.Helpers\GameOptionUtils.cs` (35-145, texto "Cannot be picked because…").

  Confira que, depois do ajuste, o lobby aceita 16 jogadores em todos os tamanhos e que a opção aleatória de tamanho de mapa não fica inválida.
- **Mapas customizados:**
  - `Assembly-CSharp\Amplitude.Mercury.UI.Helpers\OutgameUtils.cs:377-379` marca `empiresCount > 10` como `TooManyStartingPoint`. Troque para `MaxImperios`.
  - `WorldMapProviderHelper.GetMaxPlayablePlayerCount` (assembly Terrain) já devolve 16. Confirme.
  - O editor de mapas (`EmpireCountOptions` de "1" a "10") fica opcional: só ajuste se for simples.

### 3. Geração de mapa com 16 impérios em mapas pequenos

O gerador procedural (`Amplitude.Mercury.WorldGenerator`) dimensiona os arrays pelo número de impérios:
- `SelectSpawnRegions.cs:120-121`;
- `CreateContinents.cs:79, 210, 509` força pelo menos `EmpiresCount` regiões.

Se faltarem regiões, `PickSpawnRegion` (192-197) **deixa o spawn em (0,0) sem erro**, e os impérios acabam empilhados. A importação de spawns lança "Insufficient number of spawn location" em `WorldGeneratorOutput.cs:393-420`.

Gere uma partida de teste com 16 impérios em **cada tamanho de mapa**, com as configurações padrão de continentes e de terra/água. Para cada uma, verifique:
- todos os 16 impérios receberam ponto inicial válido e distinto;
- ninguém nasceu no tile (0,0) nem empilhado;
- nenhuma exceção no log.

Se algum tamanho falhar, corrija com o mínimo de interferência e na ordem abaixo:
1. Só quando houver mais impérios do que o tamanho comportava originalmente, aumente as regiões ou os territórios gerados: `WorldGeneratorOptions.ExpectedRegionArea` menor, preenchido por `GameOptionsManager.cs:596-656` a partir da opção WorldSize.
2. Ou relaxe a distância mínima entre spawns.
3. Em último caso, aumente o `WorldTileCount` daquele tamanho.

Limites rígidos: no máximo 255 territórios no mapa todo e menos de 65 536 tiles.

Registre no documento quais tamanhos precisaram de ajuste e qual ajuste foi feito.

### 4. Cores dos impérios

- O jogo tem só **12 cores**:
  - `Amplitude.Mercury.Data\...\PaletteDefinition.cs:7` define `MajorEmpiresColors = new PaletteColor[12]`;
  - `Assembly-CSharp\Amplitude.Mercury.PlayerProfile\G2GPlayerProfileManager.cs:138` define `numberOfColorForSlot = 12`.
- Hoje, com mais de 12 impérios, `Session.cs:1208-1217` (`GetAvailableColorIndexes`) se esgota e `InitializeSessionSlotColor` (`Session.cs:1886-1904`) loga erro e dá **cor 0** para os excedentes.
- Gere em tempo de execução as cores que faltam, até `MaxImperios`.
  - Elas têm que ser bem distintas entre si e das 12 nativas.
  - Use a mesma faixa de saturação e brilho das nativas e matizes intercalados.
  - Cada `PaletteColor` precisa ter todos os campos preenchidos (fronteira, bandeira, UI). Copie a estrutura de uma nativa e ajuste o tom.
- A paleta salva no registro só é aceita com `(NumberOfColorForSlot+1)*3` entradas (`G2GPlayerProfileManager.cs:729`). Garanta que a paleta do usuário não seja descartada nem corrompida.
- A tela de configuração de cores (`ColorSettingsPanel_EmpireColors.cs:21` e `ColorSettingsPanel.cs:112`) usa arrays fixos de prefab. Ela não pode quebrar; mostrar só as 12 primeiras é aceitável.
- Lookups que precisam funcionar com índice até 15: `EmpireNamesRepository.cs:105, 302` (`ColorBySlotIndex[empire.ColorIndex]`).

### 5. Personas, facções e símbolos

- `Session.cs:1854-1866` (`FillRandomPersonaForSlot`) tira cada persona sorteada do pool e não tem fallback.
  - Quando o pool acabar, recicle: permita repetir, mas prefira as menos usadas.
  - Veja também `G2GPlayerProfileManager.cs:184`, `DefaultPersonaForFirstLaunch[10]`.
- Facções e símbolos (`Session.cs:1915-1919` e `1952-1956`) já duplicam e só logam erro. Faça a duplicação acontecer sem erro no log.

### 6. Interface dentro da partida com 16 impérios

As listas já se dimensionam pelo número de impérios. O risco é o layout. Confira com capturas de tela:
- o banner diplomático do topo (15 retratos);
- a tela de diplomacia;
- o painel de fim de turno (`EndTurnWindow_PlayerStatus`);
- as estatísticas e o pódio de fim de jogo (`EndGameStatisticsPanel*`);
- a tela internacional;
- o placar.

Corrija o que ficar cortado ou sobreposto usando o kit de UI nativa do projeto. Coloque dicas curtas ao passar o mouse em qualquer elemento novo.

### 7. Compatibilidade com o CurrencyMod

- Varra `src\CurrencyMod` à procura de arrays, laços ou chaves que assumam 10 impérios.
- As chaves `*64` (`TradePolicy.Key`, `TradeBlockade.cs:386`, `TurnCapture.cs:418`) só funcionam se o índice multiplicado for de império maior.
  - Confirme isso.
  - Se algum índice puder ser de povo independente (com 16 maiores, os índices dos menores vão até ~116), troque o multiplicador por 256.
- Telas do CurrencyMod que listam impérios (Banco Central, painel econômico da diplomacia, janela do posto comercial) precisam caber ou rolar com 15 impérios estrangeiros.

### 8. Desempenho

A IA roda em uma única thread, um cérebro depois do outro (`Amplitude.Mercury.Firstpass\Amplitude.Mercury.AI\AIController.cs`, `RunAIDecisionCycle` 1695-1773). Não altere isso.

- **Meça:** crie um comando ou log que registre a duração de cada passagem de turno, do clique em fim de turno até o próximo turno do jogador. Compare uma partida com 10 impérios e outra com 16 no mesmo tamanho de mapa, por ~30 turnos.
- **Ponto de atenção:** `DepartmentOfForeignAffairs.GetVisibilityBits` (linhas 125-180) é uma DFS recursiva sem memória. Com blocos grandes de visão compartilhada, o custo cresce de forma fatorial.
  - Só otimize se as medições mostrarem impacto.
  - Se otimizar, use fecho transitivo (BFS) com resultado **bit a bit idêntico**. Compare as duas versões antes de trocar.

### 9. Pontos de nascimento aleatórios

Hoje, em mapa customizado, o usuário **sempre nasce no mesmo lugar**. A causa:

- `Amplitude.Mercury.WorldGenerator\Amplitude.Mercury.WorldGenerator\WorldGeneratorOutput.cs:393-421` (`ImportSpawnLocationsTable`) copia os spawn points do mapa **na ordem do arquivo**. Ficam só os que valem para o número de jogadores, conforme `spawnPoints[i].IsSpawnPoint(empiresCount)`.
- Depois, o império de índice `i` recebe `World.Tables.SpawnLocations[i]`:
  - `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\MajorEmpire.cs:838`;
  - `DepartmentOfDefense.cs:7991, 8068`.
- O jogador humano é quase sempre o índice 0, e por isso pega sempre o primeiro ponto.

O que fazer:

- **Postfix em `ImportSpawnLocationsTable`.** Embaralhe `map1D.Data` com Fisher-Yates. Assim, cada império, humano ou IA, recebe um ponto aleatório entre os válidos do mapa.
  - Use uma semente nova a cada **partida nova**.
  - Não reaproveite a semente fixa do mapa customizado, senão o resultado se repete.
  - `SpawnLocations` é salvo no save (`World.cs:154, 3072`). Ao carregar, nada pode ser re-sorteado. Confirme que o import só roda ao criar a partida.
- **Config `[Nascimento] Modo`:**
  - `Embaralhar` (padrão): sorteia a ordem entre os pontos que o mapa definiu para aquele número de jogadores.
  - `QualquerPonto`: sorteia entre **todos** os spawn points do mapa, de qualquer número de jogadores. Use as flags de `SpawnPoint`, que no assembly Terrain tem `uint Flags` e `MaxEmpireCount = 32`.
    - Garanta uma distância mínima entre os escolhidos, por exemplo a menor distância entre os pontos do conjunto original daquele número de jogadores.
    - Se não der para cumprir, volte para `Embaralhar` e registre no log.
  - `Original`: comportamento do jogo.
- **Mapas procedurais:** confira se a ordem dos spawns gerados (`ExportSpawnRegions.cs`, `SelectSpawnRegions.cs`) já é aleatória em relação ao índice do império. Se o jogador tender sempre à mesma região relativa (por exemplo, sempre o primeiro continente), aplique o mesmo embaralhamento.
- **Efeito colateral:** `MinorFactionManager.cs:994-998` e `World.cs:909-916` leem `SpawnLocations` por índice antes das cidades existirem. Confirme que continuam corretos com a ordem embaralhada; eles só usam distância e continente.
- **Multiplayer:** fora do escopo. Registre no doc que o embaralhamento precisaria de semente sincronizada entre os jogadores.

## Testes de aceitação

1. O lobby permite de 2 a 16 jogadores em **todos** os tamanhos de mapa, sem item cortado, e o seletor de tamanho não marca nada como inválido por causa do número de jogadores.
2. Uma partida com 16 impérios inicia em cada tamanho de mapa, com spawns válidos e distintos e 16 cores visualmente distintas.
3. 50 turnos jogados num mapa médio com 16 impérios. Durante o jogo, aconteçam:
   - guerra;
   - aliança;
   - fronteiras fechadas;
   - comércio;
   - visão compartilhada envolvendo os impérios de índice 10 a 15.

   No fim, salve, carregue o save e confirme zero exceções novas no `LogOutput.log`.
4. Tempo de turno medido com 10 e com 16 impérios, e informado ao usuário.
5. Com `MaxImperios = 10` e `[Nascimento] Modo = Original`, o jogo se comporta exatamente como o original.
6. Nascimento aleatório: no mesmo mapa customizado, 5 partidas novas seguidas põem o jogador em pelo menos 3 pontos diferentes. Um save carregado mantém exatamente os mesmos pontos.

## Entregáveis

- `src\MoreEmpires` compilado e instalado em `BepInEx\plugins\MoreEmpires\`.
- Atualizar `_Modding\docs\pesquisa-30-jogadores.md` com uma seção "Etapa 1 feita": o que foi alterado, os ajustes de mapa por tamanho, os números de desempenho e as pendências.
- Seção no `_Modding\README.md` explicando o mod e como instalar em outra máquina.
- Backup em zip ao final.

## Forma de trabalho

- Itere por conta própria: compile, rode e confira o log.
- Ao terminar, mostre ao usuário capturas do lobby com 16 jogadores e da partida.
- Não abra janelas por cima da tela do usuário enquanto ele estiver jogando.
- Informe os resultados com honestidade: o que foi testado e o que não foi.
