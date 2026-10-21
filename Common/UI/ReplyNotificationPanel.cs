#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Common.UI;

/// <summary>Draw-only notification: no hit targets, input capture or game actions.</summary>
internal sealed class ReplyNotificationPanel
{
    private ChatEntry? _cachedEntry;
    private DynamicSpriteFont? _cachedFont;
    private int _cachedWidth;
    private IReadOnlyList<ChatTextLine> _lines = Array.Empty<ChatTextLine>();
    private const float TextScale = 0.70f;

    public void Reset()
    {
        _cachedEntry = null;
        _cachedFont = null;
        _lines = Array.Empty<ChatTextLine>();
    }

    public void Draw(SpriteBatch batch, ReplyNotificationState state, double now)
    {
        // The chat tab carries the unread marker while either chat channel is
        // open. Menus, maps and other text editors keep their own clear surface.
        if (!state.HasUnread || Main.dedServ || Main.gameMenu || Main.drawingPlayerChat
            || Main.mapFullscreen || Main.hideUI || Main.ingameOptionsWindow || Main.inFancyUI
            || Main.editSign || Main.editChest || Main.CurrentInputTextTakerOverride is not null) return;

        float uiScale = Math.Max(0.01f, Main.UIScale);
        int screenWidth = Math.Max(1, (int)(PlayerInput.RealScreenWidth / uiScale));
        int screenHeight = Math.Max(1, (int)(PlayerInput.RealScreenHeight / uiScale));
        int inset = Math.Min(14, Math.Min(screenWidth / 4, screenHeight / 4));
        Rectangle badge = new(inset, Math.Max(0, screenHeight - inset - 26),
            Math.Min(58, Math.Max(1, screenWidth - inset * 2)), Math.Min(26, screenHeight));
        GptChatPanel.DrawBox(batch, badge, new Color(15, 33, 38, 170), new Color(91, 171, 147, 190));
        GptChatPanel.DrawClippedText(batch, "GPT *", new Vector2(badge.X + 7, badge.Y + 3),
            new Color(168, 247, 208), 0.69f, badge);

        float opacity = state.PreviewOpacity(now);
        if (opacity <= 0 || state.PreviewReply is not ChatEntry entry) return;
        int x = Math.Min(78, Math.Max(0, screenWidth / 5));
        int width = Math.Min(370, Math.Max(1, screenWidth - x - inset));
        DynamicSpriteFont font = FontAssets.MouseText.Value;
        int lineHeight = Math.Max(18, (int)Math.Ceiling(font.LineSpacing * TextScale));
        if (!ReferenceEquals(_cachedEntry, entry) || !ReferenceEquals(_cachedFont, font) || _cachedWidth != width)
        {
            // Only a short prefix is laid out; the full reply stays in chat.
            int length = Math.Min(600, entry.Text.Length);
            if (length > 0 && char.IsHighSurrogate(entry.Text[length - 1])) length--;
            string prefix = entry.Text[..length];
            _lines = ChatTextLayout.Wrap(ChatTextLayout.Highlight(prefix, entry.Highlights),
                Math.Max(1, (width - 24) / TextScale), character => GptChatPanel.Metrics(font, character), font.CharacterSpacing);
            _cachedEntry = entry;
            _cachedFont = font;
            _cachedWidth = width;
        }
        int rows = Math.Min(3, _lines.Count);
        int height = Math.Min(screenHeight, 49 + rows * lineHeight);
        Rectangle panel = new(x, Math.Max(0, screenHeight - height - 50), width, height);
        Rectangle clip = new(panel.X + 12, panel.Y + 26, Math.Max(0, panel.Width - 24), Math.Max(0, panel.Height - 46));
        GptChatPanel.DrawBox(batch, panel, new Color(15, 25, 37, 185) * opacity, new Color(95, 166, 152, 160) * opacity);
        GptChatPanel.DrawClippedText(batch, "GPT replied", new Vector2(panel.X + 12, panel.Y + 5),
            new Color(168, 247, 208), 0.70f, panel, opacity);
        for (int row = 0; row < rows; row++)
        {
            foreach (ChatTextRun run in _lines[row].Runs)
            {
                Color color = run.Kind switch
                {
                    AnswerTextKind.Location => new Color(255, 218, 112),
                    AnswerTextKind.Item => new Color(116, 219, 255),
                    AnswerTextKind.Missing => new Color(255, 176, 113),
                    AnswerTextKind.Owned => new Color(147, 235, 160),
                    _ => new Color(224, 234, 240)
                };
                GptChatPanel.DrawClippedText(batch, run.Text, new Vector2(clip.X + run.X * TextScale, clip.Y + row * lineHeight),
                    color, TextScale, clip, opacity);
            }
        }
        GptChatPanel.DrawClippedText(batch, "Open GPT chat to read the reply", new Vector2(panel.X + 12, panel.Bottom - 20),
            new Color(177, 198, 209), 0.59f, panel, opacity);
    }
}
