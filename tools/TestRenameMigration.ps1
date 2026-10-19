# Offline tests of the real renamed-mod player migration and persistence hooks.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    'Common\Knowledge\KnowledgeSnapshot.cs',
    'Common\Knowledge\LocalKnowledgeAnswer.cs',
    'Common\Knowledge\CraftingPlanner.cs',
    'Common\Knowledge\WorldKnowledgeLedger.cs',
    'Common\Players\GptKnowledgePlayer.cs',
    'Common\Players\LegacyPlayerData.cs',
    'Common\Goals\GoalModels.cs',
    'Common\Goals\GoalPlanner.cs',
    'Common\Goals\WorldGoalLedger.cs',
    'Common\Players\GptProgressPlayer.cs',
    'tests\GoalProgressStubs.cs',
    'tests\UnloadedPlayerStub.cs',
    'tests\RenameMigrationTests.cs'
) | ForEach-Object { Join-Path $taskRoot $_ }
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.RenameMigrationTests]::Run()
$results | ForEach-Object { Write-Output $_ }
Write-Output "Passed $($results.Count) rename migration checks."
