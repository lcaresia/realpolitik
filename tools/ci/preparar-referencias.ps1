# Monta, ao lado do repositório, a estrutura mínima de "pasta do jogo" que os projetos esperam, para compilar sem o
# HUMANKIND instalado (GitHub Actions ou um PC sem o jogo):
#   <pai>\_Modding\              este repositório
#   <pai>\Humankind_Data\Managed\ assemblies de referência do jogo e da Unity (refs\Managed: só metadados, sem código)
#   <pai>\BepInEx\core\           do zip oficial do BepInEx (installer\vendor)
# Nunca sobrescreve uma instalação de verdade: se já existir Humankind.exe na pasta pai, não faz nada.
# Uso: powershell -ExecutionPolicy Bypass -File _Modding\tools\ci\preparar-referencias.ps1
$ErrorActionPreference = 'Stop'
$mod = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$game = Split-Path $mod -Parent
if (Test-Path (Join-Path $game 'Humankind.exe')) { 'Jogo instalado na pasta pai: nada a fazer.'; return }

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
"Referências prontas em $game"
