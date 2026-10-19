using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.Knowledge;

namespace TerrariaChatGPT.Tests;

public static class ChatConvenienceTests
{
    public static string[] Run()
    {
        var results = new List<string>();
        Check("Short item follow-ups retain the public subject", () => {
            Equal("Where can I find it? (About Starfury)", FollowUpContext.Expand("Where can I find it?", "Starfury"));
            Equal("Can I make it with Wood? (About Work Bench)", FollowUpContext.Expand("Can I make it with Wood?", "Work Bench"));
            Equal("What am I missing? (About Blade of Grass)", FollowUpContext.Expand("What am I missing?", "Blade of Grass"));
        }, results);
        Check("New capability topics never inherit the previous item", () => {
            const string query = "What negates fall damage and can I make it?";
            Equal(query, FollowUpContext.Expand(query, "Starfury"));
            Equal("How do I get Wood?", FollowUpContext.Expand("How do I get Wood?", "Starfury"));
            Equal("Can I make this Wooden Sword?", FollowUpContext.Expand("Can I make this Wooden Sword?", "Starfury"));
            Equal("Where can I find that Wooden Sword?", FollowUpContext.Expand("Where can I find that Wooden Sword?", "Starfury"));
            Equal("What is this Wooden Sword?", FollowUpContext.Expand("What is this Wooden Sword?", "Starfury"));
            Equal("What does it do? (About Starfury)", FollowUpContext.Expand("What does it do?", "Starfury"));
            Equal("Can I use it with Wood? (About Starfury)", FollowUpContext.Expand("Can I use it with Wood?", "Starfury"));
            Equal("Where can I get it with Dirt Block? (About Starfury)", FollowUpContext.Expand("Where can I get it with Dirt Block?", "Starfury"));
            Equal("Can I make it rain?", FollowUpContext.Expand("Can I make it rain?", "Starfury"));
        }, results);
        Check("Pronoun ownership questions inherit only the previous public item", () => {
            foreach (string pronoun in new[] { "it", "that", "this", "one", "any" })
                Equal("Do I have " + pronoun + "? (About Wood)", FollowUpContext.Expand("Do I have " + pronoun + "?", "Wood"));
            Equal("And did I have it? (About Wood)", FollowUpContext.Expand("And did I have it?", "Wood"));
            Equal("Do I have any Wood?", FollowUpContext.Expand("Do I have any Wood?", "Starfury"));
            Equal("Do I have any other weapons?", FollowUpContext.Expand("Do I have any other weapons?", "Starfury"));
            Equal("Do I have it?", FollowUpContext.Expand("Do I have it?", null));
            Equal("Can I make it rain?", FollowUpContext.Expand("Can I make it rain?", "Starfury"));
        }, results);
        Check("Ownership follow-up reaches the actual local inventory total", () => {
            KnowledgeSnapshot snapshot = Snapshot();
            string query = FollowUpContext.Expand("Do I have it?", "Wood");
            string answer = LocalKnowledgeAnswer.TryAnswer(query, snapshot, "Terraria/Wood", "Wood") ?? throw new Exception("Ownership follow-up was not recognized.");
            if (!answer.Contains("You have 2 Wood", StringComparison.Ordinal)) throw new Exception("The previous public item did not retain its observed total.");
        }, results);
        Check("Crafting follow-up retains the requested target even when an ingredient is named", () => {
            KnowledgeSnapshot snapshot = Snapshot();
            foreach (string query in new[] { "What am I missing?", "Can I make it?", "Can I make it with Wood?" }) {
                string expanded = FollowUpContext.Expand(query, "Wooden Sword");
                CraftingAssessment plan = CraftingPlanner.Assess(expanded, snapshot, "Terraria/WoodenSword", "Wooden Sword")
                    ?? throw new Exception("A crafting follow-up lost its target: " + query);
                Equal("Wooden Sword", plan.ResultName); Equal(2, plan.Ingredients.Single().MissingCount);
            }
        }, results);
        Check("Disabled sharing drops all historical answers", () => {
            var turns = new[] { new ConversationTurn("user", "Where is my Wood?"), new ConversationTurn("assistant", "Private chest count") };
            Equal(0, FollowUpContext.HistoryForRequest(turns, false).Count);
        }, results);
        Check("Conversation context is bounded and never promotes commands to instructions", () => {
            var turns = Enumerable.Range(0, 20).Select(index => new ConversationTurn(index % 2 == 0 ? "user" : "assistant", new string('a', 1200)));
            var context = FollowUpContext.HistoryForRequest(turns, true);
            Equal(6, context.Count); Equal("user", context[0].Role);
            if (context.Any(turn => turn.Content.Length > 800)) throw new Exception("Unbounded historical message.");
            Equal(0, FollowUpContext.HistoryForRequest(new[] { new ConversationTurn("system", "Untrusted"), new ConversationTurn("user", "/clear") }, true).Count);
        }, results);
        Check("Pure AI context keeps twelve recent messages independently of legacy sharing", () => {
            var turns = Enumerable.Range(0, 20).Select(index => new ConversationTurn(index % 2 == 0 ? "user" : "assistant", "Natural message " + index));
            var context = ChatConversationContext.HistoryForRequest(turns);
            Equal(12, context.Count); Equal("Natural message 8", context[0].Content); Equal("Natural message 19", context[^1].Content);
            Equal(0, ChatConversationContext.HistoryForRequest(new[] { new ConversationTurn("system", "Untrusted") }).Count);
        }, results);
        Check("Pure AI context bounds each message and total text without orphaning an assistant", () => {
            var turns = Enumerable.Range(0, 20).Select(index => new ConversationTurn(index % 2 == 0 ? "user" : "assistant", new string((char)('a' + index), 2500)));
            var context = ChatConversationContext.HistoryForRequest(turns);
            Equal(6, context.Count); Equal("user", context[0].Role);
            Equal(12000, context.Sum(turn => turn.Content.Length));
            if (context.Any(turn => turn.Content.Length > 2000)) throw new Exception("AI message bound failed.");
            Equal(0, ChatConversationContext.HistoryForRequest(new[] { new ConversationTurn("assistant", "Orphan") }).Count);
        }, results);
        Check("Question recall preserves an unfinished draft", () => {
            var recall = new QuestionRecall(); recall.Record("First question"); recall.Record("Second question");
            Equal("Second question", recall.Previous("My unfinished draft"));
            Equal("First question", recall.Previous("Second question"));
            Equal("Second question", recall.Next("First question"));
            Equal("My unfinished draft", recall.Next("Second question"));
        }, results);
        Check("Recall ignores commands, adjacent duplicates and cleared worlds", () => {
            var recall = new QuestionRecall(); recall.Record("Question"); recall.Record("Question"); recall.Record("/clear");
            Equal("Question", recall.Previous("Draft")); Equal("Question", recall.Previous("Question"));
            recall.Clear(); Equal("Draft", recall.Previous("Draft"));
        }, results);
        Check("Editing a recalled question starts a fresh recall session", () => {
            var recall = new QuestionRecall(); recall.Record("Question"); recall.Previous("Old draft"); recall.Edited();
            Equal("Question", recall.Previous("Edited question")); Equal("Edited question", recall.Next("Question"));
        }, results);
        return results.ToArray();
    }
    private static KnowledgeSnapshot Snapshot() => new("Fabricated character", DateTimeOffset.UnixEpoch,
        Array.Empty<KnowledgeSubject>(), new[] { new ItemStackSnapshot("Terraria/Wood", "Wood", 2, "Fabricated inventory") },
        Array.Empty<OwnedChestSnapshot>(), Array.Empty<string>())
    {
        Recipes = new[] { new RecipeReferenceSnapshot("Terraria/WoodenSword", "Wooden Sword", 1,
            new[] { new RecipeIngredientSnapshot("Wood", 4, new[] { "Terraria/Wood" }) }, new[] { "Work Bench" }, Array.Empty<string>()) }
    };
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Check(string name, Action test, List<string> output) { test(); output.Add("PASS " + name); }
}
