# Compile actual formatting/recipe helpers with fabricated detached data only.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @('Common\Knowledge\AnswerHighlight.cs', 'Common\Knowledge\AnswerFormatting.cs',
    'Common\Knowledge\KnowledgeSnapshot.cs', 'Common\Knowledge\CraftingPlanner.cs',
    'Common\Knowledge\LocalKnowledgeAnswer.cs', 'Common\UI\ChatTextLayout.cs', 'tests\AnswerFormattingTests.cs')
Add-Type -Path ($sourcePaths | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable'
$results = [TerrariaGPT.Tests.AnswerFormattingTests]::Run()
$results
Write-Output "Passed $($results.Count) answer formatting checks."
