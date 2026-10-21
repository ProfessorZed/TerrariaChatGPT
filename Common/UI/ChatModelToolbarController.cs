#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerrariaChatGPT.Common.Native;

namespace TerrariaChatGPT.Common.UI;

internal enum ChatPickerKind { None, Model, Effort }
internal sealed record ChatPickerChoice(string Value, string Label, bool Selected);
internal delegate bool SaveChatPreferences(string model, string effort, out string message);

/// <summary>UI-thread state; async work returns public metadata only and never mutates UI/config.</summary>
internal sealed class ChatModelToolbarController
{
    private readonly Func<CancellationToken, bool, Task<IReadOnlyList<ChatGptModelOption>>> _load;
    private readonly SaveChatPreferences _save;
    private CancellationTokenSource _stop = new();
    private Task<CatalogResult>? _pending;
    private IReadOnlyList<ChatGptModelOption> _models = Array.Empty<ChatGptModelOption>();
    private int _epoch = int.MinValue;
    private bool _enabled;
    private bool _chatOpen;
    private string _model = "", _effort = "";
    public ChatPickerKind Picker { get; private set; }
    public bool IsOpen => Picker != ChatPickerKind.None;
    public bool Busy => _pending is not null;
    public bool Enabled => _enabled;
    public string Feedback { get; private set; } = "Changes apply to your next question.";
    public bool Error { get; private set; }
    public int Page { get; private set; }
    private sealed record CatalogResult(IReadOnlyList<ChatGptModelOption> Models, string Error);

    public ChatModelToolbarController(Func<CancellationToken, bool, Task<IReadOnlyList<ChatGptModelOption>>> load, SaveChatPreferences save)
    { _load = load; _save = save; }

    public void Update(bool chatOpen, bool enabled, int epoch, string model, string effort)
    {
        if (_epoch != epoch) { Reset(); _epoch = epoch; }
        _model = model ?? ""; _effort = effort ?? "";
        _enabled = enabled; _chatOpen = chatOpen;
        if (!chatOpen || !enabled)
        {
            Close();
            if (!enabled) _models = Array.Empty<ChatGptModelOption>();
            return;
        }
        if (_pending is { IsCompleted: true })
        {
            CatalogResult result = _pending.IsCompletedSuccessfully ? _pending.Result
                : new(Array.Empty<ChatGptModelOption>(), "Model loading stopped. Choose Refresh to try again.");
            _pending = null;
            _models = result.Models;
            Error = result.Error.Length > 0;
            Feedback = Error ? result.Error : "Changes apply to your next question.";
            if (!Error && EffectiveModel is null)
            { Feedback = "Saved model unavailable. Choose another model or Auto."; Error = true; }
            if (!Error && _effort.Length > 0 && EffectiveModel is { } selected && !selected.SupportedReasoningEfforts.Contains(_effort))
            { Feedback = "Saved effort unavailable. Choose a supported effort or Auto."; Error = true; }
            Page = 0;
        }
    }

    public string ModelLabel => _model.Length == 0 ? _models.Count == 0 ? "Auto" : "Auto: " + _models[0].DisplayName
        : _models.FirstOrDefault(model => model.Slug == _model)?.DisplayName ?? _model;
    public string EffortLabel => ChatGptEffortOptions.Label(_effort);
    private ChatGptModelOption? EffectiveModel => _model.Length == 0 ? _models.FirstOrDefault()
        : _models.FirstOrDefault(model => model.Slug == _model);

    public IReadOnlyList<ChatPickerChoice> Choices()
    {
        if (Picker == ChatPickerKind.Model)
            return new[] { new ChatPickerChoice("", "Auto — first available model", _model.Length == 0) }
                .Concat(_models.Select(model => new ChatPickerChoice(model.Slug, model.DisplayName, model.Slug == _model))).ToArray();
        return new[] { new ChatPickerChoice("", "Auto — service default", _effort.Length == 0) }
            .Concat((EffectiveModel?.SupportedReasoningEfforts ?? Array.Empty<string>())
                .Where(ChatGptEffortOptions.IsKnown).Distinct(StringComparer.Ordinal)
                .Select(value => new ChatPickerChoice(value, ChatGptEffortOptions.Label(value), value == _effort))).ToArray();
    }

    public void Toggle(ChatPickerKind kind)
    {
        if (!_chatOpen || !_enabled) return;
        if (Picker == kind) { Close(); return; }
        Picker = kind; Page = 0;
        if (_models.Count == 0 && _pending is null) Load(false);
    }

    public void Refresh() { if (IsOpen && _enabled && _chatOpen && !Busy) Load(true); }
    public void ChangePage(int change, int rows)
    {
        int maximum = Math.Max(0, (Choices().Count - 1) / Math.Max(1, rows));
        Page = Math.Clamp(Page + change, 0, maximum);
    }
    public void FitPage(int rows) => ChangePage(0, rows);

    public void Select(int choiceIndex)
    {
        if (!IsOpen || !_enabled || !_chatOpen) return;
        IReadOnlyList<ChatPickerChoice> choices = Choices();
        if (choiceIndex < 0 || choiceIndex >= choices.Count) return;
        string model = Picker == ChatPickerKind.Model ? choices[choiceIndex].Value : _model;
        string effort = Picker == ChatPickerKind.Effort ? choices[choiceIndex].Value : _effort;
        bool reset = false;
        if (Picker == ChatPickerKind.Model && effort.Length > 0)
        {
            ChatGptModelOption? target = model.Length == 0 ? _models.FirstOrDefault() : _models.FirstOrDefault(row => row.Slug == model);
            if (target is null || !target.SupportedReasoningEfforts.Contains(effort)) { effort = ""; reset = true; }
        }
        if (!_save(model, effort, out string message)) { Feedback = message; Error = true; return; }
        _model = model; _effort = effort;
        Feedback = reset ? "Saved. Effort changed to Auto for this model." : message;
        Error = false;
        Close();
    }

    public void Close()
    {
        Picker = ChatPickerKind.None; Page = 0;
        if (_pending is not null) CancelLoad();
    }
    public void Reset()
    {
        Close(); CancelLoad(); _models = Array.Empty<ChatGptModelOption>();
        Feedback = "Changes apply to your next question."; Error = false;
    }
    private void CancelLoad()
    {
        _stop.Cancel(); _stop.Dispose(); _stop = new CancellationTokenSource(); _pending = null;
    }
    private void Load(bool force)
    {
        Error = false; Feedback = "Loading your account's models...";
        CancellationToken signal = _stop.Token;
        _pending = Task.Run(async () =>
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(signal);
                deadline.CancelAfter(TimeSpan.FromSeconds(35));
                return new CatalogResult(await _load(deadline.Token, force).ConfigureAwait(false), "");
            }
            catch (NativeChatException error) { return new CatalogResult(Array.Empty<ChatGptModelOption>(), error.Message); }
            catch (OperationCanceledException) { return new CatalogResult(Array.Empty<ChatGptModelOption>(), "Model loading stopped. Choose Refresh to retry."); }
            catch { return new CatalogResult(Array.Empty<ChatGptModelOption>(), "Couldn't load models. Check your connection, then Refresh."); }
        }, signal);
    }
}
