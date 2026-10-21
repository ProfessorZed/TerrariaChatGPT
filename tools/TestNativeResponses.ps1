[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    'Common\Native\NativeResponsesClient.cs', 'Common\Native\ChatGptModelOption.cs', 'Common\Native\ChatGptEffortOptions.cs', 'Common\Native\NativeChatPrompt.cs',
    'Common\Native\NativeChatException.cs', 'Common\Native\NativeReplyParser.cs',
    'Common\Services\ChatConvenience.cs', 'Common\Services\WebCitation.cs', 'Common\Services\GoalProposalReader.cs',
    'Common\Knowledge\AnswerFormatting.cs', 'Common\Knowledge\AnswerHighlight.cs',
    'Common\Knowledge\CraftingPlanner.cs', 'Common\Knowledge\LocalKnowledgeAnswer.cs',
    'Common\Knowledge\KnowledgeSnapshot.cs', 'Common\Goals\GoalModels.cs',
    'tests\NativeResponsesTests.cs'
) | ForEach-Object { Join-Path $taskRoot $_ }
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.NativeResponsesTests]::Run()
$results
Write-Output "Passed $($results.Count) native response transport checks (mocked HTTP only)."
