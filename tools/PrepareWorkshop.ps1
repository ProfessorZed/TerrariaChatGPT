[CmdletBinding()]
param([string]$SaveDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Terraria\tModLoader'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $root '.build\source\TerrariaChatGPT'
if (-not (Test-Path -LiteralPath (Join-Path $source 'icon_workshop.png'))) { throw 'Run Build Mod.cmd first to prepare the publishing source.' }
$modSources = Join-Path ([IO.Path]::GetFullPath($SaveDirectory)) 'ModSources'
$destination = Join-Path $modSources 'TerrariaChatGPT'
if (Test-Path -LiteralPath $destination) {
    $existing = Get-Item -LiteralPath $destination
    if ($existing.FullName -eq $root -or ($existing.LinkType -eq 'Junction' -and $existing.Target -contains $source)) {
        Write-Host 'Terraria ChatGPT is already available under Develop Mods.'
        return
    }
    throw 'A different TerrariaChatGPT source folder already exists. It has been left untouched.'
}
New-Item -ItemType Directory -Path $modSources -Force | Out-Null
New-Item -ItemType Junction -Path $destination -Target $source | Out-Null
Write-Host 'Open tModLoader > Workshop > Develop Mods > Terraria ChatGPT. Build + Reload, then Publish.'
Write-Host 'Keep this project folder in place until publication is complete.'
