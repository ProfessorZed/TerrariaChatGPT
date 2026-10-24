#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Players;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.Native;

namespace TerrariaChatGPT.Tests;

/// <summary>Production service against an in-process connection fixture and fabricated observations.</summary>
public static partial class AssistantServiceTests
{
    public static string[] Run(string workspaceRoot)
    {
        var output = new List<string>();
        Check("Outgoing shared history is bounded, excludes the current question, and carries detached observations", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            for (int i = 0; i < 8; i++)
            {
                GptAssistantService.Submit("Do I have Wood? Question " + i + " " + new string('x', 900));
                GptAssistantService.AddNotice("Private remembered supplies " + new string('y', 1_000));
            }
            Reply reply = fixture.Reply("Search the general sky islands.");
            const string question = "Where can I find Starfury?";
            GptAssistantService.Submit(question);
            Request captured = Arrived(reply);
            Finished();
            using JsonDocument request = JsonDocument.Parse(captured.Body);
            JsonElement root = request.RootElement;
            var turns = root.GetProperty("conversation").EnumerateArray().ToArray();
            Require(turns.Length is > 0 and <= 6 && turns[0].GetProperty("role").GetString() == "user", "History was not bounded to six turns starting with a user.");
            Require(turns.All(turn => turn.GetProperty("content").GetString()!.Length <= 800), "Historical text was not bounded.");
            Require(turns.All(turn => turn.GetProperty("content").GetString() != question), "The current question was duplicated in history.");
            Equal(1, root.GetProperty("knowledge").GetProperty("inventory").GetArrayLength());
            Require(root.GetProperty("observedWorldOnly").GetBoolean(), "Observation restriction was omitted.");
        }, output);
        Check("Disabling sharing removes historical private answers and every private snapshot field", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            GptAssistantService.Submit("Do I have Wood?");
            GptAssistantService.AddNotice("PRIVATE-HISTORY-MARKER: remembered chest contents.");
            Terraria.ModLoader.ModContent.Config.ShareObservedInventory = false;
            Reply reply = fixture.Reply("General Starfury acquisition advice.");
            GptAssistantService.Submit("Where can I find Starfury?");
            Request captured = Arrived(reply);
            Finished();
            using JsonDocument request = JsonDocument.Parse(captured.Body);
            JsonElement root = request.RootElement, knowledge = root.GetProperty("knowledge");
            Equal(0, root.GetProperty("conversation").GetArrayLength());
            Equal("", knowledge.GetProperty("playerName").GetString());
            foreach (string field in new[] { "knownSubjects", "inventory", "ownedChests", "currentBiomes" })
                Equal(0, knowledge.GetProperty(field).GetArrayLength());
            Require(!knowledge.GetProperty("inventoryShared").GetBoolean(), "Sharing restriction was not explicit.");
            Require(knowledge.GetProperty("facts").GetArrayLength() > 0 && knowledge.GetProperty("referenceSubjects").GetArrayLength() > 0, "Public reference evidence was unnecessarily removed.");
            Require(!captured.Body.Contains("PRIVATE-", StringComparison.Ordinal), "Private historical or snapshot markers escaped.");
        }, output);
        Check("Only highlights present in the cleaned answer can select the next public subject", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            Reply first = fixture.Reply("Use **Lucky Horseshoe**.", new[] {
                new AnswerHighlight("Starfury", "item"), new AnswerHighlight("Lucky Horseshoe", "item") });
            GptAssistantService.Submit("Recommend an accessory."); Arrived(first); Finished();
            Equal(1, GptAssistantService.Messages[^1].Highlights.Count);
            Reply followUp = fixture.Reply("Search general floating islands.");
            GptAssistantService.Submit("Where can I find it?");
            using JsonDocument request = JsonDocument.Parse(Arrived(followUp).Body);
            Equal("Where can I find it? (About Lucky Horseshoe)", request.RootElement.GetProperty("question").GetString());
            Finished();
        }, output);
        Check("Reset cancels an old in-flight answer and rejects already-queued old epoch completions", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            Reply old = fixture.Reply("OLD-WORLD-ANSWER", gated: true);
            int oldEpoch = GptAssistantService.Epoch;
            GptAssistantService.Submit("Where can I find Starfury?"); Arrived(old);
            GptAssistantService.Reset();
            Reply current = fixture.Reply("NEW-WORLD-ANSWER", gated: true);
            GptAssistantService.Submit("Describe Lucky Horseshoe."); Arrived(current);
            MethodInfo complete = typeof(GptAssistantService).GetMethod("Complete", BindingFlags.NonPublic | BindingFlags.Static)!;
            complete.Invoke(null, new object?[] { oldEpoch, "OLD-QUEUED-ANSWER", null });
            GptAssistantService.Pump();
            Require(GptAssistantService.IsBusy, "An old completion cleared the new request's busy state.");
            old.Release.Set(); current.Release.Set(); Finished();
            Thread.Sleep(40); GptAssistantService.Pump();
            Require(GptAssistantService.Messages.Count == 2 && GptAssistantService.LastAnswer == "NEW-WORLD-ANSWER", "An old world answer entered the new conversation.");
            Require(!GptAssistantService.Messages.Any(entry => entry.Text.Contains("OLD-", StringComparison.Ordinal)), "Old response data survived reset.");
        }, output);
        Check("An answer reduced to links and markup gets a readable fallback", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            Reply reply = fixture.Reply("**https://terraria.wiki.gg/wiki/Starfury** [1]");
            GptAssistantService.Submit("Where can I find Starfury?"); Arrived(reply); Finished();
            Equal("No readable answer was returned. Try asking again.", GptAssistantService.LastAnswer);
        }, output);
        Check("Local crafting arithmetic returns immediately without contacting an online provider", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            GptAssistantService.Submit("What am I missing for Wooden Sword?");
            Require(!GptAssistantService.IsBusy && GptAssistantService.LastAnswer!.Contains("Still need: 2 Wood", StringComparison.Ordinal), "Local crafting did not return exact missing materials.");
            Equal(0, fixture.ReceivedCount);
            Require(GptAssistantService.Messages[^1].Highlights.Any(mark => mark.Kind == "missing"), "Local crafting formatting was bypassed.");
        }, output);
        Check("Remote answers retain public item and station colors even when GPT supplies no metadata", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            Reply reply = fixture.Reply("Recipe: 4 Wood for a Wooden Sword.\nCrafting station required: Work Bench.");
            GptAssistantService.Submit("Where can I get Wooden Sword and how do I use it?");
            Arrived(reply); Finished();
            IReadOnlyList<AnswerHighlight> highlights = GptAssistantService.Messages[^1].Highlights;
            Require(highlights.Any(mark => mark.Kind == "item" && mark.Text == "Wood"), "The recipe material was not highlighted.");
            Require(highlights.Any(mark => mark.Kind == "item" && mark.Text == "Wooden Sword"), "The result item was not highlighted.");
            Require(highlights.Any(mark => mark.Kind == "location" && mark.Text == "Work Bench"), "The crafting station was not highlighted.");
        }, output);
        Check("Named follow-up ingredients add public references without replacing the pronoun target", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            Reply initial = fixture.Reply("Starfury is a general item reference.");
            GptAssistantService.Submit("Describe Starfury."); Arrived(initial); Finished();
            foreach (string question in new[] { "Can I use it with Wood?", "Where can I get it with Dirt Block?" })
            {
                Reply followUp = fixture.Reply("General compatibility advice.");
                GptAssistantService.Submit(question);
                using JsonDocument request = JsonDocument.Parse(Arrived(followUp).Body);
                Equal(question + " (About Starfury)", request.RootElement.GetProperty("question").GetString());
                var references = request.RootElement.GetProperty("knowledge").GetProperty("referenceSubjects").EnumerateArray().ToArray();
                Equal("Starfury", references[0].GetProperty("name").GetString());
                Equal(2, references.Length);
                Require(references.Any(item => item.GetProperty("name").GetString() == (question.Contains("Dirt", StringComparison.Ordinal) ? "Dirt Block" : "Wood")), "The named public secondary item was omitted.");
                Finished();
                Equal(question, GptAssistantService.Messages[^2].Text);
            }
        }, output);
        Check("A new named target takes priority and remains the target of subsequent crafting follow-ups", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            Reply initial = fixture.Reply("Starfury is a general item reference.");
            GptAssistantService.Submit("Describe Starfury."); Arrived(initial); Finished();
            Reply named = fixture.Reply("General Wooden Sword acquisition advice.");
            const string question = "Where can I find that Wooden Sword?";
            GptAssistantService.Submit(question);
            using JsonDocument request = JsonDocument.Parse(Arrived(named).Body);
            Equal(question, request.RootElement.GetProperty("question").GetString());
            Equal("Wooden Sword", request.RootElement.GetProperty("knowledge").GetProperty("referenceSubjects")[0].GetProperty("name").GetString());
            Finished();
            GptAssistantService.Submit("Can I make it with Wood?");
            Require(!GptAssistantService.IsBusy && GptAssistantService.LastAnswer!.Contains("Still need: 2 Wood for Wooden Sword.", StringComparison.Ordinal), "The follow-up selected the named ingredient or old subject as its crafting target.");
            Equal(2, fixture.ReceivedCount);
        }, output);
        Check("An uncraftable pronoun target never receives a local answer for a secondary item's recipe", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            Reply initial = fixture.Reply("Starfury is a general item reference.");
            GptAssistantService.Submit("Describe Starfury."); Arrived(initial); Finished();
            Reply followUp = fixture.Reply("Starfury is found rather than crafted.");
            GptAssistantService.Submit("Can I make it without Wood?");
            using JsonDocument request = JsonDocument.Parse(Arrived(followUp).Body);
            Equal("Can I make it without Wood? (About Starfury)", request.RootElement.GetProperty("question").GetString());
            var recipes = request.RootElement.GetProperty("knowledge").GetProperty("recipes").EnumerateArray().ToArray();
            Require(recipes.Any(recipe => recipe.GetProperty("resultName").GetString() == "Wood"), "Secondary public recipe evidence was removed from remote reasoning.");
            Finished();
            Equal("Starfury is found rather than crafted.", GptAssistantService.LastAnswer);
        }, output);
        Check("An unrelated unnamed topic clears the previous explicit public item", () =>
        {
            using var fixture = new Fixture(workspaceRoot);
            Reply initial = fixture.Reply("Starfury is a general item reference.");
            GptAssistantService.Submit("Describe Starfury."); Arrived(initial); Finished();
            Reply newTopic = fixture.Reply("General weather information.");
            GptAssistantService.Submit("Tell me about world weather."); Arrived(newTopic); Finished();
            Reply followUp = fixture.Reply("Please name the item.");
            GptAssistantService.Submit("Where can I find it?");
            using JsonDocument request = JsonDocument.Parse(Arrived(followUp).Body);
            Equal("Where can I find it?", request.RootElement.GetProperty("question").GetString());
            Equal(0, request.RootElement.GetProperty("knowledge").GetProperty("referenceSubjects").GetArrayLength());
            Finished();
        }, output);
        Check("Each request keeps its selected model while later questions use the new setting", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            Terraria.ModLoader.ModContent.Config.ChatGptModel = "test-model-first";
            Terraria.ModLoader.ModContent.Config.ChatGptEffort = "low";
            Reply first = fixture.Reply("First answer.", gated: true);
            GptAssistantService.Submit("Tell me about flight.");
            Terraria.ModLoader.ModContent.Config.ChatGptModel = "test-model-second";
            Terraria.ModLoader.ModContent.Config.ChatGptEffort = "high";
            using JsonDocument initial = JsonDocument.Parse(Arrived(first).Body);
            Equal("test-model-first", initial.RootElement.GetProperty("chatGptModel").GetString());
            Equal("low", initial.RootElement.GetProperty("chatGptEffort").GetString());
            first.Release.Set(); Finished();
            Reply next = fixture.Reply("Follow-up answer.");
            GptAssistantService.Submit("And which is easiest?");
            using JsonDocument followUp = JsonDocument.Parse(Arrived(next).Body);
            Equal("test-model-second", followUp.RootElement.GetProperty("chatGptModel").GetString());
            Equal("high", followUp.RootElement.GetProperty("chatGptEffort").GetString());
            Equal(2, followUp.RootElement.GetProperty("conversation").GetArrayLength());
            Finished();
        }, output);
        Check("Only completed requests notify, including safe failures; local notices do not", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            GptAssistantService.AddNotice("A local notice.");
            GptAssistantService.Submit("/help");
            Require(GptAssistantService.Messages.All(entry => !entry.NotifiesReply), "A notice was marked as a completed answer.");
            Reply reply = fixture.Reply("Try Rocket Boots.");
            GptAssistantService.Submit("How can I fly?"); Arrived(reply);
            Require(!GptAssistantService.Messages[^1].NotifiesReply, "The user's question produced a reply notification.");
            Finished();
            Require(GptAssistantService.Messages[^1].NotifiesReply, "The completed answer did not produce a notification.");
            Reply error = fixture.Reply("Connection unavailable.", mode: "error");
            GptAssistantService.Submit("What else?"); Arrived(error); Finished();
            Require(GptAssistantService.Messages[^1].NotifiesReply, "A connection failure left the waiting user uninformed.");
            Equal("Connection unavailable.", GptAssistantService.LastAnswer);
        }, output);
        CheckChatOnly(workspaceRoot, output);
        CheckOnlineFeatures(workspaceRoot, output);
        CheckMemory(workspaceRoot, output);
        CheckActionableSorting(workspaceRoot, output);
        GptAssistantService.Reset();
        return output.ToArray();
    }

    private sealed record Request(string Header, string Body);
    private sealed class Reply
    {
        public string Body { get; }
        public ManualResetEventSlim Release { get; }
        public TaskCompletionSource<Request> Arrival { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Reply(string answer, IReadOnlyList<AnswerHighlight>? highlights, bool gated, string? mode)
        {
            Body = JsonSerializer.Serialize(new { answer, mode, highlights = (highlights ?? Array.Empty<AnswerHighlight>()).Select(mark => new { text = mark.Text, kind = mark.Kind }) });
            Release = new ManualResetEventSlim(!gated);
        }
        public Reply(string body, bool gated) { Body = body; Release = new ManualResetEventSlim(!gated); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly ConcurrentQueue<Reply> _replies = new();
        private readonly List<Reply> _allReplies = new();
        private readonly CancellationTokenSource _stop = new();
        private int _received;
        public int ReceivedCount => Volatile.Read(ref _received);
        public Fixture(string workspaceRoot, GptProvider provider = GptProvider.WikiOnly)
        {
            Terraria.Main.SavePath = workspaceRoot; Terraria.Main.gameMenu = false; Terraria.Main.dedServ = false;
            Terraria.Main.LocalPlayer = new();
            Terraria.Main.ActiveWorldFileData = new(); GptKnowledgePlayer.Session = Guid.NewGuid();
            TerrariaChatGPT.Common.Sorting.ChestSortService.RememberCalls = 0;
            TerrariaChatGPT.Common.Sorting.ChestSortService.RememberSucceeds = true;
            TerrariaChatGPT.Common.Sorting.ChestSortService.LastRemembered = null;
            Terraria.Player.ModPlayerReads = 0; Terraria.Player.RejectReads = false; GameReferenceFacts.ResolveCalls = 0;
            GptAssistantService.Reset();
            GptKnowledgePlayer.Snapshot = new KnowledgeSnapshot("PRIVATE-PLAYER", DateTimeOffset.UnixEpoch,
                new[] { new KnowledgeSubject("item", "Terraria/Wood", "PRIVATE-DISCOVERY") },
                new[] { new ItemStackSnapshot("Terraria/Wood", "Wood", 2, "PRIVATE-INVENTORY") },
                new[] { new OwnedChestSnapshot("PRIVATE-CHEST", 40, 50, DateTimeOffset.UnixEpoch,
                    new[] { new ItemStackSnapshot("Terraria/Stone", "Stone", 9, "PRIVATE-CHEST") }) }, new[] { "PRIVATE-BIOME" });
            Terraria.ModLoader.ModContent.Config = new GptClientConfig { Provider = provider, ShareObservedProgress = false };
            NativeAssistantRuntime.Handler = Handle;
        }
        public Reply Reply(string answer, IReadOnlyList<AnswerHighlight>? highlights = null, bool gated = false, string? mode = "chatgpt")
        {
            var reply = new Reply(answer, highlights, gated, mode);
            _allReplies.Add(reply); _replies.Enqueue(reply); return reply;
        }
        public Reply ReplyJson(string body, bool gated = false)
        {
            var reply = new Reply(body, gated); _allReplies.Add(reply); _replies.Enqueue(reply); return reply;
        }
        private Task<NativeChatReply> Handle(NativeServiceRequest request, CancellationToken cancellation) => Task.Run(() =>
        {
            if (!_replies.TryDequeue(out Reply? reply)) throw new InvalidOperationException("No synthetic response queued.");
            Interlocked.Increment(ref _received);
            reply.Arrival.TrySetResult(new Request("In-process fixture", request.ToJson()));
            reply.Release.Wait(cancellation);
            using JsonDocument document = JsonDocument.Parse(reply.Body);
            JsonElement root = document.RootElement;
            string answer = root.GetProperty("answer").GetString() ?? "";
            if (root.TryGetProperty("mode", out var mode) && mode.GetString() == "error")
                throw new NativeChatException("fixture_failure", answer);
            var highlights = new List<AnswerHighlight>();
            if (root.TryGetProperty("highlights", out var marks))
                foreach (JsonElement mark in marks.EnumerateArray())
                    highlights.Add(new(mark.GetProperty("text").GetString()!, mark.GetProperty("kind").GetString()!));
            return new NativeChatReply(answer, highlights, WebCitationLinks.Read(root, answer),
                root.TryGetProperty("searched", out var searched) && searched.ValueKind == JsonValueKind.True, GoalProposalReader.Read(root))
            {
                MemorySuggestions = root.TryGetProperty("memorySuggestions", out _) ?
                    NativeReplyParser.Parse(reply.Body, Array.Empty<NativeSourceAnnotation>(), false).MemorySuggestions :
                    Array.Empty<TerrariaChatGPT.Common.Memory.MemoryProposal>(),
                SortSuggestion = root.TryGetProperty("sortingSuggestion", out _) ?
                    NativeReplyParser.Parse(reply.Body, Array.Empty<NativeSourceAnnotation>(), false).SortSuggestion : null
            };
        }, cancellation);
        public void Dispose()
        {
            Terraria.Player.RejectReads = false;
            GptAssistantService.Reset();
            foreach (Reply reply in _allReplies) reply.Release.Set();
            _stop.Cancel(); NativeAssistantRuntime.Handler = null;
        }
    }
    private static Request Arrived(Reply reply)
    {
        if (!reply.Arrival.Task.Wait(5_000))
        {
            GptAssistantService.Pump();
            throw new InvalidOperationException("The in-process fixture did not receive a request. Busy=" + GptAssistantService.IsBusy + "; answer=" + GptAssistantService.LastAnswer);
        }
        return reply.Arrival.Task.GetAwaiter().GetResult();
    }
    private static void Finished()
    {
        long until = Environment.TickCount64 + 5_000;
        do { GptAssistantService.Pump(); if (!GptAssistantService.IsBusy) return; Thread.Sleep(5); } while (Environment.TickCount64 < until);
        throw new InvalidOperationException("The service did not finish a fake request.");
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(string name, Action test, List<string> output)
    {
        try { test(); output.Add("PASS " + name); }
        catch (Exception error) { throw new InvalidOperationException(name + ": " + error.Message, error); }
    }
}
