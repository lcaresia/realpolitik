# Testes do instalador na instalação REAL do jogo (Windows Home não tem Sandbox). Antes de rodar: backup com hashes
# em Documentos\HumankindModding\pre-teste-instalador\<data>\ (ver docs\prompt-instalador.md, fase 3); no fim,
# restaurar e conferir os hashes. Cada teste grava a prova em dist\testes\<versão>\<teste>\ (o gerar-release apaga dist\<versão>).
# Uso: powershell -ExecutionPolicy Bypass -File _Modding\installer\testes\testar-instalador.ps1 -Versao 1.0.0 -Teste T1
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [Parameter(Mandatory = $true)][string]$Teste
)
$ErrorActionPreference = 'Stop'
$mod = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$game = Split-Path $mod -Parent
$dist = Join-Path $mod "dist\$Versao"
$setup = Join-Path $dist "Realpolitik_Setup_$Versao.exe"
$zip = Join-Path $dist "Realpolitik_$($Versao)_manual.zip"
$ev = Join-Path $mod "dist\testes\$Versao\$Teste"
New-Item -ItemType Directory -Force $ev | Out-Null
$script:ok = 0; $script:fail = 0

function Check([string]$name, [bool]$cond, $detail = '') {
    $line = if ($cond) { "  OK    $name" } else { "  FALHA $name  ($detail)" }
    if ($cond) { $script:ok++ } else { $script:fail++ }
    Write-Host $line
    Add-Content (Join-Path $ev 'resultado.txt') $line -Encoding UTF8
}

function GameRunning { [bool](Get-Process Humankind -ErrorAction SilentlyContinue) }

# Tira o BepInEx e os arquivos do Doorstop da pasta do jogo (máquina limpa simulada). O backup tem tudo.
function Clean([string]$dir = $game) {
    if (GameRunning) { throw 'feche o jogo' }
    foreach ($p in 'BepInEx', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
        $full = Join-Path $dir $p
        if (Test-Path $full) { Remove-Item $full -Recurse -Force -Confirm:$false }
    }
}

# Árvore (caminho + sha256) do que o mod/BepInEx toca na pasta do jogo.
function Tree([string]$name, [string]$dir = $game) {
    $files = @()
    foreach ($p in 'BepInEx', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
        $full = Join-Path $dir $p
        if (Test-Path $full) { $files += Get-ChildItem $full -Recurse -File -Force | Where-Object { $_.Extension -ne '.log' -and $_.FullName -notmatch '\\BepInEx\\cache\\' } }
    }
    $lines = $files | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName).Hash.Substring(0, 16), $_.FullName.Substring($dir.Length + 1) } | Sort-Object { $_.Substring(18) }
    $lines | Set-Content (Join-Path $ev "arvore-$name.txt") -Encoding UTF8
    return $lines
}

function HashOf([string]$rel, [string]$dir = $game) {
    $full = Join-Path $dir $rel
    if (Test-Path $full) { (Get-FileHash $full).Hash } else { $null }
}

function Install([string[]]$extra = @(), [string]$dir = $game, [string]$tag = 'install') {
    $log = Join-Path $ev "inno-$tag.log"
    $args = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$dir`"", "/LOG=`"$log`"") + $extra
    $p = Start-Process $setup -ArgumentList $args -Wait -PassThru
    Write-Host "  Setup ($tag) saiu com $($p.ExitCode)"
    return $p.ExitCode
}

function Uninstall([string[]]$extra = @(), [string]$dir = $game, [string]$tag = 'uninstall') {
    $unins = Join-Path $dir 'BepInEx\Realpolitik\unins000.exe'
    if (-not (Test-Path $unins)) { Write-Host '  (sem desinstalador)'; return -1 }
    $log = Join-Path $ev "inno-$tag.log"
    $p = Start-Process $unins -ArgumentList (@('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$log`"") + $extra) -Wait -PassThru
    # O desinstalador se copia para o %TEMP% e o processo original sai logo: espera a cópia terminar.
    $deadline = (Get-Date).AddSeconds(90)
    do { Start-Sleep 1 } while ((Get-Process | Where-Object { $_.ProcessName -like '_iu*' }) -and (Get-Date) -lt $deadline)
    Start-Sleep 7
    Write-Host "  Desinstalador ($tag) saiu com $($p.ExitCode)"
    return $p.ExitCode
}

$modFiles = 'BepInEx\plugins\CurrencyMod\CurrencyMod.Loader.dll', 'BepInEx\CurrencyModCore\CurrencyMod.dll', 'BepInEx\plugins\MoreEmpires\MoreEmpires.dll'
$bepFiles = 'BepInEx\core\BepInEx.dll', 'BepInEx\core\BepInEx.Preloader.dll', 'BepInEx\core\0Harmony.dll', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version'
$marker = 'BepInEx\Realpolitik\bepinex-instalado-pelo-realpolitik.txt'
Set-Content (Join-Path $ev 'resultado.txt') "$Teste · $Versao · $(Get-Date -Format 'yyyy-MM-dd HH:mm')" -Encoding UTF8

switch ($Teste) {
    'T1' {
        # Máquina limpa simulada: sem BepInEx nem Doorstop. O Setup instala tudo.
        Clean
        $code = Install
        Check 'Setup terminou com código 0' ($code -eq 0) $code
        foreach ($f in $modFiles + $bepFiles) { Check "instalado: $f" (Test-Path (Join-Path $game $f)) }
        Check 'marca "BepInEx instalado pelo Realpolitik"' (Test-Path (Join-Path $game $marker))
        Check 'desinstalador em BepInEx\Realpolitik' (Test-Path (Join-Path $game 'BepInEx\Realpolitik\unins000.exe'))
        Check 'leia-me e termos nos 5 idiomas' (@(Get-ChildItem (Join-Path $game 'BepInEx\Realpolitik') -Filter 'LEIA-ME_*.txt').Count -eq 5)
        Check 'licenças' (@(Get-ChildItem (Join-Path $game 'BepInEx\Realpolitik\licencas')).Count -eq 5)
        Check 'nada de .env, .key, pdb ou credenciais vindo do pacote' (-not (Get-ChildItem (Join-Path $game 'BepInEx') -Recurse -File -Force | Where-Object { $_.Name -match '(?i)\.env|\.key$|\.pdb$' -or $_.FullName -match 'credenciais' }))
        $exp = (Get-FileHash (Join-Path $mod "dist\staging\$Versao\payload\BepInEx\CurrencyModCore\CurrencyMod.dll")).Hash
        Check 'núcleo instalado = o do pacote (sha256)' ((HashOf 'BepInEx\CurrencyModCore\CurrencyMod.dll') -eq $exp)
        $null = Tree 'T1'
    }
    'T3' {
        # Atualizar por cima: configurações, credenciais e logs da IA intactos.
        # Instalação limpa ainda não tem o que proteger: cria uma config, uma credencial e um log da IA de exemplo.
        foreach ($p in 'BepInEx\config\lucas.humankind.currency.cfg', 'BepInEx\config\credenciais\teste.dat', 'BepInEx\DiplomaciaIA\logs\teste.json') {
            $full = Join-Path $game $p
            if (-not (Test-Path $full)) { New-Item -ItemType Directory -Force (Split-Path $full -Parent) | Out-Null; [IO.File]::WriteAllText($full, "conteudo de teste $p") }
        }
        $keep = @(Get-ChildItem (Join-Path $game 'BepInEx\config'), (Join-Path $game 'BepInEx\DiplomaciaIA') -Recurse -File -Force -ErrorAction SilentlyContinue)
        $before = @{}; foreach ($f in $keep) { $before[$f.FullName] = (Get-FileHash $f.FullName).Hash }
        Check "há o que proteger ($($keep.Count) arquivos de config/credenciais/IA)" ($keep.Count -gt 0)
        $code = Install @() $game 'update'
        Check 'Setup por cima terminou com código 0' ($code -eq 0) $code
        $changed = $before.Keys | Where-Object { -not (Test-Path $_) -or (Get-FileHash $_).Hash -ne $before[$_] }
        Check 'config, credenciais e DiplomaciaIA idênticos (hash)' (-not $changed) ($changed -join ', ')
        $log = Get-Content (Join-Path $ev 'inno-update.log') -Raw
        Check 'log mostra a versão anterior' ($log -match "Versão anterior: $([regex]::Escape($Versao))") ''
    }
    'T4' {
        # BepInEx 5.4 já presente com outro plugin: mantém o BepInEx e o plugin.
        Clean
        Expand-Archive (Join-Path $mod 'installer\vendor\BepInEx_win_x64_5.4.23.5.zip') $game -Force
        New-Item -ItemType Directory -Force (Join-Path $game 'BepInEx\plugins\OutroMod') | Out-Null
        [IO.File]::WriteAllText((Join-Path $game 'BepInEx\plugins\OutroMod\OutroMod.txt'), 'plugin de outro autor')
        $coreBefore = HashOf 'BepInEx\core\BepInEx.dll'; $iniBefore = HashOf 'doorstop_config.ini'
        $code = Install
        Check 'Setup terminou com código 0' ($code -eq 0) $code
        Check 'BepInEx existente não foi trocado' ((HashOf 'BepInEx\core\BepInEx.dll') -eq $coreBefore)
        Check 'doorstop_config.ini existente não foi trocado' ((HashOf 'doorstop_config.ini') -eq $iniBefore)
        Check 'plugin do outro autor intacto' (Test-Path (Join-Path $game 'BepInEx\plugins\OutroMod\OutroMod.txt'))
        Check 'sem marca (o BepInEx não é nosso)' (-not (Test-Path (Join-Path $game $marker)))
        foreach ($f in $modFiles) { Check "instalado: $f" (Test-Path (Join-Path $game $f)) }
        $code = Uninstall
        Check 'desinstalou' ($code -eq 0) $code
        Check 'desinstalar manteve o BepInEx (não era nosso)' (Test-Path (Join-Path $game 'BepInEx\core\BepInEx.dll'))
        Check 'desinstalar manteve o outro plugin' (Test-Path (Join-Path $game 'BepInEx\plugins\OutroMod\OutroMod.txt'))
        foreach ($f in $modFiles) { Check "removido: $f" (-not (Test-Path (Join-Path $game $f))) }
        $null = Tree 'T4-depois'
    }
    'T5' {
        # doorstop_config.ini com enabled = false: o Setup religa (silencioso = sim).
        $ini = Join-Path $game 'doorstop_config.ini'
        (Get-Content $ini) -replace '^\s*enabled\s*=\s*true', 'enabled = false' | Set-Content $ini -Encoding ASCII
        Check 'preparado: enabled = false' ([bool]((Get-Content $ini) -match '^\s*enabled\s*=\s*false'))
        $code = Install @() $game 'religar'
        Check 'Setup terminou com código 0' ($code -eq 0) $code
        Check 'BepInEx religado (enabled = true)' ([bool]((Get-Content $ini) -match '^\s*enabled\s*=\s*true')) ((Get-Content $ini | Select-String enabled) -join ' ')
    }
    'T6' {
        # Desinstalar mantendo configurações (padrão do silencioso).
        $cfg = Join-Path $game 'BepInEx\config\lucas.humankind.currency.cfg'
        if (-not (Test-Path $cfg)) { [IO.File]::WriteAllText($cfg, '# teste') }
        New-Item -ItemType Directory -Force (Join-Path $game 'BepInEx\config\credenciais') | Out-Null
        [IO.File]::WriteAllText((Join-Path $game 'BepInEx\config\credenciais\teste.dat'), 'x')
        $code = Uninstall @() $game 'manter'
        Check 'desinstalou' ($code -eq 0) $code
        foreach ($f in $modFiles) { Check "removido: $f" (-not (Test-Path (Join-Path $game $f))) }
        Check 'configuração mantida' (Test-Path $cfg)
        Check 'credenciais mantidas' (Test-Path (Join-Path $game 'BepInEx\config\credenciais\teste.dat'))
        Check 'BepInEx (nosso, sem outro plugin) removido' (-not (Test-Path (Join-Path $game 'BepInEx\core')) -and -not (Test-Path (Join-Path $game 'winhttp.dll')))
        Check 'pasta do desinstalador removida' (-not (Test-Path (Join-Path $game 'BepInEx\Realpolitik')))
        $null = Tree 'T6-depois'
    }
    'T7' {
        # Instala de novo e desinstala apagando tudo (/APAGARTUDO).
        $code = Install @() $game 'reinstalar'
        Check 'reinstalou' ($code -eq 0) $code
        $code = Uninstall @('/APAGARTUDO') $game 'apagar'
        Check 'desinstalou' ($code -eq 0) $code
        Check 'configuração do mod apagada' (-not (Test-Path (Join-Path $game 'BepInEx\config\lucas.humankind.currency.cfg')))
        Check 'credenciais apagadas' (-not (Test-Path (Join-Path $game 'BepInEx\config\credenciais')))
        Check 'sem sobras do mod' (-not (Test-Path (Join-Path $game 'BepInEx\CurrencyModCore')) -and -not (Test-Path (Join-Path $game 'BepInEx\plugins\CurrencyMod')))
        Check 'pasta do jogo limpa (sem BepInEx nem Doorstop)' (-not (Test-Path (Join-Path $game 'BepInEx')) -and -not (Test-Path (Join-Path $game 'winhttp.dll')) -and -not (Test-Path (Join-Path $game 'doorstop_config.ini')))
        Check 'Humankind.exe intacto' (Test-Path (Join-Path $game 'Humankind.exe'))
        $null = Tree 'T7-depois'
    }
    'T8' {
        # Pasta escolhida à mão: cópia falsa válida, pasta sem o exe, pasta sem permissão de escrita.
        $tmp = Join-Path $env:TEMP 'realpolitik-teste-T8'
        if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -Confirm:$false }
        $fake = Join-Path $tmp 'JogoFalso'; $empty = Join-Path $tmp 'SemExe'; $locked = Join-Path $tmp 'Trancada'
        New-Item -ItemType Directory -Force $fake, $empty, $locked | Out-Null
        [IO.File]::WriteAllText((Join-Path $fake 'Humankind.exe'), 'falso'); [IO.File]::WriteAllText((Join-Path $locked 'Humankind.exe'), 'falso')
        $code = Install @() $fake 'falso'
        Check 'pasta válida escolhida à mão: instala' ($code -eq 0 -and (Test-Path (Join-Path $fake 'BepInEx\CurrencyModCore\CurrencyMod.dll'))) $code
        $code = Install @() $empty 'semexe'
        Check 'pasta sem Humankind.exe: recusa' ($code -ne 0 -and -not (Test-Path (Join-Path $empty 'BepInEx'))) $code
        & icacls $locked /deny "$($env:USERNAME):(W,AD,WD)" | Out-Null
        $code = Install @() $locked 'trancada'
        Check 'pasta sem permissão (silencioso, sem /ALLUSERS): recusa sem gravar' ($code -ne 0 -and -not (Test-Path (Join-Path $locked 'BepInEx'))) $code
        $log = Get-Content (Join-Path $ev 'inno-trancada.log') -Raw
        Check 'log explica: sem permissão de escrita' ($log -match 'sem permiss') ''
        & icacls $locked /remove:d $env:USERNAME | Out-Null
        $u = Uninstall @('/APAGARTUDO') $fake 'falso'
        Check 'desinstalou da pasta falsa' ($u -eq 0 -and -not (Test-Path (Join-Path $fake 'BepInEx'))) $u
        Remove-Item $tmp -Recurse -Force -Confirm:$false
    }
    'T9' {
        # Jogo aberto: o Setup silencioso recusa e não mexe em nada.
        if (-not (GameRunning)) { throw 'abra o jogo antes do T9' }
        $before = Tree 'T9-antes'
        $code = Install @() $game 'jogoaberto'
        Check 'Setup recusou com o jogo aberto' ($code -ne 0) $code
        $after = Tree 'T9-depois'
        Check 'nenhum arquivo mudou' (-not (Compare-Object $before $after))
        $log = Get-Content (Join-Path $ev 'inno-jogoaberto.log') -Raw
        Check 'log registra o jogo aberto' ($log -match 'HUMANKIND está aberto') ''
    }
    'T11' {
        # Zip manual numa pasta limpa simulada: mesmo resultado do Setup (fora o desinstalador e a marca).
        Clean
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $game + '\zip-tmp')
        Get-ChildItem (Join-Path $game 'zip-tmp') -Force | ForEach-Object { Move-Item $_.FullName $game -Force }
        Remove-Item (Join-Path $game 'zip-tmp') -Recurse -Force -Confirm:$false
        foreach ($f in $modFiles + $bepFiles) { Check "no lugar: $f" (Test-Path (Join-Path $game $f)) }
        Check 'leia-me na raiz do zip' (Test-Path (Join-Path $game 'REALPOLITIK-README.txt'))
        $t1 = Get-Content (Join-Path $mod "dist\testes\$Versao\T1\arvore-T1.txt") -ErrorAction SilentlyContinue | Where-Object { $_ -notmatch 'unins000|bepinex-instalado|realpolitik\.ico' }
        $t11 = Tree 'T11' | Where-Object { $_ -notmatch 'unins000|bepinex-instalado|realpolitik\.ico' }
        if ($t1) { Check 'mesmos arquivos e hashes do T1 (Setup)' (-not (Compare-Object $t1 $t11)) ((Compare-Object $t1 $t11 | ForEach-Object { $_.InputObject }) -join '; ') }
        Remove-Item (Join-Path $game 'REALPOLITIK-README.txt') -Force -Confirm:$false
    }
    default { throw "teste desconhecido: $Teste" }
}
"RESULTADO $Teste`: $script:ok OK, $script:fail falha(s)" | Tee-Object -FilePath (Join-Path $ev 'resultado.txt') -Append







