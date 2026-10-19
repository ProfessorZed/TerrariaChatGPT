[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
foreach ($asset in @(@{Name='icon.png';Size=80}, @{Name='icon_workshop.png';Size=512})) {
    $path = Join-Path $taskRoot $asset.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing $($asset.Name). Restore the publishing icons before building." }
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 33 -or $bytes.Length -ge 1MB -or
        [BitConverter]::ToString($bytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A' -or
        [Text.Encoding]::ASCII.GetString($bytes, 12, 4) -ne 'IHDR') { throw "$($asset.Name) must be a valid PNG smaller than 1 MB." }
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    if ($width -ne $asset.Size -or $height -ne $asset.Size) { throw "$($asset.Name) must be $($asset.Size)x$($asset.Size)." }
}
