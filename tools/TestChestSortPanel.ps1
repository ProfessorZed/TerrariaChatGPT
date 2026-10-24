[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sources = @('Common/UI/ChatPanelGeometry.cs', 'Common/UI/ChestSortPanelGeometry.cs', 'tests/ChestSortPanelGeometryTests.cs')
Add-Type -Path ($sources | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable'
$results = [TerrariaChatGPT.Tests.ChestSortPanelGeometryTests]::Run()
$results
Write-Output "Passed $($results.Count) chest sort layout and click-guard checks."
