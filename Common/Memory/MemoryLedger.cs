#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TerrariaChatGPT.Common.Sorting;

namespace TerrariaChatGPT.Common.Memory;

/// <summary>
/// Bounded, character-owned preferences. No world reads, disk access or model
/// actions. Suggestions become data only after the latest user message grounds
/// them. Existing entries can resolve an unambiguous edit or preserve earlier
/// preferences; only the latest user message can authorize a new change.
/// </summary>
public sealed class MemoryLedger
{
    public const int MaximumEntries = 32;
    public const int MaximumProposals = 3;
    public const int MaximumText = 240;
    public const int MaximumEvidence = 180;
    private static readonly HashSet<string> Topics = new(StringComparer.Ordinal)
    { "response_style", "playstyle", "accessibility", "language", "building", "organization" };
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    { "ore", "bar", "material", "weapon", "tool", "armor", "accessory", "ammo", "potion", "food", "block", "wall",
      "furniture", "station", "paint", "plant", "seed", "banner", "fishing", "consumable", "pet", "mount", "dye", "vanity", "bosssummon", "wiring", "travel" };
    private static readonly Regex Secret = Pattern(@"https?://|www\.|[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}|\b(?:api[ _-]?key|password|passphrase|access[ _-]?token|refresh[ _-]?token|auth(?:entication|orization)?[ _-]?(?:token|cookie)|session[ _-]?(?:token|cookie)|secret[ _-]?key|private[ _-]?key|recovery[ _-]?(?:code|phrase)|credit[ _-]?card)\b|\b(?:sk-|gh[pousr]_|github_pat_)[a-z0-9_\-]{8,}|\beyJ[a-z0-9_\-]{12,}|\b\d{1,3}(?:\.\d{1,3}){3}\b|[a-z]:\\|\\\\");
    private static readonly Regex Instructions = Pattern(@"\b(?:system|developer)\s+(?:prompt|message|instruction)|\b(?:ignore|override|bypass|disregard)\b.{0,35}\b(?:instruction|rule|safety|restriction|policy|system)|\b(?:execute|run|invoke)\s+(?:code|command|tool|script)|\b(?:spawn|duplicate|teleport|kill)\b.{0,25}\b(?:item|npc|boss|player)|\b(?:send|upload|exfiltrate|reveal)\b.{0,30}\b(?:credential|token|password|secret)|\b(?:memorySuggestions|goalSuggestion|response_json)\b");
    private static readonly Regex ExternalClaim = Pattern(@"\b(?:wiki|website|article|guide|friend|brother|sister|someone|another player|he|she|they)\s+(?:says?|said|told|prefers?|likes?|wants?)\b|\b(?:quote|quoted|pretend|hypothetically|roleplay|example sentence)\b");
    private static readonly Regex DoNotSave = Pattern(@"\b(?:do not|don't|dont|never|stop)\s+(?:save|remember|store|learn)\b|\b(?:forget|delete|remove|erase)\b.{0,25}\b(?:memory|memories|preference|rule)\b|\b(?:just|only|for)\s+(?:this|one)\s+(?:time|answer|reply|question|session|world)\b|\b(?:temporary|temporarily|for now|today only)\b");
    private static readonly Regex Observations = Pattern(@"\b(?:i|we|user|player)\s+(?:(?:currently|now|already)\s+)?(?:have|own|possess|equipped|defeated|killed|beat|rescued|discovered|unlocked|visited|found)\b|\b(?:currently|right now)\s+(?:wear|equip|carry|hold|play|use)|\b(?:boss|npc|chest|biome|ore)\b.{0,30}\b(?:coordinates?|located|position|tiles? away)|\b(?:at|coordinates?|position)\s*[:=]?\s*-?\d+\s*[,/]\s*-?\d+|\b(?:price modifier|happiness|version|hardmode|pre.?hardmode|world seed|health|mana)\s*(?:is|=|:|of)\s*\d|\b(?:i|we|user|player)\s+(?:am|are|is)\s+(?:in|at|wearing|holding|carrying)\b|\b(?:has|have)\s+(?:beaten|defeated|rescued|unlocked|discovered)\b");
    private static readonly Regex OneOffGoal = Pattern(@"\b(?:i|we)\s+(?:want|need|plan|am going)\s+to\s+(?:make|craft|get|find|defeat|beat|kill|obtain|build)\b");
    private static readonly Regex StableCue = Pattern(@"\b(?:prefer|prefers|usually|always|generally|normally|playstyle|favorite|favourite|style|instead|rather|organize|organise|categorize|categorise|classify|sort|store|group|keep|call|label|respond|answer|explain|language|concise|brief|detailed|large text|colorblind|colourblind)\b");
    private static readonly Regex InformationalQuestion = Pattern(@"^\s*(?:what|where|when|why|which|how|do i|did i|does|is|are|can i|could i|should i|would i)\b");
    private static readonly Regex PreferenceIntent = Pattern(@"\b(?:i|we)\s+(?:prefer|like|love|enjoy|dislike|hate|avoid|usually|always|mostly|generally|normally|struggle)\b|\b(?:i|we)\s+(?:play|build|fight)\s+(?:mostly|mainly|usually|primarily)\b|\b(?:my|our)\s+(?:favorite|favourite|playstyle|preferred|style)\b|\b(?:i am|i'm)\s+(?:a|an)\b.{0,25}\b(?:player|builder)\b|\b(?:usually|always|generally|normally|by default|from now on|for future)\b|\b(?:keep|make|give|use)\b.{0,25}\b(?:answers?|replies|responses?|explanations?|text|bullets?|bullet points|plain English)\b|\b(?:answer|respond|explain|speak|write)\b.{0,35}\b(?:plain|simple|short|brief|concise|detail|detailed|English|Arabic|Spanish|French|German|Chinese|Japanese|Korean|Russian|Portuguese|Hindi|Turkish|Italian|language)\b|\b(?:too long|too much text|shorter please|larger text|bigger text|colorblind|colourblind)\b");
    private static readonly Regex SubjectiveSummary = Pattern(@"\b(?:prefers?|likes?|enjoys?|dislikes?|avoids?|usually|normally|mostly|primarily|playstyle|preferred|favorite|favourite|style)\b");
    private static readonly Regex SortingIntent = Pattern(@"\b(?:sort|sorting|put|store|organize|organise|categorize|categorise|recategorize|recategorise|re-categorize|re-categorise|classify|group|keep|belongs?|go(?:es)?|place|send|move|change|update|modify|adjust|switch)\b|\b(?:instead|rather|from now on|for future)\b");
    private static readonly Regex EditIntent = Pattern(@"\b(?:change|update|modify|adjust|switch|make|add)\b|\b(?:instead|also|rather|more|less)\b");
    private static readonly Regex ReferenceTerm = Pattern(@"\b(?:it|that|this|them|those|these|the rule|the preference|my preference|the memory)\b");
    private static readonly Regex ResponseDirective = Pattern(@"\b(?:keep|make|give|use)\b.{0,25}\b(?:answers?|replies|responses?|explanations?|text|bullets?|bullet points|plain English)\b|\b(?:answer|respond|explain|speak|write)\b.{0,35}\b(?:plain|simple|short|brief|concise|detail|detailed|English|Arabic|Spanish|French|German|Chinese|Japanese|Korean|Russian|Portuguese|Hindi|Turkish|Italian|language)\b");
    private static readonly HashSet<string> GroundingStopWords = new("a an the i we my our me us user player prefer prefers like likes enjoy enjoys dislike dislikes avoid avoids usually mostly normally generally primarily style playstyle want wants need needs please use uses using keep keeps always answer answers reply replies respond responses response text explanation explanations to of for in on at with without and or it this that them those is are am be do does not no more less than would could should have has".Split(' '), StringComparer.Ordinal);
    private readonly List<MemoryEntry> _entries = new();
    private UndoState? _undo;
    private sealed record UndoState(string Id, long Revision, MemoryEntry[] Before);

    public bool Enabled { get; private set; } = true;
    public long Revision { get; private set; }
    public IReadOnlyList<MemoryEntry> Entries => Array.AsReadOnly(_entries.ToArray());

    public MemoryChangeBatch? ApplyProposals(IReadOnlyList<MemoryProposal> proposals, string currentQuestion,
        IReadOnlyList<string> recentUserMessages, Func<string, MemoryItemIdentity?> resolveItem)
    {
        if (!Enabled || proposals is null || proposals.Count == 0 || string.IsNullOrWhiteSpace(currentQuestion)
            || currentQuestion.Length > 10000 || DoNotSave.IsMatch(currentQuestion)) return null;

        // Stage the complete bounded batch before changing saved state. A model
        // cannot evict another topic or consume capacity using repeated records.
        MemoryEntry[] before = _entries.ToArray();
        var next = new List<MemoryEntry>(before);
        var descriptions = new List<string>();
        bool updated = false;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var validated = new List<MemoryEntry>();
        foreach (MemoryProposal proposal in proposals.Take(MaximumProposals))
        {
            MemoryEntry? entry;
            try { entry = Validate(proposal, currentQuestion, recentUserMessages, resolveItem, before, now); }
            catch (Exception) { continue; } // Bad optional metadata cannot spoil a normal answer.
            if (entry is null) continue;
            validated.Add(entry);
        }
        foreach (var group in validated.GroupBy(Identity, StringComparer.Ordinal))
        {
            MemoryEntry entry = group.First();
            if (group.Any(candidate => !SamePreference(candidate, entry))) continue;
            string key = group.Key;
            int previousIndex = next.FindIndex(saved => Identity(saved) == key);
            MemoryEntry? previous = previousIndex < 0 ? null : next[previousIndex];
            if (previous is not null && SamePreference(previous, entry)) continue;
            if (previous is null && next.Count >= MaximumEntries) continue;
            if (previous is not null)
            {
                updated = true;
                entry = entry with { Id = previous.Id };
                next[previousIndex] = entry;
            }
            else next.Add(entry);
            descriptions.Add(entry.Text);
        }
        if (descriptions.Count == 0) return null;
        string id = Guid.NewGuid().ToString("N");
        string summary = descriptions.Count == 1 ? descriptions[0] : $"Saved {descriptions.Count} preferences: " + string.Join("; ", descriptions);
        var receipt = new MemoryChangeBatch(id, updated ? "Updated: " + summary : summary);
        _entries.Clear();
        _entries.AddRange(next);
        BumpRevision();
        _undo = new UndoState(id, Revision, before);
        return receipt;
    }

    /// <summary>Only the explicit Remember choices button calls this path. It saves
    /// a reviewed complete batch atomically; conversational suggestions retain
    /// their separate evidence requirements and three-proposal bound.</summary>
    public MemoryChangeBatch? RememberSortingChoices(SortSuggestion suggestion,
        Func<string, MemoryItemIdentity?> resolveItem, out string message)
    {
        message = "No sorting choices were saved.";
        if (!Enabled) { message = "Memory is paused. Resume it in Memory before remembering these choices."; return null; }
        if (suggestion?.Assignments is null || suggestion.Assignments.Count is < 1 or > SortSuggestionValidator.MaximumAssignments)
            return null;
        MemoryEntry[] before = _entries.ToArray();
        var next = new List<MemoryEntry>(before);
        var entries = new List<MemoryEntry>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (SortAssignment choice in suggestion.Assignments)
        {
            if (choice is null || !SortSuggestionValidator.IsItemKey(choice.ItemKey) || !SortSuggestionValidator.IsChestName(choice.ChestName)
                || !TryNormalizeTarget("chest:" + choice.ChestName, out string target)) return null;
            MemoryItemIdentity? item;
            try { item = resolveItem(choice.ItemKey); } catch (Exception) { return null; }
            if (item is null || item.Key != choice.ItemKey) return null;
            string text = "Sort " + item.Name + " into " + choice.ChestName + ".";
            // Explicit chest evidence prevents legacy migration from changing an
            // approved exact label such as Vanity into a broad category on reload.
            string evidence = "Approved sorting choice: " + choice.ChestName + " chest.";
            if (!SafeText(text, MaximumText) || SensitiveOrInstruction(text)
                || !SafeText(evidence, MaximumEvidence) || SensitiveOrInstruction(evidence)) return null;
            entries.Add(new MemoryEntry(Guid.NewGuid().ToString("N"), "sorting", "organization", text, evidence, item.Key, target, now));
        }
        var distinct = new List<MemoryEntry>();
        foreach (var group in entries.GroupBy(Identity, StringComparer.Ordinal))
        {
            MemoryEntry first = group.First();
            if (group.Any(entry => !SamePreference(entry, first)))
            { message = "These choices contain conflicting destinations. Nothing was saved."; return null; }
            distinct.Add(first);
        }
        int newCount = distinct.Count(entry => !before.Any(saved => Identity(saved) == Identity(entry)));
        if (before.Length + newCount > MaximumEntries)
        {
            message = $"These choices need {newCount} memory slots, but only {MaximumEntries - before.Length} are free. Remove some memories and try again; nothing was saved.";
            return null;
        }
        int changed = 0;
        foreach (MemoryEntry entry in distinct)
        {
            int index = next.FindIndex(saved => Identity(saved) == Identity(entry));
            if (index >= 0 && SamePreference(next[index], entry)) continue;
            if (index >= 0) next[index] = entry with { Id = next[index].Id }; else next.Add(entry);
            changed++;
        }
        if (changed == 0) { message = "These sorting choices are already remembered."; return null; }
        string id = Guid.NewGuid().ToString("N");
        message = $"Remembered {changed} sorting {(changed == 1 ? "choice" : "choices")}. Nothing was moved.";
        MemoryChangeBatch receipt = new(id, message);
        _entries.Clear(); _entries.AddRange(next); BumpRevision();
        _undo = new UndoState(id, Revision, before);
        return receipt;
    }

    public bool Undo(string id, out string message)
    {
        UndoState? undo = _undo;
        if (undo is null || undo.Id != id || undo.Revision != Revision)
        { message = "That memory change can no longer be undone. You can remove it in Memory."; return false; }
        _entries.Clear();
        _entries.AddRange(undo.Before);
        BumpRevision();
        message = "Memory change undone.";
        return true;
    }

    public bool Forget(string id)
    {
        if (_entries.RemoveAll(entry => entry.Id == id) == 0) return false;
        BumpRevision();
        return true;
    }

    public void Clear()
    {
        if (_entries.Count == 0) return;
        _entries.Clear();
        BumpRevision();
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled == Enabled) return;
        Enabled = enabled;
        BumpRevision();
    }

    public MemoryEntry? ResolveSorting(string itemKey, IReadOnlyList<string>? groupKeys = null)
        => ResolveSorting(itemKey, groupKeys, out _);

    public MemoryEntry? ResolveSorting(string itemKey, IReadOnlyList<string>? groupKeys, out bool conflict)
    {
        conflict = false;
        if (!Enabled) return null;
        MemoryEntry? exact = _entries.FirstOrDefault(entry => entry.Kind == "sorting" && entry.ItemKey == itemKey);
        if (exact is not null) return exact;
        // Membership comes from actual public item definitions in the sorter,
        // never from model text. Exact item exceptions always take priority.
        MemoryEntry[] matching = groupKeys?.Take(32).Where(IsGroupKey).Distinct(StringComparer.Ordinal).Select(key =>
            _entries.FirstOrDefault(entry => entry.Kind == "sorting" && entry.ItemKey == key)).Where(entry => entry is not null).Cast<MemoryEntry>().ToArray()
            ?? Array.Empty<MemoryEntry>();
        MemoryEntry? specific = matching.FirstOrDefault(entry => entry.ItemKey == "group:grappling_hooks");
        if (specific is not null) return specific;
        if (matching.Select(entry => entry.Target).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
        { conflict = true; return null; }
        return matching.FirstOrDefault();
    }

    public MemoryContext CreateContext() => new(Enabled, Array.AsReadOnly(Enabled
        ? _entries.Select(entry => new MemoryContextEntry(entry.Kind, entry.Topic, entry.Text, entry.ItemKey, entry.Target)).ToArray()
        : Array.Empty<MemoryContextEntry>()));

    /// <summary>Persistence calls this for already bounded, typed records. Does not resolve game content during character loading.</summary>
    public void Restore(bool enabled, long revision, IEnumerable<MemoryEntry> entries)
    {
        var restored = new List<MemoryEntry>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (MemoryEntry saved in entries.Take(MaximumEntries * 4))
        {
            if (restored.Count == MaximumEntries) break;
            MemoryEntry entry = NormalizeLegacySorting(saved);
            if (!ValidSaved(entry) || !keys.Add(Identity(entry)) || !ids.Add(entry.Id)) continue;
            restored.Add(entry);
        }
        _entries.Clear();
        _entries.AddRange(restored);
        Enabled = enabled;
        Revision = Math.Max(0, Math.Min(revision, long.MaxValue - 1));
        _undo = null; // Receipts only exist in the session that displayed them.
    }

    private void BumpRevision()
    {
        Revision = Revision == long.MaxValue ? 1 : Revision + 1;
        _undo = null;
    }

    private static MemoryEntry? Validate(MemoryProposal p, string question, IReadOnlyList<string> userHistory,
        Func<string, MemoryItemIdentity?> resolver, IReadOnlyList<MemoryEntry> savedEntries, DateTimeOffset now)
    {
        if (p is null || !SafeText(p.Text, MaximumText) || !SafeText(p.Evidence, MaximumEvidence)
            || !question.Contains(p.Evidence, StringComparison.Ordinal) || p.Evidence.Trim().Length < 8
            || SensitiveOrInstruction(p.Text) || SensitiveOrInstruction(p.Evidence)
            || ExternalClaim.IsMatch(question) || DoNotSave.IsMatch(p.Evidence)) return null;
        string kind = p.Kind?.Trim().ToLowerInvariant() ?? "";
        string text = p.Text.Trim();
        string evidence = p.Evidence.Trim();
        if (kind == "preference")
        {
            string topic = p.Topic?.Trim().ToLowerInvariant() ?? "";
            MemoryEntry? previous = savedEntries.FirstOrDefault(entry => entry.Kind == "preference" && entry.Topic == topic);
            bool updateIntent = previous is not null && HasPreferenceEditIntent(evidence, topic, savedEntries);
            if (!Topics.Contains(topic) || !string.IsNullOrEmpty(p.Item) || !string.IsNullOrEmpty(p.Target)
                || Observations.IsMatch(text) || Observations.IsMatch(evidence)
                || !SubjectiveSummary.IsMatch(text) || (!PreferenceIntent.IsMatch(evidence) && !updateIntent)
                || !TextIsGrounded(text, evidence)
                || (previous is not null && !RevisionIsGrounded(text, evidence, previous.Text))
                || (InformationalQuestion.IsMatch(question) && !ResponseDirective.IsMatch(question)
                    && !Pattern(@"[.?!;]\s*(?:I|We|My|Our)\b").IsMatch(question))
                || (OneOffGoal.IsMatch(evidence) && !StableCue.IsMatch(evidence))) return null;
            return new MemoryEntry(Guid.NewGuid().ToString("N"), kind, topic, text, evidence, "", "", now);
        }
        if (kind != "sorting" || !SafeText(p.Item, 100) || !TryNormalizeSortingTarget(p.Target, evidence, out string target)
            || !SortingIntent.IsMatch(evidence) || InformationalQuestion.IsMatch(question)) return null;
        MemoryItemIdentity? item = MemoryItemGroups.TryResolve(p.Item) ?? resolver(p.Item.Trim());
        if (item is null || !ValidItemKey(item.Key)) return null;
        if (IsGroupKey(item.Key) && MemoryItemGroups.NamesOnlyOneItem(question, item.Key)) return null;
        // Only the latest six user messages may resolve an explicit follow-up.
        // Assistant suggestions never authorize an item or a destination.
        string[] grounding = userHistory.TakeLast(6).Where(message => message is not null && message.Length <= 10000)
            .Append(question).ToArray();
        bool mentioned = grounding.Any(message => MentionsItem(message, item, p.Item));
        if (!mentioned && !UnambiguousSavedSortingReference(evidence, item.Key, savedEntries)) return null;
        string destination = target.StartsWith("chest:", StringComparison.Ordinal) ? target[6..] : target;
        // The new destination must be grounded in the current message. An old
        // destination cannot silently be reused when the player changes a rule.
        if (!ContainsTerm(question, p.Target) && !ContainsTerm(question, destination)
            && !CategoryMention(question, destination)) return null;
        if (NegatedOrPreviousDestination(question, destination)) return null;
        // Render a locally constructed instruction: model prose cannot conceal a
        // different item/target than the rule that actually affects the sorter.
        text = "Sort " + item.Name + " into " + (target.StartsWith("chest:", StringComparison.Ordinal) ? target[6..] : CategoryLabel(target)) + ".";
        if (!SafeText(text, MaximumText) || SensitiveOrInstruction(text)) return null;
        return new MemoryEntry(Guid.NewGuid().ToString("N"), kind, "organization", text, evidence, item.Key, target, now);
    }

    public static bool TryNormalizeTarget(string? value, out string target)
    {
        target = "";
        if (!SafeText(value, 110) || SensitiveOrInstruction(value!)) return false;
        string trimmed = value!.Trim();
        if (trimmed.StartsWith("chest:", StringComparison.OrdinalIgnoreCase))
        {
            string label = trimmed[6..].Trim();
            if (label.Length is < 1 or > 80 || !SafeText(label, 80)) return false;
            target = "chest:" + label;
            return true;
        }
        string category = trimmed.ToLowerInvariant().Replace("_", "").Replace(" ", "");
        if (!Categories.Contains(category)) return false;
        target = category;
        return true;
    }

    private static bool TryNormalizeSortingTarget(string? value, string evidence, out string target)
    {
        if (!TryNormalizeTarget(value, out target)) return false;
        if (!target.StartsWith("chest:", StringComparison.Ordinal)) return true;
        string label = target[6..];
        if (TryCategoryAlias(label, out string category) && !ExplicitChestLabel(evidence, label)) target = category;
        return true;
    }

    private static bool TryCategoryAlias(string text, out string category)
    {
        string value = text.Trim().ToLowerInvariant();
        if (Categories.Contains(value)) { category = value; return true; }
        value = value switch
        {
            "materials" => "material", "accessories" or "equipment" => "accessory", "blocks" or "building supplies" or "building materials" => "block",
            "paints" => "paint", "armor" or "armour" => "armor", "ammunition" => "ammo", "mobility" or "movement" or "flight" or "teleport" => "travel",
            _ when value.EndsWith('s') => value[..^1],
            _ => value
        };
        category = Categories.Contains(value) ? value : "";
        return category.Length != 0;
    }

    private static bool ExplicitChestLabel(string evidence, string label)
    {
        string escaped = Regex.Escape(label);
        return Pattern(@"\b" + escaped + @"[\""']?\s+chest\b|\bchest\s+(?:(?:called|named|labelled|labeled)\s+)?[\""']?" + escaped + @"\b").IsMatch(evidence);
    }

    private static MemoryEntry NormalizeLegacySorting(MemoryEntry saved)
    {
        if (saved is null || saved.Kind != "sorting" || !SafeText(saved.Evidence, MaximumEvidence)
            || !TryNormalizeSortingTarget(saved.Target, saved.Evidence, out string target) || target == saved.Target) return saved!;
        // Earlier builds sometimes saved 'vanity' as an exact chest label even
        // though the user requested a category. Evidence distinguishes those
        // entries from an explicitly named 'Vanity chest'.
        Match summary = Regex.Match(saved.Text ?? "", @"^Sort (.+?) into .+\.$", RegexOptions.CultureInvariant);
        string displayName = summary.Success ? summary.Groups[1].Value
            : IsGroupKey(saved.ItemKey) ? MemoryItemGroups.TryResolve(saved.ItemKey)!.Name : Regex.Replace(saved.ItemKey?.Split('/').Last() ?? "item", "(?<=[a-z0-9])(?=[A-Z])", " ");
        return saved with { Target = target, Text = "Sort " + displayName + " into " + CategoryLabel(target) + "." };
    }

    private static bool IsGroupKey(string? value) => MemoryItemGroups.IsKnown(value);
    private static bool MentionsItem(string message, MemoryItemIdentity item, string supplied) =>
        ContainsTerm(message, supplied) || ContainsTerm(message, item.Name) || ContainsTerm(message, item.CanonicalName)
        || ContainsTerm(message, item.Key) || (IsGroupKey(item.Key) && MemoryItemGroups.IsMentioned(message, item.Key));

    private static bool NegatedOrPreviousDestination(string message, string destination)
    {
        // A malformed suggestion must not turn the rejected/source category
        // into a saved destination during natural language corrections.
        string target = Regex.Escape(destination);
        if (Pattern(@"\bfrom\s+(?:the\s+)?" + target + @"s?\b[^.;!?]{0,30}\b(?:to|into|as)\b").IsMatch(message)) return true;
        if (Pattern(@"\b(?:not|except|excluding)\s+(?:in(?:to)?\s+)?" + target + @"s?\b").IsMatch(message)) return true;
        foreach (string clause in Regex.Split(message, @"[.;!?]|\bbut\b|\binstead\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (!ContainsTerm(clause, destination) && !CategoryMention(clause, destination)) continue;
            if (Pattern(@"\b(?:do not|don't|dont|never|stop|avoid)\b[^.;!?]{0,60}\b(?:sort|sorting|put|store|group|classify|move|send|keep)\b").IsMatch(clause)) return true;
        }
        return false;
    }

    private static bool UnambiguousSavedSortingReference(string evidence, string itemKey, IReadOnlyList<MemoryEntry> entries)
    {
        if (!ReferenceTerm.IsMatch(evidence)) return false;
        MemoryEntry[] rules = entries.Where(entry => entry.Kind == "sorting").Take(2).ToArray();
        return rules.Length == 1 && rules[0].ItemKey == itemKey;
    }

    private static bool HasPreferenceEditIntent(string evidence, string topic, IReadOnlyList<MemoryEntry> entries)
    {
        if (!EditIntent.IsMatch(evidence)) return false;
        string topicWords = topic switch
        {
            "response_style" => @"\b(?:answer|reply|response|explanation|example|bullet|concise|brief|short|detailed|detail|long|verbose)\w*\b",
            "language" => @"\b(?:language|English|Arabic|Spanish|French|German|Chinese|Japanese|Korean|Russian|Portuguese|Hindi|Turkish|Italian)\b",
            "playstyle" => @"\b(?:playstyle|melee|ranged|mage|summoner|magic|combat|weapons?)\b",
            "accessibility" => @"\b(?:accessibility|colorblind|colourblind|font|text size|contrast)\b",
            "building" => @"\b(?:build|building|architecture|medieval|modern|houses?)\w*\b",
            "organization" => @"\b(?:organization|organisation|organize|organise|storage|chests?|categories)\b",
            _ => "(?!)"
        };
        if (Pattern(topicWords).IsMatch(evidence)) return true;
        MemoryEntry[] preferences = entries.Where(entry => entry.Kind == "preference").Take(2).ToArray();
        return ReferenceTerm.IsMatch(evidence) && preferences.Length == 1 && preferences[0].Topic == topic;
    }

    private static bool RevisionIsGrounded(string text, string evidence, string previous)
    {
        // Keeping earlier details is valid, but sharing one old word must not
        // let the model invent an unrelated new preference during an edit.
        HashSet<string> allowed = GroundingTerms(previous);
        allowed.UnionWith(GroundingTerms(evidence));
        return GroundingTerms(text).All(allowed.Contains);
    }

    private static bool ValidSaved(MemoryEntry? entry)
    {
        if (entry is null || !Guid.TryParseExact(entry.Id, "N", out _) || !SafeText(entry.Text, MaximumText)
            || !SafeText(entry.Evidence, MaximumEvidence) || SensitiveOrInstruction(entry.Text)
            || SensitiveOrInstruction(entry.Evidence) || entry.UpdatedUtc.Year < 2020
            || entry.UpdatedUtc > DateTimeOffset.UtcNow.AddDays(1)) return false;
        if (entry.Kind == "preference") return Topics.Contains(entry.Topic) && entry.ItemKey == "" && entry.Target == ""
            && !Observations.IsMatch(entry.Text) && !Observations.IsMatch(entry.Evidence)
            && SubjectiveSummary.IsMatch(entry.Text) && (PreferenceIntent.IsMatch(entry.Evidence) || EditIntent.IsMatch(entry.Evidence))
            && TextIsGrounded(entry.Text, entry.Evidence);
        return entry.Kind == "sorting" && entry.Topic == "organization" && ValidItemKey(entry.ItemKey)
            && TryNormalizeTarget(entry.Target, out string normalized) && normalized == entry.Target;
    }

    private static bool ValidItemKey(string? value) => value is not null && (IsGroupKey(value) || (value.Length is > 2 and <= 180
        && Regex.IsMatch(value, @"^[A-Za-z0-9_]+/[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)));
    private static bool SafeText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum
        && !value.Any(character => char.IsControl(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format
            || character is '[' or ']' or '{' or '}' or '<' or '>');
    private static bool SensitiveOrInstruction(string value) => Secret.IsMatch(value) || Instructions.IsMatch(value);
    private static string Identity(MemoryEntry entry) => entry.Kind == "sorting" ? "sorting:" + entry.ItemKey : "preference:" + entry.Topic;
    private static bool SamePreference(MemoryEntry previous, MemoryEntry entry) => previous.Kind == entry.Kind
        && previous.Topic == entry.Topic && previous.ItemKey == entry.ItemKey && previous.Target == entry.Target
        && (entry.Kind == "sorting" || string.Equals(previous.Text, entry.Text, StringComparison.OrdinalIgnoreCase));
    private static Regex Pattern(string expression) => new(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static bool TextIsGrounded(string text, string evidence)
    {
        HashSet<string> source = GroundingTerms(evidence);
        return GroundingTerms(text).Any(source.Contains);
    }
    private static HashSet<string> GroundingTerms(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}]+")
        .Select(match => match.Value).Where(value => !GroundingStopWords.Contains(value)).Select(value => value switch
        {
            "brief" or "concise" or "shorter" => "short",
            "simple" or "plain" or "simpler" => "plain",
            "detailed" or "detail" => "detail",
            "colourblind" => "colorblind",
            _ => value.Length > 4 && value.EndsWith('s') ? value[..^1] : value
        }).ToHashSet(StringComparer.Ordinal);
    private static bool ContainsTerm(string sentence, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;
        return Regex.IsMatch(sentence, @"(?<![\p{L}\p{N}_])" + Regex.Escape(term.Trim()) + @"(?![\p{L}\p{N}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    }
    private static bool CategoryMention(string sentence, string target) => target switch
    {
        "bosssummon" => Pattern(@"\bboss\s+(?:summons?|summoning)\b").IsMatch(sentence),
        "accessory" => ContainsTerm(sentence, "accessories") || ContainsTerm(sentence, "equipment"),
        "ammo" => ContainsTerm(sentence, "ammunition"),
        "block" => ContainsTerm(sentence, "blocks") || ContainsTerm(sentence, "building supplies") || ContainsTerm(sentence, "building materials"),
        "plant" => ContainsTerm(sentence, "plants") || ContainsTerm(sentence, "herbs"),
        "dye" => ContainsTerm(sentence, "dyes"),
        "travel" => ContainsTerm(sentence, "mobility") || ContainsTerm(sentence, "movement") || ContainsTerm(sentence, "flight") || ContainsTerm(sentence, "teleport"),
        _ => ContainsTerm(sentence, target + "s")
    };
    private static string CategoryLabel(string target) => target switch
    {
        "bosssummon" => "Boss Summons", "accessory" => "Accessories", "block" => "Building Blocks",
        "ammo" => "Ammo", "food" => "Food", "furniture" => "Furniture", "fishing" => "Fishing",
        "paint" => "Paint", "vanity" => "Vanity", "wiring" => "Wiring", "armor" => "Armor", "travel" => "Travel",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(target) + "s"
    };
}
