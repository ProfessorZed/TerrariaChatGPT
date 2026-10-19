param(
    [string]$TmlPath = 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'ValidatePublishAssets.ps1')
$buildRoot = Join-Path $projectRoot '.build'
$saveRoot = Join-Path $buildRoot 'save'
$distRoot = Join-Path $projectRoot 'dist'
$modName = 'TerrariaChatGPT'
$sourceRoot = Join-Path $buildRoot 'source\TerrariaChatGPT'
$tmlAssembly = Join-Path $TmlPath 'tModLoader.dll'
if (-not (Test-Path -LiteralPath $tmlAssembly)) { throw "tModLoader not found at $TmlPath. Pass -TmlPath with its folder." }
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$bundledDotnet = Join-Path $TmlPath 'dotnet\dotnet.exe'
$dotnetPath = if (Test-Path -LiteralPath $bundledDotnet) { $bundledDotnet } elseif ($dotnetCommand) { $dotnetCommand.Source } else { throw 'The tModLoader .NET runtime was not found.' }
New-Item -ItemType Directory -Path $buildRoot,$saveRoot,$distRoot -Force | Out-Null
# The source folder name determines tModLoader's internal mod identity. Stage a
# clean folder so ZIP downloads named TerrariaChatGPT-main build correctly too.
. (Join-Path $PSScriptRoot 'ReleaseFiles.ps1')
$sourceFiles = @(Get-ReleaseSourceFiles -Root $projectRoot)
if (Test-Path -LiteralPath $sourceRoot) {
    $resolvedSource = [IO.Path]::GetFullPath($sourceRoot)
    if ($resolvedSource -ne (Join-Path $projectRoot '.build\source\TerrariaChatGPT') -or
        (Get-Item -LiteralPath $sourceRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Refusing to clear an unexpected staging directory.'
    }
    Remove-Item -LiteralPath $resolvedSource -Recurse -Force
}
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($projectRoot.Length + 1)
    $target = Join-Path $sourceRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
Push-Location -LiteralPath $buildRoot
try {
    $buildStartedAt = [DateTime]::UtcNow
    # tModLoader includes its own compiler. No .NET SDK or NuGet restore is needed.
    & $dotnetPath exec --additionalprobingpath (Join-Path $TmlPath 'Libraries') $tmlAssembly -server -build $sourceRoot -tmlsavedirectory $saveRoot -savedirectory $saveRoot -modpath $distRoot -nosteam
    if ($LASTEXITCODE -ne 0) { throw "tModLoader compilation failed (exit $LASTEXITCODE)." }
    $builtMod = Join-Path $distRoot "$modName.tmod"
    # The bundled compiler writes to its save directory even with -modpath.
    # Always stage that fresh output; an existing dist file may be an older mod.
    $candidate = Get-ChildItem -LiteralPath $saveRoot -Filter "$modName.tmod" -Recurse |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($candidate -and $candidate.LastWriteTimeUtc -ge $buildStartedAt.AddSeconds(-2)) {
        Copy-Item -LiteralPath $candidate.FullName -Destination $builtMod -Force
    }
    elseif (-not (Test-Path -LiteralPath $builtMod) -or (Get-Item -LiteralPath $builtMod).LastWriteTimeUtc -lt $buildStartedAt.AddSeconds(-2)) {
        throw 'The compiler did not produce a fresh TerrariaChatGPT.tmod. The previous staged file was not replaced.'
    }
    . (Join-Path $PSScriptRoot 'ModPackageInfo.ps1')
    $packageInfo = Get-TerrariaGptPackageInfo -Path $builtMod
    $versionLine = Get-Content -LiteralPath (Join-Path $projectRoot 'build.txt') | Where-Object { $_ -match '^version\s*=\s*' } | Select-Object -First 1
    $expectedVersion = ($versionLine -split '=', 2)[1].Trim()
    if ($packageInfo.Name -ne $modName -or $packageInfo.Version -ne $expectedVersion) {
        throw 'The staged package does not match the version just built.'
    }
    Write-Host "Built and verified Terraria ChatGPT $($packageInfo.Version): $builtMod"
    Write-Host "Prepared Workshop source: $sourceRoot"
} finally { Pop-Location }
