param([Parameter(Mandatory = $true)][string[]]$Commands, [int]$TimeoutSec = 30)

# Manda comandos pelo canal de desenvolvimento do mod (_Modding\dev\cmd.txt, lido pelo carregador com o jogo aberto) e
# mostra só as respostas novas do _Modding\dev\out\result.txt. Respostas longas vão para um arquivo próprio em dev\out
# (o result.txt diz "ok: N caracteres em <arquivo>").
# Uso: powershell -File _Modding\tools\dev\devcmd.ps1 -Commands @("ia status", "jogo estado")
$modding = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dev = Join-Path $modding 'dev'
$result = Join-Path $dev 'out\result.txt'
$start = if (Test-Path $result) { [IO.File]::ReadAllText($result, [Text.Encoding]::UTF8).Length } else { 0 }
[IO.File]::WriteAllLines((Join-Path $dev 'cmd.txt'), $Commands, (New-Object Text.UTF8Encoding($false)))
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$answers = 0
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 400
    if (Test-Path $result) {
        $text = [IO.File]::ReadAllText($result, [Text.Encoding]::UTF8)
        if ($text.Length -gt $start) {
            $new = $text.Substring($start)
            $answers = ([regex]::Matches($new, '(?m)^\[\d\d:\d\d:\d\d\] > ')).Count
            if ($answers -ge $Commands.Count) {
                $new
                return
            }
        }
    }
}
"(tempo esgotado: $answers de $($Commands.Count) respostas)"
if (Test-Path $result) {
    $text = [IO.File]::ReadAllText($result, [Text.Encoding]::UTF8)
    if ($text.Length -gt $start) { $text.Substring($start) }
}
