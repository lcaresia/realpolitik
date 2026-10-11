# Installs, uninstalls or packages MoreEmpires (up to 16 empires + random spawn).
# Usage (from the game folder):
#   powershell -ExecutionPolicy Bypass -File _Modding\tools\install-moreempires.ps1             # build and install
#   powershell -ExecutionPolicy Bypass -File _Modding\tools\install-moreempires.ps1 -Uninstall  # turn off (renames the DLL)
#   powershell -ExecutionPolicy Bypass -File _Modding\tools\install-moreempires.ps1 -Package    # build _Modding\dist\MoreEmpires_<version>.zip
#
# The plugin only loads when the game starts: install/uninstall with the game CLOSED and start the game afterwards.
# Its configuration lives in BepInEx\config\lucas.humankind.moreempires.cfg (created on first start).

param(
    [switch]$Uninstall,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$modding = Join-Path $gameRoot '_Modding'
$project = Join-Path $modding 'src\MoreEmpires'
$pluginDir = Join-Path $gameRoot 'BepInEx\plugins\MoreEmpires'
$dll = Join-Path $pluginDir 'MoreEmpires.dll'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

function Test-GameRunning {
    return [bool](Get-Process -Name 'Humankind' -ErrorAction SilentlyContinue)
}

if ($Uninstall) {
    if (Test-GameRunning) { throw 'Close Humankind before uninstalling (the DLL is in use while the game runs).' }
    if (Test-Path $dll) {
        $off = "$dll.disabled"
        if (Test-Path $off) { Move-Item $off "$off.$(Get-Date -Format yyyyMMddHHmmss)" }
        Move-Item $dll $off
        Write-Host "MoreEmpires turned off: $off (BepInEx only loads .dll files). To turn it back on, run this script without parameters."
    } else {
        Write-Host 'MoreEmpires is not installed.'
    }
    return
}

$pluginCs = Get-Content (Join-Path $project 'Plugin.cs') -Raw
$version = [regex]::Match($pluginCs, 'Version\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Version not found in Plugin.cs' }

if ($Package) {
    Write-Host "Building MoreEmpires $version (without installing)..."
    & $dotnet build $project -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'MoreEmpires build failed' }
    $dist = Join-Path $modding 'dist'
    $stage = Join-Path $dist "MoreEmpires_$version"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
    New-Item -ItemType Directory -Force (Join-Path $stage 'BepInEx\plugins\MoreEmpires') | Out-Null
    Copy-Item (Join-Path $project 'bin\Release\net472\MoreEmpires.dll') (Join-Path $stage 'BepInEx\plugins\MoreEmpires\')
    Copy-Item (Join-Path $modding 'docs\pesquisa-30-jogadores.md') (Join-Path $stage 'MoreEmpires-detalhes.md')
    # Player-facing readme (Portuguese, as shipped to players).
    @"
MoreEmpires $version - até 16 impérios em qualquer tamanho de mapa + nascimento aleatório (Humankind)

Requisitos: BepInEx 5 (x64) instalado na pasta do jogo.
Instalar: copie a pasta BepInEx deste pacote para a pasta do jogo (junta com a existente) e abra o jogo.
Configurar: BepInEx\config\lucas.humankind.moreempires.cfg (criado no primeiro início; MaxImperios = 10 volta ao original).
Desinstalar: apague (ou renomeie) BepInEx\plugins\MoreEmpires\MoreEmpires.dll.
Saves com mais de 10 impérios só abrem com o mod instalado.
"@ | Set-Content -Encoding utf8 (Join-Path $stage 'LEIA-ME.txt')
    $zip = Join-Path $dist "MoreEmpires_$version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force -Confirm:$false }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Write-Host "Package ready: $zip"
    return
}

if (Test-GameRunning) { throw 'Close Humankind before installing: plugins only load when the game starts (and the DLL is in use).' }
Write-Host "Building and installing MoreEmpires $version into $pluginDir ..."
& $dotnet build $project -c Release -nologo -v q -p:Deploy=true
if ($LASTEXITCODE -ne 0) { throw 'MoreEmpires build failed' }
if (-not (Test-Path $dll)) { throw "The DLL did not show up in $dll" }
Write-Host 'Installed. Start the game and check BepInEx\LogOutput.log for the MoreEmpires startup lines (max empires and patches applied, 0 failed).'
