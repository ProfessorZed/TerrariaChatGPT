#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaChatGPT.Common.Knowledge;

namespace Terraria
{
    public sealed class Player
    {
        private readonly TerrariaChatGPT.Common.Players.GptMemoryPlayer _memory = new();
        public Item[] inventory = Enumerable.Range(0, 59).Select(_ => new Item()).ToArray();
        public T GetModPlayer<T>() where T : new() => typeof(T) == typeof(TerrariaChatGPT.Common.Players.GptMemoryPlayer)
            ? (T)(object)_memory : new();
    }
    public static partial class Main
    {
        public static bool gameMenu, dedServ;
        public static string SavePath = "", versionNumber = "public-fixture";
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
        public static System.Version tMLVersion = new(2026, 8, 3, 0);
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
        public bool EnableOnlineAnswers, ShareObservedInventory = true;
        public bool EnableWebSearch = true, ShareObservedProgress = true;
        public int BridgePort = 38765;
        public GptProvider Provider = GptProvider.ChatGPT;
        public string ChatGptModel = "", ChatGptEffort = "";
    }
}
namespace TerrariaChatGPT.Common.Players
{
    public sealed class GptKnowledgePlayer
    {
        public Guid SortingSessionId => Guid.Empty;
        public static KnowledgeSnapshot Snapshot = null!;
        public KnowledgeSnapshot CreateSnapshot() => Snapshot;
        public string ClaimOpenChest() => "fixture";
        public string ForgetOpenChest() => "fixture";
    }
    public sealed class GptMemoryPlayer
    {
        public bool Enabled => false;
        public long Revision => 0;
        public TerrariaChatGPT.Common.Memory.MemoryContext CreateContext() => new(false, Array.Empty<TerrariaChatGPT.Common.Memory.MemoryContextEntry>());
        public TerrariaChatGPT.Common.Memory.MemoryChangeBatch? ApplyProposals(
            IReadOnlyList<TerrariaChatGPT.Common.Memory.MemoryProposal> proposals, string question,
            IReadOnlyList<TerrariaChatGPT.Common.Services.ConversationTurn> conversation) => null;
    }
}
namespace TerrariaChatGPT.Common.Sorting
{
    public static class ChestSortService
    {
        public static TerrariaChatGPT.Common.Memory.MemoryChangeBatch? RememberChoices(SortSuggestion proposal, out string message)
            => throw new InvalidOperationException("Recipe lookup attempted to save a sorting choice.");
    }
}
