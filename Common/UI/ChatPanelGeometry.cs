#nullable enable
using System;

namespace TerrariaChatGPT.Common.UI;

internal readonly record struct ChatRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

internal static class ChatScrollPosition
{
    public static int AfterNewAnswer(int answerLines, int visibleLines, int currentScroll) =>
        currentScroll > 0 || visibleLines <= 0 ? Math.Max(0, currentScroll)
            : Math.Max(0, answerLines - visibleLines);
}

internal readonly record struct ChatPanelGeometry(ChatRect Panel, ChatRect Transcript, ChatRect Input,
    ChatRect TerrariaTab, ChatRect GptTab, ChatRect ConnectionBadge, ChatRect ManageUsage, ChatRect CopyAnswer)
{
    public static ChatPanelGeometry Calculate(int screenWidth, int screenHeight, int preferredWidth, int preferredHeight)
    {
        screenWidth = Math.Max(1, screenWidth);
        screenHeight = Math.Max(1, screenHeight);
        // Keep vanilla's x=78 input alignment, including its two tabs, except
        // when an exceptionally small logical viewport needs every pixel.
        int x = Math.Min(78, Math.Max(0, screenWidth / 3));
        int width = Math.Max(1, Math.Min(Math.Clamp(preferredWidth, 480, 1200), screenWidth - x - Math.Min(12, screenWidth - x - 1)));
        int inputHeight = Math.Min(32, screenHeight);
        int inputY = Math.Max(0, screenHeight - inputHeight - Math.Min(10, Math.Max(0, screenHeight - inputHeight)));
        ChatRect input = new(x, inputY, width, inputHeight);
        int gap = Math.Min(8, inputY);
        int height = Math.Min(Math.Clamp(preferredHeight, 240, 720), Math.Max(0, inputY - gap - 4));
        ChatRect panel = new(x, Math.Max(0, inputY - height - gap), width, height);
        int tabWidth = Math.Max(0, x - Math.Min(14, x));
        int tabX = Math.Min(8, x - tabWidth);
        int tabHeight = Math.Min(24, input.Bottom / 2);
        int tabGap = Math.Min(4, Math.Max(0, input.Bottom - 2 * tabHeight));
        int tabsY = Math.Max(0, input.Bottom - tabHeight * 2 - tabGap);
        int padding = Math.Min(14, width / 5);
        int header = Math.Min(44, height / 3);
        int footer = height >= 130 ? 58 : height >= 85 ? 46 : 0;
        int transcriptWidth = Math.Max(0, width - 2 * padding - 7);
        ChatRect transcript = new(panel.X + padding, panel.Y + header,
            transcriptWidth, Math.Max(0, height - header - footer));
        bool buttonsFit = width >= 250 && height >= 130;
        ChatRect usage = buttonsFit ? new(panel.Right - 128, panel.Bottom - 48, 114, 22) : default;
        ChatRect copy = buttonsFit ? new(panel.Right - 100, panel.Y + 9, 86, 24) : default;
        ChatRect connection = height >= 85 ? new(panel.X + padding, panel.Bottom - 46,
            Math.Max(0, width - 2 * padding - (buttonsFit ? 124 : 0)), 20) : default;
        return new(panel, transcript, input,
            new(tabX, tabsY, tabWidth, tabHeight),
            new(tabX, tabsY + tabHeight + tabGap, tabWidth, tabHeight), connection, usage, copy);
    }
}
