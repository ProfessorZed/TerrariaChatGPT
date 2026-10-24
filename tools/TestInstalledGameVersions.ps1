# Read public managed metadata only. This does not initialize the game, start a
# client/server, load a world, read saves or credentials, or make network calls.
[CmdletBinding()]
param([string]$TmlPath = 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader')
$ErrorActionPreference = 'Stop'
$cecilPath = Get-ChildItem -LiteralPath (Join-Path $TmlPath 'Libraries\mono.cecil') -Filter 'Mono.Cecil.dll' -Recurse |
    Where-Object FullName -Match '[\\/]netstandard2\.0[\\/]' | Select-Object -First 1 -ExpandProperty FullName
if (-not $cecilPath) { throw 'The installed tModLoader metadata reader was not found.' }
Add-Type -Path $cecilPath
$gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $TmlPath 'tModLoader.dll'))
try {
    $main = $gameAssembly.MainModule.GetType('Terraria.Main')
    $versionField = @($main.Fields | Where-Object Name -CEQ 'versionNumber2')
    if ($versionField.Count -ne 1 -or -not $versionField[0].IsPublic -or -not $versionField[0].IsStatic -or $versionField[0].IsLiteral -or $versionField[0].FieldType.FullName -cne 'System.String') {
        throw 'Terraria.Main.versionNumber2 is not the expected public runtime string field.'
    }
    $initializer = @($main.Methods | Where-Object Name -CEQ '.cctor')[0]
    $initialValue = $null
    $instructions = @($initializer.Body.Instructions)
    for ($index = 1; $index -lt $instructions.Count; $index++) {
        $instruction = $instructions[$index]
        if ($instruction.OpCode.Code.ToString() -ceq 'Stsfld' -and $instruction.Operand.FullName -ceq 'System.String Terraria.Main::versionNumber2') {
            if ($instructions[$index - 1].OpCode.Code.ToString() -cne 'Ldstr') { throw 'The base game version initializer changed; review runtime capture.' }
            $initialValue = [string]$instructions[$index - 1].Operand
        }
    }
    if ($null -eq $initialValue -or $initialValue -cnotmatch '\Av?[0-9]{1,3}(?:\.[0-9]{1,5}){1,3}\z') {
        throw 'The installed base game runtime version has an unexpected format.'
    }
    $buildInfo = $gameAssembly.MainModule.GetType('Terraria.ModLoader.BuildInfo')
    $loaderField = @($buildInfo.Fields | Where-Object Name -CEQ 'tMLVersion')
    if ($loaderField.Count -ne 1 -or -not $loaderField[0].IsPublic -or -not $loaderField[0].IsStatic -or $loaderField[0].FieldType.FullName -cne 'System.Version') {
        throw 'BuildInfo.tMLVersion is not the expected public runtime Version field.'
    }
    $buildIdentifier = @($gameAssembly.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'System.Reflection.AssemblyInformationalVersionAttribute' })[0].ConstructorArguments[0].Value
    $metadata = [string]$buildIdentifier
    $delimiter = $metadata.IndexOf('+')
    if ($delimiter -lt 0) { throw 'The tModLoader build metadata format changed.' }
    # BuildInfo's installed static initializer reads this first payload field;
    # it is the loader version, never the base Terraria version.
    $loaderVersion = $metadata.Substring($delimiter + 1).Split('|')[0]
    if ($loaderVersion -cnotmatch '\A[0-9]{1,4}(?:\.[0-9]{1,5}){1,3}\z') { throw 'The loader version has an unexpected format.' }
    Write-Output ('PASS installed Terraria public runtime version field: ' + $initialValue)
    Write-Output ('PASS installed tModLoader public runtime version field: ' + $loaderVersion)
    Write-Output 'Passed 2 installed-version metadata checks without starting the game.'
} finally { $gameAssembly.Dispose() }
