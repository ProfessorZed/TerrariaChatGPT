#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaGPT.Common.Knowledge;
using TerrariaGPT.Common.UI;

namespace TerrariaGPT.Tests;

public static class ChatLayoutTests
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed.Add(name);
        }
        string Text(IReadOnlyList<ChatTextSpan> spans) => string.Concat(spans.Select(span => span.Text));
        string Line(ChatTextLine line) => string.Concat(line.Runs.Select(run => run.Text));
        ChatGlyphMetrics Mono(char character) => new(0, 1, 0);
        IReadOnlyList<ChatTextLine> Wrap(string text, float width) => ChatTextLayout.Wrap(
            new[] { new ChatTextSpan(text, AnswerTextKind.Plain) }, width, Mono, 0);

        string answer = "Find Starfury in Sky Islands. Missing: 3 Stingers. You have 2 Stingers.";
        var spans = ChatTextLayout.Highlight(answer, new[]
        {
            new AnswerHighlight("Starfury", "item"), new AnswerHighlight("Sky Islands", "location"),
            new AnswerHighlight("Stingers", "item"), new AnswerHighlight("3 Stingers", "missing"),
            new AnswerHighlight("2 Stingers", "owned")
        });
        Check(Text(spans) == answer, "Highlights preserve the exact literal answer.");
        Check(spans.Any(span => span.Kind == AnswerTextKind.Location && span.Text == "Sky Islands"), "Locations use their semantic category.");
        Check(spans.Any(span => span.Kind == AnswerTextKind.Item && span.Text == "Starfury"), "Item names use their semantic category.");
        Check(spans.Any(span => span.Kind == AnswerTextKind.Missing && span.Text == "3 Stingers") &&
            spans.Any(span => span.Kind == AnswerTextKind.Owned && span.Text == "2 Stingers"), "Missing and owned quantities take priority over overlapping item names.");
        Check(!spans.Any(span => span.Kind == AnswerTextKind.Item && span.Text == "Stingers"), "Overlapping lower-priority phrases are not painted again.");
        var boundary = ChatTextLayout.Highlight("Wooden Wood wood.", new[] { new AnswerHighlight("Wood", "item") });
        Check(boundary.Where(span => span.Kind == AnswerTextKind.Item).Sum(span => span.Text.Length) == 4,
            "Phrase matches are exact and respect word boundaries.");
        var caseDistinct = ChatTextLayout.Highlight("Wood wood", new[] { new AnswerHighlight("Wood", "owned"), new AnswerHighlight("wood", "missing") });
        Check(caseDistinct.Any(span => span.Text == "Wood" && span.Kind == AnswerTextKind.Owned) &&
            caseDistinct.Any(span => span.Text == "wood" && span.Kind == AnswerTextKind.Missing),
            "Case-distinct exact phrases cannot paint each other's different meaning.");
        var invalid = ChatTextLayout.Highlight("Sky Islands", new[]
        {
            new AnswerHighlight("Sky Islands", "red"), new AnswerHighlight("Sky\nIslands", "location"),
            new AnswerHighlight(new string('x', 121), "missing")
        });
        Check(invalid.Count == 1 && invalid[0].Kind == AnswerTextKind.Plain, "Unknown colors, controls, and oversized phrases are ignored.");
        string hostile = "[c/ff0000:spawn] [i:1] <location>Sky Islands</location>";
        Check(Text(ChatTextLayout.Highlight(hostile, Array.Empty<AnswerHighlight>())) == hostile,
            "Terraria tags and arbitrary markup remain literal text.");
        var repeated = ChatTextLayout.Highlight(string.Join(" ", Enumerable.Repeat("Wood", 100)), new[] { new AnswerHighlight("Wood", "item") });
        Check(repeated.Count(span => span.Kind == AnswerTextKind.Item) == 12, "Repeated metadata has a bounded number of matches.");
        var orderA = ChatTextLayout.Highlight("3 Stingers", new[] { new AnswerHighlight("Stingers", "item"), new AnswerHighlight("3 Stingers", "missing") });
        var orderB = ChatTextLayout.Highlight("3 Stingers", new[] { new AnswerHighlight("3 Stingers", "missing"), new AnswerHighlight("Stingers", "item") });
        Check(orderA.SequenceEqual(orderB), "Metadata ordering cannot change overlap priority.");
        string floodText = string.Join(" ", Enumerable.Range(0, 12).SelectMany(i => Enumerable.Repeat("item" + i, 12))) + " Missing Stingers";
        var floodMetadata = Enumerable.Range(0, 12).Select(i => new AnswerHighlight("item" + i, "item"))
            .Append(new AnswerHighlight("Missing Stingers", "missing")).ToArray();
        Check(ChatTextLayout.Highlight(floodText, floodMetadata).Any(span => span.Text == "Missing Stingers" && span.Kind == AnswerTextKind.Missing),
            "Low-priority repeated item metadata cannot consume the missing-material highlight budget.");
        const string scopedText = "Still need: 2 Wood. You have: 2 Wood. Recipe: 4 Wood.";
        int carriedAt = scopedText.IndexOf("2 Wood", scopedText.IndexOf("You have:", StringComparison.Ordinal), StringComparison.Ordinal);
        var scoped = ChatTextLayout.Highlight(scopedText, new[]
        {
            new AnswerHighlight("2 Wood", "missing") { Start = scopedText.IndexOf("2 Wood", StringComparison.Ordinal) },
            new AnswerHighlight("2 Wood", "owned") { Start = carriedAt },
            new AnswerHighlight("4 Wood", "item") { Start = scopedText.IndexOf("4 Wood", StringComparison.Ordinal) }
        });
        Check(scoped.Count(span => span.Text == "2 Wood" && span.Kind == AnswerTextKind.Missing) == 1 &&
            scoped.Count(span => span.Text == "2 Wood" && span.Kind == AnswerTextKind.Owned) == 1 &&
            scoped.Any(span => span.Text == "4 Wood" && span.Kind == AnswerTextKind.Item),
            "Occurrence-scoped colors keep equal missing and carried quantities distinct from the full recipe.");
        string scopedManyText = string.Join(", ", Enumerable.Range(0, 150).Select(index => "Material " + index));
        var scopedMany = Enumerable.Range(0, 150).Select(index => new AnswerHighlight("Material " + index, "item")
            { Start = scopedManyText.IndexOf("Material " + index + (index == 149 ? "" : ","), StringComparison.Ordinal) }).ToArray();
        var scopedManySpans = ChatTextLayout.Highlight(scopedManyText, scopedMany);
        Check(scopedManySpans.Count(span => span.Kind == AnswerTextKind.Item) == 150,
            "Long deterministic ingredient lists bypass the old global-phrase and repeat budgets.");
        var invalidScopes = ChatTextLayout.Highlight("Wood Wood", new[] { new AnswerHighlight("Wood", "owned") { Start = -1 },
            new AnswerHighlight("Wood", "owned") { Start = 1 }, new AnswerHighlight("Wood", "owned") { Start = int.MaxValue } });
        Check(invalidScopes.Count == 1 && invalidScopes[0].Kind == AnswerTextKind.Plain,
            "Invalid occurrence offsets never fall back to painting another match.");
        string longLocalName = "Material " + new string('x', 150);
        var longLocal = ChatTextLayout.Highlight(longLocalName, new[] { new AnswerHighlight(longLocalName, "item") { Start = 0 } });
        Check(longLocal.Count == 1 && longLocal[0].Kind == AnswerTextKind.Item && longLocal[0].Text == longLocalName,
            "A long local mod item name retains its complete color span.");

        var words = Wrap("one two three", 7);
        Check(words.Select(Line).SequenceEqual(new[] { "one two", "three" }), "Word wrapping prefers spaces.");
        var narrow = Wrap("abcdef", 2);
        Check(narrow.Select(Line).SequenceEqual(new[] { "ab", "cd", "ef" }), "Long words split without losing characters.");
        Check(Wrap("a\n\nb\n", 20).Select(Line).SequenceEqual(new[] { "a", "", "b", "" }), "Paragraphs and blank lines remain readable.");
        Check(Line(Wrap("a\tb", 20)[0]) == "a    b", "Tabs normalize to spaces.");
        var unicode = Wrap("a😀e\u0301z", 1);
        Check(unicode.Select(Line).SequenceEqual(new[] { "a", "😀", "e\u0301", "z" }), "Narrow layouts keep emoji and combining marks intact.");
        Check(string.Concat(Wrap("verylongword", 0).Select(Line)) == "verylongword", "A zero-width layout always makes progress.");
        var colored = ChatTextLayout.Wrap(new[] { new ChatTextSpan("ab", AnswerTextKind.Plain), new ChatTextSpan("cd", AnswerTextKind.Item) },
            10, Mono, 0);
        Check(colored.Count == 1 && colored[0].Runs[1].X == 2 && Line(colored[0]) == "abcd", "Color changes preserve text and horizontal spacing.");
        ChatGlyphMetrics Bearings(char character) => character == 'b' ? new(-1, 2, -0.5f) : new(1, 2, 1);
        var kerned = ChatTextLayout.Wrap(new[] { new ChatTextSpan("a", AnswerTextKind.Plain), new ChatTextSpan("b", AnswerTextKind.Item) },
            20, Bearings, 0.5f)[0];
        Check(Math.Abs(kerned.Runs[1].X - 3.5f) < 0.001f && Math.Abs(kerned.Width - 5.5f) < 0.001f,
            "Negative bearings and character spacing remain correct across color runs.");
        var invariantText = Enumerable.Range(0, 80).Select(i => new ChatTextSpan("word" + i + " ", (AnswerTextKind)(i % 6))).ToArray();
        var wrapped = ChatTextLayout.Wrap(invariantText, 31, Mono, 0);
        Check(wrapped.All(line => line.Width <= 31), "Many differently colored words remain within the available width.");
        Check(ChatScrollPosition.AfterNewAnswer(25, 10, 0) == 15,
            "A new long answer starts at its beginning rather than hiding its recommendation.");
        Check(ChatScrollPosition.AfterNewAnswer(6, 10, 0) == 0 && ChatScrollPosition.AfterNewAnswer(10, 10, 0) == 0,
            "Short and exactly fitting answers stay completely visible.");
        Check(ChatScrollPosition.AfterNewAnswer(25, 10, 7) == 7,
            "New-answer behavior cannot move a player who is reading earlier messages.");
        Check(ChatScrollPosition.AfterNewAnswer(25, 0, 0) == 0,
            "A minimized transcript cannot create an invisible scrolling jump.");

        const string citedText = "Rocket Boots [1] or Wings [12]. Unknown [9], tag [i:1], plain URL https://example.com.";
        var cited = ChatTextLayout.WithCitations(ChatTextLayout.Highlight(citedText, new[] { new AnswerHighlight("Rocket Boots", "item") }), new[] { 1, 12 });
        Check(Text(cited) == citedText, "Citation metadata never changes literal answer text.");
        Check(cited.Count(span => span.Kind == AnswerTextKind.Citation) == 2 && cited.Any(span => span.LinkId == 12 && span.Text == "[12]"),
            "Only supplied numeric citation IDs become links.");
        Check(cited.Any(span => span.Kind == AnswerTextKind.Item && span.Text == "Rocket Boots"), "Citation linking preserves semantic item colors.");
        Check(cited.Where(span => span.LinkId != 0).All(span => span.Text is "[1]" or "[12]"), "Unverified markers, item tags and URLs never become clickable.");
        var linkedWrapped = ChatTextLayout.Wrap(cited, 10, Mono, 0);
        Check(string.Concat(linkedWrapped.SelectMany(line => line.Runs).Where(run => run.LinkId == 1).Select(run => run.Text)) == "[1]",
            "Citation identity survives wrapping and remains attached to its text.");
        var splitLink = ChatTextLayout.Wrap(ChatTextLayout.WithCitations(new[] { new ChatTextSpan("[12]", AnswerTextKind.Plain) }, new[] { 12 }), 1, Mono, 0);
        Check(splitLink.SelectMany(line => line.Runs).All(run => run.LinkId == 12) && string.Concat(splitLink.Select(Line)) == "[12]",
            "Even a tiny viewport keeps every fragment of a citation attached to the right source.");
        var adjacentLinks = ChatTextLayout.Wrap(ChatTextLayout.WithCitations(new[] { new ChatTextSpan("[1][2]", AnswerTextKind.Plain) }, new[] { 1, 2 }), 30, Mono, 0);
        Check(adjacentLinks[0].Runs.Count == 2 && adjacentLinks[0].Runs[0].LinkId == 1 && adjacentLinks[0].Runs[1].LinkId == 2 && adjacentLinks[0].Runs[1].X == 3,
            "Adjacent citations never merge into one target or alter glyph spacing.");

        foreach ((int width, int height) in new[] { (1, 1), (80, 50), (320, 180), (640, 360), (800, 600), (1280, 720), (1920, 1080) })
        {
            foreach (int preferred in new[] { int.MinValue, 820, int.MaxValue })
            {
                ChatPanelGeometry geometry = ChatPanelGeometry.Calculate(width, height, preferred, preferred);
                foreach (ChatRect rectangle in new[] { geometry.Panel, geometry.Transcript, geometry.Input, geometry.TerrariaTab,
                    geometry.GptTab, geometry.ConnectionBadge, geometry.ManageUsage, geometry.CopyAnswer })
                    Check(rectangle.Width >= 0 && rectangle.Height >= 0 && rectangle.X >= 0 && rectangle.Y >= 0 &&
                        (rectangle.Height == 0 || rectangle.Width == 0 || rectangle.Right <= width && rectangle.Bottom <= height),
                        $"Viewport {width}x{height}, preferred {preferred}: all visible rectangles fit.");
                Check(geometry.Transcript.Y + geometry.Transcript.Height <= geometry.Panel.Bottom,
                    $"Viewport {width}x{height}: transcript remains inside the panel.");
                if (geometry.ConnectionBadge.Height > 0)
                    Check(geometry.Transcript.Bottom <= geometry.ConnectionBadge.Y, $"Viewport {width}x{height}: footer cannot overlap transcript.");
            }
        }
        ChatPanelGeometry normal = ChatPanelGeometry.Calculate(1920, 1080, 820, 390);
        Check(normal.Input.X == 78 && normal.Panel.Width == 820 && normal.Panel.Height == 390, "Normal layouts preserve vanilla chat alignment and requested size.");
        return passed;
    }
}
