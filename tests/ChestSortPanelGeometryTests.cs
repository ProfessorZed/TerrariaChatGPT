#nullable enable
using System;
using System.Collections.Generic;
using TerrariaChatGPT.Common.UI;

namespace TerrariaChatGPT.Tests;

public static class ChestSortPanelGeometryTests
{
    public static string[] Run()
    {
        var passed = new List<string>();
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed.Add(message); }
        static bool Inside(ChatRect outer, ChatRect inner) => inner.Width == 0 || inner.Height == 0 ||
            inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
        static bool Overlap(ChatRect a, ChatRect b) => a.Width > 0 && a.Height > 0 && b.Width > 0 && b.Height > 0
            && a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;
        foreach (var size in new[] { (1,1), (80,60), (320,240), (480,270), (640,480), (1280,720), (1920,1080), (3440,1440) })
        {
            var g = ChestSortPanelGeometry.Calculate(size.Item1, size.Item2);
            Check(Inside(new(0,0,size.Item1,size.Item2), g.Panel), $"{size}: preview stays on screen.");
            var parts = new[] { g.Title, g.Summary, g.Body, g.ScrollHint, g.Remember, g.Refresh, g.Cancel, g.Confirm };
            foreach (var part in parts) if (!Inside(g.Panel, part)) throw new Exception($"{size}: child exceeds preview.");
            for (int i = 0; i < parts.Length; i++)
                for (int j = i + 1; j < parts.Length; j++)
                    if (Overlap(parts[i], parts[j])) throw new Exception($"{size}: child controls overlap.");
            passed.Add($"{size}: text viewport, footer and action buttons never overlap.");
        }
        foreach (int width in new[] { 480, 820, 1200 })
        {
            var chat = ChatPanelGeometry.Calculate(1920,1080,width,390);
            Check(Inside(chat.Panel,chat.SortChests) && !Overlap(chat.SortChests,chat.CopyAnswer)
                && !Overlap(chat.SortChests,chat.ModelSelector) && !Overlap(chat.SortChests,chat.EffortSelector),
                $"Chat width {width}: Sort chests does not cover model, effort or copy controls.");
        }
        var normal = ChestSortPanelGeometry.Calculate(1280,720);
        var guard = new ChestSortClickGuard();
        Check(!guard.Pressed(true,normal),"An unseen/opening click cannot confirm a preview.");
        guard.MarkDrawn(normal);
        Check(!guard.Pressed(true,normal),"Holding the opening click cannot confirm after drawing.");
        Check(!guard.Pressed(false,normal),"A release only arms the next deliberate click.");
        Check(guard.Pressed(true,normal),"A fresh click on a drawn preview may be handled.");
        Check(!guard.Pressed(true,normal),"A held confirm gesture cannot repeat.");
        guard.Pressed(false,normal);
        var resized = ChestSortPanelGeometry.Calculate(640,480);
        Check(!guard.Pressed(true,resized),"A resize rejects the old hit target until redrawn and clicked again.");
        guard.Reset(); guard.MarkDrawn(normal);
        Check(!guard.Pressed(true,normal),"Reopening requires another physical mouse release.");
        Check(!ChestSortClickGuard.Contains(default,0,0) && !ChestSortClickGuard.Contains(normal.Confirm,normal.Confirm.Right,normal.Confirm.Y),
            "Invisible buttons and pixels outside their right edge cannot commit.");
        Check(ChestSortClickGuard.Scroll(0,-3,100,10)==0 && ChestSortClickGuard.Scroll(95,3,100,10)==90
            && ChestSortClickGuard.Scroll(5,3,3,10)==0,"Scrolling clamps at either end and after content shrinks.");
        return passed.ToArray();
    }
}
