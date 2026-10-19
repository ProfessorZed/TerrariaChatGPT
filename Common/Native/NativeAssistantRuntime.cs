#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TerrariaGPT.Common.Config;
using TerrariaGPT.Common.Knowledge;
using TerrariaGPT.Common.Security;
using TerrariaGPT.Common.Services;

namespace TerrariaGPT.Common.Native;

/// <summary>One in-process connection per mod load. No worker programs or persistent local server.</summary>
public sealed class NativeAssistantRuntime : IDisposable
{
    private static readonly object Gate = new();
    private static NativeAssistantRuntime? _current;
    private readonly NativeChatGptSession _session = new();
    private readonly NativeResponsesClient _responses = new();
    private readonly NativeWikiClient _wiki = new();
    private readonly CancellationTokenSource _lifetime = new();

    public static void Initialize() { lock (Gate) _current ??= new(); }
    private static NativeAssistantRuntime Current { get { lock (Gate) return _current ??= new(); } }
    public static void Shutdown()
    {
        NativeAssistantRuntime? previous;
        lock (Gate) { previous = _current; _current = null; }
        previous?.Dispose();
    }

    public static Task<ChatGptConnectionStatus> ConnectionAsync(ChatGptConnectionAction action, CancellationToken cancellation) =>
        Current._session.RequestAsync(action, cancellation);

    public static Task<NativeChatReply> AskAsync(string question, KnowledgeSnapshot knowledge,
        IReadOnlyList<ConversationTurn> conversation, GptProvider provider, bool webSearch,
        object? observedContext, CancellationToken cancellation) =>
        Current.AskCoreAsync(question, knowledge, conversation, provider, webSearch, observedContext, cancellation);

    private async Task<NativeChatReply> AskCoreAsync(string question, KnowledgeSnapshot knowledge,
        IReadOnlyList<ConversationTurn> conversation, GptProvider provider, bool webSearch,
        object? observedContext, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(150));
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
            webSearch, token, provider == GptProvider.ChatGPT, stop).ConfigureAwait(false);
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
