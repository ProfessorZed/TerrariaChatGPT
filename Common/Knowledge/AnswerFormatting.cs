#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TerrariaChatGPT.Common.Knowledge;

/// <summary>Literal display text only. No Terraria markup or executable links.</summary>
public static class AnswerFormatting
{
    private const int MaximumGlobalHighlights = 24;
    private const int MaximumLocalHighlights = 512;
    private const int MaximumLocalPhraseLength = 1024;
    private static readonly Regex SectionMarkers = new(
        @"(?<!\w)(?:(?<owned>You\s+have|Inventory(?:\s+and\s+equipment)?|Carried(?:\s+(?:items|materials|supplies))?|Open\s+chests?)\s*:|(?<missing>Still\s+need(?:\s+from\s+current\s+supplies)?|Missing(?:\s+(?:items|materials|ingredients))?)\s*:|(?<other>(?:Full\s+)?Recipe|Materials|Ingredients|Requirements|Remembered(?:\s+(?:items|supplies))?|In\s+chests|Crafting\s+station(?:\s+required)?|Station|Other\s+requirements|Next|Where)\s*:)|^\s*(?:(?<owned>You\s+have)\b|(?<missing>Still\s+need|Missing)\b|(?<other>(?:Full\s+)?Recipe|Materials|Ingredients|Requirements|Remembered|Station)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    public static string Clean(string text) => Clean(text, null);

    public static string Clean(string text, IEnumerable<int>? citationIndices)
    {
        text = Regex.Replace(text, @"\[([^\]\r\n]+)\]\(https?://[^)\s]+\)", "$1", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?m)^\s*(?:Wiki|Sources?):\s*https?://\S+\s*$", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"https?://[^\s<>]+", "", RegexOptions.IgnoreCase);
        var cited = citationIndices is null ? new HashSet<int>() : new HashSet<int>(citationIndices.Where(index => index is >= 1 and <= 24));
        text = Regex.Replace(text, @"\s*\[\d+(?:\s*[,;-]\s*\d+)*\]", match =>
        {
            string marker = match.Value.Trim();
            return int.TryParse(marker[1..^1], out int index) && cited.Contains(index) ? match.Value : "";
        });
        text = text.Replace("**", "").Replace("__", "").Replace("```", "");
        text = text.Replace("`", "");
        text = Regex.Replace(text, @"(?<!\w)\*([^*\n]+)\*(?!\w)", "$1");
        text = Regex.Replace(text, @"(?m)^\s*(?:Wiki|Sources?):\s*$", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", "");
        text = Regex.Replace(text, @"[ \t]+\r?$", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return text.Length > 12000 ? text[..12000] + "…" : text;
    }

    public static IReadOnlyList<AnswerHighlight> Validate(string text, IEnumerable<AnswerHighlight>? proposed)
    {
        if (proposed is null) return Array.Empty<AnswerHighlight>();
        var kinds = new Dictionary<string, string?>(StringComparer.Ordinal);
        var accepted = new List<AnswerHighlight>();
        var local = new Dictionary<(int Start, string Text), AnswerHighlight?>();
        foreach (AnswerHighlight highlight in proposed.Take(2048))
        {
            if (highlight is null || highlight.Kind is not ("location" or "item" or "missing" or "owned")
                || string.IsNullOrWhiteSpace(highlight.Text) || highlight.Text.Any(char.IsControl)) continue;
            if (highlight.Start is int start)
            {
                if (highlight.Text.Length > MaximumLocalPhraseLength || start < 0 || start > text.Length - highlight.Text.Length
                    || !text.AsSpan(start, highlight.Text.Length).SequenceEqual(highlight.Text.AsSpan())) continue;
                var key = (start, highlight.Text);
                if (!local.TryGetValue(key, out AnswerHighlight? previous)) local.Add(key, highlight);
                else if (previous is not null && previous.Kind != highlight.Kind)
                {
                    // The same occurrence cannot be both owned and missing.
                    // Contextual quantities otherwise outrank item-name color.
                    if (previous.Kind is "owned" or "missing" && highlight.Kind is "owned" or "missing") local[key] = null;
                    else if (Priority(highlight.Kind) > Priority(previous.Kind)) local[key] = highlight;
                }
                continue;
            }
            if (highlight.Text.Length > 120 || !text.Contains(highlight.Text, StringComparison.Ordinal)) continue;
            if (kinds.TryGetValue(highlight.Text, out string? kind))
            {
                // A phrase alone cannot distinguish two occurrences with
                // conflicting meanings. Leave both uncolored rather than
                // painting a missing quantity as owned (or vice versa).
                if (kind != highlight.Kind) kinds[highlight.Text] = null;
                continue;
            }
            kinds[highlight.Text] = highlight.Kind;
            accepted.Add(highlight);
        }
        return accepted.Where(highlight => kinds[highlight.Text] is not null).Take(MaximumGlobalHighlights)
            .Concat(local.Values.Where(highlight => highlight is not null).Select(highlight => highlight!).Take(MaximumLocalHighlights)).ToArray();
    }

    public static IReadOnlyList<AnswerHighlight> ForCrafting(CraftingAssessment plan)
    {
        string text = Clean(CraftingPlanner.Format(plan));
        var highlights = new List<AnswerHighlight>();
        AddMatches(highlights, text, Clean(plan.ResultName), "item");
        CraftingIngredientAssessment[] chestIngredients = plan.Ingredients.Where(item => item.OpenChestCount + item.RememberedChestCount > 0).ToArray();
        int chestRow = 0;
        foreach (Match row in Regex.Matches(text, @"[^\n]+"))
        {
            string line = row.Value;
            int start = row.Index, end = start + row.Length;
            if (line.StartsWith("Still need:", StringComparison.Ordinal))
            {
                AddMatches(highlights, text, "Still need:", "missing", start, end);
                foreach (var item in plan.Ingredients.Where(item => item.MissingCount > 0))
                    AddMatches(highlights, text, $"{item.MissingCount} {Clean(item.Name)}", "missing", start, end);
            }
            else if (line.StartsWith("You have:", StringComparison.Ordinal))
            {
                AddMatches(highlights, text, "You have:", "owned", start, end);
                foreach (var item in plan.Ingredients.Where(item => item.InventoryCount > 0))
                    AddMatches(highlights, text, $"{item.InventoryCount} {Clean(item.Name)}", "owned", start, end);
            }
            else if (line.StartsWith("You have all the ingredients in your inventory", StringComparison.Ordinal))
                AddMatches(highlights, text, "You have all the ingredients in your inventory", "owned", start, end);
            else if (line.StartsWith("Recipe:", StringComparison.Ordinal))
            {
                AddMatches(highlights, text, "Recipe:", "item", start, end);
                foreach (var item in plan.Ingredients)
                    AddMatches(highlights, text, $"{item.Required} {Clean(item.Name)}", "item", start, end);
            }
            else if (line.StartsWith("In chests:", StringComparison.Ordinal))
            {
                AddMatches(highlights, text, "In chests:", "location", start, end);
                if (chestRow >= chestIngredients.Length) continue;
                CraftingIngredientAssessment chestItem = chestIngredients[chestRow++];
                foreach (var source in chestItem.Sources.Where(source => source.LastObservedUtc.HasValue))
                {
                    AddMatches(highlights, text, Clean(source.Location), "location", start, end);
                }
                // The planner can aggregate several source stacks or summarize
                // extra chests. Color the quantities it actually printed.
                foreach (string name in chestItem.Sources.Select(source => Clean(source.ItemName)).Append(Clean(chestItem.Name)).Distinct(StringComparer.Ordinal))
                    foreach (Match count in Regex.Matches(line, @"(?<!\w)\d+\s+(?:more\s+)?" + Regex.Escape(name) + @"(?!\w)"))
                        AddMatches(highlights, text, count.Value, "item", start, end);
            }
            else if (line.StartsWith("Crafting station required:", StringComparison.Ordinal))
            {
                AddMatches(highlights, text, "Crafting station required:", "location", start, end);
                foreach (string station in plan.Stations)
                    AddMatches(highlights, text, Clean(station), "location", start, end);
            }
        }
        return Validate(text, highlights);
    }

    /// <summary>Supplement model metadata with names already present in detached reference data.</summary>
    public static IReadOnlyList<AnswerHighlight> ForResponse(string cleanedAnswer, KnowledgeSnapshot snapshot,
        IEnumerable<AnswerHighlight>? proposed)
    {
        var highlights = Validate(cleanedAnswer, ScopeRepeatedSemantics(cleanedAnswer, proposed)).ToList();
        IEnumerable<string> names = snapshot.ReferenceSubjects.Select(subject => subject.Name)
            .Concat(snapshot.Recipes.Select(recipe => recipe.ResultName))
            .Concat(snapshot.Recipes.SelectMany(recipe => recipe.Ingredients).Select(item => item.Name))
            .Concat(snapshot.Inventory.Select(item => item.Name));
        foreach (string name in names.Select(Clean).Where(name => name.Length > 1).Distinct(StringComparer.Ordinal)
            .OrderByDescending(name => name.Length).Take(256))
            AddMatches(highlights, cleanedAnswer, name, "item", ignoreCase: true);
        foreach (string station in snapshot.Recipes.SelectMany(recipe => recipe.Stations).Select(Clean)
            .Where(station => station.Length > 1).Distinct(StringComparer.Ordinal).Take(64))
            AddMatches(highlights, cleanedAnswer, station, "location", ignoreCase: true);
        // No inference of owned/missing quantities from prose: only the
        // deterministic planner or the supplied explicit metadata chooses those.
        return Validate(cleanedAnswer, highlights);
    }

    private static IEnumerable<AnswerHighlight> ScopeRepeatedSemantics(string text, IEnumerable<AnswerHighlight>? proposed)
    {
        if (proposed is null) yield break;
        Match[] markers = SectionMarkers.Matches(text).Cast<Match>().ToArray();
        foreach (AnswerHighlight original in proposed.Take(2048))
        {
            if (original is null) continue;
            AnswerHighlight highlight = original.Start is null && original.Text is not null
                ? original with { Text = original.Text.Trim() } : original;
            if (highlight.Start.HasValue || highlight.Kind is not ("missing" or "owned")
                || string.IsNullOrWhiteSpace(highlight.Text) || highlight.Text.Length > 120 || highlight.Text.Any(char.IsControl))
            {
                yield return highlight;
                continue;
            }
            var occurrences = new List<int>();
            int search = 0;
            while (search < text.Length)
            {
                int found = text.IndexOf(highlight.Text, search, StringComparison.Ordinal);
                if (found < 0) break;
                int after = found + highlight.Text.Length;
                if ((found == 0 || !IsWord(text[found - 1])) && (after == text.Length || !IsWord(text[after]))) occurrences.Add(found);
                search = after;
            }
            if (occurrences.Count <= 1)
            {
                yield return highlight;
                continue;
            }
            // One global quantity can occur in carried, missing and complete
            // recipe sections. Keep its semantic color only where the explicit
            // section establishes that meaning; public item fallback handles
            // other occurrences without inventing an inventory assertion.
            foreach (int start in occurrences)
                if (SemanticSection(text, start, markers) == highlight.Kind)
                    yield return highlight with { Start = start };
        }
    }

    private static string? SemanticSection(string text, int position, IReadOnlyList<Match> markers)
    {
        Match? marker = null;
        for (int i = 0; i < markers.Count && markers[i].Index <= position; i++) marker = markers[i];
        if (marker is null || marker.Index + marker.Length > position || marker.Groups["other"].Success) return null;
        string kind = marker.Groups["owned"].Success ? "owned" : "missing";
        int bodyStart = marker.Index + marker.Length;
        int markerLineEnd = text.IndexOf('\n', bodyStart);
        if (markerLineEnd < 0 || position <= markerLineEnd)
        {
            ReadOnlySpan<char> prefix = text.AsSpan(bodyStart, position - bodyStart);
            return prefix.IndexOfAny('.', '!', '?') >= 0 || prefix.Contains(':')
                || HasContextShift(prefix.ToString()) ? null : kind;
        }

        int lineStart = text.LastIndexOf('\n', position) + 1;
        foreach (string line in text[(markerLineEnd + 1)..lineStart].Split('\n'))
            if (line.Trim().Length > 0 && !IsListContinuation(line)) return null;
        int lineEnd = text.IndexOf('\n', position);
        if (lineEnd < 0) lineEnd = text.Length;
        return IsListContinuation(text[lineStart..lineEnd]) && !HasContextShift(text[lineStart..position]) ? kind : null;
    }

    private static bool IsListContinuation(string line) => Regex.IsMatch(line, @"^\s*(?:[-*•–]\s+|\d)");
    private static bool HasContextShift(string prefix) => Regex.IsMatch(prefix,
        @"\b(?:recipe|ingredients?|materials?|remembered|uses|requires|costs)\b|\bstill\s+need\b|\bmissing\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<AnswerHighlight> ForInventory(string text, KnowledgeSnapshot snapshot)
    {
        text = Clean(text);
        var highlights = new List<AnswerHighlight>();
        string[] names = snapshot.Inventory.Select(item => item.Name)
            .Concat(snapshot.OwnedChests.SelectMany(chest => chest.Items).Select(item => item.Name))
            .Concat(snapshot.ReferenceSubjects.Select(subject => subject.Name)).Select(Clean)
            .Where(name => name.Length > 1).Distinct(StringComparer.Ordinal).OrderByDescending(name => name.Length).ToArray();
        foreach (string name in names) AddMatches(highlights, text, name, "item");
        foreach (Match row in Regex.Matches(text, @"[^\n]+"))
        {
            int start = row.Index, end = start + row.Length;
            if (row.Value.StartsWith("Inventory and equipment:", StringComparison.Ordinal))
            {
                foreach (var group in snapshot.Inventory.GroupBy(item => Clean(item.Name), StringComparer.Ordinal))
                    AddMatches(highlights, text, $"{group.Key} x{group.Sum(item => item.Stack)}", "owned", start, end);
            }
            else if (row.Value.StartsWith("You have ", StringComparison.Ordinal))
            {
                foreach (var group in snapshot.Inventory.GroupBy(item => (item.ItemKey, Name: Clean(item.Name))))
                    AddMatches(highlights, text, $"{group.Sum(item => item.Stack)} {group.Key.Name}", "owned", start, end);
            }
            else if (row.Value.StartsWith("I don't see ", StringComparison.Ordinal))
                AddMatches(highlights, text, "I don't see", "missing", start, end);
            foreach (OwnedChestSnapshot chest in snapshot.OwnedChests)
            {
                string label = Clean(chest.Label);
                if (!row.Value.StartsWith(label + ":", StringComparison.Ordinal)) continue;
                AddMatches(highlights, text, label, "location", start, start + label.Length);
                foreach (var group in chest.Items.GroupBy(item => Clean(item.Name), StringComparer.Ordinal))
                    AddMatches(highlights, text, $"{group.Sum(item => item.Stack)} {group.Key}", "item", start, end);
            }
        }
        return Validate(text, highlights);
    }

    private static void AddMatches(List<AnswerHighlight> target, string text, string phrase, string kind, int start = 0, int end = -1, bool ignoreCase = false)
    {
        if (phrase.Length == 0 || phrase.Length > MaximumLocalPhraseLength || phrase.Any(char.IsControl) || target.Count >= MaximumLocalHighlights) return;
        if (end < 0) end = text.Length;
        while (start < end && target.Count < MaximumLocalHighlights)
        {
            int found = text.IndexOf(phrase, start, end - start, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (found < 0) break;
            int after = found + phrase.Length;
            string exact = ignoreCase ? text.Substring(found, phrase.Length) : phrase;
            if ((found == 0 || !IsWord(text[found - 1])) && (after == text.Length || !IsWord(text[after]))
                && !target.Any(existing => existing.Start == found && existing.Text == exact && existing.Kind == kind))
                target.Add(new AnswerHighlight(exact, kind) { Start = found });
            start = after;
        }
    }

    private static bool IsWord(char value) => char.IsLetterOrDigit(value) || value == '_';
    private static int Priority(string kind) => kind switch { "missing" => 4, "owned" => 3, "location" => 2, "item" => 1, _ => 0 };
}
