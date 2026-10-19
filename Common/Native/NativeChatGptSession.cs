// C# adaptation of OpenAI Sign in with ChatGPT DevKit; see NOTICE.txt and SignInWithChatGPT-LICENSE.txt.
// Terraria ChatGPT 0.9.0: renamed app labels; existing credential storage retained (2026-10-19).
// Terraria GPT 0.8.0: native storage/OAuth, safe migration and refresh recovery (2026-10-19).
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Common.Native;

/// <summary>Owns OAuth inside tModLoader. No companion, child runtime, or API-key fallback.</summary>
public sealed class NativeChatGptSession : IDisposable
{
    private readonly NativeAuthStorage _store;
    private readonly NativeAuthProtocol _protocol;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Func<Uri, Task> _openBrowser;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _signInCancellation;
    private Task? _signIn;
    private string _message = "";
    private bool _isError;
    private bool _disposed;

    public NativeChatGptSession(string? storageDirectory = null, HttpClient? httpClient = null, Func<Uri, Task>? openBrowser = null)
    {
        _store = new NativeAuthStorage(storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerrariaGPT", "ChatGPT"));
        _http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = httpClient is null;
        _protocol = new NativeAuthProtocol(_http);
        _openBrowser = openBrowser ?? OpenBrowserAsync;
    }
    public async Task<ChatGptConnectionStatus> RequestAsync(ChatGptConnectionAction action, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        CancellationToken ct = linked.Token;
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfDisposed();
            if (action == ChatGptConnectionAction.ManageUsage)
                await _openBrowser(new Uri("https://chatgpt.com/settings/usage"));
            if (action == ChatGptConnectionAction.SignOut)
            {
                _signInCancellation?.Cancel();
                using var fileLock = await _store.LockAsync(ct);
                var state = _store.Read(); var profile = state.Active;
                bool confirmed = true;
                if (profile is not null)
                {
                    try
                    {
                        if (profile.Credentials is not null || profile.PendingRefresh is not null)
                            confirmed = await _protocol.RevokeAsync(await _protocol.DiscoverAsync(ct), profile, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch { confirmed = false; }
                    profile.Credentials = null; profile.PendingRefresh = null; profile.ProfileIdToken = null;
                    profile.RefreshUncertain = false; profile.Scopes = new(); profile.Status = "disconnected"; profile.SavedAt = Timestamp();
                    _store.Write(state);
                }
                _message = confirmed ? "Signed out of ChatGPT." : "Signed out locally. Remote disconnection could not be confirmed; use Manage usage to disconnect the app in ChatGPT settings.";
                _isError = !confirmed;
            }
            if (action == ChatGptConnectionAction.SignIn && (_signIn is null || _signIn.IsCompleted))
            {
                _message = "Continue in your browser to connect ChatGPT."; _isError = false;
                _signInCancellation?.Dispose();
                _signInCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _signInCancellation.CancelAfter(TimeSpan.FromMinutes(10));
                CancellationToken signInToken = _signInCancellation.Token;
                _signIn = Task.Run(() => SignInAsync(signInToken), CancellationToken.None);
            }
            using (var fileLock = await _store.LockAsync(ct))
            {
                var state = _store.Read();
                if (action == ChatGptConnectionAction.Welcome && state.Active is { Status: "connected" } active && active.Scopes.Contains(NativeAuthProtocol.PlanScope))
                {
                    if (!state.Welcomed.Contains(active.Id)) { state.Welcomed.Add(active.Id); _store.Write(state); }
                    _message = "Using ChatGPT plan"; _isError = false;
                }
                return Status(state);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        { return new(false, false, "", SafeMessage(error), false, false, true); }
        finally { _gate.Release(); }
    }
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        CancellationToken ct = linked.Token;
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfDisposed();
            if (_signIn is { IsCompleted: false }) throw new NativeChatException("sign_in_pending", "Finish ChatGPT sign-in in your browser first.");
            using var fileLock = await _store.LockAsync(ct);
            var state = _store.Read(); var profile = state.Active;
            if (profile is null || profile.Status != "connected" || profile.Credentials is null)
                throw new NativeChatException("not_connected", "Open mod settings and choose Continue with ChatGPT to connect your account.");
            if (!profile.Scopes.Contains(NativeAuthProtocol.PlanScope)) throw new NativeChatException("sharing_not_enabled", "Sign in again and allow ChatGPT plan usage to chat in Terraria.");
            if (!state.Welcomed.Contains(profile.Id)) throw new NativeChatException("welcome_required", "Open mod settings and confirm the ChatGPT plan welcome message with Got it.");
            if (profile.RefreshUncertain && profile.PendingRefresh is null)
                throw new NativeChatException("refresh_interrupted", "ChatGPT renewal was interrupted. Reconnect through Continue with ChatGPT; the earlier refresh token will not be reused.");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (profile.PendingRefresh is not null || profile.Credentials.ExpiresAt <= now + 60000)
            {
                if (profile.PendingRefresh is null && Earliest(profile.Credentials.EarliestRefreshAt) > now)
                {
                    if (profile.Credentials.ExpiresAt <= now) throw new NativeChatException("refresh_not_ready", "ChatGPT renewal is not ready yet. Try again shortly.");
                }
                else await RefreshAsync(state, profile, ct);
            }
            ct.ThrowIfCancellationRequested();
            if (!profile.Scopes.Contains(NativeAuthProtocol.PlanScope)) throw new NativeChatException("sharing_not_enabled", "ChatGPT plan usage is no longer enabled for this connection. Reconnect in mod settings.");
            if (profile.Credentials is null || profile.Credentials.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                throw new NativeChatException("refresh_not_ready", "The saved ChatGPT connection was recovered. Try the request again to renew it.");
            return profile.Credentials.AccessToken;
        }
        finally { _gate.Release(); }
    }
    private async Task RefreshAsync(NativeAuthState state, NativeAuthProfile profile, CancellationToken ct)
    {
        var endpoints = await _protocol.DiscoverAsync(ct);
        if (profile.PendingRefresh is null)
        {
            if (string.IsNullOrEmpty(profile.Credentials?.RefreshToken)) throw new NativeChatException("not_connected", "Your ChatGPT session needs to be renewed. Continue with ChatGPT in mod settings.");
            // Write before consuming a rotating grant. A crash or lost response cannot replay its predecessor.
            profile.RefreshUncertain = true; _store.Write(state);
            try
            {
                using var body = await _protocol.TokenAsync(endpoints, new Dictionary<string, string> {
                    ["grant_type"] = "refresh_token", ["client_id"] = profile.ClientId,
                    ["refresh_token"] = profile.Credentials.RefreshToken, ["resource"] = NativeAuthProtocol.Resource }, ct);
                profile.PendingRefresh = NativeAuthProtocol.ReadTokens(body.RootElement, profile.Scopes, true);
                _store.Write(state);
            }
            catch (NativeChatException error) when (error.Code == "invalid_grant")
            { profile.Status = "reauth_required"; profile.Credentials = null; profile.ProfileIdToken = null; profile.RefreshUncertain = false; profile.SavedAt = Timestamp(); _store.Write(state); throw; }
            catch (NativeChatException error) when (error.Code is "auth_rate_limited" or "refresh_not_ready" or "invalid_client")
            { profile.RefreshUncertain = false; _store.Write(state); throw; }
        }
        var pending = profile.PendingRefresh!;
        if (pending.IdToken is not null)
        {
            var identity = await _protocol.VerifyAsync(pending.IdToken, profile.ClientId, null, pending.ReceivedAt, endpoints, ct);
            if (identity.Subject != profile.Subject) throw new NativeChatException("account_mismatch", "The renewed ChatGPT account did not match. Reconnect to the original account in mod settings.");
            profile.Identity = identity.Identity; profile.ProfileIdToken = pending.IdToken;
        }
        profile.Credentials = pending.Credentials; profile.Scopes = pending.Scopes;
        profile.PendingRefresh = null; profile.RefreshUncertain = false; profile.SavedAt = Timestamp(); _store.Write(state);
    }
    private async Task SignInAsync(CancellationToken ct)
    {
        try
        {
            NativeAuthProfile? previous; string id, clientId, hostId; string? hint; string? tokenHint; bool upgrade;
            using (var fileLock = await _store.LockAsync(ct))
            {
                var state = _store.Read(); previous = state.Active;
                upgrade = previous?.RequiresNewRegistration == true;
                var pending = upgrade ? state.PendingRegistrations.FirstOrDefault(p => p.ReplacesProfileId == previous!.Id)
                    : previous is null ? state.PendingRegistrations.FirstOrDefault(p => p.ReplacesProfileId is null) : null;
                id = !upgrade && previous is not null ? previous.Id : pending?.Id ?? Guid.NewGuid().ToString();
                clientId = !upgrade && previous is not null ? previous.ClientId : pending?.ClientId ?? "dynamic_agent_client";
                hostId = state.HostId; hint = previous?.Identity?.Email; tokenHint = upgrade ? null : previous?.ProfileIdToken;
            }
            var endpoints = await _protocol.DiscoverAsync(ct);
            string nonce = RandomValue(), stateValue = RandomValue(), verifier = RandomValue();
            using var callback = new NativeAuthCallback();
            var fields = new Dictionary<string, string> {
                ["client_id"] = clientId, ["response_type"] = "code", ["redirect_uri"] = callback.RedirectUri,
                ["scope"] = NativeAuthProtocol.Scopes, ["resource"] = NativeAuthProtocol.Resource,
                ["state"] = stateValue, ["nonce"] = nonce, ["code_challenge_method"] = "S256",
                ["code_challenge"] = NativeAuthProtocol.Base64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                ["ext_agent_host_id"] = hostId };
            if (clientId == "dynamic_agent_client") fields["agent_name_hint"] = "Terraria ChatGPT";
            if (previous is not null && !previous.Scopes.Contains(NativeAuthProtocol.PlanScope)) fields["prompt"] = "consent";
            if (!string.IsNullOrEmpty(hint)) fields["login_hint"] = hint;
            if (!string.IsNullOrEmpty(tokenHint)) fields["id_token_hint"] = tokenHint;
            Uri authorization = new(endpoints.Authorization.AbsoluteUri + "?" + string.Join('&', fields.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
            // Listener exists before the system browser opens. No browser action occurs on normal game startup.
            Task<NativeAuthCallbackResult> callbackTask = callback.WaitAsync(stateValue, clientId == "dynamic_agent_client" ? null : clientId, ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                await _openBrowser(authorization);
                var result = await callbackTask;
                using (var fileLock = await _store.LockAsync(ct))
                {
                    var latest = _store.Read(); EnsureUnchanged(latest, previous);
                    if ((previous is null || upgrade) && !latest.PendingRegistrations.Any(p => p.Id == id))
                    { latest.PendingRegistrations.Add(new() { Id = id, ClientId = result.ClientId, Label = "Connection " + (latest.Profiles.Count + 1), SavedAt = Timestamp(), ReplacesProfileId = upgrade ? previous!.Id : null }); _store.Write(latest); }
                }
                using var tokens = await _protocol.TokenAsync(endpoints, new Dictionary<string, string> {
                    ["grant_type"] = "authorization_code", ["client_id"] = result.ClientId, ["code"] = result.Code,
                    ["code_verifier"] = verifier, ["redirect_uri"] = callback.RedirectUri, ["resource"] = NativeAuthProtocol.Resource }, ct);
                NativeAuthRotation received = NativeAuthProtocol.ReadTokens(tokens.RootElement, null, false);
                if (received.IdToken is null) throw new NativeChatException("invalid_id_token", "ChatGPT did not return a verifiable identity. Try signing in again.");
                var identity = await _protocol.VerifyAsync(received.IdToken, result.ClientId, nonce, received.ReceivedAt, endpoints, ct);
                if (previous?.Subject is not null && previous.Subject != identity.Subject) throw new NativeChatException("account_mismatch", "Sign-in returned a different ChatGPT account. Reconnect to the original account; the saved registration was preserved.");
                await _gate.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    using var fileLock = await _store.LockAsync(ct);
                    var latest = _store.Read(); EnsureUnchanged(latest, previous);
                    var profile = new NativeAuthProfile { Id = id, Label = !upgrade && previous is not null ? previous.Label : "Connection " + (latest.Profiles.Count + 1), ClientId = result.ClientId,
                        Status = "connected", SavedAt = Timestamp(), Subject = identity.Subject, Identity = identity.Identity,
                        ProfileIdToken = received.IdToken, Credentials = received.Credentials.AccessToken.Length == 0 ? null : received.Credentials, Scopes = received.Scopes };
                    latest.Profiles.RemoveAll(p => p.Id == id); latest.Profiles.Add(profile);
                    latest.PendingRegistrations.RemoveAll(p => p.Id == id); latest.ActiveProfileId = id; _store.Write(latest);
                    _message = received.Scopes.Contains(NativeAuthProtocol.PlanScope) ? "Using ChatGPT plan" : "Signed in. Allow ChatGPT plan usage to chat in Terraria.";
                    _isError = false;
                }
                finally { _gate.Release(); }
            }
            finally
            {
                callback.Dispose();
                // Observe a callback fault even if browser opening fails first.
                _ = callbackTask.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (OperationCanceledException) { if (!_disposed) { _message = "ChatGPT sign-in was cancelled or timed out. You can try again."; _isError = false; } }
        catch (Exception error) { if (!_disposed) { _message = SafeMessage(error); _isError = true; } }
    }
    private static void EnsureUnchanged(NativeAuthState latest, NativeAuthProfile? previous)
    {
        if (previous is null ? latest.Active is not null : latest.Active is not { } active || active.Id != previous.Id || active.SavedAt != previous.SavedAt)
            throw new NativeChatException("connection_changed", "The saved ChatGPT connection changed during sign-in. Try again.");
    }
    private ChatGptConnectionStatus Status(NativeAuthState state)
    {
        var profile = state.Active; bool connected = profile?.Status == "connected";
        bool pendingVerification = profile?.PendingRefresh is not null;
        bool interrupted = profile?.RefreshUncertain == true && !pendingVerification;
        bool plan = connected && profile!.Credentials is not null && profile.Scopes.Contains(NativeAuthProtocol.PlanScope) && !pendingVerification && !interrupted;
        bool signing = _signIn is { IsCompleted: false };
        string label = connected ? profile!.Identity?.Email ?? profile.Identity?.Name ?? profile.Label : "";
        string message = _message.Length != 0 ? _message : connected ? plan ? "Using ChatGPT plan" : "Sign in again and allow ChatGPT plan usage." : "Continue with ChatGPT to connect your account.";
        if (!signing)
        {
            if (interrupted) message = "ChatGPT renewal was interrupted. Continue with ChatGPT to reconnect safely.";
            else if (pendingVerification) message = "The renewed ChatGPT identity still needs verification. Try your question again to resume safely.";
            else if (profile?.Status == "reauth_required") message = "Your ChatGPT session has expired. Continue with ChatGPT to reconnect.";
        }
        return new(connected, signing, new string(label.Where(c => !char.IsControl(c)).Take(120).ToArray()), message, plan,
            plan && !state.Welcomed.Contains(profile!.Id), _isError || interrupted || profile?.Status == "reauth_required");
    }
    private static long Earliest(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double seconds) && double.IsFinite(seconds) && seconds > 0 && seconds < 253402300799) return (long)(seconds * 1000);
        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return date.ToUnixTimeMilliseconds();
        return 0;
    }
    private static string Timestamp() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private static string RandomValue() => NativeAuthProtocol.Base64(RandomNumberGenerator.GetBytes(32));
    private static string SafeMessage(Exception error) => error is NativeChatException known ? known.Message : "The ChatGPT connection could not be completed. Your saved credentials have been preserved. Try again.";
    private static Task OpenBrowserAsync(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); return Task.CompletedTask; }
        catch { throw new NativeChatException("browser_unavailable", "The default browser could not be opened. Check your browser settings and try again."); }
    }
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(NativeChatGptSession)); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _signInCancellation?.Cancel(); _lifetime.Cancel();
        if (_ownsHttp) _http.Dispose();
        // Semaphores and cancellation sources remain valid until outstanding operations observe cancellation.
    }
}
