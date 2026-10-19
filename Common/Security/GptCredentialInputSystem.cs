using Terraria;
using Terraria.ModLoader;
using TerrariaChatGPT.Common.UI;

namespace TerrariaChatGPT.Common.Security;

/// <summary>Keeps a detached settings row from retaining its transient draft.</summary>
public sealed class GptCredentialInputSystem : ModSystem
{
    public override void PostUpdateInput()
    {
        if (!Main.dedServ)
            ApiKeyConfigElement.MaintainInput();
    }

    public override void Unload() => ApiKeyConfigElement.DiscardActiveDraft();
}
