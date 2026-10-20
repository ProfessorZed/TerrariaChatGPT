# Build and source releases share this allowlist. A .gitignore is not a release
# boundary: reject private state and unexpected files, including inside source
# directories, before returning any file for copying or archiving.
function Get-ReleaseSourceFiles {
    param([Parameter(Mandatory=$true)][string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    $manifest = [Collections.Generic.List[IO.FileInfo]]::new()

    function Assert-ReleasePath([string]$Path) {
        $full = [IO.Path]::GetFullPath($Path)
        if ($full -ne $rootPath -and -not $full.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Release source path escaped the project directory.'
        }
        $entry = Get-Item -LiteralPath $full -Force -ErrorAction Stop
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Release sources must not contain symbolic links or junctions.'
        }
        $parent = if ($entry.PSIsContainer) { $entry.Parent } else { $entry.Directory }
        while ($null -ne $parent -and $parent.FullName.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            if (($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Release source paths must not pass through symbolic links or junctions.'
            }
            $parent = $parent.Parent
        }
        return $entry
    }
    # Check ancestors as well: checking only a file's own attributes would miss
    # a normal-looking file reached through a redirected project directory.
    $ancestor = Get-Item -LiteralPath $rootPath -Force -ErrorAction Stop
    if (-not $ancestor.PSIsContainer) { throw 'Release source root must be a directory.' }
    while ($null -ne $ancestor) {
        if (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Release source root must not pass through symbolic links or junctions.'
        }
        $ancestor = $ancestor.Parent
    }

    function Add-ReleaseFile([string]$Relative) {
        $file = Assert-ReleasePath (Join-Path $rootPath $Relative)
        if ($file.PSIsContainer) { throw 'A required release file is a directory.' }
        $manifest.Add($file)
    }
    function Add-ReleaseDirectory([string]$Relative, [string[]]$Extensions, [string[]]$ExtraFiles = @()) {
        $directory = Assert-ReleasePath (Join-Path $rootPath $Relative)
        if (-not $directory.PSIsContainer) { throw 'A required release directory is a file.' }
        # Deliberately walk one level at a time. Recursive providers can traverse
        # a junction before the caller has a chance to reject its attributes.
        foreach ($entry in Get-ChildItem -LiteralPath $directory.FullName -Force -ErrorAction Stop) {
            $checked = Assert-ReleasePath $entry.FullName
            $child = $checked.FullName.Substring($rootPrefix.Length)
            if ($checked.PSIsContainer) {
                if ($checked.Name.StartsWith('.') -or $checked.Name -in @('bin','obj','node_modules','tModLoader-Logs')) {
                    throw "Unexpected state/build directory in release sources: $child"
                }
                Add-ReleaseDirectory $child $Extensions $ExtraFiles
            }
            elseif ($checked.Name.StartsWith('.') -or $checked.Name -match '(?i)\.dpapi([.-]|$)' -or
                $checked.Name -match '(?i)(^|[.-])(auth|credential|token|secret|password|welcome)([.-]|$)') {
                throw "Private state must not be included in release sources: $child"
            }
            elseif ($checked.Extension -in $Extensions -or $child.Replace('\','/') -in $ExtraFiles) {
                $manifest.Add($checked)
            }
            else { throw "Unexpected file in release sources: $child" }
        }
    }

    foreach ($name in @('.gitignore','README.md','LICENSE','CHANGELOG.md','build.txt','description.txt',
        'icon.png','icon_workshop.png','TerrariaChatGPT.csproj','TerrariaChatGPTMod.cs',
        'Build Mod.cmd','Install Mod.cmd')) { Add-ReleaseFile $name }
    Add-ReleaseDirectory 'Common' @('.cs') @('Common/Native/NOTICE.txt',
        'Common/Native/SignInWithChatGPT-LICENSE.txt','Common/Native/SignInWithChatGPT-THIRD-PARTY-NOTICES.txt')
    Add-ReleaseDirectory 'Localization' @('.hjson')
    Add-ReleaseFile 'Properties/launchSettings.json'
    Add-ReleaseDirectory 'assets/branding' @('.png')

    foreach ($folder in @('tests','tools')) { $null = Assert-ReleasePath (Join-Path $rootPath $folder) }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $rootPath 'tests') -File -Filter '*.cs' -Force) {
        Add-ReleaseFile ('tests/' + $file.Name)
    }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $rootPath 'tools') -File -Filter 'Test*.ps1' -Force) {
        Add-ReleaseFile ('tools/' + $file.Name)
    }
    foreach ($name in @('Build.ps1','InstallMod.ps1','InstallNativeUpdate.ps1','MigrateCredentialStorage.ps1','ModPackageInfo.ps1',
        'ReleaseFiles.ps1','PrepareRelease.ps1','PrepareWorkshop.ps1','CreateModIcons.ps1','ValidatePublishAssets.ps1','BenchmarkKnowledge.ps1')) {
        Add-ReleaseFile "tools/$name"
    }
    $null = Assert-ReleasePath (Join-Path $rootPath 'docs')
    foreach ($name in @('ICON.md','PUBLISHING.md','PRIVACY.md','SECURITY.md','ACCEPTANCE.md')) {
        Add-ReleaseFile "docs/$name"
    }
    # Emit only after the complete set passes validation.
    return $manifest.ToArray()
}
