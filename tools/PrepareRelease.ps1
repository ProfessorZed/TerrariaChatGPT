[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'ReleaseFiles.ps1')
. (Join-Path $PSScriptRoot 'ModPackageInfo.ps1')
& (Join-Path $PSScriptRoot 'ValidatePublishAssets.ps1')
$packagePath = Join-Path $root 'dist\TerrariaChatGPT.tmod'
$package = Get-TerrariaGptPackageInfo -Path $packagePath
$version = ((Get-Content -LiteralPath (Join-Path $root 'build.txt') | Where-Object { $_ -match '^version\s*=' }) -split '=',2)[1].Trim()
if ($package.Name -ne 'TerrariaChatGPT' -or $package.Version -ne $version) { throw 'Build the current version before preparing a release.' }
$files = @(Get-ReleaseSourceFiles -Root $root)
$releaseRoot = Join-Path $root ('dist\release-' + $version + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$sourceRoot = Join-Path $releaseRoot 'TerrariaChatGPT'
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length + 1)
    $target = Join-Path $sourceRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
$zipPath = Join-Path $root "dist\TerrariaChatGPT-$version-source.zip"
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$zipStream = [IO.File]::Create($zipPath)
$zip = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($root.Length + 1)
        $entryName = 'TerrariaChatGPT/' + $relative.Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $sourceRoot $relative), $entryName) | Out-Null
    }
} finally { $zip.Dispose(); $zipStream.Dispose() }
$hashLines = @($packagePath,$zipPath) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $_) }
$hashPath = Join-Path $root 'dist\SHA256SUMS.txt'
[IO.File]::WriteAllLines($hashPath, $hashLines, [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Version=$version; SourceFiles=$files.Count; SourceDirectory=$sourceRoot; SourceZip=$zipPath; Mod=$packagePath; Checksums=$hashPath } | ConvertTo-Json
