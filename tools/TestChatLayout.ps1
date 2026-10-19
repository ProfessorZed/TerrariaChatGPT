# Run with PowerShell 7. No graphics device, game, credentials, or network is used.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    (Join-Path $taskRoot 'Common\Knowledge\AnswerHighlight.cs'),
    (Join-Path $taskRoot 'Common\UI\ChatTextLayout.cs'),
    (Join-Path $taskRoot 'Common\UI\ChatPanelGeometry.cs'),
    (Join-Path $taskRoot 'tests\ChatLayoutTests.cs')
)
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.ChatLayoutTests]::Run()
Write-Output "Passed $($results.Count) highlighting, wrapping, and viewport checks."
