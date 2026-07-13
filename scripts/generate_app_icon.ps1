$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$AssetRoot = Join-Path $ProjectRoot "src\Satl.Gui\Assets"

function New-RoundedRectanglePath(
    [single] $X,
    [single] $Y,
    [single] $Width,
    [single] $Height,
    [single] $Radius
) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-AppIconBitmap([int] $Size) {
    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $scale = [single]($Size / 256.0)
        $graphics.ScaleTransform($scale, $scale)

        $background = New-RoundedRectanglePath 8 8 240 240 48
        $backgroundBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new(24, 20),
            [System.Drawing.PointF]::new(232, 236),
            [System.Drawing.Color]::FromArgb(255, 13, 41, 62),
            [System.Drawing.Color]::FromArgb(255, 18, 74, 90))
        $graphics.FillPath($backgroundBrush, $background)

        # The ribbon and medal represent a Steam achievement.
        $gold = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 185, 0))
        $graphics.FillPolygon($gold, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(76, 142),
            [System.Drawing.PointF]::new(116, 151),
            [System.Drawing.PointF]::new(109, 224),
            [System.Drawing.PointF]::new(88, 204),
            [System.Drawing.PointF]::new(61, 218)))
        $graphics.FillPolygon($gold, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(124, 151),
            [System.Drawing.PointF]::new(164, 142),
            [System.Drawing.PointF]::new(183, 218),
            [System.Drawing.PointF]::new(156, 204),
            [System.Drawing.PointF]::new(135, 224)))

        $graphics.FillEllipse($gold, 42, 26, 164, 164)
        $medal = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 0, 120, 212))
        $graphics.FillEllipse($medal, 55, 39, 138, 138)
        $medalHighlight = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(150, 110, 210, 255), 7)
        $graphics.DrawArc($medalHighlight, 67, 51, 114, 114, 205, 190)

        $check = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 17)
        $check.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $check.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $check.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawLines($check, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(84, 108),
            [System.Drawing.PointF]::new(110, 134),
            [System.Drawing.PointF]::new(162, 79)))

        # A speech bubble with opposing arrows represents translation/localization.
        $bubble = New-RoundedRectanglePath 130 124 106 76 19
        $teal = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 15, 157, 136))
        $graphics.FillPath($teal, $bubble)
        $graphics.FillPolygon($teal, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(184, 194),
            [System.Drawing.PointF]::new(202, 224),
            [System.Drawing.PointF]::new(211, 191)))
        $bubbleBorder = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(220, 255, 255, 255), 5)
        $graphics.DrawPath($bubbleBorder, $bubble)

        $arrows = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 6)
        $arrows.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $arrows.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($arrows, 151, 148, 213, 148)
        $graphics.DrawLine($arrows, 213, 148, 202, 139)
        $graphics.DrawLine($arrows, 213, 148, 202, 157)
        $graphics.DrawLine($arrows, 215, 176, 153, 176)
        $graphics.DrawLine($arrows, 153, 176, 164, 167)
        $graphics.DrawLine($arrows, 153, 176, 164, 185)

        $background.Dispose()
        $backgroundBrush.Dispose()
        $gold.Dispose()
        $medal.Dispose()
        $medalHighlight.Dispose()
        $check.Dispose()
        $bubble.Dispose()
        $teal.Dispose()
        $bubbleBorder.Dispose()
        $arrows.Dispose()
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

function Save-AppIconPng([string] $Path, [int] $Size) {
    $bitmap = New-AppIconBitmap $Size
    try {
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Save-AppIconIco([string] $Path) {
    $images = @()
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
        $bitmap = New-AppIconBitmap $size
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images += [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
        }
        finally {
            $stream.Dispose()
            $bitmap.Dispose()
        }
    }

    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$images.Count)
        $offset = 6 + (16 * $images.Count)
        foreach ($image in $images) {
            $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $image.Bytes.Length
        }
        foreach ($image in $images) {
            $writer.Write([byte[]]$image.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}

New-Item -ItemType Directory -Path $AssetRoot -Force | Out-Null
Save-AppIconPng (Join-Path $AssetRoot "AppIcon.preview.png") 512
Save-AppIconIco (Join-Path $AssetRoot "AppIcon.ico")

Write-Host "Generated SATL application icon and preview in $AssetRoot"
