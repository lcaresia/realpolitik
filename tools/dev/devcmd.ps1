param([Parameter(Mandatory = $true)][string[]]$Commands, [int]$TimeoutSec = 30)

# Sends commands through the mod's dev channel (_Modding\dev\cmd.txt, read by the loader while the game is running) and
# prints only the new answers from _Modding\dev\out\result.txt. Long answers go to their own file in dev\out
# (result.txt says "ok: N characters in <file>"). Command reference: _Modding\docs\dev-commands.md.
# Usage: powershell -File _Modding\tools\dev\devcmd.ps1 -Commands @("ai status", "game state")
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
"(timed out: $answers of $($Commands.Count) answers)"
if (Test-Path $result) {
    $text = [IO.File]::ReadAllText($result, [Text.Encoding]::UTF8)
    if ($text.Length -gt $start) { $text.Substring($start) }
}
