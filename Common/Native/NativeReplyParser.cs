#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TerrariaChatGPT.Common.Goals;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Memory;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.Sorting;

namespace TerrariaChatGPT.Common.Native;

public sealed record NativeSourceAnnotation(string Url, string Title, int Start, int End);
public sealed record NativeChatReply(string Answer, IReadOnlyList<AnswerHighlight> Highlights,
    IReadOnlyList<WebCitation> Citations, bool WebSearched, GoalProposal? GoalSuggestion)
{
    public IReadOnlyList<MemoryProposal> MemorySuggestions { get; init; } = Array.Empty<MemoryProposal>();
    public SortSuggestion? SortSuggestion { get; init; }
}

/// <summary>
/// Decodes a completed model reply into literal text and inert display metadata.
/// Only provider annotations can create source links. This class has no game,
/// account, network, browser or goal activation access.
/// </summary>
public static class NativeReplyParser
{
    private const int MaximumInput = 24000;
    private const int MaximumAnswer = 6000;
    private static readonly Regex Fence = new(@"\A\s*```(?:json)?\s*([\s\S]*?)\s*```\s*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Tail = new("\"\\s*,\\s*\"highlights\"\\s*:");
    private static readonly Regex PartialTail = new("\"\\s*,\\s*\"highlights(?:\"\\s*)?\\z");
    private static readonly Regex BrokenStart = new("^\\s*\"?(?:answer|highlights)\"\\s*:");
    private static readonly Regex GoalTail = new("[,}]\\s*\"goalSuggestion\"\\s*:");
    private static readonly Regex MemoryTail = new("[,}]\\s*\"memorySuggestions\"\\s*:");
    private static readonly Regex SortingTail = new("[,}]\\s*\"sortingSuggestion\"\\s*:");
    private static readonly Regex CitationMarkup = new(@"\A(?:\(?\[[^\]\n]+\]\(https?://[^\s]+\)\)?|\ue200[^\ue201]*\ue201|【[^】]*】)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 32 };
    private static readonly HashSet<string> BossKeys = new(GoalCatalog.Bosses.Select(boss => boss.Key), StringComparer.Ordinal);

    private sealed record Decoded(string Answer, int Start, int End, bool Escaped,
        IReadOnlyList<AnswerHighlight> Highlights, GoalProposal? Goal)
    {
        public IReadOnlyList<MemoryProposal> Memories { get; init; } = Array.Empty<MemoryProposal>();
        public SortSuggestion? Sorting { get; init; }
    }
    private sealed record SourceEdit(int Start, int End, string Replacement);

    public static NativeChatReply Parse(string text, IReadOnlyList<NativeSourceAnnotation> annotations, bool searched)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumInput) throw BadReply();
        string raw = text;
        Match fence = Fence.Match(raw);
        int offset = fence.Success ? fence.Groups[1].Index : 0;
        string value = fence.Success ? fence.Groups[1].Value : raw;
        if (!fence.Success && raw.TrimStart().StartsWith("```", StringComparison.Ordinal)) throw BadReply();
        Decoded decoded = Decode(value);
        if (HasEnvelopeFragment(decoded.Answer)) throw BadReply();

        string nonce = "TGPCITE" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var sources = new List<WebCitation>();
        var byUrl = new Dictionary<string, int>(StringComparer.Ordinal);
        var edits = new List<SourceEdit>();
        int[] mapping = AnswerMapping(value[decoded.Start..decoded.End], decoded.Escaped);
        foreach (NativeSourceAnnotation source in (annotations ?? Array.Empty<NativeSourceAnnotation>()).Take(100))
        {
            if (source is null || !SafeUrl(source.Url, out Uri? target) || source.Start < 0 || source.End < source.Start) continue;
            var candidates = new List<(int Start, int End)>();
            AddCandidate(source.Start, source.End);
            // API annotations can use Unicode code points; .NET and the local
            // transport use UTF-16. Prefer a candidate selecting citation markup.
            AddCandidate(CodePointBoundary(raw, source.Start), CodePointBoundary(raw, source.End));
            if (candidates.Count == 0) continue;
            var candidate = candidates[0];
            foreach (var pair in candidates)
                if (CitationMarkup.IsMatch(value[pair.Start..pair.End])) { candidate = pair; break; }
            int start = mapping[candidate.Start - decoded.Start], end = mapping[candidate.End - decoded.Start];
            if (start < 0 || end < start || !CharacterBoundary(decoded.Answer, start) || !CharacterBoundary(decoded.Answer, end)) continue;
            bool sameSpan = edits.Any(edit => edit.Start == start && edit.End == end);
            if (!sameSpan && edits.Any(edit => start < edit.End && end > edit.Start)) continue;
            string url = target!.AbsoluteUri;
            if (!byUrl.TryGetValue(url, out int number))
            {
                if (sources.Count >= 24) continue;
                number = sources.Count + 1;
                byUrl[url] = number;
                string title = CleanLabel(source.Title, 180);
                sources.Add(new(number, url, title.Length == 0 ? target.IdnHost : title));
            }
            string token = nonce + "N" + number + "END";
            int existing = edits.FindIndex(edit => edit.Start == start && edit.End == end);
            if (existing >= 0)
            {
                if (!edits[existing].Replacement.Contains(token, StringComparison.Ordinal))
                    edits[existing] = edits[existing] with { Replacement = edits[existing].Replacement + " " + token };
            }
            else
            {
                string fragment = decoded.Answer[start..end];
                edits.Add(new(start, end, CitationMarkup.IsMatch(fragment) ? token : fragment + " " + token));
            }

            void AddCandidate(int absoluteStart, int absoluteEnd)
            {
                int a = absoluteStart - offset, b = absoluteEnd - offset;
                if (absoluteStart < 0 || absoluteEnd < 0 || a < decoded.Start || b > decoded.End || b < a) return;
                if (mapping[a - decoded.Start] < 0 || mapping[b - decoded.Start] < 0) return;
                if (!candidates.Contains((a, b))) candidates.Add((a, b));
            }
        }
        string answer = decoded.Answer;
        foreach (SourceEdit edit in edits.OrderByDescending(edit => edit.Start))
            answer = answer[..edit.Start] + edit.Replacement + answer[edit.End..];
        answer = Sanitize(answer);
        foreach (WebCitation source in sources)
            answer = answer.Replace(nonce + "N" + source.Index + "END", "[" + source.Index + "]", StringComparison.Ordinal);
        if (answer.Length > MaximumAnswer)
        {
            int length = char.IsHighSurrogate(answer[MaximumAnswer - 1]) ? MaximumAnswer - 1 : MaximumAnswer;
            answer = answer[..length].TrimEnd();
            answer = Regex.Replace(answer, @"\s*\[\d{0,2}\z", "");
        }
        if (string.IsNullOrWhiteSpace(answer) || HasEnvelopeFragment(answer)) throw BadReply();
        sources = sources.Where(source => answer.Contains("[" + source.Index + "]", StringComparison.Ordinal)).ToList();
        IReadOnlyList<AnswerHighlight> highlights = AnswerFormatting.Validate(answer, decoded.Highlights);
        return new(answer, highlights, sources.AsReadOnly(), searched, decoded.Goal)
        { MemorySuggestions = decoded.Memories, SortSuggestion = decoded.Sorting };
    }

    private static Decoded Decode(string value)
    {
        string trimmed = value.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            JsonDocument? document = null;
            try { document = JsonDocument.Parse(value, JsonOptions); }
            catch (JsonException) { }
            if (document is not null)
            {
                using (document)
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count(field => field.Name == "answer") != 1
                        || !root.TryGetProperty("answer", out JsonElement answer) || answer.ValueKind != JsonValueKind.String
                        || !FindAnswer(value, out int start, out int end, out string decoded)) throw BadReply();
                    return new(decoded, start, end, true, ReadHighlights(root), ReadGoal(root))
                    { Memories = ReadMemories(root), Sorting = ReadSorting(root) };
                }
            }
            // A complete top-level answer followed by truncated optional JSON
            // metadata remains useful. Never render the broken tail as prose.
            if (trimmed.StartsWith('{') && FindAnswer(value, out int a, out int b, out string complete))
                return new(complete, a, b, true, Array.Empty<AnswerHighlight>(), null);
            throw BadReply();
        }
        MatchCollection markers = Tail.Matches(value);
        if (markers.Count != 0)
        {
            if (markers.Count != 1 || BrokenStart.IsMatch(value)) throw BadReply();
            Match marker = markers[0];
            string prefix = value[..marker.Index];
            if (string.IsNullOrWhiteSpace(prefix) || PartialTail.IsMatch(prefix)) throw BadReply();
            try
            {
                using JsonDocument suffix = JsonDocument.Parse("{\"highlights\":" + value[(marker.Index + marker.Length)..], JsonOptions);
                JsonElement root = suffix.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || root.EnumerateObject().Where(field => field.Name is not ("memorySuggestions" or "sortingSuggestion")).Any(field => HasDuplicates(field.Value))
                    || root.EnumerateObject().Where(field => field.Name is not ("memorySuggestions" or "sortingSuggestion")).GroupBy(field => field.Name).Any(group => group.Count() > 1)
                    || root.EnumerateObject().Any(field => field.Name is not ("highlights" or "goalSuggestion" or "memorySuggestions" or "sortingSuggestion"))
                    || !root.TryGetProperty("highlights", out JsonElement rows) || !StrictHighlights(rows)) throw BadReply();
                bool escaped = false;
                string answer = prefix;
                if (prefix.Contains('\\') && !prefix.Contains('\n') && !prefix.Contains('\r'))
                {
                    try { answer = JsonSerializer.Deserialize<string>("\"" + prefix + "\"")!; escaped = true; }
                    catch (JsonException) { }
                }
                return new(answer, 0, marker.Index, escaped, ReadHighlights(root), ReadGoal(root))
                { Memories = ReadMemories(root), Sorting = ReadSorting(root) };
            }
            catch (JsonException) { throw BadReply(); }
        }
        if (HasEnvelopeFragment(value)) throw BadReply();
        return new(value, 0, value.Length, false, Array.Empty<AnswerHighlight>(), null);
    }

    private static bool FindAnswer(string value, out int start, out int end, out string answer)
    {
        start = end = 0; answer = "";
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 32 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                bool wanted = reader.ValueTextEquals("answer");
                if (!reader.Read()) return false;
                if (wanted)
                {
                    if (reader.TokenType != JsonTokenType.String) return false;
                    start = Encoding.UTF8.GetCharCount(utf8.AsSpan(0, (int)reader.TokenStartIndex + 1));
                    end = Encoding.UTF8.GetCharCount(utf8.AsSpan(0, (int)reader.BytesConsumed - 1));
                    answer = reader.GetString()!;
                    return true;
                }
                if (!reader.TrySkip()) return false;
            }
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { } // A string can contain an invalid escaped surrogate.
        return false;
    }

    private static int[] AnswerMapping(string interior, bool escaped)
    {
        int[] map = Enumerable.Repeat(-1, interior.Length + 1).ToArray();
        int decoded = 0;
        for (int i = 0; i < interior.Length;)
        {
            map[i] = decoded;
            i += escaped && interior[i] == '\\' ? (interior[i + 1] == 'u' ? 6 : 2) : 1;
            decoded++;
            map[i] = decoded;
        }
        map[interior.Length] = decoded;
        return map;
    }

    private static int CodePointBoundary(string raw, int point)
    {
        if (point < 0) return -1;
        int index = 0;
        while (point > 0 && index < raw.Length)
        {
            index += char.IsHighSurrogate(raw[index]) && index + 1 < raw.Length && char.IsLowSurrogate(raw[index + 1]) ? 2 : 1;
            point--;
        }
        return point == 0 ? index : -1;
    }

    private static bool CharacterBoundary(string text, int index) => index >= 0 && index <= text.Length
        && !(index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]));

    private static bool HasEnvelopeFragment(string value) => Tail.IsMatch(value) || PartialTail.IsMatch(value)
        || BrokenStart.IsMatch(value) || GoalTail.IsMatch(value) || MemoryTail.IsMatch(value) || SortingTail.IsMatch(value);

    private static bool HasDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicates(property.Value)) return true;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray()) if (HasDuplicates(item)) return true;
        return false;
    }

    private static bool StrictHighlights(JsonElement rows) => rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() <= 24
        && rows.EnumerateArray().All(row => row.ValueKind == JsonValueKind.Object && row.EnumerateObject().Count() == 2
            && TryHighlight(row, out _));

    private static IReadOnlyList<AnswerHighlight> ReadHighlights(JsonElement root)
    {
        if (root.EnumerateObject().Count(field => field.Name == "highlights") != 1
            || !root.TryGetProperty("highlights", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
            return Array.Empty<AnswerHighlight>();
        var result = new List<AnswerHighlight>();
        foreach (JsonElement row in rows.EnumerateArray().Take(24))
            if (TryHighlight(row, out AnswerHighlight? highlight)) result.Add(highlight!);
        return result.AsReadOnly();
    }

    private static bool TryHighlight(JsonElement row, out AnswerHighlight? highlight)
    {
        highlight = null;
        if (row.ValueKind != JsonValueKind.Object || HasDuplicates(row)
            || !row.TryGetProperty("text", out JsonElement phrase) || phrase.ValueKind != JsonValueKind.String
            || !row.TryGetProperty("kind", out JsonElement category) || category.ValueKind != JsonValueKind.String) return false;
        string? phraseText = StringValue(phrase), categoryText = StringValue(category);
        if (phraseText is null || categoryText is null) return false;
        string text = phraseText, kind = categoryText;
        if (kind is not ("item" or "location" or "owned" or "missing") || string.IsNullOrWhiteSpace(text)
            || text.Length > 120 || text.Any(char.IsControl) || text.IndexOfAny(new[] { '[', ']', '<', '>' }) >= 0
            || Sanitize(text) != text) return false;
        highlight = new(text, kind);
        return true;
    }

    private static IReadOnlyList<MemoryProposal> ReadMemories(JsonElement root)
    {
        // Optional data must never discard a complete answer. Refuse ambiguous
        // or oversized metadata rather than silently persisting a partial set.
        if (root.EnumerateObject().Count(field => field.Name == "memorySuggestions") != 1
            || !root.TryGetProperty("memorySuggestions", out JsonElement rows)
            || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 3)
            return Array.Empty<MemoryProposal>();
        var result = new List<MemoryProposal>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || HasDuplicates(row)
                || row.EnumerateObject().Any(field => field.Name is not ("kind" or "topic" or "text" or "evidence" or "item" or "target"))
                || !Field(row, "kind", 12, out string kind) || kind is not ("preference" or "sorting")
                || !Field(row, "text", 240, out string text) || !NativeMemoryProtocol.Literal(text, 240)
                || !Field(row, "evidence", 180, out string evidence) || !NativeMemoryProtocol.Literal(evidence, 180)) continue;
            string topic = "", item = "", target = "";
            if (kind == "preference")
            {
                if (!Field(row, "topic", 24, out topic) || !NativeMemoryProtocol.Topic(topic)
                    || !EmptyOptional(row, "item") || !EmptyOptional(row, "target")) continue;
            }
            else
            {
                if (!Field(row, "item", 100, out item) || !NativeMemoryProtocol.Literal(item, 100)
                    || item.StartsWith("group:", StringComparison.Ordinal) && !NativeMemoryProtocol.ItemKey(item)
                    || !Field(row, "target", 110, out string suppliedTarget)
                    || NativeMemoryProtocol.Target(suppliedTarget) is not string validTarget) continue;
                target = validTarget;
            }
            result.Add(new(kind, topic, text, evidence, item, target));
        }
        return result.AsReadOnly();
    }

    private static bool EmptyOptional(JsonElement row, string name) => !row.TryGetProperty(name, out JsonElement field)
        || field.ValueKind == JsonValueKind.String && StringValue(field) == "";

    private static SortSuggestion? ReadSorting(JsonElement root)
    {
        // This is inert routing data. The host must bind every key and exact
        // chest label to its request snapshot, then revalidate the local preview.
        if (root.EnumerateObject().Count(field => field.Name == "sortingSuggestion") != 1
            || !root.TryGetProperty("sortingSuggestion", out JsonElement value) || value.ValueKind != JsonValueKind.Object
            || HasDuplicates(value) || value.EnumerateObject().Any(field => field.Name != "assignments")
            || !value.TryGetProperty("assignments", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() is < 1 or > 40) return null;
        var result = new List<SortAssignment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Count() != 2
                || !Field(row, "itemKey", 180, out string key) || !SortSuggestionValidator.IsItemKey(key)
                || StringValue(row.GetProperty("itemKey")) != key || !seen.Add(key)
                || !Field(row, "chestName", 80, out string chest) || !SortSuggestionValidator.IsChestName(chest)
                || StringValue(row.GetProperty("chestName")) != chest) return null;
            result.Add(new(key, chest));
        }
        return new(result.AsReadOnly());
    }

    private static GoalProposal? ReadGoal(JsonElement root)
    {
        if (root.EnumerateObject().Count(field => field.Name == "goalSuggestion") != 1
            || !root.TryGetProperty("goalSuggestion", out JsonElement value) || value.ValueKind != JsonValueKind.Object
            || HasDuplicates(value) || !Field(value, "title", 100, out string title)
            || !value.TryGetProperty("steps", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() is < 1 or > 24) return null;
        var steps = new List<object>();
        var seen = new HashSet<(string, string)>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !Field(row, "kind", 20, out string kind)
                || kind is not ("boss" or "item" or "craft" or "biome" or "npc" or "achievement" or "manual")
                || !Field(row, "label", 120, out string label)) return null;
            int target = 1;
            if (row.TryGetProperty("target", out JsonElement count)
                && (count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out target) || target is < 1 or > 999999)) return null;
            string key = "";
            if (row.TryGetProperty("key", out JsonElement id))
            {
                if (id.ValueKind != JsonValueKind.String) return null;
                string? keyText = StringValue(id);
                if (keyText is null) return null;
                key = keyText.Trim();
                if (key.Length > 128 || key.Any(char.IsControl)) return null;
            }
            if (key.Length == 0 || kind == "boss" && !BossKeys.Contains(key)) { kind = "manual"; key = ""; }
            if (kind == "boss" && key == "evil_boss") label = "Defeat Eater of Worlds or Brain of Cthulhu";
            if (seen.Add((kind, key.Length == 0 ? label : key))) steps.Add(new { kind, key, label, target });
        }
        if (steps.Count == 0) return null;
        using JsonDocument normalized = JsonDocument.Parse(JsonSerializer.Serialize(new { goalSuggestion = new { title, steps } }));
        return GoalProposalReader.Read(normalized.RootElement);
    }

    private static bool Field(JsonElement row, string name, int length, out string text)
    {
        text = "";
        if (!row.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return false;
        string? content = StringValue(value);
        if (content is null) return false;
        text = content.Trim();
        return text.Length > 0 && text.Length <= length && !text.Any(char.IsControl);
    }

    private static string? StringValue(JsonElement value)
    {
        try { return value.GetString(); }
        catch (InvalidOperationException) { return null; }
    }

    private static bool SafeUrl(string value, out Uri? uri)
    {
        uri = null;
        if (value is null || value.Any(character => char.IsWhiteSpace(character) || character == '\\')
            || !WebCitationLinks.TryGetPublicUri(value, out Uri? candidate)) return false;
        string host = candidate!.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.EndsWith(".test", StringComparison.Ordinal) || host.EndsWith(".invalid", StringComparison.Ordinal)
            || Regex.IsMatch(host, @"\A[0-9a-fA-FxX.]+\z")) return false;
        uri = candidate;
        return true;
    }

    private static string CleanLabel(string? value, int length) => new((value ?? "").Where(character => !char.IsControl(character)).Take(length).ToArray());

    private static string Sanitize(string value)
    {
        // Native text rendering is literal, but strip Terraria chat tags and
        // unverified provider glyphs as the companion did before transport.
        value = Regex.Replace(value, @"【[^】\n]*】|\ue200[^\ue201]*\ue201", "");
        value = Regex.Replace(value, @"\[c/[0-9a-fA-F]{6}:([^\]\n]*)\]", "$1");
        value = Regex.Replace(value, @"\[i(?:/[^:\]\n]*)?:[^\]\n]*\]", "");
        value = Regex.Replace(value, @"^[ \t]*#{1,6}\s+", "", RegexOptions.Multiline);
        value = Regex.Replace(value, @"^[ \t]*(?:Sources?|References?):[^\n]*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"[ \t]+", " ");
        value = Regex.Replace(value, @"[ \t]+([.,;!?])", "$1");
        // Retain complete Unicode pairs while removing isolated surrogate units.
        var clean = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i])) clean.Append(value[i]);
            else if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                clean.Append(value[i]).Append(value[++i]);
        }
        // Collapse/remove invalid units before Clean's own length bound so a
        // clipped private citation placeholder cannot shrink into view later.
        return AnswerFormatting.Clean(clean.ToString());
    }

    private static NativeChatException BadReply() => new("response_format", "ChatGPT returned an unreadable reply. Please try your question again.");
}
