[CmdletBinding()]
param([string]$TmlPath = 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$probeRoot = Join-Path $projectRoot '.build\installed-recipe-probe'
$sourceRoot = Join-Path $probeRoot 'RecipeProbe'
$saveRoot = Join-Path $probeRoot 'save'
$modsRoot = Join-Path $saveRoot 'Mods'
$outputFile = Join-Path $probeRoot 'results.json'
New-Item -ItemType Directory -Path $sourceRoot,$modsRoot -Force | Out-Null
@'
displayName = Public Recipe Verification
author = TerrariaGPT tests
version = 1.0
side = Both
includeSource = false
'@ | Set-Content -LiteralPath (Join-Path $sourceRoot 'build.txt') -Encoding utf8
foreach ($sourceFile in @('KnowledgeSnapshot.cs','AnswerHighlight.cs','AnswerFormatting.cs','LocalKnowledgeAnswer.cs','CraftingPlanner.cs','GameReferenceFacts.cs')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot ('Common\Knowledge\' + $sourceFile)) -Destination (Join-Path $sourceRoot $sourceFile) -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'tests\InstalledRecipeProbe.cs') -Destination (Join-Path $sourceRoot 'RecipeProbe.cs') -Force
'["RecipeProbe"]' | Set-Content -LiteralPath (Join-Path $modsRoot 'enabled.json') -Encoding utf8
$runtime = Join-Path $TmlPath 'dotnet\dotnet.exe'
$assembly = Join-Path $TmlPath 'tModLoader.dll'
$probeStart = [DateTime]::UtcNow
Push-Location -LiteralPath $probeRoot
try {
    & $runtime exec --additionalprobingpath (Join-Path $TmlPath 'Libraries') $assembly -server -build $sourceRoot -tmlsavedirectory $saveRoot -savedirectory $saveRoot -nosteam
    if ($LASTEXITCODE -ne 0) { throw 'Public recipe probe compilation failed.' }
    $env:TERRARIAGPT_RECIPE_PROBE_OUTPUT = $outputFile
    Set-Location -LiteralPath $TmlPath
    # This isolated server loads public definitions only, then the probe exits
    # in PostSetupRecipes. It never chooses, loads, creates, or saves a world.
    & $runtime exec --additionalprobingpath (Join-Path $TmlPath 'Libraries') $assembly -server -tmlsavedirectory $saveRoot -savedirectory $saveRoot -modpath $modsRoot -nosteam
    if (-not (Test-Path -LiteralPath $outputFile) -or (Get-Item -LiteralPath $outputFile).LastWriteTimeUtc -lt $probeStart) {
        throw 'The isolated public recipe probe did not produce a fresh result.'
    }
    $result = Get-Content -LiteralPath $outputFile -Raw | ConvertFrom-Json
    $result | ConvertTo-Json -Depth 8
    if (-not $result.passed) { throw 'Installed public recipe verification found failures.' }
} finally {
    Remove-Item Env:TERRARIAGPT_RECIPE_PROBE_OUTPUT -ErrorAction SilentlyContinue
    Pop-Location
}
