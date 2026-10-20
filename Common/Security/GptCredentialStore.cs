#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TerrariaChatGPT.Common.Security;

/// <summary>
/// Stores only a Windows-account-encrypted credential, compatible with the
/// original PowerShell setup. Only the native API provider reads this credential.
/// </summary>
public static class GptCredentialStore
{
    public const int MaximumKeyLength = 512;

    private const string Filename = "openai-key.dpapi";
    private static SecureCredentialDirectory Files => new(CredentialStoragePaths.GetDirectory());
    public static string GetPath() => Files.PathFor(Filename);

    public static bool HasSavedKey
    {
        get
        {
            try
            {
                var files = Files;
                return files.Exists(Filename) && files.ReadBytes(Filename, 16384, true).Length > 0;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
            catch (PlatformNotSupportedException) { return false; }
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
        // An empty encrypted-store marker means the user explicitly cleared it.
        WriteAtomically(string.Empty);
    }

    internal static string ReadKey()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Encrypted key storage currently requires Windows.");
        var files = Files;
        if (!files.Exists(Filename)) return string.Empty;
        string ciphertext = Encoding.UTF8.GetString(files.ReadBytes(Filename, 16384, true)).Trim();
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
        Files.AtomicWrite(Filename, Encoding.UTF8.GetBytes(ciphertext), 16384, true);
    }
}
