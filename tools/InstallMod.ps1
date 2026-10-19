param(
    [string]$SaveDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Terraria\tModLoader')
)
$ErrorActionPreference = 'Stop'
$running = @(Get-CimInstance Win32_Process | Where-Object {
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)\btModLoader\.dll\b') -or $_.Name -eq 'tModLoader.exe'
})
if ($running.Count -gt 0) { throw 'Save and exit tModLoader completely before installing.' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceMod = Join-Path $projectRoot 'dist\TerrariaChatGPT.tmod'
if (-not (Test-Path -LiteralPath $sourceMod)) { throw 'Build the mod first with Build Mod.cmd.' }
. (Join-Path $PSScriptRoot 'ModPackageInfo.ps1')
$packageInfo = Get-TerrariaGptPackageInfo -Path $sourceMod
if ($packageInfo.Name -ne 'TerrariaChatGPT') { throw 'The staged file is not the Terraria ChatGPT mod.' }
$versionLine = Get-Content -LiteralPath (Join-Path $projectRoot 'build.txt') | Where-Object { $_ -match '^version\s*=\s*' } | Select-Object -First 1
if ($packageInfo.Version -ne ($versionLine -split '=', 2)[1].Trim()) { throw 'The staged mod is an older version. Run Build Mod.cmd before installing.' }
$modsDirectory = Join-Path ([IO.Path]::GetFullPath($SaveDirectory)) 'Mods'
New-Item -ItemType Directory -Path $modsDirectory -Force | Out-Null
$enabledPath = Join-Path $modsDirectory 'enabled.json'
$enabledMods = @()
if (Test-Path -LiteralPath $enabledPath) {
    $enabledJson = Get-Content -LiteralPath $enabledPath -Raw
    if (-not $enabledJson.TrimStart().StartsWith('[')) { throw 'The enabled mod list is invalid; it was not changed.' }
    # Windows PowerShell returns JSON arrays as one pipeline object; PowerShell 7
    # enumerates them. Assign first, then normalize, to support both hosts.
    $parsedEnabled = ConvertFrom-Json -InputObject $enabledJson
    $enabledMods = @($parsedEnabled)
    foreach ($enabledMod in $enabledMods) {
        if ($enabledMod -isnot [string] -or [string]::IsNullOrWhiteSpace($enabledMod)) { throw 'The enabled mod list is invalid; it was not changed.' }
    }
}
# A familiar name and version alone cannot identify this project: another mod
# already owns the old name. Match two exact content fingerprints from our native
# 0.8 prereleases, without loading or executing their DLLs.
$legacyMod = Join-Path $modsDirectory 'TerrariaGPT.tmod'
$isOurPrerelease = $false
if (Test-Path -LiteralPath $legacyMod) {
    try {
        $oldInfo = Get-TerrariaGptPackageInfo -Path $legacyMod
        $isOurPrerelease = $oldInfo.Name -eq 'TerrariaGPT' -and $oldInfo.Version -in @('0.8.0','0.8.1') -and
            (Get-TerrariaGptPackageEntryHash -Path $legacyMod -EntryName 'Common/Native/NOTICE.txt') -eq '74951b6c0a23da0814b9c6ab8ff5ebf593f5beb96c1429302d9c7f6dc2212d41' -and
            (Get-TerrariaGptPackageEntryHash -Path $legacyMod -EntryName 'Localization/en-US.hjson') -eq '386e4cfa1da71bfd5acb6b4ed3225d9d57387df49c672ee1f9ed78a1ee19c80d'
    } catch { }
}
$backupSuffix = '.backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$destinationMod = Join-Path $modsDirectory 'TerrariaChatGPT.tmod'
if (Test-Path -LiteralPath $destinationMod) {
    Copy-Item -LiteralPath $destinationMod -Destination ($destinationMod + $backupSuffix)
}
Copy-Item -LiteralPath $sourceMod -Destination $destinationMod -Force
if ((Get-FileHash -LiteralPath $sourceMod -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $destinationMod -Algorithm SHA256).Hash) {
    throw 'The installed mod did not match the staged package.'
}
$configDirectory = Join-Path ([IO.Path]::GetFullPath($SaveDirectory)) 'ModConfigs'
$oldConfig = Join-Path $configDirectory 'TerrariaGPT_GptClientConfig.json'
$newConfig = Join-Path $configDirectory 'TerrariaChatGPT_GptClientConfig.json'
# Copy preferences only. Credentials remain in their existing encrypted stores.
if ($isOurPrerelease -and (Test-Path -LiteralPath $oldConfig) -and -not (Test-Path -LiteralPath $newConfig)) {
    Copy-Item -LiteralPath $oldConfig -Destination $newConfig
}
if (Test-Path -LiteralPath $enabledPath) {
    Copy-Item -LiteralPath $enabledPath -Destination ($enabledPath + $backupSuffix)
}
$enabledMods = @($enabledMods | Where-Object { $_ -ne 'TerrariaChatGPT' -and (-not $isOurPrerelease -or $_ -ne 'TerrariaGPT') }) + 'TerrariaChatGPT'
$enabledTemporary = Join-Path $modsDirectory ('.enabled-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText($enabledTemporary, (ConvertTo-Json -InputObject $enabledMods), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $enabledPath) { [IO.File]::Replace($enabledTemporary, $enabledPath, [NullString]::Value) }
    else { [IO.File]::Move($enabledTemporary, $enabledPath) }
} finally {
    if (Test-Path -LiteralPath $enabledTemporary) { Remove-Item -LiteralPath $enabledTemporary }
}
Write-Host "Installed and enabled Terraria ChatGPT $($packageInfo.Version)."
if ($isOurPrerelease) { Write-Host 'The previous local Terraria GPT build is disabled and retained for rollback.' }
Write-Host 'In a world, press Enter and select the GPT tab, or type /gpt.'
