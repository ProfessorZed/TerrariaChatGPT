#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerrariaGPT.Common.Knowledge;
using TerrariaGPT.Common.Native;

namespace TerrariaGPT.Tests;

public static class NativeWikiTests
{
    private static readonly KnowledgeSubject[] Named = { new("item", "Terraria/BladeofGrass", "Blade of Grass") };
    private const string Intro = "The Blade of Grass is a sword made from materials collected in the Jungle.";
    public static IReadOnlyList<string> Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task<IReadOnlyList<string>> RunAsync()
    {
        var results = new List<string>();
        await Check("Named public subjects fetch only the official wiki article", async () =>
        {
            using var fixture = new Fixture(); fixture.Queue(Article("<p>" + Intro + "</p>"));
            NativeChatReply reply = await fixture.Client.AskAsync("Any question", Named, default);
            Require(reply.Answer.Contains(Intro, StringComparison.Ordinal), "Reference text missing.");
            Require(reply.Citations.Count == 0 && !reply.WebSearched && reply.GoalSuggestion == null, "Wiki reference invented AI metadata.");
            Require(fixture.Handler.Urls.Single().StartsWith("https://terraria.wiki.gg/api.php?", StringComparison.Ordinal)
                && fixture.Handler.Urls.Single().Contains("page=Blade%20of%20Grass", StringComparison.Ordinal), "Incorrect source URL.");
        }, results);
        await Check("Modded subjects are rejected before a public network request", async () =>
        {
            using var fixture = new Fixture(); await Error(fixture.Client.AskAsync("Hello", new[] { new KnowledgeSubject("item", "OtherMod/Item", "Modded item") }, default), "wiki_modded");
            Equal(0, fixture.Handler.Urls.Count);
        }, results);
        await Check("Search text is bounded and query-escaped before fetching the returned title", async () =>
        {
            using var fixture = new Fixture(); fixture.Queue("{\"query\":{\"search\":[{\"title\":\"Blade of Grass\"}]}}"); fixture.Queue(Article("<p>" + Intro + "</p>"));
            string question = "x&action=delete " + new string('q', 400);
            await fixture.Client.AskAsync(question, Array.Empty<KnowledgeSubject>(), default);
            string first = fixture.Handler.Urls[0]; Require(first.Contains("srsearch=x%26action%3Ddelete%20", StringComparison.Ordinal), "Query injection was not escaped.");
            string value = first[(first.IndexOf("srsearch=", StringComparison.Ordinal) + 9)..]; Equal(300, Uri.UnescapeDataString(value).Length);
            Equal(2, fixture.Handler.Urls.Count);
        }, results);
        await Check("Search cannot select a non-article namespace", async () =>
        {
            using var fixture = new Fixture(); fixture.Queue("{\"query\":{\"search\":[{\"title\":\"File:Something\"}]}}");
            await Error(fixture.Client.AskAsync("Something", Array.Empty<KnowledgeSubject>(), default), "wiki_unavailable"); Equal(1, fixture.Handler.Urls.Count);
        }, results);
        await Check("HTML scripts, styles, tables, headings and short navigation are excluded", () =>
        {
            string html = "<script><p>SECRET script content should never show in reference text.</p></script><style><p>SECRET style content should never show in reference text.</p></style>"
                + "<table><tr><td><p>SECRET table content should never show in reference text.</p></td></tr></table>"
                + "<h1>SECRET heading</h1><p>Menu</p><p>" + Intro.Replace("sword", "<b>sword</b>") + " &amp; more.</p>";
            string result = NativeWikiClient.ExtractParagraphs(html);
            Require(!result.Contains("SECRET", StringComparison.Ordinal) && !result.Contains("<b>", StringComparison.Ordinal)
                && result.Contains("& more.", StringComparison.Ordinal), "HTML cleanup lost prose or retained non-prose."); return Task.CompletedTask;
        }, results);
        await Check("References stop at three paragraphs or 180 words", () =>
        {
            string html = string.Concat(Enumerable.Repeat("<p>" + Intro + "</p>", 5));
            Equal(3, NativeWikiClient.ExtractParagraphs(html).Split("\n\n").Length);
            string words = NativeWikiClient.ExtractParagraphs("<p>" + string.Join(" ", Enumerable.Repeat("word", 300)) + "</p>");
            Equal(180, words.Split(' ').Length); Require(words.EndsWith('…'), "Truncation not indicated."); return Task.CompletedTask;
        }, results);
        await Check("Missing readable paragraphs produce an actionable wiki error", async () =>
        {
            using var fixture = new Fixture(); fixture.Queue(Article("<table><tr><td>Only a table</td></tr></table>"));
            await Error(fixture.Client.AskAsync("Hello", Named, default), "wiki_unavailable");
        }, results);
        await Check("Malformed search and article shapes produce safe wiki errors", async () =>
        {
            foreach (string body in new[] { "null", "[]", "{\"query\":\"bad\"}", "{\"query\":{\"search\":[null]}}",
                "{\"query\":{\"search\":[{}]}}", "{\"query\":{\"search\":[{\"title\":99}]}}", "{malformed json}" })
            {
                using var fixture = new Fixture(); fixture.Queue(body);
                await Error(fixture.Client.AskAsync("PRIVATE-QUESTION", Array.Empty<KnowledgeSubject>(), default), "wiki_unavailable");
            }
            foreach (string body in new[] { "null", "[]", "{\"parse\":\"bad\"}", "{\"parse\":{\"text\":{}}}", "{\"parse\":{}}" })
            {
                using var fixture = new Fixture(); fixture.Queue(body);
                await Error(fixture.Client.AskAsync("PRIVATE-QUESTION", Named, default), "wiki_unavailable");
            }
        }, results);
        await Check("A redirect is rejected without forwarding the question elsewhere", async () =>
        {
            using var fixture = new Fixture(); fixture.Handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Redirect)
                { Content = new StringContent(""), Headers = { Location = new Uri("https://other.example/") } });
            await Error(fixture.Client.AskAsync("Hello", Named, default), "wiki_unavailable"); Equal(1, fixture.Handler.Urls.Count);
        }, results);
        await Check("Declared and streamed response size caps both reject oversized articles", async () =>
        {
            using var declared = new Fixture(); var big = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            big.Content.Headers.ContentLength = 2 * 1024 * 1024 + 1; declared.Handler.Responses.Enqueue(big);
            await Error(declared.Client.AskAsync("Hello", Named, default), "wiki_unavailable");
            using var streamed = new Fixture(); var unknown = new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StreamContent(new NonSeekingStream(new byte[2 * 1024 * 1024 + 1])) };
            streamed.Handler.Responses.Enqueue(unknown);
            await Error(streamed.Client.AskAsync("Hello", Named, default), "wiki_unavailable");
        }, results);
        await Check("Cancellation reaches the HTTP request before any response is returned", async () =>
        {
            using var fixture = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
            try { await fixture.Client.AskAsync("Hello", Named, stop.Token); throw new InvalidOperationException("Cancellation was ignored."); }
            catch (OperationCanceledException) { }
            Equal(0, fixture.Handler.Urls.Count);
        }, results);
        return results;
    }
    private static string Article(string html) => JsonSerializer.Serialize(new { parse = new { text = html } });
    private static async Task Check(string name, Func<Task> action, List<string> results) { await action(); results.Add("PASS " + name); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) => Require(EqualityComparer<T>.Default.Equals(expected, actual), "Expected " + expected + ", got " + actual);
    private static async Task Error(Task<NativeChatReply> task, string code)
    {
        try { await task; throw new InvalidOperationException("Expected " + code); }
        catch (NativeChatException error)
        {
            Equal(code, error.Code);
            Require(!error.ToString().Contains("PRIVATE-QUESTION", StringComparison.Ordinal), "Private request escaped diagnostics.");
        }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly Handler Handler = new(); private readonly HttpClient _http; public NativeWikiClient Client { get; }
        public Fixture() { _http = new HttpClient(Handler); Client = new NativeWikiClient(_http); }
        public void Queue(string body) => Handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        public void Dispose() { Client.Dispose(); _http.Dispose(); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<string> Urls = new(); public readonly Queue<HttpResponseMessage> Responses = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Urls.Add(request.RequestUri!.AbsoluteUri); return Task.FromResult(Responses.Dequeue()); }
    }
    private sealed class NonSeekingStream : Stream
    {
        private readonly MemoryStream _inner; public NonSeekingStream(byte[] data) { _inner = new MemoryStream(data); }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
