# Extracts the texts marked with L.T / L.F / L.N in the core and updates the translation tables
# (_Modding\src\CurrencyMod\Lang\<language>.json: {"Portuguese text": "translation"}).
# New texts come in with an empty translation (""): the mod shows the Portuguese until someone translates them.
# Texts that left the code leave the table (the list goes to Lang\_removidos.txt).
# Usage: powershell -ExecutionPolicy Bypass -File _Modding\tools\dev\extract-texts.ps1 [-Report] [-Language en]
# With -Language, only that table is rewritten (to translate several languages at the same time).
param([switch]$Report, [string]$Language)
$ErrorActionPreference = 'Stop'
$mod = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$src = Join-Path $mod 'src\CurrencyMod'
$langDir = Join-Path $src 'Lang'
$languages = if ($Language) { @($Language) } else { @('en', 'es', 'fr', 'de') }

function Unescape-CSharp([string]$s) {
    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $s.Length; $i++) {
        $c = $s[$i]
        if ($c -ne '\' -or $i + 1 -ge $s.Length) { [void]$sb.Append($c); continue }
        $i++
        switch -CaseSensitive ($s[$i]) {
            'n' { [void]$sb.Append("`n") }
            'r' { [void]$sb.Append("`r") }
            't' { [void]$sb.Append("`t") }
            '0' { [void]$sb.Append([char]0) }
            'u' { [void]$sb.Append([char][Convert]::ToInt32($s.Substring($i + 1, 4), 16)); $i += 4 }
            default { [void]$sb.Append($s[$i]) }
        }
    }
    $sb.ToString()
}

function Json-String([string]$s) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($c in $s.ToCharArray()) {
        switch ($c) {
            '"' { [void]$sb.Append('\"') }
            '\' { [void]$sb.Append('\\') }
            "`n" { [void]$sb.Append('\n') }
            "`r" { [void]$sb.Append('\r') }
            "`t" { [void]$sb.Append('\t') }
            default {
                if ([int]$c -lt 32) { [void]$sb.Append(('\u{0:x4}' -f [int]$c)) } else { [void]$sb.Append($c) }
            }
        }
    }
    [void]$sb.Append('"')
    $sb.ToString()
}

$regular = [regex]'\bL\.(?:T|F|N)\(\s*"((?:[^"\\\r\n]|\\.)*)"'
$verbatim = [regex]'\bL\.(?:T|F|N)\(\s*@"((?:[^"]|"")*)"'
# L.P(n, "{0} turno", "{0} turnos"): both plural forms are keys.
$plural = [regex]'\bL\.P\([^,"]+,\s*"((?:[^"\\\r\n]|\\.)*)"\s*,\s*"((?:[^"\\\r\n]|\\.)*)"'
$keys = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
$where = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem $src -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $rel = $file.FullName.Substring($src.Length + 1)
    foreach ($m in $regular.Matches($text)) { $k = Unescape-CSharp $m.Groups[1].Value; [void]$keys.Add($k); if (-not $where.ContainsKey($k)) { $where[$k] = $rel } }
    foreach ($m in $plural.Matches($text)) { foreach ($g in 1, 2) { $k = Unescape-CSharp $m.Groups[$g].Value; [void]$keys.Add($k); if (-not $where.ContainsKey($k)) { $where[$k] = $rel } } }
    foreach ($m in $verbatim.Matches($text)) { $k = $m.Groups[1].Value.Replace('""', '"'); [void]$keys.Add($k); if (-not $where.ContainsKey($k)) { $where[$k] = $rel } }
}
[void]$keys.Remove('')

$utf8 = New-Object System.Text.UTF8Encoding($false)
$removed = New-Object System.Collections.Generic.List[string]
foreach ($lang in $languages) {
    $path = Join-Path $langDir "$lang.json"
    $old = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    if (Test-Path $path) {
        # ConvertFrom-Json rejects keys that differ only in case: uses the .NET reader.
        Add-Type -AssemblyName System.Web.Extensions
        $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
        $serializer.MaxJsonLength = [int]::MaxValue
        $parsed = $serializer.DeserializeObject([IO.File]::ReadAllText($path, $utf8))
        if ($parsed) { foreach ($p in $parsed.GetEnumerator()) { $old[$p.Key] = [string]$p.Value } }
    }
    $lines = New-Object System.Collections.Generic.List[string]
    $done = 0
    foreach ($k in $keys) {
        $v = if ($old.ContainsKey($k)) { $old[$k] } else { '' }
        if ($v) { $done++ }
        $lines.Add('  ' + (Json-String $k) + ': ' + (Json-String $v))
    }
    foreach ($k in $old.Keys) { if (-not $keys.Contains($k) -and $old[$k]) { $removed.Add("[$lang] $k") } }
    [IO.File]::WriteAllText($path, "{`n" + ($lines -join ",`n") + "`n}`n", $utf8)
    "{0}: {1} texts, {2} translated, {3} missing" -f $lang, $keys.Count, $done, ($keys.Count - $done)
}
if ($removed.Count -gt 0) {
    [IO.File]::WriteAllLines((Join-Path $langDir ('_removidos' + $(if ($Language) { "_$Language" } else { '' }) + '.txt')), $removed, $utf8)
    "$($removed.Count) translations of texts that left the code: Lang\_removidos.txt"
}
if ($Report) {
    $reportLines = foreach ($k in $keys) { "$($where[$k])`t$k" }
    [IO.File]::WriteAllLines((Join-Path $langDir '_chaves.tsv'), $reportLines, $utf8)
    "Source file of each text: Lang\_chaves.tsv"
}
