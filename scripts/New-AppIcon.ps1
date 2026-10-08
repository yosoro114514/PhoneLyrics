param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/PhoneLyrics/Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

# Original artwork, drawn from simple shapes. No downloaded or third-party icons.
$frames = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
foreach ($size in $sizes) {
    $canvas = [Drawing.Bitmap]::new(512, 512)
    $graphics = [Drawing.Graphics]::FromImage($canvas)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(4, 4)
    $back = [Drawing.Drawing2D.GraphicsPath]::new()
    $back.AddArc(4, 4, 36, 36, 180, 90)
    $back.AddArc(88, 4, 36, 36, 270, 90)
    $back.AddArc(88, 88, 36, 36, 0, 90)
    $back.AddArc(4, 88, 36, 36, 90, 90)
    $back.CloseFigure()
    $navy = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(4,4,120,120),
        [Drawing.ColorTranslator]::FromHtml('#293649'), [Drawing.ColorTranslator]::FromHtml('#101923'), 90.0)
    $gold = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(20,20,90,90),
        [Drawing.ColorTranslator]::FromHtml('#FFF1BC'), [Drawing.ColorTranslator]::FromHtml('#EAA73C'), 90.0)
    $graphics.FillPath($navy, $back)
    $note = [Drawing.Drawing2D.GraphicsPath]::new()
    $note.AddPolygon([Drawing.PointF[]]@([Drawing.PointF]::new(49,33),[Drawing.PointF]::new(70,25),
        [Drawing.PointF]::new(70,42),[Drawing.PointF]::new(57,47),[Drawing.PointF]::new(57,87),[Drawing.PointF]::new(49,87)))
    $graphics.FillPath($gold, $note)
    $graphics.FillEllipse($gold, 27, 79, 30, 22)
    $pen = [Drawing.Pen]::new($gold, 7)
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($pen, 80, 55, 105, 55)
    $graphics.DrawLine($pen, 80, 74, 105, 74)
    $graphics.DrawLine($pen, 80, 93, 98, 93)
    $scaled = [Drawing.Bitmap]::new($size, $size)
    $resize = [Drawing.Graphics]::FromImage($scaled)
    $resize.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
    $resize.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $resize.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $resize.DrawImage($canvas, [Drawing.Rectangle]::new(0,0,$size,$size), 0,0,512,512, [Drawing.GraphicsUnit]::Pixel)
    $memory = [IO.MemoryStream]::new()
    $scaled.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($memory.ToArray())
    if ($size -eq 256) { $scaled.Save((Join-Path $OutputDirectory 'PhoneLyrics.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $resize.Dispose(); $scaled.Dispose(); $pen.Dispose(); $note.Dispose()
    $gold.Dispose(); $navy.Dispose(); $back.Dispose(); $graphics.Dispose(); $canvas.Dispose()
}
$stream = [IO.File]::Create((Join-Path $OutputDirectory 'PhoneLyrics.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Length)
    $offset = 6 + 16 * $sizes.Length
    for ($index = 0; $index -lt $sizes.Length; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose() }
Get-Item (Join-Path $OutputDirectory 'PhoneLyrics.ico') | Select-Object FullName, Length
