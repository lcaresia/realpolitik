# Humankind — Modding (lucas)

Mods de código para Humankind via BepInEx 5 + Harmony. Single-player.

## Mapa das pastas

| Pasta | O que tem | Vai no backup? |
|---|---|---|
| `src\CurrencyMod.Loader\` | Plugin fixo do BepInEx que carrega/recarrega o núcleo e lê comandos. | Sim |
| `src\CurrencyMod\` | O mod (núcleo recarregável): economia, câmbio, textos, janelas. | Sim |
| `src\CurrencyMod\NativeUI\` | Telas com a interface nativa do jogo: Banco Central, botão da barra, painel de economia na diplomacia, ícones SDF, `NativeUIKit`. | Sim |
| `src\CurrencyMod\Diplomacia\` | **Diplomacia IA**: cada nação do computador pensa com a IA escolhida pelo jogador (dossiê, cartas, memória, visualizador F10). Provedores (OpenRouter, DeepSeek, OpenAI, Gemini, GLM, xAI e ChatGPT pelo Codex instalado no PC), credenciais e logins em `Diplomacia\Llm\Providers\`; a tela "Diplomacia IA" (menu principal e pausa) em `Diplomacia\UI\ProvidersScreen.cs`. Doc: `docs\diplomacia-ia.md` (§15 = provedores). | Sim |
| `docs\design-diplomacia-ia.md` | Design e decisões da Diplomacia IA (fases, furos, regras). | Sim |
| `docs\bloqueio-comercial.md` | Bloqueio comercial por dentro: regras, preços, custo do pedágio no caminho, cobrança, IA e prévia de desvio. | Sim |
| `cronicas\` | Crônicas da partida para compartilhar (PDF e o HTML que gerou o PDF). | Sim |
| `docs\relatorios\` | Relatórios em PDF (ex.: `Economia-Calibracao-ELI5.pdf`, a calibração da economia explicada de forma simples). | Sim |
| `src\CurrencyMod\Lang\` + `L.cs` | **Tradução** da interface (en, es, fr, de; o português é a chave). Como mexer: `docs\traducao.md`; scripts `tools\dev\extrair-textos.ps1` e `validar-traducao.ps1`. | Sim |
| `docs\instalacao.md` | **Instalação em outra máquina** (vai no pacote como LEIA-ME). | Sim |
| `tools\empacotar.ps1` | Gera `dist\HumankindMod_<versão>.zip` sem a chave da API. | Sim |
| `tools\dev\` | Scripts de teste com o jogo aberto: `devcmd.ps1` (manda comandos pelo canal `dev\cmd.txt` e mostra as respostas), `passturn.ps1` / `passturns.ps1 -Target N` (passa turnos esperando as nações da IA decidirem) e `backup.ps1` (zip em Documentos, sem a chave da API). | Sim |
| `docs\prompt-*.md` | Prompts prontos para sessões novas do Claude Code (16 impérios; revisão de bugs; pacote para outros jogadores). | Sim |
| `docs\negociacao\` | Material do módulo de negociação, ainda em conversa (prints da aba Crise de hoje). | Sim |
| `dist\` | Pacotes gerados. | Não |
| `src\HelloHumankind\` | Plugin de teste inicial (modelo mínimo de projeto). Não está instalado. | Sim |
| `docs\guia-telas-nativas.md` | **Guia para criar novas telas nativas** (kit, receita, doadores, armadilhas). Inclui a receita de tela do sistema (menu principal e pausa), com o doador do menu principal. | Sim |
| `research\provedores-ia.md` | Pesquisa dos provedores de IA: endereços, preços, json, erros, logins e o que depende de aprovação. | Sim |
| `docs\pedidos-provedores.md` | Rascunho (em inglês) da política de privacidade para o site do produto. | Sim |
| `research\` | Pesquisas no código do jogo: UI nativa, crise/negociação, Diplomacia IA (viabilidade, relógios nativos, dossiê, correio em tela cheia e aba na diplomacia). | Sim |
| `loja\` | **Loja própria** (Stripe + Cloudflare Worker/D1/R2): checkout, chaves de licença, downloads. Deploy `loja\tools\deploy-loja.ps1`, teste `loja\tools\testar-loja.ps1`. Doc: `docs\loja.md`; integração no jogo: `docs\integracao-licenca.md`. Segredos só no `.env`. | Sim |
| `decompiled\` | Código do jogo descompilado (regenerável com `ilspycmd`). | Não |
| `dev\` | Canal de comandos e capturas de tela do kit de desenvolvimento. | Não |

## Onde o mod fica instalado
- `BepInEx\plugins\CurrencyMod\CurrencyMod.Loader.dll` — carregador.
- `BepInEx\CurrencyModCore\CurrencyMod.dll` — núcleo (fora de `plugins` de propósito).
- `BepInEx\config\lucas.humankind.currency.cfg` — balanceamento e opções (inclui a seção `[IA]`).
- `BepInEx\config\credenciais\` — chaves e logins dos provedores, criptografados (DPAPI, usuário atual). Pessoal: **nunca** vai para backup nem pacote.
- `BepInEx\config\deepseek.key` — chave antiga do DeepSeek (importada para as credenciais; continua valendo). Pessoal: **nunca** vai para backup nem pacote.
- `BepInEx\DiplomaciaIA\logs\` — log de cada decisão da IA (dossiê, prompt, resposta, custo).
- `Humankind_Data\Managed_backup\` — cópia das DLLs originais do jogo.

## Compilar
```
dotnet build _Modding\src\CurrencyMod -c Release          # núcleo (recarrega sozinho com o jogo aberto)
dotnet build _Modding\src\CurrencyMod.Loader -c Release   # carregador (exige reiniciar o jogo)
```
O SDK do .NET 8 fica em `C:\Program Files\dotnet` (pode não estar no PATH do terminal).

## Regenerar o código descompilado
```
ilspycmd -p -o _Modding\decompiled\<Assembly> -r Humankind_Data\Managed Humankind_Data\Managed\<Assembly>.dll
```
(ilspycmd 9.1 — a versão mais nova exige .NET mais recente.)

## Funcionalidades do CurrencyMod
- Moeda própria por império (nome, plural, símbolo), câmbio, inflação, juros (manual/automático).
- Conversão de moeda em comércio de recursos e transferências diplomáticas.
- Nome da moeda nos textos com valores ("500 Reais").
- Banco Central nativo (botão ao lado de "Comercializar", janela com abas Câmbio / Política / Ciclo / Sua moeda). A aba Ciclo explica o ciclo produção → inflação → juros → produção do império: diagnóstico com o que fazer, os três elos, estabilidade, rendimento do saldo e o rumo da inflação decomposto (`docs\proposta-ciclo-economico.md`). Os efeitos também entram nos números e nos tooltips do próprio jogo: crédito na indústria das cidades, inflação e desemprego na meta de estabilidade e juros do saldo na renda da barra do topo, cada um com a sua linha (`src\CurrencyMod\NativeEffects.cs`).
- Painel "Economia" na aba Relações da diplomacia.
- Dados salvos dentro do próprio save (`CurrencyMod.json` no container).
- **Bloqueio comercial** (`src\CurrencyMod\Trade\` + `NativeUI\TradePostWindow.cs`): na visão de comércio, clicar num
  território seu abre o "Posto Comercial", que lista só os impérios cujas rotas usam aquele posto (e quem já foi
  taxado/bloqueado ali). Para cada um: Livre / Pedágio / Bloqueado (botão Taxar → Bloquear → Liberar). O último
  cartão, "Todos os seus postos", copia as escolhas para todos os seus territórios. Na diplomacia, aba Comércio, o
  bloco "Seus Postos Comerciais" deixa Livre / Taxar / Bloquear aquele império em todos os postos de uma vez.
  Com pedágio, cada rota compara pagar com desviar (manutenção extra por território) e escolhe o mais barato. Bloqueio = rota não atravessa (desvia ou é destruída); pedágio = cobrança por turno por recurso, na
  sua moeda. A vítima ganha a reclamação "Bloqueio comercial" (aceitar a exigência retira o bloqueio; recusar abre
  caminho para guerra). A IA também bloqueia e taxa rivais. Opções em `[BloqueioComercial]` no .cfg.
  - **Preço do pedágio:** você define quanto cada império paga, por posto (botões `-`/`+` na linha dele no Posto
    Comercial) ou para todos os postos de uma vez (botões na aba Comércio da diplomacia). Sem preço definido vale o
    padrão (`PedagioPorRecurso` × era). A rota paga uma vez por dono, pelo posto mais caro dele no caminho.
  - **Prévia de desvio, na hora:** ao mudar o preço, o cartão mostra quantas rotas daquele império continuam passando
    e pagando e quantas desviam ("1 paga · 0 desviam" → "0 pagam · 1 desvia"). A dica diz qual rota e para onde ela
    vai (sai das suas terras ou passa por outro posto seu). O cálculo usa o próprio caminho do jogo, com um cenário
    "e se" (`Trade\TollPreview.cs`), sem mexer em nenhuma rota. Na diplomacia, a mesma prévia vale para o preço geral.
  - **Nações da IA de linguagem** escolhem a própria política comercial (`politica_comercial`: livre, pedágio com
    preço, bloqueio) contra cada império; a IA de regras (`Trade\TradeAi.cs`) só taxa quem usa os postos dela.
  - Comandos de teste: `jogo rotas`, `jogo pedagio …`, `trade previa …` (ver `docs\diplomacia-ia.md` §7).
  - Detalhes técnicos: `docs\bloqueio-comercial.md`.

## Diplomacia IA
- **Correio Diplomático** (botão com envelope na barra inferior, ao lado do Banco Central): tela cheia com Novas,
  A responder, Lidas, Respondidas, Enviadas, Comunicados públicos e Nações. Lá você lê, responde (a resposta fica
  ligada à carta) e escreve cartas às nações, ultimatos e declarações públicas, e pode recusar a correspondência de
  alguém. Selo vermelho = cartas novas.
- **Aba Cartas na diplomacia** de cada nação: a conversa com ela e o campo de escrever.
- **Espionagem:** espiões ocultos no território de outra nação interceptam cartas privadas, e a carta interceptada
  nunca chega. As suas aparecem na aba **Cartas** da janela de espionagem (Inteligência), com um selo no botão.
- Cada nação do computador pensa uma vez por turno: lê um dossiê com só o que ela sabe, escreve diário secreto,
  atualiza sentimentos e memória, e manda cartas (para outras IAs e para você).
- **As ações dela viram ordens do jogo:**
  - guerra, paz, acordos e aliança;
  - rendição (oferecer, impor quando o inimigo zera o apoio à guerra, responder), com os termos e preços do jogo;
  - reclamações e exigências;
  - presentes (dinheiro, influência, cidades, exércitos) e cessão de território;
  - patrocínio e tratados com povos independentes (inclusive vassalo e anexação);
  - ordens a exércitos (mover, atacar, defender, parar), com a IA nativa travada no exército até cumprir;
  - política comercial (livre, pedágio com preço, bloqueio) contra cada império;
  - **Congresso mundial** (DLC Together We Rule): propor leis quando preside, votar e subornar votos, abrir crises
    internacionais, cumprir ou desafiar um veredito (guerra surpresa) e contribuir para o consenso ideológico. A IA
    nativa fica travada nessas votações; quando a nação não decide a tempo, a IA nativa volta a decidir por ela;
  - renomear.

  Postura e foco guiam a IA nativa. Cada nação tem um conselho de 12 ministros que opina no dossiê, e o líder pode
  demitir.
- **Seu conselho** (botão com três bustos, ao lado do correio): a cada turno a Mão abre a pauta e os ministros das
  pastas mais urgentes falam sobre o seu reino. A tela abre sozinha com o mapa livre. Você responde, eles reagem e o
  apreço deles por você muda. Na aba Ministros você demite quem não serve mais.
- **F10** abre o visualizador no navegador (`http://localhost:8765/`): cartas do mundo, diários, dossiês, prompts,
  respostas e custos; dá para escrever cartas para as nações por lá.
- Comandos de dev: `ia status`, `ia dossie <n>`, `ia agora [n]`, `ia carta <n> <texto>`... em `_Modding\dev\cmd.txt`.
- Detalhes: `docs\diplomacia-ia.md`. Design: `docs\design-diplomacia-ia.md`.

## Distribuir
`powershell -ExecutionPolicy Bypass -File _Modding\tools\empacotar.ps1` gera `_Modding\dist\HumankindMod_<versão>.zip`
(carregador + núcleo + LEIA-ME), compilando sem instalar. Instruções para quem recebe: `docs\instalacao.md`.

## Backups
Ficam em `Documentos\HumankindModding\backups\` (fora da pasta da Steam).

## Repositório e versões
- Repositório privado: `github.com/lcaresia/realpolitik`. A raiz é esta pasta (`_Modding`).
- Use o Git do **WSL**, não o do Windows: `wsl -e bash -c "cd '/mnt/c/Program Files (x86)/Steam/steamapps/common/Humankind/_Modding' && git status"`.
- O `.gitignore` deixa de fora o `.env` e as chaves, o `decompiled\` (código do jogo), o `dev\`, o `dist\`, o `bin`/`obj` e os dados do `ia-bench`.
- Cada versão publicada recebe uma tag `vX.Y.Z` (ex.: `v1.0.0`) no commit que gerou o pacote. Versões de teste usam o sufixo `-beta.N`.
- O número de versão do produto (Realpolitik) é separado do número interno das DLLs.

## MoreEmpires: até 16 impérios em qualquer tamanho de mapa
Plugin BepInEx **separado** do CurrencyMod. Ele faz cinco coisas:
- deixa o lobby aceitar de 2 a 16 impérios (jogador + IA) em todos os tamanhos de mapa;
- gera as cores dos impérios 13 a 16;
- recicla personas quando elas acabam;
- valida os pontos iniciais dos mapas procedurais;
- sorteia o ponto de nascimento em mapas customizados.

Detalhes, fontes e dados: `docs\pesquisa-30-jogadores.md`, seção "Etapa 1". Estado: **testado dentro do jogo com
16 impérios** (mapas Tiny, Normal e Huge). O ajuste do banner (`AjustarBanner`) trava o jogo e fica desligado. Mais de
16 impérios (o plano dos 30) não será feito.

**Pastas:**

| Pasta | O que tem |
|---|---|
| `src\MoreEmpires\` | O plugin. `Core\` tem config, log, canal de comandos e os algoritmos puros; `Features\` tem um arquivo por tarefa. |
| `src\MoreEmpires\Tests\` | Testes fora do jogo e o leitor das opções do bundle. Não entram na DLL. |
| `tools\instalar-moreempires.ps1` | Instalar, desligar e empacotar. |

**Instalado em:** `BepInEx\plugins\MoreEmpires\MoreEmpires.dll`. É um plugin comum, sem recarga a quente: qualquer mudança exige reiniciar o jogo.

**Compilar sem instalar:**
```
"C:\Program Files\dotnet\dotnet.exe" build _Modding\src\MoreEmpires -c Release
```

**Instalar nesta máquina** (com o jogo fechado):
```
powershell -ExecutionPolicy Bypass -File _Modding\tools\instalar-moreempires.ps1
```
Compila com `-p:Deploy=true`; sem esse parâmetro, o build não copia nada para o BepInEx.

**Desligar:**
```
powershell -ExecutionPolicy Bypass -File _Modding\tools\instalar-moreempires.ps1 -Desinstalar
```
Renomeia a DLL para `.desligado`; não apaga nada.

**Testes fora do jogo** (visibilidade e cores):
```
"C:\Program Files\dotnet\dotnet.exe" run --project _Modding\src\MoreEmpires\Tests -c Release
```

**Instalar em outra máquina:**
1. Instale o BepInEx 5 x64 na pasta do jogo.
2. Gere o pacote:
   ```
   powershell -ExecutionPolicy Bypass -File _Modding\tools\instalar-moreempires.ps1 -Pacote
   ```
   O resultado é `dist\MoreEmpires_<versão>.zip`, com `BepInEx\plugins\MoreEmpires\MoreEmpires.dll`, `LEIA-ME.txt` e o documento de detalhes.
3. Copie a pasta `BepInEx` do pacote para a pasta do jogo e abra o jogo.

O MoreEmpires não depende do CurrencyMod nem de chave de API. Saves com mais de 10 impérios só abrem com o mod instalado.

**Configuração.** Fica em `BepInEx\config\lucas.humankind.moreempires.cfg`, criado no primeiro início.

| Chave | Valores | Efeito |
|---|---|---|
| `[Geral] MaxImperios` | 2..16 | 10 = jogo original |
| `[Nascimento] Modo` | `Embaralhar` / `QualquerPonto` / `Original` | Ponto de nascimento em mapas customizados |
| `[Interface] CompactarLobby`, `AjustarBanner` | true/false | Reduzir listas que não cabem |
| `[Cores] PaletaExtra` | texto | Cores 13 a 16 salvas pela tela de cores |
| `[Mapa] AjusteAutomatico` | true/false | Regerar o mapa se faltar ponto inicial |
| `[Desempenho] VisibilidadeRapida` | `Auto` / `Sempre` / `Nunca` | Cálculo da visão compartilhada |
| `[Desempenho] MedirTurnos` | true/false | Medir a passagem de turno (`BepInEx\MoreEmpires\turnos.csv`) |

**Comandos de desenvolvimento.** O canal é próprio, separado do `dev\cmd.txt` do CurrencyMod. Escreva em `_Modding\dev\moreempires_cmd.txt` e leia em `_Modding\dev\out\moreempires.txt`. Comandos: `status`, `opcoes`, `cores`, `nascimento`, `mapa`, `turnos [n]`, `visao`, `imperios`, `lobby`, `ui`, `screenshot <nome>`.
