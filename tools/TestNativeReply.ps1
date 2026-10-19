[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    'Common\Knowledge\AnswerHighlight.cs',
    'Common\Knowledge\AnswerFormatting.cs',
    'Common\Knowledge\KnowledgeSnapshot.cs',
    'Common\Knowledge\CraftingPlanner.cs',
    'Common\Knowledge\LocalKnowledgeAnswer.cs',
    'Common\Goals\GoalModels.cs',
    'Common\Services\GoalProposalReader.cs',
    'Common\Services\WebCitation.cs',
    'Common\Native\NativeChatException.cs',
    'Common\Native\NativeReplyParser.cs',
    'tests\NativeReplyTests.cs'
) | ForEach-Object { Join-Path $taskRoot $_ }
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaChatGPT.Tests.NativeReplyTests]::Run()
$results | ForEach-Object { Write-Output $_ }
Write-Output "Passed $($results.Count) native reply checks."
