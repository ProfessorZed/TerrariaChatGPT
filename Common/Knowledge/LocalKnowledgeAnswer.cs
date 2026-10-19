#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TerrariaChatGPT.Common.Knowledge;

/// <summary>Deterministic answers about owned items. No AI or world queries.</summary>
public static class LocalKnowledgeAnswer
{
    public static string Normalize(string value)
    {
        string text = value.ToLowerInvariant().Replace('×', 'x');
        if (text.Contains('\'') || text.Contains('’') || text.Contains('‘') || text.Contains('ʼ'))
            text = Regex.Replace(text, @"(?<=\p{L})['’‘ʼ](?=\p{L})", "");
        if (text.Contains('x') && text.Any(char.IsDigit))
        {
            // Normalize compact stack notation on both queries and public
            // names, so x10Torches does not hide the actual item name.
            text = Regex.Replace(text, @"\bx\s*(\d+)(?=\p{L})", "$1 ");
            text = Regex.Replace(text, @"(?<=\d)\s*x\s*(?=\p{L})", " ");
            text = Regex.Replace(text, @"(?<=\p{L})\s*x\s*(?=\d)", " x ");
        }
        return Regex.Replace(text, "[^\\p{L}\\p{N}]+", " ").Trim();
    }

    /// <summary>Public name variants: punctuation, possessives and the noun before "of" or "with".</summary>
    public static IReadOnlyList<string> ItemNameForms(string itemName)
    {
        string name = Normalize(itemName);
        if (name.Length == 0) return Array.Empty<string>();
        string[] words = name.Split(' ');
        int noun = words.Length - 1;
        for (int i = 1; i < words.Length; i++)
            if (words[i] is "of" or "with") { noun = i - 1; break; }
        var forms = new HashSet<string>(StringComparer.Ordinal) { name };
        string original = words[noun];
        words[noun] = Plural(original);
        forms.Add(string.Join(" ", words));
        if (original.Length > 3 && original.EndsWith("ies", StringComparison.Ordinal))
            words[noun] = original[..^3] + "y";
        else if (original.Length > 3 && (original.EndsWith("ches", StringComparison.Ordinal) || original.EndsWith("shes", StringComparison.Ordinal)
            || original.EndsWith("xes", StringComparison.Ordinal) || original.EndsWith("zes", StringComparison.Ordinal)))
            words[noun] = original[..^2];
        else if (original.Length > 2 && original.EndsWith("s", StringComparison.Ordinal) && !original.EndsWith("ss", StringComparison.Ordinal)
            && !original.EndsWith("us", StringComparison.Ordinal))
            words[noun] = original[..^1];
        else words[noun] = original;
        forms.Add(string.Join(" ", words));
        return forms.ToArray();
    }

    private static string Plural(string noun) => noun.Length > 1 && noun.EndsWith("y", StringComparison.Ordinal) && !"aeiou".Contains(noun[^2])
        ? noun[..^1] + "ies"
        : noun.EndsWith("s", StringComparison.Ordinal) || noun.EndsWith("x", StringComparison.Ordinal) || noun.EndsWith("z", StringComparison.Ordinal)
            || noun.EndsWith("ch", StringComparison.Ordinal) || noun.EndsWith("sh", StringComparison.Ordinal) ? noun + "es" : noun + "s";

    /// <summary>Matches complete public name variants, never a partial word such as "grassland".</summary>
    public static bool NameAppearsInQuestion(string question, string itemName)
    {
        string text = " " + Normalize(question) + " ";
        return ItemNameForms(itemName).Any(name => text.Contains(" " + name + " ", StringComparison.Ordinal));
    }

    public static bool IsInventoryQuestion(string question)
    {
        string text = Normalize(question);
        return Regex.IsMatch(text, @"\b(do i have|did i have|have i got|where is my|where are my|find my|locate my|in my|inventory|inventories|my chests|my storage|which chest)\b")
            || Regex.IsMatch(text, @"\bhow many\b.*\b(have|carrying|holding|own)\b");
    }

    public static bool NeedsCraftingAdvice(string question) => Regex.IsMatch(Normalize(question),
        @"\b(missing|ingredients?|materials?|recipes?|craft|crafting|crafted)\b|\b(can|could|should|do|did) (i|we|you) (craft|make|build|create)\b|\bhow (?:to )?(craft|make|build|create)\b|\b(how|what) (is|are)\b.*\b(made|crafted)\b|^(?:please )?(make|craft|build|create)\b|\bto (craft|make|build|create)\b|\b(need|needed|required|requirements)\b.*\b(for|craft|make)\b");

    /// <summary>Requests for recipes accepting an item, rather than recipes producing it.</summary>
    public static bool IsIngredientUsesQuestion(string question)
    {
        string text = Normalize(question);
        // "What is used to make X?" asks for X's ingredients. A named
        // subject before "used" instead asks what that subject can produce.
        if (Regex.IsMatch(text, @"^(?:please )?(?:what|which)\s+(?:(?:items?|ingredients?|materials?|weapons?|tools?)\s+)?(?:is|are)\s+(?:needed|required|used)\s+(?:to\s+(?:make|craft|build|create)|in|for)\b")
            || Regex.IsMatch(text, @"^(?:is|are)\b.*\bused\s+(?:in|for|to)\b"))
            return false;
        return Regex.IsMatch(text, @"\b(?:make|craft|build|create)\s+(?:with|using|from|out of)\b|\brecipes?\s+(?:using|with)\b|\b(?:what|which)\s+recipes?\s+(?:need|require|use)\b|\buses\s+(?:of|for)\b|\bused\s+(?:in|for)\b")
            || Regex.IsMatch(text, @"\b(?:what|which)(?:\s+(?:items?|things?|weapons?|tools?|potions?|armor|accessories|furniture|blocks?|materials?))?\s+(?:is|are|can be)\s+(?:made|crafted|built|created)\s+(?:from|with|using|out of)\b")
            || Regex.IsMatch(text, @"\b(?:what|which)\b.+\bused\s+to\s+(?:craft|make|build|create)\b")
            || Regex.IsMatch(text, @"\b(?:what|which)\b.+\b(?:craft|turn|upgrade)\b.*\binto\b");
    }

    public static string? TryAnswer(string question, KnowledgeSnapshot snapshot, string? resolvedItemKey = null, string? resolvedItemName = null)
    {
        if (!IsInventoryQuestion(question) || NeedsCraftingAdvice(question)) return null;
        string normalized = " " + Normalize(question) + " ";
        var allItems = snapshot.Inventory.Concat(snapshot.OwnedChests.SelectMany(chest => chest.Items)).ToArray();
        var matched = allItems.Where(item => NameAppearsInQuestion(question, item.Name))
            .OrderByDescending(item => item.Name.Length).FirstOrDefault();
        string? key = resolvedItemKey ?? matched?.ItemKey;
        string? name = resolvedItemName ?? matched?.Name;

        if (key is null)
        {
            if (normalized.Trim() is "inventory" or "my inventory" or "what is in my inventory" or "what s in my inventory" or "whats in my inventory")
            {
                var groups = snapshot.Inventory.GroupBy(item => item.Name)
                    .Select(group => $"{group.Key} x{group.Sum(item => item.Stack)}").Take(30).ToArray();
                return groups.Length == 0 ? "Your inventory and equipment are empty." : "Inventory and equipment: " + string.Join(", ", groups) + ".";
            }
            return "Which item should I look for? Try ‘Do I have Wood?’ or ‘Which chest has my Iron Bar?’ I can check your inventory and remembered contents of chests you have opened.";
        }

        ItemStackSnapshot[] carried = snapshot.Inventory.Where(item => item.ItemKey == key).ToArray();
        var foundChests = snapshot.OwnedChests
            .Select(chest => new { Chest = chest, Matches = chest.Items.Where(item => item.ItemKey == key).ToArray() })
            .Where(found => found.Matches.Length > 0).ToArray();
        var answer = new StringBuilder();
        int inventoryCount = carried.Sum(item => item.Stack);
        if (inventoryCount > 0)
        {
            answer.Append($"You have {inventoryCount} {name} with you: ");
            answer.Append(string.Join("; ", carried.Select(item => $"{item.Location} x{item.Stack}")));
            answer.Append('.');
        }
        else answer.Append($"I don't see {name} in your current inventory or equipment.");

        foreach (var found in foundChests.Take(8))
        {
            int count = found.Matches.Sum(item => item.Stack);
            string freshness = found.Chest.IsCurrentlyOpen ? "currently open" : $"last seen {found.Chest.LastObservedUtc.ToLocalTime():g}";
            answer.Append($"\n{found.Chest.Label}: {count} {name} ({freshness}).");
        }
        if (foundChests.Length == 0)
            answer.Append(" No match in the remembered contents of chests you have opened.");
        if (foundChests.Any(found => !found.Chest.IsCurrentlyOpen))
            answer.Append("\nClosed-chest contents are remembered, not checked live. Reopen a chest to refresh them.");
        answer.Append("\nUnopened and forgotten chests are never searched.");
        return answer.ToString();
    }
}
