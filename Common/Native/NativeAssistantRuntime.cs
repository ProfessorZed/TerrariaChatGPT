#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Security;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Common.Native;

/// <summary>One in-process connection per mod load. No worker programs or persistent local server.</summary>
public sealed class NativeAssistantRuntime : IDisposable
{
    private static readonly object Gate = new();
    private static NativeAssistantRuntime? _current;
    private readonly NativeChatGptSession _session = new();
    private readonly NativeResponsesClient _responses = new();
    private readonly NativeWikiClient _wiki = new();
    private readonly CancellationTokenSource _lifetime = new();
    private int _authGeneration;

    public static void Initialize() { lock (Gate) _current ??= new(); }
    private static NativeAssistantRuntime Current { get { lock (Gate) return _current ??= new(); } }
    public static void Shutdown()
    {
        NativeAssistantRuntime? previous;
        lock (Gate) { previous = _current; _current = null; }
        previous?.Dispose();
    }

    public static async Task<ChatGptConnectionStatus> ConnectionAsync(ChatGptConnectionAction action, CancellationToken cancellation)
    {
        NativeAssistantRuntime current = Current;
        bool changesConnection = action is ChatGptConnectionAction.SignIn or ChatGptConnectionAction.SignOut;
        if (changesConnection) current.InvalidateCatalog();
        try { return await current._session.RequestAsync(action, cancellation).ConfigureAwait(false); }
        finally
        {
            // A catalog may have started while the session gate was still
            // completing this action. Those results belong to the old state.
            if (changesConnection) current.InvalidateCatalog();
        }
    }

    private void InvalidateCatalog()
    {
        Interlocked.Increment(ref _authGeneration);
        _responses.ClearModelCache();
    }

    public static Task<IReadOnlyList<ChatGptModelOption>> GetModelsAsync(CancellationToken cancellation, bool forceRefresh = false) =>
        Current.GetModelsCoreAsync(cancellation, forceRefresh);

    private async Task<IReadOnlyList<ChatGptModelOption>> GetModelsCoreAsync(CancellationToken cancellation, bool forceRefresh)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(45));
        int generation = Volatile.Read(ref _authGeneration);
        string token = await _session.GetAccessTokenAsync(linked.Token).ConfigureAwait(false);
        CheckCatalogGeneration(generation);
        IReadOnlyList<ChatGptModelOption> result = await _responses.GetModelsAsync(token, linked.Token, forceRefresh).ConfigureAwait(false);
        CheckCatalogGeneration(generation);
        return result;
    }

    private void CheckCatalogGeneration(int generation)
    {
        if (generation != Volatile.Read(ref _authGeneration))
            throw new NativeChatException("model_catalog_changed", "The ChatGPT connection changed. Refresh the model list to try again.");
    }

    public static Task<NativeChatReply> AskAsync(string question, KnowledgeSnapshot knowledge,
        IReadOnlyList<ConversationTurn> conversation, GptProvider provider, bool webSearch,
        object? observedContext, CancellationToken cancellation, string chatGptModel = "", string chatGptEffort = "", object? savedMemory = null) =>
        Current.AskCoreAsync(question, knowledge, conversation, provider, webSearch, observedContext, cancellation, chatGptModel, chatGptEffort, savedMemory);

    private async Task<NativeChatReply> AskCoreAsync(string question, KnowledgeSnapshot knowledge,
        IReadOnlyList<ConversationTurn> conversation, GptProvider provider, bool webSearch,
        object? observedContext, CancellationToken cancellation, string chatGptModel, string chatGptEffort, object? savedMemory)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(ChatGptEffortOptions.RuntimeTimeoutSeconds(provider == GptProvider.ChatGPT ? chatGptEffort : "")));
        CancellationToken stop = linked.Token;
        if (provider == GptProvider.WikiOnly)
            return await _wiki.AskAsync(question, knowledge.ReferenceSubjects, stop).ConfigureAwait(false);
        // Explicit provider selection is the only route to separately billed API usage.
        string token = provider switch
        {
            GptProvider.ChatGPT => await _session.GetAccessTokenAsync(stop).ConfigureAwait(false),
            GptProvider.OpenAiApi => GptCredentialStore.ReadKey(),
            _ => throw new NativeChatException("provider", "Choose a connection in Terraria ChatGPT's mod settings.")
        };
        if (string.IsNullOrEmpty(token))
            throw new NativeChatException("api_key", "Save an OpenAI API key in mod settings, or choose ChatGPT to use your connected account.");
        return await _responses.AskAsync(question, conversation, knowledge.GameVersion, observedContext,
            webSearch, token, provider == GptProvider.ChatGPT, stop, chatGptModel: chatGptModel, chatGptEffort: chatGptEffort,
            tModLoaderVersion: knowledge.TModLoaderVersion, savedMemory: savedMemory).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _session.Dispose();
        _responses.Dispose();
        _wiki.Dispose();
        // In-flight requests may still register cancellation while unwinding.
        // The small CTS remains valid until this runtime is collected.
    }
}
