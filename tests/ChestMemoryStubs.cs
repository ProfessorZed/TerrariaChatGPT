#nullable enable
using System;
using System.Collections.Generic;

// This deliberately small test host compiles the production observation and
// persistence classes without loading Terraria, player saves, or a world.
namespace Terraria
{
    public static class Main
    {
        public static bool dedServ;
        public static bool gameMenu;
        public static int myPlayer;
        public static int netMode;
        public static int maxChests = 1000, maxTilesX = 10000, maxTilesY = 10000;
        public static TileMap tile = new(); public static Player LocalPlayer = new();
        public static ChestReadSpy chest = new();
        public static Item mouseItem = new();
        public static WorldFileData ActiveWorldFileData = new();
    }

    public sealed class TileMap { public bool Intact = true; public Tile this[int x,int y] => new() { HasTile = Intact, TileType = 21 }; }
    public struct Tile { public bool HasTile; public ushort TileType; public short TileFrameX, TileFrameY; }
    public sealed class WorldFileData
    {
        public Guid UniqueId { get; set; }
    }

    public sealed class ChestReadSpy
    {
        private readonly Dictionary<int, Chest> _values = new();
        public List<int> Reads { get; } = new();
        public bool RecordReads { get; set; } = true;
        public int? AllowedIndex { get; set; }
        public void Set(int index, Chest value) => _values[index] = value;
        public Chest? this[int index]
        {
            get
            {
                if (RecordReads) Reads.Add(index);
                if (AllowedIndex != index)
                    throw new InvalidOperationException($"Unexpected chest read at index {index}.");
                return _values.TryGetValue(index, out Chest? value) ? value : null;
            }
        }
    }

    public sealed class Chest
    {
        public static readonly Dictionary<(int, int), int> Locations = new(); public static int FindChest(int x, int y) => Locations.GetValueOrDefault((x, y), -1);
        public int x;
        public int y;
        public string name = string.Empty;
        public Item[] item = Array.Empty<Item>();
    }

    public sealed class Item
    {
        public int createTile;
        public int type;
        public int stack;
        public string Name { get; set; } = string.Empty;
        public Terraria.ModLoader.ModItem? ModItem { get; set; }
        public bool IsAir => type == 0 || stack <= 0;
    }

    public sealed class Player
    {
        private readonly Dictionary<Type, Terraria.ModLoader.ModPlayer> _modPlayers = new();
        public T GetModPlayer<T>() where T : Terraria.ModLoader.ModPlayer, new()
        {
            if (!_modPlayers.TryGetValue(typeof(T), out var player))
            { player = new T { Player = this }; _modPlayers[typeof(T)] = player; player.Initialize(); }
            return (T)player;
        }
        public int whoAmI;
        public string name = "Chest memory test character";
        public int chest = -1;
        public Item[] inventory = Array.Empty<Item>();
        public Item[] armor = Array.Empty<Item>();
        public Item[] dye = Array.Empty<Item>();
        public Item[] miscEquips = Array.Empty<Item>();
        public Item[] miscDyes = Array.Empty<Item>();
        public bool ZoneForest, ZoneDesert, ZoneUndergroundDesert, ZoneJungle,
            ZoneSnow, ZoneBeach, ZoneCorrupt, ZoneCrimson, ZoneHallow,
            ZoneDungeon, ZoneGlowshroom, ZoneMeteor, ZoneGraveyard, ZoneShimmer,
            ZoneSkyHeight, ZoneDirtLayerHeight, ZoneRockLayerHeight, ZoneUnderworldHeight;
    }
}

namespace Terraria.ID
{
    public static class NetmodeID
    {
        public const int SinglePlayer = 0;
        public const int MultiplayerClient = 1;
        public const int Server = 2;
    }

    public static class TileID { public const int Containers = 21, Containers2 = 467; }
    public static class ItemID
    {
        public static ItemNameSearch Search { get; } = new();
    }

    public sealed class ItemNameSearch
    {
        public string GetName(int type) => type switch
        {
            1 => "IronBar",
            2 => "Torch",
            3 => "Diamond",
            _ => "TestItem" + type
        };
    }
}

namespace Terraria.ModLoader
{
    public abstract class ModSystem { public virtual void ClearWorld() {} public virtual void PostUpdateWorld() {} public virtual void SaveWorldData(IO.TagCompound tag) {} public virtual void LoadWorldData(IO.TagCompound tag) {} }
    public abstract class GlobalTile { public virtual void PlaceInWorld(int i, int j, int type, Terraria.Item item) {} public virtual void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem) {} }
    public class ModItem
    {
        public string FullName { get; set; } = "TestMod/TestItem";
    }

    public abstract class ModPlayer
    {
        public Terraria.Player Player { get; set; } = new();
        public virtual void Initialize() { }
        public virtual void OnEnterWorld() { }
        public virtual void PostUpdate() { }
        public virtual void SaveData(IO.TagCompound tag) { }
        public virtual void LoadData(IO.TagCompound tag) { }
    }
}

namespace Terraria.ModLoader.IO
{
    public sealed class TagCompound : Dictionary<string, object>
    {
        public string GetString(string key) => TryGetValue(key, out object? value) ? (string)value : string.Empty;
        public int GetInt(string key) => TryGetValue(key, out object? value) ? (int)value : 0;
        public IList<T> GetList<T>(string key) => TryGetValue(key, out object? value) ? (IList<T>)value : new List<T>();
    }
}


