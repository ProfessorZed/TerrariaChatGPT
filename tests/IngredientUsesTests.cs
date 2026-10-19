#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using TerrariaGPT.Common.Knowledge;
using TerrariaGPT.Common.Players;
using TerrariaGPT.Common.Services;

namespace TerrariaGPT.Tests;

public static class IngredientUsesTests
{
    public static string[] Run()
    {
        var results = new List<string>();
        Check("Reverse phrases distinguish source items from output requirements and membership", () => {
            foreach (string query in new[] { "what can i make from blade of grass", "What can I craft with Blade of Grass?", "Recipes using Blade of Grass",
                "What recipes require Blade of Grass?", "Uses of Blade of Grass", "What is Blade of Grass used for?", "What can Blade of Grass be used to make?",
                "What is made from Blade of Grass?", "Which items are made from Blade of Grass?", "Which weapons can be crafted from Blade of Grass?", "What can I turn Blade of Grass into?",
                "What can I make from Blade of Grass and what materials are used for it?", "What materials can I craft from Blade of Grass?" })
                Require(LocalKnowledgeAnswer.IsIngredientUsesQuestion(query), "Unrecognized reverse phrase: " + query);
            foreach (string query in new[] { "How do I make Blade of Grass?", "Can I make Night's Edge using Blade of Grass?", "What is Blade of Grass made from?",
                "What is used to make Blade of Grass?", "What materials are used for Blade of Grass?", "What ingredients are used for making Blade of Grass?", "Are Jungle Spores used in Blade of Grass?" })
                Require(!LocalKnowledgeAnswer.IsIngredientUsesQuestion(query), "Forward/membership phrase became reverse: " + query);
        }, results);
        Check("Reverse extraction excludes the source construction recipe, disabled uses and unrelated recipes", () => {
            var f = new Fixture(); var recipes = GameReferenceFacts.BuildIngredientUses(f.Blade);
            Require(recipes.Count == 2 && recipes.All(recipe => recipe.ResultKey == "Terraria/NightsEdge"), "Wrong recipe direction or disabled/unrelated output.");
            Require(recipes.All(recipe => recipe.Ingredients.Any(item => item.AcceptedItemKeys.Contains("Terraria/BladeofGrass"))), "Output does not accept the source.");
            Require(GameReferenceFacts.BuildRecipes(new[] { f.Blade }).Single().ResultName == "Blade of Grass", "Forward recipe changed direction.");
        }, results);
        Check("Recipe-group substitutions count as uses without evaluating world conditions", () => {
            var f = new Fixture(); Item iron = Item(20, "IronBar", "Iron Bar"), lead = Item(21, "LeadBar", "Lead Bar"), output = Item(22, "GroupResult", "Group Result");
            RecipeGroup.recipeGroups[1] = new RecipeGroup { ValidItems = new HashSet<int> { iron.type, lead.type }, GetText = () => "Any Iron Bar" };
            var recipe = Recipe(output, (iron, 2)); recipe.acceptedGroups.Add(1); int calls = 0;
            recipe.Conditions.Add(new Condition { Description = new LocalizedText { Value = "Public condition" }, Predicate = () => { calls++; throw new Exception("World read"); } });
            SetRecipes(recipe); var extracted = GameReferenceFacts.BuildIngredientUses(lead).Single();
            Require(extracted.Ingredients.Single().AcceptedItemKeys.Contains("Terraria/LeadBar") && extracted.Conditions.Single() == "Public condition" && calls == 0, "Group substitution/condition copy failed.");
        }, results);
        Check("Local uses show output first, all materials, exact remembered deficits and colors", () => {
            var f = new Fixture();
            foreach (string query in new[] { "what can i make from blade of grass", "What can I craft using Blades of Grass?", "What is made from Blade of Grass?" }) {
                f.Reset(); GptAssistantService.Submit(query); string text = GptAssistantService.LastAnswer!;
                Require(!GptAssistantService.IsBusy && !text.Contains("disabled", StringComparison.OrdinalIgnoreCase), "Use lookup depended on a companion.");
                Require(text.StartsWith("Blade of Grass is an ingredient for: Night's Edge.", StringComparison.Ordinal), "Wrong leading output.");
                foreach (string material in new[] { "1 Blade of Grass", "1 Muramasa", "1 Volcano", "1 Light's Bane" }) Require(text.Contains(material, StringComparison.Ordinal), "Omitted: " + material);
                Require(text.Contains("Still need: 1 Muramasa, 1 Light's Bane", StringComparison.Ordinal), "Wrong inventory/chest deficits.");
                Require(text.Contains("Materials", StringComparison.Ordinal) && text.Contains("last seen", StringComparison.Ordinal), "Chest location/freshness omitted.");
                Require(!text.Contains("12 Jungle Spores", StringComparison.Ordinal) && !text.Contains("Guide", StringComparison.Ordinal), "Source recipe/Guide deferral replaced output recipe.");
                foreach (string kind in new[] { "item", "owned", "missing", "location" }) Require(GptAssistantService.Messages[^1].Highlights.Any(mark => mark.Kind == kind), "Missing color " + kind);
            }
        }, results);
        Check("Pronoun uses retain reverse direction and a single output becomes the next target", () => {
            var f = new Fixture(); GptAssistantService.Submit("Do I have Blade of Grass?"); GptAssistantService.Submit("What can I make from it?");
            Require(GptAssistantService.LastAnswer!.StartsWith("Blade of Grass is an ingredient for: Night's Edge.", StringComparison.Ordinal), "Pronoun use returned source construction.");
            GptAssistantService.Submit("Can I make it?");
            Require(GptAssistantService.LastAnswer!.Contains("for Night's Edge", StringComparison.Ordinal) && !GptAssistantService.LastAnswer.Contains("12 Jungle Spores", StringComparison.Ordinal), "Next target stayed on input.");
        }, results);
        Check("Compound acquisition questions keep companion routing with output-recipe public facts", () => {
            var f = new Fixture();
            foreach (string query in new[] { "What can I make from Blade of Grass and where do I find the rest?", "What can I make from Blade of Grass and what materials are used for it?" }) {
                f.Reset(); GptAssistantService.Submit(query); string text = GptAssistantService.LastAnswer!;
                Require(text.Contains("disabled", StringComparison.OrdinalIgnoreCase) && text.Contains("Night's Edge", StringComparison.Ordinal) && !text.Contains("12 Jungle Spores", StringComparison.Ordinal), "Compound routing retained input recipe or bypassed companion.");
            }
        }, results);
        Check("Large use lists disclose exact displayed and total result counts", () => {
            var f = new Fixture(); SetRecipes(Enumerable.Range(30, 40).Select(index => Recipe(Item(index, "Result" + index, "Result " + index), (f.Blade, 1))).ToArray());
            GptAssistantService.Submit("What can I make from Blade of Grass?"); string text = GptAssistantService.LastAnswer!;
            Require(text.Contains("Showing 24 of 40 distinct crafting results", StringComparison.Ordinal) && text.Contains("Ask how to make a named result", StringComparison.Ordinal), "Summary limit was hidden.");
            var empty = GptKnowledgePlayer.Snapshot with { Recipes = Array.Empty<RecipeReferenceSnapshot>(), RecipeLookupComplete = false };
            Require(IngredientUsesPlanner.TryAnswer("What can I make from Blade of Grass?", empty, "Blade of Grass")!.Text.Contains("other recipes may exist", StringComparison.Ordinal), "Incomplete lookup claimed global absence.");
        }, results);
        Check("Explicit named and passive forward requests keep complete input recipes", () => {
            var f = new Fixture(); GptAssistantService.Submit("Can I make Night's Edge using Blade of Grass?");
            Require(GptAssistantService.LastAnswer!.Contains("Recipe: 1 Blade of Grass, 1 Muramasa, 1 Volcano, 1 Light's Bane.", StringComparison.Ordinal), "Named target became reverse lookup.");
            f.Reset(); GptAssistantService.Submit("What is Blade of Grass made from?");
            Require(GptAssistantService.LastAnswer!.Contains("Recipe: 12 Jungle Spores, 15 Stinger, 3 Vine.", StringComparison.Ordinal), "Passive forward target became reverse lookup.");
        }, results);
        GptAssistantService.Reset(); return results.ToArray();
    }
    private sealed class Fixture
    {
        public Item Blade { get; }
        public Fixture() {
            ContentSamples.ItemsByType.Clear(); ItemID.Search.Names.Clear(); RecipeGroup.recipeGroups.Clear(); Lang.Tooltips.Clear();
            Blade = Item(1, "BladeofGrass", "Blade of Grass");
            Item spores = Item(2, "JungleSpores", "Jungle Spores"), stinger = Item(3, "Stinger", "Stinger"), vine = Item(4, "Vine", "Vine"), night = Item(5, "NightsEdge", "Night's Edge"),
                mura = Item(6, "Muramasa", "Muramasa"), volcano = Item(7, "Volcano", "Volcano"), lights = Item(8, "LightsBane", "Light's Bane"), blood = Item(9, "BloodButcherer", "Blood Butcherer"),
                other = Item(10, "Other", "Other"), disabled = Item(11, "Disabled", "Disabled");
            var a = Recipe(night, (Blade, 1), (mura, 1), (volcano, 1), (lights, 1)); a.requiredTile.Add(26);
            var b = Recipe(night, (Blade, 1), (mura, 1), (volcano, 1), (blood, 1)); b.requiredTile.Add(26);
            var skipped = Recipe(disabled, (Blade, 1)); skipped.Disabled = true; TileID.Search.Names[26] = "DemonAltar";
            SetRecipes(Recipe(Blade, (spores, 12), (stinger, 15), (vine, 3)), a, b, Recipe(other, (spores, 1)), skipped);
            Terraria.ModLoader.ModContent.Config = new TerrariaGPT.Common.Config.GptClientConfig { EnableOnlineAnswers = false, Provider = TerrariaGPT.Common.Config.GptProvider.WikiOnly };
            Main.gameMenu = false; Main.dedServ = false; GameReferenceFacts.ResetCache(); Reset();
        }
        public void Reset() { GptAssistantService.Reset(); GptKnowledgePlayer.Snapshot = new KnowledgeSnapshot("fixture", DateTimeOffset.UnixEpoch, Array.Empty<KnowledgeSubject>(),
            new[] { new ItemStackSnapshot("Terraria/BladeofGrass", "Blade of Grass", 1, "inventory") },
            new[] { new OwnedChestSnapshot("Materials", 0, 0, DateTimeOffset.UnixEpoch, new[] { new ItemStackSnapshot("Terraria/Volcano", "Volcano", 1, "Materials") }) }, Array.Empty<string>()); }
    }
    private static Item Item(int type, string key, string name) { var item = new Item { type = type, Name = name }; ContentSamples.ItemsByType[type] = item; ItemID.Search.Names[type] = key; return item; }
    private static Terraria.Recipe Recipe(Item output, params (Item Item, int Stack)[] ingredients) => new() { createItem = new Item { type = output.type, Name = output.Name, stack = 1 }, requiredItem = ingredients.Select(row => new Item { type = row.Item.type, Name = row.Item.Name, stack = row.Stack }).ToList() };
    private static void SetRecipes(params Terraria.Recipe[] recipes) { Main.recipe = recipes; Terraria.Recipe.numRecipes = recipes.Length; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(string name, Action test, List<string> results) { try { test(); results.Add("PASS " + name); } catch (Exception error) { throw new InvalidOperationException(name + ": " + error.Message, error); } }
}
