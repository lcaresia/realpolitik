# Extrai os textos marcados com L.T / L.F / L.N no núcleo e atualiza as tabelas de tradução
# (_Modding\src\CurrencyMod\Lang\<idioma>.json: {"texto em português": "tradução"}).
# Textos novos entram com tradução vazia (""): o mod mostra o português até alguém traduzir.
# Textos que sumiram do código saem da tabela (a lista vai para Lang\_removidos.txt).
# Uso: powershell -ExecutionPolicy Bypass -File _Modding\tools\dev\extrair-textos.ps1 [-Relatorio] [-Idioma en]
# Com -Idioma, só aquela tabela é regravada (para traduzir vários idiomas ao mesmo tempo).
param([switch]$Relatorio, [string]$Idioma)
$ErrorActionPreference = 'Stop'
$mod = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$src = Join-Path $mod 'src\CurrencyMod'
$langDir = Join-Path $src 'Lang'
$languages = if ($Idioma) { @($Idioma) } else { @('en', 'es', 'fr', 'de') }

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
$keys = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
$where = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem $src -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $rel = $file.FullName.Substring($src.Length + 1)
    foreach ($m in $regular.Matches($text)) { $k = Unescape-CSharp $m.Groups[1].Value; [void]$keys.Add($k); if (-not $where.ContainsKey($k)) { $where[$k] = $rel } }
    foreach ($m in $verbatim.Matches($text)) { $k = $m.Groups[1].Value.Replace('""', '"'); [void]$keys.Add($k); if (-not $where.ContainsKey($k)) { $where[$k] = $rel } }
}
[void]$keys.Remove('')

$utf8 = New-Object System.Text.UTF8Encoding($false)
$removed = New-Object System.Collections.Generic.List[string]
foreach ($lang in $languages) {
    $path = Join-Path $langDir "$lang.json"
    $old = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    if (Test-Path $path) {
        # ConvertFrom-Json não aceita chaves que só mudam maiúsculas: usa o leitor do .NET.
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
    "{0}: {1} textos, {2} traduzidos, {3} faltando" -f $lang, $keys.Count, $done, ($keys.Count - $done)
}
if ($removed.Count -gt 0) {
    [IO.File]::WriteAllLines((Join-Path $langDir ('_removidos' + $(if ($Idioma) { "_$Idioma" } else { '' }) + '.txt')), $removed, $utf8)
    "$($removed.Count) traduções de textos que saíram do código: Lang\_removidos.txt"
}
if ($Relatorio) {
    $report = foreach ($k in $keys) { "$($where[$k])`t$k" }
    [IO.File]::WriteAllLines((Join-Path $langDir '_chaves.tsv'), $report, $utf8)
    "Arquivo de cada texto: Lang\_chaves.tsv"
}
