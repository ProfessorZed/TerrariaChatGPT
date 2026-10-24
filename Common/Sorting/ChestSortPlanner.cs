#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TerrariaChatGPT.Common.Sorting;

/// <summary>Produces a preview without reading or changing any live game state.</summary>
public static class ChestSortPlanner
{
    private sealed record LabelRule(string[] Aliases, ChestSortCategory Categories, int Specificity);
    private static readonly LabelRule[] Labels =
    [
        new(["ore", "ores"], ChestSortCategory.Ore, 100),
        new(["bar", "bars", "ingot", "ingots"], ChestSortCategory.Bar, 100),
        new(["material", "materials", "resource", "resources", "ingredient", "ingredients"], ChestSortCategory.Material, 20),
        new(["weapon", "weapons"], ChestSortCategory.Weapon, 90),
        new(["tool", "tools"], ChestSortCategory.Tool, 110),
        new(["armor", "armour"], ChestSortCategory.Armor, 100),
        new(["accessory", "accessories"], ChestSortCategory.Accessory, 100),
        new(["equipment", "gear"], ChestSortCategory.Armor | ChestSortCategory.Accessory | ChestSortCategory.Vanity | ChestSortCategory.Pet | ChestSortCategory.Mount, 50),
        new(["ammo", "ammunition"], ChestSortCategory.Ammo, 110),
        new(["potion", "potions", "elixir", "elixirs"], ChestSortCategory.Potion, 100),
        new(["food", "foods", "meal", "meals", "cooking"], ChestSortCategory.Food, 100),
        new(["block", "blocks"], ChestSortCategory.Block, 90),
        new(["building", "construction"], ChestSortCategory.Block | ChestSortCategory.Wall | ChestSortCategory.Paint, 85),
        new(["wall", "walls"], ChestSortCategory.Wall, 100),
        new(["furniture", "furnishings", "decoration", "decorations", "decor"], ChestSortCategory.Furniture, 80),
        new(["station", "stations"], ChestSortCategory.Station, 100),
        new(["paint", "painting", "paints"], ChestSortCategory.Paint, 110),
        new(["plant", "plants", "herb", "herbs", "flower", "flowers"], ChestSortCategory.Plant, 90),
        new(["seed", "seeds"], ChestSortCategory.Seed, 100),
        new(["banner", "banners"], ChestSortCategory.Banner, 110),
        new(["fishing"], ChestSortCategory.Fishing, 110),
        new(["consumable", "consumables"], ChestSortCategory.Consumable | ChestSortCategory.Ammo | ChestSortCategory.Potion | ChestSortCategory.Food | ChestSortCategory.BossSummon, 40),
        new(["pet", "pets"], ChestSortCategory.Pet, 110),
        new(["mount", "mounts"], ChestSortCategory.Mount, 110),
        new(["dye", "dyes"], ChestSortCategory.Dye, 110),
        new(["vanity", "costume", "costumes"], ChestSortCategory.Vanity, 110),
        new(["travel", "mobility", "movement", "flight", "teleport", "teleportation", "transport", "transportation"], ChestSortCategory.Travel, 120),
        new(["summon", "summons", "boss summon", "boss summons", "boss summoning"], ChestSortCategory.BossSummon, 110),
        new(["wire", "wires", "wiring", "mechanism", "mechanisms"], ChestSortCategory.Wiring, 110)
    ];
    private const ChestSortCategory PhysicalResources = ChestSortCategory.Ore | ChestSortCategory.Bar |
        ChestSortCategory.Plant | ChestSortCategory.Seed | ChestSortCategory.Material;

    public static ChestSortPlan Build(IReadOnlyList<ChestSortItemSnapshot> inventory, IReadOnlyList<ChestSortChestSnapshot> chests)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(chests);
        List<WorkingChest> destinations = PrepareChests(chests);
        List<ChestSortMove> moves = [];
        List<ChestSortUnsortedItem> unsorted = [];
        // Conflicting observations of a slot cannot authorize moving it.
        IEnumerable<ChestSortItemSnapshot> sources = inventory.Where(item => item is not null)
            .GroupBy(item => item.Slot).Where(group => group.Count() == 1).Select(group => group.Single())
            .Where(item => item.Slot >= 10 && item.Slot <= 49 && !item.Favorite && item.Stack > 0)
            .OrderBy(item => item.Slot);

        foreach (ChestSortItemSnapshot item in sources)
        {
            if (item.UnassignedBySuggestion)
            {
                unsorted.Add(Unsorted(item, item.Stack, ChestSortUnsortedReason.NotAssigned,
                    "No destination was included for this item in GPT's choices; left in inventory.", SuggestChest(item.Categories)));
                continue;
            }
            if (item.PreferenceConflict)
            {
                unsorted.Add(Unsorted(item, item.Stack, ChestSortUnsortedReason.AmbiguousCategory,
                    "Saved sorting preferences disagree for this item. Choose its category in chat or edit the conflicting memories.", ItemLabel(item)));
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.ItemKey) || item.MaxStack <= 0 || item.Stack > item.MaxStack)
            {
                unsorted.Add(Unsorted(item, item.Stack, ChestSortUnsortedReason.UnsupportedItem,
                    "Item data could not be safely matched; left in inventory."));
                continue;
            }
            ChestSortCategory categories = item.PreferredCategory ?? EffectiveCategories(item.Categories);
            string suggestion = !string.IsNullOrEmpty(item.PreferredChestName) ? item.PreferredChestName
                : categories == ChestSortCategory.None ? ItemLabel(item) : SuggestChest(categories);
            var eligible = destinations.Select(chest => (Chest: chest, Score: MatchScore(chest, item, categories)))
                .Where(pair => pair.Score > 0).OrderByDescending(pair => pair.Score)
                .ThenBy(pair => pair.Chest.Source.DistanceSquared).ThenBy(pair => pair.Chest.Source.ChestId).ToList();
            if (eligible.Count == 0)
            {
                unsorted.Add(Unsorted(item, item.Stack, ChestSortUnsortedReason.NoMatchingChest,
                    "No suitable named chest nearby; create or rename a chest to " + suggestion + ".", suggestion));
                continue;
            }
            int remaining = item.Stack;
            // Keep the same semantic destination: a full dedicated chest should not spill into
            // a broader, lower-ranked chest just because that chest has spare space.
            int bestScore = eligible[0].Score;
            var bestDestinations = eligible.Where(pair => pair.Score == bestScore).ToList();
            if (bestScore < 200 && bestDestinations.Select(pair => MatchingCategories(pair.Chest.Labels, categories, bestScore))
                .Aggregate((left, right) => left & right) == ChestSortCategory.None)
            {
                unsorted.Add(Unsorted(item, remaining, ChestSortUnsortedReason.AmbiguousCategory,
                    "More than one equally suitable chest category; left in inventory. Use a chest named " + ItemLabel(item) + " to choose explicitly.", ItemLabel(item)));
                continue;
            }
            foreach (var destination in bestDestinations)
            {
                WorkingChest chest = destination.Chest;
                for (int slot = 0; slot < chest.Slots.Length && remaining > 0; slot++)
                {
                    ChestSortItemSnapshot? existing = chest.Slots[slot];
                    if (item.WholeStackOnly || !item.CanMerge || existing is null || !existing.CanMerge || !CanStack(item, existing)) continue;
                    int available = Math.Max(0, Math.Min(item.MaxStack, existing.MaxStack) - existing.Stack);
                    int amount = Math.Min(remaining, available);
                    if (amount <= 0) continue;
                    chest.Slots[slot] = existing with { Stack = existing.Stack + amount };
                    moves.Add(Move(item, chest.Source, slot, amount));
                    remaining -= amount;
                }
                for (int slot = 0; slot < chest.Slots.Length && remaining > 0; slot++)
                {
                    if (chest.Slots[slot] is not null) continue;
                    int amount = Math.Min(remaining, item.MaxStack);
                    chest.Slots[slot] = item with { Slot = slot, Stack = amount };
                    moves.Add(Move(item, chest.Source, slot, amount));
                    remaining -= amount;
                }
                if (remaining == 0) break;
            }
            if (remaining > 0)
                unsorted.Add(Unsorted(item, remaining, ChestSortUnsortedReason.NoSpace,
                    "Suitable nearby chests are full; free space or add another " + suggestion + " chest.", suggestion));
        }
        return new ChestSortPlan { Moves = moves.ToArray(), Unsorted = unsorted.ToArray() };
    }

    public static string SuggestChest(ChestSortCategory categories)
    {
        categories = EffectiveCategories(categories);
        (ChestSortCategory Flags, string Name)[] names =
        [
            (ChestSortCategory.Banner, "Banners"), (ChestSortCategory.Paint, "Paint"),
            (ChestSortCategory.Wiring, "Wiring"), (ChestSortCategory.Fishing, "Fishing"),
            (ChestSortCategory.BossSummon, "Boss Summons"), (ChestSortCategory.Ammo, "Ammo"),
            (ChestSortCategory.Pet, "Pets"), (ChestSortCategory.Mount, "Mounts"),
            (ChestSortCategory.Dye, "Dyes"), (ChestSortCategory.Vanity, "Vanity"),
            (ChestSortCategory.Travel, "Travel"),
            (ChestSortCategory.Tool, "Tools"), (ChestSortCategory.Weapon, "Weapons"),
            (ChestSortCategory.Armor | ChestSortCategory.Accessory, "Equipment"),
            (ChestSortCategory.Potion | ChestSortCategory.Food, "Potions & Food"),
            (ChestSortCategory.Ore | ChestSortCategory.Bar, "Ores & Bars"),
            (ChestSortCategory.Station | ChestSortCategory.Furniture, "Furniture & Stations"),
            (ChestSortCategory.Wall, "Walls"), (ChestSortCategory.Block, "Building Blocks"),
            (ChestSortCategory.Seed | ChestSortCategory.Plant, "Seeds & Plants"),
            (ChestSortCategory.Material, "Materials"), (ChestSortCategory.Consumable, "Consumables")
        ];
        return names.FirstOrDefault(pair => (categories & pair.Flags) != 0).Name ?? "a chest for this item type";
    }

    public static IReadOnlyList<string> PreferenceGroups(ChestSortCategory categories) =>
        Enum.GetValues<ChestSortCategory>().Where(category => category != ChestSortCategory.None
            && (EffectiveCategories(categories) & category) != 0).Select(category => "group:" + category.ToString().ToLowerInvariant()).ToArray();

    private static ChestSortCategory EffectiveCategories(ChestSortCategory categories)
    {
        // Being usable in a recipe does not make a weapon, block, or potion a generic material.
        if ((categories & ~PhysicalResources) != ChestSortCategory.None)
            categories &= ~ChestSortCategory.Material;
        if ((categories & ChestSortCategory.Tool) != 0) categories &= ~ChestSortCategory.Weapon;
        if ((categories & ChestSortCategory.Ammo) != 0) categories &= ~ChestSortCategory.Weapon;
        if ((categories & (ChestSortCategory.Pet | ChestSortCategory.Mount | ChestSortCategory.Fishing | ChestSortCategory.Paint)) != 0)
            categories &= ~(ChestSortCategory.Weapon | ChestSortCategory.Tool);
        if ((categories & ChestSortCategory.BossSummon) != 0) categories &= ~ChestSortCategory.Weapon;
        return categories;
    }

    private static LabelRule[] ParseLabels(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) return [];
        string normalized = " " + Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim() + " ";
        // Negated/instruction-like labels are not category declarations.
        if (new[] { " not ", " except ", " ignore ", " don t ", " dont ", " donot ", " exclude " }.Any(normalized.Contains)) return [];
        return Labels.Where(rule => rule.Aliases.Any(alias => normalized.Contains(" " + alias + " ", StringComparison.Ordinal))).ToArray();
    }

    private static int MatchScore(WorkingChest chest, ChestSortItemSnapshot item, ChestSortCategory categories)
    {
        if (!string.IsNullOrEmpty(item.PreferredChestName))
            return string.Equals(chest.Source.Name.Trim(), item.PreferredChestName.Trim(), StringComparison.OrdinalIgnoreCase) ? 300 : 0;
        if (item.PreferredCategory is null && (ExactItemLabel(chest.Source.Name, ItemLabel(item)) ||
            (!string.IsNullOrWhiteSpace(item.Name) && ExactItemLabel(chest.Source.Name, item.Name)))) return 200;
        return chest.Labels.Where(rule => (rule.Categories & categories) != 0)
            .Select(rule => rule.Specificity).DefaultIfEmpty(0).Max();
    }

    private static ChestSortCategory MatchingCategories(LabelRule[] labels, ChestSortCategory categories, int score) =>
        labels.Where(rule => rule.Specificity == score).Aggregate(ChestSortCategory.None,
            (found, rule) => found | (rule.Categories & categories));

    private static string ItemLabel(ChestSortItemSnapshot item) =>
        !string.IsNullOrWhiteSpace(item.CanonicalName) ? item.CanonicalName :
        !string.IsNullOrWhiteSpace(item.Name) ? item.Name : "this item";

    private static bool ExactItemLabel(string chestName, string itemName)
    {
        string normalizedChest = NormalizeLabel(chestName);
        string normalizedItem = NormalizeLabel(itemName);
        if (normalizedItem.Length == 0) return false;
        string plural = normalizedItem.EndsWith("y", StringComparison.Ordinal) && normalizedItem.Length > 1 &&
            !"aeiou".Contains(normalizedItem[^2]) ? normalizedItem[..^1] + "ies" :
            new[] { "s", "sh", "ch", "x", "z" }.Any(ending => normalizedItem.EndsWith(ending, StringComparison.Ordinal)) ? normalizedItem + "es" : normalizedItem + "s";
        foreach (string candidate in new[] { normalizedItem, plural })
            if (normalizedChest == candidate || normalizedChest == candidate + " chest" ||
                normalizedChest == candidate + " storage" || normalizedChest == candidate + " box") return true;
        return false;
    }

    private static string NormalizeLabel(string text) => Regex.Replace(text.ToLowerInvariant(), "[^\\p{L}\\p{N}]+", " ").Trim();

    private static List<WorkingChest> PrepareChests(IReadOnlyList<ChestSortChestSnapshot> chests)
    {
        List<WorkingChest> result = [];
        foreach (var group in chests.Where(chest => chest is not null && chest.ChestId >= 0).GroupBy(chest => chest.ChestId))
        {
            ChestSortChestSnapshot chest = group.First();
            if (group.Any(other => !SameChest(chest, other)) || chest.SlotCount <= 0 || chest.SlotCount > 40 ||
                !double.IsFinite(chest.DistanceSquared) || chest.DistanceSquared < 0) continue;
            LabelRule[] labels = ParseLabels(chest.Name);
            if (string.IsNullOrWhiteSpace(chest.Name) || chest.Name.Length > 200) continue;
            ChestSortItemSnapshot?[] slots = new ChestSortItemSnapshot?[chest.SlotCount];
            bool valid = true;
            foreach (ChestSortItemSnapshot item in chest.Items)
            {
                if (item.Slot < 0 || item.Slot >= slots.Length || slots[item.Slot] is not null ||
                    item.Stack < 0 || (item.Stack > 0 && (string.IsNullOrWhiteSpace(item.ItemKey) || item.MaxStack <= 0 || item.Stack > item.MaxStack)))
                { valid = false; break; }
                if (item.Stack > 0) slots[item.Slot] = item;
            }
            if (valid) result.Add(new WorkingChest(chest, labels, slots));
        }
        return result;
    }

    private static bool SameChest(ChestSortChestSnapshot first, ChestSortChestSnapshot second) =>
        first.Name == second.Name && first.DistanceSquared == second.DistanceSquared && first.SlotCount == second.SlotCount &&
        first.Items.OrderBy(item => item.Slot).SequenceEqual(second.Items.OrderBy(item => item.Slot));

    private static bool CanStack(ChestSortItemSnapshot source, ChestSortItemSnapshot destination) =>
        source.ItemKey == destination.ItemKey && source.Prefix == destination.Prefix &&
        string.Equals(source.StackKey, destination.StackKey, StringComparison.Ordinal);

    private static ChestSortMove Move(ChestSortItemSnapshot item, ChestSortChestSnapshot chest, int slot, int amount) =>
        new() { SourceSlot = item.Slot, ChestId = chest.ChestId, DestinationSlot = slot, Amount = amount,
            ItemKey = item.ItemKey, StackKey = item.StackKey, ItemName = item.Name, ChestName = chest.Name, Prefix = item.Prefix };

    private static ChestSortUnsortedItem Unsorted(ChestSortItemSnapshot item, int remaining, ChestSortUnsortedReason code,
        string reason, string suggestion = "") => new()
        { Slot = item.Slot, ItemKey = item.ItemKey, Name = item.Name, Remaining = remaining, Code = code, Reason = reason, SuggestedChest = suggestion };

    private sealed record WorkingChest(ChestSortChestSnapshot Source, LabelRule[] Labels, ChestSortItemSnapshot?[] Slots);
}
