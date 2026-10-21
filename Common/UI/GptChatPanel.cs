#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using ReLogic.OS;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.Players;

namespace TerrariaChatGPT.Common.UI;

internal readonly record struct GptChatLayout(
    Rectangle Panel, Rectangle Transcript, Rectangle Input,
    Rectangle TerrariaTab, Rectangle GptTab, Rectangle ConnectionBadge,
    Rectangle ManageUsage, Rectangle CopyAnswer, Rectangle ModelSelector, Rectangle EffortSelector);

/// <summary>Literal glyph rendering: remote text never becomes Terraria chat tags.</summary>
internal sealed class GptChatPanel
{
    private static readonly Color UserTextColor = new(121, 207, 255);
    private static readonly Color GptTextColor = new(224, 234, 240);
    private static readonly Color MutedTextColor = new(163, 183, 200);
    private static readonly Color LocationTextColor = new(255, 218, 112);
    private static readonly Color ItemTextColor = new(116, 219, 255);
    private static readonly Color MissingTextColor = new(255, 176, 113);
    private static readonly Color OwnedTextColor = new(147, 235, 160);
    private readonly List<DisplayLine> _lines = new();
    private readonly Dictionary<ChatEntry, IReadOnlyList<DisplayLine>> _messageCache = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<ChatTextLine> _welcomeLines = Array.Empty<ChatTextLine>();
    private readonly List<ChatActionHit> _actions = new();
    private GptChatLayout _actionLayout;
    private string _actionStatus = "";
    private ulong _actionStatusUntil;
    private int _scrollFromBottom;
    private int _cachedCount = -1;
    private ChatEntry? _cachedFirstEntry;
    private ChatEntry? _cachedLastEntry;
    private int _cachedWidth;
    private float _cachedScale;
    private DynamicSpriteFont? _cachedFont;
    private string _inputCacheText = string.Empty;
    private string _inputCacheVisible = string.Empty;
    private int _inputCacheWidth;
    private bool _inputCacheCursor;

    public static GptChatLayout GetLayout()
    {
        GptClientConfig config = ModContent.GetInstance<GptClientConfig>();
        float uiScale = Math.Max(0.01f, Main.UIScale);
        ChatPanelGeometry geometry = ChatPanelGeometry.Calculate(
            (int)(PlayerInput.RealScreenWidth / uiScale), (int)(PlayerInput.RealScreenHeight / uiScale),
            config.ChatPanelWidth, config.ChatPanelHeight);
        return new(ToRectangle(geometry.Panel), ToRectangle(geometry.Transcript), ToRectangle(geometry.Input),
            ToRectangle(geometry.TerrariaTab), ToRectangle(geometry.GptTab), ToRectangle(geometry.ConnectionBadge),
            ToRectangle(geometry.ManageUsage), ToRectangle(geometry.CopyAnswer),
            ToRectangle(geometry.ModelSelector), ToRectangle(geometry.EffortSelector));
    }

    public void Reset()
    {
        _lines.Clear();
        _actions.Clear();
        _actionStatus = "";
        _messageCache.Clear();
        _welcomeLines = Array.Empty<ChatTextLine>();
        _scrollFromBottom = 0;
        _cachedCount = -1;
        _cachedFirstEntry = null;
        _cachedLastEntry = null;
        _cachedWidth = 0;
        _cachedScale = 0;
        _cachedFont = null;
        _inputCacheText = string.Empty;
    }

    public void Scroll(int lines) { _scrollFromBottom = Math.Max(0, _scrollFromBottom + lines); _actions.Clear(); }
    public void ScrollToLatest() { _scrollFromBottom = 0; _actions.Clear(); }

    public bool HandleClick(Point mouse)
    {
        // A resize invalidates the previous frame's link/button hit rectangles.
        if (_actionLayout != GetLayout()) return false;
        ChatActionHit? hit = _actions.LastOrDefault(action => action.Bounds.Contains(mouse));
        if (hit is null || !GptAssistantService.Messages.Any(entry => ReferenceEquals(entry, hit.Entry))) return false;
        if (hit.Goal && hit.Entry.GoalSuggestion is not null)
        {
            Main.LocalPlayer.GetModPlayer<GptProgressPlayer>().TrySetGoal(hit.Entry.GoalSuggestion, out _, out string message);
            _actionStatus = message;
        }
        else if (hit.Citation is not null && WebCitationLinks.TryGetPublicUri(hit.Citation.Url, out Uri? uri))
        {
            try { Platform.Get<IClipboard>().Value = uri!.AbsoluteUri; _actionStatus = "Source link copied. Paste it into your browser."; }
            catch { _actionStatus = "Couldn't copy that source link."; }
        }
        _actionStatusUntil = Main.GameUpdateCount + 300;
        return true;
    }

    public void Draw(SpriteBatch spriteBatch, string question,
        IReadOnlyList<ChatEntry> messages, bool busy, GptProvider provider,
        ChatGptConnectionStatus connection, bool checkingConnection, string copyStatus = "", string toolbarStatus = "")
    {
        GptChatLayout layout = GetLayout();
        _actionLayout = layout;
        _actions.Clear();
        float configuredScale = ModContent.GetInstance<GptClientConfig>().ChatTextScale;
        float textScale = float.IsFinite(configuredScale) ? Math.Clamp(configuredScale, 0.7f, 1.2f) : 0.9f;
        DynamicSpriteFont font = FontAssets.MouseText.Value;
        int lineHeight = Math.Max(18, (int)Math.Ceiling(font.LineSpacing * textScale) + 1);
        DrawBox(spriteBatch, layout.Panel, new Color(15, 23, 35, 247), new Color(77, 111, 127));
        if (layout.Panel.Height >= 80)
        {
            DrawClippedText(spriteBatch, "GPT", new Vector2(layout.Panel.X + 14, layout.Panel.Y + 9),
                new Color(155, 245, 211), 0.95f, layout.Panel);
            if (layout.Panel.Width >= 450)
                DrawClippedText(spriteBatch, toolbarStatus.Length > 0 ? toolbarStatus : "Your Terraria companion",
                    new Vector2(layout.Panel.X + 65, layout.Panel.Y + 12), MutedTextColor, 0.70f,
                    new Rectangle(layout.Panel.X + 65, layout.Panel.Y + 7, Math.Max(0, layout.Panel.Width - 180), 30));
            bool canCopy = false;
            for (int i = messages.Count - 1; i >= 0; i--)
                if (messages[i].Role == "assistant") { canCopy = true; break; }
            DrawButton(spriteBatch, layout.CopyAnswer, copyStatus.Length > 0 ? copyStatus : "Copy answer", canCopy, 0.60f);
        }

        int visibleLines = Math.Max(0, layout.Transcript.Height / lineHeight);
        UpdateLines(messages, layout.Transcript.Width, textScale, font, visibleLines);
        int maximumScroll = Math.Max(0, _lines.Count - visibleLines);
        _scrollFromBottom = Math.Min(_scrollFromBottom, maximumScroll);
        int end = _lines.Count - _scrollFromBottom;
        int start = Math.Max(0, end - visibleLines);
        for (int i = start; i < end; i++)
        {
            Vector2 position = new(layout.Transcript.X, layout.Transcript.Y + (i - start) * lineHeight);
            if (_lines[i].Actions && _lines[i].Entry is ChatEntry owner)
                DrawAnswerActions(spriteBatch, owner, position, lineHeight, layout.Transcript);
            else DrawLine(spriteBatch, _lines[i].Line, position, _lines[i].Color, textScale, layout.Transcript, _lines[i].Entry);
        }

        if (_lines.Count == 0)
        {
            int row = 0;
            for (int i = 0; i < _welcomeLines.Count; i++)
            {
                if (row >= visibleLines) break;
                DrawLine(spriteBatch, _welcomeLines[i], new Vector2(layout.Transcript.X, layout.Transcript.Y + row++ * lineHeight),
                    new Color(190, 206, 219), textScale, layout.Transcript);
            }
        }
        DrawScrollIndicator(spriteBatch, layout.Transcript, _lines.Count, visibleLines, start);
        DrawConnection(spriteBatch, layout, provider, connection, checkingConnection);

        if (layout.Panel.Height >= 85)
        {
            string hint = Main.GameUpdateCount < _actionStatusUntil && _actionStatus.Length > 0
                ? _actionStatus : toolbarStatus.Length > 0 ? toolbarStatus : _scrollFromBottom > 0
                ? "More below · Scroll / PgDn · End for latest"
                : "Enter send · Tab switch · ↑↓ recent questions · Esc close";
            DrawClippedText(spriteBatch, HeadToFit(hint, layout.Transcript.Width, 0.60f),
                new Vector2(layout.Transcript.X, layout.Panel.Bottom - 21), MutedTextColor, 0.60f,
                new Rectangle(layout.Transcript.X, layout.Panel.Bottom - 23, layout.Transcript.Width, 21));
        }

        DrawTabs(spriteBatch, layout, true);
        DrawBox(spriteBatch, layout.Input, new Color(19, 31, 44, 250), new Color(100, 182, 161));
        bool cursor = Main.GameUpdateCount % 60 < 30;
        string draft = question.Length == 0
            ? busy ? "Thinking... You can keep typing." : "Ask GPT..."
            : question;
        if (_inputCacheText != draft || _inputCacheWidth != layout.Input.Width || _inputCacheCursor != cursor)
        {
            _inputCacheText = draft;
            _inputCacheWidth = layout.Input.Width;
            _inputCacheCursor = cursor;
            _inputCacheVisible = TailToFit(draft + (cursor ? "|" : string.Empty), layout.Input.Width - 24, 0.85f);
        }
        DrawClippedText(spriteBatch, _inputCacheVisible,
            new Vector2(layout.Input.X + 12, layout.Input.Y + 5),
            question.Length == 0 ? new Color(163, 185, 201) : UserTextColor, 0.85f,
            Inset(layout.Input, Math.Min(4, layout.Input.Width / 3), 1));
        DrawSourceTooltip(spriteBatch);
    }

    public void DrawTabs(SpriteBatch spriteBatch, GptChatLayout layout, bool gptSelected, bool unread = false)
    {
        DrawTab(spriteBatch, layout.TerrariaTab, "Terraria", !gptSelected);
        DrawTab(spriteBatch, layout.GptTab, unread && !gptSelected ? "GPT *" : "GPT", gptSelected);
    }

    private static void DrawConnection(SpriteBatch spriteBatch, GptChatLayout layout, GptProvider provider,
        ChatGptConnectionStatus connection, bool checkingConnection)
    {
        if (layout.ConnectionBadge.Height <= 0) return;
        string text = provider switch
        {
            GptProvider.WikiOnly => "Wiki only · no AI account needed",
            GptProvider.OpenAiApi => "OpenAI API · separately billed",
            _ => connection.Connected && connection.UsingPlan ? "Using ChatGPT plan"
                : connection.SigningIn ? "ChatGPT · finish browser sign-in"
                : connection.Connected ? "ChatGPT connected · plan unavailable"
                : connection.IsError ? "ChatGPT · connection unavailable"
                : checkingConnection ? "ChatGPT · checking connection..."
                : "ChatGPT · connect in Mod Settings"
        };
        Color color = provider == GptProvider.OpenAiApi ? LocationTextColor
            : provider == GptProvider.ChatGPT && connection.UsingPlan && connection.Connected
                ? OwnedTextColor : new Color(184, 200, 215);
        Rectangle badge = provider == GptProvider.ChatGPT ? layout.ConnectionBadge
            : new Rectangle(layout.ConnectionBadge.X, layout.ConnectionBadge.Y, layout.Transcript.Width, layout.ConnectionBadge.Height);
        DrawClippedText(spriteBatch, HeadToFit(text, badge.Width, 0.62f),
            new Vector2(badge.X, badge.Y + 1), color, 0.62f, badge);
        if (provider == GptProvider.ChatGPT)
            DrawButton(spriteBatch, layout.ManageUsage, "Copy usage link", true, 0.55f);
    }

    private void UpdateLines(IReadOnlyList<ChatEntry> messages, int width, float scale, DynamicSpriteFont font, int visibleLines)
    {
        ChatEntry? firstEntry = messages.Count == 0 ? null : messages[0];
        ChatEntry? lastEntry = messages.Count == 0 ? null : messages[messages.Count - 1];
        bool geometryChanged = _cachedWidth != width || _cachedScale != scale || !ReferenceEquals(_cachedFont, font);
        if (!geometryChanged && _cachedCount == messages.Count && ReferenceEquals(_cachedFirstEntry, firstEntry)
            && ReferenceEquals(_cachedLastEntry, lastEntry)) return;
        bool revealNewAnswer = _scrollFromBottom == 0 && lastEntry?.Role == "assistant"
            && !ReferenceEquals(_cachedLastEntry, lastEntry);

        // Preserve what the player is reading if an answer arrives above the
        // bottom. At the bottom, new answers remain visible automatically.
        DisplayLine? anchor = _scrollFromBottom > 0 && _lines.Count > 0
            ? _lines[Math.Clamp(_lines.Count - _scrollFromBottom - 1, 0, _lines.Count - 1)] : null;
        if (geometryChanged)
        {
            _messageCache.Clear();
            _welcomeLines = ChatTextLayout.Wrap(new[] { new ChatTextSpan(
                "Ask a question, then keep the conversation going.\n\n" +
                "Try: How do I get Starfury?\n" +
                "Or: Help me prepare for my next boss fight.\n\n" +
                "Ask for a goal, then choose Set goal? to pin a progress note.\n" +
                "Click a small source number to copy its link.", AnswerTextKind.Plain) },
                Math.Max(1, width / scale), character => Metrics(font, character), font.CharacterSpacing);
        }
        _cachedCount = messages.Count;
        _cachedFirstEntry = firstEntry;
        _cachedLastEntry = lastEntry;
        _cachedWidth = width;
        _cachedScale = scale;
        _cachedFont = font;
        _lines.Clear();
        var liveEntries = new HashSet<ChatEntry>(ReferenceEqualityComparer.Instance);
        foreach (ChatEntry entry in messages)
        {
            liveEntries.Add(entry);
            if (!_messageCache.TryGetValue(entry, out IReadOnlyList<DisplayLine>? cached))
            {
                bool user = entry.Role == "user";
                Color baseColor = user ? UserTextColor : GptTextColor;
                string time = entry.Timestamp.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                var spans = new List<ChatTextSpan> { new($"[{time}] " + (user ? "You: " : entry.Role == "assistant" ? "GPT: " : "Info: "), AnswerTextKind.Muted) };
                IReadOnlyList<ChatTextSpan> answerSpans = ChatTextLayout.Highlight(entry.Text, user ? null : entry.Highlights);
                if (!user && entry.Citations.Count > 0)
                    answerSpans = ChatTextLayout.WithCitations(answerSpans, entry.Citations.Select(source => source.Index).ToArray());
                spans.AddRange(answerSpans);
                IReadOnlyList<ChatTextLine> wrapped = ChatTextLayout.Wrap(spans, Math.Max(1, width / scale),
                    character => Metrics(font, character), font.CharacterSpacing);
                var display = new List<DisplayLine>(wrapped.Count + 1);
                foreach (ChatTextLine line in wrapped) display.Add(new DisplayLine(line, baseColor, entry));
                if (entry.Role == "assistant" && (entry.GoalSuggestion is not null || entry.WebSearched))
                    display.Add(new DisplayLine(new ChatTextLine(Array.Empty<ChatTextRun>(), 0), baseColor, entry, true));
                display.Add(new DisplayLine(new ChatTextLine(Array.Empty<ChatTextRun>(), 0), baseColor));
                cached = display;
                _messageCache.Add(entry, cached);
            }
            _lines.AddRange(cached);
        }
        // A separator belongs between messages, never below the newest answer.
        if (_lines.Count > 0) _lines.RemoveAt(_lines.Count - 1);
        if (_messageCache.Count > liveEntries.Count)
        {
            var expired = new List<ChatEntry>();
            foreach (ChatEntry entry in _messageCache.Keys)
                if (!liveEntries.Contains(entry)) expired.Add(entry);
            foreach (ChatEntry entry in expired) _messageCache.Remove(entry);
        }
        if (anchor is not null)
        {
            int anchorIndex = _lines.FindIndex(line => ReferenceEquals(line, anchor));
            if (anchorIndex >= 0) _scrollFromBottom = _lines.Count - anchorIndex - 1;
        }
        else if (revealNewAnswer && lastEntry is not null && _messageCache.TryGetValue(lastEntry, out var lastLines))
            _scrollFromBottom = ChatScrollPosition.AfterNewAnswer(lastLines.Count - 1, visibleLines, _scrollFromBottom);
    }

    internal static ChatGlyphMetrics Metrics(DynamicSpriteFont font, char character)
    {
        DynamicSpriteFont.SpriteCharacterData glyph = font.SpriteCharacters.TryGetValue(character, out var found)
            ? found : font.DefaultCharacterData;
        return new(glyph.Kerning.X, glyph.Kerning.Y, glyph.Kerning.Z);
    }

    private void DrawLine(SpriteBatch spriteBatch, ChatTextLine line, Vector2 position,
        Color baseColor, float scale, Rectangle clip, ChatEntry? entry = null)
    {
        for (int i = 0; i < line.Runs.Count; i++)
        {
            ChatTextRun run = line.Runs[i];
            Color color = run.Kind switch
            {
                AnswerTextKind.Muted => MutedTextColor, AnswerTextKind.Location => LocationTextColor,
                AnswerTextKind.Item => ItemTextColor, AnswerTextKind.Missing => MissingTextColor,
                AnswerTextKind.Owned => OwnedTextColor, AnswerTextKind.Citation => new Color(166, 194, 255), _ => baseColor
            };
            Vector2 runPosition = new(position.X + run.X * scale, position.Y);
            DrawClippedText(spriteBatch, run.Text, runPosition, color, scale, clip);
            if (run.LinkId > 0 && entry is not null)
            {
                WebCitation? citation = entry.Citations.FirstOrDefault(source => source.Index == run.LinkId);
                if (citation is null || !WebCitationLinks.TryGetPublicUri(citation.Url, out _)) continue;
                Rectangle bounds = Rectangle.Intersect(clip, new Rectangle((int)runPosition.X, (int)runPosition.Y,
                    Math.Max(1, (int)Math.Ceiling(FontAssets.MouseText.Value.MeasureString(run.Text).X * scale)),
                    Math.Max(1, (int)Math.Ceiling(FontAssets.MouseText.Value.LineSpacing * scale))));
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                _actions.Add(new ChatActionHit(bounds, entry, citation, false));
                spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 1), color * 0.8f);
            }
        }
    }

    private void DrawAnswerActions(SpriteBatch batch, ChatEntry entry, Vector2 position, int lineHeight, Rectangle clip)
    {
        int x = (int)position.X;
        if (entry.GoalSuggestion is not null)
        {
            Rectangle bounds = Rectangle.Intersect(clip, new Rectangle(x, (int)position.Y, Math.Min(106, clip.Width), Math.Min(24, lineHeight)));
            DrawButton(batch, bounds, "Set goal?", true, 0.63f);
            _actions.Add(new ChatActionHit(bounds, entry, null, true));
            x += 116;
        }
        if (entry.WebSearched)
            DrawClippedText(batch, "Searched the web", new Vector2(x, position.Y + 3), MutedTextColor, 0.61f, clip);
    }

    private void DrawSourceTooltip(SpriteBatch batch)
    {
        Point mouse = UiMouse();
        ChatActionHit? hit = _actions.LastOrDefault(action => action.Citation is not null && action.Bounds.Contains(mouse));
        if (hit?.Citation is not WebCitation citation || !WebCitationLinks.TryGetPublicUri(citation.Url, out Uri? uri)) return;
        DrawLiteralTooltip(batch, (string.IsNullOrWhiteSpace(citation.Title) ? uri!.Host : citation.Title) + "\n" + uri!.Host + " - Click to copy link", mouse);
    }

    internal static void DrawLiteralTooltip(SpriteBatch batch, string text, Point mouse)
    {
        int screenWidth = Math.Max(1, (int)(PlayerInput.RealScreenWidth / Math.Max(0.01f, Main.UIScale)));
        int screenHeight = Math.Max(1, (int)(PlayerInput.RealScreenHeight / Math.Max(0.01f, Main.UIScale)));
        int width = Math.Min(360, Math.Max(1, screenWidth - 16));
        float scale = 0.66f;
        DynamicSpriteFont font = FontAssets.MouseText.Value;
        var lines = ChatTextLayout.Wrap(new[] { new ChatTextSpan(text.Length <= 500 ? text : text[..500], AnswerTextKind.Plain) },
            Math.Max(1, (width - 16) / scale), character => Metrics(font, character), font.CharacterSpacing);
        int lineHeight = Math.Max(18, (int)Math.Ceiling(font.LineSpacing * scale));
        int height = Math.Min(screenHeight, Math.Min(6, lines.Count) * lineHeight + 12);
        Rectangle bounds = new(Math.Clamp(mouse.X + 14, 0, Math.Max(0, screenWidth - width)),
            Math.Clamp(mouse.Y + 20, 0, Math.Max(0, screenHeight - height)), width, height);
        DrawBox(batch, bounds, new Color(19, 28, 40, 250), new Color(115, 148, 176));
        int row = 0;
        foreach (ChatTextLine line in lines.Take(6))
        {
            foreach (ChatTextRun run in line.Runs)
                DrawClippedText(batch, run.Text, new Vector2(bounds.X + 8 + run.X * scale, bounds.Y + 5 + row * lineHeight),
                    GptTextColor, scale, Inset(bounds, 3, 2));
            row++;
        }
    }

    internal static Point UiMouse()
    {
        var mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
        float scale = Math.Max(0.01f, Main.UIScale);
        return new Point((int)(mouse.X / scale), (int)(mouse.Y / scale));
    }

    private static void DrawScrollIndicator(SpriteBatch spriteBatch, Rectangle transcript, int total, int visible, int start)
    {
        if (total <= visible || visible <= 0 || transcript.Height <= 0) return;
        Rectangle track = new(transcript.Right + 4, transcript.Y, 3, transcript.Height);
        spriteBatch.Draw(TextureAssets.MagicPixel.Value, track, new Color(51, 67, 82));
        int thumbHeight = Math.Clamp((int)((float)visible / total * track.Height), Math.Min(16, track.Height), track.Height);
        int thumbY = track.Y + (int)((float)start / Math.Max(1, total - visible) * (track.Height - thumbHeight));
        spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(track.X, thumbY, track.Width, thumbHeight), new Color(127, 176, 188));
    }

    private static string TailToFit(string text, int width, float scale)
    {
        DynamicSpriteFont font = FontAssets.MouseText.Value;
        if (width <= 0) return string.Empty;
        if (font.MeasureString(text).X * scale <= width) return text;
        if (font.MeasureString("...").X * scale > width) return string.Empty;
        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int start = low + (high - low) / 2;
            if (font.MeasureString("..." + text[start..]).X * scale <= width) high = start;
            else low = start + 1;
        }
        if (low < text.Length && char.IsLowSurrogate(text[low])) low++;
        return "..." + text[low..];
    }

    internal static string HeadToFit(string text, int width, float scale)
    {
        DynamicSpriteFont font = FontAssets.MouseText.Value;
        if (width <= 0) return string.Empty;
        if (font.MeasureString(text).X * scale <= width) return text;
        if (font.MeasureString("...").X * scale > width) return string.Empty;
        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int count = low + (high - low + 1) / 2;
            if (font.MeasureString(text[..count] + "...").X * scale <= width) low = count;
            else high = count - 1;
        }
        if (low > 0 && char.IsHighSurrogate(text[low - 1])) low--;
        return text[..low] + "...";
    }

    private static void DrawTab(SpriteBatch spriteBatch, Rectangle rectangle, string label, bool selected)
    {
        DrawBox(spriteBatch, rectangle,
            selected ? new Color(40, 83, 76, 250) : new Color(29, 38, 52, 245),
            selected ? new Color(107, 214, 174) : new Color(88, 103, 124));
        float scale = 0.74f;
        string visible = HeadToFit(label, rectangle.Width - 8, scale);
        float labelWidth = FontAssets.MouseText.Value.MeasureString(visible).X;
        DrawClippedText(spriteBatch, visible,
            new Vector2(rectangle.Center.X - labelWidth * scale / 2f, rectangle.Y + 3),
            selected ? new Color(188, 255, 222) : new Color(208, 216, 227), scale, Inset(rectangle, 2, 1));
    }

    internal static void DrawButton(SpriteBatch spriteBatch, Rectangle rectangle, string label, bool enabled, float scale)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        bool hovered = enabled && rectangle.Contains(UiMouse());
        DrawBox(spriteBatch, rectangle, hovered ? new Color(54, 82, 98) : new Color(29, 45, 62),
            hovered ? new Color(143, 218, 190) : new Color(73, 106, 124));
        string visible = HeadToFit(label, rectangle.Width - 10, scale);
        float textWidth = FontAssets.MouseText.Value.MeasureString(visible).X * scale;
        DrawClippedText(spriteBatch, visible, new Vector2(rectangle.Center.X - textWidth / 2, rectangle.Y + 3),
            enabled ? new Color(213, 234, 229) : new Color(123, 142, 157), scale, Inset(rectangle, 3, 1));
    }

    internal static void DrawBox(SpriteBatch spriteBatch, Rectangle rectangle, Color fill, Color border)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        spriteBatch.Draw(pixel, rectangle, border);
        Rectangle inner = Inset(rectangle, 1, 1);
        if (inner.Width > 0 && inner.Height > 0) spriteBatch.Draw(pixel, inner, fill);
    }

    internal static void DrawClippedText(SpriteBatch batch, string text, Vector2 position, Color color, float scale, Rectangle clip, float opacity = 1)
    {
        if (clip.Width <= 0 || clip.Height <= 0 || text.Length == 0) return;
        opacity = float.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 0;
        if (opacity <= 0) return;
        color *= opacity;
        DynamicSpriteFont font = FontAssets.MouseText.Value;
        float pen = 0;
        bool first = true;
        foreach (char character in text)
        {
            DynamicSpriteFont.SpriteCharacterData data = font.SpriteCharacters.TryGetValue(character, out var found)
                ? found : font.DefaultCharacterData;
            pen += first ? Math.Max(data.Kerning.X, 0) : font.CharacterSpacing + data.Kerning.X;
            Vector2 glyphPosition = position + new Vector2((pen + data.Padding.X) * scale, data.Padding.Y * scale);
            if (data.Glyph.Width > 0 && data.Glyph.Height > 0)
            {
                Color shadow = new Color(0, 0, 0, 185) * opacity;
                DrawGlyph(batch, data, glyphPosition + new Vector2(-1, 0), shadow, scale, clip);
                DrawGlyph(batch, data, glyphPosition + new Vector2(1, 0), shadow, scale, clip);
                DrawGlyph(batch, data, glyphPosition + new Vector2(0, -1), shadow, scale, clip);
                DrawGlyph(batch, data, glyphPosition + new Vector2(0, 1), shadow, scale, clip);
                DrawGlyph(batch, data, glyphPosition, color, scale, clip);
            }
            pen += data.Kerning.Y + data.Kerning.Z;
            first = false;
        }
    }

    private static void DrawGlyph(SpriteBatch batch, DynamicSpriteFont.SpriteCharacterData data,
        Vector2 position, Color color, float scale, Rectangle clip)
    {
        // Crop glyphs without restarting Terraria's SpriteBatch or changing its
        // shared scissor/rasterizer state.
        int left = Math.Max(0, (int)Math.Ceiling((clip.Left - position.X) / scale));
        int top = Math.Max(0, (int)Math.Ceiling((clip.Top - position.Y) / scale));
        int right = Math.Min(data.Glyph.Width, (int)Math.Floor((clip.Right - position.X) / scale));
        int bottom = Math.Min(data.Glyph.Height, (int)Math.Floor((clip.Bottom - position.Y) / scale));
        if (right <= left || bottom <= top) return;
        Rectangle source = new(data.Glyph.X + left, data.Glyph.Y + top, right - left, bottom - top);
        batch.Draw(data.Texture, position + new Vector2(left * scale, top * scale), source,
            color, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    private static Rectangle Inset(Rectangle rectangle, int x, int y) => new(
        rectangle.X + x, rectangle.Y + y, Math.Max(0, rectangle.Width - 2 * x), Math.Max(0, rectangle.Height - 2 * y));
    private static Rectangle ToRectangle(ChatRect rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    private sealed record DisplayLine(ChatTextLine Line, Color Color, ChatEntry? Entry = null, bool Actions = false);
    private sealed record ChatActionHit(Rectangle Bounds, ChatEntry Entry, WebCitation? Citation, bool Goal);
}
