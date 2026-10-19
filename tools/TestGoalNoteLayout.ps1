# No game, graphics device, account, or network is needed.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $taskRoot 'Common\UI\ChatPanelGeometry.cs'),
    (Join-Path $taskRoot 'Common\UI\GoalNoteGeometry.cs'),
    (Join-Path $taskRoot 'Common\UI\GoalNoteText.cs'),
    (Join-Path $taskRoot 'tests\GoalNoteLayoutTests.cs')
) -CompilerOptions '/nullable:enable'
$results = [TerrariaChatGPT.Tests.GoalNoteLayoutTests]::Run()
Write-Output "Passed $($results.Count) goal-note viewport, drag, and multi-note checks."
