$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object Drawing.Bitmap(32, 32)
$g = [Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([Drawing.Color]::Transparent)
$brush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(33, 99, 190))
$g.FillRectangle($brush, 3, 2, 22, 28)
$white = New-Object Drawing.SolidBrush([Drawing.Color]::White)
$g.FillRectangle($white, 8, 8, 13, 3)
$g.FillRectangle($white, 8, 14, 10, 3)
$green = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(31, 178, 138))
$g.FillEllipse($green, 17, 18, 14, 14)
$pen = New-Object Drawing.Pen([Drawing.Color]::White, 2)
$g.DrawLine($pen, 21, 25, 27, 25)
$g.DrawLine($pen, 24, 22, 24, 28)
# Encode a PNG-backed Windows icon: no unmanaged icon handles.
$memory = New-Object IO.MemoryStream
$bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
$data = $memory.ToArray()
$destination = Join-Path $PSScriptRoot '..\Resources\FileTools.ico'
$writer = New-Object IO.BinaryWriter([IO.File]::Create($destination))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
    $writer.Write([byte]32); $writer.Write([byte]32); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$data.Length)
    $writer.Write([uint32]22); $writer.Write($data)
} finally { $writer.Dispose(); $memory.Dispose(); $pen.Dispose(); $g.Dispose(); $brush.Dispose(); $white.Dispose(); $green.Dispose(); $bitmap.Dispose() }
