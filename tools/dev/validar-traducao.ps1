# Confere uma tabela de tradução (_Modding\src\CurrencyMod\Lang\<idioma>.json) contra o português:
# mesmos marcadores {0} {1:0.0}, mesmas tags <...> e [...], chaves duplas {{ }} iguais, nada vazio.
# Uso: powershell -ExecutionPolicy Bypass -File _Modding\tools\dev\validar-traducao.ps1 -Idioma en
param([Parameter(Mandatory = $true)][string]$Idioma)
$ErrorActionPreference = 'Stop'
$mod = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$path = Join-Path $mod "src\CurrencyMod\Lang\$Idioma.json"
$utf8 = New-Object System.Text.UTF8Encoding($false)
# ConvertFrom-Json não aceita chaves que só mudam maiúsculas ("BANCO CENTRAL" e "Banco Central"): usa o leitor do .NET.
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
    if ((Tokens $k $placeholder) -ne (Tokens $v $placeholder)) { $issues += "marcadores: '$(Tokens $k $placeholder)' vs '$(Tokens $v $placeholder)'" }
    if ((Tokens $k $tag) -ne (Tokens $v $tag)) { $issues += "tags: '$(Tokens $k $tag)' vs '$(Tokens $v $tag)'" }
    if (([regex]::Matches($k, '\{\{')).Count -ne ([regex]::Matches($v, '\{\{')).Count -or ([regex]::Matches($k, '\}\}')).Count -ne ([regex]::Matches($v, '\}\}')).Count) { $issues += 'chaves duplas {{ }}' }
    if ($k.EndsWith(' ') -ne $v.EndsWith(' ') -or $k.StartsWith(' ') -ne $v.StartsWith(' ')) { $issues += 'espaço no começo/fim' }
    if ($issues) { $problems++; "PROBLEMA  $k`n          -> $v`n          $($issues -join '; ')" }
}
"$Idioma`: $total textos, $($total - $empty) traduzidos, $empty vazios, $problems com problema"
