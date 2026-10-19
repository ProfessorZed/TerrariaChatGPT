# Run with PowerShell 7. No tModLoader installation, account, or network is used.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    (Join-Path $taskRoot 'Common\Knowledge\KnowledgeSnapshot.cs'),
    (Join-Path $taskRoot 'Common\Knowledge\LocalKnowledgeAnswer.cs'),
    (Join-Path $taskRoot 'Common\Knowledge\CraftingPlanner.cs'),
    (Join-Path $taskRoot 'tests\CraftingPlannerTests.cs')
)
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaGPT.Tests.CraftingPlannerTests]::Run()
$results | ForEach-Object { Write-Output $_ }
Write-Output "Passed $($results.Count) crafting planner checks."
