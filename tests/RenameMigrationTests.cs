#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.Default;
using Terraria.ModLoader.IO;
using TerrariaChatGPT.Common.Goals;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Players;

namespace TerrariaChatGPT.Tests;

public static class RenameMigrationTests
{
    private static readonly Guid WorldA = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid WorldB = new("22222222-2222-4222-8222-222222222222");

    public static string[] Run()
    {
        var results = new List<string>();
        void Check(string name, Action test) { test(); results.Add(name); }

        Check("Legacy goals, placement, manual completion and opened chest memory survive the rename", () =>
        {
            var legacy = SavedLegacy();
            var player = FreshPlayer(legacy);
            Enter(player);
            var note = player.GetModPlayer<GptProgressPlayer>().ActiveGoals.Single();
            Assert(note.Title == "Build an arena" && note.X == .3f && note.Y == .4f && note.Collapsed && note.Steps.Single().IsComplete,
                "Goal details were lost.");
            var memory = player.GetModPlayer<GptKnowledgePlayer>().CreateSnapshot();
            Assert(memory.OwnedChests.Single().Items.Single().Stack == 12 && memory.KnownSubjects.Any(item => item.Name == "Jungle"),
                "The recorded chest or visited biome was not restored.");
            Assert(Main.chest.Reads.Count == 0, "Migration indexed a world chest.");
        });
        Check("Progress may enter first without losing knowledge migration or NPC history", () =>
        {
            var player = FreshPlayer(SavedLegacy());
            player.GetModPlayer<GptProgressPlayer>().OnEnterWorld();
            player.GetModPlayer<GptKnowledgePlayer>().OnEnterWorld();
            var progress = player.GetModPlayer<GptProgressPlayer>().CreateSnapshot();
            Assert(progress.RememberedItems.Single().Stack == 12 && progress.KnownNpcs.Any(npc => npc.Key == "Terraria/Guide" && npc.TalkedTo),
                "Hook ordering lost observations.");
        });
        Check("Migration preserves original and unrelated unloaded entries for rollback", () =>
        {
            var legacy = SavedLegacy();
            var unrelated = new TagCompound { ["mod"] = "OtherMod", ["name"] = "OtherPlayer", ["data"] = "Private other-mod data" };
            legacy.Add(unrelated);
            var player = FreshPlayer(legacy); Enter(player);
            var unloaded = player.GetModPlayer<UnloadedPlayer>();
            Assert(unloaded.Entries.Count == 3 && ReferenceEquals(unloaded.Entries[0], legacy[0]) && ReferenceEquals(unloaded.Entries[2], unrelated),
                "Migration changed unloaded records.");
            Assert(unrelated.GetString("data") == "Private other-mod data", "Another mod's contents changed.");
        });
        Check("New-format data wins even if an old copy contains more goals and chests", () =>
        {
            var player = FreshPlayer(SavedLegacy());
            player.GetModPlayer<GptProgressPlayer>().LoadData(Empty(1));
            player.GetModPlayer<GptKnowledgePlayer>().LoadData(Empty(2));
            Enter(player);
            Assert(player.GetModPlayer<GptProgressPlayer>().ActiveGoals.Count == 0 && player.GetModPlayer<GptKnowledgePlayer>().CreateSnapshot().OwnedChests.Count == 0,
                "Legacy data overwrote new saves.");
            Assert(player.GetModPlayer<UnloadedPlayer>().SaveCalls == 0, "Already migrated data was inspected again.");
        });
        Check("Deleting a migrated goal and forgetting its chest survives save and reload", () =>
        {
            var legacy = SavedLegacy();
            var player = FreshPlayer(legacy); Enter(player);
            var progress = player.GetModPlayer<GptProgressPlayer>();
            progress.RemoveGoal(progress.ActiveGoals.Single().Id);
            var knowledge = player.GetModPlayer<GptKnowledgePlayer>();
            Main.chest.Set(7, Chest()); Main.chest.AllowedIndex = 7; player.chest = 7;
            knowledge.ForgetOpenChest(); player.chest = -1; Main.chest.AllowedIndex = null;
            var progressSave = Save(progress); var knowledgeSave = Save(knowledge);
            var reloaded = FreshPlayer(legacy);
            reloaded.GetModPlayer<GptProgressPlayer>().LoadData(progressSave);
            reloaded.GetModPlayer<GptKnowledgePlayer>().LoadData(knowledgeSave);
            Enter(reloaded);
            Assert(reloaded.GetModPlayer<GptProgressPlayer>().ActiveGoals.Count == 0 && reloaded.GetModPlayer<GptKnowledgePlayer>().CreateSnapshot().OwnedChests.Count == 0,
                "Removed content reappeared from the untouched old copy.");
        });
        Check("Saving a character before world entry still carries forward its legacy data", () =>
        {
            var player = FreshPlayer(SavedLegacy());
            var progressSave = Save(player.GetModPlayer<GptProgressPlayer>());
            var knowledgeSave = Save(player.GetModPlayer<GptKnowledgePlayer>());
            Assert(progressSave.GetList<TagCompound>("worlds").Single().GetList<TagCompound>("notes").Count == 1 &&
                knowledgeSave.GetList<TagCompound>("worlds").Single().GetList<TagCompound>("chests").Count == 1,
                "A pre-entry save wrote empty state over the migration opportunity.");
        });
        Check("World partitions remain separate across migration and world changes", () =>
        {
            var player = FreshPlayer(SavedLegacy()); Enter(player);
            Main.ActiveWorldFileData.UniqueId = WorldB; Enter(player);
            Assert(player.GetModPlayer<GptProgressPlayer>().ActiveGoals.Count == 0 && player.GetModPlayer<GptKnowledgePlayer>().CreateSnapshot().OwnedChests.Count == 0,
                "Another world's records were mixed in.");
            Main.ActiveWorldFileData.UniqueId = WorldA; Enter(player);
            Assert(player.GetModPlayer<GptProgressPlayer>().ActiveGoals.Count == 1, "Original world goal disappeared.");
            Assert(player.GetModPlayer<UnloadedPlayer>().SaveCalls == 2, "World changes retriggered migration.");
        });
        Check("Repeated snapshot and save calls scan unloaded data only once per component", () =>
        {
            var player = FreshPlayer(SavedLegacy()); Enter(player);
            for (int i = 0; i < 30; i++)
            {
                player.GetModPlayer<GptKnowledgePlayer>().CreateSnapshot();
                player.GetModPlayer<GptProgressPlayer>().CreateSnapshot();
                Save(player.GetModPlayer<GptKnowledgePlayer>()); Save(player.GetModPlayer<GptProgressPlayer>());
            }
            Assert(player.GetModPlayer<UnloadedPlayer>().SaveCalls == 2, "Migration repeated on ordinary reads or saves.");
        });
        Check("Identically named classes belonging to another mod never migrate", () =>
        {
            var legacy = SavedLegacy(); foreach (var entry in legacy) entry["mod"] = "AnotherTerrariaGPT";
            var player = FreshPlayer(legacy); Enter(player);
            Assert(player.GetModPlayer<GptProgressPlayer>().ActiveGoals.Count == 0 && player.GetModPlayer<GptKnowledgePlayer>().CreateSnapshot().OwnedChests.Count == 0,
                "Other mod data was claimed.");
        });
        Check("Legacy mod data under unrelated player class names is ignored", () =>
        {
            var legacy = SavedLegacy(); foreach (var entry in legacy) entry["name"] = "OtherPlayer";
            Assert(LegacyPlayerData.Find(legacy, nameof(GptProgressPlayer)) is null && LegacyPlayerData.Find(legacy, nameof(GptKnowledgePlayer)) is null,
                "Unknown classes were imported.");
        });
        Check("Unsupported versions and invalid world shapes do not migrate", () =>
        {
            var legacy = SavedLegacy();
            ((TagCompound)legacy[0]["data"])["version"] = 99;
            ((TagCompound)legacy[1]["data"]).GetList<TagCompound>("worlds")[0].Remove("chests");
            var player = FreshPlayer(legacy); Enter(player);
            Assert(player.GetModPlayer<GptProgressPlayer>().ActiveGoals.Count == 0 && player.GetModPlayer<GptKnowledgePlayer>().CreateSnapshot().OwnedChests.Count == 0,
                "An unknown schema migrated.");
        });
        Check("Malformed unrelated entries cannot prevent later valid legacy imports", () =>
        {
            var legacy = SavedLegacy(); legacy.Insert(0, new TagCompound { ["mod"] = 7, ["name"] = "invalid" });
            var player = FreshPlayer(legacy); Enter(player);
            Assert(player.GetModPlayer<GptProgressPlayer>().ActiveGoals.Count == 1, "Unrelated corruption prevented migration.");
        });
        Check("Ambiguous duplicate legacy records are left intact without selecting one", () =>
        {
            var legacy = SavedLegacy(); legacy.Add(legacy[0]);
            Assert(LegacyPlayerData.Find(legacy, nameof(GptProgressPlayer)) is null && legacy.Count == 3, "A duplicate was silently preferred or removed.");
        });
        Check("The older version-one chest schema remains supported", () =>
        {
            var legacy = SavedLegacy(); ((TagCompound)legacy[1]["data"])["version"] = 1;
            var player = FreshPlayer(legacy); Enter(player);
            Assert(player.GetModPlayer<GptKnowledgePlayer>().CreateSnapshot().OwnedChests.Count == 1, "Manual chest memory from version one was lost.");
        });
        Check("Imported single-player chest memory remains hidden in multiplayer", () =>
        {
            var player = FreshPlayer(SavedLegacy()); Main.netMode = NetmodeID.MultiplayerClient; Enter(player);
            Assert(player.GetModPlayer<GptKnowledgePlayer>().CreateSnapshot().OwnedChests.Count == 0 &&
                player.GetModPlayer<GptProgressPlayer>().CreateSnapshot().RememberedItems.Count == 0, "Migration bypassed the multiplayer boundary.");
        });
        return results.ToArray();
    }

    private static List<TagCompound> SavedLegacy()
    {
        var player = FreshPlayer(); Enter(player);
        var knowledge = player.GetModPlayer<GptKnowledgePlayer>();
        player.ZoneJungle = true; Main.chest.Set(7, Chest()); Main.chest.AllowedIndex = 7; player.chest = 7;
        knowledge.CreateSnapshot(); player.chest = -1; Main.chest.AllowedIndex = null;
        player.TalkNPC = new NPC { type = 22, TypeName = "Guide" };
        var progress = player.GetModPlayer<GptProgressPlayer>(); progress.OnEnterWorld();
        Assert(progress.TrySetGoal(new GoalProposal("Build an arena", new[] { new GoalStepDefinition("manual", "arena", "Place platforms") }), out var note, out _), "Fixture goal failed.");
        progress.MoveGoal(note!.Id, .3f, .4f); progress.SetCollapsed(note.Id, true); progress.ToggleManualStep(note.Id, 0);
        return new List<TagCompound>
        {
            new() { ["mod"] = "TerrariaGPT", ["name"] = nameof(GptProgressPlayer), ["data"] = Save(progress) },
            new() { ["mod"] = "TerrariaGPT", ["name"] = nameof(GptKnowledgePlayer), ["data"] = Save(knowledge) }
        };
    }

    private static Player FreshPlayer(IEnumerable<TagCompound>? legacy = null)
    {
        Main.dedServ = Main.gameMenu = Main.hardMode = false; Main.myPlayer = 0; Main.netMode = NetmodeID.SinglePlayer;
        Main.chest = new ChestReadSpy(); Main.mouseItem = new Item(); Main.ActiveWorldFileData = new WorldFileData { UniqueId = WorldA };
        Main.Achievements = new AchievementManager(); ContentSamples.ItemsByType = new(); GameReferenceFacts.Recipes = new();
        var player = new Player { whoAmI = 0 };
        if (legacy is not null) foreach (var entry in legacy) player.GetModPlayer<UnloadedPlayer>().Entries.Add(entry);
        return player;
    }

    private static void Enter(Player player) { player.GetModPlayer<GptKnowledgePlayer>().OnEnterWorld(); player.GetModPlayer<GptProgressPlayer>().OnEnterWorld(); }
    private static Chest Chest() => new() { x = 123, y = 456, name = "Materials", item = new[] { new Item { type = 1, stack = 12, Name = "Iron Bar" } } };
    private static TagCompound Empty(int version) => new() { ["version"] = version, ["worlds"] = new List<TagCompound>() };
    private static TagCompound Save(Terraria.ModLoader.ModPlayer player) { var tag = new TagCompound(); player.SaveData(tag); return tag; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
