#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Goals;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Players;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.Sorting;

namespace TerrariaChatGPT.Tests;

public static partial class AssistantServiceTests
{
    private static readonly (string Key, string Name)[] LooseSortItems = {
        ("Terraria/AmethystHook", "Amethyst Hook"), ("Terraria/MagicMirror", "Magic Mirror"),
        ("Terraria/MoneyTrough", "Money Trough"), ("Terraria/ShadowKey", "Shadow Key"),
        ("Terraria/KiteJellyfishBlue", "Blue Jellyfish Kite"), ("Terraria/DefenderMedal", "Defender Medal") };
    private const string CategoriseRequest = "Categorise them according to my chests";

    private static void PrepareSortingFixture()
    {
        Terraria.ModLoader.ModContent.Config.ShareObservedProgress = true;
        for (int i = 0; i < LooseSortItems.Length; i++)
            Terraria.Main.LocalPlayer.inventory[10 + i] = new Terraria.Item
            { type = 100 + i, stack = 1, Name = LooseSortItems[i].Name, ContentKey = LooseSortItems[i].Key };
        GptKnowledgePlayer.Snapshot = GptKnowledgePlayer.Snapshot with { OwnedChests = new[] {
            new OwnedChestSnapshot("Tools & Travel", 123, 456, DateTimeOffset.UnixEpoch, Array.Empty<ItemStackSnapshot>()),
            new OwnedChestSnapshot("Consumables", 124, 456, DateTimeOffset.UnixEpoch, Array.Empty<ItemStackSnapshot>()) } };
        GptProgressPlayer.Snapshot = new(DateTimeOffset.UnixEpoch,
            Array.Empty<ProgressItem>(), Array.Empty<RememberedProgressItem>(), Array.Empty<ProgressFlag>(),
            Array.Empty<ProgressFlag>(), Array.Empty<ProgressFlag>(), Array.Empty<ProgressSubject>(),
            Array.Empty<NpcProgress>(), Array.Empty<string>());
    }

    private static string SortReply(IEnumerable<SortAssignment>? assignments = null, string answer = "Prepared six item types. Review sort to check the moves.") =>
        JsonSerializer.Serialize(new { answer, memorySuggestions = Array.Empty<object>(),
            sortingSuggestion = new { assignments = (assignments ?? LooseSortItems.Select((item, index) =>
                new SortAssignment(item.Key, index == 5 ? "Consumables" : "Tools & Travel")))
                .Select(row => new { itemKey = row.ItemKey, chestName = row.ChestName }).ToArray() } });

    private static SortSuggestion ReadySort(Fixture fixture)
    {
        Reply reply = fixture.ReplyJson(SortReply());
        GptAssistantService.Submit(CategoriseRequest); Arrived(reply); Finished();
        return GptAssistantService.LatestSortSuggestion ?? throw new InvalidOperationException("No current sort suggestion.");
    }

    private static void CheckActionableSorting(string workspaceRoot, List<string> output)
    {
        Check("Exact categorisation request carries six actionable destinations into the chat entry and latest preview", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            Reply reply = fixture.ReplyJson(SortReply(), gated: true);
            GptAssistantService.Submit(CategoriseRequest);
            using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
            Equal(CategoriseRequest, request.RootElement.GetProperty("question").GetString());
            var observations = request.RootElement.GetProperty("observedContext");
            Equal(6, observations.GetProperty("sortingCandidates").GetArrayLength());
            Equal(2, observations.GetProperty("knownChests").GetArrayLength());
            Require(!observations.GetRawText().Contains("123", StringComparison.Ordinal)
                && !observations.GetRawText().Contains("456", StringComparison.Ordinal), "Chest coordinates escaped.");
            Require(GptAssistantService.LatestSortSuggestion is null, "A pending reply became an actionable plan.");
            reply.Release.Set(); Finished();
            SortSuggestion plan = GptAssistantService.LatestSortSuggestion!;
            Equal(6, plan.Assignments.Count);
            Require(ReferenceEquals(plan, GptAssistantService.Messages[^1].SortSuggestion), "The displayed reply and header preview use different routing.");
            Require(GptAssistantService.Messages[^2].SortSuggestion is null, "The user message became an action.");
            Equal("Tools & Travel", plan.Assignments[0].ChestName); Equal("Consumables", plan.Assignments[5].ChestName);
            Equal(0, ChestSortService.RememberCalls); Equal(0, Terraria.Main.LocalPlayer.Preferences.AppliedCount);
        }, output);
        Check("Sorting candidate capture excludes protected slots, favorites, coins and empty items while deduplicating keys", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            var inventory = Terraria.Main.LocalPlayer.inventory;
            inventory[0] = new() { type = 201, stack = 1, Name = "Hotbar marker" };
            inventory[16] = new() { type = 202, stack = 1, Name = "Favorite marker", favorited = true };
            inventory[17] = new() { type = 203, stack = 1, Name = "Coin marker", IsACoin = true };
            inventory[18] = new() { type = 204, stack = 0, Name = "Empty marker" };
            inventory[19] = new() { type = 100, stack = 7, Name = "Amethyst Hook", ContentKey = "Terraria/AmethystHook" };
            inventory[50] = new() { type = 205, stack = 1, Name = "Coin slot marker" };
            inventory[54] = new() { type = 206, stack = 1, Name = "Ammo marker" };
            inventory[58] = new() { type = 207, stack = 1, Name = "Cursor marker" };
            Reply reply = fixture.ReplyJson(SortReply()); GptAssistantService.Submit(CategoriseRequest);
            using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
            JsonElement candidates = request.RootElement.GetProperty("observedContext").GetProperty("sortingCandidates");
            Equal(6, candidates.GetArrayLength());
            foreach (JsonElement row in candidates.EnumerateArray()) Equal(2, row.EnumerateObject().Count());
            Require(!candidates.GetRawText().Contains("marker", StringComparison.Ordinal), "A protected or empty item became a candidate.");
            Finished(); Equal(6, GptAssistantService.LatestSortSuggestion!.Assignments.Count);
        }, output);
        Check("Model assignments are bound to the captured candidate keys and known chest names", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            var rows = new[] { new SortAssignment("Terraria/AmethystHook", "Tools & Travel"),
                new SortAssignment("Terraria/MagicMirror", "Invented chest"),
                new SortAssignment("Terraria/Minishark", "Tools & Travel") };
            Reply reply = fixture.ReplyJson(SortReply(rows)); GptAssistantService.Submit(CategoriseRequest);
            Arrived(reply); Finished();
            Equal(1, GptAssistantService.LatestSortSuggestion!.Assignments.Count);
            Equal("Terraria/AmethystHook", GptAssistantService.LatestSortSuggestion.Assignments[0].ItemKey);
        }, output);
        Check("Captured sorting identities remain detached while the online request is in flight", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            Reply reply = fixture.ReplyJson(SortReply(), gated: true); GptAssistantService.Submit(CategoriseRequest); Arrived(reply);
            Terraria.Main.LocalPlayer.inventory[10].ContentKey = "Terraria/ChangedAfterRequest";
            GptKnowledgePlayer.Snapshot = GptKnowledgePlayer.Snapshot with { OwnedChests = Array.Empty<OwnedChestSnapshot>() };
            reply.Release.Set(); Finished();
            Equal(6, GptAssistantService.LatestSortSuggestion!.Assignments.Count);
            Equal("Terraria/AmethystHook", GptAssistantService.LatestSortSuggestion.Assignments[0].ItemKey);
            // Local preview still rechecks actual current slots/chests; this only
            // checks that background data could not mutate the request allowlist.
        }, output);
        Check("Ordinary and explicitly negated questions cannot attach unsolicited sorting metadata", () =>
        {
            foreach (string question in new[] { "What does the Magic Mirror do?", "How does Sort chests work?", "Don't sort my items.",
                "Explain how sorting works.", "Tell me about sorting.", "I don't want to sort anything." })
            {
                using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
                Reply reply = fixture.ReplyJson(SortReply()); GptAssistantService.Submit(question);
                using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
                Equal(0, request.RootElement.GetProperty("observedContext").GetProperty("sortingCandidates").GetArrayLength());
                Finished(); Require(GptAssistantService.LatestSortSuggestion is null && GptAssistantService.Messages[^1].SortSuggestion is null, "Unrequested sorting metadata entered the UI.");
            }
        }, output);
        Check("Natural classification requests and contextual corrections request fresh actionable routing", () =>
        {
            foreach (string question in new[] { "Classify these items according to my chests.", "Recategorize my items.", "What chest should these go in?" })
            {
                using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
                Reply reply = fixture.ReplyJson(SortReply()); GptAssistantService.Submit(question);
                using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
                Equal(6, request.RootElement.GetProperty("observedContext").GetProperty("sortingCandidates").GetArrayLength());
                Finished(); Equal(6, GptAssistantService.LatestSortSuggestion!.Assignments.Count);
            }
            foreach (string question in new[] { "Actually put those in Tools & Travel", "Use Tools & Travel instead", "No, put the mirror into Travel instead",
                "Change those to Tools & Travel", "Switch them to Consumables", "No, the mirror should go in Travel instead",
                "Don't put the mirror into Consumables; put it into Travel instead" })
            {
                using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
                SortSuggestion previous = ReadySort(fixture);
                Reply reply = fixture.ReplyJson(SortReply(new[] { new SortAssignment("Terraria/MagicMirror", "Tools & Travel") }));
                GptAssistantService.Submit(question);
                using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
                Equal(6, request.RootElement.GetProperty("observedContext").GetProperty("sortingCandidates").GetArrayLength());
                Finished(); Equal(1, GptAssistantService.LatestSortSuggestion!.Assignments.Count);
                Require(!GptAssistantService.IsCurrentSortSuggestion(previous), "Correction retained its previous batch.");
            }
        }, output);
        Check("Explicit sorting cancellation retires an already prepared batch and its remember action", () =>
        {
            foreach (string question in new[] { "Never mind", "Stop sorting", "Cancel sorting", "I don't want to sort anything.", "Do not move those into storage" })
            {
                using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
                SortSuggestion previous = ReadySort(fixture);
                Reply reply = fixture.Reply("Okay.", gated: true); GptAssistantService.Submit(question); Arrived(reply);
                Require(GptAssistantService.LatestSortSuggestion is null && !GptAssistantService.IsCurrentSortSuggestion(previous), "Cancellation kept an older batch active.");
                Require(!GptAssistantService.RememberSortChoices(previous, GptAssistantService.Epoch, out _), "Cancelled choices could be saved.");
                Equal(0, ChestSortService.RememberCalls); reply.Release.Set(); Finished();
                Require(GptAssistantService.LatestSortSuggestion is null, "Cancellation answer restored old routing.");
            }
        }, output);
        Check("A prose-only answer to a valid sorting request explicitly reports the absent usable plan", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            Reply reply = fixture.Reply("I would put those items in Tools & Travel and Consumables.");
            GptAssistantService.Submit(CategoriseRequest); Arrived(reply); Finished();
            Require(GptAssistantService.LastAnswer!.Contains("No usable GPT sorting plan was returned.", StringComparison.Ordinal), "Prose-only suggestion masqueraded as actionable routing.");
            Require(GptAssistantService.LastAnswer.Contains("Nothing moved.", StringComparison.Ordinal), "The user cannot tell whether any transfer occurred.");
            Require(GptAssistantService.LatestSortSuggestion is null && GptAssistantService.Messages[^1].SortSuggestion is null, "Missing metadata produced an action.");
            Equal(0, ChestSortService.RememberCalls);
        }, output);
        Check("Unrelated follow-ups and cancelled sorting do not inherit action scope from an old categorisation", () =>
        {
            foreach (string question in new[] { "What does Magic Mirror do?", "Explain how sorting works.", "Never mind", "Stop sorting", "Do not move those into storage" })
            {
                using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
                SortSuggestion previous = ReadySort(fixture);
                Reply reply = fixture.ReplyJson(SortReply()); GptAssistantService.Submit(question);
                using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
                Equal(0, request.RootElement.GetProperty("observedContext").GetProperty("sortingCandidates").GetArrayLength());
                Finished(); Require(GptAssistantService.Messages[^1].SortSuggestion is null, "Unrelated/negated follow-up acquired an action.");
            }
        }, output);
        Check("Disabled observation sharing performs no character reads and refuses a remote sorting plan", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            Terraria.ModLoader.ModContent.Config.ShareObservedProgress = false; Terraria.Player.RejectReads = true;
            Reply reply = fixture.ReplyJson(SortReply()); GptAssistantService.Submit(CategoriseRequest);
            using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
            Equal(JsonValueKind.Null, request.RootElement.GetProperty("observedContext").ValueKind);
            Finished(); Equal(0, Terraria.Player.ModPlayerReads);
            Require(GptAssistantService.LatestSortSuggestion is null && GptAssistantService.Messages[^1].SortSuggestion is null, "Sharing-off request accepted routing.");
        }, output);
        Check("World and session changes reject pending routing metadata without claiming it is actionable", () =>
        {
            foreach (bool changeWorld in new[] { true, false })
            {
                using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
                Reply reply = fixture.ReplyJson(SortReply(), gated: true); GptAssistantService.Submit(CategoriseRequest); Arrived(reply);
                if (changeWorld) Terraria.Main.ActiveWorldFileData.UniqueId = Guid.NewGuid(); else GptKnowledgePlayer.Session = Guid.NewGuid();
                reply.Release.Set(); Finished();
                Require(GptAssistantService.LatestSortSuggestion is null && GptAssistantService.Messages.All(row => row.SortSuggestion is null), "Previous world/session routing survived.");
            }
        }, output);
        Check("Already displayed sorting choices expire on epoch, player, world, session and menu changes", () =>
        {
            foreach (string change in new[] { "epoch", "player", "world", "session", "menu" })
            {
                using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
                SortSuggestion plan = ReadySort(fixture);
                switch (change)
                {
                    case "epoch": GptAssistantService.Reset(); break;
                    case "player": Terraria.Main.LocalPlayer = new(); break;
                    case "world": Terraria.Main.ActiveWorldFileData.UniqueId = Guid.NewGuid(); break;
                    case "session": GptKnowledgePlayer.Session = Guid.NewGuid(); break;
                    case "menu": Terraria.Main.gameMenu = true; break;
                }
                Require(GptAssistantService.LatestSortSuggestion is null && !GptAssistantService.IsCurrentSortSuggestion(plan), "Stale " + change + " plan remained actionable.");
                Require(!GptAssistantService.RememberSortChoices(plan, GptAssistantService.Epoch, out _), "Stale choices could be remembered.");
                Equal(0, ChestSortService.RememberCalls);
            }
        }, output);
        Check("A new sorting request retires the older routing even if its answer supplies no usable plan", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            SortSuggestion previous = ReadySort(fixture);
            Reply next = fixture.Reply("No suitable destination is available.", gated: true);
            GptAssistantService.Submit("Categorise my Magic Mirror according to my chests"); Arrived(next);
            Require(GptAssistantService.LatestSortSuggestion is null && !GptAssistantService.IsCurrentSortSuggestion(previous), "Old plan remained available during a replacement request.");
            next.Release.Set(); Finished(); Require(GptAssistantService.LatestSortSuggestion is null, "Failed replacement restored old routing.");
        }, output);
        Check("Remember choices is explicit, supports more than three assignments and attaches an Undo receipt", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            SortSuggestion plan = ReadySort(fixture); int epoch = GptAssistantService.Epoch;
            Equal(0, ChestSortService.RememberCalls); Equal(0, Terraria.Main.LocalPlayer.Preferences.Entries.Count);
            Require(GptAssistantService.RememberSortChoices(plan, epoch, out string message), "Explicit reviewed memory was rejected.");
            Equal(1, ChestSortService.RememberCalls); Equal(6, ChestSortService.LastRemembered!.Assignments.Count);
            ChatEntry receipt = GptAssistantService.Messages[^1]; Equal(message, receipt.Text);
            Equal("reviewed-sort-memory", receipt.MemoryChangeId); Require(!receipt.NotifiesReply, "Local save created an online-reply notification.");
            GptAssistantService.Pump(); Equal(epoch, GptAssistantService.Epoch);
            Require(GptAssistantService.IsCurrentSortSuggestion(plan), "The host invalidated the preview after its own explicit save.");
            Require(Terraria.Main.LocalPlayer.Preferences.Undo(receipt.MemoryChangeId!, out _), "Receipt did not identify an undoable save.");
            GptAssistantService.Pump(); Equal(0, Terraria.Main.LocalPlayer.Preferences.Entries.Count);
            Require(!GptAssistantService.IsCurrentSortSuggestion(plan), "Undo left old remembered routing actionable.");
        }, output);
        Check("Failed Remember choices keeps the answer and creates no false memory receipt", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            SortSuggestion plan = ReadySort(fixture); ChestSortService.RememberSucceeds = false;
            int messages = GptAssistantService.Messages.Count;
            Require(!GptAssistantService.RememberSortChoices(plan, GptAssistantService.Epoch, out _), "Failed backend save reported success.");
            Equal(messages, GptAssistantService.Messages.Count);
            Require(GptAssistantService.Messages.All(row => row.MemoryChangeId is null), "Failed save attached Undo receipt.");
        }, output);
        Check("Completing the reviewed suggestion consumes only that exact current batch", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            SortSuggestion plan = ReadySort(fixture);
            GptAssistantService.CompleteSortSuggestion(new(plan.Assignments));
            Require(GptAssistantService.IsCurrentSortSuggestion(plan), "A reconstructed stale plan consumed current routing.");
            GptAssistantService.CompleteSortSuggestion(plan);
            Require(GptAssistantService.LatestSortSuggestion is null && !GptAssistantService.IsCurrentSortSuggestion(plan), "Completed plan remained reusable.");
            Require(!GptAssistantService.RememberSortChoices(plan, GptAssistantService.Epoch, out _), "Consumed plan could still save memories.");
            Equal(0, ChestSortService.RememberCalls);
        }, output);
        string livePath = Path.Combine(workspaceRoot, ".build", "native-live-actionablesorting.json");
        if (File.Exists(livePath)) Check("Captured live six-item reply becomes the same six local preview assignments", () =>
        {
            using JsonDocument live = JsonDocument.Parse(File.ReadAllText(livePath));
            SortAssignment[] assignments = live.RootElement.GetProperty("SortSuggestion").GetProperty("Assignments").EnumerateArray()
                .Select(row => new SortAssignment(row.GetProperty("ItemKey").GetString()!, row.GetProperty("ChestName").GetString()!)).ToArray();
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT); PrepareSortingFixture();
            Reply reply = fixture.ReplyJson(SortReply(assignments, live.RootElement.GetProperty("Answer").GetString()!));
            GptAssistantService.Submit(CategoriseRequest); Arrived(reply); Finished();
            Equal(6, GptAssistantService.LatestSortSuggestion!.Assignments.Count);
            Require(assignments.SequenceEqual(GptAssistantService.LatestSortSuggestion.Assignments), "Live routing changed during service integration.");
            Equal(0, ChestSortService.RememberCalls); Equal(0, Terraria.Main.LocalPlayer.Preferences.AppliedCount);
        }, output);
    }
}
