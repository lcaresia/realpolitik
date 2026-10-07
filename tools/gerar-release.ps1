# Gera uma versão do Realpolitik para venda, do zero e de forma repetível:
#   dist\<versão>\Realpolitik_Setup_<versão>.exe   instalador (Inno Setup)
#   dist\<versão>\Realpolitik_<versão>_manual.zip  mesmo conteúdo, para extrair na pasta do jogo (Steam Deck, plano B)
#   dist\<versão>\MANIFEST.txt e SHA256SUMS.txt
# Uso (na pasta do jogo):
#   powershell -ExecutionPolicy Bypass -File _Modding\tools\gerar-release.ps1 -Versao 1.0.0
#   ... -Versao 1.0.1-beta.1          versão de teste (o Plugin.ProductVersion tem que bater sem o sufixo)
#   ... -Certificado C:\x.pfx         assina o Setup com signtool (senha em $env:REALPOLITIK_PFX_SENHA)
#
# Falha alto em qualquer problema: arquivo fora da lista branca, segredo ou caminho desta máquina dentro de qualquer
# arquivo (inclusive DLLs, em ASCII e UTF-16), versão que não bate, BepInEx diferente do oficial. Nunca lê de
# BepInEx\config, .env, credenciais ou da instalação do jogo: só compila do código e copia de _Modding\installer.
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [string]$Certificado,
    # Só para testar a trava: planta um .env falso e uma "chave" sk_live_ no staging; a geração TEM que falhar.
    [switch]$TestarTrava
)
$ErrorActionPreference = 'Stop'
$mod = Split-Path $PSScriptRoot -Parent
$installer = Join-Path $mod 'installer'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 não encontrado (ISCC.exe).' }
$bepZip = Join-Path $installer 'vendor\BepInEx_win_x64_5.4.23.5.zip'
$bepSha = '82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'
$utf8Bom = New-Object Text.UTF8Encoding($true)

function Step([string]$text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Sha([string]$path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLower() }

# ---------------- 1. versão ----------------
Step "Versão $Versao"
if ($Versao -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "Versão inválida: $Versao (use 1.0.0 ou 1.0.1-beta.1)" }
$base = $Versao.Split('-')[0]
$pluginCs = Get-Content (Join-Path $mod 'src\CurrencyMod\Plugin.cs') -Raw
$product = [regex]::Match($pluginCs, 'ProductVersion\s*=\s*"([^"]+)"').Groups[1].Value
if ($product -ne $base) { throw "Plugin.ProductVersion = $product, mas a versão pedida é $base. Ajuste Plugin.cs (o aviso de atualização do jogo usa esse número)." }
if ((Sha $bepZip) -ne $bepSha) { throw 'O zip do BepInEx não é o oficial (sha256 diferente).' }

$dist = Join-Path $mod 'dist'
$stage = Join-Path $dist "staging\$Versao"
$out = Join-Path $dist $Versao
foreach ($d in $stage, $out) { if (Test-Path $d) { Remove-Item $d -Recurse -Force -Confirm:$false } }
New-Item -ItemType Directory -Force $stage, $out | Out-Null

# ---------------- 2. compilar (sem instalar no jogo, sem pdb e sem caminho desta máquina dentro da DLL) ----------------
Step 'Compilando em Release'
$build = Join-Path $stage 'build'
$projects = [ordered]@{ 'CurrencyMod.Loader' = 'CurrencyMod.Loader.dll'; 'CurrencyMod' = 'CurrencyMod.dll'; 'MoreEmpires' = 'MoreEmpires.dll' }
foreach ($p in $projects.Keys) {
    $o = Join-Path $build $p
    & $dotnet build (Join-Path $mod "src\$p") -c Release --no-incremental -nologo -v q `
        -p:SkipDeploy=true -p:Deploy=false -p:DebugType=none -p:DebugSymbols=false -p:Deterministic=true "-p:OutDir=$o\"
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar $p" }
    if (-not (Test-Path (Join-Path $o $projects[$p]))) { throw "Saída não encontrada: $p" }
}

# ---------------- 2b. ofuscar o núcleo (Obfuscar; regras em installer\obfuscar.xml) ----------------
Step 'Ofuscando o CurrencyMod.dll'
$obfExe = Join-Path $installer 'vendor\obfuscar\Obfuscar.Console.exe'
if (-not (Test-Path $obfExe)) { throw "Obfuscar não encontrado em $obfExe (pacote NuGet obfuscar 2.2.50, pasta tools\)." }
$managed = Join-Path (Split-Path $mod -Parent) 'Humankind_Data\Managed'
$bepRef = Join-Path $build 'bepinex-ref'
Expand-Archive $bepZip $bepRef
$obfIn = Join-Path $build 'CurrencyMod'
$obfOut = Join-Path $build 'CurrencyMod-ofuscado'
$obfCfg = Join-Path $build 'obfuscar.xml'
$cfgText = (Get-Content (Join-Path $installer 'obfuscar.xml') -Raw).Replace('{IN}', $obfIn).Replace('{OUT}', $obfOut).Replace('{MANAGED}', $managed).Replace('{BEPINEX}', (Join-Path $bepRef 'BepInEx\core'))
[IO.File]::WriteAllText($obfCfg, $cfgText, (New-Object Text.UTF8Encoding($false)))
$obfLog = & $obfExe $obfCfg 2>&1
if ($LASTEXITCODE -ne 0) { $obfLog | Select-Object -Last 15 | Write-Host; throw 'Falha no Obfuscar' }
& powershell -ExecutionPolicy Bypass -File (Join-Path $installer 'conferir-ofuscacao.ps1') -Original (Join-Path $obfIn 'CurrencyMod.dll') `
    -Ofuscada (Join-Path $obfOut 'CurrencyMod.dll') -Cecil (Join-Path $bepRef 'BepInEx\core\Mono.Cecil.dll')
if ($LASTEXITCODE -ne 0) { throw 'A conferência da ofuscação falhou (algo que precisa do nome foi renomeado).' }
# O mapa (nome original -> ofuscado) fica só neste PC: serve para ler logs de erro de compradores. Nunca vai no pacote.
$maps = Join-Path $dist "mapas\$Versao"
New-Item -ItemType Directory -Force $maps | Out-Null
Copy-Item (Join-Path $obfOut 'Mapping.xml') (Join-Path $maps 'CurrencyMod-Mapping.xml') -Force
Copy-Item (Join-Path $obfOut 'CurrencyMod.dll') (Join-Path $obfIn 'CurrencyMod.dll') -Force

# ---------------- 3. montar o staging ----------------
Step 'Montando o staging'
function Put([string]$from, [string]$rel) {
    $to = Join-Path $stage $rel
    New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
    Copy-Item $from $to
}
function PutText([string]$from, [string]$rel) {
    # Texto sempre em UTF-8 com BOM (o Inno exige BOM para mostrar acentos).
    $to = Join-Path $stage $rel
    New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
    $text = [IO.File]::ReadAllText($from)
    [IO.File]::WriteAllText($to, $text.TrimStart([char]0xFEFF), $utf8Bom)
}
Put (Join-Path $build 'CurrencyMod.Loader\CurrencyMod.Loader.dll') 'payload\BepInEx\plugins\CurrencyMod\CurrencyMod.Loader.dll'
Put (Join-Path $build 'CurrencyMod\CurrencyMod.dll') 'payload\BepInEx\CurrencyModCore\CurrencyMod.dll'
Put (Join-Path $build 'MoreEmpires\MoreEmpires.dll') 'payload\BepInEx\plugins\MoreEmpires\MoreEmpires.dll'
$langs = 'en', 'pt', 'es', 'fr', 'de'
foreach ($l in $langs) {
    PutText (Join-Path $installer "textos\LEIA-ME_$l.txt") "docs\LEIA-ME_$l.txt"
    PutText (Join-Path $installer "textos\TERMOS_$l.txt") "docs\TERMOS_$l.txt"
}
PutText (Join-Path $installer 'textos\THIRD-PARTY.txt') 'docs\THIRD-PARTY.txt'
foreach ($f in Get-ChildItem (Join-Path $installer 'vendor\licencas') -File) { PutText $f.FullName "licencas\$($f.Name)" }
foreach ($f in 'realpolitik.ico', 'grande-164.bmp', 'grande-328.bmp', 'pequena-55.bmp', 'pequena-110.bmp') { Put (Join-Path $installer "art\$f") "art\$f" }
$bepDir = Join-Path $stage 'bepinex'
Expand-Archive $bepZip $bepDir
Remove-Item (Join-Path $bepDir 'changelog.txt') -Force -Confirm:$false

# ---------------- 4. lista branca ----------------
Step 'Lista branca'
$bepFiles = (Get-ChildItem $bepDir -Recurse -File -Force | ForEach-Object { 'bepinex\' + $_.FullName.Substring($bepDir.Length + 1) })
$allowed = @(
    'payload\BepInEx\plugins\CurrencyMod\CurrencyMod.Loader.dll'
    'payload\BepInEx\CurrencyModCore\CurrencyMod.dll'
    'payload\BepInEx\plugins\MoreEmpires\MoreEmpires.dll'
    'docs\THIRD-PARTY.txt'
    'licencas\BepInEx-LICENSE.txt', 'licencas\UnityDoorstop-LICENSE.txt', 'licencas\HarmonyX-LICENSE.txt', 'licencas\MonoMod-LICENSE.txt', 'licencas\Mono.Cecil-LICENSE.txt'
    'art\realpolitik.ico', 'art\grande-164.bmp', 'art\grande-328.bmp', 'art\pequena-55.bmp', 'art\pequena-110.bmp'
) + ($langs | ForEach-Object { "docs\LEIA-ME_$_.txt"; "docs\TERMOS_$_.txt" })
# O BepInEx entra arquivo por arquivo do zip oficial (sha256 já conferido), sem nada a mais.
$bepExpected = @('bepinex\.doorstop_version', 'bepinex\doorstop_config.ini', 'bepinex\winhttp.dll') + `
    @('0Harmony.dll', '0Harmony.xml', '0Harmony20.dll', 'BepInEx.dll', 'BepInEx.Harmony.dll', 'BepInEx.Harmony.xml', 'BepInEx.Preloader.dll',
      'BepInEx.Preloader.xml', 'BepInEx.xml', 'HarmonyXInterop.dll', 'Mono.Cecil.dll', 'Mono.Cecil.Mdb.dll', 'Mono.Cecil.Pdb.dll', 'Mono.Cecil.Rocks.dll',
      'MonoMod.RuntimeDetour.dll', 'MonoMod.RuntimeDetour.xml', 'MonoMod.Utils.dll', 'MonoMod.Utils.xml' | ForEach-Object { "bepinex\BepInEx\core\$_" })
$allowed += $bepExpected
$present = Get-ChildItem $stage -Recurse -File -Force | Where-Object { $_.FullName -notlike "$build\*" } | ForEach-Object { $_.FullName.Substring($stage.Length + 1) }
if ($present | Where-Object { $_ -match '(?i)mapping' }) { throw 'O mapa da ofuscação entrou no staging' }
$extra = $present | Where-Object { $allowed -notcontains $_ }
$missing = $allowed | Where-Object { $present -notcontains $_ }
if ($extra) { throw "Arquivo fora da lista branca: $($extra -join ', ')" }
if ($missing) { throw "Faltou no staging: $($missing -join ', ')" }
Remove-Item $build -Recurse -Force -Confirm:$false
Write-Host "  $($present.Count) arquivos, todos na lista branca"

# ---------------- 5. trava de segredos ----------------
Step 'Trava de segredos'
function Test-Secrets([string]$root) {
    $problems = @()
    $patterns = @(
        'sk-[A-Za-z0-9_\-]{20,}', 'sk-or-v1-[a-f0-9]{20,}', 'sk_live_[A-Za-z0-9]+', 'rk_live_[A-Za-z0-9]+', 'sk_test_[A-Za-z0-9]+', 'whsec_[A-Za-z0-9]+',
        'cfat_[A-Za-z0-9]+', 'LOJA_[A-Z_]+', 'STRIPE_[A-Z_]+', 'CLOUDFLARE_API', 'AIza[0-9A-Za-z_\-]{35}', 'xai-[A-Za-z0-9]{30,}',
        'Bearer [A-Za-z0-9_\-]{24,}', 'RPLN-(?!XXXXX)[0-9A-Z]{5}-[0-9A-Z]{5}',
        'Users\\lucas', 'steamapps\\common\\Humankind', 'Program Files \(x86\)\\Steam', '[A-Za-z]:\\[^\x00"]{0,80}_Modding'
    )
    # Só nas nossas DLLs: nenhum caminho de .pdb (compiladas com DebugType=none). As do BepInEx são as oficiais,
    # conferidas pelo sha256 do zip, e citam o .pdb de quem as compilou.
    $ownOnly = @('\.pdb')
    # Os próprios valores do .env (chaves reais), procurados literalmente. Nunca são impressos.
    $envFile = Join-Path $mod '.env'
    $literals = @()
    if (Test-Path $envFile) {
        foreach ($l in [IO.File]::ReadAllLines($envFile)) {
            if ($l -match '^\s*[^#][^=]*=(.+)$') { $v = $Matches[1].Trim(); if ($v.Length -ge 12) { $literals += $v } }
        }
    }
    $deepseek = Join-Path (Split-Path $mod -Parent) 'BepInEx\config\deepseek.key'
    if (Test-Path $deepseek) { $k = ([IO.File]::ReadAllText($deepseek)).Trim(); if ($k.Length -ge 12 -and $k -notmatch '\s') { $literals += $k } }
    foreach ($f in Get-ChildItem $root -Recurse -File -Force) {
        $rel = $f.FullName.Substring($root.Length + 1)
        if ($f.Name -match '(?i)^\.env|\.key$|\.pdb$|\.dat$|deepseek' -or $rel -match '(?i)credenciais|_Modding|decompiled') { $problems += "nome proibido: $rel"; continue }
        $bytes = [IO.File]::ReadAllBytes($f.FullName)
        foreach ($text in @([Text.Encoding]::ASCII.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes, 1, [Math]::Max(0, $bytes.Length - 1)))) {
            foreach ($p in $patterns) { if ($text -match $p) { $problems += "padrão '$p' em $rel"; break } }
            if ($rel -notmatch '^bepinex\\' -and $rel -notmatch '^BepInEx\\core\\' -and $rel -notmatch '^(winhttp\.dll|doorstop_config\.ini|\.doorstop_version)$') {
                foreach ($p in $ownOnly) { if ($text -match $p) { $problems += "padrão '$p' em $rel"; break } }
            }
            foreach ($v in $literals) { if ($text.Contains($v)) { $problems += "valor do .env/deepseek.key em $rel"; break } }
        }
    }
    return $problems | Select-Object -Unique
}
if ($TestarTrava) {
    [IO.File]::WriteAllText((Join-Path $stage 'docs\.env'), 'FAKE=1')
    [IO.File]::AppendAllText((Join-Path $stage 'docs\THIRD-PARTY.txt'), "`r`nsk_live_TESTE1234567890")
}
$found = Test-Secrets $stage
if ($found) { throw "TRAVA DE SEGREDOS: $($found -join ' | ')" }
Write-Host '  nenhum segredo, chave ou caminho desta máquina'

# ---------------- 6. manifesto ----------------
Step 'Manifesto'
$manifest = @("Realpolitik: Living Nations for HUMANKIND $Versao", "Gerado em $(Get-Date -Format 'yyyy-MM-dd HH:mm') por gerar-release.ps1", '', 'tamanho  sha256  caminho (staging)')
foreach ($rel in ($present | Sort-Object)) {
    $f = Get-Item (Join-Path $stage $rel)
    $manifest += '{0,9}  {1}  {2}' -f $f.Length, (Sha $f.FullName), $rel
}
[IO.File]::WriteAllLines((Join-Path $out 'MANIFEST.txt'), $manifest, $utf8Bom)

# ---------------- 7. Setup.exe ----------------
Step 'Compilando o Setup (Inno Setup)'
& $iscc /Q "/DVersao=$Versao" "/DStaging=$stage" "/DSaida=$out" (Join-Path $installer 'Realpolitik.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha no ISCC' }
$setup = Join-Path $out "Realpolitik_Setup_$Versao.exe"
if (-not (Test-Path $setup)) { throw 'O Setup não apareceu' }
if ($Certificado) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe não encontrado (Windows SDK).' }
    & $signtool.FullName sign /f $Certificado /p $env:REALPOLITIK_PFX_SENHA /fd sha256 /tr http://timestamp.digicert.com /td sha256 /d 'Realpolitik: Living Nations for HUMANKIND' $setup
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao assinar' }
}

# ---------------- 8. zip manual ----------------
Step 'Zip manual'
$zipTree = Join-Path $dist "staging\$Versao-zip"
if (Test-Path $zipTree) { Remove-Item $zipTree -Recurse -Force -Confirm:$false }
Copy-Item (Join-Path $stage 'bepinex') $zipTree -Recurse
Copy-Item (Join-Path $stage 'payload\BepInEx\*') (Join-Path $zipTree 'BepInEx') -Recurse -Force
New-Item -ItemType Directory -Force (Join-Path $zipTree 'BepInEx\Realpolitik\licencas') | Out-Null
Copy-Item (Join-Path $stage 'docs\*') (Join-Path $zipTree 'BepInEx\Realpolitik')
Copy-Item (Join-Path $stage 'licencas\*') (Join-Path $zipTree 'BepInEx\Realpolitik\licencas')
New-Item -ItemType Directory -Force (Join-Path $zipTree 'BepInEx\plugins') | Out-Null
# Um leia-me só na raiz (os 5 idiomas), para quem abre o zip antes de extrair.
$readme = ($langs | ForEach-Object { [IO.File]::ReadAllText((Join-Path $stage "docs\LEIA-ME_$_.txt")).TrimStart([char]0xFEFF) }) -join "`r`n`r`n"
[IO.File]::WriteAllText((Join-Path $zipTree 'REALPOLITIK-README.txt'), $readme, $utf8Bom)
$zipFound = Test-Secrets $zipTree
if ($zipFound) { throw "TRAVA DE SEGREDOS (zip): $($zipFound -join ' | ')" }
$zip = Join-Path $out "Realpolitik_$($Versao)_manual.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($zipTree, $zip, 'Optimal', $false)
Remove-Item $zipTree -Recurse -Force -Confirm:$false

# ---------------- 9. resumo ----------------
Step 'Pronto'
$sums = foreach ($f in $setup, $zip) { '{0}  {1}' -f (Sha $f), (Split-Path $f -Leaf) }
[IO.File]::WriteAllLines((Join-Path $out 'SHA256SUMS.txt'), $sums)
foreach ($f in $setup, $zip) { '  {0,-40} {1,8:N0} KB  {2}' -f (Split-Path $f -Leaf), ((Get-Item $f).Length / 1KB), (Sha $f) }
"  staging: $($present.Count) arquivos · trava de segredos OK · $out"
"  Próximo passo: testes (docs\prompt-instalador.md, fase 3). Publicar só com OK do dono:"
"  powershell -ExecutionPolicy Bypass -File _Modding\loja\tools\publicar-versao.ps1 -Versao $Versao -Arquivo `"$setup`" -Notas `"...`" -JogoTestado 1.31.4836"
