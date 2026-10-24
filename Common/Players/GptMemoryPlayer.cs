#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Memory;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.Sorting;

namespace TerrariaChatGPT.Common.Players;

/// <summary>
/// Stable preferences follow this character through worlds in the ordinary
/// character save. No separate process, credential store or multiplayer sync.
/// </summary>
public sealed class GptMemoryPlayer : ModPlayer
{
    private MemoryLedger _ledger = new();
    private bool IsLocal => !Main.dedServ && !Main.gameMenu && Player.whoAmI == Main.myPlayer;
    public bool Enabled => IsLocal && _ledger.Enabled;
    public long Revision => _ledger.Revision;
    public IReadOnlyList<MemoryEntry> Entries => IsLocal ? _ledger.Entries : Array.Empty<MemoryEntry>();

    public override void Initialize() => _ledger = new MemoryLedger();

    public override ModPlayer Clone(Player newEntity)
    {
        var clone = (GptMemoryPlayer)base.Clone(newEntity);
        clone._ledger = new MemoryLedger();
        clone._ledger.Restore(_ledger.Enabled, _ledger.Revision, _ledger.Entries);
        return clone;
    }

    public MemoryChangeBatch? ApplyProposals(IReadOnlyList<MemoryProposal> proposals, string currentQuestion,
        IReadOnlyList<ConversationTurn> history)
    {
        if (!IsLocal) return null;
        string[] userMessages = history.Where(turn => turn.Role == "user").TakeLast(6).Select(turn => turn.Content).ToArray();
        return _ledger.ApplyProposals(proposals, currentQuestion, userMessages, ResolveItem);
    }

    public bool Undo(string id, out string message)
    {
        if (!IsLocal) { message = "Enter a world to manage this character's memory."; return false; }
        return _ledger.Undo(id, out message);
    }

    internal MemoryChangeBatch? RememberSortingChoices(SortSuggestion suggestion, out string message)
    {
        if (!IsLocal) { message = "Enter a world to remember this character's sorting choices."; return null; }
        return _ledger.RememberSortingChoices(suggestion, ResolveItem, out message);
    }

    public bool Forget(string id) => IsLocal && _ledger.Forget(id);
    public void Clear() { if (IsLocal) _ledger.Clear(); }
    public void SetEnabled(bool enabled) { if (IsLocal) _ledger.SetEnabled(enabled); }
    public MemoryContext CreateContext() => IsLocal ? _ledger.CreateContext() : new(false, Array.Empty<MemoryContextEntry>());
    public MemoryEntry? ResolveSorting(string itemKey, IReadOnlyList<string>? groupKeys = null) => IsLocal ? _ledger.ResolveSorting(itemKey, groupKeys) : null;
    public MemoryEntry? ResolveSorting(string itemKey, IReadOnlyList<string>? groupKeys, out bool conflict)
    {
        conflict = false;
        return IsLocal ? _ledger.ResolveSorting(itemKey, groupKeys, out conflict) : null;
    }

    public override void SaveData(TagCompound tag)
    {
        tag["version"] = 1;
        tag["enabled"] = _ledger.Enabled;
        tag["revision"] = _ledger.Revision.ToString(CultureInfo.InvariantCulture);
        tag["entries"] = _ledger.Entries.Select(entry => new TagCompound
        {
            ["id"] = entry.Id, ["kind"] = entry.Kind, ["topic"] = entry.Topic,
            ["text"] = entry.Text, ["evidence"] = entry.Evidence,
            ["itemKey"] = entry.ItemKey, ["target"] = entry.Target,
            ["updated"] = entry.UpdatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        }).ToList();
    }

    public override void LoadData(TagCompound tag)
    {
        Initialize();
        try
        {
            if (!tag.ContainsKey("version")) return; // First load before this feature existed.
            if (tag.GetInt("version") != 1) { _ledger.SetEnabled(false); return; }
            bool enabled = tag.GetBool("enabled");
            long.TryParse(tag.GetString("revision"), NumberStyles.None, CultureInfo.InvariantCulture, out long revision);
            var entries = new List<MemoryEntry>();
            foreach (TagCompound saved in tag.GetList<TagCompound>("entries").Take(MemoryLedger.MaximumEntries * 4))
            {
                try
                {
                    if (!DateTimeOffset.TryParseExact(saved.GetString("updated"), "O", CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out DateTimeOffset updated)) continue;
                    entries.Add(new MemoryEntry(saved.GetString("id"), saved.GetString("kind"), saved.GetString("topic"),
                        saved.GetString("text"), saved.GetString("evidence"), saved.GetString("itemKey"), saved.GetString("target"), updated));
                }
                catch (Exception) { /* One damaged entry must not prevent this character from loading. */ }
            }
            _ledger.Restore(enabled, revision, entries);
        }
        catch (Exception)
        {
            // Invalid save shapes do not enable automatic learning accidentally.
            _ledger = new MemoryLedger();
            _ledger.SetEnabled(false);
        }
    }

    private static MemoryItemIdentity? ResolveItem(string supplied)
    {
        // This catalogue contains public definitions, not inventory or world
        // instances. Never resolve an approximate word to the first matching item.
        string wanted = Normalize(supplied);
        Item[] matches = ContentSamples.ItemsByType.Values.Where(item => !item.IsAir)
            .Where(item => Normalize(GameReferenceFacts.Key(item)) == wanted || Normalize(item.Name) == wanted
                || Normalize(CanonicalName(item)) == wanted).Take(2).ToArray();
        if (matches.Length != 1) return null;
        Item found = matches[0];
        // Use the existing reference resolver for display-name requests, while
        // exact catalogue matching above prevents its broad question matching
        // from turning an ambiguous phrase into a lasting rule.
        if (!supplied.Contains('/') && GameReferenceFacts.ResolveItem(supplied)?.type != found.type) return null;
        return new MemoryItemIdentity(GameReferenceFacts.Key(found), found.Name, CanonicalName(found));
    }

    private static string CanonicalName(Item item)
    {
        string key = GameReferenceFacts.Key(item).Split('/').Last();
        return Regex.Replace(Regex.Replace(key, "(?<=[a-z0-9])(?=[A-Z])", " "), @"\b(\w+)of\b", "$1 of");
    }
    private static string Normalize(string value) => Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant();
}
