#nullable enable
using System;

namespace TerrariaChatGPT.Common.UI;

/// <summary>Short progress labels keep known counts separate from remembered supplies.</summary>
internal static class GoalNoteText
{
    public static string Label(string label, string kind, int target) =>
        kind == "ingredient" ? Math.Max(1, target) + " " + label : label;

    public static string Summary(string kind, int current, int target, int remembered, bool complete)
    {
        if (kind is "ingredient" or "item" or "craft")
        {
            if (complete) return kind == "ingredient" ? "Step complete" : "Goal item obtained";
            string live = Math.Max(0, current) + " / " + Math.Max(1, target) + " live";
            return remembered > 0 ? live + " + " + remembered + " remembered" : live;
        }
        return kind switch
        {
            "boss" => complete ? "Defeated - world record" : "Not defeated - world record",
            "biome" => complete ? "Personally visited" : "Not recorded as visited yet",
            "npc" => complete ? "Encountered or rescued" : "Not encountered or rescued yet",
            "achievement" => complete ? "Completed - account achievement" : "Account achievement incomplete",
            "manual" => complete ? "Marked complete - click to undo" : "Manual step - click when complete",
            _ => complete ? "Complete" : "Not completed yet"
        };
    }
}
