#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaGPT.Common.Goals;

namespace TerrariaGPT.Common.Services;

/// <summary>Explicit bounded export. Never serialize a game object or legacy world snapshot.</summary>
public static class ObservedContextBuilder
{
    public static object Build(ProgressSnapshot snapshot, string question)
    {
        bool truncated = false;
        string query = question.ToLowerInvariant();
        int Relevance(string name, string key) => query.Contains(name.ToLowerInvariant(), StringComparison.Ordinal)
            || query.Contains(key.ToLowerInvariant(), StringComparison.Ordinal) ? 1 : 0;
        T[] Limit<T>(IEnumerable<T> values, int maximum)
        {
            T[] selection = values.Take(maximum + 1).ToArray();
            truncated |= selection.Length > maximum;
            return selection.Take(maximum).ToArray();
        }
        var inventory = Limit(snapshot.Inventory.Where(row => row.Stack > 0)
            .GroupBy(row => (row.ItemKey, row.Name, row.Location, row.CanUseForCrafting))
            .Select(rows => new { key = Clean(rows.Key.ItemKey, 128), name = Clean(rows.Key.Name, 120),
                count = Sum(rows.Select(row => row.Stack)), location = Clean(rows.Key.Location, 120),
                canUseForCrafting = rows.Key.CanUseForCrafting })
            .OrderByDescending(row => Relevance(row.name, row.key)).ThenBy(row => row.name, StringComparer.Ordinal), 120);
        var remembered = Limit(snapshot.RememberedItems.Where(row => row.Stack > 0)
            .GroupBy(row => (row.ItemKey, row.Name, row.ChestLabel, row.LastObservedUtc, row.IsCurrentlyOpen))
            .Select(rows => new { key = Clean(rows.Key.ItemKey, 128), name = Clean(rows.Key.Name, 120),
                count = Sum(rows.Select(row => row.Stack)), chest = ChestName(rows.Key.ChestLabel),
                lastObservedUtc = rows.Key.LastObservedUtc.ToUniversalTime().ToString("O"),
                isCurrentlyOpen = rows.Key.IsCurrentlyOpen })
            .OrderByDescending(row => Relevance(row.name, row.key)).ThenBy(row => row.name, StringComparer.Ordinal), 160);
        object[] Flags(IEnumerable<ProgressFlag> rows, int maximum) => Limit(rows
            .OrderByDescending(row => Relevance(row.Name, row.Key)).ThenByDescending(row => row.Completed), maximum)
            .Select(row => (object)new { key = Clean(row.Key, 128), name = Clean(row.Name, 120), completed = row.Completed,
                scope = row.Scope is "world" or "account" or "character-world" ? row.Scope : "character-world" }).ToArray();
        var bosses = Flags(snapshot.Bosses, 40);
        var worldProgress = Flags(snapshot.WorldProgress, 40);
        var achievements = Flags(snapshot.Achievements, 120);
        var biomes = Limit(snapshot.VisitedBiomes, 40)
            .Select(row => new { key = Clean(row.Key, 128), name = Clean(row.Name, 120) }).ToArray();
        var npcs = Limit(snapshot.KnownNpcs, 80)
            .Select(row => new { key = Clean(row.Key, 128), name = Clean(row.Name, 120), talkedTo = row.TalkedTo, rescued = row.Rescued }).ToArray();
        // Local biome observations are display names; transport accepts public
        // keys. Keep multi-word biomes instead of losing them at validation.
        var currentBiomes = Limit(snapshot.CurrentBiomes, 24).Select(row =>
            Clean(row.Contains('/', StringComparison.Ordinal) ? row : "Terraria/" + row.Replace(" ", ""), 128)).ToArray();
        return new { enabled = true, inventory, rememberedItems = remembered, bosses, worldProgress,
            achievements, biomes, npcs, currentBiomes, truncated };
    }

    private static int Sum(IEnumerable<int> values) => (int)Math.Min(999999, values.Sum(value => (long)Math.Max(0, value)));
    private static string Clean(string value, int maximum) => new string(value.Where(character => !char.IsControl(character)).Take(maximum).ToArray());
    private static string ChestName(string label) => label.StartsWith("Chest at tile ", StringComparison.OrdinalIgnoreCase)
        ? "Opened chest (remembered)" : Clean(label, 120);
}
