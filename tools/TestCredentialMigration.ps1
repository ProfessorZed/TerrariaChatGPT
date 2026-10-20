# Isolated synthetic files only. Does not access the user's account stores.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $root ('.build\credential-migration-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$state = @{ Running=$false }
function Get-CimInstance { param([string]$ClassName) if ($state.Running) { [pscustomobject]@{Name='tModLoader.exe';CommandLine='tModLoader.exe'} } }
function Assert([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Migrate([string]$Name) {
    & (Join-Path $PSScriptRoot 'MigrateCredentialStorage.ps1') -SaveDirectory (Join-Path $testRoot "$Name\save") -LegacyChatGptDirectory (Join-Path $testRoot "$Name\old") -LegacyApiKeyPath (Join-Path $testRoot "$Name\key.dpapi")
}
function Fixture([string]$Name) {
    $old = Join-Path $testRoot "$Name\old"
    New-Item -ItemType Directory -Path $old -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $old 'native-auth.dpapi'), [byte[]](1..64))
    [IO.File]::WriteAllText((Join-Path $old 'chatgpt-auth.json'), 'DO NOT IMPORT THIS LEGACY FILE')
    [IO.File]::WriteAllText((Join-Path $testRoot "$Name\key.dpapi"), 'SYNTHETIC-CIPHERTEXT')
}
$missing = Migrate 'empty'
Assert ($missing.ChatGPT -eq 'Not present' -and -not (Test-Path -LiteralPath (Join-Path $testRoot 'empty\save'))) 'An empty migration created credential state.'
Write-Output 'PASS Absent stores do not create credential state.'
Fixture 'copy'
$result = Migrate 'copy'
$destination = Join-Path $testRoot 'copy\save\TerrariaChatGPT'
Assert ($result.ChatGPT -eq 'Encrypted copy verified' -and $result.ApiKey -eq 'Encrypted copy verified' -and -not $result.CredentialsDecrypted) 'Migration did not verify encrypted copies.'
Assert ((Get-FileHash -LiteralPath (Join-Path $destination 'native-auth.dpapi')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $testRoot 'copy\old\native-auth.dpapi')).Hash) 'Native store bytes changed.'
Assert (-not (Test-Path -LiteralPath (Join-Path $destination 'chatgpt-auth.json'))) 'Legacy JSON was copied.'
Assert ((Get-Content -LiteralPath (Join-Path $destination 'native-auth.initialized') -Raw) -eq '1') 'Initialization marker missing.'
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$acl = Get-Acl -LiteralPath $destination
Assert ($acl.AreAccessRulesProtected -and @($acl.Access | Where-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin @($owner,'S-1-5-18') }).Count -eq 0) 'Credential folder is not restricted to the current account and SYSTEM.'
Write-Output 'PASS Ciphertext is copied byte-for-byte to a private folder; legacy JSON is ignored.'
[IO.File]::WriteAllText((Join-Path $destination 'native-auth.dpapi'), 'NEWER-SYNTHETIC-STATE')
$result = Migrate 'copy'
Assert ($result.ChatGPT -eq 'Existing destination preserved' -and (Get-Content -LiteralPath (Join-Path $destination 'native-auth.dpapi') -Raw) -eq 'NEWER-SYNTHETIC-STATE') 'Reinstallation rolled back newer credentials.'
Write-Output 'PASS Reinstallation never overwrites newer saved credentials.'
Remove-Item -LiteralPath (Join-Path $destination 'native-auth.dpapi')
$rejected = $false
try { Migrate 'copy' | Out-Null } catch { $rejected = $_.Exception.Message -match 'Automatic rollback' }
Assert $rejected 'Missing initialized credentials allowed an older token rollback.'
Write-Output 'PASS Missing initialized credentials fail closed instead of replaying an older refresh token.'
Fixture 'running'
$state.Running = $true; $rejected = $false
try { Migrate 'running' | Out-Null } catch { $rejected = $_.Exception.Message -match 'exit tModLoader' } finally { $state.Running=$false }
Assert ($rejected -and -not (Test-Path -LiteralPath (Join-Path $testRoot 'running\save'))) 'Running game did not block migration.'
Write-Output 'PASS Running-game guard prevents migration.'
Fixture 'locked'
$handle = [IO.File]::Open((Join-Path $testRoot 'locked\old\native-auth.lock'), [IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$rejected=$false
try { Migrate 'locked' | Out-Null } catch { $rejected=$true } finally { $handle.Dispose() }
Assert ($rejected -and -not (Test-Path -LiteralPath (Join-Path $testRoot 'locked\save\TerrariaChatGPT\native-auth.dpapi'))) 'Concurrent credential writer was not refused.'
Write-Output 'PASS Concurrent credential writer prevents copying.'
Fixture 'oversize'
[IO.File]::WriteAllBytes((Join-Path $testRoot 'oversize\old\native-auth.dpapi'), (New-Object byte[] 2097153))
$rejected=$false
try { Migrate 'oversize' | Out-Null } catch { $rejected=$_.Exception.Message -match 'invalid size' }
Assert $rejected 'Oversized store was accepted.'
Assert (@(Get-ChildItem -LiteralPath (Join-Path $testRoot 'oversize\save\TerrariaChatGPT') -Filter '.migration-*.tmp').Count -eq 0) 'Failed migration left temporary files.'
Write-Output 'PASS Oversized files are rejected without temporary residue.'
Fixture 'clear'
[IO.File]::WriteAllText((Join-Path $testRoot 'clear\key.dpapi'), '')
$result = Migrate 'clear'
Assert ($result.ApiKey -eq 'Encrypted copy verified' -and (Get-Item -LiteralPath (Join-Path $testRoot 'clear\save\TerrariaChatGPT\openai-key.dpapi')).Length -eq 0) 'Explicit key clearing was not preserved.'
Write-Output 'PASS Explicitly cleared API-key marker is preserved.'

function Add-BroadSyntheticAcl([string]$Path) {
    $entry = Get-Item -LiteralPath $Path
    $acl = if ($entry.PSIsContainer) { [Security.AccessControl.DirectorySecurity]::new() } else { [Security.AccessControl.FileSecurity]::new() }
    $acl.SetAccessRuleProtection($true, $false)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        [Security.Principal.SecurityIdentifier]::new($owner),
        [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        [Security.Principal.SecurityIdentifier]::new('S-1-1-0'),
        [Security.AccessControl.FileSystemRights]::Read, [Security.AccessControl.AccessControlType]::Allow))
    if ($PSVersionTable.PSVersion.Major -ge 7) { [IO.FileSystemAclExtensions]::SetAccessControl($entry, $acl) }
    else { $entry.SetAccessControl($acl) }
}
function Assert-PrivateAcl([string]$Path) {
    $acl = Get-Acl -LiteralPath $Path
    Assert ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -eq $owner) 'Credential owner changed.'
    Assert ($acl.AreAccessRulesProtected -and @($acl.Access | Where-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin @($owner,'S-1-5-18') }).Count -eq 0) 'Explicit broad access survived migration.'
}
Fixture 'broad-existing'
$null = Migrate 'broad-existing'
$broadTarget = Join-Path $testRoot 'broad-existing\save\TerrariaChatGPT'
$oldCipherHash = (Get-FileHash -LiteralPath (Join-Path $broadTarget 'native-auth.dpapi')).Hash
foreach ($name in @('native-auth.dpapi','openai-key.dpapi','native-auth.initialized','native-auth.lock')) { Add-BroadSyntheticAcl (Join-Path $broadTarget $name) }
Add-BroadSyntheticAcl $broadTarget
$result = Migrate 'broad-existing'
Assert ($result.ChatGPT -eq 'Existing destination preserved' -and (Get-FileHash -LiteralPath (Join-Path $broadTarget 'native-auth.dpapi')).Hash -eq $oldCipherHash) 'ACL repair changed existing ciphertext.'
Assert-PrivateAcl $broadTarget
foreach ($name in @('native-auth.dpapi','openai-key.dpapi','native-auth.initialized','native-auth.lock')) { Assert-PrivateAcl (Join-Path $broadTarget $name) }
Write-Output 'PASS Existing credential files lose explicit broad permissions without changing ciphertext or account ownership.'

Remove-Item -LiteralPath (Join-Path $testRoot 'broad-existing\old\native-auth.dpapi')
Remove-Item -LiteralPath (Join-Path $testRoot 'broad-existing\key.dpapi')
Add-BroadSyntheticAcl (Join-Path $broadTarget 'native-auth.dpapi')
$result = Migrate 'broad-existing'
Assert ($result.ChatGPT -eq 'Not present' -and (Get-FileHash -LiteralPath (Join-Path $broadTarget 'native-auth.dpapi')).Hash -eq $oldCipherHash) 'Existing credentials changed when legacy source was absent.'
Assert-PrivateAcl (Join-Path $broadTarget 'native-auth.dpapi')
Write-Output 'PASS Existing target permissions are secured even after legacy source retirement.'

Fixture 'target-locked'
$null = Migrate 'target-locked'
$lockTarget = Join-Path $testRoot 'target-locked\save\TerrariaChatGPT\native-auth.lock'
$handle = [IO.File]::Open($lockTarget,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$rejected=$false
try { Migrate 'target-locked' | Out-Null } catch { $rejected=$true } finally { $handle.Dispose() }
Assert $rejected 'An active target store was not refused.'
Write-Output 'PASS Existing target lock prevents concurrent migration.'

Fixture 'junction-source'
$alias = Join-Path $testRoot 'source-alias'
New-Item -ItemType Junction -Path $alias -Target (Join-Path $testRoot 'junction-source\old') | Out-Null
$rejected=$false
try {
    & (Join-Path $PSScriptRoot 'MigrateCredentialStorage.ps1') -SaveDirectory (Join-Path $testRoot 'junction-source\save') -LegacyChatGptDirectory $alias -LegacyApiKeyPath (Join-Path $testRoot 'junction-source\key.dpapi') | Out-Null
} catch { $rejected = $_.Exception.Message -match 'linked or redirected' }
Assert ($rejected -and -not (Test-Path -LiteralPath (Join-Path $testRoot 'junction-source\save'))) 'Redirected source path was traversed.'
Write-Output 'PASS A linked source directory is refused before creating target state.'

Fixture 'junction-target'
$outside = Join-Path $testRoot 'redirect-target'
New-Item -ItemType Directory -Path $outside,(Join-Path $testRoot 'junction-target\save') -Force | Out-Null
New-Item -ItemType Junction -Path (Join-Path $testRoot 'junction-target\save\TerrariaChatGPT') -Target $outside | Out-Null
$rejected=$false
try { Migrate 'junction-target' | Out-Null } catch { $rejected=$_.Exception.Message -match 'linked or redirected' }
Assert ($rejected -and @(Get-ChildItem -LiteralPath $outside -Force).Count -eq 0) 'Redirected target was modified.'
Write-Output 'PASS A linked target directory is refused without modifying its target.'
Write-Output 'Passed 13 credential migration checks using fabricated data.'
