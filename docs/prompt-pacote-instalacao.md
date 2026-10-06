> **SUBSTITUÍDO (2026-10-06)** por `docs\prompt-instalador.md` (instalador .exe para venda, loja própria).

# Prompt: pacote para outros jogadores instalarem os mods

> Prompt independente para uma sessão nova do Claude Code, aberta na pasta do Humankind. Cole o conteúdo abaixo da linha.

---

## Objetivo

Preparar os mods do lucas para **outras pessoas instalarem no próprio PC**: um pacote para baixar, com instruções
claras, que funcione numa máquina limpa (sem a pasta `_Modding`, sem a chave de API do lucas e sem o `.cfg` dele).

**Antes de gerar qualquer coisa, converse com o usuário.** Ele pediu isso explicitamente. Na primeira fase você só
lê e conversa: nada de mexer em código, scripts, documentos ou pacotes até ele dizer, com todas as letras, que pode
gerar.

O usuário fala português (pt-BR, informal). Responda em pt-BR, curto e claro. A sua memória do projeto (MEMORY.md) já
vem carregada; leia `installable-goal.md` e os arquivos de cada mod.

## O que existe hoje

- **Os mods:**
  - **CurrencyMod:** um carregador fixo mais o núcleo. O núcleo tem moeda própria por império (inflação, juros,
    câmbio, Banco Central), bloqueio comercial e pedágio, e a Diplomacia IA (nações guiadas pelo DeepSeek: cartas,
    ações de verdade, conselho, Congresso, visualizador F10).
  - **MoreEmpires:** plugin separado que permite até 16 impérios.
- **`tools\empacotar.ps1`:** compila sem instalar e gera `_Modding\dist\HumankindMod_<versão>.zip` com:
  - o carregador e o núcleo;
  - `LEIA-ME.md` (cópia de `docs\instalacao.md`) e `Diplomacia-IA.md`.

  Ele se recusa a empacotar se achar um arquivo de chave ou uma chave de API dentro dos arquivos. Não leva o BepInEx
  nem o MoreEmpires.
- **`tools\instalar-moreempires.ps1 -Pacote`:** gera `dist\MoreEmpires_<versão>.zip`, separado.
- **`docs\instalacao.md`:** o guia de quem recebe. Cobre:
  - BepInEx 5.4.23.5 x64;
  - a chave do DeepSeek;
  - como conferir que funcionou;
  - configuração;
  - custos e privacidade;
  - como desinstalar;
  - problemas comuns.
- **Versões:** `Plugin.cs` diz 1.1.0. Testado com Humankind 1.31.4836 e BepInEx 5.4.23.5.
- **Teste:** nenhum pacote foi testado numa instalação limpa até hoje.
- **Documentação para conferir:**
  - `_Modding\README.md`;
  - `docs\diplomacia-ia.md` (§5 configuração);
  - `docs\bloqueio-comercial.md`;
  - `docs\proposta-ciclo-economico.md`;
  - `docs\pesquisa-30-jogadores.md` (MoreEmpires).

## Fase 1: conversa (sem gerar nada)

1. Leia os scripts e documentos acima, e confira o que o pacote atual levaria.
2. Mostre ao usuário, curto:
   - como o pacote ficaria: a árvore de pastas do zip;
   - o passo a passo de quem recebe, do download até a primeira partida;
   - o que cada jogador precisa ter (jogo, DLC, conta no DeepSeek).
3. Leve os pontos abaixo, cada um com a sua recomendação. Pergunte em blocos pequenos, com opções, e use o
   AskUserQuestion se estiver disponível. Não despeje tudo de uma vez.
4. Só passe para a fase 2 quando o usuário disser claramente que pode gerar.

### Pontos para conversar

1. **Para quem é:**
   - O mod inteiro fala pt-BR: a interface é escrita no código e as nações da IA escrevem em português.
   - Pacote só para quem fala português, ou traduzir? Traduzir é um trabalho grande, separado (centenas de textos
     mais os prompts da IA).
   - Recomendação: v1 só em pt-BR.
2. **Formato:**
   - Um pacote só com tudo, ou separados (moeda e diplomacia num, MoreEmpires noutro)?
   - Levar o BepInEx dentro do zip? A licença LGPL permite redistribuir, e o jogador só descompactaria. Ou mandar
     baixar à parte, como hoje?
   - Instalador automático (um `.bat`/`.ps1` que acha a pasta do jogo pela Steam e copia tudo) ou só instruções?
   - Recomendação: um zip com o BepInEx dentro e um instalador simples, com o MoreEmpires como parte opcional.
3. **A IA de linguagem para quem instala:**
   - Cada jogador precisa da própria chave do DeepSeek, com crédito.
   - Opções para o pacote: vir ligada mas parada até a pessoa pôr a chave (como hoje: "sem chave"); vir desligada;
     ou documentar o uso de outro serviço no formato da OpenAI (o endpoint e o modelo já são configuráveis).
   - Recomendação: ligada e esperando a chave, com um passo a passo da chave bem claro.
4. **Custo e teto de gasto (importante):**
   - Medido em 2026-10-05: com 16 impérios (15 nações da IA), cerca de **US$ 0,12 por turno**. Na partida de teste,
     de US$ 0,52 no T68 para US$ 1,46 no T76.
   - O teto padrão (`[IA] TetoGastoPartidaUSD` = 5) para a IA depois de uns 40 turnos de partida de 16 (os primeiros
     turnos devem sair mais baratos, com dossiês menores; confira). Uma partida longa sairia na casa das dezenas de
     dólares.
   - O LEIA-ME hoje fala em ~US$ 0,025 por turno, que era o número com 9 nações fora do pico.
   - Decidir: o teto padrão; uma sugestão para 16 impérios (`MaxNacoesPorTurno`, `ChamadasParalelas`); e o que o
     LEIA-ME promete de custo.
   - Não mude valores sem o usuário escolher.
5. **Configuração padrão:**
   - O pacote não leva `.cfg`: ele nasce com os padrões do código na primeira vez que o jogo abre.
   - Confirmar com o usuário os padrões que importam para quem instala, e que o `.cfg` dele não entra. O dele tem
     escolhas pessoais, como `ExpansaoEmSaveAntigo = true`, cujo padrão é false.
6. **DLC e versão do jogo:**
   - O Congresso só existe com a DLC Together We Rule.
   - O resto funciona sem DLC (confirmar lendo o código).
   - Quando o jogo atualizar, cada patch que falhar desliga só a parte dele e vai para o log.
   - Recomendação: documentar a versão testada e esses comportamentos.
7. **Saves:**
   - Saves feitos com o mod abrem sem ele: os dados extras são ignorados.
   - Saves com mais de 10 impérios só abrem com o MoreEmpires.
   - Recomendação: avisar no LEIA-ME e no instalador.
8. **Versão e histórico:** subir de 1.1.0 para qual versão? Escrever um histórico de mudanças em pt-BR para quem
   recebe?
9. **Créditos, licença e aviso:**
   - Créditos: o lucas; BepInEx, Harmony e Mono.Cecil, que o carregador usa.
   - Aviso de que não é afiliado à Amplitude nem à SEGA.
   - Licença do mod (MIT? só uso pessoal?).
   - O código descompilado do jogo **nunca** vai no pacote.
10. **O que fica de fora ou desligado para o jogador:**
    - o canal de comandos de desenvolvimento (só funciona com a pasta `_Modding`);
    - a recarga a quente do núcleo (inofensiva?);
    - os logs da IA (`[IA] SalvarLogs`, ligado por padrão);
    - o visualizador F10 (só aceita conexões do próprio PC).

    Decidir o padrão de cada um.
11. **Como testar sem estragar a instalação de desenvolvimento do lucas:**
    - Opções: guardar a pasta `BepInEx` atual, instalar o pacote limpo, testar e devolver tudo como estava; ou testar
      em outro PC.
    - Recomendação: a primeira, com backup completo antes e conferência depois.
12. **Desinstalar e atualizar:** um desinstalador junto? Atualizar = trocar os arquivos do mod, mantendo `.cfg` e chave.
13. **Ordem com a revisão de bugs:**
    - Existe um prompt de revisão de bugs (`docs\prompt-revisao-bugs.md`).
    - O ideal é rodar a revisão antes de empacotar, para o pacote sair com as correções.
    - Pergunte se ela já foi feita.

## Fase 2: depois do "pode gerar"

1. Faça backup (`_Modding\tools\dev\backup.ps1`).
2. Registre as decisões: para quem instala, em `docs\instalacao.md`; para quem desenvolve, no `README.md`.
3. Ajuste os scripts de empacotamento, o instalador e a documentação conforme o combinado. Mexa no código do mod só
   se for preciso para o pacote, e explique cada mudança.
4. Confira o conteúdo do pacote, arquivo por arquivo:
   - **Não pode entrar nada da máquina do lucas:** a chave, o `.cfg`, saves, logs, a memória do Claude, a pasta
     `_Modding`, o código descompilado e as DLLs do jogo.
   - **Caminhos e nomes desta máquina:** procure no código e nos textos por `C:\`, `lucas` e `Steam\steamapps`.
   - **A trava de chave do `empacotar.ps1`** tem que continuar.
5. **Teste a instalação limpa** do jeito combinado, numa **partida de 16 impérios**. Confira:
   - o log com "Núcleo carregado";
   - Banco Central (F8), correio, conselho, posto comercial e o painel da diplomacia;
   - a situação "sem chave" no F10;
   - o MoreEmpires com 16;
   - salvar e carregar.

   Testar a IA de linguagem com a chave do usuário só se ele permitir. Mande os prints.
6. Devolva a instalação de desenvolvimento do lucas exatamente como estava (BepInEx, `.cfg`, chave, plugins) e
   confira que o jogo dele abre como antes.
7. Entregue:
   - o zip em `_Modding\dist`, com o tamanho e a lista de arquivos;
   - o LEIA-ME final;
   - o que ficou pendente.

   **Não publique em lugar nenhum.** Subir o arquivo (Nexus, GitHub, Drive…) é com o usuário, ou só com permissão
   explícita para o destino que ele indicar.

## Compilar e testar

- .NET SDK 8 em `C:\Program Files\dotnet` (ponha no PATH do terminal).
- **Núcleo:** `dotnet build _Modding\src\CurrencyMod -c Release` compila e instala. Com o jogo aberto, ele recarrega
  sozinho em ~15 s. Com `-p:SkipDeploy=true` só compila.
- **Carregador e MoreEmpires:** só com o jogo fechado.
- **Canal de teste com o jogo aberto:** `_Modding\tools\dev\devcmd.ps1 -Commands @("ia status", "jogo estado")`.
  Os comandos estão em `docs\diplomacia-ia.md` §7 e `docs\guia-telas-nativas.md` §1. Os prints saem com
  `screenshot <nome>` em `dev\out\`.
- Para uma partida nova: `jogo novo 16 Normal` (início rápido). A partida de teste existente é "Teste16 T76
  congresso".

## Regras que não se quebram

- A chave da API fica só em `BepInEx\config\deepseek.key`. Nunca a mostre, logue, copie para backup ou pacote, nem a
  cole no chat.
- Nunca instale o núcleo com as nações pensando: antes, `ia status` tem que dizer "pensando agora: 0".
- Esta sessão é de pacote e documentação: **não mude funcionalidades nem balanceamento** sem o usuário pedir. Bug
  achado no caminho: anote e pergunte.
- Não rode esta sessão ao mesmo tempo que outra que mexa no mesmo código ou no mesmo jogo.
- Nada que saia deste PC (publicar, enviar, subir) sem permissão explícita.
- Faça backup antes de começar e no fim.
