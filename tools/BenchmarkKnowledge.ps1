# Offline synthetic benchmark of the production C# sources. Run each scenario in a fresh PowerShell 7 process.
[CmdletBinding()]
param([ValidateSet('Observation', 'Reference')][string]$Scenario = 'Observation')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @('Common\Knowledge\KnowledgeSnapshot.cs')
if ($Scenario -eq 'Observation') {
    $sourcePaths += @('Common\Knowledge\WorldKnowledgeLedger.cs', 'Common\Players\GptKnowledgePlayer.cs', 'Common\Players\LegacyPlayerData.cs',
        'tests\ChestMemoryStubs.cs', 'tests\UnloadedPlayerStub.cs', 'tests\ObservationBenchmarks.cs')
} else {
    $sourcePaths += @('Common\Knowledge\LocalKnowledgeAnswer.cs', 'Common\Knowledge\CraftingPlanner.cs',
        'Common\Knowledge\GameReferenceFacts.cs', 'tests\GameReferenceFactsStubs.cs', 'tests\ReferenceBenchmarks.cs')
}
Add-Type -Path ($sourcePaths | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable'
if ($Scenario -eq 'Observation') { [TerrariaChatGPT.Tests.ObservationBenchmarks]::Run() | ConvertTo-Json }
else { [TerrariaChatGPT.Tests.ReferenceBenchmarks]::Run() | ConvertTo-Json }
