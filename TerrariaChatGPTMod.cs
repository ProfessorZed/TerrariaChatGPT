using Terraria.ModLoader;
using TerrariaGPT.Common.Services;

namespace TerrariaGPT;

public sealed class TerrariaChatGPTMod : Mod
{
    public override void Unload() => GptAssistantService.Reset();
}
