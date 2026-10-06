# Minimal static server for local preview (no Node/Python needed).
# Usage: powershell -ExecutionPolicy Bypass -File tools\serve.ps1 [-Port 8790]
# (8765 is taken by the mod's dev kit while the game runs.)
param([int]$Port = 8790)

$root = Split-Path -Parent $PSScriptRoot
$types = @{
  '.html' = 'text/html; charset=utf-8'; '.css' = 'text/css; charset=utf-8'; '.js' = 'text/javascript; charset=utf-8'
  '.json' = 'application/json; charset=utf-8'; '.svg' = 'image/svg+xml'; '.png' = 'image/png'; '.jpg' = 'image/jpeg'
  '.webp' = 'image/webp'; '.avif' = 'image/avif'; '.mp4' = 'video/mp4'; '.webm' = 'video/webm'; '.woff2' = 'font/woff2'
  '.ico' = 'image/x-icon'; '.xml' = 'application/xml'; '.txt' = 'text/plain; charset=utf-8'
}

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Serving $root at http://localhost:$Port/"

while ($listener.IsListening) {
  $ctx = $listener.GetContext()
  $path = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath).TrimStart('/')
  $file = Join-Path $root $path
  if (Test-Path $file -PathType Container) { $file = Join-Path $file 'index.html' }
  $full = [IO.Path]::GetFullPath($file)
  $res = $ctx.Response
  if ($full.StartsWith($root) -and (Test-Path $full -PathType Leaf)) {
    $bytes = [IO.File]::ReadAllBytes($full)
    $ext = [IO.Path]::GetExtension($full).ToLower()
    $res.ContentType = if ($types[$ext]) { $types[$ext] } else { 'application/octet-stream' }
    $res.Headers.Add('Cache-Control', 'no-store')
    $res.ContentLength64 = $bytes.Length
    $res.OutputStream.Write($bytes, 0, $bytes.Length)
  } else {
    $res.StatusCode = 404
  }
  $res.Close()
  Write-Host "$($res.StatusCode) /$path"
}
