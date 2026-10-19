#nullable enable
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using TerrariaGPT.Common.UI;

namespace TerrariaGPT.Common.Systems;

public sealed class GptGoalUiSystem : ModSystem
{
    private readonly GoalNotesPanel _notes = new();

    public override void PostUpdateInput() => _notes.Update();
    public override void OnWorldLoad() => _notes.Reset();
    public override void OnWorldUnload() => _notes.Reset();
    public override void Unload() => _notes.Reset();

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        if (Main.dedServ) return;
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (index < 0) index = layers.Count;
        layers.Insert(index, new LegacyGameInterfaceLayer("TerrariaChatGPT: Goal notes", () =>
        {
            _notes.Draw(Main.spriteBatch);
            return true;
        }, InterfaceScaleType.UI));
    }
}
