#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaChatGPT.Common.Knowledge;

namespace TerrariaChatGPT.Common.Goals;

/// <summary>Pure local goal evaluation. It accepts detached observations and public recipes only.</summary>
public static class GoalPlanner
{
    public const int MaximumNotes = 6;
    public const int MaximumProposalSteps = 24;
    public const int MaximumSteps = 48;
    public const int MaximumTarget = 999999;

    public static string CleanText(string? text, int maximum) => new string((text ?? "")
        .Where(character => !char.IsControl(character) && character != '[' && character != ']').Take(maximum).ToArray()).Trim();

    /// <param name="resolveItem">Exact public item key/name lookup, never world lookup.</param>
    /// <param name="recipesFor">Enabled installed recipe definitions, never recipe-availability predicates.</param>
    public static GoalNote? Create(GoalProposal proposal, ProgressSnapshot progress, KnowledgeSnapshot knowledge,
        Func<string, ProgressSubject?> resolveItem, Func<string, IReadOnlyList<RecipeReferenceSnapshot>> recipesFor)
    {
        if (proposal is null || proposal.Steps is null || proposal.Steps.Count is < 1 or > MaximumProposalSteps) return null;
        string title = CleanText(proposal.Title, 100);
        if (title.Length == 0) return null;
        var note = new GoalNote { Title = title };
        int group = 0;
        foreach (GoalStepDefinition definition in proposal.Steps)
        {
            if (definition is null) return null;
            string label = CleanText(definition.Label, 120);
            string key = CleanText(definition.Key, 128);
            string kind = (definition.Kind ?? "").Trim().ToLowerInvariant();
            if (label.Length == 0) label = key;
            if (label.Length == 0 || definition.Target is < 1 or > MaximumTarget) return null;
            var step = new GoalStepState { Kind = kind, Key = key, Label = label, Target = definition.Target };
            switch (kind)
            {
                case "boss":
                    ProgressSubject? boss = GoalCatalog.FindBoss(key);
                    if (boss is null) MakeManual(step, "No separate installed defeat flag is available; check this yourself.");
                    else { step.Key = boss.Key; step.Label = boss.Name; step.Target = 1; }
                    break;
                case "item":
                case "craft":
                    ProgressSubject? item = resolveItem(key) ?? resolveItem(label);
                    if (item is null) { MakeManual(step, "Item could not be matched exactly to installed content; check this yourself."); break; }
                    step.Key = item.Key;
                    step.Label = item.Name;
                    if (kind == "craft")
                    {
                        IReadOnlyList<RecipeReferenceSnapshot> recipes = recipesFor(item.Key);
                        var candidates = recipes.Select(recipe => new
                        {
                            Recipe = recipe,
                            Assessment = Assess(recipe, knowledge, step.Target)
                        }).Where(pair => pair.Assessment is not null && pair.Assessment.Ingredients.All(ingredient => ingredient.Required <= MaximumTarget)).ToArray();
                        // An impossible/overflowing quantity must not silently turn into one batch or a recipe-less goal.
                        if (recipes.Count > 0 && candidates.Length == 0) return null;
                        var selected = candidates.OrderBy(pair => pair.Assessment!.Ingredients.Sum(i => (long)i.MissingCount))
                            .ThenBy(pair => pair.Assessment!.Ingredients.Sum(i => (long)i.RememberedChestCount)).FirstOrDefault();
                        if (selected is not null)
                        {
                            step.Recipe = selected.Recipe;
                            step.CraftGroup = group++;
                            for (int index = 0; index < selected.Assessment!.Ingredients.Count; index++)
                            {
                                CraftingIngredientAssessment ingredient = selected.Assessment.Ingredients[index];
                                note.MutableSteps.Add(new GoalStepState { Kind = "ingredient", Key = item.Key,
                                    Label = ingredient.Name, Target = ingredient.Required, CraftGroup = step.CraftGroup, IngredientIndex = index });
                            }
                        }
                        else step.Detail = "No installed crafting recipe; tracks obtaining the result.";
                        step.Label = "Obtain " + item.Name;
                    }
                    break;
                case "biome":
                    // All vanilla biome keys are public. A not-yet-visited biome is an unfinished goal.
                    ProgressSubject? biome = VanillaBiomes.FirstOrDefault(value => Matches(value.Key, value.Name, key))
                        ?? progress.VisitedBiomes.FirstOrDefault(value => Matches(value.Key, value.Name, key));
                    if (biome is null) MakeManual(step, "This biome has no supported local visit tracker; check this yourself.");
                    else { step.Key = biome.Key; step.Label = "Visit " + biome.Name; step.Target = 1; }
                    break;
                case "npc":
                    ProgressSubject? npc = RescueNpcs.FirstOrDefault(value => Matches(value.Key, value.Name, key))
                        ?? progress.KnownNpcs.Where(value => Matches(value.Key, value.Name, key)).Select(value => new ProgressSubject(value.Key, value.Name)).FirstOrDefault();
                    if (npc is null) MakeManual(step, "This NPC has no supported rescue/talk record yet; check this yourself.");
                    else { step.Key = npc.Key; step.Label = npc.Name; step.Target = 1; }
                    break;
                case "achievement":
                    ProgressFlag? achievement = progress.Achievements.FirstOrDefault(value => Matches(value.Key, value.Name, key));
                    if (achievement is null) MakeManual(step, "Achievement is unavailable in this local profile; check this yourself.");
                    else { step.Key = achievement.Key; step.Label = achievement.Name + " (account)"; step.Target = 1; }
                    break;
                default:
                    MakeManual(step, "Check this step when you finish it.");
                    break;
            }
            note.MutableSteps.Add(step);
            if (note.MutableSteps.Count > MaximumSteps) return null;
        }
        // Identity describes accepted actions, not the model's variable prose title.
        note.Signature = string.Join("|", note.MutableSteps.Select(step => step.Kind + ":" + step.Key + ":" + step.Target
            + (step.IsManual ? ":" + GoalCatalog.Normalize(step.Label) : "")));
        Refresh(note, progress, knowledge);
        return note;
    }

    private static void MakeManual(GoalStepState step, string reason)
    {
        step.Kind = "manual";
        step.Target = 1;
        step.Detail = reason;
    }

    private static bool Matches(string key, string name, string value) =>
        GoalCatalog.Normalize(key) == GoalCatalog.Normalize(value) || GoalCatalog.Normalize(name) == GoalCatalog.Normalize(value);

    private static CraftingAssessment? Assess(RecipeReferenceSnapshot recipe, KnowledgeSnapshot knowledge, int target) =>
        CraftingPlanner.Assess("How do I make " + target + " " + recipe.ResultName + "?",
            knowledge with { Recipes = new[] { recipe }, InventoryShared = true }, recipe.ResultKey, recipe.ResultName);

    public static void Refresh(GoalNote note, ProgressSnapshot progress, KnowledgeSnapshot knowledge)
    {
        foreach (GoalStepState step in note.MutableSteps)
        {
            if (step.Kind == "ingredient") continue;
            if (step.IsManual) { step.Current = step.IsComplete ? 1 : 0; continue; }
            step.Remembered = 0;
            switch (step.Kind)
            {
                case "boss":
                    SetFlag(step, progress.Bosses.Any(flag => flag.Key == step.Key && flag.Completed));
                    step.Detail = step.Key == "evil_boss" ? "World record: either evil boss defeated; this does not prove both."
                        : step.Key == "wall_of_flesh" ? "World Hardmode record; does not identify who defeated the boss."
                        : "Current-world defeat record; does not identify which player defeated it.";
                    break;
                case "biome":
                    SetFlag(step, progress.VisitedBiomes.Any(biome => biome.Key == step.Key));
                    step.Detail = "Personally visited by this character in this world while the mod was active.";
                    break;
                case "npc":
                    NpcProgress? npc = progress.KnownNpcs.FirstOrDefault(value => value.Key == step.Key);
                    SetFlag(step, npc is not null && (npc.TalkedTo || npc.Rescued));
                    step.Detail = npc?.Rescued == true ? "Rescued according to this world's public record; current location is unknown."
                        : "Tracks an actual conversation with this character, or a public world rescue record.";
                    break;
                case "achievement":
                    SetFlag(step, progress.Achievements.Any(value => value.Key == step.Key && value.Completed));
                    step.Detail = "Local account/profile achievement; it may have been earned in a different world or character.";
                    break;
                case "item":
                case "craft":
                    int carried = SafeSum(progress.Inventory.Where(value => value.ItemKey == step.Key).Select(value => value.Stack));
                    int open = SafeSum(progress.RememberedItems.Where(value => value.ItemKey == step.Key && value.IsCurrentlyOpen).Select(value => value.Stack));
                    step.Current = Math.Min(step.Target, SafeSum(new[] { carried, open }));
                    step.Remembered = SafeSum(progress.RememberedItems.Where(value => value.ItemKey == step.Key && !value.IsCurrentlyOpen).Select(value => value.Stack));
                    // Acquiring the target is a milestone. Its completion survives using/equipping the item.
                    step.Acquired |= step.Current >= step.Target;
                    step.IsComplete = step.Acquired;
                    step.Detail = step.IsComplete ? "Target was observed in your inventory, equipment, cursor or an open remembered chest."
                        : "Current supplies are live; remembered closed-chest supplies may have changed.";
                    if (step.Recipe is not null) RefreshCraft(note, step, knowledge);
                    else if (step.Kind == "craft") step.Detail += " No current installed recipe is attached; this step tracks obtaining the result.";
                    break;
            }
        }
    }

    private static void RefreshCraft(GoalNote note, GoalStepState craft, KnowledgeSnapshot knowledge)
    {
        CraftingAssessment? assessment = Assess(craft.Recipe!, knowledge, craft.Target);
        string stations = craft.Recipe!.Stations.Count == 0 ? "By hand" : string.Join(" or ", craft.Recipe.Stations);
        string conditions = craft.Recipe.Conditions.Count == 0 ? "" : "; " + string.Join("; ", craft.Recipe.Conditions);
        craft.Detail += " Recipe: " + stations + conditions + ". This does not check for a nearby station.";
        foreach (GoalStepState ingredient in note.MutableSteps.Where(step => step.Kind == "ingredient" && step.CraftGroup == craft.CraftGroup))
        {
            if (craft.IsComplete)
            {
                ingredient.Current = ingredient.Target;
                ingredient.Remembered = 0;
                ingredient.IsComplete = true;
                ingredient.Detail = "Result obtained; these ingredients are no longer required for this goal.";
            }
            else if (assessment is not null && ingredient.IngredientIndex >= 0 && ingredient.IngredientIndex < assessment.Ingredients.Count)
            {
                CraftingIngredientAssessment counts = assessment.Ingredients[ingredient.IngredientIndex];
                ingredient.Target = counts.Required;
                ingredient.Current = counts.InventoryCount + counts.OpenChestCount;
                ingredient.Remembered = counts.RememberedChestCount;
                ingredient.IsComplete = ingredient.Current >= ingredient.Target;
                int missing = Math.Max(0, counts.Required - ingredient.Current - ingredient.Remembered);
                ingredient.Detail = ingredient.Remembered > 0
                    ? $"{ingredient.Remembered} remembered in closed chests; {missing} still missing after remembered supplies. Retrieve and confirm remembered items."
                    : $"{Math.Max(0, counts.Required - ingredient.Current)} still missing. Worn equipment and cursor items are not counted as craft-ready ingredients.";
            }
            else
            {
                ingredient.Current = ingredient.Remembered = 0;
                ingredient.IsComplete = false;
                ingredient.Detail = "This saved recipe could not be evaluated; recreate the goal after content changes.";
            }
        }
    }

    private static void SetFlag(GoalStepState step, bool value) { step.Current = value ? 1 : 0; step.IsComplete = value; }
    private static int SafeSum(IEnumerable<int> values) => (int)Math.Min(MaximumTarget, values.Where(value => value > 0).Sum(value => (long)value));

    public static readonly IReadOnlyList<ProgressSubject> VanillaBiomes = Array.AsReadOnly(new[]
    {
        "Forest", "Desert", "Underground Desert", "Jungle", "Snow", "Ocean", "Corruption", "Crimson", "Hallow", "Dungeon",
        "Glowing Mushroom", "Meteorite", "Graveyard", "Shimmer", "Space", "Underground", "Cavern", "Underworld"
    }.Select(name => new ProgressSubject("Terraria/" + name.Replace(" ", ""), name)).ToArray());

    public static readonly IReadOnlyList<ProgressSubject> RescueNpcs = Array.AsReadOnly(new[]
    {
        new ProgressSubject("Terraria/GoblinTinkerer", "Goblin Tinkerer"), new ProgressSubject("Terraria/Wizard", "Wizard"),
        new ProgressSubject("Terraria/Mechanic", "Mechanic"), new ProgressSubject("Terraria/Stylist", "Stylist"),
        new ProgressSubject("Terraria/Angler", "Angler"), new ProgressSubject("Terraria/TaxCollector", "Tax Collector"),
        new ProgressSubject("Terraria/Tavernkeep", "Tavernkeep"), new ProgressSubject("Terraria/Golfer", "Golfer")
    });
}
