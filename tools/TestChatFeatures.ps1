[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    'Common\Knowledge\KnowledgeSnapshot.cs',
    'Common\Goals\GoalModels.cs',
    'Common\Services\ObservedContextBuilder.cs',
    'Common\Services\GoalProposalReader.cs',
    'Common\Services\WebCitation.cs',
    'tests\ChatFeatureBoundaryTests.cs'
) | ForEach-Object { Join-Path $taskRoot $_ }
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.ChatFeatureBoundaryTests]::Run()
$results | ForEach-Object { Write-Output $_ }
Write-Output "Passed $($results.Count) chat feature boundary checks."
