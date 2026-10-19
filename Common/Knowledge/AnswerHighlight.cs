#nullable enable
namespace TerrariaChatGPT.Common.Knowledge;

/// <summary>A literal phrase and an allowed semantic category, never a color or chat tag.</summary>
public sealed record AnswerHighlight(string Text, string Kind)
{
    /// <summary>Optional UTF-16 position for one locally generated occurrence.</summary>
    public int? Start { get; init; }
}
