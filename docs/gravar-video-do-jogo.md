# Gravar vídeo e prints do jogo pelo canal de comandos (sem mexer no mouse)

**Regra:** o jogo é controlável por `_Modding\dev\cmd.txt`. Abrir o jogo, carregar save, mover a câmera, dar ordens a
exércitos, passar turno, abrir telas, tirar print e **gravar vídeo** se faz tudo por comando. Nunca dizer que "não dá
para controlar o jogo". Os comandos completos estão em `guia-telas-nativas.md` (tabela "Comandos") e
`diplomacia-ia.md` (comandos `ia` e `jogo`). Este arquivo é o roteiro prático para gravar material de divulgação.

## 1. Pré-requisitos
- Núcleo compilado: `dotnet build _Modding\src\CurrencyMod -c Release` (a DLL é recarregada a quente; só o Loader exige reiniciar).
- ffmpeg **no WSL** (`wsl -e ffmpeg`). Não existe ffmpeg no Windows e não se instala (regra: sempre WSL).
- Abrir o jogo: `Start-Process "steam://rungameid/1124300"` (PowerShell). Leva ~20 s até o menu; o canal já responde no menu.

## 2. Como mandar comando e ler a resposta
Escrever uma linha em `dev\cmd.txt`; o Loader lê e apaga em ~1 s e escreve em `dev\out\result.txt` (acrescenta no fim).
Script de apoio usado nas sessões (Git Bash): escreve a linha, espera `result.txt` mudar e mostra o fim.
```bash
D="/c/Program Files (x86)/Steam/steamapps/common/Humankind/_Modding/dev"
before=$(stat -c %Y "$D/out/result.txt"); printf '%s\n' "$1" > "$D/cmd.txt"
for i in $(seq 1 60); do sleep 1; [ "$(stat -c %Y "$D/out/result.txt")" != "$before" ] && break; done
tail -c 1500 "$D/out/result.txt"
```
Comandos longos (carregar save, passar turno, gravar) demoram: espere o `result.txt` mudar ou consulte `jogo estado`
até aparecer `SandboxState_TurnMain` ("pode passar o turno: sim").

## 3. Receita: de zero até uma partida pronta para gravar
1. `jogo carregar <título>` (no menu). Se aparecer "Jogo salvo inválido", o save é de outro conjunto de mods:
   fechar o aviso com `click WindowsRoot/SystemFullscreen/MessageModalWindow/MessageBox/Table/Buttons/ButtonSample`
   e carregar outro. O save **Teste IA T102** (16 impérios, jogador Francos E1) funciona.
2. `ia off` **antes de qualquer turno**: desliga a IA de linguagem e **evita gastar a API** (cada turno com IA custa dinheiro).
3. `jogo estado` até `TurnMain`.
4. Preparar a cena (ver §5) e gravar.

## 4. O comando `rec` (vídeo)
`rec <nome> <segundos> [fps]` salva quadros JPG (qualidade 95, 1920×1080, **com a UI**) em `dev\out\rec_<nome>\f00000.jpg…`.
O tempo do jogo fica travado em `fps` (padrão 30) via `Time.captureFramerate`: a captura pode ser lenta, o vídeo sai liso.
Implementação: `DevTools.RecordFrames` em `src\CurrencyMod\DevTools.cs`. Para montar o MP4 (no WSL):
```bash
ffmpeg -framerate 30 -i dev/out/rec_<nome>/f%05d.jpg -c:v libx264 -crf 14 -pix_fmt yuv420p clip.mp4
```
Medido: 30 quadros em poucos segundos. Para ações que acontecem **durante a passagem de turno** (exército marchando, navios
atacando), dispare `jogo turno` e `rec ...` na mesma linha de tempo (primeiro `jogo turno`, logo em seguida `rec`).

## 5. Planos do vídeo de 60 s (como conseguir cada um)
| # | Plano | Como |
|---|---|---|
| 1 | Correio com cartas | `ia cartas E#` ou abrir a tela de Correio; `screenshot`/`rec` |
| 3 | Conselho | `ia conselho abrir` (já aberta no T102); `rec` de 6 a 8 s |
| 4 | Exército marchando | `jogo camera T#` no território; `ia exercito E# A# mover T#`; `jogo turno` + `rec` |
| 6 | Inteligência → Cartas | `ia espionagem …` / `ia interceptar …` (§13 de `diplomacia-ia.md`) |
| 8 | Banco Central, aba Ciclo | `nbank open` + `nbank tab 2` |
| 9 | Pedágio | `trade open <território>` |
| 10 | Mapa amplo | `jogo camera` + afastar com o zoom (a UI esconde com a tecla de olho) |

## 6. Armadilhas
- Print/vídeo logo após abrir uma janela pega a animação de abertura: esperar ~1 s.
- `ia off` não vale depois de reiniciar o jogo: repetir.
- O jogo precisa estar em primeiro plano para os comandos rodarem rápido.
- Nomes de líderes: no site foram trocados por nomes fictícios ("Shah"); nos vídeos de divulgação, conferir se há nome real de jogador.

## 7. O que aprendemos gravando (2026-10-07)
- **Idioma do jogo (vídeo em inglês):** o idioma nativo fica em `Documents\Humankind\Users\<id>\Registry.xml`
  (`<SelectedLanguage>pt-BR</SelectedLanguage>`). Feche o jogo (`jogo sair`), troque para `en-US`, reabra
  (`steam -shutdown`, `steam -silent`, `steam://rungameid/1124300`). Há cópia do original em `Registry.xml.bak-pt`;
  **restaure ao terminar** (o usuário joga em português).
- **Várias linhas no `cmd.txt`:** são executadas em sequência. O `rec` não bloqueia (é coroutine), então
  `rec nome 14 30` seguido de `jogo turno` grava a passagem de turno. Mas o fim de turno é quase instantâneo e depois
  do turno entram vários pop-ups de notificação; a marcha do exército **não** aparece animada (as ordens de IA são
  aplicadas na virada).
- **Cartas em inglês sem gastar API:** `ia teste carta-de E# [Assunto] texto` (agora aceita `[Assunto]` e
  `[Assunto|publica]`; com `[ ]` o assunto fica vazio). Aparece no Correio como carta recebida. Nações eliminadas
  aparecem como "Império N".
- **Câmera:** `jogo camera T#` desliza suavemente (serve de panorâmica); combine com `rec`.
- **Pop-ups de notificação no mapa** ("Attitude changed", "Embassy available"…): `hide NotificationBanner` e
  `click …OkButton` **não** os removem; clicar com o mouse do Windows (user32 `mouse_event`) também não funcionou.
  Gravar logo após carregar o save (só um card pequeno) ou usar telas cheias (Conselho, Correio, Banco Central), que
  cobrem o mapa. Pendente: achar um jeito de limpar os pop-ups.
- **`trade open T#`** não abriu a janela do posto neste save (a tela "Trade" dos prints do site veio de outra sessão);
  investigar `TradePostWindow.SetOpen` / `TradeViewWindow`.
- **Clipes gravados:** `build\assets\clips\{council,cycle,mail,pan}.mp4` (1080p, 30 fps, sem áudio). Montados com
  `ffmpeg -framerate 30 -i rec_<nome>/f%05d.jpg -c:v libx264 -crf 15 -pix_fmt yuv420p -an <nome>.mp4`.
- **Foco da janela:** `SetForegroundWindow` no `Humankind.exe` (PowerShell) ajuda os comandos a rodarem mais rápido.
