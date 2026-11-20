# Generates src/BreakApp/Assets/icon.ico (PNG-compressed entries, 16..256 px).
# Same drawing as the runtime tray icon: accent gradient tile, white timer ring, pause bars.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\src\BreakApp\Assets\icon.ico'

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [single]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = [single]($s - 1)
    $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($w - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($w - 2 * $r, $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc(0, $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.ColorTranslator]::FromHtml('#2563eb')), ([System.Drawing.ColorTranslator]::FromHtml('#60a5fa')), 45
    $g.FillPath($brush, $path)
    $pw = [single]([Math]::Max(1.5, $s * 0.085))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $pw
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $m = [single]($s * 0.2)
    $g.DrawArc($pen, $m, $m, $s - 2 * $m, $s - 2 * $m, -60, 300)
    $bw = [single]($s * 0.075); $bh = [single]($s * 0.24)
    $cx = [single]($s / 2); $cy = [single]($s / 2)
    $white = [System.Drawing.Brushes]::White
    $g.FillRectangle($white, $cx - $bw * 1.6, $cy - $bh / 2, $bw, $bh)
    $g.FillRectangle($white, $cx + $bw * 0.6, $cy - $bh / 2, $bw, $bh)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = foreach ($s in $sizes) { , (New-IconPng $s) }
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Output "wrote $out ($((Get-Item $out).Length) bytes)"

# PNG copy for the README
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot '..\docs\icon.png'), (New-IconPng 256))
Write-Output "wrote docs\icon.png"
