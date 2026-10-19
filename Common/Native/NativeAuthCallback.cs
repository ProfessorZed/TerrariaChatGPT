// C# adaptation of OpenAI Sign in with ChatGPT DevKit; see NOTICE.txt and SignInWithChatGPT-LICENSE.txt.
// Terraria ChatGPT 0.9.0: renamed app labels; existing credential storage retained (2026-10-19).
// Terraria GPT 0.8.0: native storage/OAuth, safe migration and refresh recovery (2026-10-19).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TerrariaGPT.Common.Native;

internal sealed record NativeAuthCallbackResult(string Code, string ClientId);
internal sealed class NativeAuthCallback : IDisposable
{
    private readonly TcpListener _listener;
    internal string RedirectUri { get; }
    internal NativeAuthCallback()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Server.ExclusiveAddressUse = true;
        try { _listener.Start(8); }
        catch { _listener.Stop(); throw new NativeChatException("callback_unavailable", "The local sign-in listener could not start. Close another copy of tModLoader and try again."); }
        RedirectUri = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/auth/callback";
    }
    internal async Task<NativeAuthCallbackResult> WaitAsync(string state, string? savedClient, CancellationToken cancellation)
    {
        while (true)
        {
            using TcpClient client = await _listener.AcceptTcpClientAsync(cancellation);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            using NetworkStream stream = client.GetStream();
            string? request = null;
            try
            {
                using var buffer = new MemoryStream(); byte[] single = new byte[1];
                while (buffer.Length < 16384 && await stream.ReadAsync(single, requestTimeout.Token) != 0)
                {
                    buffer.WriteByte(single[0]);
                    if (buffer.Length >= 4)
                    {
                        byte[] bytes = buffer.GetBuffer(); int n = (int)buffer.Length;
                        if (bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10) { request = Encoding.ASCII.GetString(bytes, 0, n); break; }
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { continue; }
            catch (IOException) { continue; }
            cancellation.ThrowIfCancellationRequested();
            if (!TryParse(request, RedirectUri, state, out var values))
            { await ReplyAsync(stream, false, requestTimeout.Token); continue; }
            // Scrub the browser's address bar before any credentials are exchanged.
            await ReplyAsync(stream, true, requestTimeout.Token);
            if (values.TryGetValue("error", out _))
                throw new NativeChatException("access_denied", "ChatGPT sign-in was not completed. You can try again when ready.");
            string? code = values.GetValueOrDefault("code"), returnedClient = values.GetValueOrDefault("client_id");
            string? issued = returnedClient ?? savedClient;
            if (!NativeAuthStorage.Text(code, 4096) || !NativeAuthStorage.ValidClient(issued) ||
                (savedClient is not null && returnedClient is not null && savedClient != returnedClient))
                throw new NativeChatException("registration_incomplete", "ChatGPT did not complete app registration. Continue with ChatGPT to try again.");
            return new(code!, issued!);
        }
    }
    internal static bool TryParse(string? request, string callbackUri, string state, out Dictionary<string, string> values)
    {
        values = new(StringComparer.Ordinal);
        try
        {
            if (request is null || request.Length > 16384) return false;
            string[] lines = request.Split("\r\n", StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            Uri callback = new(callbackUri);
            if (first.Length != 3 || first[0] != "GET" || first[2] != "HTTP/1.1" || !first[1].StartsWith("/auth/callback?", StringComparison.Ordinal) || first[1].Contains('#')) return false;
            string[] hosts = lines.Skip(1).Where(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (hosts.Length != 1 || hosts[0][5..].Trim() != callback.Authority) return false;
            if (!Uri.TryCreate(callback.GetLeftPart(UriPartial.Authority) + first[1], UriKind.Absolute, out var target) || target.AbsolutePath != "/auth/callback" || target.Authority != callback.Authority) return false;
            foreach (string part in target.Query.TrimStart('?').Split('&'))
            {
                string[] pair = part.Split('=', 2);
                string name = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
                string value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
                if (!values.TryAdd(name, value)) return false;
            }
            if (!values.TryGetValue("state", out string? returned) || returned.Length != state.Length) return false;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(returned), Encoding.UTF8.GetBytes(state));
        }
        catch { return false; }
    }
    private static async Task ReplyAsync(NetworkStream stream, bool accepted, CancellationToken cancellation)
    {
        const string script = "history.replaceState(null, '', '/auth/complete');";
        string content = accepted ? "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Return to Terraria</title><h1>Return to Terraria</h1><p>Terraria ChatGPT will finish checking your connection. You can close this tab.</p><script>" + script + "</script></html>" : "Invalid sign-in callback. Return to the browser tab that started sign-in.";
        string hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        byte[] body = Encoding.UTF8.GetBytes(content);
        string headers = $"HTTP/1.1 {(accepted ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; script-src 'sha256-{hash}'; frame-ancestors 'none'; base-uri 'none'\r\n\r\n";
        try { await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellation); await stream.WriteAsync(body, cancellation); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or SocketException) { }
    }
    public void Dispose() => _listener.Stop();
}
