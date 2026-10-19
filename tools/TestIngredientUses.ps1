# Actual service/public-reference/arithmetic pipeline with fabricated definitions. No network or real saves.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$files = @('Common\Services\GptAssistantService.cs','Common\Native\NativeChatException.cs','Common\Native\NativeReplyParser.cs','tests\NativeServiceRuntimeStub.cs','Common\Services\ChatConvenience.cs',
    'Common\Knowledge\KnowledgeSnapshot.cs','Common\Knowledge\AnswerHighlight.cs','Common\Knowledge\AnswerFormatting.cs',
    'Common\Knowledge\LocalKnowledgeAnswer.cs','Common\Knowledge\CraftingPlanner.cs','Common\Knowledge\GameReferenceFacts.cs',
    'tests\GameReferenceFactsStubs.cs','tests\RecipePipelineStubs.cs','tests\IngredientUsesTests.cs',
    'tests\ProgressServiceStub.cs','Common\Goals\GoalModels.cs','Common\Services\WebCitation.cs',
    'Common\Services\GoalProposalReader.cs','Common\Services\ObservedContextBuilder.cs')
Add-Type -Path ($files | ForEach-Object { Join-Path $projectRoot $_ }) -CompilerOptions '/nullable:enable'
$result = [TerrariaGPT.Tests.IngredientUsesTests]::Run()
$result
Write-Output "Passed $($result.Count) ingredient-use pipeline checks."
