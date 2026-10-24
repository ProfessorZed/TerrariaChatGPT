#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerrariaChatGPT.Common.Sorting;

namespace ChestSortProbe;
public sealed class ChestSortProbe : Mod { }

/// <summary>Public definitions only, exits before this isolated process loads any world.</summary>
public sealed class PublicChestSortProbe : ModSystem
{
    public override void PostSetupRecipes()
    {
        string? output = Environment.GetEnvironmentVariable("TERRARIAGPT_CHEST_SORT_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        List<string> failures = [];
        List<object> examples = [];
        int classified = 0, caseCount = 0;
        string[] labels = ["Ores & Bars", "Material", "Potions & Food", "Weapons", "Equipment", "Furniture & Stations", "Consumables", "Building Blocks", "Paint", "Banners", "Tools", "Dyes", "Fishing"];
        ChestSortChestSnapshot[] chests = labels.Select((name, index) => new ChestSortChestSnapshot { ChestId = index, Name = name, DistanceSquared = index }).ToArray();
        try
        {
            Dictionary<string, string> cases = new(StringComparer.Ordinal)
            {
                ["Glass"] = "Building Blocks", ["DirtBlock"] = "Building Blocks", ["IceBlock"] = "Building Blocks",
                ["MudBlock"] = "Building Blocks", ["GraniteBlock"] = "Building Blocks", ["GrayBrick"] = "Building Blocks",
                ["StoneSlab"] = "Building Blocks", ["WoodPlatform"] = "Building Blocks",
                ["IronOre"] = "Ores & Bars", ["IronBar"] = "Ores & Bars", ["Gel"] = "Material",
                ["FallenStar"] = "Material", ["Mushroom"] = "Material", ["Fireblossom"] = "Material",
                ["Pumpkin"] = "Material", ["Lemon"] = "Potions & Food", ["HealingPotion"] = "Potions & Food",
                ["GravitationPotion"] = "Potions & Food", ["Minishark"] = "Weapons", ["NightsEdge"] = "Weapons",
                ["EoCShield"] = "Equipment", ["IronHelmet"] = "Equipment", ["IronPickaxe"] = "Tools",
                ["MagicMirror"] = "Tools", ["PaintRoller"] = "Paint", ["RedPaint"] = "Paint", ["RedDye"] = "Dyes",
                ["BewitchingTable"] = "Furniture & Stations", ["Extractinator"] = "Furniture & Stations",
                ["Solidifier"] = "Furniture & Stations", ["WorkBench"] = "Furniture & Stations", ["WoodenDoor"] = "Furniture & Stations",
                ["WoodenArrow"] = "Consumables", ["MeteorShot"] = "Consumables", ["Flare"] = "Consumables",
                ["Dynamite"] = "Consumables", ["Grenade"] = "Consumables", ["Shuriken"] = "Consumables",
                ["ThrowingKnife"] = "Consumables", ["SpikyBall"] = "Consumables", ["RottenEgg"] = "Consumables",
                ["GoblinArcherBanner"] = "Banners", ["GoblinPeonBanner"] = "Banners", ["GoblinWarriorBanner"] = "Banners"
            };
            foreach ((string key, string expected) in cases)
            {
                caseCount++;
                if (!ItemID.Search.TryGetId(key, out int type)) { failures.Add("Unknown fixture item: " + key); continue; }
                Item item = ContentSamples.ItemsByType[type];
                ChestSortCategory category = ChestSortItemClassifier.Classify(item);
                ChestSortItemSnapshot source = Describe(item, category);
                ChestSortPlan plan = ChestSortPlanner.Build([source], chests);
                string? actual = plan.Moves.FirstOrDefault()?.ChestName;
                if (actual != expected) failures.Add(key + ": expected " + expected + ", got " + (actual ?? "kept") + " (" + category + ")");
                examples.Add(new { key, item.Name, category = category.ToString(), expected, actual, item.hairDye });
                if (key == "Glass" && (category & ChestSortCategory.Ore) != 0) failures.Add("Glass incorrectly classified as ore.");
                if (key == "FallenStar" && (category & ChestSortCategory.Ammo) != 0) failures.Add("Fallen Star incorrectly classified as ammo.");
                ChestSortPlan missing = ChestSortPlanner.Build([source], []);
                if (missing.Moves.Count != 0 || missing.Unsorted.Count != 1 || string.IsNullOrWhiteSpace(missing.Unsorted[0].SuggestedChest))
                    failures.Add(key + ": no-chest case did not flag a recommendation.");
            }
            foreach (Item item in ContentSamples.ItemsByType.Values.Where(item => item.type > 0 && item.type < ItemID.Count))
            {
                classified++;
                ChestSortCategory category = ChestSortItemClassifier.Classify(item);
                if (item.hairDye < 0 && item.dye == 0 && !ItemID.Search.GetName(item.type).EndsWith("Dye", StringComparison.Ordinal) && (category & ChestSortCategory.Dye) != 0)
                    failures.Add("False dye: " + ItemID.Search.GetName(item.type));
                if (string.IsNullOrWhiteSpace(ChestSortItemClassifier.CanonicalName(item))) failures.Add("Empty canonical item name: " + item.type);
                string key = ItemID.Search.GetName(item.type);
                if (item.ammo > 0 && !item.notAmmo && item.createTile < 0 && key is not "FallenStar" and not "Gel"
                    && (category & ChestSortCategory.Ammo) == 0) failures.Add("Ammo category missing: " + key);
            }
            foreach (string key in new[] { "GrapplingHook", "IvyWhip", "AmethystHook", "WebSlinger", "StaticHook", "LunarHook", "Hook" })
            {
                caseCount++;
                if (!ItemID.Search.TryGetId(key, out int type)) { failures.Add("Unknown hook fixture: " + key); continue; }
                Item item = ContentSamples.ItemsByType[type];
                bool actual = ChestSortItemClassifier.PreferenceGroups(item).Contains("group:grappling_hooks");
                if (actual != (key != "Hook")) failures.Add("Incorrect grappling group: " + key);
                if (actual)
                {
                    var plan = ChestSortPlanner.Build([Describe(item, ChestSortItemClassifier.Classify(item))],
                        [new ChestSortChestSnapshot { ChestId = 1, Name = "Travel & Mobility" }]);
                    if (plan.Moves.Count != 1 || plan.Moves[0].ChestId != 1) failures.Add("Hook did not reach Travel: " + key);
                }
            }
            foreach (string key in new[] { "MagicMirror", "IceMirror", "CellPhone", "MagicConch", "DemonConch" })
            {
                caseCount++;
                if (!ItemID.Search.TryGetId(key, out int type)) { failures.Add("Unknown travel fixture: " + key); continue; }
                Item item = ContentSamples.ItemsByType[type];
                var plan = ChestSortPlanner.Build([Describe(item, ChestSortItemClassifier.Classify(item))],
                    [new ChestSortChestSnapshot { ChestId = 1, Name = "Tools" }, new ChestSortChestSnapshot { ChestId = 2, Name = "Travel & Mobility" }]);
                if (plan.Moves.Count != 1 || plan.Moves[0].ChestId != 2) failures.Add("Travel tool did not prefer Travel: " + key);
            }
            caseCount++;
            Item bag = ContentSamples.ItemsByType[ItemID.GoodieBag];
            var vanity = ChestSortPlanner.Build([Describe(bag, ChestSortItemClassifier.Classify(bag)) with { PreferredCategory = ChestSortCategory.Vanity }],
                [new ChestSortChestSnapshot { ChestId = 1, Name = "Vanity & Paints" }]);
            if (vanity.Moves.Count != 1 || vanity.Moves[0].ChestId != 1) failures.Add("Goodie Bag did not reach Vanity & Paints.");
            foreach (string key in new[] { "AmethystHook", "MagicMirror", "MoneyTrough", "ShadowKey", "KiteJellyfishBlue", "DefenderMedal" })
            {
                caseCount++;
                if (!ItemID.Search.TryGetId(key, out int type)) { failures.Add("Unknown reported inventory item: " + key); continue; }
                Item item = ContentSamples.ItemsByType[type];
                examples.Add(new { reportedItemKey = "Terraria/" + key, item.Name });
            }
        }
        catch (Exception error) { failures.Add(error.GetType().Name + ": " + error.Message); }
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = failures.Count == 0, classified, caseCount, failures, examples }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.Exit(failures.Count == 0 ? 0 : 1);
    }

    private static ChestSortItemSnapshot Describe(Item item, ChestSortCategory categories) => new()
    {
        Slot = 10, ItemKey = "Terraria/" + ItemID.Search.GetName(item.type), Name = item.Name,
        CanonicalName = ChestSortItemClassifier.CanonicalName(item), Stack = 1, MaxStack = item.maxStack,
        StackKey = item.type.ToString(), Categories = categories
    };
}
