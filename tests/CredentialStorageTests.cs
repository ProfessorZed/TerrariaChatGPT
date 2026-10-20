#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using TerrariaChatGPT.Common.Security;

namespace TerrariaChatGPT.Tests;

public static class CredentialStorageTests
{
    private static readonly List<string> Passed = new();
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Passed.Add(label); }
    private static void Reject(Action action, string label)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or FormatException)
        { Passed.Add(label); return; }
        throw new Exception(label);
    }

    public static string[] Run(string testRoot, string linkedDirectory)
    {
        Passed.Clear();
        Terraria.Main.SavePath = Path.Combine(testRoot, "custom-save");
        string expected = Path.Combine(Terraria.Main.SavePath, "TerrariaChatGPT", "openai-key.dpapi");
        Check(GptCredentialStore.GetPath() == expected, "API credential path uses the active custom tModLoader save directory");
        Check(!GptCredentialStore.HasSavedKey && GptCredentialStore.ReadKey() == "", "Fresh save folder has no inherited account key");
        const string synthetic = "sk-synthetic-fixture-keep-out-of-plaintext";
        GptCredentialStore.SaveKey(synthetic);
        Check(GptCredentialStore.HasSavedKey && GptCredentialStore.ReadKey() == synthetic, "Synthetic API credential survives an encrypted round trip");
        byte[] original = File.ReadAllBytes(expected);
        Check(!Encoding.UTF8.GetString(original).Contains(synthetic, StringComparison.Ordinal), "Stored API credential contains ciphertext only");
        Check(IsPrivate(new DirectoryInfo(Path.GetDirectoryName(expected)!).GetAccessControl()), "Credential directory permits only its Windows owner and SYSTEM");
        Check(IsPrivate(new FileInfo(expected).GetAccessControl()), "Encrypted credential file permits only its Windows owner and SYSTEM");
        var permissive = new FileInfo(expected).GetAccessControl();
        permissive.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(expected).SetAccessControl(permissive);
        Check(GptCredentialStore.ReadKey() == synthetic && IsPrivate(new FileInfo(expected).GetAccessControl()), "Reading a migrated file removes broad file permissions before decrypting");
        using (var locked = new FileStream(expected, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Reject(() => GptCredentialStore.SaveKey("sk-replacement-synthetic-credential"), "Failed replacement reports failure instead of destroying a saved key");
            Check(File.ReadAllBytes(expected).SequenceEqual(original), "Failed atomic replacement preserves the original ciphertext");
        }
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(expected)!, "*.tmp").Any(), "Failed replacement removes temporary ciphertext");
        File.WriteAllText(expected, "not-a-dpapi-envelope");
        Reject(() => GptCredentialStore.ReadKey(), "Malformed ciphertext fails closed");
        File.WriteAllText(expected, new string('0', 16385));
        Reject(() => GptCredentialStore.ReadKey(), "Oversized API credential is rejected before parsing");
        File.WriteAllBytes(expected, original);
        GptCredentialStore.ClearKey();
        Check(!GptCredentialStore.HasSavedKey && GptCredentialStore.ReadKey() == "" && File.Exists(expected), "Clearing a key persists an empty disabled marker");
        Terraria.Main.SavePath = Path.Combine(testRoot, "different-save");
        Check(!GptCredentialStore.HasSavedKey && GptCredentialStore.ReadKey() == "", "Changing active save folders cannot discover credentials in another folder");
        Terraria.Main.SavePath = "";
        Reject(() => GptCredentialStore.GetPath(), "An unavailable active save path fails closed");
        Reject(() => CredentialStoragePaths.ForSaveDirectory("relative-save"), "A relative save path cannot escape into a working directory");
        Reject(() => new SecureCredentialDirectory(@"\\server\share\credentials").Ensure(), "Network credential storage is refused");
        Reject(() => new SecureCredentialDirectory(Path.Combine(testRoot, "file:stream")).Ensure(), "Alternate data stream paths are refused");
        var files = new SecureCredentialDirectory(Path.Combine(testRoot, "safe"));
        Reject(() => files.PathFor("../escape"), "Parent path traversal filenames are refused");
        Reject(() => files.PathFor("key:stream"), "Alternate data stream filenames are refused");
        Reject(() => new SecureCredentialDirectory(linkedDirectory).Ensure(), "A credential folder junction is refused");
        Reject(() => new SecureCredentialDirectory(Path.Combine(linkedDirectory, "nested")).Ensure(), "A linked ancestor is refused before a child folder is created");
        Check(!Directory.Exists(Path.Combine(testRoot, "link-target", "nested")), "Rejected linked paths never create data in the target");
        var fileLinkDirectory = Path.Combine(testRoot, "linked-entry");
        Directory.CreateDirectory(fileLinkDirectory);
        // A directory in a credential filename must never be treated as absent.
        Directory.CreateDirectory(Path.Combine(fileLinkDirectory, "native-auth.dpapi"));
        Reject(() => new SecureCredentialDirectory(fileLinkDirectory).Exists("native-auth.dpapi"), "A directory substituted for a credential file fails closed");
        return Passed.ToArray();
    }

    private static bool IsPrivate(FileSystemSecurity permissions)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        return permissions.AreAccessRulesProtected && identity.User!.Equals(permissions.GetOwner(typeof(SecurityIdentifier))) &&
            permissions.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().All(rule =>
                rule.AccessControlType == AccessControlType.Allow && !rule.IsInherited &&
                (identity.User.Equals(rule.IdentityReference) || system.Equals(rule.IdentityReference)));
    }
}
