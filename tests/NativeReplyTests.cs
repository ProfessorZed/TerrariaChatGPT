#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using TerrariaChatGPT.Common.Native;

namespace TerrariaChatGPT.Tests;

/// <summary>Public/fabricated complete Responses text. No game or network access.</summary>
public static class NativeReplyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private const string Wiki = "https://terraria.wiki.gg/wiki/Night%27s_Edge";
    private const string Link = "([wiki](https://terraria.wiki.gg/wiki/Night%27s_Edge))";
    // Exact public diagnostic prose saved for the 0.7 connection-error report.
    // Embedded here so this regression needs no account or local failure log.
    private const string FailedPublicAnswer = "The Blade of Grass is used to craft Night’s Edge, a strong pre-Hardmode sword.\n\n"
        + "You need:\n• 1 Blade of Grass\n• 1 Muramasa — found in locked Gold Chests in the Dungeon\n"
        + "• 1 Volcano — crafted from 20 Hellstone Bars at an Iron or Lead Anvil\n"
        + "• 1 Light’s Bane OR 1 Blood Butcherer — either one works\n\n"
        + "Combine all four swords at a Demon Altar or Crimson Altar to make 1 Night’s Edge.\n\n"
        + "Keep it around: Night’s Edge eventually upgrades into True Night’s Edge and helps make the Terra Blade!";
    private static readonly object[] FailedHighlights = new[]
    {
        ("Blade of Grass", "item"), ("Night’s Edge", "item"), ("Muramasa", "item"), ("Dungeon", "location"),
        ("Volcano", "item"), ("Hellstone Bars", "item"), ("Iron or Lead Anvil", "location"), ("Light’s Bane", "item"),
        ("Blood Butcherer", "item"), ("Demon Altar", "location"), ("Crimson Altar", "location"), ("Terra Blade", "item")
    }.Select(pair => (object)new { text = pair.Item1, kind = pair.Item2 }).ToArray();

    public static string[] Run()
    {
        var results = new List<string>();
        void Check(string name, Action action) { action(); results.Add(name); }
        Check("Exact failed Blade of Grass reply preserves its complete answer and all twelve highlights", () =>
        {
            var reply = Parse(FailedPublicAnswer + "\",\"highlights\":" + Json(FailedHighlights) + ",\"goalSuggestion\":null}");
            Equal(reply.Answer, FailedPublicAnswer); Assert(reply.Highlights.Count == 12 && reply.GoalSuggestion is null);
        });
        Check("Completed structured answer and dropped-prefix answer have identical meaning", () =>
        {
            string raw = Envelope(FailedPublicAnswer, FailedHighlights);
            Equal(Parse(raw).Answer, Parse(raw["{\"answer\":\"".Length..]).Answer);
        });
        Check("Escaped newlines, quotes and Unicode decode only when the full string interior is valid", () =>
        {
            string answer = "Use Night’s Edge.\n\nSay \"keep it\" — upgrading is optional.";
            foreach (string prefix in new[] { Json(answer)[1..^1], JsonSerializer.Serialize(answer)[1..^1] })
                Equal(Parse(prefix + Tail()).Answer, answer);
        });
        Check("Already-decoded prose keeps ordinary quotes and literal backslashes", () =>
        {
            string answer = "Say \"Night’s Edge\".\nA literal code escape \\n stays literal.";
            Equal(Parse(answer + Tail()).Answer, answer);
        });
        Check("A complete answer survives syntactically truncated optional proposal data", () =>
        {
            var reply = Parse("{\"answer\":\"Use Rocket Boots.\",\"highlights\":[],\"goalSuggestion\":{\"title\":\"broken\",");
            Equal(reply.Answer, "Use Rocket Boots."); Assert(reply.Highlights.Count == 0 && reply.GoalSuggestion is null);
        });
        Check("Malformed complete optional metadata cannot discard useful answer text", () =>
        {
            foreach (object? invalid in new object?[] { null, "not a goal", new { title = "Unsafe", steps = new[] { new { kind = "spawn", key = "Terraria/NightsEdge", label = "Spawn", target = 1 } } } })
            {
                var reply = Parse(Envelope("Make Night’s Edge.", goal: invalid));
                Equal(reply.Answer, "Make Night’s Edge."); Assert(reply.GoalSuggestion is null);
            }
        });
        Check("Dropped-prefix proposal remains inert and retains its exact craft identity", () =>
        {
            string raw = Envelope("Make Night’s Edge.", goal: Goal());
            var reply = Parse(raw["{\"answer\":\"".Length..]);
            Assert(reply.GoalSuggestion?.Steps.Single().Key == "Terraria/NightsEdge");
        });
        Check("Unknown boss names become manual checkboxes instead of invented defeat records", () =>
        {
            var reply = Parse(Envelope("Defeat the boss.", goal: new { title = "Bosses", steps = new[] { new { kind = "boss", key = "brain_of_cthulhu", label = "Brain of Cthulhu" } } }));
            Assert(reply.GoalSuggestion?.Steps.Single().Kind == "manual" && reply.GoalSuggestion.Steps[0].Key == "");
        });
        Check("Shared evil-boss record is explicitly labeled and duplicate goals are removed", () =>
        {
            var step = new { kind = "boss", key = "evil_boss", label = "Defeat both evil bosses", target = 1 };
            var reply = Parse(Envelope("Defeat your world’s evil boss.", goal: new { title = "Bosses", steps = new[] { step, step } }));
            Assert(reply.GoalSuggestion?.Steps.Count == 1);
            Equal(reply.GoalSuggestion!.Steps[0].Label, "Defeat Eater of Worlds or Brain of Cthulhu");
        });
        Check("Goal quantities and allowed actions stay bounded", () =>
        {
            foreach (object count in new object[] { 0, -1, 1000000, 1.5, "1", true })
                Assert(Parse(Envelope("Make the item.", goal: new { title = "Goal", steps = new[] { new { kind = "craft", key = "Terraria/NightsEdge", label = "Make", target = count } } })).GoalSuggestion is null);
            var good = Parse(Envelope("Make the item.", goal: new { title = "Goal", steps = new[] { new { kind = "item", key = "Terraria/Wood", label = "Collect", target = 999999 } } }));
            Assert(good.GoalSuggestion?.Steps[0].Target == 999999);
        });
        Check("Goal proposal bounds reject oversized lists and labels", () =>
        {
            var step = new { kind = "manual", key = "", label = "Do this", target = 1 };
            Assert(Parse(Envelope("Useful answer.", goal: new { title = "Goal", steps = Enumerable.Repeat(step, 25).ToArray() })).GoalSuggestion is null);
            Assert(Parse(Envelope("Useful answer.", goal: new { title = new string('x', 101), steps = new[] { step } })).GoalSuggestion is null);
        });
        Check("No source link is invented from answer text or model-generated citation fields", () =>
        {
            var reply = Parse("{\"answer\":\"Use Night’s Edge. [1] [fake](https://example.com)\",\"citations\":[{\"index\":1,\"url\":\"https://example.com\"}],\"highlights\":[]}");
            Assert(reply.Citations.Count == 0 && !reply.Answer.Contains("[1]") && !reply.Answer.Contains("https"));
        });
        Check("Provider-linked Markdown becomes a compact clickable inline marker", () =>
        {
            string raw = Envelope("Make Night’s Edge. " + Link);
            var reply = Parse(raw, Source(raw, Link));
            Equal(reply.Answer, "Make Night’s Edge. [1]"); Assert(reply.Citations.Single().Url == Wiki);
        });
        Check("Claim-span citations retain every word of the supported claim", () =>
        {
            string raw = Envelope("First claim. Second claim.");
            var reply = Parse(raw, Source(raw, "First claim."), Source(raw, "Second claim."));
            Equal(reply.Answer, "First claim. [1] Second claim. [1]"); Assert(reply.Citations.Count == 1);
        });
        Check("Multiple distinct sources can support the same claim without erasing it", () =>
        {
            string raw = Envelope("First claim.");
            var source = Source(raw, "First claim.");
            var reply = Parse(raw, source, source with { Url = "https://example.com/source", Title = "Other" });
            Equal(reply.Answer, "First claim. [1] [2]"); Assert(reply.Citations.Count == 2);
        });
        Check("Provider annotations in highlights or goals cannot create citations", () =>
        {
            string raw = Envelope("Night’s Edge.", goal: Goal());
            int start = raw.IndexOf("goalSuggestion", StringComparison.Ordinal);
            var reply = Parse(raw, new NativeSourceAnnotation(Wiki, "metadata", start, start + 14));
            Assert(reply.Citations.Count == 0);
        });
        Check("UTF16 source offsets remain correct after a supplementary Unicode character", () =>
        {
            string raw = "{\"answer\":\"🚀 Flight works. " + Link + "\",\"highlights\":[]}";
            Equal(Parse(raw, Source(raw, Link)).Answer, "🚀 Flight works. [1]");
        });
        Check("Code-point source offsets also remain correct after a supplementary character", () =>
        {
            string raw = "{\"answer\":\"🚀 Flight works. " + Link + "\",\"highlights\":[]}";
            var utf16 = Source(raw, Link);
            Equal(Parse(raw, utf16 with { Start = utf16.Start - 1, End = utf16.End - 1 }).Answer, "🚀 Flight works. [1]");
        });
        Check("Fenced dropped-prefix replies preserve original source positions", () =>
        {
            string raw = "```json\n🚀 Night’s Edge.\n" + Link + Tail() + "\n```";
            Equal(Parse(raw, Source(raw, Link)).Answer, "🚀 Night’s Edge.\n[1]");
        });
        Check("Unicode JSON escapes do not shift the following source marker", () =>
        {
            string raw = "{\"answer\":\"\\uD83D\\uDE80 Night\\u2019s Edge.\\n" + Link + "\",\"highlights\":[]}";
            Equal(Parse(raw, Source(raw, Link)).Answer, "🚀 Night’s Edge.\n[1]");
        });
        Check("An annotation cannot split a JSON escape or Unicode surrogate pair", () =>
        {
            string raw = "{\"answer\":\"\\uD83D\\uDE80 flight.\",\"highlights\":[]}";
            int start = raw.IndexOf("D83D", StringComparison.Ordinal);
            Assert(Parse(raw, new NativeSourceAnnotation(Wiki, "Bad span", start, start + 2)).Citations.Count == 0);
            int middle = raw.IndexOf("\\uDE80", StringComparison.Ordinal);
            Assert(Parse(raw, new NativeSourceAnnotation(Wiki, "Half a character", middle, middle + 6)).Citations.Count == 0);
        });
        Check("Unsafe source schemes, ports, credentials and local hosts never become clickable", () =>
        {
            string raw = "A public claim.";
            foreach (string url in new[] { "javascript:alert(1)", "file:///C:/private", "http://localhost/a", "http://127.0.0.1/a", "http://127.1/a", "http://2130706433/a", "http://0x7f000001/a", "http://10.0.0.1/a", "http://[::1]/", "https://example.com:38765/", "https://user:password@example.com/", "https://server.local/a", "https://server.invalid/a", "https://example.com\\@localhost", "https://example.com/a b" })
                Assert(Parse(raw, new NativeSourceAnnotation(url, "Unsafe", 0, raw.Length)).Citations.Count == 0, url);
        });
        Check("Invalid and overlapping source ranges cannot truncate or duplicate answer text", () =>
        {
            string raw = "First claim. Second claim.";
            var reply = Parse(raw, new NativeSourceAnnotation(Wiki, "first", 0, 12), new("https://example.com", "overlap", 5, 20), new(Wiki, "negative", -1, 5), new(Wiki, "large", 1, int.MaxValue));
            Equal(reply.Answer, "First claim. [1] Second claim."); Assert(reply.Citations.Count == 1);
        });
        Check("Source annotations stay bounded and preserve the actual search status", () =>
        {
            string raw = "Claim.";
            var sources = Enumerable.Range(0, 100).Select(index => new NativeSourceAnnotation("https://example.com/" + index, "Source", 0, 6)).ToArray();
            var reply = NativeReplyParser.Parse(raw, sources, true);
            Assert(reply.Citations.Count == 24 && reply.WebSearched);
            Assert(!Parse(raw).WebSearched);
        });
        Check("Metadata only colors exact existing phrases and conflicts lose their color", () =>
        {
            var rows = new[] { new { text = "Night’s Edge", kind = "item" }, new { text = "Night’s Edge", kind = "owned" }, new { text = "A hidden chest", kind = "location" } };
            Assert(Parse(Envelope("Night’s Edge.", rows)).Highlights.Count == 0);
        });
        Check("Structured invalid highlight rows are discarded without losing the answer", () =>
        {
            var rows = new object[] { "bad", new { text = "Night’s Edge", kind = "execute" }, new { text = "<Night’s Edge>", kind = "item" }, new { text = "Night’s Edge", kind = "item" } };
            var reply = Parse(Envelope("Night’s Edge.", rows));
            Assert(reply.Highlights.Count == 1); Equal(reply.Answer, "Night’s Edge.");
        });
        Check("Source glyphs, chat tags, raw URLs and a source footer never leak into the answer", () =>
        {
            var reply = Parse("# Best choice\n[c/00ff00:Rocket Boots] [i:1] citeturn0search0 【source】 https://example.com\nSources: more links");
            Equal(reply.Answer, "Best choice\nRocket Boots");
        });
        Check("Ordinary quoted metadata words and inline JSON examples stay readable", () =>
        {
            foreach (string prose in new[] { "The words \"highlights\" and \"goalSuggestion\" are metadata field names.", "Use {\"text\":\"Starfury\",\"kind\":\"item\"} as an example.", "He called it \"bright\", \"highlights\" appeared later." }) Equal(Parse(prose).Answer, prose);
        });
        Check("Partial or ambiguous dropped metadata fails safely instead of becoming visible JSON", () =>
        {
            foreach (string suffix in new[] { "\",\"highlights\":", "\",\"highlights\":[", "\",\"highlights\"", "\",\"highlights", "\",\"highlights\":[]} trailing content", "\",\"highlights\":[]}{}", "\",\"highlights\":[],\"execute\":\"spawn\"}", "\",\"highlights\":[],\"highlights\":[]}", "\",\"highlights\":[],\"goalSuggestion\":null,\"goalSuggestion\":null}", "\",\"highlights\":[],\"goalSuggestion\":{\"title\":\"unfinished\"," })
                Reject("Night’s Edge." + suffix);
            Reject("Quoted example: \"text\", \"highlights\":[] and more prose. Night’s Edge." + Tail());
        });
        Check("Malformed dropped highlight metadata is not mistaken for an ordinary answer", () =>
        {
            foreach (object? rows in new object?[] { null, "bad", new { }, new object[] { "bad" }, new[] { new { text = "Night’s Edge", kind = "spawn" } }, new[] { new { text = "Night’s Edge", kind = "item", style = "red" } }, new[] { new { text = "[c/ff0000:Night’s Edge]", kind = "item" } }, Enumerable.Repeat(new { text = "Night’s Edge", kind = "item" }, 25).ToArray() })
                Reject("Night’s Edge.\",\"highlights\":" + Json(rows) + "}");
            Reject("Night’s Edge.\",\"highlights\":[{\"text\":\"Night’s Edge\",\"kind\":\"item\",\"kind\":\"location\"}]}");
        });
        Check("Broken starts, nested envelopes and incomplete fences do not leak metadata", () =>
        {
            foreach (string raw in new[] { "\"answer\":\"Night’s Edge\"" + Tail(), "answer\":\"Night’s Edge\"" + Tail(), "\"highlights\":[]}", "{\"answer\":\"unfinished", "[]", "```json\nNight’s Edge.", "Night’s Edge.\", \"goalSuggestion\":{\"title\":\"broken\"}" }) Reject(raw);
            Reject(Envelope("Nested Night’s Edge." + Tail()));
            Reject("{\"answer\":\"First\",\"answer\":\"Second\"}");
        });
        Check("Reply and text limits remain bounded without splitting a Unicode character", () =>
        {
            Reject(new string('x', 24001));
            var reply = Parse(new string('x', 5999) + "🚀" + new string('y', 1000));
            Assert(reply.Answer.Length == 5999 && !char.IsHighSurrogate(reply.Answer[^1]));
        });
        Check("Citations beyond the displayed answer are not exposed as unattached sources", () =>
        {
            string raw = new string('x', 6100) + " " + Link;
            var reply = Parse(raw, Source(raw, Link));
            Assert(reply.Answer.Length == 6000 && reply.Citations.Count == 0 && !reply.Answer.Contains("TGPCITE"));
        });
        Check("Whitespace-heavy answers never expose a partially clipped private citation token", () =>
        {
            string raw = string.Concat(Enumerable.Repeat("x     ", 1995)) + " " + Link;
            var reply = Parse(raw, Source(raw, Link));
            Assert(!reply.Answer.Contains("TGPCITE") && reply.Answer.EndsWith("[1]", StringComparison.Ordinal));
        });
        Check("Answer truncation does not leave a broken citation marker", () =>
        {
            string raw = new string('x', 5997) + " " + Link;
            var reply = Parse(raw, Source(raw, Link));
            Assert(reply.Answer.Length == 5997 && reply.Citations.Count == 0);
        });
        Check("Invalid escaped Unicode in optional metadata leaves the complete answer usable", () =>
        {
            var reply = Parse("{\"answer\":\"Useful answer.\",\"highlights\":[{\"text\":\"\\uD800\",\"kind\":\"item\"}],\"goalSuggestion\":{\"title\":\"\\uD800\",\"steps\":[]}}");
            Equal(reply.Answer, "Useful answer."); Assert(reply.Highlights.Count == 0 && reply.GoalSuggestion is null);
            Reject("{\"answer\":\"\\uD800\"}");
        });
        return results.ToArray();
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value, JsonOptions);
    private static string Envelope(string answer, object? highlights = null, object? goal = null) => Json(new { answer, highlights = highlights ?? Array.Empty<object>(), goalSuggestion = goal });
    private static string Tail() => "\",\"highlights\":[{\"text\":\"Night’s Edge\",\"kind\":\"item\"}],\"goalSuggestion\":null}";
    private static object Goal() => new { title = "Craft Night’s Edge", steps = new[] { new { kind = "craft", key = "Terraria/NightsEdge", label = "Craft Night’s Edge", target = 1 } } };
    private static NativeChatReply Parse(string value, params NativeSourceAnnotation[] sources) => NativeReplyParser.Parse(value, sources, false);
    private static NativeSourceAnnotation Source(string raw, string fragment) { int start = raw.IndexOf(fragment, StringComparison.Ordinal); Assert(start >= 0, "Missing source fixture."); return new(Wiki, "Terraria Wiki", start, start + fragment.Length); }
    private static void Reject(string value)
    {
        try { Parse(value); }
        catch (NativeChatException error) { Assert(error.Code == "response_format" && !error.Message.Contains(value, StringComparison.Ordinal)); return; }
        throw new InvalidOperationException("Accepted malformed response: " + value[..Math.Min(value.Length, 160)]);
    }
    private static void Equal(string actual, string expected) => Assert(actual == expected, "Expected " + Json(expected) + "; received " + Json(actual));
    private static void Assert(bool value, string message = "Native reply boundary failed.") { if (!value) throw new InvalidOperationException(message); }
}
