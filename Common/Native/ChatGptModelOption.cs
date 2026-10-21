#nullable enable
using System;
using System.Collections.Generic;

namespace TerrariaChatGPT.Common.Native;

/// <summary>Public catalog metadata only. Never contains account or credential data.</summary>
public sealed record ChatGptModelOption(string Slug, string DisplayName)
{
    public IReadOnlyList<string> SupportedReasoningEfforts { get; init; } = Array.Empty<string>();
    public string DefaultReasoningEffort { get; init; } = "";
}
