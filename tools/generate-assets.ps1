# Generates YAHAA branded icon assets (an HA-blue rounded tile with a white house)
# Run with Windows PowerShell:  powershell.exe -ExecutionPolicy Bypass -File tools\generate-assets.ps1
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\YAHAA\Assets"
$assets = [System.IO.Path]::GetFullPath($assets)
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$blue  = [System.Drawing.Color]::FromArgb(255, 3, 169, 244)
$white = [System.Drawing.Color]::White

function New-RoundedRectPath($x, $y, $w, $h, $r) {
    $d = $r * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
    $p.CloseFigure()
    return $p
}

function PtF($x0, $y0, $side, $nx, $ny) {
    New-Object System.Drawing.PointF([single]($x0 + $nx * $side), [single]($y0 + $ny * $side))
}

# mode: 'tile'  -> blue rounded square + white house
#       'glyph' -> transparent background + blue house
function New-LogoBitmap([int]$size, [double]$squareFraction, [string]$mode) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $side = $size * $squareFraction
    $x0 = ($size - $side) / 2
    $y0 = ($size - $side) / 2

    if ($mode -eq 'tile') {
        $radius = $side * 0.18
        $rr = New-RoundedRectPath $x0 $y0 $side $side $radius
        $bb = New-Object System.Drawing.SolidBrush($blue)
        $g.FillPath($bb, $rr)
        $bb.Dispose(); $rr.Dispose()
        $houseColor = $white
    }
    else {
        $houseColor = $blue
    }

    # House silhouette (roof + body) with a door punched out via Alternate fill.
    $hp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hp.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
    $pts = @(
        (PtF $x0 $y0 $side 0.50 0.20),
        (PtF $x0 $y0 $side 0.82 0.50),
        (PtF $x0 $y0 $side 0.70 0.50),
        (PtF $x0 $y0 $side 0.70 0.80),
        (PtF $x0 $y0 $side 0.30 0.80),
        (PtF $x0 $y0 $side 0.30 0.50),
        (PtF $x0 $y0 $side 0.18 0.50)
    )
    $hp.AddPolygon([System.Drawing.PointF[]]$pts)

    $dx = $x0 + 0.44 * $side; $dy = $y0 + 0.56 * $side
    $dw = 0.12 * $side; $dh = 0.24 * $side
    $door = New-Object System.Drawing.RectangleF([single]$dx, [single]$dy, [single]$dw, [single]$dh)
    $hp.AddRectangle($door)

    $hb = New-Object System.Drawing.SolidBrush($houseColor)
    $g.FillPath($hb, $hp)
    $hb.Dispose(); $hp.Dispose()
    $g.Dispose()
    return $bmp
}

function New-Canvas([int]$w, [int]$h, $logo) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $lx = [int](($w - $logo.Width) / 2)
    $ly = [int](($h - $logo.Height) / 2)
    $g.DrawImage($logo, $lx, $ly)
    $g.Dispose()
    return $bmp
}

# Draws the white house silhouette (with door cut-out) into the given square region.
function Add-House($g, $x0, $y0, $side, $color) {
    $hp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hp.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
    $pts = @(
        (PtF $x0 $y0 $side 0.50 0.20),
        (PtF $x0 $y0 $side 0.82 0.50),
        (PtF $x0 $y0 $side 0.70 0.50),
        (PtF $x0 $y0 $side 0.70 0.80),
        (PtF $x0 $y0 $side 0.30 0.80),
        (PtF $x0 $y0 $side 0.30 0.50),
        (PtF $x0 $y0 $side 0.18 0.50)
    )
    $hp.AddPolygon([System.Drawing.PointF[]]$pts)
    $dx = $x0 + 0.44 * $side; $dy = $y0 + 0.56 * $side
    $dw = 0.12 * $side; $dh = 0.24 * $side
    $hp.AddRectangle((New-Object System.Drawing.RectangleF([single]$dx, [single]$dy, [single]$dw, [single]$dh)))
    $hb = New-Object System.Drawing.SolidBrush($color)
    $g.FillPath($hb, $hp)
    $hb.Dispose(); $hp.Dispose()
}

function New-LogoCanvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $bb = New-Object System.Drawing.SolidBrush($blue)
    $g.FillRectangle($bb, 0, 0, $size, $size)   # full-bleed blue; corners are rounded by the UI
    $bb.Dispose()
    return @{ bmp = $bmp; g = $g }
}

# "HA" logo: full blue square + white house.
function New-HaLogo([int]$size) {
    $c = New-LogoCanvas $size
    Add-House $c.g 0 0 $size $white
    $c.g.Dispose()
    return $c.bmp
}

# Placeholder "YAHAA" logo: house wearing a cowboy hat. Replace Assets\Logo-YAHAA.png with the real art.
function New-YahaaLogo([int]$size) {
    $c = New-LogoCanvas $size
    $g = $c.g

    $side2 = $size * 0.82
    $hx0 = ($size - $side2) / 2
    $hy0 = $size * 0.22
    Add-House $g $hx0 $hy0 $side2 $white

    $cx = $size * 0.5
    $brimY = $size * 0.345
    $brimRx = $size * 0.27
    $brimRy = $size * 0.055
    $wb = New-Object System.Drawing.SolidBrush($white)
    $g.FillEllipse($wb, [single]($cx - $brimRx), [single]($brimY - $brimRy), [single]($brimRx * 2), [single]($brimRy * 2))

    $crW = $size * 0.21; $crH = $size * 0.20
    $crX = $cx - $crW / 2; $crY = $brimY - $crH + $size * 0.02
    $cr = New-RoundedRectPath $crX $crY $crW $crH ($size * 0.045)
    $g.FillPath($wb, $cr); $cr.Dispose()
    $wb.Dispose()

    $band = New-Object System.Drawing.SolidBrush($blue)
    $g.FillRectangle($band, [single]$crX, [single]($brimY - $size * 0.05), [single]$crW, [single]($size * 0.03))
    $band.Dispose()

    $g.Dispose()
    return $c.bmp
}

# Scales an existing image down to a square of the given size (for building .ico files from art).
function Resize-Bitmap($src, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
    $g.Dispose()
    return $bmp
}

function Save-Png($bmp, $name) {
    $path = Join-Path $assets $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "  $name  ($($bmp.Width)x$($bmp.Height))"
}

function Save-Ico($bitmaps, $name) {
    $pngs = @()
    foreach ($b in $bitmaps) {
        $s = New-Object System.IO.MemoryStream
        $b.Save($s, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += , ($s.ToArray())
        $s.Dispose()
    }
    $count = $pngs.Count
    $fs = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$count)
    $offset = 6 + 16 * $count
    for ($i = 0; $i -lt $count; $i++) {
        $sz = $bitmaps[$i].Width
        $dim = if ($sz -ge 256) { 0 } else { $sz }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim)
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path $assets $name), $fs.ToArray())
    $bw.Dispose(); $fs.Dispose()
    Write-Host "  $name  (multi-size ico)"
}

Write-Host "Generating assets into $assets"

# MSIX tile / logo assets (replace the template placeholders)
Save-Png (New-LogoBitmap 300 0.86 'tile')  "Square150x150Logo.scale-200.png"
Save-Png (New-LogoBitmap 88  0.92 'tile')  "Square44x44Logo.scale-200.png"
Save-Png (New-LogoBitmap 24  0.96 'glyph') "Square44x44Logo.targetsize-24_altform-unplated.png"
Save-Png (New-LogoBitmap 50  0.92 'tile')  "StoreLogo.png"
Save-Png (New-LogoBitmap 48  0.92 'tile')  "LockScreenLogo.scale-200.png"

$wideLogo = New-LogoBitmap 220 0.90 'tile'
Save-Png (New-Canvas 620 300 $wideLogo) "Wide310x150Logo.scale-200.png"
$wideLogo.Dispose()

$splashLogo = New-LogoBitmap 360 0.90 'tile'
Save-Png (New-Canvas 1240 600 $splashLogo) "SplashScreen.scale-200.png"
$splashLogo.Dispose()

# In-app selectable logos
Save-Png (New-HaLogo 256) "Logo-HA.png"

# Logo-YAHAA.png is the user's own art once supplied; only generate the placeholder if it's missing.
if (Test-Path (Join-Path $assets "Logo-YAHAA.png")) {
    Write-Host "  Logo-YAHAA.png  (kept existing)"
}
else {
    Save-Png (New-YahaaLogo 256) "Logo-YAHAA.png"
}

# Application / window / tray icon (.ico, multiple sizes) for the "HA" logo
$icoSizes = 16, 24, 32, 48, 64, 128, 256
$icoBitmaps = @()
foreach ($s in $icoSizes) { $icoBitmaps += (New-LogoBitmap $s 0.94 'tile') }
Save-Ico $icoBitmaps "AppIcon.ico"
foreach ($b in $icoBitmaps) { $b.Dispose() }

# Window / tray icon (.ico) for the "YAHAA" logo, scaled from the YAHAA art
$yahaaPath = Join-Path $assets "Logo-YAHAA.png"
$yahaaSrc = New-Object System.Drawing.Bitmap($yahaaPath)
$yahaaIco = @()
foreach ($s in $icoSizes) { $yahaaIco += (Resize-Bitmap $yahaaSrc $s) }
Save-Ico $yahaaIco "AppIcon-YAHAA.ico"
foreach ($b in $yahaaIco) { $b.Dispose() }
$yahaaSrc.Dispose()

Write-Host "Done."
