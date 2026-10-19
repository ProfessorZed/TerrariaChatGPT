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
using Terraria.ModLoader;
using TerrariaChatGPT.Common.Goals;
using TerrariaChatGPT.Common.Players;
using TerrariaChatGPT.Common.Systems;

namespace TerrariaChatGPT.Common.UI;

/// <summary>Read-only automatic progress, with explicit local pin/remove actions.</summary>
internal sealed class GoalNotesPanel
{
    private readonly Dictionary<string, int> _scroll = new(StringComparer.Ordinal);
    private readonly GoalPointerCapture _pointer = new();
    private string? _dragging;
    private Point _dragOffset;
    private string? _hoverText;
    private static readonly Color TextColor = new(224, 234, 240);
    private static readonly Color MutedColor = new(161, 183, 199);
    private static readonly Color DoneColor = new(147, 235, 160);

    private static bool CanShow => !Main.dedServ && !Main.gameMenu && !Main.mapFullscreen && !Main.hideUI
        && !Main.editSign && !Main.editChest && !Main.ingameOptionsWindow
        && !Main.inFancyUI && Main.CurrentInputTextTakerOverride is null && !Main.LocalPlayer.dead
        && Main.LocalPlayer.talkNPC < 0;

    public void Reset()
    {
        _scroll.Clear();
        _dragging = null;
        // A world/UI reset can occur while a close/drag gesture is still held.
        // Keep pointer ownership until BeginFrame sees its physical release.
        _hoverText = null;
    }

    public void Update()
    {
        if (Main.dedServ) return;
        MouseState nativeMouse = Mouse.GetState();
        bool leftDown = nativeMouse.LeftButton == ButtonState.Pressed;
        _pointer.BeginFrame(leftDown, nativeMouse.RightButton == ButtonState.Pressed);
        bool clicked = _pointer.LeftPressed;
        // Do this before visibility and note-count checks: closing the note,
        // opening a map/menu or leaving its bounds must not release a held
        // gesture into the underlying inventory or world.
        ApplyCapturedPointer();
        _hoverText = null;
        if (!CanShow) { _dragging = null; return; }
        var player = Main.LocalPlayer.GetModPlayer<GptProgressPlayer>();
        IReadOnlyList<GoalNote> notes = player.ActiveGoals;
        (int width, int height) = ScreenSize();
        Point mouse = GptChatPanel.UiMouse();
        if (_dragging is not null)
        {
            int draggingIndex = -1;
            for (int i = 0; i < notes.Count; i++) if (notes[i].Id == _dragging) { draggingIndex = i; break; }
            if (!leftDown || draggingIndex < 0) _dragging = null;
            else
            {
                GoalNoteGeometry geometry = Geometry(notes[draggingIndex], draggingIndex, notes.Count, width, height);
                var normalized = GoalNoteGeometry.Normalize(mouse.X - _dragOffset.X, mouse.Y - _dragOffset.Y,
                    width, height, geometry.Panel.Width, geometry.Panel.Height);
                player.MoveGoal(_dragging, normalized.X, normalized.Y);
                ConsumePointer();
                PlayerInput.LockVanillaMouseScroll("TerrariaChatGPT:GoalDrag");
                return;
            }
        }
        // Topmost note gets the click if the player deliberately overlaps notes.
        for (int index = notes.Count - 1; index >= 0; index--)
        {
            GoalNote note = notes[index];
            GoalNoteGeometry geometry = Geometry(note, index, notes.Count, width, height);
            if (CoveredByChat(geometry) || !Rect(geometry.Panel).Contains(mouse)) continue;
            ConsumePointer();
            PlayerInput.LockVanillaMouseScroll("TerrariaChatGPT:Goals");
            if (Rect(geometry.Close).Contains(mouse))
            {
                _hoverText = "Remove this goal note";
                if (clicked) { player.RemoveGoal(note.Id); _scroll.Remove(note.Id); }
                return;
            }
            if (Rect(geometry.Collapse).Contains(mouse))
            {
                _hoverText = note.Collapsed ? "Expand goal" : "Collapse goal";
                if (clicked) player.SetCollapsed(note.Id, !note.Collapsed);
                return;
            }
            if (Rect(geometry.Header).Contains(mouse))
            {
                _hoverText = note.Title + "\nDrag the title to move this note.";
                if (clicked)
                {
                    _dragging = note.Id;
                    _dragOffset = new Point(mouse.X - geometry.Panel.X, mouse.Y - geometry.Panel.Y);
                }
                return;
            }
            int offset = ScrollOffset(note, geometry.VisibleRows);
            if (Rect(geometry.Body).Contains(mouse))
            {
                int delta = PlayerInput.ScrollWheelDeltaForUI;
                if (delta != 0)
                {
                    offset = Math.Clamp(offset + (delta > 0 ? -2 : 2), 0, Math.Max(0, note.Steps.Count - geometry.VisibleRows));
                    _scroll[note.Id] = offset;
                    PlayerInput.ScrollWheelDeltaForUI = 0;
                }
                int row = (mouse.Y - geometry.Body.Y) / GoalNoteGeometry.RowHeight;
                int stepIndex = offset + row;
                if (row >= 0 && row < geometry.VisibleRows && stepIndex < note.Steps.Count)
                {
                    GoalStepState step = note.Steps[stepIndex];
                    _hoverText = step.Label + (step.Detail.Length > 0 ? "\n" + step.Detail : "")
                        + (step.IsManual ? "\nClick to mark this step yourself." : "\nUpdates automatically from your known progress.");
                    if (clicked && step.IsManual) player.ToggleManualStep(note.Id, stepIndex);
                }
            }
            return;
        }
    }

    public void Draw(SpriteBatch batch)
    {
        if (!CanShow) return;
        IReadOnlyList<GoalNote> notes = Main.LocalPlayer.GetModPlayer<GptProgressPlayer>().ActiveGoals;
        (int width, int height) = ScreenSize();
        for (int index = 0; index < notes.Count; index++)
        {
            GoalNote note = notes[index];
            GoalNoteGeometry geometry = Geometry(note, index, notes.Count, width, height);
            if (CoveredByChat(geometry)) continue;
            DrawNote(batch, note, geometry);
        }
        if (!string.IsNullOrWhiteSpace(_hoverText))
            GptChatPanel.DrawLiteralTooltip(batch, _hoverText, GptChatPanel.UiMouse());
    }

    private void DrawNote(SpriteBatch batch, GoalNote note, GoalNoteGeometry geometry)
    {
        Rectangle panel = Rect(geometry.Panel);
        Color border = note.IsComplete ? new Color(90, 167, 111) : new Color(98, 132, 150);
        GptChatPanel.DrawBox(batch, panel, new Color(16, 26, 37, 234), border);
        Rectangle header = Rect(geometry.Header);
        GptChatPanel.DrawBox(batch, header, new Color(31, 48, 59, 248), border);
        int titleWidth = Math.Max(0, geometry.Collapse.X - panel.X - 15);
        string title = GptChatPanel.HeadToFit(note.Title, titleWidth, 0.68f);
        GptChatPanel.DrawClippedText(batch, title, new Vector2(panel.X + 8, panel.Y + 8),
            note.IsComplete ? DoneColor : new Color(255, 223, 142), 0.68f,
            new Rectangle(panel.X + 6, panel.Y + 2, titleWidth, Math.Max(0, header.Height - 4)));
        GptChatPanel.DrawButton(batch, Rect(geometry.Collapse), note.Collapsed ? "+" : "-", true, 0.7f);
        GptChatPanel.DrawButton(batch, Rect(geometry.Close), "x", true, 0.7f);
        if (note.Collapsed) return;
        int offset = ScrollOffset(note, geometry.VisibleRows);
        Rectangle body = Rect(geometry.Body);
        for (int row = 0; row < geometry.VisibleRows && offset + row < note.Steps.Count; row++)
        {
            GoalStepState step = note.Steps[offset + row];
            int y = body.Y + row * GoalNoteGeometry.RowHeight;
            Rectangle box = new(body.X + 1, y + 6, 13, 13);
            Color color = step.IsComplete ? DoneColor : TextColor;
            GptChatPanel.DrawBox(batch, box, step.IsComplete ? new Color(43, 82, 55) : new Color(27, 40, 53),
                step.IsManual ? new Color(179, 200, 219) : new Color(90, 116, 135));
            if (step.IsComplete) GptChatPanel.DrawClippedText(batch, "x", new Vector2(box.X + 2, box.Y - 2), DoneColor, 0.62f, box);
            string label = GptChatPanel.HeadToFit(GoalNoteText.Label(step.Label, step.Kind, step.Target), Math.Max(0, body.Width - 25), 0.64f);
            GptChatPanel.DrawClippedText(batch, label, new Vector2(body.X + 22, y + 1), color, 0.64f, body);
            string detail = GoalNoteText.Summary(step.Kind, step.Current, step.Target, step.Remembered, step.IsComplete);
            GptChatPanel.DrawClippedText(batch, GptChatPanel.HeadToFit(detail, Math.Max(0, body.Width - 25), 0.50f),
                new Vector2(body.X + 22, y + 19), step.IsComplete ? DoneColor * 0.85f : MutedColor, 0.50f, body);
        }
        int completed = note.Steps.Count(step => step.IsComplete);
        string footer = note.IsComplete ? "Goal complete" : completed + " / " + note.Steps.Count + " completed";
        if (note.Steps.Count > geometry.VisibleRows) footer += "  |  Scroll for more";
        GptChatPanel.DrawClippedText(batch, GptChatPanel.HeadToFit(footer, geometry.Footer.Width, 0.54f),
            new Vector2(geometry.Footer.X, geometry.Footer.Y + 3), note.IsComplete ? DoneColor : MutedColor, 0.54f, Rect(geometry.Footer));
        if (note.Steps.Count > geometry.VisibleRows && geometry.VisibleRows > 0)
        {
            int thumbHeight = Math.Max(10, body.Height * geometry.VisibleRows / note.Steps.Count);
            int thumbY = body.Y + (body.Height - thumbHeight) * offset / Math.Max(1, note.Steps.Count - geometry.VisibleRows);
            batch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(panel.Right - 4, thumbY, 2, thumbHeight), new Color(127, 176, 188));
        }
    }

    private int ScrollOffset(GoalNote note, int visibleRows)
    {
        _scroll.TryGetValue(note.Id, out int offset);
        offset = Math.Clamp(offset, 0, Math.Max(0, note.Steps.Count - visibleRows));
        _scroll[note.Id] = offset;
        return offset;
    }

    private static GoalNoteGeometry Geometry(GoalNote note, int index, int count, int width, int height) =>
        GoalNoteGeometry.Calculate(width, height, index, count, note.Steps.Count, note.Collapsed, note.X, note.Y, Main.playerInventory);

    private void ConsumePointer()
    {
        _pointer.CaptureHeld();
        Main.LocalPlayer.mouseInterface = true;
        ApplyCapturedPointer();
    }

    private void ApplyCapturedPointer()
    {
        if (!_pointer.HasCapture) return;
        if (!Main.gameMenu) Main.LocalPlayer.mouseInterface = true;
        // Vanilla stack splitting/repeat crafting also inspect held buttons,
        // not just release edges. Reapply the mask every frame until native
        // release, even if vanilla's draw code has rearmed its release flags.
        if (_pointer.BlockLeft) { Main.mouseLeftRelease = false; Main.mouseLeft = false; }
        if (_pointer.BlockRight) { Main.mouseRightRelease = false; Main.mouseRight = false; }
    }

    private static bool CoveredByChat(GoalNoteGeometry geometry) =>
        ModContent.GetInstance<GptChatSystem>().IsGptOpen && Rect(geometry.Panel).Intersects(GptChatPanel.GetLayout().Panel);

    private static (int Width, int Height) ScreenSize()
    {
        float scale = Math.Max(0.01f, Main.UIScale);
        return (Math.Max(1, (int)(PlayerInput.RealScreenWidth / scale)), Math.Max(1, (int)(PlayerInput.RealScreenHeight / scale)));
    }

    private static Rectangle Rect(ChatRect rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
}
