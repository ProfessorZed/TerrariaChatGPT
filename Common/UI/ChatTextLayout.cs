#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TerrariaChatGPT.Common.Knowledge;

namespace TerrariaChatGPT.Common.UI;

internal enum AnswerTextKind : byte { Plain, Muted, Location, Item, Missing, Owned, Citation }
internal readonly record struct ChatTextSpan(string Text, AnswerTextKind Kind, int LinkId = 0);
internal readonly record struct ChatGlyphMetrics(float Left, float Width, float Right);
internal readonly record struct ChatTextRun(string Text, float X, AnswerTextKind Kind, int LinkId = 0);
internal sealed record ChatTextLine(IReadOnlyList<ChatTextRun> Runs, float Width);

/// <summary>
/// Literal text, bounded semantic highlights, and a linear glyph-metric layout.
/// It has no game state, graphics device, or Terraria markup dependency.
/// </summary>
internal static class ChatTextLayout
{
    private const int MaximumHighlights = 560;
    private const int MaximumMatches = 96;
    private const int MaximumPhraseLength = 120;

    public static IReadOnlyList<ChatTextSpan> Highlight(string text, IReadOnlyList<AnswerHighlight>? highlights)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<ChatTextSpan>();
        if (highlights is null || highlights.Count == 0)
            return new[] { new ChatTextSpan(text, AnswerTextKind.Plain) };

        var phrases = new List<HighlightPhrase>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var matches = new List<HighlightMatch>();
        foreach (AnswerHighlight highlight in highlights.Take(MaximumHighlights))
        {
            if (highlight is null || !TryKind(highlight.Kind, out AnswerTextKind kind)) continue;
            if (highlight.Start is int scopedStart)
            {
                string exact = highlight.Text ?? string.Empty;
                if (exact.Length == 0 || exact.Length > 1024 || exact.Any(char.IsControl) || scopedStart < 0
                    || scopedStart > text.Length - exact.Length || !text.AsSpan(scopedStart, exact.Length).SequenceEqual(exact.AsSpan())) continue;
                matches.Add(new HighlightMatch(scopedStart, exact.Length, kind));
                continue;
            }
            string phrase = highlight.Text?.Trim() ?? string.Empty;
            if (phrase.Length == 0 || phrase.Length > MaximumPhraseLength || phrase.Any(char.IsControl)) continue;
            if (unique.Add(((int)kind).ToString(CultureInfo.InvariantCulture) + ":" + phrase))
                phrases.Add(new HighlightPhrase(phrase, kind));
        }
        phrases.Sort((a, b) =>
        {
            int priority = Priority(b.Kind).CompareTo(Priority(a.Kind));
            return priority != 0 ? priority : b.Text.Length.CompareTo(a.Text.Length);
        });
        int globalMatches = 0;
        foreach (HighlightPhrase candidate in phrases)
        {
            string phrase = candidate.Text;
            AnswerTextKind kind = candidate.Kind;
            int search = 0;
            int repeated = 0;
            while (search < text.Length && repeated < 12 && globalMatches < MaximumMatches)
            {
                int start = text.IndexOf(phrase, search, StringComparison.Ordinal);
                if (start < 0) break;
                int end = start + phrase.Length;
                if ((start == 0 || !IsWord(phrase[0]) || !IsWord(text[start - 1])) &&
                    (end == text.Length || !IsWord(phrase[^1]) || !IsWord(text[end])))
                {
                    matches.Add(new HighlightMatch(start, phrase.Length, kind));
                    repeated++;
                    globalMatches++;
                }
                search = end;
            }
        }

        // A missing-material phrase must win over its item name. The same
        // fixed rule applies regardless of the order of remote metadata.
        matches.Sort((a, b) =>
        {
            int priority = Priority(b.Kind).CompareTo(Priority(a.Kind));
            if (priority != 0) return priority;
            int length = b.Length.CompareTo(a.Length);
            return length != 0 ? length : a.Start.CompareTo(b.Start);
        });
        var selected = new List<HighlightMatch>();
        foreach (HighlightMatch match in matches)
            if (!selected.Any(existing => match.Start < existing.Start + existing.Length && existing.Start < match.Start + match.Length))
                selected.Add(match);
        selected.Sort((a, b) => a.Start.CompareTo(b.Start));

        var spans = new List<ChatTextSpan>();
        int position = 0;
        foreach (HighlightMatch match in selected)
        {
            if (position < match.Start) AddSpan(spans, text[position..match.Start], AnswerTextKind.Plain);
            AddSpan(spans, text.Substring(match.Start, match.Length), match.Kind);
            position = match.Start + match.Length;
        }
        if (position < text.Length) AddSpan(spans, text[position..], AnswerTextKind.Plain);
        return spans;
    }

    // Only trusted source IDs supplied by the service become links. Text such
    // as [i:1], invented markers, URLs and remote markup stays literal.
    public static IReadOnlyList<ChatTextSpan> WithCitations(IReadOnlyList<ChatTextSpan> spans, IReadOnlyCollection<int> sourceIds)
    {
        if (sourceIds.Count == 0) return spans;
        string text = string.Concat(spans.Select(span => span.Text));
        var links = new int[text.Length];
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"\[(\d{1,2})\]"))
            if (int.TryParse(match.Groups[1].Value, out int id) && id >= 1 && id <= 24 && sourceIds.Contains(id))
                for (int at = match.Index; at < match.Index + match.Length; at++) links[at] = id;
        var result = new List<ChatTextSpan>();
        int offset = 0;
        foreach (ChatTextSpan span in spans)
        {
            int start = 0;
            while (start < span.Text.Length)
            {
                int id = links[offset + start];
                int end = start + 1;
                while (end < span.Text.Length && links[offset + end] == id) end++;
                result.Add(new ChatTextSpan(span.Text[start..end], id > 0 ? AnswerTextKind.Citation : span.Kind, id));
                start = end;
            }
            offset += span.Text.Length;
        }
        return result;
    }

    public static IReadOnlyList<ChatTextLine> Wrap(IReadOnlyList<ChatTextSpan> spans, float width,
        Func<char, ChatGlyphMetrics> glyph, float characterSpacing)
    {
        width = Math.Max(1, float.IsFinite(width) ? width : 1);
        int estimatedLength = 0;
        foreach (ChatTextSpan span in spans) estimatedLength += span.Text.Length;
        var textBuilder = new StringBuilder(estimatedLength);
        var kinds = new List<AnswerTextKind>(estimatedLength);
        var links = new List<int>(estimatedLength);
        foreach (ChatTextSpan span in spans)
        {
            foreach (char character in span.Text)
            {
                if (character == '\r') continue;
                if (character == '\t')
                {
                    textBuilder.Append("    ");
                    for (int i = 0; i < 4; i++) { kinds.Add(span.Kind); links.Add(span.LinkId); }
                }
                else
                {
                    textBuilder.Append(character);
                    kinds.Add(span.Kind);
                    links.Add(span.LinkId);
                }
            }
        }
        string text = textBuilder.ToString();
        var lines = new List<ChatTextLine>();
        int paragraphStart = 0;
        while (paragraphStart <= text.Length)
        {
            int paragraphEnd = text.IndexOf('\n', paragraphStart);
            if (paragraphEnd < 0) paragraphEnd = text.Length;
            if (paragraphEnd == paragraphStart)
                lines.Add(new ChatTextLine(Array.Empty<ChatTextRun>(), 0));
            else
                WrapParagraph(text, kinds, links, paragraphStart, paragraphEnd, width, glyph, characterSpacing, lines);
            if (paragraphEnd == text.Length) break;
            paragraphStart = paragraphEnd + 1;
        }
        return lines;
    }

    private static void WrapParagraph(string text, List<AnswerTextKind> kinds, List<int> links, int start, int end, float width,
        Func<char, ChatGlyphMetrics> glyph, float spacing, List<ChatTextLine> output)
    {
        while (start < end)
        {
            float pen = 0;
            float right = 0;
            int fit = start;
            int lastSpace = -1;
            int cursor = start;
            while (cursor < end)
            {
                // Keep surrogate pairs and combining sequences together when
                // a narrow window forces a word to break.
                int elementLength = StringInfo.GetNextTextElementLength(text, cursor);
                elementLength = Math.Min(elementLength, end - cursor);
                float nextPen = pen;
                float nextRight = right;
                for (int i = cursor; i < cursor + elementLength; i++)
                {
                    ChatGlyphMetrics metrics = glyph(text[i]);
                    nextPen += i == start ? Math.Max(metrics.Left, 0) : spacing + nextRight + metrics.Left;
                    nextPen += metrics.Width;
                    nextRight = metrics.Right;
                }
                if (nextPen + Math.Max(nextRight, 0) > width && cursor > start) break;
                pen = nextPen;
                right = nextRight;
                fit = cursor + elementLength;
                if (text[cursor] == ' ') lastSpace = cursor;
                cursor += elementLength;
                if (pen + Math.Max(right, 0) > width) break;
            }
            int takeEnd = fit;
            if (fit < end && text[fit] != ' ' && lastSpace > start) takeEnd = lastSpace;
            int visibleEnd = takeEnd;
            while (visibleEnd > start && text[visibleEnd - 1] == ' ') visibleEnd--;
            output.Add(BuildLine(text, kinds, links, start, visibleEnd, glyph, spacing));
            start = takeEnd;
            while (start < end && text[start] == ' ') start++;
        }
    }

    private static ChatTextLine BuildLine(string text, List<AnswerTextKind> kinds, List<int> links, int start, int end,
        Func<char, ChatGlyphMetrics> glyph, float spacing)
    {
        var runs = new List<ChatTextRun>(2);
        float pen = 0;
        float right = 0;
        int runStart = start;
        float runX = 0;
        for (int i = start; i < end; i++)
        {
            ChatGlyphMetrics metrics = glyph(text[i]);
            float beforeGlyph = i == start ? 0 : pen + spacing + right;
            float left = i == start ? Math.Max(metrics.Left, 0) : metrics.Left;
            if (i > start && (kinds[i] != kinds[runStart] || links[i] != links[runStart]))
            {
                runs.Add(new ChatTextRun(text[runStart..i], runX, kinds[runStart], links[runStart]));
                runStart = i;
                // Drawing a separate colored run resets first-glyph bearing.
                // Compensate so color changes never change letter spacing.
                runX = beforeGlyph + metrics.Left - Math.Max(metrics.Left, 0);
            }
            pen = beforeGlyph + left + metrics.Width;
            right = metrics.Right;
        }
        if (runStart < end) runs.Add(new ChatTextRun(text[runStart..end], runX, kinds[runStart], links[runStart]));
        return new ChatTextLine(runs, pen + Math.Max(right, 0));
    }

    private static bool TryKind(string? text, out AnswerTextKind kind)
    {
        kind = text?.ToLowerInvariant() switch
        {
            "location" => AnswerTextKind.Location,
            "item" => AnswerTextKind.Item,
            "missing" => AnswerTextKind.Missing,
            "owned" => AnswerTextKind.Owned,
            _ => AnswerTextKind.Plain
        };
        return kind != AnswerTextKind.Plain;
    }

    private static int Priority(AnswerTextKind kind) => kind switch
    {
        AnswerTextKind.Missing => 4, AnswerTextKind.Owned => 3,
        AnswerTextKind.Location => 2, AnswerTextKind.Item => 1, _ => 0
    };
    private static bool IsWord(char value) => char.IsLetterOrDigit(value) || value == '_';
    private static void AddSpan(List<ChatTextSpan> spans, string text, AnswerTextKind kind)
    {
        if (text.Length == 0) return;
        if (spans.Count > 0 && spans[^1].Kind == kind)
            spans[^1] = new ChatTextSpan(spans[^1].Text + text, kind);
        else spans.Add(new ChatTextSpan(text, kind));
    }
    private readonly record struct HighlightMatch(int Start, int Length, AnswerTextKind Kind);
    private readonly record struct HighlightPhrase(string Text, AnswerTextKind Kind);
}
