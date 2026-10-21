# Actual picker controller with minimal game/UI stubs. No credentials, HTTP or game interaction.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    'Common\UI\ChatGptModelConfigElement.cs',
    'Common\Native\ChatGptModelOption.cs', 'Common\Native\NativeChatException.cs',
    'tests\ModelSelectorStubs.cs', 'tests\ModelSelectorTests.cs'
) | ForEach-Object { Join-Path $taskRoot $_ }
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.ModelSelectorTests]::Run()
$results
Write-Output "Passed $($results.Count) model-selector interaction checks (headless, mocked catalog only)."
