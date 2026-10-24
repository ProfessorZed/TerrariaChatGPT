# Real service + detached local helpers against fabricated data and an in-process connection fixture.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @('Common\Services\GptAssistantService.cs','Common\Native\NativeChatException.cs','Common\Native\NativeReplyParser.cs','Common\Sorting\SortSuggestion.cs','Common\Native\NativeMemoryProtocol.cs','Common\Memory\MemoryModels.cs','tests\NativeServiceRuntimeStub.cs', 'Common\Services\ChatConvenience.cs',
    'Common\Knowledge\KnowledgeSnapshot.cs', 'Common\Knowledge\AnswerHighlight.cs',
    'Common\Knowledge\AnswerFormatting.cs', 'Common\Knowledge\LocalKnowledgeAnswer.cs',
    'Common\Knowledge\CraftingPlanner.cs', 'tests\AssistantServiceStubs.cs', 'tests\AssistantServiceTests.cs',
    'tests\ChatOnlyAssistantTests.cs','tests\OnlineFeatureServiceTests.cs','tests\MemoryServiceTests.cs','tests\ActionableSortServiceTests.cs','tests\ProgressServiceStub.cs',
    'Common\Goals\GoalModels.cs','Common\Services\WebCitation.cs','Common\Services\GoalProposalReader.cs','Common\Services\ObservedContextBuilder.cs',
    'Common\Services\GameVersionContext.cs','tests\GameVersionTests.cs')
Add-Type -Path ($sourcePaths | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable'
$results = [TerrariaChatGPT.Tests.AssistantServiceTests]::Run($taskRoot)
$results += [TerrariaChatGPT.Tests.AssistantServiceTests]::RunGameVersionChecks($taskRoot)
$results
Write-Output "Passed $($results.Count) assistant service integration checks."
