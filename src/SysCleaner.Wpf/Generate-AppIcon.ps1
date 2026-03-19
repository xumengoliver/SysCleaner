param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'app.ico')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$sizes = 16, 24, 32, 48, 64, 128, 256

function New-Brushes {
    $teal = [Windows.Media.LinearGradientBrush]::new()
    $teal.MappingMode = [Windows.Media.BrushMappingMode]::Absolute
    $teal.StartPoint = [Windows.Point]::new(0, 0)
    $teal.EndPoint = [Windows.Point]::new(512, 512)
    $teal.GradientStops.Add([Windows.Media.GradientStop]::new(([Windows.Media.ColorConverter]::ConvertFromString('#0D6B63')), 0.0))
    $teal.GradientStops.Add([Windows.Media.GradientStop]::new(([Windows.Media.ColorConverter]::ConvertFromString('#14B8A6')), 1.0))
    $teal.Freeze()

    $white = [Windows.Media.SolidColorBrush]::new([Windows.Media.Colors]::White)
    $white.Freeze()

    $sparkleOne = [Windows.Media.SolidColorBrush]::new([Windows.Media.Colors]::White)
    $sparkleOne.Opacity = 0.7
    $sparkleOne.Freeze()

    $sparkleTwo = [Windows.Media.SolidColorBrush]::new([Windows.Media.Colors]::White)
    $sparkleTwo.Opacity = 0.4
    $sparkleTwo.Freeze()

    return @{
        Teal = $teal
        White = $white
        SparkleOne = $sparkleOne
        SparkleTwo = $sparkleTwo
    }
}

function Get-GeometrySet {
    return @{
        Shield = [Windows.Media.Geometry]::Parse('M256 100 C320 100 370 130 378 185 L378 296 C378 358 325 398 256 425 C187 398 134 358 134 296 L134 185 C142 130 192 100 256 100Z')
        Star = [Windows.Media.Geometry]::Parse('M256 196 L267 242 L314 258 L267 274 L256 320 L245 274 L198 258 L245 242Z')
        SparkleOne = [Windows.Media.Geometry]::Parse('M398 108 L401 96 L404 108 L416 111 L404 114 L401 126 L398 114 L386 111Z')
        SparkleTwo = [Windows.Media.Geometry]::Parse('M116 390 L118 383 L120 390 L127 392 L120 394 L118 401 L116 394 L109 392Z')
    }
}

function New-PngBytes {
    param([int]$Size)

    $brushes = New-Brushes
    $geometries = Get-GeometrySet
    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $scale = $Size / 512.0

    $context.PushTransform([Windows.Media.ScaleTransform]::new($scale, $scale))
    $context.DrawRoundedRectangle($brushes.Teal, $null, [Windows.Rect]::new(0, 0, 512, 512), 112, 112)
    $context.DrawGeometry($brushes.White, $null, $geometries.Shield)
    $context.DrawGeometry($brushes.Teal, $null, $geometries.Star)
    $context.DrawGeometry($brushes.SparkleOne, $null, $geometries.SparkleOne)
    $context.DrawGeometry($brushes.SparkleTwo, $null, $geometries.SparkleTwo)

    $context.Pop()
    $context.Close()

    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))

    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return ,$bytes
}

$images = foreach ($size in $sizes) {
    [pscustomobject]@{
        Size = $size
        Bytes = New-PngBytes -Size $size
    }
}

$directory = Split-Path -Parent $OutputPath
if ($directory -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory | Out-Null
}

$stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
try {
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dirSize = if ($image.Size -ge 256) { 0 } else { [byte]$image.Size }
        $writer.Write([byte]$dirSize)
        $writer.Write([byte]$dirSize)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write($image.Bytes)
    }

    $writer.Flush()
}
finally {
    $stream.Dispose()
}

$raw = [System.IO.File]::ReadAllBytes($OutputPath)
$count = [BitConverter]::ToUInt16($raw, 4)
$frames = for ($index = 0; $index -lt $count; $index++) {
    $entry = 6 + (16 * $index)
    $offset = [BitConverter]::ToUInt32($raw, $entry + 12)
    $realWidth = [BitConverter]::ToUInt32([byte[]]($raw[$offset + 19], $raw[$offset + 18], $raw[$offset + 17], $raw[$offset + 16]), 0)
    $realHeight = [BitConverter]::ToUInt32([byte[]]($raw[$offset + 23], $raw[$offset + 22], $raw[$offset + 21], $raw[$offset + 20]), 0)
    [pscustomobject]@{
        ActualWidth = $realWidth
        ActualHeight = $realHeight
        Bytes = [BitConverter]::ToUInt32($raw, $entry + 8)
    }
}

$frames | Format-Table -AutoSize