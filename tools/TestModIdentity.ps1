# Validate the actual packaged DLL with the installed game's VerifyMod method.
# This loads managed assemblies only. It does not launch Terraria, instantiate
# the mod, run its hooks, access credentials, or open a character/world.
[CmdletBinding()]
param(
    [string]$PackagePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\TerrariaChatGPT.tmod'),
    [string]$TmlPath = 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader',
    [string]$ExpectedName = 'TerrariaChatGPT',
    [switch]$ExpectNamespaceFailure
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ModPackageInfo.ps1')
$package = Get-TerrariaGptPackageInfo -Path $PackagePath -IncludeFileTable
if ($package.Name -cne $ExpectedName) { throw "Unexpected package name: $($package.Name)" }
$entry = @($package.Entries | Where-Object Name -CEQ "$ExpectedName.dll")
if ($entry.Count -ne 1) { throw 'The package must contain exactly one matching main assembly.' }
$entry = $entry[0]
if ($entry.Length -gt 64MB) { throw 'The packaged assembly exceeds the validation size limit.' }
$source = [IO.File]::OpenRead($PackagePath)
$reader = [IO.BinaryReader]::new($source)
try {
    $source.Position = $entry.Offset
    $assemblyBytes = $reader.ReadBytes($entry.StoredLength)
    if ($assemblyBytes.Length -ne $entry.StoredLength) { throw 'The packaged assembly is incomplete.' }
} finally { $reader.Dispose() }
if ($entry.StoredLength -ne $entry.Length) {
    $compressed = [IO.MemoryStream]::new($assemblyBytes, $false)
    $inflater = [IO.Compression.DeflateStream]::new($compressed, [IO.Compression.CompressionMode]::Decompress)
    $expanded = [IO.MemoryStream]::new()
    try {
        $buffer = New-Object byte[] 4096
        while (($read = $inflater.Read($buffer, 0, $buffer.Length)) -gt 0) {
            if ($expanded.Length + $read -gt $entry.Length) { throw 'The assembly expands past its declared size.' }
            $expanded.Write($buffer, 0, $read)
        }
        if ($expanded.Length -ne $entry.Length) { throw 'The expanded assembly has the wrong size.' }
        $assemblyBytes = $expanded.ToArray()
    } finally { $inflater.Dispose(); $compressed.Dispose(); $expanded.Dispose() }
}

if ([Environment]::Version.Major -lt 6) {
    # Windows PowerShell 5.1 ships with Windows but cannot load .NET 8 game
    # assemblies itself. Its built-in compiler can emit this simple reflection
    # runner; tModLoader's bundled runtime then executes it in a separate process.
    # No SDK, NuGet restore, PowerShell 7, or game launch is required.
    $projectRoot = Split-Path -Parent $PSScriptRoot
    $runRoot = Join-Path $projectRoot ('.build\identity-check-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $runner = Join-Path $runRoot 'VerifyMod.exe'
    $modDll = Join-Path $runRoot "$ExpectedName.dll"
    [IO.File]::WriteAllBytes($modDll, $assemblyBytes)
    Add-Type -OutputAssembly $runner -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;

public static class TerrariaChatGPTIdentityRunner
{
    public static int Main(string[] args)
    {
        try {
            string gamePath = Path.GetFullPath(args[0]);
            string expectedName = args[1];
            bool expectFailure = args[3] == "true";
            string[] libraries = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(gamePath), "Libraries"), "*.dll", SearchOption.AllDirectories);
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request) {
                AssemblyName requested = new AssemblyName(request.Name);
                foreach (string path in libraries.Where(p => Path.GetFileNameWithoutExtension(p) == requested.Name && !p.Replace('\\', '/').Contains("/ref/")).OrderBy(p => p.Contains("runtimes") ? 1 : 0)) {
                    try {
                        AssemblyName identity = AssemblyName.GetAssemblyName(path);
                        if (requested.Version == null || identity.Version == requested.Version) return Assembly.LoadFrom(path);
                    } catch (BadImageFormatException) { }
                }
                return null;
            };
            Assembly game = Assembly.LoadFrom(gamePath);
            Assembly mod = Assembly.Load(File.ReadAllBytes(args[2]));
            if (mod.GetName().Name != expectedName) throw new InvalidOperationException("Compiled assembly name does not match package name.");
            Type manager = game.GetType("Terraria.ModLoader.Core.AssemblyManager", true);
            MethodInfo verify = manager.GetMethod("VerifyMod", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (verify == null) throw new MissingMethodException(manager.FullName, "VerifyMod");
            object[] parameters = new object[] { expectedName, mod, null };
            try { verify.Invoke(null, parameters); }
            catch (TargetInvocationException error) {
                Exception cause = error.InnerException ?? error;
                if (expectFailure && (cause.Message.Contains("BuildErrorNamespaceFolderDontMatch") || cause.Message.Contains("Namespace and Folder name do not match"))) {
                    Console.WriteLine("PASS: installed tModLoader VerifyMod rejected the broken namespace fixture.");
                    return 0;
                }
                throw new InvalidOperationException("Installed tModLoader VerifyMod rejected this package: " + cause, cause);
            }
            if (expectFailure) throw new InvalidOperationException("Expected the broken namespace fixture to fail, but VerifyMod accepted it.");
            Type modType = parameters[2] as Type;
            if (modType == null || modType.Namespace != expectedName) throw new InvalidOperationException("The compiled Mod entry class must be in the exact package root namespace.");
            Type[] stale = mod.GetTypes().Where(t => t.Namespace == "TerrariaGPT" || (t.Namespace ?? "").StartsWith("TerrariaGPT.", StringComparison.Ordinal)).ToArray();
            if (stale.Length > 0) throw new InvalidOperationException("Compiled types remain in the old namespace: " + string.Join(", ", stale.Select(t => t.FullName)));
            Console.WriteLine("PASS: installed tModLoader VerifyMod accepted " + expectedName + "; entry class " + modType.FullName + "; no stale compiled namespaces.");
            return 0;
        } catch (Exception error) {
            Console.Error.WriteLine(error.ToString());
            return 1;
        }
    }
}
'@ -ErrorAction Stop
    $dotnet = Join-Path $TmlPath 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet)) {
        $command = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $command) { throw 'tModLoader .NET runtime not found.' }
        $dotnet = $command.Source
    }
    $expectArgument = if ($ExpectNamespaceFailure) { 'true' } else { 'false' }
    & $dotnet exec --runtimeconfig (Join-Path $TmlPath 'tModLoader.runtimeconfig.json') $runner (Join-Path $TmlPath 'tModLoader.dll') $ExpectedName $modDll $expectArgument
    if ($LASTEXITCODE -ne 0) { throw "Packaged mod identity validation failed (exit $LASTEXITCODE)." }
    Write-Output "Validated packaged mod $($package.Name) $($package.Version) without starting the game."
    return
}

if (-not ('TerrariaChatGPT.BuildChecks.ModIdentityVerifier' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace TerrariaChatGPT.BuildChecks
{
    // Isolate the game and mod from PowerShell's existing dependency versions.
    internal sealed class ValidationContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;
        private readonly Dictionary<string, List<string>> libraries;

        public ValidationContext(string gamePath) : base("TerrariaChatGPT identity validation", true)
        {
            resolver = new AssemblyDependencyResolver(gamePath);
            libraries = Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(gamePath), "Libraries"), "*.dll", SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/ref/"))
                .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Contains("runtimes") ? 1 : 0).ToList(), StringComparer.OrdinalIgnoreCase);
        }

        protected override Assembly Load(AssemblyName name)
        {
            string resolved = resolver.ResolveAssemblyToPath(name);
            if (resolved != null) return LoadFromAssemblyPath(resolved);
            if (libraries.TryGetValue(name.Name, out var candidates)) {
                foreach (string candidate in candidates) {
                    try {
                        var identity = AssemblyName.GetAssemblyName(candidate);
                        if (identity.Name == name.Name && (name.Version == null || identity.Version == name.Version))
                            return LoadFromAssemblyPath(candidate);
                    } catch (BadImageFormatException) { }
                }
            }
            return null; // Standard runtime assemblies are resolved by .NET.
        }

        protected override IntPtr LoadUnmanagedDll(string name)
        {
            throw new InvalidOperationException("Identity validation must not initialize native game code: " + name);
        }
    }

    public static class ModIdentityVerifier
    {
        public static string Verify(string gamePath, string expectedName, byte[] modBytes, bool expectNamespaceFailure)
        {
            var context = new ValidationContext(Path.GetFullPath(gamePath));
            try {
                var game = context.LoadFromAssemblyPath(Path.GetFullPath(gamePath));
                Assembly mod;
                using (var stream = new MemoryStream(modBytes, false)) mod = context.LoadFromStream(stream);
                if (mod.GetName().Name != expectedName)
                    throw new InvalidOperationException("Compiled assembly name does not match package name.");
                var manager = game.GetType("Terraria.ModLoader.Core.AssemblyManager", true);
                var verify = manager.GetMethod("VerifyMod", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (verify == null) throw new MissingMethodException(manager.FullName, "VerifyMod");
                var parameters = new object[] { expectedName, mod, null };
                try {
                    verify.Invoke(null, parameters);
                } catch (TargetInvocationException error) {
                    var cause = error.InnerException ?? error;
                    if (expectNamespaceFailure && (cause.Message.Contains("BuildErrorNamespaceFolderDontMatch") || cause.Message.Contains("Namespace and Folder name do not match")))
                        return "PASS: installed tModLoader VerifyMod rejected the broken namespace fixture.";
                    throw new InvalidOperationException("Installed tModLoader VerifyMod rejected this package: " + cause, cause);
                }
                if (expectNamespaceFailure)
                    throw new InvalidOperationException("Expected the broken namespace fixture to fail, but VerifyMod accepted it.");

                var modType = parameters[2] as Type;
                if (modType == null || modType.Namespace != expectedName)
                    throw new InvalidOperationException("The compiled Mod entry class must be in the exact package root namespace.");
                // Require every first-party type to have migrated, rather than
                // accepting tModLoader's weaker single matching-prefix check.
                var stale = mod.GetTypes().Where(t => t.Namespace == "TerrariaGPT" || (t.Namespace ?? "").StartsWith("TerrariaGPT.", StringComparison.Ordinal)).ToArray();
                if (stale.Length > 0)
                    throw new InvalidOperationException("Compiled types remain in the old namespace: " + string.Join(", ", stale.Select(t => t.FullName)));
                return "PASS: installed tModLoader VerifyMod accepted " + expectedName + "; entry class " + modType.FullName + "; no stale compiled namespaces.";
            } finally { context.Unload(); }
        }
    }
}
'@ -ErrorAction Stop
}
[TerrariaChatGPT.BuildChecks.ModIdentityVerifier]::Verify(
    (Join-Path $TmlPath 'tModLoader.dll'), $ExpectedName, $assemblyBytes, $ExpectNamespaceFailure.IsPresent)
Write-Output "Validated packaged mod $($package.Name) $($package.Version) without starting the game."
