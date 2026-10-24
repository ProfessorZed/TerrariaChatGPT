#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ModLoader;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Players;
using TerrariaChatGPT.Common.Goals;
using TerrariaChatGPT.Common.Native;
using TerrariaChatGPT.Common.Memory;
using TerrariaChatGPT.Common.Sorting;

namespace TerrariaChatGPT.Common.Services;

public sealed record ChatEntry(string Role, string Text, DateTimeOffset Timestamp)
{
    public IReadOnlyList<AnswerHighlight> Highlights { get; init; } = Array.Empty<AnswerHighlight>();
    public IReadOnlyList<WebCitation> Citations { get; init; } = Array.Empty<WebCitation>();
    public bool WebSearched { get; init; }
    public GoalProposal? GoalSuggestion { get; init; }
    public bool NotifiesReply { get; init; }
    public string? MemoryChangeId { get; init; }
    public string? MemorySummary { get; init; }
    public SortSuggestion? SortSuggestion { get; init; }
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
        bool ChatOnly, string? Question, IReadOnlyList<WebCitation> Citations, bool WebSearched, GoalProposal? GoalSuggestion)
    {
        public IReadOnlyList<MemoryProposal> MemorySuggestions { get; init; } = Array.Empty<MemoryProposal>();
        public IReadOnlyList<ConversationTurn> MemoryConversation { get; init; } = Array.Empty<ConversationTurn>();
        public SortSuggestion? SortSuggestion { get; init; }
        public SortingRequestScope? SortingScope { get; init; }
    }
    private sealed record SortingRequestScope(Player Owner, Guid World, Guid Session, int Epoch,
        IReadOnlyList<ProgressSubject> Candidates, IReadOnlyList<KnownProgressChest> Chests)
    {
        public IEnumerable<string> ChestNames => Chests.Select(chest => chest.Name);
    }
    private static SortSuggestion? _latestSortSuggestion;
    private static SortingRequestScope? _sortScope;
    private static readonly ConcurrentQueue<CompletedAnswer> Completed = new();
    private static CancellationTokenSource _cancellation = new();
    private static int _epoch;
    private static string? _subject;
    private static GptProvider? _conversationProvider;
    private static bool? _sharedProgress;
    private static bool? _onlineAnswers;
    private static GptMemoryPlayer? _memoryOwner;
    private static long _memoryRevision;
    public static int Epoch => _epoch;
    public static IReadOnlyList<ChatEntry> Messages => ReadOnlyHistory;
    public static string? LastAnswer => History.LastOrDefault(entry => entry.Role == "assistant")?.Text;
    public static bool IsBusy { get; private set; }
    public static SortSuggestion? LatestSortSuggestion => SortingScopeIsCurrent(_sortScope) ? _latestSortSuggestion : null;
    public static bool IsCurrentSortSuggestion(SortSuggestion? suggestion) => suggestion is not null
        && ReferenceEquals(suggestion, LatestSortSuggestion);
    public static void CompleteSortSuggestion(SortSuggestion? suggestion)
    {
        if (!IsCurrentSortSuggestion(suggestion)) return;
        _latestSortSuggestion = null;
        _sortScope = null;
    }

    public static bool RememberSortChoices(SortSuggestion suggestion, int epoch, out string message)
    {
        if (epoch != _epoch || !IsCurrentSortSuggestion(suggestion))
        { message = "These sorting choices are out of date. Ask GPT to categorise your current items again."; return false; }
        MemoryChangeBatch? receipt = ChestSortService.RememberChoices(suggestion, out message);
        if (receipt is null) return false;
        _memoryRevision = _memoryOwner?.Revision ?? 0;
        Add("GPT", message, memoryChange: receipt);
        return true;
    }

    public static void Submit(string question)
    {
        question = question.Trim();
        if (Main.gameMenu || Main.dedServ || question.Length == 0) return;
        if (question.Equals("/clear", StringComparison.OrdinalIgnoreCase)) { Reset(); return; }
        GptClientConfig config = ModContent.GetInstance<GptClientConfig>();
        ObserveOptions(config);
        ObserveMemory();
        if (question.Length > 1000) { Add("GPT", "Please keep each question under 1,000 characters."); return; }
        if (IsBusy) return;
        Add("You", question);
        GameVersionSnapshot versions = GameVersionContext.Capture();
        // These are explicit UI commands. Ordinary questions, including "help",
        // go to the selected AI without item matching or template interception.
        if (config.Provider != GptProvider.WikiOnly)
        {
            if (question.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                Add("GPT", "Chat naturally, ask for a web lookup, or describe a goal and choose Set goal? under the answer. Stable preferences and sorting corrections can be remembered for this character, with a visible notice and Undo. Memory lets you review, remove or disable them. Sort chests previews inventory transfers; only Confirm sort moves items. Previously opened or player-placed chests stay known across restarts. Hotbar and favorites stay protected. Hidden locations and unopened storage are unavailable. Progress sharing and saved memory have separate controls. /clear starts a new conversation without deleting memories.");
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
                GameVersion = versions.GameVersion,
                TModLoaderVersion = versions.TModLoaderVersion
            };
            IReadOnlyList<ConversationTurn> nativeConversation = ChatConversationContext.HistoryForRequest(ChatHistory);
            bool sortingRequest = IsSortingRequest(question, nativeConversation);
            SortingRequestScope? sortingScope = config.ShareObservedProgress && sortingRequest
                ? CaptureSortingScope() : null;
            if (sortingRequest || IsSortingCancellation(question)) { _latestSortSuggestion = null; _sortScope = null; }
            object? observedContext = null;
            if (config.ShareObservedProgress)
            {
                ProgressSnapshot observed = Main.LocalPlayer.GetModPlayer<GptProgressPlayer>().CreateSnapshot();
                if (sortingScope is not null)
                    observed = observed with { KnownChests = sortingScope.Chests };
                observedContext = ObservedContextBuilder.Build(observed, question, sortingScope?.Candidates);
            }
            StartRequest(question, emptyKnowledge, nativeConversation, config, chatOnly: true, observedContext, sortingScope);
            return;
        }
        GptKnowledgePlayer knowledge = Main.LocalPlayer.GetModPlayer<GptKnowledgePlayer>();
        switch (question.ToLowerInvariant())
        {
            case "/help": case "help":
                Add("GPT", "Ask any Terraria wiki question, or ‘Do I have Wood?’ for inventory help. In single-player, chests are remembered automatically when you open them. Reopen a chest to refresh its contents. Sort chests previews transfers into nearby named chests; Confirm sort moves only reviewed items and protects hotbar and favorites. /forget stops remembering that open chest, and /claim resumes it. /clear clears this chat. Hidden locations and unopened storage are unavailable."); return;
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
            GameVersion = versions.GameVersion,
            TModLoaderVersion = versions.TModLoaderVersion
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

    private static bool ReplacesSortingChoice(string text) => Regex.IsMatch(text,
        @"[;,]\s*(?:please\s+)?(?:put|place|send|move|sort|use|categor[isz]\w*|classif\w*)\b.{1,120}\b(?:instead|in|into|for)\b", RegexOptions.IgnoreCase);

    private static bool IsSortingCancellation(string text) => !ReplacesSortingChoice(text) && Regex.IsMatch(text,
        @"\b(?:don't|do not|never|stop|cancel)\s+(?:(?:want|need)(?:\s+you)?\s+to\s+|please\s+)?(?:sort\w*|(?:re)?categor[isz]\w*|(?:re)?classif\w*|organi[sz]\w*|put|place|send|move)\b|^\s*(?:never mind|nevermind|cancel)\b", RegexOptions.IgnoreCase);

    private static bool IsSortingRequest(string question, IReadOnlyList<ConversationTurn> conversation)
    {
        static bool Informational(string text) => Regex.IsMatch(text,
            @"^\s*(?:please\s+)?(?:explain|describe|tell me about|(?:can|could) you explain)\b", RegexOptions.IgnoreCase);
        static bool Direct(string text) => !IsSortingCancellation(text) && (
            Regex.IsMatch(text, @"\b(?:what|which)\s+chests?\b.{0,80}\b(?:these|those|items|put|go)\b", RegexOptions.IgnoreCase)
            || !Informational(text) && !Regex.IsMatch(text, @"^\s*(?:how|what|where|why|which|when|can i|should i)\b", RegexOptions.IgnoreCase)
                && Regex.IsMatch(text, @"\b(?:sort\w*|(?:re)?categor[isz]\w*|(?:re)?classif\w*|organi[sz]\w*)\b|\b(?:put|place|send|move)\b.{0,80}\b(?:chests?|storage)\b", RegexOptions.IgnoreCase));
        if (IsSortingCancellation(question) || Informational(question)) return false;
        if (Direct(question)) return true;
        bool sortingContext = LatestSortSuggestion is not null
            || conversation.Where(turn => turn.Role == "user").TakeLast(3).Any(turn => Direct(turn.Content));
        // These cues only request an inert review proposal. Exact item/chest
        // allowlists and explicit user confirmation still govern every transfer.
        return sortingContext && (ReplacesSortingChoice(question) || Regex.IsMatch(question,
            @"^\s*(?:(?:no|actually|instead|please)[,\s]+)*(?:yes\b|please do\b|do (?:it|that)\b|same for\b|(?:those|these) too\b|(?:use|put|place|send|move|change|switch|make|treat|classify|reclassify)\b)|\b(?:should\s+go|belongs?|goes|go)\s+(?:in|into|with)\b", RegexOptions.IgnoreCase));
    }

    private static SortingRequestScope CaptureSortingScope()
    {
        Player player = Main.LocalPlayer;
        GptKnowledgePlayer knowledge = player.GetModPlayer<GptKnowledgePlayer>();
        KnowledgeSnapshot snapshot = knowledge.CreateSnapshot();
        ProgressSubject[] candidates = player.inventory.Skip(10).Take(40)
            .Where(item => item is not null && !item.IsAir && !item.favorited && !item.IsACoin)
            .Where(item => SortSuggestionValidator.IsItemKey(GameReferenceFacts.Key(item)))
            .GroupBy(GameReferenceFacts.Key, StringComparer.Ordinal)
            .Select(items => new ProgressSubject(items.Key, items.First().Name)).ToArray();
        KnownProgressChest[] chests = snapshot.OwnedChests
            .Where(chest => SortSuggestionValidator.IsChestName(chest.Label) && !chest.Label.StartsWith("Chest at tile ", StringComparison.OrdinalIgnoreCase))
            .GroupBy(chest => chest.Label, StringComparer.OrdinalIgnoreCase).Select(rows => rows.OrderByDescending(chest => chest.LastObservedUtc).First())
            .Select(chest => new KnownProgressChest(chest.Label, chest.LastObservedUtc)).ToArray();
        return new(player, Main.ActiveWorldFileData.UniqueId, knowledge.SortingSessionId, _epoch,
            Array.AsReadOnly(candidates), Array.AsReadOnly(chests));
    }

    private static bool SortingScopeIsCurrent(SortingRequestScope? scope) => scope is not null
        && scope.Epoch == _epoch && !Main.gameMenu && !Main.dedServ && ReferenceEquals(scope.Owner, Main.LocalPlayer)
        && Main.ActiveWorldFileData.UniqueId == scope.World
        && Main.LocalPlayer.GetModPlayer<GptKnowledgePlayer>().SortingSessionId == scope.Session;

    private static void StartRequest(string question, KnowledgeSnapshot snapshot, IReadOnlyList<ConversationTurn> conversation, GptClientConfig config, bool chatOnly, object? observedContext = null,
        SortingRequestScope? sortingScope = null)
    {
        int epoch = _epoch;
        IsBusy = true;
        // All game reads and option snapshots are finished before leaving this thread.
        GptProvider provider = config.Provider;
        bool webSearch = config.EnableWebSearch;
        string chatGptModel = config.ChatGptModel ?? string.Empty;
        string chatGptEffort = config.ChatGptEffort ?? string.Empty;
        object? savedMemory = chatOnly ? _memoryOwner?.CreateContext() : null;
        CancellationToken cancellation = _cancellation.Token;
        _ = Task.Run(() => AskAsync(question, snapshot, conversation, provider, epoch, cancellation, chatOnly,
            webSearch, observedContext, chatGptModel, chatGptEffort, savedMemory, sortingScope), cancellation);
    }

    public static void Pump()
    {
        ObserveOptions(ModContent.GetInstance<GptClientConfig>());
        ObserveMemory();
        while (Completed.TryDequeue(out var response))
        {
            if (response.Epoch != _epoch) continue;
            IsBusy = false;
            MemoryChangeBatch? memoryChange = null;
            SortSuggestion? sorting = null;
            if (response.ChatOnly && SortingScopeIsCurrent(response.SortingScope))
            {
                sorting = SortSuggestionValidator.Validate(response.SortSuggestion,
                    response.SortingScope!.Candidates.Select(item => item.Key), response.SortingScope.ChestNames);
                if (sorting is not null) { _latestSortSuggestion = sorting; _sortScope = response.SortingScope; }
            }
            if (response.ChatOnly && response.Question is not null && _memoryOwner?.Enabled == true)
            {
                try
                {
                    memoryChange = _memoryOwner.ApplyProposals(response.MemorySuggestions, response.Question, response.MemoryConversation);
                    _memoryRevision = _memoryOwner.Revision;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                { /* Optional memory cannot discard a completed answer. */ }
            }
            string display = response.Text;
            if (response.SortSuggestion is null && response.Question is not null && SortingScopeIsCurrent(response.SortingScope)
                && response.SortingScope!.Candidates.Count > 0 && response.SortingScope.Chests.Count > 0)
                display += "\nNo usable GPT sorting plan was returned. Nothing moved. Ask me to try again; Sort chests currently uses your saved and default rules.";
            else if (response.SortSuggestion is not null && SortingScopeIsCurrent(response.SortingScope)
                && (sorting is null || sorting.Assignments.Count < response.SortSuggestion.Assignments.Select(row => row.ItemKey).Distinct().Count()))
                display += sorting is null
                    ? "\nThose destinations could not be matched to your eligible items and known chests, so no GPT sorting plan was applied."
                    : "\nSome suggested destinations could not be matched to your eligible items and known chests. Only validated choices appear in Review sort.";
            Add("GPT", display, response.Highlights, response.Citations, response.WebSearched, response.GoalSuggestion,
                notifiesReply: true, memoryChange: memoryChange, sortSuggestion: sorting);
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

    private static void ObserveMemory()
    {
        GptMemoryPlayer? current = Main.gameMenu || Main.dedServ ? null : Main.LocalPlayer.GetModPlayer<GptMemoryPlayer>();
        long revision = current?.Revision ?? 0;
        if (ReferenceEquals(current, _memoryOwner) && revision == _memoryRevision) return;
        if (_memoryOwner is not null)
        {
            // Removing/undoing/disabling a memory must also stop old requests and
            // prevent recent conversational copies from teaching it back again.
            _epoch++;
            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
            IsBusy = false;
            ChatHistory.Clear();
            _subject = null;
            while (Completed.TryDequeue(out _)) { }
        }
        _memoryOwner = current;
        _memoryRevision = revision;
    }

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
        _memoryOwner = null;
        _memoryRevision = 0;
        _latestSortSuggestion = null;
        _sortScope = null;
        ChatHistory.Clear();
        History.Clear();
        while (Completed.TryDequeue(out _)) { }
    }

    private static async Task AskAsync(string question, KnowledgeSnapshot snapshot, IReadOnlyList<ConversationTurn> conversation,
        GptProvider provider, int epoch, CancellationToken cancellation, bool chatOnly, bool webSearch, object? observedContext,
        string chatGptModel, string chatGptEffort, object? savedMemory, SortingRequestScope? sortingScope)
    {
        try
        {
            NativeChatReply reply = await NativeAssistantRuntime.AskAsync(question, snapshot, conversation, provider,
                webSearch, observedContext, cancellation, chatGptModel, chatGptEffort, savedMemory).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            string answer = AnswerFormatting.Clean(reply.Answer, reply.Citations.Select(source => source.Index));
            IReadOnlyList<WebCitation> citations = reply.Citations.Where(source => answer.Contains("[" + source.Index + "]", StringComparison.Ordinal)).ToArray();
            IReadOnlyList<AnswerHighlight> highlights = AnswerFormatting.ForResponse(answer, snapshot, reply.Highlights);
            bool usable = !string.IsNullOrWhiteSpace(answer);
            CompleteRequest(epoch, answer, highlights, chatOnly, chatOnly && usable ? question : null,
                citations, reply.WebSearched, usable ? reply.GoalSuggestion : null,
                usable && chatOnly ? reply.MemorySuggestions : null, conversation,
                usable && chatOnly ? reply.SortSuggestion : null, sortingScope);
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
        IReadOnlyList<WebCitation>? citations = null, bool searched = false, GoalProposal? goalSuggestion = null,
        IReadOnlyList<MemoryProposal>? memories = null, IReadOnlyList<ConversationTurn>? memoryConversation = null,
        SortSuggestion? sortSuggestion = null, SortingRequestScope? sortingScope = null) =>
        Completed.Enqueue(new(epoch, text, highlights ?? Array.Empty<AnswerHighlight>(), chatOnly, question,
            citations ?? Array.Empty<WebCitation>(), searched, goalSuggestion)
        {
            MemorySuggestions = memories ?? Array.Empty<MemoryProposal>(),
            MemoryConversation = memoryConversation ?? Array.Empty<ConversationTurn>(),
            SortSuggestion = sortSuggestion, SortingScope = sortingScope
        });

    private static void Add(string role, string text, IReadOnlyList<AnswerHighlight>? highlights = null,
        IReadOnlyList<WebCitation>? citations = null, bool searched = false, GoalProposal? goalSuggestion = null,
        bool notifiesReply = false, MemoryChangeBatch? memoryChange = null, SortSuggestion? sortSuggestion = null)
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
            NotifiesReply = !user && notifiesReply,
            MemoryChangeId = user ? null : memoryChange?.Id,
            MemorySummary = user ? null : memoryChange?.Summary,
            SortSuggestion = user ? null : sortSuggestion
        });
        if (History.Count > 100) History.RemoveAt(0);
    }
}
