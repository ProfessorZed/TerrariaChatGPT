#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TerrariaGPT.Common.Config;
using TerrariaGPT.Common.Goals;
using TerrariaGPT.Common.Players;
using TerrariaGPT.Common.Services;

namespace TerrariaGPT.Tests;

public static partial class AssistantServiceTests
{
    private static void CheckOnlineFeatures(string workspaceRoot, List<string> output)
    {
        Check("Opted-in observations are bounded detached data and never replace the original question", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Terraria.ModLoader.ModContent.Config.ShareObservedProgress = true;
            GptProgressPlayer.Snapshot = ObservedFixture();
            Reply reply = fixture.Reply("Ordinary model advice.");
            GptAssistantService.Submit("What should I aim for next?");
            using JsonDocument sent = JsonDocument.Parse(Arrived(reply).Body);
            JsonElement root = sent.RootElement, observations = root.GetProperty("observedContext");
            Equal("What should I aim for next?", root.GetProperty("question").GetString());
            Require(root.GetProperty("shareProgress").GetBoolean(), "Sharing choice absent.");
            Require(root.GetProperty("webSearch").GetBoolean(), "Search setting absent.");
            Equal(3, observations.GetProperty("inventory")[0].GetProperty("count").GetInt32());
            Equal("account", observations.GetProperty("achievements")[0].GetProperty("scope").GetString());
            Require(!root.GetRawText().Contains("tile 123, 456", StringComparison.Ordinal), "Coordinate chest label escaped projection.");
            Require(!root.GetRawText().Contains("PRIVATE-", StringComparison.Ordinal), "Legacy private fixture escaped.");
            Equal(0, root.GetProperty("knowledge").GetProperty("inventory").GetArrayLength());
            Finished(); Equal(0, TerrariaGPT.Common.Knowledge.GameReferenceFacts.ResolveCalls);
        }, output);
        Check("Turning sharing off cancels late context-bearing replies and retains ordinary private-free follow-ups", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Terraria.ModLoader.ModContent.Config.ShareObservedProgress = true;
            GptProgressPlayer.Snapshot = ObservedFixture();
            Reply old = fixture.Reply("OLD-OBSERVED-ANSWER", gated: true);
            GptAssistantService.Submit("What do I own?"); Arrived(old);
            Terraria.ModLoader.ModContent.Config.ShareObservedProgress = false;
            Terraria.Player.RejectReads = true;
            GptAssistantService.Pump(); Require(!GptAssistantService.IsBusy, "Privacy change did not cancel pending reply.");
            old.Release.Set();
            Reply fresh = fixture.Reply("NEW-PUBLIC-ANSWER");
            GptAssistantService.Submit("Why are there seasons?");
            using JsonDocument sent = JsonDocument.Parse(Arrived(fresh).Body);
            Equal(JsonValueKind.Null, sent.RootElement.GetProperty("observedContext").ValueKind);
            Equal(0, sent.RootElement.GetProperty("conversation").GetArrayLength());
            Finished(); Equal("NEW-PUBLIC-ANSWER", GptAssistantService.LastAnswer);
            Reply follow = fixture.Reply("A normal follow-up.");
            GptAssistantService.Submit("Explain that more simply.");
            using JsonDocument history = JsonDocument.Parse(Arrived(follow).Body);
            Equal(2, history.RootElement.GetProperty("conversation").GetArrayLength());
            Require(!history.RootElement.GetRawText().Contains("OLD-OBSERVED", StringComparison.Ordinal), "Old private answer reached history.");
            Finished();
        }, output);
        Check("Verified inline sources and inert goal suggestions survive display cleanup", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Reply reply = fixture.ReplyJson("""
                {"mode":"chatgpt","answer":"Defeat King Slime first. [1] Then prepare for the Eye. [2]",
                 "searched":true,"citations":[{"index":1,"title":"Official reference","url":"https://terraria.wiki.gg/wiki/King_Slime"},
                    {"index":2,"title":"Unsafe","url":"file:///C:/private.txt"}],
                 "goalSuggestion":{"title":"Boss goal","steps":[{"kind":"boss","key":"king_slime","label":"King Slime","target":1}]}}
                """);
            GptAssistantService.Submit("I want to defeat King Slime."); Arrived(reply); Finished();
            ChatEntry answer = GptAssistantService.Messages.Last();
            Require(answer.Text.Contains("[1]", StringComparison.Ordinal), "Verified inline citation was removed.");
            Require(!answer.Text.Contains("[2]", StringComparison.Ordinal), "Unsafe link marker survived.");
            Equal(1, answer.Citations.Count); Require(answer.WebSearched, "Real search status lost.");
            Equal("king_slime", answer.GoalSuggestion!.Steps[0].Key);
            Reply next = fixture.Reply("Follow-up.");
            GptAssistantService.Submit("Tell me more.");
            using JsonDocument history = JsonDocument.Parse(Arrived(next).Body);
            Require(!history.RootElement.GetProperty("conversation").GetRawText().Contains("[1]", StringComparison.Ordinal), "Unbound citation IDs leaked into the next generation.");
            Finished();
        }, output);
        Check("Malformed goals and source metadata do not discard an otherwise useful answer", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Reply reply = fixture.ReplyJson("""
                {"mode":"chatgpt","answer":"Useful answer.","citations":[{"index":{},"url":"https://terraria.wiki.gg/"}],
                 "goalSuggestion":{"title":"Bad","steps":[{"kind":"craft","key":"Wood","label":"Wood","target":"infinite"}]}}
                """);
            GptAssistantService.Submit("Give me advice."); Arrived(reply); Finished();
            Equal("Useful answer.", GptAssistantService.LastAnswer);
            Equal(0, GptAssistantService.Messages.Last().Citations.Count);
            Require(GptAssistantService.Messages.Last().GoalSuggestion is null, "Malformed goal reached UI.");
        }, output);
        Check("Disabling web search is transmitted without changing the selected provider", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Terraria.ModLoader.ModContent.Config.EnableWebSearch = false;
            Reply reply = fixture.Reply("Ordinary answer.");
            GptAssistantService.Submit("Hello.");
            using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
            Require(!request.RootElement.GetProperty("webSearch").GetBoolean(), "Search toggle ignored.");
            Equal("ChatGPT", request.RootElement.GetProperty("provider").GetString()); Finished();
        }, output);
    }

    private static ProgressSnapshot ObservedFixture() => new(DateTimeOffset.UnixEpoch,
        new[] { new ProgressItem("Terraria/Wood", "Wood", 3, "Inventory", true) },
        new[] { new RememberedProgressItem("Terraria/StoneBlock", "Stone Block", 9, "Chest at tile 123, 456", DateTimeOffset.UnixEpoch, false) },
        new[] { new ProgressFlag("king_slime", "King Slime", true, "world") },
        Array.Empty<ProgressFlag>(), new[] { new ProgressFlag("TEST", "Test achievement", true, "account") },
        new[] { new ProgressSubject("Terraria/Forest", "Forest") },
        new[] { new NpcProgress("Terraria/Guide", "Guide", true, false) }, new[] { "Forest" });
}
