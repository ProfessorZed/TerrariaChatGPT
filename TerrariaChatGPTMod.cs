using Terraria.ModLoader;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT;

public sealed class TerrariaChatGPTMod : Mod
{
    public override void Unload() => GptAssistantService.Reset();
}
