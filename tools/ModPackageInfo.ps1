function Get-TerrariaGptPackageInfo {
    param([Parameter(Mandatory = $true)][string]$Path, [switch]$IncludeFileTable)
    $packageStream = [IO.File]::OpenRead($Path)
    $packageReader = [IO.BinaryReader]::new($packageStream)
    try {
        $magic = [Text.Encoding]::ASCII.GetString($packageReader.ReadBytes(4))
        if ($magic -ne 'TMOD') { throw 'This file is not a tModLoader package.' }
        $loaderVersion = $packageReader.ReadString()
        # The TMOD header stores its SHA-1, signature and payload length before metadata.
        $packageReader.ReadBytes(20 + 256) | Out-Null
        $payloadLength = $packageReader.ReadInt32()
        if ($payloadLength -le 0 -or $payloadLength -ne $packageStream.Length - $packageStream.Position) {
            throw 'The tModLoader package is incomplete.'
        }
        $packageName = $packageReader.ReadString()
        $packageVersion = $packageReader.ReadString()
        $entries = @()
        if ($IncludeFileTable) {
            $count = $packageReader.ReadInt32()
            if ($count -lt 0 -or $count -gt 100000) { throw 'Invalid package file count.' }
            [long]$relativeOffset = 0
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            for ($index = 0; $index -lt $count; $index++) {
                $name = $packageReader.ReadString()
                $length = $packageReader.ReadInt32()
                $storedLength = $packageReader.ReadInt32()
                if ($length -lt 0 -or $storedLength -lt 0 -or $storedLength -gt $length -or -not $names.Add($name)) {
                    throw 'Invalid package file table.'
                }
                $entries += [pscustomobject]@{ Name=$name; Length=$length; StoredLength=$storedLength; Offset=$relativeOffset }
                $relativeOffset += $storedLength
            }
            $dataStart = $packageStream.Position
            if ($relativeOffset -ne $packageStream.Length - $dataStart) { throw 'The package data does not match its file table.' }
            foreach ($entry in $entries) { $entry.Offset += $dataStart }
        }
        [pscustomobject]@{ Name = $packageName; Version = $packageVersion; TModLoaderVersion = $loaderVersion; Entries = $entries }
    }
    finally { $packageReader.Dispose() }
}

# Reads a bounded data entry as bytes only; no assembly is loaded or executed.
function Get-TerrariaGptPackageEntryHash {
    param([Parameter(Mandatory=$true)][string]$Path, [Parameter(Mandatory=$true)][string]$EntryName)
    $info = Get-TerrariaGptPackageInfo -Path $Path -IncludeFileTable
    $entry = @($info.Entries | Where-Object { $_.Name -ceq $EntryName })
    if ($entry.Count -ne 1) { return $null }
    $entry = $entry[0]
    if ($entry.Length -gt 1MB) { throw 'The fingerprint entry is too large.' }
    $source = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($source)
    try {
        $source.Position = $entry.Offset
        $bytes = $reader.ReadBytes($entry.StoredLength)
        if ($bytes.Length -ne $entry.StoredLength) { throw 'The fingerprint entry is incomplete.' }
    } finally { $reader.Dispose() }
    if ($entry.StoredLength -ne $entry.Length) {
        $compressed = [IO.MemoryStream]::new($bytes, $false)
        $inflater = [IO.Compression.DeflateStream]::new($compressed, [IO.Compression.CompressionMode]::Decompress)
        $expanded = [IO.MemoryStream]::new()
        try {
            $buffer = New-Object byte[] 4096
            while (($read = $inflater.Read($buffer, 0, $buffer.Length)) -gt 0) {
                if ($expanded.Length + $read -gt $entry.Length) { throw 'The fingerprint entry expands past its declared size.' }
                $expanded.Write($buffer, 0, $read)
            }
            if ($expanded.Length -ne $entry.Length) { throw 'The fingerprint entry has the wrong size.' }
            $bytes = $expanded.ToArray()
        } finally { $inflater.Dispose(); $compressed.Dispose(); $expanded.Dispose() }
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
