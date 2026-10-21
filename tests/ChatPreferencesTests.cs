#nullable enable
using System;
using System.Collections.Generic;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Services;
using Terraria.ModLoader.Config;

// The real preference saver, with isolated tModLoader persistence stand-ins.
namespace Terraria { public static class Main { public static bool gameMenu, dedServ; } }
namespace Terraria.ModLoader
{
    public static class ModContent
    {
        public static GptClientConfig Config = new();
        public static T GetInstance<T>() where T : class => (T)(object)Config;
    }
}
namespace Terraria.ModLoader.Config
{
    public enum ConfigSaveResult { Success, NeedsReload }
    public static class ConfigManager
    {
        public static object GeneratePopulatedClone(GptClientConfig original) => new GptClientConfig
        {
            Provider = original.Provider, ChatGptModel = original.ChatGptModel,
            ChatGptEffort = original.ChatGptEffort, ShareObservedProgress = original.ShareObservedProgress
        };
    }
}
namespace TerrariaChatGPT.Common.Config
{
    public enum GptProvider { ChatGPT, OpenAiApi, WikiOnly }
    public sealed class GptClientConfig
    {
        public GptProvider Provider;
        public string ChatGptModel = "old-model", ChatGptEffort = "medium";
        public bool ShareObservedProgress;
        public static int Saves;
        public static bool Fail, Reject, Silent, Broadcast, SameInstance;
        public ConfigSaveResult SaveChanges(GptClientConfig pending, bool silent, bool broadcast)
        {
            Saves++; Silent = silent; Broadcast = broadcast; SameInstance = ReferenceEquals(this, pending);
            if (Fail) throw new InvalidOperationException("PRIVATE-PATH-AND-SETTINGS");
            if (Reject) return ConfigSaveResult.NeedsReload;
            ChatGptModel = pending.ChatGptModel; ChatGptEffort = pending.ChatGptEffort;
            ShareObservedProgress = pending.ShareObservedProgress;
            return ConfigSaveResult.Success;
        }
    }
}
namespace TerrariaChatGPT.Tests
{
    public static class ChatPreferencesTests
    {
        public static string[] Run()
        {
            var output = new List<string>();
            void Setup()
            {
                Terraria.Main.gameMenu = Terraria.Main.dedServ = false;
                Terraria.ModLoader.ModContent.Config = new();
                GptClientConfig.Saves = 0; GptClientConfig.Fail = GptClientConfig.Reject = false;
            }
            void Check(bool condition, string text) { if (!condition) throw new Exception(text); output.Add(text); }
            Setup();
            Check(GptChatPreferences.TrySave("chosen-model", "low", out _) && Terraria.ModLoader.ModContent.Config.ChatGptModel == "chosen-model"
                && Terraria.ModLoader.ModContent.Config.ChatGptEffort == "low", "Toolbar choices persist through the client-config save flow.");
            Check(GptClientConfig.Silent && !GptClientConfig.Broadcast && !GptClientConfig.SameInstance
                && !Terraria.ModLoader.ModContent.Config.ShareObservedProgress, "Saving is silent, uses a clone and preserves the user's privacy preferences.");
            Setup(); GptClientConfig.Fail = true;
            Check(!GptChatPreferences.TrySave("new-model", "high", out string error)
                && Terraria.ModLoader.ModContent.Config.ChatGptModel == "old-model"
                && Terraria.ModLoader.ModContent.Config.ChatGptEffort == "medium" && !error.Contains("PRIVATE"), "Failed persistence leaves active preferences intact and hides private error details.");
            Setup(); GptClientConfig.Reject = true;
            Check(!GptChatPreferences.TrySave("new-model", "high", out _) && Terraria.ModLoader.ModContent.Config.ChatGptModel == "old-model", "A rejected save does not activate the pending selection.");
            Setup();
            Check(GptChatPreferences.TrySave("", "", out _) && Terraria.ModLoader.ModContent.Config.ChatGptModel == ""
                && Terraria.ModLoader.ModContent.Config.ChatGptEffort == "", "Auto choices are saved as empty public identifiers.");
            Setup();
            Check(!GptChatPreferences.TrySave("bad\nmodel", "low", out _)
                && !GptChatPreferences.TrySave("good", "unsupported", out _) && GptClientConfig.Saves == 0, "Invalid model/effort values never reach persistence.");
            foreach (var provider in new[] { GptProvider.WikiOnly, GptProvider.OpenAiApi })
            {
                Setup(); Terraria.ModLoader.ModContent.Config.Provider = provider;
                Check(!GptChatPreferences.TrySave("new", "low", out _) && GptClientConfig.Saves == 0, provider + " cannot be changed by plan model controls.");
            }
            Setup(); Terraria.Main.dedServ = true;
            Check(!GptChatPreferences.TrySave("new", "low", out _) && GptClientConfig.Saves == 0, "Dedicated servers cannot save client chat choices.");
            Setup(); Terraria.Main.gameMenu = true;
            Check(!GptChatPreferences.TrySave("new", "low", out _) && GptClientConfig.Saves == 0, "A stale chat control cannot save choices after leaving the world.");
            return output.ToArray();
        }
    }
}
