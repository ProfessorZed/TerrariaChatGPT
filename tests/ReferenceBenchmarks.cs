#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.UI;
using TerrariaChatGPT.Common.Knowledge;

namespace TerrariaChatGPT.Tests;

/// <summary>Only generated public item definitions; no player/world state exists in this host.</summary>
public static class ReferenceBenchmarks
{
    public static object[] Run()
    {
        ContentSamples.ItemsByType.Clear();
        Lang.Tooltips.Clear();
        const int itemCount = 6_000;
        for (int i = 1; i <= itemCount; i++)
        {
            ContentSamples.ItemsByType[i] = new Item { type = i, Name = "Fabricated Item " + i };
            Lang.Tooltips[i] = new ItemTooltip { Text = new[] { i % 100 == 0 ? "Negates fall damage" : "A fabricated reference item" } };
        }
        var named = Measure("Named resolution across 6,000 public items", 128,
            () => { if (GameReferenceFacts.ResolveItem("Where do I find Fabricated Item 5999?")?.type != 5999) throw new InvalidOperationException("Named resolution changed."); });
        var capability = Measure("Capability resolution across 6,000 public items", 128,
            () => { if (GameReferenceFacts.ResolveSubjects("What negates fall damage and can I make it?", null).Count != 6) throw new InvalidOperationException("Capability resolution changed."); });
        return new[] { named, capability };
    }

    private static object Measure(string scenario, int count, Action action)
    {
        for (int i = 0; i < 4; i++) action();
        GC.Collect();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < count; i++) action();
        clock.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return new { Scenario = scenario, Queries = count, Milliseconds = clock.Elapsed.TotalMilliseconds,
            AllocatedBytes = bytes, BytesPerQuery = (double)bytes / count };
    }
}
