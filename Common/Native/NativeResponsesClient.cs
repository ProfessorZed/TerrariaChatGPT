#nullable enable
// Terraria ChatGPT 0.9.0: renamed app labels; existing credential storage retained (2026-10-19).
// Adapted from the vendored Sign in with ChatGPT responses/models transport.
// See NOTICE.txt and SignInWithChatGPT-LICENSE.txt in this directory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.Memory;

namespace TerrariaChatGPT.Common.Native;

/// <summary>Direct, bounded HTTPS inference. No helper process, local server, game access or tool execution.</summary>
public sealed class NativeResponsesClient : IDisposable
{
    private const int MaxText = 24000;
    private const int MaxParts = 512;
    // The account catalog includes capability/configuration metadata in addition
    // to the few display fields used here. It is much larger than a model picker.
    private const int MaxCatalogCharacters = 4 * 1024 * 1024;
    private const int MaxStreamCharacters = 4 * 1024 * 1024;
    private const int MaxEventCharacters = 256 * 1024;
    private const string ApiRoot = "https://api.openai.com/v1/";
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private readonly object _cacheLock = new();
    private string? _cachedTokenHash;
    private IReadOnlyList<ChatGptModelOption>? _cachedModels;
    private DateTimeOffset _catalogTime;
    private int _cacheGeneration;
    public string SelectedModel { get; private set; } = "";

    public NativeResponsesClient(HttpClient? client = null)
    {
        _ownsClient = client == null;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void ClearModelCache()
    {
        lock (_cacheLock)
        {
            _cacheGeneration++;
            _cachedTokenHash = null;
            _cachedModels = null;
            SelectedModel = "";
        }
    }

    public async Task<NativeChatReply> AskAsync(string question, IReadOnlyList<ConversationTurn> conversation,
        string gameVersion, object? observedContext, bool webSearch, string bearerToken, bool useChatGptPlan,
        CancellationToken cancellation, string apiModel = "gpt-4o-mini", string chatGptModel = "", string chatGptEffort = "", string tModLoaderVersion = "", object? savedMemory = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(ChatGptEffortOptions.InferenceTimeoutSeconds(useChatGptPlan ? chatGptEffort : "", webSearch)));
        CancellationToken signal = deadline.Token;
        try
        {
            signal.ThrowIfCancellationRequested();
            ValidateBearer(bearerToken);
            // Validate the detached data before touching the network.
            object[] input = NativeChatPrompt.BuildInput(question, conversation, gameVersion, observedContext, tModLoaderVersion, savedMemory);
            bool memoryEnabled = NativeChatPrompt.IsMemoryEnabled(savedMemory);
            if (useChatGptPlan && chatGptEffort != "" && !ChatGptEffortOptions.IsKnown(chatGptEffort))
                throw new NativeChatException("invalid_effort", "Choose a supported effort in GPT chat, or choose Auto.");
            ChatGptModelOption? selected = useChatGptPlan ? await ModelAsync(bearerToken, chatGptModel, signal).ConfigureAwait(false) : null;
            if (selected != null && chatGptEffort != "" && !selected.SupportedReasoningEfforts.Contains(chatGptEffort, StringComparer.Ordinal))
                throw new NativeChatException("effort_unavailable", "Your selected effort is unavailable for this model. Open the effort selector in GPT chat and choose a supported level or Auto.");
            string model = selected?.Slug ?? ValidateApiModel(apiModel);
            var payload = new Dictionary<string, object>
            {
                ["model"] = model, ["store"] = false, ["stream"] = true,
                ["instructions"] = NativeChatPrompt.SystemInstructions, ["input"] = input
            };
            // Plan usage explicitly forbids this API-only field.
            if (!useChatGptPlan) payload["max_output_tokens"] = NativeChatPrompt.HasSortingCandidates(observedContext) ? 4800 : 2400;
            if (useChatGptPlan && chatGptEffort != "") payload["reasoning"] = new { effort = chatGptEffort };
            if (webSearch)
            {
                payload["tools"] = new[] { new { type = "web_search", external_web_access = true } };
                payload["tool_choice"] = "auto";
            }
            using var request = Request(HttpMethod.Post, "responses", bearerToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, signal).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw await HttpErrorAsync(response, webSearch, useChatGptPlan, signal).ConfigureAwait(false);
            string? contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType != null && !contentType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase))
                throw InvalidStream();
            using Stream body = await response.Content.ReadAsStreamAsync(signal).ConfigureAwait(false);
            StreamReply completed = await ReadStreamAsync(body, webSearch, useChatGptPlan, signal).ConfigureAwait(false);
            signal.ThrowIfCancellationRequested();
            NativeChatReply reply = NativeReplyParser.Parse(completed.Text, webSearch ? completed.Annotations : Array.Empty<NativeSourceAnnotation>(),
                webSearch && completed.Searched);
            // Model output is only a proposal, never a permission or execution
            // path. Disabled memory and evidence from other turns are refused.
            return reply with { MemorySuggestions = memoryEnabled
                ? reply.MemorySuggestions.Where(proposal => question.Contains(proposal.Evidence, StringComparison.Ordinal)).Take(3).ToArray()
                : Array.Empty<MemoryProposal>() };
        }
        catch (NativeChatException) { throw; }
        catch (OperationCanceledException)
        {
            throw new NativeChatException(cancellation.IsCancellationRequested ? "cancelled" : "timeout",
                cancellation.IsCancellationRequested ? "The request was cancelled." : "The answer took too long. Try again.");
        }
        catch (JsonException) { throw InvalidStream(); }
        catch (DecoderFallbackException) { throw InvalidStream(); }
        catch (HttpRequestException) { throw new NativeChatException("network_error", "Could not reach the selected service. Check your internet connection and try again."); }
        catch (IOException) { throw new NativeChatException("stream_interrupted", "The connection ended before the answer completed. Try again."); }
        catch (Exception)
        {
            // Exception messages can contain request text or authentication headers.
            throw new NativeChatException("request_failed", "The request could not be completed. Try again.");
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, ApiRoot + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("TerrariaChatGPT/0.9.0");
        return request;
    }

    public async Task<IReadOnlyList<ChatGptModelOption>> GetModelsAsync(string bearerToken,
        CancellationToken cancellation, bool forceRefresh = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            ValidateBearer(bearerToken);
            return await CatalogAsync(bearerToken, deadline.Token, forceRefresh).ConfigureAwait(false);
        }
        catch (NativeChatException) { throw; }
        catch (OperationCanceledException)
        {
            throw new NativeChatException(cancellation.IsCancellationRequested ? "cancelled" : "timeout",
                cancellation.IsCancellationRequested ? "The model list request was cancelled." : "The model list took too long. Refresh it to try again.");
        }
        catch (JsonException) { throw InvalidCatalog(); }
        catch (DecoderFallbackException) { throw InvalidCatalog(); }
        catch (HttpRequestException) { throw new NativeChatException("network_error", "Could not load ChatGPT models. Check your internet connection and refresh the list."); }
        catch (IOException) { throw new NativeChatException("network_error", "The model list connection ended early. Refresh the list to try again."); }
        catch (Exception) { throw new NativeChatException("model_catalog_failed", "Could not load ChatGPT models. Refresh the list to try again."); }
    }

    private async Task<ChatGptModelOption> ModelAsync(string token, string requestedModel, CancellationToken signal)
    {
        if (requestedModel != "" && !SafeModel(requestedModel))
            throw new NativeChatException("invalid_model", "Choose a model in GPT chat, or choose Auto.");
        IReadOnlyList<ChatGptModelOption> models = await GetModelsAsync(token, signal).ConfigureAwait(false);
        ChatGptModelOption? selected = requestedModel == "" ? models[0]
            : models.FirstOrDefault(model => model.Slug == requestedModel);
        if (selected == null)
            throw new NativeChatException("model_unavailable", "Your selected ChatGPT model is no longer available for this account. Refresh the model list in GPT chat and choose another model or Auto.");
        signal.ThrowIfCancellationRequested();
        lock (_cacheLock)
        {
            if (!ReferenceEquals(_cachedModels, models))
                throw new NativeChatException("model_catalog_changed", "The ChatGPT connection changed. Refresh the model list to try again.");
            SelectedModel = selected.DisplayName;
        }
        return selected;
    }

    private async Task<IReadOnlyList<ChatGptModelOption>> CatalogAsync(string token, CancellationToken signal, bool forceRefresh)
    {
        // Only a one-way digest is retained, never the bearer credential. Token
        // rotation and account changes force an account-specific catalog refresh.
        string tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        await _catalogGate.WaitAsync(signal).ConfigureAwait(false);
        try
        {
            int generation;
            lock (_cacheLock)
            {
                if (!forceRefresh && _cachedTokenHash == tokenHash && _cachedModels != null && DateTimeOffset.UtcNow - _catalogTime < TimeSpan.FromMinutes(5))
                    return _cachedModels;
                generation = _cacheGeneration;
            }
            using var request = Request(HttpMethod.Get, "models", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, signal).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw await HttpErrorAsync(response, false, true, signal).ConfigureAwait(false);
            string text = await BoundedBodyAsync(response.Content, MaxCatalogCharacters, "catalog_too_large", signal).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            if (!Try(json.RootElement, "models", out JsonElement models) || models.ValueKind != JsonValueKind.Array || models.GetArrayLength() > 1024)
                throw InvalidCatalog();
            var visible = new List<ChatGptModelOption>();
            var slugs = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement model in models.EnumerateArray())
            {
                if (String(model, "visibility") != "list") continue;
                string? candidate = String(model, "slug"), display = String(model, "display_name");
                if (!SafeModel(candidate) || string.IsNullOrWhiteSpace(display) || display.Length > 200 || display.Any(char.IsControl))
                    throw InvalidCatalog();
                if (!slugs.Add(candidate!)) throw InvalidCatalog();
                // Preserve server ordering. Auto uses the first visible entry.
                IReadOnlyList<string> efforts = ReadReasoningEfforts(model);
                string defaultEffort = String(model, "default_reasoning_level") ?? "";
                visible.Add(new ChatGptModelOption(candidate!, display!)
                {
                    SupportedReasoningEfforts = efforts,
                    DefaultReasoningEffort = efforts.Contains(defaultEffort, StringComparer.Ordinal) ? defaultEffort : ""
                });
            }
            if (visible.Count == 0) throw new NativeChatException("not_eligible", "No model is available for this ChatGPT account or workspace.");
            IReadOnlyList<ChatGptModelOption> result = Array.AsReadOnly(visible.ToArray());
            signal.ThrowIfCancellationRequested();
            lock (_cacheLock)
            {
                if (_cacheGeneration != generation)
                    throw new NativeChatException("model_catalog_changed", "The ChatGPT connection changed. Refresh the model list to try again.");
                _cachedTokenHash = tokenHash; _cachedModels = result; _catalogTime = DateTimeOffset.UtcNow;
            }
            return result;
        }
        finally { _catalogGate.Release(); }
    }

    private static void ValidateBearer(string bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken) || bearerToken.Length > 32768 || bearerToken.Any(char.IsControl))
            throw new NativeChatException("not_connected", "Connect your selected account in mod settings, then try again.");
    }

    private static IReadOnlyList<string> ReadReasoningEfforts(JsonElement model)
    {
        // Capability metadata is optional. Missing, oversized or unfamiliar
        // formats preserve the model's safe Auto option rather than guessing.
        if (!Try(model, "supported_reasoning_levels", out JsonElement levels)
            || levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() > 32)
            return Array.Empty<string>();
        var supported = new List<string>();
        foreach (JsonElement level in levels.EnumerateArray())
        {
            string? effort = String(level, "effort");
            // The catalog can expose modes for other clients (such as agent
            // delegation). Only documented standard Responses values are sent.
            if (ChatGptEffortOptions.IsKnown(effort) && !supported.Contains(effort!, StringComparer.Ordinal))
                supported.Add(effort!);
        }
        return Array.AsReadOnly(supported.ToArray());
    }

    private static string ValidateApiModel(string model) => SafeModel(model) ? model
        : throw new NativeChatException("invalid_model", "Choose a valid OpenAI API model in mod settings.");
    private static bool SafeModel(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(char.IsControl);
    private static NativeChatException InvalidCatalog() => new("invalid_model_catalog", "ChatGPT returned an unexpected model list. Try again.");
    private static NativeChatException InvalidStream() => new("invalid_stream", "The service returned an invalid response stream. Try again.");
    private static NativeChatException TooLarge(string code = "response_text_too_large") => new(code, code switch
    {
        "catalog_too_large" => "ChatGPT returned a model list larger than this version supports. Try again or update Terraria ChatGPT.",
        "response_event_too_large" => "The service returned an oversized response event. Try again or update Terraria ChatGPT.",
        "response_stream_too_large" or "response_events_too_many" => "The response stream exceeded its size limit. Try a smaller question.",
        "response_parts_too_many" => "The response contained too many text parts. Try a smaller question.",
        "response_body_too_large" => "The service returned an oversized error response. Try again shortly.",
        _ => "The answer text was too large. Try a smaller question."
    });
    private sealed record StreamReply(string Text, IReadOnlyList<NativeSourceAnnotation> Annotations, bool Searched);
    private sealed record TextPart(int Output, int Content, string Text);

    private static async Task<StreamReply> ReadStreamAsync(Stream stream, bool webSearch, bool plan, CancellationToken signal)
    {
        var deltas = new StringBuilder();
        var doneParts = new Dictionary<(int, int), TextPart>();
        var messageParts = new Dictionary<(int, int), TextPart>();
        var annotations = new Dictionary<(int, int), IReadOnlyList<NativeSourceAnnotation>>();
        bool searched = false;
        void SaveAnnotations((int, int) key, JsonElement rows)
        {
            if (annotations.ContainsKey(key) || annotations.Count < 100) annotations[key] = CitationRows(rows, 0).Take(24).ToArray();
        }
        void SaveMessage(int output, int content, JsonElement part)
        {
            string? text = String(part, "text");
            if (text == null || output < 0 || output >= 4096 || content < 0 || content >= 4096) throw InvalidStream();
            var key = (output, content);
            messageParts.TryGetValue(key, out TextPart? previous);
            if (previous == null && messageParts.Count == MaxParts) throw TooLarge("response_parts_too_many");
            messageParts[key] = new TextPart(output, content, text);
            if (messageParts.Values.Sum(row => row.Text.Length) > MaxText) throw TooLarge();
            if (Try(part, "annotations", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array) SaveAnnotations(key, rows);
            else if ((previous ?? doneParts.GetValueOrDefault(key)) is TextPart old && old.Text != text) annotations.Remove(key);
        }
        await foreach (string data in EventsAsync(stream, signal).ConfigureAwait(false))
        {
            signal.ThrowIfCancellationRequested();
            if (data == "[DONE]") break;
            using JsonDocument json = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement item = json.RootElement;
            string? type = String(item, "type");
            if (type == null) throw InvalidStream();
            if (type == "response.web_search_call.completed") searched = true;
            if (type == "response.output_text.delta")
            {
                string? delta = String(item, "delta");
                if (delta == null) throw InvalidStream();
                if (deltas.Length + delta.Length > MaxText) throw TooLarge();
                deltas.Append(delta);
            }
            else if (type == "response.output_text.done")
            {
                var key = PartKey(item);
                string? text = String(item, "text");
                if (text == null) throw InvalidStream();
                if (doneParts.TryGetValue(key, out TextPart? previous) && previous.Text != text) throw InvalidStream();
                if (previous == null && doneParts.Count == MaxParts) throw TooLarge("response_parts_too_many");
                doneParts[key] = new TextPart(key.Item1, key.Item2, text);
                if (doneParts.Values.Sum(row => row.Text.Length) > MaxText) throw TooLarge();
                if (Try(item, "annotations", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array) SaveAnnotations(key, rows);
            }
            else if (type == "response.content_part.done" && Try(item, "part", out JsonElement part) && String(part, "type") == "output_text")
            {
                var key = PartKey(item); SaveMessage(key.Item1, key.Item2, part);
            }
            else if (type == "response.output_item.done" && Try(item, "item", out JsonElement completedItem))
            {
                if (String(completedItem, "type") == "web_search_call" && String(completedItem, "status") == "completed") searched = true;
                if (String(completedItem, "type") == "message" && String(completedItem, "role") == "assistant"
                    && Try(completedItem, "content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
                {
                    int output = Index(item, "output_index");
                    if (content.GetArrayLength() > MaxParts) throw TooLarge("response_parts_too_many");
                    int index = 0;
                    foreach (JsonElement p in content.EnumerateArray()) { if (String(p, "type") == "output_text") SaveMessage(output, index, p); index++; }
                }
            }
            else if (type == "response.output_text.annotation.added" && Try(item, "annotation", out JsonElement annotation))
            {
                var key = PartKey(item);
                if (annotations.ContainsKey(key) || annotations.Count < 100)
                {
                    var current = annotations.GetValueOrDefault(key) ?? Array.Empty<NativeSourceAnnotation>();
                    annotations[key] = current.Concat(CitationRow(annotation, 0)).Take(24).ToArray();
                }
            }
            else if (type is "response.failed" or "error")
            {
                throw RemoteError(type == "response.failed" && Try(item, "response", out JsonElement failed) ? failed : item, 200, webSearch, plan);
            }
            else if (type == "response.incomplete")
                throw new NativeChatException("response_incomplete", "The answer stopped before completion. Try again.");
            else if (type == "response.completed")
            {
                Try(item, "response", out JsonElement completed);
                string? status = String(completed, "status");
                if (status == "incomplete") throw new NativeChatException("response_incomplete", "The answer stopped before completion. Try again.");
                if (status == "failed") throw RemoteError(completed, 200, webSearch, plan);
                if (status != null && status != "completed") throw InvalidStream();
                foreach (var pair in messageParts) doneParts[pair.Key] = pair.Value;
                TextPart[] sorted = doneParts.Values.OrderBy(row => row.Output).ThenBy(row => row.Content).ToArray();
                string? snapshot = sorted.Length > 0 ? string.Concat(sorted.Select(row => row.Text)) : null;
                if (snapshot?.Length > MaxText) throw TooLarge();
                var final = FinalSnapshot(completed);
                searched |= final.Searched;
                string answer = final.Text ?? snapshot ?? deltas.ToString();
                IReadOnlyList<NativeSourceAnnotation> sources = final.Annotations;
                // Only reuse event annotations if their source text still exactly
                // matches the final snapshot. A restored prefix shifts offsets.
                if (sources.Count == 0 && answer == snapshot)
                {
                    var fallback = new List<NativeSourceAnnotation>();
                    int offset = 0;
                    foreach (TextPart row in sorted)
                    {
                        if (annotations.TryGetValue((row.Output, row.Content), out var rows))
                            fallback.AddRange(rows.Where(source => source.Start >= 0 && source.End <= row.Text.Length)
                                .Select(source => source with { Start = source.Start + offset, End = source.End + offset }));
                        offset += row.Text.Length;
                    }
                    sources = fallback.Take(100).ToArray();
                }
                if (string.IsNullOrWhiteSpace(answer)) throw new NativeChatException("empty_response", "The service completed without an answer. Try again.");
                return new StreamReply(answer, sources, searched);
            }
        }
        signal.ThrowIfCancellationRequested();
        throw new NativeChatException("stream_interrupted", "The connection ended before the answer completed. Try again.");
    }

    private static StreamReply FinalSnapshot(JsonElement response)
    {
        if (!Try(response, "output", out JsonElement output) || output.ValueKind != JsonValueKind.Array)
            return new StreamReply(null!, Array.Empty<NativeSourceAnnotation>(), false);
        if (output.GetArrayLength() > MaxParts) throw TooLarge("response_parts_too_many");
        var text = new StringBuilder(); var sources = new List<NativeSourceAnnotation>(); bool searched = false, found = false;
        foreach (JsonElement item in output.EnumerateArray())
        {
            if (String(item, "type") == "web_search_call" && String(item, "status") == "completed") searched = true;
            if (String(item, "type") != "message" || String(item, "role") != "assistant"
                || !Try(item, "content", out JsonElement content) || content.ValueKind != JsonValueKind.Array) continue;
            if (content.GetArrayLength() > MaxParts) throw TooLarge("response_parts_too_many");
            foreach (JsonElement part in content.EnumerateArray())
            {
                if (String(part, "type") != "output_text") continue;
                string? value = String(part, "text");
                if (value == null) throw InvalidStream();
                if (text.Length + value.Length > MaxText) throw TooLarge();
                if (Try(part, "annotations", out JsonElement rows) && sources.Count < 100)
                    sources.AddRange(CitationRows(rows, text.Length).Where(row => row.End <= text.Length + value.Length).Take(100 - sources.Count));
                text.Append(value); found = true;
            }
        }
        return new StreamReply(found ? text.ToString() : null!, sources, searched);
    }

    private static IEnumerable<NativeSourceAnnotation> CitationRows(JsonElement rows, int offset)
    {
        if (rows.ValueKind != JsonValueKind.Array) yield break;
        foreach (JsonElement row in rows.EnumerateArray().Take(100))
            foreach (NativeSourceAnnotation citation in CitationRow(row, offset)) yield return citation;
    }
    private static IEnumerable<NativeSourceAnnotation> CitationRow(JsonElement row, int offset)
    {
        if (String(row, "type") != "url_citation" || String(row, "url") is not string url || url.Length > 2048
            || !Try(row, "start_index", out JsonElement start) || start.ValueKind != JsonValueKind.Number || !start.TryGetInt32(out int a)
            || !Try(row, "end_index", out JsonElement end) || end.ValueKind != JsonValueKind.Number || !end.TryGetInt32(out int b)
            || a < 0 || b < a || b > MaxText) yield break;
        string title = String(row, "title") ?? "Source";
        yield return new NativeSourceAnnotation(url, title[..Math.Min(title.Length, 240)], offset + a, offset + b);
    }
    private static (int, int) PartKey(JsonElement item) => (Index(item, "output_index"), Index(item, "content_index"));
    private static int Index(JsonElement item, string name) => Try(item, name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int index) && index >= 0 && index < 4096 ? index : throw InvalidStream();
    private static bool Try(JsonElement item, string name, out JsonElement value)
    {
        value = default;
        return item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out value);
    }
    private static string? String(JsonElement item, string name) => Try(item, name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async IAsyncEnumerable<string> EventsAsync(Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken signal)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 4096, leaveOpen: true);
        var buffer = new char[4096]; var line = new StringBuilder(); var data = new StringBuilder();
        int total = 0, events = 0; bool carriageReturn = false, firstCharacter = true;
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), signal).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaxStreamCharacters) throw TooLarge("response_stream_too_large");
            for (int i = 0; i < read; i++)
            {
                char c = buffer[i];
                if (firstCharacter) { firstCharacter = false; if (c == '\uFEFF') continue; }
                if (carriageReturn && c == '\n') { carriageReturn = false; continue; }
                carriageReturn = c == '\r';
                if (c is not ('\n' or '\r'))
                {
                    if (line.Length == MaxEventCharacters) throw TooLarge("response_event_too_large");
                    line.Append(c); continue;
                }
                if (line.Length == 0)
                {
                    if (data.Length == 0) continue;
                    if (++events > 16000) throw TooLarge("response_events_too_many");
                    string value = data.ToString(0, data.Length - 1); data.Clear();
                    yield return value;
                }
                else
                {
                    string value = line.ToString(); line.Clear();
                    if (!value.StartsWith("data:", StringComparison.Ordinal)) continue;
                    string piece = value[5..]; if (piece.StartsWith(' ')) piece = piece[1..];
                    if (data.Length + piece.Length + 1 > MaxEventCharacters) throw TooLarge("response_event_too_large");
                    data.Append(piece).Append('\n');
                }
            }
        }
        // Per SSE framing, an unterminated event is not a completed event.
    }

    private static async Task<string> BoundedBodyAsync(HttpContent content, int maximum, string limitCode, CancellationToken signal)
    {
        if (content.Headers.ContentLength > maximum) throw TooLarge(limitCode);
        using Stream stream = await content.ReadAsStreamAsync(signal).ConfigureAwait(false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var result = new StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), signal).ConfigureAwait(false);
            if (count == 0) return result.ToString();
            if (result.Length + count > maximum) throw TooLarge(limitCode);
            result.Append(buffer, 0, count);
        }
    }
    private static async Task<NativeChatException> HttpErrorAsync(HttpResponseMessage response, bool web, bool plan, CancellationToken signal)
    {
        try
        {
            string text = await BoundedBodyAsync(response.Content, 65536, "response_body_too_large", signal).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 24 });
            return RemoteError(json.RootElement, (int)response.StatusCode, web, plan);
        }
        catch (JsonException) { return RemoteError(default, (int)response.StatusCode, web, plan); }
        catch (DecoderFallbackException) { return RemoteError(default, (int)response.StatusCode, web, plan); }
    }
    private static NativeChatException RemoteError(JsonElement detail, int status, bool web, bool plan)
    {
        for (int depth = 0; depth < 4; depth++)
        {
            if (Try(detail, "error", out JsonElement error) && error.ValueKind == JsonValueKind.Object) detail = error;
            else if (Try(detail, "detail", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object) detail = nested;
            else break;
        }
        string code = String(detail, "error") ?? String(detail, "code") ?? "";
        string param = String(detail, "param") ?? "";
        // Only allowlisted diagnostics survive; unknown error codes can themselves contain private text.
        if ((status is 200 or 400 or 422) && (param is "reasoning" or "reasoning.effort" or "reasoning_effort" or "reasoning[effort]"))
            return new("effort_unavailable", "This model did not accept the selected effort. Refresh the models in GPT chat and choose another effort or Auto.");
        if (web && (code is "unsupported_tool" or "tool_not_supported" or "web_search_not_available" or "unsupported_parameter" or "subscription_sharing_unsupported_capability"
            || param == "tools" || param.StartsWith("tools.", StringComparison.Ordinal) || param.StartsWith("tools[", StringComparison.Ordinal)))
            return new("web_search_unavailable", "Live web search is unavailable for this account or model. Turn off live web search in mod settings to use ordinary GPT answers, or try again later.");
        return code switch
        {
            "subscription_sharing_usage_limit_exceeded" => new("usage_limit", "Your ChatGPT plan allowance or app limit has been reached. Check Manage usage. No API charges were used."),
            "subscription_sharing_usage_unavailable" => new("usage_unavailable", "ChatGPT plan usage is temporarily unavailable. Try again shortly."),
            "subscription_sharing_user_not_eligible" or "subscription_sharing_v2_user_not_eligible" => new("not_eligible", "This account or workspace is not eligible for ChatGPT plan usage."),
            "subscription_sharing_user_unavailable" or "subscription_sharing_v2_user_unavailable" => new("account_unavailable", "Your ChatGPT account or workspace is temporarily unavailable. Try again shortly."),
            "invalid_token" or "invalid_api_key" or "token_expired" => new("invalid_token", plan ? "Your ChatGPT connection was not accepted. Reconnect in mod settings." : "Your OpenAI API key was not accepted. Update it in mod settings."),
            "model_not_found" => new("model_not_found", "The selected model is unavailable for this account. Check your model or connection settings."),
            "subscription_sharing_invalid_user" or "subscription_sharing_v2_invalid_user" or "chatpass_v2_scope_not_authorized" or "chatpass_v2_invalid_authorization_context" => new("permission_denied", "The ChatGPT connection does not authorize this request. Reconnect in mod settings and check the granted permissions."),
            "subscription_sharing_unsupported_capability" or "subscription_sharing_route_not_supported" or "subscription_sharing_v2_route_not_supported" => new("unsupported_request", "This request is unavailable through ChatGPT plan usage."),
            "subscription_sharing_v2_client_not_enabled" => new("app_not_enabled", "This app is not enabled for ChatGPT plan usage. Check its registration in mod settings."),
            _ => status switch
            {
                401 => new("invalid_token", plan ? "Your ChatGPT connection was not accepted. Reconnect in mod settings." : "Your OpenAI API key was not accepted. Update it in mod settings."),
                403 => new("permission_denied", "An account policy or permission restriction prevented this request."),
                429 => new("rate_limit", "Too many requests. Wait a moment before trying again."),
                >= 500 => new("service_unavailable", "The service is temporarily unavailable. Try again shortly."),
                400 or 422 => new("invalid_request", "The service rejected the request format or selected options. Check the model and web search settings."),
                >= 300 and < 400 => new("unexpected_redirect", "The service returned an unexpected redirect. The request was not forwarded."),
                _ => new("request_failed", "The service could not complete this request. Try again.")
            }
        };
    }

    public void Dispose()
    {
        ClearModelCache();
        if (_ownsClient) _client.Dispose();
        // A cancelled catalog request may still be releasing its gate while the
        // mod unloads; do not dispose the semaphore beneath that continuation.
    }
}
