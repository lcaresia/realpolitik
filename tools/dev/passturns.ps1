# Passes turns up to the target, with a summary per turn (AI spending, tolls, Congress). Stops at the first "STOPPED".
# Usage: powershell -File _Modding\tools\dev\passturns.ps1 -Target 80
param([int]$Target = 75)
$dev = Join-Path $PSScriptRoot 'devcmd.ps1'
$pass = Join-Path $PSScriptRoot 'passturn.ps1'
$modding = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$congressFile = Join-Path $modding 'dev\out\congress.txt'
while ($true) {
    $s = & $dev -Commands @('game state') -TimeoutSec 30 | Out-String
    $turn = [int][regex]::Match($s, '\bturn (\d+)').Groups[1].Value
    if ($turn -ge $Target) { "target reached: turn $turn"; break }
    $out = & $pass | Out-String
    $out.Trim()
    if ($out -match 'STOPPED') { break }
    $sum = & $dev -Commands @('ai status', 'game routes') -TimeoutSec 30 | Out-String
    ($sum -split "`n") | Where-Object { $_ -match '^spending:|tolls at the last end of turn|Congress: verdicts' } | ForEach-Object { '  ' + $_.Trim() }
    & $dev -Commands @('ai congress') -TimeoutSec 60 | Out-Null
    Start-Sleep -Seconds 2
    if (Test-Path $congressFile) {
        Get-Content $congressFile -Encoding UTF8 | Where-Object { $_ -match 'next president|^law |LAW VOTE|^CRISIS|imposed' } | Select-Object -First 8 | ForEach-Object { '  ' + $_ }
    }
}
