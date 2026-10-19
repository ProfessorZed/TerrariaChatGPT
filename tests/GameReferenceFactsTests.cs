#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using TerrariaGPT.Common.Knowledge;

namespace TerrariaGPT.Tests;

/// <summary>Exercises actual reference extraction against public-only Terraria fixtures.</summary>
public static class GameReferenceFactsTests
{
    public static string[] Run()
    {
        var results = new List<string>();
        Check("Named item resolution respects complete names and longest match", () =>
        {
            Item iron = Item(1, "IronBar", "Iron Bar");
            Item grass = Item(2, "Grass", "Grass");
            Item blade = Item(3, "BladeofGrass", "Blade of Grass");
            Same(blade, GameReferenceFacts.ResolveItem("What am I missing for Blade of Grass?"));
            Same(iron, GameReferenceFacts.ResolveItem("How many Iron Bars do I need?"));
            Require(GameReferenceFacts.ResolveItem("What grassland gear is good?") is null, "A partial name matched.");
        }, results);
        Check("Display names outrank another item's internal aliases", () =>
        {
            Item wall = Item(1, "MarbleWallUnsafe", "Marble Wall");
            Item block = Item(2, "MarbleWall", "Marble Block");
            Same(wall, GameReferenceFacts.ResolveItem("How do I make Marble Wall?"));
            Same(wall, GameReferenceFacts.ResolveItem("How do I make Marble Wall using Marble Block?"));
            Same(block, GameReferenceFacts.ResolveItem("How do I make Marble Block?"));
        }, results);
        Check("Possessives middle-noun plurals and public internal names resolve recipe targets", () =>
        {
            Item blade = Item(1, "BladeofGrass", "Blade of Grass");
            Item nights = Item(2, "NightsEdge", "Night's Edge");
            Item torch = Item(3, "Torch", "Torch");
            Item spores = Item(4, "JungleSpores", "Jungle Spores");
            Same(blade, GameReferenceFacts.ResolveItem("How do I make Blades of Grass?"));
            Same(blade, GameReferenceFacts.ResolveItem("Recipe for BladeofGrass?"));
            foreach (string name in new[] { "Night's Edge", "Night’s Edge", "Nights Edge", "NightsEdge" })
                Same(nights, GameReferenceFacts.ResolveItem("How do I make " + name + "?"));
            Same(torch, GameReferenceFacts.ResolveItem("How do I make torches?"));
            Same(spores, GameReferenceFacts.ResolveItem("Do I have Jungle Spore?"));
        }, results);

        Check("Explicit named subject remains the only public recommendation target", () =>
        {
            Item blade = Item(3, "BladeofGrass", "Blade of Grass", "Poisons enemies");
            Item horseshoe = Item(4, "LuckyHorseshoe", "Lucky Horseshoe", "Negates fall damage");
            IReadOnlyList<Item> subjects = GameReferenceFacts.ResolveSubjects("What about Blade of Grass and fall damage?", blade);
            Require(subjects.Count == 1, "A named target was expanded into unrelated recommendations.");
            Same(blade, subjects[0]);
        }, results);
        Check("Crafting target selection does not choose a longer named ingredient", () =>
        {
            Item bars = Item(1, "IronBar", "Iron Bar");
            Item anvil = Item(2, "IronAnvil", "Iron Anvil");
            Item wood = Item(3, "BorealWood", "Boreal Wood");
            Item bench = Item(4, "WorkBench", "Work Bench");
            Same(anvil, GameReferenceFacts.ResolveItem("How many Iron Bars do I need to craft an Iron Anvil?"));
            Same(bench, GameReferenceFacts.ResolveItem("Can I make a Work Bench with Boreal Wood?"));
        }, results);
        Check("All installed recipe alternatives remain available to local comparison", () =>
        {
            Item result = Item(1, "TestResult", "Test Result");
            Item wood = Item(2, "Wood", "Wood");
            SetRecipes(Enumerable.Range(1, 20).Select(count => Recipe(result, 1, (wood, count))).ToArray());
            Require(GameReferenceFacts.BuildRecipes(new[] { result }).Count == 20,
                "A local comparison lost an installed recipe alternative.");
        }, results);

        Check("Fall damage capability resolves a public tooltip without discovery state", () =>
        {
            Item horseshoe = Item(4, "LuckyHorseshoe", "Lucky Horseshoe", "Negates fall damage");
            Item sword = Item(5, "Sword", "Sword", "A basic weapon");
            IReadOnlyList<Item> subjects = GameReferenceFacts.ResolveSubjects("What negates fall damage and can I make it?", null);
            Require(subjects.Any(item => item.type == horseshoe.type), "The public fall-damage item was missed.");
            Require(subjects.All(item => item.type != sword.type), "An unrelated tooltip matched.");
            Require(GameReferenceFacts.BuildRecipes(subjects).Count == 0, "An uncraftable item received a fabricated recipe.");
        }, results);

        Check("Public tooltip facts are supplied alongside item reference data", () =>
        {
            Item horseshoe = Item(4, "LuckyHorseshoe", "Lucky Horseshoe", "Negates fall damage");
            IReadOnlyList<KnowledgeFact> facts = GameReferenceFacts.Build("What negates fall damage?", horseshoe);
            Require(facts.Any(fact => fact.Text.Contains("Negates fall damage", StringComparison.OrdinalIgnoreCase)), "Tooltip capability was excluded.");
            Require(facts.All(fact => fact.SubjectKey == "Terraria/LuckyHorseshoe"), "A fact has the wrong subject identity.");
        }, results);

        Check("Recipe extraction preserves required amounts, result count, and stations", () =>
        {
            Item blade = Item(3, "BladeofGrass", "Blade of Grass");
            Item spores = Item(6, "JungleSpores", "Jungle Spores");
            Item stinger = Item(7, "Stinger", "Stinger");
            Item vine = Item(8, "Vine", "Vine");
            TileID.Search.Names[16] = "Anvils";
            SetRecipes(Recipe(blade, 1, (spores, 12), (stinger, 15), (vine, 3)));
            Terraria.Main.recipe[0].requiredTile.Add(16);
            RecipeReferenceSnapshot recipe = GameReferenceFacts.BuildRecipes(new[] { blade }).Single();
            Require(recipe.ResultKey == "Terraria/BladeofGrass" && recipe.ResultName == "Blade of Grass" && recipe.ResultStack == 1,
                "The recipe result identity or count changed.");
            Require(recipe.Ingredients.Count == 3, "Ingredients were lost or combined.");
            Require(Ingredient(recipe, "Jungle Spores").Stack == 12 && Ingredient(recipe, "Stinger").Stack == 15 && Ingredient(recipe, "Vine").Stack == 3,
                "An ingredient requirement changed.");
            Require(recipe.Stations.SequenceEqual(new[] { "Iron or Lead Anvil" }), "The public station requirement was omitted.");
        }, results);
        Check("Public empty ingredient placeholders are ignored without dropping the whole recipe", () =>
        {
            Item result = Item(1, "TestResult", "Test Result");
            Item wood = Item(2, "Wood", "Wood");
            Terraria.Recipe recipe = Recipe(result, 1, (wood, 2));
            recipe.requiredItem.Add(new Item());
            recipe.requiredItem.Add(new Item { type = wood.type, Name = wood.Name, stack = 0 });
            SetRecipes(recipe);
            RecipeReferenceSnapshot extracted = GameReferenceFacts.BuildRecipes(new[] { result }).Single();
            Require(extracted.Ingredients.Count == 1 && extracted.Ingredients.Single().Stack == 2, "An empty placeholder invalidated real ingredients.");
        }, results);
        Check("A public representative ingredient retains its identity if no sample entry exists", () =>
        {
            Item result = Item(1, "TestResult", "Test Result");
            Item ingredient = Item(2, "PublicIngredient", "Public Ingredient");
            Terraria.Recipe recipe = Recipe(result, 1, (ingredient, 2));
            ContentSamples.ItemsByType.Remove(ingredient.type);
            SetRecipes(recipe);
            Require(GameReferenceFacts.BuildRecipes(new[] { result }).Single().Ingredients.Single().AcceptedItemKeys.Contains("Terraria/PublicIngredient"),
                "Missing sample data removed the required item's public key.");
        }, results);
        Check("Vanilla crafting station labels use player-facing names", () =>
        {
            Item result = Item(1, "TestResult", "Test Result");
            Item wood = Item(2, "Wood", "Wood");
            Terraria.Recipe recipe = Recipe(result, 1, (wood, 1));
            TileID.Search.Names[13] = "Bottles";
            TileID.Search.Names[18] = "WorkBenches";
            recipe.requiredTile.AddRange(new[] { 13, 18 });
            SetRecipes(recipe);
            Require(GameReferenceFacts.BuildRecipes(new[] { result }).Single().Stations.SequenceEqual(new[] { "Placed Bottle", "Work Bench" }),
                "Internal station identifiers reached the answer.");
        }, results);

        Check("Blade of Grass question compares extracted recipe with inventory and remembered materials", () =>
        {
            const string question = "What am I missing for Blade of Grass?";
            Item blade = Item(3, "BladeofGrass", "Blade of Grass", "Poisons enemies");
            blade.damage = 28;
            Item spores = Item(6, "JungleSpores", "Jungle Spores");
            Item stinger = Item(7, "Stinger", "Stinger");
            Item vine = Item(8, "Vine", "Vine");
            TileID.Search.Names[16] = "Anvils";
            Terraria.Recipe recipe = Recipe(blade, 1, (spores, 12), (stinger, 15), (vine, 3));
            recipe.requiredTile.Add(16);
            SetRecipes(recipe);
            Item? target = GameReferenceFacts.ResolveItem(question);
            Require(target is not null, "The user's recipe target did not resolve.");
            IReadOnlyList<Item> subjects = GameReferenceFacts.ResolveSubjects(question, target);
            IReadOnlyList<KnowledgeFact> facts = GameReferenceFacts.Build(question, target);
            DateTimeOffset observed = new(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);
            var snapshot = new KnowledgeSnapshot("Test character", observed, Array.Empty<KnowledgeSubject>(),
                new[] { Stack(spores, 10), Stack(stinger, 8), Stack(vine, 1) },
                new[] { new OwnedChestSnapshot("Jungle supplies", 20, 30, observed, new[] { Stack(stinger, 2) }) },
                Array.Empty<string>())
            {
                Facts = facts,
                Recipes = GameReferenceFacts.BuildRecipes(subjects)
            };

            string answer = CraftingPlanner.TryAnswer(question, snapshot, GameReferenceFacts.Key(target!), target!.Name)
                ?? throw new InvalidOperationException("The user's exact question did not receive a crafting assessment.");
            Require(answer.Contains("2 Jungle Spores, 5 Stinger, 2 Vine", StringComparison.Ordinal), "The inventory-plus-remembered deficits were incorrect.");
            Require(answer.Contains("Jungle supplies", StringComparison.Ordinal) && answer.Contains("last seen", StringComparison.OrdinalIgnoreCase),
                "The remembered materials lost their location or freshness.");
            Require(answer.Contains("Crafting station required: Iron or Lead Anvil", StringComparison.Ordinal) && answer.Contains("not checked whether that station is nearby", StringComparison.Ordinal),
                "The extracted station was omitted or its availability was invented.");
            Require(facts.All(fact => !fact.Text.Contains("base damage", StringComparison.OrdinalIgnoreCase) && !fact.Text.Contains("maximum stack", StringComparison.OrdinalIgnoreCase)),
                "A materials question was padded with unrelated weapon statistics.");
            Require(!answer.Contains("Poisons enemies", StringComparison.OrdinalIgnoreCase) && !answer.Contains("base damage", StringComparison.OrdinalIgnoreCase),
                "Generic weapon information replaced the user's materials answer.");

            KnowledgeSnapshot carriedOnly = snapshot with { OwnedChests = Array.Empty<OwnedChestSnapshot>() };
            string inventoryAnswer = CraftingPlanner.TryAnswer(question, carriedOnly)
                ?? throw new InvalidOperationException("Inventory-only comparison was skipped.");
            Require(inventoryAnswer.Contains("2 Jungle Spores, 7 Stinger, 2 Vine", StringComparison.Ordinal),
                "The inventory-only comparison incorrectly reused remembered chest amounts.");
        }, results);

        Check("Uncraftable recommendation explicitly distinguishes a missing recipe from missing materials", () =>
        {
            Item horseshoe = Item(4, "LuckyHorseshoe", "Lucky Horseshoe", "Negates fall damage");
            const string question = "What negates fall damage and can I make it?";
            IReadOnlyList<Item> subjects = GameReferenceFacts.ResolveSubjects(question, null);
            IReadOnlyList<RecipeReferenceSnapshot> recipes = GameReferenceFacts.BuildRecipes(subjects);
            IReadOnlyList<KnowledgeFact> facts = GameReferenceFacts.Build(question, horseshoe);
            Require(recipes.Count == 0, "An uncraftable recommendation was assigned a recipe.");
            Require(facts.Any(fact => fact.Text.Contains("no enabled crafting recipe", StringComparison.OrdinalIgnoreCase)),
                "No-recipe evidence was omitted, allowing the assistant to confuse acquisition with crafting.");
            Require(facts.Any(fact => fact.Text.Contains("does not mean it is unobtainable", StringComparison.OrdinalIgnoreCase)),
                "Missing recipe evidence incorrectly implied the item cannot be obtained.");
            DateTimeOffset observed = new(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);
            var snapshot = new KnowledgeSnapshot("Test character", observed, Array.Empty<KnowledgeSubject>(), Array.Empty<ItemStackSnapshot>(),
                Array.Empty<OwnedChestSnapshot>(), Array.Empty<string>()) { Facts = facts, Recipes = recipes };
            Require(CraftingPlanner.TryAnswer("Can I make Lucky Horseshoe?", snapshot) is null,
                "No-recipe evidence was interpreted as an empty, already-complete materials list.");
        }, results);

        Check("Recipe groups include only alternatives accepted for that ingredient", () =>
        {
            Item anvil = Item(9, "IronAnvil", "Iron Anvil");
            Item iron = Item(1, "IronBar", "Iron Bar");
            Item lead = Item(10, "LeadBar", "Lead Bar");
            Item wood = Item(11, "Wood", "Wood");
            Item boreal = Item(12, "BorealWood", "Boreal Wood");
            RecipeGroup.recipeGroups[100] = new RecipeGroup { ValidItems = new HashSet<int> { iron.type, lead.type }, GetText = () => "Any Iron Bar" };
            RecipeGroup.recipeGroups[101] = new RecipeGroup { ValidItems = new HashSet<int> { wood.type, boreal.type }, GetText = () => "Any Wood" };
            Terraria.Recipe recipe = Recipe(anvil, 1, (iron, 5));
            recipe.acceptedGroups.Add(100);
            recipe.acceptedGroups.Add(101);
            SetRecipes(recipe);
            RecipeIngredientSnapshot ingredient = GameReferenceFacts.BuildRecipes(new[] { anvil }).Single().Ingredients.Single();
            Require(ingredient.Stack == 5, "The group changed the required amount.");
            Require(ingredient.AcceptedItemKeys.OrderBy(key => key).SequenceEqual(new[] { "Terraria/IronBar", "Terraria/LeadBar" }),
                "A supported alternative was lost or an unrelated recipe group leaked into the ingredient.");
        }, results);

        Check("Disabled recipes and recipes using the target as an ingredient are excluded", () =>
        {
            Item blade = Item(3, "BladeofGrass", "Blade of Grass");
            Item spores = Item(6, "JungleSpores", "Jungle Spores");
            Item laterSword = Item(13, "LaterSword", "Later Sword");
            Terraria.Recipe enabled = Recipe(blade, 1, (spores, 12));
            Terraria.Recipe disabled = Recipe(blade, 2, (spores, 1));
            disabled.Disabled = true;
            Terraria.Recipe usedIn = Recipe(laterSword, 1, (blade, 1));
            SetRecipes(enabled, disabled, usedIn);
            IReadOnlyList<RecipeReferenceSnapshot> recipes = GameReferenceFacts.BuildRecipes(new[] { blade });
            Require(recipes.Count == 1 && recipes[0].ResultStack == 1 && recipes[0].ResultKey == "Terraria/BladeofGrass",
                "A disabled or used-in recipe was interpreted as a recipe for the target.");
        }, results);

        Check("Condition descriptions are copied without evaluating world predicates", () =>
        {
            Item result = Item(14, "ConditionalItem", "Conditional Item");
            Item ingredient = Item(11, "Wood", "Wood");
            int calls = 0;
            Terraria.Recipe recipe = Recipe(result, 3, (ingredient, 2));
            recipe.Conditions.Add(new Condition { Description = new LocalizedText { Value = "During a full moon" }, Predicate = () => { calls++; throw new InvalidOperationException("Dynamic world state was read."); } });
            SetRecipes(recipe);
            RecipeReferenceSnapshot snapshot = GameReferenceFacts.BuildRecipes(new[] { result }).Single();
            Require(snapshot.Conditions.SequenceEqual(new[] { "During a full moon" }), "The condition label was omitted.");
            Require(calls == 0, "The current world condition was evaluated.");
            Require(snapshot.ResultStack == 3, "A multi-output recipe count changed.");
        }, results);

        Check("Recipe snapshots detach ingredients and conditions from mutable definitions", () =>
        {
            Item result = Item(14, "ConditionalItem", "Conditional Item");
            Item wood = Item(11, "Wood", "Wood");
            Terraria.Recipe source = Recipe(result, 2, (wood, 4));
            source.Conditions.Add(new Condition { Description = new LocalizedText { Value = "At night" } });
            SetRecipes(source);
            RecipeReferenceSnapshot snapshot = GameReferenceFacts.BuildRecipes(new[] { result }).Single();
            source.requiredItem[0].stack = 999;
            source.requiredItem.Clear();
            source.Conditions[0].Description.Value = "Changed";
            source.createItem.stack = 100;
            Require(snapshot.Ingredients.Single().Stack == 4 && snapshot.Conditions.Single() == "At night" && snapshot.ResultStack == 2,
                "A captured recipe retained a mutable source reference.");
        }, results);

        Check("Capability candidate count is bounded", () =>
        {
            for (int index = 1; index <= 12; index++) Item(index, "FallItem" + index, "Fall Item " + index, "Negates fall damage");
            IReadOnlyList<Item> subjects = GameReferenceFacts.ResolveSubjects("What negates fall damage and can I make it?", null);
            Require(subjects.Count > 0 && subjects.Count <= 6, "The candidate set is empty or unbounded.");
        }, results);

        Check("Repeated capability queries reuse public tooltip data and language changes refresh it", () =>
        {
            Item horseshoe = Item(4, "LuckyHorseshoe", "Lucky Horseshoe", "Negates fall damage");
            const string question = "What negates fall damage and can I make it?";
            Require(GameReferenceFacts.ResolveSubjects(question, null).Count == 1, "Initial capability was missed.");
            int reads = Lang.TooltipReads;
            Require(GameReferenceFacts.ResolveSubjects(question, null).Count == 1 && Lang.TooltipReads == reads,
                "An unchanged tooltip was reparsed on a repeated question.");
            Lang.Tooltips[horseshoe.type].Text = new[] { "Immunite aux degats de chute" };
            Language.ActiveCulture = new GameCulture { Name = "fr-FR" };
            Require(GameReferenceFacts.ResolveSubjects(question, null).Count == 0 && Lang.TooltipReads > reads,
                "Language change reused stale tooltip search terms.");
        }, results);

        Check("Replacing the public sample collection refreshes cached names even at the same count", () =>
        {
            Item old = Item(1, "StablePublicIdentity", "Old Item");
            Same(old, GameReferenceFacts.ResolveItem("Where can I find Old Item?"));
            var replacement = new Item { type = 1, Name = "Replacement Item" };
            ContentSamples.ItemsByType = new Dictionary<int, Item> { [1] = replacement };
            Same(replacement, GameReferenceFacts.ResolveItem("Where can I find Replacement Item?"));
            Require(GameReferenceFacts.ResolveItem("Where can I find Old Item?") is null, "An old collection's name survived replacement.");
        }, results);

        Check("An explicit content reload refreshes names and tooltips within the same collection", () =>
        {
            Item item = Item(1, "MutablePublicItem", "Old Public Item", "Negates fall damage");
            Same(item, GameReferenceFacts.ResolveItem("Where is Old Public Item?"));
            GameReferenceFacts.ResolveSubjects("What negates fall damage?", null);
            item.Name = "Reloaded Public Item";
            Lang.Tooltips[item.type].Text = new[] { "Prevents poison damage" };
            GameReferenceFacts.ResetCache();
            Same(item, GameReferenceFacts.ResolveItem("Where is Reloaded Public Item?"));
            Require(GameReferenceFacts.ResolveSubjects("What negates fall damage?", null).Count == 0,
                "Content reload retained stale capability terms.");
            Require(GameReferenceFacts.ResolveSubjects("What prevents poison damage?", null).Count == 1,
                "Content reload omitted new capability terms.");
        }, results);

        Check("Recipe disabling remains live after the public name cache is warm", () =>
        {
            Item result = Item(1, "MutableRecipe", "Mutable Recipe");
            Item wood = Item(2, "Wood", "Wood");
            Terraria.Recipe recipe = Recipe(result, 1, (wood, 2));
            SetRecipes(recipe);
            GameReferenceFacts.ResolveItem("Can I make Mutable Recipe?");
            Require(GameReferenceFacts.BuildRecipes(new[] { result }).Count == 1, "An enabled recipe was missed.");
            recipe.Disabled = true;
            Require(GameReferenceFacts.BuildRecipes(new[] { result }).Count == 0, "A dynamically disabled recipe was served from cache.");
        }, results);

        return results.ToArray();
    }

    private static Item Item(int type, string key, string name, params string[] tooltip)
    {
        var item = new Item { type = type, Name = name };
        ContentSamples.ItemsByType[type] = item;
        ItemID.Search.Names[type] = key;
        Lang.Tooltips[type] = new ItemTooltip { Text = tooltip };
        return item;
    }

    private static Terraria.Recipe Recipe(Item result, int count, params (Item Item, int Stack)[] ingredients) => new()
    {
        createItem = new Item { type = result.type, Name = result.Name, stack = count },
        requiredItem = ingredients.Select(value => new Item { type = value.Item.type, Name = value.Item.Name, stack = value.Stack }).ToList()
    };

    private static ItemStackSnapshot Stack(Item item, int count) => new(GameReferenceFacts.Key(item), item.Name, count, "Inventory, slot 1");

    private static void SetRecipes(params Terraria.Recipe[] recipes)
    {
        Terraria.Main.recipe = recipes;
        Terraria.Recipe.numRecipes = recipes.Length;
    }

    private static RecipeIngredientSnapshot Ingredient(RecipeReferenceSnapshot recipe, string name) =>
        recipe.Ingredients.Single(ingredient => ingredient.Name == name);

    private static void Same(Item expected, Item? actual) => Require(ReferenceEquals(expected, actual), "The expected public item was not resolved.");

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }

    private static void Check(string name, Action test, List<string> results)
    {
        GameReferenceFacts.ResetCache();
        Language.ActiveCulture = new GameCulture { Name = "en-US" };
        Lang.TooltipReads = 0;
        ContentSamples.ItemsByType.Clear();
        ItemID.Search.Names.Clear();
        TileID.Search.Names.Clear();
        Terraria.ModLoader.TileLoader.Tiles.Clear();
        Lang.Tooltips.Clear();
        RecipeGroup.recipeGroups.Clear();
        SetRecipes();
        try
        {
            test();
            results.Add("PASS " + name);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(name + ": " + exception.Message, exception);
        }
    }
}
