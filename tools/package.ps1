# Builds the installable package of the mod (CurrencyMod + AI Diplomacy) into _Modding\dist\.
# Usage (from the game folder):  powershell -ExecutionPolicy Bypass -File _Modding\tools\package.ps1
#
# The package does NOT carry the API key (BepInEx\config\deepseek.key), the provider credentials
# (BepInEx\config\credenciais) or this machine's .cfg: each player connects their own account on the AI Diplomacy
# screen, and the .cfg is created with the defaults the first time the game starts.

$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$modding = Join-Path $gameRoot '_Modding'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# The version comes from Plugin.cs (PluginVersion).
$pluginCs = Get-Content (Join-Path $modding 'src\CurrencyMod\Plugin.cs') -Raw
$version = [regex]::Match($pluginCs, 'PluginVersion\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'PluginVersion not found in Plugin.cs' }
$name = "HumankindMod_$version"
$dist = Join-Path $modding 'dist'
$stage = Join-Path $dist $name

Write-Host "Building $name (without installing on this machine)..."
foreach ($project in @('src\CurrencyMod.Loader', 'src\CurrencyMod')) {
    & $dotnet build (Join-Path $modding $project) -c Release -nologo -v q -p:SkipDeploy=true
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force (Join-Path $stage 'BepInEx\plugins\CurrencyMod') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $stage 'BepInEx\CurrencyModCore') | Out-Null

Copy-Item (Join-Path $modding 'src\CurrencyMod.Loader\bin\Release\net472\CurrencyMod.Loader.dll') (Join-Path $stage 'BepInEx\plugins\CurrencyMod\')
Copy-Item (Join-Path $modding 'src\CurrencyMod\bin\Release\net472\CurrencyMod.dll') (Join-Path $stage 'BepInEx\CurrencyModCore\')
# Player-facing docs: the packaged file names stay as the players know them.
Copy-Item (Join-Path $modding 'docs\instalacao.md') (Join-Path $stage 'LEIA-ME.md')
Copy-Item (Join-Path $modding 'docs\diplomacia-ia.md') (Join-Path $stage 'Diplomacia-IA.md')

# Safety guard: no key file, no credential (BepInEx\config\credenciais) and no key inside the package files.
$leak = Get-ChildItem $stage -Recurse -File | Where-Object {
    $_.Name -like '*deepseek*' -or $_.Name -like '*.key' -or $_.Name -like '*.dat' -or $_.FullName -match '\\credenciais\\'
}
if ($leak) { throw "Key or credential file in the package: $($leak.FullName -join ', ')" }
$keyPattern = 'sk-[A-Za-z0-9]{24,}|sk-or-v1-[a-f0-9]{32,}|sk-proj-[A-Za-z0-9_\-]{24,}|AIza[0-9A-Za-z_\-]{35}|xai-[A-Za-z0-9]{40,}'
foreach ($file in Get-ChildItem $stage -Recurse -File) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    # Text files and DLLs: check as ASCII/UTF-8 and as UTF-16 (how C# stores strings inside the DLL).
    foreach ($text in @([Text.Encoding]::ASCII.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes))) {
        if ($text -match $keyPattern) { throw "There seems to be an API key inside $($file.FullName)" }
    }
}

$zip = Join-Path $dist "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force -Confirm:$false }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Package ready: $zip"
Get-ChildItem $stage -Recurse -File | ForEach-Object { '  ' + $_.FullName.Substring($stage.Length + 1) }
