#nullable enable
using System.Collections.Generic;
using Terraria.ModLoader.IO;

namespace Terraria.ModLoader.Default;

public sealed class UnloadedPlayer : ModPlayer
{
    public IList<TagCompound> Entries { get; } = new List<TagCompound>();
    public int SaveCalls { get; private set; }
    public override void SaveData(TagCompound tag) { SaveCalls++; tag["list"] = Entries; }
}
