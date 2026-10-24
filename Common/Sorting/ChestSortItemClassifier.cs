#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ID;

namespace TerrariaChatGPT.Common.Sorting;

/// <summary>Public item definitions only. No world, NPC, or chest discovery.</summary>
internal static class ChestSortItemClassifier
{
    private static readonly HashSet<string> Herbs = new(StringComparer.Ordinal)
    { "Daybloom", "Moonglow", "Blinkroot", "Deathweed", "Waterleaf", "Fireblossom", "Shiverthorn", "Mushroom", "GlowingMushroom", "VileMushroom", "ViciousMushroom", "Pumpkin" };
    private static readonly HashSet<string> Summons = new(StringComparer.Ordinal)
    { "SuspiciousLookingEye", "WormFood", "BloodySpine", "Abeemination", "DeerThing", "SlimeCrown", "GuideVoodooDoll", "ClothierVoodooDoll", "MechanicalEye", "MechanicalWorm", "MechanicalSkull", "OcramRazor", "LihzahrdPowerCell", "TruffleWorm", "CelestialSigil", "GelatinCrystal", "PrismaticLacewing", "GoblinBattleStandard", "PirateMap", "SnowGlobe", "PumpkinMoonMedallion", "NaughtyPresent", "SolarTablet", "BloodyTear", "DD2ElderCrystal" };
    private static readonly HashSet<string> UtilityTools = new(StringComparer.Ordinal)
    { "MagicMirror", "IceMirror", "CellPhone", "Shellphone", "ShellphoneSpawn", "ShellphoneOcean", "ShellphoneHell", "MagicConch", "DemonConch", "RodofDiscord", "RodOfHarmony", "BugNet", "GoldenBugNet", "LavaproofBugNet", "BreathingReed", "Binoculars", "Ruler", "MechanicalRuler", "RopeCoil", "VineRopeCoil", "SilkRopeCoil", "WebRopeCoil", "Umbrella", "TragicUmbrella" };
    private static readonly HashSet<string> PaintTools = new(StringComparer.Ordinal)
    { "Paintbrush", "PaintRoller", "PaintScraper", "SpectrePaintbrush", "SpectrePaintRoller", "SpectrePaintScraper" };
    private static readonly HashSet<string> TravelTools = new(StringComparer.Ordinal)
    { "MagicMirror", "IceMirror", "CellPhone", "Shellphone", "ShellphoneSpawn", "ShellphoneOcean", "ShellphoneHell", "MagicConch", "DemonConch", "RodofDiscord", "RodOfHarmony" };
    private static readonly HashSet<string> Explosives = new(StringComparer.Ordinal)
    { "Bomb", "StickyBomb", "BouncyBomb", "Dynamite", "StickyDynamite", "BouncyDynamite", "Grenade", "StickyGrenade", "BouncyGrenade", "SmokeBomb", "ScarabBomb", "DirtBomb", "DirtStickyBomb", "LavaBomb", "WetBomb", "HoneyBomb", "DryBomb" };
    private static HashSet<int>? _stationTiles;
    private static int _recipeCount = -1;

    public static string CanonicalName(Item item)
    {
        string key = item.ModItem?.Name ?? ItemID.Search.GetName(item.type) ?? item.Name;
        return Regex.Replace(key, "(?<=[a-z0-9])(?=[A-Z])", " ");
    }

    // This identifies the usable grappling equipment from its public projectile
    // behavior, not the word "Hook" (which is also a crafting ingredient).
    public static IReadOnlyList<string> PreferenceGroups(Item item)
    {
        var groups = new List<string>(ChestSortPlanner.PreferenceGroups(Classify(item)));
        if (item.shoot > 0 && ContentSamples.ProjectilesByType.TryGetValue(item.shoot, out Projectile? projectile)
            && projectile.aiStyle == 7) groups.Add("group:grappling_hooks");
        return groups;
    }

    public static ChestSortCategory Classify(Item item)
    {
        if (item.IsAir || item.IsACoin) return ChestSortCategory.None;
        string key = item.ModItem?.Name ?? ItemID.Search.GetName(item.type) ?? string.Empty;
        ChestSortCategory result = item.material ? ChestSortCategory.Material : ChestSortCategory.None;
        bool vanilla = item.ModItem is null && item.type < ItemID.Count;
        bool food = item.type >= 0 && item.type < ItemID.Sets.IsFood.Length && ItemID.Sets.IsFood[item.type];
        bool seed = key.EndsWith("Seeds", StringComparison.Ordinal) || key.EndsWith("Seed", StringComparison.Ordinal) || key == "Acorn";
        bool herb = Herbs.Contains(key);
        bool ore = key.EndsWith("Ore", StringComparison.Ordinal) || (item.createTile >= 0 && item.createTile < TileID.Sets.Ore.Length && TileID.Sets.Ore[item.createTile]);
        bool bar = item.createTile == TileID.MetalBars;
        bool paint = item.paint > 0 || item.paintCoating > 0 || PaintTools.Contains(key);
        bool summon = vanilla && Summons.Contains(key);
        bool tool = item.pick > 0 || item.axe > 0 || item.hammer > 0 || (vanilla && UtilityTools.Contains(key));
        bool hook = item.shoot > 0 && ContentSamples.ProjectilesByType.TryGetValue(item.shoot, out Projectile? shot) && shot.aiStyle == 7;
        bool fishing = item.fishingPole > 0 || item.bait > 0 || (item.type >= 0 && item.type < ItemID.Sets.IsFishingCrate.Length && ItemID.Sets.IsFishingCrate[item.type]);
        bool pet = item.buffType > 0 && ((item.buffType < Main.vanityPet.Length && Main.vanityPet[item.buffType])
            || (item.buffType < Main.lightPet.Length && Main.lightPet[item.buffType]));
        bool potion = !food && item.consumable && (item.healLife > 0 || item.healMana > 0 || item.potion || (item.buffType > 0 && !pet));
        // Fallen Stars power the Star Cannon but are primarily a crafting resource.
        // Placeable sand likewise remains a building block instead of ammo.
        bool ammo = item.ammo > 0 && !item.notAmmo && item.createTile < 0 && key is not "FallenStar" and not "Gel";
        if (ore) result |= ChestSortCategory.Ore;
        if (bar) result |= ChestSortCategory.Bar;
        if (seed) result |= ChestSortCategory.Seed;
        if (herb) result |= ChestSortCategory.Plant;
        if (food) result |= ChestSortCategory.Food;
        if (paint) result |= ChestSortCategory.Paint;
        if (summon) result |= ChestSortCategory.BossSummon;
        if (tool) result |= ChestSortCategory.Tool;
        if (hook || (vanilla && TravelTools.Contains(key))) result |= ChestSortCategory.Travel;
        if (fishing && !summon) result |= ChestSortCategory.Fishing;
        if (pet) result |= ChestSortCategory.Pet;
        if (potion && !herb) result |= ChestSortCategory.Potion;
        if (ammo) result |= ChestSortCategory.Ammo;
        if (item.mountType >= 0) result |= ChestSortCategory.Mount;
        // Shader indices are unavailable in headless public-content probes; the
        // canonical vanilla item identifier still identifies dyes reliably.
        if (item.dye > 0 || item.hairDye >= 0 || (vanilla && key.EndsWith("Dye", StringComparison.Ordinal))) result |= ChestSortCategory.Dye;
        if (item.accessory) result |= ChestSortCategory.Accessory;
        if (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0)
            result |= item.vanity ? ChestSortCategory.Vanity : ChestSortCategory.Armor;
        if (item.mech) result |= ChestSortCategory.Wiring;
        if (item.createWall >= 0) result |= ChestSortCategory.Wall;
        if (item.createTile >= 0 && !ore && !bar && !seed && !herb && !paint)
        {
            int tile = item.createTile;
            if (tile == TileID.Banners) result |= ChestSortCategory.Banner | ChestSortCategory.Furniture;
            else if (IsStation(tile)) result |= ChestSortCategory.Station | ChestSortCategory.Furniture;
            else if ((tile < Main.tileSolid.Length && Main.tileSolid[tile] && !Main.tileFrameImportant[tile]) ||
                     (tile < TileID.Sets.Platforms.Length && TileID.Sets.Platforms[tile]) || key.EndsWith("Rope", StringComparison.Ordinal))
                result |= ChestSortCategory.Block;
            else result |= ChestSortCategory.Furniture;
        }
        if (item.damage > 0 && !tool && !ammo && !summon && !fishing && !paint && !pet && !item.accessory
            && item.mountType < 0 && item.headSlot < 0 && item.bodySlot < 0 && item.legSlot < 0 && item.createTile < 0)
        {
            // Throwing knives, bombs, grenades and similar consumable weapons belong
            // with consumables; reusable weapons belong with weapons.
            result |= item.consumable ? ChestSortCategory.Consumable : ChestSortCategory.Weapon;
        }
        if (vanilla && Explosives.Contains(key)) result |= ChestSortCategory.Consumable;
        if (item.consumable && result == ChestSortCategory.None) result |= ChestSortCategory.Consumable;
        return result;
    }

    private static bool IsStation(int tile)
    {
        if (_stationTiles is null || _recipeCount != Recipe.numRecipes)
        {
            _recipeCount = Recipe.numRecipes;
            _stationTiles = [];
            for (int i = 0; i < Recipe.numRecipes; i++)
                foreach (int station in Main.recipe[i].requiredTile)
                    if (station >= 0) _stationTiles.Add(station);
        }
        return _stationTiles.Contains(tile);
    }
}
