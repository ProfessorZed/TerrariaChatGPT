#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TerrariaChatGPT.Common.Memory;

// Model suggestions are untrusted. Only MemoryLedger can turn them into saved entries.
public sealed record MemoryProposal(string Kind, string Topic, string Text, string Evidence, string Item, string Target);

public sealed record MemoryEntry(string Id, string Kind, string Topic, string Text, string Evidence,
    string ItemKey, string Target, DateTimeOffset UpdatedUtc);

public sealed record MemoryChangeBatch(string Id, string Summary);

// Context intentionally excludes local identifiers, evidence quotes and timestamps.
public sealed record MemoryContextEntry(string Kind, string Topic, string Text, string ItemKey, string Target);
public sealed record MemoryContext(bool Enabled, IReadOnlyList<MemoryContextEntry> Entries);

public sealed record MemoryItemIdentity(string Key, string Name, string CanonicalName);

// Only public item families with deterministic game-side membership belong
// here. A model cannot invent a new group or expand a single named item.
public static class MemoryItemGroups
{
    private sealed record Definition(string Name, string Singular, params string[] Aliases);
    private static readonly Dictionary<string, Definition> Groups = new(StringComparer.Ordinal)
    {
        ["group:grappling_hooks"] = new("Grappling Hooks", "grappling hook", "grappling hooks"),
        ["group:weapon"] = new("Weapons", "weapon", "weapons"),
        ["group:tool"] = new("Tools", "tool", "tools"),
        ["group:armor"] = new("Armor", "", "armor", "armour"),
        ["group:accessory"] = new("Accessories", "accessory", "accessories"),
        ["group:ammo"] = new("Ammo", "", "ammo", "ammunition"),
        ["group:potion"] = new("Potions", "potion", "potions"),
        ["group:food"] = new("Food", "", "food"),
        ["group:block"] = new("Building Blocks", "block", "blocks", "building blocks", "building supplies"),
        ["group:wall"] = new("Walls", "wall", "walls"),
        ["group:furniture"] = new("Furniture", "", "furniture"),
        ["group:station"] = new("Crafting Stations", "crafting station", "crafting stations", "stations"),
        ["group:paint"] = new("Paints", "paint", "paints", "painting supplies"),
        ["group:plant"] = new("Plants", "plant", "plants", "herbs"),
        ["group:seed"] = new("Seeds", "seed", "seeds"),
        ["group:banner"] = new("Banners", "banner", "banners"),
        ["group:fishing"] = new("Fishing Items", "fishing item", "fishing items", "fishing gear", "fishing supplies"),
        ["group:consumable"] = new("Consumables", "consumable", "consumables"),
        ["group:pet"] = new("Pets", "pet", "pets", "pet items"),
        ["group:mount"] = new("Mounts", "mount", "mounts", "mount items"),
        ["group:dye"] = new("Dyes", "dye", "dyes"),
        ["group:vanity"] = new("Vanity Items", "vanity item", "vanity items", "vanity gear"),
        ["group:bosssummon"] = new("Boss Summons", "boss summon", "boss summons", "boss summoning items"),
        ["group:wiring"] = new("Wiring Items", "wiring item", "wiring items", "wiring supplies"),
        ["group:ore"] = new("Ores", "ore", "ores"),
        ["group:bar"] = new("Bars", "bar", "bars"),
        ["group:material"] = new("Materials", "material", "materials", "crafting materials")
    };

    public static bool IsKnown(string? key) => key is not null && Groups.ContainsKey(key);
    public static MemoryItemIdentity? TryResolve(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return null;
        string value = supplied.Trim().ToLowerInvariant();
        var found = Groups.FirstOrDefault(pair => pair.Key == value || pair.Value.Aliases.Contains(value, StringComparer.Ordinal));
        return found.Value is null ? null : new(found.Key, found.Value.Name, found.Value.Name);
    }

    public static bool IsMentioned(string message, string key)
    {
        if (!Groups.TryGetValue(key, out Definition? group)) return false;
        return Contains(message, key) || group.Aliases.Any(alias => Contains(message, alias)) || FamilyCue(message, group);
    }

    public static bool NamesOnlyOneItem(string message, string key)
    {
        if (!Groups.TryGetValue(key, out Definition? group) || group.Singular.Length == 0) return false;
        return Contains(message, group.Singular) && !IsMentioned(message, key);
    }

    private static bool FamilyCue(string message, Definition group) => group.Singular.Length != 0 && Regex.IsMatch(message,
        @"\b(?:all|every|any)\s+(?:(?:my|the)\s+)?" + Regex.Escape(group.Singular) + @"\b|\b" + Regex.Escape(group.Singular) + @"\s+(?:type|family|category)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static bool Contains(string message, string term) => Regex.IsMatch(message,
        @"(?<![\p{L}\p{N}_])" + Regex.Escape(term) + @"(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
}
