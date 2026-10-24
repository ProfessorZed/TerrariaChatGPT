#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Players;

namespace TerrariaChatGPT.Tests;

/// <summary>Behavioral checks of the actual game observer using guarded dummy chests.</summary>
public static class ChestMemoryTests
{
    private static readonly Guid FirstWorld = new("00000000-0000-4000-8000-000000000001");
    private static readonly Guid SecondWorld = new("00000000-0000-4000-8000-000000000002");

    public static string[] Run()
    {
        var results = new List<string>();
        Check("Opening a chest is remembered on the next tick without a claim or 30-tick delay", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            Assert(SavedChests(fixture.Observer).Single().GetList<TagCompound>("items").Single().GetInt("stack") == 12,
                "A newly opened chest was not saved immediately.");
            Assert(Main.chest.Reads.Count > 0 && Main.chest.Reads.All(index => index == 7), "The open chest was not the only chest read.");
        }, results);

        Check("Contents refresh every open tick and remain detached after the chest closes", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            fixture.Chest.item[0].stack = 4;
            fixture.Observer.PostUpdate();
            fixture.Close();
            fixture.Chest.item[0].stack = 999;
            Main.chest.Reads.Clear();
            for (int tick = 0; tick < 35; tick++)
                fixture.Observer.PostUpdate();
            KnowledgeSnapshot snapshot = fixture.Observer.CreateSnapshot();
            Assert(snapshot.OwnedChests.Single().Items.Single().Stack == 4, "A closed chest was reread or retained a live item reference.");
            Assert(!snapshot.OwnedChests.Single().IsCurrentlyOpen, "A closed chest was marked open.");
            Assert(Main.chest.Reads.Count == 0, "Closing the chest did not stop reads.");
        }, results);

        Check("Unopened chests are never indexed or included in memory", () =>
        {
            var fixture = new Fixture();
            Main.chest.Set(99, NewChest(900, 901, "Unopened secret", 1000));
            for (int tick = 0; tick < 35; tick++)
                fixture.Observer.PostUpdate();
            Assert(fixture.Observer.CreateSnapshot().OwnedChests.Count == 0, "An unopened chest entered memory.");
            Assert(Main.chest.Reads.Count == 0, "A closed or unopened chest was indexed.");
        }, results);

        Check("Same-count slot replacements, renamed items and mod identities refresh immediately", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            fixture.Chest.item[0] = new Item { type = 2, stack = 12, Name = "Torch" };
            fixture.Observer.PostUpdate();
            ItemStackSnapshot replaced = fixture.Observer.CreateSnapshot().OwnedChests.Single().Items.Single();
            Assert(replaced.Name == "Torch" && replaced.ItemKey == "Terraria/Torch", "Equal counts hid a changed item identity.");
            fixture.Chest.item[0].Name = "Named Torch";
            fixture.Chest.item[0].ModItem = new Terraria.ModLoader.ModItem { FullName = "Fixture/FirstTorch" };
            fixture.Observer.PostUpdate();
            fixture.Chest.item[0].ModItem!.FullName = "Fixture/SecondTorch";
            fixture.Observer.PostUpdate();
            ItemStackSnapshot renamed = fixture.Observer.CreateSnapshot().OwnedChests.Single().Items.Single();
            Assert(renamed.Name == "Named Torch" && renamed.ItemKey == "Fixture/SecondTorch", "A name or mod identity change was skipped.");
        }, results);

        Check("Unchanged visible contents reuse detached stacks while chest renaming refreshes locations", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            var first = fixture.Observer.CreateSnapshot().OwnedChests.Single();
            for (int tick = 0; tick < 65; tick++) fixture.Observer.PostUpdate();
            var unchanged = fixture.Observer.CreateSnapshot().OwnedChests.Single();
            Assert(ReferenceEquals(first.Items, unchanged.Items), "Unchanged chest contents were reconstructed every tick.");
            fixture.Chest.name = "Jungle supplies";
            fixture.Observer.PostUpdate();
            var renamed = fixture.Observer.CreateSnapshot().OwnedChests.Single();
            Assert(renamed.Label == "Jungle supplies" && renamed.Items.Single().Location.StartsWith("Jungle supplies", StringComparison.Ordinal),
                "A renamed chest retained its old label or item locations.");
        }, results);

        Check("Personal bank and invalid chest indices never reach world chest storage", () =>
        {
            var fixture = new Fixture();
            foreach (int index in new[] { -1, -2, -3, -4, -5, Main.maxChests, Main.maxChests + 10 })
            {
                fixture.Observer.Player.chest = index;
                fixture.Observer.PostUpdate();
                fixture.Observer.CreateSnapshot();
                fixture.Observer.ClaimOpenChest();
                fixture.Observer.ForgetOpenChest();
            }
            Assert(Main.chest.Reads.Count == 0, "A bank or invalid index reached Main.chest.");
        }, results);

        Check("Snapshots identify the currently open remembered chest without exporting another chest", () =>
        {
            var fixture = new Fixture();
            Main.chest.Set(99, NewChest(900, 901, "Unopened secret", 1000));
            fixture.Open(7, fixture.Chest);
            KnowledgeSnapshot snapshot = fixture.Observer.CreateSnapshot();
            Assert(snapshot.OwnedChests.Count == 1 && snapshot.OwnedChests.Single().IsCurrentlyOpen,
                "The open chest was missing or was not marked current.");
            Assert(snapshot.OwnedChests.Single().Label == "Workshop", "A different chest was exported.");
        }, results);

        Check("Forgetting an open chest persists while open, after reopening, and after save/load", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            fixture.Observer.ForgetOpenChest();
            fixture.Observer.PostUpdate();
            Assert(fixture.Observer.CreateSnapshot().OwnedChests.Count == 0, "A forgotten chest was immediately remembered again.");
            fixture.Close();
            fixture.Observer.PostUpdate();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            Assert(SavedChests(fixture.Observer).Count == 0, "A forgotten chest was remembered on reopening.");

            TagCompound saved = Save(fixture.Observer);
            Assert(saved.GetList<TagCompound>("worlds").Single().GetList<string>("ignoredChests").Contains("40,50"),
                "The forgotten chest exclusion was not saved.");
            var restored = new GptKnowledgePlayer { Player = fixture.Observer.Player };
            restored.Initialize();
            restored.LoadData(saved);
            restored.OnEnterWorld();
            restored.PostUpdate();
            Assert(restored.CreateSnapshot().OwnedChests.Count == 0, "Save/load lost the forgotten chest exclusion.");
        }, results);

        Check("Explicit claim restores a forgotten chest and resumes automatic refresh", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            fixture.Observer.ForgetOpenChest();
            fixture.Chest.item[0].stack = 8;
            fixture.Observer.ClaimOpenChest();
            Assert(fixture.Observer.CreateSnapshot().OwnedChests.Single().Items.Single().Stack == 8,
                "Explicit remembering did not restore the chest.");
            fixture.Chest.item[0].stack = 3;
            fixture.Observer.PostUpdate();
            Assert(SavedChests(fixture.Observer).Single().GetList<TagCompound>("items").Single().GetInt("stack") == 3,
                "Automatic refresh did not resume after explicit remembering.");
            Assert(!Save(fixture.Observer).GetList<TagCompound>("worlds").Single().GetList<string>("ignoredChests").Contains("40,50"),
                "Explicit remembering left the exclusion saved.");
        }, results);

        Check("Chest memories and exclusions stay separate for each world UUID", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            fixture.Observer.ForgetOpenChest();
            fixture.Close();
            Main.ActiveWorldFileData.UniqueId = SecondWorld;
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            Assert(fixture.Observer.CreateSnapshot().OwnedChests.Count == 1, "Another world's exclusion hid this chest.");
            fixture.Close();
            Main.ActiveWorldFileData.UniqueId = FirstWorld;
            Assert(fixture.Observer.CreateSnapshot().OwnedChests.Count == 0, "Another world's memory leaked into this world.");
            Assert(Save(fixture.Observer).GetList<TagCompound>("worlds").Count == 2, "World partitions were not preserved.");
        }, results);

        Check("Existing saved chest memories load without a new ignored-chests field", () =>
        {
            var fixture = new Fixture();
            var oldWorld = new TagCompound
            {
                ["world"] = FirstWorld.ToString("N"),
                ["subjects"] = new List<TagCompound>(),
                ["chests"] = new List<TagCompound>
                {
                    new()
                    {
                        ["label"] = "Legacy chest", ["x"] = 21, ["y"] = 22,
                        ["observed"] = "2026-10-01T12:34:00.0000000+00:00",
                        ["items"] = new List<TagCompound>
                        {
                            new() { ["key"] = "Terraria/IronBar", ["name"] = "Iron Bar", ["stack"] = 6, ["location"] = "Legacy chest" }
                        }
                    }
                }
            };
            fixture.Observer.LoadData(new TagCompound { ["worlds"] = new List<TagCompound> { oldWorld } });
            KnowledgeSnapshot snapshot = fixture.Observer.CreateSnapshot();
            Assert(snapshot.OwnedChests.Single().Label == "Legacy chest" && snapshot.OwnedChests.Single().Items.Single().Stack == 6,
                "An old chest save was lost or changed.");
            Assert(Main.chest.Reads.Count == 0, "Loading legacy memory read live storage.");
        }, results);

        Check("Multiplayer neither reads open chest storage nor exports saved single-player chests", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            Main.netMode = NetmodeID.MultiplayerClient;
            Main.chest.AllowedIndex = null;
            Main.chest.Reads.Clear();
            for (int tick = 0; tick < 35; tick++)
                fixture.Observer.PostUpdate();
            fixture.Observer.ClaimOpenChest();
            fixture.Observer.ForgetOpenChest();
            Assert(fixture.Observer.CreateSnapshot().OwnedChests.Count == 0, "A multiplayer snapshot exported single-player chest memory.");
            Assert(Main.chest.Reads.Count == 0, "Multiplayer accessed open world chest storage.");
            Assert(SavedChests(fixture.Observer).Count == 1, "Multiplayer changed saved single-player chest memory.");
        }, results);

        Check("Another player's observer does not read or save chest contents", () =>
        {
            var fixture = new Fixture();
            fixture.Observer.Player.whoAmI = Main.myPlayer + 1;
            fixture.Observer.Player.chest = 7;
            fixture.Observer.OnEnterWorld();
            for (int tick = 0; tick < 35; tick++)
                fixture.Observer.PostUpdate();
            fixture.Observer.ClaimOpenChest();
            fixture.Observer.ForgetOpenChest();
            Assert(Save(fixture.Observer).GetList<TagCompound>("worlds").Count == 0, "Another player's observer saved world knowledge.");
            Assert(Main.chest.Reads.Count == 0, "Another player's observer read chest storage.");
        }, results);

        Check("Menu and dedicated-server updates do not read chest storage", () =>
        {
            var fixture = new Fixture();
            fixture.Observer.Player.chest = 7;
            Main.gameMenu = true;
            fixture.Observer.OnEnterWorld();
            for (int tick = 0; tick < 35; tick++)
                fixture.Observer.PostUpdate();
            Main.gameMenu = false;
            Main.dedServ = true;
            fixture.Observer.OnEnterWorld();
            for (int tick = 0; tick < 35; tick++)
                fixture.Observer.PostUpdate();
            Assert(Main.chest.Reads.Count == 0, "An inactive game context read chest storage.");
        }, results);

        Check("Sorting candidates contain only exact instances opened this visit and do not read closed storage", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            fixture.Close();
            Main.chest.Reads.Clear();
            var candidate = fixture.Observer.GetSortingChestCandidates().Single();
            Assert(candidate.X == 40 && candidate.Y == 50 && ReferenceEquals(candidate.Instance, fixture.Chest), "Sorting candidate lost its observed identity.");
            Assert(Main.chest.Reads.Count == 0, "Candidate discovery read a closed chest.");
        }, results);

        Check("Saved chest memory remains eligible without reopening after restart", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            TagCompound saved = Save(fixture.Observer);
            fixture.Close();
            Guid previous = fixture.Observer.SortingSessionId;
            fixture.Observer.LoadData(saved);
            Assert(fixture.Observer.SortingSessionId != previous && fixture.Observer.GetSortingChestCandidates().Count == 1,
                "A remembered chest unnecessarily required reopening.");
            var restored = fixture.Observer.GetSortingChestCandidates().Single();
            Assert(restored.Instance is null && restored.Identity.Length > 0, "Saved memory retained a live instance or lost its token.");
            Assert(fixture.Observer.BindSortingChest(restored, fixture.Chest, 21, 0, 0), "An unchanged known chest could not be rebound.");
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            fixture.Close();
            Assert(fixture.Observer.GetSortingChestCandidates().Count == 1, "Reopening did not restore eligibility.");
            fixture.Observer.OnEnterWorld();
            Assert(fixture.Observer.GetSortingChestCandidates().Single().Instance is null, "World entry retained live chest references.");
        }, results);

        Check("Forgetting and switching worlds revoke sorting identities", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            fixture.Observer.ForgetOpenChest();
            Assert(fixture.Observer.GetSortingChestCandidates().Count == 0, "Ignored chest remained sortable.");
            fixture.Observer.ClaimOpenChest();
            Assert(fixture.Observer.GetSortingChestCandidates().Count == 1, "Claiming an open chest did not restore sorting.");
            fixture.Close();
            Main.ActiveWorldFileData.UniqueId = SecondWorld;
            Assert(fixture.Observer.GetSortingChestCandidates().Count == 0, "First-world identity leaked into second world.");
            Main.ActiveWorldFileData.UniqueId = FirstWorld;
            Assert(fixture.Observer.GetSortingChestCandidates().Count == 1 && fixture.Observer.GetSortingChestCandidates().Single().Instance is null,
                "Returning should restore a saved candidate, not a stale object reference.");
        }, results);

        Check("Post-sort memory refresh accepts only the already observed instance", () =>
        {
            var fixture = new Fixture();
            fixture.Open(7, fixture.Chest);
            fixture.Observer.PostUpdate();
            fixture.Close();
            Chest replacement = NewChest(40, 50, "Replacement", 777);
            fixture.Observer.RefreshSortedChest(replacement);
            Assert(fixture.Observer.CreateSnapshot().OwnedChests.Single().Items.Single().Stack == 12, "Replacement chest was silently trusted.");
            fixture.Chest.item[0].stack = 15;
            fixture.Observer.RefreshSortedChest(fixture.Chest);
            Assert(fixture.Observer.CreateSnapshot().OwnedChests.Single().Items.Single().Stack == 15, "Known sorted chest was not refreshed.");
        }, results);

        Check("Persisted chest identity survives a world save and rejects replaced storage", () =>
        {
            var fixture = new Fixture(); fixture.Open(7, fixture.Chest); fixture.Observer.PostUpdate();
            TagCompound playerSave = Save(fixture.Observer), worldSave = new();
            var identitySystem = new KnownChestIdentitySystem(); identitySystem.SaveWorldData(worldSave);
            fixture.Close(); fixture.Observer.LoadData(playerSave); identitySystem.ClearWorld(); identitySystem.LoadWorldData(worldSave);
            var candidate = fixture.Observer.GetSortingChestCandidates().Single();
            Assert(fixture.Observer.BindSortingChest(candidate, fixture.Chest, 21, 0, 0), "World reload lost chest identity.");
            fixture.Observer.LoadData(playerSave);
            KnownChestIdentitySystem.Remove(40, 50); KnownChestIdentitySystem.Ensure(40, 50);
            Assert(!fixture.Observer.BindSortingChest(fixture.Observer.GetSortingChestCandidates().Single(), NewChest(40, 50, "Workshop", 0), 21, 0, 0),
                "A replacement chest with the same name acquired the old identity.");
        }, results);
        Check("Style changes, moved coordinates and same-session replacement cannot reuse a memory", () =>
        {
            var fixture = new Fixture(); fixture.Open(7, fixture.Chest); fixture.Observer.PostUpdate(); fixture.Close();
            var candidate = fixture.Observer.GetSortingChestCandidates().Single();
            Assert(!fixture.Observer.BindSortingChest(candidate, fixture.Chest, 21, 36, 0), "Changed chest style was accepted.");
            Assert(!fixture.Observer.BindSortingChest(candidate, NewChest(41, 50, "Workshop", 0), 21, 0, 0), "Moved chest was accepted.");
            Assert(!fixture.Observer.BindSortingChest(candidate, NewChest(40, 50, "Workshop", 0), 21, 0, 0), "Replaced same-session instance was accepted.");
        }, results);
        Check("Local placement remembers a chest without reading initial contents", () =>
        {
            var fixture = new Fixture(); Main.LocalPlayer = fixture.Observer.Player;
            Main.chest.Set(7, fixture.Chest); Main.chest.AllowedIndex = 7; Chest.Locations[(40,50)] = 7;
            new KnownChestPlacement().PlaceInWorld(40, 50, 21, new Item { createTile = 21 });
            var memory = Main.LocalPlayer.GetModPlayer<GptKnowledgePlayer>();
            Assert(memory.GetSortingChestCandidates().Count == 1 && memory.CreateSnapshot().OwnedChests.Single().Items.Count == 0,
                "Placement did not remember the chest or inspected initial contents.");
            bool fail = true, effectOnly = false, noItem = false;
            new KnownChestPlacement().KillTile(40, 50, 21, ref fail, ref effectOnly, ref noItem);
            Assert(memory.GetSortingChestCandidates().Count == 1, "A failed mining attempt forgot the chest.");
            fail = false; new KnownChestPlacement().KillTile(40, 50, 21, ref fail, ref effectOnly, ref noItem);
            new KnownChestIdentitySystem().PostUpdateWorld();
            Assert(memory.GetSortingChestCandidates().Count == 1, "A later mining veto forgot an intact chest.");
            new KnownChestPlacement().KillTile(40, 50, 21, ref fail, ref effectOnly, ref noItem);
            Main.tile.Intact = false; new KnownChestIdentitySystem().PostUpdateWorld();
            Assert(memory.GetSortingChestCandidates().Count == 0, "Removed storage remained remembered.");
        }, results);

        return results.ToArray();
    }

    private sealed class Fixture
    {
        public GptKnowledgePlayer Observer { get; }
        public Chest Chest { get; } = NewChest(40, 50, "Workshop", 12);

        public Fixture()
        {
            Main.dedServ = false;
            Main.gameMenu = false;
            Main.myPlayer = 0;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.maxChests = 1000;
            Main.chest = new ChestReadSpy();
            Main.tile = new TileMap();
            Main.mouseItem = new Item();
            Main.ActiveWorldFileData = new WorldFileData { UniqueId = FirstWorld };
            new KnownChestIdentitySystem().ClearWorld(); Chest.Locations.Clear();
            Observer = new GptKnowledgePlayer { Player = new Player { whoAmI = 0 } };
            Observer.Initialize();
        }

        public void Open(int index, Chest chest)
        {
            Main.chest.Set(index, chest);
            Main.chest.AllowedIndex = index;
            Observer.Player.chest = index;
        }

        public void Close()
        {
            Observer.Player.chest = -1;
            Main.chest.AllowedIndex = null;
        }
    }

    private static Chest NewChest(int x, int y, string label, int count) => new()
    {
        x = x, y = y, name = label,
        item = new[] { new Item { type = 1, stack = count, Name = "Iron Bar" } }
    };

    private static TagCompound Save(GptKnowledgePlayer observer)
    {
        var tag = new TagCompound();
        observer.SaveData(tag);
        return tag;
    }

    private static IList<TagCompound> SavedChests(GptKnowledgePlayer observer) =>
        Save(observer).GetList<TagCompound>("worlds")
            .Single(tag => tag.GetString("world") == Main.ActiveWorldFileData.UniqueId.ToString("N"))
            .GetList<TagCompound>("chests");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Check(string name, Action test, List<string> results)
    {
        try
        {
            test();
            results.Add("PASS " + name);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(name + ": " + exception.Message, exception);
        }
    }
}
