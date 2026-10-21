[CmdletBinding()]
param([switch]$RunLive, [switch]$CompileOnly, [ValidateSet('Recipe','WebGoal','Catalog')][string]$Phase = 'Recipe',
    [string]$SaveDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Terraria\tModLoader'))
$ErrorActionPreference = 'Stop'
if (-not $RunLive -and -not $CompileOnly) { throw 'This check uses the saved ChatGPT plan connection. Pass -RunLive explicitly.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
$sources = @('Common/Native/NativeAssistantRuntime.cs','Common/Native/NativeAuthStorage.cs',
    'Common/Native/NativeAuthProtocol.cs','Common/Native/NativeAuthCallback.cs','Common/Native/NativeChatGptSession.cs',
    'Common/Native/NativeResponsesClient.cs','Common/Native/ChatGptModelOption.cs','Common/Native/ChatGptEffortOptions.cs','Common/Native/NativeChatPrompt.cs','Common/Native/NativeChatException.cs',
    'Common/Native/NativeReplyParser.cs','Common/Native/NativeWikiClient.cs','Common/Security/GptCredentialStore.cs','Common/Security/CredentialStorage.cs',
    'Common/Services/ChatConvenience.cs','Common/Services/WebCitation.cs','Common/Services/GoalProposalReader.cs',
    'Common/Knowledge/AnswerFormatting.cs','Common/Knowledge/AnswerHighlight.cs','Common/Knowledge/CraftingPlanner.cs',
    'Common/Knowledge/LocalKnowledgeAnswer.cs','Common/Knowledge/KnowledgeSnapshot.cs','Common/Goals/GoalModels.cs',
    'tests/NativeAuthStubs.cs','tests/CredentialStorageStubs.cs','tests/NativeRuntimeConfigStub.cs','tests/NativeLiveAcceptance.cs')
$references = @((Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + [System.Security.Cryptography.ProtectedData].Assembly.Location
Add-Type -Path ($sources | ForEach-Object { Join-Path $taskRoot $_ }) -CompilerOptions '/nullable:enable' -ReferencedAssemblies $references
if ($CompileOnly) { Write-Output 'Production native runtime acceptance harness compiled; no account or network access.'; return }
[Terraria.Main]::SavePath = [IO.Path]::GetFullPath($SaveDirectory)
# An unrelated inherited API key must never participate in plan acceptance.
Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
if ($Phase -eq 'Catalog') { [TerrariaChatGPT.Tests.NativeLiveAcceptance]::InspectCatalogAsync().GetAwaiter().GetResult(); return }
$watch = [Diagnostics.Stopwatch]::StartNew()
$reply = [TerrariaChatGPT.Tests.NativeLiveAcceptance]::RunAsync($Phase).GetAwaiter().GetResult()
$watch.Stop()
$reply | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $taskRoot ".build/native-live-$($Phase.ToLowerInvariant()).json") -Encoding utf8
[pscustomobject]@{ Phase=$Phase; AnswerCharacters=$reply.Answer.Length; Highlights=$reply.Highlights.Count;
    WebSearched=$reply.WebSearched; Citations=$reply.Citations.Count; GoalSteps=$reply.GoalSuggestion.Steps.Count;
    Seconds=[Math]::Round($watch.Elapsed.TotalSeconds,1); SeparateCompanionUsed=$false; ApiProviderUsed=$false } | ConvertTo-Json
