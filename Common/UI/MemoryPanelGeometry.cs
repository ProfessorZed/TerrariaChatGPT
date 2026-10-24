#nullable enable
using System;

namespace TerrariaChatGPT.Common.UI;

internal readonly record struct MemoryPanelGeometry(ChatRect Panel, ChatRect Title, ChatRect Summary,
    ChatRect Body, ChatRect Status, ChatRect Toggle, ChatRect Clear, ChatRect Close)
{
    public static MemoryPanelGeometry Calculate(int width, int height)
    {
        // Both managers share the same accessible modal sizing, including tiny viewports.
        ChestSortPanelGeometry layout = ChestSortPanelGeometry.Calculate(width, height);
        return new(layout.Panel, layout.Title, layout.Summary, layout.Body, layout.ScrollHint,
            layout.Refresh, layout.Cancel, layout.Confirm);
    }
}

/// <summary>Memory actions require a fresh click on the currently drawn revision and scroll position.</summary>
internal sealed class MemoryClickGuard
{
    private bool _leftDown = true;
    private MemoryPanelGeometry? _drawn;
    private long _revision;
    private int _scroll;
    public void Reset() { _leftDown = true; _drawn = null; }
    public void Invalidate() => _drawn = null;
    public void MarkDrawn(MemoryPanelGeometry layout, long revision, int scroll)
    { _drawn = layout; _revision = revision; _scroll = scroll; }
    public bool Pressed(bool leftDown, MemoryPanelGeometry layout, long revision, int scroll)
    {
        bool edge = leftDown && !_leftDown;
        _leftDown = leftDown;
        return edge && _drawn == layout && _revision == revision && _scroll == scroll;
    }
}
