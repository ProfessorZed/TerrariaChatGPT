# Synthetic manifest fixtures only: no real account data is opened or copied.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $taskRoot ('.build\release-safety-' + [Guid]::NewGuid().ToString('N'))
. (Join-Path $PSScriptRoot 'ReleaseFiles.ps1')
$script:passed = 0
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Pass([string]$Message) { $script:passed++; Write-Output "PASS $Message" }
function Write-FixtureFile([string]$Root, [string]$Relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    Assert ($path.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Fixture escaped isolated test directory.'
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path, 'synthetic release fixture only')
}
function New-Fixture([string]$Name) {
    $fixture = Join-Path $testRoot $Name
    foreach ($file in @('.gitignore','README.md','LICENSE','CHANGELOG.md','build.txt','description.txt',
        'icon.png','icon_workshop.png','TerrariaChatGPT.csproj','TerrariaChatGPTMod.cs','Build Mod.cmd','Install Mod.cmd',
        'Common/Native/NativeAuthStorage.cs','Common/Native/NOTICE.txt','Common/Native/SignInWithChatGPT-LICENSE.txt',
        'Common/Native/SignInWithChatGPT-THIRD-PARTY-NOTICES.txt','Localization/en-US.hjson','Properties/launchSettings.json',
        'assets/branding/master.png','tests/SyntheticTests.cs','tools/TestSynthetic.ps1',
        'tools/Build.ps1','tools/InstallMod.ps1','tools/InstallNativeUpdate.ps1','tools/MigrateCredentialStorage.ps1',
        'tools/ModPackageInfo.ps1','tools/ReleaseFiles.ps1','tools/PrepareRelease.ps1','tools/PrepareWorkshop.ps1',
        'tools/CreateModIcons.ps1','tools/ValidatePublishAssets.ps1','tools/BenchmarkKnowledge.ps1',
        'docs/ICON.md','docs/PUBLISHING.md','docs/PRIVACY.md','docs/SECURITY.md','docs/ACCEPTANCE.md')) {
        Write-FixtureFile $fixture $file
    }
    return $fixture
}
function Must-Reject([string]$Fixture, [string]$Message) {
    $rejected = $false
    $emitted = [Collections.Generic.List[object]]::new()
    try { Get-ReleaseSourceFiles -Root $Fixture | ForEach-Object { $emitted.Add($_) } }
    catch { $rejected = $true }
    Assert $rejected $Message
    Assert ($emitted.Count -eq 0) 'A rejected manifest emitted a partial set of release files.'
    Pass $Message
}

$valid = New-Fixture 'valid'
$files = @(Get-ReleaseSourceFiles -Root $valid)
Assert ($files.Count -eq 37) 'Expected release files were lost or duplicated.'
Assert (($files | Where-Object { $_.Name -eq 'MigrateCredentialStorage.ps1' }).Count -eq 1) 'The encrypted migration tool was not included exactly once.'
Pass 'Expected code, licenses, migration tool and security documentation are included exactly once.'

foreach ($noise in @('native-auth.dpapi','.env','chatgpt-auth.json','welcome.json','capture.log','backup.cs.bak','unreviewed.dll','native-auth.dpapi.cs')) {
    $fixture = New-Fixture ('reject-' + [Guid]::NewGuid().ToString('N'))
    Write-FixtureFile $fixture ('Common/Native/' + $noise)
    Must-Reject $fixture "Unexpected/private source file is rejected before any release files are emitted: $noise"
}
$hidden = New-Fixture 'hidden-state'
Write-FixtureFile $hidden 'Common/.cache/Hidden.cs'
Must-Reject $hidden 'Hidden state/build directories cannot become source files.'

$excluded = New-Fixture 'excluded-roots'
foreach ($noise in @('.env','chatgpt-auth.json','native-auth.dpapi','dist/native-auth.dpapi',
    'bridge/chatgpt-auth.json','.build/welcome.json','tools/private.env','tests/profile.json',
    'Properties/private.json','docs/private.json')) { Write-FixtureFile $excluded $noise }
$selection = @(Get-ReleaseSourceFiles -Root $excluded)
Assert ($selection.Count -eq $files.Count) 'Excluded private files entered the release manifest.'
Pass 'Private state in excluded roots and non-allowlisted tools/properties/docs never enters a release.'

if ($env:OS -eq 'Windows_NT') {
    $outside = Join-Path $testRoot 'outside-source'
    Write-FixtureFile $outside 'Stolen.cs'
    $redirected = New-Fixture 'junction-source'
    New-Item -ItemType Junction -Path (Join-Path $redirected 'Common/Redirected') -Target $outside | Out-Null
    Must-Reject $redirected 'A source-directory junction is rejected before traversal.'

    $fixedParent = New-Fixture 'fixed-parent-junction'
    # Replace only the empty synthetic Properties folder after moving its sole
    # fixture elsewhere, never deleting or following a user path.
    $properties = [IO.Path]::GetFullPath((Join-Path $fixedParent 'Properties'))
    Assert ($properties.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Unsafe fixture path.'
    Remove-Item -LiteralPath (Join-Path $properties 'launchSettings.json')
    Remove-Item -LiteralPath $properties
    Write-FixtureFile $outside 'launchSettings.json'
    New-Item -ItemType Junction -Path $properties -Target $outside | Out-Null
    Must-Reject $fixedParent 'An explicitly allowlisted file cannot be reached through a redirected parent.'

    $alias = Join-Path $testRoot 'root-alias'
    New-Item -ItemType Junction -Path $alias -Target $valid | Out-Null
    Must-Reject $alias 'A redirected release root is rejected.'
}
Write-Output "Passed $script:passed release safety checks. Synthetic files only."
