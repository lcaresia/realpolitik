# Prompt: provedores de IA (login e chave de API) + tela no menu principal

> Prompt independente para uma sessão nova do Claude Code, aberta na pasta do Humankind. Cole o conteúdo abaixo da linha.

---

## Objetivo

Hoje a Diplomacia IA só fala com o DeepSeek, com a chave num arquivo (`BepInEx\config\deepseek.key`) e o resto no `.cfg`.
O mod vai ser vendido para jogadores de fora (veja a memória `installable-goal.md`). Quem comprar tem que conseguir
ligar a IA **sem editar arquivo nenhum**, numa **tela no menu principal** com a interface nativa do jogo: fácil, bonita
e igual às telas do Humankind.

Provedores a implementar:

| # | Provedor | Como conecta | Situação |
|---|---|---|---|
| 1 | **OpenAI: "Sign in with ChatGPT"** | Login OAuth com a conta do ChatGPT; o uso desconta da cota do Plus/Pro do jogador | Oficial desde o DevDay de 29/09/2026, só para apps aprovados no programa de parceiros. **O lucas precisa se inscrever** |
| 2 | **xAI: login do SuperGrok / X Premium+** | Login OAuth com a conta da xAI; usa a cota da assinatura | Só para apps numa lista de liberados. **O lucas precisa pedir a liberação** |
| 3 | **OpenRouter: login com a conta** | OAuth (PKCE) que devolve uma chave do próprio jogador; usa os créditos dele; dá acesso a Claude, GPT, Gemini, Grok, DeepSeek, GLM… | Não precisa de aprovação: **dá para fazer já** |
| 4 | **Chave de API** | O jogador cola a chave uma vez | DeepSeek (o de hoje), OpenAI, Anthropic (Claude), Google Gemini (AI Studio, com nível grátis), xAI, GLM (Zhipu/Z.ai), OpenRouter |
| 5 | **Modelo local** | Sem conta e sem chave: Ollama ou LM Studio no próprio PC | Grátis; precisa de placa de vídeo boa |

**Proibido (não implementar, nem como opção escondida):**
- login com a assinatura do **Claude Pro/Max**: a Anthropic bloqueia apps de fora desde abril de 2026 e proíbe nos termos;
- login com a assinatura do **Google AI Pro/Ultra** ou do Gemini CLI/Antigravity: o Google bane as contas que fazem isso, sem reembolso;
- usar o identificador de outro app (por exemplo o do Codex CLI) para fingir ser ele. Login só com o identificador próprio do mod, depois de aprovado.

O usuário fala português (pt-BR, informal). Responda em pt-BR, curto e claro. A sua memória do projeto (MEMORY.md) já
vem carregada: leia `installable-goal.md`, `llm-diplomacy-idea.md`, `translation-status.md`, `native-ui-research.md` e
`feedback-workflow.md` antes de começar.

## O que existe hoje (leia antes)

- **Cliente da API:** `src\CurrencyMod\Diplomacia\Llm\LlmClient.cs`. É genérico, no formato chat/completions da OpenAI,
  com `response_format: json_object`. Testado só com o DeepSeek.
- **Chave:** `Diplomacia\Llm\ApiKey.cs` lê `BepInEx\config\deepseek.key`.
- **Configuração:** `Diplomacia\IaConfig.cs`, seção `[IA]`: `Endpoint`, `Modelo`, `Raciocinio`, preços por milhão de
  tokens (`PriceInputCacheHit/Miss`, `PriceOutput`, horário de desconto), `TetoGastoPartidaUSD`, `MaxNacoesPorTurno`,
  `ChamadasParalelas`, `IdiomaDasCartas`.
- **Onde a IA é chamada:**
  - `IaJobs.cs`: as nações;
  - `Council\PlayerCouncil.cs`: o conselho do jogador;
  - o medidor de gasto e o teto, em `IaModule`.
- **Telas nativas:**
  - o guia é `docs\guia-telas-nativas.md`, com o kit `NativeUI\NativeUIKit.cs`, os doadores e as armadilhas;
  - exemplos prontos: `NativeUI\NativeBankWindow.cs` (janela lateral) e `Diplomacia\UI\MailScreen.cs` /
    `CouncilScreen.cs` (tela cheia clonada da tela de Configurações).
  - Todas são **dentro da partida**. A tela nova é no **menu principal, fora da partida**: procure um doador lá (por exemplo
    a tela de opções ou a de mods do menu) e documente no guia.
- **Tradução:** `L.cs` e `docs\traducao.md`. Todo texto novo entra com `L.T`, `L.F` ou `L.N` e é traduzido para en, es,
  fr e de com os scripts `tools\dev\extrair-textos.ps1` e `validar-traducao.ps1`.
- **Recarga a quente:** só o núcleo (`src\CurrencyMod`) recarrega com o jogo aberto. O carregador exige reiniciar o jogo.

## Fase 0: pesquisa (sem código)

Antes de escrever código, confira na documentação **oficial** de cada provedor e anote em `research\provedores-ia.md`:

1. **OpenAI, "Sign in with ChatGPT":**
   - como o app se inscreve no programa de parceiros e o que a OpenAI pede (nome, descrição, URLs de retorno, política de privacidade…);
   - o fluxo de login: endereços de autorização e de token, escopos, PKCE, retorno em `127.0.0.1` ou código de dispositivo;
   - a renovação do token;
   - qual API o token chama (Responses ou chat/completions), os modelos permitidos e como o limite por parceiro aparece (erros, cabeçalhos).
2. **xAI, OAuth do SuperGrok:**
   - como pedir para entrar na lista de apps liberados;
   - o fluxo (PKCE com retorno local ou código de dispositivo), os escopos, a API chamada e os modelos;
   - o erro que vem quando a assinatura não dá direito (há relatos de HTTP 403 em alguns planos).
3. **OpenRouter, login OAuth PKCE:**
   - o fluxo (`/auth` → código → troca pela chave do usuário);
   - limites de crédito, o endpoint compatível com a OpenAI e o suporte a `response_format` por modelo;
   - os cabeçalhos de identificação do app (`HTTP-Referer`, `X-Title`).
4. **APIs por chave:**
   - endereço e formato de cada uma: DeepSeek, OpenAI, Anthropic (Messages API nativa, que não tem `json_object`), Gemini
     (endpoint compatível com a OpenAI do AI Studio, e os limites do nível grátis), xAI e GLM;
   - preços atuais dos modelos baratos de cada uma.
5. **Local:** endpoints compatíveis com a OpenAI do Ollama (`localhost:11434`) e do LM Studio (`localhost:1234`); como
   listar os modelos instalados.

Mostre ao usuário um resumo curto: o que cada um exige, o que depende de aprovação e o que dá para fazer já.

## Fase 1: conversa (sem gerar nada)

O usuário quer discutir o design antes de uma funcionalidade grande e, depois de aprovado, quer tudo de uma vez.
Mostre, curto:

1. **Rascunho da tela** (texto ou um mock em HTML, se ajudar). Proposta:
   - **Entrada:** um botão no menu principal, "Diplomacia IA" / "AI Diplomacy", no estilo dos botões nativos. A mesma tela
     também abre de dentro da partida (pelo menu de pausa ou pelo F10), se não der muito trabalho.
   - **Esquerda:** a lista de provedores, cada um com um chip de estado: Conectado, Sem chave, Aguardando aprovação, Não
     encontrado (local). O provedor em uso fica marcado.
   - **Centro:** o cartão do provedor escolhido:
     - o botão **Entrar com…** (login) ou o campo da **chave** (mascarada, com colar e mostrar);
     - o seletor de **modelo**, com o recomendado marcado e o preço por turno estimado para 16 impérios;
     - o botão **Testar conexão**, que faz uma chamada mínima e confere o json;
     - **Desconectar** / **Apagar chave**;
     - um link "Como conseguir uma chave", que abre o navegador na página oficial;
     - uma nota curta de privacidade: o que sai do PC e para onde.
   - **Direita:** um resumo do que está em uso:
     - provedor e modelo;
     - teto de gasto por partida (com cota em vez de dólar nos provedores por assinatura);
     - nações por turno;
     - idioma das cartas;
     - um provedor reserva para quando a cota acabar.
2. **Comportamento quando falta crédito ou cota** (erro 429, limite do parceiro):
   - usar o provedor reserva, se houver;
   - se não houver, devolver as decisões à IA nativa do jogo até voltar, avisando o jogador uma vez;
   - nunca travar o turno.
3. **Pendências do lucas:**
   - inscrição na OpenAI e pedido à xAI. Prepare os textos dos pedidos em inglês, e ele envia;
   - política de privacidade e página do produto, se os pedidos exigirem.

   Enquanto não houver aprovação, esses dois provedores aparecem como "Aguardando aprovação". O identificador do app
   fica no `.cfg`, vazio por padrão.

Use o AskUserQuestion se estiver disponível, em blocos pequenos e com a sua recomendação. Só passe para a fase 2 quando o
usuário disser que pode fazer.

## Fase 2: implementação

1. **Backup:** `_Modding\tools\dev\backup.ps1`.
2. **Camada de provedores** (`Diplomacia\Llm\Providers\`):
   - **Interface comum:**
     - id, nome, tipo de acesso (login, chave, local);
     - endpoint e modelos (com o recomendado);
     - preço por milhão de tokens, ou "por assinatura";
     - `Send(request)` devolvendo o mesmo `ChatResult` de hoje: texto, raciocínio, tokens e custo.
   - **Adaptadores de formato:**
     - chat/completions (OpenAI, DeepSeek, xAI, GLM, OpenRouter, Gemini compatível, local);
     - Messages da Anthropic: sem `json_object`, então o pedido de json vai no prompt, com a resposta começando em `{`;
     - Responses, se o login do ChatGPT exigir.
   - **Quem não aceita `json_object`:** o adaptador pede o json no texto e o `DecisionParser` continua igual.
   - **O resto do mod não muda:** as nações e o conselho chamam a camada nova no lugar do `LlmClient` direto.
3. **Credenciais (segurança):**
   - **Onde ficam:** um arquivo por provedor em `BepInEx\config\credenciais\`, **criptografado pelo Windows (DPAPI,
     usuário atual)**. Só abre no mesmo usuário do mesmo PC.
   - **Migração:** o `deepseek.key` atual é importado uma vez, e o arquivo antigo continua funcionando.
   - **Nunca expor:** chave e token não aparecem em log, visualizador F10, save, backup, pacote nem chat.
     - Na tela, aparecem só os 4 últimos caracteres.
     - O `empacotar.ps1` e o `backup.ps1` passam a recusar a pasta `credenciais`.
   - **Login OAuth:**
     - PKCE com `state` aleatório;
     - retorno só em `127.0.0.1`, numa porta livre, com `HttpListener`;
     - o navegador abre com `Application.OpenURL`;
     - tempo limite de 5 minutos e cancelar a qualquer momento;
     - a página de retorno diz "pode voltar ao jogo", traduzida;
     - o token se renova sozinho;
     - código de dispositivo como alternativa, se o provedor oferecer.
   - **Campos de texto da tela:** `NativeUIKit.BlockGameShortcuts` (digitar a chave não pode acionar atalhos do jogo).
4. **Configuração:**
   - Provedor, modelo e reserva vão para o `.cfg`, editáveis pela tela. Nada de chave no `.cfg`.
   - Os preços saem de uma tabela no código por provedor/modelo, que o `.cfg` pode sobrescrever.
   - O medidor de gasto e o teto passam a usar o preço do modelo em uso.
5. **A tela:**
   - interface nativa clonada (guia de telas nativas), com dica curta em todo chip, número e botão (`Tip`);
   - ESC fecha;
   - textos com `L.T`/`L.F` e traduzidos nos 5 idiomas;
   - tudo funciona fora da partida (no menu principal não há `Sandbox`; cuidado com o que o código atual supõe).
6. **Documentação:**
   - `docs\diplomacia-ia.md`: a seção de configuração e uma seção nova "Provedores";
   - `docs\instalacao.md`: o passo a passo do jogador com a tela nova, em vez de editar arquivos;
   - `README.md` e o guia de telas nativas: o doador do menu principal;
   - `docs\traducao.md`, se houver novidade.

## Testes

- **Sempre numa partida de 16 impérios.** A de teste é a "Teste16", guid b57b0e07. Para começar uma nova: `jogo novo 16 Normal`.
- **Instalação:**
  - nunca instale o núcleo com nações pensando: antes, `ia status` tem que dizer "pensando agora: 0";
  - compile com `-p:SkipDeploy=true` enquanto elas pensam.
- **Testes sem custo:**
  - a tela no menu principal e dentro da partida, com prints em pt e em en, de/es/fr (procure texto cortado);
  - "Testar conexão" com chave errada (mensagem clara) e sem internet;
  - o modelo local, se o Ollama estiver instalado. Se não estiver, pergunte antes de instalar qualquer coisa.
- **Testes com custo ou conta do usuário:** só com permissão explícita, um provedor de cada vez. Para cada um, passe
  **um** turno e anote no `research\provedores-ia.md`:
  - custo e tempo do turno;
  - erros de json;
  - qualidade das cartas.

  A chave DeepSeek atual pode ser usada como sempre.
- **Login do ChatGPT e do Grok:** só depois da aprovação. Antes disso, teste o fluxo até o ponto em que ele para por falta
  do identificador, com a mensagem "Aguardando aprovação".
- Mande os prints ao usuário conforme for avançando (SendUserFile).
- No fim, deixe o `.cfg` e o provedor do usuário exatamente como estavam (DeepSeek), e confira que o jogo dele abre como antes.

## Regras que não se quebram

- Chave e token nunca aparecem em log, print, chat, backup ou pacote. Se um print mostrar a chave, apague o print.
- Nada sai deste PC sem permissão explícita: inscrições, pedidos de liberação, publicar, enviar. Os pedidos à OpenAI e à
  xAI são do lucas; você só prepara os textos.
- Nada de login com assinatura da Anthropic ou do Google, nem identificador de outro app.
- Não mude balanceamento nem comportamento das nações sem o usuário pedir. Bug achado no caminho: anote e pergunte.
- Não rode esta sessão ao mesmo tempo que outra que mexa no mesmo código ou no mesmo jogo.
- Faça backup antes de começar e no fim.
