# Pure state tests: no game, account, credential or network access.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @('Common\UI\ReplyNotificationState.cs', 'tests\ReplyNotificationTests.cs') |
    ForEach-Object { Join-Path $taskRoot $_ }
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.ReplyNotificationTests]::Run()
$results
Write-Output "Passed $($results.Count) reply notification checks."
