#nullable enable
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader.Default;
using Terraria.ModLoader.IO;

namespace TerrariaGPT.Common.Players;

/// <summary>
/// Imports this project's pre-release player records after the internal mod rename.
/// Uses tModLoader's public unloaded-data save hook, never player-file access or
/// reflection. Other mods' entries and the original records remain untouched.
/// </summary>
internal static class LegacyPlayerData
{
    internal static TagCompound? Read(Player player, string playerName)
    {
        try
        {
            var unloaded = new TagCompound();
            player.GetModPlayer<UnloadedPlayer>().SaveData(unloaded);
            return Find(unloaded.GetList<TagCompound>("list"), playerName);
        }
        catch (Exception) { return null; } // Migration must never block loading a character.
    }

    internal static TagCompound? Find(IEnumerable<TagCompound> entries, string playerName)
    {
        if (playerName is not (nameof(GptKnowledgePlayer) or "GptProgressPlayer")) return null;
        TagCompound? candidate = null;
        foreach (TagCompound entry in entries)
        {
            try
            {
                if (entry.GetString("mod") != "TerrariaGPT" || entry.GetString("name") != playerName ||
                    !entry.ContainsKey("data") || entry["data"] is not TagCompound data || !MatchesSchema(data, playerName)) continue;
                // Ambiguous duplicates must not silently choose one set of goals or chests.
                if (candidate is not null) return null;
                candidate = data;
            }
            catch (Exception) { /* Ignore malformed unrelated or legacy entries. */ }
        }
        return candidate;
    }

    private static bool MatchesSchema(TagCompound data, string playerName)
    {
        int version = data.GetInt("version");
        bool knowledge = playerName == nameof(GptKnowledgePlayer);
        if (knowledge ? version is not (1 or 2) : version != 1) return false;
        if (!data.ContainsKey("worlds")) return false;
        foreach (TagCompound world in data.GetList<TagCompound>("worlds"))
        {
            if (!Guid.TryParse(world.GetString("world"), out _)) return false;
            string first = knowledge ? "subjects" : "npcs", second = knowledge ? "chests" : "notes";
            if (!world.ContainsKey(first) || !world.ContainsKey(second)) return false;
            _ = world.GetList<TagCompound>(first);
            _ = world.GetList<TagCompound>(second);
        }
        return true;
    }
}
