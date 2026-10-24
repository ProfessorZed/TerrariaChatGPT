#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Memory;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Tests;

public static partial class AssistantServiceTests
{
    private static string MemoryReply(string answer = "I will keep answers concise.") => JsonSerializer.Serialize(new
    {
        answer, highlights = Array.Empty<object>(), goalSuggestion = (object?)null,
        memorySuggestions = new[] { new { kind = "preference", topic = "response_style", text = "Prefers concise answers.",
            evidence = "I prefer concise answers", item = "", target = "" } }
    });

    private static void CheckMemory(string workspaceRoot, List<string> output)
    {
        Check("Saved preferences are detached from game observations and captured before background requests", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            var memory = Terraria.Main.LocalPlayer.Preferences;
            memory.Entries.Add(new("preference", "playstyle", "Prefers melee weapons.", "", ""));
            Reply reply = fixture.Reply("A melee recommendation.", gated: true);
            GptAssistantService.Submit("What should I use?");
            memory.Entries.Clear(); // Captured context must not share the mutable list.
            using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
            var saved = request.RootElement.GetProperty("savedMemory");
            Equal(1, saved.GetProperty("entries").GetArrayLength());
            Require(saved.GetProperty("enabled").GetBoolean(), "Memory context was omitted.");
            AssertNoObservations(request.RootElement); reply.Release.Set(); Finished();
        }, output);
        Check("Memory applies on the game thread after completion and attaches a visible Undo receipt", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Reply reply = fixture.ReplyJson(MemoryReply(), gated: true);
            GptAssistantService.Submit("I prefer concise answers"); Arrived(reply);
            Equal(0, Terraria.Main.LocalPlayer.Preferences.AppliedCount);
            reply.Release.Set(); Finished();
            Equal(1, Terraria.Main.LocalPlayer.Preferences.AppliedCount);
            Equal("I prefer concise answers", Terraria.Main.LocalPlayer.Preferences.LastQuestion);
            Equal("synthetic-change", GptAssistantService.Messages[^1].MemoryChangeId);
            Equal("Prefers concise answers.", GptAssistantService.Messages[^1].MemorySummary);
            Reply follow = fixture.Reply("Continuing."); GptAssistantService.Submit("Thanks.");
            using JsonDocument request = JsonDocument.Parse(Arrived(follow).Body);
            Equal(2, request.RootElement.GetProperty("conversation").GetArrayLength()); Finished();
        }, output);
        Check("Manual memory changes cancel old replies and their memory suggestions", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Reply reply = fixture.ReplyJson(MemoryReply(), gated: true);
            GptAssistantService.Submit("I prefer concise answers"); Arrived(reply);
            Terraria.Main.LocalPlayer.Preferences.Revision++; GptAssistantService.Pump(); reply.Release.Set();
            Thread.Sleep(40); GptAssistantService.Pump();
            Equal(0, Terraria.Main.LocalPlayer.Preferences.AppliedCount);
            Require(!GptAssistantService.IsBusy && GptAssistantService.Messages.All(entry => entry.MemoryChangeId is null), "Stale memory was applied.");
        }, output);
        Check("Changing character cannot save an old character's pending suggestion", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Reply reply = fixture.ReplyJson(MemoryReply(), gated: true);
            GptAssistantService.Submit("I prefer concise answers"); Arrived(reply);
            var previous = Terraria.Main.LocalPlayer;
            Terraria.Main.LocalPlayer = new(); GptAssistantService.Pump(); reply.Release.Set();
            Thread.Sleep(40); GptAssistantService.Pump();
            Equal(0, previous.Preferences.AppliedCount); Equal(0, Terraria.Main.LocalPlayer.Preferences.AppliedCount);
        }, output);
        Check("Disabling memory sends no entries and ignores unsolicited suggestions", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            var memory = Terraria.Main.LocalPlayer.Preferences;
            memory.Entries.Add(new("preference", "response_style", "Prefers concise answers.", "", ""));
            memory.Enabled = false; memory.Revision++;
            Reply reply = fixture.ReplyJson(MemoryReply());
            GptAssistantService.Submit("I prefer concise answers");
            using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
            Require(!request.RootElement.GetProperty("savedMemory").GetProperty("enabled").GetBoolean(), "Disabled memory was enabled remotely.");
            Equal(0, request.RootElement.GetProperty("savedMemory").GetProperty("entries").GetArrayLength());
            Finished(); Equal(0, memory.AppliedCount); Require(GptAssistantService.Messages[^1].MemoryChangeId is null, "False saved notice.");
        }, output);
        Check("Forgetting a memory also excludes its copies in old conversation from later requests", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Reply first = fixture.Reply("Your preference is concise answers.");
            GptAssistantService.Submit("I prefer concise answers"); Arrived(first); Finished();
            Terraria.Main.LocalPlayer.Preferences.Revision++;
            Reply next = fixture.Reply("Fresh conversation."); GptAssistantService.Submit("Hello.");
            using JsonDocument request = JsonDocument.Parse(Arrived(next).Body);
            Equal(0, request.RootElement.GetProperty("conversation").GetArrayLength()); Finished();
        }, output);
        Check("Clear chat keeps saved memory but clears short-term conversation", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Terraria.Main.LocalPlayer.Preferences.Entries.Add(new("preference", "language", "Prefers English.", "", ""));
            GptAssistantService.Submit("/clear");
            Reply reply = fixture.Reply("Hello."); GptAssistantService.Submit("Hello.");
            using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
            Equal(0, request.RootElement.GetProperty("conversation").GetArrayLength());
            Equal(1, request.RootElement.GetProperty("savedMemory").GetProperty("entries").GetArrayLength()); Finished();
        }, output);
        Check("Optional memory failure preserves the completed answer without a false saved notice", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Terraria.Main.LocalPlayer.Preferences.ThrowOnApply = true;
            Reply reply = fixture.ReplyJson(MemoryReply()); GptAssistantService.Submit("I prefer concise answers"); Arrived(reply); Finished();
            Equal("I will keep answers concise.", GptAssistantService.LastAnswer);
            Require(GptAssistantService.Messages[^1].MemoryChangeId is null, "Failed optional memory claimed success.");
        }, output);
        Check("Wiki-only replies cannot learn personal memory", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            Reply reply = fixture.ReplyJson(MemoryReply()); GptAssistantService.Submit("Tell me about flight."); Arrived(reply); Finished();
            Equal(0, Terraria.Main.LocalPlayer.Preferences.AppliedCount);
        }, output);
    }
}
