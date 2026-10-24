[CmdletBinding()]
param([switch]$ExportPreview)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$sources=@('Common/Knowledge/AnswerHighlight.cs','Common/UI/ChatTextLayout.cs','Common/UI/ChatPanelGeometry.cs',
 'Common/Services/WebCitation.cs','Common/Sorting/ChestSortModels.cs','Common/Sorting/SortSuggestion.cs','Common/UI/ModReleaseNotes.cs','Common/UI/GptChatPanel.cs','tests/ChatTooltipRenderingTests.cs')
Add-Type -Path ($sources | ForEach-Object {Join-Path $taskRoot $_}) -CompilerOptions '/nullable:enable'
$results=[TerrariaChatGPT.Tests.ChatTooltipRenderingTests]::Run()
$system=[IO.File]::ReadAllText((Join-Path $taskRoot 'Common/Systems/GptChatSystem.cs'))
if($system.IndexOf('_panel.DrawTooltips(Main.spriteBatch)') -le $system.IndexOf('_toolbar.Draw(Main.spriteBatch')) {
 throw 'The compositor must draw chat tooltips after the model toolbar.'
}
if($system -notmatch 'if \(!_chestSort.OwnsPointer && !_memory.OwnsPointer\)\s+_panel.DrawTooltips') {
 throw 'Chat tooltips must be suppressed while sort or memory modals own the pointer.'
}
Write-Output "Passed $($results.Count + 2) production chat tooltip rendering and compositor checks."
if($ExportPreview) {
 $preview=Join-Path $taskRoot '.build/ui-preview'
 $font=[IO.File]::ReadAllText((Join-Path $preview 'font.json'))
 foreach($size in @(@(1280,720,820),@(640,360,480))) {
  $name='tooltip-'+($size -join '-')+'.json'
  [TerrariaChatGPT.Tests.ChatTooltipRenderingTests]::Export($font,$size[0],$size[1],$size[2]) | Set-Content -LiteralPath (Join-Path $preview $name) -Encoding utf8
 }
}
