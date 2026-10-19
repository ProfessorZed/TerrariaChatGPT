#nullable enable
using System;

namespace TerrariaChatGPT.Common.UI;

/// <summary>Viewport-only note layout. It never reads world tiles or entities.</summary>
internal readonly record struct GoalNoteGeometry(ChatRect Panel, ChatRect Header, ChatRect Body,
    ChatRect Collapse, ChatRect Close, ChatRect Footer, int VisibleRows)
{
    public const int RowHeight = 35;
    public const int HeaderHeight = 36;

    public static GoalNoteGeometry Calculate(int screenWidth, int screenHeight, int index, int count,
        int steps, bool collapsed, float normalizedX, float normalizedY, bool inventoryOpen = false)
    {
        screenWidth = Math.Max(1, screenWidth);
        screenHeight = Math.Max(1, screenHeight);
        count = Math.Clamp(count, 1, 6);
        index = Math.Clamp(index, 0, count - 1);
        bool positioned = float.IsFinite(normalizedX) && float.IsFinite(normalizedY)
            && normalizedX >= 0 && normalizedY >= 0;
        if (positioned) inventoryOpen = false;
        int margin = Math.Min(10, Math.Min(screenWidth, screenHeight) / 4);
        int width = Math.Min(294, Math.Max(1, screenWidth - margin * 2));
        int gap = 10;
        // At ordinary desktop sizes, the space between inventory/chest slots
        // and the right equipment controls is a useful temporary home. Saved
        // player positions still win; opening inventory never moves those.
        int usableLeft = inventoryOpen && screenWidth >= 1100 ? Math.Min(580, screenWidth / 2) : margin;
        int usableRight = inventoryOpen && screenWidth >= 1100 ? screenWidth - 280 : screenWidth - margin;
        int columns = Math.Max(1, Math.Min(3, (usableRight - usableLeft + gap) / (width + gap)));
        columns = Math.Min(columns, count);
        int rows = (count + columns - 1) / columns;
        // Prefer the space beneath the usual minimap. On small viewports use
        // more of the screen and scroll the list instead of losing its buttons.
        int top = Math.Min(330, Math.Max(margin, screenHeight / 2));
        int maximumHeight = Math.Max(1, (screenHeight - top - margin - gap * (rows - 1)) / rows);
        if (maximumHeight < 130)
        {
            top = margin;
            maximumHeight = Math.Max(1, (screenHeight - margin * 2 - gap * (rows - 1)) / rows);
        }
        int desiredHeight = collapsed ? HeaderHeight : HeaderHeight + Math.Clamp(steps, 1, 8) * RowHeight + 22;
        int height = Math.Min(screenHeight - margin * 2, Math.Min(desiredHeight, maximumHeight));
        height = Math.Max(1, height);
        int rightSpace = Math.Max(0, screenWidth - width - margin * 2);
        int bottomSpace = Math.Max(0, screenHeight - height - margin * 2);
        int x = positioned ? margin + (int)Math.Round(Math.Clamp(normalizedX, 0, 1) * rightSpace)
            : usableRight - width - (index % columns) * (width + gap);
        int y = positioned ? margin + (int)Math.Round(Math.Clamp(normalizedY, 0, 1) * bottomSpace)
            : top + (index / columns) * (maximumHeight + gap);
        x = Math.Clamp(x, 0, Math.Max(0, screenWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, screenHeight - height));
        ChatRect panel = new(x, y, width, height);
        int headerHeight = Math.Min(HeaderHeight, height);
        int buttonWidth = Math.Min(22, Math.Max(1, width / 5));
        int buttonHeight = Math.Min(22, headerHeight);
        ChatRect close = new(panel.Right - buttonWidth - Math.Min(6, width / 8), y + Math.Min(6, Math.Max(0, headerHeight - buttonHeight)), buttonWidth, buttonHeight);
        ChatRect collapse = new(Math.Max(x, close.X - buttonWidth - 3), close.Y, buttonWidth, buttonHeight);
        int footerHeight = collapsed ? 0 : Math.Min(22, Math.Max(0, height - headerHeight));
        int bodyHeight = collapsed ? 0 : Math.Max(0, height - headerHeight - footerHeight);
        ChatRect body = new(x + Math.Min(8, width / 4), y + headerHeight, Math.Max(0, width - 16), bodyHeight);
        ChatRect footer = new(x + Math.Min(8, width / 4), panel.Bottom - footerHeight, Math.Max(0, width - 16), footerHeight);
        return new(panel, new(x, y, width, headerHeight), body, collapse, close, footer, body.Height / RowHeight);
    }

    public static (float X, float Y) Normalize(int x, int y, int screenWidth, int screenHeight, int width, int height)
    {
        int margin = Math.Min(10, Math.Min(Math.Max(1, screenWidth), Math.Max(1, screenHeight)) / 4);
        return (Math.Clamp((float)(x - margin) / Math.Max(1, screenWidth - width - 2 * margin), 0, 1),
            Math.Clamp((float)(y - margin) / Math.Max(1, screenHeight - height - 2 * margin), 0, 1));
    }
}
