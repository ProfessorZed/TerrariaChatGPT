using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaChatGPT.Common.Knowledge;

namespace TerrariaChatGPT.Tests;

public static class CraftingPlannerTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);

    public static string[] Run()
    {
        var results = new List<string>();
        // Deliberately synthetic recipe quantities: these test comparison, not a hardcoded wiki recipe.
        RecipeReferenceSnapshot grass = Recipe("Blade of Grass", [Ingredient("Jungle Spore", 12), Ingredient("Stinger", 8), Ingredient("Vine", 3)], ["Anvil"]);
        Check("Missing ingredients use exact installed-recipe deficits", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot([Stack("Jungle Spore", 5), Stack("Stinger", 8)], [], grass);
            CraftingAssessment plan = Assess("What am I missing for Blade of Grass?", snapshot);
            Equal(7, plan.Ingredients.Single(item => item.Name == "Jungle Spore").MissingCount);
            Equal(0, plan.Ingredients.Single(item => item.Name == "Stinger").MissingCount);
            Equal(3, plan.Ingredients.Single(item => item.Name == "Vine").MissingCount);
            Contains(CraftingPlanner.Format(plan), "7 Jungle Spore, 3 Vine");
        }, results);
        Check("A complete inventory reports ingredients ready without claiming a nearby station", () =>
        {
            CraftingAssessment plan = Assess("Can I make Blade of Grass?", Snapshot([Stack("Jungle Spore", 99), Stack("Stinger", 8), Stack("Vine", 3)], [], grass));
            if (!plan.InventoryHasAllIngredients || !plan.ObservedHasAllIngredients) throw new InvalidOperationException("A complete recipe was not recognized.");
            string answer = CraftingPlanner.Format(plan);
            Contains(answer, "all the ingredients in your inventory");
            Contains(answer, "Anvil");
            Contains(answer, "not checked whether that station is nearby");
        }, results);
        Check("An entirely absent recipe reports each missing ingredient", () =>
        {
            CraftingAssessment plan = Assess("Ingredients for Blade of Grass?", Snapshot([], [], grass));
            Equal(23, plan.Ingredients.Sum(item => item.MissingCount));
            if (plan.ObservedHasAllIngredients) throw new InvalidOperationException("Missing ingredients were called ready.");
        }, results);
        Check("Inventory and remembered chest counts combine but preserve stale evidence", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot([Stack("Jungle Spore", 5), Stack("Stinger", 8), Stack("Vine", 3)], [Chest("Jungle supplies", false, Stack("Jungle Spore", 7))], grass);
            CraftingAssessment plan = Assess("What do I need for Blade of Grass?", snapshot);
            if (!plan.ObservedHasAllIngredients || plan.InventoryHasAllIngredients || !plan.UsesRememberedChests) throw new InvalidOperationException("Chest freshness was lost.");
            CraftingIngredientAssessment spores = plan.Ingredients.Single(item => item.Name == "Jungle Spore");
            Equal(5, spores.InventoryCount);
            Equal(7, spores.RememberedChestCount);
            string answer = CraftingPlanner.Format(plan);
            Contains(answer, "Retrieve the chest items and reopen");
            Contains(answer, "Jungle supplies");
            Contains(answer, "last seen");
            Excludes(answer, "all the ingredients in your inventory");
        }, results);
        Check("An open chest is current but retrieval remains necessary", () =>
        {
            CraftingAssessment plan = Assess("Can I craft a Test Item?", Snapshot([], [Chest("Open materials", true, Stack("Wood", 2))], Recipe("Test Item", [Ingredient("Wood", 2)])));
            Equal(2, plan.Ingredients.Single().OpenChestCount);
            Equal(0, plan.Ingredients.Single().RememberedChestCount);
            Contains(CraftingPlanner.Format(plan), "Move the chest ingredients into your inventory");
            Contains(CraftingPlanner.Format(plan), "currently open");
        }, results);
        Check("Equipment vanity and cursor stacks cannot satisfy crafting requirements", () =>
        {
            ItemStackSnapshot excluded = Stack("Sword", 3) with { CanUseForCrafting = false };
            CraftingAssessment plan = Assess("Can I make Test Item?", Snapshot([excluded], [], Recipe("Test Item", [Ingredient("Sword", 1)])));
            Equal(1, plan.Ingredients.Single().MissingCount);
        }, results);
        Check("Recipe-group substitutions reassign items instead of greedy false deficits", () =>
        {
            RecipeReferenceSnapshot recipe = Recipe("Test Item", [new("Any bar", 1, [Key("Iron Bar"), Key("Lead Bar")]), Ingredient("Iron Bar", 1)]);
            CraftingAssessment plan = Assess("Can I make Test Item?", Snapshot([Stack("Iron Bar", 1), Stack("Lead Bar", 1)], [], recipe));
            if (!plan.InventoryHasAllIngredients) throw new InvalidOperationException("A valid substitution assignment was missed.");
            Equal("Lead Bar", plan.Ingredients[0].Sources.Single().ItemName);
            Equal("Iron Bar", plan.Ingredients[1].Sources.Single().ItemName);
        }, results);
        Check("Overlapping groups cannot consume the same stack twice", () =>
        {
            RecipeReferenceSnapshot recipe = Recipe("Test Item", [new("Any bar", 1, [Key("Iron Bar"), Key("Lead Bar")]), Ingredient("Iron Bar", 1)]);
            CraftingAssessment plan = Assess("Can I make Test Item?", Snapshot([Stack("Iron Bar", 1)], [], recipe));
            Equal(1, plan.Ingredients.Sum(item => item.MissingCount));
            Equal(1, plan.Ingredients.Sum(item => item.InventoryCount));
            if (plan.ObservedHasAllIngredients) throw new InvalidOperationException("One item was allocated twice.");
        }, results);
        Check("Current inventory wins over unnecessary remembered chest evidence", () =>
        {
            CraftingAssessment plan = Assess("Can I make Test Item?", Snapshot([Stack("Wood", 3)], [Chest("Old stock", false, Stack("Wood", 50))], Recipe("Test Item", [Ingredient("Wood", 2)])));
            if (!plan.InventoryHasAllIngredients || plan.UsesRememberedChests) throw new InvalidOperationException("Stale stock was preferred over carried ingredients.");
            Equal(2, plan.Ingredients.Single().InventoryCount);
        }, results);
        Check("A later closed-chest substitution can reroute current items safely", () =>
        {
            RecipeReferenceSnapshot recipe = Recipe("Test Item", [new("Any bar", 1, [Key("Iron Bar"), Key("Lead Bar")]), Ingredient("Iron Bar", 1)]);
            CraftingAssessment plan = Assess("Can I make Test Item?", Snapshot([Stack("Iron Bar", 1)], [Chest("Bars", false, Stack("Lead Bar", 1))], recipe));
            if (!plan.ObservedHasAllIngredients || plan.InventoryHasAllIngredients) throw new InvalidOperationException("The mixed source assignment was incorrect.");
            Equal(1, plan.Ingredients[0].RememberedChestCount);
            Equal(1, plan.Ingredients[1].InventoryCount);
        }, results);
        Check("Alternative recipes remain separate and the least missing recipe is selected", () =>
        {
            RecipeReferenceSnapshot first = Recipe("Test Item", [Ingredient("Wood", 4), Ingredient("Stone", 4)]);
            RecipeReferenceSnapshot second = Recipe("Test Item", [Ingredient("Copper", 2), Ingredient("Iron", 2)]);
            CraftingAssessment plan = Assess("Can I make Test Item?", Snapshot([Stack("Wood", 4), Stack("Iron", 2), Stack("Copper", 1)], [], first, second));
            Equal(1, plan.AlternativeCount);
            Equal(1, plan.Ingredients.Sum(item => item.MissingCount));
            if (plan.Ingredients.Any(item => item.Name == "Wood" || item.Name == "Stone")) throw new InvalidOperationException("Two alternative recipes were merged.");
            Contains(CraftingPlanner.Format(plan), "2 installed alternatives");
        }, results);
        Check("Recipe conditions are reported without inventing world availability", () =>
        {
            RecipeReferenceSnapshot recipe = Recipe("Test Item", [Ingredient("Wood", 1)], ["Work Bench"], ["At night"]);
            string answer = CraftingPlanner.Format(Assess("Can I make Test Item?", Snapshot([Stack("Wood", 1)], [], recipe)));
            Contains(answer, "At night");
            Contains(answer, "conditions have not been checked");
        }, results);
        Check("Recipe output quantities make one-batch comparisons explicit", () =>
        {
            RecipeReferenceSnapshot recipe = Recipe("Test Item", [Ingredient("Wood", 1)]) with { ResultStack = 10 };
            Contains(CraftingPlanner.Format(Assess("Can I make Test Item?", Snapshot([Stack("Wood", 1)], [], recipe))), "10 Test Item");
        }, results);
        Check("Explicit output quantities scale every requirement instead of answering for one batch", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot([Stack("Wood", 1)], [], Recipe("Test Item", [Ingredient("Wood", 1)]));
            CraftingAssessment plan = Assess("Can I make 100 Test Item?", snapshot);
            Equal(100, plan.ResultStack); Equal(100, plan.BatchCount);
            Equal(99, plan.Ingredients.Single().MissingCount);
            Contains(CraftingPlanner.Format(plan), "100 Wood");
        }, results);
        Check("Common making phrases all select the full deterministic recipe", () =>
        {
            foreach (string question in new[] { "How do I make Blade of Grass?", "How to make Blade of Grass?", "How can I make Blade of Grass?",
                "Make Blade of Grass", "Please make Blade of Grass", "How is Blade of Grass made?", "Recipe for Blade of Grass", "How do I build Blade of Grass?" })
            {
                CraftingAssessment plan = Assess(question, Snapshot([], [], grass));
                Equal(3, plan.Ingredients.Count);
                Contains(CraftingPlanner.Format(plan), "Recipe: 12 Jungle Spore, 8 Stinger, 3 Vine.");
            }
            if (CraftingPlanner.IsCraftingQuestion("What makes Blade of Grass good?")) throw new InvalidOperationException("A general weapon question became a recipe request.");
            if (CraftingPlanner.IsCraftingQuestion("What can I make with Wood?")) throw new InvalidOperationException("An ingredient-use question became a recipe for Wood.");
        }, results);
        Check("Whole output batches round up and compare inventory plus remembered supplies", () =>
        {
            RecipeReferenceSnapshot recipe = Recipe("Torch", [Ingredient("Wood", 1), Ingredient("Gel", 1)]) with { ResultStack = 3 };
            CraftingAssessment plan = Assess("How do I make 10 Torches?", Snapshot([Stack("Wood", 2)], [Chest("Gels", false, Stack("Gel", 4))], recipe));
            Equal(4, plan.BatchCount); Equal(12, plan.ResultStack); Equal(10, plan.RequestedOutputCount);
            Equal(4, plan.Ingredients[0].Required); Equal(2, plan.Ingredients[0].MissingCount);
            Equal(4, plan.Ingredients[1].RememberedChestCount); Equal(0, plan.Ingredients[1].MissingCount);
            Contains(CraftingPlanner.Format(plan), "12 total for 10 requested");
        }, results);
        Check("Common x-count prefixes and suffixes preserve exact requested output", () =>
        {
            RecipeReferenceSnapshot recipe = Recipe("Torch", [Ingredient("Wood", 1)]) with { ResultStack = 3 };
            foreach (string target in new[] { "10x Torches", "10xTorches", "x10 Torches", "x10Torches", "Torches x10", "Torches×10" })
            {
                CraftingAssessment plan = Assess("How do I make " + target + "?", Snapshot([], [], recipe));
                Equal(10, plan.RequestedOutputCount); Equal(4, plan.BatchCount); Equal(4, plan.Ingredients.Single().Required);
            }
            if (CraftingPlanner.Assess("How do I make Torches x0?", Snapshot([], [], recipe)) is not null)
                throw new InvalidOperationException("A zero suffix count became one batch.");
        }, results);
        Check("Numbers describing carried ingredients do not become requested output quantities", () =>
        {
            RecipeReferenceSnapshot recipe = Recipe("Torch", [Ingredient("Wood", 1)]) with { ResultStack = 3 };
            CraftingAssessment plan = Assess("I have 5 Wood, can I make Torch?", Snapshot([Stack("Wood", 5)], [], recipe));
            Equal(1, plan.BatchCount); Equal(3, plan.ResultStack); Equal<int?>(null, plan.RequestedOutputCount);
        }, results);
        Check("Invalid fractional negative or overflowing quantity requests never become a one-batch answer", () =>
        {
            foreach (string quantity in new[] { "0", "-2", "1.5", "1/2", "1e3", "2147483648", "999999999999999999999999999" })
                if (CraftingPlanner.Assess("How do I make " + quantity + " Test Item?", Snapshot([], [], Recipe("Test Item", [Ingredient("Wood", 1)]))) is not null)
                    throw new InvalidOperationException("An unsupported amount became a positive one-batch recipe: " + quantity);
            var excessive = Recipe("Test Item", [Ingredient("Wood", 1_000_000)]);
            if (CraftingPlanner.Assess("How do I make 1000000 Test Items?", Snapshot([], [], excessive)) is not null)
                throw new InvalidOperationException("Scaled ingredient arithmetic overflowed.");
        }, results);
        Check("Numbers inside an item's name are not interpreted as requested output counts", () =>
        {
            CraftingAssessment plan = Assess("How do I make a 1 Second Timer?", Snapshot([], [], Recipe("1 Second Timer", [Ingredient("Wire", 1)])));
            Equal(1, plan.BatchCount); Equal(1, plan.ResultStack); Equal(1, plan.Ingredients.Single().Required);
        }, results);
        Check("Resolved aliases choose the canonical recipe and never a named ingredient recipe", () =>
        {
            var target = Recipe("Blade of Grass", [Ingredient("Iron Bar", 2)]);
            var bars = Recipe("Iron Bar", [Ingredient("Iron Ore", 3)]);
            var snapshot = Snapshot([], [], target, bars);
            var plan = CraftingPlanner.Assess("How do I make BladeofGrass with Iron Bars?", snapshot, target.ResultKey, target.ResultName)!;
            Equal("Blade of Grass", plan.ResultName); Equal(2, plan.Ingredients.Single().Required);
            if (CraftingPlanner.Assess("How do I make Starfury with Iron Bars?", snapshot, Key("Starfury"), "Starfury") is not null)
                throw new InvalidOperationException("An uncraftable resolved target received an ingredient's recipe.");
        }, results);
        Check("More than four contributing chest sources remain explicitly accounted for", () =>
        {
            var chests = Enumerable.Range(1, 7).Select(index => Chest("Materials " + index, false, Stack("Wood", 1))).ToArray();
            CraftingAssessment plan = Assess("How do I make Test Item?", Snapshot([], chests, Recipe("Test Item", [Ingredient("Wood", 8)])));
            Equal(7, plan.Ingredients.Single().RememberedChestCount); Equal(1, plan.Ingredients.Single().MissingCount);
            Contains(CraftingPlanner.Format(plan), "Plus 3 more Wood across 3 additional chest sources included in these counts");
        }, results);
        Check("A named target after the crafting cue wins over an ingredient name", () =>
        {
            RecipeReferenceSnapshot bars = Recipe("Iron Bar", [Ingredient("Iron Ore", 3)]);
            RecipeReferenceSnapshot anvil = Recipe("Anvil", [Ingredient("Iron Bar", 5)]);
            CraftingAssessment plan = Assess("How many Iron Bars do I need to craft an Anvil?", Snapshot([Stack("Iron Bar", 2)], [], bars, anvil));
            Equal("Anvil", plan.ResultName);
            Equal(3, plan.Ingredients.Single().MissingCount);
        }, results);
        Check("Ownership and broad compound wiki questions bypass a local recipe-only answer", () =>
        {
            foreach (string question in new[] { "Do I have Blade of Grass?", "What does Blade of Grass do?", "What negates fall damage and can I make it?", "What do I need for Blade of Grass and where do I find the materials?" })
                if (CraftingPlanner.IsCraftingQuestion(question)) throw new InvalidOperationException("Question should use inventory/wiki reasoning: " + question);
            if (CraftingPlanner.TryAnswer("Can I make it?", Snapshot([], [], grass)) is not null) throw new InvalidOperationException("An unnamed recipe target was guessed.");
        }, results);
        Check("Disabled inventory sharing does not manufacture a personal crafting assessment", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot([Stack("Jungle Spore", 12)], [], grass) with { InventoryShared = false };
            if (CraftingPlanner.Assess("What am I missing for Blade of Grass?", snapshot) is not null) throw new InvalidOperationException("Disabled inventory was used.");
        }, results);
        Check("Assessment does not mutate detached inventory or chest observations", () =>
        {
            ItemStackSnapshot stack = Stack("Wood", 8);
            OwnedChestSnapshot chest = Chest("Timber", false, Stack("Wood", 10));
            KnowledgeSnapshot snapshot = Snapshot([stack], [chest], Recipe("Test Item", [Ingredient("Wood", 12)]));
            Assess("Can I make Test Item?", snapshot);
            Equal(8, stack.Stack);
            Equal(10, chest.Items.Single().Stack);
            Equal(ObservedAt, chest.LastObservedUtc);
        }, results);
        return results.ToArray();
    }

    private static string Key(string name) => "Fixture/" + name.Replace(" ", "");
    private static RecipeIngredientSnapshot Ingredient(string name, int count) => new(name, count, [Key(name)]);
    private static ItemStackSnapshot Stack(string name, int count) => new(Key(name), name, count, "Inventory, slot 1");
    private static OwnedChestSnapshot Chest(string label, bool open, params ItemStackSnapshot[] items) => new(label, 20, 30, ObservedAt, items) { IsCurrentlyOpen = open };
    private static RecipeReferenceSnapshot Recipe(string name, RecipeIngredientSnapshot[] ingredients, string[]? stations = null, string[]? conditions = null) => new(Key(name), name, 1, ingredients, stations ?? [], conditions ?? []);
    private static KnowledgeSnapshot Snapshot(ItemStackSnapshot[] inventory, OwnedChestSnapshot[] chests, params RecipeReferenceSnapshot[] recipes) => new("Test character", ObservedAt, [], inventory, chests, []) { Recipes = recipes };
    private static CraftingAssessment Assess(string question, KnowledgeSnapshot snapshot) => CraftingPlanner.Assess(question, snapshot) ?? throw new InvalidOperationException("Expected a named recipe assessment.");
    private static void Contains(string answer, string expected) { if (!answer.Contains(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Expected '{expected}' in: {answer}"); }
    private static void Excludes(string answer, string forbidden) { if (answer.Contains(forbidden, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Unexpected '{forbidden}' in: {answer}"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Check(string name, Action test, List<string> results)
    {
        try { test(); results.Add("PASS " + name); }
        catch (Exception exception) { throw new InvalidOperationException(name + ": " + exception.Message, exception); }
    }
}
