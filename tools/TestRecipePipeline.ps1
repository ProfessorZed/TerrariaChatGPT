$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$files = @('Common\Services\GptAssistantService.cs','Common\Native\NativeChatException.cs','Common\Native\NativeReplyParser.cs','Common\Sorting\SortSuggestion.cs','Common\Native\NativeMemoryProtocol.cs','Common\Memory\MemoryModels.cs','tests\NativeServiceRuntimeStub.cs','Common\Services\ChatConvenience.cs',
    'Common\Knowledge\KnowledgeSnapshot.cs','Common\Knowledge\AnswerHighlight.cs','Common\Knowledge\AnswerFormatting.cs',
    'Common\Knowledge\LocalKnowledgeAnswer.cs','Common\Knowledge\CraftingPlanner.cs','Common\Knowledge\GameReferenceFacts.cs',
    'tests\GameReferenceFactsStubs.cs','tests\RecipePipelineStubs.cs','tests\RecipePipelineTests.cs',
    'tests\ProgressServiceStub.cs','Common\Goals\GoalModels.cs','Common\Services\WebCitation.cs',
    'Common\Services\GoalProposalReader.cs','Common\Services\ObservedContextBuilder.cs','Common\Services\GameVersionContext.cs')
Add-Type -Path ($files | ForEach-Object { Join-Path $projectRoot $_ }) -CompilerOptions '/nullable:enable'
$result = [TerrariaChatGPT.Tests.RecipePipelineTests]::Run()
$result
Write-Output "Passed $($result.Count) complete recipe pipeline checks."
