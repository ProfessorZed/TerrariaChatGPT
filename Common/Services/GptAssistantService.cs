#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Terraria;
using Terraria.ModLoader;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Players;
using TerrariaChatGPT.Common.Goals;
using TerrariaChatGPT.Common.Native;

namespace TerrariaChatGPT.Common.Services;

public sealed record ChatEntry(string Role, string Text, DateTimeOffset Timestamp)
{
    public IReadOnlyList<AnswerHighlight> Highlights { get; init; } = Array.Empty<AnswerHighlight>();
    public IReadOnlyList<WebCitation> Citations { get; init; } = Array.Empty<WebCitation>();
    public bool WebSearched { get; init; }
    public GoalProposal? GoalSuggestion { get; init; }
    public bool NotifiesReply { get; init; }
}

/// <summary>UI/game-thread controller. Only explicitly projected observations leave the game thread.</summary>
public static class GptAssistantService
{
    private static readonly List<ChatEntry> History = new();
    // Only successful AI exchanges enter this buffer. Display history may also
    // contain legacy wiki inventory answers, notices and transport errors.
    private static readonly List<ConversationTurn> ChatHistory = new();
    private static readonly IReadOnlyList<ChatEntry> ReadOnlyHistory = History.AsReadOnly();
    private sealed record CompletedAnswer(int Epoch, string Text, IReadOnlyList<AnswerHighlight> Highlights,
        bool ChatOnly, string? Question, IReadOnlyList<WebCitation> Citations, bool WebSearched, GoalProposal? GoalSuggestion);
    private static readonly ConcurrentQueue<CompletedAnswer> Completed = new();
    private static CancellationTokenSource _cancellation = new();
    private static int _epoch;
    private static string? _subject;
    private static GptProvider? _conversationProvider;
    private static bool? _sharedProgress;
    private static bool? _onlineAnswers;
    public static int Epoch => _epoch;
    public static IReadOnlyList<ChatEntry> Messages => ReadOnlyHistory;
    public static string? LastAnswer => History.LastOrDefault(entry => entry.Role == "assistant")?.Text;
    public static bool IsBusy { get; private set; }

    public static void Submit(string question)
    {
        question = question.Trim();
        if (Main.gameMenu || Main.dedServ || question.Length == 0) return;
        if (question.Equals("/clear", StringComparison.OrdinalIgnoreCase)) { Reset(); return; }
        GptClientConfig config = ModContent.GetInstance<GptClientConfig>();
        ObserveOptions(config);
        if (question.Length > 1000) { Add("GPT", "Please keep each question under 1,000 characters."); return; }
        if (IsBusy) return;
        Add("You", question);
        // These are explicit UI commands. Ordinary questions, including "help",
        // go to the selected AI without item matching or template interception.
        if (config.Provider != GptProvider.WikiOnly)
        {
            if (question.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                Add("GPT", "Chat naturally, ask for a web lookup, or describe a goal and choose Set goal? under the answer. Goals update from recorded progress and your observed items. Hidden locations and unopened storage are unavailable. Turn off progress sharing in Mod Configuration to keep observations local. /clear starts a new conversation.");
                return;
            }
            if (!config.EnableOnlineAnswers)
            {
                Add("GPT", "Online answers are disabled in Mod Configuration.");
                return;
            }
            var emptyKnowledge = new KnowledgeSnapshot("", DateTimeOffset.UtcNow,
                Array.Empty<KnowledgeSubject>(), Array.Empty<ItemStackSnapshot>(),
                Array.Empty<OwnedChestSnapshot>(), Array.Empty<string>())
            {
                InventoryShared = false,
                GameVersion = Main.versionNumber
            };
            object? observedContext = config.ShareObservedProgress
                ? ObservedContextBuilder.Build(Main.LocalPlayer.GetModPlayer<GptProgressPlayer>().CreateSnapshot(), question)
                : null;
            StartRequest(question, emptyKnowledge, ChatConversationContext.HistoryForRequest(ChatHistory), config, chatOnly: true, observedContext);
            return;
        }
        GptKnowledgePlayer knowledge = Main.LocalPlayer.GetModPlayer<GptKnowledgePlayer>();
        switch (question.ToLowerInvariant())
        {
            case "/help": case "help":
                Add("GPT", "Ask any Terraria wiki question, or ‘Do I have Wood?’ for inventory help. In single-player, chests are remembered automatically when you open them. Reopen a chest to refresh its contents. /forget stops remembering that open chest, and /claim resumes it. /clear clears this chat. I cannot scan hidden locations or change your game."); return;
            case "/claim": case "/gptclaim": Add("GPT", knowledge.ClaimOpenChest()); return;
            case "/forget": case "/gptforget": Add("GPT", knowledge.ForgetOpenChest()); return;
        }

        KnowledgeSnapshot snapshot = knowledge.CreateSnapshot();
        string expandedQuestion = FollowUpContext.Expand(question, _subject);
        bool followUp = expandedQuestion != question;
        bool ingredientUses = LocalKnowledgeAnswer.IsIngredientUsesQuestion(expandedQuestion);
        Item? namedItem = GameReferenceFacts.ResolveItem(question);
        Item? item = followUp && _subject is not null ? GameReferenceFacts.ResolveItem(_subject) : namedItem;
        if (!followUp && item is null) _subject = null;
        IReadOnlyList<Item> subjects = GameReferenceFacts.ResolveSubjects(expandedQuestion, item);
        // A named ingredient or accessory can add public compatibility facts
        // without replacing the item referred to by "it" in a follow-up.
        if (followUp && namedItem is not null && !subjects.Any(subject => subject.type == namedItem.type))
            subjects = subjects.Concat(new[] { namedItem }).ToArray();
        if (item is not null) _subject = item.Name;
        snapshot = snapshot with
        {
            Facts = subjects.SelectMany(subject => GameReferenceFacts.Build(expandedQuestion, subject)).ToArray(),
            ReferenceSubjects = subjects.Select(subject => new KnowledgeSubject("item", GameReferenceFacts.Key(subject), subject.Name)).ToArray(),
            Recipes = ingredientUses ? item is null ? Array.Empty<RecipeReferenceSnapshot>() : GameReferenceFacts.BuildIngredientUses(item)
                : GameReferenceFacts.BuildRecipes(subjects),
            RecipeLookupComplete = ingredientUses ? item is not null : subjects.Count > 0,
            GameVersion = Main.versionNumber
        };
        if (ingredientUses && item is not null)
        {
            IngredientUsesAnswer? uses = IngredientUsesPlanner.TryAnswer(expandedQuestion, snapshot, item.Name);
            if (uses is not null)
            {
                string display = AnswerFormatting.Clean(uses.Text);
                var highlights = AnswerFormatting.ForResponse(display, snapshot, null).ToList();
                highlights.AddRange(snapshot.Recipes.Select(recipe => recipe.ResultName).Distinct(StringComparer.Ordinal)
                    .Where(name => display.Contains(name, StringComparison.Ordinal)).Select(name => new AnswerHighlight(name, "item")));
                int search = 0;
                foreach (CraftingAssessment plan in uses.Plans)
                {
                    string section = AnswerFormatting.Clean(CraftingPlanner.Format(plan));
                    int start = display.IndexOf(section, search, StringComparison.Ordinal);
                    if (start < 0) continue;
                    highlights.AddRange(AnswerFormatting.ForCrafting(plan).Select(mark => mark.Start.HasValue
                        ? mark with { Start = mark.Start.Value + start } : mark));
                    search = start + section.Length;
                }
                if (uses.Plans.Count == 1) _subject = uses.Plans[0].ResultName;
                Add("GPT", display, highlights);
                return;
            }
        }
        // Ingredient arithmetic belongs to the game reference layer, so exact
        // missing-material answers work even without an AI connection.
        KnowledgeSnapshot craftingSnapshot = followUp && item is not null
            ? snapshot with { Recipes = snapshot.Recipes.Where(recipe => recipe.ResultKey == GameReferenceFacts.Key(item)).ToArray() }
            : snapshot;
        CraftingAssessment? crafting = item is null ? null : CraftingPlanner.Assess(expandedQuestion, craftingSnapshot, GameReferenceFacts.Key(item), item.Name);
        if (crafting is not null) { Add("GPT", CraftingPlanner.Format(crafting), AnswerFormatting.ForCrafting(crafting)); return; }
        string? local = CraftingPlanner.IsCraftingQuestion(expandedQuestion) ? null
            : LocalKnowledgeAnswer.TryAnswer(expandedQuestion, snapshot, item is null ? null : GameReferenceFacts.Key(item), item?.Name);
        if (local is not null) { Add("GPT", local, AnswerFormatting.ForInventory(local, snapshot)); return; }
        if (!config.EnableOnlineAnswers)
        {
            Add("GPT", OfflineAnswer(snapshot, "Online wiki lookup is disabled in Mod Configuration.")); return;
        }
        if (!config.ShareObservedInventory)
            snapshot = snapshot with { PlayerName = "", KnownSubjects = Array.Empty<KnowledgeSubject>(), Inventory = Array.Empty<ItemStackSnapshot>(), OwnedChests = Array.Empty<OwnedChestSnapshot>(), CurrentBiomes = Array.Empty<string>(), InventoryShared = false };
        // Local comparisons see every alternative. Bound only the public
        // remote recipe list, and disclose any truncation to the assistant.
        if (snapshot.Recipes.Count > 32)
            snapshot = snapshot with { Recipes = snapshot.Recipes.Take(32).ToArray(), RecipeLookupComplete = false };

        IReadOnlyList<ConversationTurn> conversation = FollowUpContext.HistoryForRequest(
            History.Take(Math.Max(0, History.Count - 1)).Select(entry => new ConversationTurn(entry.Role, entry.Text)), config.ShareObservedInventory);
        StartRequest(expandedQuestion, snapshot, conversation, config, chatOnly: false);
    }

    private static void StartRequest(string question, KnowledgeSnapshot snapshot, IReadOnlyList<ConversationTurn> conversation, GptClientConfig config, bool chatOnly, object? observedContext = null)
    {
        int epoch = _epoch;
        IsBusy = true;
        // All game reads and option snapshots are finished before leaving this thread.
        GptProvider provider = config.Provider;
        bool webSearch = config.EnableWebSearch;
        string chatGptModel = config.ChatGptModel ?? string.Empty;
        string chatGptEffort = config.ChatGptEffort ?? string.Empty;
        CancellationToken cancellation = _cancellation.Token;
        _ = Task.Run(() => AskAsync(question, snapshot, conversation, provider, epoch, cancellation, chatOnly,
            webSearch, observedContext, chatGptModel, chatGptEffort), cancellation);
    }

    public static void Pump()
    {
        ObserveOptions(ModContent.GetInstance<GptClientConfig>());
        while (Completed.TryDequeue(out var response))
        {
            if (response.Epoch != _epoch) continue;
            IsBusy = false;
            Add("GPT", response.Text, response.Highlights, response.Citations, response.WebSearched, response.GoalSuggestion, notifiesReply: true);
            if (response.ChatOnly)
            {
                if (response.Question is not null)
                {
                    ChatHistory.Add(new ConversationTurn("user", response.Question));
                    // Web citations belong to this particular answer. Do not
                    // carry detached [n] numbers into later model generations.
                    ChatHistory.Add(new ConversationTurn("assistant", AnswerFormatting.Clean(History[^1].Text)));
                    IReadOnlyList<ConversationTurn> bounded = ChatConversationContext.HistoryForRequest(ChatHistory);
                    ChatHistory.Clear();
                    ChatHistory.AddRange(bounded);
                }
                continue;
            }
            // Prefer the answer's first explicitly highlighted public item for
            // a subsequent 'Where can I find it?' question. This never uses a
            // chest label, tile position, world scan or hidden observation.
            foreach (AnswerHighlight highlight in History[^1].Highlights.Where(highlight => highlight.Kind == "item"))
            {
                Item? recommended = GameReferenceFacts.ResolveItem(highlight.Text);
                if (recommended is null || !recommended.Name.Equals(highlight.Text, StringComparison.OrdinalIgnoreCase)) continue;
                _subject = recommended.Name;
                break;
            }
        }
    }

    public static void AddNotice(string text) => Add("GPT", text);

    private static void ObserveOptions(GptClientConfig config)
    {
        if (_conversationProvider == config.Provider && _sharedProgress == config.ShareObservedProgress
            && _onlineAnswers == config.EnableOnlineAnswers) return;
        if (_conversationProvider.HasValue)
        {
            // A late response captured before a privacy/provider change must
            // not refill remote context. Locally visible history stays readable.
            _epoch++;
            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
            IsBusy = false;
            while (Completed.TryDequeue(out _)) { }
        }
        ChatHistory.Clear();
        _subject = null;
        _conversationProvider = config.Provider;
        _sharedProgress = config.ShareObservedProgress;
        _onlineAnswers = config.EnableOnlineAnswers;
    }

    public static void Reset()
    {
        _epoch++;
        _cancellation.Cancel();
        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
        IsBusy = false;
        _subject = null;
        _conversationProvider = null;
        _sharedProgress = null;
        _onlineAnswers = null;
        ChatHistory.Clear();
        History.Clear();
        while (Completed.TryDequeue(out _)) { }
    }

    private static async Task AskAsync(string question, KnowledgeSnapshot snapshot, IReadOnlyList<ConversationTurn> conversation,
        GptProvider provider, int epoch, CancellationToken cancellation, bool chatOnly, bool webSearch, object? observedContext, string chatGptModel, string chatGptEffort)
    {
        try
        {
            NativeChatReply reply = await NativeAssistantRuntime.AskAsync(question, snapshot, conversation, provider,
                webSearch, observedContext, cancellation, chatGptModel, chatGptEffort).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            string answer = AnswerFormatting.Clean(reply.Answer, reply.Citations.Select(source => source.Index));
            IReadOnlyList<WebCitation> citations = reply.Citations.Where(source => answer.Contains("[" + source.Index + "]", StringComparison.Ordinal)).ToArray();
            IReadOnlyList<AnswerHighlight> highlights = AnswerFormatting.ForResponse(answer, snapshot, reply.Highlights);
            bool usable = !string.IsNullOrWhiteSpace(answer);
            CompleteRequest(epoch, answer, highlights, chatOnly, chatOnly && usable ? question : null,
                citations, reply.WebSearched, usable ? reply.GoalSuggestion : null);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (NativeChatException exception)
        {
            CompleteRequest(epoch, ConnectionFailure(snapshot, exception.Message, chatOnly), null, chatOnly);
        }
        catch (OperationCanceledException)
        {
            CompleteRequest(epoch, ConnectionFailure(snapshot, "The request took too long. Check your internet connection and try again.", chatOnly), null, chatOnly);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Never show request headers, raw server bodies, credentials or local paths.
            CompleteRequest(epoch, ConnectionFailure(snapshot,
                "The request could not finish. Check your internet connection and the selected connection in mod settings, then try again.", chatOnly), null, chatOnly);
        }
    }

    private static string ConnectionFailure(KnowledgeSnapshot snapshot, string reason, bool chatOnly) => chatOnly ? reason : OfflineAnswer(snapshot, reason);

    private static string OfflineAnswer(KnowledgeSnapshot snapshot, string reason)
    {
        string facts = string.Join("\n", snapshot.Facts.Select(fact => fact.Text).Take(8));
        return facts.Length == 0 ? reason + " Inventory lookup still works here without it." : facts + "\nSource: installed game reference data.\n" + reason;
    }

    private static void Complete(int epoch, string text, IReadOnlyList<AnswerHighlight>? highlights = null) =>
        CompleteRequest(epoch, text, highlights, chatOnly: false);

    private static void CompleteRequest(int epoch, string text, IReadOnlyList<AnswerHighlight>? highlights, bool chatOnly, string? question = null,
        IReadOnlyList<WebCitation>? citations = null, bool searched = false, GoalProposal? goalSuggestion = null) =>
        Completed.Enqueue(new(epoch, text, highlights ?? Array.Empty<AnswerHighlight>(), chatOnly, question,
            citations ?? Array.Empty<WebCitation>(), searched, goalSuggestion));

    private static void Add(string role, string text, IReadOnlyList<AnswerHighlight>? highlights = null,
        IReadOnlyList<WebCitation>? citations = null, bool searched = false, GoalProposal? goalSuggestion = null, bool notifiesReply = false)
    {
        // Draw as literal text in the UI; assistant output is never parsed as commands.
        bool user = role == "You";
        citations ??= Array.Empty<WebCitation>();
        string display = user ? text : AnswerFormatting.Clean(text, citations.Select(source => source.Index));
        if (!user && string.IsNullOrWhiteSpace(display))
            display = "No readable answer was returned. Try asking again.";
        History.Add(new ChatEntry(user ? "user" : "assistant", display, DateTimeOffset.Now)
        {
            Highlights = user ? Array.Empty<AnswerHighlight>() : AnswerFormatting.Validate(display, highlights),
            Citations = user ? Array.Empty<WebCitation>() : citations,
            WebSearched = !user && searched,
            GoalSuggestion = user ? null : goalSuggestion,
            NotifiesReply = !user && notifiesReply
        });
        if (History.Count > 100) History.RemoveAt(0);
    }
}
