# One-time installer operation, never loaded or executed by the mod. Copies only
# encrypted native stores; it never decrypts credentials or imports legacy JSON.
[CmdletBinding()]
param(
    [string]$SaveDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Terraria\tModLoader'),
    [string]$LegacyChatGptDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TerrariaGPT\ChatGPT'),
    [string]$LegacyApiKeyPath = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Terraria\tModLoader\TerrariaGPT\openai-key.dpapi')
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Encrypted credential migration requires Windows.' }
$running = @(Get-CimInstance Win32_Process | Where-Object {
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)\btModLoader\.dll\b') -or $_.Name -eq 'tModLoader.exe'
})
if ($running.Count -gt 0) { throw 'Save and exit tModLoader completely before migrating credentials.' }
function Assert-LocalCredentialPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.StartsWith('\\') -or $full.Substring(2).Contains(':')) { throw 'Credential storage must be a local path without alternate data streams.' }
    $part = $full
    while ($part) {
        if (Test-Path -LiteralPath $part) {
            if ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Credential migration refused a linked or redirected path.'
            }
        }
        $parent = [IO.Path]::GetDirectoryName($part)
        if ($parent -eq $part) { break }; $part = $parent
    }
    return $full
}
function Protect-CredentialDirectory([string]$Path) {
    $null = Assert-LocalCredentialPath $Path
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $directoryInfo = [IO.DirectoryInfo]::new($Path)
    if (Test-Path -LiteralPath $Path) {
        if (-not $directoryInfo.Exists) { throw 'Credential storage must be a directory owned by the current Windows account.' }
        $current = if ($PSVersionTable.PSVersion.Major -ge 7) { [IO.FileSystemAclExtensions]::GetAccessControl($directoryInfo) } else { $directoryInfo.GetAccessControl() }
        if (-not $identity.Equals($current.GetOwner([Security.Principal.SecurityIdentifier]))) {
            throw 'Credential storage must be owned by the current Windows account.'
        }
    }
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetOwner($identity)
    $acl.SetAccessRuleProtection($true, $false)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity,
        [Security.AccessControl.FileSystemRights]::FullControl,
        [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',
        [Security.AccessControl.PropagationFlags]::None,
        [Security.AccessControl.AccessControlType]::Allow))
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::LocalSystemSid, $null),
        [Security.AccessControl.FileSystemRights]::FullControl,
        [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',
        [Security.AccessControl.PropagationFlags]::None,
        [Security.AccessControl.AccessControlType]::Allow))
    if (-not $directoryInfo.Exists) {
        if ($PSVersionTable.PSVersion.Major -ge 7) { [IO.FileSystemAclExtensions]::Create($directoryInfo, $acl) }
        else { $directoryInfo.Create($acl) }
    }
    else {
        if ($PSVersionTable.PSVersion.Major -ge 7) { [IO.FileSystemAclExtensions]::SetAccessControl($directoryInfo, $acl) }
        else { $directoryInfo.SetAccessControl($acl) }
    }
    $null = Assert-LocalCredentialPath $Path
}
function Protect-CredentialFile([string]$Path) {
    $null = Assert-LocalCredentialPath $Path
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $file = [IO.FileInfo]::new($Path)
    if (-not $file.Exists) { throw 'A credential file path is occupied by a directory.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $current = if ($PSVersionTable.PSVersion.Major -ge 7) { [IO.FileSystemAclExtensions]::GetAccessControl($file) } else { $file.GetAccessControl() }
    if (-not $identity.Equals($current.GetOwner([Security.Principal.SecurityIdentifier]))) {
        throw 'Credential files must be owned by the current Windows account.'
    }
    $acl = [Security.AccessControl.FileSecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($principal in @($identity, [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::LocalSystemSid, $null))) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($principal,
            [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
    }
    if ($PSVersionTable.PSVersion.Major -ge 7) { [IO.FileSystemAclExtensions]::SetAccessControl($file, $acl) }
    else { $file.SetAccessControl($acl) }
    $null = Assert-LocalCredentialPath $Path
}
function Copy-EncryptedStore([string]$Source, [string]$Destination, [int]$Maximum, [bool]$AllowEmpty) {
    $null = Assert-LocalCredentialPath $Source
    $null = Assert-LocalCredentialPath $Destination
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { return 'Not present' }
    if (Test-Path -LiteralPath $Destination) { Protect-CredentialFile $Destination; return 'Existing destination preserved' }
    $inputFile = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    $temporary = Join-Path ([IO.Path]::GetDirectoryName($Destination)) ('.migration-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        if ($inputFile.Length -gt $Maximum -or (-not $AllowEmpty -and $inputFile.Length -eq 0)) { throw 'Encrypted credential file has an invalid size.' }
        $outputFile = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try { $inputFile.CopyTo($outputFile); $outputFile.Flush($true) } finally { $outputFile.Dispose() }
        Protect-CredentialFile $temporary
        $inputFile.Position = 0
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $expected = [BitConverter]::ToString($hasher.ComputeHash($inputFile)).Replace('-','') } finally { $hasher.Dispose() }
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $expected) { throw 'Encrypted credential copy did not verify.' }
        $null = Assert-LocalCredentialPath $Destination
        [IO.File]::Move($temporary, $Destination)
        return 'Encrypted copy verified'
    } finally {
        $inputFile.Dispose()
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
}
$target = Assert-LocalCredentialPath (Join-Path $SaveDirectory 'TerrariaChatGPT')
$legacy = Assert-LocalCredentialPath $LegacyChatGptDirectory
$apiSource = Assert-LocalCredentialPath $LegacyApiKeyPath
$nativeSource = Join-Path $legacy 'native-auth.dpapi'
$nativeTarget = Join-Path $target 'native-auth.dpapi'
$marker = Join-Path $target 'native-auth.initialized'
if ((Test-Path -LiteralPath $marker) -and -not (Test-Path -LiteralPath $nativeTarget)) {
    throw 'The destination was initialized but its credentials are missing. Automatic rollback to older credentials is refused.'
}
if (-not (Test-Path -LiteralPath $nativeSource) -and -not (Test-Path -LiteralPath $apiSource) -and -not (Test-Path -LiteralPath $target)) {
    [pscustomobject]@{ ChatGPT='Not present'; ApiKey='Not present'; CredentialsDecrypted=$false }; return
}
Protect-CredentialDirectory $target
foreach ($name in @('native-auth.dpapi','openai-key.dpapi','native-auth.initialized','native-auth.lock')) {
    Protect-CredentialFile (Join-Path $target $name)
}
$lockPath = Join-Path $target 'native-auth.lock'
$null = Assert-LocalCredentialPath $lockPath
$targetLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$sourceLock = $null
try {
    # Recheck while holding the same lock as the runtime. A second installer
    # must not turn a missing initialized store into a stale-token rollback.
    if ((Test-Path -LiteralPath $marker) -and -not (Test-Path -LiteralPath $nativeTarget)) {
        throw 'The destination was initialized but its credentials are missing. Automatic rollback to older credentials is refused.'
    }
    if ((Test-Path -LiteralPath $nativeSource) -and -not [string]::Equals($legacy,$target,[StringComparison]::OrdinalIgnoreCase)) {
        $oldLock = Join-Path $legacy 'native-auth.lock'
        $null = Assert-LocalCredentialPath $oldLock
        $sourceLock = [IO.File]::Open($oldLock, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    }
    $nativeResult = Copy-EncryptedStore $nativeSource $nativeTarget 2097152 $false
    if ((Test-Path -LiteralPath $nativeTarget) -and -not (Test-Path -LiteralPath $marker)) {
        $null = Assert-LocalCredentialPath $marker
        $markerFile = [IO.File]::Open($marker, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $markerFile.WriteByte(49); $markerFile.Flush($true) } finally { $markerFile.Dispose() }
    }
    $apiResult = Copy-EncryptedStore $apiSource (Join-Path $target 'openai-key.dpapi') 16384 $true
    [pscustomobject]@{ ChatGPT=$nativeResult; ApiKey=$apiResult; CredentialsDecrypted=$false }
} finally {
    if ($sourceLock) { $sourceLock.Dispose() }; $targetLock.Dispose()
}
