# Pedido de teste na loja, sem cobrança: cria uma compra falsa assinada como a Stripe faria, para ter uma chave de
# licença de verdade no jogo. A chave vai para _Modding\dev\out\chave-teste.txt (fora do Git) e não é impressa.
# Uso (na pasta do jogo):
#   powershell -ExecutionPolicy Bypass -File _Modding\loja\tools\pedido-teste.ps1 -Criar
#   ... -Ocupar 2        ativa a chave em N PCs falsos (testar o limite de 3)
#   ... -Reembolsar      manda o charge.refunded (a chave deixa de valer na próxima validação)
#   ... -Apagar          apaga do D1 tudo o que o pedido de teste criou (sempre rode no fim)
param([switch]$Criar, [int]$Ocupar = 0, [switch]$Reembolsar, [switch]$Apagar)
$ErrorActionPreference = 'Stop'
$loja = Split-Path $PSScriptRoot -Parent
$mod = Split-Path $loja -Parent
$kv = @{}
foreach ($l in [IO.File]::ReadAllLines((Join-Path $mod '.env'))) { if ($l -match '^\s*[^#].*=') { $i = $l.IndexOf('='); $kv[$l.Substring(0, $i).Trim()] = $l.Substring($i + 1).Trim() } }
$cfg = Get-Content (Join-Path $loja 'config.json') -Raw | ConvertFrom-Json
$W = $cfg.worker_url
$whSecret = $kv["STRIPE_WEBHOOK_SECRET_$($cfg.stripe_mode.ToUpper())"]
$out = Join-Path $mod 'dev\out'
New-Item -ItemType Directory -Force $out | Out-Null
$keyFile = Join-Path $out 'chave-teste.txt'
$stateFile = Join-Path $out 'pedido-teste.json'

function Call($m, $path, $body, [hashtable]$headers = @{}) {
  $p = @{ Uri = "$W$path"; Method = $m; UseBasicParsing = $true; Headers = $headers }
  if ($body -is [string]) { $p.Body = [Text.Encoding]::UTF8.GetBytes($body); $p.ContentType = 'application/json' }
  elseif ($body) { $p.Body = ($body | ConvertTo-Json -Compress); $p.ContentType = 'application/json' }
  try { $r = Invoke-WebRequest @p; [pscustomobject]@{ code = [int]$r.StatusCode; json = ($r.Content | ConvertFrom-Json) } }
  catch { $code = [int]$_.Exception.Response.StatusCode; $j = $null; try { $j = $_.ErrorDetails.Message | ConvertFrom-Json } catch {}; [pscustomobject]@{ code = $code; json = $j } }
}
function Send-Event([hashtable]$ev) {
  $payload = $ev | ConvertTo-Json -Depth 6 -Compress
  $t = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
  $h = New-Object Security.Cryptography.HMACSHA256 (, [Text.Encoding]::UTF8.GetBytes($whSecret))
  $sig = -join ($h.ComputeHash([Text.Encoding]::UTF8.GetBytes("$t.$payload")) | ForEach-Object { $_.ToString('x2') })
  Call POST '/api/webhook' $payload @{ 'Stripe-Signature' = "t=$t,v1=$sig" }
}

if ($Criar) {
  $tag = 'TESTE' + (Get-Random -Maximum 999999)
  $state = @{ tag = $tag; sid = "cs_$($cfg.stripe_mode)_$tag"; pi = "pi_$tag"; email = "$($tag.ToLower())@example.invalid" }
  $r = Send-Event @{ id = "evt_$tag-1"; type = 'checkout.session.completed'; data = @{ object = @{ id = $state.sid; payment_status = 'paid'; payment_intent = $state.pi; amount_total = 1000; currency = 'usd'; customer_details = @{ email = $state.email } } } }
  if ($r.code -ne 200) { throw "webhook recusou: $($r.code)" }
  $r = Call GET "/api/order?session_id=$($state.sid)"
  if ($r.json.key -notmatch '^RPLN-') { throw "chave não veio: $($r.code)" }
  [IO.File]::WriteAllText($keyFile, $r.json.key)
  $state | ConvertTo-Json | Set-Content $stateFile -Encoding UTF8
  "ok: pedido $tag criado; chave (…$($r.json.key.Substring($r.json.key.Length - 4))) em $keyFile"
}
if (-not (Test-Path $stateFile)) { if (-not $Criar) { throw 'nenhum pedido de teste (use -Criar)' } }
$state = Get-Content $stateFile -Raw | ConvertFrom-Json
if ($Ocupar -gt 0) {
  $key = [IO.File]::ReadAllText($keyFile).Trim()
  foreach ($n in 1..$Ocupar) {
    $r = Call POST '/api/license/activate' @{ key = $key; instance_name = "PC-TESTE-$n" }
    "PC-TESTE-${n}: $($r.code) usage=$($r.json.usage) $($r.json.error)"
  }
}
if ($Reembolsar) {
  $r = Send-Event @{ id = "evt_$($state.tag)-2"; type = 'charge.refunded'; data = @{ object = @{ id = "ch_$($state.tag)"; payment_intent = $state.pi } } }
  "reembolso: HTTP $($r.code)"
}
if ($Apagar) {
  $e = $state.email
  $sql = "DELETE FROM activations WHERE order_id IN (SELECT id FROM orders WHERE email = '$e'); DELETE FROM deactivations WHERE order_id IN (SELECT id FROM orders WHERE email = '$e'); DELETE FROM codes WHERE order_id IN (SELECT id FROM orders WHERE email = '$e'); DELETE FROM orders WHERE email = '$e'; DELETE FROM events WHERE id LIKE 'evt_$($state.tag)%'; DELETE FROM ratelimit;"
  $cfH = @{ Authorization = "Bearer $($kv['CLOUDFLARE_API_KEY'])" }
  $null = Invoke-RestMethod -Uri "https://api.cloudflare.com/client/v4/accounts/$($kv['CLOUDFLARE_ACCOUNT_ID'])/d1/database/$($cfg.d1_id)/query" -Method Post -Headers $cfH -ContentType 'application/json' -Body (@{ sql = $sql } | ConvertTo-Json)
  Remove-Item $keyFile, $stateFile -Force -Confirm:$false -ErrorAction SilentlyContinue
  "ok: pedido $($state.tag) apagado do D1"
}
