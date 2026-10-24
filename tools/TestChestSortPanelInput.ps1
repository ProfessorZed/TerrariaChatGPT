[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sources = @('Common/Knowledge/AnswerHighlight.cs', 'Common/UI/ChatTextLayout.cs', 'Common/UI/ChatPanelGeometry.cs',
 'Common/UI/ChestSortPanelGeometry.cs', 'Common/UI/GoalPointerCapture.cs', 'Common/Sorting/ChestSortModels.cs', 'Common/Sorting/SortSuggestion.cs',
 'Common/UI/ChestSortPanel.cs', 'tests/ChestSortPanelInputTests.cs')
Add-Type -Path ($sources | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable'
$results = [TerrariaChatGPT.Tests.ChestSortPanelInputTests]::Run()
$results
Write-Output "Passed $($results.Count) actual chest sort panel input checks."
