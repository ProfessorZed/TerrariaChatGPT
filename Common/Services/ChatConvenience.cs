#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TerrariaChatGPT.Common.Services;

public sealed record ConversationTurn(string Role, string Content);

/// <summary>Bounds the new, observation-free AI conversation. Callers supply only verified chat exchanges.</summary>
public static class ChatConversationContext
{
    public const int MaxMessages = 12;
    public const int MaxMessageCharacters = 2000;
    public const int MaxTotalCharacters = 12000;

    public static IReadOnlyList<ConversationTurn> HistoryForRequest(IEnumerable<ConversationTurn> turns)
    {
        var selected = new List<ConversationTurn>();
        int characters = 0;
        foreach (ConversationTurn turn in turns.Reverse())
        {
            if (turn.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(turn.Content)) continue;
            string content = turn.Content[..Math.Min(turn.Content.Length, MaxMessageCharacters)];
            if (selected.Count == MaxMessages || characters + content.Length > MaxTotalCharacters) break;
            selected.Add(new ConversationTurn(turn.Role, content));
            characters += content.Length;
        }
        selected.Reverse();
        // A cropped context never begins with an orphaned assistant response.
        return selected.SkipWhile(turn => turn.Role != "user").ToArray();
    }
}

/// <summary>Small, world-scoped convenience helpers. No game or network access.</summary>
public static class FollowUpContext
{
    private const string ItemTail = @"(?=$|\s+(?:with|using|from|in|on|at|without|yet|now|again|near|for|after|before|while)\b)";
    private const string DescriptionTail = @"(?=$|\s+(?:do|work|help|used|useful|made|crafted|obtained|found|with|using|from|in|on|at|without|yet|now|again)\b)";
    private static readonly Regex FollowUp = new(
        @"^(?:(?:and )?(?:where|how) (?:can|do|could) i (?:find|get|obtain|craft|make|use|equip|buy|sell) (?:it|that|this|them|those|one)" + ItemTail
        + @"|(?:and )?(?:can|could|should) i (?:craft|make|use|equip|buy|sell|get) (?:it|that|this|them|one)" + ItemTail
        + @"|(?:and )?(?:what|which)(?: (?:items?|things?))? (?:can|could) i (?:make|craft|build|create) (?:from|with|using|out of) (?:it|that|this|them|those)$"
        + @"|(?:and )?(?:do|did) i have (?:it|that|this|one|any)$|(?:and )?what (?:am i missing|do i (?:still )?need)(?: for (?:it|that|this))?$|(?:and )?(?:what|how) (?:is|does) (?:it|that|this)" + DescriptionTail + @")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Expand(string question, string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 150 || question.Length > 220)
            return question;
        string normalized = Regex.Replace(question.Trim().TrimEnd('?', '.', '!'), @"\s+", " ");
        if (Regex.IsMatch(normalized, @"\bmake it (?:rain|snow|stop|night|day|hardmode)\b", RegexOptions.IgnoreCase))
            return question;
        if (!FollowUp.IsMatch(normalized))
            return question;
        // This is a public item name, never a remembered location or chest label.
        return question + " (About " + subject + ")";
    }

    public static IReadOnlyList<ConversationTurn> HistoryForRequest(IEnumerable<ConversationTurn> turns, bool shareObservations)
    {
        // Old answers can contain private counts. Disabling sharing therefore
        // removes all previous conversation, as well as the current snapshot.
        if (!shareObservations)
            return Array.Empty<ConversationTurn>();
        return turns.Where(turn => turn.Role is "user" or "assistant")
            .Where(turn => !turn.Content.TrimStart().StartsWith('/'))
            .TakeLast(6).SkipWhile(turn => turn.Role != "user")
            .Select(turn => turn with { Content = turn.Content[..Math.Min(turn.Content.Length, 800)] })
            .ToArray();
    }
}

public sealed class QuestionRecall
{
    private readonly List<string> _questions = new();
    private int _position = -1;
    private string _draft = "";

    public void Record(string question)
    {
        question = question.Trim();
        if (question.Length > 0 && !question.StartsWith('/') && (_questions.Count == 0 || _questions[^1] != question))
        {
            _questions.Add(question[..Math.Min(question.Length, 1000)]);
            if (_questions.Count > 30) _questions.RemoveAt(0);
        }
        Edited();
    }

    public string Previous(string draft)
    {
        if (_questions.Count == 0) return draft;
        if (_position < 0) { _draft = draft; _position = _questions.Count; }
        _position = Math.Max(0, _position - 1);
        return _questions[_position];
    }

    public string Next(string draft)
    {
        if (_position < 0) return draft;
        if (++_position < _questions.Count) return _questions[_position];
        string restored = _draft;
        Edited();
        return restored;
    }

    public void Edited() { _position = -1; _draft = ""; }
    public void Clear() { _questions.Clear(); Edited(); }
}
