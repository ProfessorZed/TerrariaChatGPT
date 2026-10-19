#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaChatGPT.Common.Knowledge;

namespace TerrariaChatGPT.Common.Goals;

/// <summary>An untrusted suggestion. Acceptance validates it against local public definitions.</summary>
public sealed record GoalStepDefinition(string Kind, string Key, string Label, int Target = 1);
public sealed record GoalProposal(string Title, IReadOnlyList<GoalStepDefinition> Steps);

public sealed record ProgressItem(string ItemKey, string Name, int Stack, string Location, bool CanUseForCrafting);
public sealed record RememberedProgressItem(string ItemKey, string Name, int Stack, string ChestLabel,
    DateTimeOffset LastObservedUtc, bool IsCurrentlyOpen);
public sealed record ProgressFlag(string Key, string Name, bool Completed, string Scope);
public sealed record ProgressSubject(string Key, string Name);
public sealed record NpcProgress(string Key, string Name, bool TalkedTo, bool Rescued);

/// <summary>Detached observations only. No world identity, coordinates, seed or entity references.</summary>
public sealed record ProgressSnapshot(DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ProgressItem> Inventory,
    IReadOnlyList<RememberedProgressItem> RememberedItems,
    IReadOnlyList<ProgressFlag> Bosses,
    IReadOnlyList<ProgressFlag> WorldProgress,
    IReadOnlyList<ProgressFlag> Achievements,
    IReadOnlyList<ProgressSubject> VisitedBiomes,
    IReadOnlyList<NpcProgress> KnownNpcs,
    IReadOnlyList<string> CurrentBiomes);

public sealed class GoalStepState
{
    public string Kind { get; internal set; } = "manual";
    public string Key { get; internal set; } = "";
    public string Label { get; internal set; } = "";
    public int Target { get; internal set; } = 1;
    public int Current { get; internal set; }
    public int Remembered { get; internal set; }
    public bool IsComplete { get; internal set; }
    public string Detail { get; internal set; } = "";
    public bool IsManual => Kind == "manual";
    internal bool Acquired { get; set; }
    internal int CraftGroup { get; set; } = -1;
    internal int IngredientIndex { get; set; } = -1;
    internal RecipeReferenceSnapshot? Recipe { get; set; }
}

public sealed class GoalNote
{
    private readonly IReadOnlyList<GoalStepState> _steps;
    public GoalNote() { _steps = MutableSteps.AsReadOnly(); }
    public string Id { get; internal set; } = Guid.NewGuid().ToString("N");
    public string Title { get; internal set; } = "Goal";
    internal List<GoalStepState> MutableSteps { get; } = new();
    public IReadOnlyList<GoalStepState> Steps => _steps;
    public float X { get; internal set; } = -1f;
    public float Y { get; internal set; } = -1f;
    public bool Collapsed { get; internal set; }
    public bool IsComplete => MutableSteps.Count > 0 && MutableSteps.All(step => step.IsComplete);
    internal string Signature { get; set; } = "";
}

/// <summary>Stable public keys. Shared defeat flags deliberately remain shared.</summary>
public static class GoalCatalog
{
    public static readonly IReadOnlyList<ProgressSubject> Bosses = Array.AsReadOnly(new[]
    {
        new ProgressSubject("king_slime", "King Slime"),
        new ProgressSubject("eye_of_cthulhu", "Eye of Cthulhu"),
        new ProgressSubject("evil_boss", "Eater of Worlds or Brain of Cthulhu"),
        new ProgressSubject("queen_bee", "Queen Bee"),
        new ProgressSubject("skeletron", "Skeletron"),
        new ProgressSubject("deerclops", "Deerclops"),
        new ProgressSubject("wall_of_flesh", "Wall of Flesh / entered Hardmode"),
        new ProgressSubject("queen_slime", "Queen Slime"),
        new ProgressSubject("the_twins", "The Twins"),
        new ProgressSubject("the_destroyer", "The Destroyer"),
        new ProgressSubject("skeletron_prime", "Skeletron Prime"),
        new ProgressSubject("plantera", "Plantera"),
        new ProgressSubject("golem", "Golem"),
        new ProgressSubject("duke_fishron", "Duke Fishron"),
        new ProgressSubject("empress_of_light", "Empress of Light"),
        new ProgressSubject("lunatic_cultist", "Lunatic Cultist"),
        new ProgressSubject("moon_lord", "Moon Lord")
    });

    public static string Normalize(string? text) => new string((text ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static ProgressSubject? FindBoss(string key) => Bosses.FirstOrDefault(boss =>
        Normalize(boss.Key) == Normalize(key) || Normalize(boss.Name) == Normalize(key)
        || boss.Key == "wall_of_flesh" && Normalize(key) == "wallofflesh");

    public static GoalProposal PreHardmodeBosses() => new("Defeat the pre-Hardmode bosses",
        Bosses.Take(7).Select(boss => new GoalStepDefinition("boss", boss.Key, boss.Name)).ToArray());
}
