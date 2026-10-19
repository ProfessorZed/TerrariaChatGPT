#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TerrariaGPT.Common.Knowledge;
using TerrariaGPT.Common.Services;

namespace TerrariaGPT.Common.Native;

/// <summary>Optional Wiki-only provider. Only public titles/question text are sent to the official wiki.</summary>
public sealed class NativeWikiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(250);
    public NativeWikiClient(HttpClient? httpClient = null)
    {
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<NativeChatReply> AskAsync(string question, IReadOnlyList<KnowledgeSubject> subjects, CancellationToken cancellation)
    {
        try { return await AskCoreAsync(question, subjects, cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (NativeChatException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException
            or HttpRequestException or IOException or RegexMatchTimeoutException or ArgumentException)
        {
            // Neither server responses nor HTTP exceptions are safe diagnostics:
            // they may echo question text. Keep optional wiki failures readable.
            throw Error();
        }
    }

    private async Task<NativeChatReply> AskCoreAsync(string question, IReadOnlyList<KnowledgeSubject> subjects, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (question == null || subjects == null) throw Error();
        KnowledgeSubject? named = subjects.FirstOrDefault();
        if (named is not null && !named.Key.StartsWith("Terraria/", StringComparison.Ordinal))
            throw new NativeChatException("wiki_modded", "This is a modded item. Choose ChatGPT for general advice, or use the installed recipe and inventory tools.");
        string title = named?.Name ?? "";
        if (title.Length == 0)
        {
            string search = question[..Math.Min(question.Length, 300)];
            using JsonDocument results = await Request("action=query&list=search&srnamespace=0&srlimit=1&srsearch=" + Uri.EscapeDataString(search), cancellation).ConfigureAwait(false);
            if (results.RootElement.ValueKind != JsonValueKind.Object || !results.RootElement.TryGetProperty("query", out var query)
                || query.ValueKind != JsonValueKind.Object || !query.TryGetProperty("search", out var list)
                || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
                throw Error();
            if (list[0].ValueKind != JsonValueKind.Object || !list[0].TryGetProperty("title", out var foundTitle)
                || foundTitle.ValueKind != JsonValueKind.String) throw Error();
            title = foundTitle.GetString() ?? "";
        }
        if (title.Length is 0 or > 200 || title.Contains(':') || title.Any(char.IsControl)) throw Error();
        using JsonDocument article = await Request("action=parse&prop=text&redirects=1&disablelimitreport=1&disableeditsection=1&page=" + Uri.EscapeDataString(title), cancellation).ConfigureAwait(false);
        if (article.RootElement.ValueKind != JsonValueKind.Object || !article.RootElement.TryGetProperty("parse", out var parsed)
            || parsed.ValueKind != JsonValueKind.Object || !parsed.TryGetProperty("text", out var html)
            || html.ValueKind != JsonValueKind.String) throw Error();
        string body = ExtractParagraphs(html.GetString() ?? "");
        if (body.Length == 0) throw Error();
        string answer = title + " — Wiki reference\n" + body;
        return new(answer, new[] { new AnswerHighlight(title, "item") }, Array.Empty<WebCitation>(), false, null);
    }

    internal static string ExtractParagraphs(string html)
    {
        // Ignore menus, scripts and tables; preserve the article's introductory prose.
        string cleaned = Regex.Replace(html, @"<(script|style|table)\b[^>]*>[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase, RegexLimit);
        var paragraphs = new List<string>();
        int words = 0;
        foreach (Match match in Regex.Matches(cleaned, @"<p\b[^>]*>([\s\S]*?)</p\s*>", RegexOptions.IgnoreCase, RegexLimit))
        {
            string text = WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, "<[^>]*>", "", RegexOptions.None, RegexLimit));
            text = Regex.Replace(text, @"\s+", " ", RegexOptions.None, RegexLimit).Trim();
            if (text.Length < 30) continue;
            string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int count = Math.Min(tokens.Length, 180 - words);
            paragraphs.Add(string.Join(" ", tokens.Take(count)) + (count < tokens.Length ? "…" : ""));
            words += count;
            if (words >= 180 || paragraphs.Count == 3) break;
        }
        return string.Join("\n\n", paragraphs);
    }

    private async Task<JsonDocument> Request(string query, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://terraria.wiki.gg/api.php?format=json&formatversion=2&" + query);
        request.Headers.UserAgent.ParseAdd("TerrariaChatGPT/0.9.0 (in-game reference reader)");
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw Error();
        using Stream stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes, cancellation).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > 2 * 1024 * 1024) throw Error();
            buffer.Write(bytes, 0, count);
        }
        return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }
    private static NativeChatException Error() => new("wiki_unavailable", "The official wiki did not return a readable article. Try a specific item name, or choose ChatGPT in mod settings.");
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
