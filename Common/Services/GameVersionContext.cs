#nullable enable
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaChatGPT.Common.Services;

/// <summary>Public installed-version metadata, detached before any background request.</summary>
public sealed record GameVersionSnapshot(string GameVersion, string TModLoaderVersion);

public static class GameVersionContext
{
    private static readonly Regex TerrariaVersion = new(@"\Av?[0-9]{1,3}(?:\.[0-9]{1,5}){1,3}\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LoaderVersion = new(@"\A[0-9]{1,4}(?:\.[0-9]{1,5}){1,3}\z",
        RegexOptions.CultureInvariant);

    /// <summary>Call on the game thread. These public versions require no player/world access.</summary>
    public static GameVersionSnapshot Capture() => FromRuntimeValues(
        // versionNumber is a display/debug label. versionNumber2 remains the
        // base Terraria version; do not use the mod loader's assembly version
        // or a const that would be embedded when this mod was compiled.
        Main.versionNumber2, BuildInfo.tMLVersion?.ToString());

    public static GameVersionSnapshot FromRuntimeValues(string? terrariaVersion, string? tModLoaderVersion)
    {
        // Validate the entire value: do not extract numeric fragments from a
        // branded display string, arbitrary text or a malformed version label.
        string game = terrariaVersion is { Length: <= 24 } && TerrariaVersion.IsMatch(terrariaVersion)
            ? terrariaVersion.TrimStart('v', 'V') : "";
        string loader = tModLoaderVersion is { Length: <= 24 } && LoaderVersion.IsMatch(tModLoaderVersion)
            ? tModLoaderVersion : "";
        return new(game, loader);
    }
}
