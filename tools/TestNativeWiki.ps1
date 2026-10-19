[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    'Common\Native\NativeWikiClient.cs', 'Common\Native\NativeChatException.cs', 'Common\Native\NativeReplyParser.cs',
    'Common\Services\WebCitation.cs', 'Common\Services\GoalProposalReader.cs',
    'Common\Knowledge\AnswerFormatting.cs', 'Common\Knowledge\AnswerHighlight.cs',
    'Common\Knowledge\CraftingPlanner.cs', 'Common\Knowledge\LocalKnowledgeAnswer.cs',
    'Common\Knowledge\KnowledgeSnapshot.cs', 'Common\Goals\GoalModels.cs', 'tests\NativeWikiTests.cs'
) | ForEach-Object { Join-Path $taskRoot $_ }
Add-Type -Path $sourcePaths -CompilerOptions '/nullable:enable' -ErrorAction Stop
$results = [TerrariaGPT.Tests.NativeWikiTests]::Run()
$results
Write-Output "Passed $($results.Count) native wiki checks (mocked HTTP only)."
