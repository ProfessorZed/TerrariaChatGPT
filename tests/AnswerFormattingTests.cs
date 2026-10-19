#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaGPT.Common.Knowledge;
using TerrariaGPT.Common.UI;

namespace TerrariaGPT.Tests;

/// <summary>Exercises real formatting and arithmetic helpers with detached fabricated data.</summary>
public static class AnswerFormattingTests
{
    public static string[] Run()
    {
        var results = new List<string>();
        Check("Readable answers remove bold markers, citations and source footer links", () =>
        {
            string cleaned = AnswerFormatting.Clean("Find **Starfury** in [Floating Islands](https://terraria.wiki.gg/wiki/Floating_Island). [1, 2]\n\nWiki: https://terraria.wiki.gg/wiki/Starfury\n\nEquip it.");
            Equal("Find Starfury in Floating Islands.\n\nEquip it.", cleaned);
            Require(!cleaned.Contains("https://", StringComparison.Ordinal) && !cleaned.Contains("[1", StringComparison.Ordinal), "A source URL or citation survived.");
        }, results);
        Check("Code fences and control bytes are removed while answer line breaks survive", () =>
        {
            Equal("Make it at an Anvil.\nThen equip it.", AnswerFormatting.Clean("```\nMake it at an Anvil.\u0000\u0007\u001b\nThen equip it.\n```"));
        }, results);
        Check("Inline code and italic markers do not remain around the useful answer", () =>
        {
            Equal("Use a Lucky Horseshoe from Floating Islands.", AnswerFormatting.Clean("Use a `Lucky Horseshoe` from *Floating Islands*."));
        }, results);
        Check("Terraria chat-tag strings remain literal text and cannot choose a highlight color", () =>
        {
            const string literal = "[c/FF0000:Floating Islands] [i:1]";
            Equal(literal, AnswerFormatting.Clean(literal));
            Equal(0, AnswerFormatting.Validate(literal, new[] { new AnswerHighlight(literal, "c/FF0000") }).Count);
        }, results);
        Check("Long answer display is bounded", () =>
        {
            string cleaned = AnswerFormatting.Clean(new string('x', 15_000));
            Require(cleaned.Length <= 12_001 && cleaned.EndsWith("…", StringComparison.Ordinal), "The display answer was not truncated.");
        }, results);
        Check("Only exact displayed phrases and fixed semantic highlight kinds are accepted", () =>
        {
            const string text = "Starfury: Floating Islands. Still need: 3 Jungle Spores. You have: 10 Jungle Spores.";
            var candidates = new[]
            {
                new AnswerHighlight("Floating Islands", "location"), new AnswerHighlight("Starfury", "item"),
                new AnswerHighlight("3 Jungle Spores", "missing"), new AnswerHighlight("10 Jungle Spores", "owned"),
                new AnswerHighlight("Undiscovered chest", "location"), new AnswerHighlight("starfury", "item"),
                new AnswerHighlight("Starfury", "red"), new AnswerHighlight("Starfury", "Item")
            };
            var valid = AnswerFormatting.Validate(text, candidates);
            Equal(4, valid.Count);
            Require(valid.All(mark => text.Contains(mark.Text, StringComparison.Ordinal)), "A nonexact phrase was accepted.");
        }, results);
        Check("Null entries and nullable metadata fields are ignored without breaking an answer", () =>
        {
            var candidates = new AnswerHighlight[] { null!, new(null!, "item"), new("Wood", null!), new("Wood", "item") };
            Equal(1, AnswerFormatting.Validate("Wood", candidates).Count);
            Equal(0, AnswerFormatting.Validate("Wood", null).Count);
        }, results);
        Check("Empty, whitespace, excessive and control-bearing highlight phrases are rejected", () =>
        {
            string maximum = new('a', 120);
            string excessive = new('b', 121);
            string text = maximum + " " + excessive + " \nWood";
            var candidates = new[] { new AnswerHighlight("", "item"), new AnswerHighlight(" ", "item"),
                new AnswerHighlight(excessive, "item"), new AnswerHighlight("\nWood", "item"), new AnswerHighlight(maximum, "item") };
            var valid = AnswerFormatting.Validate(text, candidates);
            Equal(1, valid.Count); Equal(maximum, valid[0].Text);
        }, results);
        Check("Duplicate metadata is removed and the accepted highlight count is bounded", () =>
        {
            var candidates = Enumerable.Range(1, 40).Select(index => new AnswerHighlight("Item " + index, "item")).ToArray();
            string text = string.Join("; ", candidates.Select(mark => mark.Text));
            Equal(24, AnswerFormatting.Validate(text, candidates).Count);
            Equal(1, AnswerFormatting.Validate("Wood Wood", new[] { new AnswerHighlight("Wood", "item"), new AnswerHighlight("Wood", "item") }).Count);
        }, results);
        Check("Identical phrases with conflicting meanings remain uncolored regardless of order", () =>
        {
            const string text = "Still need 2 Wood. You have 2 Wood. Starfury.";
            foreach (bool reverse in new[] { false, true })
            {
                var conflict = new[] { new AnswerHighlight("2 Wood", "owned"), new AnswerHighlight("2 Wood", "missing") };
                if (reverse) Array.Reverse(conflict);
                var proposed = conflict.Concat(new[] { new AnswerHighlight("Starfury", "item"), new AnswerHighlight("2 Wood", "owned") });
                var valid = AnswerFormatting.Validate(text, proposed);
                Equal(1, valid.Count); Equal("Starfury", valid[0].Text);
            }
            Equal(1, AnswerFormatting.Validate("Wood", new[] { new AnswerHighlight("Wood", "unknown"), new AnswerHighlight("Wood", "item") }).Count);
        }, results);
        Check("Late conflicts are removed before applying the output highlight limit", () =>
        {
            var proposed = Enumerable.Range(1, 25).Select(index => new AnswerHighlight("Phrase " + index, "item")).ToList();
            proposed.Add(new AnswerHighlight("Phrase 1", "location"));
            string text = string.Join("; ", proposed.Select(mark => mark.Text));
            var valid = AnswerFormatting.Validate(text, proposed);
            Equal(24, valid.Count);
            Require(valid.All(mark => mark.Text != "Phrase 1") && valid.Any(mark => mark.Text == "Phrase 25"), "Truncation hid a conflicting meaning or lost its valid replacement.");
        }, results);
        Check("Case-distinct exact phrases are validated independently", () =>
        {
            var valid = AnswerFormatting.Validate("Wood wood", new[] { new AnswerHighlight("Wood", "owned"), new AnswerHighlight("wood", "missing") });
            Equal(2, valid.Count);
        }, results);
        Check("Cleaning stale citation suffixes does not cause highlights to point outside displayed text", () =>
        {
            string cleaned = AnswerFormatting.Clean("**Floating Islands** [1].");
            var valid = AnswerFormatting.Validate(cleaned, new[] { new AnswerHighlight("Floating Islands", "location"),
                new AnswerHighlight("Floating Islands [1]", "location"), new AnswerHighlight("**Floating Islands**", "location") });
            Equal(1, valid.Count); Equal("Floating Islands", valid[0].Text);
        }, results);
        Check("Crafting highlights separate owned and missing sections even for identical counts", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot(new[] { Stack("Wood", 2) });
            CraftingAssessment plan = CraftingPlanner.Assess("What am I missing for Wooden Sword?", snapshot)
                ?? throw new InvalidOperationException("The installed recipe was not assessed.");
            string text = CraftingPlanner.Format(plan);
            var highlights = AnswerFormatting.Validate(text, AnswerFormatting.ForCrafting(plan));
            Require(highlights.Any(mark => mark.Kind == "missing" && mark.Text == "2 Wood" && mark.Start == text.IndexOf("2 Wood", StringComparison.Ordinal)), "Missing materials were not highlighted at their occurrence.");
            Require(highlights.Any(mark => mark.Kind == "owned" && mark.Text == "2 Wood" && mark.Start == text.IndexOf("2 Wood", text.IndexOf("You have:", StringComparison.Ordinal), StringComparison.Ordinal)), "Carried materials were not highlighted at their occurrence.");
            Require(!highlights.Where(mark => mark.Kind == "owned").Select(mark => mark.Start).Intersect(highlights.Where(mark => mark.Kind == "missing").Select(mark => mark.Start)).Any(),
                "One occurrence was painted as both owned and missing.");
            Require(highlights.Any(mark => mark.Kind == "item" && mark.Text == "4 Wood" && mark.Start > text.IndexOf("Recipe:", StringComparison.Ordinal)), "The full recipe requirement was not highlighted.");
            Require(highlights.Any(mark => mark.Text == "Work Bench" && mark.Kind == "location"), "The station was not highlighted.");
        }, results);
        Check("Long modded material names are fully highlighted without truncating sections", () =>
        {
            string material = "Exceptionally Long Named " + new string('x', 110);
            var plan = new CraftingAssessment("Test/Sword", "Test Sword", 1,
                new[] { new CraftingIngredientAssessment(material, 4, 2, 0, 0, 2, Array.Empty<CraftingIngredientSource>()) },
                Array.Empty<string>(), Array.Empty<string>(), 0);
            string text = CraftingPlanner.Format(plan);
            var sections = AnswerFormatting.Validate(text, AnswerFormatting.ForCrafting(plan))
                .Where(mark => mark.Kind is "missing" or "owned" && mark.Text.Contains(material, StringComparison.Ordinal)).ToArray();
            Equal(2, sections.Length);
            foreach (var mark in sections)
            {
                Require(mark.Start.HasValue && mark.Text.Length > 120 && mark.Text.Length <= 1024 && !char.IsWhiteSpace(mark.Text[^1]), "A long material lost its occurrence-scoped highlight.");
                int after = mark.Start!.Value + mark.Text.Length;
                Require(after == text.Length || !char.IsLetterOrDigit(text[after]), "A section was cut inside a material name.");
            }
        }, results);
        Check("Remembered closed-chest supplies are not highlighted as carried inventory", () =>
        {
            var chest = new OwnedChestSnapshot("Fabricated supplies", 20, 30, DateTimeOffset.UnixEpoch, new[] { Stack("Wood", 2) });
            var snapshot = Snapshot(Array.Empty<ItemStackSnapshot>()) with { OwnedChests = new[] { chest } };
            CraftingAssessment plan = CraftingPlanner.Assess("What am I missing for Wooden Sword?", snapshot)!;
            var highlights = AnswerFormatting.ForCrafting(plan);
            Require(plan.UsesRememberedChests && highlights.All(mark => mark.Kind != "owned"), "Stale supplies were labeled as carried inventory.");
            Require(CraftingPlanner.Format(plan).Contains("last seen", StringComparison.Ordinal), "Chest freshness disappeared.");
        }, results);
        Check("Inventory highlights use the actual observed total rather than individual stack counts", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot(new[] { Stack("Wood", 5), Stack("Wood", 2) });
            string text = LocalKnowledgeAnswer.TryAnswer("Do I have Wood?", snapshot, "Terraria/Wood", "Wood")!;
            var highlights = AnswerFormatting.Validate(text, AnswerFormatting.ForInventory(text, snapshot));
            Require(highlights.Any(mark => mark.Kind == "owned" && mark.Text == "7 Wood"), "The carried total was not highlighted.");
            Require(highlights.All(mark => mark.Text != "5 Wood" && mark.Text != "2 Wood"), "A single stack replaced the owned total.");
        }, results);
        Check("Inventory counts include punctuation-ending and localized mod item names", () =>
        {
            foreach (string name in new[] { "Wood (Test)", "Épée (Légendaire)", "剣（試作）", "C++" })
            {
                KnowledgeSnapshot snapshot = Snapshot(new[] { Stack(name, 7) });
                string text = "You have 7 " + name + " in your inventory.";
                var highlights = AnswerFormatting.Validate(text, AnswerFormatting.ForInventory(text, snapshot));
                Require(highlights.Any(mark => mark.Kind == "owned" && mark.Text == "7 " + name), "A punctuation-ending item lost its owned count: " + name);
            }
        }, results);
        Check("Punctuation-ending item matching does not include a longer word suffix", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot(new[] { Stack("Wood (Test)", 7) });
            var highlights = AnswerFormatting.ForInventory("You have 7 Wood (Test)Extra.", snapshot);
            Require(highlights.All(mark => mark.Kind != "owned"), "A partial name inherited the longer item's count.");
        }, results);
        Check("Every ingredient in a large full recipe receives deterministic color beyond the old metadata cap", () =>
        {
            var ingredients = Enumerable.Range(0, 64).Select(index => new CraftingIngredientAssessment("Material " + index, 4, 2, 0, 0, 2, Array.Empty<CraftingIngredientSource>())).ToArray();
            var plan = new CraftingAssessment("Test/Large", "Large Recipe", 1, ingredients, new[] { "Test Station" }, Array.Empty<string>(), 0);
            string text = CraftingPlanner.Format(plan);
            var highlights = AnswerFormatting.Validate(text, AnswerFormatting.ForCrafting(plan));
            Require(highlights.Count > 150, "Local colors were truncated by the old 24-entry API metadata limit.");
            foreach (var item in ingredients)
            {
                Require(highlights.Any(mark => mark.Text == "4 " + item.Name && mark.Kind == "item" && mark.Start > text.IndexOf("Recipe:", StringComparison.Ordinal)), "A full-recipe material was uncolored: " + item.Name);
                Require(highlights.Any(mark => mark.Text == "2 " + item.Name && mark.Kind == "missing" && mark.Start < text.IndexOf("You have:", StringComparison.Ordinal)), "A late missing material was uncolored: " + item.Name);
                Require(highlights.Any(mark => mark.Text == "2 " + item.Name && mark.Kind == "owned" && mark.Start > text.IndexOf("You have:", StringComparison.Ordinal) && mark.Start < text.IndexOf("Recipe:", StringComparison.Ordinal)), "A late carried material was uncolored: " + item.Name);
            }
            Require(highlights.Any(mark => mark.Text == "Test Station" && mark.Kind == "location"), "The station lost its gold highlight.");
        }, results);
        Check("Inventory list item-x-count syntax highlights every actual carried entry", () =>
        {
            var snapshot = Snapshot(Enumerable.Range(0, 30).Select(index => Stack("Material " + index, index + 1)).ToArray());
            string text = LocalKnowledgeAnswer.TryAnswer("What is in my inventory?", snapshot)!;
            var highlights = AnswerFormatting.ForInventory(text, snapshot);
            Equal(30, highlights.Count(mark => mark.Kind == "owned"));
            Require(highlights.Any(mark => mark.Text == "Material 29 x30" && mark.Kind == "owned"), "The end of a long inventory list remained uncolored.");
        }, results);
        Check("Equal carried and remembered counts are colored only at their own occurrences", () =>
        {
            var snapshot = Snapshot(new[] { Stack("Wood", 2) }) with { OwnedChests = new[] { new OwnedChestSnapshot("Supplies", 1, 2, DateTimeOffset.UnixEpoch, new[] { Stack("Wood", 2) }) } };
            string text = LocalKnowledgeAnswer.TryAnswer("Do I have Wood?", snapshot, "Terraria/Wood", "Wood")!;
            var highlights = AnswerFormatting.ForInventory(text, snapshot);
            var carried = highlights.Where(mark => mark.Text == "2 Wood" && mark.Kind == "owned").ToArray();
            Equal(1, carried.Length);
            Require(carried[0].Start < text.IndexOf('\n'), "A remembered count was colored as carried inventory.");
            Require(highlights.Any(mark => mark.Text == "2 Wood" && mark.Kind == "item" && mark.Start > text.IndexOf('\n')), "The remembered count lost its neutral item color.");
            Require(highlights.Any(mark => mark.Text == "Supplies" && mark.Kind == "location"), "The known chest label lost its location color.");
        }, results);
        Check("Invalid occurrence offsets are ignored rather than recoloring another match", () =>
        {
            const string text = "Wood Wood";
            var candidates = new[] { new AnswerHighlight("Wood", "owned") { Start = -1 }, new AnswerHighlight("Wood", "owned") { Start = 1 },
                new AnswerHighlight("Wood", "owned") { Start = int.MaxValue }, new AnswerHighlight("Wood", "item") { Start = 5 } };
            var valid = AnswerFormatting.Validate(text, candidates);
            Equal(1, valid.Count); Equal(5, valid[0].Start);
        }, results);
        Check("Public response fallback colors recipe names and stations without model metadata", () =>
        {
            var snapshot = Snapshot(Array.Empty<ItemStackSnapshot>());
            const string text = "Wooden Sword needs 4 Wood at a Work Bench.";
            var highlights = AnswerFormatting.ForResponse(text, snapshot, null);
            Require(highlights.Any(mark => mark.Text == "Wooden Sword" && mark.Kind == "item"), "The known result was uncolored.");
            Require(highlights.Any(mark => mark.Text == "Wood" && mark.Kind == "item" && mark.Start == text.IndexOf("Wood at", StringComparison.Ordinal)), "The known ingredient was uncolored.");
            Require(highlights.Any(mark => mark.Text == "Work Bench" && mark.Kind == "location"), "The known station was uncolored.");
            Require(highlights.All(mark => mark.Kind is "item" or "location"), "Fallback inferred unsupported owned or missing quantities.");
        }, results);
        Check("Occurrence validation keeps distinct meanings and rejects a contradictory same occurrence", () =>
        {
            const string text = "2 Wood. 2 Wood.";
            var valid = AnswerFormatting.Validate(text, new[] { new AnswerHighlight("2 Wood", "missing") { Start = 0 }, new AnswerHighlight("2 Wood", "owned") { Start = 8 } });
            Equal(2, valid.Count);
            var conflict = AnswerFormatting.Validate(text, new[] { new AnswerHighlight("2 Wood", "missing") { Start = 0 }, new AnswerHighlight("2 Wood", "owned") { Start = 0 } });
            Equal(0, conflict.Count);
        }, results);
        Check("Grouped chest stacks and extra-source summaries keep their actual printed quantities colored", () =>
        {
            var sources = new[] { new CraftingIngredientSource("Wood", 2, "Supplies", true, DateTimeOffset.UnixEpoch),
                new CraftingIngredientSource("Wood", 3, "Supplies", true, DateTimeOffset.UnixEpoch) }
                .Concat(Enumerable.Range(0, 5).Select(index => new CraftingIngredientSource("Wood", 1, "Chest " + index, true, DateTimeOffset.UnixEpoch))).ToArray();
            var plan = new CraftingAssessment("Test/Sword", "Test Sword", 1,
                new[] { new CraftingIngredientAssessment("Wood", 12, 0, 0, 10, 2, sources) }, Array.Empty<string>(), Array.Empty<string>(), 0);
            string text = CraftingPlanner.Format(plan);
            var highlights = AnswerFormatting.ForCrafting(plan);
            Require(text.Contains("Supplies: 5 Wood", StringComparison.Ordinal), "The planner fixture did not aggregate equal chest stacks.");
            Require(highlights.Any(mark => mark.Text == "5 Wood" && mark.Kind == "item" && mark.Start > text.IndexOf("In chests:", StringComparison.Ordinal)), "The aggregated chest quantity was uncolored.");
            Require(highlights.Any(mark => mark.Text == "2 more Wood" && mark.Kind == "item"), "The remaining-source summary was uncolored.");
            Require(highlights.All(mark => mark.Kind != "owned"), "Remembered chest amounts were colored as carried inventory.");
        }, results);
        Check("Response fallback recognizes lowercase prose while preserving exact occurrence meanings", () =>
        {
            const string text = "Wood wood at a work bench.";
            var highlights = AnswerFormatting.ForResponse(text, Snapshot(Array.Empty<ItemStackSnapshot>()),
                new[] { new AnswerHighlight("Wood", "owned"), new AnswerHighlight("wood", "missing") });
            Require(highlights.Any(mark => mark.Text == "wood" && mark.Kind == "item" && mark.Start == 5), "Lowercase public item names were left uncolored.");
            Require(highlights.Any(mark => mark.Text == "work bench" && mark.Kind == "location" && mark.Start == 15), "Lowercase station prose was left uncolored.");
            Require(highlights.Any(mark => mark.Text == "Wood" && mark.Kind == "owned" && mark.Start is null) &&
                highlights.Any(mark => mark.Text == "wood" && mark.Kind == "missing" && mark.Start is null), "Exact metadata meanings were lost when supplementing lowercase names.");
        }, results);
        Check("Repeated remote numeric metadata is scoped to owned and missing rows without recoloring the recipe", () =>
        {
            const string text = "You have: 1 Feather.\nStill need: 1 Feather.\nMaterials: 1 Feather.";
            var snapshot = Snapshot(new[] { Stack("Feather", 1) });
            var highlights = AnswerFormatting.ForResponse(text, snapshot,
                new[] { new AnswerHighlight("1 Feather", "owned"), new AnswerHighlight("1 Feather", "missing") });
            var spans = ChatTextLayout.Highlight(text, highlights);
            Equal(AnswerTextKind.Owned, KindAt(spans, text.IndexOf("1 Feather", StringComparison.Ordinal)));
            Equal(AnswerTextKind.Missing, KindAt(spans, text.IndexOf("1 Feather", text.IndexOf("Still need:", StringComparison.Ordinal), StringComparison.Ordinal)));
            Equal(AnswerTextKind.Item, KindAt(spans, text.LastIndexOf("Feather", StringComparison.Ordinal)));
            Require(highlights.Where(mark => mark.Kind is "owned" or "missing").All(mark => mark.Start.HasValue), "A repeated quantity retained global semantic color.");
        }, results);
        Check("Repeated remote counts follow labeled bullet lists and inline sections", () =>
        {
            foreach (string text in new[] { "Inventory:\n- 1 Feather\nStill need:\n- 1 Feather\nRecipe:\n- 1 Feather",
                "Open chest: 1 Feather; Missing: 1 Feather; Full recipe: 1 Feather." })
            {
                var highlights = AnswerFormatting.ForResponse(text, Snapshot(new[] { Stack("Feather", 1) }),
                    new[] { new AnswerHighlight("1 Feather", "owned"), new AnswerHighlight("1 Feather", "missing") });
                var spans = ChatTextLayout.Highlight(text, highlights);
                Equal(1, spans.Count(span => span.Kind == AnswerTextKind.Owned));
                Equal(1, spans.Count(span => span.Kind == AnswerTextKind.Missing));
                Equal(AnswerTextKind.Item, KindAt(spans, text.LastIndexOf("Feather", StringComparison.Ordinal)));
            }
        }, results);
        Check("Ambiguous duplicate remote counts keep item color without inventing section meaning", () =>
        {
            const string text = "One route uses 1 Feather. Another route also uses 1 Feather.";
            var highlights = AnswerFormatting.ForResponse(text, Snapshot(new[] { Stack("Feather", 1) }), new[] { new AnswerHighlight("1 Feather", "missing") });
            Require(highlights.All(mark => mark.Kind != "missing"), "Unlabeled repeated counts were painted as missing.");
            var spans = ChatTextLayout.Highlight(text, highlights);
            Equal(2, spans.Count(span => span.Text == "Feather" && span.Kind == AnswerTextKind.Item));
        }, results);
        Check("Unique remote quantity metadata remains available and section color cannot leak into free prose", () =>
        {
            const string unique = "Still need: 1 Feather.";
            var valid = AnswerFormatting.ForResponse(unique, Snapshot(new[] { Stack("Feather", 1) }), new[] { new AnswerHighlight("1 Feather", "missing") });
            Require(valid.Any(mark => mark.Text == "1 Feather" && mark.Kind == "missing" && mark.Start is null), "A unique supplied quantity was removed.");
            foreach (string text in new[] { "You have: 1 Feather. A recipe uses 1 Feather.", "You have: 1 Feather.\n\nA recipe uses 1 Feather.",
                "You have: 1 Feather, but the recipe requires 1 Feather.", "You have:\n- 1 Feather\n- A recipe uses 1 Feather." })
            {
                var scoped = AnswerFormatting.ForResponse(text, Snapshot(new[] { Stack("Feather", 1) }), new[] { new AnswerHighlight("1 Feather", "owned") });
                Equal(1, scoped.Count(mark => mark.Kind == "owned"));
                Require(scoped.Single(mark => mark.Kind == "owned").Start == text.IndexOf("1 Feather", StringComparison.Ordinal), "Owned color leaked into another sentence or paragraph.");
            }
        }, results);
        Check("Whitespace around repeated remote quantity metadata cannot bypass occurrence scoping", () =>
        {
            const string text = "Still need: 1 Feather in inventory.\nRecipe: 1 Feather with other materials.";
            var highlights = AnswerFormatting.ForResponse(text, Snapshot(new[] { Stack("Feather", 1) }), new[] { new AnswerHighlight(" 1 Feather ", "missing") });
            Equal(1, highlights.Count(mark => mark.Kind == "missing"));
            Require(highlights.Single(mark => mark.Kind == "missing").Start == text.IndexOf("1 Feather", StringComparison.Ordinal), "Whitespace preserved a global duplicate highlight.");
        }, results);
        return results.ToArray();
    }

    private static KnowledgeSnapshot Snapshot(IReadOnlyList<ItemStackSnapshot> inventory) => new("Fabricated character", DateTimeOffset.UnixEpoch,
        Array.Empty<KnowledgeSubject>(), inventory, Array.Empty<OwnedChestSnapshot>(), Array.Empty<string>())
    {
        Recipes = new[] { new RecipeReferenceSnapshot("Terraria/WoodenSword", "Wooden Sword", 1,
            new[] { new RecipeIngredientSnapshot("Wood", 4, new[] { "Terraria/Wood" }) }, new[] { "Work Bench" }, Array.Empty<string>()) }
    };

    private static ItemStackSnapshot Stack(string name, int count) => new("Terraria/" + name, name, count, "Fabricated inventory slot");
    private static AnswerTextKind KindAt(IReadOnlyList<ChatTextSpan> spans, int index)
    {
        int start = 0;
        foreach (ChatTextSpan span in spans)
        {
            if (index >= start && index < start + span.Text.Length) return span.Kind;
            start += span.Text.Length;
        }
        throw new InvalidOperationException("Expected displayed character was missing.");
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(string name, Action test, List<string> results)
    {
        try { test(); results.Add("PASS " + name); }
        catch (Exception exception) { throw new InvalidOperationException(name + ": " + exception.Message, exception); }
    }
}
