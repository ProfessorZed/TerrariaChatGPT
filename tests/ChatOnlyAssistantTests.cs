#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Tests;

public static partial class AssistantServiceTests
{
    private static void CheckChatOnly(string workspaceRoot, List<string> output)
    {
        Check("ChatGPT routes natural, crafting, inventory and follow-up questions without game reads or local templates", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Terraria.Player.RejectReads = true;
            foreach (string question in new[] { "What am I missing for Wooden Sword?", "Do I have Wood?", "What can I make from Wood?", "Where can I find it?", "help", "Tell me a short joke." })
            {
                Reply reply = fixture.Reply("This is the model's own answer.");
                GptAssistantService.Submit(question);
                using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
                JsonElement root = request.RootElement;
                Equal(question, root.GetProperty("question").GetString());
                Equal("ChatGPT", root.GetProperty("provider").GetString());
                Require(root.GetProperty("chatOnly").GetBoolean(), "Chat-only contract was omitted.");
                AssertNoObservations(root);
                Finished();
                Equal("This is the model's own answer.", GptAssistantService.LastAnswer);
            }
            Equal(0, Terraria.Player.ModPlayerReads);
            Equal(0, GameReferenceFacts.ResolveCalls);
        }, output);
        Check("OpenAI API uses the same observation-free original-question path", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.OpenAiApi);
            Terraria.Player.RejectReads = true;
            Reply reply = fixture.Reply("An API response.", mode: "openai");
            GptAssistantService.Submit("What should I craft next?");
            using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
            Equal("OpenAiApi", request.RootElement.GetProperty("provider").GetString());
            Require(request.RootElement.GetProperty("chatOnly").GetBoolean(), "API did not opt into pure chat.");
            AssertNoObservations(request.RootElement); Finished();
            Reply followUp = fixture.Reply("The next API response.", mode: "openai");
            GptAssistantService.Submit("Why that one?");
            using JsonDocument next = JsonDocument.Parse(Arrived(followUp).Body);
            Equal(2, next.RootElement.GetProperty("conversation").GetArrayLength()); Finished();
            Equal(0, Terraria.Player.ModPlayerReads); Equal(0, GameReferenceFacts.ResolveCalls);
        }, output);
        Check("AI follow-ups use successful natural conversation even when legacy inventory sharing is disabled", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Terraria.ModLoader.ModContent.Config.ShareObservedInventory = false;
            Reply first = fixture.Reply("Use Lucky Horseshoe for fall damage.", new[] { new AnswerHighlight("Lucky Horseshoe", "item") });
            GptAssistantService.Submit("What counters fall damage?"); Arrived(first); Finished();
            GptAssistantService.AddNotice("PRIVATE-NOTICE: old remembered storage.");
            GptAssistantService.Submit("/help");
            Reply followUp = fixture.Reply("A natural follow-up answer.");
            GptAssistantService.Submit("Can I make it instead?");
            using JsonDocument request = JsonDocument.Parse(Arrived(followUp).Body);
            JsonElement root = request.RootElement;
            Equal("Can I make it instead?", root.GetProperty("question").GetString());
            JsonElement turns = root.GetProperty("conversation");
            Equal(2, turns.GetArrayLength());
            Equal("What counters fall damage?", turns[0].GetProperty("content").GetString());
            Equal("Use Lucky Horseshoe for fall damage.", turns[1].GetProperty("content").GetString());
            Require(!request.RootElement.GetRawText().Contains("PRIVATE-", StringComparison.Ordinal), "A local notice reached the AI.");
            Finished();
            Require(GptAssistantService.Messages.Any(entry => entry.Highlights.Any(mark => mark.Text == "Lucky Horseshoe")), "Existing semantic colors were lost.");
            Equal(0, GameReferenceFacts.ResolveCalls);
        }, output);
        Check("Legacy wiki/private display history cannot enter AI chat after provider changes", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            GptAssistantService.Submit("Do I have Wood?");
            Require(GptAssistantService.LastAnswer!.Contains("Wood", StringComparison.Ordinal), "Legacy fixture did not produce an inventory answer.");
            GptAssistantService.AddNotice("PRIVATE-NOTICE from Wiki only.");
            Terraria.ModLoader.ModContent.Config.Provider = GptProvider.ChatGPT;
            Reply reply = fixture.Reply("Fresh AI conversation.");
            GptAssistantService.Submit("Could you help me?");
            using JsonDocument first = JsonDocument.Parse(Arrived(reply).Body);
            Equal(0, first.RootElement.GetProperty("conversation").GetArrayLength());
            AssertNoObservations(first.RootElement); Finished();
            Terraria.ModLoader.ModContent.Config.Provider = GptProvider.WikiOnly;
            GptAssistantService.Submit("Do I have Wood?");
            Terraria.ModLoader.ModContent.Config.Provider = GptProvider.ChatGPT;
            Reply again = fixture.Reply("Another fresh conversation.");
            GptAssistantService.Submit("What were we discussing?");
            using JsonDocument second = JsonDocument.Parse(Arrived(again).Body);
            Equal(0, second.RootElement.GetProperty("conversation").GetArrayLength());
            Require(!second.RootElement.GetRawText().Contains("PRIVATE-", StringComparison.Ordinal), "Private legacy text escaped."); Finished();
        }, output);
        Check("Provider failures and unreadable answers never become AI conversation", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            foreach (var sample in new[] { (Mode: "error", Answer: "Connect your plan first."),
                (Mode: "chatgpt", Answer: "**https://terraria.wiki.gg/wiki/Wood** [1]") })
            {
                Reply reply = fixture.Reply(sample.Answer, mode: sample.Mode);
                GptAssistantService.Submit("Failed or legacy request: " + sample.Answer); Arrived(reply); Finished();
            }
            Reply success = fixture.Reply("A successful fresh answer.");
            GptAssistantService.Submit("Please try again.");
            using JsonDocument request = JsonDocument.Parse(Arrived(success).Body);
            Equal(0, request.RootElement.GetProperty("conversation").GetArrayLength()); Finished();
        }, output);
        Check("Successful AI conversation is bounded without duplicating the current question", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            for (int index = 0; index < 8; index++)
            {
                Reply reply = fixture.Reply("Answer " + index + ": " + new string('a', 2400));
                GptAssistantService.Submit("Question " + index + ": " + new string('q', 900)); Arrived(reply); Finished();
            }
            Reply latest = fixture.Reply("Latest answer.");
            const string question = "Continue our discussion.";
            GptAssistantService.Submit(question);
            using JsonDocument request = JsonDocument.Parse(Arrived(latest).Body);
            JsonElement[] history = request.RootElement.GetProperty("conversation").EnumerateArray().ToArray();
            Require(history.Length is > 0 and <= 12, "AI history message bound failed.");
            Equal("user", history[0].GetProperty("role").GetString());
            Require(history.All(turn => turn.GetProperty("content").GetString()!.Length <= 2000), "Individual AI history was unbounded.");
            Require(history.Sum(turn => turn.GetProperty("content").GetString()!.Length) <= 12000, "Total AI history exceeded its budget.");
            Require(history.All(turn => turn.GetProperty("content").GetString() != question), "Current question was duplicated.");
            Require(history[^1].GetProperty("content").GetString()!.StartsWith("Answer 7:", StringComparison.Ordinal), "Newest answer was dropped."); Finished();
        }, output);
        Check("Clearing chat cancels old AI work and prevents old answers from refilling safe history", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Reply old = fixture.Reply("OLD-AI-ANSWER", gated: true);
            GptAssistantService.Submit("Old question."); Arrived(old);
            GptAssistantService.Submit("/clear");
            Require(!GptAssistantService.IsBusy && GptAssistantService.Messages.Count == 0, "Clear command did not cancel the pending request.");
            old.Release.Set();
            Reply current = fixture.Reply("NEW-AI-ANSWER");
            GptAssistantService.Submit("New question."); Arrived(current); Finished();
            Thread.Sleep(40); GptAssistantService.Pump();
            Reply next = fixture.Reply("After reset.");
            GptAssistantService.Submit("Continue this one.");
            using JsonDocument request = JsonDocument.Parse(Arrived(next).Body);
            JsonElement history = request.RootElement.GetProperty("conversation");
            Equal(2, history.GetArrayLength());
            Equal("New question.", history[0].GetProperty("content").GetString());
            Equal("NEW-AI-ANSWER", history[1].GetProperty("content").GetString());
            Require(!request.RootElement.GetRawText().Contains("OLD-", StringComparison.Ordinal), "A canceled old AI reply entered safe history."); Finished();
            GptAssistantService.Submit("/clear");
            Equal(0, GptAssistantService.Messages.Count);
        }, output);
        Check("Disabled online answers stay local without falling back to inventory tools", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Terraria.Player.RejectReads = true;
            Terraria.ModLoader.ModContent.Config.EnableOnlineAnswers = false;
            GptAssistantService.Submit("What am I missing for Wooden Sword?");
            Equal("Online answers are disabled in Mod Configuration.", GptAssistantService.LastAnswer);
            Equal(0, Terraria.Player.ModPlayerReads); Equal(0, fixture.ReceivedCount);
        }, output);
    }

    private static void AssertNoObservations(JsonElement request)
    {
        JsonElement knowledge = request.GetProperty("knowledge");
        Equal("", knowledge.GetProperty("playerName").GetString());
        Require(!knowledge.GetProperty("inventoryShared").GetBoolean(), "AI observations were not explicitly disabled.");
        foreach (string name in new[] { "knownSubjects", "inventory", "ownedChests", "currentBiomes", "facts", "referenceSubjects", "recipes" })
            Equal(0, knowledge.GetProperty(name).GetArrayLength());
        Require(!knowledge.GetProperty("recipeLookupComplete").GetBoolean(), "Empty AI context claimed recipe completeness.");
        Equal("fabricated-version", knowledge.GetProperty("gameVersion").GetString());
        Require(!request.GetRawText().Contains("PRIVATE-", StringComparison.Ordinal), "Private snapshot fixture data reached AI chat.");
    }
}
