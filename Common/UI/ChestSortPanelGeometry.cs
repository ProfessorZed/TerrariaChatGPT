#nullable enable
using System;

namespace TerrariaChatGPT.Common.UI;

internal readonly record struct ChestSortPanelGeometry(ChatRect Panel, ChatRect Title, ChatRect Summary,
    ChatRect Body, ChatRect ScrollHint, ChatRect Remember, ChatRect Refresh, ChatRect Cancel, ChatRect Confirm)
{
    public static ChestSortPanelGeometry Calculate(int screenWidth, int screenHeight)
    {
        screenWidth = Math.Max(1, screenWidth); screenHeight = Math.Max(1, screenHeight);
        int margin = Math.Min(12, Math.Min(screenWidth / 20, screenHeight / 20));
        int width = Math.Min(780, screenWidth - margin * 2);
        int height = Math.Min(620, screenHeight - margin * 2);
        ChatRect panel = new((screenWidth - width) / 2, (screenHeight - height) / 2, width, height);
        int pad = Math.Min(12, Math.Min(width / 10, height / 12));
        int innerWidth = width - pad * 2, innerHeight = height - pad * 2;
        int gap = Math.Min(6, innerHeight / 30);
        int buttonHeight = Math.Min(28, innerHeight / 4);
        int titleHeight = Math.Min(28, Math.Max(0, (innerHeight - buttonHeight * 2 - gap * 5) / 3));
        int summaryHeight = innerHeight >= 250 ? 66 : innerHeight >= 160 ? 20 : 0;
        int hintHeight = innerHeight >= 160 ? 18 : 0;
        int x = panel.X + pad, top = panel.Y + pad;
        int buttonsY = panel.Bottom - pad - buttonHeight;
        int rememberY = buttonsY - gap - buttonHeight;
        int hintY = rememberY - gap - hintHeight;
        ChatRect title = new(x, top, innerWidth, titleHeight);
        ChatRect summary = new(x, title.Bottom + gap, innerWidth, summaryHeight);
        int bodyY = summary.Bottom + gap;
        ChatRect body = new(x, bodyY, Math.Max(0, innerWidth - 8), Math.Max(0, hintY - gap - bodyY));
        int buttonGap = Math.Min(8, innerWidth / 12);
        int buttonWidth = Math.Max(0, (innerWidth - buttonGap * 2) / 3);
        ChatRect refresh = new(x, buttonsY, buttonWidth, buttonHeight);
        ChatRect cancel = new(refresh.Right + buttonGap, buttonsY, buttonWidth, buttonHeight);
        ChatRect confirm = new(cancel.Right + buttonGap, buttonsY, Math.Max(0, x + innerWidth - cancel.Right - buttonGap), buttonHeight);
        ChatRect remember = new(x, rememberY, innerWidth, buttonHeight);
        return new(panel, title, summary, body, new(x, hintY, innerWidth, hintHeight), remember, refresh, cancel, confirm);
    }
}

/// <summary>An opening/held mouse gesture, unseen layout or Enter can never authorize a move.</summary>
internal sealed class ChestSortClickGuard
{
    private bool _leftDown = true;
    private ChestSortPanelGeometry? _drawn;
    public void Reset() { _leftDown = true; _drawn = null; }
    public void MarkDrawn(ChestSortPanelGeometry layout) => _drawn = layout;
    public bool Pressed(bool leftDown, ChestSortPanelGeometry layout)
    {
        bool edge = leftDown && !_leftDown;
        _leftDown = leftDown;
        return edge && _drawn == layout;
    }
    public static bool Contains(ChatRect bounds, int x, int y) => bounds.Width > 0 && bounds.Height > 0
        && x >= bounds.X && y >= bounds.Y && x < bounds.Right && y < bounds.Bottom;
    public static int Scroll(int current, int delta, int total, int visible) =>
        Math.Clamp(current + delta, 0, Math.Max(0, total - Math.Max(0, visible)));
}
