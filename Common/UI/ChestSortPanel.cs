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
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.Sorting;

namespace TerrariaChatGPT.Common.UI;

/// <summary>Local, explicit preview/confirm UI. Nothing in drawing or keyboard input moves items.</summary>
internal sealed class ChestSortPanel
{
    private readonly ChestSortClickGuard _click = new();
    private readonly GoalPointerCapture _capture = new();
    private readonly List<ChatTextSpan> _content = new();
    private readonly List<ChatTextSpan> _leftovers = new();
    private IReadOnlyList<ChatTextLine> _lines = Array.Empty<ChatTextLine>();
    private int _scroll, _cachedWidth = -1;
    private float _cachedScale;
    private bool _canConfirm;
    private SortSuggestion? _proposal;
    private Player? _owner;
    private int _epoch, _world;
    private bool _remembered;
    private bool _completedGptPlan;
    private string _memoryStatus = "";
    private string _summary = "";
    private string _compactSummary = "";
    public bool IsOpen { get; private set; }
    public bool OwnsPointer => IsOpen || _capture.HasCapture;

    public void Open(SortSuggestion? proposal = null)
    {
        // Accepted suggestions contain detached, read-only assignments. Keep
        // their identity so an older reply cannot reopen a superseded plan.
        if (proposal is not null && !GptAssistantService.IsCurrentSortSuggestion(proposal)) return;
        _proposal = proposal;
        _owner = Main.LocalPlayer;
        _world = Main.worldID;
        _epoch = GptAssistantService.Epoch;
        _remembered = false;
        _completedGptPlan = false;
        _memoryStatus = "";
        IsOpen = true;
        _click.Reset();
        Refresh();
        CapturePointer();
    }

    public void Close()
    {
        IsOpen = false;
        _canConfirm = false;
        _proposal = null;
        _owner = null;
        ChestSortService.Cancel();
        _click.Reset();
        // Keep ownership of a held closing gesture until its physical release.
    }

    public void Update(bool chatOpen)
    {
        if (IsOpen && (!chatOpen || !CurrentContext())) Close();
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
        PlayerInput.LockVanillaMouseScroll("TerrariaChatGPT:ChestSort");
        if (_capture.BlockLeft) { Main.mouseLeftRelease = false; Main.mouseLeft = false; }
        if (_capture.BlockRight) { Main.mouseRightRelease = false; Main.mouseRight = false; }
    }

    public void HandleInput(Point mouse, bool escape, int pageDelta)
    {
        if (!IsOpen) return;
        if (!CurrentContext()) { Close(); CapturePointer(); return; }
        CapturePointer();
        PlayerInput.WritingText = true;
        int wheel = PlayerInput.ScrollWheelDeltaForUI;
        PlayerInput.ScrollWheelDeltaForUI = 0;
        ChestSortPanelGeometry layout = Geometry();
        float scale = TextScale();
        EnsureLines(layout.Body.Width, scale);
        int visible = Math.Max(0, layout.Body.Height / LineHeight(scale));
        if (wheel != 0 || pageDelta != 0)
            _scroll = ChestSortClickGuard.Scroll(_scroll, pageDelta != 0 ? pageDelta * Math.Max(1, visible - 1)
                : (wheel > 0 ? -1 : 1) * Math.Min(3, Math.Max(1, visible - 1)),
                _lines.Count, visible);
        if (escape) { Close(); return; }
        if (!_click.Pressed(Mouse.GetState().LeftButton == ButtonState.Pressed, layout)) return;
        if (ChestSortClickGuard.Contains(layout.Cancel, mouse.X, mouse.Y)) { Close(); return; }
        if (!_completedGptPlan && ChestSortClickGuard.Contains(layout.Refresh, mouse.X, mouse.Y)) { Refresh(); return; }
        if (_proposal is not null && !_remembered && ChestSortClickGuard.Contains(layout.Remember, mouse.X, mouse.Y))
        {
            _remembered = GptAssistantService.RememberSortChoices(_proposal, _epoch, out _memoryStatus);
            // Saving changes the memory revision and invalidates the old transfer
            // preview. Rebuild the same reviewed destinations, never move them.
            Refresh();
            return;
        }
        if (_canConfirm && ChestSortClickGuard.Contains(layout.Confirm, mouse.X, mouse.Y))
        {
            // Invalidate the UI before executing; no repeated or held click can replay a plan.
            _canConfirm = false;
            ChestSortResult result = ChestSortService.Confirm();
            if (result.Success && _proposal is not null)
            {
                GptAssistantService.CompleteSortSuggestion(_proposal);
                _proposal = null;
                _completedGptPlan = true;
            }
            _summary = result.Success ? $"Sorted {result.MovedItemCount} items from {result.MovedStackCount} inventory stacks."
                : "Nothing sorted. Refresh the preview before trying again.";
            _compactSummary = _summary;
            _content.Clear();
            Add(result.Message + "\n\n", result.Success ? AnswerTextKind.Owned : AnswerTextKind.Missing);
            if (_completedGptPlan)
                Add("This plan is complete. Close this preview and ask GPT for a new plan.\n\n", AnswerTextKind.Muted);
            if (result.Success)
                _content.AddRange(_leftovers);
            Add("Hotbar, favorited items, equipment, coin slots and ammo slots were protected.", AnswerTextKind.Muted);
            InvalidateLines();
        }
    }

    private void Refresh()
    {
        _click.Reset();
        _canConfirm = false;
        _content.Clear();
        _leftovers.Clear();
        ChestSortPreview? preview = ChestSortService.BeginPreview(_proposal);
        if (preview is null)
        {
            _summary = "No items have been moved.";
            _compactSummary = _summary;
            Add(ChestSortService.Status, AnswerTextKind.Missing);
            InvalidateLines();
            return;
        }
        var plan = preview.Plan;
        _canConfirm = plan.Moves.Count > 0;
        _summary = $"{plan.MovedItemCount} items ready to move to nearby named chests.\n" +
            (_proposal is null ? "Only main inventory is sorted. Hotbar and favorited items stay with you."
                : "GPT's destinations below are used by Confirm sort. Unassigned and protected items stay with you.");
        _compactSummary = $"{plan.MovedItemCount} items ready. Hotbar and favorites stay.";
        if (preview.EligibleChestCount == 0)
            Add(ChestSortService.Status + "\n\n", AnswerTextKind.Missing);
        if (_canConfirm)
        {
            Add("WILL MOVE\n", AnswerTextKind.Owned);
            // One stack may be split over several chest slots. Show a single readable destination row.
            foreach (var group in plan.Moves.GroupBy(move => new { move.SourceSlot, move.ChestId, move.ItemName, move.ChestName }))
            {
                Add($"{group.Sum(move => move.Amount)} {Clean(group.Key.ItemName)}", AnswerTextKind.Item);
                Add("  ->  ", AnswerTextKind.Muted);
                Add(Clean(group.Key.ChestName) + "\n", AnswerTextKind.Location);
            }
            Add("\n", AnswerTextKind.Plain);
        }
        else Add("No matching items can be moved.\n\n", AnswerTextKind.Missing);
        if (plan.Unsorted.Count > 0)
        {
            int leftoverStart = _content.Count;
            Add("STAY IN INVENTORY\n", AnswerTextKind.Missing);
            foreach (var item in plan.Unsorted)
            {
                Add($"{item.Remaining} {Clean(item.Name)}", AnswerTextKind.Item);
                string reason = _proposal is not null ? item.Reason : item.Code switch
                {
                    ChestSortUnsortedReason.NoMatchingChest => "No suitable named chest nearby.",
                    ChestSortUnsortedReason.NoSpace => "Suitable chests have no usable space for this stack.",
                    ChestSortUnsortedReason.AmbiguousCategory => "More than one suitable category; choose a chest named for this item.",
                    _ => item.Reason
                };
                Add(" - " + Clean(reason) + "\n", AnswerTextKind.Missing);
                if (!string.IsNullOrWhiteSpace(item.SuggestedChest))
                {
                    bool assigned = _proposal?.Assignments.Any(assignment => assignment.ItemKey == item.ItemKey) == true;
                    Add(assigned ? "  Destination: " : "  Recommended chest: ", AnswerTextKind.Muted);
                    Add(Clean(item.SuggestedChest) + "\n", AnswerTextKind.Location);
                }
            }
            Add("\n", AnswerTextKind.Plain);
            _leftovers.AddRange(_content.Skip(leftoverStart));
        }
        InvalidateLines();
    }

    public void Draw(SpriteBatch batch)
    {
        if (!IsOpen) return;
        if (!CurrentContext()) { Close(); return; }
        ChestSortPanelGeometry geometry = Geometry();
        _click.MarkDrawn(geometry);
        float scale = TextScale();
        EnsureLines(geometry.Body.Width, scale);
        int lineHeight = LineHeight(scale);
        int visible = Math.Max(0, geometry.Body.Height / lineHeight);
        _scroll = ChestSortClickGuard.Scroll(_scroll, 0, _lines.Count, visible);
        Rectangle body = Rect(geometry.Body);
        GptChatPanel.DrawBox(batch, Rect(geometry.Panel), new Color(15, 24, 36, 255), new Color(126, 197, 177));
        DrawText(batch, "Sort inventory into named chests", geometry.Title, new Color(162, 243, 208), 0.82f);
        DrawWrapped(batch, geometry.Summary.Height < 66 ? _compactSummary : _summary,
            geometry.Summary, new Color(204, 220, 231), 0.65f);
        for (int row = 0; row < visible && _scroll + row < _lines.Count; row++)
            foreach (ChatTextRun run in _lines[_scroll + row].Runs)
                GptChatPanel.DrawClippedText(batch, run.Text, new Vector2(body.X + run.X * scale, body.Y + row * lineHeight),
                    ColorFor(run.Kind), scale, body);
        bool scrollable = _lines.Count > visible;
        DrawText(batch, _memoryStatus.Length > 0 ? _memoryStatus : scrollable ? $"Scroll / Page Up / Page Down   ({Math.Min(_lines.Count, _scroll + 1)}-{Math.Min(_lines.Count, _scroll + visible)} of {_lines.Count})"
            : "Preview only until you confirm. Escape cancels.", geometry.ScrollHint, new Color(162, 184, 203), 0.55f);
        if (scrollable && visible > 0 && body.Height > 0)
        {
            int thumbHeight = Math.Clamp(body.Height * visible / Math.Max(1, _lines.Count), Math.Min(16, body.Height), body.Height);
            int thumbY = body.Y + (body.Height - thumbHeight) * _scroll / Math.Max(1, _lines.Count - visible);
            batch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(body.Right + 4, thumbY, 3, thumbHeight), new Color(133, 193, 180));
        }
        GptChatPanel.DrawButton(batch, Rect(geometry.Refresh), _completedGptPlan ? "Plan complete" : "Refresh preview", !_completedGptPlan, 0.63f);
        if (_proposal is not null)
            GptChatPanel.DrawButton(batch, Rect(geometry.Remember), _remembered ? "Choices remembered - Undo in chat"
                : "Remember choices for future sorting", !_remembered, 0.63f);
        GptChatPanel.DrawButton(batch, Rect(geometry.Cancel), _canConfirm ? "Cancel" : "Close", true, 0.66f);
        GptChatPanel.DrawButton(batch, Rect(geometry.Confirm), "Confirm sort", _canConfirm, 0.66f);
    }

    private void Add(string text, AnswerTextKind kind) => _content.Add(new(text, kind));
    private bool CurrentContext() => !Main.gameMenu && ReferenceEquals(_owner, Main.LocalPlayer)
        && _world == Main.worldID && _epoch == GptAssistantService.Epoch
        && (_proposal is null || GptAssistantService.IsCurrentSortSuggestion(_proposal));
    private void InvalidateLines() { _cachedWidth = -1; _scroll = 0; }
    private void EnsureLines(int width, float scale)
    {
        if (_cachedWidth == width && _cachedScale == scale) return;
        _cachedWidth = width; _cachedScale = scale;
        var font = FontAssets.MouseText.Value;
        var lines = ChatTextLayout.Wrap(_content, Math.Max(1, width / scale), c => GptChatPanel.Metrics(font, c), font.CharacterSpacing).ToList();
        while (lines.Count > 0 && lines[^1].Runs.Count == 0) lines.RemoveAt(lines.Count - 1);
        _lines = lines;
    }
    private static string Clean(string text) => new((text ?? "").Where(character => !char.IsControl(character)).Take(200).ToArray());
    private static float TextScale()
    {
        float configured = ModContent.GetInstance<GptClientConfig>().ChatTextScale;
        return float.IsFinite(configured) ? Math.Clamp(configured, 0.7f, 1.2f) : 0.9f;
    }
    private static int LineHeight(float scale) => Math.Max(18, (int)Math.Ceiling(FontAssets.MouseText.Value.LineSpacing * scale) + 1);
    private static Color ColorFor(AnswerTextKind kind) => kind switch
    {
        AnswerTextKind.Item => new(116, 219, 255), AnswerTextKind.Location => new(255, 218, 112),
        AnswerTextKind.Missing => new(255, 176, 113), AnswerTextKind.Owned => new(147, 235, 160),
        AnswerTextKind.Muted => new(163, 183, 200), _ => new(224, 234, 240)
    };
    private static void DrawText(SpriteBatch batch, string text, ChatRect bounds, Color color, float scale) =>
        GptChatPanel.DrawClippedText(batch, GptChatPanel.HeadToFit(text, bounds.Width, scale), new Vector2(bounds.X, bounds.Y), color, scale, Rect(bounds));
    private static void DrawWrapped(SpriteBatch batch, string text, ChatRect bounds, Color color, float scale)
    {
        var font = FontAssets.MouseText.Value;
        var lines = ChatTextLayout.Wrap(new[] { new ChatTextSpan(text, AnswerTextKind.Plain) }, Math.Max(1, bounds.Width / scale), c => GptChatPanel.Metrics(font, c), font.CharacterSpacing);
        int lineHeight = LineHeight(scale);
        for (int i = 0; i < Math.Min(lines.Count, bounds.Height / lineHeight); i++)
            foreach (var run in lines[i].Runs)
                GptChatPanel.DrawClippedText(batch, run.Text, new Vector2(bounds.X + run.X * scale, bounds.Y + i * lineHeight), color, scale, Rect(bounds));
    }
    private static ChestSortPanelGeometry Geometry()
    {
        float scale = Math.Max(0.01f, Main.UIScale);
        return ChestSortPanelGeometry.Calculate((int)(PlayerInput.RealScreenWidth / scale), (int)(PlayerInput.RealScreenHeight / scale));
    }
    private static Rectangle Rect(ChatRect rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
}
