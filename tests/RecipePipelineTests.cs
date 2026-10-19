#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using TerrariaGPT.Common.Knowledge;
using TerrariaGPT.Common.Players;
using TerrariaGPT.Common.Services;

namespace TerrariaGPT.Tests;

/// <summary>Real question -> public resolver -> recipe extraction -> comparison -> displayed colored answer.</summary>
public static class RecipePipelineTests
{
    public static string[] Run()
    {
        var results = new List<string>();
        Item fire = Item(1, "Fireblossom"), water = Item(2, "Bottled Water"), feather = Item(3, "Feather"),
            death = Item(4, "Deathweed"), blink = Item(5, "Blinkroot"), potion = Item(6, "Gravitation Potion"),
            bottle = Item(7, "Bottle"), gel = Item(8, "Gel"), wood = Item(9, "Wood"),
            torch = Item(10, "Torch"), vine = Item(11, "Vine"), spores = Item(12, "Jungle Spores"),
            blade = Item(13, "Blade of Grass"), stinger = Item(14, "Stinger");
        Main.recipe = new[] {
            Recipe(potion, 1, (water, 1), (fire, 1), (death, 1), (blink, 1), (feather, 1)),
            Recipe(torch, 3, (gel, 1), (wood, 1)),
            Recipe(blade, 1, (spores, 12), (stinger, 15), (vine, 3))
        };
        Terraria.Recipe.numRecipes = Main.recipe.Length;
        TileID.Search.Names[13] = "PlacedBottle";
        Main.recipe[0].requiredTile.Add(13);
        GameReferenceFacts.ResetCache();
        foreach (string query in new[] { "How do I make gravitation potions?", "How to make Gravitation Potion?", "What's the recipe for Gravitation Potions?", "What am I missing for Gravitation Potion?" })
        {
            Reset(new[] { Stack(fire, 3) }, new[] { Stack(bottle, 37), Stack(death, 2) });
            GptAssistantService.Submit(query);
            string answer = GptAssistantService.LastAnswer ?? "";
            Require(!GptAssistantService.IsBusy, "Crafting waited for a network request.");
            Require(!answer.Contains("disabled", StringComparison.OrdinalIgnoreCase), "Recipe routing fell through to wiki: " + query);
            foreach (Item ingredient in new[] { water, fire, death, blink, feather })
                Require(answer.Contains("1 " + ingredient.Name, StringComparison.Ordinal), "Incomplete recipe: " + ingredient.Name);
            Require(answer.Contains("Materials", StringComparison.Ordinal), "Remembered material source omitted.");
            foreach (string kind in new[] { "item", "missing", "owned", "location" })
                Require(GptAssistantService.Messages[^1].Highlights.Any(mark => mark.Kind == kind), "Missing highlight kind " + kind);
            Require(!answer.Contains("Guide", StringComparison.Ordinal), "Answer deferred to Guide.");
            results.Add("PASS Full colored recipe and remembered comparison: " + query);
        }
        Reset(new[] { Stack(wood, 1) }, new[] { Stack(gel, 1) });
        GptAssistantService.Submit("How do I make torches?");
        Require(GptAssistantService.LastAnswer!.Contains("3 Torch", StringComparison.Ordinal), "Recipe batch output was omitted: " + GptAssistantService.LastAnswer);
        results.Add("PASS Plural recipe target and batch output use the full pipeline.");

        Reset(new[] { Stack(spores, 2) }, new[] { Stack(spores, 5), Stack(stinger, 15), Stack(vine, 1) });
        GptAssistantService.Submit("How to make Blade of Grass?");
        string bladeAnswer = GptAssistantService.LastAnswer!;
        Require(bladeAnswer.Contains("5 Jungle Spores", StringComparison.Ordinal) && bladeAnswer.Contains("2 Vine", StringComparison.Ordinal), "Cross-chest deficit was not exact.");
        GptAssistantService.Submit("Can I make it?");
        Require(GptAssistantService.LastAnswer == bladeAnswer, "Follow-up changed target or arithmetic.");
        results.Add("PASS Exact shared-material deficits survive a pronoun follow-up.");
        GptAssistantService.Reset();
        return results.ToArray();
    }

    private static void Reset(ItemStackSnapshot[] inventory, ItemStackSnapshot[] chest)
    {
        GptAssistantService.Reset();
        Terraria.ModLoader.ModContent.Config.Provider = TerrariaGPT.Common.Config.GptProvider.WikiOnly;
        GptKnowledgePlayer.Snapshot = new KnowledgeSnapshot("fixture", DateTimeOffset.UnixEpoch,
            Array.Empty<KnowledgeSubject>(), inventory,
            new[] { new OwnedChestSnapshot("Materials", 0, 0, DateTimeOffset.UnixEpoch, chest) }, Array.Empty<string>());
    }
    private static Item Item(int type, string name)
    {
        var item = new Item { type = type, Name = name };
        ContentSamples.ItemsByType[type] = item;
        ItemID.Search.Names[type] = name.Replace(" ", "");
        return item;
    }
    private static ItemStackSnapshot Stack(Item item, int amount) => new(GameReferenceFacts.Key(item), item.Name, amount, "inventory");
    private static Recipe Recipe(Item result, int count, params (Item Item, int Count)[] ingredients) => new() {
        createItem = new Item { type = result.type, Name = result.Name, stack = count },
        requiredItem = ingredients.Select(pair => new Item { type = pair.Item.type, Name = pair.Item.Name, stack = pair.Count }).ToList()
    };
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
