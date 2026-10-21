#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.ModLoader.Config.UI;
using Terraria.UI;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Native;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Common.UI;

/// <summary>Edits only a pending public effort value; never receives credentials.</summary>
public sealed class ChatGptEffortConfigElement : ConfigElement
{
    private const int ClosedHeight = 142;
    private const int ExpandedHeight = 258;
    private IReadOnlyList<ChatGptModelOption> _models = Array.Empty<ChatGptModelOption>();
    private IReadOnlyList<string> _choices = Array.Empty<string>();
    private ChatGptModelOption? _model;
    private CancellationTokenSource _cancellation = new();
    private Task<LoadResult>? _pending;
    private string _feedback = "Load effort choices for the selected ChatGPT model.";
    private string _modelSlug = "";
    private bool _expanded;
    private bool _loaded;
    private bool _error;
    private int _epoch;
    private sealed record LoadResult(IReadOnlyList<ChatGptModelOption> Models, string Error);

    public override void OnBind()
    {
        base.OnBind();
        DrawLabel = false;
        Height.Set(ClosedHeight, 0);
        _epoch = GptAssistantService.Epoch;
        _modelSlug = Pending.ChatGptModel ?? "";
    }

    public override void OnActivate() { ClearCatalog(); base.OnActivate(); }
    public override void OnDeactivate()
    {
        _cancellation.Cancel();
        _pending = null;
        _models = Array.Empty<ChatGptModelOption>();
        _choices = Array.Empty<string>();
        _model = null;
        base.OnDeactivate();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        SynchronizeSelection();
        if (!IsShown()) return;
        if (!CanLoad(out _) && (_loaded || _pending is not null)) ClearCatalog();
        if (_pending is { IsCompleted: true })
        {
            if (_pending.IsCompletedSuccessfully)
            {
                LoadResult result = _pending.Result;
                _models = result.Models;
                _loaded = result.Error.Length == 0;
                _error = !_loaded;
                _feedback = result.Error;
                RefreshChoices();
            }
            else { _error = true; _feedback = "The request stopped. Choose Load efforts to retry."; }
            _pending = null;
        }
        if (!Main.gameMenu && VisibleRectangle().Contains(Main.MouseScreen.ToPoint()))
            Main.LocalPlayer.mouseInterface = true;
    }

    public override void LeftMouseDown(UIMouseEvent evt)
    {
        base.LeftMouseDown(evt);
        SynchronizeSelection();
        Point mouse = evt.MousePosition.ToPoint();
        if (!IsShown() || !VisibleRectangle().Contains(mouse)) return;
        Rectangle area = GetDimensions().ToRectangle();
        if (SelectedRectangle(area).Contains(mouse)) { SetExpanded(!_expanded); return; }
        if (AutoRectangle(area).Contains(mouse)) { Select(""); return; }
        if (LoadRectangle(area).Contains(mouse))
        {
            if (_pending is not null) return;
            if (!CanLoad(out string reason)) { _feedback = reason; _error = true; return; }
            LoadChoices();
            return;
        }
        if (!_expanded || !CanLoad(out _)) return;
        for (int index = 0; index < _choices.Count; index++)
            if (ChoiceRectangle(area, index).Contains(mouse)) { Select(_choices[index]); return; }
    }

    private void SynchronizeSelection()
    {
        if (_epoch != GptAssistantService.Epoch) ClearCatalog();
        string slug = Pending.ChatGptModel ?? "";
        if (_modelSlug == slug) return;
        _modelSlug = slug;
        RefreshChoices();
    }

    private void RefreshChoices()
    {
        _model = _modelSlug.Length == 0 ? _models.FirstOrDefault() : _models.FirstOrDefault(model => model.Slug == _modelSlug);
        _choices = _model?.SupportedReasoningEfforts.Where(ChatGptEffortOptions.IsKnown)
            .Where(effort => effort.Length > 0).Distinct(StringComparer.Ordinal).Take(8).ToArray() ?? Array.Empty<string>();
        if (!_loaded) return;
        _error = false;
        if (_model is null)
        {
            _feedback = "The selected model is unavailable. Choose another model above, or use Auto effort.";
            _error = true;
        }
        else if (Selected.Length > 0 && !_choices.Contains(Selected, StringComparer.Ordinal))
        {
            _feedback = "Your saved effort isn't available for this model. Choose a listed effort or Auto, then Save Config.";
            _error = true;
        }
        else _feedback = _choices.Count == 0 ? "This model offers only Auto effort. The service default is used."
            : "Choices for " + _model.DisplayName + ". Select one, then Save Config.";
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        Rectangle area = GetDimensions().ToRectangle();
        if (area.Width < 30 || !IsShown()) return;
        DrawText(spriteBatch, Label, new(area.X + 10, area.Y + 8), Color.White, 0.8f, area.Width - 20);
        string selected = Selected.Length == 0
            ? "Auto: service default"
            : ChatGptEffortOptions.IsKnown(Selected) ? ChatGptEffortOptions.Label(Selected) : "Saved effort";
        if (_loaded && Selected.Length > 0 && !_choices.Contains(Selected, StringComparer.Ordinal)) selected += " (unavailable)";
        DrawButton(spriteBatch, SelectedRectangle(area), selected + (_expanded ? "  [-]" : "  [+]"), true, true);
        bool canLoad = CanLoad(out string reason);
        DrawButton(spriteBatch, LoadRectangle(area), _pending is not null ? "Loading..." : _loaded ? "Refresh efforts" : "Load efforts", canLoad && _pending is null);
        DrawButton(spriteBatch, AutoRectangle(area), Selected.Length == 0 ? "Auto selected" : "Use Auto", true);
        string feedback = !canLoad ? reason : _pending is not null ? "Checking available efforts; no chat request is sent." : _feedback;
        DrawStatus(spriteBatch, feedback, new(area.X + 10, area.Y + 108), _error && canLoad ? new(255, 183, 165) : new(193, 209, 221), area.Width - 20);
        if (!_expanded) return;
        for (int index = 0; index < _choices.Count; index++)
        {
            string effort = _choices[index];
            DrawButton(spriteBatch, ChoiceRectangle(area, index), ChatGptEffortOptions.Label(effort), canLoad, effort == Selected);
        }
        if (_choices.Count == 0)
            DrawText(spriteBatch, _pending is not null ? "Effort choices will appear here."
                : _loaded ? "Auto is available above." : "Choose Load efforts to see supported choices.",
                new(area.X + 10, area.Y + 151), new(193, 209, 221), 0.60f, area.Width - 20);
    }

    private void LoadChoices()
    {
        if (_cancellation.IsCancellationRequested) ClearCatalog();
        _models = Array.Empty<ChatGptModelOption>();
        _choices = Array.Empty<string>();
        _model = null;
        _loaded = false;
        _error = false;
        CancellationToken signal = _cancellation.Token;
        _pending = Task.Run(() => LoadAsync(signal), signal);
        SetExpanded(true);
    }

    private static async Task<LoadResult> LoadAsync(CancellationToken cancellation)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            return new(await NativeAssistantRuntime.GetModelsAsync(deadline.Token, forceRefresh: true).ConfigureAwait(false), "");
        }
        catch (NativeChatException error) { return new(Array.Empty<ChatGptModelOption>(), error.Message); }
        catch (OperationCanceledException) { return new(Array.Empty<ChatGptModelOption>(), "The request stopped. Choose Load efforts to retry."); }
        catch { return new(Array.Empty<ChatGptModelOption>(), "Couldn't load effort choices. Check your ChatGPT connection and try again."); }
    }

    private void Select(string effort)
    {
        if (Selected != effort) SetObject(effort);
        _feedback = "Use tModLoader's Save Config button to apply this choice.";
        _error = false;
        SetExpanded(false);
    }
    private string Selected => GetObject() as string ?? "";
    private GptClientConfig Pending => Item as GptClientConfig ?? ModContent.GetInstance<GptClientConfig>();
    private bool CanLoad(out string reason)
    {
        GptClientConfig actual = ModContent.GetInstance<GptClientConfig>();
        if (Pending.Provider != GptProvider.ChatGPT || actual.Provider != GptProvider.ChatGPT)
        { reason = "Select ChatGPT plan and save the settings to load effort choices."; return false; }
        if (!Pending.EnableOnlineAnswers || !actual.EnableOnlineAnswers)
        { reason = "Enable online answers and save the settings to load effort choices."; return false; }
        reason = ""; return true;
    }
    private void ClearCatalog()
    {
        _cancellation.Cancel(); _cancellation.Dispose(); _cancellation = new();
        _pending = null; _models = Array.Empty<ChatGptModelOption>(); _choices = Array.Empty<string>(); _model = null;
        _loaded = false; _error = false; _epoch = GptAssistantService.Epoch;
        _modelSlug = Pending.ChatGptModel ?? "";
        _feedback = "Load effort choices for the selected ChatGPT model.";
        SetExpanded(false);
    }
    private void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        int height = expanded ? ExpandedHeight : ClosedHeight;
        Height.Set(height, 0);
        if (Parent is not null) { Parent.Height.Set(height, 0); GetRoot().Recalculate(); }
    }
    private UIElement GetRoot() { UIElement root = this; while (root.Parent is not null) root = root.Parent; return root; }
    private bool IsShown()
    {
        UIState? active = Main.gameMenu ? Main.MenuUI.CurrentState : Main.InGameUI.CurrentState;
        return ReferenceEquals(GetRoot(), active) && VisibleRectangle().Width > 0 && VisibleRectangle().Height > 0;
    }
    private Rectangle VisibleRectangle()
    {
        Rectangle visible = GetDimensions().ToRectangle();
        for (UIElement? parent = Parent; parent is not null; parent = parent.Parent)
            if (parent.OverflowHidden) visible = Rectangle.Intersect(visible, parent.GetDimensions().ToRectangle());
        return visible;
    }
    private static Rectangle SelectedRectangle(Rectangle area) => new(area.X + 8, area.Y + 34, Math.Max(0, area.Width - 16), 32);
    private static Rectangle LoadRectangle(Rectangle area) => new(area.X + 8, area.Y + 74, Math.Max(0, (area.Width - 24) / 2), 28);
    private static Rectangle AutoRectangle(Rectangle area) { Rectangle first = LoadRectangle(area); return new(first.Right + 8, first.Y, Math.Max(0, area.Right - first.Right - 16), first.Height); }
    private static Rectangle ChoiceRectangle(Rectangle area, int index)
    {
        int width = Math.Max(0, (area.Width - 24) / 2);
        return new(area.X + 8 + index % 2 * (width + 8), area.Y + 140 + index / 2 * 28, width, 26);
    }
    private static string SafeText(string text) => new(text.Where(character => !char.IsControl(character)).Take(220).ToArray());
    private static void DrawButton(SpriteBatch batch, Rectangle r, string text, bool enabled, bool selected = false)
    {
        if (r.Width < 3 || r.Height < 3) return;
        Color fill = !enabled ? new(43, 48, 59) : selected ? new(34, 72, 73) : r.Contains(Main.MouseScreen.ToPoint()) ? new(69, 101, 135) : new(40, 64, 98);
        batch.Draw(TextureAssets.MagicPixel.Value, r, selected ? new Color(124, 227, 187) : new Color(117, 147, 180));
        batch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), fill);
        DrawText(batch, text, new(r.X + 8, r.Y + 5), enabled ? Color.White : new(157, 166, 180), 0.66f, r.Width - 16);
    }
    private static void DrawStatus(SpriteBatch batch, string text, Vector2 position, Color color, int width)
    {
        text = SafeText(text);
        if (width <= 0) return;
        const float scale = 0.55f;
        int split = text.Length;
        while (split > 0 && FontAssets.MouseText.Value.MeasureString(text[..split]).X * scale > width) split--;
        if (split < text.Length) { int space = text.LastIndexOf(' ', Math.Max(0, split - 1), split); if (space > 0) split = space; }
        DrawText(batch, text[..split], position, color, scale, width);
        if (split < text.Length) DrawText(batch, text[split..].TrimStart(), position + new Vector2(0, 16), color, scale, width);
    }
    private static void DrawText(SpriteBatch batch, string text, Vector2 position, Color color, float scale, int width)
    {
        if (width <= 0) return;
        text = SafeText(text); var font = FontAssets.MouseText.Value;
        if (font.MeasureString(text).X * scale > width)
        { while (text.Length > 0 && font.MeasureString(text + "...").X * scale > width) text = text[..^1]; text += "..."; }
        if (font.MeasureString(text).X * scale <= width)
            Utils.DrawBorderStringFourWay(batch, font, text, position.X, position.Y, color, Color.Black, Vector2.Zero, scale);
    }
}
