# Prompt: pesquisa de viabilidade de um instalador .exe para distribuir o mod

> Prompt independente para uma sessão nova do Claude Code, aberta na pasta do Humankind. Cole o conteúdo abaixo da linha.

---

## Objetivo

O lucas vai **vender o mod** para jogadores de fora: venda única de uns US$ 10, por uma loja como a Lemon Squeezy. O
comprador baixa um arquivo, instala, e recebe as atualizações baixando de novo. O lucas não quer ficar dando suporte
um a um.

A pergunta desta sessão: **dá para distribuir o mod como um `.exe` (instalador) que o comprador só executa e pronto?**
Se dá, qual é o melhor jeito, quanto custa e quais são os riscos.

**Esta sessão é só de pesquisa.** Não mexa no código do mod, nos scripts nem na instalação do jogo. O resultado é um
relatório com recomendação. Uma prova de conceito pequena só é feita se o usuário pedir, depois do relatório, e na pasta
de rascunho da sessão (scratchpad), nunca na pasta do jogo.

O usuário fala português (pt-BR, informal). Responda em pt-BR, curto e claro. A sua memória do projeto (MEMORY.md) já vem
carregada; leia `installable-goal.md`, `humankind-modding-setup.md`, `translation-status.md` e `ai-providers.md`.
**Não discuta as regras do jogo (EULA da SEGA):** o usuário já decidiu e não quer voltar nesse assunto.

## O que existe hoje (leia antes)

- **O mod:**
  - BepInEx 5.4.23.5 x64 na pasta do jogo, que entra pelo `winhttp.dll` (Doorstop);
  - o **CurrencyMod**: um carregador em `BepInEx\plugins\CurrencyMod\` e o núcleo em `BepInEx\CurrencyModCore\`, com
    recarga a quente;
  - o **MoreEmpires**, um plugin separado (até 16 impérios);
  - a configuração em `BepInEx\config\*.cfg`;
  - credenciais de IA: hoje `BepInEx\config\deepseek.key`; outra sessão está mudando para `BepInEx\config\credenciais\`
    criptografado.
- **Testado** com Humankind 1.31.4836 na Steam (Windows).
- **Pacote atual:** `_Modding\tools\empacotar.ps1` gera um zip sem o BepInEx. `tools\instalar-moreempires.ps1 -Pacote` faz
  outro zip. Instruções em `docs\instalacao.md`. Nada disso foi testado numa máquina limpa.
- **Documentos para o contexto:** `_Modding\README.md`, `docs\prompt-pacote-instalacao.md` (os 13 pontos de decisão do
  pacote) e `docs\instalacao.md`.

## O que pesquisar

Para cada ponto: o que é possível, como os outros fazem (cite exemplos reais de mods de jogos Unity com BepInEx e
instaladores conhecidos), custo, risco e a sua recomendação. Use fontes atuais (2026) e cite os links.

1. **Achar o jogo no PC do comprador:**
   - Steam: registro do Windows, `libraryfolders.vdf`, várias bibliotecas, outros discos;
   - **Epic Games Store**: o Humankind também é vendido lá. Dá para modar com BepInEx? Onde fica instalado?
   - **Xbox / Game Pass / Microsoft Store**: pasta protegida (WindowsApps)? Dá para injetar o BepInEx? Se não der, como o
     instalador explica isso;
   - versão do jogo: como detectar e o que fazer se for diferente da testada;
   - **Steam Deck / Linux (Proton)**: o BepInEx funciona com `WINEDLLOVERRIDES="winhttp=n,b"`? O instalador pode só
     mostrar a instrução?
   - **macOS**: o Humankind tem versão para Mac? O BepInEx funciona lá? Vale a pena?
2. **Tecnologia do instalador:**
   - Inno Setup, NSIS, WiX/MSI, Velopack/Squirrel, ou um app próprio em .NET (WPF/WinForms, autocontido);
   - qual dá a melhor experiência: escolher a pasta, detectar o jogo, componentes opcionais (o MoreEmpires), idiomas
     (pt, en, es, fr, de), desinstalar limpo;
   - tamanho final;
   - instalador + atualizador num app só, ou um "launcher" pequeno?
3. **Permissões do Windows:**
   - a Steam costuma ficar em `C:\Program Files (x86)`, que exige administrador para escrever;
   - instalar pede UAC? Dá para pedir só quando precisar?
   - o que acontece com bibliotecas da Steam em outros discos.
4. **Assinatura de código e alertas:**
   - o SmartScreen ("O Windows protegeu o computador") em `.exe` sem assinatura: quanto espanta o comprador?
   - opções de certificado em 2026: certificado OV/EV de autoridade, Azure Trusted Signing / Artifact Signing (aceita
     pessoa física? aceita quem mora no Brasil?), SignPath para projetos abertos;
   - preços e exigências (CNPJ? documento?), e quanto tempo leva para o SmartScreen ganhar reputação.
5. **Antivírus e falsos positivos:**
   - o `winhttp.dll` do Doorstop e a injeção de código costumam ser marcados por antivírus? Há relatos com BepInEx?
   - como reduzir: assinatura, enviar o arquivo para a Microsoft e outros fabricantes analisarem, não compactar o `.exe`
     com UPX, etc.;
   - como testar antes de vender (VirusTotal: o upload torna o arquivo público para os fabricantes; **pergunte ao usuário
     antes de enviar qualquer coisa**).
6. **O que pode ir dentro:**
   - licenças: BepInEx (LGPL-2.1), Harmony (MIT), Mono.Cecil (MIT), Doorstop. O que exigem (avisos, textos de licença,
     código-fonte)?
   - confirmar que **nenhuma DLL nem código do jogo** vai no instalador (o código descompilado e as DLLs publicizadas ficam
     de fora);
   - credenciais e configuração do lucas nunca entram. A trava do `empacotar.ps1` tem que valer também no instalador.
7. **Atualizações:**
   - como o jogador descobre que tem versão nova: aviso dentro do jogo, o próprio instalador checando, um e-mail da loja;
   - onde fica a versão nova: a própria loja (Lemon Squeezy/Gumroad entregam o arquivo atualizado a quem comprou?),
     GitHub Releases privado, um servidor próprio;
   - atualizar sem perder o `.cfg`, as credenciais nem os dados das partidas;
   - o que fazer quando um patch do Humankind quebra o mod (avisar? desligar só a parte quebrada, como já acontece?).
8. **Chave de ativação:**
   - onde ela entra melhor: no instalador ou dentro do jogo;
   - como a API de licenças da loja escolhida funciona (ativações, limite de máquinas, uso sem internet);
   - o mod é .NET, então a trava se remove com facilidade. Diga o que vale a pena (ofuscação leve?) e o que não vale.
9. **Desinstalar e restaurar:**
   - remover o mod e o BepInEx sem tocar nos saves;
   - o "Verificar integridade dos arquivos" da Steam apaga ou mantém o BepInEx? Sobrevive às atualizações do jogo?
   - convivência com outros mods BepInEx que o comprador já tenha: não sobrescrever o BepInEx dele sem perguntar.
10. **Alternativas ao .exe** (compare):
    - zip + `instalar.bat`/`.ps1` (o que já existe), que é mais fácil de fazer mas assusta mais e pode ser bloqueado pela
      política de scripts do PowerShell;
    - gerenciadores de mods: r2modman / Thunderstore (suportam Humankind? aceitam mod pago? provavelmente não),
      Vortex/Nexus;
    - mod.io, que o Humankind usa para conteúdo: serve para mod de código? para mod pago?
11. **Experiência do comprador, do download à primeira partida:** descreva o caminho ideal, passo a passo, com o que ele
    vê em cada tela (inclusive a tela da IA que a outra sessão está fazendo no menu principal).
12. **Custo e trabalho:**
    - para cada caminho: custo em dinheiro (certificado, hospedagem) e de trabalho (dias de implementação, manutenção a
      cada versão);
    - o que o lucas mesmo precisa fazer: conta, documentos, pagamentos.

## Entrega

1. **Relatório** em `_Modding\docs\pesquisa-instalador-exe.md`, em pt-BR, com:
   - um **resumo de uma tela no topo**: dá ou não dá, o caminho recomendado, o custo total e os 3 maiores riscos;
   - uma tabela comparando os caminhos (instalador .exe, launcher próprio, zip + script, gerenciador de mods);
   - as respostas dos 12 pontos, com links;
   - um plano de implementação do caminho recomendado, por etapas;
   - as decisões que ficam com o lucas, com a sua recomendação em cada uma.
2. **No chat**, um resumo curto, e a pergunta se ele quer uma prova de conceito. A prova de conceito seria, por exemplo,
   um instalador mínimo que só detecta a pasta do jogo e mostra a versão, feito no scratchpad.
3. Atualize a memória `installable-goal.md` com a conclusão e o caminho do relatório.

## Regras que não se quebram

- Esta sessão não muda o mod nem a instalação do jogo. Pesquisa, relatório e, só se pedirem, uma prova de conceito no
  scratchpad.
- Nada sai deste PC sem permissão explícita: upload no VirusTotal, contas, compras de certificado, formulários.
- Nunca leia, copie nem mostre chaves ou credenciais (`deepseek.key`, a pasta `credenciais`).
- Não discuta as regras do jogo nem a legalidade de vender: o usuário já decidiu.
- Não rode ao mesmo tempo que outra sessão que esteja instalando coisas no jogo; ler arquivos é seguro.
