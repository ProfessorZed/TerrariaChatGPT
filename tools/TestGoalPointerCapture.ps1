# Pure gesture sequences: no game, native input, credentials, or network.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $taskRoot 'Common\UI\GoalPointerCapture.cs'),
    (Join-Path $taskRoot 'tests\GoalPointerCaptureTests.cs')
) -CompilerOptions '/nullable:enable'
$results = [TerrariaChatGPT.Tests.GoalPointerCaptureTests]::Run()
Write-Output "Passed $($results.Count) goal pointer-capture regression checks."
