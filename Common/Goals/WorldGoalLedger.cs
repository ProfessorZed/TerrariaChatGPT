#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ModLoader.IO;
using TerrariaGPT.Common.Knowledge;

namespace TerrariaGPT.Common.Goals;

/// <summary>Local character data partitioned by world. Never changes world or achievement saves.</summary>
internal sealed class WorldGoalLedger
{
    public List<GoalNote> Notes { get; } = new();
    public IReadOnlyList<GoalNote> NoteView { get; }
    public WorldGoalLedger() { NoteView = Notes.AsReadOnly(); }
    public Dictionary<string, ProgressSubject> TalkedNpcs { get; } = new(StringComparer.Ordinal);

    public TagCompound Save(string world) => new()
    {
        ["world"] = world,
        ["npcs"] = TalkedNpcs.Values.Select(npc => new TagCompound { ["key"] = npc.Key, ["name"] = npc.Name }).ToList(),
        ["notes"] = Notes.Take(GoalPlanner.MaximumNotes).Select(note => new TagCompound
        {
            ["id"] = note.Id, ["title"] = note.Title, ["signature"] = note.Signature,
            ["x"] = note.X, ["y"] = note.Y, ["collapsed"] = note.Collapsed,
            ["steps"] = note.MutableSteps.Select(SaveStep).ToList()
        }).ToList()
    };

    private static TagCompound SaveStep(GoalStepState step)
    {
        var tag = new TagCompound
        {
            ["kind"] = step.Kind, ["key"] = step.Key, ["label"] = step.Label, ["target"] = step.Target,
            ["manualDone"] = step.IsManual && step.IsComplete, ["acquired"] = step.Acquired,
            ["group"] = step.CraftGroup, ["ingredient"] = step.IngredientIndex, ["detail"] = step.Detail
        };
        if (step.Recipe is not null)
        {
            RecipeReferenceSnapshot recipe = step.Recipe;
            tag["recipe"] = new TagCompound
            {
                ["key"] = recipe.ResultKey, ["name"] = recipe.ResultName, ["stack"] = recipe.ResultStack,
                ["stations"] = recipe.Stations.ToList(), ["conditions"] = recipe.Conditions.ToList(),
                ["ingredients"] = recipe.Ingredients.Select(ingredient => new TagCompound
                {
                    ["name"] = ingredient.Name, ["stack"] = ingredient.Stack, ["keys"] = ingredient.AcceptedItemKeys.ToList()
                }).ToList()
            };
        }
        return tag;
    }

    public static WorldGoalLedger Load(TagCompound tag)
    {
        var ledger = new WorldGoalLedger();
        foreach (TagCompound npc in tag.GetList<TagCompound>("npcs").Take(1000))
        {
            string key = GoalPlanner.CleanText(npc.GetString("key"), 128), name = GoalPlanner.CleanText(npc.GetString("name"), 100);
            if (key.Length > 0 && name.Length > 0) ledger.TalkedNpcs[key] = new ProgressSubject(key, name);
        }
        foreach (TagCompound saved in tag.GetList<TagCompound>("notes").Take(GoalPlanner.MaximumNotes))
        {
            string title = GoalPlanner.CleanText(saved.GetString("title"), 100);
            if (title.Length == 0) continue;
            var note = new GoalNote
            {
                Id = Guid.TryParse(saved.GetString("id"), out Guid id) ? id.ToString("N") : Guid.NewGuid().ToString("N"),
                Title = title, Signature = saved.GetString("signature"), Collapsed = saved.GetBool("collapsed"),
                X = ValidPosition(saved.GetFloat("x")), Y = ValidPosition(saved.GetFloat("y"))
            };
            IList<TagCompound> steps = saved.GetList<TagCompound>("steps");
            if (steps.Count is < 1 or > GoalPlanner.MaximumSteps) continue;
            foreach (TagCompound savedStep in steps)
            {
                int target = savedStep.GetInt("target");
                string kind = savedStep.GetString("kind");
                if (target is < 1 or > GoalPlanner.MaximumTarget) continue;
                if (kind is not ("boss" or "biome" or "npc" or "achievement" or "item" or "craft" or "ingredient" or "manual")) kind = "manual";
                var step = new GoalStepState
                {
                    Kind = kind, Key = GoalPlanner.CleanText(savedStep.GetString("key"), 128),
                    Label = GoalPlanner.CleanText(savedStep.GetString("label"), 120), Target = target,
                    IsComplete = kind == "manual" && savedStep.GetBool("manualDone"), Acquired = savedStep.GetBool("acquired"),
                    CraftGroup = savedStep.GetInt("group"), IngredientIndex = savedStep.GetInt("ingredient"),
                    Detail = GoalPlanner.CleanText(savedStep.GetString("detail"), 600)
                };
                if (step.Label.Length == 0) continue;
                if (kind == "craft" && savedStep.TryGet("recipe", out TagCompound recipe)) step.Recipe = LoadRecipe(recipe);
                note.MutableSteps.Add(step);
            }
            if (note.MutableSteps.Count > 0 && !ledger.Notes.Any(value => value.Id == note.Id)) ledger.Notes.Add(note);
        }
        return ledger;
    }

    private static RecipeReferenceSnapshot? LoadRecipe(TagCompound tag)
    {
        int resultStack = tag.GetInt("stack");
        if (resultStack is < 1 or > GoalPlanner.MaximumTarget) return null;
        var ingredients = new List<RecipeIngredientSnapshot>();
        foreach (TagCompound ingredient in tag.GetList<TagCompound>("ingredients").Take(GoalPlanner.MaximumSteps))
        {
            int stack = ingredient.GetInt("stack");
            string[] keys = ingredient.GetList<string>("keys").Take(500).Select(value => GoalPlanner.CleanText(value, 128)).Where(value => value.Length > 0).ToArray();
            if (stack is < 1 or > GoalPlanner.MaximumTarget || keys.Length == 0) return null;
            ingredients.Add(new RecipeIngredientSnapshot(GoalPlanner.CleanText(ingredient.GetString("name"), 120), stack, keys));
        }
        return new RecipeReferenceSnapshot(GoalPlanner.CleanText(tag.GetString("key"), 128), GoalPlanner.CleanText(tag.GetString("name"), 120),
            resultStack, ingredients.AsReadOnly(), tag.GetList<string>("stations").Take(16).Select(value => GoalPlanner.CleanText(value, 120)).ToArray(),
            tag.GetList<string>("conditions").Take(16).Select(value => GoalPlanner.CleanText(value, 180)).ToArray());
    }

    public static float ValidPosition(float value) => float.IsFinite(value) ? value < 0 ? -1f : Math.Clamp(value, 0f, 1f) : -1f;
}
