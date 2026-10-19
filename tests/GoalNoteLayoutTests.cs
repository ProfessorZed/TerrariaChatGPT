#nullable enable
using System;
using System.Collections.Generic;
using TerrariaGPT.Common.UI;

namespace TerrariaGPT.Tests;

public static class GoalNoteLayoutTests
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); }
        foreach (var size in new[] { (1, 1), (80, 50), (320, 180), (640, 360), (800, 600), (1280, 720), (1920, 1080) })
        foreach (int count in new[] { 1, 2, 4, 6 })
        foreach (bool collapsed in new[] { false, true })
        foreach (var position in new[] { (-1f, -1f), (0f, 0f), (1f, 1f), (float.NaN, float.PositiveInfinity), (200f, -20f) })
        {
            for (int i = 0; i < count; i++)
            {
                GoalNoteGeometry geometry = GoalNoteGeometry.Calculate(size.Item1, size.Item2, i, count, 48, collapsed, position.Item1, position.Item2);
                foreach (ChatRect rectangle in new[] { geometry.Panel, geometry.Header, geometry.Body, geometry.Collapse, geometry.Close, geometry.Footer })
                    Check(rectangle.Width >= 0 && rectangle.Height >= 0 && rectangle.X >= 0 && rectangle.Y >= 0 &&
                        (rectangle.Width == 0 || rectangle.Height == 0 || rectangle.Right <= size.Item1 && rectangle.Bottom <= size.Item2),
                        $"Goal rectangle fits {size.Item1}x{size.Item2}; note {i}/{count}, collapsed {collapsed}, position {position}.");
                Check(collapsed ? geometry.VisibleRows == 0 : geometry.VisibleRows * GoalNoteGeometry.RowHeight <= geometry.Body.Height,
                    "Goal rows cannot overlap header or footer.");
            }
        }
        var first = GoalNoteGeometry.Calculate(1280, 720, 0, 1, 7, false, -1, -1);
        Check(first.Panel.Y >= 330 && first.VisibleRows == 7, "A normal boss goal fits all seven rows beneath the minimap.");
        var six = new List<ChatRect>();
        for (int i = 0; i < 6; i++) six.Add(GoalNoteGeometry.Calculate(1280, 720, i, 6, 48, false, -1, -1).Panel);
        for (int a = 0; a < six.Count; a++) for (int b = a + 1; b < six.Count; b++)
            Check(six[a].Right <= six[b].X || six[b].Right <= six[a].X || six[a].Bottom <= six[b].Y || six[b].Bottom <= six[a].Y,
                "Six default notes do not overlap at the ordinary viewport.");
        var normalized = GoalNoteGeometry.Normalize(400, 220, 1280, 720, first.Panel.Width, first.Panel.Height);
        var moved = GoalNoteGeometry.Calculate(1280, 720, 0, 1, 7, false, normalized.X, normalized.Y);
        Check(Math.Abs(moved.Panel.X - 400) <= 1 && Math.Abs(moved.Panel.Y - 220) <= 1, "Dragging round-trips through persisted normalized positions.");
        var offscreen = GoalNoteGeometry.Normalize(-500, 5000, 1280, 720, 294, 303);
        Check(offscreen.X == 0 && offscreen.Y == 1, "Dragging beyond the window clamps instead of losing the note.");
        Check(GoalNoteText.Label("Stinger", "ingredient", 12) == "12 Stinger", "Ingredient labels retain the full recipe quantity.");
        Check(GoalNoteText.Summary("ingredient", 3, 12, 6, false) == "3 / 12 live + 6 remembered", "Visible quantities distinguish live and remembered supplies.");
        Check(GoalNoteText.Summary("craft", 0, 1, 0, true) == "Goal item obtained", "An acquired milestone remains understandable after its item is used.");
        Check(GoalNoteText.Summary("boss", 1, 1, 0, true).Contains("world record"), "Boss progress clearly identifies its world-wide scope.");
        Check(GoalNoteText.Summary("achievement", 1, 1, 0, true).Contains("account"), "Achievement progress identifies account-wide scope.");
        Check(GoalNoteText.Summary("manual", 0, 1, 0, false).Contains("click"), "Manual-only steps visibly explain their checkbox action.");
        for (int i = 0; i < 6; i++)
        {
            var inventory = GoalNoteGeometry.Calculate(1280, 720, i, 6, 48, false, -1, -1, true);
            Check(inventory.Panel.X >= 580 && inventory.Panel.Right <= 1000 && inventory.Panel.Y >= 0 && inventory.Panel.Bottom <= 720,
                "Inventory-open default notes avoid ordinary inventory slots and equipment controls.");
            Check(inventory.VisibleRows >= 1, "Inventory-open notes keep at least one progress row visible.");
        }
        var pinnedClosed = GoalNoteGeometry.Calculate(1280, 720, 0, 2, 7, false, .4f, .5f);
        var pinnedOpen = GoalNoteGeometry.Calculate(1280, 720, 0, 2, 7, false, .4f, .5f, true);
        Check(pinnedClosed.Panel == pinnedOpen.Panel, "Opening inventory preserves a player's dragged note placement and dimensions.");
        return passed;
    }
}
