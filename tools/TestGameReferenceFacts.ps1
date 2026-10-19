# Run with PowerShell 7. Uses actual extraction code and public-only fixtures;
# no tModLoader installation, API key, account, network, or world save is used.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    (Join-Path $taskRoot 'Common\Knowledge\KnowledgeSnapshot.cs'),
    (Join-Path $taskRoot 'Common\Knowledge\LocalKnowledgeAnswer.cs'),
    (Join-Path $taskRoot 'Common\Knowledge\CraftingPlanner.cs'),
    (Join-Path $taskRoot 'Common\Knowledge\GameReferenceFacts.cs'),
    (Join-Path $taskRoot 'tests\GameReferenceFactsStubs.cs'),
    (Join-Path $taskRoot 'tests\GameReferenceFactsTests.cs')
)

Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaGPT.Tests.GameReferenceFactsTests]::Run()
$results | ForEach-Object { Write-Output $_ }
Write-Output "Passed $($results.Count) public reference extraction checks."
