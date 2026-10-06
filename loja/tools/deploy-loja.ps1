# Monta/atualiza a loja: segredos, banco D1, bucket R2, produto + webhook na Stripe e o Worker.
# Idempotente: pode rodar de novo a cada mudança no worker.js. Nunca imprime segredos.
# Lê e grava _Modding\.env (segredos) e _Modding\loja\config.json (ids, nada secreto).
# Uso: powershell -ExecutionPolicy Bypass -File _Modding\loja\tools\deploy-loja.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$loja = Split-Path $PSScriptRoot -Parent
$mod = Split-Path $loja -Parent
$envPath = Join-Path $mod '.env'
$cfgPath = Join-Path $loja 'config.json'

$WorkerName = 'realpolitik-loja'
$DbName = 'realpolitik-loja'
$BucketName = 'realpolitik-releases'
$SiteUrl = 'https://realpolitik-living-nations.pages.dev'
$AllowedOrigins = "$SiteUrl,http://localhost:8790"
$PriceCents = 1000
$ProductName = 'Realpolitik: Living Nations for HUMANKIND'

# ---------- .env ----------
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
  [Convert]::ToBase64String($b)
}

$envv = Read-Env
# Segredos da loja: se forem perdidos, as chaves já vendidas param de funcionar. Guarde uma cópia do .env.
foreach ($s in @(@('LOJA_KEY_HMAC_SECRET', 32), @('LOJA_KEY_ENC_SECRET', 32), @('LOJA_DL_SECRET', 32), @('LOJA_ADMIN_TOKEN', 24))) {
  if (-not $envv.Contains($s[0])) { Add-EnvLine $s[0] (New-Secret $s[1]); "segredo $($s[0]) criado no .env" }
}
$envv = Read-Env
$stripeKey = if ($envv.Contains('STRIPE_SECRET_KEY')) { $envv['STRIPE_SECRET_KEY'] } else { $envv['STRIPE_API_KEY'] }
if (-not $stripeKey) { throw 'Falta STRIPE_SECRET_KEY (ou STRIPE_API_KEY) no .env' }
$cfToken = $envv['CLOUDFLARE_API_KEY']; $acc = $envv['CLOUDFLARE_ACCOUNT_ID']
$cfH = @{ Authorization = "Bearer $cfToken" }
$cfBase = "https://api.cloudflare.com/client/v4/accounts/$acc"

$cfg = if (Test-Path $cfgPath) { Get-Content $cfgPath -Raw | ConvertFrom-Json } else { New-Object psobject }
function Set-Cfg([string]$k, $v) { $cfg | Add-Member -NotePropertyName $k -NotePropertyValue $v -Force }
function Save-Cfg { [IO.File]::WriteAllText($cfgPath, ($cfg | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false))) }

function Stripe([string]$method, [string]$path, [hashtable]$form) {
  $p = @{ Uri = "https://api.stripe.com$path"; Method = $method; Headers = @{ Authorization = "Bearer $stripeKey" } }
  if ($form) {
    $p.ContentType = 'application/x-www-form-urlencoded'
    $p.Body = ($form.GetEnumerator() | ForEach-Object { [Uri]::EscapeDataString($_.Key) + '=' + [Uri]::EscapeDataString([string]$_.Value) }) -join '&'
  }
  Invoke-RestMethod @p
}

# ---------- D1 ----------
if (-not $cfg.d1_id) {
  $existing = (Invoke-RestMethod -Uri "$cfBase/d1/database?name=$DbName" -Headers $cfH).result | Where-Object { $_.name -eq $DbName }
  if ($existing) { Set-Cfg 'd1_id' $existing.uuid }
  else {
    $r = Invoke-RestMethod -Uri "$cfBase/d1/database" -Method Post -Headers $cfH -ContentType 'application/json' -Body (@{ name = $DbName } | ConvertTo-Json)
    Set-Cfg 'd1_id' $r.result.uuid; "D1 criado"
  }
  Save-Cfg
}
$schema = Get-Content (Join-Path $loja 'worker\schema.sql') -Raw
$schema = ($schema -split "`n" | Where-Object { $_ -notmatch '^\s*--' }) -join "`n"
$null = Invoke-RestMethod -Uri "$cfBase/d1/database/$($cfg.d1_id)/query" -Method Post -Headers $cfH -ContentType 'application/json; charset=utf-8' `
  -Body ([Text.Encoding]::UTF8.GetBytes((@{ sql = $schema } | ConvertTo-Json)))
"D1 ok (schema aplicado)"

# ---------- R2 ----------
$r2ok = $false
try {
  $buckets = (Invoke-RestMethod -Uri "$cfBase/r2/buckets" -Headers $cfH).result.buckets
  if (-not ($buckets | Where-Object { $_.name -eq $BucketName })) {
    $null = Invoke-RestMethod -Uri "$cfBase/r2/buckets" -Method Post -Headers $cfH -ContentType 'application/json' -Body (@{ name = $BucketName } | ConvertTo-Json)
    "R2 bucket criado"
  }
  $r2ok = $true; "R2 ok"
} catch { "AVISO: R2 indisponível ($($_.Exception.Message)). Worker sobe sem downloads; rode de novo depois de ativar o R2." }

# ---------- Stripe: produto, preço, webhook ----------
$workerUrl = "https://$WorkerName.$((Invoke-RestMethod -Uri "$cfBase/workers/subdomain" -Headers $cfH).result.subdomain).workers.dev"
Set-Cfg 'worker_url' $workerUrl
$live = $stripeKey -like '*_live_*'
Set-Cfg 'stripe_mode' $(if ($live) { 'live' } else { 'test' })
$pfx = if ($live) { 'live' } else { 'test' }

if (-not $cfg."stripe_${pfx}_price_id") {
  $prod = Stripe POST '/v1/products' @{ name = $ProductName; description = 'Single-player mod for HUMANKIND (PC, Steam). One-time purchase, free updates, license key for 3 PCs.' }
  $price = Stripe POST '/v1/prices' @{ product = $prod.id; unit_amount = $PriceCents; currency = 'usd' }
  Set-Cfg "stripe_${pfx}_product_id" $prod.id; Set-Cfg "stripe_${pfx}_price_id" $price.id; Save-Cfg
  "Stripe ($pfx): produto e preço criados"
}
$whName = "STRIPE_WEBHOOK_SECRET_$($pfx.ToUpper())"
if (-not $envv.Contains($whName)) {
  $events = @('checkout.session.completed', 'checkout.session.async_payment_succeeded', 'charge.refunded', 'charge.dispute.created', 'charge.dispute.closed')
  $form = @{ url = "$workerUrl/api/webhook"; description = 'Realpolitik loja (Cloudflare Worker)' }
  for ($i = 0; $i -lt $events.Count; $i++) { $form["enabled_events[$i]"] = $events[$i] }
  $wh = Stripe POST '/v1/webhook_endpoints' $form
  Add-EnvLine $whName $wh.secret
  Set-Cfg "stripe_${pfx}_webhook_id" $wh.id; Save-Cfg
  "Stripe ($pfx): webhook criado"
  $envv = Read-Env
}

# ---------- Worker ----------
$bindings = @(
  @{ type = 'd1'; name = 'DB'; id = $cfg.d1_id },
  @{ type = 'plain_text'; name = 'SITE_URL'; text = $SiteUrl },
  @{ type = 'plain_text'; name = 'ALLOWED_ORIGINS'; text = $AllowedOrigins },
  @{ type = 'plain_text'; name = 'STRIPE_PRICE_ID'; text = $cfg."stripe_${pfx}_price_id" },
  @{ type = 'plain_text'; name = 'STRIPE_AUTOMATIC_TAX'; text = 'false' },
  @{ type = 'secret_text'; name = 'STRIPE_SECRET_KEY'; text = $stripeKey },
  @{ type = 'secret_text'; name = 'STRIPE_WEBHOOK_SECRET'; text = $envv[$whName] },
  @{ type = 'secret_text'; name = 'KEY_HMAC_SECRET'; text = $envv['LOJA_KEY_HMAC_SECRET'] },
  @{ type = 'secret_text'; name = 'KEY_ENC_SECRET'; text = $envv['LOJA_KEY_ENC_SECRET'] },
  @{ type = 'secret_text'; name = 'DL_SECRET'; text = $envv['LOJA_DL_SECRET'] },
  @{ type = 'secret_text'; name = 'ADMIN_TOKEN'; text = $envv['LOJA_ADMIN_TOKEN'] }
)
if ($r2ok) { $bindings += @{ type = 'r2_bucket'; name = 'FILES'; bucket_name = $BucketName } }
if ($envv.Contains('RESEND_API_KEY')) {
  $bindings += @{ type = 'secret_text'; name = 'RESEND_API_KEY'; text = $envv['RESEND_API_KEY'] }
  $bindings += @{ type = 'plain_text'; name = 'MAIL_FROM'; text = $envv['LOJA_MAIL_FROM'] }
}
$meta = @{ main_module = 'worker.js'; compatibility_date = '2026-09-01'; bindings = $bindings } | ConvertTo-Json -Depth 5

$client = New-Object Net.Http.HttpClient
$client.DefaultRequestHeaders.Authorization = New-Object Net.Http.Headers.AuthenticationHeaderValue('Bearer', $cfToken)
$mp = New-Object Net.Http.MultipartFormDataContent
$mc = New-Object Net.Http.StringContent($meta, [Text.Encoding]::UTF8, 'application/json')
$mp.Add($mc, 'metadata')
$code = New-Object Net.Http.ByteArrayContent(, [IO.File]::ReadAllBytes((Join-Path $loja 'worker\worker.js')))
$code.Headers.ContentType = New-Object Net.Http.Headers.MediaTypeHeaderValue('application/javascript+module')
$mp.Add($code, 'worker.js', 'worker.js')
$resp = $client.PutAsync("$cfBase/workers/scripts/$WorkerName", $mp).Result
$body = $resp.Content.ReadAsStringAsync().Result
if (-not $resp.IsSuccessStatusCode) { throw "Upload do worker falhou: $($resp.StatusCode) $body" }
"Worker enviado"
$null = Invoke-RestMethod -Uri "$cfBase/workers/scripts/$WorkerName/subdomain" -Method Post -Headers $cfH -ContentType 'application/json' -Body '{"enabled":true}'
Save-Cfg
"Pronto: $workerUrl (Stripe $pfx, R2 $(if ($r2ok) { 'ligado' } else { 'DESLIGADO' }))"
