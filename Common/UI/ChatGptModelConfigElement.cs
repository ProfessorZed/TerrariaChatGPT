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

/// <summary>A pending-config model picker. The UI receives names and slugs, never credentials.</summary>
public sealed class ChatGptModelConfigElement : ConfigElement
{
    private const int PageSize = 4;
    private const int ClosedHeight = 142;
    private const int ExpandedHeight = 294;
    private IReadOnlyList<ChatGptModelOption> _models = Array.Empty<ChatGptModelOption>();
    private CancellationTokenSource _cancellation = new();
    private Task<LoadResult>? _pending;
    private string _feedback = "Load the models available to your connected ChatGPT account.";
    private bool _error;
    private bool _expanded;
    private bool _loaded;
    private int _page;
    private int _epoch;

    private sealed record LoadResult(IReadOnlyList<ChatGptModelOption> Models, string Error);

    public override void OnBind()
    {
        base.OnBind();
        DrawLabel = false;
        Height.Set(ClosedHeight, 0);
        _epoch = GptAssistantService.Epoch;
    }

    public override void OnActivate()
    {
        ClearCatalog();
        base.OnActivate();
    }

    public override void OnDeactivate()
    {
        _cancellation.Cancel();
        _pending = null;
        _models = Array.Empty<ChatGptModelOption>();
        base.OnDeactivate();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (_epoch != GptAssistantService.Epoch)
            ClearCatalog();
        if (!IsShown()) return;
        if (!CanLoad(out _) && (_pending is not null || _loaded))
            ClearCatalog();
        if (_pending is { IsCompleted: true })
        {
            if (_pending.IsCompletedSuccessfully)
            {
                LoadResult result = _pending.Result;
                _models = result.Models;
                _loaded = result.Error.Length == 0;
                _error = !_loaded;
                _feedback = _loaded
                    ? "Choose a model below, then use tModLoader's Save Config button."
                    : result.Error;
                if (_loaded && Selected.Length > 0 && !_models.Any(model => model.Slug == Selected))
                {
                    _feedback = "Your saved model isn't available to this account. Choose another model or Auto, then Save Config.";
                    _error = true;
                }
                _page = SelectedPage();
            }
            else
            {
                _feedback = "The model-list request was cancelled. Choose Load models to retry.";
                _error = true;
            }
            _pending = null;
        }
        if (!Main.gameMenu && VisibleRectangle().Contains(Main.MouseScreen.ToPoint()))
            Main.LocalPlayer.mouseInterface = true;
    }

    public override void LeftMouseDown(UIMouseEvent evt)
    {
        base.LeftMouseDown(evt);
        Point mouse = evt.MousePosition.ToPoint();
        if (!IsShown() || !VisibleRectangle().Contains(mouse)) return;
        Rectangle area = GetDimensions().ToRectangle();
        if (SelectedRectangle(area).Contains(mouse))
        {
            SetExpanded(!_expanded);
            return;
        }
        if (LoadRectangle(area).Contains(mouse))
        {
            if (_pending is null && CanLoad(out string reason))
                LoadModels();
            else if (_pending is null)
            {
                CanLoad(out reason);
                _feedback = reason;
                _error = true;
            }
            return;
        }
        if (AutoRectangle(area).Contains(mouse))
        {
            Select("");
            return;
        }
        if (!_expanded) return;
        for (int row = 0; row < PageSize; row++)
        {
            int index = _page * PageSize + row;
            if (index < _models.Count && RowRectangle(area, row).Contains(mouse))
            {
                Select(_models[index].Slug);
                return;
            }
        }
        if (_page > 0 && PreviousRectangle(area).Contains(mouse)) _page--;
        else if (_page + 1 < PageCount && NextRectangle(area).Contains(mouse)) _page++;
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        Rectangle area = GetDimensions().ToRectangle();
        if (area.Width < 30 || !IsShown()) return;
        DrawText(spriteBatch, Label, new Vector2(area.X + 10, area.Y + 8), Color.White, 0.8f, area.Width - 20);
        DrawButton(spriteBatch, SelectedRectangle(area), SelectedLabel() + (_expanded ? "  [-]" : "  [+]"), true, true);
        bool canLoad = CanLoad(out string reason);
        DrawButton(spriteBatch, LoadRectangle(area), _pending is not null ? "Loading..." : _loaded ? "Refresh models" : "Load models", canLoad && _pending is null);
        DrawButton(spriteBatch, AutoRectangle(area), Selected.Length == 0 ? "Auto selected" : "Use Auto", true);
        string feedback = !canLoad ? reason : _pending is not null
            ? "Checking available models; no chat request is sent." : _feedback;
        DrawStatus(spriteBatch, feedback, new Vector2(area.X + 10, area.Y + 108),
            _error && canLoad ? new Color(255, 183, 165) : new Color(193, 209, 221), area.Width - 20);
        if (!_expanded) return;
        if (_models.Count == 0)
        {
            DrawText(spriteBatch, _pending is not null ? "The model list will appear here." : "Choose Load models to see your account's choices.",
                new Vector2(area.X + 10, area.Y + 151), new Color(193, 209, 221), 0.62f, area.Width - 20);
        }
        for (int row = 0; row < PageSize; row++)
        {
            int index = _page * PageSize + row;
            if (index >= _models.Count) break;
            ChatGptModelOption option = _models[index];
            bool selected = option.Slug == Selected;
            DrawButton(spriteBatch, RowRectangle(area, row), (selected ? "* " : "") + option.DisplayName, true, selected);
        }
        DrawButton(spriteBatch, PreviousRectangle(area), "< Previous", _page > 0);
        DrawButton(spriteBatch, NextRectangle(area), "Next >", _page + 1 < PageCount);
        Rectangle page = PageRectangle(area);
        DrawText(spriteBatch, $"{_page + 1} / {PageCount}", new Vector2(page.X + 4, page.Y + 6), new Color(193, 209, 221), 0.62f, page.Width - 8);
    }

    private void LoadModels()
    {
        if (_cancellation.IsCancellationRequested) ClearCatalog();
        _models = Array.Empty<ChatGptModelOption>();
        _loaded = false;
        _page = 0;
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
            IReadOnlyList<ChatGptModelOption> models = await NativeAssistantRuntime.GetModelsAsync(deadline.Token, forceRefresh: true).ConfigureAwait(false);
            return new(models, "");
        }
        catch (NativeChatException error) { return new(Array.Empty<ChatGptModelOption>(), error.Message); }
        catch (OperationCanceledException) { return new(Array.Empty<ChatGptModelOption>(), "The model-list request stopped. Choose Load models to retry."); }
        catch { return new(Array.Empty<ChatGptModelOption>(), "Couldn't load ChatGPT models. Check the connection above and try again."); }
    }

    private void Select(string slug)
    {
        if (Selected != slug) SetObject(slug);
        _feedback = "Use tModLoader's Save Config button to apply this choice.";
        _error = false;
        SetExpanded(false);
    }

    private string Selected => GetObject() as string ?? "";
    private int PageCount => Math.Max(1, (_models.Count + PageSize - 1) / PageSize);

    private string SelectedLabel()
    {
        string slug = Selected;
        if (slug.Length == 0)
            return _models.Count > 0 ? "Auto: " + _models[0].DisplayName : "Auto: first available model";
        ChatGptModelOption? option = _models.FirstOrDefault(model => model.Slug == slug);
        return option?.DisplayName ?? SafeText(slug) + (_loaded ? " (unavailable)" : " (saved)");
    }

    private int SelectedPage()
    {
        for (int i = 0; i < _models.Count; i++)
            if (_models[i].Slug == Selected) return i / PageSize;
        return 0;
    }

    private bool CanLoad(out string reason)
    {
        GptClientConfig actual = ModContent.GetInstance<GptClientConfig>();
        GptClientConfig pending = Item as GptClientConfig ?? actual;
        if (pending.Provider != GptProvider.ChatGPT || actual.Provider != GptProvider.ChatGPT)
        {
            reason = "Select ChatGPT plan and save the settings to load account models.";
            return false;
        }
        if (!pending.EnableOnlineAnswers || !actual.EnableOnlineAnswers)
        {
            reason = "Enable online answers and save the settings to load models.";
            return false;
        }
        reason = "";
        return true;
    }

    private void ClearCatalog()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
        _pending = null;
        _models = Array.Empty<ChatGptModelOption>();
        _loaded = false;
        _error = false;
        _page = 0;
        _epoch = GptAssistantService.Epoch;
        _feedback = "Load the models available to your connected ChatGPT account.";
        SetExpanded(false);
    }

    private void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        int height = expanded ? ExpandedHeight : ClosedHeight;
        Height.Set(height, 0);
        // tModLoader wraps each config editor in a same-height sortable element.
        if (Parent is not null)
        {
            Parent.Height.Set(height, 0);
            GetRoot().Recalculate();
        }
    }

    private UIElement GetRoot()
    {
        UIElement root = this;
        while (root.Parent is not null) root = root.Parent;
        return root;
    }

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
    private static Rectangle AutoRectangle(Rectangle area)
    {
        Rectangle first = LoadRectangle(area);
        return new(first.Right + 8, first.Y, Math.Max(0, area.Right - first.Right - 16), first.Height);
    }
    private static Rectangle RowRectangle(Rectangle area, int row) => new(area.X + 8, area.Y + 140 + row * 28, Math.Max(0, area.Width - 16), 26);
    private static Rectangle PreviousRectangle(Rectangle area) => new(area.X + 8, area.Y + 258, Math.Max(0, (area.Width - 32) / 3), 28);
    private static Rectangle PageRectangle(Rectangle area)
    {
        Rectangle previous = PreviousRectangle(area);
        return new(previous.Right + 8, previous.Y, previous.Width, previous.Height);
    }
    private static Rectangle NextRectangle(Rectangle area)
    {
        Rectangle page = PageRectangle(area);
        return new(page.Right + 8, page.Y, Math.Max(0, area.Right - page.Right - 16), page.Height);
    }

    private static void DrawButton(SpriteBatch spriteBatch, Rectangle rectangle, string text, bool enabled, bool selected = false)
    {
        if (rectangle.Width < 3 || rectangle.Height < 3) return;
        bool hovered = enabled && rectangle.Contains(Main.MouseScreen.ToPoint());
        Color fill = !enabled ? new Color(43, 48, 59) : selected
            ? new Color(34, 72, 73) : hovered ? new Color(69, 101, 135) : new Color(40, 64, 98);
        spriteBatch.Draw(TextureAssets.MagicPixel.Value, rectangle, selected ? new Color(124, 227, 187) : new Color(117, 147, 180));
        spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(rectangle.X + 1, rectangle.Y + 1, rectangle.Width - 2, rectangle.Height - 2), fill);
        DrawText(spriteBatch, text, new Vector2(rectangle.X + 8, rectangle.Y + 5),
            enabled ? Color.White : new Color(157, 166, 180), 0.66f, rectangle.Width - 16);
    }

    private static string SafeText(string text) => new(text.Where(character => !char.IsControl(character)).Take(220).ToArray());

    private static void DrawStatus(SpriteBatch spriteBatch, string text, Vector2 position, Color color, int width)
    {
        text = SafeText(text);
        if (width <= 0) return;
        const float scale = 0.55f;
        var font = FontAssets.MouseText.Value;
        int split = text.Length;
        while (split > 0 && font.MeasureString(text[..split]).X * scale > width) split--;
        if (split < text.Length)
        {
            int space = text.LastIndexOf(' ', Math.Max(0, split - 1), split);
            if (space > 0) split = space;
        }
        DrawText(spriteBatch, text[..split], position, color, scale, width);
        if (split < text.Length)
            DrawText(spriteBatch, text[split..].TrimStart(), position + new Vector2(0, 16), color, scale, width);
    }

    private static void DrawText(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale, int width)
    {
        if (width <= 0) return;
        text = SafeText(text);
        var font = FontAssets.MouseText.Value;
        if (font.MeasureString(text).X * scale > width)
        {
            while (text.Length > 0 && font.MeasureString(text + "...").X * scale > width)
                text = text[..^1];
            text += "...";
        }
        if (font.MeasureString(text).X * scale > width) return;
        // DrawBorderStringFourWay renders literal glyphs, not Terraria chat tags.
        Utils.DrawBorderStringFourWay(spriteBatch, font, text, position.X, position.Y, color, Color.Black, Vector2.Zero, scale);
    }
}
