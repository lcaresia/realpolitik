# Passes ONE turn with test quality: waits for the language-model AI nations to decide the current turn, ends the turn
# and waits for the next main turn. Stops (STOPPED) when the game waits for a player decision or the turn does not advance.
# Usage: powershell -File _Modding\tools\dev\passturn.ps1
param([int]$WaitDecisionsSec = 420, [int]$WaitTurnSec = 480)
$dev = Join-Path $PSScriptRoot 'devcmd.ps1'

function Get-Turn {
    $s = & $dev -Commands @('game state') -TimeoutSec 30 | Out-String
    $m = [regex]::Match($s, '\bturn (\d+)')
    $state = [regex]::Match($s, 'sandbox: (\S+)').Groups[1].Value
    $can = $s -match 'can end turn: yes'
    return @{ Turn = $(if ($m.Success) { [int]$m.Groups[1].Value } else { -1 }); State = $state; CanEnd = $can; Raw = $s }
}

$start = Get-Turn
"current turn: $($start.Turn) ($($start.State))"

# 1) Waits for the current turn's decisions (every nation with "last decision" on this turn and nobody thinking).
$deadline = (Get-Date).AddSeconds($WaitDecisionsSec)
while ($true) {
    $st = & $dev -Commands @('ai status') -TimeoutSec 30 | Out-String
    $thinking = [regex]::Match($st, 'thinking now: (\d+)').Groups[1].Value
    $pending = @([regex]::Matches($st, '(E\d+) [^\n]*?last decision T(-?\d+)') | Where-Object { $_.Groups[1].Value -ne 'E0' -and [int]$_.Groups[2].Value -lt $start.Turn } | ForEach-Object { $_.Groups[1].Value })
    if ($thinking -eq '0' -and $pending.Count -eq 0) { "decisions of turn $($start.Turn): complete"; break }
    if ((Get-Date) -gt $deadline) { "warning: passing without waiting ($thinking thinking; no decision: $($pending -join ','))"; break }
    Start-Sleep -Seconds 15
}

# 2) Ends the turn.
$end = & $dev -Commands @('game turn') -TimeoutSec 30 | Out-String
$end.Trim()
if ($end -match 'wait for you to answer|waiting for you|you answer') {
    & $dev -Commands @('game proposals') -TimeoutSec 30
    "STOPPED: the game waits for an answer from the player (game answer <E#> accept|ignore)"
    return
}

# 3) Waits for the next main turn; every 2 min checks whether it is stuck (a battle waiting for the player).
$deadline = (Get-Date).AddSeconds($WaitTurnSec)
$lastCheck = Get-Date
while ($true) {
    Start-Sleep -Seconds 10
    $now = Get-Turn
    if ($now.Turn -gt $start.Turn -and $now.State -eq 'SandboxState_TurnMain') { "new turn: $($now.Turn)"; break }
    if (((Get-Date) - $lastCheck).TotalSeconds -gt 120) {
        $lastCheck = Get-Date
        $stuck = & $dev -Commands @('game stuck 5') -TimeoutSec 30 | Out-String
        if ($stuck -match 'battle') {
            & $dev -Commands @('game battle auto') -TimeoutSec 30 | Out-String
        }
    }
    if ((Get-Date) -gt $deadline) {
        "STOPPED: the turn did not advance in $WaitTurnSec s"
        & $dev -Commands @('game stuck 10') -TimeoutSec 30
        return
    }
}
