#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Native;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Tests;

// Explicit live acceptance only. Uses the native production runtime, no API key,
// private game observations, browser launches, legacy helper or loopback bridge.
// Happiness uses explicitly fabricated NPC readings; it never reads a world save.
public static class NativeLiveAcceptance
{
    public static readonly System.Collections.Generic.List<object> MemorySortingReplies = new();
    public static readonly System.Collections.Generic.List<object> ActionableSortingReplies = new();
    public static async Task<string> InspectCatalogAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var session = new NativeChatGptSession();
        string token = await session.GetAccessTokenAsync(timeout.Token);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        byte[] bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, timeout.Token)) > 0)
        {
            if (buffer.Length + count > 4 * 1024 * 1024) return "Catalog exceeded 4 MiB; no content logged.";
            buffer.Write(bytes, 0, count);
        }
        using JsonDocument body = JsonDocument.Parse(buffer.ToArray());
        return JsonSerializer.Serialize(new { Status = (int)response.StatusCode,
            DeclaredBytes = response.Content.Headers.ContentLength, ActualBytes = buffer.Length,
            Models = body.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array ? models.GetArrayLength() : -1 });
    }
    public static async Task<NativeChatReply> RunAsync(string phase)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(phase.StartsWith("Memory", StringComparison.Ordinal) || phase == "ActionableSorting" ? 360 : 160));
        try
        {
            NativeAssistantRuntime.Initialize();
            ChatGptConnectionStatus status = await NativeAssistantRuntime.ConnectionAsync(ChatGptConnectionAction.Status, timeout.Token);
            if (!status.Connected || !status.UsingPlan || status.WelcomePending || status.IsError)
                throw new NativeChatException("acceptance_connection", "The saved account is not ready for a live check: " + status.Message);
            string question = phase switch
            {
                "Recipe" => "what can i make from blade of grass",
                "Sorting" => "Can you put my inventory items into the appropriate nearby chests based on their names? Keep my hotbar and favorites. If an item has no appropriate chest, leave it and recommend a chest name. Tell me exactly which buttons I should click in this mod.",
                "Happiness" => "How happy are my Merchant and Goblin Tinkerer? Give each one's price modifier and discount or markup, explain what I should change to improve them, and say which Terraria and tModLoader versions I'm playing. Would a Love Potion improve their prices in my version? Verify that last part if needed.",
                _ => "Search the official Terraria wiki to verify how I get Rocket Boots in Terraria 1.4.4. I want to get Rocket Boots; offer me a goal for that."
            };
            var snapshot = new KnowledgeSnapshot("", DateTimeOffset.UtcNow, Array.Empty<KnowledgeSubject>(),
                Array.Empty<ItemStackSnapshot>(), Array.Empty<OwnedChestSnapshot>(), Array.Empty<string>())
                { InventoryShared = false, GameVersion = "1.4.4.9", TModLoaderVersion = "2026.8.3.0" };
            if (phase == "Memory") return await CheckMemoryAsync(snapshot, timeout.Token);
            if (phase == "MemorySorting") return await CheckMemorySortingAsync(snapshot, timeout.Token);
            if (phase == "ActionableSorting") return await CheckActionableSortingAsync(snapshot, timeout.Token);
            object? observations = phase == "Happiness" ? new
            {
                enabled = true,
                npcHappiness = new[] { new {
                    key = "Terraria/Merchant", name = "Merchant", priceAdjustment = 0.89,
                    happinessReport = "I like the Forest. I enjoy having space to myself.",
                    lastObservedUtc = DateTimeOffset.UtcNow.AddDays(-1).ToString("O"), isCurrent = false,
                    observedBiomes = new[] { "Terraria/Forest" }
                } }
            } : null;
            NativeChatReply reply = await NativeAssistantRuntime.AskAsync(question, snapshot, Array.Empty<ConversationTurn>(),
                GptProvider.ChatGPT, true, observations, timeout.Token);
            if (string.IsNullOrWhiteSpace(reply.Answer) || (phase != "Sorting" && reply.Highlights.Count == 0))
                throw new NativeChatException("acceptance_answer", "The live reply did not include a readable, highlighted answer.");
            if (phase == "WebGoal" && (!reply.WebSearched || reply.Citations.Count == 0 || reply.GoalSuggestion is null))
                throw new NativeChatException("acceptance_features", "The live reply did not demonstrate all of web search, citations and a goal suggestion.");
            if (phase == "Happiness" && (!reply.Answer.Contains("89", StringComparison.Ordinal)
                || !reply.Answer.Contains("11", StringComparison.Ordinal) || !reply.Answer.Contains("1.4.4", StringComparison.Ordinal)))
                throw new NativeChatException("acceptance_happiness", "The live reply did not preserve the supplied NPC price arithmetic and game version.");
            if (phase == "Sorting" && (!reply.Answer.Contains("Sort chests", StringComparison.OrdinalIgnoreCase)
                || !reply.Answer.Contains("Confirm sort", StringComparison.OrdinalIgnoreCase) || reply.GoalSuggestion is not null))
                throw new NativeChatException("acceptance_sorting", "The live reply did not explain the preview and confirmation buttons, or incorrectly proposed a goal.");
            return reply;
        }
        finally { NativeAssistantRuntime.Shutdown(); }
    }

    private static async Task<NativeChatReply> CheckMemorySortingAsync(KnowledgeSnapshot snapshot, CancellationToken token)
    {
        MemorySortingReplies.Clear();
        // All names and preferences here are synthetic. No character is loaded or changed.
        var known = new { enabled = true, knownChests = new[] {
            new { name = "Vanity & Paints" }, new { name = "Travel & Mobility" }, new { name = "Equipment" } } };
        var empty = new { enabled = true, entries = Array.Empty<object>() };
        async Task<NativeChatReply> Ask(string question, object memory)
        {
            NativeChatReply reply = await NativeAssistantRuntime.AskAsync(question, snapshot, Array.Empty<ConversationTurn>(),
                GptProvider.ChatGPT, false, known, token, savedMemory: memory);
            MemorySortingReplies.Add(new { question, reply });
            if (string.IsNullOrWhiteSpace(reply.Answer) || reply.GoalSuggestion is not null)
                throw new NativeChatException("acceptance_memory_sorting", "Sorting preference lost its answer or created an unsolicited goal.");
            return reply;
        }
        NativeChatReply goodie = await Ask("sort my goodie bag into vanity", empty);
        if (!goodie.MemorySuggestions.Any(row => row.Kind == "sorting" && row.Item.Contains("Goodie", StringComparison.OrdinalIgnoreCase)
            && row.Target == "Vanity") || !goodie.Answer.Contains("Vanity & Paints", StringComparison.OrdinalIgnoreCase))
            throw new NativeChatException("acceptance_memory_vanity", "Goodie Bag was not categorized as Vanity using the known combined chest label.");
        var stored = new { enabled = true, entries = new[] {
            new { kind = "sorting", topic = "", text = "Sort Goodie Bag as Vanity.", itemKey = "Terraria/GoodieBag", target = "Vanity" } } };
        NativeChatReply changed = await Ask("Actually, change that sorting preference to Consumables instead.", stored);
        if (changed.MemorySuggestions.Count != 1 || !changed.MemorySuggestions[0].Item.Contains("Goodie", StringComparison.OrdinalIgnoreCase)
            || changed.MemorySuggestions[0].Target != "Consumable")
            throw new NativeChatException("acceptance_memory_revision", "An unambiguous saved rule could not be revised with no prior conversation.");
        NativeChatReply hooks = await Ask("sort my grappling hooks into travel", empty);
        if (hooks.MemorySuggestions.Count != 1 || hooks.MemorySuggestions[0].Item != "group:grappling_hooks"
            || hooks.MemorySuggestions[0].Target != "Travel")
            throw new NativeChatException("acceptance_memory_group", "Grappling hooks were not interpreted as an item family with the Travel category.");
        NativeChatReply potions = await Ask("sort all my potions into consumables", empty);
        if (potions.MemorySuggestions.Count != 1 || potions.MemorySuggestions[0].Item != "group:potion"
            || potions.MemorySuggestions[0].Target != "Consumable")
            throw new NativeChatException("acceptance_memory_family", "The potion-family request did not remain a potion group rule.");
        NativeChatReply negated = await Ask("Don't sort Goodie Bag into vanity.", stored);
        if (negated.MemorySuggestions.Count != 0)
            throw new NativeChatException("acceptance_memory_negation", "A negated sorting destination became a proposed memory.");
        var style = new { enabled = true, entries = new[] {
            new { kind = "preference", topic = "response_style", text = "Prefers concise answers.", itemKey = "", target = "" } } };
        NativeChatReply updated = await Ask("Update my answer style preference to also use bullet points.", style);
        if (updated.MemorySuggestions.Count != 1 || updated.MemorySuggestions[0].Topic != "response_style"
            || !updated.MemorySuggestions[0].Text.Contains("bullet", StringComparison.OrdinalIgnoreCase)
            || !(updated.MemorySuggestions[0].Text.Contains("concise", StringComparison.OrdinalIgnoreCase)
                || updated.MemorySuggestions[0].Text.Contains("short", StringComparison.OrdinalIgnoreCase)))
            throw new NativeChatException("acceptance_memory_update", "Preference update lost existing compatible style details.");
        return hooks;
    }

    private static async Task<NativeChatReply> CheckActionableSortingAsync(KnowledgeSnapshot snapshot, CancellationToken token)
    {
        ActionableSortingReplies.Clear();
        // Fabricated request data reproduces the user's six loose item types.
        // Protected hotbar/equipment are deliberately absent from candidates.
        var candidates = new[] {
            new { key = "Terraria/AmethystHook", name = "Amethyst Hook" },
            new { key = "Terraria/MagicMirror", name = "Magic Mirror" },
            new { key = "Terraria/MoneyTrough", name = "Money Trough" },
            new { key = "Terraria/ShadowKey", name = "Shadow Key" },
            new { key = "Terraria/KiteJellyfishBlue", name = "Blue Jellyfish Kite" },
            new { key = "Terraria/DefenderMedal", name = "Defender Medal" } };
        string[] chestNames = { "Tools & Travel", "Consumables", "Weapons", "Potions & Food", "Ammo & Explosives & Consumables", "Furniture & Stations" };
        var known = new { enabled = true, sortingCandidates = candidates,
            knownChests = chestNames.Select(name => new { name }).ToArray(),
            inventory = new[] {
                new { key = "Terraria/Minishark", name = "Minishark", count = 1, location = "Hotbar", canUseForCrafting = true },
                new { key = "Terraria/NightsEdge", name = "Night's Edge", count = 1, location = "Hotbar", canUseForCrafting = true },
                new { key = "Terraria/Torch", name = "Torch", count = 50, location = "Hotbar", canUseForCrafting = true } } };
        var empty = new { enabled = true, entries = Array.Empty<object>() };
        async Task<NativeChatReply> Ask(string question, ConversationTurn[] history)
        {
            NativeChatReply reply = await NativeAssistantRuntime.AskAsync(question, snapshot, history,
                GptProvider.ChatGPT, false, known, token, savedMemory: empty);
            ActionableSortingReplies.Add(new { question, history, observed = known, reply });
            if (string.IsNullOrWhiteSpace(reply.Answer) || reply.GoalSuggestion is not null || reply.MemorySuggestions.Count != 0)
                throw new NativeChatException("acceptance_sort_proposal", "Sorting lost its answer, created an unsolicited goal or tried to save AI choices as preferences.");
            if (reply.SortSuggestion is not null && reply.SortSuggestion.Assignments.Any(row => !candidates.Any(item => item.key == row.ItemKey)
                || !chestNames.Contains(row.ChestName, StringComparer.Ordinal)))
                throw new NativeChatException("acceptance_sort_scope", "Sorting proposed an item or chest outside its observed allowlist.");
            return reply;
        }
        var context = new[] {
            new ConversationTurn("user", "The loose items are Amethyst Hook, Magic Mirror, Money Trough, Shadow Key, Blue Jellyfish Kite and Defender Medal."),
            new ConversationTurn("assistant", "Those six loose item types can be grouped into your storage.") };
        NativeChatReply broad = await Ask("Categorise them according to my chests", context);
        if (broad.SortSuggestion?.Assignments.Count != 6
            || !broad.Answer.Contains("Review sort", StringComparison.OrdinalIgnoreCase))
            throw new NativeChatException("acceptance_sort_batch", "The exact reported request did not prepare all six loose types with a review button instruction.");
        // Vague reference to a specific earlier subset must not categorise the
        // other five eligible types just because they are in the snapshot.
        NativeChatReply subset = await Ask("Categorise it according to my chests", new[] {
            new ConversationTurn("user", "I only want to organise my Amethyst Hook right now.") });
        if (subset.SortSuggestion?.Assignments.Count != 1 || subset.SortSuggestion.Assignments[0].ItemKey != "Terraria/AmethystHook")
            throw new NativeChatException("acceptance_sort_subset", "The pronoun request widened the explicit one-item subset.");
        NativeChatReply information = await Ask("What does the Magic Mirror do?", context);
        if (information.SortSuggestion is not null)
            throw new NativeChatException("acceptance_sort_information", "An ordinary item question created an unsolicited sorting batch.");
        return broad;
    }

    private static async Task<NativeChatReply> CheckMemoryAsync(KnowledgeSnapshot snapshot, CancellationToken token)
    {
        // Synthetic preferences only. This path never constructs a player or a
        // memory ledger and therefore cannot write a character's saved memories.
        const string firstQuestion = "For future chats, I prefer concise bullet-point answers. For sorting, I want Gel in Material.";
        var empty = new { enabled = true, entries = Array.Empty<object>() };
        NativeChatReply first = await NativeAssistantRuntime.AskAsync(firstQuestion, snapshot, Array.Empty<ConversationTurn>(),
            GptProvider.ChatGPT, false, null, token, savedMemory: empty);
        if (!first.MemorySuggestions.Any(row => row.Kind == "preference" && row.Topic == "response_style")
            || !first.MemorySuggestions.Any(row => row.Kind == "sorting" && row.Item.Contains("Gel", StringComparison.OrdinalIgnoreCase) && row.Target == "Material"))
            throw new NativeChatException("acceptance_memory_initial", "The synthetic first turn did not recognize the durable style and sorting preferences.");
        var context = new { enabled = true, entries = new object[] {
            new { kind = "preference", topic = "response_style", text = "Prefers concise bullet-point answers.", itemKey = "", target = "" },
            new { kind = "sorting", topic = "", text = "Sort Gel as Material.", itemKey = "Terraria/Gel", target = "Material" } } };
        const string correction = "Actually, classify it as Block for future sorting instead. Also, I have 87 Gel right now.";
        NativeChatReply changed = await NativeAssistantRuntime.AskAsync(correction, snapshot,
            new[] { new ConversationTurn("user", firstQuestion), new ConversationTurn("assistant", first.Answer) },
            GptProvider.ChatGPT, false, null, token, savedMemory: context);
        if (changed.MemorySuggestions.Count != 1 || changed.MemorySuggestions[0].Kind != "sorting"
            || !changed.MemorySuggestions[0].Item.Contains("Gel", StringComparison.OrdinalIgnoreCase) || changed.MemorySuggestions[0].Target != "Block"
            || changed.MemorySuggestions[0].Text.Contains("87", StringComparison.Ordinal))
            throw new NativeChatException("acceptance_memory_correction", "The contextual correction did not isolate the durable Gel category change from its temporary count.");
        NativeChatReply volatileReply = await NativeAssistantRuntime.AskAsync(
            "I currently have 87 Gel and just defeated King Slime. Could you suggest what to do next?", snapshot, Array.Empty<ConversationTurn>(),
            GptProvider.ChatGPT, false, null, token, savedMemory: context);
        if (volatileReply.MemorySuggestions.Count != 0 || string.IsNullOrWhiteSpace(volatileReply.Answer))
            throw new NativeChatException("acceptance_memory_volatile", "Temporary inventory or progress was incorrectly proposed as durable memory.");
        return changed;
    }
}
