#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Native;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Common.UI;

/// <summary>Draws and handles only the GPT model menus. It never edits the chat draft.</summary>
internal sealed class ChatModelToolbar
{
    private readonly ChatModelToolbarController _controller = new(NativeAssistantRuntime.GetModelsAsync, GptChatPreferences.TrySave);
    private GptChatLayout _drawnLayout;
    private string _lastFeedback = "";
    private long _statusUntil;
    public bool IsOpen => _controller.IsOpen;
    public string Status
    {
        get
        {
            if (_lastFeedback != _controller.Feedback)
            {
                _lastFeedback = _controller.Feedback;
                _statusUntil = Environment.TickCount64 + 8000;
            }
            return !_controller.IsOpen && Environment.TickCount64 < _statusUntil
                && (_controller.Error || _controller.Feedback.StartsWith("Saved", StringComparison.Ordinal))
                ? _controller.Feedback : "";
        }
    }

    public void Update(bool chatOpen, GptClientConfig config, int epoch) => _controller.Update(chatOpen,
        config.Provider == GptProvider.ChatGPT && config.EnableOnlineAnswers, epoch, config.ChatGptModel, config.ChatGptEffort);
    public void Reset() { _controller.Reset(); _lastFeedback = ""; _statusUntil = 0; }
    public void Close() => _controller.Close();

    // True means that this event must never reach the transcript's goals/links.
    public bool HandleInput(GptChatLayout layout, Point mouse, bool clicked, int wheel, bool escape, int pageDelta)
    {
        if (_controller.IsOpen && escape) { _controller.Close(); return true; }
        if (_drawnLayout != layout)
        {
            // Do not accept hit rectangles from before a resize/UI-scale change.
            if (clicked || wheel != 0) return _controller.IsOpen;
        }
        if (clicked && layout.ModelSelector.Contains(mouse)) { _controller.Toggle(ChatPickerKind.Model); return true; }
        if (clicked && layout.EffortSelector.Contains(mouse)) { _controller.Toggle(ChatPickerKind.Effort); return true; }
        if (!_controller.IsOpen) return false;
        ChatToolbarPopupLayout popup = Popup(layout);
        if (popup.Rows == 0) { _controller.Close(); return true; }
        _controller.FitPage(popup.Rows);
        if (wheel != 0 || pageDelta != 0)
        {
            _controller.ChangePage(pageDelta != 0 ? pageDelta : wheel > 0 ? -1 : 1, popup.Rows);
            return true;
        }
        if (!clicked) return false;
        if (!Rect(popup.Bounds).Contains(mouse)) { _controller.Close(); return true; }
        if (Rect(popup.Close).Contains(mouse)) { _controller.Close(); return true; }
        if (Rect(popup.Refresh).Contains(mouse)) { _controller.Refresh(); return true; }
        if (Rect(popup.Previous).Contains(mouse)) { _controller.ChangePage(-1, popup.Rows); return true; }
        if (Rect(popup.Next).Contains(mouse)) { _controller.ChangePage(1, popup.Rows); return true; }
        for (int row = 0; row < popup.Rows; row++)
            if (Rect(popup.Row(row)).Contains(mouse)) { _controller.Select(_controller.Page * popup.Rows + row); break; }
        return true;
    }

    public void Draw(SpriteBatch batch, GptChatLayout layout)
    {
        _drawnLayout = layout;
        if (layout.ModelSelector.Width <= 0) return;
        bool enabled = _controller.Enabled;
        GptChatPanel.DrawButton(batch, layout.ModelSelector, enabled ? "Model: " + _controller.ModelLabel + "  v" : "Model: ChatGPT plan required", enabled, 0.64f);
        GptChatPanel.DrawButton(batch, layout.EffortSelector, enabled ? "Effort: " + _controller.EffortLabel + "  v" : "Effort: unavailable", enabled, 0.64f);
        if (!_controller.IsOpen) return;
        ChatToolbarPopupLayout popup = Popup(layout);
        if (popup.Rows == 0) return;
        _controller.FitPage(popup.Rows);
        Rectangle bounds = Rect(popup.Bounds);
        GptChatPanel.DrawBox(batch, bounds, new Color(14, 24, 36, 255), new Color(119, 195, 174));
        Rectangle titleClip = new(bounds.X + 8, bounds.Y + 5, Math.Max(0, popup.Refresh.X - bounds.X - 15), 22);
        GptChatPanel.DrawClippedText(batch, _controller.Picker == ChatPickerKind.Model ? "Choose model" : "Thinking effort",
            new Vector2(titleClip.X, titleClip.Y), new Color(163, 243, 208), 0.65f, titleClip);
        GptChatPanel.DrawButton(batch, Rect(popup.Refresh), _controller.Busy ? "Loading" : "Refresh", !_controller.Busy, 0.60f);
        GptChatPanel.DrawButton(batch, Rect(popup.Close), "Close", true, 0.57f);
        IReadOnlyList<ChatPickerChoice> choices = _controller.Choices();
        for (int row = 0; row < popup.Rows; row++)
        {
            int index = _controller.Page * popup.Rows + row;
            if (index >= choices.Count) break;
            ChatPickerChoice choice = choices[index];
            Rectangle rectangle = Rect(popup.Row(row));
            GptChatPanel.DrawButton(batch, rectangle, (choice.Selected ? "* " : "") + choice.Label, true, 0.64f);
        }
        int pages = Math.Max(1, (choices.Count + popup.Rows - 1) / popup.Rows);
        GptChatPanel.DrawButton(batch, Rect(popup.Previous), "< Previous", _controller.Page > 0, 0.55f);
        GptChatPanel.DrawButton(batch, Rect(popup.Next), "Next >", _controller.Page + 1 < pages, 0.55f);
        Rectangle page = Rect(popup.Page);
        GptChatPanel.DrawClippedText(batch, $"{_controller.Page + 1} / {pages}", new Vector2(page.X + 5, page.Y + 4), new Color(179, 199, 212), 0.58f, page);
        string status = _controller.Feedback;
        if (!_controller.Busy && !_controller.Error && _controller.Picker == ChatPickerKind.Effort && choices.Count == 1)
            status = "Only Auto is available for this model. Refresh to check.";
        DrawStatus(batch, status, Rect(popup.Status), _controller.Error);
    }

    private static void DrawStatus(SpriteBatch batch, string text, Rectangle bounds, bool error)
    {
        float scale = 0.52f;
        Color color = error ? new Color(255, 183, 165) : new Color(181, 204, 216);
        var font = Terraria.GameContent.FontAssets.MouseText.Value;
        IReadOnlyList<ChatTextLine> lines = ChatTextLayout.Wrap(new[] { new ChatTextSpan(text, AnswerTextKind.Plain) },
            Math.Max(1, bounds.Width / scale), c => GptChatPanel.Metrics(font, c), font.CharacterSpacing);
        for (int i = 0; i < Math.Min(2, lines.Count); i++)
            foreach (ChatTextRun run in lines[i].Runs)
                GptChatPanel.DrawClippedText(batch, run.Text, new Vector2(bounds.X + run.X * scale, bounds.Y + i * 14), color, scale, bounds);
    }
    private static ChatToolbarPopupLayout Popup(GptChatLayout layout) => ChatToolbarPopupLayout.Calculate(
        Shape(layout.Panel), Shape(layout.ModelSelector), Shape(layout.EffortSelector));
    private static ChatRect Shape(Rectangle rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    private static Rectangle Rect(ChatRect rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
}
