# Relatório de testes do instalador: Realpolitik 1.0.0 (2026-10-06)

## Pacote

| Arquivo | Tamanho | SHA-256 |
|---|---|---|
| `Realpolitik_Setup_1.0.0.exe` | 3,7 MB | `09da4a955be0930f17ab47d47dd3c2f90312e8dbbc16f34436831fa07363720d` |
| `Realpolitik_1.0.0_manual.zip` | 1,3 MB | `149b57453b3ad2e65b9b2cd8cc6c06066f53e90665cc9f21eb44251f32b3f178` |

- Gerados por `tools\gerar-release.ps1 -Versao 1.0.0` em `dist\1.0.0\`, junto com `MANIFEST.txt` (45 arquivos, cada um
  com tamanho e sha256) e `SHA256SUMS.txt`.
- Não têm assinatura de código (decisão: caminho gratuito; ver `docs\envio-microsoft.md`).
- Provas de cada teste (logs do Inno, árvores de arquivos com hash e prints): `dist\testes\1.0.0\<teste>\`, fora do Git.

## Matriz

| # | Cenário | Resultado | Prova |
|---|---|---|---|
| T1 | Máquina limpa simulada (sem BepInEx nem Doorstop) → Setup silencioso | **OK** 16/16 | O Steam foi achado sozinho, sem administrador. Instalou o BepInEx, o mod, o MoreEmpires, os leia-me, os termos e as licenças, mais a marca "BepInEx instalado pelo Realpolitik". O núcleo instalado tem o mesmo sha256 do pacote. |
| T1b | O mesmo, aberto **como comprador** (sem a pasta `_Modding`) | **OK** | Log: "sem licença neste PC" (sem modo dev), 79 patches sem falha, "Núcleo carregado", `.cfg` criado com os padrões, nenhum canal de comandos criado. Print do menu com o botão Diplomacia IA. **Achou o bug descrito abaixo.** |
| T2 | Partida nova de **16 impérios** com as DLLs instaladas pelo Setup | **OK** | Banco Central, Correio e a seção Licença (estado de comprador: campo + Ativar) abrem certo. A IA mostra "sem licença" / "nenhum provedor configurado", e o resto funciona sem chave. Prints `t2_*.png`. |
| T3 | Atualizar por cima | **OK** 4/4 | `.cfg`, `credenciais` e `DiplomaciaIA` com hash idêntico depois da instalação; o log registra a versão anterior. |
| T4 | BepInEx 5.4 já presente + plugin de outro autor | **OK** 14/14 | O BepInEx e o `doorstop_config.ini` existentes não foram trocados, o outro plugin ficou e não há marca. Ao desinstalar, o BepInEx e o outro plugin ficaram e só o mod saiu. |
| T5 | `doorstop_config.ini` com `enabled = false` | **OK** 3/3 | O Setup religa (no silencioso a resposta é "sim"; com tela, pergunta). |
| T6 | Desinstalar mantendo configurações | **OK** 8/8 | O mod saiu e as configurações e credenciais ficaram. O BepInEx (instalado por nós, sem outro plugin) saiu. A pasta do desinstalador foi removida. |
| T7 | Desinstalar apagando tudo (`/APAGARTUDO`) | **OK** 7/7 | A pasta do jogo voltou ao original (sem `BepInEx`, `winhttp.dll` ou `doorstop_config.ini`) e o `Humankind.exe` ficou intacto. **Achou o segundo bug abaixo.** |
| T8 | Pasta escolhida à mão | **OK** 5/5 | Uma cópia falsa com `Humankind.exe` instala. Uma pasta sem o exe é recusada. Uma pasta sem permissão de escrita (silencioso, sem `/ALLUSERS`) é recusada sem gravar nada, e o log explica. A desinstalação da pasta falsa ficou limpa. |
| T9 | Jogo aberto durante a instalação | **OK** 3/3 | O Setup recusou, nenhum arquivo mudou e o log registra "HUMANKIND está aberto". |
| T10 | Idiomas (`/LANG=pt` e `/LANG=en`) | **OK** | Boas-vindas, termos, pasta, opções e "Pronto para instalar" traduzidos, com a arte do selo. Prints `pt-*.png` e `en-*.png`. |
| T11 | Zip manual extraído numa pasta limpa simulada | **OK** 11/11 | Mesmos arquivos e hashes do T1 (tirando o desinstalador e a marca). |
| T12 | Instalação silenciosa (`/VERYSILENT /SUPPRESSMSGBOXES /LOG=`) | **OK** | Usada em todos os testes acima. |
| T13 | Trava de segredos (`.env` falso + `sk_live_TESTE` plantados) | **OK** | `gerar-release.ps1 -TestarTrava` saiu com código 1 e **não gerou o Setup**. |
| T14 | Smart App Control | **Não testado** | O SAC está desligado neste PC, e ligar é decisão de segurança do dono. Pendente: ver se o SAC barra o `winhttp.dll` (sem assinatura) do BepInEx. |
| T15 | Licença | **OK** (no código; não repetido no pacote) | Testada contra o Worker de verdade com um pedido de teste: ativar, chave inventada, limite de 3, servidor fora do ar com 14 dias, liberar e reembolso (`docs\diplomacia-ia.md` §16.5). O pacote leva o mesmo código. |

Restauração: a instalação de dev voltou do backup `Documentos\HumankindModding\pre-teste-instalador\2026-10-06_2045`. Os
**630 arquivos conferem hash por hash**, a raiz do jogo voltou igual (o `changelog.txt` do BepInEx, apagado pelo T4, foi
devolvido do zip oficial) e não sobrou entrada de desinstalação. O jogo abriu em modo dev: o canal de comandos responde,
o DeepSeek está conectado e os 597 arquivos de log da IA voltaram.

## Bugs que os testes acharam (corrigidos)

1. **O mod não carregava em instalação nova.** Na primeira vez que o jogo abre, o `Plugin.BindConfig` chamava
   `config.Reload()` antes de o `.cfg` existir (FileNotFoundException). Na máquina de dev o `.cfg` sempre existiu, por
   isso nunca apareceu. **Todo comprador teria recebido o mod quebrado.** Correção: só recarrega se o arquivo existir.
2. **"Apagar tudo" deixava a pasta `BepInEx` vazia.** Os arquivos do desinstalador só saem depois do último passo do
   código. Correção: o desinstalador agenda `rd` (só apaga pasta vazia) para logo depois de terminar.

## Proteção do código (adicionada depois dos testes acima)

- **Ofuscação.** O `CurrencyMod.dll` é ofuscado pelo Obfuscar 2.2.50 dentro do `gerar-release.ps1`, com as regras em
  `installer\obfuscar.xml`.
  - Renomeia 507 tipos internos e criptografa todos os textos: os prompts, as mensagens e os endereços da loja.
  - Mantém os nomes que o carregador, o Harmony, a Unity, os saves em JSON e a DPAPI precisam.
  - `installer\conferir-ofuscacao.ps1` roda a cada geração e falha se algo que precisa do nome mudou: os 96 métodos de
    patch com os mesmos parâmetros, o ModEntry, as mensagens da Unity, os campos dos tipos salvos e o P/Invoke.
  - O mapa de nomes fica só no PC do dono, em `dist\mapas\<versão>\`, e serve para ler logs de erro de compradores.
- **Testes no jogo com a DLL ofuscada:**
  - 79 patches aplicados, 0 com falha;
  - partida de 16 impérios com um turno completo de IA (15 nações e o conselho, respostas entendidas);
  - **save antigo** (Teste16, turno 102, criado sem ofuscação) carregou com a moeda e a memória das nações intactas, e o
    turno da IA funcionou;
  - Banco Central abre.
  - Custo das chamadas de teste: US$ 0,06. Foram acidentais, porque o save carregado tinha o DeepSeek configurado.
- **Termos.** Nova seção "Código proprietário" no instalador (5 idiomas) e no site: proíbe copiar, redistribuir,
  revender, modificar e descompilar o código do mod, mantendo os direitos das licenças de código aberto.
- O T1–T13 acima rodou com a DLL sem ofuscação. A ofuscação só muda os nomes internos do núcleo: os arquivos, os
  caminhos e o instalador são os mesmos.

## Riscos conhecidos

- **Sem assinatura:** todo comprador vê a tela azul do SmartScreen ("Mais informações → Executar assim mesmo").
  Mitigação: enviar cada Setup à Microsoft (`docs\envio-microsoft.md`) e explicar na página de instalação.
- **Smart App Control ligado:** pode bloquear o Setup e talvez o `winhttp.dll` do BepInEx (não testado).
- **Epic:** a detecção pelos manifests da Epic Games está no código, mas não há HUMANKIND da Epic neste PC para
  testar. A pasta escolhida à mão (T8) cobre o caso.
- O site ainda diz "installer in preparation" e "BepInEx (LGPL-2.1)" (o BepInEx é MIT; só o Doorstop é LGPL). Atualizar
  o `install.html` e o `terms.html` antes de vender. **Não foi publicado.**

## Publicar (só com OK do dono)

```
powershell -ExecutionPolicy Bypass -File _Modding\loja\tools\publicar-versao.ps1 -Versao 1.0.0 -Arquivo "_Modding\dist\1.0.0\Realpolitik_Setup_1.0.0.exe" -Notas "First release" -JogoTestado 1.31.4836
```

A loja aceita um arquivo por versão. O zip manual pode ir como versão separada (`1.0.0-manual`, `-NaoMarcarComoUltima`).
