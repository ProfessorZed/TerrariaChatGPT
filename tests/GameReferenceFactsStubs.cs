#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

// This executable test harness deliberately exposes no world tiles, NPCs,
// progression flags, player inventory, or chest APIs. Only public reference
// definitions used by GameReferenceFacts are available to the compiler.
namespace Terraria
{
    public sealed class Item
    {
        public int type;
        public int stack = 1;
        public bool favorited, IsACoin;
        public string Name { get; set; } = "";
        public bool IsAir => type == 0;
        public int maxStack = 9999;
        public int damage;
        public int useTime;
        public float knockBack;
        public int defense;
        public int healLife;
        public int healMana;
        public int pick;
        public int axe;
        public int hammer;
        public ModLoader.ModItem? ModItem;
    }

    public static partial class Main
    {
        public static Recipe[] recipe = Array.Empty<Recipe>();
    }

    public sealed class Recipe
    {
        public static int numRecipes;
        public bool Disabled;
        public Item createItem = new();
        public List<Item> requiredItem = new();
        public List<int> requiredTile = new();
        public List<int> acceptedGroups = new();
        public List<Condition> Conditions = new();

        public bool AcceptedByItemGroups(int invType, int reqType) => acceptedGroups.Any(id =>
            RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup? group) &&
            group.ValidItems.Contains(invType) && group.ValidItems.Contains(reqType));
    }

    public sealed class RecipeGroup
    {
        public static Dictionary<int, RecipeGroup> recipeGroups = new();
        public HashSet<int> ValidItems = new();
        public Func<string> GetText = () => "ingredient group";
    }

    public sealed class Condition
    {
        public Localization.LocalizedText Description { get; set; } = new();
        public Func<bool> Predicate { get; set; } = () => throw new InvalidOperationException("World condition evaluated");
        public bool IsMet() => Predicate();
    }

    public static class Lang
    {
        public static Dictionary<int, UI.ItemTooltip> Tooltips = new();
        public static int TooltipReads;
        public static UI.ItemTooltip GetTooltip(int type)
        {
            TooltipReads++;
            return Tooltips.TryGetValue(type, out UI.ItemTooltip? tooltip) ? tooltip : new UI.ItemTooltip();
        }
    }
}

namespace Terraria.Localization
{
    public static class Language
    {
        public static GameCulture ActiveCulture { get; set; } = new();
    }

    public sealed class GameCulture
    {
        public string Name { get; set; } = "en-US";
    }

    public sealed class LocalizedText
    {
        public string Value { get; set; } = "";
    }
}

namespace Terraria.UI
{
    public sealed class ItemTooltip
    {
        public string[] Text { get; set; } = Array.Empty<string>();
        public int Lines => Text.Length;
        public string GetLine(int index) => Text[index];
    }
}

namespace Terraria.ID
{
    public sealed class IdSearch
    {
        public Dictionary<int, string> Names = new();
        public string? GetName(int type) => Names.TryGetValue(type, out string? name) ? name : null;
    }

    public static class ItemID
    {
        public static IdSearch Search = new();
    }

    public static class TileID
    {
        public const int Count = 1000;
        public static IdSearch Search = new();
    }

    public static class ContentSamples
    {
        public static Dictionary<int, Terraria.Item> ItemsByType = new();
    }
}

namespace Terraria.ModLoader
{
    public sealed class ModItem
    {
        public string FullName { get; set; } = "";
    }

    public sealed class ModTile
    {
        public string Name { get; set; } = "";
    }

    public static class TileLoader
    {
        public static Dictionary<int, ModTile> Tiles = new();
        public static ModTile? GetTile(int type) => Tiles.TryGetValue(type, out ModTile? tile) ? tile : null;
    }
}
