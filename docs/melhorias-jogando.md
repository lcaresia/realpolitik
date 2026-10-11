# Melhorias anotadas jogando

Sessões de jogo em que o Lucas aponta o que melhorar. Aqui só anotamos, identificamos e documentamos: nada é implementado sem conversar antes.

Formato de cada item:
- **O que aconteceu**: o que o jogador viu/sentiu.
- **Causa provável**: onde está no código/sistema (quando der para identificar).
- **Proposta**: ideia de correção ou melhoria.
- **Status**: aberto / discutido / decidido / feito.

---

## Plano: grupos por lugar, em ordem de dificuldade (sessão 2026-10-10)

Dificuldade: 🟢 fácil (horas, um arquivo) · 🟡 média (um dia, alguns arquivos, um dado novo) · 🔴 difícil (pesquisa no jogo, patch novo ou sistema novo) · ⚫ muito difícil (feature nova inteira, precisa de sessão de design).

Os grupos vão do mais fácil ao mais difícil. Dentro de cada grupo, os itens também estão do mais fácil ao mais difícil. Os números são os dos itens abaixo.

### Grupo A: Correio e cartas (UI) · 🟢→🟡
Arquivos: `UI/MailScreen.cs`, `UI/CorrespondenceTab.cs`, `IaModule.cs` (envio).

| # | Item | Dif. | Observação |
|---|------|------|------------|
| 6 | Aba "Cartas" da nação: mais recentes no topo | 🟢 | Inverter a ordem e rolar para o topo, num lugar só. |
| 14 | Nome do líder junto com a nação | 🟢 | Título "Povo · Líder"; chip do estado separado. |
| 9 | Contador/limite por caracteres (só cartas do jogador) | 🟢 | Config + 2 contadores + traduções. |
| 18 | BUG: texto colado fica invisível | 🟡 | Primeiro reproduzir (tamanho?). Normalizar `\r\n` de qualquer jeito. |
| 13 | Comunicados públicos: dourado + aviso quando citam o jogador | 🟡 | Detecção por nomes (líder, povos de todas as eras, cidades). |
| 12 | BUG: "Novas" perde cartas na virada de turno | 🟡 | Reproduzir pelo canal de dev; marcar como lida só quando visível. |

Fazer tudo junto num único teste do Correio: os itens 6, 14 e 9 são rápidos; os itens 12 e 18 exigem passar turnos pelo canal de dev.

### Grupo B: Conselho de ministros · 🟢→🔴
Arquivos: `Council/PlayerCouncil.cs`, `Council/CouncilEngine.cs`, `Council/CouncilBank.cs`, `UI/CouncilScreen.cs`, `UI/CouncilButton.cs`, `IaConfig.cs`.

| # | Item | Dif. | Observação |
|---|------|------|------------|
| 4 | Caixa de resumo no topo da reunião | 🟢 | Monta com as linhas "Conselho:" que já vêm, sem custo de API. |
| 5 | Ministro inventa mecânica | 🟢/🟡 | Prompt: lista fechada de acordos + regra anti-invenção (🟢). Passar as falas pelo `Grounding` (🟡, compartilhado com o Grupo C). |
| 1 | Reunião só a cada 7 turnos ou por audiência | 🟡 | Novo gatilho em `Tick`, botão "Convocar", config de intervalo. **Maior economia de custo da lista.** |
| 3 (+2) | Escolher ministros entre candidatos | 🔴 | Muda o modelo: começa só com a Mão, vagas abertas, tela de nomeação com 3 candidatos, demissão reabre a vaga. Resolve o item 2. |

Ordem sugerida: 1 → 4 → 5 primeiro (custo e qualidade); 3 depois, com design de tela.

### Grupo C: Veracidade das IAs (o que elas dizem bate com o jogo) · 🟡
Arquivos: `Prompts.cs`, `Grounding.cs`, `DossierBuilder.cs`, `DecisionParser.cs`, `UI/MailScreen.cs` (exibição).

| # | Item | Dif. | Observação |
|---|------|------|------------|
| 7 | Nação oferece recurso que não tem / "porto" inventado | 🟡 | Dossiê: cidades costeiras e com porto. Prompt: "só ofereça o que está em Recursos:". Grounding: verbo de oferta + recurso que a nação não tem → reescrever. |
| 11 | "O que isso significa" nas cartas em metáfora | 🟡 | Chip determinístico com o contexto do remetente no turno (cooldown de declaração, acordos, presentes). |

Faz sentido fazer o item 5 (Grupo B) junto com este grupo, porque usa o mesmo `Grounding`.

### Grupo D: Banco Central · 🟡
Arquivos: `NativeUI/NativeBankWindow.cs`, `NativeUI/BankChart.cs`, `CurrencyState.cs`.

| # | Item | Dif. | Observação |
|---|------|------|------------|
| 10 | Aba "Recursos": situação atual + gráfico no tempo | 🟡 | O gráfico e o hover já existem. Falta gravar o histórico por recurso a cada turno e montar a 6ª aba. |

### Grupo E: Rotas comerciais e pedágio · 🔴
Arquivos: `Trade\*` (`TradePolicy`, `TradeBlockade`, `TollPreview`), novo patch em `PresentationTradeRoadController`, `Prompts.cs`/`DecisionParser.cs` (ação nova).

| # | Item | Dif. | Observação |
|---|------|------|------------|
| 17 | A IA escolhe pagar o pedágio ou desviar | 🟡/🔴 | Ação nova `rota_pedagio` + custo no pathfinding por escolha. Reaproveita o `TollPreview`. |
| 8 | Cor/espessura por rota + rótulo "£/t" | 🔴 | Postfix no desenho das rotas (cor e largura). O rótulo exige criar pinos no mapa, coisa que o mod nunca fez, então precisa de pesquisa. Começar pela cor e deixar o rótulo para depois. |

### Grupo F: Diplomacia multilateral · ⚫
Arquivos: modelo novo em `IaWorld.cs`, Correio, dossiê, prompt, ações novas; liga com o Grupo E (bônus de rota) e o Congresso.

| # | Item | Dif. | Observação |
|---|------|------|------------|
| 15 | Conversas em grupo | 🔴 | Carta com destino = grupo, nova seção no Correio, resumo no dossiê. Não aumenta chamadas. |
| 16 | Blocos de nações (OTAN/BRICS) com bônus | ⚫ | Sistema novo: fundar/entrar/sair, bônus de comércio, militar e Congresso. **Precisa de sessão de design antes.** O item 15 é o canal dele. |

Contexto do jogo atual: o Lucas já anunciou em público uma "aliança de seis" (turno 75), e as IAs reagiram. É um bom caso de teste para os blocos.

### Ordem geral sugerida
1. **Grupo A** (fáceis + 2 bugs visíveis) e **itens 1, 4 e 5 do Grupo B** (custo e qualidade do conselho).
2. **Grupo C** (veracidade) junto com o restante do item 5.
3. **Grupo D** (aba Recursos).
4. **Grupo E**: item 17, depois a cor do item 8, depois o rótulo.
5. **Item 3** (escolher ministros) e **Grupo F**, com sessão de design.

---

## Sessão 2026-10-10

Caminhos relativos a `src\CurrencyMod\Diplomacia\`.

### 1. Conselho se reúne todo turno (custo alto)
- **O que aconteceu**: tem reunião do conselho em todo turno, o que gasta muita API.
- **Causa**: `PlayerCouncil.Tick` (`Council/PlayerCouncil.cs:130-169`) abre uma reunião por turno, com 1 chamada agrupada para todos os ministros, até 3 novas tentativas se der erro e mais uma chamada se o JSON vier quebrado. Não existe intervalo configurável nem botão de convocar. O único "Convocar de novo" (`Retry`, `:310`) só aparece quando dá erro. Config em `IaConfig.cs:128-135`.
- **Proposta (Lucas)**: duas regras.
  1. **Audiência**: o líder convoca quando quiser, por um botão na tela do conselho.
  2. **Automática a cada 7 turnos.**
  - Deixar o intervalo como config (`ConselhoIntervalo = 7`) e zerar a contagem quando o líder convoca.
- **Custo**: cai de ~1 chamada/turno para ~1/7 turnos mais as audiências, uma redução de ~85% no conselho.
- **Decidido (Lucas)**: emergência (guerra declarada contra você, ultimato vencendo) **não** convoca o conselho. Fora o ciclo de 7 turnos, só reúne se o líder pedir. Não há exceções.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): reunião automática a cada `ConselhoIntervaloTurnos` (7) e audiência pelo botão "Convocar" (`PlayerCouncil.Convene`, `NextAutoTurn`); a tela mostra a última reunião e "O conselho está dispensado".

### 2. Ministros demais no começo, sem vínculo com o jogo
- **O que aconteceu**: já no início aparece um monte de ministros, inclusive de pastas que ainda não fazem sentido.
- **Causa**: são 12 cadeiras fixas desde o turno 1 (`CouncilBank.Portfolios`, `Council/CouncilBank.cs:29-32`), preenchidas por `CouncilEngine.Ensure` (`Council/CouncilEngine.cs:55-70`). Nada é desbloqueado com o tempo, só o título muda por era. Os números de cada pasta são agregados (crescimento total, produção total, força militar). Não há ligação com terrenos, unidades ou distritos.
- **Proposta (Lucas)**: a pasta só existe quando há algo real por trás dela:
  - agricultura: precisa de distrito/terreno de comida;
  - guerra: precisa de unidade militar;
  - indústria: precisa de distrito de indústria;
  - e assim por diante.
- **Relação com o item 3**: o Lucas disse que, se o item 3 for feito, este aqui não precisa.
- **Status**: feito com o item 3 (o conselho começa só com a Mão; as pastas são preenchidas por nomeação).

### 3. Escolher os ministros
- **O que aconteceu**: hoje o jogo escolhe os ministros sozinho, por um hash fixo sobre um banco de 73 personalidades (`CouncilEngine.Hire`, `:72-112`). O jogador não escolhe ninguém.
- **Proposta (Lucas)**: o jogo gera X candidatos e o jogador nomeia quem quiser, na hora que quiser. Isso já resolve o item 2, porque o conselho cresce no ritmo do jogador.
- **Ideias para discutir**:
  - começar só com a Mão (o chefe do conselho);
  - um botão "Nomear ministro" com 3 candidatos por pasta, mostrando traços e voz (os campos já existem no banco);
  - a demissão (`CouncilEngine.Fire`) passa a reabrir a vaga em vez de contratar um substituto automaticamente.
  - Bônus de custo: menos ministros significa um prompt menor.
- **Status**: feito em 2026-10-10 (testado no Teste16): ver "Item 3 implementado" no fim.

### 4. Caixa de resumo no topo da reunião
- **O que aconteceu**: para saber o que o conselho acha, é preciso ler todos os cartões.
- **Causa**: `UI/CouncilScreen.cs:603-637` mostra só a lista de cartões. A fala da Mão é o primeiro cartão, mas não é um resumo de todos.
- **Proposta**: uma caixa fixa no topo com uma linha por ministro (pasta: conselho). Ela pode ser montada a partir das linhas "Conselho:" que já vêm na resposta, **sem chamada extra à API**. A tela "Reuniões anteriores" (`:803-817`) já faz algo parecido.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): cartão "Resumo do conselho" no topo (`CouncilScreen.FillSummary`), montado das linhas "Conselho:".

### 5. Ministro inventa mecânica que não existe
- **O que aconteceu**: a Mestra dos Ritos Isolde Brandt aconselhou fazer um acordo "apenas com reciprocidade plena de escribas e costumes". Isso não existe no jogo.
- **Causa**:
  - O conselho usa um prompt de sistema próprio (`PlayerCouncil.SystemPrompt`, `:744-770`). Ele diz "propor um acordo", mas **não traz a lista fechada de acordos válidos** nem a regra "não invente escribas, tarifas, tribunais…".
  - Essas duas coisas só existem no prompt das nações (`Prompts.cs:42-44`, `:63-64`).
  - O `Grounding.cs`, que barra palavras inventadas, só confere as cartas das nações (`DecisionParser.cs:311-348`), nunca a saída do conselho. `ApplyMeeting` (`:531-570`) só confere o formato.
  - As palavras "escriba" e "ritos" vêm dos próprios títulos da Era Antiga ("Escriba-Mor", "Mestre(a) dos Ritos"). Já "reciprocidade" e "costumes" foram inventadas.
- **Proposta**:
  1. Pôr no prompt do conselho a lista fechada de ações e acordos reais (econômico, informação, cultural, militar, aliança, paz, guerra, produção…) e a regra anti-invenção.
  2. Passar `texto` e `conselho` pelo `Grounding` (`NotInGameWords`/`UnknownNames`). Se reprovar, descartar ou trocar só aquela fala.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): prompt do conselho com a lista fechada de ações e a regra anti-invenção; `PlayerCouncil.Ground` tira as frases com mecanismo inventado (lista `CouncilInvented` + `Grounding.NotInGame`) e registra no log.


### 6. Aba "Cartas" da nação: mais recentes no topo
- **O que aconteceu**: na tela da nação, aba "Cartas", a carta mais nova fica no fim da lista. Para ler o que acabou de chegar, é preciso descer até o fundo.
- **Causa**:
  - `UI/CorrespondenceTab.cs:671` ordena em ordem crescente (`OrderBy` por turno e depois por Id), como num chat, e rola até o fim (`ResetVertically(toEnd: true)`, `:681-684`).
  - O corte de `MaxShown` (`:673-676`) mantém as últimas.
  - A tela do Correio Diplomático (`UI/MailScreen.cs:878-879`) já faz o contrário (`OrderByDescending`), então as duas telas estão inconsistentes.
- **Proposta**:
  - Ordenar em ordem decrescente (`OrderByDescending(...).ThenByDescending(l => l.Id)`), cortar com `Take(MaxShown)` e rolar para o topo (`toEnd: false`).
  - Ajustar `shownLastId` para usar `letters[0]`.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): aba Cartas em ordem decrescente, rolando para o topo.

### ~~Botão "Lida" nas cartas~~
- **Descartado (Lucas)**: não precisa.

### 7. Carta dos Micênicos promete "abrir o porto de Mykênê"
- **O que aconteceu**: Māui (Micênicos, E6) escreveu no turno 30, e a carta chegou no turno 32: "Proponho acordo econômico… abro o porto de Mykênê às vossas caravanas". Ficou a dúvida: esse recurso existe?
- **Checagem** (log `BepInEx\DiplomaciaIA\logs\2f44534a\T030_E6.json`):
  - **Mykênê existe.** É a capital deles (C8, pop. 3, território T13, anexo Heze).
  - **O acordo econômico existe.** É `propor_acordo economico`, uma ação real.
  - **"Porto" não tem base.** O dossiê não diz se Mykênê é costeira nem lista distritos ou cais, e não existe um "abrir porto" separado. É floreio narrativo sobre o acordo econômico. O "mar" deles também é inventado, porque o dossiê não fala de costa.
  - **Promessa falsa de recurso.** Numa proclamação pública da mesma época, Māui oferece "ânforas, cobre", mas o dossiê diz: "você não tem acesso a nenhum recurso estratégico ou de luxo". Cobre é um recurso real do jogo e por isso passa no `Grounding`, que só confere se a palavra existe, não se a nação tem o recurso.
- **Causa**: o `Grounding` aceita qualquer nome de recurso do mapa (`Grounding.cs:66`). Nada confere "ofereço X" contra os recursos de quem escreve. O dossiê também não informa costa/cais, então o modelo preenche a lacuna.
- **Proposta**:
  1. No dossiê da própria nação, dizer quais cidades são costeiras e têm cais/porto. Se a cidade não tiver, aparece "sem porto".
  2. No prompt, a regra: "só ofereça recursos que estão em 'Recursos:' da sua nação".
  3. No `Grounding`, se a carta cita um recurso que a nação não tem perto de um verbo de oferta (ofereço/abro/dou/envio), mandar reescrever. Sem o verbo, a citação pode ser legítima, por exemplo pedir o cobre do outro.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): dossiê marca "tem porto/sem porto" (`CapturedCity.HasHarbour`); prompt "só ofereça o que está em Recursos:"; `Grounding.OfferedNotOwned` devolve a carta que oferece recurso que a nação não acessa ("ouro" fica fora: é dinheiro nas cartas).

### 8. Rotas comerciais todas brancas: cor/peso por rota
- **O que aconteceu**: na visão de comércio, todas as rotas aparecem como linhas brancas iguais. Não dá para ver quais valem mais nem de quem são, o que dificulta decidir onde taxar ou bloquear.
- **Causa** (jogo nativo, nada do mod):
  - `PresentationTradeRoadController.TradePathSnapshot_PresentationChanged` (`decompiled\Assembly-CSharp\Amplitude.Mercury.Presentation\PresentationTradeRoadController.cs:145-198`) desenha cada rota com `Color.clear`, então a cor vem do estilo.
  - Só existem 3 estilos em uso: normal, interrompida e destruída (`TradeRoadSettings.cs:21-29`).
  - As linhas são desenhadas na GPU (`WorldSpaceLinesWithSteps`), sem tooltip nem legenda.
- **Dados disponíveis por rota**:
  - Nativos (`ExternalTradeExchange`): os dois impérios, recursos comprados (`BoughtResource..AccessCount`), `NumberOfDifferentResourceTraded` e `TradeUpkeep` (custo em dinheiro).
  - Do mod: `TradePolicy.RoutesThrough(território)` (quem compra o quê) e `TollRecord`/`GetPrice` (pedágio pago).
- **Viabilidade**: alta. Um postfix nesse método reconstrói o mapeamento linha → relação (mesma ordem de `TradePathSnapshot.ComputePathRendering`, `:277-367`), troca `Lines[i].Color`/`GlowColor`, ajusta `Steps[j].WidthFactor` (só afina, de 0 a 1) e chama `Apply()` de novo.
  - Se o estilo usa cor fixa (`UseCustomColor`, ainda a confirmar em execução), é preciso clonar os estilos com `UpdateStyles` ou criar estilos extras, por exemplo por faixa de valor e com largura maior.
- **Ideias de codificação**:
  - **Espessura** = volume (número de recursos/acessos que passam).
  - **Cor** = império dono da rota (cor do império no jogo) ou estado em relação a mim (livre / paga meu pedágio / desvia / bloqueada).
  - Opcional: um rótulo "N rotas · £X/turno" no pino do posto comercial (`TradeRoadDistrictNodePin.cs`), porque a linha não tem tooltip.
- **Pedido extra (Lucas)**: um **valor escrito ao lado da rota**, no mapa.
  - **Viabilidade**: a linha não tem rótulo próprio, mas o jogo tem um sistema nativo de pinos presos ao mapa (`decompiled\Assembly-CSharp\Amplitude.Mercury.UI\*Pin*.cs`, `*PinsSubset.cs`). Um pino por rota, no ponto do meio do caminho, com um texto curto, mantém o visual nativo. O mod ainda não cria pinos, então isso é novo e precisa de pesquisa.
  - **Decidido (Lucas)**: o rótulo mostra **£ por turno que a rota vale para você**. Se já paga pedágio, "£12/t" em dourado. Se ainda não paga, "£12/t?", que é quanto pagaria no preço atual. Rota que não passa pelas suas terras fica sem rótulo.
- **Recomendação de cor** (sem objeção até agora): espessura = volume; cor = situação em relação a mim (branca livre, dourada paga pedágio, laranja desviou, vermelha bloqueada). A cor por império foi descartada porque com 16 jogadores fica ilegível.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): cores e espessuras por estilo próprio do mod (`Trade\RouteVisuals.cs`, config `CoresDasRotas`) e rótulo "£ N/t" (`Trade\RouteLabels.cs`, IMGUI na visão de comércio). Se o rótulo destoar do visual nativo, o passo seguinte é trocar por pinos nativos.

### 9. Contar o tamanho das cartas por caracteres, não por palavras
- **O que aconteceu**: o limite e o contador das cartas usam palavras. O Lucas quer contar por caracteres.
- **Onde está hoje**:
  - **Jogador**:
    - limite `PalavrasCartaJogador = 600` (`IaConfig.cs:119`), conferido em `IaModule.cs:1714`;
    - contador "N/600 palavras" na aba Cartas (`UI/CorrespondenceTab.cs:821-825`) e no Correio (`UI/MailScreen.cs:1309`);
    - dicas "Até N palavras" (`CorrespondenceTab.cs:527`, `MailScreen.cs:499`).
  - **IA**:
    - `PalavrasPorCarta = 150` (`IaConfig.cs:117`);
    - o prompt pede de 30 a ~100 palavras (`Prompts.cs:18-19, 53`);
    - o parser devolve a carta acima de 1,25× (`DecisionParser.cs:295-304`).
  - **Diário**: `PalavrasDiario = 60`. **Conselho**: "até 60 palavras" (`PlayerCouncil.cs:776, 791`).
- **Proposta**:
  - Trocar as chaves por caracteres, por exemplo `CaracteresCartaJogador` (~3600 ≈ 600 palavras em PT) e `CaracteresPorCarta` (~900 ≈ 150 palavras).
  - O contador passa a mostrar "N/3600 caracteres".
  - Traduzir os textos novos nos 4 idiomas (`Lang\*.json`).
  - Chaves antigas do .cfg: migrar convertendo ×6.
- **Decidido por padrão**: a contagem por caracteres vale **só para as cartas do jogador** (contador e limite). A IA, o diário e o conselho continuam em palavras, porque o modelo conta mal caracteres e cada carta devolvida é uma chamada a mais.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): `CaracteresCartaJogador = 2000` (teto do campo nativo, ver item 18); contadores e dicas em caracteres; texto limpo no envio (`IaModule.CleanPlayerText`).

### 10. Banco Central: aba "Recursos" com situação atual e gráfico no tempo
- **O que aconteceu**: o Lucas quer, no Banco Central, uma aba que mostre os recursos que o império tem agora e um gráfico de como isso mudou ao longo dos turnos.
- **Onde está hoje**:
  - A janela nativa tem 5 abas: Câmbio, Comércio, Política, Ciclo, Sua moeda (`NativeUI/NativeBankWindow.cs:29, 428`).
  - Já existe a infraestrutura de gráfico com hover (`NativeUI/BankChart.cs`, `NativeBankChartHover.cs`), usada em Câmbio e Comércio.
  - O histórico por império guarda só câmbio, inflação e juros (`HistoryPoint`, `CurrencyState.cs:9-15`, até 60 turnos), mais o histórico de trocas (`TradeHistory`, com `Goods` = quantidade comprada).
  - **Recursos por turno não são gravados**, então o gráfico precisa de um histórico novo.
- **Interpretação adotada**: "recursos" no sentido do jogo, ou seja, os **estratégicos e de luxo** (cobre, ferro, seda…), que já são o vocabulário do mod no dossiê e no comércio. Rendimentos (comida, indústria, ciência) ficam fora; se for isso, é outra aba.
- **Proposta**:
  - Nova aba **"Recursos"** (6ª):
    1. **Cartão atual**: um por recurso que o império acessa, com ícone nativo, quantidade de acessos e a origem (jazida própria / comprado por rota de X / vendido para Y). Recurso com zero acesso mas visível no mapa aparece apagado.
    2. **Gráfico no tempo**: uma linha por recurso (ou total estratégico × luxo), com hover por turno igual ao do Câmbio.
  - **Gravação**: no fim de turno, junto com o `HistoryPoint`, guardar acessos por recurso (dicionário pequeno), com o mesmo limite de 60 turnos. Save antigo começa vazio.
  - Fonte dos dados: acessos do império no sandbox. As compras por rota já existem em `ExternalTradeExchange.BoughtResource..AccessCount` (ver item 8).
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): aba "Recursos" (6ª) no Banco Central (`NativeUI\NativeBankResources.cs`); histórico gravado no fim do turno (`HistoryPoint.Resources`, só humanos).

### 11. Cartas falam de regras do mod em metáfora, e o jogador não entende
- **O que aconteceu**: carta dos Assírios (Miyamoto Musashi, E3) escrita no turno 48 e chegada no turno 50: "No turno 46 gritei em praça… A praça hoje está fechada a gritos meus; quando reabrir, o repito diante do mundo". O Lucas perguntou o que isso significa de verdade.
- **Tradução** (log `T048_E3.json`):
  - "Gritei em praça no turno 46" é real: a declaração pública "A palavra de Aššur não é de barro", em que ele se põe ao lado dos Chou contra Shaka (Carais).
  - "Praça fechada" é a regra do mod `TurnosEntreDeclaracoes = 5` (`IaConfig.cs:123`). O dossiê dele dizia "Declaração pública: só a partir do turno 51" (`DossierBuilder.cs:1290-1292`). Ele **promete repetir a declaração citando o Lucas no turno 51 ou depois**. É uma promessa sem garantia, porque fica na memória dele e ele pode ou não cumprir.
  - "Minha estrada, meu ouro e minha palavra são teus" é retórica de aliado. Já existe ALIANÇA (comércio, fronteiras abertas, mapas, não agressão). Nenhum presente foi dado. Ouro só chegaria se ele usasse `presentear`, e ele mesmo anota "sem mina nem sal".
- **Melhoria proposta**:
  - Quando a carta cita uma regra/mecânica em metáfora, a tela mostra uma linha "O que isso significa", sem custo de API.
  - Opção A, determinística: detectar o contexto do remetente no turno em que escreveu (ex.: declaração pública bloqueada até o turno X, ação executada nesse turno) e mostrar um chip "Declaração pública dele: liberada no turno 51".
  - Opção B: pedir ao modelo, no mesmo JSON da carta, um campo curto `"nota_jogador"` em linguagem simples. Custa poucos tokens.
  - Recomendação: **A** para regras conhecidas (cooldown de declaração, acordos propostos/aceitos no turno, presentes). É barata e não inventa.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): bloco "O que isso significa no jogo:" no fim da carta (`LetterContext.For`): ações do remetente com você no turno da carta e o prazo da declaração pública.

### 12. BUG: "Novas" selecionada + passar o turno → cartas novas somem
- **O que aconteceu**:
  1. Correio aberto (ou deixado) na seção "Novas".
  2. O Lucas passa o turno.
  3. As cartas que chegaram não aparecem na lista.
  4. Ele clica em "Lidas" e volta para "Novas", que agora fica vazia.
  - Ou seja, as cartas foram **marcadas como lidas sem o jogador ver**.
- **Como funciona hoje** (`UI/MailScreen.cs`):
  - "Novas" lista o conjunto `freshShown`. A cada atualização, que roda a cada 0,5 s enquanto `Shown`, toda carta não lida entra nesse conjunto (`:844-850`).
  - O próprio `FillLetters` já marca como lido tudo o que listou (`MarkReadByPlayer`, `:914`). A carta continua em "Novas" só enquanto estiver em `freshShown`.
  - `SelectSection` limpa `freshShown` ao trocar de seção (`:670-673`). Ao voltar para "Novas", só aparece o que ainda não foi lido, que nesse caso é nada.
- **Causas prováveis** (precisa reproduzir):
  - **A (mais provável)**: na virada de turno a tela continua `Shown` para o código, mas fica invisível. O grupo de telas cheias se esconde sozinho com outro cursor, conforme o comentário em `SetOpen`, `:640-642`. O `RefreshContent` roda "no escuro", marca as cartas como lidas e o `freshShown` guarda os ids. Algo então reconstrói a lista ou limpa `freshShown`, e quando o Lucas olha já não há não lidas.
  - **B**: a lista não redesenha quando o número de linhas muda depois da virada (`SetLetterRowCount`), mas a marcação de lida (`:914`) acontece mesmo assim.
- **Correção proposta, qualquer que seja a causa**:
  - Marcar como lida **só quando a carta foi vista de fato**: tela visível (`VisibleSelf` do grupo, não só `Shown`) e linha desenhada.
  - E/ou manter em "Novas" toda carta que chegou neste turno ou no anterior, mesmo já lida, até o jogador trocar de seção **depois de ter visto**.
- **Reproduzir** pelo canal de dev: `ai mail open` → `ai mail section 0` → `game turn` → `screenshot` + `ai mail`/log. Não faço isso agora para não mexer na partida do Lucas.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): marca como lida só com a lista visível (`NativeUIKit.IsReallyVisible`), e "Novas" mostra também as que chegaram no turno.

### 13. Comunicados públicos: avisar e destacar em dourado os que citam o jogador
- **O que aconteceu**: um comunicado público que fala do Lucas (cita o nome ou o povo dele, ou responde a algo que ele disse) se perde no meio dos outros. Exemplo real: a declaração dos Carais "A palavra de barro dos Chou" (turno 44) e a resposta dos Assírios (turno 46).
- **Como está hoje**:
  - A seção "Comunicados públicos" mostra só o número de não lidos (`UI/MailScreen.cs:780, 787`). Não existe destaque nem aviso específico.
  - Cartas públicas **não guardam a quem respondem**: `InReplyTo` só liga cartas privadas, e a reconstrução pula as públicas (`IaWorld.cs:199`).
- **Proposta**:
  1. **Detectar a menção** de forma determinística, sem custo de API. O texto ou o assunto do comunicado cita:
     - o nome do líder do jogador;
     - o nome do povo dele, atual **e dos povos anteriores** (o povo muda por era);
     - uma cidade ou território dele.
     - Opcional: também conta como resposta se o autor recebeu um comunicado do jogador nos últimos N turnos e cita o assunto dele.
  2. **Destaque**: o cartão do comunicado ganha o título/borda em **dourado** e um chip "Cita você" (ou "Responde a você").
  3. **Aviso**:
     - a seção ganha a contagem em dourado, por exemplo "Comunicados públicos (3 · 1 cita você)";
     - o botão do correio ganha o selo de novidade;
     - opcional: uma notificação nativa do jogo "X falou de você em público".
  - Alternativa (B): pedir à IA um campo `"cita": ["E0"]` no JSON do comunicado. É mais preciso para alusões indiretas ("o rei do rio"), mas custa tokens. A recomendação é começar pela **detecção por nomes**.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): detector `Diplomacia\Mentions.cs` (líder, povos de todas as eras em `IaWorld.CultureNames`, cidades, 1 erro de digitação); título dourado + chip "Cita você", contagem dourada na seção e aviso no botão do correio.

### 14. Correio: mostrar o nome do líder junto com o da nação
- **O que aconteceu**: no Correio, o cartão às vezes mostra só "Os Micênicos", sem o líder (print da carta do Māui). Em outros cartões o líder aparece (print do Miyamoto Musashi).
- **Causa** (`UI/MailScreen.cs`, `FillReceivedRow`, ~`:936-943`):
  - O título é só `sender.Culture`.
  - O líder aparece num chip que **divide o lugar** com o estado da carta: se a carta pede resposta (ou é ultimato), o chip vira "Pede resposta" e o nome do líder some.
  - Nas enviadas, o título é "Para {povo}", também sem líder (`:970-971`).
- **Proposta**:
  - O título passa a ser "Os Micênicos · Māui" (povo · líder) nas recebidas, enviadas ("Para Os Assírios · Miyamoto Musashi") e públicas ("Os Carais · Shaka Zulu · a todos").
  - O chip do estado fica só com o estado.
  - Conferir também a seção "Nações" e o título da aba Cartas da diplomacia ("Correspondência com {povo}", `CorrespondenceTab.cs:663`), para ficarem iguais.
  - Atenção ao `FitTitle`: com nomes longos, o título não pode invadir os botões "Na diplomacia/Responder".
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): título "Povo · Líder" nas recebidas, enviadas e públicas (`MailScreen.NationAndLeader`).

### 15. Grupos de conversa com várias nações
- **Pedido**: criar um "chat em grupo": uma conversa com 3 ou mais nações, em que todos leem e respondem.
- **Status**: feito em 2026-10-10. Testado no Teste16 o lado do jogador: criar grupo (Nações → "+ Grupo", nome no assunto), mensagem a grupo existente, seção "Grupos" com cartões, botões Escrever/Sair. Traduzido para en/es/fr/de.
  - Modelo: `ChatGroup` (`IaWorld.cs`), carta com `To = Letter.Group`, `GroupId` e `Members` fixados no envio.
  - IA: `criar_grupo`, `sair_grupo` e carta tipo "grupo"; o dossiê mostra "SEUS GRUPOS". **Falta ver a IA usando** (sem cota do Codex em 2026-10-10).
  - Comandos: `ai group show|create|msg`; `ai mail group N|new`, `member E#`, `subject`, `send`.
  - Fora da v1: interceptação de mensagem de grupo.
- **Hoje**: a carta é privada (1 destinatário) ou pública (todos que conhecem o remetente). Não existe destinatário múltiplo nem conversa persistente (`Letter.To` é um índice só, `IaWorld.cs:376`).
- **Ideia**:
  - **Grupo** = nome + membros + histórico próprio.
  - Carta de grupo: `To = grupo` (novo campo), entregue a cada membro no tempo de entrega da era dele.
  - Uma aba/seção "Grupos" no Correio e na diplomacia.
  - A IA ganha a ação `escrever_grupo`, e o dossiê mostra a conversa do grupo como um bloco só.
  - Interceptação: um espião em qualquer membro lê.
- **Custo**: não aumenta chamadas. Cada IA já decide 1 vez por turno, e a mensagem de grupo é só mais um bloco no dossiê dela. O que cresce são os tokens do dossiê (histórico do grupo, que precisa de resumo/corte como a correspondência).
- **Ligação**: é o canal natural dos blocos do item 16.
- **Status (design antigo)**: substituído pela implementação acima.

### 16. Blocos de nações (tipo OTAN, BRICS)
- **Pedido**: alianças multilaterais com nome, membros e **bônus** (rotas comerciais e outros).
- **Status**: feito em 2026-10-10 e testado no Teste16 (T84–85), menos a IA de linguagem usando as ações (sem cota do Codex). Código em `Diplomacia\Blocs\`:
  - `Bloc.cs` (modelo, no save em `IaWorld.Blocs`) e `BlocEngine.cs` (regras: fundar, convites de 5 turnos, formação com 3, pautas, votos, eleição de 20 em 20 turnos, saída com espera de 10, desfaz com menos de 3). Testado: maioria, empate pelo presidente, expulsão por 2/3, eleição, espera depois da expulsão.
  - `BlocIndex.cs`: retrato para a thread da simulação. `TradePolicy.GetMode`: entre membros é sempre livre; contra quem é de fora vale o mais duro entre a tarifa comum e a regra do posto. Testado: o pedágio de E0 contra E7 parou de valer na rota de E7.
  - `BlocTrade.cs`: devolve 25% da manutenção das rotas entre membros no fim do turno. Testado: "império 7 recebeu 11,9 de volta".
  - `BlocDefense.cs`: Harmony em `DiplomaticState_War.OnBegin` (o jogo sabe quem começou). Os outros membros em paz com o agressor ganham a reclamação nativa "Em guerra com aliado". Testado: guerra de E0 a E6 deu a reclamação a E8.
  - `BlocCongress.cs`: a pauta "congresso" aprovada manda o mesmo voto (`votar_congresso`) para cada membro da IA que ainda não votou. Testado: E7 apoiou E0 na crise K6 com peso 95.
  - IA: `fundar_bloco`, `responder_bloco`, `pauta_bloco` (o presidente põe em votação, os outros sugerem), `abrir_pauta`, `votar_pauta`, `votar_presidente`, `sair_bloco` (`BlocActions.cs`); seção BLOCOS no dossiê (`BlocDossier.cs`).
  - Tela **Alianças** (`UI\AlliancesScreen.cs`, botão de três anéis com selo "!" em `UI\AlliancesButton.cs`). Seções: Meu bloco (cabeçalho, 4 benefícios com números, membros com fama, força e era), Pautas e eleição (Sim/Não, pôr em votação/descartar, candidatos), Convites, Blocos do mundo, Histórico. À direita: fundar (nome + "+ Convidar") ou propor pauta (tipo/nação/valor com "Trocar", motivo) e sair com confirmação.
  - Comandos: `ai bloc show|found|accept|decline|leave|propose|open|discard|vote|president|turn [+N]` (com "as E#" age como outra nação); `ai alliances open|section N|kind N|target E#|value N|text|invite E#|send`.
  - IA testada em 2026-10-11 (T85, Codex): E9 leu o convite no dossiê e recusou com `responder_bloco` (aplicada). O jogador vota no Congresso pela tela nativa (o bloco só avisa).
- **Hoje**: o jogo só tem acordos **bilaterais** (comércio, fronteiras abertas, mapas, não agressão, aliança), e o mod usa esses (`propor_acordo economico|informacao|cultural|militar|alianca`, `Prompts.cs:65`). Não existe entidade "bloco".
- **Ideias de bônus**, para discutir:
  - **Comércio**: rotas entre membros sem pedágio nos postos dos membros e com manutenção reduzida. Liga com o bloqueio comercial (`docs\bloqueio-comercial.md`).
  - **Política**: postura/tarifa comum contra não membros, como uma "união aduaneira".
  - **Militar**, estilo OTAN: ataque a um membro gera reclamação/pretexto de guerra para todos.
  - **Congresso Mundial**: voto em bloco ou influência somada.
- **Regras a definir**: fundar (quem convida, mínimo de membros), entrar/sair/expulsar, liderança, nome/bandeira, custo de manutenção (influência?), o que acontece com membros em guerra entre si.
- **IA**: ações `fundar_bloco`, `convidar_bloco`, `sair_bloco`. O dossiê mostra os blocos do mundo. Sem forçar decisões (memória "AI freedom over rules").
- **Status (design antigo)**: substituído pela implementação acima.

### 17. A IA escolhe pagar o pedágio ou desviar, conforme a conversa
- **Pedido**: hoje o desvio é automático. Deveria ser uma **decisão da IA**, influenciada pela negociação com o Lucas (ex.: "pague meu pedágio e eu protejo sua rota" → ela aceita pagar mesmo sendo mais caro).
- **Hoje**: a rota compara custos sozinha. Se o pedágio, convertido em custo de caminho, sai mais caro que a volta, ela desvia (`docs\bloqueio-comercial.md:34-39`, peso `PesoPedagioNasRotas`). A IA de linguagem só controla os **próprios** postos (`politica_comercial`, `Prompts.cs:74`), não as rotas dela que passam pelos postos dos outros.
- **Proposta**:
  - Nova ação da IA: `rota_pedagio {"posto_de": "E0", "escolha": "pagar|desviar|automatico", "motivo": "..."}`. Vale para todas as rotas dela pelos postos daquela nação, até mudar.
  - `pagar`: o custo do pedágio vira 0 no pathfinding dela para os postos dessa nação, e ela paga.
  - `desviar`: custo infinito naqueles territórios, como no bloqueio.
  - `automatico`: a regra de hoje.
  - No dossiê, a IA vê quanto paga por turno a cada nação, e quanto custaria o desvio em manutenção (já calculado no `TollPreview`).
  - A escolha aparece para o Lucas no Posto Comercial e na prévia: "Os Assírios escolheram pagar".
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): ação `rota_pedagio` (pagar/desviar/automatico), `TradePolicy.SetTollChoice`, custo no caminho (`TradeBlockade.TollPathCost`), dossiê mostra a escolha, painel da diplomacia mostra "eles escolheram pagar/desviar".

### 18. BUG: texto colado de fora fica transparente no campo da carta
- **O que aconteceu**: ao escrever uma carta, o Lucas colou um texto copiado de fora do jogo. O texto ficou **transparente/invisível** no campo.
- **O que já se sabe**:
  - O log do BepInEx não tem erro no momento. O campo é o `UITextField` nativo clonado (`BodyField` em `UI/MailScreen.cs` e `UI/CorrespondenceTab.cs`). O mod só liga o `TextChange` (`NativeUIKit.EnsureTextChange`, `NativeUI/NativeUIKit.cs:99`) e **não trata a colagem nem limpa o texto colado**.
- **Causas prováveis**, a confirmar:
  1. **`\r\n` (quebra de linha do Windows)** ou outros caracteres de controle (tab, NUL, espaço sem quebra, caracteres de largura zero). O `\r` pode quebrar o layout/mesh do texto nativo, que espera só `\n` (Enter no campo gera `\n`).
  2. **Marcação do texto da Amplitude**: o texto nativo interpreta tags como `<c=...>`. Um `<` ou `>` no texto colado pode abrir uma tag inválida e deixar o resto sem cor (alfa 0).
  3. **Glifo fora da fonte**: emoji ou símbolos raros sem glifo na fonte do jogo. É menos provável deixar *tudo* transparente; costuma sumir só o caractere.
- **Correção proposta**:
  - Limpar o texto **ao mudar** (no handler do `TextChange`):
    - `\r\n`/`\r` → `\n`;
    - tab → espaço;
    - remover controles e caracteres de largura zero;
    - escapar ou trocar `<`/`>`;
    - trocar emoji/glifos sem suporte por "?".
  - Aplicar a mesma limpeza no envio (`IaModule.cs:~1714`), para o que vai para a IA e para o save ficar limpo.
- **Reproduzir**: pelo canal de dev, `ai mail text ...` com `\r\n`, com `<b>` e com emoji, e depois `screenshot`. Se puder, me mande de onde veio o texto (site, Word, WhatsApp…) ou o próprio texto, para ver qual dos três é.
- **Status**: feito no código em 2026-10-10 (testado no Teste16 em 2026-10-10, ver "Testes de 2026-10-10" no fim): reposicionamento do texto depois de colar (`NativeUIKit.FixLayoutAfterPaste`) nos campos do Correio, da aba Cartas e do conselho.
- **Checagem 2026-10-10** (logs T077/T078): **o texto chegou inteiro e foi entendido.**
  - A carta colada é a declaração pública "Carta Pública às Nações do Mundo" (escrita no turno 75, chegou no 77): ~2.700 caracteres, ~450 palavras, 20+ parágrafos.
  - Todas as 15 IAs a receberam completa no dossiê e reagiram ao conteúdo:
    - Banto: "não assino em branco", perguntou o que a aliança obriga;
    - Pama-Nyungan: "falo com voz própria";
    - Assírios: notaram que **ficaram de fora apesar do sim**;
    - Babilônia: passou a desconfiar, porque o bloco cerca o território dela.
  - **O bug é só visual, no campo de texto.**
- **O que isso muda nas hipóteses**:
  - O texto colado **não tem `<`, `>` nem emoji**, então as hipóteses 2 e 3 caem.
  - Ele usa só `\n` (LF), enquanto as cartas digitadas no jogo terminam com `\r`. Isso indica que o campo nativo grava `\r` e o mod guarda os dois sem normalizar, e vale corrigir de qualquer jeito.
  - **Nova hipótese principal**: o **tamanho**. Muitas linhas e muitos caracteres estouram o limite de vértices/altura do texto nativo do `UITextField`, e ele deixa de desenhar o texto. Testar colando um texto curto de fora; se aparecer, a causa é o tamanho.
  - Se for isso, a correção é: limitar/rolar o campo, ou mostrar um contador "texto longo demais para exibir" sem perder o conteúdo, além de normalizar `\r\n` → `\n` na entrada e no envio.

### 19. IA fora do ar: um aviso só, X vermelho nas cartas e sem rajada de chamadas
- **Pedido (Lucas)**: quando a IA realmente não funciona (a fila inteira de provedores falhando), nada de vários erros seguidos. Detectar isso de forma eficiente e marcar com um X vermelho / "Desconectado" no botão das cartas.
- **Antes**: o aviso "Nenhum provedor de IA respondeu" aparecia de novo a cada recuperação breve, e as nações continuavam chamando a API sem crédito. No teste, as chamadas da DeepSeek subiram de 32 para 42 só com HTTP 402.
- **Feito** (`Llm\Providers\ProviderRouter.cs`):
  - Estado `AllDown` ("IA desconectada"): liga quando todos os provedores falham e desliga na primeira resposta boa ("IA reconectada: usando X").
  - Enquanto desconectada, `MayCall()` só libera **uma chamada de teste** a cada 2, 4, 8 e até 10 minutos. `IaModule.Schedule` e `PlayerCouncil.Tick` respeitam isso, e a reunião espera sem gastar tentativas.
  - Trocar a fila ou a credencial (`ResetHealth`) testa na hora.
  - O aviso na tela aparece uma vez por queda (e não de novo se cair outra vez em menos de 15 min).
  - **X vermelho** no selo do botão do Correio, com o motivo e o prazo do próximo teste na dica, e a linha "X IA desconectada" no topo do Correio.
- **Testado no Teste16**:
  - Fila só com a DeepSeek sem crédito: aviso novo uma vez, X no botão e linha no Correio.
  - Fila com o Codex e uma chamada boa: o X some.
- **Status**: feito e testado em 2026-10-10.

---

## Testes de 2026-10-10 (save de teste Teste16 T82, nunca o save do Lucas)

- **Backup antes dos testes**: `Documents\HumankindModding\backups\saves-2026-10-10-antes-dos-testes\` (o "NAO DELETE PELO AMOR DE DEUS" e os autosaves 664–673). Os arquivos do teste (a cópia do Teste16 e o autosave 2208) foram para `…\arquivos-do-teste\`.
  - Efeito colateral: o rodízio do jogo apagou o `auto-save 2198` da pasta Cloud, um autosave antigo de março (partida "Chineses").
- **Provedor**:
  - A DeepSeek ficou **sem crédito** (HTTP 402), o que vale também para a partida do Lucas.
  - Os testes com IA usaram o **Codex** (fila = `codex`, a pedido do Lucas). A fila ficou assim.

| Item | Resultado no jogo |
|---|---|
| 1 | Turno 83 sem reunião automática (próxima no 89); `ai council convene` fez a audiência pelo Codex. |
| 4 | Cartão "Resumo do conselho" no topo, uma linha por ministro. |
| 5 | Reunião nova sem mecanismo inventado. Ajuste: o prompt também proíbe os códigos do Congresso (K6, V2, I3). |
| 6 | Ordem decrescente (não fotografado: a aba fica dentro da diplomacia). |
| 7 | Compilado; depende de cartas novas das IAs para ver na prática. |
| 8 | Rota do jogador em **azul** (Café para os Axumitas). Rótulo testado com `game routes colors all`. Estilos não se acumulam mais a cada recarga. |
| 9 | Contador "N/2000 caracteres". |
| 10 | Aba "Recursos" com o Café (1 acesso, luxo, "Seu · vendido para os Axumitas"). Gráfico vazio com a mensagem dentro do cartão. |
| 11 | "O que isso significa no jogo:" em cartas reais ("transformou queixas em exigências formais", "propôs um acordo econômico"). |
| 12 | Correio aberto em "Novas" + passar o turno: a carta do turno 83 apareceu como nova. |
| 13 | `ai mentions`: reconhece "Icaresia" com I e "o Persa"; marcou o comunicado real "O bronze de Mykênê", que cita o Persa. |
| 14 | "Os Garamantes · Kiviuq" (o artigo ficava minúsculo; corrigido). |
| 17 | Prévia: pagar = passa mesmo com pedágio 400; automático = desvia com 401; desviar = desvia com 7. |
| 18 | **Causa real achada**: o rótulo do campo nativo para de desenhar com pouco mais de **2050 caracteres** (até 2050 aparece, com 2100 some, com ou sem quebras de linha). Não era colar nem `\r\n`. Correção: carta do jogador limitada a **2000 caracteres** (`IaConfig.PlayerLetterLimit`; o campo para de aceitar texto nesse ponto). A carta pública do Lucas tinha ~2550. |
| 19 | Ver item 19. |

**Ferramentas de teste novas**:
- `game saves rescan`: relê a pasta de saves, para o jogo ver um save copiado com ele aberto.
- `ai mentions [text]`
- `ai mail paste [arquivo em _Modding\dev]`
- `game routes colors E#|all`: ver as cores como outro império, ou rótulo em toda rota.

**Testado em 2026-10-11 (antes "ainda não visto")**: rótulo de pedágio pago na visão de comércio ("Tl 4/t" em dourado, comando `game routes labels`); item 7: os Han, sem cavalos nem seda, recusaram oferecer o que não têm; item 5: o conselho disse não conhecer o "Tesouro dos Grãos Sagrados" (nada a cortar); tarifa comum de bloqueio desviou a rota E1↔E7 do território do bloco; Fé +10% medida (`ai council faith T#`: 3 → 3,3); IA escrevendo em grupo (Nascas no GRP1); 7 ações de bloco pelo validador da IA (`ai action E# {json}`). Bug achado e corrigido: candidatos a ministro não marcavam o mundo como alterado (sorteio novo depois de recarga). Itens antigos desta lista:
- rótulo de pedágio real (nenhuma rota cruza as terras do jogador no Teste16; o rótulo foi visto no modo de teste);
- carta devolvida por oferecer recurso que a nação não tem (item 7);
- frase cortada pelo filtro do conselho (item 5): nada inventado apareceu.

---

## Design da etapa 5 (conversa de 2026-10-10)

### Item 3: escolher ministros, decidido
- **Início:** o conselho começa só com a **Mão**. As 11 pastas ficam vagas, e cada um nomeia quando quiser. Com isso o item 2 (pastas ligadas a terreno/unidade) não é necessário.
- **Custo:** **influência, uma vez**, ao nomear. Padrão a definir na implementação, por exemplo 25 × (era + 1): 50 na Antiga e 175 na Contemporânea. Sem manutenção.
- **Candidatos:** **3 por vaga**, com nome, traços e voz. Ficam os mesmos até a próxima era ou até alguém ser nomeado (assim não dá para sortear até aparecer o perfeito).
- **Demitir:** abre a vaga, sem substituto automático. A pasta ganha candidatos novos.
- **IA:** as nações da IA **também** passam a começar só com a Mão e nomeiam com a ação `nomear_ministro {"pasta", "candidato"}` (pagando a influência). O dossiê mostra as vagas e os 3 candidatos de cada uma.
- **Saves em andamento:** **reset**. Ao carregar, o conselho de todos (jogador e IAs) volta a ter só a Mão. Apreço e credibilidade dos que saem se perdem.
- **Veterano (pedido do Lucas):** ministro que fica no cargo ganha **bônus real na área da pasta**: +1% a cada 5 turnos no cargo, até +10%. Demitir zera. Vale também para as IAs.
  - Agricultura: comida
  - Finanças: dinheiro
  - Guerra: força de combate
  - Ciência: ciência
  - Obras: indústria
  - Comércio: dinheiro das rotas
  - Interior: estabilidade
  - Fé: fé
  - Cultura: influência
  - Relações: influência
  - Espionagem: chance de interceptar cartas
  - Mão: sem bônus próprio (a definir)
  - Implementação: aplicar efeito nos rendimentos do jogo (pesquisar o caminho: modificadores ou descritores nativos, como a economia do mod já faz com o dinheiro).

### Itens 15 e 16: grupos de conversa e blocos, decidido
**Grupos de conversa** (separados dos blocos; todo bloco ganha automaticamente o grupo dos membros):
- Qualquer um cria: o jogador e as IAs. A IA escreve no grupo na decisão do turno, sem chamada extra.
- Mensagem de grupo segue a regra da carta privada: chega pelo tempo da era e pode ser interceptada.
- Padrões a confirmar na implementação:
  - o criador convida e os convidados entram, podendo sair (`sair_grupo`);
  - até 8 membros;
  - 1 mensagem por grupo por turno;
  - o dossiê mostra as últimas mensagens, resumidas como a correspondência.
- Os grupos ficam no Correio (seção "Grupos").

**Blocos:**
- **Nascimento:** o fundador escolhe o nome e convida. Cada convidado aceita ou ignora em até 5 turnos. O bloco nasce com **3 membros** (contando o fundador); quem aceitar depois entra direto.
- **Benefícios** (os quatro):
  - **comércio interno:** sem pedágio nem bloqueio entre membros e manutenção das rotas mais barata (ex.: -25%);
  - **defesa mútua:** ataque a um membro dá a todos uma queixa (motivo de guerra) contra o agressor, sem obrigar ninguém a lutar;
  - **voz no Congresso:** voto em conjunto com influência somada;
  - **tarifa comum:** pedágio ou bloqueio contra uma nação de fora em todos os postos dos membros.
- **Presidente** (ideia do Lucas):
  - o fundador é o primeiro presidente; mandato de **20 turnos**; eleição com **1 voto por membro**, maioria simples, empate mantém o atual; reeleição sem limite;
  - **só o presidente põe pautas em votação**; os outros membros **sugerem** pautas.
- **Votação:** cada membro vota sim ou não em até 3 turnos (sem voto = abstenção). Passa com **maioria simples**; no empate, decide o voto do presidente. **Expulsar** exige **2/3**. Pautas possíveis: tarifa comum, admitir ou expulsar membro, voto do bloco no Congresso (e, na implementação, renomear e dissolver).
- **Exclusividade:** **um bloco por nação**.
- **Guerra entre membros não expulsa.** A defesa mútua vale só contra quem é de fora.
- **Saída** (padrão): sair é livre, com 10 turnos de espera para entrar em outro bloco. O bloco com menos de 3 membros se desfaz.
- **IA:** participa de tudo por ações: `fundar_bloco`, `responder_bloco`, `sugerir_pauta`, `pautar` (presidente), `votar_pauta`, `votar_presidente`, `sair_bloco`. O dossiê mostra o bloco, as pautas abertas e os blocos do mundo.
- **Tela nova "Alianças"** (pedido do Lucas: linda, bem elaborada, intuitiva e com os dados importantes):
  - membros com bandeira, força e fama; o presidente e quanto falta do mandato;
  - pautas abertas com o placar e o prazo, sugestões, convites e o histórico de decisões;
  - os benefícios ativos, com números (pedágio poupado, manutenção poupada);
  - os outros blocos do mundo;
  - um atalho para o grupo do bloco.
  - Botão novo na barra, ao lado do Correio e do Conselho.
  - Nome: a tela chama "Alianças", e a entidade, "bloco", para não confundir com o acordo de Aliança do próprio jogo.

### Item 3 implementado (2026-10-10, testado no Teste16)
- **Modelo:** `IaNation.CouncilModel/CouncilCandidates/CouncilCandidatesEra`.
  - `CouncilEngine.Ensure` mantém a Mão e 3 candidatos por pasta vaga (mudam na era nova).
  - `Appoint` nomeia, `Fire` abre a vaga (a Mão é trocada na hora).
  - Custo `AppointCost` = 25 × (era + 1), descontado pela ordem nativa `OrderGainInfluence` com valor negativo (`PlayerCouncil.ChargeInfluence`).
  - Testado: 1008 → 933 de influência.
- **Reset:** `IaWorld.Migrate` zera os conselhos antigos de todas as nações ao carregar (antes, as IAs ainda rendiam bônus com os 12 veteranos até o dossiê ser montado).
- **IA:**
  - ação `nomear_ministro {"pasta","candidato":"p###"}` (`IaModule.ApplyAppoint`);
  - o dossiê lista as PASTAS VAGAS com os candidatos e o custo, e mostra os veteranos.
  - No teste, nenhuma IA nomeou ainda: estavam ocupadas com guerra e Congresso, e o mod não força decisões.
- **Bônus de veterano** (`Council\VeteranEffects.cs`, Harmony no mesmo estilo da economia do mod; descritores nativos descartados porque só entram na compilação do boot):
  - Fim de turno: dinheiro, ciência e influência (sobre o líquido positivo), comida (crescimento das cidades) e Comércio (sobre o que vendeu pelas rotas no turno anterior).
  - Indústria: `ComputeProductionIncome`. Estabilidade: meta + pontos. Combate: `BattleUnit.GetCombatStrength`. Espionagem: peso dos espiões.
  - **Fé:** o jogo não tem estoque de fé; o bônus aumenta em % a força de crença da religião do império nos territórios dele (Harmony em `ReligionManager.ComputeTerritoryBeliefStrengths`, `VeteranEffects.FaithPatch`). Compila e aplica; o efeito no mapa ainda não foi medido.
  - Testado: E0 com 6 veteranos a +10% → dinheiro +2,7, ciência +0,8, influência +4,0 no fim do turno (log "Veterano E#").
- **Tela:**
  - seção Ministros com os que estão no cargo (chip "Veterano +N%"/"Novato");
  - pastas vagas com "Escolher", que abre os 3 candidatos, cada um com "Nomear · custo".
- **Comandos de teste:** `ai council candidates`, `ai council appoint <portfolio> <1|2|3|p###>`, `ai council veteran <portfolio> <turns>`.
- **Retratos** (pedido do Lucas: "na hora de escolher deve ser bem bonito"):
  - gerados com o **Codex do WSL** (geração de imagem embutida, plano Plus): `tools\portraits\generate_portraits.py` + `generate.sh`, depois `convert.py` (256 px, embutidos na DLL como `retrato.<cargo>_<época>_<g>.jpg`);
  - carregados por `Council\Portraits.cs` e mostrados à esquerda dos cartões de ministro, candidato e fala.
  - **Escolha do Lucas:** genérico por cargo × 3 épocas × gênero (72 imagens), para poupar a cota. O retrato por pessoa (216) gastaria ~72% da semana.
  - **Medido:** 3 imagens ≈ 10% da janela de 5 h e ≈ 1% da semana. O script espera a janela renovar quando passa de 85%.
  - Testes por pessoa (Aurélio Varro, Nefertari, Kang Wei, em 3 épocas) ficaram em `media\retratos\pessoais-teste\`.
