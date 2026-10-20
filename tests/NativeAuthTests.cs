#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerrariaChatGPT.Common.Native;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Tests;

public static class NativeAuthTests
{
    public static string[] Run(string root) => RunAsync(root).GetAwaiter().GetResult();
    private static readonly List<string> Passed = new();
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Passed.Add(label); }
    private static async Task Fails(Func<Task> operation, string expected, string label)
    {
        try { await operation(); }
        catch (NativeChatException error) when (error.Code == expected) { Passed.Add(label); return; }
        throw new Exception(label);
    }
    private static async Task<string[]> RunAsync(string root)
    {
        Passed.Clear();
        byte[] plain = Encoding.UTF8.GetBytes("synthetic fixture only");
        byte[] encrypted = NativeAuthDpapi.Protect(plain);
        Check(!encrypted.SequenceEqual(plain) && NativeAuthDpapi.Unprotect(encrypted).SequenceEqual(plain), "Windows DPAPI encrypts and decrypts in-process");
        encrypted[encrypted.Length / 2] ^= 1;
        await Fails(() => Task.Run(() => NativeAuthDpapi.Unprotect(encrypted)), "storage_encryption_unavailable", "Corrupt ciphertext is rejected without plaintext fallback");
        Check(NativeAuthCallback.TryParse("GET /auth/callback?state=secret-state&code=fake&client_id=oaiapp_test HTTP/1.1\r\nHost: 127.0.0.1:1234\r\n\r\n", "http://127.0.0.1:1234/auth/callback", "secret-state", out _), "Exact loopback callback accepted");
        foreach (string suffix in new[] { "state=bad&code=fake", "state=secret-state&state=secret-state&code=fake", "state=secret-state&code=fake&code=other" })
            Check(!NativeAuthCallback.TryParse("GET /auth/callback?" + suffix + " HTTP/1.1\r\nHost: 127.0.0.1:1234\r\n\r\n", "http://127.0.0.1:1234/auth/callback", "secret-state", out _), "Wrong or duplicate callback parameters rejected");
        Check(!NativeAuthCallback.TryParse("GET /auth/callback?state=secret-state HTTP/1.1\r\nHost: evil.example\r\n\r\n", "http://127.0.0.1:1234/auth/callback", "secret-state", out _), "Callback host mismatch rejected");
        Check(!NativeAuthCallback.TryParse("GET /callback?state=secret-state HTTP/1.1\r\nHost: 127.0.0.1:1234\r\n\r\n", "http://127.0.0.1:1234/auth/callback", "secret-state", out _), "Different callback path rejected");
        foreach (string endpoint in new[] { "http://auth.openai.com/authorize", "https://evil.example/authorize", "https://auth.openai.com:444/authorize",
            "https://user@auth.openai.com/authorize", "https://auth.openai.com/authorize?login_hint=private", "https://auth.openai.com/authorize#token" })
        {
            bool rejected = false;
            try { NativeAuthProtocol.SafeEndpoint(endpoint); }
            catch (FormatException) { rejected = true; }
            Check(rejected, "Unsafe or pre-filled discovery endpoint rejected");
        }

        using RSA rsa = RSA.Create(2048);
        using var handler = new FixtureHandler(rsa);
        using var http = new HttpClient(handler);
        await CheckLazyStorage(root, http);
        var protocol = new NativeAuthProtocol(http);
        var endpoints = await protocol.DiscoverAsync(CancellationToken.None);
        var accessOnly = Profile(); accessOnly.Credentials!.RefreshToken = null;
        Check(!await protocol.RevokeAsync(endpoints, accessOnly, CancellationToken.None), "Access-only session cannot falsely confirm remote revocation");
        long received = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var valid = await protocol.VerifyAsync(handler.Jwt("oaiapp_test", "subject-test", "test-nonce"), "oaiapp_test", "test-nonce", received, endpoints, CancellationToken.None);
        Check(valid.Subject == "subject-test", "Signed RSA identity validated against issuer audience expiration nonce and subject");
        foreach (var scenario in new[] { "issuer", "audience", "nonce", "expiry", "future", "azp", "algorithm", "signature", "duplicate" })
        {
            string token = handler.Jwt("oaiapp_test", "subject-test", "test-nonce", scenario);
            await Fails(() => protocol.VerifyAsync(token, "oaiapp_test", "test-nonce", received, endpoints, CancellationToken.None), "invalid_id_token", "Invalid JWT " + scenario + " rejected");
        }
        handler.KeysUnavailable = true;
        await Fails(() => protocol.VerifyAsync(handler.Jwt("oaiapp_test", "subject-test", null), "oaiapp_test", null, received, endpoints, CancellationToken.None), "identity_verification_unavailable", "JWKS outage distinguished from invalid identity");
        handler.KeysUnavailable = false;

        string directory = Path.Combine(root, ".build", "native-auth-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new NativeAuthStorage(directory);
        NativeAuthState state;
        using (var locked = await store.LockAsync(CancellationToken.None))
        {
            state = store.Read(); state.Profiles.Add(Profile()); state.ActiveProfileId = "profile-test"; state.Welcomed.Add("profile-test"); store.Write(state);
        }
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "native-auth.dpapi"))).Contains("test-access", StringComparison.Ordinal), "Native credential file does not contain plaintext tokens");
        using (var session = new NativeChatGptSession(directory, http))
        {
            var status = await session.RequestAsync(ChatGptConnectionAction.Status, CancellationToken.None);
            Check(status.Connected && status.UsingPlan && !status.WelcomePending && status.ClipboardLink.Length == 0 && !status.CanCopySignInLink, "Startup restores acknowledged account without exposing sign-in links");
            var usage = await session.RequestAsync(ChatGptConnectionAction.CopyUsageLink, CancellationToken.None);
            Check(usage.ClipboardLink == "https://chatgpt.com/settings/usage", "Explicit usage click returns the fixed public settings link");
            Check((await session.RequestAsync(ChatGptConnectionAction.Status, CancellationToken.None)).ClipboardLink.Length == 0, "Copied links never persist in ordinary status responses");
            Check(await session.GetAccessTokenAsync(CancellationToken.None) == "test-access", "Unexpired token reused without refresh");
            using (var locked = await store.LockAsync(CancellationToken.None)) { state = store.Read(); state.Active!.Credentials!.ExpiresAt = 1; store.Write(state); }
            handler.RefreshCount = 0;
            Check(await session.GetAccessTokenAsync(CancellationToken.None) == "successor-access", "Refresh replaces expiring token");
            Check(handler.RefreshCount == 1 && handler.LastRefreshToken == "test-refresh", "One refresh consumes original token exactly once");
            using (var locked = await store.LockAsync(CancellationToken.None))
            { state = store.Read(); Check(state.Active!.Credentials!.RefreshToken == "successor-refresh" && state.Active.PendingRefresh is null && !state.Active.RefreshUncertain, "Successful rotation durably stores successor and clears checkpoint"); state.Active.Credentials.ExpiresAt = 1; store.Write(state); }
            handler.KeysUnavailable = true;
            await Fails(() => session.GetAccessTokenAsync(CancellationToken.None), "identity_verification_unavailable", "Refresh JWKS outage preserves received rotation");
            var verifying = await session.RequestAsync(ChatGptConnectionAction.Status, CancellationToken.None);
            Check(!verifying.UsingPlan && verifying.Message.Contains("verification", StringComparison.Ordinal), "Pending identity verification is not advertised as ready plan usage");
        }
        handler.KeysUnavailable = false;
        int previousCount = handler.RefreshCount;
        using (var restored = new NativeChatGptSession(directory, http))
        {
            Check(await restored.GetAccessTokenAsync(CancellationToken.None) == "successor-access" && handler.RefreshCount == previousCount, "Restart verifies checkpoint without reusing consumed refresh token");
            using (var locked = await store.LockAsync(CancellationToken.None))
            { state = store.Read(); state.Active!.Credentials!.ExpiresAt = 1; store.Write(state); }
            handler.RejectRefresh = "rate";
            await Fails(() => restored.GetAccessTokenAsync(CancellationToken.None), "auth_rate_limited", "Explicit OAuth rate limit leaves connection recoverable");
            using (var locked = await store.LockAsync(CancellationToken.None))
            { state = store.Read(); Check(!state.Active!.RefreshUncertain && state.Active.Credentials is not null, "Rejected refresh does not poison rotation state"); }
            handler.RejectRefresh = "early";
            await Fails(() => restored.GetAccessTokenAsync(CancellationToken.None), "refresh_not_ready", "Explicit not-before response remains retryable");
            handler.RejectRefresh = null;
            handler.OmitRefreshScope = true;
            Check(await restored.GetAccessTokenAsync(CancellationToken.None) == "successor-access", "Scope omitted on refresh retains previous grant");
            handler.OmitRefreshScope = false;
            using (var locked = await store.LockAsync(CancellationToken.None))
            { state = store.Read(); state.Active!.Credentials!.ExpiresAt = 1; store.Write(state); }
            handler.LoseResponse = true;
            await Fails(() => restored.GetAccessTokenAsync(CancellationToken.None), "network_error", "Lost refresh response reported safely");
            handler.LoseResponse = false;
            int afterLost = handler.RefreshCount;
            await Fails(() => restored.GetAccessTokenAsync(CancellationToken.None), "refresh_interrupted", "Ambiguous rotation never replays predecessor");
            Check(afterLost == handler.RefreshCount, "Ambiguous rotation blocked before network call");
            var interrupted = await restored.RequestAsync(ChatGptConnectionAction.Status, CancellationToken.None);
            Check(!interrupted.UsingPlan && interrupted.IsError && interrupted.Message.Contains("reconnect", StringComparison.Ordinal), "Interrupted renewal status gives reconnect instruction without claiming readiness");
            var outStatus = await restored.RequestAsync(ChatGptConnectionAction.SignOut, CancellationToken.None);
            Check(!outStatus.Connected && !outStatus.UsingPlan && handler.Revocations == 1, "Explicit signout revokes refresh grant and clears local tokens");
        }
        using (var locked = await store.LockAsync(CancellationToken.None))
        { state = store.Read(); Check(state.Active!.Credentials is null && state.Active.ProfileIdToken is null && state.Active.ClientId == "oaiapp_test", "Signout preserves registration while deleting credentials and ID-token hint"); }
        string native = Path.Combine(directory, "native-auth.dpapi");
        byte[] original = File.ReadAllBytes(native); File.WriteAllBytes(native, new byte[] { 1, 2, 3 });
        await Fails(() => Task.Run(() => store.Read()), "storage_encryption_unavailable", "Malformed native file refuses legacy fallback");
        Check(File.ReadAllBytes(native).SequenceEqual(new byte[] { 1, 2, 3 }), "Unreadable credential file remains untouched");
        File.Delete(native);
        await Fails(() => Task.Run(() => store.Read()), "storage_missing", "Initialization marker blocks stale credential reimport after native loss");
        File.WriteAllBytes(native, original);
        using (var exclusive = new FileStream(native, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Fails(() => Task.Run(() => store.Write(state)), "storage_error", "Failed atomic credential replacement produces safe storage error");
            Check(File.ReadAllBytes(native).SequenceEqual(original), "Failed save preserves previously encrypted credentials");
        }
        await CheckMigration(root);
        await CheckInteractive(root, handler, http);
        await CheckInteractive(root, handler, http, true);
        await CheckCancellation(root, http);
        await CheckTerminalRefresh(root, handler, http);
        using (var identityOnly = JsonDocument.Parse("{\"scope\":\"openid profile email\",\"id_token\":\"synthetic-signed-id-token\"}"))
            Check(NativeAuthProtocol.ReadTokens(identityOnly.RootElement, null, false).Credentials.AccessToken.Length == 0, "Identity-only sign-in does not fabricate plan credentials");
        return Passed.ToArray();
    }
    private static NativeAuthProfile Profile() => new() { Id = "profile-test", Label = "Test account", ClientId = "oaiapp_test", Status = "connected", SavedAt = DateTimeOffset.UtcNow.ToString("O"), Subject = "subject-test",
        Identity = new() { Email = "fixture@example.invalid" }, Scopes = new() { "openid", "offline_access", "chatgpt.tokens.use.direct" },
        Credentials = new() { AccessToken = "test-access", RefreshToken = "test-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() } };
    private static async Task CheckLazyStorage(string root, HttpClient http)
    {
        string previous = Terraria.Main.SavePath;
        try
        {
            Terraria.Main.SavePath = "";
            using var session = new NativeChatGptSession(httpClient: http);
            var status = await session.RequestAsync(ChatGptConnectionAction.Status, CancellationToken.None);
            Check(!status.Connected && status.IsError && status.ClipboardLink.Length == 0 && status.Account.Length == 0,
                "Missing save path does not fail session construction or throw from status");
            Check(!status.Message.Contains(root, StringComparison.OrdinalIgnoreCase) && !status.Message.Contains("C:", StringComparison.OrdinalIgnoreCase),
                "Unavailable save-path status contains no filesystem paths");
            await Fails(() => session.GetAccessTokenAsync(CancellationToken.None), "storage_unavailable", "Missing save path fails inference safely instead of accessing another location");
            Terraria.Main.SavePath = "private-invalid-location";
            status = await session.RequestAsync(ChatGptConnectionAction.Status, CancellationToken.None);
            Check(status.IsError && !status.Message.Contains("private-invalid-location", StringComparison.Ordinal), "Invalid save-path values never appear in connection errors");
            string save = Path.Combine(root, ".build", "native-auth-lazy-" + Guid.NewGuid().ToString("N"));
            Terraria.Main.SavePath = save;
            var statuses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => session.RequestAsync(ChatGptConnectionAction.Status, CancellationToken.None)));
            Check(statuses.All(value => !value.IsError && !value.Connected) && File.Exists(Path.Combine(save, "TerrariaChatGPT", "native-auth.dpapi")),
                "Session recovers safely when the active save path becomes available, including concurrent status requests");
        }
        finally { Terraria.Main.SavePath = previous; }
    }
    private static async Task CheckMigration(string root)
    {
        string directory = Path.Combine(root, ".build", "native-auth-no-legacy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        const string legacy = "deliberately invalid obsolete credentials";
        File.WriteAllText(Path.Combine(directory, "chatgpt-auth.json"), legacy);
        File.WriteAllText(Path.Combine(directory, "chatgpt-host.json"), legacy);
        File.WriteAllText(Path.Combine(directory, "welcome.json"), legacy);
        var store = new NativeAuthStorage(directory);
        Directory.CreateDirectory(Path.Combine(directory, ".chatgpt-auth.lock"));
        using var locked = await store.LockAsync(CancellationToken.None);
        var state = store.Read();
        Check(state.Active is null && state.Profiles.Count == 0 && state.HostId.StartsWith("urn:uuid:", StringComparison.Ordinal), "Runtime ignores obsolete credentials host welcome and migration locks");
        Check(File.ReadAllText(Path.Combine(directory, "chatgpt-auth.json")) == legacy, "Runtime leaves obsolete credential files untouched");
        Check(store.Read().HostId == state.HostId, "Fresh encrypted host identity persists across reads");
    }
    private static async Task CheckInteractive(string root, FixtureHandler handler, HttpClient http, bool upgrade = false)
    {
        string directory = Path.Combine(root, ".build", "native-auth-login-" + Guid.NewGuid().ToString("N"));
        var store = new NativeAuthStorage(directory);
        if (upgrade)
        {
            using var locked = await store.LockAsync(CancellationToken.None); var saved = store.Read();
            var old = Profile(); old.RequiresNewRegistration = true; old.Status = "reauth_required"; old.Credentials = null;
            saved.Profiles.Add(old); saved.ActiveProfileId = old.Id; store.Write(saved);
        }
        using var session = new NativeChatGptSession(directory, http);
        Uri authorization = await CompleteInteractive(session, handler);
        ChatGptConnectionStatus current;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        do { await Task.Delay(30, timeout.Token); current = await session.RequestAsync(ChatGptConnectionAction.Status, timeout.Token); } while (current.SigningIn);
        Check(current.Connected && current.WelcomePending && current.UsingPlan, "Browser PKCE callback signs in but requires welcome acknowledgement: " + current.Message);
        Check(authorization.Host == "auth.openai.com" && authorization.Query.Contains("dynamic_agent_client") && authorization.Query.Contains("ext_agent_host_id"), "First registration uses official authorize endpoint and persistent host identifier");
        Check(handler.VerifierMatched, "Authorization code exchange uses exact callback URI and PKCE verifier");
        await Fails(() => session.GetAccessTokenAsync(CancellationToken.None), "welcome_required", "Plan inference blocked before explicit welcome acknowledgement");
        await session.RequestAsync(ChatGptConnectionAction.Welcome, CancellationToken.None);
        Check(await session.GetAccessTokenAsync(CancellationToken.None) == "successor-access", "Welcome acknowledgement enables saved plan access");
        if (upgrade)
        {
            using var locked = await store.LockAsync(CancellationToken.None); var saved = store.Read();
            Check(saved.Profiles.Count == 2 && saved.Active!.Id != "profile-test" && saved.Profiles.Single(p => p.Id == "profile-test").RequiresNewRegistration, "Old localhost registration can reconnect while preserving original profile");
        }
        string priorHost = authorization.Query.Split('&').Single(p => p.StartsWith("ext_agent_host_id=", StringComparison.Ordinal));
        authorization = await CompleteInteractive(session, handler);
        do { await Task.Delay(30, timeout.Token); current = await session.RequestAsync(ChatGptConnectionAction.Status, timeout.Token); } while (current.SigningIn);
        Check(current.Connected && authorization is not null && !authorization.Query.Contains("dynamic_agent_client", StringComparison.Ordinal)
            && !authorization.Query.Contains("id_token_hint=", StringComparison.Ordinal) && !authorization.Query.Contains("login_hint=", StringComparison.Ordinal)
            && !authorization.Query.Contains("agent_name_hint=", StringComparison.Ordinal)
            && authorization.Query.Split('&').Single(p => p.StartsWith("ext_agent_host_id=", StringComparison.Ordinal)) == priorHost,
            "Returning authorization reuses registration without exposing identity-token or account hints");
        var completed = await session.RequestAsync(ChatGptConnectionAction.CopySignInLink, CancellationToken.None);
        Check(!completed.CanCopySignInLink && completed.ClipboardLink.Length == 0, "Completed sign-in no longer exposes its temporary link");
    }
    private static async Task<Uri> PrepareInteractive(NativeChatGptSession session)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await session.RequestAsync(ChatGptConnectionAction.SignIn, timeout.Token);
        ChatGptConnectionStatus current;
        bool statusContainsNoLinks = true;
        do
        {
            await Task.Delay(30, timeout.Token);
            current = await session.RequestAsync(ChatGptConnectionAction.Status, timeout.Token);
            statusContainsNoLinks &= current.ClipboardLink.Length == 0;
            if (!current.SigningIn) throw new Exception("Sign-in preparation unexpectedly stopped: " + current.Message);
        } while (!current.CanCopySignInLink);
        Check(statusContainsNoLinks, "Status polling never exposes the pending authorization URL");
        var copy = await session.RequestAsync(ChatGptConnectionAction.CopySignInLink, timeout.Token);
        var uri = new Uri(copy.ClipboardLink);
        var query = Query(uri);
        Check(uri.Scheme == "https" && uri.Host == "auth.openai.com" && uri.IsDefaultPort && uri.UserInfo.Length == 0, "Sign-in copy returns only the verified OpenAI HTTPS origin");
        Check(!query.ContainsKey("login_hint") && !query.ContainsKey("id_token_hint") && !query.ContainsKey("code_verifier")
            && !query.ContainsKey("access_token") && !query.ContainsKey("refresh_token"), "Copied authorization URL excludes email hints, identity tokens and PKCE verifier");
        Check(query["code_challenge_method"] == "S256" && query["state"].Length >= 43 && query["nonce"].Length >= 43, "Manual browser flow retains PKCE, unpredictable state and nonce");
        return uri;
    }
    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
        .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
    private static async Task<Uri> CompleteInteractive(NativeChatGptSession session, FixtureHandler handler)
    {
        Uri authorization = await PrepareInteractive(session);
        var query = Query(authorization);
        handler.AuthorizationNonce = query["nonce"];
        handler.ExpectedVerifierChallenge = query["code_challenge"];
        handler.ExpectedRedirect = query["redirect_uri"];
        using var callbackHttp = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var wrong = await callbackHttp.GetAsync(query["redirect_uri"] + "?state=wrong&code=wrong&client_id=oaiapp_test");
        Check(wrong.StatusCode == HttpStatusCode.BadRequest, "Unrelated wrong-state request does not consume sign-in transaction");
        using var result = await callbackHttp.GetAsync(query["redirect_uri"] + "?state=" + Uri.EscapeDataString(query["state"]) + "&code=fake-code&client_id=oaiapp_test");
        string response = await result.Content.ReadAsStringAsync();
        Check(result.IsSuccessStatusCode && result.Headers.CacheControl?.NoStore == true && response.Contains("history.replaceState", StringComparison.Ordinal)
            && !response.Contains("fake-code", StringComparison.Ordinal), "Callback disables caching and scrubs address bar without echoing the authorization code");
        return authorization;
    }
    private static async Task CheckCancellation(string root, HttpClient http)
    {
        string directory = Path.Combine(root, ".build", "native-auth-cancel-" + Guid.NewGuid().ToString("N"));
        var store = new NativeAuthStorage(directory);
        using (var locked = await store.LockAsync(CancellationToken.None))
        { var state = store.Read(); state.Profiles.Add(Profile()); state.ActiveProfileId = "profile-test"; state.Welcomed.Add("profile-test"); store.Write(state); }
        using var session = new NativeChatGptSession(directory, http);
        Uri authorization = await PrepareInteractive(session);
        var cancelled = await session.RequestAsync(ChatGptConnectionAction.CancelSignIn, CancellationToken.None);
        Check(cancelled.Connected && !cancelled.SigningIn && !cancelled.CanCopySignInLink && cancelled.ClipboardLink.Length == 0, "Cancel sign-in immediately removes the link and preserves existing connection");
        await Task.Delay(100);
        Check((await session.RequestAsync(ChatGptConnectionAction.CopySignInLink, CancellationToken.None)).ClipboardLink.Length == 0, "Cancelled sign-in cannot copy a stale URL");
        Check(await session.GetAccessTokenAsync(CancellationToken.None) == "test-access", "Cancelling a reconnect keeps the saved usable credentials");
        using var callbackHttp = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
        bool refused = false;
        try { using var response = await callbackHttp.GetAsync(Query(authorization)["redirect_uri"] + "?state=late&code=fake"); }
        catch (HttpRequestException) { refused = true; }
        catch (OperationCanceledException) { refused = true; }
        Check(refused, "Cancelled listener no longer accepts local callbacks");
    }
    private static async Task CheckTerminalRefresh(string root, FixtureHandler handler, HttpClient http)
    {
        string directory = Path.Combine(root, ".build", "native-auth-terminal-" + Guid.NewGuid().ToString("N"));
        var store = new NativeAuthStorage(directory);
        using (var locked = await store.LockAsync(CancellationToken.None))
        { var state = store.Read(); var profile = Profile(); profile.Credentials!.ExpiresAt = 1; state.Profiles.Add(profile); state.ActiveProfileId = profile.Id; state.Welcomed.Add(profile.Id); store.Write(state); }
        using var session = new NativeChatGptSession(directory, http);
        handler.RejectRefresh = "invalid";
        await Fails(() => session.GetAccessTokenAsync(CancellationToken.None), "invalid_grant", "Terminal refresh rejection requests reauthorization");
        handler.RejectRefresh = null;
        var status = await session.RequestAsync(ChatGptConnectionAction.Status, CancellationToken.None);
        Check(!status.Connected && !status.UsingPlan && status.IsError && status.Message.Contains("reconnect", StringComparison.Ordinal), "Expired session status is actionable after restart");
        using var checkLock = await store.LockAsync(CancellationToken.None); var saved = store.Read();
        Check(saved.Active!.Credentials is null && saved.Active.ClientId == "oaiapp_test", "Terminal refresh clears unusable tokens while preserving registration");
    }
    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly RSA _rsa;
        internal bool KeysUnavailable, LoseResponse, VerifierMatched, OmitRefreshScope;
        internal int RefreshCount, Revocations;
        internal string? LastRefreshToken, AuthorizationNonce, ExpectedVerifierChallenge, ExpectedRedirect, RejectRefresh;
        internal FixtureHandler(RSA rsa) { _rsa = rsa; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/.well-known/openid-configuration") return Json(new { issuer = NativeAuthProtocol.Issuer, authorization_endpoint = NativeAuthProtocol.Issuer + "/api/accounts/authorize", token_endpoint = NativeAuthProtocol.Issuer + "/api/accounts/oauth/token", jwks_uri = NativeAuthProtocol.Issuer + "/jwks", revocation_endpoint = NativeAuthProtocol.Issuer + "/revoke" });
            if (path == "/jwks")
            {
                if (KeysUnavailable) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
                var key = _rsa.ExportParameters(false); return Json(new { keys = new[] { new { kty = "RSA", kid = "fixture-key", use = "sig", alg = "RS256", n = NativeAuthProtocol.Base64(key.Modulus!), e = NativeAuthProtocol.Base64(key.Exponent!) } } });
            }
            if (path == "/revoke") { Revocations++; return new HttpResponseMessage(HttpStatusCode.OK); }
            if (path == "/api/accounts/oauth/token")
            {
                string form = await request.Content!.ReadAsStringAsync(ct);
                var values = form.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
                bool refresh = values["grant_type"] == "refresh_token";
                if (refresh)
                {
                    RefreshCount++; LastRefreshToken = values["refresh_token"]; if (LoseResponse) throw new HttpRequestException("fixture private secret never displayed");
                    if (RejectRefresh == "rate") return new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
                    if (RejectRefresh is "early" or "invalid") return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(JsonSerializer.Serialize(new { error = RejectRefresh == "early" ? "refresh_not_ready" : "invalid_grant" })) };
                }
                else VerifierMatched = values["redirect_uri"] == ExpectedRedirect && NativeAuthProtocol.Base64(SHA256.HashData(Encoding.ASCII.GetBytes(values["code_verifier"]))) == ExpectedVerifierChallenge;
                if (refresh && OmitRefreshScope) return Json(new { access_token = "successor-access", refresh_token = "successor-refresh", token_type = "Bearer", expires_in = 3600, id_token = Jwt("oaiapp_test", "subject-test", null) });
                return Json(new { access_token = "successor-access", refresh_token = "successor-refresh", token_type = "Bearer", expires_in = 3600, scope = "openid offline_access chatgpt.tokens.use.direct", id_token = Jwt("oaiapp_test", "subject-test", refresh ? null : AuthorizationNonce) });
            }
            throw new Exception("Unexpected test endpoint");
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        internal string Jwt(string audience, string subject, string? nonce, string? invalid = null)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string header = JsonSerializer.Serialize(new { alg = invalid == "algorithm" ? "HS256" : "RS256", kid = "fixture-key" });
            var claims = new Dictionary<string, object?> { ["iss"] = invalid == "issuer" ? "https://evil.example" : NativeAuthProtocol.Issuer, ["aud"] = invalid == "audience" ? "other" : audience, ["sub"] = subject,
                ["iat"] = invalid == "future" ? now + 1000 : now, ["exp"] = invalid == "expiry" ? now - 100 : now + 3600, ["nonce"] = invalid == "nonce" ? "other" : nonce, ["email"] = "fixture@example.invalid" };
            if (invalid == "azp") claims["azp"] = "other";
            string payload = JsonSerializer.Serialize(claims);
            if (invalid == "duplicate") payload = payload[..^1] + ",\"sub\":\"other\"}";
            string data = NativeAuthProtocol.Base64(Encoding.UTF8.GetBytes(header)) + "." + NativeAuthProtocol.Base64(Encoding.UTF8.GetBytes(payload));
            byte[] signature = _rsa.SignData(Encoding.ASCII.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (invalid == "signature") signature[0] ^= 1;
            return data + "." + NativeAuthProtocol.Base64(signature);
        }
    }
}
