# Compiles the production ledger and ModPlayer against a tiny public-content
# host. Does not touch real characters, worlds, credentials or the network.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    'Common\Memory\MemoryModels.cs',
    'Common\Sorting\SortSuggestion.cs',
    'Common\Memory\MemoryLedger.cs',
    'Common\Players\GptMemoryPlayer.cs',
    'Common\Services\ChatConvenience.cs',
    'tests\PersistentMemoryStubs.cs',
    'tests\PersistentMemoryTests.cs'
) | ForEach-Object { Join-Path $taskRoot $_ }
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.PersistentMemoryTests]::Run()
$results | ForEach-Object { Write-Output $_ }
Write-Output "Passed $($results.Count) persistent memory checks."
