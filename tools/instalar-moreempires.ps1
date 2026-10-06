# Instala, desinstala ou empacota o MoreEmpires (até 16 impérios + nascimento aleatório).
# Uso (na pasta do jogo):
#   powershell -ExecutionPolicy Bypass -File _Modding\tools\instalar-moreempires.ps1              # compila e instala
#   powershell -ExecutionPolicy Bypass -File _Modding\tools\instalar-moreempires.ps1 -Desinstalar # desliga (renomeia a DLL)
#   powershell -ExecutionPolicy Bypass -File _Modding\tools\instalar-moreempires.ps1 -Pacote      # gera _Modding\dist\MoreEmpires_<versão>.zip
#
# O plugin é carregado só quando o jogo abre: instale/desinstale com o jogo FECHADO e abra o jogo depois.
# A configuração fica em BepInEx\config\lucas.humankind.moreempires.cfg (criada no primeiro início).

param(
    [switch]$Desinstalar,
    [switch]$Pacote
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

if ($Desinstalar) {
    if (Test-GameRunning) { throw 'Feche o Humankind antes de desinstalar (a DLL fica em uso enquanto o jogo roda).' }
    if (Test-Path $dll) {
        $off = "$dll.desligado"
        if (Test-Path $off) { Move-Item $off "$off.$(Get-Date -Format yyyyMMddHHmmss)" }
        Move-Item $dll $off
        Write-Host "MoreEmpires desligado: $off (o BepInEx só carrega arquivos .dll). Para voltar, rode este script sem parâmetros."
    } else {
        Write-Host 'MoreEmpires não está instalado.'
    }
    return
}

$pluginCs = Get-Content (Join-Path $project 'Plugin.cs') -Raw
$version = [regex]::Match($pluginCs, 'Version\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Não achei a versão em Plugin.cs' }

if ($Pacote) {
    Write-Host "Compilando MoreEmpires $version (sem instalar)..."
    & $dotnet build $project -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o MoreEmpires' }
    $dist = Join-Path $modding 'dist'
    $stage = Join-Path $dist "MoreEmpires_$version"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
    New-Item -ItemType Directory -Force (Join-Path $stage 'BepInEx\plugins\MoreEmpires') | Out-Null
    Copy-Item (Join-Path $project 'bin\Release\net472\MoreEmpires.dll') (Join-Path $stage 'BepInEx\plugins\MoreEmpires\')
    Copy-Item (Join-Path $modding 'docs\pesquisa-30-jogadores.md') (Join-Path $stage 'MoreEmpires-detalhes.md')
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
    Write-Host "Pacote pronto: $zip"
    return
}

if (Test-GameRunning) { throw 'Feche o Humankind antes de instalar: plugins só são carregados quando o jogo abre (e a DLL fica em uso).' }
Write-Host "Compilando e instalando MoreEmpires $version em $pluginDir ..."
& $dotnet build $project -c Release -nologo -v q -p:Deploy=true
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o MoreEmpires' }
if (-not (Test-Path $dll)) { throw "A DLL não apareceu em $dll" }
Write-Host 'Instalado. Abra o jogo e confira no BepInEx\LogOutput.log a linha "MoreEmpires ...: MaxImperios = ..." e "Patches aplicados: N ok, 0 com falha".'
