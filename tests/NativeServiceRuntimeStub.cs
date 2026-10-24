#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Common.Native;

// Network/auth are tested separately with injected HTTP handlers. This seam tests
// the real game-thread controller, privacy projection, history and cancellation.
public sealed record NativeServiceRequest(string Question, KnowledgeSnapshot Knowledge,
    IReadOnlyList<ConversationTurn> Conversation, GptProvider Provider, bool WebSearch, object? ObservedContext, string ChatGptModel, string ChatGptEffort, object? SavedMemory = null)
{
    public string ToJson() => JsonSerializer.Serialize(new {
        question = Question, knowledge = Knowledge, conversation = Conversation, provider = Provider.ToString(),
        chatOnly = Provider != GptProvider.WikiOnly, webSearch = WebSearch, chatGptModel = ChatGptModel, chatGptEffort = ChatGptEffort,
        observedContext = ObservedContext, shareProgress = ObservedContext is not null, observedWorldOnly = true, savedMemory = SavedMemory
    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
public static class NativeAssistantRuntime
{
    public static Func<NativeServiceRequest, CancellationToken, Task<NativeChatReply>>? Handler;
    public static Task<NativeChatReply> AskAsync(string question, KnowledgeSnapshot knowledge,
        IReadOnlyList<ConversationTurn> conversation, GptProvider provider, bool webSearch,
        object? observedContext, CancellationToken cancellation, string chatGptModel = "", string chatGptEffort = "", object? savedMemory = null) =>
        Handler?.Invoke(new(question, knowledge, conversation, provider, webSearch, observedContext, chatGptModel, chatGptEffort, savedMemory), cancellation)
        ?? throw new InvalidOperationException("An unexpected online request occurred in a local-only test.");
}
