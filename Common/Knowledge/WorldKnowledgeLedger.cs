#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ModLoader.IO;

namespace TerrariaChatGPT.Common.Knowledge;

/// <summary>Player-owned persistence, partitioned by world UUID.</summary>
internal sealed class WorldKnowledgeLedger
{
    public Dictionary<string, KnowledgeSubject> Subjects { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, OwnedChestSnapshot> RememberedChests { get; } = new(StringComparer.Ordinal);
    public HashSet<string> IgnoredChests { get; } = new(StringComparer.Ordinal);

    public void Discover(KnowledgeSubject subject) => Subjects[subject.Kind + ":" + subject.Key] = subject;

    public TagCompound Save(string worldKey) => new()
    {
        ["world"] = worldKey,
        ["subjects"] = Subjects.Values.Select(subject => new TagCompound
        {
            ["kind"] = subject.Kind,
            ["key"] = subject.Key,
            ["name"] = subject.Name
        }).ToList(),
        ["ignoredChests"] = IgnoredChests.OrderBy(key => key, StringComparer.Ordinal).ToList(),
        // Keep the existing save key so older manually remembered chests survive.
        ["chests"] = RememberedChests.Values.Select(chest => new TagCompound
        {
            ["label"] = chest.Label,
            ["x"] = chest.TileX,
            ["y"] = chest.TileY,
            ["observed"] = chest.LastObservedUtc.ToString("O"),
            ["items"] = chest.Items.Select(item => new TagCompound
            {
                ["key"] = item.ItemKey,
                ["name"] = item.Name,
                ["stack"] = item.Stack,
                ["location"] = item.Location
            }).ToList()
        }).ToList()
    };

    public static WorldKnowledgeLedger Load(TagCompound tag)
    {
        var ledger = new WorldKnowledgeLedger();
        foreach (TagCompound saved in tag.GetList<TagCompound>("subjects"))
        {
            string kind = saved.GetString("kind");
            string key = saved.GetString("key");
            string name = saved.GetString("name");
            if ((kind is "item" or "biome") && key.Length > 0 && name.Length > 0)
                ledger.Discover(new KnowledgeSubject(kind, key, name));
        }

        foreach (string key in tag.GetList<string>("ignoredChests"))
            if (!string.IsNullOrWhiteSpace(key))
                ledger.IgnoredChests.Add(key);

        foreach (TagCompound saved in tag.GetList<TagCompound>("chests"))
        {
            if (!DateTimeOffset.TryParse(saved.GetString("observed"), out DateTimeOffset observed))
                continue;

            var items = new List<ItemStackSnapshot>();
            foreach (TagCompound item in saved.GetList<TagCompound>("items"))
            {
                string key = item.GetString("key");
                int stack = item.GetInt("stack");
                if (key.Length > 0 && stack > 0)
                    items.Add(new ItemStackSnapshot(key, item.GetString("name"), stack, item.GetString("location")));
            }

            var chest = new OwnedChestSnapshot(saved.GetString("label"), saved.GetInt("x"), saved.GetInt("y"), observed, items.AsReadOnly());
            string chestKey = ChestKey(chest.TileX, chest.TileY);
            if (!ledger.IgnoredChests.Contains(chestKey))
                ledger.RememberedChests[chestKey] = chest;
        }

        return ledger;
    }

    public static string ChestKey(int x, int y) => $"{x},{y}";
}
