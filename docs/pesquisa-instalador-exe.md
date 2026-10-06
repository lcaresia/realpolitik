# Pesquisa: instalador .exe para distribuir o mod

Pesquisa feita em 2026-10-06 (sessão só de pesquisa: nada no mod nem no jogo foi alterado). Fontes de 2024–2026, com
links em cada ponto. Marcado **[LOCAL]** = conferido nesta máquina.

---

## Resumo (uma tela)

**Dá, sim.** Um `Setup.exe` feito com **Inno Setup** (grátis) acha o jogo sozinho na Steam e na Epic, instala o BepInEx
só se faltar, instala o mod (com o MoreEmpires opcional), fala pt/en/es/fr/de e desinstala limpo. Na instalação padrão
da Steam **nem pede administrador**: a Steam dá Controle Total ao grupo Usuários na pasta dela [LOCAL].

**Caminho recomendado**
1. Inno Setup `Setup.exe` (~3–5 MB) + o mesmo conteúdo num **zip manual** (Steam Deck, quem prefere, plano B).
2. **Chave de ativação dentro do jogo** (na tela de IA do menu principal), pela API de licenças da Lemon Squeezy, com
   tolerância a ficar sem internet.
3. Atualização: troca o arquivo na Lemon Squeezy (todos os compradores passam a baixar o novo) + aviso no jogo lendo um
   `version.json` público.
4. **Assinar o código** (Certum ou SSL.com, pessoa física, sem CNPJ) — pode ser na 1ª versão ou logo depois.

**Custo total:** US$ 0 sem assinatura; **~US$ 130–230/ano** com certificado. Loja: ~16–18% de cada venda
(Lemon Squeezy 5% + 50¢ + 1,5% internacional + 3% saque PayPal) → ~US$ 8,20–8,40 líquidos por venda de US$ 10.
Trabalho: **~8–12 dias** para instalador + ativação + aviso de atualização; ~meio dia por versão depois.

**3 maiores riscos**
1. **Sem assinatura, o Windows 11 com Smart App Control ligado bloqueia o instalador sem opção de continuar** (e talvez
   o `winhttp.dll` também). Nos outros PCs aparece "O Windows protegeu o computador" → "Mais informações" → "Executar
   assim mesmo", que espanta parte dos compradores.
2. **Futuro da Lemon Squeezy:** ela está migrando para o Stripe Managed Payments, que hoje **não aceita vendedor no
   Brasil**. Deixe o Gumroad pronto como plano B (código de licença atrás de uma interface trocável).
3. **Falso positivo de antivírus** (instaladores Inno/NSIS e o proxy `winhttp.dll` do BepInEx às vezes são marcados por
   heurística). Mitigação: assinar tudo, não usar UPX/auto-extração, e mandar o arquivo para análise da Microsoft e dos
   fabricantes quando acontecer.

---

## Comparação dos caminhos

| | Instalador .exe (Inno) | Launcher/app próprio (.NET) | Zip + script `.ps1`/`.bat` | Gerenciador de mods |
|---|---|---|---|---|
| Experiência | Melhor: duplo clique, detecta jogo, idiomas, desinstalar no Painel | Igual ou melhor (tela própria), mas é muito mais código | Pior: extrair, rodar script, política do PowerShell bloqueia | Seria ótima, mas não existe para Humankind |
| Detecta Steam/Epic | Sim (script Pascal lê registro/VDF/JSON) | Sim (pacote GameFinder) | Sim, mas script | — |
| Desinstalar limpo | Nativo (log do que instalou) | Tem que fazer | Manual | — |
| Tamanho | ~3–5 MB | <1 MB (.NET Framework 4.8) ou 60–150 MB (.NET 8 autocontido) | ~2 MB | — |
| SmartScreen/antivírus | Assina 1 exe + DLLs | Idem | `.ps1` assusta mais; script baixado é bloqueado pela política | — |
| Trabalho inicial | 3–5 dias | 8–15 dias | 0 (já existe `empacotar.ps1`) | — |
| Manutenção por versão | Recompilar o `.iss` | Recompilar | Nenhuma | — |
| Mod pago aceito? | Sim | Sim | Sim | **Não**: Thunderstore não tem comunidade Humankind; Nexus proíbe mod pago; mod.io do Humankind é só para conteúdo do editor, sem marketplace |

**Veredito:** Inno Setup + zip manual. Um launcher próprio não compensa agora: a "tela própria" que o comprador precisa
(chave, provedor de IA) já vai existir **dentro do jogo**, no menu principal.

---

## Respostas dos 12 pontos

### 1. Achar o jogo no PC do comprador

**Steam**
- Pasta da Steam: `HKCU\Software\Valve\Steam\SteamPath` (barras `/`, minúsculo) ou
  `HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`. Os dois existem aqui [LOCAL].
- Bibliotecas: `<Steam>\steamapps\libraryfolders.vdf` (entradas `"0"`, `"1"`… com `"path"` e um bloco `"apps"`). Aqui o
  `1124300` está na biblioteca `"0"` [LOCAL]. Para cada `path`, ver se existe
  `<path>\steamapps\appmanifest_1124300.acf`, ler `"installdir"` (`Humankind`) e montar
  `<path>\steamapps\common\Humankind`; conferir `Humankind.exe`. Último recurso: escolher a pasta à mão.
  Formato: https://unpkg.com/@ciberus/find-steam-app@4.0.0/README.md
- A chave de desinstalação `Steam App 1124300` **não existe** aqui [LOCAL] — não confiar nela.

**Epic Games Store**
- O Humankind é vendido lá e foi **jogo grátis da semana em fev/2025** → pode ter muito dono na Epic.
  https://heise.de/-10274198 · https://overclock3d.net/?p=286210
- Detectar: JSONs em `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (`DisplayName`, `InstallLocation`).
  https://jayd.ml/games/2020/05/16/epic-games-store-steam-libraries.html
- BepInEx: é o mesmo build Unity Mono de Windows; o proxy `winhttp.dll` não depende da loja. **Sem relato específico do
  Humankind na Epic → testar** (dá para pegar uma cópia na Epic se você resgatou o grátis).
- Atenção: o padrão da Epic é `C:\Program Files\Epic Games\…`, que **pede administrador** para escrever (ver ponto 3).

**Xbox / Game Pass / Microsoft Store**
- Entrou no Game Pass PC no lançamento (2021) e **saiu em 31/12/2024**.
  https://www.gamewatcher.com/humankind-leaves-xbox-pc-game-pass-end-of-december
- Jogos da Xbox ficam em pasta protegida; o "Ativar mods" só existe para jogos que optaram por isso.
  https://www.pcworld.com/article/399265/microsofts-xbox-app-for-windows-supports-mods-for-pc-gaming.html
- **Não suportar.** Se o instalador achar só uma instalação da Xbox (pasta `WindowsApps`/`XboxGames`), mostra: "A versão
  Xbox/Game Pass não aceita mods de código. Use a versão Steam ou Epic."

**Versão do jogo**
- Ler o FileVersion do `Humankind.exe`: aqui `1.31.4836+7b68e330a4` [LOCAL]. Funciona em qualquer loja. (O `buildid` do
  appmanifest, `24927712`, é só da Steam e não é versão legível.)
- Últimos patches: 1.30.4814 (~mai/2026) e "Cassandra Update" (~26/ago/2026 = 1.31.4836). **Humankind 2 foi anunciado
  para 2027** — os patches do 1 devem rarear. https://www.pcgamesn.com/humankind-2/announcement
- Se for diferente da testada: **avisar, não bloquear** ("testado com 1.31.4836; pode funcionar"). O mod já desliga só
  a parte que quebra.

**Steam Deck / Linux (Proton)**
- Não há build nativo de Linux; o Deck roda o build de Windows (ProtonDB: **Gold**).
  https://www.protondb.com/api/v1/reports/summaries/1124300.json
- Funciona com a opção de inicialização `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
  https://docs.bepinex.dev/master/articles/advanced/steam_interop.html
- O `.exe` não roda no Deck → **zip manual + instrução** (copiar e colar a opção de inicialização).

**macOS**
- Existe versão Mac na Steam (desde nov/2021). BepInEx 5 no Mac usa `run_bepinex.sh`, e **não roda nativo em Apple
  Silicon** (só via Rosetta, com problemas de permissão/quarentena). https://github.com/toebeann/gib
- **Não vale a pena.** Declarar "não suportado".

### 2. Tecnologia do instalador

| Opção | Resumo |
|---|---|
| **Inno Setup 6.x** (recomendado) | Script Pascal lê registro/VDF/JSON, escolha de pasta, componentes (MoreEmpires), **traduções oficiais pt-BR/en/es/fr/de**, desinstalador com log, assinatura integrada. Grátis; desde 6.5 pede (não exige) licença paga para quem fatura >US$ 5 mil/ano. https://jrsoftware.org/isorder.php · https://jrsoftware.org/files/istrans |
| NSIS | Faz o mesmo, linguagem mais chata, mesmo histórico de falso positivo. |
| WiX (MSI) | Bom para desinstalar/reparar e dizem ter menos falso positivo, mas MSI é desajeitado para "instalar dentro da pasta de outro programa"; a v6 cobra taxa de quem tem receita. https://docs.firegiant.com/wix/osmf/ |
| Velopack/Squirrel | **Ferramenta errada**: feito para app que se atualiza sozinho em `%LocalAppData%`. https://docs.velopack.io/packaging/operating-systems/windows |
| App próprio .NET | .NET Framework 4.8 (já vem no Windows 10/11): exe <1 MB, mas desinstalador e entrada no Painel de Controle ficam por sua conta. .NET 8 autocontido: 60–150 MB, e o formato "extrai e roda" lembra malware. |

- **Tamanho final:** ~1,5–2 MB do Inno + ~1,5 MB do mod + ~1 MB do BepInEx comprimido ≈ **3–5 MB**.
- **Instalador + atualizador num app só?** Não precisa. O aviso de versão nova fica no jogo (ponto 7) e o novo
  `Setup.exe` instala por cima, mantendo `.cfg` e credenciais.

### 3. Permissões do Windows (UAC)
- **Steam:** o instalador da Steam dá `BUILTIN\Usuários: Controle Total` em `C:\Program Files (x86)\Steam` e tudo
  abaixo herda [LOCAL]; a Steam até se oferece para reparar se alguém tirar.
  https://steamcommunity.com/discussions/forum/0/2149847423924910741 → **instalar no Humankind da Steam não pede UAC.**
- Bibliotecas em outros discos (`D:\SteamLibrary`) são criadas pelo usuário → também graváveis.
- **Epic** em `C:\Program Files\Epic Games` → precisa de administrador.
- Configuração: `PrivilegesRequired=lowest` + `PrivilegesRequiredOverridesAllowed=dialog`. O script testa se a pasta é
  gravável; se não for, pede para reabrir como administrador (o Inno faz o "reiniciar elevado").
  https://jrsoftware.org/ishelp/topic_setup_privilegesrequiredoverridesallowed.htm

### 4. Assinatura de código e alertas
- **Sem assinatura:** tela "O Windows protegeu o computador" → "Mais informações" → "Executar assim mesmo". Cada versão
  nova recomeça do zero (a reputação fica no hash do arquivo). Não há como pedir revisão manual do SmartScreen.
  https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
- **Smart App Control (Windows 11):** bloqueia código sem assinatura e sem reputação **sem botão de continuar**; o usuário
  teria que desligar o SAC inteiro. Desde ~abr/2026 dá para religar o SAC sem reinstalar o Windows, então mais PCs podem
  ter. Ele confere **cada módulo**, inclusive DLLs → um `winhttp.dll` sem assinatura pode ser barrado mesmo com o Setup
  assinado. (Para DLL .NET carregada pelo Mono do jogo não achei fonte — **testar num PC com SAC ligado**.)
  https://support.microsoft.com/en-us/topic/what-is-smart-app-control-285ea03d-fa88-4d56-882e-6698afdb7003 ·
  https://textslashplain.com/2026/04/28/smart-app-control/
- **Azure Artifact Signing** (ex-Trusted Signing, US$ 9,99/mês): pessoa física **só EUA/Canadá**; empresa só numa lista de
  países **sem o Brasil**. **Fora.** https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart
- **EV não vale mais:** desde 2024 não pula o SmartScreen.
- **Opções reais (pessoa física, sem CNPJ):**
  - **Certum Standard**: €169 (cartão + leitor) ou €209 (nuvem SimplySign), ~US$ 189/ano via revenda. Pede documento +
    conta de consumo no seu nome; validação por vídeo (IDnow), ~2 semanas.
    https://shop.certum.eu/code-signing.html · https://support.certum.eu/en/code-signing-required-documents/
  - **SSL.com IV**: US$ 129/ano + eSigner na nuvem US$ 180/ano (ou YubiKey US$ 379 uma vez).
    https://ssl.com/products/software-integrity/code-signing/iv/
  - Certum "Open Source" (€25–69) e SignPath grátis **não servem** para produto pago e fechado (revogam).
- Desde mar/2026 todo certificado dura no máximo **460 dias** (reemissão periódica).
- Reputação com certificado: o aviso some depois de "algumas semanas e centenas de instalações limpas", e passa para as
  versões seguintes assinadas com o mesmo certificado.
- **O que assinar:** `Setup.exe`, o desinstalador, `CurrencyMod.Loader.dll`, `CurrencyMod.dll` e `MoreEmpires.dll`.
  O `winhttp.dll` e as DLLs do BepInEx são de terceiros: o certo é mandá-los sem alteração e contar com a reputação do
  arquivo oficial, que é muito baixado. Assiná-los com o seu nome é possível tecnicamente, mas faria você aparecer como
  o publicador deles. O teste com SAC (ponto "Não confirmado") diz se isso basta.

### 5. Antivírus e falsos positivos
- BepInEx/Doorstop: detecções ocasionais por heurística (Gridinsoft em nov/2024), sem caso grande.
  https://blog.gridinsoft.com/bepinex-malware-or-false-positive/
- Instaladores Inno e NSIS já foram marcados pelo Defender como `Trojan:Win32/Wacatac!ml` (aprendizado de máquina).
  Solução relatada: assinar o instalador e os arquivos de dentro.
  https://forum.juce.com/t/proactive-measures-to-prevent-inno-setup-installer-from-getting-flagged-by-windows-defender/65217
- **Não usar:** UPX, PyInstaller, "extrai em %TEMP% e executa".
- **Reclamar do falso positivo:** Microsoft https://www.microsoft.com/wdsi/filesubmission ("Incorrectly detected");
  lista dos outros fabricantes: https://docs.virustotal.com/docs/false-positive-contacts
- **VirusTotal:** o upload público entrega o arquivo para os assinantes do VirusTotal (pirataria do mod pago). Para
  testar, mandar uma **versão de teste sem a parte paga** ou só consultar o hash. **Só com sua permissão.**

### 6. O que pode ir dentro

| Componente | Licença | O que exige |
|---|---|---|
| BepInEx 5 | LGPL-2.1 | Texto da licença, aviso de uso, link para o código-fonte da versão exata |
| UnityDoorstop 4 (`winhttp.dll`) | LGPL-2.1 | Idem |
| HarmonyX, MonoMod, Mono.Cecil | MIT | Manter o aviso de copyright + texto MIT |

Fontes: https://github.com/BepInEx/BepInEx · https://github.com/NeighTools/UnityDoorstop ·
https://github.com/BepInEx/HarmonyX · https://github.com/MonoMod/MonoMod · https://github.com/jbevain/cecil

- O mod pode continuar **fechado**: plugin carregado pelo BepInEx é "obra que usa a biblioteca" (LGPL §5).
- Cuidado com os seus termos: a LGPL §6 pede que o usuário possa modificar a biblioteca e fazer engenharia reversa
  para depurar isso → **não pôr cláusula genérica "proibido engenharia reversa"** nos termos.
- Na prática: pasta `licencas\` no instalador com LGPL-2.1, MIT ×3 e um `THIRD-PARTY.txt` com os links das versões.
- **Nada do jogo vai junto:** só as DLLs compiladas do mod. O código descompilado e as DLLs publicizadas são só
  referência de compilação e ficam fora (o `empacotar.ps1` já faz assim).
- **Trava de credenciais:** o `.iss` só puxa arquivos de `_Modding\dist\HumankindMod_<versão>\` (a pasta que o
  `empacotar.ps1` já varreu atrás de chaves). O Inno nunca deve apontar para `BepInEx\config`.

### 7. Atualizações
- **Onde fica:** na própria Lemon Squeezy. Ao trocar o arquivo do produto, **todo comprador passa a baixar o novo** em
  "My Orders" ou no link do e-mail. Não há e-mail automático de "versão nova".
  https://docs.lemonsqueezy.com/help/products/adding-products
- **Como o jogador fica sabendo:** o mod lê um `version.json` público ao abrir (timeout curto, silencioso sem internet):
  `{ "latest": "1.4.0", "testedGame": "1.31.4836", "notes": "…", "url": "…" }` e mostra "Versão nova disponível" no menu.
  Hospedar no GitHub (repositório público só com esse JSON e o changelog) ou Cloudflare R2 (grátis).
  GitHub Releases **privado não serve** (precisaria de token embutido).
- **Sem perder nada:** o instalador nunca sobrescreve `BepInEx\config\*.cfg`, `BepInEx\config\credenciais\` nem
  `BepInEx\DiplomaciaIA\`; os dados das partidas ficam dentro dos saves.
- **Patch do Humankind quebrou:** o mod já desliga só a parte quebrada. Somar: aviso "versão do jogo X não testada" e,
  no `version.json`, um campo `brokenOn` para o mod avisar "aguarde a atualização".

### 8. Chave de ativação
- **Onde:** **dentro do jogo**, na tela de IA do menu principal (que outra sessão está fazendo). Vantagens: vale para
  quem instala pelo zip, pelo Deck, e para reinstalações; a desativação ("liberar este PC") fica no mesmo lugar.
- **API da Lemon Squeezy** (https://docs.lemonsqueezy.com/api/license-api):
  - `activate` (chave + nome da máquina) → devolve `instance.id`, limite e uso; `validate`; `deactivate`.
  - **Não precisa de chave secreta** → nada de segredo dentro da DLL. Limite: 60 chamadas/min.
  - **Conferir `store_id` e `product_id` na resposta**, senão qualquer chave de qualquer loja LS passa.
  - Limite de máquinas por variante (sugestão: 3). Sem expiração.
  - Só funciona online → o mod guarda o último resultado bom e aceita ficar offline (sugestão: sem limite, ou 30 dias).
    Só trava se a LS disser explicitamente "disabled"/inválida.
- **O que trancar:** sugestão — sem chave, o mod funciona como demonstração (ex.: Diplomacia IA desligada, ou só os
  primeiros N turnos). Decisão sua (abaixo).
- **Proteção:** a trava sai com um patch Harmony em 10 minutos. ConfuserEx é desfeito por ferramenta pública. Vale no
  máximo renomear com **Obfuscar** (grátis). A chave é "prova de compra", não DRM; não pune quem pagou.
  https://github.com/mkaring/ConfuserEx · https://www.softanics.com/net-obfuscation/tools/confuserex-alternative

### 9. Desinstalar e restaurar
- O Inno registra cada arquivo que instalou e remove só esses. O BepInEx só sai se **foi o instalador que o colocou** e
  **não sobrou outro plugin** em `BepInEx\plugins`.
- Pergunta ao desinstalar: "Manter configurações e credenciais?" (padrão: manter).
- Saves: não são tocados. Continuam abrindo sem o mod (os dados do mod são arquivos extras no save).
- **"Verificar integridade" da Steam:** só confere os arquivos que a Steam instalou; **não apaga** `BepInEx\`,
  `winhttp.dll` nem `doorstop_config.ini`. Atualizações do jogo também não.
  https://steamcommunity.com/discussions/forum/0/3047182696776825128
- **Outros mods BepInEx do comprador:**
  - BepInEx 5.4.x já instalado → instala só as pastas do mod, sem tocar no core nem no `BepInEx.cfg`;
  - BepInEx 6 ou desconhecido → avisa e oferece "fazer backup e trocar", nunca em silêncio;
  - `doorstop_config.ini` com `enabled = false` → oferece religar.
  - Marcadores: `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` (`4.5.0`), FileVersion de
    `BepInEx\core\BepInEx.dll` (`5.4.23.5`) [LOCAL].

### 10. Alternativas ao .exe
- **Zip + script:** já existe, custo zero, mas o comprador tem que extrair na pasta certa; `.ps1` baixado é bloqueado
  pela política de execução e assusta. **Manter só como plano B / Steam Deck.**
- **Thunderstore / r2modman:** não há comunidade Humankind.
  https://thunderstore.io/api/experimental/community/
- **Nexus/Vortex:** proíbe mod pago, demo de mod pago e link para venda. https://www.nexusmods.com/rust/news/14698
- **mod.io:** é o canal oficial do Humankind, mas para conteúdo do editor (mapas, culturas), não DLL; mod pago só onde o
  estúdio liga o Marketplace, e não há sinal disso no Humankind.
  https://support.mod.io/hc/en-us/articles/9860299120015-What-is-the-mod-io-marketplace
- **Conclusão:** vender e distribuir pela sua página/loja.

### 11. Experiência do comprador, do download à primeira partida
1. **Compra** na Lemon Squeezy → e-mail com **chave de licença** + link do `RealpolitikSetup_1.4.0.exe` (nome ilustrativo).
2. **Abre o Setup.**
   - Sem assinatura: tela azul do SmartScreen → "Mais informações" → "Executar assim mesmo" (a página da loja mostra um
     print explicando).
   - Com assinatura e reputação: só a janela do instalador.
3. **Idioma** (detectado do Windows; troca na lista).
4. **"Encontramos o Humankind"**: Steam (ou Epic), versão `1.31.4836 ✓ testada`. Botão "Procurar…" para outra pasta.
   Se a versão for outra: aviso amarelo, não bloqueia.
5. **BepInEx:** "Não encontrado — será instalado (grátis, LGPL)" ou "BepInEx 5.4.23 encontrado — será mantido".
6. **Componentes:** ☑ Mod principal · ☐ MoreEmpires (até 16 impérios).
7. **Instalar** (segundos). Fim: ☑ "Abrir o Humankind agora" ☑ "Ler o guia rápido".
8. **No jogo, menu principal:** a tela do mod abre sozinha na 1ª vez:
   - **Chave de licença:** cola a chave → "Ativado neste PC (1 de 3)".
   - **Diplomacia IA:** escolhe o provedor — padrão **"Entrar com OpenRouter"** (abre o navegador, volta logado) ou cola
     uma chave de API (DeepSeek, OpenAI, Gemini…). Pode pular: o resto do mod funciona sem IA.
9. **Nova partida** → F8 Banco Central, botões novos na barra inferior, cartas das nações a partir dos primeiros turnos.
10. **Atualização:** quando sair versão nova, aparece "Versão 1.5 disponível" no menu → link para "My Orders" → roda o
    novo Setup por cima; configurações e login ficam.

### 12. Custo e trabalho

| Caminho | Dinheiro | Trabalho inicial | Por versão |
|---|---|---|---|
| Zip + script (hoje) | 0 | 0 | 0 |
| **Inno Setup sem assinatura** | 0 | 3–5 dias (script, detecção Steam/Epic/Xbox, BepInEx, idiomas, desinstalar, teste em máquina limpa) | ~30 min |
| + assinatura | US$ 130–230/ano | +1 dia (configurar) + ~2 semanas de espera da validação | ~10 min (assinar 4–5 arquivos) |
| + ativação no jogo | 0 | 2–3 dias | 0 |
| + aviso de atualização | 0 (GitHub/R2) | 1 dia | 2 min (editar o JSON) |
| Launcher próprio .NET | igual | 8–15 dias | ~1 h |

**O que você mesmo precisa fazer:**
- Lemon Squeezy: criar a loja, enviar documento (KYC, ~2–3 dias), configurar o saque por **PayPal** (o Brasil não está
  na lista de saque bancário), criar o produto com licença (limite 3, sem expiração). Antes, **mandar um e-mail ao
  suporte** confirmando que aceitam vendedor pessoa física no Brasil e mod de jogo (código próprio, sem arquivos do
  jogo), e perguntar do preço especial para produtos abaixo de US$ 10.
  https://docs.lemonsqueezy.com/help/getting-started/fees · https://docs.lemonsqueezy.com/help/getting-started/prohibited-products
- Certificado (se for assinar): pedir na Certum ou SSL.com com RG/CNH + conta de consumo, fazer a validação por vídeo,
  pagar.
- Uma conta no GitHub (ou Cloudflare) para o `version.json`.
- Testar numa cópia Epic, se quiser suportar a Epic de verdade.

---

## Plano de implementação (caminho recomendado)

0. **Antes de tudo:** rodar a revisão de bugs (`docs\prompt-revisao-bugs.md`) e terminar a tela de provedores de IA.
1. **Instalador Inno (versão sem assinatura)** — `_Modding\installer\HumankindMod.iss` + `tools\gerar-instalador.ps1`
   que roda o `empacotar.ps1` (com a trava de chaves) e depois o compilador do Inno (`ISCC.exe`).
   - Detecção: Steam (registro → VDF → ACF) → Epic (manifests) → Xbox (só para avisar) → "Procurar…".
   - Versão: FileVersion do `Humankind.exe`, comparada com a testada.
   - BepInEx: detectar 5.x/6.x/ausente; instalar o zip oficial 5.4.23.5 x64 só se ausente; registrar no log.
   - Componentes: mod (obrigatório), MoreEmpires (opcional). Idiomas: pt-BR, en, es, fr, de.
   - Nunca sobrescrever `config\*.cfg`, `config\credenciais\`, `DiplomaciaIA\`.
   - `licencas\` + `THIRD-PARTY.txt`.
   - Desinstalador: só o que instalou; BepInEx só se foi ele e sobrou nada; pergunta sobre manter configurações.
2. **Teste em máquina limpa** (VM do Windows 11 / Windows Sandbox): Steam em `Program Files (x86)`, biblioteca em outro
   disco, BepInEx já presente, reinstalação por cima, desinstalação; **e um teste com Smart App Control ligado**.
   Depois, partida de 16 impérios.
3. **Ativação no jogo** (LS License API), com cache offline e "liberar este PC"; interface trocável (LS ↔ Gumroad).
4. **Aviso de atualização** (`version.json`) + campo de versão do jogo quebrada.
5. **Assinatura**: quando o certificado chegar, `signtool` no `gerar-instalador.ps1` para as DLLs do mod, o Setup e o
   desinstalador (o Inno tem `SignTool=`).
6. **Zip manual** para Steam Deck/plano B, com o guia da opção `WINEDLLOVERRIDES`.
7. **Página da loja:** requisitos (Windows, Steam/Epic, Humankind 1.31.x), print do SmartScreen explicando, Deck
   "experimental", Mac/Xbox "não suportado", privacidade.

---

## Decisões que ficam com você

| # | Decisão | Minha recomendação |
|---|---|---|
| 1 | Instalador .exe ou só zip? | **Inno Setup + zip manual junto** |
| 2 | Assinar o código? Quando? | **Sim**, Certum ou SSL.com (~US$ 130–230/ano). Pode lançar sem e assinar na 1ª atualização, mas avisando na página sobre o SmartScreen/SAC |
| 3 | Lojas suportadas | **Steam + Epic** (Epic depois de testar); Deck pelo zip "experimental"; Mac e Xbox fora |
| 4 | Levar o BepInEx dentro? | **Sim**, oficial e sem alteração, com as licenças — instalado só se faltar |
| 5 | Onde entra a chave de ativação | **Dentro do jogo** (tela do menu principal) |
| 6 | O que funciona sem chave | Tudo menos a Diplomacia IA (a parte "premium"), ou um teste de 30 turnos. Prefiro a 1ª: simples e honesta |
| 7 | Limite de PCs por chave | **3**, com botão "liberar este PC" |
| 8 | Sem internet | **Aceitar sempre** o último resultado bom (o mod de IA já precisa de internet de qualquer jeito) |
| 9 | Ofuscação | Só **Obfuscar** (renomear), ou nada |
| 10 | Loja | **Lemon Squeezy** (após e-mail ao suporte), **Gumroad** como plano B |
| 11 | Onde fica o `version.json` | GitHub público só com o JSON e o changelog |
| 12 | MoreEmpires | Componente opcional do mesmo instalador, desmarcado por padrão |

## Não confirmado (testar)
- Smart App Control barra DLL .NET carregada pelo Mono do jogo? (testar num PC com SAC ligado)
- BepInEx no Humankind da Epic (sem relato).
- Se a Lemon Squeezy aceita pessoa física do Brasil (a documentação não diz que não; confirmar com o suporte).
- Preço exato da licença comercial do Inno Setup (só aparece no checkout; é opcional).
