param(
    [string] $Source = 'apps/windows/ShowWhere.Desktop/Assets/Assistant/monkey-sit.png',
    [string] $Output = 'apps/windows/ShowWhere.Desktop/Assets/ShowWhere.ico'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sourcePath = [IO.Path]::GetFullPath((Join-Path $root $Source))
$outputPath = [IO.Path]::GetFullPath((Join-Path $root $Output))
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Icon source does not exist: $sourcePath" }
if (-not $outputPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Icon output must stay inside the repository.' }

Add-Type -AssemblyName System.Drawing
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$sourceImage = [Drawing.Image]::FromFile($sourcePath)
$frames = New-Object System.Collections.Generic.List[byte[]]
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
                $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
            } finally { $graphics.Dispose() }
            $memory = New-Object IO.MemoryStream
            try {
                $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
                $frames.Add($memory.ToArray())
            } finally { $memory.Dispose() }
        } finally { $bitmap.Dispose() }
    }
} finally { $sourceImage.Dispose() }

[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
$stream = [IO.File]::Create($outputPath)
$writer = New-Object IO.BinaryWriter $stream
try {
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$frames.Count)
    $offset = 6 + (16 * $frames.Count)
    for ($index = 0; $index -lt $frames.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([UInt16]1); $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frames[$index].Length); $writer.Write([UInt32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose(); $stream.Dispose() }

Write-Host "Generated Windows icon: $outputPath"
