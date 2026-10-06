# Teste de ponta a ponta do Worker da loja, sem cobrança real: manda eventos da Stripe assinados com o segredo do
# webhook (como a Stripe faria), percorre licença e download, e no fim apaga do D1 tudo o que o teste criou.
# Uso: powershell -ExecutionPolicy Bypass -File _Modding\loja\tools\testar-loja.ps1
$ErrorActionPreference = 'Stop'
$loja = Split-Path $PSScriptRoot -Parent
$mod = Split-Path $loja -Parent
$kv = @{}
foreach ($l in [IO.File]::ReadAllLines((Join-Path $mod '.env'))) { if ($l -match '^\s*[^#].*=') { $i = $l.IndexOf('='); $kv[$l.Substring(0, $i).Trim()] = $l.Substring($i + 1).Trim() } }
$cfg = Get-Content (Join-Path $loja 'config.json') -Raw | ConvertFrom-Json
$W = $cfg.worker_url
$whSecret = $kv["STRIPE_WEBHOOK_SECRET_$($cfg.stripe_mode.ToUpper())"]
$script:fail = 0

function Call($m, $path, $body, [hashtable]$headers = @{}) {
  $p = @{ Uri = "$W$path"; Method = $m; UseBasicParsing = $true; Headers = $headers }
  if ($body -is [string]) { $p.Body = [Text.Encoding]::UTF8.GetBytes($body); $p.ContentType = 'application/json' }
  elseif ($body) { $p.Body = ($body | ConvertTo-Json -Compress); $p.ContentType = 'application/json' }
  try { $r = Invoke-WebRequest @p; [pscustomobject]@{ code = [int]$r.StatusCode; json = ($r.Content | ConvertFrom-Json) } }
  catch { $code = [int]$_.Exception.Response.StatusCode; $j = $null; try { $j = $_.ErrorDetails.Message | ConvertFrom-Json } catch {}; [pscustomobject]@{ code = $code; json = $j } }
}
function Check([string]$name, [bool]$ok, $detail) {
  if ($ok) { "  OK   $name" } else { "  FALHA $name -> $detail"; $script:fail++ }
}
function Send-Event([hashtable]$ev) {
  $payload = $ev | ConvertTo-Json -Depth 6 -Compress
  $t = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
  $h = New-Object Security.Cryptography.HMACSHA256 (, [Text.Encoding]::UTF8.GetBytes($whSecret))
  $sig = -join ($h.ComputeHash([Text.Encoding]::UTF8.GetBytes("$t.$payload")) | ForEach-Object { $_.ToString('x2') })
  Call POST '/api/webhook' $payload @{ 'Stripe-Signature' = "t=$t,v1=$sig" }
}

$tag = 'TESTE' + (Get-Random -Maximum 999999)
$sid = "cs_$($cfg.stripe_mode)_$tag"
$pi = "pi_$tag"
"Worker: $W"

"Webhook"
$r = Call POST '/api/webhook' '{"id":"evt_x"}' @{ 'Stripe-Signature' = 't=1,v1=00' }
Check 'assinatura falsa recusada' ($r.code -eq 400) $r.code
$ev = @{ id = "evt_$tag-1"; type = 'checkout.session.completed'; data = @{ object = @{ id = $sid; payment_status = 'paid'; payment_intent = $pi; amount_total = 1000; currency = 'usd'; customer_details = @{ email = "$tag@example.invalid".ToLower() } } } }
$r = Send-Event $ev
Check 'compra aceita' ($r.code -eq 200) "$($r.code) $($r.json | ConvertTo-Json -Compress)"
$r = Send-Event $ev
Check 'evento repetido ignorado' ($r.json.duplicate -eq $true) ($r.json | ConvertTo-Json -Compress)

"Página obrigado"
$r = Call GET "/api/order?session_id=$sid"
Check 'mostra a chave' ($r.code -eq 200 -and $r.json.key -match '^RPLN-[0-9A-Z]{5}(-[0-9A-Z]{5}){3}$') $r.code
$key = $r.json.key
$code1 = $r.json.code

"Recuperar pelo recibo"
$r = Call POST '/api/recover' @{ email = "$tag@example.invalid".ToLower(); receipt = '1234-5678' }
Check 'recibo errado não revela a chave' ($r.code -eq 404 -and -not $r.json.key) "$($r.code)"

"Licença"
$ids = @()
foreach ($n in 1..3) {
  $r = Call POST '/api/license/activate' @{ key = $key; instance_name = "PC-$n" }
  Check "ativa PC $n" ($r.json.activated -eq $true -and $r.json.usage -eq $n) ($r.json | ConvertTo-Json -Compress)
  $ids += $r.json.instance_id
}
$r = Call POST '/api/license/activate' @{ key = $key; instance_name = 'PC-1' }
Check 'mesmo PC não gasta vaga' ($r.json.activated -eq $true -and $r.json.instance_id -eq $ids[0]) ($r.json | ConvertTo-Json -Compress)
$r = Call POST '/api/license/activate' @{ key = $key; instance_name = 'PC-4' }
Check '4º PC barrado' ($r.code -eq 409 -and $r.json.error -eq 'limit_reached') "$($r.code)"
$r = Call POST '/api/license/activate' @{ key = ($key.ToLower() -replace '-', ' '); instance_name = 'PC-1' }
Check 'chave digitada em minúsculas/espaços aceita' ($r.json.activated -eq $true) ($r.json | ConvertTo-Json -Compress)
$r = Call POST '/api/license/validate' @{ key = $key; instance_id = $ids[1] }
Check 'valida' ($r.json.valid -eq $true) ($r.json | ConvertTo-Json -Compress)
$r = Call POST '/api/license/activate' @{ key = 'RPLN-AAAAA-BBBBB-CCCCC-DDDDD'; instance_name = 'x' }
Check 'chave inventada recusada' ($r.code -eq 404) $r.code
$r = Call POST '/api/license/deactivate' @{ key = $key; instance_id = $ids[2] }
Check 'libera PC 3' ($r.json.deactivated -eq $true -and $r.json.usage -eq 2) ($r.json | ConvertTo-Json -Compress)
$r = Call POST '/api/license/activate' @{ key = $key; instance_name = 'PC-4' }
Check 'PC 4 entra na vaga' ($r.json.activated -eq $true) ($r.json | ConvertTo-Json -Compress)
$ids += $r.json.instance_id
$r = Call POST '/api/license/deactivate' @{ key = $key; instance_id = $ids[3] }; $null = $r
$r = Call POST '/api/license/activate' @{ key = $key; instance_name = 'PC-5' }; $ids += $r.json.instance_id
$r = Call POST '/api/license/deactivate' @{ key = $key; instance_id = $ids[4] }
Check '3ª liberação no mês ok' ($r.json.deactivated -eq $true) ($r.json | ConvertTo-Json -Compress)
$r = Call POST '/api/license/activate' @{ key = $key; instance_name = 'PC-6' }; $ids += $r.json.instance_id
$r = Call POST '/api/license/deactivate' @{ key = $key; instance_id = $ids[5] }
Check '4ª liberação no mês barrada' ($r.code -eq 429) "$($r.code)"

"Download"
$r = Call POST '/api/download/code' @{ key = $key }
Check 'jogo recebe código' ($r.json.code.Length -eq 48) ($r.json | ConvertTo-Json -Compress)
$code2 = $r.json.code
$r = Call POST '/api/download/list' @{ code = $code2 }
Check 'código aceito (503 = ainda sem versões no R2)' ($r.code -eq 200 -or ($r.code -eq 503 -and $r.json.error -eq 'no_releases')) "$($r.code)"
$r = Call POST '/api/download/list' @{ code = $code2 }
Check 'código não vale duas vezes' ($r.code -eq 403) "$($r.code)"
$r = Call GET "/dl/releases/1.0.0/x.zip?exp=9999999999&sig=00"
Check 'link de download falso recusado' ($r.code -eq 403) "$($r.code)"
$r = Call POST '/api/download/list' @{ key = $key }
if ($r.code -eq 200) {
  $v = @($r.json.versions | Where-Object { $_.version -eq $r.json.latest })[0]
  $tmp = [IO.Path]::GetTempFileName()
  try {
    Invoke-WebRequest -Uri $v.url -OutFile $tmp -UseBasicParsing
    $sha = -join ([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($tmp)) | ForEach-Object { $_.ToString('x2') })
    Check "baixa a versão $($v.version) íntegra (sha256)" ($sha -eq $v.sha256) $sha
  } finally { Remove-Item $tmp -Confirm:$false }
  $bad = $v.url -replace 'sig=[0-9a-f]{4}', 'sig=0000'
  $r2 = Call GET ($bad.Substring($W.Length))
  Check 'link adulterado recusado' ($r2.code -eq 403) "$($r2.code)"
  $u = [Uri]$v.url; $exp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + 99999
  $r2 = Call GET ($u.AbsolutePath + ($u.Query -replace 'exp=\d+', "exp=$exp"))
  Check 'prazo do link não pode ser esticado' ($r2.code -eq 403) "$($r2.code)"
} else { "  (sem versões no R2: teste de arquivo pulado)" }

"Reembolso"
$r = Send-Event @{ id = "evt_$tag-2"; type = 'charge.refunded'; data = @{ object = @{ id = "ch_$tag"; payment_intent = $pi } } }
Check 'evento de reembolso aceito' ($r.code -eq 200) $r.code
$r = Call POST '/api/license/validate' @{ key = $key; instance_id = $ids[0] }
Check 'chave desligada após reembolso' ($r.code -eq 403 -and $r.json.error -eq 'refunded') "$($r.code) $($r.json.error)"
$r = Call POST '/api/download/code' @{ key = $key }
Check 'download barrado após reembolso' ($r.code -eq 403) $r.code

"Reincidente"
$sid2 = "cs_$($cfg.stripe_mode)_${tag}b"
$null = Send-Event @{ id = "evt_$tag-3"; type = 'checkout.session.completed'; data = @{ object = @{ id = $sid2; payment_status = 'paid'; payment_intent = "${pi}b"; amount_total = 1000; currency = 'usd'; customer_details = @{ email = "$tag@example.invalid".ToLower() } } } }
$r = Call GET "/api/order?session_id=$sid2"
Check 'segunda compra marcada sem direito a reembolso' ($r.json.prior_refund -eq $true) ($r.json | ConvertTo-Json -Compress)

"Admin"
$r = Call GET "/api/admin/order?email=$($tag.ToLower())@example.invalid"
Check 'admin sem token recusado' ($r.code -eq 401) $r.code
$r = Call GET "/api/admin/order?email=$($tag.ToLower())@example.invalid" $null @{ Authorization = "Bearer $($kv['LOJA_ADMIN_TOKEN'])" }
Check 'admin lista 2 pedidos' (@($r.json.orders).Count -eq 2) ($r.json | ConvertTo-Json -Compress -Depth 4)

"Limpeza"
$cfH = @{ Authorization = "Bearer $($kv['CLOUDFLARE_API_KEY'])" }
$sql = "DELETE FROM activations WHERE order_id IN (SELECT id FROM orders WHERE email = '$($tag.ToLower())@example.invalid'); DELETE FROM deactivations WHERE order_id IN (SELECT id FROM orders WHERE email = '$($tag.ToLower())@example.invalid'); DELETE FROM codes WHERE order_id IN (SELECT id FROM orders WHERE email = '$($tag.ToLower())@example.invalid'); DELETE FROM orders WHERE email = '$($tag.ToLower())@example.invalid'; DELETE FROM events WHERE id LIKE 'evt_$tag%'; DELETE FROM ratelimit;"
$null = Invoke-RestMethod -Uri "https://api.cloudflare.com/client/v4/accounts/$($kv['CLOUDFLARE_ACCOUNT_ID'])/d1/database/$($cfg.d1_id)/query" -Method Post -Headers $cfH -ContentType 'application/json' -Body (@{ sql = $sql } | ConvertTo-Json)
"  dados do teste apagados"
if ($script:fail) { "RESULTADO: $script:fail falha(s)"; exit 1 } else { "RESULTADO: tudo OK" }
