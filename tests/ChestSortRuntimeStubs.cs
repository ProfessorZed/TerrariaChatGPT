#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaChatGPT.Common.Sorting;

// Fake game host only. These tests do not load Terraria or open any saved world.
namespace Microsoft.Xna.Framework
{
    public readonly record struct Vector2(float X, float Y)
    {
        public float Length() => (float)Math.Sqrt(X * X + Y * Y);
        public static Vector2 operator -(Vector2 left, Vector2 right) => new(left.X - right.X, left.Y - right.Y);
        public static float DistanceSquared(Vector2 left, Vector2 right) { var delta = left - right; return delta.X * delta.X + delta.Y * delta.Y; }
    }
}

namespace Terraria
{
    public static class Main
    {
        public static bool dedServ, gameMenu;
        public static int netMode, myPlayer, maxChests = 1000, maxTilesX = 500, maxTilesY = 500;
        public static Player LocalPlayer = new();
        public static Item mouseItem = new();
        public static WorldFileData ActiveWorldFileData = new();
        public static ChestReadSpy chest = new();
        public static TileReadSpy tile = new();
    }
    public sealed class WorldFileData { public Guid UniqueId = Guid.NewGuid(); }
    public sealed class ChestReadSpy
    {
        private readonly Dictionary<int, Chest> _values = [];
        public readonly List<int> Reads = [];
        public readonly HashSet<int> Forbidden = [];
        public Chest? this[int index]
        {
            get { Reads.Add(index); if (Forbidden.Contains(index)) throw new InvalidOperationException("Forbidden unseen chest read."); return _values.GetValueOrDefault(index); }
            set { if (value is null) _values.Remove(index); else _values[index] = value; }
        }
    }
    public sealed class TileReadSpy
    {
        private readonly Dictionary<(int, int), Tile> _values = [];
        public readonly List<(int, int)> Reads = [];
        public Tile this[int x, int y]
        {
            get { Reads.Add((x, y)); return _values.GetValueOrDefault((x, y)); }
            set => _values[(x, y)] = value;
        }
    }
    public struct Tile
    {
        public bool HasTile;
        public ushort TileType;
        public short TileFrameX, TileFrameY;
    }
    public sealed class Chest
    {
        public int x, y;
        public string name = string.Empty;
        public Item[] item = NewItems(40);
        public static Dictionary<(int, int), int> Locations = [];
        public static HashSet<int> InUse = [];
        public static HashSet<(int, int)> Locked = [];
        public static List<(int, int)> FindCalls = [];
        public static int FindChest(int x, int y) { FindCalls.Add((x, y)); return Locations.GetValueOrDefault((x, y), -1); }
        public static bool IsPlayerInChest(int id) => InUse.Contains(id);
        public static int UsingChest(int id) => InUse.Contains(id) ? 0 : -1;
        public static bool IsLocked(int x, int y) => Locked.Contains((x, y));
        public static Item[] NewItems(int count) { Item[] items = new Item[count]; for (int i = 0; i < count; i++) items[i] = new(); return items; }
    }
    public sealed class Item
    {
        public int type, stack, prefix, maxStack = 9999;
        public int damage, defense, value, rare, shoot, useTime, useAnimation, mana, crit;
        public float knockBack, scale = 1f, shootSpeed;
        public bool favorited, IsACoin;
        public string Name = string.Empty;
        public int? shopCustomPrice;
        public Terraria.ModLoader.ModItem? ModItem;
        public List<Terraria.ModLoader.GlobalItem> Globals = [];
        public ChestSortCategory TestCategory;
        public object? TestMetadata; public bool TestHook;
        public bool IsAir => type == 0 || stack <= 0;
        public static int CloneCalls;
        public static int FailCloneAt;
        public Item Clone()
        {
            CloneCalls++;
            if (FailCloneAt > 0 && CloneCalls == FailCloneAt) throw new InvalidOperationException("Synthetic clone failure.");
            return (Item)MemberwiseClone();
        }
    }
    public sealed class Player
    {
        public bool active = true, dead, ghost, LockedInventory;
        public int whoAmI, chest = -1;
        public Item[] inventory = Chest.NewItems(59);
        public Item[] armor = Chest.NewItems(20), dye = Chest.NewItems(10), miscEquips = Chest.NewItems(5), miscDyes = Chest.NewItems(5);
        public Chest bank = new(), bank2 = new(), bank3 = new(), bank4 = new();
        public Microsoft.Xna.Framework.Vector2 Center = new(176, 176);
        public TerrariaChatGPT.Common.Players.GptKnowledgePlayer Memory = new();
        public TerrariaChatGPT.Common.Players.GptMemoryPlayer Preferences = new();
        public bool HasLockedInventory() => LockedInventory;
        public T GetModPlayer<T>() where T : class => (Preferences as T ?? Memory as T)!;
    }
    public static class Recipe
    {
        public static int Refreshes;
        public static bool ThrowOnRefresh;
        public static void FindRecipes() { Refreshes++; if (ThrowOnRefresh) throw new InvalidOperationException("Synthetic recipe refresh failure."); }
    }
}

namespace Terraria.ID
{
    public static class NetmodeID { public const int SinglePlayer = 0, MultiplayerClient = 1, Server = 2; }
    public static class TileID { public const ushort Containers = 21, Containers2 = 467; }
    public static class ItemID
    {
        public const int Count = 1000;
        public static readonly NameSearch Search = new();
    }
    public sealed class NameSearch { public string GetName(int id) => id switch { 1 => "Wood", 2 => "Stone", 3 => "Sword", 4 => "Coin",
        20 => "AmethystHook", 21 => "MagicMirror", 22 => "MoneyTrough", 23 => "ShadowKey", 24 => "KiteJellyfishBlue", 25 => "DefenderMedal", _ => "Item" + id }; }
}

namespace Terraria.ModLoader
{
    public sealed class GlobalItem { }
    public sealed class ModItem { public string FullName = "ExampleMod/Item"; }
}

namespace TerrariaChatGPT.Common.Players
{
    public sealed class GptMemoryPlayer
    {
        public sealed record SortingPreference(string Target);
        public bool Enabled = true;
        public long Revision;
        public Dictionary<string, SortingPreference> Rules = [];
        public TerrariaChatGPT.Common.Memory.MemoryChangeBatch? RememberSortingChoices(SortSuggestion suggestion, out string message)
        {
            if (!Enabled) { message = "Memory is paused."; return null; }
            foreach (var row in suggestion.Assignments) Rules[row.ItemKey] = new("chest:" + row.ChestName);
            Revision++; message = "Remembered choices.";
            return new("test-receipt", message);
        }
        public SortingPreference? ResolveSorting(string key, IReadOnlyList<string>? groups, out bool conflict)
        {
            conflict = false;
            if (!Enabled) return null;
            if (Rules.TryGetValue(key, out var exact)) return exact;
            if (groups is null) return null;
            if (groups.Contains("group:grappling_hooks") && Rules.TryGetValue("group:grappling_hooks", out var hooks)) return hooks;
            var matches = groups.Where(Rules.ContainsKey).Select(group => Rules[group]).ToArray();
            conflict = matches.Select(rule => rule.Target).Distinct().Count() > 1;
            return conflict ? null : matches.FirstOrDefault();
        }
    }
    public sealed class GptKnowledgePlayer
    {
        public sealed record SortingChest(int X, int Y, Terraria.Chest? Instance, string Identity = "", string Label = "", int TileType = -1, int FrameX = 0, int FrameY = 0);
        public Guid SortingSessionId = Guid.NewGuid();
        public List<SortingChest> Candidates = [];
        public List<Terraria.Chest> Refreshed = [];
        public bool ThrowOnRefresh;
        public IReadOnlyList<SortingChest> GetSortingChestCandidates() => Candidates.ToArray();
        public bool BindSortingChest(SortingChest known, Terraria.Chest chest, ushort type, short frameX, short frameY) { if (known.Instance is not null && !ReferenceEquals(known.Instance, chest)) return false; int index = Candidates.IndexOf(known); Candidates[index] = known with { Instance = chest }; return true; }
        public void RefreshSortedChest(Terraria.Chest chest) { if (ThrowOnRefresh) throw new InvalidOperationException("Synthetic observation refresh failure."); Refreshed.Add(chest); }
    }
}

namespace TerrariaChatGPT.Common.Sorting
{
    // Classification is tested separately against the real classifier; this host controls
    // its output so transaction and visibility invariants can be tested independently.
    public static class ChestSortItemClassifier
    {
        public static ChestSortCategory Classify(Terraria.Item item) => item.TestCategory;
        public static string CanonicalName(Terraria.Item item) => item.Name;
        public static IReadOnlyList<string> PreferenceGroups(Terraria.Item item) => ChestSortPlanner.PreferenceGroups(item.TestCategory).Concat(item.TestHook ? new[] { "group:grappling_hooks" } : Array.Empty<string>()).ToArray();
    }
}



