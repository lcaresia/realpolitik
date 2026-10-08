# Passa UM turno com qualidade de teste: espera as nações da IA de linguagem decidirem o turno atual, passa o turno e
# espera o próximo turno principal. Para (PARADO) quando o jogo espera uma decisão do jogador ou não vira o turno.
# Uso: powershell -File _Modding\tools\dev\passturn.ps1
param([int]$WaitDecisionsSec = 420, [int]$WaitTurnSec = 480)
$dev = Join-Path $PSScriptRoot 'devcmd.ps1'

function Get-Turn {
    $s = & $dev -Commands @('jogo estado') -TimeoutSec 30 | Out-String
    $m = [regex]::Match($s, 'turno (\d+)')
    $state = [regex]::Match($s, 'sandbox: (\S+)').Groups[1].Value
    $can = $s -match 'pode passar o turno: sim'
    return @{ Turn = $(if ($m.Success) { [int]$m.Groups[1].Value } else { -1 }); State = $state; CanEnd = $can; Raw = $s }
}

$start = Get-Turn
"turno atual: $($start.Turn) ($($start.State))"

# 1) Espera as decisões do turno atual (todas as nações com "última decisão" no turno e ninguém pensando).
$deadline = (Get-Date).AddSeconds($WaitDecisionsSec)
while ($true) {
    $st = & $dev -Commands @('ia status') -TimeoutSec 30 | Out-String
    $thinking = [regex]::Match($st, 'pensando agora: (\d+)').Groups[1].Value
    $pending = @([regex]::Matches($st, '(E\d+) [^\n]*?última decisão T(-?\d+)') | Where-Object { $_.Groups[1].Value -ne 'E0' -and [int]$_.Groups[2].Value -lt $start.Turn } | ForEach-Object { $_.Groups[1].Value })
    if ($thinking -eq '0' -and $pending.Count -eq 0) { "decisões do turno $($start.Turn): completas"; break }
    if ((Get-Date) -gt $deadline) { "aviso: passando sem esperar ($thinking pensando; sem decisão: $($pending -join ','))"; break }
    Start-Sleep -Seconds 15
}

# 2) Passa o turno.
$end = & $dev -Commands @('jogo turno') -TimeoutSec 30 | Out-String
$end.Trim()
if ($end -match 'proposta|espera o jogador|aguarda|esperar voc|voc. responde') {
    & $dev -Commands @('jogo propostas') -TimeoutSec 30
    "PARADO: o jogo espera uma resposta do jogador (jogo responder <E#> aceitar|ignorar)"
    return
}

# 3) Espera o próximo turno principal; a cada 2 min confere se está travado (batalha esperando o jogador).
$deadline = (Get-Date).AddSeconds($WaitTurnSec)
$lastCheck = Get-Date
while ($true) {
    Start-Sleep -Seconds 10
    $now = Get-Turn
    if ($now.Turn -gt $start.Turn -and $now.State -eq 'SandboxState_TurnMain') { "novo turno: $($now.Turn)"; break }
    if (((Get-Date) - $lastCheck).TotalSeconds -gt 120) {
        $lastCheck = Get-Date
        $stuck = & $dev -Commands @('jogo travado 5') -TimeoutSec 30 | Out-String
        if ($stuck -match 'batalha') {
            & $dev -Commands @('jogo batalha auto') -TimeoutSec 30 | Out-String
        }
    }
    if ((Get-Date) -gt $deadline) {
        "PARADO: o turno não virou em $WaitTurnSec s"
        & $dev -Commands @('jogo travado 10') -TimeoutSec 30
        return
    }
}
