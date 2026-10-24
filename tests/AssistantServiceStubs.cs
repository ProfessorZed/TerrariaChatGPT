#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaChatGPT.Common.Knowledge;

// Deliberately exposes no real game, save, credential or world-state APIs.
namespace Terraria
{
    public sealed class Item
    {
        public int type, stack;
        public bool favorited, IsACoin;
        public bool IsAir => type <= 0 || stack <= 0;
        public string Name { get; set; } = "";
        public string ContentKey { get; set; } = "";
    }
    public sealed class Player
    {
        public static int ModPlayerReads;
        public static bool RejectReads;
        public TerrariaChatGPT.Common.Players.GptMemoryPlayer Preferences = new();
        public Item[] inventory = Enumerable.Range(0, 59).Select(_ => new Item()).ToArray();
        public T GetModPlayer<T>() where T : new()
        {
            // Saved preferences are separate from observed inventory/world reads.
            if (typeof(T) == typeof(TerrariaChatGPT.Common.Players.GptMemoryPlayer)) return (T)(object)Preferences;
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
        public static string versionNumber2 = "v1.4.4.9";
        public static Player LocalPlayer = new();
        public static WorldFileData ActiveWorldFileData = new();
    }
    public sealed class WorldFileData { public Guid UniqueId = Guid.NewGuid(); }
}
namespace Terraria.ModLoader
{
    public static class BuildInfo
    {
        public static Version tMLVersion = new(2026, 8, 3, 0);
    }
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
    public sealed class GptMemoryPlayer
    {
        public bool Enabled = true;
        public long Revision;
        public int AppliedCount;
        public bool ThrowOnApply;
        public string PendingUndoId = "";
        public string LastQuestion = "";
        public IReadOnlyList<TerrariaChatGPT.Common.Services.ConversationTurn> LastConversation = Array.Empty<TerrariaChatGPT.Common.Services.ConversationTurn>();
        public readonly List<TerrariaChatGPT.Common.Memory.MemoryContextEntry> Entries = new();
        public TerrariaChatGPT.Common.Memory.MemoryContext CreateContext() => new(Enabled,
            Enabled ? Entries.ToArray() : Array.Empty<TerrariaChatGPT.Common.Memory.MemoryContextEntry>());
        public TerrariaChatGPT.Common.Memory.MemoryChangeBatch? ApplyProposals(
            IReadOnlyList<TerrariaChatGPT.Common.Memory.MemoryProposal> proposals, string question,
            IReadOnlyList<TerrariaChatGPT.Common.Services.ConversationTurn> conversation)
        {
            if (ThrowOnApply) throw new InvalidOperationException("Synthetic optional memory failure.");
            if (!Enabled || proposals.Count == 0) return null;
            AppliedCount += proposals.Count; LastQuestion = question; LastConversation = conversation.ToArray(); Revision++;
            return new("synthetic-change", proposals[0].Text);
        }
        public bool Undo(string id, out string message)
        {
            if (id != PendingUndoId || id.Length == 0) { message = "No matching change."; return false; }
            PendingUndoId = ""; Entries.Clear(); Revision++; message = "Undid saved sorting choices."; return true;
        }
    }
    public sealed class GptKnowledgePlayer
    {
        public Guid SortingSessionId => Session;
        public static Guid Session = Guid.NewGuid();
        public static KnowledgeSnapshot Snapshot = null!;
        public KnowledgeSnapshot CreateSnapshot() => Snapshot;
        public string ClaimOpenChest() => "Fabricated chest remembered.";
        public string ForgetOpenChest() => "Fabricated chest forgotten.";
    }
}
namespace TerrariaChatGPT.Common.Sorting
{
    public static class ChestSortService
    {
        public static int RememberCalls;
        public static bool RememberSucceeds = true;
        public static SortSuggestion? LastRemembered;
        public static TerrariaChatGPT.Common.Memory.MemoryChangeBatch? RememberChoices(SortSuggestion proposal, out string message)
        {
            RememberCalls++;
            if (!RememberSucceeds || !Terraria.Main.LocalPlayer.Preferences.Enabled)
            { message = "Memory is unavailable."; return null; }
            LastRemembered = proposal;
            foreach (SortAssignment row in proposal.Assignments)
                Terraria.Main.LocalPlayer.Preferences.Entries.Add(new("sorting", "", "Reviewed choice", row.ItemKey, "chest:" + row.ChestName));
            Terraria.Main.LocalPlayer.Preferences.Revision++;
            Terraria.Main.LocalPlayer.Preferences.PendingUndoId = "reviewed-sort-memory";
            message = "Saved reviewed sorting choices.";
            return new("reviewed-sort-memory", message);
        }
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
        public static string Key(Terraria.Item item) => item.ContentKey.Length > 0 ? item.ContentKey : "Terraria/" + item.Name.Replace(" ", "");
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
