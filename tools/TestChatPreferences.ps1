# Production preference saver with synthetic config storage; no account/files are changed.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$sources=@('Common/Services/GptChatPreferences.cs','Common/Native/ChatGptEffortOptions.cs','tests/ChatPreferencesTests.cs')
Add-Type -Path ($sources | ForEach-Object {Join-Path $taskRoot $_}) -CompilerOptions '/nullable:enable'
$results=[TerrariaChatGPT.Tests.ChatPreferencesTests]::Run()
$results
Write-Output "Passed $($results.Count) chat preference persistence checks."
