using Terraria.ModLoader;
using TerrariaGPT.Common.UI;

namespace TerrariaGPT.Common.Systems;

public sealed class ChatGptConnectionUiSystem : ModSystem
{
    public override void PostUpdateInput() => ChatGptConnectionConfigElement.MaintainOverlays();
    public override void Unload() => ChatGptConnectionConfigElement.ClearOverlays();
}
