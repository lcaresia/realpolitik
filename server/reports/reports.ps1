# Reads the error reports players sent from the game.
#   reports.ps1                 lists the last 7 days
#   reports.ps1 -Days 30        lists the last 30 days
#   reports.ps1 -Get R-ABC123   downloads one report and unpacks its files into dev\out\reports\R-ABC123\
#   reports.ps1 -File x.json.gz unpacks a bundle a player attached by hand (Discord/GitHub)
# Reads REPORTS_ADMIN_TOKEN from _Modding\.env. Never prints it.
param([int]$Days = 7, [string]$Get, [string]$File, [string]$Server = 'https://realpolitik-reports.lcaresia.workers.dev')
$ErrorActionPreference = 'Stop'

$mod = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$token = $null
foreach ($l in [IO.File]::ReadAllLines((Join-Path $mod '.env'))) {
  if ($l -match '^\s*REPORTS_ADMIN_TOKEN\s*=(.*)$') { $token = $Matches[1].Trim() }
}

function Expand-Bundle([byte[]]$gz, [string]$name) {
  $in = New-Object IO.MemoryStream(, $gz)
  $unzip = New-Object IO.Compression.GZipStream($in, [IO.Compression.CompressionMode]::Decompress)
  $reader = New-Object IO.StreamReader($unzip, [Text.Encoding]::UTF8)
  $bundle = $reader.ReadToEnd() | ConvertFrom-Json
  $out = Join-Path $mod "dev\out\reports\$name"
  New-Item -ItemType Directory -Force $out | Out-Null
  $utf8 = New-Object Text.UTF8Encoding($false)
  foreach ($p in $bundle.files.PSObject.Properties) {
    $target = Join-Path $out ($p.Name -replace '/', '\')
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    [IO.File]::WriteAllText($target, [string]$p.Value, $utf8)
  }
  $summary = "mod $($bundle.mod) | game $($bundle.game) | language $($bundle.language) | created $($bundle.created)`r`nnote: $($bundle.note)"
  [IO.File]::WriteAllText((Join-Path $out 'note.txt'), $summary, $utf8)
  $summary
  "files: $out"
}

if ($File) {
  Expand-Bundle ([IO.File]::ReadAllBytes((Resolve-Path $File))) ([IO.Path]::GetFileNameWithoutExtension([IO.Path]::GetFileNameWithoutExtension($File)))
  return
}
if (-not $token) { throw 'Missing REPORTS_ADMIN_TOKEN in .env (run deploy-reports.ps1 first)' }
$headers = @{ Authorization = "Bearer $token" }
if ($Get) {
  $tmp = [IO.Path]::GetTempFileName()
  Invoke-WebRequest -Uri "$Server/admin/get/$Get" -Headers $headers -OutFile $tmp -UseBasicParsing
  Expand-Bundle ([IO.File]::ReadAllBytes($tmp)) $Get
  Remove-Item $tmp
  return
}
(Invoke-RestMethod -Uri "$Server/admin/list?days=$Days" -Headers $headers).reports |
  Sort-Object uploaded -Descending |
  Format-Table code, uploaded, @{ n = 'KB'; e = { [math]::Round($_.size / 1024) } }, agent, country -AutoSize
