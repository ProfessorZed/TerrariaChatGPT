# Re-export the original release artwork at tModLoader's publishing sizes.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$masterPath = Join-Path $taskRoot 'assets/branding/terraria-chatgpt-master.png'
if (-not (Test-Path -LiteralPath $masterPath -PathType Leaf)) {
    throw "The original icon artwork is missing: $masterPath"
}
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile($masterPath)
try {
    if ($source.Width -ne $source.Height) { throw 'The icon artwork must be square.' }
    foreach ($export in @(
        @{ File = 'icon.png'; Size = 80 },
        @{ File = 'icon_workshop.png'; Size = 512 }
    )) {
        $bitmap = [Drawing.Bitmap]::new($export.Size, $export.Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $canvas = [Drawing.Graphics]::FromImage($bitmap)
        try {
            # Keep deliberate pixel edges crisp; do not add smoothing or new art.
            $canvas.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $canvas.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
            $canvas.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $canvas.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $export.Size, $export.Size),
                0, 0, $source.Width, $source.Height, [Drawing.GraphicsUnit]::Pixel)
            $bitmap.Save((Join-Path $taskRoot $export.File), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $canvas.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }
Write-Output 'Exported icon.png (80x80) and icon_workshop.png (512x512).'
