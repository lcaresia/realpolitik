# Publica uma versão do mod para os compradores: sobe o arquivo para o R2 (releases/<versão>/<arquivo>) e atualiza
# releases/manifest.json, que alimenta a página de download e o aviso "versão nova" no jogo (/api/version).
# Uso:
#   powershell -ExecutionPolicy Bypass -File _Modding\loja\tools\publicar-versao.ps1 -Versao 1.0.0 -Arquivo C:\...\Realpolitik_1.0.0.zip `
#     -Notas "O que mudou" -JogoTestado 1.31.4836 [-NaoMarcarComoUltima]
#   ... -Remover 1.0.0     (tira uma versão da lista e apaga o arquivo)
#   ... -Listar
# Confira antes: o pacote não pode levar .env, deepseek.key, credenciais nem _Modding\dev.
param(
  [string]$Versao,
  [string]$Arquivo,
  [string]$Notas = '',
  [string]$JogoTestado = '',
  [switch]$NaoMarcarComoUltima,
  [string]$Remover,
  [switch]$Listar,
  [string[]]$JogoQuebrado
)
$ErrorActionPreference = 'Stop'
$loja = Split-Path $PSScriptRoot -Parent
$mod = Split-Path $loja -Parent
$kv = @{}
foreach ($l in [IO.File]::ReadAllLines((Join-Path $mod '.env'))) { if ($l -match '^\s*[^#].*=') { $i = $l.IndexOf('='); $kv[$l.Substring(0, $i).Trim()] = $l.Substring($i + 1).Trim() } }
$cfg = Get-Content (Join-Path $loja 'config.json') -Raw | ConvertFrom-Json
$bucket = 'realpolitik-releases'
$base = "https://api.cloudflare.com/client/v4/accounts/$($kv['CLOUDFLARE_ACCOUNT_ID'])/r2/buckets/$bucket/objects"
$H = @{ Authorization = "Bearer $($kv['CLOUDFLARE_API_KEY'])" }

function Get-Manifest {
  try {
    $c = New-Object Net.WebClient; $c.Headers.Add('Authorization', $H.Authorization); $c.Encoding = [Text.Encoding]::UTF8
    $c.DownloadString("$base/releases/manifest.json") | ConvertFrom-Json
  } catch { [pscustomobject]@{ latest = $null; versions = @(); brokenOn = @() } }
}
function Put-Object([string]$key, [byte[]]$bytes, [string]$type) {
  $null = Invoke-RestMethod -Uri "$base/$key" -Method Put -Headers $H -ContentType $type -Body $bytes
}
function Save-Manifest($m) {
  $json = $m | ConvertTo-Json -Depth 5
  Put-Object 'releases/manifest.json' ([Text.Encoding]::UTF8.GetBytes($json)) 'application/json'
}
function Compare-Ver([string]$a, [string]$b) { try { ([version]($a -replace '-.*$', '')).CompareTo([version]($b -replace '-.*$', '')) } catch { [string]::Compare($a, $b) } }

$m = Get-Manifest
$list = @($m.versions | Where-Object { $_ })

if ($Listar) {
  "latest: $($m.latest)"
  $list | ForEach-Object { "  $($_.version)  $($_.file)  $([Math]::Round($_.size / 1MB, 1)) MB  $($_.date)" }
  return
}

if ($Remover) {
  $v = $list | Where-Object { $_.version -eq $Remover }
  if (-not $v) { throw "Versão $Remover não está no manifest" }
  $null = Invoke-RestMethod -Uri "$base/releases/$Remover/$($v.file)" -Method Delete -Headers $H
  $list = @($list | Where-Object { $_.version -ne $Remover })
  $m.versions = $list
  if ($m.latest -eq $Remover) { $m.latest = ($list | Sort-Object { [version]($_.version -replace '-.*$', '') } | Select-Object -Last 1).version }
  Save-Manifest $m
  "Removida $Remover. latest agora: $($m.latest)"
  return
}

if (-not $Versao -or -not $Arquivo) { throw 'Use -Versao e -Arquivo (ou -Listar / -Remover)' }
if ($Versao -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "Versão inválida: $Versao (use 1.2.3)" }
$full = (Resolve-Path $Arquivo).Path
$name = [IO.Path]::GetFileName($full)
if ($name -notmatch '^[\w.\-]+$') { throw "Nome de arquivo com caracteres estranhos: $name" }
# Regra do dono (2026-10-07): a loja recebe só o instalador. O zip manual fica em dist\<versão>\, nunca sobe.
if ($name -notmatch '^Realpolitik_Setup_.+\.exe$') { throw "Na loja sobe só o Realpolitik_Setup_<versão>.exe (recebido: $name)." }

# Trava de segurança: zip não pode levar segredos.
if ($name -like '*.zip') {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $z = [IO.Compression.ZipFile]::OpenRead($full)
  try {
    $bad = $z.Entries | Where-Object { $_.FullName -match '(^|/)\.env$|deepseek\.key|credenciais/|_Modding/dev/|\.key$' }
    if ($bad) { throw "O pacote leva arquivos proibidos: $(($bad | ForEach-Object FullName) -join ', ')" }
  } finally { $z.Dispose() }
}

$bytes = [IO.File]::ReadAllBytes($full)
$type = if ($name -like '*.zip') { 'application/zip' } elseif ($name -like '*.exe') { 'application/vnd.microsoft.portable-executable' } else { 'application/octet-stream' }
"Enviando $name ($([Math]::Round($bytes.Length / 1MB, 1)) MB)..."
Put-Object "releases/$Versao/$name" $bytes $type

$sha = -join ([Security.Cryptography.SHA256]::Create().ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') })
$entry = [pscustomobject]@{ version = $Versao; file = $name; size = $bytes.Length; sha256 = $sha; date = (Get-Date -Format 'yyyy-MM-dd'); notes = $Notas; testedGame = $JogoTestado }
$list = @($list | Where-Object { $_.version -ne $Versao }) + $entry
$list = @($list | Sort-Object { try { [version]($_.version -replace '-.*$', '') } catch { [version]'0.0' } })
$m.versions = $list
if (-not $NaoMarcarComoUltima -and (-not $m.latest -or (Compare-Ver $Versao $m.latest) -ge 0)) { $m.latest = $Versao }
if ($PSBoundParameters.ContainsKey('JogoQuebrado')) { $m | Add-Member -NotePropertyName brokenOn -NotePropertyValue $JogoQuebrado -Force }
Save-Manifest $m
"Publicada $Versao. latest: $($m.latest). Confira: $($cfg.worker_url)/api/version"
