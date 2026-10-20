#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TerrariaChatGPT.Common.Services;

public sealed record WebCitation(int Index, string Url, string Title);

/// <summary>Only explicit clicks on validated public HTTP(S) citations can copy a source link.</summary>
public static class WebCitationLinks
{
    public static bool TryGetPublicUri(string? raw, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 2048 || raw.Any(char.IsControl)
            || !Uri.TryCreate(raw, UriKind.Absolute, out Uri? candidate)
            || candidate.Scheme is not ("https" or "http") || candidate.UserInfo.Length != 0
            || candidate.HostNameType != UriHostNameType.Dns || candidate.IsLoopback
            || !candidate.IsDefaultPort) return false;
        string host;
        // Uri.TryCreate accepts some Unicode hosts whose IDN normalization
        // fails later. Optional citation metadata must never discard the answer.
        try { host = candidate.IdnHost.TrimEnd('.').ToLowerInvariant(); }
        catch (UriFormatException) { return false; }
        if (!host.Contains('.') || host.EndsWith(".localhost", StringComparison.Ordinal)
            || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal)
            || host.EndsWith(".home", StringComparison.Ordinal) || host.EndsWith(".lan", StringComparison.Ordinal)) return false;
        uri = candidate;
        return true;
    }

    public static IReadOnlyList<WebCitation> Read(JsonElement root, string answer)
    {
        if (!root.TryGetProperty("citations", out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<WebCitation>();
        var result = new Dictionary<int, WebCitation>();
        var conflicts = new HashSet<int>();
        foreach (JsonElement row in value.EnumerateArray().Take(32))
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("index", out JsonElement index)
                || index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out int number) || number < 1 || number > 24
                || !row.TryGetProperty("url", out JsonElement url) || url.ValueKind != JsonValueKind.String
                || !TryGetPublicUri(url.GetString(), out Uri? target)
                || !answer.Contains("[" + number + "]", StringComparison.Ordinal)) continue;
            string title = row.TryGetProperty("title", out JsonElement label) && label.ValueKind == JsonValueKind.String
                ? label.GetString() ?? "" : "";
            title = new string(title.Where(character => !char.IsControl(character)).Take(180).ToArray()).Trim();
            var source = new WebCitation(number, target!.AbsoluteUri, title.Length == 0 ? target.IdnHost : title);
            if (result.TryGetValue(number, out WebCitation? previous) && previous.Url != source.Url) conflicts.Add(number);
            else result[number] = source;
        }
        return result.Values.Where(row => !conflicts.Contains(row.Index)).OrderBy(row => row.Index).ToArray();
    }
}
