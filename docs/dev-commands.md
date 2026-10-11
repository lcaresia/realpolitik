# Dev channel commands

The mod reads commands from `_Modding\dev\cmd.txt` (one per line, while the game is running) and appends the answers
to `_Modding\dev\out\result.txt`. Long answers (over 6000 characters) go to their own file in `dev\out` and
`result.txt` only says `ok: N characters in <file>`. Dumps and screenshots also go to `dev\out`.

Send commands with `tools\dev\devcmd.ps1`:

```
powershell -File _Modding\tools\dev\devcmd.ps1 -Commands @("ai status", "game state")
```

Answers start with `ok:` on success, `error:` on failure and `usage:` when the arguments are wrong.
Codes stay as in the game: `E#` empire, `T#` territory, `A#` army, `C#` city, `G#` grievance, `V#` law,
`K#` Congress crisis, `GRP#` chat group, `BLC#` bloc, `P#` portfolio.

Values that are saved or that the AI nations write (letter types, bloc motion kinds, council portfolio ids such as
`financas`, army goals inside the action JSON, config values such as `[IA] Raciocinio`) keep their stored spelling;
the command words around them are English.

## Top level (`DevTools.cs`)

| Old | New | What it does |
|---|---|---|
| `screenshot [name]` | `screenshot [name]` | captures the game screen to `dev\out\<name>.png` |
| `rec <nome> <segundos> [fps]` | `rec <name> <seconds> [fps]` | records jpg frames at a fixed fps into `dev\out\rec_<name>\` |
| `find <text>` | `find <text>` | labels whose visible text contains the text |
| `tree [visible\|all] [root]` | same | tree of UITransforms |
| `inspect <path> [depth]` | same | components, styles and serialized fields |
| `layout <path>` | same | state of a UILayout |
| `findtype <type>` | same | objects of a component type |
| `responders <x> <y>` | same | responders under a screen point, in click order |
| `windows` | same | game windows and whether they are open |
| — | `cameras` | cameras and UI views (depth, clear, target, components): diagnose rendering bugs |
| — | `resolution [WxH [window\|full\|exclusive]]` | shows or sets the screen resolution (recreates the screen buffers) |
| `styles [filter]` | same | UI style names |
| `show <Window>` / `hide <Window>` | same | opens/closes a game window by type |
| `hover <path>\|off` | same | opens/closes an element's native tooltip |
| `click <path>` | same | clicks a toggle or button (the action really happens) |
| `texdump <path>` | same | saves an image's texture as PNG and measures channels |
| `bank open\|close\|tab 0-3` | same | old Central Bank window |
| `nbank open\|close\|tab N\|scroll inicio\|fim\|ledger\|fake` | `nbank open\|close\|tab N\|scroll top\|bottom\|ledger\|fake` | native Central Bank window |
| `trade ...` | `trade ...` | trade post window (see below) |
| `ia ...` | `ai ...` | AI diplomacy (`Diplomacy\AiCommands.cs`) |
| `jogo ...` | `game ...` | game control (`GameCommands.cs`) |
| `idioma [code]` | `language [code]` | language in use; with a code (`Auto\|pt\|en\|es\|fr\|de`) switches it |
| `idioma faltando` | `language missing` | texts without translation, to `dev\out\language_missing_<code>.txt` |

### `trade` (`NativeUI\TradePostWindow.cs`)

| Old | New | What it does |
|---|---|---|
| `trade [info]` | same | trade rules, incidents, tolls, prices and window state |
| `trade open <territory>` / `trade close` | same | opens/closes the trade post window |
| `trade previa <dono> <território\|-1> <alvo> <preço> [bloqueio]` | `trade preview <owner> <territory\|-1> <target> <price> [block]` | detour preview (run twice to read the answer) |
| `trade price <empire> up\|down` | same | the row's + / − buttons |
| `trade mine` / `trade costs` | same | routes through my territories / route upkeep |
| `trade set <territory> <empire> free\|toll\|block` | same | sets a post rule |

## `game` (was `jogo`, `GameCommands.cs`)

| Old | New | What it does |
|---|---|---|
| `jogo estado` | `game state` | runtime/sandbox state, turn, whether the turn can be ended |
| `jogo saves` | `game saves` | lists saves (newest first) |
| `jogo saves reler` | `game saves rescan` | rereads the saves folder |
| `jogo carregar [título]` | `game load [title]` | loads a save (default: most recent) |
| `jogo salvar <título>` | `game save <title>` | saves the game |
| `jogo turno` | `game turn` | ends the turn, like the button |
| `jogo propostas` | `game proposals` | pending treaties/agreements with the player |
| `jogo responder <E#> aceitar\|ignorar` | `game answer <E#> accept\|ignore` | answers a pending proposal |
| `jogo propor <E#> economico\|informacao\|cultural\|militar\|alianca\|paz` | `game propose <E#> economic\|information\|cultural\|military\|alliance\|peace` | formal proposal by the player |
| `jogo crises [E#]` | `game crises [E#]` | grievances, demands and crises |
| `jogo exigir <E#> todas\|G#...` | `game demand <E#> all\|G#...` | turns grievances into demands |
| `jogo exigencias <E#> aceitar\|recusar\|enrolar\|retirar` | `game demands <E#> accept\|refuse\|stall\|withdraw` | answers demands (or withdraws own) |
| `jogo travado [n]` | `game stuck [n]` | why the end of turn does not finish, last n orders |
| `jogo batalha [auto\|recuar]` | `game battle [auto\|retreat]` | ongoing battles; answers those waiting for the player |
| `jogo humor [E#]` | `game mood [E#]` | native AI behaviour and rapport per nation |
| `jogo novo <2-16> [tamanho]` / `jogo novo restaurar` | `game new <2-16> [size]` / `game new restore` | starts a new test game / restores settings |
| `jogo espectador on\|off` | `game spectator on\|off` | native AI plays the local empire |
| `jogo sair` | `game quit` | quits the game normally |
| `jogo menu` | `game menu` | back to the main menu without saving |
| `jogo economia` | `game economy` | economy audit |
| `jogo juros <E#> <pct>\|auto` | `game interest <E#> <pct>\|auto` | forces an interest rate |
| `jogo rpn <name>` | `game rpn <name>` | dumps a game formula |
| — | `game erastars` | the player era stars: score, thresholds and how each deed is scored |
| `jogo detalhe industria\|estabilidade\|dinheiro [n]` | `game breakdown industry\|stability\|money [n]` | native tooltip breakdown |
| `jogo interacao ...` | `game interaction ...` | `esc\|tropa\|cidade\|fe\|comercio\|aba X\|diplomacia E#\|sociedade\|espionagem\|normal\|estado` → `esc\|unit\|city\|faith\|trade\|tab relations\|trade\|treaties\|crisis\|diplomacy E#\|society\|espionage\|normal\|state` |
| `jogo foco <path>\|off` | `game focus <path>\|off` | keyboard focus on a field |
| `jogo espiao E# T# [unidade]` | `game spy E# T# [unit]` | spawns a spy army |
| `jogo posto E# T#` | `game outpost E# T#` | founds an outpost |
| `jogo camera T#\|tile` | `game camera T#\|tile` | centers the camera |
| `jogo zoom 0..1` | `game zoom 0..1` | camera zoom |
| `jogo rendicao oferecer\|impor\|aceitar\|recusar\|cancelar E#` | `game surrender offer\|impose\|accept\|refuse\|cancel E#` | surrender flow |
| `jogo congresso ...` | `game congress ...` | see below |
| `jogo rotas [detalhe]` | `game routes [detail]` | trade blockade summary (with full paths) |
| `jogo rotas rotulos` | `game routes labels` | route value labels from the last render |
| `jogo rotas cores [E#\|todas]` | `game routes colors [E#\|all]` | colors routes as seen by another empire |
| `jogo civico E# Civico` | `game civic E# Civic` | an empire's current law |
| `jogo comprar E# luxo\|estrategico [E#]` | `game buy E# luxury\|strategic [E#]` | buys all resources of a category |
| `jogo pedagio E# E# livre\|pedagio\|bloqueio` | `game toll E# E# free\|toll\|block` | sets the mode on all of the first empire's posts |
| `jogo pedagio preco E# E# n` | `game toll price E# E# n` | general toll price |
| `jogo pedagio peso n` | `game toll weight n` | `PesoPedagioNasRotas` |
| `jogo pedagio teste E# T# T# E# E# [preço]` | `game toll test E# T# T# E# E# [price]` | inner-outpost step cost self-test |
| `jogo expansao [on\|off]` | `game expansion [on\|off]` | expansion in old saves |
| `jogo alerta <texto>\|off` | `game alert <text>\|off` | forces the Central Bank badge |

### `game congress`

| Old | New |
|---|---|
| `liberar E#` | `unlock E#` |
| `encerrar lei\|K#` | `close law\|K#` |
| `consulado E#` | `consulate E#` |
| `alavancagem E# E# n` | `leverage E# E# n` |
| `lei E# Civico` | `law E# Civic` |
| `propor V#` | `propose V#` |
| `votar lei A\|B` / `votar K# E#` | `vote law A\|B` / `vote K# E#` |
| `subornar lei\|K# E# n` | `bribe law\|K# E# n` |
| `cumprir\|guerra E#` | `comply\|war E#` |

## `ai` (was `ia`, `Diplomacy\AiCommands.cs`)

| Old | New | What it does |
|---|---|---|
| `ia status` | `ai status` | state, spending, nations |
| `ia raciocinio [valor]` | `ai reasoning [value]` | shows/changes `[IA] Raciocinio` (config values unchanged) |
| `ia acoes [turnos]` | `ai actions [turns]` | the nations' actions in the last turns |
| `ia acao E# {json}` | `ai action E# {json}` | runs an action through the decision path |
| `ia foto` | `ai snapshot` | takes a turn snapshot on the main thread |
| `ia dossie <n\|todas>` | `ai dossier <n\|all>` | writes the dossier to `dev\out\ai_dossier_E#.txt` |
| `ia prompt <n>` | `ai prompt <n>` | writes the full prompt to `dev\out\ai_prompt_E#.txt` |
| `ia agora [n]` | `ai now [n]` | makes a nation (or all) think now |
| `ia carta <n> <texto>` / `ia carta! ...` | `ai letter <n> <text>` / `ai letter! ...` | private letter (`!` = delivered this turn) |
| `ia cartas E#` | `ai letters E#` | opens the nation's diplomacy on the Letters tab |
| `ia on\|off` | `ai on\|off` | turns the AI on/off |
| `ia viewer` | `ai viewer` | opens the viewer |
| `ia reset` | `ai reset` | erases this game's AI memory |
| `ia correio ...` | `ai mail ...` | see below |
| `ia grupo ...` | `ai group ...` | `ver\|criar <nome> E# E#...\|msg GRP# <texto>` → `show\|create <name> E# E#...\|msg GRP# <text>` |
| `ia bloco ...` | `ai bloc ...` | see below |
| `ia aliancas ...` | `ai alliances ...` | see below |
| `ia mencoes [texto]` | `ai mentions [text]` | public statements that mention the player / tests a text |
| — | `ai injection <text>` | prompt-injection detector on a text (flagged/clean + neutralized) |
| `ia espionagem` | `ai espionage` | spies and interception chances (also `dev\out\espionage.txt`) |
| `ia espionagem unidades` | `ai espionage units` | agent unit types and stealthy armies |
| `ia espionagem aba [furtividade\|deteccao\|estado]` | `ai espionage tab [stealth\|detection\|state]` | intercepted letters tab |
| `ia espionagem simular E# T# [vigiar] [fixar]` | `ai espionage simulate E# T# [watch] [keep]` | simulated spy |
| `ia espionagem limpar` | `ai espionage clear` | removes simulated spies |
| `ia interceptar <id> E#` | `ai intercept <id> E#` | forces an interception |
| `ia exercitos E#` | `ai armies E#` | an empire's armies |
| `ia exercito E# A# mover\|atacar\|defender\|parar [alvo] [turnos]` | `ai army E# A# move\|attack\|defend\|stop [target] [turns]` | army order test |
| `ia congresso [agora]` | `ai congress [now]` | Congress diagnostics (also `dev\out\congress.txt`) |
| `ia conselho ...` | `ai council ...` | see below |
| `ia provedor ...` / `ia provedores` | `ai provider ...` / `ai providers` | see below |
| `ia teste carta-de E# [texto]\|parser E#\|congresso\|conselho-json` | `ai test letter-from E# [text]\|parser E#\|congress\|council-json` | bug-review scenarios |

### `ai mail` (`Diplomacy\UI\MailScreen.cs`)

`open` · `close` · `secao N` → `section N` (`tab N` still works) · `to E#` · `responder [id]` → `reply [id]` ·
`limpar` → `clear` · `colar [arquivo]` → `paste [file]` · `grupo N\|novo` → `group N\|new` · `membro E#` → `member E#` ·
`assunto <texto>` → `subject <text>` · `enviar` → `send` · `texto <...>` → `text <...>`

### `ai bloc` (`Diplomacy\Blocs\BlocCommands.cs`)

`ver` → `show` · `fundar <nome> E#...` → `found <name> E#...` · `aceitar BLC#` → `accept BLC#` ·
`recusar BLC#` → `decline BLC#` · `sair` → `leave` · `pautar <tipo> [E#] [arg]` → `propose <kind> [E#] [arg]` (motion
kinds keep their stored names: `tarifa`, `fim_tarifa`, `admitir`, `expulsar`, `congresso`, `renomear`, `dissolver`) ·
`abrir <id>` → `open <id>` · `descartar <id>` → `discard <id>` · `votar <id> sim\|nao` → `vote <id> yes\|no` ·
`presidente E#` → `president E#` · `turno [+N]` → `turn [+N]` · trailing `como E#` → `as E#`

### `ai alliances` (`Diplomacy\UI\AlliancesScreen.cs`)

`abrir` → `open` · `fechar` → `close` · `secao N` → `section N` · `tipo N` → `kind N` · `alvo E#` → `target E#` ·
`valor N` → `value N` · `texto ...` → `text ...` · `convidar E#` → `invite E#` · `enviar` → `send`

### `ai council` (`Diplomacy\AiCommands.cs`, `Diplomacy\UI\CouncilScreen.cs`)

`ver` → `show` (default) · `abrir` → `open` · `fechar` → `close` · `secao N` → `section N` · `falar <texto>` → `speak <text>` ·
`demitir <pasta>` → `fire <portfolio>` · `nomear <pasta> <id\|1\|2\|3>` → `appoint <portfolio> <id\|1\|2\|3>` ·
`fe T#` → `faith T#` · `veterano <pasta> <turnos>` → `veteran <portfolio> <turns>` · `candidatos` → `candidates` ·
`reunir\|convocar` → `convene`. Portfolio ids keep their stored names (`agricultura`, `financas`, `guerra`, ...).

### `ai provider` (`Diplomacy\Llm\Providers\ProviderCommands.cs`, `Diplomacy\UI\ProvidersScreen.cs`)

`lista` → `list` (default) · `ordem a,b` → `order a,b` · `modelo <id> <nome>` → `model <id> <name>` ·
`testar <id>` → `test <id>` · `falso <id>` → `fake <id>` · `chave-falsa <id>` → `fake-key <id>` ·
`apagar <id>` → `delete <id>` · `rota` → `route` · `semrede <id>` → `offline <id>` · `login-teste <id>` → `login-test <id>` ·
`login [id]` · `cancelar` → `cancel` · `tela ...` → `screen ...`:
`abrir [id\|uso]` → `open [id\|usage]` · `fechar` → `close` · `secao <id>` → `section <id>` · `lista N` → `list N` ·
`escolher N I` → `pick N I` · `enter`

## Output files in `dev\out`

| Old | New |
|---|---|
| `ia_dossie_E#.txt` / `ia_prompt_E#.txt` | `ai_dossier_E#.txt` / `ai_prompt_E#.txt` |
| `espionagem.txt` | `espionage.txt` |
| `congresso.txt` | `congress.txt` |
| `idioma_faltando_<code>.txt` | `language_missing_<code>.txt` |

## Scripts in `tools\dev`

| Script | What it does |
|---|---|
| `devcmd.ps1 -Commands @(...)` | sends commands and prints the new answers |
| `passturn.ps1` | waits for the AI decisions, ends one turn, waits for the next |
| `passturns.ps1 -Target N` | passes turns up to N with a short summary per turn |
| `backup.ps1` | zip backup of the project |
| `extract-texts.ps1` (was `extrair-textos.ps1`) | extracts the `L.T/L.F/L.N` texts from the code |
| `validate-translation.ps1` (was `validar-traducao.ps1`) | checks the `Lang\*.json` tables against the code |
