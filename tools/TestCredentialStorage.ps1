[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $taskRoot ('.build\credential-storage-' + [Guid]::NewGuid().ToString('N'))
$target = Join-Path $fixtureRoot 'link-target'
$link = Join-Path $fixtureRoot 'linked-folder'
New-Item -ItemType Directory -Path $target -Force | Out-Null
New-Item -ItemType Junction -Path $link -Target $target | Out-Null
$sources = @('Common/Security/CredentialStorage.cs','Common/Security/GptCredentialStore.cs',
    'tests/CredentialStorageStubs.cs','tests/CredentialStorageTests.cs')
$references = @((Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + [System.Security.Cryptography.ProtectedData].Assembly.Location
Add-Type -Path ($sources | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable' -ReferencedAssemblies $references
$results = [TerrariaChatGPT.Tests.CredentialStorageTests]::Run($fixtureRoot, $link)
$results
Write-Output "Passed $($results.Count) credential storage checks. Synthetic credentials only."
