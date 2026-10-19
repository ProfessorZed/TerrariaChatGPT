#nullable enable
using TerrariaGPT.Common.Knowledge;

namespace Terraria
{
    public sealed class Player { public T GetModPlayer<T>() where T : new() => new(); }
    public static partial class Main
    {
        public static bool gameMenu, dedServ;
        public static string SavePath = "", versionNumber = "public-fixture";
        public static Player LocalPlayer = new();
    }
}
namespace Terraria.ModLoader
{
    public static class ModContent
    {
        public static TerrariaGPT.Common.Config.GptClientConfig Config = new();
        public static T GetInstance<T>() where T : class => (T)(object)Config;
    }
}
namespace TerrariaGPT.Common.Config
{
    public enum GptProvider { ChatGPT, WikiOnly, OpenAiApi }
    public sealed class GptClientConfig
    {
        public bool EnableOnlineAnswers, ShareObservedInventory = true;
        public bool EnableWebSearch = true, ShareObservedProgress = true;
        public int BridgePort = 38765;
        public GptProvider Provider = GptProvider.ChatGPT;
    }
}
namespace TerrariaGPT.Common.Players
{
    public sealed class GptKnowledgePlayer
    {
        public static KnowledgeSnapshot Snapshot = null!;
        public KnowledgeSnapshot CreateSnapshot() => Snapshot;
        public string ClaimOpenChest() => "fixture";
        public string ForgetOpenChest() => "fixture";
    }
}
