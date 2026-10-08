# Humankind Mod (lucas) — instalação

O mod reúne quatro partes:
- **Moeda própria por império:** câmbio, inflação e juros, com o Banco Central (F8).
- **Bloqueio comercial:** livre, pedágio ou bloqueado em cada posto comercial.
- **Diplomacia IA:** cada nação do computador vira um personagem que pensa, escreve cartas e guarda memória. Usa a
  IA que você escolher na tela **Diplomacia IA**: OpenRouter, DeepSeek, OpenAI, Google Gemini, GLM (Z.ai) ou xAI.
- **Visualizador de debug:** página no navegador para acompanhar a Diplomacia IA (F10).

Testado com **Humankind 1.31.4836** (Steam, Windows) e **BepInEx 5.4.23.5 x64**. Só para partidas de um jogador:
em partida online o mod se desliga sozinho.

---

## 1. Instalar o BepInEx (uma vez)
1. Feche o jogo.
2. Baixe `BepInEx_win_x64_5.4.23.5.zip` em https://github.com/BepInEx/BepInEx/releases. Precisa ser a versão
   **5.x x64**; a 6.x não serve.
3. Ache a pasta do jogo: na Steam, clique com o direito em Humankind → Gerenciar → Procurar arquivos locais. É a pasta
   onde está o `Humankind.exe`.
4. Extraia o zip nessa pasta. Devem aparecer `BepInEx\`, `winhttp.dll` e `doorstop_config.ini` ao lado do
   `Humankind.exe`.
5. Abra o jogo uma vez até o menu principal e feche. Isso cria as pastas `BepInEx\plugins`, `BepInEx\config` etc.

## 2. Instalar o mod
1. Extraia o pacote `HumankindMod_<versão>.zip` na pasta do jogo, aceitando mesclar a pasta `BepInEx`.
2. Os arquivos do mod são:
   - `BepInEx\plugins\CurrencyMod\CurrencyMod.Loader.dll`, o carregador;
   - `BepInEx\CurrencyModCore\CurrencyMod.dll`, o mod em si. Ele fica fora de `plugins` de propósito: quem carrega é
     o carregador.

## 3. Conectar a IA (só para a Diplomacia IA)
Sem IA conectada, as nações jogam com a IA normal do jogo; o resto do mod funciona normalmente. Nada de editar
arquivos: tudo é feito numa tela do jogo.
1. Abra o jogo. No menu principal, clique em **Diplomacia IA** (o mesmo botão existe no menu de pausa da partida, ESC).
2. Escolha um provedor na coluna da esquerda. O mais fácil é o **OpenRouter**:
   1. clique em **Entrar**: o navegador abre no site do OpenRouter;
   2. entre com a sua conta (ou crie uma) e autorize;
   3. volte ao jogo. Em alguns segundos o OpenRouter aparece como **Conectado · 1º**.

   Para usar o crédito, ponha saldo na sua conta do OpenRouter. Há modelos grátis, com limite de chamadas por dia.
3. **Com a conta do ChatGPT (Plus ou Pro), pelo Codex:**
   1. instale o **Codex** (o app da OpenAI; o botão **Instalar** na página "ChatGPT (Codex)" abre o site);
   2. clique em **Entrar**: o Codex abre o navegador para você entrar com a conta do ChatGPT;
   3. o uso sai da cota do seu plano, sem chave. No Plus, use o **gpt-6-luna**: os modelos maiores esgotam a cota em poucos turnos.
   4. o uso segue os termos da OpenAI.
4. **Com chave (DeepSeek, OpenAI, Gemini, GLM, xAI ou OpenRouter):**
   1. clique em **Abrir site** ao lado de "Como conseguir uma chave", crie a chave no site e copie (Ctrl+C);
   2. no jogo, clique em **Colar**. Também dá para colar no campo "Chave de API" e apertar Enter.
   3. O **GLM-4.7-Flash** (Z.ai) é grátis. O **Gemini** tem nível grátis, mas aí o Google pode usar os textos para
      treinar os modelos dele.
5. Clique em **Testar**: deve aparecer "Conectado: … respondeu em … s".
6. Em **Modelo**, a estrela ★ marca o recomendado, e o valor ao lado é o custo estimado por turno com 16 impérios.
7. **Reserva (opcional):** conecte mais de um provedor e defina a ordem em "Na fila". Se o 1º der erro (sem crédito,
   fora do ar), o 2º assume sozinho. Se todos falharem, a IA normal do jogo joga e um aviso aparece uma vez.
8. Na seção **Em uso** ficam o teto de gasto por partida, quantas nações pensam por turno, o idioma das cartas e o
   raciocínio do modelo.

As chaves e os logins ficam **criptografados neste PC** (Windows, só o seu usuário), em `BepInEx\config\credenciais`.
A tela mostra só os 4 últimos caracteres. Eles só são enviados ao provedor deles e nunca aparecem em log, no
visualizador nem no save. Não compartilhe essa pasta.

Quem já usava o arquivo `BepInEx\config\deepseek.key` de versões antigas não precisa fazer nada: a chave é importada
sozinha e o arquivo continua valendo.

## 4. Conferir se funcionou
1. Abra o jogo e carregue ou comece uma partida.
2. Em `BepInEx\LogOutput.log` devem aparecer as linhas `CurrencyMod 1.1.0 carregado` e `Núcleo carregado`.
3. Aperte **F10**: abre no navegador a página **Diplomacia IA** (`http://localhost:8765/`). A situação deve estar
   **ativa** ou **pensando**. Em poucos segundos aparecem diários e cartas das nações.
4. **F8** abre o Banco Central.
5. Na barra inferior esquerda aparecem dois botões novos, ao lado de "Comercializar":
   - templo: Banco Central;
   - **envelope: Correio Diplomático**, para ler, responder e escrever cartas às nações. O selo vermelho mostra as
     cartas novas.

## 5. Configuração
Arquivo `BepInEx\config\lucas.humankind.currency.cfg`, criado na primeira vez que o jogo abre com o mod. Edite com o
jogo fechado ou recarregue o mod. Seções:
- `[Interface]`: atalhos e botões.
- `[Geral]`, `[Inflacao]`, `[Juros]`, `[Cambio]`: economia da moeda.
- `[BloqueioComercial]`: pedágios e bloqueios.
  - `PedagioPorRecurso` (2): preço padrão do pedágio, multiplicado pela era de quem cobra. Vale onde você não
    definiu outro preço (no Posto Comercial ou na aba Comércio da diplomacia).
  - `PesoPedagioNasRotas` (1): 1 = a rota compara pagar com desviar e escolhe o mais barato; maior = foge mais do
    pedágio; 0 = sempre passa e paga.
  - `IA` e `IntervaloIA`: se a IA de regras também taxa e bloqueia, e de quantos em quantos turnos ela revê.
- `[IA]`: Diplomacia IA. As opções principais:
  - `Ativo`: liga ou desliga.
  - `Raciocinio`: `desligado`, `low`, `high` ou `max`. Mais raciocínio = mais caro.
  - `TetoGastoPartidaUSD`: o máximo que uma partida pode gastar.
  - `ChamadasParalelas`, `CartasPorTurno` e `AtrasoCartasPorEra`.
  - `ConselhoDoJogador` e `ConselhoAbreSozinho`: o seu conselho de ministros (1 chamada por turno e 1 por resposta
    sua) e se a tela dele abre sozinha.
  - `ExpansaoEmSaveAntigo` (false): liga a expansão **Together We Rule** (Congresso mundial) ao carregar um save que
    foi criado sem ela. Só funciona se o DLC estiver comprado e ativo na Steam. Partidas novas com o DLC já vêm com o
    Congresso; sem o DLC, as nações simplesmente não recebem as ferramentas do Congresso.

  A lista completa está em `Diplomacia-IA.md` §5.

Chaves de versões antigas que não fazem mais nada (`Cobertura`, `EfeitoProducao`, `TetoRendimento`…) são tiradas do
arquivo sozinhas quando o jogo abre.

## 6. Custos e privacidade
- **Custo medido** (DeepSeek V4.1 Flash, raciocínio `low`):
  - 9 nações do computador: cerca de **US$ 0,025 por turno** fora do horário de pico;
  - 16 impérios: cerca de **US$ 0,14 por turno** fora do pico, e o dobro no pico (dias úteis, 01h–04h e 06h–10h UTC).
  - O seu conselho soma cerca de US$ 0,003 por turno, mais uns US$ 0,003 por resposta sua.
  - Outros provedores: a tela mostra a estimativa por turno de cada modelo, e o gasto real aparece no topo do F10 e na
    seção "Em uso". O teto padrão é US$ 5 por partida (mude na tela).
- **O que vai para o provedor escolhido:**
  - o estado do jogo **do ponto de vista de cada nação** (dossiê), inclusive da sua, para o seu conselho;
  - as cartas que você escreve e o que você diz ao seu conselho.
  Nenhum dado pessoal vai junto (nem o seu nome nem a sua conta Steam). Cada provedor trata esses textos pela política
  de privacidade dele; no nível grátis do Gemini, o Google pode usá-los para treinar.

## 7. Desinstalar
1. Apague `BepInEx\plugins\CurrencyMod\` e `BepInEx\CurrencyModCore\`.
2. Para tirar também o BepInEx, apague `BepInEx\`, `winhttp.dll` e `doorstop_config.ini`.

Os saves continuam abrindo sem o mod: os dados do mod (`CurrencyMod.json`, `DiplomaciaIA.json`) vão em arquivos
extras dentro do save, e o jogo os ignora.

## 8. Problemas comuns
| Sintoma | O que fazer |
|---|---|
| Não existe `BepInEx\LogOutput.log` | O BepInEx não carregou. Confira o `winhttp.dll` ao lado do `Humankind.exe` e o antivírus. |
| Log sem "Núcleo carregado" | Confira `BepInEx\CurrencyModCore\CurrencyMod.dll`. |
| F10 não abre nada | Abra `http://localhost:8765/` à mão. Se a porta estiver ocupada, mude `[IA] PortaVisualizador`. |
| Situação "sem chave" | Nenhum provedor conectado. Abra a tela **Diplomacia IA** (§3). |
| "Chave recusada" no Testar | Chave incompleta ou apagada no site. Copie de novo e clique em Colar. |
| "Sem crédito na conta" | Ponha saldo no site do provedor, ou conecte outro como reserva. |
| "Limite de chamadas por minuto" (HTTP 429) ou servidor com erro | O mod tenta de novo e, se não der, passa para o próximo da fila. |
| O navegador do login não volta ao jogo | Feche a aba e confira o jogo: o login termina sozinho. Se passar de 5 minutos, clique em Entrar de novo. |
| Aviso "Nenhum provedor de IA respondeu" | Todos da fila falharam. A IA normal do jogo joga até um voltar; veja o estado de cada um na tela. |
| "API com erro": tempo esgotado | Internet instável ou API lenta. Suba `[IA] TempoLimiteSegundos`. |
| Situação "teto de gasto" | A partida chegou em `[IA] TetoGastoPartidaUSD`. Aumente o valor se quiser continuar. |

Logs detalhados de cada decisão da IA ficam em `BepInEx\DiplomaciaIA\logs\`.

---

## Para quem desenvolve
- **Código e compilação:** `_Modding\README.md`. Precisa do .NET SDK 8 e do código do jogo descompilado.
- **Gerar este pacote:** `powershell -ExecutionPolicy Bypass -File _Modding\tools\empacotar.ps1`. O pacote sai em
  `_Modding\dist\`. O script compila sem instalar e se recusa a empacotar se achar uma chave de API.
- O canal de comandos de desenvolvimento (`_Modding\dev\cmd.txt`) só existe em máquinas com a pasta `_Modding`.
