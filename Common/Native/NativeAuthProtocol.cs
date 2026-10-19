// C# adaptation of OpenAI Sign in with ChatGPT DevKit; see NOTICE.txt and SignInWithChatGPT-LICENSE.txt.
// Terraria GPT 0.8.0: native storage/OAuth, safe migration and refresh recovery (2026-10-19).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerrariaGPT.Common.Native;

internal sealed record NativeAuthEndpoints(Uri Authorization, Uri Token, Uri Jwks, Uri? Revoke);
internal sealed record NativeVerifiedIdentity(string Subject, NativeAuthIdentity Identity);
internal sealed class NativeAuthProtocol
{
    internal const string Issuer = "https://auth.openai.com";
    internal const string Resource = "https://api.openai.com/v1";
    internal const string PlanScope = "chatgpt.tokens.use.direct";
    internal const string Scopes = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    private readonly HttpClient _http;
    internal NativeAuthProtocol(HttpClient http) { _http = http; }
    internal async Task<NativeAuthEndpoints> DiscoverAsync(CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Issuer + "/.well-known/openid-configuration");
        using JsonDocument body = await SendJsonAsync(request, cancellation);
        try
        {
            var root = body.RootElement;
            if (root.GetProperty("issuer").GetString() != Issuer) throw new FormatException();
            Uri Endpoint(string name) => SafeEndpoint(root.GetProperty(name).GetString());
            return new(Endpoint("authorization_endpoint"), Endpoint("token_endpoint"), Endpoint("jwks_uri"),
                root.TryGetProperty("revocation_endpoint", out _) ? Endpoint("revocation_endpoint") : null);
        }
        catch { throw new NativeChatException("discovery_failed", "ChatGPT sign-in configuration could not be verified. Try again shortly."); }
    }
    internal static Uri SafeEndpoint(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "auth.openai.com" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw new FormatException();
        return uri;
    }
    internal Task<JsonDocument> TokenAsync(NativeAuthEndpoints endpoints, Dictionary<string, string> fields, CancellationToken cancellation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoints.Token) { Content = new FormUrlEncodedContent(fields) };
        return SendOwnedAsync(request, cancellation);
    }
    private async Task<JsonDocument> SendOwnedAsync(HttpRequestMessage request, CancellationToken cancellation)
    { using (request) return await SendJsonAsync(request, cancellation); }
    internal async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400) throw new NativeChatException("request_failed", "ChatGPT returned an unexpected redirect. Your credentials were preserved.");
            // A definite rate-limit rejection has not issued replacement credentials.
            if ((int)response.StatusCode == 429) throw new NativeChatException("auth_rate_limited", "ChatGPT sign-in requests are temporarily rate limited. Wait a little and try again.");
            if (response.Content.Headers.ContentLength > 512 * 1024) throw new NativeChatException("invalid_response", "ChatGPT returned an unusable authentication response.");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream(); byte[] block = new byte[4096]; int count;
            while ((count = await stream.ReadAsync(block, timeout.Token)) > 0)
            { if (buffer.Length + count > 512 * 1024) throw new NativeChatException("invalid_response", "ChatGPT returned an unusable authentication response."); buffer.Write(block, 0, count); }
            JsonDocument document;
            try { document = JsonDocument.Parse(buffer.ToArray()); }
            catch (JsonException) { throw new NativeChatException("invalid_response", "ChatGPT returned an unusable authentication response. Try again shortly."); }
            if (!response.IsSuccessStatusCode)
            {
                using (document)
                {
                    string code = "";
                    if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error))
                        code = error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" :
                            error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var nested) && nested.ValueKind == JsonValueKind.String ? nested.GetString() ?? "" : "";
                    if (code is "invalid_grant" or "invalid_refresh_token" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused" or "token_expired")
                        throw new NativeChatException("invalid_grant", "Your ChatGPT connection can no longer be renewed. Continue with ChatGPT in mod settings to reconnect.");
                    if (code == "access_denied") throw new NativeChatException("access_denied", "ChatGPT sign-in was not completed. You can try again when ready.");
                    if (code is "refresh_token_not_ready" or "refresh_too_early" or "refresh_not_ready" or "token_refresh_too_early")
                        throw new NativeChatException("refresh_not_ready", "ChatGPT renewal is not ready yet. Wait a little and try again.");
                    if (code == "invalid_client") throw new NativeChatException("invalid_client", "ChatGPT rejected the saved app registration. Check the app registration before reconnecting.");
                    throw new NativeChatException("auth_unavailable", "ChatGPT authentication is temporarily unavailable. Your saved connection has been preserved.");
                }
            }
            return document;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (NativeChatException) { throw; }
        catch { throw new NativeChatException("network_error", "ChatGPT could not be reached. Check your internet connection and try again."); }
    }
    internal async Task<NativeVerifiedIdentity> VerifyAsync(string token, string clientId, string? nonce, long receivedAt, NativeAuthEndpoints endpoints, CancellationToken cancellation)
    {
        string[] parts = token.Split('.');
        JsonDocument header, payload;
        try
        {
            if (token.Length > 65536 || parts.Length != 3) throw new FormatException();
            header = JsonDocument.Parse(Unbase64(parts[0])); payload = JsonDocument.Parse(Unbase64(parts[1]));
        }
        catch { throw InvalidIdentity(); }
        using (header) using (payload)
        {
            string kid;
            try
            {
                EnsureUnique(header.RootElement); EnsureUnique(payload.RootElement);
                if (header.RootElement.GetProperty("alg").GetString() != "RS256" || header.RootElement.TryGetProperty("crit", out _)) throw new FormatException();
                kid = header.RootElement.GetProperty("kid").GetString() ?? "";
                if (!NativeAuthStorage.Text(kid, 200)) throw new FormatException();
            }
            catch { throw InvalidIdentity(); }
            JsonDocument keys;
            try { using var request = new HttpRequestMessage(HttpMethod.Get, endpoints.Jwks); keys = await SendJsonAsync(request, cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch { throw KeysUnavailable(); }
            using (keys)
            {
                RSAParameters key;
                try
                {
                    var matches = keys.RootElement.GetProperty("keys").EnumerateArray().Where(k => k.ValueKind == JsonValueKind.Object &&
                        k.TryGetProperty("kid", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == kid).Take(2).ToArray();
                    if (matches.Length != 1) throw new FormatException();
                    var jwk = matches[0]; EnsureUnique(jwk);
                    if (jwk.GetProperty("kty").GetString() != "RSA" || (jwk.TryGetProperty("use", out var use) && use.GetString() != "sig") ||
                        (jwk.TryGetProperty("alg", out var alg) && alg.GetString() != "RS256") ||
                        (jwk.TryGetProperty("key_ops", out var ops) && (ops.ValueKind != JsonValueKind.Array || !ops.EnumerateArray().Any(o => o.ValueKind == JsonValueKind.String && o.GetString() == "verify")))) throw new FormatException();
                    key = new RSAParameters { Modulus = Unbase64(jwk.GetProperty("n").GetString() ?? ""), Exponent = Unbase64(jwk.GetProperty("e").GetString() ?? "") };
                    if (key.Modulus.Length < 256 || key.Modulus.Length > 1024 || key.Exponent.Length == 0 || key.Exponent.Length > 8) throw new FormatException();
                }
                catch { throw KeysUnavailable(); }
                try
                {
                    using RSA rsa = RSA.Create(); rsa.ImportParameters(key);
                    if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Unbase64(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new FormatException();
                    var claims = payload.RootElement;
                    double now = receivedAt / 1000.0;
                    if (claims.GetProperty("iss").GetString() != Issuer || !claims.GetProperty("exp").TryGetInt64(out long exp) || exp <= now - 5 ||
                        !claims.GetProperty("iat").TryGetInt64(out long issued) || issued > now + 5 || issued <= 0 || exp <= issued ||
                        (claims.TryGetProperty("nbf", out var nbf) && (!nbf.TryGetInt64(out long notBefore) || notBefore > now + 5))) throw new FormatException();
                    var aud = claims.GetProperty("aud");
                    bool multiple = aud.ValueKind == JsonValueKind.Array && aud.GetArrayLength() > 1;
                    bool audience = aud.ValueKind == JsonValueKind.String ? aud.GetString() == clientId :
                        aud.ValueKind == JsonValueKind.Array && aud.GetArrayLength() > 0 && aud.EnumerateArray().All(a => a.ValueKind == JsonValueKind.String) && aud.EnumerateArray().Any(a => a.GetString() == clientId);
                    bool hasAzp = claims.TryGetProperty("azp", out var azp);
                    if (!audience || (hasAzp && azp.GetString() != clientId) || (multiple && !hasAzp)) throw new FormatException();
                    if (nonce is not null && (!claims.TryGetProperty("nonce", out var returned) || returned.GetString() != nonce)) throw new FormatException();
                    string subject = claims.GetProperty("sub").GetString() ?? "";
                    if (!NativeAuthStorage.Text(subject, 512)) throw new FormatException();
                    string? Label(string name)
                    {
                        string text = claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? new string((value.GetString() ?? "").Where(c => !char.IsControl(c)).Take(512).ToArray()) : "";
                        return text.Length == 0 ? null : text;
                    }
                    return new(subject, new NativeAuthIdentity { Name = Label("name"), Email = Label("email") });
                }
                catch { throw InvalidIdentity(); }
            }
        }
    }
    internal static NativeAuthRotation ReadTokens(JsonElement root, List<string>? previousScopes, bool refresh)
    {
        try
        {
            EnsureUnique(root);
            string? scope = root.TryGetProperty("scope", out var granted) ? granted.GetString() : previousScopes is not null ? string.Join(' ', previousScopes) : null;
            if (scope is null || scope.Length > 10000) throw new FormatException();
            List<string> scopes = scope.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            if (scopes.Count > 50 || scopes.Any(s => !NativeAuthStorage.Text(s, 200))) throw new FormatException();
            string access = root.TryGetProperty("access_token", out var accessValue) ? accessValue.GetString() ?? "" : "";
            string? replacement = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            // Identity-only authorization remains a valid signed-in account, with inference disabled.
            if (access.Length == 0 && !refresh && !scopes.Contains("offline_access") && !scopes.Contains(PlanScope))
            {
                string? identityToken = root.TryGetProperty("id_token", out var onlyIdentity) ? onlyIdentity.GetString() : null;
                if (!NativeAuthStorage.Text(identityToken, 65536)) throw new FormatException();
                return new NativeAuthRotation { ReceivedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Scopes = scopes, IdToken = identityToken };
            }
            if (!NativeAuthStorage.Text(access, 65536) || (refresh || scopes.Contains("offline_access")) && !NativeAuthStorage.Text(replacement, 65536) ||
                !root.TryGetProperty("token_type", out var type) || !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase) ||
                !root.TryGetProperty("expires_in", out var expiry) || !expiry.TryGetDouble(out double seconds) || !double.IsFinite(seconds) || seconds <= 0 || seconds > 31 * 86400) throw new FormatException();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string? idToken = root.TryGetProperty("id_token", out var id) ? id.GetString() : null;
            if (idToken is not null && !NativeAuthStorage.Text(idToken, 65536)) throw new FormatException();
            JsonElement earliest = root.TryGetProperty("earliest_refresh_at", out var early) ? early.Clone() : default;
            if (earliest.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.String or JsonValueKind.Number)) throw new FormatException();
            return new NativeAuthRotation { ReceivedAt = now, Scopes = scopes, IdToken = idToken,
                Credentials = new NativeAuthCredentials { AccessToken = access, RefreshToken = replacement, ExpiresAt = now + (long)(seconds * 1000), EarliestRefreshAt = earliest } };
        }
        catch { throw new NativeChatException("invalid_token_response", "ChatGPT returned incomplete credentials. Continue with ChatGPT in mod settings to reconnect."); }
    }
    internal async Task<bool> RevokeAsync(NativeAuthEndpoints endpoints, NativeAuthProfile profile, CancellationToken cancellation)
    {
        string? token = profile.PendingRefresh?.Credentials.RefreshToken ?? profile.Credentials?.RefreshToken;
        if (token is null) return profile.Credentials is null && profile.PendingRefresh is null;
        if (endpoints.Revoke is null) return false;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoints.Revoke) { Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["token"] = token, ["token_type_hint"] = "refresh_token", ["client_id"] = profile.ClientId }) };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode == 200) return true;
                if ((int)response.StatusCode < 500) return false;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch { }
            if (attempt == 0) await Task.Delay(300, cancellation);
        }
        return false;
    }
    internal static void EnsureUnique(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object) throw new FormatException();
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject()) if (!names.Add(property.Name)) throw new FormatException();
    }
    internal static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static byte[] Unbase64(string text)
    {
        if (text.Length == 0 || text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new FormatException();
        byte[] bytes = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
        if (Base64(bytes) != text) throw new FormatException();
        return bytes;
    }
    private static NativeChatException InvalidIdentity() => new("invalid_id_token", "The ChatGPT identity could not be verified. Reconnect to the original ChatGPT account.");
    private static NativeChatException KeysUnavailable() => new("identity_verification_unavailable", "ChatGPT identity verification is temporarily unavailable. Your saved connection has been preserved. Try again shortly.");
}
