# Generates RectangleWinPlus/icon.ico from code, so the repo carries no opaque binary blob
# that nobody can regenerate.
#
# Frames 16-64px are written as uncompressed DIBs, which every Windows API and System.Drawing
# can decode. The 256px frame is PNG-compressed, which is conventional and keeps the file small.
# (Writing PNG frames at every size produces an .ico the shell renders but System.Drawing refuses.)
#
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 -OutFile check.ico

param([string]$OutFile)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not $OutFile) { $OutFile = Join-Path $PSScriptRoot '..\RectangleWinPlus\icon.ico' }
$DibSizes = @(16, 24, 32, 48, 64)
$PngSizes = @(256)
$Sizes    = $DibSizes + $PngSizes

# Brand gradient: a lighter azure at the top-left falling to a deep indigo at the bottom-right,
# the palette modern Windows 11 / Fluent tiles favour.
$TopColor    = [System.Drawing.Color]::FromArgb(0x3B, 0x82, 0xF6)  # azure
$BottomColor = [System.Drawing.Color]::FromArgb(0x1D, 0x4E, 0xD8)  # indigo

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

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # A squircle-ish rounded tile, inset slightly so the corners breathe.
    $inset  = [float]($s * 0.03)
    $side   = [float]($s - 2 * $inset)
    $radius = [float]($s * 0.235)
    $tile = New-RoundedPath $inset $inset $side $side $radius

    # Diagonal gradient fill.
    $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, $TopColor, $BottomColor, 55.0)
    $grad.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
    $g.FillPath($grad, $tile)

    # A soft top sheen: a translucent white highlight arcing across the upper portion, which reads
    # as a glassy tile rather than a flat swatch. Clipped to the tile so it keeps the rounded edge.
    $state = $g.Save()
    $g.SetClip($tile)
    $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF(0, 0, $s, $s * 0.6)),
        [System.Drawing.Color]::FromArgb(56, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
        90.0)
    $g.FillRectangle($sheen, 0, 0, $s, [float]($s * 0.6))
    $sheen.Dispose()
    $g.Restore($state)

    # A crisp 1px inner rim lifts the tile off dark backgrounds.
    $rim = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 255, 255, 255)), ([Math]::Max(1.0, $s * 0.012))
    $g.DrawPath($rim, $tile)

    # --- glyph: a window, quartered, with the top-left pane filled ---
    $m = [float]($s * 0.26)              # margin from tile edge to glyph
    $w = [float]($s - 2 * $m)
    $h = [float]($s * 0.48)
    $t = [float](($s - $h) / 2)
    $stroke = [Math]::Max(1.0, $s * 0.052)
    $paneRadius = [float]($s * 0.045)

    # A subtle drop shadow beneath the glyph for depth (skipped at tiny sizes where it muddies).
    if ($s -ge 32) {
        $shadow = New-RoundedPath $m ($t + $s * 0.012) $w $h $paneRadius
        $shPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(45, 0, 0, 40)), $stroke
        $shPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($shPen, $shadow)
        $shPen.Dispose(); $shadow.Dispose()
    }

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $faint = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 255, 255))

    # Filled top-left pane.
    $paneW = [float](($w - $stroke) / 2)
    $paneH = [float](($h - $stroke) / 2)
    $pane = New-RoundedPath $m $t $paneW $paneH ($paneRadius * 0.8)
    $g.FillPath($faint, $pane)
    $pane.Dispose()

    # Window outline plus the two cross bars.
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    $outline = New-RoundedPath $m $t $w $h $paneRadius
    $g.DrawPath($pen, $outline)
    $g.DrawLine($pen, $m, ($t + $h / 2), ($m + $w), ($t + $h / 2))
    $g.DrawLine($pen, ($m + $w / 2), $t, ($m + $w / 2), ($t + $h))

    $outline.Dispose(); $pen.Dispose(); $white.Dispose(); $faint.Dispose()
    $rim.Dispose(); $grad.Dispose(); $tile.Dispose(); $g.Dispose()
    return $bmp
}

function ConvertTo-Png([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# BITMAPINFOHEADER + bottom-up BGRA pixels + a padded (all-zero) AND mask.
function ConvertTo-Dib([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)

    $w.Write([uint32]40)      # biSize
    $w.Write([int32]$s)       # biWidth
    $w.Write([int32](2 * $s)) # biHeight: XOR image plus AND mask
    $w.Write([uint16]1)       # biPlanes
    $w.Write([uint16]32)      # biBitCount
    $w.Write([uint32]0)       # biCompression: BI_RGB
    $w.Write([uint32]($s * $s * 4))
    $w.Write([int32]0); $w.Write([int32]0)
    $w.Write([uint32]0); $w.Write([uint32]0)

    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }

    # 1bpp AND mask, rows padded to 4 bytes. Alpha already carries transparency, so leave it clear.
    $maskRow = [int]((($s + 31) / 32)) * 4
    $zeros = New-Object byte[] ($maskRow * $s)
    $w.Write($zeros)

    $w.Flush()
    $bytes = $ms.ToArray()
    $w.Dispose(); $ms.Dispose()
    return , $bytes
}

$frames = @{}
foreach ($s in $Sizes) {
    $bmp = New-Frame $s
    $frames[$s] = if ($PngSizes -contains $s) { ConvertTo-Png $bmp } else { ConvertTo-Dib $bmp }
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)

# ICONDIR
$w.Write([uint16]0)                # reserved
$w.Write([uint16]1)                # type: icon
$w.Write([uint16]$Sizes.Count)

# ICONDIRENTRY table; the image data follows it.
$offset = 6 + (16 * $Sizes.Count)
foreach ($s in $Sizes) {
    $data = $frames[$s]
    $w.Write([byte]($s % 256))     # 256 is encoded as 0
    $w.Write([byte]($s % 256))
    $w.Write([byte]0)              # palette entries
    $w.Write([byte]0)              # reserved
    $w.Write([uint16]1)            # colour planes
    $w.Write([uint16]32)           # bits per pixel
    $w.Write([uint32]$data.Length)
    $w.Write([uint32]$offset)
    $offset += $data.Length
}

foreach ($s in $Sizes) { $w.Write($frames[$s]) }
$w.Flush()

$bytes = $out.ToArray()
$w.Dispose(); $out.Dispose()

$full = [System.IO.Path]::GetFullPath($OutFile)
[System.IO.File]::WriteAllBytes($full, $bytes)
Write-Host "wrote $full ($($bytes.Length) bytes, $($Sizes.Count) frames)"
