#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TerrariaChatGPT.Common.Players;

namespace TerrariaChatGPT.Common.Knowledge;

/// <summary>Identity tokens for observed storage only. Never discovers or scans chests.</summary>
public sealed class KnownChestIdentitySystem : ModSystem
{
    private static readonly Dictionary<string, string> Identities = new(StringComparer.Ordinal);
    private static readonly Dictionary<(int X, int Y), string> PendingRemovals = new();
    internal static string Existing(int x, int y) => Identities.GetValueOrDefault(WorldKnowledgeLedger.ChestKey(x, y), "");
    internal static string Ensure(int x, int y)
    {
        string key = WorldKnowledgeLedger.ChestKey(x, y);
        if (!Identities.TryGetValue(key, out string? identity)) Identities[key] = identity = Guid.NewGuid().ToString("N");
        return identity;
    }
    internal static void Remove(int x, int y) => Identities.Remove(WorldKnowledgeLedger.ChestKey(x, y));
    internal static void CheckRemoval(int x, int y)
    {
        string identity = Existing(x, y);
        if (identity.Length > 0) PendingRemovals[(x, y)] = identity;
    }
    public override void PostUpdateWorld()
    {
        foreach (var pending in PendingRemovals)
        {
            (int x, int y) = pending.Key;
            if (Existing(x, y) != pending.Value) continue; // A new placement already owns the coordinate.
            bool intact = x >= 0 && y >= 0 && x + 1 < Main.maxTilesX && y + 1 < Main.maxTilesY;
            if (intact)
            {
                Tile first = Main.tile[x, y];
                intact = first.HasTile && first.TileType is TileID.Containers or TileID.Containers2;
                for (int dx = 0; intact && dx < 2; dx++)
                    for (int dy = 0; intact && dy < 2; dy++)
                    {
                        Tile part = Main.tile[x + dx, y + dy];
                        intact = part.HasTile && part.TileType == first.TileType;
                    }
            }
            // GlobalTile.KillTile runs before other mods can veto the mining.
            // Only actual removal revokes knowledge; failed attempts keep it.
            if (!intact)
            {
                Remove(x, y);
                Main.LocalPlayer.GetModPlayer<GptKnowledgePlayer>().ForgetRemovedChest(x, y);
            }
        }
        PendingRemovals.Clear();
    }
    public override void ClearWorld() { Identities.Clear(); PendingRemovals.Clear(); }
    public override void SaveWorldData(TagCompound tag) => tag["knownStorage"] = Identities.Select(pair => new TagCompound
        { ["key"] = pair.Key, ["identity"] = pair.Value }).ToList();
    public override void LoadWorldData(TagCompound tag)
    {
        Identities.Clear(); PendingRemovals.Clear();
        foreach (TagCompound entry in tag.GetList<TagCompound>("knownStorage"))
        {
            string key = entry.GetString("key"), identity = entry.GetString("identity");
            string[] parts = key.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y)
                && x >= 0 && y >= 0 && Guid.TryParseExact(identity, "N", out _)) Identities[key] = identity;
        }
    }
}

/// <summary>The documented local placement event supplies the location; no area scan is used.</summary>
public sealed class KnownChestPlacement : GlobalTile
{
    public override void PlaceInWorld(int i, int j, int type, Item item)
    {
        if (!LocalWorld || type is not TileID.Containers and not TileID.Containers2 || item.createTile != type
            || i < 0 || j < 0 || i >= Main.maxTilesX || j >= Main.maxTilesY) return;
        Tile tile = Main.tile[i, j];
        if (!tile.HasTile || tile.TileType != type) return;
        int x = i - tile.TileFrameX % 36 / 18, y = j - tile.TileFrameY % 36 / 18;
        KnownChestIdentitySystem.Remove(x, y);
        KnownChestIdentitySystem.Ensure(x, y);
        Main.LocalPlayer.GetModPlayer<GptKnowledgePlayer>().RememberPlacedChest(x, y);
    }

    public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (!LocalWorld || fail || effectOnly || type is not TileID.Containers and not TileID.Containers2) return;
        // The event can concern any of a chest's four tiles. Only known tokens
        // whose footprint includes that tile are invalidated, without storage reads.
        for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
                if (KnownChestIdentitySystem.Existing(i - dx, j - dy).Length > 0)
                {
                    KnownChestIdentitySystem.CheckRemoval(i - dx, j - dy);
                }
    }
    private static bool LocalWorld => !Main.dedServ && !Main.gameMenu && Main.netMode == NetmodeID.SinglePlayer;
}
