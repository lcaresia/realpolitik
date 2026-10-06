# Gera a arte do instalador a partir do selo do site (site\assets\perfil\perfil-selo-1-512.png):
#   realpolitik.ico            ícone do Setup e do desinstalador (256/48/32/16, PNG dentro do .ico)
#   pequena-55.bmp, -110.bmp   canto superior direito das páginas (100% e 200%)
#   grande-164.bmp, -328.bmp   lateral das páginas de boas-vindas e fim (100% e 200%)
# Uso: powershell -ExecutionPolicy Bypass -File _Modding\installer\art\gerar-arte.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$here = $PSScriptRoot
$mod = Split-Path (Split-Path $here -Parent) -Parent
$src = [Drawing.Image]::FromFile((Join-Path $mod 'site\assets\perfil\perfil-selo-1-512.png'))
$seal = New-Object Drawing.Rectangle 92, 118, 300, 300   # recorte no selo (lacre + pena)
$navy = [Drawing.ColorTranslator]::FromHtml('#0c1522')
$gold = [Drawing.ColorTranslator]::FromHtml('#e2c172')

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object Drawing.Bitmap $w, $h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'; $g.TextRenderingHint = 'AntiAliasGridFit'
    return $bmp, $g
}

function Save-Bmp($bmp, [string]$name) {
    # BMP de 24 bits (o Inno não usa transparência em BMP).
    $flat = New-Object Drawing.Bitmap $bmp.Width, $bmp.Height, ([Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [Drawing.Graphics]::FromImage($flat); $g.Clear($navy); $g.DrawImage($bmp, 0, 0, $bmp.Width, $bmp.Height); $g.Dispose()
    $flat.Save((Join-Path $here $name), [Drawing.Imaging.ImageFormat]::Bmp); $flat.Dispose()
}

# Ícone: PNGs de 256, 48, 32 e 16 dentro de um .ico.
$pngs = @()
foreach ($s in 256, 48, 32, 16) {
    $bmp, $g = New-Canvas $s $s
    $g.DrawImage($src, (New-Object Drawing.Rectangle 0, 0, $s, $s), $seal, 'Pixel'); $g.Dispose()
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $pngs += , @($s, $ms.ToArray())
}
$ico = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p[0] -ge 256) { 0 } else { $p[0] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$p[1].Length); $w.Write([uint32]$offset)
    $offset += $p[1].Length
}
foreach ($p in $pngs) { $w.Write($p[1]) }
[IO.File]::WriteAllBytes((Join-Path $here 'realpolitik.ico'), $ico.ToArray())

# Imagem pequena (canto): o selo sobre o azul do site.
foreach ($s in 55, 110) {
    $bmp, $g = New-Canvas $s $s
    $g.Clear($navy); $g.DrawImage($src, (New-Object Drawing.Rectangle 0, 0, $s, $s), $seal, 'Pixel'); $g.Dispose()
    Save-Bmp $bmp "pequena-$s.bmp"; $bmp.Dispose()
}

# Imagem grande (lateral): azul, o selo no alto, o nome em dourado embaixo.
foreach ($k in 1, 2) {
    $W = 164 * $k; $H = 314 * $k
    $bmp, $g = New-Canvas $W $H
    $g.Clear($navy)
    $size = [int](136 * $k); $x = [int](($W - $size) / 2)
    $g.DrawImage($src, (New-Object Drawing.Rectangle $x, ([int](40 * $k)), $size, $size), $seal, 'Pixel')
    $brush = New-Object Drawing.SolidBrush $gold
    $fmt = New-Object Drawing.StringFormat; $fmt.Alignment = 'Center'
    $title = New-Object Drawing.Font 'Georgia', ([single](15 * $k)), ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
    $sub = New-Object Drawing.Font 'Georgia', ([single](11 * $k)), ([Drawing.FontStyle]::Italic), ([Drawing.GraphicsUnit]::Pixel)
    $g.DrawString('REALPOLITIK', $title, $brush, (New-Object Drawing.RectangleF 0, ([single](196 * $k)), $W, ([single](24 * $k))), $fmt)
    $g.DrawString('Living Nations', $sub, $brush, (New-Object Drawing.RectangleF 0, ([single](220 * $k)), $W, ([single](20 * $k))), $fmt)
    $g.DrawString('for HUMANKIND', $sub, $brush, (New-Object Drawing.RectangleF 0, ([single](236 * $k)), $W, ([single](20 * $k))), $fmt)
    $g.Dispose()
    Save-Bmp $bmp "grande-$W.bmp"; $bmp.Dispose()
}
$src.Dispose()
Get-ChildItem $here -Include *.ico, *.bmp -Recurse | ForEach-Object { '{0,-18} {1,8:N0} B' -f $_.Name, $_.Length }
