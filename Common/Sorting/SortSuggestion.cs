#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TerrariaChatGPT.Common.Sorting;

/// <summary>Detached destinations proposed by GPT; never authorization to transfer items.</summary>
public sealed record SortAssignment(string ItemKey, string ChestName);
public sealed record SortSuggestion(IReadOnlyList<SortAssignment> Assignments);

public static class SortSuggestionValidator
{
    public const int MaximumAssignments = 40;

    /// <summary>Bind optional model metadata to the captured eligible items and observed chest names.
    /// Unknown rows and all conflicting rows for an item are excluded, never guessed.</summary>
    public static SortSuggestion? Validate(SortSuggestion? proposal, IEnumerable<string> allowedItemKeys,
        IEnumerable<string> knownChestNames)
    {
        if (proposal?.Assignments is null || proposal.Assignments.Count is < 1 or > MaximumAssignments) return null;
        HashSet<string> keys = allowedItemKeys.Where(IsItemKey).ToHashSet(StringComparer.Ordinal);
        var names = knownChestNames.Where(IsChestName).GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<SortAssignment> accepted = [];
        foreach (var group in proposal.Assignments.Where(row => row is not null && IsItemKey(row.ItemKey))
            .GroupBy(row => row.ItemKey, StringComparer.Ordinal))
        {
            if (!keys.Contains(group.Key)) continue;
            // Include invalid destinations in this check so a valid/invalid pair
            // cannot silently choose one side of a conflicting model proposal.
            SortAssignment first = group.First();
            if (!IsChestName(first.ChestName) || group.Any(row => !string.Equals(row.ChestName, first.ChestName, StringComparison.OrdinalIgnoreCase))
                || !names.TryGetValue(first.ChestName, out string? canonicalName)) continue;
            accepted.Add(new(group.Key, canonicalName));
        }
        return accepted.Count == 0 ? null : new(Array.AsReadOnly(accepted.ToArray()));
    }

    public static bool IsItemKey(string? value) => value is not null && value.Length is > 2 and <= 180
        && Regex.IsMatch(value, @"\A[A-Za-z0-9_]+/[A-Za-z0-9_]+\z", RegexOptions.CultureInvariant);

    public static bool IsChestName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80
        && value == value.Trim() && HasValidUnicode(value) && !value.Any(character => char.IsControl(character)
            || char.GetUnicodeCategory(character) == UnicodeCategory.Format || character is '[' or ']' or '{' or '}' or '<' or '>');

    private static bool HasValidUnicode(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (char.IsLowSurrogate(value[index])) return false;
            if (!char.IsHighSurrogate(value[index])) continue;
            if (++index >= value.Length || !char.IsLowSurrogate(value[index])) return false;
        }
        return true;
    }
}
