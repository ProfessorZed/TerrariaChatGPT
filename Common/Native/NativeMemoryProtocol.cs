#nullable enable
using System;
using System.Linq;
using TerrariaChatGPT.Common.Memory;

namespace TerrariaChatGPT.Common.Native;

/// <summary>Wire-only bounds. Durable intent, item identity and saving remain local host decisions.</summary>
internal static class NativeMemoryProtocol
{
    internal static bool Topic(string value) => value is "response_style" or "playstyle" or "accessibility"
        or "language" or "building" or "organization";

    private static readonly string[] Categories = { "Ore", "Bar", "Material", "Weapon", "Tool", "Armor", "Accessory",
        "Ammo", "Potion", "Food", "Block", "Wall", "Furniture", "Station", "Paint", "Plant", "Seed", "Banner",
        "Fishing", "Consumable", "Pet", "Mount", "Dye", "Vanity", "BossSummon", "Wiring", "Travel" };

    internal static bool ItemKey(string value) => value.StartsWith("group:", StringComparison.Ordinal)
        ? MemoryItemGroups.IsKnown(value)
        : System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z");

    internal static string? Target(string value)
    {
        if (!Literal(value, 110)) return null;
        if (value.StartsWith("chest:", StringComparison.Ordinal))
        {
            string label = value[6..];
            return Literal(label, 80) && label == label.Trim() ? value : null;
        }
        string compact = value.Replace("_", "", StringComparison.Ordinal);
        return Categories.FirstOrDefault(category => category.Equals(compact, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool Literal(string value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && !value.Any(char.IsControl) && ValidUnicode(value)
        && value.IndexOfAny(new[] { '<', '>', '[', ']' }) < 0;

    private static bool ValidUnicode(string value)
    {
        for (int i = 0; i < value.Length; i++)
            if (char.IsSurrogate(value[i]) && (!char.IsHighSurrogate(value[i]) || ++i >= value.Length || !char.IsLowSurrogate(value[i])))
                return false;
        return true;
    }
}
