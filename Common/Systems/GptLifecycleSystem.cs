using Terraria;
using Terraria.ModLoader;
using TerrariaGPT.Common.Services;
using TerrariaGPT.Common.Knowledge;
using TerrariaGPT.Common.Native;

namespace TerrariaGPT.Common.Systems;

public sealed class GptLifecycleSystem : ModSystem
{
    public override void PostUpdateInput()
    {
        if (!Main.dedServ) GptAssistantService.Pump();
    }
    public override void Load()
    {
        GameReferenceFacts.ResetCache();
        if (!Main.dedServ) NativeAssistantRuntime.Initialize();
    }
    public override void Unload()
    {
        GptAssistantService.Reset();
        NativeAssistantRuntime.Shutdown();
        GameReferenceFacts.ResetCache();
    }
    public override void OnWorldLoad() { GptAssistantService.Reset(); GameReferenceFacts.ResetCache(); }
    public override void OnWorldUnload() { GptAssistantService.Reset(); GameReferenceFacts.ResetCache(); }
}
