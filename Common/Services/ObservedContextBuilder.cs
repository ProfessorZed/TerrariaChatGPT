#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TerrariaChatGPT.Common.Goals;

namespace TerrariaChatGPT.Common.Services;

/// <summary>Explicit bounded export. Never serialize a game object or legacy world snapshot.</summary>
public static class ObservedContextBuilder
{
    private static readonly Regex PublicKey = new(@"\A[A-Za-z0-9_.:/-]{1,128}\z", RegexOptions.CultureInvariant);

    public static object Build(ProgressSnapshot snapshot, string question, IReadOnlyList<ProgressSubject>? sortCandidates = null)
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
        var sortingCandidates = Limit((sortCandidates ?? Array.Empty<ProgressSubject>())
            .Where(row => row.Key is { Length: > 2 and <= 180 }
                && Regex.IsMatch(row.Key, @"\A[A-Za-z0-9_]+/[A-Za-z0-9_]+\z", RegexOptions.CultureInvariant)
                && !string.IsNullOrWhiteSpace(row.Name))
            .GroupBy(row => row.Key, StringComparer.Ordinal).Select(rows => rows.First()), 40)
            .Select(row => new { key = row.Key, name = Clean(row.Name, 120) }).ToArray();
        var remembered = Limit(snapshot.RememberedItems.Where(row => row.Stack > 0)
            .GroupBy(row => (row.ItemKey, row.Name, row.ChestLabel, row.LastObservedUtc, row.IsCurrentlyOpen))
            .Select(rows => new { key = Clean(rows.Key.ItemKey, 128), name = Clean(rows.Key.Name, 120),
                count = Sum(rows.Select(row => row.Stack)), chest = ChestName(rows.Key.ChestLabel),
                lastObservedUtc = rows.Key.LastObservedUtc.ToUniversalTime().ToString("O"),
                isCurrentlyOpen = rows.Key.IsCurrentlyOpen })
            .OrderByDescending(row => Relevance(row.name, row.key)).ThenBy(row => row.name, StringComparer.Ordinal), 160);
        // Names are useful even for empty known chests. Export no coordinates,
        // chest IDs, live contents or claims that a remembered chest is nearby.
        var knownChests = Limit(snapshot.KnownChests.Where(row => !string.IsNullOrWhiteSpace(row.Name)
                && !row.Name.StartsWith("Chest at tile ", StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => Clean(row.Name, 80), StringComparer.OrdinalIgnoreCase)
            .Select(rows => rows.OrderByDescending(row => row.LastObservedUtc).First())
            .OrderByDescending(row => Relevance(row.Name, row.Name)).ThenBy(row => row.Name, StringComparer.Ordinal), 80)
            .Select(row => new { name = Clean(row.Name, 80), lastObservedUtc = row.LastObservedUtc.ToUniversalTime().ToString("O") }).ToArray();
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
        var npcHappiness = Limit(snapshot.NpcHappiness
            .Where(row => row != null && row.Key != null && PublicKey.IsMatch(row.Key) && !string.IsNullOrWhiteSpace(Clean(row.Name, 120))
                && double.IsFinite(row.PriceAdjustment) && row.PriceAdjustment >= 0.01 && row.PriceAdjustment <= 100
                && !string.IsNullOrWhiteSpace(Clean(row.HappinessReport, 1200)))
            .OrderByDescending(row => Relevance(row.Name, row.Key)).ThenByDescending(row => row.IsCurrent)
            .ThenByDescending(row => row.LastObservedUtc), 80)
            .Select(row => new { key = row.Key, name = Clean(row.Name, 120), priceAdjustment = row.PriceAdjustment,
                pricePercent = Math.Round(row.PriceAdjustment * 100, 2, MidpointRounding.AwayFromZero),
                priceChangePercent = Math.Round((row.PriceAdjustment - 1) * 100, 2, MidpointRounding.AwayFromZero),
                happinessReport = Clean(row.HappinessReport, 1200), lastObservedUtc = row.LastObservedUtc.ToUniversalTime().ToString("O"),
                isCurrent = row.IsCurrent, observedBiomes = Limit((row.ObservedBiomes ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Select(BiomeKey).Where(value => PublicKey.IsMatch(value)).Distinct(StringComparer.Ordinal), 24) })
            .ToArray();
        // Local biome observations are display names; transport accepts public
        // keys. Keep multi-word biomes instead of losing them at validation.
        var currentBiomes = Limit(snapshot.CurrentBiomes, 24).Select(row =>
            BiomeKey(row)).ToArray();
        return new { enabled = true, capturedAtUtc = snapshot.CapturedAtUtc.ToUniversalTime().ToString("O"), inventory, rememberedItems = remembered, bosses, worldProgress,
            achievements, biomes, npcs, npcHappiness, knownChests, sortingCandidates, currentBiomes, truncated };
    }

    private static string BiomeKey(string value) => Clean(value.Contains('/', StringComparison.Ordinal)
        ? value : "Terraria/" + value.Replace(" ", ""), 128);
    private static int Sum(IEnumerable<int> values) => (int)Math.Min(999999, values.Sum(value => (long)Math.Max(0, value)));
    private static string Clean(string? value, int maximum) => new string((value ?? "").Where(character => !char.IsControl(character)).Take(maximum).ToArray());
    private static string ChestName(string label) => label.StartsWith("Chest at tile ", StringComparison.OrdinalIgnoreCase)
        ? "Opened chest (remembered)" : Clean(label, 120);
}
