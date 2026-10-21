# Actual toolbar hit testing with no-op rendering and a fabricated catalog.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$sources=@('Common/Native/NativeChatException.cs','Common/Native/ChatGptModelOption.cs','Common/Native/ChatGptEffortOptions.cs',
 'Common/Knowledge/AnswerHighlight.cs','Common/UI/ChatTextLayout.cs','Common/UI/ChatModelToolbarController.cs',
 'Common/UI/ChatModelToolbar.cs','Common/UI/ChatToolbarPopupLayout.cs','Common/UI/ChatPanelGeometry.cs','tests/ChatToolbarInputTests.cs')
Add-Type -Path ($sources | ForEach-Object {Join-Path $taskRoot $_}) -CompilerOptions '/nullable:enable'
$results=[TerrariaChatGPT.Tests.ChatToolbarInputTests]::Run()
$results
Write-Output "Passed $($results.Count) chat toolbar input checks."
