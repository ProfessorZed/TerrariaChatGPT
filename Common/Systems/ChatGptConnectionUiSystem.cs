using Terraria.ModLoader;
using TerrariaChatGPT.Common.UI;

namespace TerrariaChatGPT.Common.Systems;

public sealed class ChatGptConnectionUiSystem : ModSystem
{
    public override void PostUpdateInput() => ChatGptConnectionConfigElement.MaintainOverlays();
    public override void Unload() => ChatGptConnectionConfigElement.ClearOverlays();
}
