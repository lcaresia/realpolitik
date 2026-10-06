# Gera o pacote instalável do mod (CurrencyMod + Diplomacia IA) em _Modding\dist\.
# Uso (na pasta do jogo):  powershell -ExecutionPolicy Bypass -File _Modding\tools\empacotar.ps1
#
# O pacote NÃO leva a chave da API (BepInEx\config\deepseek.key), as credenciais dos provedores
# (BepInEx\config\credenciais) nem o .cfg desta máquina: cada pessoa conecta a própria conta na tela Diplomacia IA, e o
# .cfg é criado com os padrões na primeira vez que o jogo abre.

$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$modding = Join-Path $gameRoot '_Modding'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# Versão vem do Plugin.cs (PluginVersion).
$pluginCs = Get-Content (Join-Path $modding 'src\CurrencyMod\Plugin.cs') -Raw
$version = [regex]::Match($pluginCs, 'PluginVersion\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Não achei PluginVersion em Plugin.cs' }
$name = "HumankindMod_$version"
$dist = Join-Path $modding 'dist'
$stage = Join-Path $dist $name

Write-Host "Compilando $name (sem instalar nesta máquina)..."
foreach ($project in @('src\CurrencyMod.Loader', 'src\CurrencyMod')) {
    & $dotnet build (Join-Path $modding $project) -c Release -nologo -v q -p:SkipDeploy=true
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar $project" }
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force (Join-Path $stage 'BepInEx\plugins\CurrencyMod') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $stage 'BepInEx\CurrencyModCore') | Out-Null

Copy-Item (Join-Path $modding 'src\CurrencyMod.Loader\bin\Release\net472\CurrencyMod.Loader.dll') (Join-Path $stage 'BepInEx\plugins\CurrencyMod\')
Copy-Item (Join-Path $modding 'src\CurrencyMod\bin\Release\net472\CurrencyMod.dll') (Join-Path $stage 'BepInEx\CurrencyModCore\')
Copy-Item (Join-Path $modding 'docs\instalacao.md') (Join-Path $stage 'LEIA-ME.md')
Copy-Item (Join-Path $modding 'docs\diplomacia-ia.md') (Join-Path $stage 'Diplomacia-IA.md')

# Trava de segurança: nenhum arquivo de chave, nenhuma credencial (BepInEx\config\credenciais) e nenhuma chave
# dentro dos arquivos do pacote.
$leak = Get-ChildItem $stage -Recurse -File | Where-Object {
    $_.Name -like '*deepseek*' -or $_.Name -like '*.key' -or $_.Name -like '*.dat' -or $_.FullName -match '\\credenciais\\'
}
if ($leak) { throw "Arquivo de chave ou credencial no pacote: $($leak.FullName -join ', ')" }
$keyPattern = 'sk-[A-Za-z0-9]{24,}|sk-or-v1-[a-f0-9]{32,}|sk-proj-[A-Za-z0-9_\-]{24,}|AIza[0-9A-Za-z_\-]{35}|xai-[A-Za-z0-9]{40,}'
foreach ($file in Get-ChildItem $stage -Recurse -File) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    # Textos e DLLs: confere em ASCII/UTF-8 e em UTF-16 (como o C# guarda strings na DLL).
    foreach ($text in @([Text.Encoding]::ASCII.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes))) {
        if ($text -match $keyPattern) { throw "Parece haver uma chave de API dentro de $($file.FullName)" }
    }
}

$zip = Join-Path $dist "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force -Confirm:$false }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Pacote pronto: $zip"
Get-ChildItem $stage -Recurse -File | ForEach-Object { '  ' + $_.FullName.Substring($stage.Length + 1) }
