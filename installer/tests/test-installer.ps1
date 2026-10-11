# Installer tests on the REAL game installation (Windows Home has no Sandbox). Before running: back up with hashes
# into Documents\HumankindModding\pre-teste-instalador\<date>\ (see docs\prompt-instalador.md, phase 3); at the end,
# restore and check the hashes. Each test writes its evidence to dist\tests\<version>\<test>\ (make-release deletes dist\<version>).
# Usage: powershell -ExecutionPolicy Bypass -File _Modding\installer\tests\test-installer.ps1 -Version 1.0.0 -Test T1
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Test
)
$ErrorActionPreference = 'Stop'
$mod = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$game = Split-Path $mod -Parent
$dist = Join-Path $mod "dist\$Version"
$setup = Join-Path $dist "Realpolitik_Setup_$Version.exe"
$zip = Join-Path $dist "Realpolitik_$($Version)_manual.zip"
$ev = Join-Path $mod "dist\tests\$Version\$Test"
New-Item -ItemType Directory -Force $ev | Out-Null
$script:ok = 0; $script:fail = 0

function Check([string]$name, [bool]$cond, $detail = '') {
    $line = if ($cond) { "  OK    $name" } else { "  FAIL  $name  ($detail)" }
    if ($cond) { $script:ok++ } else { $script:fail++ }
    Write-Host $line
    Add-Content (Join-Path $ev 'result.txt') $line -Encoding UTF8
}

function GameRunning { [bool](Get-Process Humankind -ErrorAction SilentlyContinue) }

# Removes BepInEx and the Doorstop files from the game folder (simulated clean machine). The backup has everything.
function Clean([string]$dir = $game) {
    if (GameRunning) { throw 'close the game' }
    foreach ($p in 'BepInEx', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
        $full = Join-Path $dir $p
        if (Test-Path $full) { Remove-Item $full -Recurse -Force -Confirm:$false }
    }
}

# Tree (path + sha256) of what the mod/BepInEx touches in the game folder.
function Tree([string]$name, [string]$dir = $game) {
    $files = @()
    foreach ($p in 'BepInEx', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
        $full = Join-Path $dir $p
        if (Test-Path $full) { $files += Get-ChildItem $full -Recurse -File -Force | Where-Object { $_.Extension -ne '.log' -and $_.FullName -notmatch '\\BepInEx\\cache\\' } }
    }
    $lines = $files | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName).Hash.Substring(0, 16), $_.FullName.Substring($dir.Length + 1) } | Sort-Object { $_.Substring(18) }
    $lines | Set-Content (Join-Path $ev "tree-$name.txt") -Encoding UTF8
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
    Write-Host "  Setup ($tag) exited with $($p.ExitCode)"
    return $p.ExitCode
}

function Uninstall([string[]]$extra = @(), [string]$dir = $game, [string]$tag = 'uninstall') {
    $unins = Join-Path $dir 'BepInEx\Realpolitik\unins000.exe'
    if (-not (Test-Path $unins)) { Write-Host '  (no uninstaller)'; return -1 }
    $log = Join-Path $ev "inno-$tag.log"
    $p = Start-Process $unins -ArgumentList (@('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$log`"") + $extra) -Wait -PassThru
    # The uninstaller copies itself to %TEMP% and the original process exits right away: wait for the copy to finish.
    $deadline = (Get-Date).AddSeconds(90)
    do { Start-Sleep 1 } while ((Get-Process | Where-Object { $_.ProcessName -like '_iu*' }) -and (Get-Date) -lt $deadline)
    Start-Sleep 7
    Write-Host "  Uninstaller ($tag) exited with $($p.ExitCode)"
    return $p.ExitCode
}

$modFiles = 'BepInEx\plugins\CurrencyMod\CurrencyMod.Loader.dll', 'BepInEx\CurrencyModCore\CurrencyMod.dll', 'BepInEx\plugins\MoreEmpires\MoreEmpires.dll'
$bepFiles = 'BepInEx\core\BepInEx.dll', 'BepInEx\core\BepInEx.Preloader.dll', 'BepInEx\core\0Harmony.dll', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version'
# Installed file names (marker, LEIA-ME_*, licencas\, config\credenciais, DiplomaciaIA) are data and keep their names.
$marker = 'BepInEx\Realpolitik\bepinex-instalado-pelo-realpolitik.txt'
Set-Content (Join-Path $ev 'result.txt') "$Test - $Version - $(Get-Date -Format 'yyyy-MM-dd HH:mm')" -Encoding UTF8

switch ($Test) {
    'T1' {
        # Simulated clean machine: no BepInEx and no Doorstop. The Setup installs everything.
        Clean
        $code = Install
        Check 'Setup finished with code 0' ($code -eq 0) $code
        foreach ($f in $modFiles + $bepFiles) { Check "installed: $f" (Test-Path (Join-Path $game $f)) }
        Check 'marker "BepInEx installed by Realpolitik"' (Test-Path (Join-Path $game $marker))
        Check 'uninstaller in BepInEx\Realpolitik' (Test-Path (Join-Path $game 'BepInEx\Realpolitik\unins000.exe'))
        Check 'readme and terms in all 5 languages' (@(Get-ChildItem (Join-Path $game 'BepInEx\Realpolitik') -Filter 'LEIA-ME_*.txt').Count -eq 5)
        Check 'licenses' (@(Get-ChildItem (Join-Path $game 'BepInEx\Realpolitik\licencas')).Count -eq 5)
        Check 'no .env, .key, pdb or credentials coming from the package' (-not (Get-ChildItem (Join-Path $game 'BepInEx') -Recurse -File -Force | Where-Object { $_.Name -match '(?i)\.env|\.key$|\.pdb$' -or $_.FullName -match 'credenciais' }))
        $exp = (Get-FileHash (Join-Path $mod "dist\staging\$Version\payload\BepInEx\CurrencyModCore\CurrencyMod.dll")).Hash
        Check 'installed core = the packaged one (sha256)' ((HashOf 'BepInEx\CurrencyModCore\CurrencyMod.dll') -eq $exp)
        $null = Tree 'T1'
    }
    'T3' {
        # Update over an existing install: settings, credentials and AI logs untouched.
        # A clean install has nothing to protect yet: creates a sample config, credential and AI log.
        foreach ($p in 'BepInEx\config\lucas.humankind.currency.cfg', 'BepInEx\config\credenciais\teste.dat', 'BepInEx\DiplomaciaIA\logs\teste.json') {
            $full = Join-Path $game $p
            if (-not (Test-Path $full)) { New-Item -ItemType Directory -Force (Split-Path $full -Parent) | Out-Null; [IO.File]::WriteAllText($full, "test content $p") }
        }
        $keep = @(Get-ChildItem (Join-Path $game 'BepInEx\config'), (Join-Path $game 'BepInEx\DiplomaciaIA') -Recurse -File -Force -ErrorAction SilentlyContinue)
        $before = @{}; foreach ($f in $keep) { $before[$f.FullName] = (Get-FileHash $f.FullName).Hash }
        Check "there is something to protect ($($keep.Count) config/credential/AI files)" ($keep.Count -gt 0)
        $code = Install @() $game 'update'
        Check 'Setup over the existing install finished with code 0' ($code -eq 0) $code
        $changed = $before.Keys | Where-Object { -not (Test-Path $_) -or (Get-FileHash $_).Hash -ne $before[$_] }
        Check 'config, credentials and DiplomaciaIA identical (hash)' (-not $changed) ($changed -join ', ')
        $log = Get-Content (Join-Path $ev 'inno-update.log') -Raw
        Check 'log shows the previous version' ($log -match "Previous version: $([regex]::Escape($Version))") ''
    }
    'T4' {
        # BepInEx 5.4 already present with another plugin: keeps BepInEx and the plugin.
        Clean
        Expand-Archive (Join-Path $mod 'installer\vendor\BepInEx_win_x64_5.4.23.5.zip') $game -Force
        New-Item -ItemType Directory -Force (Join-Path $game 'BepInEx\plugins\OtherMod') | Out-Null
        [IO.File]::WriteAllText((Join-Path $game 'BepInEx\plugins\OtherMod\OtherMod.txt'), 'plugin by another author')
        $coreBefore = HashOf 'BepInEx\core\BepInEx.dll'; $iniBefore = HashOf 'doorstop_config.ini'
        $code = Install
        Check 'Setup finished with code 0' ($code -eq 0) $code
        Check 'existing BepInEx was not replaced' ((HashOf 'BepInEx\core\BepInEx.dll') -eq $coreBefore)
        Check 'existing doorstop_config.ini was not replaced' ((HashOf 'doorstop_config.ini') -eq $iniBefore)
        Check 'other author''s plugin intact' (Test-Path (Join-Path $game 'BepInEx\plugins\OtherMod\OtherMod.txt'))
        Check 'no marker (BepInEx is not ours)' (-not (Test-Path (Join-Path $game $marker)))
        foreach ($f in $modFiles) { Check "installed: $f" (Test-Path (Join-Path $game $f)) }
        $code = Uninstall
        Check 'uninstalled' ($code -eq 0) $code
        Check 'uninstall kept BepInEx (it was not ours)' (Test-Path (Join-Path $game 'BepInEx\core\BepInEx.dll'))
        Check 'uninstall kept the other plugin' (Test-Path (Join-Path $game 'BepInEx\plugins\OtherMod\OtherMod.txt'))
        foreach ($f in $modFiles) { Check "removed: $f" (-not (Test-Path (Join-Path $game $f))) }
        $null = Tree 'T4-after'
    }
    'T5' {
        # doorstop_config.ini with enabled = false: the Setup turns it back on (silent = yes).
        $ini = Join-Path $game 'doorstop_config.ini'
        (Get-Content $ini) -replace '^\s*enabled\s*=\s*true', 'enabled = false' | Set-Content $ini -Encoding ASCII
        Check 'prepared: enabled = false' ([bool]((Get-Content $ini) -match '^\s*enabled\s*=\s*false'))
        $code = Install @() $game 'reenable'
        Check 'Setup finished with code 0' ($code -eq 0) $code
        Check 'BepInEx turned back on (enabled = true)' ([bool]((Get-Content $ini) -match '^\s*enabled\s*=\s*true')) ((Get-Content $ini | Select-String enabled) -join ' ')
    }
    'T6' {
        # Uninstall keeping settings (the silent default).
        $cfg = Join-Path $game 'BepInEx\config\lucas.humankind.currency.cfg'
        if (-not (Test-Path $cfg)) { [IO.File]::WriteAllText($cfg, '# test') }
        New-Item -ItemType Directory -Force (Join-Path $game 'BepInEx\config\credenciais') | Out-Null
        [IO.File]::WriteAllText((Join-Path $game 'BepInEx\config\credenciais\teste.dat'), 'x')
        $code = Uninstall @() $game 'keep'
        Check 'uninstalled' ($code -eq 0) $code
        foreach ($f in $modFiles) { Check "removed: $f" (-not (Test-Path (Join-Path $game $f))) }
        Check 'settings kept' (Test-Path $cfg)
        Check 'credentials kept' (Test-Path (Join-Path $game 'BepInEx\config\credenciais\teste.dat'))
        Check 'BepInEx (ours, no other plugin) removed' (-not (Test-Path (Join-Path $game 'BepInEx\core')) -and -not (Test-Path (Join-Path $game 'winhttp.dll')))
        Check 'uninstaller folder removed' (-not (Test-Path (Join-Path $game 'BepInEx\Realpolitik')))
        $null = Tree 'T6-after'
    }
    'T7' {
        # Install again and uninstall deleting everything (/DELETEALL).
        $code = Install @() $game 'reinstall'
        Check 'reinstalled' ($code -eq 0) $code
        $code = Uninstall @('/DELETEALL') $game 'deleteall'
        Check 'uninstalled' ($code -eq 0) $code
        Check 'mod settings deleted' (-not (Test-Path (Join-Path $game 'BepInEx\config\lucas.humankind.currency.cfg')))
        Check 'credentials deleted' (-not (Test-Path (Join-Path $game 'BepInEx\config\credenciais')))
        Check 'no mod leftovers' (-not (Test-Path (Join-Path $game 'BepInEx\CurrencyModCore')) -and -not (Test-Path (Join-Path $game 'BepInEx\plugins\CurrencyMod')))
        Check 'game folder clean (no BepInEx or Doorstop)' (-not (Test-Path (Join-Path $game 'BepInEx')) -and -not (Test-Path (Join-Path $game 'winhttp.dll')) -and -not (Test-Path (Join-Path $game 'doorstop_config.ini')))
        Check 'Humankind.exe intact' (Test-Path (Join-Path $game 'Humankind.exe'))
        $null = Tree 'T7-after'
    }
    'T8' {
        # Folder picked by hand: valid fake copy, folder without the exe, folder without write permission.
        $tmp = Join-Path $env:TEMP 'realpolitik-test-T8'
        if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -Confirm:$false }
        $fake = Join-Path $tmp 'FakeGame'; $empty = Join-Path $tmp 'NoExe'; $locked = Join-Path $tmp 'Locked'
        New-Item -ItemType Directory -Force $fake, $empty, $locked | Out-Null
        [IO.File]::WriteAllText((Join-Path $fake 'Humankind.exe'), 'fake'); [IO.File]::WriteAllText((Join-Path $locked 'Humankind.exe'), 'fake')
        $code = Install @() $fake 'fake'
        Check 'valid folder picked by hand: installs' ($code -eq 0 -and (Test-Path (Join-Path $fake 'BepInEx\CurrencyModCore\CurrencyMod.dll'))) $code
        $code = Install @() $empty 'noexe'
        Check 'folder without Humankind.exe: refuses' ($code -ne 0 -and -not (Test-Path (Join-Path $empty 'BepInEx'))) $code
        & icacls $locked /deny "$($env:USERNAME):(W,AD,WD)" | Out-Null
        $code = Install @() $locked 'locked'
        Check 'folder without permission (silent, no /ALLUSERS): refuses without writing' ($code -ne 0 -and -not (Test-Path (Join-Path $locked 'BepInEx'))) $code
        $log = Get-Content (Join-Path $ev 'inno-locked.log') -Raw
        Check 'log explains: folder not writable' ($log -match 'not writable') ''
        & icacls $locked /remove:d $env:USERNAME | Out-Null
        $u = Uninstall @('/DELETEALL') $fake 'fake'
        Check 'uninstalled from the fake folder' ($u -eq 0 -and -not (Test-Path (Join-Path $fake 'BepInEx'))) $u
        Remove-Item $tmp -Recurse -Force -Confirm:$false
    }
    'T9' {
        # Game running: the silent Setup refuses and touches nothing.
        if (-not (GameRunning)) { throw 'start the game before T9' }
        $before = Tree 'T9-before'
        $code = Install @() $game 'gamerunning'
        Check 'Setup refused with the game running' ($code -ne 0) $code
        $after = Tree 'T9-after'
        Check 'no file changed' (-not (Compare-Object $before $after))
        $log = Get-Content (Join-Path $ev 'inno-gamerunning.log') -Raw
        Check 'log records the running game' ($log -match 'HUMANKIND is running') ''
    }
    'T11' {
        # Manual zip in a simulated clean folder: same result as the Setup (except the uninstaller and the marker).
        Clean
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $game + '\zip-tmp')
        Get-ChildItem (Join-Path $game 'zip-tmp') -Force | ForEach-Object { Move-Item $_.FullName $game -Force }
        Remove-Item (Join-Path $game 'zip-tmp') -Recurse -Force -Confirm:$false
        foreach ($f in $modFiles + $bepFiles) { Check "in place: $f" (Test-Path (Join-Path $game $f)) }
        Check 'readme at the zip root' (Test-Path (Join-Path $game 'REALPOLITIK-README.txt'))
        $t1 = Get-Content (Join-Path $mod "dist\tests\$Version\T1\tree-T1.txt") -ErrorAction SilentlyContinue | Where-Object { $_ -notmatch 'unins000|bepinex-instalado|realpolitik\.ico' }
        $t11 = Tree 'T11' | Where-Object { $_ -notmatch 'unins000|bepinex-instalado|realpolitik\.ico' }
        if ($t1) { Check 'same files and hashes as T1 (Setup)' (-not (Compare-Object $t1 $t11)) ((Compare-Object $t1 $t11 | ForEach-Object { $_.InputObject }) -join '; ') }
        Remove-Item (Join-Path $game 'REALPOLITIK-README.txt') -Force -Confirm:$false
    }
    default { throw "unknown test: $Test" }
}
"RESULT $Test`: $script:ok OK, $script:fail failure(s)" | Tee-Object -FilePath (Join-Path $ev 'result.txt') -Append
