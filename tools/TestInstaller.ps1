# Uses fabricated save directories only. Optionally verifies migration with a
# known old local package; it never reads real preferences or changes real saves.
[CmdletBinding()]
param([string]$LegacyPackage)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $root ('.build\installer-tests-' + [Guid]::NewGuid().ToString('N'))
$staged = Join-Path $root 'dist\TerrariaChatGPT.tmod'
if (-not (Test-Path -LiteralPath $staged)) { throw 'Build the release first.' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
. (Join-Path $PSScriptRoot 'ModPackageInfo.ps1')
$testState = @{ RunningGame = $false }
# Only this isolated test invocation substitutes process discovery. The real
# installer retains its unconditional running-game guard.
function Get-CimInstance {
    param([string]$ClassName)
    if ($testState.RunningGame) { [pscustomobject]@{ Name='tModLoader.exe'; CommandLine='tModLoader.exe' } }
}
function Assert([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Read-Enabled([string]$Save) {
    $parsed = Get-Content -LiteralPath (Join-Path $Save 'Mods\enabled.json') -Raw | ConvertFrom-Json
    foreach ($name in $parsed) { $name }
}
function Install-TestSave([string]$Save) {
    $resolved = [IO.Path]::GetFullPath($Save)
    if (-not $resolved.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test save escaped its isolated directory.' }
    & (Join-Path $PSScriptRoot 'InstallMod.ps1') -SaveDirectory $resolved
}
function Write-TestSettings([string]$Save) {
    New-Item -ItemType Directory -Path (Join-Path $Save 'Mods'),(Join-Path $Save 'ModConfigs') -Force | Out-Null
    '["TerrariaGPT","OtherMod"]' | Set-Content -LiteralPath (Join-Path $Save 'Mods\enabled.json')
    '{"Provider":"WikiOnly","ChatPanelWidth":900}' | Set-Content -LiteralPath (Join-Path $Save 'ModConfigs\TerrariaGPT_GptClientConfig.json')
}
function Write-UnrelatedPackage([string]$Path) {
    $payload = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($payload)
    try {
        $writer.Write('TerrariaGPT'); $writer.Write('0.8.1'); $writer.Write([int]2)
        $notice = [Text.Encoding]::UTF8.GetBytes('This is another author''s unrelated mod.')
        $localization = [Text.Encoding]::UTF8.GetBytes('Unrelated localization')
        foreach ($entry in @(@('Common/Native/NOTICE.txt',$notice),@('Localization/en-US.hjson',$localization))) {
            $writer.Write([string]$entry[0]); $writer.Write([int]$entry[1].Length); $writer.Write([int]$entry[1].Length)
        }
        $writer.Write([byte[]]$notice); $writer.Write([byte[]]$localization); $writer.Flush()
        $body = $payload.ToArray()
        $file = [IO.BinaryWriter]::new([IO.File]::Create($Path))
        try {
            $file.Write([Text.Encoding]::ASCII.GetBytes('TMOD')); $file.Write('2026.8.3.0')
            $file.Write((New-Object byte[] 276)); $file.Write([int]$body.Length); $file.Write([byte[]]$body)
        } finally { $file.Dispose() }
    } finally { $writer.Dispose(); $payload.Dispose() }
}

$fresh = Join-Path $testRoot 'fresh'
Install-TestSave $fresh
$enabled = @(Read-Enabled $fresh)
Assert ($enabled.Count -eq 1 -and $enabled[0] -eq 'TerrariaChatGPT') 'A fresh install did not enable exactly the new mod.'
Assert ((Get-FileHash -LiteralPath $staged).Hash -eq (Get-FileHash -LiteralPath (Join-Path $fresh 'Mods\TerrariaChatGPT.tmod')).Hash) 'The installed package differs from its source.'
Write-Output 'PASS Fresh installation copies and enables the verified release.'

$unrelated = Join-Path $testRoot 'unrelated'
Write-TestSettings $unrelated
Write-UnrelatedPackage (Join-Path $unrelated 'Mods\TerrariaGPT.tmod')
Install-TestSave $unrelated
$enabled = @(Read-Enabled $unrelated)
Assert ($enabled -contains 'TerrariaGPT' -and $enabled -contains 'OtherMod' -and $enabled -contains 'TerrariaChatGPT') 'An unrelated mod with the same name/version was disabled.'
Assert (-not (Test-Path -LiteralPath (Join-Path $unrelated 'ModConfigs\TerrariaChatGPT_GptClientConfig.json'))) 'Preferences were copied from an unrelated mod.'
Write-Output 'PASS Same name and version cannot claim another mod or its preferences.'

$bad = Join-Path $testRoot 'invalid-list'
Write-TestSettings $bad
'{"unexpected":true}' | Set-Content -LiteralPath (Join-Path $bad 'Mods\enabled.json')
$rejected = $false
try { Install-TestSave $bad } catch { $rejected = $_.Exception.Message -match 'enabled mod list is invalid' }
Assert ($rejected -and -not (Test-Path -LiteralPath (Join-Path $bad 'Mods\TerrariaChatGPT.tmod'))) 'Invalid enable settings were not rejected before package installation.'
Write-Output 'PASS Invalid enabled.json leaves installed packages untouched.'

$testState.RunningGame = $true
$runningSave = Join-Path $testRoot 'running-guard'
$rejected = $false
try { Install-TestSave $runningSave } catch { $rejected = $_.Exception.Message -match 'exit tModLoader' }
finally { $testState.RunningGame = $false }
Assert ($rejected -and -not (Test-Path -LiteralPath $runningSave)) 'The running-game guard allowed changes.'
Write-Output 'PASS A running game blocks installation before any save-directory changes.'

if ($LegacyPackage) {
    $known = Join-Path $testRoot 'known-prerelease'
    Write-TestSettings $known
    Copy-Item -LiteralPath $LegacyPackage -Destination (Join-Path $known 'Mods\TerrariaGPT.tmod')
    $oldHash = (Get-FileHash -LiteralPath (Join-Path $known 'Mods\TerrariaGPT.tmod')).Hash
    Install-TestSave $known
    $enabled = @(Read-Enabled $known)
    Assert ($enabled -notcontains 'TerrariaGPT' -and $enabled -contains 'OtherMod' -and $enabled -contains 'TerrariaChatGPT') 'The known old build did not switch to the renamed mod.'
    $newConfig = Join-Path $known 'ModConfigs\TerrariaChatGPT_GptClientConfig.json'
    Assert ((Get-FileHash -LiteralPath $newConfig).Hash -eq (Get-FileHash -LiteralPath (Join-Path $known 'ModConfigs\TerrariaGPT_GptClientConfig.json')).Hash) 'Original preferences did not carry forward byte-for-byte.'
    Assert ((Get-FileHash -LiteralPath (Join-Path $known 'Mods\TerrariaGPT.tmod')).Hash -eq $oldHash) 'The old package was modified.'
    Write-Output 'PASS Known prerelease migration preserves settings, unrelated mods and the old package.'
    '{"Provider":"WikiOnly","ChatPanelWidth":700}' | Set-Content -LiteralPath $newConfig
    $editedHash = (Get-FileHash -LiteralPath $newConfig).Hash
    Install-TestSave $known
    Assert ((Get-FileHash -LiteralPath $newConfig).Hash -eq $editedHash) 'Reinstallation overwrote edited preferences.'
    Assert (@(Get-ChildItem -LiteralPath (Join-Path $known 'Mods') -Filter 'enabled.json.backup-*').Count -eq 2) 'Unique enabled.json backups were not retained.'
    Assert (@(Get-ChildItem -LiteralPath (Join-Path $known 'Mods') -Filter '.enabled-*.tmp').Count -eq 0) 'Atomic-write temporary files leaked.'
    Write-Output 'PASS Reinstallation preserves edited settings, keeps unique backups and leaves no temporary enable list.'
} else { Write-Output 'SKIP Known-prerelease migration: pass -LegacyPackage to verify an old local 0.8 package.' }
Write-Output "Installer test files retained at $testRoot"
