[CmdletBinding()]
param([string]$SaveDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Terraria\tModLoader'))
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$running = @(Get-CimInstance Win32_Process | Where-Object {
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)\btModLoader\.dll\b') -or $_.Name -eq 'tModLoader.exe'
})
if ($running.Count -gt 0) { throw 'tModLoader is still running. Save and exit it completely before installing.' }
. (Join-Path $PSScriptRoot 'ModPackageInfo.ps1')
$staged = Join-Path $taskRoot 'dist\TerrariaChatGPT.tmod'
$package = Get-TerrariaGptPackageInfo -Path $staged
if ($package.Name -ne 'TerrariaChatGPT') { throw 'Expected the Terraria ChatGPT package.' }
& (Join-Path $PSScriptRoot 'InstallMod.ps1') -SaveDirectory $SaveDirectory
$installed = Join-Path ([IO.Path]::GetFullPath($SaveDirectory)) 'Mods\TerrariaChatGPT.tmod'
$enabledPath = Join-Path ([IO.Path]::GetFullPath($SaveDirectory)) 'Mods\enabled.json'
$enabled = $false
if (Test-Path -LiteralPath $enabledPath) {
    $parsedEnabled = Get-Content -LiteralPath $enabledPath -Raw | ConvertFrom-Json
    $enabled = @($parsedEnabled) -contains 'TerrariaChatGPT'
}
[pscustomobject]@{
    InstalledVersion = (Get-TerrariaGptPackageInfo -Path $installed).Version
    HashVerified = (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash
    Enabled = $enabled
    RequiresCompanion = $false
    GameLaunched = $false
} | ConvertTo-Json
