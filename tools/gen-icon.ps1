# Generates src/PwrMon/Assets/app.ico — amber lightning bolt on a dark rounded square.
# ICO packs PNG-compressed entries at 16/24/32/48/64/128/256 px (PNG-in-ICO is fine on Vista+).
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot "..\src\PwrMon\Assets"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outPath = Join-Path $outDir "app.ico"

function New-BasePng {
    $size = 256
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded square background
    $r = 56; $inset = 6; $w = $size - 2 * $inset
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($inset, $inset, $r, $r, 180, 90)
    $path.AddArc($inset + $w - $r, $inset, $r, $r, 270, 90)
    $path.AddArc($inset + $w - $r, $inset + $w - $r, $r, $r, 0, 90)
    $path.AddArc($inset, $inset + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x17, 0x1A, 0x21))
    $g.FillPath($bg, $path)

    # lightning bolt
    $bolt = @(
        (New-Object System.Drawing.PointF(148, 26)),
        (New-Object System.Drawing.PointF(66, 148)),
        (New-Object System.Drawing.PointF(122, 148)),
        (New-Object System.Drawing.PointF(106, 230)),
        (New-Object System.Drawing.PointF(192, 106)),
        (New-Object System.Drawing.PointF(132, 106))
    )
    $amber = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xF5, 0xB6, 0x2E))
    $g.FillPolygon($amber, $bolt)

    $g.Dispose()
    return $bmp
}

$base = New-BasePng
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngBlobs = @()

foreach ($s in $sizes) {
    $resized = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($resized)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.DrawImage($base, 0, 0, $s, $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $resized.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBlobs += , $ms.ToArray()
    $resized.Dispose()
}
$base.Dispose()

# ---- pack ICO ----
$stream = [System.IO.File]::Create($outPath)
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([UInt16]0)               # reserved
$writer.Write([UInt16]1)               # type: icon
$writer.Write([UInt16]$sizes.Count)    # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $writer.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # width (0 = 256)
    $writer.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $writer.Write([Byte]0)             # palette
    $writer.Write([Byte]0)             # reserved
    $writer.Write([UInt16]1)           # planes
    $writer.Write([UInt16]32)          # bpp
    $writer.Write([UInt32]$pngBlobs[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $pngBlobs[$i].Length
}
foreach ($blob in $pngBlobs) { $writer.Write($blob) }
$writer.Dispose()
$stream.Dispose()

Write-Host "wrote $outPath ($((Get-Item $outPath).Length) bytes)"
