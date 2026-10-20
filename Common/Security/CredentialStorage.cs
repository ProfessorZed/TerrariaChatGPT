#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TerrariaChatGPT.Common.Security;

internal sealed class CredentialStorageException : IOException
{
    internal CredentialStorageException() : base("Credential storage must be an ordinary local folder owned by your Windows account, without linked folders or files.") { }
}

internal static class CredentialStoragePaths
{
    internal static string GetDirectory() => ForSaveDirectory(Terraria.Main.SavePath);

    internal static string ForSaveDirectory(string saveDirectory)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory) || !Path.IsPathFullyQualified(saveDirectory))
            throw new IOException("The active tModLoader save folder is unavailable.");
        return Path.Combine(Path.GetFullPath(saveDirectory), "TerrariaChatGPT");
    }
}

// Only ciphertext is persisted here. A protected directory ACL prevents other
// ordinary Windows accounts from replacing files between validation and use.
// Code already running as this Windows account remains outside that guarantee.
internal sealed class SecureCredentialDirectory
{
    private readonly string _directory;
    internal SecureCredentialDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) throw Unsafe();
        _directory = Path.GetFullPath(directory);
    }

    internal void Ensure()
    {
        ValidateLocation();
        CheckPath(_directory);
        var directory = new DirectoryInfo(_directory);
        SecurityIdentifier owner = CurrentOwner();
        if (!directory.Exists)
        {
            var permissions = new DirectorySecurity();
            permissions.SetOwner(owner);
            permissions.SetAccessRuleProtection(true, false);
            AddPrivateRules(permissions, owner);
            // Apply the private ACL at creation, before writing any file.
            directory.Create(permissions);
        }
        CheckPath(_directory);
        DirectorySecurity current = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        if (!owner.Equals(current.GetOwner(typeof(SecurityIdentifier)))) throw Unsafe();
        var restricted = new DirectorySecurity();
        restricted.SetAccessRuleProtection(true, false);
        AddPrivateRules(restricted, owner);
        directory.SetAccessControl(restricted);
        CheckPath(_directory);
    }

    internal string PathFor(string name)
    {
        ValidateLocation();
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.Contains(':') || name is "." or "..") throw Unsafe();
        string path = Path.Combine(_directory, name);
        CheckPath(path);
        return path;
    }

    private void ValidateLocation()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Secure credential storage requires Windows.");
        // Network shares and alternate data streams are not credential storage.
        if (_directory.StartsWith(@"\\", StringComparison.Ordinal) || _directory.AsSpan(2).Contains(':')) throw Unsafe();
    }

    internal bool Exists(string name)
    {
        Ensure();
        string path = PathFor(name);
        if (Directory.Exists(path)) throw Unsafe();
        if (!File.Exists(path)) return false;
        RestrictFile(path);
        return true;
    }

    internal FileStream OpenLock(string name)
    {
        Ensure();
        string path = PathFor(name);
        if (File.Exists(path)) RestrictFile(path);
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { CheckPath(path); return stream; }
        catch { stream.Dispose(); throw; }
    }

    internal byte[] ReadBytes(string name, int maximumBytes, bool allowEmpty = false)
    {
        Ensure();
        string path = PathFor(name);
        RestrictFile(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        CheckPath(path);
        if ((!allowEmpty && stream.Length == 0) || stream.Length > maximumBytes) throw new IOException("The saved credential file has an invalid size.");
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    internal void AtomicWrite(string name, byte[] bytes, int maximumBytes, bool allowEmpty = false)
    {
        if ((!allowEmpty && bytes.Length == 0) || bytes.Length > maximumBytes) throw new IOException("The saved credential file has an invalid size.");
        Ensure();
        string path = PathFor(name);
        if (File.Exists(path)) RestrictFile(path);
        string temporary = PathFor(".credential-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            RestrictFile(temporary);
            CheckPath(path);
            File.Move(temporary, path, true);
            CheckPath(path);
        }
        finally
        {
            // Never follow a substituted link during cleanup; the file is ciphertext.
            try { CheckPath(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void RestrictFile(string path)
    {
        CheckPath(path);
        var file = new FileInfo(path);
        SecurityIdentifier owner = CurrentOwner();
        FileSecurity current = file.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        if (!owner.Equals(current.GetOwner(typeof(SecurityIdentifier)))) throw Unsafe();
        var restricted = new FileSecurity();
        restricted.SetAccessRuleProtection(true, false);
        restricted.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        restricted.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        file.SetAccessControl(restricted);
        CheckPath(path);
    }

    private static void AddPrivateRules(DirectorySecurity permissions, SecurityIdentifier owner)
    {
        const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        permissions.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        permissions.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
    }

    private static SecurityIdentifier CurrentOwner()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw Unsafe();
    }

    internal static void CheckPath(string path)
    {
        // Check ancestors before children, so even metadata checks do not walk
        // through a junction before discovering that it must be rejected.
        var ancestors = new Stack<string>();
        string? candidate = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(candidate))
        {
            ancestors.Push(candidate);
            candidate = Path.GetDirectoryName(candidate);
        }
        while (ancestors.Count > 0)
        {
            try
            {
                if ((File.GetAttributes(ancestors.Pop()) & FileAttributes.ReparsePoint) != 0) throw Unsafe();
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static CredentialStorageException Unsafe() => new();
}
