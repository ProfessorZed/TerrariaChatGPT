#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TerrariaGPT.Common.Security;

/// <summary>
/// Stores only a Windows-account-encrypted credential, compatible with the
/// original PowerShell setup. Only the native API provider reads this credential.
/// </summary>
public static class GptCredentialStore
{
    public const int MaximumKeyLength = 512;

    public static string GetPath()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
            throw new IOException("The Documents folder could not be located.");

        // A test world's save path must not redirect account credentials.
        return Path.Combine(documents, "My Games", "Terraria", "tModLoader",
            "TerrariaGPT", "openai-key.dpapi");
    }

    public static bool HasSavedKey
    {
        get
        {
            try
            {
                FileInfo file = new(GetPath());
                return file.Exists && file.Length > 0;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }
    }

    public static void SaveKey(string key)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Encrypted key storage currently requires Windows.");

        string normalized = (key ?? string.Empty).Trim();
        if (!IsValidKey(normalized))
            throw new ArgumentException("Enter an API key of 20 to 512 printable ASCII characters, without spaces.");

        byte[] plaintext = Encoding.Unicode.GetBytes(normalized);
        try
        {
            byte[] encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            WriteAtomically(Convert.ToHexString(encrypted).ToLowerInvariant());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static bool IsValidKey(string key)
    {
        if (key.Length < 20 || key.Length > MaximumKeyLength)
            return false;
        foreach (char character in key)
            if (character <= ' ' || character > '~')
                return false;
        return true;
    }

    public static void ClearKey()
    {
        // An empty file explicitly disables a previously saved key, including
        // an older companion's inherited OPENAI_API_KEY environment fallback.
        WriteAtomically(string.Empty);
    }

    internal static string ReadKey()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Encrypted key storage currently requires Windows.");
        string path = GetPath();
        if (!File.Exists(path)) return string.Empty;
        if (new FileInfo(path).Length > 16384)
            throw new IOException("The saved API key needs to be replaced in settings.");
        string ciphertext = File.ReadAllText(path).Trim();
        if (ciphertext.Length == 0) return string.Empty;
        byte[] plaintext = ProtectedData.Unprotect(Convert.FromHexString(ciphertext), null, DataProtectionScope.CurrentUser);
        try
        {
            if (plaintext.Length > MaximumKeyLength * 2 || plaintext.Length % 2 != 0)
                throw new IOException("The saved API key needs to be replaced in settings.");
            string key = Encoding.Unicode.GetString(plaintext);
            if (!IsValidKey(key)) throw new IOException("The saved API key needs to be replaced in settings.");
            return key;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static void WriteAtomically(string ciphertext)
    {
        string path = GetPath();
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".openai-key-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, ciphertext, new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            // The temporary file contains ciphertext only.
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
