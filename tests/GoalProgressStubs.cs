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
        public static int maxChests = 1000, maxTilesX = 10000, maxTilesY = 10000; public static TileMap tile = new(); public static Player LocalPlayer = new();
        public static ChestReadSpy chest = new();
        public static Item mouseItem = new();
        public static WorldFileData ActiveWorldFileData = new();
        public static bool hardMode;
        public static bool remixWorld;
        public static AchievementManager Achievements = new();
    }

    public sealed class TileMap { public Tile this[int x,int y] => new() { HasTile = true, TileType = 21 }; }
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
        public static int FindChest(int x,int y) => -1;
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
        public NPC? TalkNPC { get; set; }
        public ShoppingSettings currentShoppingSettings = new() { PriceAdjustment = 1, HappinessReport = "" };
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

    public sealed class NPC
    {
        public bool active = true;
        public bool townNPC = true;
        public int createTile;
        public int type;
        public string TypeName = "";
        public Terraria.ModLoader.ModNPC? ModNPC;
        public static bool downedSlimeKing, downedBoss1, downedBoss2, downedQueenBee, downedBoss3,
            downedDeerclops, downedQueenSlime, downedMechBoss1, downedMechBoss2, downedMechBoss3,
            downedPlantBoss, downedGolemBoss, downedFishron, downedEmpressOfLight, downedAncientCultist,
            downedMoonlord, downedGoblins, downedPirates, downedFrost, downedMartians,
            savedGoblin, savedWizard, savedMech, savedStylist, savedAngler, savedTaxCollector, savedBartender, savedGolfer;
    }
    public struct ShoppingSettings
    {
        public double PriceAdjustment;
        public string HappinessReport;
    }
    public sealed class AchievementManager
    {
        public List<Achievement> Values = new();
        public int Reads;
        public List<Achievement> CreateAchievementsList() { Reads++; return new(Values); }
    }
    public sealed class Achievement
    {
        public string Name = "";
        public LocalizedText FriendlyName = new();
        public bool IsCompleted;
    }
    public sealed class LocalizedText { public string Value = ""; }
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

    public static class NPCID
    {
        public static class Sets
        {
            public static bool[] NoTownNPCHappiness = new bool[1000];
            public static bool[] IsTownPet = new bool[1000];
        }
        public const int DD2Bartender = 550;
        public static NpcNameSearch Search { get; } = new();
    }
    public sealed class NpcNameSearch { public string GetName(int type) => type == 22 ? "Guide" : "Npc" + type; }
    public static class ContentSamples { public static Dictionary<int, Terraria.Item> ItemsByType = new(); }

    public sealed class ItemNameSearch
    {
        public string GetName(int type) => type switch
        {
            1 => "IronBar",
            2 => "Torch",
            3 => "Diamond",
            4 => "TestSword",
            5 => "CopperBar",
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
    public sealed class ModNPC { public string FullName = "TestMod/TestNpc"; }

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
        public float GetFloat(string key) => TryGetValue(key, out object? value) ? (float)value : 0f;
        public double GetDouble(string key) => TryGetValue(key, out object? value) ? (double)value : 0;
        public bool GetBool(string key) => TryGetValue(key, out object? value) && (bool)value;
        public bool TryGet<T>(string key, out T value)
        { if (TryGetValue(key, out object? raw) && raw is T typed) { value = typed; return true; } value = default!; return false; }
        public IList<T> GetList<T>(string key) => TryGetValue(key, out object? value) ? (IList<T>)value : new List<T>();
    }
}

namespace TerrariaChatGPT.Common.Knowledge
{
    public static class GameReferenceFacts
    {
        public static List<RecipeReferenceSnapshot> Recipes = new();
        public static string Key(Terraria.Item item) => item.ModItem?.FullName ?? "Terraria/" + Terraria.ID.ItemID.Search.GetName(item.type);
        public static IReadOnlyList<RecipeReferenceSnapshot> BuildRecipes(IReadOnlyList<Terraria.Item> items)
        {
            var keys = new HashSet<string>(); foreach (var item in items) keys.Add(Key(item));
            return Recipes.FindAll(recipe => keys.Contains(recipe.ResultKey));
        }
    }
}

