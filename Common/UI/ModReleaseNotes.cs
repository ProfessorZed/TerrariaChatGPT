#nullable enable
using System;

namespace TerrariaChatGPT.Common.UI;

internal static class ModReleaseNotes
{
    // Select notes using the loaded mod version so future builds never show a stale release summary.
    public static string ForVersion(Version version)
    {
        string heading = "What's new in " + version;
        return version.ToString() switch
        {
            "0.9.9" => heading + "\n" +
            "• Review GPT's chest destinations.\n" +
            "• Confirm the exact sorting plan.\n" +
            "• Remember choices for next time.",
            "0.9.8" => heading + "\n" +
            "• Correct memories through chat.\n" +
            "• Smarter named-chest matching.\n" +
            "• Clear, unobstructed patch notes.",
            "0.9.7" => heading + "\n" +
            "• Context-aware character memory.\n" +
            "• Review, undo or pause memories.\n" +
            "• Remember item sorting choices.",
            _ => heading + "\nNo embedded notes for this version."
        };
    }

    public static ChatRect Badge(ChatRect panel, ChatRect memory, ChatRect sort)
    {
        int edge = memory.Width > 0 ? memory.X : sort.Width > 0 ? sort.X : panel.Right - 14;
        int left = panel.X + 65;
        return panel.Height < 80 ? default : new(left, panel.Y + 9, Math.Clamp(edge - left - 8, 0, 78), 24);
    }
}
