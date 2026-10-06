# Crops dev\out screenshots into web-sized JPEGs for the site.
# Usage: powershell -ExecutionPolicy Bypass -File tools\crop.ps1
# Each entry: source png, crop x, y, w, h, output name, max width.
Add-Type -AssemblyName System.Drawing
$src = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'dev\out'
$dst = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\shots'
New-Item -ItemType Directory -Force $dst | Out-Null

$jobs = @(
  @('meio16.png',                  380, 140, 1540, 488, 'map-hero',        1920),
  @('espionagem_interceptada.png', 300,   0, 1620, 1080,'spy-full',        1600),
  @('espionagem_interceptada.png',1445, 150,  460, 710, 'spy-panel',        920),
  @('diplomacia_cartas.png',       372,   0, 1176, 440, 'letters-tab',     1176),
  @('rendicao_e7_oferece.png',     372,   0, 1548, 730, 'surrender',       1548),
  @('loc_en_banco2.png',          1445, 145,  465, 735, 'bank-panel',       930),
  @('hattusa_e7.png',              380, 150, 1100, 810, 'map-parchment',   1100)
)

$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$ep = New-Object System.Drawing.Imaging.EncoderParameters 1
$ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 82L

foreach ($j in $jobs) {
  $img = [System.Drawing.Image]::FromFile((Join-Path $src $j[0]))
  $w = [Math]::Min($j[3], $j[6]); $h = [int]($j[4] * $w / $j[3])
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = 'HighQualityBicubic'
  $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $w, $h), (New-Object System.Drawing.Rectangle $j[1], $j[2], $j[3], $j[4]), 'Pixel')
  $out = Join-Path $dst ($j[5] + '.jpg')
  $bmp.Save($out, $codec, $ep)
  $g.Dispose(); $bmp.Dispose(); $img.Dispose()
  '{0,-16} {1}x{2}  {3:N0} KB' -f $j[5], $w, $h, ((Get-Item $out).Length / 1KB)
}
