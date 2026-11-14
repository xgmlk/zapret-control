# Generates src\app.ico from a vector-style recreation of the reference icon:
# white rounded square + soft ON toggle + green "zapret" wordmark.
# Uses only System.Drawing (in-box), so the build stays dependency-free.
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
$masterPath = Join-Path $src 'app-icon-1024.png'
$icoPath = Join-Path $src 'app.ico'

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

$size = 1024
$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([System.Drawing.Color]::Transparent)

# White rounded square
$square = New-Object System.Drawing.RectangleF(4, 4, ($size - 8), ($size - 8))
$squarePath = New-RoundedPath $square.X $square.Y $square.Width $square.Height 216
$squareBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $square,
    [System.Drawing.Color]::FromArgb(255, 255, 255),
    [System.Drawing.Color]::FromArgb(247, 247, 246),
    90.0)
$g.FillPath($squareBrush, $squarePath)
$squarePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(236, 235, 232), 3)
$g.DrawPath($squarePen, $squarePath)

# Toggle track
$track = New-Object System.Drawing.RectangleF(128, 293, 772, 338)
$trackRadius = $track.Height / 2
$trackPath = New-RoundedPath $track.X $track.Y $track.Width $track.Height $trackRadius
$trackBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $track,
    [System.Drawing.Color]::FromArgb(243, 242, 239),
    [System.Drawing.Color]::FromArgb(230, 229, 226),
    90.0)
$g.FillPath($trackBrush, $trackPath)

# Inner top shadow of the track
$g.SetClip($trackPath)
$innerRect = New-Object System.Drawing.RectangleF($track.X, $track.Y, $track.Width, ($track.Height * 0.45))
$innerBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $innerRect,
    [System.Drawing.Color]::FromArgb(26, 60, 58, 54),
    [System.Drawing.Color]::FromArgb(0, 60, 58, 54),
    90.0)
$g.FillRectangle($innerBrush, $innerRect)
$g.ResetClip()

$trackPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(213, 211, 206), 4)
$g.DrawPath($trackPen, $trackPath)

# Knob
$margin = 8
$knobSize = $track.Height - ($margin * 2)
$knob = New-Object System.Drawing.RectangleF(
    ($track.Right - $margin - $knobSize),
    ($track.Y + $margin),
    $knobSize,
    $knobSize)

for ($layer = 9; $layer -ge 1; $layer--) {
    $grow = $layer * 4
    $shadow = New-Object System.Drawing.RectangleF(
        ($knob.X - ($grow / 2)),
        ($knob.Y + 10),
        ($knob.Width + $grow),
        ($knob.Height + $grow))
    $alpha = [int](14 - $layer)
    if ($alpha -lt 1) { $alpha = 1 }
    $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($alpha, 70, 68, 62))
    $g.FillEllipse($shadowBrush, $shadow)
    $shadowBrush.Dispose()
}

$knobBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $knob,
    [System.Drawing.Color]::White,
    [System.Drawing.Color]::FromArgb(243, 242, 239),
    90.0)
$g.FillEllipse($knobBrush, $knob)
$knobPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(226, 224, 219), 3)
$g.DrawEllipse($knobPen, $knob)

# "ON"
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$onFont = New-Object System.Drawing.Font(
    'Segoe UI', 170, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$onRect = New-Object System.Drawing.RectangleF($track.X, $track.Y, ($knob.Left - $track.X), $track.Height)
$onBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(154, 154, 154))
$g.DrawString('ON', $onFont, $onBrush, $onRect, $format)

# "zapret"
$zapretFont = New-Object System.Drawing.Font(
    'Segoe UI', 190, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$zapretRect = New-Object System.Drawing.RectangleF(0, 660, $size, 220)
$zapretBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(26, 160, 22))
$g.DrawString('zapret', $zapretFont, $zapretBrush, $zapretRect, $format)

$g.Dispose()
$bmp.Save($masterPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Multi-size ICO (PNG-compressed entries, supported by Windows Vista+)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$blobs = @()
foreach ($s in $sizes) {
    $scaled = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($scaled)
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $sg.DrawImage($bmp, 0, 0, $s, $s)
    $sg.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $scaled.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $blobs += ,$stream.ToArray()
    $stream.Dispose()
    $scaled.Dispose()
}
$bmp.Dispose()

$file = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = $sizes[$i]
    if ($dimension -ge 256) { $dimension = 0 }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$blobs[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $blobs[$i].Length
}
foreach ($blob in $blobs) {
    $writer.Write($blob)
}
$writer.Close()

Write-Host ("Icon: {0} ({1:N0} bytes)" -f $icoPath, (Get-Item $icoPath).Length)
Write-Host ("Master: {0}" -f $masterPath)
