[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sources = @('Common/Memory/MemoryModels.cs', 'Common/Knowledge/AnswerHighlight.cs', 'Common/UI/ChatTextLayout.cs', 'Common/UI/ChatPanelGeometry.cs',
 'Common/UI/ChestSortPanelGeometry.cs', 'Common/UI/MemoryPanelGeometry.cs', 'Common/UI/GoalPointerCapture.cs',
 'Common/UI/MemoryPanel.cs', 'tests/MemoryPanelInputTests.cs')
Add-Type -Path ($sources | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable'
$results = [TerrariaChatGPT.Tests.MemoryPanelInputTests]::Run()
$results
Write-Output "Passed $($results.Count) actual memory panel input checks."
