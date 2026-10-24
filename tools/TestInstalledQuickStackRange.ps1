[CmdletBinding()]
param([string]$TmlPath = 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader')
$ErrorActionPreference = 'Stop'
$cecilPath = Get-ChildItem -LiteralPath (Join-Path $TmlPath 'Libraries\mono.cecil') -Recurse -Filter Mono.Cecil.dll | Select-Object -First 1 -ExpandProperty FullName
Add-Type -Path $cecilPath
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $TmlPath 'tModLoader.dll'))
try {
    $chest = $assembly.MainModule.GetType('Terraria.Chest')
    $put = $chest.Methods | Where-Object Name -eq 'PutItemInNearbyChest' | Select-Object -First 1
    $instructions = @($put.Body.Instructions)
    $body = ($instructions | ForEach-Object ToString) -join "`n"
    if ($body -notmatch 'Vector2::Length\(\)\r?\n[^\n]+ldc.r4 600\r?\n[^\n]+bge') {
        throw 'Installed placed-chest Quick Stack range/boundary changed; review the sort radius before release.'
    }
    if ($body -notmatch 'Chest::x\r?\n[^\n]+ldc.i4.s 16\r?\n[^\n]+mul\r?\n[^\n]+ldc.i4.s 16\r?\n[^\n]+add' -or
        $body -notmatch 'Chest::y\r?\n[^\n]+ldc.i4.s 16\r?\n[^\n]+mul\r?\n[^\n]+ldc.i4.s 16\r?\n[^\n]+add') {
        throw 'Installed Quick Stack chest-center geometry changed.'
    }
    foreach ($required in @('Chest::IsPlayerInChest', 'Chest::IsLocked')) {
        if (-not $body.Contains($required)) { throw "Installed Quick Stack is missing expected guard $required." }
    }
    $player = $assembly.MainModule.GetType('Terraria.Player')
    $quick = $player.Methods | Where-Object Name -eq 'QuickStackAllChests' | Select-Object -First 1
    $quickBody = ($quick.Body.Instructions | ForEach-Object ToString) -join "`n"
    if (-not $quickBody.Contains('Chest::PutItemInNearbyChest(Terraria.Item,Microsoft.Xna.Framework.Vector2)') -or
        -not $quickBody.Contains('Player::HasLockedInventory()') -or -not $quickBody.Contains('Terraria.Entity::get_Center()')) {
        throw 'The installed Quick Stack entry point changed.'
    }
    [pscustomobject]@{
        passed = $true
        source = 'Installed Terraria.Chest.PutItemInNearbyChest IL'
        radiusPixels = 600
        boundary = 'strictly less than; exactly 600 is excluded'
        chestCenter = '(tileX * 16 + 16, tileY * 16 + 16)'
        playerPoint = 'Player.Center'
        excludes = @('locked chest', 'chest currently open by a player')
    } | ConvertTo-Json
}
finally { $assembly.Dispose() }
