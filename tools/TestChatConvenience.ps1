$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @('Common\Services\ChatConvenience.cs', 'Common\Knowledge\KnowledgeSnapshot.cs',
    'Common\Knowledge\LocalKnowledgeAnswer.cs', 'Common\Knowledge\CraftingPlanner.cs', 'tests\ChatConvenienceTests.cs')
Add-Type -Path ($sourcePaths | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable'
$results = [TerrariaChatGPT.Tests.ChatConvenienceTests]::Run()
$results
Write-Output "Passed $($results.Count) chat convenience checks."
