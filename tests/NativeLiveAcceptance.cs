#nullable enable
using System;
using System.IO;
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
// game observations, browser launches, legacy helper or loopback bridge.
public static class NativeLiveAcceptance
{
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(160));
        try
        {
            NativeAssistantRuntime.Initialize();
            ChatGptConnectionStatus status = await NativeAssistantRuntime.ConnectionAsync(ChatGptConnectionAction.Status, timeout.Token);
            if (!status.Connected || !status.UsingPlan || status.WelcomePending || status.IsError)
                throw new NativeChatException("acceptance_connection", "The saved account is not ready for a live check: " + status.Message);
            string question = phase == "Recipe" ? "what can i make from blade of grass"
                : "Search the official Terraria wiki to verify how I get Rocket Boots in Terraria 1.4.4. I want to get Rocket Boots; offer me a goal for that.";
            var snapshot = new KnowledgeSnapshot("", DateTimeOffset.UtcNow, Array.Empty<KnowledgeSubject>(),
                Array.Empty<ItemStackSnapshot>(), Array.Empty<OwnedChestSnapshot>(), Array.Empty<string>())
                { InventoryShared = false, GameVersion = "1.4.4.9" };
            NativeChatReply reply = await NativeAssistantRuntime.AskAsync(question, snapshot, Array.Empty<ConversationTurn>(),
                GptProvider.ChatGPT, true, null, timeout.Token);
            if (string.IsNullOrWhiteSpace(reply.Answer) || reply.Highlights.Count == 0)
                throw new NativeChatException("acceptance_answer", "The live reply did not include a readable, highlighted answer.");
            if (phase == "WebGoal" && (!reply.WebSearched || reply.Citations.Count == 0 || reply.GoalSuggestion is null))
                throw new NativeChatException("acceptance_features", "The live reply did not demonstrate all of web search, citations and a goal suggestion.");
            return reply;
        }
        finally { NativeAssistantRuntime.Shutdown(); }
    }
}
