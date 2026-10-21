#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaChatGPT.Common.Knowledge;

// Deliberately exposes no real game, save, credential or world-state APIs.
namespace Terraria
{
    public sealed class Item { public int type; public string Name { get; set; } = ""; }
    public sealed class Player
    {
        public static int ModPlayerReads;
        public static bool RejectReads;
        public T GetModPlayer<T>() where T : new()
        {
            ModPlayerReads++;
            if (RejectReads) throw new InvalidOperationException("Chat-only mode accessed the local character.");
            return new();
        }
    }
    public static class Main
    {
        public static bool gameMenu, dedServ;
        public static string SavePath = "";
        public static string versionNumber = "fabricated-version";
        public static Player LocalPlayer = new();
    }
}
namespace Terraria.ModLoader
{
    public static class ModContent
    {
        public static TerrariaChatGPT.Common.Config.GptClientConfig Config = new();
        public static T GetInstance<T>() where T : class => (T)(object)Config;
    }
}
namespace TerrariaChatGPT.Common.Config
{
    public enum GptProvider { ChatGPT, WikiOnly, OpenAiApi }
    public sealed class GptClientConfig
    {
        public bool EnableOnlineAnswers = true, ShareObservedInventory = true;
        public bool EnableWebSearch = true, ShareObservedProgress = true;
        public int BridgePort;
        public GptProvider Provider = GptProvider.ChatGPT;
        public string ChatGptModel = "";
        public string ChatGptEffort = "";
    }
}
namespace TerrariaChatGPT.Common.Players
{
    public sealed class GptKnowledgePlayer
    {
        public static KnowledgeSnapshot Snapshot = null!;
        public KnowledgeSnapshot CreateSnapshot() => Snapshot;
        public string ClaimOpenChest() => "Fabricated chest remembered.";
        public string ForgetOpenChest() => "Fabricated chest forgotten.";
    }
}
namespace TerrariaChatGPT.Common.Knowledge
{
    // Public catalog fixture only. Local arithmetic and privacy projection use
    // the production service, snapshot, planner and formatting implementations.
    public static class GameReferenceFacts
    {
        public static int ResolveCalls;
        private static readonly Terraria.Item[] Items = {
            new() { type = 1, Name = "Starfury" }, new() { type = 2, Name = "Wooden Sword" },
            new() { type = 3, Name = "Wood" }, new() { type = 4, Name = "Lucky Horseshoe" },
            new() { type = 5, Name = "Dirt Block" }
        };
        public static Terraria.Item? ResolveItem(string question)
        {
            ResolveCalls++;
            return Items.Where(item => LocalKnowledgeAnswer.NameAppearsInQuestion(question, item.Name))
                .OrderByDescending(item => item.Name.Length).FirstOrDefault();
        }
        public static IReadOnlyList<Terraria.Item> ResolveSubjects(string question, Terraria.Item? item) => item is null ? Array.Empty<Terraria.Item>() : new[] { item };
        public static string Key(Terraria.Item item) => "Terraria/" + item.Name.Replace(" ", "");
        public static IReadOnlyList<KnowledgeFact> Build(string question, Terraria.Item item) => new[] { new KnowledgeFact("public", Key(item), item.Name + ": fabricated public reference.", "Public fixture") };
        public static IReadOnlyList<RecipeReferenceSnapshot> BuildIngredientUses(Terraria.Item item) => BuildRecipes(Items)
            .Where(recipe => recipe.Ingredients.Any(ingredient => ingredient.AcceptedItemKeys.Contains(Key(item)))).ToArray();
        public static IReadOnlyList<RecipeReferenceSnapshot> BuildRecipes(IReadOnlyList<Terraria.Item> subjects)
        {
            var recipes = new List<RecipeReferenceSnapshot>();
            if (subjects.Any(item => item.type == 2))
                recipes.Add(new RecipeReferenceSnapshot("Terraria/WoodenSword", "Wooden Sword", 1,
                    new[] { new RecipeIngredientSnapshot("Wood", 4, new[] { "Terraria/Wood" }) }, new[] { "Work Bench" }, Array.Empty<string>()));
            if (subjects.Any(item => item.type == 3))
                recipes.Add(new RecipeReferenceSnapshot("Terraria/Wood", "Wood", 1,
                    new[] { new RecipeIngredientSnapshot("Dirt Block", 1, new[] { "Terraria/DirtBlock" }) }, Array.Empty<string>(), Array.Empty<string>()));
            return recipes;
        }
    }
}
