#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using TerrariaChatGPT.Common.Memory;
using TerrariaChatGPT.Common.Players;

namespace TerrariaChatGPT.Common.UI;

/// <summary>Review/delete controls only; model output is displayed literally and cannot click actions.</summary>
internal sealed class MemoryPanel
{
    private readonly GoalPointerCapture _capture = new();
    private readonly MemoryClickGuard _click = new();
    private readonly List<(ChatRect Bounds, string Id)> _remove = new();
    private readonly List<MemoryRow> _rows = new();
    private Player? _owner;
    private long _cachedRevision = -1;
    private int _cachedWidth = -1, _scroll, _totalLines;
    private long? _clearRevision;
    private string _status = "";
    public bool IsOpen { get; private set; }
    public bool OwnsPointer => IsOpen || _capture.HasCapture;

    public void Open()
    {
        _owner = Main.LocalPlayer;
        IsOpen = true;
        _click.Reset(); _clearRevision = null; _scroll = 0;
        _cachedRevision = -1; _cachedWidth = -1; _status = ""; _remove.Clear();
        CapturePointer();
    }

    public void Close()
    {
        IsOpen = false; _owner = null; _clearRevision = null;
        _click.Reset(); _remove.Clear(); _rows.Clear();
        // A closing click remains consumed until its physical release.
    }

    public void Update(bool chatOpen)
    {
        if (IsOpen && (!chatOpen || !ReferenceEquals(_owner, Main.LocalPlayer))) Close();
        CapturePointer();
    }

    private void CapturePointer()
    {
        if (Main.dedServ) return;
        MouseState physical = Mouse.GetState();
        _capture.BeginFrame(physical.LeftButton == ButtonState.Pressed, physical.RightButton == ButtonState.Pressed);
        if (IsOpen) _capture.CaptureHeld();
        if (!OwnsPointer) return;
        if (!Main.gameMenu)
        {
            Main.LocalPlayer.mouseInterface = true;
            Main.LocalPlayer.controlUseItem = false;
            Main.LocalPlayer.controlUseTile = false;
        }
        PlayerInput.LockVanillaMouseScroll("TerrariaChatGPT:Memory");
        if (_capture.BlockLeft) { Main.mouseLeft = false; Main.mouseLeftRelease = false; }
        if (_capture.BlockRight) { Main.mouseRight = false; Main.mouseRightRelease = false; }
    }

    public void HandleInput(Point mouse, bool escape, int pageDelta)
    {
        if (!IsOpen) return;
        if (!ReferenceEquals(_owner, Main.LocalPlayer)) { Close(); return; }
        CapturePointer(); PlayerInput.WritingText = true;
        MemoryPanelGeometry layout = Geometry();
        GptMemoryPlayer memory = _owner!.GetModPlayer<GptMemoryPlayer>();
        EnsureRows(memory, layout.Body.Width);
        int visible = Math.Max(0, layout.Body.Height / LineHeight);
        int wheel = PlayerInput.ScrollWheelDeltaForUI;
        PlayerInput.ScrollWheelDeltaForUI = 0;
        if (wheel != 0 || pageDelta != 0)
        {
            _scroll = ChestSortClickGuard.Scroll(_scroll, pageDelta != 0 ? pageDelta * Math.Max(1, visible - 1) : wheel > 0 ? -3 : 3,
                _totalLines, visible);
            _click.Invalidate(); _remove.Clear();
        }
        if (escape) { Close(); return; }
        if (!_click.Pressed(Mouse.GetState().LeftButton == ButtonState.Pressed, layout, memory.Revision, _scroll)) return;
        if (Contains(layout.Close, mouse)) { Close(); return; }
        if (Contains(layout.Toggle, mouse))
        {
            memory.SetEnabled(!memory.Enabled);
            _status = memory.Enabled ? "Memory enabled for this character." : "Memory paused; saved memories are kept.";
            Changed(); return;
        }
        if (Contains(layout.Clear, mouse) && memory.Entries.Count > 0)
        {
            if (_clearRevision == memory.Revision)
            {
                memory.Clear(); _status = "All saved memories removed from this character."; Changed();
            }
            else
            {
                _clearRevision = memory.Revision;
                _status = "Click Confirm clear to permanently remove every saved memory.";
                _click.Reset();
            }
            return;
        }
        var remove = _remove.LastOrDefault(hit => Contains(hit.Bounds, mouse));
        if (remove.Id is not null)
        {
            _status = memory.Forget(remove.Id) ? "Memory removed." : "Memory changed; review the current list.";
            Changed();
        }
    }

    private void Changed()
    { _clearRevision = null; _cachedRevision = -1; _click.Reset(); _remove.Clear(); }

    public void Draw(SpriteBatch batch)
    {
        if (!IsOpen || !ReferenceEquals(_owner, Main.LocalPlayer)) return;
        MemoryPanelGeometry layout = Geometry();
        GptMemoryPlayer memory = _owner!.GetModPlayer<GptMemoryPlayer>();
        EnsureRows(memory, layout.Body.Width);
        int visible = Math.Max(0, layout.Body.Height / LineHeight);
        _scroll = ChestSortClickGuard.Scroll(_scroll, 0, _totalLines, visible);
        _click.MarkDrawn(layout, memory.Revision, _scroll);
        _remove.Clear();
        GptChatPanel.DrawBox(batch, Rect(layout.Panel), new Color(15, 24, 36, 255), new Color(126, 197, 177));
        DrawText(batch, "Character memory", layout.Title, new Color(162, 243, 208), 0.82f);
        DrawWrapped(batch, $"{memory.Entries.Count} saved memories · {(memory.Enabled ? "On" : "Paused")}\n" +
            "Saved with this character across worlds. Save & Quit to keep changes.\n" +
            "Stable preferences and sorting choices only; you can remove any below.", layout.Summary, new Color(204, 220, 231), 0.62f);
        if (_rows.Count == 0)
            DrawWrapped(batch, "No saved memories yet. Talk naturally about your preferences or how you want an item sorted. " +
                "A small notice and Undo button appear when a useful memory is saved.", layout.Body, new Color(184, 205, 218), Scale);
        foreach (MemoryRow row in _rows)
        {
            int y = layout.Body.Y + (row.StartLine - _scroll) * LineHeight;
            for (int i = 0; i < row.Lines.Count; i++)
            {
                int lineY = y + i * LineHeight;
                if (lineY < layout.Body.Y || lineY + LineHeight > layout.Body.Bottom) continue;
                foreach (ChatTextRun run in row.Lines[i].Runs)
                    GptChatPanel.DrawClippedText(batch, run.Text, new Vector2(layout.Body.X + run.X * Scale, lineY),
                        row.Sorting ? new Color(116, 219, 255) : new Color(224, 234, 240), Scale, Rect(layout.Body));
            }
            ChatRect remove = new(layout.Body.Right - 68, y, 68, Math.Min(24, LineHeight));
            if (remove.Y >= layout.Body.Y && remove.Bottom <= layout.Body.Bottom && remove.X >= layout.Body.X)
            {
                GptChatPanel.DrawButton(batch, Rect(remove), "Remove", true, 0.60f);
                _remove.Add((remove, row.Id));
            }
        }
        bool scrollable = _totalLines > visible;
        string hint = _status.Length > 0 ? _status : scrollable ? "Scroll / Page Up / Page Down to review your saved memories." : "Changes affect this character in every world.";
        DrawText(batch, hint, layout.Status, _clearRevision.HasValue ? new Color(255, 176, 113) : new Color(162, 184, 203), 0.55f);
        if (scrollable && visible > 0)
        {
            int thumbHeight = Math.Clamp(layout.Body.Height * visible / Math.Max(1, _totalLines), Math.Min(16, layout.Body.Height), layout.Body.Height);
            int thumbY = layout.Body.Y + (layout.Body.Height - thumbHeight) * _scroll / Math.Max(1, _totalLines - visible);
            batch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(layout.Body.Right + 4, thumbY, 3, thumbHeight), new Color(133, 193, 180));
        }
        GptChatPanel.DrawButton(batch, Rect(layout.Toggle), memory.Enabled ? "Memory: On" : "Memory: Paused", true, 0.65f);
        GptChatPanel.DrawButton(batch, Rect(layout.Clear), _clearRevision == memory.Revision ? "Confirm clear" : "Clear all", memory.Entries.Count > 0, 0.65f);
        GptChatPanel.DrawButton(batch, Rect(layout.Close), "Close", true, 0.65f);
    }

    private void EnsureRows(GptMemoryPlayer memory, int width)
    {
        if (_cachedRevision == memory.Revision && _cachedWidth == width) return;
        if (_clearRevision.HasValue && _clearRevision != memory.Revision)
        { _clearRevision = null; _status = "Memories changed; review them before clearing."; }
        _cachedRevision = memory.Revision; _cachedWidth = width;
        _rows.Clear(); _totalLines = 0; _remove.Clear(); _click.Invalidate();
        foreach (MemoryEntry entry in memory.Entries)
        {
            bool sorting = entry.Kind == "sorting";
            string text = (sorting ? "Sorting: " : "Preference: ") + Clean(entry.Text);
            if (sorting) text += "\n" + FriendlyItem(entry.ItemKey) + " -> " + FriendlyTarget(entry.Target);
            var lines = Wrap(text, Math.Max(1, width - 78), Scale);
            _rows.Add(new(entry.Id, sorting, _totalLines, lines));
            _totalLines += Math.Max(2, lines.Count) + 1;
        }
    }

    private static string FriendlyItem(string key) => MemoryItemGroups.TryResolve(key) is MemoryItemIdentity group ? "All " + Clean(group.Name)
        : Clean(key.Contains('/') ? key[(key.IndexOf('/') + 1)..] : key);
    private static string FriendlyTarget(string target) => target.StartsWith("chest:", StringComparison.Ordinal)
        ? "Chest: " + Clean(target[6..])
        : "Category: " + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Clean(target).Replace('_', ' '));
    private static string Clean(string value) => new((value ?? "").Where(c => !char.IsControl(c)).Take(500).ToArray());
    private const float Scale = 0.72f;
    private static int LineHeight => Math.Max(18, (int)Math.Ceiling(FontAssets.MouseText.Value.LineSpacing * Scale) + 1);
    private static IReadOnlyList<ChatTextLine> Wrap(string text, int width, float scale)
    {
        var font = FontAssets.MouseText.Value;
        return ChatTextLayout.Wrap(new[] { new ChatTextSpan(text, AnswerTextKind.Plain) }, Math.Max(1, width / scale), c => GptChatPanel.Metrics(font, c), font.CharacterSpacing);
    }
    private static void DrawWrapped(SpriteBatch batch, string text, ChatRect bounds, Color color, float scale)
    {
        var lines = Wrap(text, bounds.Width, scale);
        int height = Math.Max(18, (int)Math.Ceiling(FontAssets.MouseText.Value.LineSpacing * scale) + 1);
        for (int i = 0; i < Math.Min(lines.Count, bounds.Height / height); i++)
            foreach (var run in lines[i].Runs)
                GptChatPanel.DrawClippedText(batch, run.Text, new Vector2(bounds.X + run.X * scale, bounds.Y + i * height), color, scale, Rect(bounds));
    }
    private static void DrawText(SpriteBatch batch, string text, ChatRect bounds, Color color, float scale) =>
        GptChatPanel.DrawClippedText(batch, GptChatPanel.HeadToFit(text, bounds.Width, scale), new Vector2(bounds.X, bounds.Y), color, scale, Rect(bounds));
    private static bool Contains(ChatRect rect, Point point) => ChestSortClickGuard.Contains(rect, point.X, point.Y);
    private static Rectangle Rect(ChatRect value) => new(value.X, value.Y, value.Width, value.Height);
    private static MemoryPanelGeometry Geometry()
    {
        float scale = Math.Max(0.01f, Main.UIScale);
        return MemoryPanelGeometry.Calculate((int)(PlayerInput.RealScreenWidth / scale), (int)(PlayerInput.RealScreenHeight / scale));
    }
    private sealed record MemoryRow(string Id, bool Sorting, int StartLine, IReadOnlyList<ChatTextLine> Lines);
}
