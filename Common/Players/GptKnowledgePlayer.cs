#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TerrariaGPT.Common.Knowledge;

namespace TerrariaGPT.Common.Players;

/// <summary>
/// Local observations only. In particular, never iterate Main.chest: the single
/// chest the player has open is the only world storage this component reads.
/// </summary>
public sealed class GptKnowledgePlayer : ModPlayer
{
    private readonly Dictionary<string, WorldKnowledgeLedger> _worlds = new(StringComparer.Ordinal);
    private int _observationTimer;
    private Guid? _activeWorldId;
    private WorldKnowledgeLedger? _activeLedger;
    private Chest? _observedChest;
    private WorldKnowledgeLedger? _observedChestLedger;
    private string? _observedChestKey;
    private string? _observedChestName;
    private int _observedChestX, _observedChestY;
    private VisibleSlot[] _observedSlots = Array.Empty<VisibleSlot>();
    private DateTimeOffset _lastVisibleChestTime;
    private bool _legacyDataChecked;

    private readonly record struct VisibleSlot(int Type, int Stack, string? Name, string? ModKey);

    public override void Initialize()
    {
        _worlds.Clear();
        _observationTimer = 0;
        _activeWorldId = null;
        _activeLedger = null;
        _legacyDataChecked = false;
        ClearOpenChestObservation();
    }

    public override void OnEnterWorld()
    {
        if (IsLocalPlayer)
        {
            EnsureLegacyData();
            Observe();
        }
    }

    public override void PostUpdate()
    {
        if (!IsLocalPlayer)
            return;

        if (++_observationTimer >= 30)
        {
            _observationTimer = 0;
            Observe();
        }
        else
            // Capture even a brief opening, and the last visible state before
            // closing, without ever following a saved index into a closed chest.
            ObserveOpenChest();
    }

    public override void SaveData(TagCompound tag)
    {
        EnsureLegacyData();
        tag["version"] = 2;
        tag["worlds"] = _worlds.Select(pair => pair.Value.Save(pair.Key)).ToList();
    }

    public override void LoadData(TagCompound tag)
    {
        _legacyDataChecked = true;
        ClearOpenChestObservation();
        _activeWorldId = null;
        _activeLedger = null;
        _worlds.Clear();
        foreach (TagCompound worldTag in tag.GetList<TagCompound>("worlds"))
        {
            string worldKey = worldTag.GetString("world");
            if (worldKey.Length > 0)
                _worlds[worldKey] = WorldKnowledgeLedger.Load(worldTag);
        }
    }

    private void EnsureLegacyData()
    {
        if (_legacyDataChecked) return;
        _legacyDataChecked = true;
        TagCompound? legacy = LegacyPlayerData.Read(Player, nameof(GptKnowledgePlayer));
        if (legacy is null) return;
        try { LoadData(legacy); }
        catch (Exception) { /* Original unloaded data stays available for recovery. */ }
    }

    /// <summary>Call only from Terraria's main thread, before starting asynchronous work.</summary>
    public KnowledgeSnapshot CreateSnapshot()
    {
        if (!IsLocalPlayer)
            throw new InvalidOperationException("Only the local player's knowledge may be captured.");

        EnsureLegacyData();
        List<ItemStackSnapshot> inventory = Observe();
        WorldKnowledgeLedger ledger = CurrentLedger;
        string? openChestKey = Main.netMode == NetmodeID.SinglePlayer && TryGetOpenChest(out Chest? openChest)
            ? WorldKnowledgeLedger.ChestKey(openChest!.x, openChest.y)
            : null;

        // Single-player observations must never become shared-server storage access.
        var chests = Main.netMode == NetmodeID.SinglePlayer
            ? ledger.RememberedChests.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value with { IsCurrentlyOpen = pair.Key == openChestKey }).ToArray()
            : Array.Empty<OwnedChestSnapshot>();

        return new KnowledgeSnapshot(
            Player.name,
            DateTimeOffset.UtcNow,
            Array.AsReadOnly(ledger.Subjects.Values.OrderBy(subject => subject.Kind, StringComparer.Ordinal)
                .ThenBy(subject => subject.Name, StringComparer.OrdinalIgnoreCase).ToArray()),
            inventory.AsReadOnly(),
            Array.AsReadOnly(chests),
            CurrentBiomes().AsReadOnly());
    }

    public string ClaimOpenChest()
    {
        if (!IsLocalPlayer)
            return "Only the local player can remember a chest.";
        if (Main.netMode != NetmodeID.SinglePlayer)
            return "Chest memory is single-player only until server-verified ownership is supported.";
        if (!TryGetOpenChest(out Chest? chest))
            return "Open a placed chest first. Opened chests are remembered automatically in single-player; personal banks are not supported yet.";

        WorldKnowledgeLedger ledger = CurrentLedger;
        OwnedChestSnapshot captured = CaptureChest(chest!, ledger);
        string key = WorldKnowledgeLedger.ChestKey(chest!.x, chest.y);
        ledger.IgnoredChests.Remove(key);
        ledger.RememberedChests[key] = captured;
        return $"GPT is remembering {captured.Label}. Opened chests are remembered automatically; /gptclaim also resumes remembering a forgotten chest.";
    }

    public string ForgetOpenChest()
    {
        if (Main.netMode != NetmodeID.SinglePlayer)
            return "Chest memory is single-player only until server-verified ownership is supported.";
        if (!IsLocalPlayer || !TryGetOpenChest(out Chest? chest))
            return "Open a remembered chest, then use /gptforget.";

        WorldKnowledgeLedger ledger = CurrentLedger;
        string key = WorldKnowledgeLedger.ChestKey(chest!.x, chest.y);
        ledger.RememberedChests.Remove(key);
        ledger.IgnoredChests.Add(key);
        ClearOpenChestObservation();
        return "GPT forgot this chest and will skip it on future openings. Use /gptclaim while it is open to remember it again. Previously discovered item names remain known.";
    }

    private bool IsLocalPlayer => !Main.dedServ && Player.whoAmI == Main.myPlayer && !Main.gameMenu;

    private WorldKnowledgeLedger CurrentLedger
    {
        get
        {
            Guid worldId = Main.ActiveWorldFileData.UniqueId;
            if (_activeWorldId == worldId && _activeLedger is not null)
                return _activeLedger;
            string key = worldId.ToString("N");
            if (!_worlds.TryGetValue(key, out WorldKnowledgeLedger? ledger))
                _worlds[key] = ledger = new WorldKnowledgeLedger();
            _activeWorldId = worldId;
            _activeLedger = ledger;
            return ledger;
        }
    }

    private List<ItemStackSnapshot> Observe()
    {
        WorldKnowledgeLedger ledger = CurrentLedger;
        List<ItemStackSnapshot> inventory = CaptureInventory(ledger);
        foreach (string biome in CurrentBiomes())
            ledger.Discover(new KnowledgeSubject("biome", "Terraria/" + biome.Replace(" ", ""), biome));

        ObserveOpenChest(refreshTimestamp: true);
        return inventory;
    }

    private void ObserveOpenChest(bool refreshTimestamp = false)
    {
        if (Main.netMode != NetmodeID.SinglePlayer || !TryGetOpenChest(out Chest? chest))
        {
            FinishOpenChestObservation();
            return;
        }
        WorldKnowledgeLedger ledger = CurrentLedger;
        bool sameChest = ReferenceEquals(chest, _observedChest) && ReferenceEquals(ledger, _observedChestLedger)
            && chest!.x == _observedChestX && chest.y == _observedChestY;
        string key = sameChest ? _observedChestKey! : WorldKnowledgeLedger.ChestKey(chest!.x, chest.y);
        if (ledger.IgnoredChests.Contains(key))
        {
            FinishOpenChestObservation();
            return;
        }
        if (!sameChest)
        {
            FinishOpenChestObservation();
            _observedChest = chest;
            _observedChestLedger = ledger;
            _observedChestKey = key;
            _observedChestX = chest!.x;
            _observedChestY = chest.y;
        }

        bool changed = !sameChest || _observedChestName != chest!.name || _observedSlots.Length != chest.item.Length;
        if (_observedSlots.Length != chest!.item.Length)
            _observedSlots = new VisibleSlot[chest.item.Length];
        // Compare exact visible values, including replacements with the same
        // stack count. No hashes, live references or unseen chest scans.
        for (int i = 0; i < chest.item.Length; i++)
        {
            Item item = chest.item[i];
            VisibleSlot slot = item is null || item.IsAir ? default
                : new VisibleSlot(item.type, item.stack, item.Name, item.ModItem?.FullName);
            if (_observedSlots[i] != slot)
            {
                changed = true;
                _observedSlots[i] = slot;
            }
        }
        _observedChestName = chest.name;
        _lastVisibleChestTime = DateTimeOffset.UtcNow;
        if (changed)
            ledger.RememberedChests[key] = CaptureChest(chest, ledger) with { LastObservedUtc = _lastVisibleChestTime };
        else if (refreshTimestamp && ledger.RememberedChests.TryGetValue(key, out OwnedChestSnapshot? remembered))
            ledger.RememberedChests[key] = remembered with { LastObservedUtc = _lastVisibleChestTime };
    }

    private void FinishOpenChestObservation()
    {
        // Closing only stamps the last *visible* observation. Never follow the
        // old index or object to inspect contents after the chest has closed.
        if (_observedChestLedger is not null && _observedChestKey is not null
            && _observedChestLedger.RememberedChests.TryGetValue(_observedChestKey, out OwnedChestSnapshot? remembered))
            _observedChestLedger.RememberedChests[_observedChestKey] = remembered with { LastObservedUtc = _lastVisibleChestTime };
        ClearOpenChestObservation();
    }

    private void ClearOpenChestObservation()
    {
        _observedChest = null;
        _observedChestLedger = null;
        _observedChestKey = null;
        _observedChestName = null;
    }

    private List<ItemStackSnapshot> CaptureInventory(WorldKnowledgeLedger ledger)
    {
        var items = new List<ItemStackSnapshot>();
        // Slot 58 is Terraria's cursor mirror, not another owned stack.
        AddItems(Player.inventory, "Inventory", items, ledger, maximumSlots: 58);
        AddItems(Player.armor, "Equipment and vanity", items, ledger, canUseForCrafting: false);
        AddItems(Player.dye, "Equipment dye", items, ledger, canUseForCrafting: false);
        AddItems(Player.miscEquips, "Miscellaneous equipment", items, ledger, canUseForCrafting: false);
        AddItems(Player.miscDyes, "Miscellaneous dye", items, ledger, canUseForCrafting: false);
        if (!Main.mouseItem.IsAir)
        {
            DiscoverItem(Main.mouseItem, ledger);
            items.Add(ToStack(Main.mouseItem, "Cursor") with { CanUseForCrafting = false });
        }
        return items;
    }

    private static void AddItems(Item[] slots, string location, List<ItemStackSnapshot> target, WorldKnowledgeLedger ledger, int maximumSlots = int.MaxValue, bool canUseForCrafting = true)
    {
        for (int i = 0; i < Math.Min(slots.Length, maximumSlots); i++)
        {
            Item item = slots[i];
            if (item is null || item.IsAir)
                continue;
            DiscoverItem(item, ledger);
            target.Add(ToStack(item, $"{location}, slot {i + 1}") with { CanUseForCrafting = canUseForCrafting });
        }
    }

    private static string ItemKey(Item item) => item.ModItem?.FullName ?? "Terraria/" + ItemID.Search.GetName(item.type);

    private static void DiscoverItem(Item item, WorldKnowledgeLedger ledger) =>
        ledger.Discover(new KnowledgeSubject("item", ItemKey(item), item.Name));

    private static ItemStackSnapshot ToStack(Item item, string location) => new(ItemKey(item), item.Name, item.stack, location);

    private static OwnedChestSnapshot CaptureChest(Chest chest, WorldKnowledgeLedger ledger)
    {
        string label = string.IsNullOrWhiteSpace(chest.name) ? $"Chest at tile {chest.x}, {chest.y}" : chest.name;
        var items = new List<ItemStackSnapshot>();
        AddItems(chest.item, label + " (remembered contents)", items, ledger);
        return new OwnedChestSnapshot(label, chest.x, chest.y, DateTimeOffset.UtcNow, items.AsReadOnly());
    }

    private bool TryGetOpenChest(out Chest? chest)
    {
        chest = null;
        // Negative indices are personal banks; never reinterpret them as a world chest.
        if (Player.chest < 0 || Player.chest >= Main.maxChests)
            return false;
        chest = Main.chest[Player.chest];
        return chest is not null;
    }

    private List<string> CurrentBiomes()
    {
        var biomes = new List<string>();
        void Add(bool occupied, string name) { if (occupied) biomes.Add(name); }

        Add(Player.ZoneForest, "Forest");
        Add(Player.ZoneDesert, "Desert");
        Add(Player.ZoneUndergroundDesert, "Underground Desert");
        Add(Player.ZoneJungle, "Jungle");
        Add(Player.ZoneSnow, "Snow");
        Add(Player.ZoneBeach, "Ocean");
        Add(Player.ZoneCorrupt, "Corruption");
        Add(Player.ZoneCrimson, "Crimson");
        Add(Player.ZoneHallow, "Hallow");
        Add(Player.ZoneDungeon, "Dungeon");
        Add(Player.ZoneGlowshroom, "Glowing Mushroom");
        Add(Player.ZoneMeteor, "Meteorite");
        Add(Player.ZoneGraveyard, "Graveyard");
        Add(Player.ZoneShimmer, "Shimmer");
        Add(Player.ZoneSkyHeight, "Space");
        Add(Player.ZoneDirtLayerHeight, "Underground");
        Add(Player.ZoneRockLayerHeight, "Cavern");
        Add(Player.ZoneUnderworldHeight, "Underworld");
        return biomes;
    }
}
