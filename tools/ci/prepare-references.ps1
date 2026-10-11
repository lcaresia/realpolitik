# Sets up, next to the repository, the minimal "game folder" layout the projects expect, so they build without
# HUMANKIND installed (GitHub Actions or a PC without the game):
#   <parent>\_Modding\              this repository
#   <parent>\Humankind_Data\Managed\ reference assemblies of the game and Unity (refs\Managed: metadata only, no code)
#   <parent>\BepInEx\core\           from the official BepInEx zip (installer\vendor)
# Never overwrites a real installation: if Humankind.exe already exists in the parent folder, it does nothing.
# Usage: powershell -ExecutionPolicy Bypass -File _Modding\tools\ci\prepare-references.ps1
$ErrorActionPreference = 'Stop'
$mod = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$game = Split-Path $mod -Parent
if (Test-Path (Join-Path $game 'Humankind.exe')) { 'Game installed in the parent folder: nothing to do.'; return }

$managed = Join-Path $game 'Humankind_Data\Managed'
New-Item -ItemType Directory -Force $managed | Out-Null
Copy-Item (Join-Path $mod 'refs\Managed\*.dll') $managed -Force

$core = Join-Path $game 'BepInEx\core'
if (-not (Test-Path (Join-Path $core 'BepInEx.dll'))) {
    $tmp = Join-Path $game '_bepinex'
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -Confirm:$false }
    Expand-Archive (Join-Path $mod 'installer\vendor\BepInEx_win_x64_5.4.23.5.zip') $tmp
    New-Item -ItemType Directory -Force $core | Out-Null
    Copy-Item (Join-Path $tmp 'BepInEx\core\*') $core -Force
    Remove-Item $tmp -Recurse -Force -Confirm:$false
}
"References ready in $game"
