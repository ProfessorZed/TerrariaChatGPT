#nullable enable
using System;

namespace TerrariaChatGPT.Common.UI;

/// <summary>Screen-bounded geometry shared by drawing and hit testing.</summary>
internal readonly record struct ChatToolbarPopupLayout(ChatRect Bounds, ChatRect Refresh, ChatRect Close,
    ChatRect Previous, ChatRect Next, ChatRect Page, ChatRect Status, int Rows)
{
    public static ChatToolbarPopupLayout Calculate(ChatRect panel, ChatRect model, ChatRect effort)
    {
        if (model.Width <= 0 || model.Height <= 0) return default;
        int x = model.X, y = model.Bottom + 4;
        int width = Math.Max(0, effort.Right - x);
        int height = Math.Min(242, Math.Max(0, panel.Bottom - y - 5));
        if (width < 120 || height < 112) return default;
        int rows = Math.Clamp((height - 88) / 24, 1, 6);
        height = 88 + rows * 24;
        ChatRect bounds = new(x, y, width, height);
        int closeWidth = Math.Min(48, width / 4);
        ChatRect close = new(bounds.Right - closeWidth - 5, y + 4, closeWidth, 22);
        ChatRect refresh = new(Math.Max(x + 5, close.X - 77), y + 4, Math.Min(72, Math.Max(0, close.X - x - 10)), 22);
        int footY = y + 32 + rows * 24;
        int buttonWidth = Math.Min(86, (width - 24) / 3);
        ChatRect previous = new(x + 6, footY, buttonWidth, 22);
        ChatRect next = new(bounds.Right - 6 - buttonWidth, footY, buttonWidth, 22);
        ChatRect page = new(previous.Right + 4, footY, Math.Max(0, next.X - previous.Right - 8), 22);
        ChatRect status = new(x + 7, footY + 26, width - 14, 29);
        return new(bounds, refresh, close, previous, next, page, status, rows);
    }

    public ChatRect Row(int index) => index < 0 || index >= Rows ? default
        : new(Bounds.X + 6, Bounds.Y + 30 + index * 24, Math.Max(0, Bounds.Width - 12), 22);
}
