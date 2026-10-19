# Run with PowerShell 7. No tModLoader installation, API key, or network is used.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    (Join-Path $taskRoot 'Common\Knowledge\KnowledgeSnapshot.cs'),
    (Join-Path $taskRoot 'Common\Knowledge\LocalKnowledgeAnswer.cs'),
    (Join-Path $taskRoot 'tests\LocalKnowledgeTests.cs')
)

Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.LocalKnowledgeTests]::Run()
$results | ForEach-Object { Write-Output $_ }
Write-Output "Passed $($results.Count) local knowledge checks."
