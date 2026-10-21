#nullable enable
using System;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Native;

namespace TerrariaChatGPT.Common.Services;

/// <summary>Game-thread preference changes using tModLoader's client config save path.</summary>
internal static class GptChatPreferences
{
    public static bool TrySave(string modelSlug, string effort, out string message)
    {
        message = "Couldn't save this choice. Try again.";
        if (Main.dedServ || Main.gameMenu) return false;
        if (modelSlug is null || modelSlug.Length > 200 || modelSlug.Any(char.IsControl)
            || (modelSlug.Length > 0 && string.IsNullOrWhiteSpace(modelSlug))
            || effort is null || (effort.Length > 0 && !ChatGptEffortOptions.IsKnown(effort)))
        {
            message = "Choose a model and effort from the available choices.";
            return false;
        }
        try
        {
            GptClientConfig active = ModContent.GetInstance<GptClientConfig>();
            if (active.Provider != GptProvider.ChatGPT)
            {
                message = "Model and effort choices apply to ChatGPT plan mode.";
                return false;
            }
            // Clone first so failed persistence cannot activate an unsaved choice.
            // Keys and login state are not properties of this configuration.
            var pending = (GptClientConfig)ConfigManager.GeneratePopulatedClone(active);
            pending.ChatGptModel = modelSlug;
            pending.ChatGptEffort = effort;
            if (active.SaveChanges(pending, silent: true, broadcast: false) != ConfigSaveResult.Success)
                return false;
            message = "Saved. Applies to your next question.";
            return true;
        }
        catch
        {
            // Never expose serialized settings, filesystem paths or exception bodies.
            return false;
        }
    }
}
