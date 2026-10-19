# Run with PowerShell 7. Tests compile the real chest observer and ledger against
# a tiny game host; no tModLoader process, save files, credentials, or network are used.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    (Join-Path $taskRoot 'Common\Knowledge\KnowledgeSnapshot.cs'),
    (Join-Path $taskRoot 'Common\Knowledge\WorldKnowledgeLedger.cs'),
    (Join-Path $taskRoot 'Common\Players\GptKnowledgePlayer.cs'),
    (Join-Path $taskRoot 'Common\Players\LegacyPlayerData.cs'),
    (Join-Path $taskRoot 'tests\ChestMemoryStubs.cs'),
    (Join-Path $taskRoot 'tests\UnloadedPlayerStub.cs'),
    (Join-Path $taskRoot 'tests\ChestMemoryTests.cs')
)

Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.ChestMemoryTests]::Run()
$results | ForEach-Object { Write-Output $_ }
Write-Output "Passed $($results.Count) chest memory checks."
