#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using TerrariaChatGPT.Common.Native;

namespace TerrariaChatGPT.Common.Services;

public enum ChatGptConnectionAction { Status, SignIn, SignOut, Welcome, ManageUsage }

/// <summary>The settings UI sees display-safe status, never authentication credentials.</summary>
public sealed record ChatGptConnectionStatus(bool Connected, bool SigningIn, string Account,
    string Message, bool UsingPlan, bool WelcomePending, bool IsError = false);

public static class ChatGptConnectionClient
{
    public static async Task<ChatGptConnectionStatus> RequestAsync(ChatGptConnectionAction action, CancellationToken cancellation)
    {
        // Called by UI on the game thread. An account transition cannot inherit
        // the previous account's in-flight answer or conversation observations.
        if (action is ChatGptConnectionAction.SignIn or ChatGptConnectionAction.SignOut)
            GptAssistantService.Reset();
        // Storage locks and DPAPI can finish synchronously inside async methods;
        // keep even that initial work off the game's UI thread.
        try { return await Task.Run(() => NativeAssistantRuntime.ConnectionAsync(action, cancellation), cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (NativeChatException error) { return Failure(error.Message); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { return Failure("The ChatGPT connection could not be checked. Try again in mod settings."); }
    }
    private static ChatGptConnectionStatus Failure(string message) => new(false, false, "", message, false, false, true);
}
