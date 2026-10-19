#nullable enable
using System;
using System.Collections.Generic;

namespace TerrariaGPT.Common.Knowledge;

/// <summary>A subject this character has actually encountered in this world.</summary>
public sealed record KnowledgeSubject(string Kind, string Key, string Name);

/// <summary>A detached value, never a live Terraria Item reference.</summary>
public sealed record ItemStackSnapshot(string ItemKey, string Name, int Stack, string Location)
{
    /// <summary>False for worn equipment, vanity and the mouse cursor.</summary>
    public bool CanUseForCrafting { get; init; } = true;
}

/// <summary>Remembered contents; these are not a live query of a closed chest.</summary>
public sealed record OwnedChestSnapshot(
    string Label,
    int TileX,
    int TileY,
    DateTimeOffset LastObservedUtc,
    IReadOnlyList<ItemStackSnapshot> Items)
{
    public bool IsCurrentlyOpen { get; init; }
}

public sealed record KnowledgeFact(string Id, string SubjectKey, string Text, string Source);

/// <summary>A public recipe requirement; accepted keys include supported recipe-group substitutions.</summary>
public sealed record RecipeIngredientSnapshot(string Name, int Stack, IReadOnlyList<string> AcceptedItemKeys);

/// <summary>Public recipe data from the installed game and enabled mods, never a scan of world state.</summary>
public sealed record RecipeReferenceSnapshot(
    string ResultKey,
    string ResultName,
    int ResultStack,
    IReadOnlyList<RecipeIngredientSnapshot> Ingredients,
    IReadOnlyList<string> Stations,
    IReadOnlyList<string> Conditions);

/// <summary>
/// The only game data passed to the assistant. Build this on the game thread;
/// every collection is copied and exposed as read-only before leaving that thread.
/// No world seed, map, other players, unvisited locations, or progression flags exist here.
/// </summary>
public sealed record KnowledgeSnapshot(
    string PlayerName,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<KnowledgeSubject> KnownSubjects,
    IReadOnlyList<ItemStackSnapshot> Inventory,
    IReadOnlyList<OwnedChestSnapshot> OwnedChests,
    IReadOnlyList<string> CurrentBiomes)
{
    public IReadOnlyList<KnowledgeFact> Facts { get; init; } = Array.Empty<KnowledgeFact>();
    /// <summary>Public encyclopedia subjects relevant to the question; not discoveries.</summary>
    public IReadOnlyList<KnowledgeSubject> ReferenceSubjects { get; init; } = Array.Empty<KnowledgeSubject>();
    public IReadOnlyList<RecipeReferenceSnapshot> Recipes { get; init; } = Array.Empty<RecipeReferenceSnapshot>();
    public bool RecipeLookupComplete { get; init; }
    public bool InventoryShared { get; init; } = true;
    public string GameVersion { get; init; } = "";
}
