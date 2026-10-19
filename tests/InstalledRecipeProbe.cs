#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerrariaGPT.Common.Knowledge;

namespace RecipeProbe;

public sealed class RecipeProbe : Mod { }

/// <summary>Runs only inside the isolated test profile. No player or world access.</summary>
public sealed class PublicRecipeProbe : ModSystem
{
    public override void PostSetupRecipes()
    {
        string? destination = Environment.GetEnvironmentVariable("TERRARIAGPT_RECIPE_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(destination)) return;
        var failures = new List<string>();
        int recipesChecked = 0, questionsChecked = 0, ingredientChecks = 0, supplyChecks = 0;
        int reverseIngredientsChecked = 0, reverseRecipeChecks = 0;
        object? example = null;
        object? reverseExample = null;
        try
        {
            var empty = new KnowledgeSnapshot("", DateTimeOffset.UnixEpoch,
                Array.Empty<KnowledgeSubject>(), Array.Empty<ItemStackSnapshot>(),
                Array.Empty<OwnedChestSnapshot>(), Array.Empty<string>());
            Recipe[] recipes = Main.recipe.Take(Recipe.numRecipes)
                .Where(recipe => recipe is not null && !recipe.Disabled && !recipe.createItem.IsAir).ToArray();
            foreach (IGrouping<int, Recipe> group in recipes.GroupBy(recipe => recipe.createItem.type))
            {
                Item target = ContentSamples.ItemsByType[group.Key];
                IReadOnlyList<RecipeReferenceSnapshot> extracted = GameReferenceFacts.BuildRecipes(new[] { target });
                if (extracted.Count != group.Count()) failures.Add("Recipe extraction count: " + target.Name);
                string key = GameReferenceFacts.Key(target);
                string[] questions = new[] { "How do I make " + target.Name + "?", "How to make " + target.Name + "?", "What am I missing for " + target.Name + "?" }
                    .Concat(LocalKnowledgeAnswer.ItemNameForms(target.Name).Where(name => name != LocalKnowledgeAnswer.Normalize(target.Name))
                        .Select(name => "How do I make " + name + "?")).ToArray();
                foreach (string question in questions)
                {
                    questionsChecked++;
                    Item? resolved = GameReferenceFacts.ResolveItem(question);
                    if (resolved?.type != target.type && resolved?.Name != target.Name)
                        failures.Add("Target lookup: " + question + " -> " + (resolved?.Name ?? "<none>") + " (" + resolved?.type + "/" + target.type + ")");
                    if (!CraftingPlanner.IsCraftingQuestion(question)) failures.Add("Intent: " + question);
                }
                foreach (RecipeReferenceSnapshot recipe in extracted)
                {
                    recipesChecked++;
                    KnowledgeSnapshot snapshot = empty with { Recipes = new[] { recipe }, RecipeLookupComplete = true };
                    CraftingAssessment? plan = CraftingPlanner.Assess("How do I make " + target.Name + "?", snapshot, key, target.Name);
                    if (plan is null) { failures.Add("No assessment: " + target.Name); continue; }
                    string text = CraftingPlanner.Format(plan);
                    IReadOnlyList<AnswerHighlight> highlights = AnswerFormatting.Validate(text, AnswerFormatting.ForCrafting(plan));
                    foreach (CraftingIngredientAssessment ingredient in plan.Ingredients)
                    {
                        ingredientChecks++;
                        if (ingredient.Required != ingredient.MissingCount) failures.Add("Empty inventory counts: " + target.Name + "/" + ingredient.Name);
                        if (!text.Contains(ingredient.Name, StringComparison.Ordinal)) failures.Add("Omitted material: " + target.Name + "/" + ingredient.Name);
                        if (!highlights.Any(highlight => highlight.Text.Contains(ingredient.Name, StringComparison.Ordinal)))
                            failures.Add("Uncolored material: " + target.Name + "/" + ingredient.Name);
                    }
                    ItemStackSnapshot[] completeSupply = recipe.Ingredients.Select(ingredient =>
                        new ItemStackSnapshot(ingredient.AcceptedItemKeys[0], ingredient.Name, ingredient.Stack, "fixture inventory")).ToArray();
                    CraftingAssessment? carried = CraftingPlanner.Assess("How do I make " + target.Name + "?",
                        snapshot with { Inventory = completeSupply }, key, target.Name);
                    supplyChecks++;
                    if (carried?.InventoryHasAllIngredients != true) failures.Add("Complete inventory: " + target.Name);
                    CraftingAssessment? remembered = CraftingPlanner.Assess("How do I make " + target.Name + "?",
                        snapshot with { OwnedChests = new[] { new OwnedChestSnapshot("Fixture memory", 0, 0, DateTimeOffset.UnixEpoch, completeSupply) } }, key, target.Name);
                    supplyChecks++;
                    if (remembered?.ObservedHasAllIngredients != true || (recipe.Ingredients.Count > 0 && remembered.InventoryHasAllIngredients))
                        failures.Add("Complete chest memory: " + target.Name);
                    CraftingAssessment? equipment = CraftingPlanner.Assess("How do I make " + target.Name + "?",
                        snapshot with { Inventory = completeSupply.Select(stack => stack with { CanUseForCrafting = false }).ToArray() }, key, target.Name);
                    supplyChecks++;
                    if (equipment is null || equipment.Ingredients.Any(ingredient => ingredient.MissingCount != ingredient.Required))
                        failures.Add("Equipment excluded: " + target.Name);
                    if (target.Name == "Gravitation Potion")
                    {
                        var fixture = snapshot with {
                            Inventory = new[] { new ItemStackSnapshot("Terraria/Fireblossom", "Fireblossom", 3, "inventory") },
                            OwnedChests = new[] { new OwnedChestSnapshot("Materials", 0, 0, DateTimeOffset.UnixEpoch,
                                new[] { new ItemStackSnapshot("Terraria/Bottle", "Bottle", 37, "Materials"),
                                    new ItemStackSnapshot("Terraria/Deathweed", "Deathweed", 2, "Materials") }) }
                        };
                        CraftingAssessment detail = CraftingPlanner.Assess("How do I make Gravitation Potions?", fixture, key, target.Name)!;
                        example = new { answer = CraftingPlanner.Format(detail), highlights = AnswerFormatting.ForCrafting(detail) };
                    }
                }
            }
            var expectedUses = new Dictionary<int, List<Recipe>>();
            foreach (Recipe recipe in recipes)
            {
                var types = new HashSet<int>();
                foreach (Item ingredient in recipe.requiredItem.Where(item => item is not null && !item.IsAir && item.stack > 0))
                {
                    types.Add(ingredient.type);
                    foreach (int groupId in recipe.acceptedGroups)
                        if (RecipeGroup.recipeGroups.TryGetValue(groupId, out RecipeGroup? group) && group.ValidItems.Contains(ingredient.type))
                            foreach (int type in group.ValidItems)
                                if (recipe.AcceptedByItemGroups(type, ingredient.type)) types.Add(type);
                }
                foreach (int type in types)
                {
                    if (!expectedUses.TryGetValue(type, out var uses)) expectedUses[type] = uses = new List<Recipe>();
                    uses.Add(recipe);
                }
            }
            foreach (var pair in expectedUses)
            {
                if (!ContentSamples.ItemsByType.TryGetValue(pair.Key, out Item? ingredient) || ingredient.IsAir) continue;
                reverseIngredientsChecked++;
                var uses = GameReferenceFacts.BuildIngredientUses(ingredient);
                reverseRecipeChecks += uses.Count;
                string key = GameReferenceFacts.Key(ingredient);
                var expected = pair.Value.GroupBy(recipe => GameReferenceFacts.Key(recipe.createItem)).ToDictionary(group => group.Key, group => group.Count());
                var actual = uses.GroupBy(recipe => recipe.ResultKey).ToDictionary(group => group.Key, group => group.Count());
                if (uses.Count != pair.Value.Count || expected.Any(row => !actual.TryGetValue(row.Key, out int count) || count != row.Value)
                    || uses.Any(recipe => !recipe.Ingredients.Any(item => item.AcceptedItemKeys.Contains(key))))
                    failures.Add("Reverse recipe membership: " + ingredient.Name);
                if (key == "Terraria/BladeofGrass")
                {
                    if (!uses.Any(recipe => recipe.ResultKey == "Terraria/NightsEdge") || uses.Any(recipe => recipe.ResultKey == key))
                        failures.Add("Blade crafting uses: Night's Edge missing or source construction returned");
                    reverseExample = new { sourceKey = key, sourceName = ingredient.Name, recipes = uses,
                        answer = IngredientUsesPlanner.TryAnswer("What can I make from Blade of Grass?", empty with { Recipes = uses, RecipeLookupComplete = true }, ingredient.Name)?.Text };
                }
            }
        }
        catch (Exception error) { failures.Add(error.GetType().Name + ": " + error.Message); }
        File.WriteAllText(destination, JsonSerializer.Serialize(new {
            passed = failures.Count == 0, recipesChecked, questionsChecked, ingredientChecks, supplyChecks, reverseIngredientsChecked, reverseRecipeChecks,
            failureCount = failures.Count,
            failureKinds = failures.GroupBy(failure => failure.Split(':')[0]).ToDictionary(group => group.Key, group => group.Count()),
            failures = failures.GroupBy(failure => failure.Split(':')[0]).SelectMany(group => group.Take(25)), example, reverseExample
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        // Terminate only this disposable headless test process before world selection.
        Environment.Exit(failures.Count == 0 ? 0 : 1);
    }
}
