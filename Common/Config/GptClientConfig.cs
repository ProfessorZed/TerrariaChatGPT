using System.ComponentModel;
using Newtonsoft.Json;
using Terraria.ModLoader.Config;
using TerrariaChatGPT.Common.UI;

namespace TerrariaChatGPT.Common.Config;

public enum GptProvider
{
    [LabelKey("$Mods.TerrariaChatGPT.Providers.ChatGPT")]
    ChatGPT,
    [LabelKey("$Mods.TerrariaChatGPT.Providers.WikiOnly")]
    WikiOnly,
    [LabelKey("$Mods.TerrariaChatGPT.Providers.OpenAiApi")]
    OpenAiApi
}

public sealed class GptClientConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;

    [DefaultValue(GptProvider.ChatGPT)]
    public GptProvider Provider { get; set; } = GptProvider.ChatGPT;

    [JsonIgnore]
    [ShowDespiteJsonIgnore]
    [CustomModConfigItem(typeof(ChatGptConnectionConfigElement))]
    public string ChatGptConnection => string.Empty;

    // This is only an anchor for the credential editor. The draft is owned by
    // that UI, so config clones, config JSON and multiplayer never hold a key.
    [JsonIgnore]
    [ShowDespiteJsonIgnore]
    [CustomModConfigItem(typeof(ApiKeyConfigElement))]
    public string OpenAiApiKey => string.Empty;

    [DefaultValue(true)]
    [JsonProperty("EnableWikiBridge")] // Preserve the previous saved on/off choice.
    public bool EnableOnlineAnswers { get; set; } = true;

    [DefaultValue(true)]
    public bool ShareObservedInventory { get; set; } = true;

    [DefaultValue(true)]
    public bool EnableWebSearch { get; set; } = true;

    [DefaultValue(true)]
    public bool ShareObservedProgress { get; set; } = true;

    [DefaultValue(0.9f)]
    [Range(0.7f, 1.2f)]
    [Increment(0.05f)]
    public float ChatTextScale { get; set; } = 0.9f;

    [DefaultValue(820)]
    [Range(480, 1200)]
    [Increment(20)]
    public int ChatPanelWidth { get; set; } = 820;

    [DefaultValue(390)]
    [Range(240, 720)]
    [Increment(10)]
    public int ChatPanelHeight { get; set; } = 390;
}
