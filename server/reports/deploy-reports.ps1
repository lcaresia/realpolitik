# Creates or updates the error report server: R2 bucket + lifecycle rules, secrets and the Worker.
# Idempotent: run again after every change to worker.js. Never prints secrets.
# Reads _Modding\.env (CLOUDFLARE_API_KEY, CLOUDFLARE_ACCOUNT_ID) and adds REPORTS_ADMIN_TOKEN / REPORTS_SALT when missing.
# Usage: powershell -ExecutionPolicy Bypass -File _Modding\server\reports\deploy-reports.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$here = $PSScriptRoot
$mod = Split-Path (Split-Path $here -Parent) -Parent
$envPath = Join-Path $mod '.env'

$WorkerName = 'realpolitik-reports'
$BucketName = 'realpolitik-reports'
$KeepDays = 90

function Read-Env {
  $kv = [ordered]@{}
  if (Test-Path $envPath) {
    foreach ($l in [IO.File]::ReadAllLines($envPath)) {
      if ($l -match '^\s*#' -or $l -notmatch '=') { continue }
      $i = $l.IndexOf('='); $kv[$l.Substring(0, $i).Trim()] = $l.Substring($i + 1).Trim()
    }
  }
  $kv
}
function Add-EnvLine([string]$name, [string]$value) {
  [IO.File]::AppendAllText($envPath, "`r`n$name=$value", (New-Object Text.UTF8Encoding($false)))
}
function New-Secret([int]$bytes) {
  $b = New-Object byte[] $bytes
  [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
  [Convert]::ToBase64String($b).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$envv = Read-Env
foreach ($s in @(@('REPORTS_ADMIN_TOKEN', 32), @('REPORTS_SALT', 32))) {
  if (-not $envv.Contains($s[0])) { Add-EnvLine $s[0] (New-Secret $s[1]); "secret $($s[0]) created in .env" }
}
$envv = Read-Env
$cfToken = $envv['CLOUDFLARE_API_KEY']; $acc = $envv['CLOUDFLARE_ACCOUNT_ID']
if (-not $cfToken -or -not $acc) { throw 'Missing CLOUDFLARE_API_KEY or CLOUDFLARE_ACCOUNT_ID in .env' }
$cfH = @{ Authorization = "Bearer $cfToken" }
$cfBase = "https://api.cloudflare.com/client/v4/accounts/$acc"

# ---------- R2 ----------
$buckets = (Invoke-RestMethod -Uri "$cfBase/r2/buckets" -Headers $cfH).result.buckets
if (-not ($buckets | Where-Object { $_.name -eq $BucketName })) {
  $null = Invoke-RestMethod -Uri "$cfBase/r2/buckets" -Method Post -Headers $cfH -ContentType 'application/json' -Body (@{ name = $BucketName } | ConvertTo-Json)
  "R2 bucket created"
}
# Reports expire after $KeepDays days; rate-limit counters after 2.
$rules = @{
  rules = @(
    @{ id = 'expire-reports'; enabled = $true; conditions = @{ prefix = 'reports/' }; deleteObjectsTransition = @{ condition = @{ type = 'Age'; maxAge = $KeepDays * 86400 } } },
    @{ id = 'expire-limits'; enabled = $true; conditions = @{ prefix = 'limits/' }; deleteObjectsTransition = @{ condition = @{ type = 'Age'; maxAge = 2 * 86400 } } }
  )
} | ConvertTo-Json -Depth 8
$null = Invoke-RestMethod -Uri "$cfBase/r2/buckets/$BucketName/lifecycle" -Method Put -Headers $cfH -ContentType 'application/json' -Body $rules
"R2 ok (reports kept $KeepDays days)"

# ---------- Worker ----------
$bindings = @(
  @{ type = 'r2_bucket'; name = 'REPORTS'; bucket_name = $BucketName },
  @{ type = 'secret_text'; name = 'ADMIN_TOKEN'; text = $envv['REPORTS_ADMIN_TOKEN'] },
  @{ type = 'secret_text'; name = 'SALT'; text = $envv['REPORTS_SALT'] }
)
$meta = @{ main_module = 'worker.js'; compatibility_date = '2026-09-01'; bindings = $bindings } | ConvertTo-Json -Depth 5

$client = New-Object Net.Http.HttpClient
$client.DefaultRequestHeaders.Authorization = New-Object Net.Http.Headers.AuthenticationHeaderValue('Bearer', $cfToken)
$mp = New-Object Net.Http.MultipartFormDataContent
$mp.Add((New-Object Net.Http.StringContent($meta, [Text.Encoding]::UTF8, 'application/json')), 'metadata')
$code = New-Object Net.Http.ByteArrayContent(, [IO.File]::ReadAllBytes((Join-Path $here 'worker.js')))
$code.Headers.ContentType = New-Object Net.Http.Headers.MediaTypeHeaderValue('application/javascript+module')
$mp.Add($code, 'worker.js', 'worker.js')
$resp = $client.PutAsync("$cfBase/workers/scripts/$WorkerName", $mp).Result
if (-not $resp.IsSuccessStatusCode) { throw "Worker upload failed: $($resp.StatusCode) $($resp.Content.ReadAsStringAsync().Result)" }
"Worker uploaded"
$null = Invoke-RestMethod -Uri "$cfBase/workers/scripts/$WorkerName/subdomain" -Method Post -Headers $cfH -ContentType 'application/json' -Body '{"enabled":true}'
$sub = (Invoke-RestMethod -Uri "$cfBase/workers/subdomain" -Headers $cfH).result.subdomain
"Ready: https://$WorkerName.$sub.workers.dev/report"
