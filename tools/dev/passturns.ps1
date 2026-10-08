# Passa turnos até o alvo, com um resumo por turno (gasto da IA, pedágios, Congresso). Para no primeiro "PARADO".
# Uso: powershell -File _Modding\tools\dev\passturns.ps1 -Target 80
param([int]$Target = 75)
$dev = Join-Path $PSScriptRoot 'devcmd.ps1'
$pass = Join-Path $PSScriptRoot 'passturn.ps1'
$modding = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$congressFile = Join-Path $modding 'dev\out\congresso.txt'
while ($true) {
    $s = & $dev -Commands @('jogo estado') -TimeoutSec 30 | Out-String
    $turn = [int][regex]::Match($s, 'turno (\d+)').Groups[1].Value
    if ($turn -ge $Target) { "alvo alcançado: turno $turn"; break }
    $out = & $pass | Out-String
    $out.Trim()
    if ($out -match 'PARADO') { break }
    $sum = & $dev -Commands @('ia status', 'jogo rotas') -TimeoutSec 30 | Out-String
    ($sum -split "`n") | Where-Object { $_ -match '^gasto:|pedágios do último|Congresso: vereditos' } | ForEach-Object { '  ' + $_.Trim() }
    & $dev -Commands @('ia congresso') -TimeoutSec 60 | Out-Null
    Start-Sleep -Seconds 2
    if (Test-Path $congressFile) {
        Get-Content $congressFile -Encoding UTF8 | Where-Object { $_ -match 'próximo presidente|^lei |VOTAÇÃO|^CRISE|imposta' } | Select-Object -First 8 | ForEach-Object { '  ' + $_ }
    }
}
