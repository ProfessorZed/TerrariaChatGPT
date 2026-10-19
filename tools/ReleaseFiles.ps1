# Explicit source allowlist. Private profiles, logs, builds and the retired companion
# never enter a release, even when they exist in the developer's working folder.
function Get-ReleaseSourceFiles {
    param([Parameter(Mandatory=$true)][string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root)
    $topFiles = @('.gitignore','README.md','LICENSE','CHANGELOG.md','build.txt','description.txt',
        'icon.png','icon_workshop.png','TerrariaChatGPT.csproj','TerrariaChatGPTMod.cs',
        'Build Mod.cmd','Install Mod.cmd')
    foreach ($name in $topFiles) {
        $file = Join-Path $rootPath $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required release file is missing: $name" }
        Get-Item -LiteralPath $file
    }
    foreach ($folder in @('Common','Localization','Properties','assets\branding')) {
        Get-ChildItem -LiteralPath (Join-Path $rootPath $folder) -File -Recurse
    }
    Get-ChildItem -LiteralPath (Join-Path $rootPath 'tests') -File -Filter '*.cs'
    Get-ChildItem -LiteralPath (Join-Path $rootPath 'tools') -File -Filter 'Test*.ps1'
    foreach ($name in @('Build.ps1','InstallMod.ps1','InstallNativeUpdate.ps1','ModPackageInfo.ps1',
        'ReleaseFiles.ps1','PrepareRelease.ps1','PrepareWorkshop.ps1','CreateModIcons.ps1','ValidatePublishAssets.ps1','BenchmarkKnowledge.ps1')) {
        Get-Item -LiteralPath (Join-Path $rootPath "tools\$name")
    }
    foreach ($name in @('ICON.md','PUBLISHING.md','PRIVACY.md','ACCEPTANCE.md')) {
        Get-Item -LiteralPath (Join-Path $rootPath "docs\$name")
    }
}
