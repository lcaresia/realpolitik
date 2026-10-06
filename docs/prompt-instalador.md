# Prompt: pacote de instalação completo (Setup.exe + zip) do Realpolitik

> Prompt independente para uma sessão nova do Claude Code, aberta na pasta do Humankind. Cole tudo abaixo da linha.
> Substitui `prompt-pacote-instalacao.md` (2026-10-05), que é de antes da decisão de vender.

---

## Missão

Construir o **pacote de instalação que os compradores vão usar** para o mod "Realpolitik: Living Nations for
HUMANKIND":
- um **`Setup.exe`** (Inno Setup);
- um **zip manual** (Steam Deck e plano B);
- os **scripts que geram os dois do zero, de forma repetível**.

Depois, provar com testes que funciona.

**Este é o passo mais crítico do produto.** Quem compra e não consegue instalar pede reembolso e não volta. Por isso:
- qualidade e verificação valem mais que velocidade;
- nenhum item "pronto" sem evidência (log, print, saída de script);
- na dúvida entre "provavelmente funciona" e "testei", teste.

O usuário (lucas) fala pt-BR informal. Responda em pt-BR, curto e claro. A memória do projeto já vem carregada: leia
`installable-goal.md`, `feedback-workflow.md`, `translation-status.md`, `ai-providers.md` e
`thirty-empires-research.md` antes de começar.

## Leitura obrigatória antes de falar com o usuário

1. `_Modding\README.md` (mapa do projeto).
2. `_Modding\docs\pesquisa-instalador-exe.md`: a pesquisa completa do instalador. Pontos 1–12, plano de implementação
   e "Não confirmado". **Ela é a base técnica deste trabalho.** Mas a parte da loja (Lemon Squeezy) está superada pela
   loja própria, abaixo.
3. `_Modding\docs\loja.md`: a loja própria (Stripe + Cloudflare Worker), as licenças e o `publicar-versao.ps1`.
4. `_Modding\docs\integracao-licenca.md`: como a chave de licença entra no jogo. Pode estar sendo feita em outra
   sessão: veja "Dependências".
5. `_Modding\docs\instalacao.md`: o guia atual de quem instala, hoje manual e em pt-BR.
6. `_Modding\tools\empacotar.ps1` e `_Modding\tools\instalar-moreempires.ps1`: os empacotadores atuais, com a trava de
   chaves.
7. O site: `_Modding\site\live\install.html`, mais o FAQ e os requisitos em `index.html`. Eles prometem coisas ao
   comprador, e o pacote tem que cumprir:
   - "Windows · Steam · Single-player";
   - "Epic: not tested yet";
   - "Steam Deck: experimental";
   - desinstalar "apagando duas pastas";
   - "An installer is in preparation".

## O que já está decidido (não reabrir)

| Tema | Decisão |
|---|---|
| Modelo | Venda única de US$ 10 para o mundo todo, pela loja própria. Sem DRM. A chave libera só a Diplomacia IA; sem chave, todo o resto funciona |
| Formato | **Inno Setup 6** `Setup.exe` + **zip manual** com o mesmo conteúdo |
| Idiomas | Instalador e textos de quem instala em **en, pt-BR, es, fr, de**, com o idioma do Windows por padrão. O mod já tem a interface nesses 5 |
| Plataformas | **Steam/Windows** suportado. Xbox/Game Pass e Mac **não**: avisar e não instalar. Steam Deck pelo zip, "experimental", com a opção `WINEDLLOVERRIDES="winhttp=n,b" %command%` |
| BepInEx | Vai dentro, **oficial e sem alteração** (5.4.23.5 x64, a versão testada). É instalado **só se faltar**. Se já houver 5.4.x, mantém; se houver 6.x ou desconhecido, avisa e nunca troca em silêncio. Se o `doorstop_config.ini` estiver com `enabled=false`, oferece religar |
| UAC | `PrivilegesRequired=lowest` + `PrivilegesRequiredOverridesAllowed=dialog`. Na Steam padrão a pasta é gravável sem administrador; se não for, reabre elevado |
| Nunca tocar | `BepInEx\config\*.cfg`, `BepInEx\config\credenciais\`, `BepInEx\DiplomaciaIA\`, saves. Atualizar = instalar por cima, mantendo tudo isso |
| Desinstalar | Remove só o que instalou. O BepInEx sai só se foi o instalador que o pôs **e** não sobrou outro plugin. Pergunta "manter configurações e credenciais?" (padrão: manter) |
| Licenças | Pasta `licencas\` com a LGPL-2.1 (BepInEx, Doorstop) e MIT (HarmonyX, MonoMod, Mono.Cecil), mais um `THIRD-PARTY.txt` com links das versões exatas. **Nada do jogo** vai junto (nem código descompilado, nem DLLs publicizadas). Os termos não podem proibir engenharia reversa de forma genérica (LGPL §6) |
| Segredos | O `.iss` e o zip só puxam de uma pasta de staging que passou pela trava de chaves. Nunca de `BepInEx\config`, `_Modding`, `.env`, `deepseek.key` ou `credenciais` |
| Antivírus | Sem UPX, sem auto-extração estranha. **Nada vai para o VirusTotal** sem permissão explícita: lá o arquivo fica acessível a terceiros e o mod é pago |
| Distribuição | Pela loja: `_Modding\loja\tools\publicar-versao.ps1` sobe para o R2. **Publicar é só com permissão explícita do usuário**, nesta sessão |
| Atualização | O jogo consulta `GET /api/version` do Worker e oferece o download (ver `integracao-licenca.md`). O novo Setup instala por cima |

## Fase 1: conversa curta (não gerar nada ainda)

O usuário pediu, em projetos anteriores, para **conversar antes de gerar**. Nesta fase só leia, confira e converse.

1. Confira o estado real e mostre um resumo curto:
   - O que o `empacotar.ps1` levaria hoje: a árvore de arquivos. Ele compila? A trava de chaves funciona?
   - Versões atuais: `Plugin.cs` do núcleo diz 1.1.0, o MoreEmpires 1.0.0. Versão do `Humankind.exe` instalado.
   - A integração da licença já está no código? Procure a tela "Licença" e as chamadas ao Worker.
   - A revisão de bugs (`docs\prompt-revisao-bugs.md`) já foi feita? Procure o relatório dela; se não achar,
     pergunte.
   - Ferramentas: o Inno Setup está instalado (`ISCC.exe`)? O zip oficial do BepInEx 5.4.23.5 x64 está em algum
     lugar do PC?
   - Smart App Control neste PC: ler
     `HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState` (0 = desligado, 1 = ligado,
     2 = avaliação).
2. Mostre a **experiência do comprador**, do e-mail da Stripe até a primeira carta de uma nação, em 8–10 passos.
3. Leve **só as decisões em aberto** abaixo, cada uma com a sua recomendação. Use o AskUserQuestion, em blocos
   pequenos.
4. Peça permissão para os downloads, um por um, dizendo nome, origem e tamanho:
   - **Inno Setup 6.x** (jrsoftware.org);
   - **BepInEx_win_x64_5.4.23.5.zip** (GitHub oficial do BepInEx). Confira o SHA-256 com o dos arquivos já
     instalados neste PC: `BepInEx\core\BepInEx.dll` e `winhttp.dll` têm que bater byte a byte com o conteúdo do zip.
5. Só passe para a fase 2 quando o usuário disser claramente que pode gerar.

### Decisões em aberto

1. **Número da versão de lançamento.** Recomendação: **1.0.0** como versão do produto (instalador, site, manifest da
   loja). Os números internos dos plugins acompanham. Manter os **GUIDs** dos plugins: eles dão nome ao `.cfg` e podem
   estar nos saves, e trocar quebraria quem atualiza. Confira no código antes de afirmar.
2. **Nome visível.** "Realpolitik: Living Nations for HUMANKIND" no instalador, no Painel de Controle e no
   desinstalador. As pastas internas (`plugins\CurrencyMod`, `CurrencyModCore`) ficam como estão, porque mudar arrisca
   saves e configs. Confirmar.
3. **MoreEmpires (até 16 impérios).** Recomendação: componente **opcional e marcado por padrão**, porque o site vende
   "16 impérios". Explique na tela de componentes que saves com mais de 10 impérios só abrem com ele.
4. **Epic.** Recomendação: detectar a instalação da Epic e permitir instalar com o aviso "não testado ainda" (o site
   diz isso). Se for `C:\Program Files\Epic Games`, reabrir como administrador. Ou só Steam no lançamento?
5. **Assinatura de código.** Custa ~US$ 130–230/ano (Certum ou SSL.com, pessoa física), e a validação leva ~2
   semanas. Sem assinatura:
   - aparece a tela azul do SmartScreen;
   - com o **Smart App Control ligado, o Windows bloqueia sem opção de continuar**.

   Recomendação: deixar o pipeline pronto para assinar (`SignTool=` no `.iss` e `signtool` opcional no script) e
   lançar sem assinatura só se o usuário aceitar o risco. Nesse caso, a página de instalação ganha um print explicando
   o SmartScreen e o caminho do zip para quem tiver o SAC ligado. **A decisão é do usuário.**
6. **Teto de gasto da IA e o custo prometido.** Com 16 impérios custa ~US$ 0,12–0,14 por turno (DeepSeek, fora do
   pico), e o teto padrão de US$ 5 por partida dura ~40 turnos. Confira o padrão atual no código e o texto do site
   antes de propor. Não mude valores sem o usuário escolher.
7. **Provedor "ChatGPT pelo Codex".** Ele usa o Codex instalado no PC do jogador e não tem trava. Vai no pacote,
   escondido ou desligado por padrão? Recomendação: perguntar. Isso depende dos termos da OpenAI; o
   `ai-providers.md` tem o histórico.
8. **Coisas de desenvolvedor no pacote do comprador.** Confirme cada uma lendo o código:
   - o canal de comandos (`_Modding\dev\cmd.txt`) só pode existir com a pasta `_Modding`;
   - a recarga a quente do núcleo deve ser inofensiva;
   - `[IA] SalvarLogs`: ligado ou desligado por padrão para o comprador?
   - o visualizador F10 só aceita conexões do próprio PC;
   - o modo dev da licença: o comprador nunca pode ganhar a Diplomacia IA sem chave por acidente.
9. **Termos no instalador.** Recomendação: uma página com os termos curtos (o `terms.html` do site, em 5 idiomas) e
   o checkbox "aceito". Ou só o link?
10. **Atalhos e páginas no fim da instalação.** Recomendação:
    - ☑ "Abrir o HUMANKIND agora" (via `steam://rungameid/1124300`);
    - ☑ "Ler o guia rápido" (abre o `install.html` no idioma escolhido);
    - nenhum atalho na área de trabalho.
11. **Telemetria.** Recomendação: **nenhuma**. O instalador não fala com a internet.

## Dependências

- **Licença no jogo** (`integracao-licenca.md`). O pacote 1.0.0 que vai à venda **tem que** levar a tela de licença
  funcionando. Se ela ainda não existir, não junte as duas coisas nesta sessão. Combine com o usuário:
  - fazer o instalador agora, gerando versões `-beta` para teste;
  - ou esperar a outra sessão terminar.

  **Nunca rode esta sessão ao mesmo tempo que outra que compile o mesmo código ou use o jogo.**
- **Revisão de bugs.** O ideal é o pacote sair depois dela.

## Fase 2: construir (depois do "pode gerar")

0. Backup: `_Modding\tools\dev\backup.ps1`.

1. **Um comando que gera tudo:** `_Modding\tools\gerar-release.ps1 -Versao 1.0.0`. Ele tem que ser repetível e
   falhar alto. Passos:
   1. Compilar em Release, sem instalar no jogo (`dotnet build ... -p:SkipDeploy=true`). O .NET SDK 8 fica em
      `C:\Program Files\dotnet` e não está no PATH.
   2. Montar o **staging** em `_Modding\dist\staging\<versão>\` com a árvore exata que vai para o jogo:
      - os plugins;
      - o núcleo;
      - o BepInEx oficial, numa subpasta separada, só para o instalador decidir se instala;
      - as licenças;
      - os LEIA-ME.
   3. **Trava de segredos e de lixo.** Falha se achar:
      - `.env`, `*.key`, `credenciais`, `deepseek`, `sk-`, `sk_live`, `rk_live`, `whsec_`;
      - `cfat_`, `LOJA_` e padrões de token, procurados também **dentro das DLLs**, como texto;
      - caminhos desta máquina (`C:\Program Files (x86)\Steam`, `Users\lucas`, `_Modding`). Não procure só
        `lucas`: o GUID legítimo dos plugins (`lucas.humankind.currency`) tem essa palavra;
      - `.pdb`, a pasta `_Modding`, código descompilado ou DLLs do jogo.

      Use uma **lista branca** de arquivos permitidos, não só lista negra. Um arquivo que não estiver na lista
      branca é erro.
   4. Gerar um `MANIFEST.txt` com o caminho, o tamanho e o SHA-256 de cada arquivo.
   5. Compilar o `.iss` com `ISCC.exe`, gerando `dist\<versão>\Realpolitik_Setup_<versão>.exe`.
   6. Gerar o zip manual `dist\<versão>\Realpolitik_<versão>_manual.zip`. A árvore é pronta para extrair na pasta do
      jogo, e leva o BepInEx e o `LEIA-ME` com o passo a passo e a opção do Steam Deck.
   7. Imprimir um resumo: tamanhos, SHA-256, contagem de arquivos e o resultado da trava.

2. **O instalador** (`_Modding\installer\Realpolitik.iss`, com o Pascal Script em arquivos separados se ficar grande).
   Cobrir os pontos 1–3, 6 e 9 da pesquisa:
   - **Detecção do jogo:**
     1. Steam: registro, depois `libraryfolders.vdf`, depois `appmanifest_1124300.acf` e `installdir`. Conferir que
        `Humankind.exe` existe.
     2. Epic: manifests `*.item`, conforme a decisão 4.
     3. Xbox: só para avisar.
     4. "Procurar…" manual, que valida a pasta escolhida.
   - **Versão do jogo:** o FileVersion do `Humankind.exe`. Testada: `1.31.4836`. Se for diferente, aviso amarelo, sem
     bloquear.
   - **Jogo aberto:** se `Humankind.exe` estiver rodando, pedir para fechar (os arquivos ficam em uso).
   - **BepInEx:** a lógica de detecção e decisão da tabela acima, registrada no log da instalação.
   - **Componentes:** o mod (obrigatório) e o MoreEmpires (decisão 3).
   - **Por cima de uma instalação existente:** detectar a versão instalada e mostrar "atualizar de X para Y". Nunca
     tocar no que está na lista "Nunca tocar".
   - **Antigo para novo:** se existir uma instalação manual antiga (zip), o Setup tem que conseguir assumir sem
     duplicar nada.
   - **Desinstalador:** a regra da tabela, mais a pergunta de manter as configurações. Deixa registro do que fez.
   - **Idiomas:** as traduções oficiais do Inno para en/pt-BR/es/fr/de, mais as suas mensagens próprias nos 5.
   - **Visual:** limpo, com o nome do produto e um ícone ou imagem simples, nas cores do site (#0c1522, #e2c172,
     #c4245e). Sem exagero: o comprador quer instalar e jogar.

3. **Documentação de quem instala.**
   - `LEIA-ME` em 5 idiomas, curto e baseado no `docs\instalacao.md`. Cobre:
     - requisitos;
     - instalar (Setup ou zip);
     - ativar a chave;
     - escolher a IA;
     - atualizar e desinstalar;
     - Steam Deck;
     - SmartScreen;
     - problemas comuns, como antivírus, versão do jogo diferente e saves com mais de 10 impérios.
   - Atualize `docs\instalacao.md` e o `install.html` do site, que hoje diz "installer in preparation" e "delete two
     folders". **Não publique o site sem permissão.**
   - Para quem desenvolve: uma seção "Gerar uma versão" no `README.md` e no `docs\loja.md`. O fluxo é
     `gerar-release.ps1`, depois os testes, depois `publicar-versao.ps1`.

## Fase 3: testar (o coração do trabalho)

Este PC é **Windows 11 Home**: não tem Windows Sandbox nem Hyper-V. Então os testes rodam na instalação real, **com
backup completo e restauração conferida**. Antes de qualquer teste:
1. Faça o backup do projeto.
2. Copie `BepInEx\`, `winhttp.dll`, `doorstop_config.ini` e `.doorstop_version` para
   `Documentos\HumankindModding\pre-teste-instalador\<data>\`.
3. Registre o SHA-256 de cada arquivo, para conferir a restauração depois.

### Matriz de testes

Cada linha precisa de evidência: o log do Inno, a árvore de arquivos antes e depois, um print, ou o log do BepInEx com
"Núcleo carregado".

| # | Cenário | O que conferir |
|---|---|---|
| T1 | **Máquina limpa simulada**: tirar `BepInEx\`, `winhttp.dll` e `doorstop_config.ini`, depois rodar o Setup | Instala o BepInEx e o mod, o jogo abre, o log mostra o núcleo carregado, a partida de **16 impérios** começa |
| T2 | No jogo, depois do T1 | Banco Central (F8), correio, conselho, posto comercial e diplomacia abrem; a tela "Diplomacia IA" mostra o estado "sem chave" ou "sem licença"; o resto funciona sem chave; salvar e carregar |
| T3 | Atualizar por cima (instalar a mesma versão ou uma `-beta2`) | `.cfg`, `credenciais` e `DiplomaciaIA` intactos (comparar os hashes); a versão nova está no log |
| T4 | BepInEx 5.4.x já presente, com outro plugin qualquer de teste | Mantém o BepInEx e não apaga o outro plugin |
| T5 | `doorstop_config.ini` com `enabled = false` | Oferece religar |
| T6 | Desinstalar escolhendo "manter configurações" | Sai só o que foi instalado; configs ficam; o jogo abre limpo, sem o mod |
| T7 | Desinstalar escolhendo "apagar tudo" | Sem sobras do mod; o BepInEx só sai se foi o instalador que o pôs e não sobrou plugin |
| T8 | Pasta do jogo escolhida à mão, numa **cópia falsa** (`Humankind.exe` de mentira numa pasta temporária) | Valida, recusa uma pasta sem o exe, e testa o caminho "sem permissão de escrita", que pede administrador |
| T9 | Jogo aberto durante a instalação | Pede para fechar e não corrompe nada |
| T10 | Idioma: rodar o Setup em pt-BR e em en (`/LANG=`) | Todas as telas e mensagens próprias traduzidas, sem texto cortado |
| T11 | Zip manual: extrair numa pasta limpa simulada, como no T1 | Mesmo resultado do T1; o LEIA-ME bate com o que acontece |
| T12 | Instalação silenciosa (`/VERYSILENT /SUPPRESSMSGBOXES /LOG=`) | Funciona; útil para os testes e para suporte |
| T13 | Trava de segredos: plantar um `.env` falso e uma string `sk_live_TESTE` no staging | O `gerar-release.ps1` **falha** |
| T14 | Smart App Control, se este PC tiver ligado ou em avaliação | Registrar o que acontece com o Setup sem assinatura e com o `winhttp.dll`. Sem SAC aqui, marcar "não testado" |
| T15 | Licença, se a integração existir | Ativar uma chave de teste (pedido de teste, ver `docs\loja.md`), ver a Diplomacia IA ligar, liberar o PC; **apagar o pedido de teste depois** |

Regras dos testes:
- **Sempre partidas de 16 impérios** (`jogo novo 16 Normal`), como o usuário pede.
- Use os comandos dev (`_Modding\tools\dev\devcmd.ps1`) só onde o canal existir. Na instalação de comprador ele
  **não pode** existir, e isso também é um teste.
- Para abrir e fechar o jogo, siga a receita da Steam da memória (`AppError_16`: `steam.exe -shutdown`, depois
  `-silent`).
- Testar a IA de verdade, que gasta crédito, só com permissão do usuário.
- Mande prints ao usuário conforme avança (SendUserFile).
- Achou bug no mod? **Anote e pergunte**: esta sessão não muda funcionalidade nem balanceamento sem pedido.

### Restaurar (obrigatório)

Ao fim dos testes, devolva a instalação de desenvolvimento **exatamente** como estava:
1. Restaure as pastas do backup `pre-teste-instalador`.
2. Confira os SHA-256 contra a lista do início.
3. Abra o jogo e confirme no log que o núcleo de dev carregou e que o canal de comandos responde (`ia status`).

Se algo não bater, pare e avise o usuário antes de qualquer outra coisa.

## Fase 4: entregar

1. Em `_Modding\dist\<versão>\`:
   - o Setup;
   - o zip manual;
   - `MANIFEST.txt`;
   - `SHA256SUMS.txt`;
   - um `RELATORIO-TESTES.md` com a matriz preenchida (OK / falhou / não testado, com o motivo) e os links dos
     prints.
2. Um resumo para o usuário com:
   - tamanhos;
   - o que funciona;
   - o que não foi testado e por quê;
   - riscos conhecidos (SmartScreen e SAC sem assinatura, Epic não testada);
   - o comando de publicação **pronto, mas não executado**:
     ```
     powershell -ExecutionPolicy Bypass -File _Modding\loja\tools\publicar-versao.ps1 -Versao 1.0.0 -Arquivo "<Setup>" -Notas "..." -JogoTestado 1.31.4836
     ```
     A loja aceita um arquivo por versão. Decida com o usuário se o comprador recebe o Setup, o zip ou os dois. Se
     forem os dois, publique o zip como versão separada (ex.: `1.0.0-manual`, `-NaoMarcarComoUltima`) ou ajuste o
     `publicar-versao.ps1` e o Worker para aceitar vários arquivos por versão, **com o usuário de acordo**.
3. Atualize a memória (`installable-goal.md`) e faça o backup final.

## Regras que não se quebram

- Segredos nunca aparecem: chaves de API, `.env`, `credenciais`, chaves de licença reais. Não mostre, não logue, não
  copie para pacote, backup ou chat.
- Nada sai deste PC sem permissão explícita: publicar na loja, publicar o site, VirusTotal, upload em qualquer lugar.
- Downloads de ferramentas só com permissão, dizendo origem e tamanho, e com o hash conferido quando houver referência.
- Nunca instale o núcleo com as nações pensando: `ia status` tem que dizer "pensando agora: 0".
- Não mude funcionalidades nem balanceamento. Bug é para anotar e perguntar.
- Uma sessão por vez no mesmo código e no mesmo jogo.
- Backup antes de começar e no fim. A instalação de dev do usuário volta exatamente como estava.
