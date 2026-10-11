# Backup of the modding project to Documents\HumankindModding\backups\HumankindModding_<date>.zip: src (without bin/obj),
# docs, research, cronicas, tools, README, installed DLLs, the mod's config and Claude's memory.
# NEVER includes BepInEx\config\deepseek.key (the API key) nor BepInEx\config\credenciais (the providers' keys and
# logins): the script stops with an error if any of them gets into the list.
# Usage: powershell -File _Modding\tools\dev\backup.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$mod = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$root = Split-Path $mod -Parent
$memory = Join-Path $env:USERPROFILE '.claude\projects\C--Program-Files--x86--Steam-steamapps-common-Humankind\memory'
$outDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'HumankindModding\backups'
New-Item -ItemType Directory -Force $outDir | Out-Null
$stamp = Get-Date -Format 'yyyy-MM-dd_HHmm'
$zipPath = Join-Path $outDir "HumankindModding_$stamp.zip"

$entries = New-Object System.Collections.Generic.List[object]
function Add-Tree([string]$dir, [string]$prefix) {
    if (-not (Test-Path $dir)) { return }
    Get-ChildItem $dir -Recurse -File | Where-Object {
        $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Name -ne 'deepseek.key'
    } | ForEach-Object {
        $rel = $_.FullName.Substring($dir.Length).TrimStart('\')
        $entries.Add(@($_.FullName, (Join-Path $prefix $rel)))
    }
}
function Add-File([string]$file, [string]$name) {
    if ((Test-Path $file) -and (Split-Path $file -Leaf) -ne 'deepseek.key') { $entries.Add(@($file, $name)) }
}

Add-Tree (Join-Path $mod 'src') '_Modding\src'
Add-Tree (Join-Path $mod 'docs') '_Modding\docs'
Add-Tree (Join-Path $mod 'research') '_Modding\research'
Add-Tree (Join-Path $mod 'cronicas') '_Modding\cronicas'
Add-Tree (Join-Path $mod 'tools') '_Modding\tools'
Add-Tree (Join-Path $mod 'loja') '_Modding\loja'
Add-File (Join-Path $mod 'README.md') '_Modding\README.md'
Add-Tree (Join-Path $root 'BepInEx\plugins') 'installed\plugins'
Add-Tree (Join-Path $root 'BepInEx\CurrencyModCore') 'installed\CurrencyModCore'
Add-File (Join-Path $root 'BepInEx\config\lucas.humankind.currency.cfg') 'installed\config\lucas.humankind.currency.cfg'
Add-File (Join-Path $root 'BepInEx\config\lucas.humankind.moreempires.cfg') 'installed\config\lucas.humankind.moreempires.cfg'
Add-Tree $memory 'claude-memory'

if ($entries | Where-Object { $_[0] -match 'deepseek\.key' }) { throw 'the API key got into the list' }
# The providers' keys and logins (encrypted, but personal) never go into the backup.
if ($entries | Where-Object { $_[0] -match '\\credenciais\\' -or $_[1] -match '\\credenciais\\' }) { throw 'the providers credentials got into the list' }

$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($e in $entries) {
        $fs = [IO.File]::Open($e[0], 'Open', 'Read', 'ReadWrite')
        try {
            $entry = $zip.CreateEntry($e[1], 'Optimal')
            $es = $entry.Open()
            try { $fs.CopyTo($es) } finally { $es.Dispose() }
        } finally { $fs.Dispose() }
    }
} finally { $zip.Dispose() }

$len = (Get-Item $zipPath).Length
"$zipPath - $($entries.Count) files, $([Math]::Round($len / 1KB)) KB"
