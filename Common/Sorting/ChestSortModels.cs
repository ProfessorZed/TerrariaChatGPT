#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaChatGPT.Common.Sorting;

[Flags]
public enum ChestSortCategory : long
{
    None = 0, Ore = 1L << 0, Bar = 1L << 1, Material = 1L << 2,
    Weapon = 1L << 3, Tool = 1L << 4, Armor = 1L << 5, Accessory = 1L << 6,
    Ammo = 1L << 7, Potion = 1L << 8, Food = 1L << 9, Block = 1L << 10,
    Wall = 1L << 11, Furniture = 1L << 12, Station = 1L << 13, Paint = 1L << 14,
    Plant = 1L << 15, Seed = 1L << 16, Banner = 1L << 17, Fishing = 1L << 18,
    Consumable = 1L << 19, Pet = 1L << 20, Mount = 1L << 21, Dye = 1L << 22,
    Vanity = 1L << 23, BossSummon = 1L << 24, Wiring = 1L << 25, Travel = 1L << 26
}

// Detached values only: neither the planner nor its output holds live game items.
public sealed record ChestSortItemSnapshot
{
    public int Slot { get; init; }
    public string ItemKey { get; init; } = string.Empty;
    public string StackKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string CanonicalName { get; init; } = string.Empty;
    public int Stack { get; init; }
    public int MaxStack { get; init; } = 1;
    public int Prefix { get; init; }
    public ChestSortCategory Categories { get; init; }
    public bool Favorite { get; init; }
    public bool CanMerge { get; init; } = true;
    public bool WholeStackOnly { get; init; }
    // Saved local preferences change destination selection only, never protection
    // or transfer constraints. A missing preferred destination leaves the item.
    public ChestSortCategory? PreferredCategory { get; init; }
    public string PreferredChestName { get; init; } = string.Empty;
    public IReadOnlyList<string> PreferenceGroups { get; init; } = Array.Empty<string>();
    public bool PreferenceConflict { get; init; }
    public bool UnassignedBySuggestion { get; init; }
}

public sealed record ChestSortChestSnapshot
{
    public int ChestId { get; init; }
    public string Name { get; init; } = string.Empty;
    public double DistanceSquared { get; init; }
    public int SlotCount { get; init; } = 40;
    public IReadOnlyList<ChestSortItemSnapshot> Items { get; init; } = Array.Empty<ChestSortItemSnapshot>();
}

public sealed record ChestSortMove
{
    public int SourceSlot { get; init; }
    public int ChestId { get; init; }
    public int DestinationSlot { get; init; }
    public int Amount { get; init; }
    public string ItemKey { get; init; } = string.Empty;
    public string StackKey { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string ChestName { get; init; } = string.Empty;
    public int Prefix { get; init; }
}

public enum ChestSortUnsortedReason { NoMatchingChest, NoSpace, AmbiguousCategory, UnsupportedItem, NotAssigned }

public sealed record ChestSortUnsortedItem
{
    public int Slot { get; init; }
    public string ItemKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Remaining { get; init; }
    public string SuggestedChest { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public ChestSortUnsortedReason Code { get; init; }
}

public sealed record ChestSortPlan
{
    public IReadOnlyList<ChestSortMove> Moves { get; init; } = Array.Empty<ChestSortMove>();
    public IReadOnlyList<ChestSortUnsortedItem> Unsorted { get; init; } = Array.Empty<ChestSortUnsortedItem>();
    public int MovedItemCount => Moves.Sum(move => move.Amount);
    public int MovedStackCount => Moves.Select(move => move.SourceSlot).Distinct().Count();
}
