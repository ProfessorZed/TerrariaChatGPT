# Run with PowerShell 7. Uses a fake in-memory world, never a real save or game process.
[CmdletBinding()]
param([string]$LiveReplyPath = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    (Join-Path $taskRoot 'Common\Sorting\ChestSortModels.cs'),
    (Join-Path $taskRoot 'Common\Sorting\SortSuggestion.cs'),
    (Join-Path $taskRoot 'Common\Memory\MemoryModels.cs'),
    (Join-Path $taskRoot 'Common\Sorting\ChestSortPlanner.cs'),
    (Join-Path $taskRoot 'Common\Sorting\ChestSortService.cs'),
    (Join-Path $taskRoot 'tests\ChestSortRuntimeStubs.cs'),
    (Join-Path $taskRoot 'tests\ChestSortRuntimeTests.cs')
)
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.ChestSortRuntimeTests]::Run()
if ($LiveReplyPath) {
    $capturedReply = Get-Content -LiteralPath $LiveReplyPath -Raw | ConvertFrom-Json
    if (-not $capturedReply.SortSuggestion.Assignments) { throw 'Captured reply has no sorting assignments.' }
    $capturedAssignments = [System.Collections.Generic.List[TerrariaChatGPT.Common.Sorting.SortAssignment]]::new()
    foreach ($assignment in $capturedReply.SortSuggestion.Assignments) {
        $capturedAssignments.Add([TerrariaChatGPT.Common.Sorting.SortAssignment]::new($assignment.ItemKey, $assignment.ChestName))
    }
    $capturedSuggestion = [TerrariaChatGPT.Common.Sorting.SortSuggestion]::new($capturedAssignments)
    $results += [TerrariaChatGPT.Tests.ChestSortRuntimeTests]::ReplayCaptured($capturedSuggestion)
}
$results | ForEach-Object { Write-Output $_ }
Write-Output "Passed $($results.Count) named-chest sorting runtime checks."
