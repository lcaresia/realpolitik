# Checks a translation table (_Modding\src\CurrencyMod\Lang\<language>.json) against the Portuguese:
# same placeholders {0} {1:0.0}, same tags <...> and [...], same double braces {{ }}, nothing empty.
# Usage: powershell -ExecutionPolicy Bypass -File _Modding\tools\dev\validate-translation.ps1 -Language en
param([Parameter(Mandatory = $true)][string]$Language)
$ErrorActionPreference = 'Stop'
$mod = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$path = Join-Path $mod "src\CurrencyMod\Lang\$Language.json"
$utf8 = New-Object System.Text.UTF8Encoding($false)
# ConvertFrom-Json rejects keys that differ only in case ("BANCO CENTRAL" and "Banco Central"): uses the .NET reader.
Add-Type -AssemblyName System.Web.Extensions
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$serializer.MaxJsonLength = [int]::MaxValue
$data = $serializer.DeserializeObject([IO.File]::ReadAllText($path, $utf8))

function Tokens([string]$s, [string]$pattern) {
    ([regex]::Matches($s, $pattern) | ForEach-Object { $_.Value } | Sort-Object) -join ' '
}
$placeholder = '(?<!\{)\{\d+(?:[,:][^{}]*)?\}(?!\})'
$tag = '<[^<>]+>|\[[a-zA-Z_]+\]'
$problems = 0
$empty = 0
$total = 0
foreach ($p in $data.GetEnumerator()) {
    $total++
    $k = $p.Key; $v = [string]$p.Value
    if (-not $v) { $empty++; continue }
    $issues = @()
    if ((Tokens $k $placeholder) -ne (Tokens $v $placeholder)) { $issues += "placeholders: '$(Tokens $k $placeholder)' vs '$(Tokens $v $placeholder)'" }
    if ((Tokens $k $tag) -ne (Tokens $v $tag)) { $issues += "tags: '$(Tokens $k $tag)' vs '$(Tokens $v $tag)'" }
    if (([regex]::Matches($k, '\{\{')).Count -ne ([regex]::Matches($v, '\{\{')).Count -or ([regex]::Matches($k, '\}\}')).Count -ne ([regex]::Matches($v, '\}\}')).Count) { $issues += 'double braces {{ }}' }
    if ($k.EndsWith(' ') -ne $v.EndsWith(' ') -or $k.StartsWith(' ') -ne $v.StartsWith(' ')) { $issues += 'space at start/end' }
    if ($issues) { $problems++; "PROBLEM   $k`n          -> $v`n          $($issues -join '; ')" }
}
"$Language`: $total texts, $($total - $empty) translated, $empty empty, $problems with problems"
