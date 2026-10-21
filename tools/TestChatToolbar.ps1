# Pure controller and geometry checks. Uses only fabricated model metadata.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sources = @('Common/Native/NativeChatException.cs','Common/Native/ChatGptModelOption.cs','Common/Native/ChatGptEffortOptions.cs',
    'Common/UI/ChatModelToolbarController.cs','Common/UI/ChatPanelGeometry.cs','Common/UI/ChatToolbarPopupLayout.cs','tests/ChatToolbarTests.cs')
Add-Type -Path ($sources | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable'
$results = [TerrariaChatGPT.Tests.ChatToolbarTests]::Run()
$results
Write-Output "Passed $($results.Count) chat toolbar checks. No game, login or network used."
