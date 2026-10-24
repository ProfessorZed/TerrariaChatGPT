[CmdletBinding()]
param([string]$TmlPath = 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader')
$ErrorActionPreference = 'Stop'
# Terraria initializes its diagnostics before loading the disposable profile.
# Verify permission before starting either process so sandbox denial cannot
# produce a native fatal-error window over the user's game.
$diagnosticLog = Join-Path $TmlPath 'tModLoader-Logs\environment-server.log'
$diagnosticStream = $null
try {
    if (-not (Test-Path -LiteralPath $diagnosticLog -PathType Leaf)) {
        throw 'The existing diagnostic log is unavailable for a safe permission check.'
    }
    $diagnosticStream = [IO.File]::Open($diagnosticLog, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
}
catch {
    throw 'No tModLoader process started. The isolated classifier test requires permission to access tModLoader diagnostic logs; rerun with the required permission.'
}
finally { if ($null -ne $diagnosticStream) { $diagnosticStream.Dispose() } }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$probe = Join-Path $root '.build\installed-chest-sort-probe'
$source = Join-Path $probe 'ChestSortProbe'
$save = Join-Path $probe 'save'
$mods = Join-Path $save 'Mods'
$output = Join-Path $probe 'results.json'
New-Item -ItemType Directory -Path $source,$mods -Force | Out-Null
@'
displayName = Public Chest Sort Verification
author = TerrariaChatGPT tests
version = 1.0
side = Both
includeSource = false
'@ | Set-Content -LiteralPath (Join-Path $source 'build.txt') -Encoding utf8
foreach ($name in @('ChestSortModels.cs','ChestSortPlanner.cs','ChestSortItemClassifier.cs')) {
    Copy-Item -LiteralPath (Join-Path $root "Common\Sorting\$name") -Destination (Join-Path $source $name) -Force
}
Copy-Item -LiteralPath (Join-Path $root 'tests\InstalledChestSortProbe.cs') -Destination (Join-Path $source 'ChestSortProbe.cs') -Force
'["ChestSortProbe"]' | Set-Content -LiteralPath (Join-Path $mods 'enabled.json') -Encoding utf8
$runtime = Join-Path $TmlPath 'dotnet\dotnet.exe'
$assembly = Join-Path $TmlPath 'tModLoader.dll'
$started = [DateTime]::UtcNow
Push-Location -LiteralPath $probe
try {
    & $runtime exec --additionalprobingpath (Join-Path $TmlPath 'Libraries') $assembly -server -build $source -tmlsavedirectory $save -savedirectory $save -nosteam
    if ($LASTEXITCODE -ne 0) { throw 'Public chest-sort probe compilation failed.' }
    $env:TERRARIAGPT_CHEST_SORT_PROBE_OUTPUT = $output
    Set-Location -LiteralPath $TmlPath
    # Disposable headless profile, public content definitions only. The mod exits
    # in PostSetupRecipes before selecting, generating, loading or saving any world.
    & $runtime exec --additionalprobingpath (Join-Path $TmlPath 'Libraries') $assembly -server -tmlsavedirectory $save -savedirectory $save -modpath $mods -nosteam
    if (-not (Test-Path -LiteralPath $output) -or (Get-Item -LiteralPath $output).LastWriteTimeUtc -lt $started) { throw 'No fresh public chest-sort probe result.' }
    $result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    $result | Select-Object passed,classified,caseCount,failures | ConvertTo-Json -Depth 6
    if (-not $result.passed) { throw 'Public chest-sort classifier checks failed.' }
}
finally { Remove-Item Env:TERRARIAGPT_CHEST_SORT_PROBE_OUTPUT -ErrorAction SilentlyContinue; Pop-Location }
