# Relatórios de erro ("Relatar um problema")

Pedido do Discord (2026-10-14): ZRENYA teve o PC travando ao sair do jogo e não tinha como mandar os registros. Agora o
jogador manda tudo com um botão e recebe um código para citar no Discord ou no GitHub.

## Para o jogador

Botão fixo na barra de baixo da partida (balão com "!", o último; aparece desde o Neolítico) ou tela **Realpolitik** (menu principal ou de pausa) → **Relatar um problema**:
1. escreve o que aconteceu (opcional, até 1000 caracteres; o texto fica guardado entre seções);
2. **Enviar** → "Enviado. Seu código: R-XXXXXX";
3. **Abrir** abre a pasta com as cópias (`BepInEx\Realpolitik\reports\report-<data>.json.gz`). Se o envio falhar, dá
   para anexar o arquivo à mão.

## O que vai no pacote (`Support\ErrorReport.cs`)

JSON comprimido (gzip) com `schema`, `mod`, `game`, `language`, `note`, `created` e `files`:
- `LogOutput.log` (BepInEx, sessão atual);
- `session.log` e `session-prev.log`: cópia do log do BepInEx que **sobrevive à próxima abertura do jogo**
  (`Support\SessionLog.cs`). O BepInEx zera o `LogOutput.log` ao abrir, então depois de um travamento a sessão que deu
  problema sumia. Na primeira carga do núcleo num processo novo, `session.log` vira `session-prev.log`. A saída do jogo
  grava `# Quit requested (Application.quitting)` com a memória: se a linha falta no `session-prev.log`, o jogo não
  chegou a fechar direito;
- `Player.log` e `Player-prev.log` (Unity);
- `config/*.cfg`: todas as configurações (mod, MoreEmpires, BepInEx); as chaves ficam em `credenciais` e `deepseek.key`, que nunca vão;
- `game/`: os 2 últimos `Diagnostics*.html` e os 3 últimos `pass_turn_timeout_report*.txt` do próprio jogo (`DocumentosHumankindTemporary Files`);
- `ai/<partida>/`: os registros da IA da partida aberta (dossiê, pedido e resposta de cada nação), dos mais novos para os mais velhos, até 6 milhões de caracteres;
- `system.txt`: versões (mod, jogo, Unity, BepInEx), Windows, CPU, RAM, GPU, tela (resolução, modo, DPI), memória,
  idioma, plugins, partida aberta (turno, impérios), IA ligada e provedor.

Cada arquivo leva só o final (1,5 milhão de caracteres). Antes de sair, `Scrub` apaga chaves (`sk-`, `AIza`, `xai-`,
`gh?_`, JWT, `Bearer`, `api_key=`/`token=`/`password=`...), e-mails e o nome de usuário do Windows **dentro de caminhos**
(`C:\Users\<user>\`). Nada é opcional para o jogador: vai tudo o que ajuda a analisar, menos os segredos.

## Servidor (`server\reports\`)

Cloudflare Worker `realpolitik-reports` + bucket R2 `realpolitik-reports`, separado da loja antiga.
- `POST /report` (corpo gzip, até 8 MB) → `{"code": "R-XXXXXX"}`; salva em `reports/<dia>/<código>.json.gz`.
- Limites por IP (hash com sal, o IP não é guardado): 3 por minuto e 20 por dia.
- Regra de ciclo de vida no R2: relatórios apagados com 90 dias, contadores com 2.
- `GET /admin/list?days=N` e `GET /admin/get/<código>` com `Authorization: Bearer <REPORTS_ADMIN_TOKEN>`.

Comandos (PowerShell, de `_Modding`):
- publicar ou atualizar: `powershell -ExecutionPolicy Bypass -File server\reports\deploy-reports.ps1` (cria
  `REPORTS_ADMIN_TOKEN` e `REPORTS_SALT` no `.env` se faltarem; usa `CLOUDFLARE_API_KEY` e `CLOUDFLARE_ACCOUNT_ID`);
- listar: `server\reports\reports.ps1` (`-Days 30`);
- baixar e abrir um: `server\reports\reports.ps1 -Get R-XXXXXX` → `dev\out\reports\R-XXXXXX\` (um arquivo por log +
  `note.txt`);
- abrir um arquivo que o jogador anexou: `server\reports\reports.ps1 -File caminho\report-....json.gz`.

O endereço que o jogo usa fica no `.cfg`: `[Support] ReportServer` (vazio = só salva a cópia local).

## Testes

- `game report [nota]` envia como o botão; `game report status` mostra estado, código e a cópia local.
- Teste sem o servidor real: um `HttpListener` local em `http://localhost:8799/` respondendo `{"code":"R-TEST01"}` e
  `ReportServer` apontando para ele (feito em 2026-10-14: 65 KB enviados, nota com chave e e-mail limpos, nenhum
  `C:\Users\<nome>` no pacote).
