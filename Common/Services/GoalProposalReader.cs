#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TerrariaChatGPT.Common.Goals;

namespace TerrariaChatGPT.Common.Services;

public static class GoalProposalReader
{
    public static GoalProposal? Read(JsonElement root)
    {
        if (!root.TryGetProperty("goalSuggestion", out JsonElement value) || value.ValueKind != JsonValueKind.Object
            || !Text(value, "title", 100, out string title)
            || !value.TryGetProperty("steps", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() is < 1 or > 24) return null;
        var steps = new List<GoalStepDefinition>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !Text(row, "kind", 20, out string kind)
                || kind is not ("boss" or "item" or "craft" or "biome" or "npc" or "achievement" or "manual")
                || !Text(row, "key", 128, out string key, allowEmpty: kind == "manual") || !Text(row, "label", 120, out string label)
                || !row.TryGetProperty("target", out JsonElement count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out int target)
                || target is < 1 or > 999999) return null;
            steps.Add(new(kind, key, label, target));
        }
        // Suggestions are inert. Only the player's Set goal click passes this
        // through the local planner's authoritative item/progress validation.
        return new(title, steps.AsReadOnly());
    }

    private static bool Text(JsonElement value, string name, int limit, out string text, bool allowEmpty = false)
    {
        text = "";
        if (!value.TryGetProperty(name, out JsonElement field) || field.ValueKind != JsonValueKind.String) return false;
        text = field.GetString()?.Trim() ?? "";
        return (allowEmpty || text.Length > 0) && text.Length <= limit && !text.Any(char.IsControl);
    }
}
