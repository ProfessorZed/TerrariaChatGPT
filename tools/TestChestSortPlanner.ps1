# Run with PowerShell 7. These tests do not load Terraria, touch saves, or use a network.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    (Join-Path $taskRoot 'Common\Sorting\ChestSortModels.cs'),
    (Join-Path $taskRoot 'Common\Sorting\ChestSortPlanner.cs'),
    (Join-Path $taskRoot 'tests\ChestSortPlannerTests.cs')
)
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.ChestSortPlannerTests]::Run()
$results | ForEach-Object { Write-Output $_ }
Write-Output "Passed $($results.Count) named-chest sorting planner checks."
