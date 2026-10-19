[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @('Common/Native/NativeChatException.cs','Common/Native/NativeAuthStorage.cs','Common/Native/NativeAuthProtocol.cs','Common/Native/NativeAuthCallback.cs','Common/Native/NativeChatGptSession.cs','tests/NativeAuthStubs.cs','tests/NativeAuthTests.cs')
Add-Type -Path ($sourcePaths | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable'
$results = [TerrariaChatGPT.Tests.NativeAuthTests]::Run($taskRoot)
$results
Write-Output "Passed $($results.Count) native authentication checks. Synthetic credentials only."
