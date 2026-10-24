#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerrariaChatGPT.Common.Players;
using TerrariaChatGPT.Common.Memory;

namespace TerrariaChatGPT.Common.Sorting;

public sealed record ChestSortPreview(ChestSortPlan Plan, int EligibleChestCount, DateTimeOffset CreatedUtc);
public sealed record ChestSortResult(bool Success, string Message, int MovedItemCount = 0, int MovedStackCount = 0);

/// <summary>
/// Explicit, local, game-thread transactions. The assistant never calls this service.
/// Preview holds detached display values; private live witnesses expire after one confirmation.
/// No world enumeration, networking, save-file editing, or item creation is used to sort.
/// </summary>
public static class ChestSortService
{
    // Verified against installed Terraria 1.4.4 Chest.PutItemInNearbyChest:
    // Vector2(chest.x * 16 + 16, chest.y * 16 + 16), distance strictly below 600.
    public const float QuickStackRangePixels = 600f;
    public const string ChestAccessHelp = "Sorting uses nearby named chests you have opened or placed, including remembered chests from earlier visits.";
    private static PendingSort? _pending;
    public static ChestSortPreview? CurrentPreview { get; private set; }
    public static string Status { get; private set; } = ChestAccessHelp;

    /// <summary>Only call from the local chat UI on Terraria's game thread.</summary>
    public static ChestSortPreview? BeginPreview(SortSuggestion? suggestion = null)
    {
        Cancel();
        if (!CanUse(out Player? player, out string reason))
        { Status = reason; return null; }
        try
        {
            GptKnowledgePlayer memory = player!.GetModPlayer<GptKnowledgePlayer>();
            GptMemoryPlayer preferences = player.GetModPlayer<GptMemoryPlayer>();
            IReadOnlyList<GptKnowledgePlayer.SortingChest> known = memory.GetSortingChestCandidates();
            if (suggestion is not null && !ValidateChoices(player, known, suggestion, out suggestion)) return null;
            List<ChestWitness> chests = [];
            foreach (GptKnowledgePlayer.SortingChest candidate in known)
                if (TryCaptureChest(player, candidate, out ChestWitness? witness))
                    chests.Add(witness!);
            // A chest index occurs only once; malformed duplicate memories authorize nothing.
            if (chests.Select(chest => chest.Id).Distinct().Count() != chests.Count)
                throw new InvalidOperationException("Duplicate storage identity.");
            ItemWitness[] bag = player.inventory.Skip(10).Take(40).Select(ItemWitness.Capture).ToArray();
            if (HasAliasedItems(player, bag, chests))
                throw new InvalidOperationException("Shared item instance.");
            Dictionary<string, string>? choices = suggestion?.Assignments.ToDictionary(choice => choice.ItemKey, choice => choice.ChestName, StringComparer.Ordinal);
            ChestSortPlan plan = ChestSortPlanner.Build(bag.Select((item, index) => ApplyChoice(Describe(item, index + 10), preferences, choices)).ToArray(),
                chests.Select(chest => new ChestSortChestSnapshot
                {
                    ChestId = chest.Id, Name = chest.Name, DistanceSquared = chest.DistanceSquared,
                    SlotCount = chest.Items.Length,
                    Items = chest.Items.Select((item, slot) => Describe(item, slot)).Where(item => item.Stack > 0).ToArray()
                }).ToArray());
            // Display collections cannot be cast to an editable array by another consumer.
            plan = plan with { Moves = Array.AsReadOnly(plan.Moves.ToArray()), Unsorted = Array.AsReadOnly(plan.Unsorted.ToArray()) };
            if (plan.Moves.Sum(move => (long)move.Amount) > int.MaxValue)
                throw new InvalidOperationException("Sorting quantity exceeds the supported range.");
            CurrentPreview = new ChestSortPreview(plan, chests.Count, DateTimeOffset.UtcNow);
            _pending = new PendingSort(player, Main.ActiveWorldFileData.UniqueId, memory.SortingSessionId,
                Environment.CurrentManagedThreadId, player.inventory, bag, chests.ToArray(), plan, preferences.Revision);
            Status = chests.Count == 0 ? "No eligible named chests nearby. " + ChestAccessHelp
                : "Preview only. Hotbar, favorites, equipment, coin/ammo slots and personal banks stay untouched.";
            return CurrentPreview;
        }
        catch (Exception)
        {
            Cancel();
            Status = "A safe sorting preview could not be made. Nothing was moved. Close any chest and refresh the preview.";
            return null;
        }
    }

    public static ChestSortResult Confirm()
    {
        PendingSort? pending = _pending;
        // One-shot even when validation fails: double-clicks and stale UI cannot replay moves.
        _pending = null;
        CurrentPreview = null;
        if (pending is null) return Result(false, "No active preview. Refresh the preview before sorting.");
        try
        {
            if (!Validate(pending)) return Stale();
            if (pending.Plan.Moves.Count == 0)
                return Result(false, "Nothing can be moved from this preview. Items without a suitable chest stay in your inventory.");

            // Stage only changed cells. Vanilla clones preserve prefix and all instance fields;
            // custom/global items move as whole original objects into an empty slot. No mod
            // stack/split/clone hooks run here, and untouched cells keep their original references.
            Dictionary<int, Item> stagedBag = [];
            Dictionary<(int Chest, int Slot), Item> stagedChests = [];
            Dictionary<int, ChestWitness> byId = pending.Chests.ToDictionary(chest => chest.Id);
            HashSet<int> wholeMoved = [];
            foreach (ChestSortMove move in pending.Plan.Moves)
            {
                if (move.SourceSlot < 10 || move.SourceSlot > 49 || move.Amount <= 0 || !byId.TryGetValue(move.ChestId, out ChestWitness? chest)
                    || move.DestinationSlot < 0 || move.DestinationSlot >= chest.Items.Length)
                    return Stale();
                ItemWitness sourceWitness = pending.Bag[move.SourceSlot - 10];
                if (sourceWitness.Air || sourceWitness.Favorite || sourceWitness.Key != move.ItemKey || sourceWitness.Prefix != move.Prefix)
                    return Stale();
                Item source = stagedBag.TryGetValue(move.SourceSlot, out Item? preparedSource) ? preparedSource : sourceWitness.Instance;
                var destinationKey = (move.ChestId, move.DestinationSlot);
                Item destination = stagedChests.TryGetValue(destinationKey, out Item? preparedDestination)
                    ? preparedDestination : chest.Items[move.DestinationSlot].Instance;
                if (source.IsAir || source.stack < move.Amount) return Stale();
                if (!sourceWitness.Plain)
                {
                    if (!destination.IsAir || source.stack != move.Amount || !wholeMoved.Add(move.SourceSlot)) return Stale();
                    stagedChests[destinationKey] = source;
                    stagedBag[move.SourceSlot] = new Item();
                    continue;
                }
                if (!IsPlain(source) || (!destination.IsAir && (!IsPlain(destination) || StackIdentity(destination) != move.StackKey))) return Stale();
                if (StackIdentity(source) != move.StackKey || move.Amount > source.maxStack) return Stale();
                if (destination.IsAir)
                {
                    Item placed = source.Clone();
                    placed.stack = move.Amount;
                    stagedChests[destinationKey] = placed;
                }
                else
                {
                    if (destination.stack > Math.Min(destination.maxStack, source.maxStack) - move.Amount) return Stale();
                    Item added = destination.Clone();
                    added.stack += move.Amount;
                    stagedChests[destinationKey] = added;
                }
                Item remaining = source.Clone();
                remaining.stack -= move.Amount;
                stagedBag[move.SourceSlot] = remaining.stack == 0 ? new Item() : remaining;
            }
            if (!ConservesItems(pending, stagedBag, stagedChests) || !Validate(pending)) return Stale();

            int movedItemCount = pending.Plan.MovedItemCount;
            int movedStackCount = pending.Plan.MovedStackCount;
            string completedMessage = $"Sorted {movedItemCount} items from {movedStackCount} inventory stacks. Unmatched items were left in place.";
            ChestSortResult completedResult = new(true, completedMessage, movedItemCount, movedStackCount);
            // All possible failures, allocations and checks precede these bounded assignments.
            // Terraria's game thread cannot interleave item use or another UI action here.
            foreach (var change in stagedBag) pending.Inventory[change.Key] = change.Value;
            foreach (var change in stagedChests) byId[change.Key.Chest].Slots[change.Key.Slot] = change.Value;

            // Memory is an observation cache, not the transaction. A refresh failure cannot
            // undo or replay committed transfers, so report success independently of cache work.
            try
            {
                GptKnowledgePlayer memory = pending.Player.GetModPlayer<GptKnowledgePlayer>();
                foreach (int id in stagedChests.Keys.Select(key => key.Chest).Distinct()) memory.RefreshSortedChest(byId[id].Instance);
                Recipe.FindRecipes();
            }
            catch (Exception) { /* The transfers already completed safely. */ }
            Status = completedMessage;
            return completedResult;
        }
        catch (Exception)
        {
            return Result(false, "The preview could not be safely applied. Nothing was moved. Refresh the preview.");
        }
    }

    public static void Cancel() { _pending = null; CurrentPreview = null; }

    /// <summary>Explicit UI action only. Persists choices, but never transfers items.</summary>
    public static MemoryChangeBatch? RememberChoices(SortSuggestion suggestion, out string message)
    {
        if (!CanUse(out Player? player, out message)) return null;
        try
        {
            var known = player!.GetModPlayer<GptKnowledgePlayer>().GetSortingChestCandidates();
            if (!ValidateChoices(player, known, suggestion, out SortSuggestion? validated))
            { message = Status; return null; }
            MemoryChangeBatch? receipt = player.GetModPlayer<GptMemoryPlayer>().RememberSortingChoices(validated!, out message);
            if (receipt is not null) Cancel();
            return receipt;
        }
        catch (Exception)
        { message = "These choices could not be safely remembered. Nothing was moved."; return null; }
    }

    private static bool ValidateChoices(Player player, IReadOnlyList<GptKnowledgePlayer.SortingChest> known,
        SortSuggestion supplied, out SortSuggestion? validated)
    {
        string[] keys = player.inventory.Skip(10).Take(40)
            .Where(item => !item.IsAir && !item.favorited && !item.IsACoin).Select(ItemKey).ToArray();
        validated = SortSuggestionValidator.Validate(supplied, keys, known.Select(chest => chest.Label));
        if (validated is not null && supplied.Assignments is not null
            && validated.Assignments.Count == supplied.Assignments.Select(choice => choice?.ItemKey).Distinct(StringComparer.Ordinal).Count()) return true;
        Status = "These choices no longer match your sortable inventory or remembered chest names. Ask GPT to categorise the remaining items again. Nothing was moved.";
        validated = null;
        return false;
    }

    public static bool WithinQuickStackRange(Vector2 playerCenter, int chestX, int chestY) =>
        (new Vector2(chestX * 16f + 16f, chestY * 16f + 16f) - playerCenter).Length() < QuickStackRangePixels;

    private static bool CanUse(out Player? player, out string reason)
    {
        player = null;
        if (Main.dedServ || Main.gameMenu || Main.netMode != NetmodeID.SinglePlayer)
        { reason = "Named-chest sorting is available in single-player only."; return false; }
        player = Main.LocalPlayer;
        if (player is null || !player.active || player.dead || player.ghost || player.whoAmI != Main.myPlayer || player.inventory.Length < 50)
        { reason = "Enter your world with an active character before sorting."; return false; }
        if (player.HasLockedInventory() || player.chest != -1 || !Main.mouseItem.IsAir)
        { reason = "Close your chest or bank and put down the cursor item before previewing a sort."; return false; }
        reason = string.Empty;
        return true;
    }

    private static bool TryCaptureChest(Player player, GptKnowledgePlayer.SortingChest known, out ChestWitness? witness)
    {
        witness = null;
        // Coordinates come exclusively from the local player's opened/placed chest memory.
        // Range and standard tile checks happen before even resolving its storage index.
        if (!WithinQuickStackRange(player.Center, known.X, known.Y) || !StandardChestTile(known.X, known.Y, out TileIdentity tile)) return false;
        int index = Chest.FindChest(known.X, known.Y);
        if (index < 0 || index >= Main.maxChests || Chest.UsingChest(index) >= 0 || Chest.IsLocked(known.X, known.Y)) return false;
        Chest? chest = Main.chest[index];
        if (chest is null || chest.x != known.X || chest.y != known.Y || string.IsNullOrWhiteSpace(chest.name)) return false;
        if (!player.GetModPlayer<GptKnowledgePlayer>().BindSortingChest(known, chest, tile.Type, tile.FrameX, tile.FrameY)) return false;
        if (chest.item.Length != 40) return false;
        witness = new ChestWitness(index, chest, chest.item, known.X, known.Y, chest.name, tile,
            Vector2.DistanceSquared(player.Center, new Vector2(known.X * 16f + 16f, known.Y * 16f + 16f)),
            chest.item.Select(ItemWitness.Capture).ToArray());
        return true;
    }

    private static bool StandardChestTile(int x, int y, out TileIdentity identity)
    {
        identity = default;
        if (x < 0 || y < 0 || x + 1 >= Main.maxTilesX || y + 1 >= Main.maxTilesY) return false;
        Tile tile = Main.tile[x, y];
        if (!tile.HasTile || (tile.TileType != TileID.Containers && tile.TileType != TileID.Containers2)
            || tile.TileFrameX % 36 != 0 || tile.TileFrameY % 36 != 0) return false;
        identity = new(tile.TileType, tile.TileFrameX, tile.TileFrameY);
        for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                Tile part = Main.tile[x + dx, y + dy];
                if (!part.HasTile || part.TileType != tile.TileType || part.TileFrameX != tile.TileFrameX + dx * 18
                    || part.TileFrameY != tile.TileFrameY + dy * 18) return false;
            }
        return true;
    }

    private static bool Validate(PendingSort pending)
    {
        if (Environment.CurrentManagedThreadId != pending.ThreadId || !CanUse(out Player? player, out _)
            || !ReferenceEquals(player, pending.Player) || !ReferenceEquals(player!.inventory, pending.Inventory)
            || Main.ActiveWorldFileData.UniqueId != pending.WorldId) return false;
        GptKnowledgePlayer memory = player.GetModPlayer<GptKnowledgePlayer>();
        if (player.GetModPlayer<GptMemoryPlayer>().Revision != pending.MemoryRevision) return false;
        IReadOnlyList<GptKnowledgePlayer.SortingChest> known = memory.GetSortingChestCandidates();
        if (memory.SortingSessionId != pending.SessionId) return false;
        for (int i = 0; i < pending.Bag.Length; i++)
            if (!pending.Bag[i].Matches(player.inventory[i + 10])) return false;
        foreach (ChestWitness old in pending.Chests)
        {
            GptKnowledgePlayer.SortingChest? candidate = known.FirstOrDefault(chest => chest.X == old.X && chest.Y == old.Y && ReferenceEquals(chest.Instance, old.Instance));
            if (candidate is null || !WithinQuickStackRange(player.Center, old.X, old.Y) || !StandardChestTile(old.X, old.Y, out TileIdentity tile)
                || tile != old.Tile || Chest.FindChest(old.X, old.Y) != old.Id || Chest.UsingChest(old.Id) >= 0
                || Chest.IsLocked(old.X, old.Y) || !ReferenceEquals(Main.chest[old.Id], old.Instance)
                || !ReferenceEquals(old.Instance.item, old.Slots) || old.Instance.name != old.Name) return false;
            if (!memory.BindSortingChest(candidate, old.Instance, tile.Type, tile.FrameX, tile.FrameY)) return false;
            for (int i = 0; i < old.Items.Length; i++) if (!old.Items[i].Matches(old.Slots[i])) return false;
        }
        return !HasAliasedItems(player, pending.Bag, pending.Chests.ToList());
    }

    private static bool IsPlain(Item item)
    {
        if (item.ModItem is not null || item.type >= ItemID.Count) return false;
        foreach (GlobalItem _ in item.Globals) return false;
        return true;
    }

    // In addition to type/prefix, retain per-instance combat/value variations that
    // another mod can set even without attaching a GlobalItem. Different values
    // may move to empty slots, but cannot be collapsed into one stack.
    private static string StackIdentity(Item item) => $"{ItemKey(item)}|{item.prefix}|{item.maxStack}|{item.Name}|{item.shopCustomPrice}"
        + $"|{item.damage}|{item.defense}|{item.value}|{item.rare}|{item.knockBack}|{item.scale}|{item.shoot}|{item.shootSpeed}"
        + $"|{item.useTime}|{item.useAnimation}|{item.mana}|{item.crit}";
    private static string ItemKey(Item item) => item.ModItem?.FullName ?? "Terraria/" + ItemID.Search.GetName(item.type);

    private static ChestSortItemSnapshot Describe(ItemWitness witness, int slot) => new()
    {
        Slot = slot, ItemKey = witness.Key, StackKey = witness.StackKey, Name = witness.Name,
        CanonicalName = witness.Instance.IsAir ? string.Empty : ChestSortItemClassifier.CanonicalName(witness.Instance),
        Stack = witness.Air ? 0 : witness.Stack, MaxStack = witness.MaxStack, Prefix = witness.Prefix,
        Favorite = witness.Favorite || witness.Instance.IsACoin,
        CanMerge = witness.Plain, WholeStackOnly = !witness.Plain,
        Categories = witness.Air ? ChestSortCategory.None : ChestSortItemClassifier.Classify(witness.Instance),
        PreferenceGroups = witness.Air ? Array.Empty<string>() : ChestSortItemClassifier.PreferenceGroups(witness.Instance)
    };

    private static ChestSortItemSnapshot ApplyPreference(ChestSortItemSnapshot item, GptMemoryPlayer preferences)
    {
        if (!preferences.Enabled || item.Stack <= 0) return item;
        var preference = preferences.ResolveSorting(item.ItemKey, item.PreferenceGroups, out bool conflict);
        if (conflict) return item with { PreferenceConflict = true };
        if (preference is null) return item;
        if (preference.Target.StartsWith("chest:", StringComparison.Ordinal) && preference.Target.Length > 6)
            return item with { PreferredChestName = preference.Target[6..] };
        if (Enum.TryParse(preference.Target, true, out ChestSortCategory category)
            && category != ChestSortCategory.None && Enum.IsDefined(category))
            return item with { PreferredCategory = category };
        return item;
    }

    private static ChestSortItemSnapshot ApplyChoice(ChestSortItemSnapshot item, GptMemoryPlayer preferences,
        IReadOnlyDictionary<string, string>? choices)
    {
        if (choices is null) return ApplyPreference(item, preferences);
        return choices.TryGetValue(item.ItemKey, out string? chest)
            ? item with { PreferredChestName = chest, PreferredCategory = null, PreferenceConflict = false }
            : item with { UnassignedBySuggestion = true };
    }

    private static bool HasAliasedItems(Player player, ItemWitness[] bag, List<ChestWitness> chests)
    {
        HashSet<Item> instances = new(ReferenceEqualityComparer.Instance);
        // Corrupt or unusual mod state must not let a bag reference move an item
        // that is also present in a protected slot or worn equipment.
        IEnumerable<Item> protectedItems = player.inventory.Take(10).Concat(player.inventory.Skip(50))
            .Concat(player.armor).Concat(player.dye).Concat(player.miscEquips).Concat(player.miscDyes)
            .Concat(player.bank.item).Concat(player.bank2.item).Concat(player.bank3.item).Concat(player.bank4.item);
        foreach (Item item in protectedItems) if (item is not null && !item.IsAir) instances.Add(item);
        return bag.Concat(chests.SelectMany(chest => chest.Items)).Any(item => !item.Air && !instances.Add(item.Instance));
    }

    private static bool ConservesItems(PendingSort pending, Dictionary<int, Item> bag, Dictionary<(int Chest, int Slot), Item> chests)
    {
        Dictionary<string, long> before = new(StringComparer.Ordinal), after = new(StringComparer.Ordinal);
        void Add(Dictionary<string, long> values, Item item)
        {
            if (item.IsAir) return;
            if (item.stack <= 0 || item.stack > item.maxStack) throw new InvalidOperationException("Invalid item stack.");
            string key = StackIdentity(item);
            values[key] = values.GetValueOrDefault(key) + item.stack;
        }
        for (int slot = 10; slot < 50; slot++)
        {
            Item original = pending.Bag[slot - 10].Instance;
            Add(before, original); Add(after, bag.GetValueOrDefault(slot, original));
        }
        foreach (ChestWitness chest in pending.Chests)
            for (int slot = 0; slot < chest.Items.Length; slot++)
            {
                Item original = chest.Items[slot].Instance;
                Add(before, original); Add(after, chests.GetValueOrDefault((chest.Id, slot), original));
            }
        return before.Count == after.Count && before.All(pair => after.GetValueOrDefault(pair.Key) == pair.Value);
    }

    private static ChestSortResult Stale() => Result(false, "Your inventory or nearby storage changed, or you moved out of range. Nothing was moved. Refresh the preview.");
    private static ChestSortResult Result(bool success, string message, int items = 0, int stacks = 0)
    { Status = message; return new(success, message, items, stacks); }

    private sealed record PendingSort(Player Player, Guid WorldId, Guid SessionId, int ThreadId, Item[] Inventory,
        ItemWitness[] Bag, ChestWitness[] Chests, ChestSortPlan Plan, long MemoryRevision);
    private readonly record struct TileIdentity(ushort Type, short FrameX, short FrameY);
    private sealed record ChestWitness(int Id, Chest Instance, Item[] Slots, int X, int Y, string Name, TileIdentity Tile,
        double DistanceSquared, ItemWitness[] Items);
    private sealed record ItemWitness(Item Instance, bool Air, int Type, int Stack, int Prefix, int MaxStack,
        bool Favorite, bool Plain, string Key, string StackKey, string Name, int? ShopPrice)
    {
        public static ItemWitness Capture(Item item)
        {
            if (item is null) throw new InvalidOperationException("Missing item slot.");
            return new(item, item.IsAir, item.type, item.stack, item.prefix, item.maxStack, item.favorited,
                IsPlain(item), item.IsAir ? string.Empty : ItemKey(item), item.IsAir ? string.Empty : StackIdentity(item), item.Name, item.shopCustomPrice);
        }
        public bool Matches(Item item) => ReferenceEquals(item, Instance) && item.IsAir == Air && item.type == Type
            && item.stack == Stack && item.prefix == Prefix && item.maxStack == MaxStack && item.favorited == Favorite
            && item.Name == Name && item.shopCustomPrice == ShopPrice && IsPlain(item) == Plain
            && (Air || (ItemKey(item) == Key && StackIdentity(item) == StackKey));
    }
}
