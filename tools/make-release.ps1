# Builds a Realpolitik release from scratch, repeatably:
#   dist\<version>\Realpolitik_Setup_<version>.exe   installer (Inno Setup)
#   dist\<version>\Realpolitik_<version>_manual.zip  same content, to extract into the game folder (Steam Deck, plan B)
#   dist\<version>\MANIFEST.txt and SHA256SUMS.txt
# Usage (from the game folder):
#   powershell -ExecutionPolicy Bypass -File _Modding\tools\make-release.ps1 -Version 1.0.0
#   ... -Version 1.0.1-beta.1          test version (Plugin.ProductVersion must match without the suffix)
#   ... -Certificate C:\x.pfx          signs the Setup with signtool (password in $env:REALPOLITIK_PFX_PASSWORD)
#
# Fails loudly on any problem: a file outside the allowlist, a secret or a path of this machine inside any file
# (DLLs included, as ASCII and UTF-16), a version mismatch, a BepInEx that is not the official one. Never reads from
# BepInEx\config, .env, credentials or the game installation: it only builds from source and copies from _Modding\installer.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Certificate,
    # Only to test the guard: plants a fake .env and an sk_live_ "key" in the staging; the build MUST fail.
    [switch]$TestSecretGuard
)
$ErrorActionPreference = 'Stop'
$mod = Split-Path $PSScriptRoot -Parent
$installer = Join-Path $mod 'installer'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (ISCC.exe).' }
$bepZip = Join-Path $installer 'vendor\BepInEx_win_x64_5.4.23.5.zip'
$bepSha = '82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'
$utf8Bom = New-Object Text.UTF8Encoding($true)

function Step([string]$text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Sha([string]$path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLower() }

# ---------------- 1. version ----------------
Step "Version $Version"
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "Invalid version: $Version (use 1.0.0 or 1.0.1-beta.1)" }
$base = $Version.Split('-')[0]
$pluginCs = Get-Content (Join-Path $mod 'src\CurrencyMod\Plugin.cs') -Raw
$product = [regex]::Match($pluginCs, 'ProductVersion\s*=\s*"([^"]+)"').Groups[1].Value
if ($product -ne $base) { throw "Plugin.ProductVersion = $product, but the requested version is $base. Fix Plugin.cs (the in-game update notice uses this number)." }
if ((Sha $bepZip) -ne $bepSha) { throw 'The BepInEx zip is not the official one (sha256 mismatch).' }

$dist = Join-Path $mod 'dist'
$stage = Join-Path $dist "staging\$Version"
$out = Join-Path $dist $Version
foreach ($d in $stage, $out) { if (Test-Path $d) { Remove-Item $d -Recurse -Force -Confirm:$false } }
New-Item -ItemType Directory -Force $stage, $out | Out-Null

# ---------------- 2. build (no install into the game, no pdb and no path of this machine inside the DLL) ----------------
Step 'Building in Release'
$build = Join-Path $stage 'build'
$projects = [ordered]@{ 'CurrencyMod.Loader' = 'CurrencyMod.Loader.dll'; 'CurrencyMod' = 'CurrencyMod.dll'; 'MoreEmpires' = 'MoreEmpires.dll' }
foreach ($p in $projects.Keys) {
    $o = Join-Path $build $p
    & $dotnet build (Join-Path $mod "src\$p") -c Release --no-incremental -nologo -v q `
        -p:SkipDeploy=true -p:Deploy=false -p:DebugType=none -p:DebugSymbols=false -p:Deterministic=true "-p:OutDir=$o\"
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $p" }
    if (-not (Test-Path (Join-Path $o $projects[$p]))) { throw "Output not found: $p" }
}

# ---------------- 3. assemble the staging ----------------
# The staged file and folder names (docs\LEIA-ME_*, licencas\, art\grande-*.bmp...) are what gets installed on the
# player's machine; they are data and keep their names.
Step 'Assembling the staging'
function Put([string]$from, [string]$rel) {
    $to = Join-Path $stage $rel
    New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
    Copy-Item $from $to
}
function PutText([string]$from, [string]$rel) {
    # Text always as UTF-8 with BOM (Inno needs the BOM to show accents).
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

# ---------------- 4. allowlist ----------------
Step 'Allowlist'
$bepFiles = (Get-ChildItem $bepDir -Recurse -File -Force | ForEach-Object { 'bepinex\' + $_.FullName.Substring($bepDir.Length + 1) })
$allowed = @(
    'payload\BepInEx\plugins\CurrencyMod\CurrencyMod.Loader.dll'
    'payload\BepInEx\CurrencyModCore\CurrencyMod.dll'
    'payload\BepInEx\plugins\MoreEmpires\MoreEmpires.dll'
    'docs\THIRD-PARTY.txt'
    'licencas\BepInEx-LICENSE.txt', 'licencas\UnityDoorstop-LICENSE.txt', 'licencas\HarmonyX-LICENSE.txt', 'licencas\MonoMod-LICENSE.txt', 'licencas\Mono.Cecil-LICENSE.txt'
    'art\realpolitik.ico', 'art\grande-164.bmp', 'art\grande-328.bmp', 'art\pequena-55.bmp', 'art\pequena-110.bmp'
) + ($langs | ForEach-Object { "docs\LEIA-ME_$_.txt"; "docs\TERMOS_$_.txt" })
# BepInEx goes in file by file from the official zip (sha256 already checked), with nothing extra.
$bepExpected = @('bepinex\.doorstop_version', 'bepinex\doorstop_config.ini', 'bepinex\winhttp.dll') + `
    @('0Harmony.dll', '0Harmony.xml', '0Harmony20.dll', 'BepInEx.dll', 'BepInEx.Harmony.dll', 'BepInEx.Harmony.xml', 'BepInEx.Preloader.dll',
      'BepInEx.Preloader.xml', 'BepInEx.xml', 'HarmonyXInterop.dll', 'Mono.Cecil.dll', 'Mono.Cecil.Mdb.dll', 'Mono.Cecil.Pdb.dll', 'Mono.Cecil.Rocks.dll',
      'MonoMod.RuntimeDetour.dll', 'MonoMod.RuntimeDetour.xml', 'MonoMod.Utils.dll', 'MonoMod.Utils.xml' | ForEach-Object { "bepinex\BepInEx\core\$_" })
$allowed += $bepExpected
$present = Get-ChildItem $stage -Recurse -File -Force | Where-Object { $_.FullName -notlike "$build\*" } | ForEach-Object { $_.FullName.Substring($stage.Length + 1) }
if ($present | Where-Object { $_ -match '(?i)mapping' }) { throw 'The obfuscation map got into the staging' }
$extra = $present | Where-Object { $allowed -notcontains $_ }
$missing = $allowed | Where-Object { $present -notcontains $_ }
if ($extra) { throw "File outside the allowlist: $($extra -join ', ')" }
if ($missing) { throw "Missing from the staging: $($missing -join ', ')" }
Remove-Item $build -Recurse -Force -Confirm:$false
Write-Host "  $($present.Count) files, all on the allowlist"

# ---------------- 5. secret guard ----------------
Step 'Secret guard'
function Test-Secrets([string]$root) {
    $problems = @()
    $patterns = @(
        'sk-[A-Za-z0-9_\-]{20,}', 'sk-or-v1-[a-f0-9]{20,}', 'sk_live_[A-Za-z0-9]+', 'rk_live_[A-Za-z0-9]+', 'sk_test_[A-Za-z0-9]+', 'whsec_[A-Za-z0-9]+',
        'cfat_[A-Za-z0-9]+', 'LOJA_[A-Z_]+', 'STRIPE_[A-Z_]+', 'CLOUDFLARE_API', 'AIza[0-9A-Za-z_\-]{35}', 'xai-[A-Za-z0-9]{30,}',
        'Bearer [A-Za-z0-9_\-]{24,}', 'RPLN-(?!XXXXX)[0-9A-Z]{5}-[0-9A-Z]{5}',
        'Users\\lucas', 'steamapps\\common\\Humankind', 'Program Files \(x86\)\\Steam', '[A-Za-z]:\\[^\x00"]{0,80}_Modding'
    )
    # Only in our DLLs: no .pdb path (built with DebugType=none). The BepInEx ones are the official files,
    # checked through the zip's sha256, and mention the .pdb of whoever built them.
    $ownOnly = @('\.pdb')
    # The .env values themselves (real keys), searched literally. They are never printed.
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
        # "credenciais" is the mod's credentials folder name on disk (data).
        if ($f.Name -match '(?i)^\.env|\.key$|\.pdb$|\.dat$|deepseek' -or $rel -match '(?i)credenciais|_Modding|decompiled') { $problems += "forbidden name: $rel"; continue }
        $bytes = [IO.File]::ReadAllBytes($f.FullName)
        foreach ($text in @([Text.Encoding]::ASCII.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes, 1, [Math]::Max(0, $bytes.Length - 1)))) {
            foreach ($p in $patterns) { if ($text -match $p) { $problems += "pattern '$p' in $rel"; break } }
            if ($rel -notmatch '^bepinex\\' -and $rel -notmatch '^BepInEx\\core\\' -and $rel -notmatch '^(winhttp\.dll|doorstop_config\.ini|\.doorstop_version)$') {
                foreach ($p in $ownOnly) { if ($text -match $p) { $problems += "pattern '$p' in $rel"; break } }
            }
            foreach ($v in $literals) { if ($text.Contains($v)) { $problems += ".env/deepseek.key value in $rel"; break } }
        }
    }
    return $problems | Select-Object -Unique
}
if ($TestSecretGuard) {
    [IO.File]::WriteAllText((Join-Path $stage 'docs\.env'), 'FAKE=1')
    [IO.File]::AppendAllText((Join-Path $stage 'docs\THIRD-PARTY.txt'), "`r`nsk_live_TEST1234567890")
}
$found = Test-Secrets $stage
if ($found) { throw "SECRET GUARD: $($found -join ' | ')" }
Write-Host '  no secret, key or path of this machine'

# ---------------- 6. manifest ----------------
Step 'Manifest'
$manifest = @("Realpolitik: Living Nations for HUMANKIND $Version", "Generated on $(Get-Date -Format 'yyyy-MM-dd HH:mm') by make-release.ps1", '', 'size  sha256  path (staging)')
foreach ($rel in ($present | Sort-Object)) {
    $f = Get-Item (Join-Path $stage $rel)
    $manifest += '{0,9}  {1}  {2}' -f $f.Length, (Sha $f.FullName), $rel
}
[IO.File]::WriteAllLines((Join-Path $out 'MANIFEST.txt'), $manifest, $utf8Bom)

# ---------------- 7. Setup.exe ----------------
Step 'Building the Setup (Inno Setup)'
& $iscc /Q "/DReleaseVersion=$Version" "/DStagingDir=$stage" "/DOutputFolder=$out" (Join-Path $installer 'Realpolitik.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
$setup = Join-Path $out "Realpolitik_Setup_$Version.exe"
if (-not (Test-Path $setup)) { throw 'The Setup did not show up' }
if ($Certificate) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe not found (Windows SDK).' }
    & $signtool.FullName sign /f $Certificate /p $env:REALPOLITIK_PFX_PASSWORD /fd sha256 /tr http://timestamp.digicert.com /td sha256 /d 'Realpolitik: Living Nations for HUMANKIND' $setup
    if ($LASTEXITCODE -ne 0) { throw 'Signing failed' }
}

# ---------------- 8. manual zip ----------------
Step 'Manual zip'
$zipTree = Join-Path $dist "staging\$Version-zip"
if (Test-Path $zipTree) { Remove-Item $zipTree -Recurse -Force -Confirm:$false }
Copy-Item (Join-Path $stage 'bepinex') $zipTree -Recurse
Copy-Item (Join-Path $stage 'payload\BepInEx\*') (Join-Path $zipTree 'BepInEx') -Recurse -Force
New-Item -ItemType Directory -Force (Join-Path $zipTree 'BepInEx\Realpolitik\licencas') | Out-Null
Copy-Item (Join-Path $stage 'docs\*') (Join-Path $zipTree 'BepInEx\Realpolitik')
Copy-Item (Join-Path $stage 'licencas\*') (Join-Path $zipTree 'BepInEx\Realpolitik\licencas')
New-Item -ItemType Directory -Force (Join-Path $zipTree 'BepInEx\plugins') | Out-Null
# A single readme at the root (all 5 languages), for whoever opens the zip before extracting.
$readme = ($langs | ForEach-Object { [IO.File]::ReadAllText((Join-Path $stage "docs\LEIA-ME_$_.txt")).TrimStart([char]0xFEFF) }) -join "`r`n`r`n"
[IO.File]::WriteAllText((Join-Path $zipTree 'REALPOLITIK-README.txt'), $readme, $utf8Bom)
$zipFound = Test-Secrets $zipTree
if ($zipFound) { throw "SECRET GUARD (zip): $($zipFound -join ' | ')" }
$zip = Join-Path $out "Realpolitik_$($Version)_manual.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($zipTree, $zip, 'Optimal', $false)
Remove-Item $zipTree -Recurse -Force -Confirm:$false

# ---------------- 9. summary ----------------
Step 'Done'
$sums = foreach ($f in $setup, $zip) { '{0}  {1}' -f (Sha $f), (Split-Path $f -Leaf) }
[IO.File]::WriteAllLines((Join-Path $out 'SHA256SUMS.txt'), $sums)
foreach ($f in $setup, $zip) { '  {0,-40} {1,8:N0} KB  {2}' -f (Split-Path $f -Leaf), ((Get-Item $f).Length / 1KB), (Sha $f) }
"  staging: $($present.Count) files - secret guard OK - $out"
"  Publish: create the tag v$Version and push it (git push origin v$Version); the 'release' Action builds everything again and publishes it on GitHub."
