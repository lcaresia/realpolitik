# Deploys a static folder to Cloudflare Pages via the Direct Upload API (no Node/wrangler needed).
# Usage: powershell -ExecutionPolicy Bypass -File tools\deploy-cloudflare.ps1 [-Dir ..\live] [-Project realpolitik-living-nations]
# Reads CLOUDFLARE_API_KEY (an API token with "Cloudflare Pages: Edit") and CLOUDFLARE_ACCOUNT_ID from _Modding\.env.
# Never prints the token.
param(
  [string]$Dir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'live'),
  [string]$Project = 'realpolitik-living-nations',
  [string]$Branch = 'main'
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http

# --- secrets from .env ---
$envFile = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.env'
$cfg = @{}
Get-Content $envFile | ForEach-Object { if ($_ -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*"?([^"]*)"?\s*$') { $cfg[$matches[1]] = $matches[2].Trim() } }
$token = $cfg['CLOUDFLARE_API_KEY']; if (-not $token) { $token = $cfg['CLOUDFLARE_API_TOKEN'] }
$acct = $cfg['CLOUDFLARE_ACCOUNT_ID']
if (-not $token -or -not $acct) { throw 'CLOUDFLARE_API_KEY / CLOUDFLARE_ACCOUNT_ID missing in .env' }
$api = 'https://api.cloudflare.com/client/v4'

function CF($method, $url, $body = $null, $bearer = $token) {
  $h = @{ Authorization = "Bearer $bearer" }
  try {
    if ($null -ne $body) { return Invoke-RestMethod -Method $method -Uri $url -Headers $h -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $body -Depth 10 -Compress))) }
    return Invoke-RestMethod -Method $method -Uri $url -Headers $h
  } catch {
    $msg = $_.ErrorDetails.Message; if (-not $msg) { $msg = $_.Exception.Message }
    throw "Cloudflare API $method $($url -replace [regex]::Escape($acct), '<account>') failed: $msg"
  }
}

# --- 1. project (create if missing) ---
try { $p = CF GET "$api/accounts/$acct/pages/projects/$Project"; "project exists: $Project" }
catch {
  if ("$_" -match '8000007|\(404\)|not found|could not be found') {
    $p = CF POST "$api/accounts/$acct/pages/projects" @{ name = $Project; production_branch = $Branch }
    "project created: $Project"
  } else { throw }
}

# --- 2. files + content hashes ---
$types = @{ '.html'='text/html'; '.css'='text/css'; '.js'='application/javascript'; '.json'='application/json'; '.svg'='image/svg+xml';
  '.png'='image/png'; '.jpg'='image/jpeg'; '.jpeg'='image/jpeg'; '.webp'='image/webp'; '.avif'='image/avif'; '.ico'='image/x-icon';
  '.txt'='text/plain'; '.xml'='application/xml'; '.mp4'='video/mp4'; '.webm'='video/webm'; '.woff2'='font/woff2' }
$root = (Resolve-Path $Dir).Path
$sha = [Security.Cryptography.SHA256]::Create()
$files = Get-ChildItem $root -Recurse -File | Where-Object { $_.Name -notmatch '^\.' } | ForEach-Object {
  $bytes = [IO.File]::ReadAllBytes($_.FullName)
  $b64 = [Convert]::ToBase64String($bytes)
  $ext = $_.Extension.TrimStart('.').ToLower()
  $hash = (($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($b64 + $ext)) | ForEach-Object { $_.ToString('x2') }) -join '').Substring(0, 32)
  [pscustomobject]@{ Path = '/' + $_.FullName.Substring($root.Length + 1).Replace('\', '/'); Hash = $hash; B64 = $b64
    Type = $(if ($types[$_.Extension.ToLower()]) { $types[$_.Extension.ToLower()] } else { 'application/octet-stream' }); Size = $bytes.Length }
}
"files: $($files.Count), {0:N1} MB" -f (($files | Measure-Object Size -Sum).Sum / 1MB)

# --- 3. upload missing assets with the project upload JWT ---
$jwt = (CF GET "$api/accounts/$acct/pages/projects/$Project/upload-token").result.jwt
$hashes = @($files | Select-Object -ExpandProperty Hash -Unique)
$missing = @((CF POST "$api/pages/assets/check-missing" @{ hashes = $hashes } $jwt).result)
"missing on Cloudflare: $($missing.Count)"
$todo = @($files | Where-Object { $missing -contains $_.Hash } | Sort-Object Hash -Unique)
$batch = @(); $batchSize = 0
foreach ($f in $todo + @($null)) {
  if ($f -and ($batchSize + $f.B64.Length -lt 20MB) -and $batch.Count -lt 400) { $batch += $f; $batchSize += $f.B64.Length; continue }
  if ($batch.Count) {
    $payload = @($batch | ForEach-Object { @{ key = $_.Hash; value = $_.B64; metadata = @{ contentType = $_.Type }; base64 = $true } })
    $null = CF POST "$api/pages/assets/upload" $payload $jwt
    "uploaded $($batch.Count) file(s)"
  }
  $batch = @(); $batchSize = 0
  if ($f) { $batch += $f; $batchSize += $f.B64.Length }
}
$null = CF POST "$api/pages/assets/upsert-hashes" @{ hashes = $hashes } $jwt

# --- 4. create the deployment from the manifest ---
$manifest = [ordered]@{}; $files | ForEach-Object { $manifest[$_.Path] = $_.Hash }
$client = New-Object Net.Http.HttpClient
$client.DefaultRequestHeaders.Authorization = New-Object Net.Http.Headers.AuthenticationHeaderValue('Bearer', $token)
$form = New-Object Net.Http.MultipartFormDataContent
$form.Add((New-Object Net.Http.StringContent(($manifest | ConvertTo-Json -Compress))), 'manifest')
$form.Add((New-Object Net.Http.StringContent($Branch)), 'branch')
$resp = $client.PostAsync("$api/accounts/$acct/pages/projects/$Project/deployments", $form).Result
$json = $resp.Content.ReadAsStringAsync().Result | ConvertFrom-Json
if (-not $json.success) { throw "deployment failed: $(($json.errors | ConvertTo-Json -Compress))" }
"deployment: $($json.result.url)"
"production: https://$Project.pages.dev"
