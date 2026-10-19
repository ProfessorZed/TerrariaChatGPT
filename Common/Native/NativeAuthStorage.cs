// C# adaptation of OpenAI Sign in with ChatGPT DevKit; see NOTICE.txt and SignInWithChatGPT-LICENSE.txt.
// Terraria ChatGPT 0.9.0: renamed app labels; existing credential storage retained (2026-10-19).
// Terraria GPT 0.8.0: native storage/OAuth, safe migration and refresh recovery (2026-10-19).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TerrariaGPT.Common.Native;

// Private persistence types deliberately never cross into chat, settings, or diagnostics.
internal sealed class NativeAuthState
{
    public int Version { get; set; } = 1;
    public string HostId { get; set; } = "";
    public string? ActiveProfileId { get; set; }
    public List<NativeAuthProfile> Profiles { get; set; } = new();
    public List<NativeAuthRegistration> PendingRegistrations { get; set; } = new();
    public List<string> Welcomed { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public NativeAuthProfile? Active => Profiles.FirstOrDefault(p => p.Id == ActiveProfileId);
}
internal sealed class NativeAuthProfile
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Label { get; set; } = "Connection 1";
    public string ClientId { get; set; } = "";
    public string Status { get; set; } = "disconnected";
    public string SavedAt { get; set; } = "";
    public string? Subject { get; set; }
    public NativeAuthIdentity? Identity { get; set; }
    public string? ProfileIdToken { get; set; }
    public List<string> Scopes { get; set; } = new();
    public NativeAuthCredentials? Credentials { get; set; }
    public NativeAuthRotation? PendingRefresh { get; set; }
    public bool RefreshUncertain { get; set; }
    public bool RequiresNewRegistration { get; set; }
}
internal sealed class NativeAuthRegistration
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "Connection 1";
    public string ClientId { get; set; } = "";
    public string SavedAt { get; set; } = "";
    public string? ReplacesProfileId { get; set; }
}
internal sealed class NativeAuthIdentity { public string? Name { get; set; } public string? Email { get; set; } }
internal sealed class NativeAuthCredentials
{
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public long ExpiresAt { get; set; }
    public JsonElement EarliestRefreshAt { get; set; }
}
internal sealed class NativeAuthRotation
{
    public NativeAuthCredentials Credentials { get; set; } = new();
    public List<string> Scopes { get; set; } = new();
    public string? IdToken { get; set; }
    public long ReceivedAt { get; set; }
}

internal sealed class NativeAuthStorage
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault };
    private readonly string _directory;
    private string Filename => Path.Combine(_directory, "native-auth.dpapi");
    private string Marker => Path.Combine(_directory, "native-auth.initialized");
    internal NativeAuthStorage(string directory) { _directory = Path.GetFullPath(directory); }
    internal async Task<FileStream> LockAsync(CancellationToken cancellation)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            CheckSafe(_directory);
            string lockPath = Path.Combine(_directory, "native-auth.lock");
            for (int attempt = 0; ; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (File.Exists(lockPath)) CheckSafe(lockPath);
                try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (attempt < 100) { await Task.Delay(100, cancellation); }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (NativeChatException) { throw; }
        catch { throw Error("storage_busy", "Another copy of Terraria ChatGPT may be using this connection. Close the other copy and try again."); }
    }
    internal NativeAuthState Read()
    {
        try
        {
            if (File.Exists(Filename))
            {
                byte[] plaintext = NativeAuthDpapi.Unprotect(ReadBytes(Filename));
                try
                {
                    NativeAuthState state = JsonSerializer.Deserialize<NativeAuthState>(plaintext, JsonOptions) ?? throw Invalid();
                    Validate(state);
                    EnsureMarker();
                    return state;
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            if (File.Exists(Marker)) throw Error("storage_missing", "Saved native credentials are missing. Restore native-auth.dpapi before reconnecting. The older companion credentials will not be reused.");
            // Atomically own the same directory lock as the old SDK until handover is durable.
            // An existence check alone would race a companion refresh immediately afterward.
            using var legacyLock = NativeAuthMigrationLock.Acquire(Path.Combine(_directory, ".chatgpt-auth.lock"));
            NativeAuthState migrated = new();
            string oldFile = Path.Combine(_directory, "chatgpt-auth.json");
            if (File.Exists(oldFile))
            {
                using JsonDocument envelope = JsonDocument.Parse(ReadBytes(oldFile));
                JsonElement root = envelope.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out int number)) throw Invalid();
                if (number == 3)
                {
                    if (root.GetProperty("provider").GetString() != "windows-dpapi-v1") throw Invalid();
                    byte[] decrypted = NativeAuthDpapi.Unprotect(Convert.FromBase64String(root.GetProperty("ciphertext").GetString() ?? ""));
                    try { using JsonDocument content = JsonDocument.Parse(decrypted); ImportProfiles(migrated, content.RootElement); }
                    finally { CryptographicOperations.ZeroMemory(decrypted); }
                }
                else ImportProfiles(migrated, root);
            }
            string hostFile = Path.Combine(_directory, "chatgpt-host.json");
            if (File.Exists(hostFile))
            {
                using JsonDocument host = JsonDocument.Parse(ReadBytes(hostFile));
                if (host.RootElement.GetProperty("version").GetInt32() != 1) throw Invalid();
                migrated.HostId = host.RootElement.GetProperty("id").GetString() ?? "";
            }
            else if (File.Exists(oldFile)) throw Error("host_identity_missing", "The saved ChatGPT host identity is missing. Restore chatgpt-host.json before reconnecting.");
            else migrated.HostId = "urn:uuid:" + Guid.NewGuid().ToString();
            string welcomeFile = Path.Combine(_directory, "welcome.json");
            if (File.Exists(welcomeFile))
            {
                try { migrated.Welcomed = JsonSerializer.Deserialize<List<string>>(ReadBytes(welcomeFile)) ?? new(); }
                catch (JsonException) { migrated.Welcomed = new(); } // A damaged acknowledgement only requires acknowledging again.
            }
            Validate(migrated);
            Write(migrated);
            return migrated;
        }
        catch (NativeChatException) { throw; }
        catch { throw Invalid(); }
    }
    private static void ImportProfiles(NativeAuthState state, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out int number)) throw Invalid();
        if (number == 2)
        {
            state.Profiles = root.GetProperty("profiles").Deserialize<List<NativeAuthProfile>>(JsonOptions) ?? throw Invalid();
            state.PendingRegistrations = root.GetProperty("pendingRegistrations").Deserialize<List<NativeAuthRegistration>>(JsonOptions) ?? throw Invalid();
            state.ActiveProfileId = root.TryGetProperty("activeProfileId", out var active) ? active.GetString() : null;
        }
        else if (number == 1)
        {
            var profile = root.Deserialize<NativeAuthProfile>(JsonOptions) ?? throw Invalid();
            profile.Id = Guid.NewGuid().ToString();
            profile.RequiresNewRegistration = true;
            state.Profiles.Add(profile);
            state.ActiveProfileId = profile.Id;
        }
        else throw Invalid();
    }
    internal void Write(NativeAuthState state)
    {
        Validate(state);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        try { AtomicWrite(Filename, NativeAuthDpapi.Protect(plaintext)); EnsureMarker(); }
        catch (NativeChatException) { throw; }
        catch { throw Error("storage_error", "ChatGPT credentials could not be saved securely. Existing files have been preserved; check storage permissions."); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    private void EnsureMarker()
    {
        if (!File.Exists(Marker)) AtomicWrite(Marker, new byte[] { 1 });
        else CheckSafe(Marker);
    }
    private void AtomicWrite(string path, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) throw Invalid();
        CheckSafe(_directory);
        if (File.Exists(path)) CheckSafe(path);
        string temporary = Path.Combine(_directory, ".native-auth." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static byte[] ReadBytes(string path)
    {
        CheckSafe(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length == 0 || file.Length > 2 * 1024 * 1024) throw Invalid();
        byte[] bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }
    private static void CheckSafe(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Error("storage_unsafe", "ChatGPT credential storage must be a normal private folder, not a linked folder or file.");
    }
    internal static void Validate(NativeAuthState state)
    {
        if (state.Version != 1 || !Regex.IsMatch(state.HostId ?? "", @"^urn:uuid:[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$") ||
            state.Profiles is null || state.PendingRegistrations is null || state.Welcomed is null || state.Profiles.Count > 100 || state.PendingRegistrations.Count > 100 || state.Welcomed.Count > 200) throw Invalid();
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (var profile in state.Profiles)
        {
            if (profile is null || !ValidId(profile.Id) || !ids.Add(profile.Id) || !ValidClient(profile.ClientId) || !Text(profile.Label, 120) ||
                profile.Version != 1 || profile.Status is not ("connected" or "disconnected" or "reauth_required") || !Text(profile.SavedAt, 100) ||
                !ScopesValid(profile.Scopes) || (profile.Subject is not null && !Text(profile.Subject, 512)) ||
                (profile.ProfileIdToken is not null && !Text(profile.ProfileIdToken, 65536))) throw Invalid();
            if (profile.Status == "connected" && string.IsNullOrEmpty(profile.Subject)) throw Invalid();
            if (profile.Identity is not null && ((profile.Identity.Email is not null && !Text(profile.Identity.Email, 512)) || (profile.Identity.Name is not null && !Text(profile.Identity.Name, 512)))) throw Invalid();
            if (profile.Credentials is not null) ValidateCredentials(profile.Credentials);
            if (profile.PendingRefresh is not null)
            {
                var pending = profile.PendingRefresh;
                ValidateCredentials(pending.Credentials);
                if (profile.Status != "connected" || profile.Credentials is null || !Text(pending.Credentials.RefreshToken, 65536) || !ScopesValid(pending.Scopes) ||
                    pending.ReceivedAt <= 0 || pending.ReceivedAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000 ||
                    (pending.IdToken is not null && !Text(pending.IdToken, 65536))) throw Invalid();
            }
        }
        foreach (var pending in state.PendingRegistrations)
            if (pending is null || !ValidId(pending.Id) || !ids.Add(pending.Id) || !ValidClient(pending.ClientId) || !Text(pending.Label, 120) || !Text(pending.SavedAt, 100) ||
                (pending.ReplacesProfileId is not null && !state.Profiles.Any(p => p.Id == pending.ReplacesProfileId))) throw Invalid();
        if (state.ActiveProfileId is not null && !state.Profiles.Any(p => p.Id == state.ActiveProfileId)) throw Invalid();
        if (state.Welcomed.Any(id => !ValidId(id))) throw Invalid();
    }
    private static void ValidateCredentials(NativeAuthCredentials value)
    {
        if (!Text(value.AccessToken, 65536) || value.ExpiresAt <= 0 || value.ExpiresAt > 253402300799000L ||
            (value.RefreshToken is not null && !Text(value.RefreshToken, 65536)) ||
            value.EarliestRefreshAt.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Number or JsonValueKind.String)) throw Invalid();
    }
    internal static bool Text(string? value, int max) => !string.IsNullOrEmpty(value) && value.Length <= max && !value.Any(char.IsControl);
    internal static bool ValidClient(string? value) => value != "dynamic_agent_client" && Regex.IsMatch(value ?? "", "^[a-zA-Z0-9_-]{1,200}$");
    private static bool ValidId(string? value) => Regex.IsMatch(value ?? "", "^[a-zA-Z0-9-]{1,100}$");
    private static bool ScopesValid(List<string>? scopes) => scopes is not null && scopes.Count <= 50 && scopes.All(s => Text(s, 200));
    private static NativeChatException Invalid() => Error("storage_invalid", "Saved ChatGPT credentials could not be read safely. The original files have been preserved. Restore access to the saved credentials before reconnecting.");
    private static NativeChatException Error(string code, string message) => new(code, message);
}

internal sealed class NativeAuthMigrationLock : IDisposable
{
    private readonly string _path;
    private NativeAuthMigrationLock(string path) { _path = path; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);
    internal static NativeAuthMigrationLock Acquire(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new NativeChatException("storage_encryption_unavailable", "Secure Windows credential storage is unavailable.");
        if (!CreateDirectoryW(path, IntPtr.Zero))
            throw new NativeChatException("storage_busy", "The old companion is updating this connection. Close it before using the built-in ChatGPT connection.");
        return new(path);
    }
    public void Dispose()
    {
        // This directory was created by this instance. Never remove another process's lock or recurse.
        try { Directory.Delete(_path, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal static class NativeAuthDpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    internal static byte[] Protect(byte[] bytes) => Transform(bytes, true);
    internal static byte[] Unprotect(byte[] bytes) => Transform(bytes, false);
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        if (!OperatingSystem.IsWindows() || bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) throw Failure();
        Blob input = new() { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool success = encrypt ? CryptProtectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success || output.Length <= 0 || output.Length > 2 * 1024 * 1024) throw Failure();
            byte[] result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        catch (NativeChatException) { throw; }
        catch { throw Failure(); }
        finally
        {
            Wipe(input.Data, input.Length); Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { Wipe(output.Data, output.Length); LocalFree(output.Data); }
        }
    }
    private static void Wipe(IntPtr pointer, int length) { for (int i = 0; i < length; i++) Marshal.WriteByte(pointer, i, 0); }
    private static NativeChatException Failure() => new("storage_encryption_unavailable", "Secure Windows credential storage is unavailable. Existing credentials have been preserved.");
}
