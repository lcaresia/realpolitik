# Pesquisa: subir de 10 para 30 impérios (IA)

Pesquisa feita em 2026-10-04 sobre o código decompilado (`_Modding\decompiled`). Nada foi implementado ainda.

## Onde estão os limites

| Limite | Onde | Efeito acima dele |
|---|---|---|
| 10 | `LobbyScreen.AllowedMaxLobbySlots` (literal 10), `LobbyScreen_LobbySlotsPanel` (`ReserveChildren(10)`, laços `i < 10`), `OutgameUtils` linhas 377-379 (`empiresCount > 10`) | Só interface: o lobby trava/estoura índice |
| 12 | `PaletteDefinition.MajorEmpiresColors[12]`, `G2GPlayerProfileManager.numberOfColorForSlot = 12` | Impérios 13+ recebem a cor 0 (sem crash, ilegível) |
| 16 | `Sandbox.cs:3521` (`NumberOfMajorEmpires > 16` → exceção) | A partida não inicia |
| 16 | Máscaras `ushort` por império: `PathfindContext` (4 campos), `DepartmentOfForeignAffairs` 892/991/1014, `Interop.AI.Entities\MajorEmpire` 737-757, `SimulationEvaluator` 733/738, `TransitionEvaluator` 264/772, `BattleAbilityController` 627, `BeliefManager` 406, `NewlyExploredTile` (struct fixa de 5 bytes), `BombardmentAftermathInfo`, `AI.Brain ...Nodes.cs` 102, `VisibilityController` 443/585 (`needRefreshBits = 65535`), `Collectible.cs:150` | Erros silenciosos: os impérios 16+ perdem regras de movimento, guerra/aliança, visão e confirmações |
| 16 | `WorldMapProviderHelper.GetMaxPlayablePlayerCount` (mapas customizados) | Mapas customizados ficam com no máximo 16 |
| 32 | Máscaras `int`: `MajorEmpire.Bits`, todos os mapas de visão/exploração por tile, visão de batalha, colecionáveis, pings | Teto absoluto (30 cabe) |
| 31 | `CollectibleInfo` testa as máscaras com `> 0` | O bit 31 falha (30 cabe) |
| 255 | Índice de território com 8 bits no terreno (`MapValidationTerritory.MaxTerritoryCount`) | Limita o tamanho do mapa: cerca de 8 territórios por império com 30 |

Os índices dos impérios seguem esta ordem: maiores 0..N-1, depois os menores (5 por maior a mais, até 100), depois um LesserEmpire.

Com 30 maiores, os menores começam no índice 30. Pontos sem guarda como `1 << indice`, que já estão errados no jogo original, passam a colidir com os bits dos maiores (os índices ≥ 32 dão a volta). Exemplos: `Territory.cs:374/383`, `TerritoryThreat.cs:343`, `TransitionEvaluator.cs:772`, `SimulationEvaluator.cs:733`, `TerritoryDistances.cs:112`.

## Desempenho

- A IA roda em uma única thread, um cérebro depois do outro, sem orçamento de tempo (`AIController.RunAIDecisionCycle`).
- Crescimento de 10 para 30:
  - parte linear ×3;
  - pares de diplomacia/comércio ×9,7 (de 45 para 435);
  - `ComputeMultiEmpireSituations` / `CulturalScore` ×27.
- `DepartmentOfForeignAffairs.GetVisibilityBits` é recursiva e não guarda resultados intermediários. Com um bloco grande de visão compartilhada o custo vira fatorial. Precisa ser trocada por fecho transitivo (BFS).
- A memória sobe algumas dezenas de MB, o que não é problema.

## Plano sugerido

1. **16 impérios.** Lobby, `OutgameUtils`, paleta 16, fallback de persona. Não exige mexer nas máscaras.
2. **30 impérios.**
   - Patcher no preloader do BepInEx (Mono.Cecil) para alargar os campos `ushort` → `int` e a struct `NewlyExploredTile`.
   - Transpilers nos pontos `conv.u2`.
   - Guardas `< NumberOfMajorEmpires` nos `1 << indice`.
   - `GetVisibilityBits` em BFS.
   - Paleta 30, tamanhos de mapa novos e rolagem no lobby.

---

## Etapa 1 (até 16 impérios): implementada e testada em parte

**Estado em 2026-10-04: instalado e testado no jogo com 16 impérios (Tiny, Normal e Huge). Resultados em "Testes no jogo (2026-10-04)", logo abaixo da tabela de ajustes de mapa.**

O mod se chama **MoreEmpires** e é um plugin BepInEx separado do CurrencyMod.

### Onde está e como usar

| O quê | Onde |
|---|---|
| Código | `src\MoreEmpires\` (`Plugin.cs`, `Core\`, `Features\`) |
| DLL compilada | `src\MoreEmpires\bin\Release\net472\MoreEmpires.dll` |
| Instalado em | `BepInEx\plugins\MoreEmpires\MoreEmpires.dll` |
| Configuração | `BepInEx\config\lucas.humankind.moreempires.cfg`, criada no primeiro início |
| Medição de turnos | `BepInEx\MoreEmpires\turnos.csv` |
| Testes fora do jogo | `src\MoreEmpires\Tests\` |
| Instalar / desligar / empacotar | `tools\instalar-moreempires.ps1` |

**Compilar sem instalar:**
```
"C:\Program Files\dotnet\dotnet.exe" build _Modding\src\MoreEmpires -c Release
```

**Instalar** (com o jogo fechado):
```
powershell -ExecutionPolicy Bypass -File _Modding\tools\instalar-moreempires.ps1
```
Compila com `-p:Deploy=true`. O alvo de cópia fica desligado por padrão.

**Desligar:**
```
powershell -ExecutionPolicy Bypass -File _Modding\tools\instalar-moreempires.ps1 -Desinstalar
```
Renomeia a DLL para `.desligado`; não apaga nada.

**Rodar os testes fora do jogo:**
```
"C:\Program Files\dotnet\dotnet.exe" run --project _Modding\src\MoreEmpires\Tests -c Release
```

**Despejar as opções direto do bundle do jogo** (útil depois de uma atualização):
```
"C:\Program Files\dotnet\dotnet.exe" run --project _Modding\src\MoreEmpires\Tests -c Release -- opcoes
```

**Ensaio dos patches fora do jogo.** Aplica todos os patches com os assemblies reais, num processo .NET Framework. Rode depois de compilar o plugin:
```
"C:\Program Files\dotnet\dotnet.exe" build _Modding\src\MoreEmpires\Tests\PatchCheck -c Release
_Modding\src\MoreEmpires\Tests\PatchCheck\bin\Release\net472\PatchCheck.exe
```

**Configuração:**

| Seção | Chave | Padrão | O que faz |
|---|---|---|---|
| `[Geral]` | `MaxImperios` | 16 | Faixa de 2 a 16. Com 10, o jogo fica como o original. |
| `[Nascimento]` | `Modo` | `Embaralhar` | Outros valores: `QualquerPonto` e `Original`. |
| `[Interface]` | `CompactarLobby` | `true` | |
| `[Interface]` | `AjustarBanner` | `false` | Experimental. Ligado, derrubou o jogo (ver "Problemas conhecidos"). |
| `[Cores]` | `PaletaExtra` | vazio | Gravada pela tela de cores do jogo. |
| `[Mapa]` | `AjusteAutomatico` | `true` | |
| `[Desempenho]` | `VisibilidadeRapida` | `Auto` | Outros valores: `Sempre` e `Nunca`. |
| `[Desempenho]` | `MedirTurnos` | `true` | |
| `[Dev]` | `CanalDeComandos` | `true` | |

**Comandos de desenvolvimento.** O canal é próprio e não usa o `dev\cmd.txt` do CurrencyMod. Escreva uma linha em `_Modding\dev\moreempires_cmd.txt` e leia a resposta em `_Modding\dev\out\moreempires.txt`.

| Comando | O que mostra |
|---|---|
| `status` | Configuração e patches aplicados. |
| `opcoes` | Constraints originais (Passo 1), mudanças feitas e estado atual das opções. |
| `cores` | As 16 cores e a menor distância ΔE entre elas. |
| `nascimento` | Ordem sorteada dos pontos e os pontos da partida atual. |
| `mapa` | Validação dos spawns de cada geração procedural. |
| `turnos [n]` | Tempos das passagens de turno. |
| `visao` | Compara o fecho transitivo com a recursão original para todos os impérios. |
| `imperios` | Lista os impérios por índice e o estado diplomático dos pares com algum índice ≥ 10 (guerra, aliança, paz, visão, mapas, comércio, luxo, fronteiras abertas). |
| `lobby` | Medidas da lista de jogadores. |
| `ui` | Medidas do banner, do fim de turno, das estatísticas e da diplomacia. |
| `screenshot <nome>` | Captura da tela. |

**Princípios:**
- Todo patch é à prova de falha. Cada classe de patch é aplicada separadamente; se uma falhar, só aquela parte desliga e o motivo vai para o `LogOutput.log`. O corpo de cada patch também captura as próprias exceções.
- Com `MaxImperios = 10`, nada que mude o comportamento é ativado. Continuam ligados só os protetores contra crash, que agem apenas onde o original quebraria, e a medição de turnos.

### O que foi verificado sem o jogo

1. **Compilação:** 0 avisos e 0 erros.

2. **Ensaio dos 29 patches com os assemblies reais do jogo.** Num processo .NET Framework, o Harmony resolve os alvos, confere os nomes dos parâmetros, roda os transpilers e compila os métodos novos.
   - **27 patches aplicados.**
   - **2 falham só nesse ambiente**, e não no Mono do jogo:
     - `LoadPalette` chama `string.Split(char, StringSplitOptions)`, que existe na BCL da Unity mas não no .NET Framework.
     - `LobbyScreen_LobbySlotsPanel.PostLoad` chama `Component.transform`, um InternalCall da Unity que o .NET Framework recusa em método dinâmico.
   - Nos dois casos o transpiler foi rodado direto sobre o IL e gera exatamente a troca pretendida.

3. **Visibilidade.** O fecho transitivo deu resultado bit a bit idêntico ao da recursão do jogo em:
   - 25 859 grafos aleatórios de 2 a 16 impérios, com combinações reais de `DiplomaticAbility`, impérios bloqueados e arestas assimétricas;
   - 1 500 grafos densos de 5 a 10 impérios, com 70 a 100% das ligações.

   Não houve nenhuma diferença.

4. **Cores.** O gerador é determinístico. As 4 cores novas ficam a ΔE ≥ 35,7 de qualquer outra; a menor distância entre as 12 nativas é ΔE 25,5 (azul × roxo).

5. **Dados reais das opções**, lidos do bundle `MercuryDatabases.AvatarPresentation` com o método descrito abaixo.

### Dados reais (bundle do jogo)

**`GameOption_SlotCount`**
- Estados `"2"` a `"10"`, cada um com KV `SlotCount=N` e `NumberOfMajorFactions=N`. Padrão `"6"`. Sem constraints.

**`GameOption_WorldSize`** (padrão `Large`)

| Tamanho | Tiles (`WorldTileCount`) | Jogadores no original | Constraint |
|---|---|---|---|
| Tiny | 2 100 | até 4 | `SE SlotCount ∈ {5..10} ENTÃO Tiny inválido` |
| Small | 3 225 | até 6 | `SE SlotCount ∈ {7..10} ENTÃO Small inválido` |
| Normal | 5 225 | até 10 | nenhum |
| Large | 9 750 | até 10 | nenhum |
| Huge | 13 200 | até 10 | nenhum |

**`GameOption_PercentageOfLandmassOverWater`**
- `SE SlotCount ∈ {4..10} ENTÃO 10% e 20% inválidos`. Liga jogadores à porcentagem de terra, não ao tamanho.
- `SE Tiny ENTÃO 10/20/30% inválidos`.
- `SE Small ENTÃO 10/20% inválidos`.

**`GameOption_NumberOfContinents`**
- `SE Tiny ENTÃO 5..8 continentes inválidos`.
- `SE 100% de terra ENTÃO ...`.

**`GameOption_TerritoryLandSize`**
- Define o `ExpectedRegionArea`: 25 / 30 / **35** (padrão) / 40 / 45.

**Estrelas de era.** `EraStarLevelParameters` tem páginas por `GameOption_SlotCount` com entradas de `"2"` a `"10"`:

| Estrela | Série | Padrão |
|---|---|---|
| Expansionista | linear: 2 a 10 | 5 |
| Diplomata | quadrática: (n−1)² = 1 a 81 | 6 |
| Catch-up | linear | 5 |

Sem a entrada, `DepartmentOfDevelopment.cs:1446-1485` loga um aviso **a cada chamada** e usa o padrão.

**Como foram lidos.** O bundle é UnityFS. O índice de blocos usa LZ4; o bloco de dados estava sem compressão. O parser segue a ordem dos campos de `OptionDefinition`/`GameOptionDefinition`. O código está em `src\MoreEmpires\Tests\BundleTools\`.

### O que mudou, por tarefa

**1. Lobby** — `Features\Lobby.cs`
- `LobbyScreen.AllowedMaxLobbySlots` (`LobbyScreen.cs:224-233`): postfix devolve `MaxImperios` sem mapa customizado.
- `MaxLobbySlots` (linha 44) é um `const` sem uso, já embutido pelo compilador; não há o que trocar.
- `LobbyScreen_LobbySlotsPanel`:
  - transpiler do `PostLoad` troca `ReserveChildren(10)` por `MaxImperios` (linha 87);
  - transpiler do `Refresh` troca os laços `< 10` por `< allLobbySlots.Count` (linhas 127 e 142);
  - prefixos em `Refresh`, `AddSlot`, `RemoveSlot` e `UpdateSlot` garantem linhas suficientes antes de `allLobbySlots[índice]` (linhas 36, 43, 58 e 134).
- `LobbyScreen_SessionPanel` (181, 420, 428, 434) já usa `AllowedMaxLobbySlots`.
- `SessionState_LobbyOwner`: não há laços `< 16` nesta versão. As linhas 45-71 só copiam metadados do save.
- **Layout.** Quando a lista não cabe, ela inteira é reduzida por igual: `UITransform.Scale`, mínimo 0,55. O limite é o fundo do painel, ou a tela − 90 px. O clique respeita a escala (`UITransform.Contains` trata `MatrixFlags.HasScale`), e o botão "adicionar jogador" continua sendo a última linha. O resultado **precisa ser conferido na tela** (comando `lobby`).

**1/2. Opções** — `Features\GameOptions.cs`. Prefixo em `GameOptionsManager.CreateOption`, que roda antes de criar cada `Option` (`OptionsManager.cs:114-121`). Há também um caminho de segurança caso o plugin carregue tarde.
- `GameOption_SlotCount`: acrescenta os estados `"11"` a `"MaxImperios"`, copiando o `"10"`. Se `MaxImperios < 10`, remove os estados acima do máximo.
- `GameOption_WorldSize`: remove **só** os 2 constraints que citam `SlotCount` e registra o máximo original de cada tamanho.
- Constraints `OU SlotCount=k..10` de outras opções (hoje só o da % de terra) ganham as condições `11..MaxImperios`, para continuar valendo "k ou mais jogadores".
- O "Passo 1" vai para o log (`[Opções] Original: ...`) e para o comando `opcoes`.
- Mapas customizados: postfix em `OutgameUtils.Maps.FillMapValidationFailureFlags` (`OutgameUtils.cs:377-379`) passa o limite de `TooManyStartingPoint` para `MaxImperios`. `WorldMapProviderHelper.GetMaxPlayablePlayerCount` (Terrain, linha 604) já conta até 16.
- Editor de mapas: o transpiler estende `EmpireCountOptions` ("1" a "10") até `MaxImperios` em `OnLoad`, `OnDraw` e `FillWithStartingPointCountPerEmpireCount` (`Features\MapEditor.cs`). `GetColorEntry("Empire10..15")` já devolve uma cor derivada do nome.
- Estrelas de era: prefixo em `DepartmentOfDevelopment.FillEraStarThresholdParams` estende as páginas de `SlotCount` até 16, continuando a série (diferenças de 2ª ordem). Resultado: Expansionista 16 = 16; Diplomata 16 = 225. Vai sempre até 16, e não até `MaxImperios`, para um save maior continuar coerente se o valor for reduzido depois.

**3. Geração de mapa** — `Features\WorldGen.cs`
- Postfix em `SelectSpawnRegions.Execute` confere cada império: hex de terra válido, fora de (0,0) e de `HexPos.Invalid`, sem repetir hex nem região. Loga `[Mapa] ...: 16/16 pontos válidos`.
- Se alguém ficou sem ponto, o **reparo é imediato**, então a geração já sai jogável. Quem ficou sem ponto recebe a região de terra livre mais distante dos outros (distância mínima relaxada). Sem região livre, recebe o hex de terra mais distante dentro de uma região já usada.
- Com `[Mapa] AjusteAutomatico` ligado, o postfix em `Generator.WorldGenerator.Generate` ainda gera de novo com a mesma semente, nesta ordem:
  1. `ExpectedRegionArea` × 0,8;
  2. × 0,65;
  3. × 0,65 com `WorldTileCount` × 1,25. Mantém menos de 65 536 tiles; resultados com mais de 255 territórios são recusados.

  O resultado só é trocado se uma tentativa sair **limpa**, ou seja, sem precisar de reparo. Se nenhuma sair, fica a primeira geração, já reparada.
- Quando a primeira geração dá certo, nada muda.
- Observação: `CreateContinents.cs:79/181/185` já reserva terra e regiões para `max(continentes, impérios)` e encolhe o `ExpectedRegionArea` sozinho, então a falha deve ser rara.

**4. Cores** — `Features\Colors.cs` e `Core\ColorMath.cs`
- Com o mod ativo (`MaxImperios` ≠ 10), a paleta tem **sempre 16 cores**, mesmo com `MaxImperios` entre 11 e 15. Motivo: `EmpireNamesRepository.cs:105` indexa a paleta pelo `ColorIndex` salvo, e um save de 16 impérios aberto depois de reduzir `MaxImperios` travaria. As cores que sobram não aparecem para ninguém.
- Todas as `PaletteDefinition` (padrão e as 4 de daltonismo) ganham as cores 13 a 16, geradas contra as 12 da própria paleta. O critério: matizes intercalados, saturação e brilho nos percentis 20/50/80 das nativas, e máximo de ΔE (CIELAB). A secundária e a terciária copiam as diferenças de HSV da nativa de matiz mais próximo.
- `numberOfColorForSlot` passa a 16 desde o construtor de `G2GPlayerProfileManager`.
- `LoadPalette` lê o registro no formato original (12 + 1). Depois o postfix completa as paletas padrão, atual e "última salva".
- `SaveColors`: o transpiler mantém o registro com 12 + 1 cores, então o jogo sem o mod continua lendo a paleta do usuário. As cores 13 a 16 editadas na tela de cores vão para `[Cores] PaletaExtra`.
- Finalizer: se o `LoadPalette` lançar exceção, a paleta volta ao padrão em vez de travar a abertura.
- Com isso deixam de existir os crashes por `ColorIndex` 13 a 15 em `LobbySlot.cs:336`, `LobbySlot_SettingsPanel.cs:254`, na tela de carregamento e no chat. O índice 12 deixa de virar a cor dos povos independentes.
- Cores novas da `Palette_Standard`, calculadas fora do jogo:

  | Cor | Primária | Secundária | Terciária |
  |---|---|---|---|
  | 12 | #4BFB4B | #3EC740 | #3FFF40 |
  | 13 | #FB4BF2 | #CD30C5 | #FF77F9 |
  | 14 | #2E9A2E | #206621 | #31C933 |
  | 15 | #FB92A7 | #92747A | #FFCAD5 |

**5. Personas** — `Features\Personas.cs`
- `Session.FillRandomPersonaForSlot` (`Session.cs:1854-1866`): quando o grupo esvazia, ele é recarregado só com as personas **menos usadas** pelos outros slots. Repetir é permitido, mas por último.
- `TryFillDefaultPersonaForSlot` (`G2GPlayerProfileManager.cs:1365-1367`, array de 10): o índice dá a volta. Hoje não tem chamadores.
- Facção e símbolo repetidos (`Session.cs:1915-1919` e `1952-1956`): o `LogError` vira informação. O teto de 100 povos independentes (`MinorFactionUtils.cs:35-41`), que sempre é atingido com 16 maiores, vira aviso.

**6. Interface na partida** — `Features\UiGuards.cs`. As listas já se dimensionam pelo número de impérios. Ficaram três protetores:
- **"Reserva curta".** `ReserveChildren(N)` conta filhos que não são itens. Para itens por império conhecidos, e só com mais de 10 maiores, a reserva compensa esses filhos.
- **`InternationalEmpireBlipsGroup.Refresh`** (linha 75) usa o rótulo "+N" sem checar `null`; o prefixo evita isso.
- **Banner diplomático:** se os 15 retratos invadirem os painéis do topo, o espaço entre eles diminui. **Desligado por padrão** (`AjustarBanner`): mudar a escala e depois o espaçamento da fileira derrubou o jogo.
- O resto (sobreposições) **precisa de captura de tela**. O comando `ui` mede as janelas.

**8. Desempenho** — `Features\Perf.cs`, `Features\Visibility.cs` e `Core\VisibilityCore.cs`
- **Medição** do clique em fim de turno (`Empire.SetReady`) até a tela mostrar o turno seguinte, com as fases:

  | Fase | Até onde |
  |---|---|
  | Espera da IA | `SandboxState_TurnFinish.Begin` |
  | Processamento | `SandboxState_TurnMain.Begin` |
  | Tela | o turno aparecer |

  Também soma o tempo do `AIController.RunAIDecisionCycle`, sem mudar a ordem dos cérebros, e o de `UpdateVisibilityBits`. Os números vão para `turnos.csv`.
- **`GetVisibilityBits`.** A recursão enumera **todos os caminhos simples** do grafo de acordos com `AnySharedVisionModifier`. Esse grupo inclui comércio, comércio de luxo e posição da capital, não só visão. Custo medido fora do jogo, num bloco em que todos comerciam com todos:

  | Impérios | Chamadas por império | Tempo |
  |---|---|---|
  | 10 | 986 410 | 21-25 ms |
  | 11 | 9,9 milhões | 230-390 ms |
  | 12 | ~1,1×10⁸ | — |
  | 16 | ~3,5×10¹² | — |

  O jogo faz esse cálculo para cada império sempre que um acordo desses muda. Com 16 impérios e muito comércio, o jogo **travaria**. O fecho transitivo leva cerca de 0,001 ms.
- **Fecho equivalente**, com R = raiz e excluindo os impérios em `alreadySharedBits`:
  - SV = alcançáveis por arestas com `ShareVision`;
  - MAP = alcançáveis por arestas com `ShareVision` ou `ShareMaps`;
  - TR = vizinhos por aresta `Trade`/`ExclusiveTrade`;
  - LX = vizinhos por aresta `LuxuryTrade`.

  | Saída | Valor |
  |---|---|
  | visibility = detection | bits(SV) |
  | exploration | bits(MAP) |
  | sharedTrade | bits(SV ∪ TR(SV)) |
  | sharedLuxury | bits(SV ∪ LX(SV)) |
  | explorationOrTrade | bits(MAP ∪ TR(MAP)) |
  | explorationOrLuxury | bits(MAP ∪ LX(MAP)) |

  **Por que dá o mesmo resultado.** Excluir ancestrais no caminho não muda a alcançabilidade. Um vizinho de comércio w excluído num caminho por ser ancestral já está no próprio conjunto alcançável.
- **Uso.** `Auto` liga o fecho só com mais de 10 maiores. As 160 primeiras chamadas de cada sessão comparam com a recursão original (limite de 60 mil chamadas); qualquer diferença desliga o atalho.
- **Números no jogo** (10 × 16 impérios): pendentes. Ver o plano de teste.

**9. Nascimento aleatório** — `Features\Spawns.cs`
- **Causa** confirmada em `WorldGeneratorOutput.cs:393-421`. O lobby importa com `empiresCount` = **máximo do mapa** (`LobbyScreen.cs:259-278` → `OutgameUtils.UseMap`), não com o número de jogadores. Os impérios pegam `SpawnLocations[0..n-1]` na ordem do arquivo (`MajorEmpire.cs:838`, `DepartmentOfDefense.cs:7991/8068`).
- **Postfix** em `ImportSpawnLocationsTable`. Age só dentro de `OutgameUtils.Maps.UseMap(string, int)`, ou seja, no lobby e na partida rápida em mapa customizado. Cenários (`RuntimeState_Staging.cs:248`) não passam por ali.
  - **Embaralhar:** Fisher-Yates com semente nova (`Guid`) a cada import.
  - **QualquerPonto:** todos os pontos do mapa (flags de qualquer número de jogadores), com distância mínima igual à menor distância entre os pontos originais. Faz até 400 tentativas; se nenhuma serve, volta a Embaralhar e registra no log.
- **Saves:** carregar um save **não** reimporta. `World.cs:3072` desserializa `Tables.SpawnLocations`.
- **Leitores por índice** antes das cidades existirem (`MinorFactionManager.cs:994-998`, `World.cs:909-916`) só usam distância e continente, então continuam corretos.
- **Mapas procedurais:** a ordem de escolha já é aleatória. `SelectSpawnRegions.cs:137-148` monta uma fila embaralhada por `Context.Randomizer`, e quem pega a melhor região varia. Não há embaralhamento extra.
- **Multiplayer:** fora do escopo. Embaralhar exigiria uma semente sincronizada entre os jogadores, então em sessão online o mod mantém a ordem original.

### Ajustes de mapa por tamanho

O log registra cada tentativa (`[Mapa] Tentativa N: área de região X, Y tiles, Z territórios → ...`), e o comando `mapa` mostra o histórico.

| Tamanho | Máx. original | Ajuste necessário com 16 | Resultado no jogo (2026-10-04) |
|---|---|---|---|
| Tiny | 4 | sim | 2ª tentativa com regiões menores: 16/16 pontos válidos |
| Small | 6 | provável | não testado |
| Normal | 10 | não | 16 impérios gerados; 3 ainda nômades no turno 64 (pouco espaço) |
| Large | 10 | provável que não | não testado |
| Huge | 10 | não | 16/16 na 1ª tentativa |

### Testes no jogo (2026-10-04)

As partidas foram criadas com o comando `jogo novo <jogadores> <tamanho>` do CurrencyMod. Ele usa o início rápido com
as opções do lobby e, com `jogo espectador on`, a IA nativa joga pelo império local.

- **Normal, 16 impérios:** 64 turnos sem exceções.
  - Auditoria da economia sem valores inválidos.
  - 3 impérios ainda eram tribos no turno 64.
- **Tiny, 16 impérios:** o ajuste automático do mapa funcionou na 2ª tentativa (16/16 pontos válidos). Rodou 82 turnos.
- **Huge, 16 impérios:** 16/16 pontos válidos na 1ª tentativa. 76 turnos sem exceções.
- **Atalho de visibilidade:** as 160 conferências com a recursão original deram resultados idênticos.
- **Compatibilidade com o CurrencyMod:** a economia (moedas, juros, câmbio e a auditoria `jogo economia`) rodou com
  16 impérios. As outras mudanças da tarefa 7 não foram exercitadas em jogo: chaves `*256`, rolagem do Banco Central
  e correio limitado a 60 cartas.
- **Não medido:** o tempo dos turnos. A medição começa no clique de fim de turno (`Empire.SetReady`), e no modo
  espectador ninguém clica, por isso o `turnos.csv` não foi criado. Medir numa partida jogada pelo jogador.
- **Não testado:**
  - o lobby com 16 linhas (o início rápido pula o lobby);
  - mapas Small e Large;
  - o nascimento embaralhado em mapa customizado;
  - a sessão B (`MaxImperios = 10`).

### Problemas conhecidos

- **Banner diplomático com 16 impérios:** o 15º e o 16º retratos ficam parcialmente sob o painel de recursos, à
  direita.
  - **Tentativas de ajuste:** mudar a escala da fileira e depois o espaçamento entre os retratos (`UITable1D.spacing`
    + `ArrangeChildren`). As duas derrubaram o jogo com crash nativo, sem exceção no log, ao carregar um save de
    16 impérios.
  - **Prova:** com `AjustarBanner = false`, o mesmo save carrega normalmente.
  - **Solução futura:** uma rolagem nativa ou uma segunda fileira, testadas com cuidado.
- **Steam depois de fechar o jogo:** depois de qualquer saída, mesmo pelo menu, a Steam pode acusar `AppError_16`
  ("aplicativo já aberto"). Reabra com `steam.exe -shutdown`, depois `steam.exe -silent`, depois
  `steam://rungameid/1124300`.
- **Corrigidos na revisão de 2026-10-05** (instalados; uma partida nova de 16 impérios Tiny abriu normal, mas o jogo
  tem personas para os 16 e a reciclagem, onde entra o filtro, não chegou a rodar):
  - a reciclagem de personas não usava o filtro de resumo inválido do jogo (`IsSummaryValid`, como em
    `Session.FillListsOfAvailablePersonae`);
  - quem saía da partida no meio do fim de turno deixava a medição aberta, e o próximo save gravava um "turno" com os
    minutos de menu dentro (`turnos.csv`).

### Pendências e riscos conhecidos

**Teste no jogo.** Faltam o lobby com 16 linhas, os mapas Small e Large, o nascimento em mapa customizado, a medição
de tempo de turno e a sessão B.

**Layout:**
- Lista do lobby, banner, tela de diplomacia (portraits de relações), estatísticas de fim de jogo e tela internacional.
- A compactação por escala é a solução cega mais segura; pode precisar virar rolagem nativa se ficar pequena demais.
- A grade de cores das Opções passa de 13 para 17 itens; conferir.

**Personas e símbolos.** Quantas personas utilizáveis existem nesta conta (se acabarem antes de 16, a reciclagem entra) e se há pelo menos 16 símbolos livres.

**Visibilidade.** Num save antigo com acordos densos, as conferências da primeira atualização podem levar alguns ms cada; foram limitadas a 160 chamadas por sessão.

**Fora do escopo:**
- Mais de 16 impérios (máscaras `ushort`).
- Multiplayer.

### Plano de teste no jogo

Ao todo são duas sessões do jogo: **A**, com `MaxImperios = 16`, e **B**, com `MaxImperios = 10`. Cada troca de configuração exige reiniciar o jogo.

**Comandos.** Escreva o comando em `_Modding\dev\moreempires_cmd.txt` e leia a resposta em `_Modding\dev\out\moreempires.txt`. As capturas vão para `_Modding\dev\out\<nome>.png`.

#### Preparação (≈ 5 min)
1. Com o jogo fechado, instale:
   ```
   powershell -ExecutionPolicy Bypass -File _Modding\tools\instalar-moreempires.ps1
   ```
2. Abra o jogo e, no menu, confira o `BepInEx\LogOutput.log`:
   - `MoreEmpires 1.0.0: MaxImperios = 16 (ativo), cores = 16 ...`
   - `Patches aplicados: 29 ok, 0 com falha`
   - linhas `[Transpiler]`: PostLoad 1, Refresh 2, SaveColors, facção, símbolo, teto de menores, editor ×3
   - `Paleta 'Palette_Standard': 12 → 16 cores`, e também as de daltonismo
   - `Cores: paleta em uso com 16 cores`
   - `[Opções] Original: ...`
3. Rode `status` e `opcoes`. Em "Mudanças feitas" devem aparecer:
   - os estados 11..16;
   - os 2 constraints de `WorldSize` removidos;
   - a condição da % de terra estendida até 16;
   - "Tiny: 4, Small: 6".

#### Sessão A — Teste 1: lobby (≈ 15 min)
1. Novo jogo. Suba até 16 jogadores, pelo "+" e também digitando 16. O "+" deve desligar em 16.
2. Na aba Mundo, passe por Tiny, Small, Normal, Large, Huge e Aleatório. Nenhum tamanho pode ficar inválido; o `opcoes` mostra `Default` em todos.
3. Confira as 16 linhas e o botão "adicionar", sem nada cortado ou sobreposto. Rode `screenshot lobby16` e `lobby` (escala aplicada e limites).
4. Abra o painel de uma IA: a aba Cores deve ter 16 cores; confira também a aba Persona. Use "Sortear todos".
5. Desça para 4 jogadores e volte para 16.
6. Confira também a grade de cores em Opções > Cores dos impérios: são 17 itens.

#### Sessão A — Teste 2: 16 impérios em cada tamanho (≈ 35-45 min, 5 × 7-9 min)
Para cada tamanho, com continentes e terra no padrão:
1. Inicie a partida.
2. Confira o log (`[Mapa] ...: 16/16 pontos válidos, 0 repetidos` ou as linhas `Tentativa N`) e rode `mapa`.
3. Rode `nascimento`: devem aparecer 16 posições distintas.
4. Rode `cores` e `imperios`.
5. Rode `screenshot banner_<tamanho>` e `ui`.
6. Procure "Exception" no log desde o início da partida.
7. Volte ao menu.

Ao final, preencha a tabela "Ajustes de mapa por tamanho".

#### Sessão A — Teste 6: nascimento aleatório (≈ 20 min)
1. Escolha um mapa customizado com pontos iniciais (aba Mapas).
2. Faça 5 partidas novas seguidas. Em cada uma, anote a linha `império 0 (jogador) em [c, r]` do comando `nascimento` e volte ao menu. Precisa dar pelo menos 3 posições diferentes.
3. Na última partida, salve, carregue e rode `nascimento`. A linha "Partida atual" deve mostrar os mesmos pontos.

#### Sessão A — Testes 3 e 4: 50 turnos em mapa Normal com 16 impérios (≈ 60-120 min)
1. Jogue ou passe 50 turnos. Cada turno gera uma linha `[Turno] t→t+1: X s ...` no log.
2. Provoque, com impérios de índice 10 a 15 (o `imperios` mostra índices e estados): guerra, aliança, fronteiras fechadas (sem "fronteiras abertas"), comércio e visão compartilhada.
3. No fim, rode `turnos`, `visao` e `imperios`.
4. Salve, carregue e confirme que não há exceções novas no log.

#### Sessão B (≈ 45-60 min)
1. Com o jogo fechado, mude o `.cfg` para `MaxImperios = 10` e `[Nascimento] Modo = Original`, e abra o jogo.
2. O log deve dizer "10 = comportamento original" e "cores = 12".
3. No lobby: máximo de 10; Tiny aceita até 4 e Small até 6; o `opcoes` mostra "Mudanças feitas" vazio.
4. No mesmo mapa customizado, o ponto de nascimento deve ser sempre igual.
5. Base de desempenho: uma partida Normal com 10 impérios por 30 turnos. Depois rode `turnos`, que agrupa os números por quantidade de impérios.
6. Ao terminar, volte o `.cfg` para 16 e `Embaralhar`.
