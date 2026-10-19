#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using Terraria;
using TerrariaGPT.Common.Players;

namespace TerrariaGPT.Tests;

/// <summary>Fabricated visible storage only; invokes the production observer.</summary>
public static class ObservationBenchmarks
{
    public static object Run()
    {
        Main.dedServ = false;
        Main.gameMenu = false;
        Main.netMode = 0;
        Main.myPlayer = 0;
        Main.ActiveWorldFileData = new WorldFileData { UniqueId = Guid.Empty };
        Main.chest = new ChestReadSpy { AllowedIndex = 7, RecordReads = false };
        Main.mouseItem = new Item();
        var chest = new Chest { x = 40, y = 50, name = "Fabricated full chest", item = Items(40) };
        Main.chest.Set(7, chest);
        var observer = new GptKnowledgePlayer { Player = new Player { whoAmI = 0, chest = 7, inventory = Items(58) } };
        observer.Initialize();
        for (int i = 0; i < 120; i++) observer.PostUpdate();
        const int ticks = 36_000;
        GC.Collect();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++) observer.PostUpdate();
        clock.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var snapshot = observer.CreateSnapshot();
        if (snapshot.OwnedChests.Count != 1 || snapshot.OwnedChests[0].Items.Count != 40)
            throw new InvalidOperationException("The benchmark did not capture the fabricated chest.");
        return new { Scenario = "Unchanged full open chest, full inventory", Ticks = ticks, Milliseconds = clock.Elapsed.TotalMilliseconds,
            AllocatedBytes = bytes, BytesPerTick = (double)bytes / ticks };
    }

    private static Item[] Items(int count) => Enumerable.Range(1, count)
        .Select(i => new Item { type = i, stack = i + 10, Name = "Fabricated Item " + i }).ToArray();
}
