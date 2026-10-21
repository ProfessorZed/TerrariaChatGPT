#nullable enable
namespace TerrariaChatGPT.Common.Native;

/// <summary>Friendly labels for protocol effort values; support still comes from each model's catalog entry.</summary>
public static class ChatGptEffortOptions
{
    public static bool IsKnown(string? value) => value is "none" or "minimal" or "low" or "medium" or "high" or "xhigh" or "max";

    // Longer reasoning is bounded but need not finish on the ordinary-chat timer.
    public static int InferenceTimeoutSeconds(string? effort, bool webSearch) => effort switch
    {
        "high" => 180,
        "xhigh" or "max" => 300,
        _ => webSearch ? 120 : 75
    };
    public static int RuntimeTimeoutSeconds(string? effort) => effort switch
    {
        "high" => 210,
        "xhigh" or "max" => 330,
        _ => 150
    };

    public static string Label(string? value) => value switch
    {
        "" or null => "Auto",
        "none" => "Instant",
        "minimal" => "Minimal",
        "low" => "Fast",
        "medium" => "Balanced",
        "high" => "Thorough",
        "xhigh" => "Extra thorough",
        "max" => "Maximum",
        _ => "Unavailable"
    };
}
