# Actual effort picker with minimal UI/runtime stubs. No credentials, HTTP, or game interaction.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    'Common\UI\ChatGptEffortConfigElement.cs', 'Common\Native\ChatGptEffortOptions.cs',
    'Common\Native\ChatGptModelOption.cs', 'Common\Native\NativeChatException.cs',
    'tests\ModelSelectorStubs.cs', 'tests\EffortSelectorTests.cs'
) | ForEach-Object { Join-Path $taskRoot $_ }
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.EffortSelectorTests]::Run()
$results
Write-Output "Passed $($results.Count) effort-selector interaction checks (headless, mocked catalog only)."
