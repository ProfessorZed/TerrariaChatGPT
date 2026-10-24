#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;
using TerrariaChatGPT.Common.Goals;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Players;

namespace TerrariaChatGPT.Tests;

public static class GoalProgressTests
{
    private static readonly Guid WorldA = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid WorldB = new("22222222-2222-4222-8222-222222222222");
    public static string[] Run()
    {
        var results = new List<string>();
        void Check(string name, Action action) { action(); results.Add(name); }

        Check("Unopened chest entries are never indexed by progress snapshots or goals", () =>
        {
            var f = new Fixture();
            Main.chest.Set(90, Chest("Secret", Stack(4, 99)));
            Assert(f.Progress.CreateSnapshot().RememberedItems.Count == 0, "Unopened storage was exposed.");
            f.Set(ItemGoal()); f.Tick();
            Assert(Main.chest.Reads.Count == 0 && !f.Progress.ActiveGoals[0].IsComplete, "Hidden supplies changed progress.");
        });
        Check("Detached remembered supplies survive closing without reading a closed chest", () =>
        {
            var f = new Fixture(); var chest = Chest("Materials", Stack(1, 3)); f.Open(chest);
            f.Knowledge.CreateSnapshot(); f.Close(); chest.item[0].stack = 900;
            f.Tick(); var snap = f.Progress.CreateSnapshot();
            Assert(snap.RememberedItems.Single().Stack == 3 && !snap.RememberedItems.Single().IsCurrentlyOpen, "Closed contents were reread.");
            Assert(Main.chest.Reads.Count == 0, "Closed chest was indexed.");
        });
        Check("Unnamed chest coordinates are stripped from progress DTO", () =>
        {
            var f = new Fixture(); f.Open(Chest("", Stack(1, 3))); f.Tick();
            Assert(f.Progress.CreateSnapshot().RememberedItems.Single().ChestLabel == "Previously opened chest", "Coordinates leaked through default label.");
            Assert(!typeof(ProgressSnapshot).GetProperties().Any(p => p.Name.Contains("World") && p.Name != "WorldProgress"), "World identity field exported.");
        });
        Check("Empty known chests retain their labels without reading closed contents", () =>
        {
            var f = new Fixture(); var chest = Chest("Vanity & Paints"); f.Open(chest); f.Tick(); f.Close();
            f.Tick(); var snapshot = f.Progress.CreateSnapshot();
            Assert(snapshot.KnownChests.Single().Name == "Vanity & Paints", "Empty known chest label lost.");
            Assert(snapshot.RememberedItems.Count == 0 && Main.chest.Reads.Count == 0, "Closed contents were inspected.");
        });
        Check("Inventory, equipment and cursor observations avoid mirror duplication", () =>
        {
            var f = new Fixture(); f.Player.inventory = Enumerable.Range(0, 59).Select(_ => new Item()).ToArray();
            f.Player.inventory[0] = Stack(4, 1); f.Player.inventory[58] = Stack(4, 10);
            Main.mouseItem = Stack(4, 10); f.Player.armor = new[] { Stack(4, 2) }; f.Tick();
            Assert(f.Progress.CreateSnapshot().Inventory.Sum(item => item.Stack) == 13, "Cursor mirror was counted twice.");
        });
        Check("World defeat flags are scoped and preserve the shared evil-boss record", () =>
        {
            var f = new Fixture(); NPC.downedBoss2 = true; f.Tick();
            var snap = f.Progress.CreateSnapshot();
            Assert(snap.Bosses.Single(boss => boss.Key == "evil_boss").Completed, "Shared flag missing.");
            Assert(!snap.Bosses.Any(boss => boss.Key.Contains("eater") || boss.Key.Contains("brain")), "Shared flag invented individual defeats.");
            Assert(snap.Bosses.All(boss => boss.Scope == "world"), "Boss scope missing.");
        });
        Check("Pre-Hardmode checklist preserves suggested order and groups ambiguous evil bosses", () =>
        {
            var f = new Fixture(); NPC.downedBoss1 = true; f.Tick(); var note = f.Set(GoalCatalog.PreHardmodeBosses());
            Assert(note.Steps.Count == 7 && note.Steps.First().Key == "king_slime" && note.Steps.Last().Key == "wall_of_flesh", "Boss order incorrect.");
            Assert(note.Steps[1].IsComplete && !note.Steps[2].IsComplete, "Wrong boss marked defeated.");
        });
        Check("Individual Eater and Brain goals remain explicitly manual with shared flags", () =>
        {
            var f = new Fixture(); NPC.downedBoss2 = true; f.Tick();
            var note = f.Set(new GoalProposal("Both evils", new[] { new GoalStepDefinition("boss", "Eater of Worlds", "Eater of Worlds"), new GoalStepDefinition("boss", "Brain of Cthulhu", "Brain of Cthulhu") }));
            Assert(note.Steps.All(step => step.IsManual && !step.IsComplete), "Shared flag falsely completed both bosses.");
        });
        Check("Account achievements never complete world-boss goals", () =>
        {
            var f = new Fixture(); Main.Achievements.Values.Add(new Achievement { Name = "EYE_ON_YOU", FriendlyName = new LocalizedText { Value = "Eye on You" }, IsCompleted = true }); f.Tick();
            var note = f.Set(new GoalProposal("Eye", new[] { new GoalStepDefinition("boss", "eye_of_cthulhu", "Eye") }));
            Assert(!note.IsComplete && f.Progress.CreateSnapshot().Achievements.Single().Scope == "account", "Account achievement was treated as world defeat.");
        });
        Check("Achievement goals are visibly account scoped and track local manager completion", () =>
        {
            var f = new Fixture(); var achievement = new Achievement { Name = "TEST", FriendlyName = new LocalizedText { Value = "Test Achievement" } }; Main.Achievements.Values.Add(achievement); f.Tick();
            var note = f.Set(new GoalProposal("Achievement", new[] { new GoalStepDefinition("achievement", "TEST", "Test") }));
            Assert(note.Steps.Single().Label.EndsWith("(account)") && !note.IsComplete, "Account label missing.");
            achievement.IsCompleted = true; f.Tick(); Assert(note.IsComplete, "Achievement did not refresh.");
        });
        Check("Only personally visited biomes are recorded, without map inspection", () =>
        {
            var f = new Fixture(); f.Player.ZoneJungle = true; f.Tick(); f.Player.ZoneJungle = false; f.Tick();
            var note = f.Set(new GoalProposal("Travel", new[] { new GoalStepDefinition("biome", "Jungle", "Jungle"), new GoalStepDefinition("biome", "Dungeon", "Dungeon") }));
            Assert(note.Steps[0].IsComplete && !note.Steps[1].IsComplete, "Visit history was lost or invented.");
        });
        Check("NPC history is sourced from the active conversation only", () =>
        {
            var f = new Fixture(); f.Player.TalkNPC = new NPC { type = 22, TypeName = "Guide" }; f.Tick(); f.Player.TalkNPC = null; f.Tick();
            Assert(f.Progress.CreateSnapshot().KnownNpcs.Single(npc => npc.Key == "Terraria/Guide").TalkedTo, "Actual conversation was not remembered.");
            Assert(!f.Progress.CreateSnapshot().KnownNpcs.Any(npc => npc.Key.Contains("Unseen")), "Unseen NPC found.");
        });
        Check("NPC rescue flags provide public status without a location", () =>
        {
            var f = new Fixture(); var note = f.Set(new GoalProposal("Rescue", new[] { new GoalStepDefinition("npc", "Goblin Tinkerer", "Goblin") }));
            Assert(!note.IsComplete, "Unrescued NPC marked done."); NPC.savedGoblin = true; f.Tick();
            Assert(note.IsComplete && note.Steps.Single().Detail.Contains("location is unknown"), "Rescue status not scoped.");
        });
        Check("One-tick NPC conversations capture exact computed happiness without waiting a second", () =>
        {
            var f = new Fixture(); f.Player.ZoneSnow = true;
            f.Talk(22, "Guide", 0.8899999856948853, "I like the Snow. I love having space.");
            f.Progress.PostUpdate(); var value = f.Progress.CreateSnapshot().NpcHappiness.Single();
            Assert(value.Key == "Terraria/Guide" && value.PriceAdjustment == 0.8899999856948853 && value.IsCurrent,
                "Exact game-computed observation was not captured immediately.");
            Assert(value.HappinessReport.Contains("having space") && value.ObservedBiomes.SequenceEqual(new[] { "Snow" }), "Visible reasons/biome missing.");
            Assert(Main.chest.Reads.Count == 0, "NPC observation touched storage.");
        });
        Check("Chat submission captures an active conversation before the next progress tick", () =>
        {
            var f = new Fixture(); f.Talk(22, "Guide", 0.95, "Space is lovely.");
            var snapshot = f.Progress.CreateSnapshot();
            Assert(snapshot.NpcHappiness.Single().IsCurrent, "Submission missed the active dialogue.");
            Assert(snapshot.CapturedAtUtc >= snapshot.NpcHappiness.Single().LastObservedUtc, "Snapshot timestamp predates its new happiness observation.");
        });
        Check("Unchanged cached shop settings preserve their original observation time", () =>
        {
            var f = new Fixture(); f.Talk(22, "Guide", 0.95, "Space is lovely.");
            var before = f.Progress.CreateSnapshot().NpcHappiness.Single(); f.Tick();
            var after = f.Progress.CreateSnapshot().NpcHappiness.Single();
            Assert(before.LastObservedUtc == after.LastObservedUtc && after.IsCurrent, "Cached result pretended to be newly computed.");
        });
        Check("Switching conversations associates each synchronous shopping result with the correct NPC", () =>
        {
            var f = new Fixture(); f.Talk(22, "Guide", 0.95, "Guide's report.");
            var old = f.Progress.CreateSnapshot();
            f.Talk(107, "Goblin Tinkerer", 1.25, "Goblin's report.");
            var values = f.Progress.CreateSnapshot().NpcHappiness;
            Assert(values.Single(value => value.Key == "Terraria/Guide") is { IsCurrent: false, PriceAdjustment: 0.95 }, "Old NPC stayed current.");
            Assert(values.Single(value => value.Key == "Terraria/Npc107") is { IsCurrent: true, PriceAdjustment: 1.25, HappinessReport: "Goblin's report." }, "New NPC inherited old settings.");
            Assert(old.NpcHappiness.Single().IsCurrent && old.NpcHappiness.Count == 1, "Detached request snapshot was mutated.");
        });
        Check("Closing a conversation immediately marks its last result remembered without reading the old NPC", () =>
        {
            var f = new Fixture(); f.Talk(22, "Guide", 0.9, "I like it here."); var npc = f.Player.TalkNPC!;
            f.Progress.CreateSnapshot(); f.EndTalk(); npc.TypeName = "Changed while off-screen";
            var value = f.Progress.CreateSnapshot().NpcHappiness.Single();
            Assert(!value.IsCurrent && value.Name == "Guide" && value.PriceAdjustment == 0.9, "Closed dialogue was polled or kept live.");
        });
        Check("Switch to unavailable default settings never assigns the previous NPC's report", () =>
        {
            var f = new Fixture(); f.Talk(22, "Guide", 0.9, "Guide report."); f.Progress.CreateSnapshot();
            f.Talk(107, "Goblin", 1, ""); var values = f.Progress.CreateSnapshot().NpcHappiness;
            Assert(values.Count == 1 && !values[0].IsCurrent && values[0].Name == "Guide", "Unavailable report created false neutral happiness.");
        });
        Check("Changes to a current game-computed result replace evidence without changing game settings", () =>
        {
            var f = new Fixture(); f.Talk(22, "Guide", 1.2, "Crowded."); f.Progress.CreateSnapshot();
            f.Player.currentShoppingSettings = new ShoppingSettings { PriceAdjustment = 0.85, HappinessReport = "I have space now." };
            f.Progress.PostUpdate(); var value = f.Progress.CreateSnapshot().NpcHappiness.Single();
            Assert(value.PriceAdjustment == 0.85 && value.HappinessReport == "I have space now." && f.Player.currentShoppingSettings.PriceAdjustment == 0.85,
                "New evidence was missed or game settings changed.");
        });
        Check("Unsupported NPCs, pets, inactive NPCs and disabled happiness produce no happiness claims", () =>
        {
            var f = new Fixture(); f.Talk(22, "Guide", 1, "Synthetic report.");
            f.Player.TalkNPC!.townNPC = false; Assert(f.Progress.CreateSnapshot().NpcHappiness.Count == 0, "Non-town NPC included.");
            f.Player.TalkNPC.townNPC = true; NPCID.Sets.NoTownNPCHappiness[22] = true;
            Assert(f.Progress.CreateSnapshot().NpcHappiness.Count == 0, "Explicit opt-out ignored.");
            NPCID.Sets.NoTownNPCHappiness[22] = false; NPCID.Sets.IsTownPet[22] = true;
            Assert(f.Progress.CreateSnapshot().NpcHappiness.Count == 0, "Pet happiness invented.");
            NPCID.Sets.IsTownPet[22] = false; f.Player.TalkNPC.active = false;
            Assert(f.Progress.CreateSnapshot().NpcHappiness.Count == 0, "Inactive NPC read.");
            f.Player.TalkNPC.active = true; Main.remixWorld = true;
            Assert(f.Progress.CreateSnapshot().NpcHappiness.Count == 0, "Disabled world happiness reported.");
        });
        Check("Invalid numeric results and empty reports are rejected while valid modded modifiers are preserved", () =>
        {
            var f = new Fixture();
            foreach (double price in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 0, 0.001, 101 })
            { f.Talk(22, "Guide", price, "Report."); Assert(f.Progress.CreateSnapshot().NpcHappiness.Count == 0, "Invalid price accepted."); }
            f.Talk(22, "Guide", 1, "\r\n [ ] "); Assert(f.Progress.CreateSnapshot().NpcHappiness.Count == 0, "Empty sanitized report accepted.");
            f.Talk(22, "Guide", 2, "Modded mood."); Assert(f.Progress.CreateSnapshot().NpcHappiness.Single().PriceAdjustment == 2, "Modded price clamped to vanilla range.");
        });
        Check("Happiness text is bounded and sanitized and snapshots expose no NPC references or coordinates", () =>
        {
            var f = new Fixture(); f.Talk(22, "[Guide]\n" + new string('n', 200), 0.95, "[report]\0" + new string('r', 4000));
            var value = f.Progress.CreateSnapshot().NpcHappiness.Single();
            Assert(value.Name.Length == 100 && value.HappinessReport.Length == 2000 && !value.HappinessReport.Any(char.IsControl)
                && !value.Name.Contains('[') && !value.HappinessReport.Contains(']'), "Unsafe/unbounded text retained.");
            Assert(!typeof(NpcHappinessObservation).GetProperties().Any(property => property.PropertyType == typeof(NPC)
                || property.Name.Contains("Position") || property.Name.Contains("Home") || property.Name.Contains("Coordinate")), "World identity/location exported.");
        });
        Check("Happiness survives saving and reloading as remembered evidence, with its exact timestamp", () =>
        {
            var f = new Fixture(); f.Talk(22, "Guide", 0.91, "I like Forest.");
            var before = f.Progress.CreateSnapshot().NpcHappiness.Single(); var saved = new TagCompound(); f.Progress.SaveData(saved);
            f.EndTalk(); f.Progress.LoadData(saved); f.Progress.OnEnterWorld(); var after = f.Progress.CreateSnapshot().NpcHappiness.Single();
            Assert(!after.IsCurrent && after.LastObservedUtc == before.LastObservedUtc && after.PriceAdjustment == before.PriceAdjustment
                && after.HappinessReport == before.HappinessReport, "Remembered happiness did not round-trip.");
        });
        Check("Happiness memory stays partitioned by world and character", () =>
        {
            var f = new Fixture(); f.Talk(22, "Guide", 0.95, "Report A."); f.Progress.CreateSnapshot(); f.EndTalk();
            Main.ActiveWorldFileData.UniqueId = WorldB; f.Progress.OnEnterWorld();
            Assert(f.Progress.CreateSnapshot().NpcHappiness.Count == 0, "World A happiness leaked to B.");
            Main.ActiveWorldFileData.UniqueId = WorldA; f.Progress.OnEnterWorld();
            Assert(f.Progress.CreateSnapshot().NpcHappiness.Single() is { IsCurrent: false, HappinessReport: "Report A." }, "Original world's memory lost.");
            Assert(new Player { whoAmI = 0 }.GetModPlayer<GptProgressPlayer>().CreateSnapshot().NpcHappiness.Count == 0, "Another character inherited happiness.");
        });
        Check("Happiness capture is disabled for remote players, menu and server and cannot retain a false current marker", () =>
        {
            var f = new Fixture(); f.Talk(22, "Guide", 0.95, "Report."); f.Progress.CreateSnapshot();
            Main.gameMenu = true; f.EndTalk(); f.Progress.PostUpdate(); Main.gameMenu = false;
            Assert(!f.Progress.CreateSnapshot().NpcHappiness.Single().IsCurrent, "Menu transition retained current status.");
            f.Talk(107, "Goblin", 1.2, "Never observe."); f.Player.whoAmI = 1; f.Progress.PostUpdate();
            f.Player.whoAmI = 0; Main.dedServ = true; f.Progress.PostUpdate(); Main.dedServ = false; f.EndTalk();
            Assert(f.Progress.CreateSnapshot().NpcHappiness.Count == 1, "Nonlocal/server happiness captured.");
        });
        Check("Happiness record count is bounded and retains the newest observations", () =>
        {
            var ledger = new WorldGoalLedger(); DateTimeOffset now = DateTimeOffset.UtcNow;
            for (int i = 0; i < WorldGoalLedger.MaximumHappinessRecords + 3; i++)
                Assert(ledger.RememberHappiness("Test/Npc" + i, "NPC " + i, 1, "Report.", now.AddSeconds(i - 200), Array.Empty<string>()), "Valid record rejected.");
            Assert(ledger.NpcHappiness.Count == WorldGoalLedger.MaximumHappinessRecords && !ledger.NpcHappiness.ContainsKey("Test/Npc0")
                && ledger.NpcHappiness.ContainsKey("Test/Npc130"), "Memory was unbounded or evicted new evidence.");
        });
        Check("Malformed happiness entries are skipped without losing goals or valid observations", () =>
        {
            var f = new Fixture(); f.Set(ManualGoal("Keep this goal")); f.Talk(22, "Guide", 0.95, "Valid report.");
            var saved = new TagCompound(); f.Progress.SaveData(saved); var world = saved.GetList<TagCompound>("worlds")[0];
            var values = world.GetList<TagCompound>("happiness");
            values.Add(new TagCompound { ["key"] = "Test/Invalid", ["name"] = "Invalid", ["price"] = "not a number", ["report"] = "Bad.", ["observed"] = DateTimeOffset.UtcNow.ToString("O") });
            values.Add(new TagCompound { ["key"] = "Test/Future", ["name"] = "Future", ["price"] = 1.0, ["report"] = "Bad.", ["observed"] = DateTimeOffset.UtcNow.AddDays(1).ToString("O") });
            f.EndTalk(); f.Progress.LoadData(saved); f.Progress.OnEnterWorld();
            Assert(f.Progress.ActiveGoals.Single().Title == "Keep this goal" && f.Progress.CreateSnapshot().NpcHappiness.Single().Name == "Guide", "Corrupt entry discarded unrelated data.");
        });
        Check("Malformed happiness containers cannot discard the world's existing goals", () =>
        {
            foreach (object invalid in new object[] { "not a list", new List<string> { "wrong elements" }, 7 })
            {
                var f = new Fixture(); f.Set(ManualGoal("Keep this goal")); var saved = new TagCompound(); f.Progress.SaveData(saved);
                saved.GetList<TagCompound>("worlds")[0]["happiness"] = invalid;
                f.Progress.LoadData(saved); f.Progress.OnEnterWorld();
                Assert(f.Progress.ActiveGoals.Single().Title == "Keep this goal" && f.Progress.CreateSnapshot().NpcHappiness.Count == 0,
                    "Malformed happiness container discarded the world ledger.");
            }
        });
        Check("Unsupported goals become honest manual steps and cannot perform game actions", () =>
        {
            var f = new Fixture(); var note = f.Set(new GoalProposal("Build arena", new[] { new GoalStepDefinition("teleport", "hidden", "Build an arena") }));
            Assert(note.Steps.Single().IsManual && !note.IsComplete, "Unsupported automation accepted.");
            f.Progress.ToggleManualStep(note.Id, 0); Assert(note.IsComplete && !NPC.downedBoss1 && !Main.hardMode, "Manual checkbox changed game state.");
        });
        Check("Automatic checkboxes cannot be toggled as if they were manual", () =>
        {
            var f = new Fixture(); var note = f.Set(ItemGoal()); f.Progress.ToggleManualStep(note.Id, 0);
            Assert(!note.IsComplete, "Automatic completion was falsified by click.");
        });
        Check("Craft goals expand actual installed quantities and stations", () =>
        {
            var f = new Fixture(); var note = f.Set(CraftGoal());
            Assert(note.Steps.Count == 3 && note.Steps[0].Target == 3 && note.Steps[1].Target == 2, "Installed ingredient amounts lost.");
            Assert(note.Steps.Last().Detail.Contains("Test Anvil"), "Installed station lost.");
        });
        Check("Craft quantities round to complete recipe batches", () =>
        {
            var f = new Fixture(); GameReferenceFacts.Recipes[0] = GameReferenceFacts.Recipes[0] with { ResultStack = 2 };
            var note = f.Set(new GoalProposal("Three swords", new[] { new GoalStepDefinition("craft", "Terraria/TestSword", "Sword", 3) }));
            Assert(note.Steps[0].Target == 6 && note.Steps[1].Target == 4, "Batches were not rounded up.");
        });
        Check("Stale remembered ingredients show separately and cannot complete the recipe", () =>
        {
            var f = new Fixture(); f.Open(Chest("Materials", Stack(1, 3), Stack(5, 2))); f.Knowledge.CreateSnapshot(); f.Close(); f.Tick();
            var note = f.Set(CraftGoal());
            Assert(note.Steps[0].Current == 0 && note.Steps[0].Remembered == 3 && !note.Steps[0].IsComplete, "Stale material called live.");
            Assert(!note.IsComplete && note.Steps.Last().Current == 0, "Materials alone marked crafting complete.");
        });
        Check("Open chest ingredients are live; closing restores remembered uncertainty", () =>
        {
            var f = new Fixture(); f.Open(Chest("Materials", Stack(1, 3), Stack(5, 2))); f.Tick(); var note = f.Set(CraftGoal());
            Assert(note.Steps[0].Current == 3 && note.Steps[0].Remembered == 0 && note.Steps[0].IsComplete, "Open chest missing.");
            f.Close(); f.Tick(); Assert(note.Steps[0].Current == 0 && note.Steps[0].Remembered == 3 && !note.Steps[0].IsComplete, "Closed count stayed live.");
        });
        Check("Equipment and cursor can satisfy acquisition but are not craft-ready materials", () =>
        {
            var f = new Fixture(); f.Player.armor = new[] { Stack(1, 3) }; Main.mouseItem = Stack(5, 2); f.Tick(); var note = f.Set(CraftGoal());
            Assert(note.Steps[0].Current == 0 && note.Steps[1].Current == 0, "Equipped/cursor ingredient counted craft-ready.");
            Main.mouseItem = Stack(4, 1); f.Tick(); Assert(note.IsComplete, "Acquired cursor result not tracked.");
        });
        Check("Acquiring the target completes ingredients even after they are consumed", () =>
        {
            var f = new Fixture(); f.Player.inventory = new[] { Stack(1, 3), Stack(5, 2) }; f.Tick(); var note = f.Set(CraftGoal());
            Assert(!note.IsComplete, "Having materials marked result acquired.");
            f.Player.inventory = new[] { Stack(4, 1) }; f.Tick(); Assert(note.IsComplete, "Crafted result did not complete goal.");
            f.Player.inventory = Array.Empty<Item>(); f.Tick(); Assert(note.IsComplete, "Acquisition milestone disappeared when using result.");
        });
        Check("Remembered result alone never marks acquisition done", () =>
        {
            var f = new Fixture(); f.Open(Chest("Old chest", Stack(4, 1))); f.Knowledge.CreateSnapshot(); f.Close(); f.Tick(); var note = f.Set(ItemGoal());
            Assert(!note.IsComplete && note.Steps.Single().Remembered == 1, "Unconfirmed remembered result completed goal.");
        });
        Check("Recipe groups allocate shared material once across overlapping requirements", () =>
        {
            var f = new Fixture(); GameReferenceFacts.Recipes[0] = Recipe(new RecipeIngredientSnapshot("Any bar", 3, new[] { "Terraria/IronBar", "Terraria/CopperBar" }), new RecipeIngredientSnapshot("Iron Bar", 2, new[] { "Terraria/IronBar" }));
            f.Player.inventory = new[] { Stack(1, 3) }; f.Tick(); var note = f.Set(CraftGoal());
            Assert(note.Steps.Where(step => step.Kind == "ingredient").Sum(step => step.Current) == 3, "Same bars allocated twice.");
        });
        Check("Alternative recipes stay separate and prefer the currently most achievable one", () =>
        {
            var f = new Fixture(); GameReferenceFacts.Recipes.Add(Recipe(new RecipeIngredientSnapshot("Diamond", 1, new[] { "Terraria/Diamond" })));
            f.Player.inventory = new[] { Stack(3, 1) }; f.Tick(); var note = f.Set(CraftGoal());
            Assert(note.Steps.Count == 2 && note.Steps[0].Label == "Diamond", "Recipe alternatives were mixed.");
        });
        Check("Goal acceptance is idempotent even when the suggestion title changes", () =>
        {
            var f = new Fixture(); var first = f.Set(ItemGoal()); var again = f.Set(ItemGoal() with { Title = "Different title" });
            Assert(first.Id == again.Id && f.Progress.ActiveGoals.Count == 1, "Duplicate goal created.");
        });
        Check("Six independent notes are supported and the seventh has a clear limit", () =>
        {
            var f = new Fixture(); for (int i = 0; i < 6; i++) f.Set(ManualGoal("Step " + i));
            Assert(!f.Progress.TrySetGoal(ManualGoal("Seventh"), out _, out string message) && message.Contains("6 goals"), "Maximum note bound missing.");
        });
        Check("Closing a goal removes only that note", () =>
        {
            var f = new Fixture(); var first = f.Set(ItemGoal()); var second = f.Set(CraftGoal());
            Assert(f.Progress.RemoveGoal(first.Id) && f.Progress.ActiveGoals.Single().Id == second.Id, "Removal touched other goal.");
        });
        Check("Goals, checkbox milestones, drag position and collapse survive save/reload", () =>
        {
            var f = new Fixture(); var note = f.Set(ManualGoal("Arena")); f.Progress.ToggleManualStep(note.Id, 0); f.Progress.MoveGoal(note.Id, 0.4f, 0.7f); f.Progress.SetCollapsed(note.Id, true);
            var saved = new TagCompound(); f.Progress.SaveData(saved); f.Progress.LoadData(saved); f.Progress.OnEnterWorld(); var restored = f.Progress.ActiveGoals.Single();
            Assert(restored.Id == note.Id && restored.IsComplete && restored.Collapsed && restored.X == 0.4f && restored.Y == 0.7f, "Persistent note fields lost.");
        });
        Check("Acquisition milestones persist but live supply counts are recalculated", () =>
        {
            var f = new Fixture(); f.Player.inventory = new[] { Stack(4, 1) }; f.Tick(); f.Set(CraftGoal()); var saved = new TagCompound(); f.Progress.SaveData(saved);
            f.Player.inventory = Array.Empty<Item>(); f.Progress.LoadData(saved); f.Progress.OnEnterWorld(); Assert(f.Progress.ActiveGoals.Single().IsComplete, "Acquired state was not saved.");
        });
        Check("Goals are partitioned by world and do not appear for another character", () =>
        {
            var f = new Fixture(); var first = f.Set(ItemGoal()); Main.ActiveWorldFileData.UniqueId = WorldB; f.Progress.OnEnterWorld();
            Assert(f.Progress.ActiveGoals.Count == 0, "World A goal leaked into World B."); f.Set(ManualGoal("B"));
            Main.ActiveWorldFileData.UniqueId = WorldA; f.Progress.OnEnterWorld(); Assert(f.Progress.ActiveGoals.Single().Id == first.Id, "World A goal lost.");
            var another = new Player { whoAmI = 0 }; Assert(another.GetModPlayer<GptProgressPlayer>().ActiveGoals.Count == 0, "Other character inherited goal.");
        });
        Check("Progress is cached for at most one second and repeated chat reads do not repoll", () =>
        {
            var f = new Fixture(); var snapshot = f.Progress.CreateSnapshot(); int reads = Main.Achievements.Reads;
            for (int i = 0; i < 59; i++) { f.Progress.PostUpdate(); Assert(ReferenceEquals(snapshot, f.Progress.CreateSnapshot()), "Repoll before60ticks."); }
            Assert(Main.Achievements.Reads == reads, "Repeated snapshots scanned achievements."); f.Progress.PostUpdate();
            Assert(!ReferenceEquals(snapshot, f.Progress.CreateSnapshot()) && Main.Achievements.Reads == reads + 1, "60-tick refresh failed.");
        });
        Check("Nonlocal players, menus and dedicated servers expose no progress", () =>
        {
            var f = new Fixture(); f.Player.whoAmI = 1; Assert(f.Progress.ActiveGoals.Count == 0 && !f.Progress.TrySetGoal(ItemGoal(), out _, out _), "Remote player captured.");
            f.Player.whoAmI = 0; Main.gameMenu = true; Assert(!f.Progress.TrySetGoal(ItemGoal(), out _, out _), "Menu accepted goal.");
            Main.gameMenu = false; Main.dedServ = true; Assert(!f.Progress.TrySetGoal(ItemGoal(), out _, out _), "Server accepted local goal.");
        });
        Check("Multiplayer never imports single-player chest memory", () =>
        {
            var f = new Fixture(); f.Open(Chest("Private", Stack(1, 3))); f.Knowledge.CreateSnapshot(); f.Close(); Main.netMode = NetmodeID.MultiplayerClient; f.Tick();
            Assert(f.Progress.CreateSnapshot().RememberedItems.Count == 0 && Main.chest.Reads.Count == 0, "Single-player storage leaked in multiplayer.");
        });
        Check("Malformed proposal bounds and invalid quantities are rejected", () =>
        {
            var f = new Fixture(); Assert(!f.Progress.TrySetGoal(ItemGoal() with { Steps = new[] { new GoalStepDefinition("item", "Terraria/TestSword", "Sword", -1) } }, out _, out _), "Negative target accepted.");
            Assert(!f.Progress.TrySetGoal(new GoalProposal("", ItemGoal().Steps), out _, out _), "Blank title accepted.");
            Assert(!f.Progress.TrySetGoal(new GoalProposal("Too many", Enumerable.Repeat(ItemGoal().Steps[0], 25).ToArray()), out _, out _), "Too many proposed steps accepted.");
        });
        Check("Position validation clamps bounds and preserves the unplaced sentinel", () =>
        {
            var f = new Fixture(); var note = f.Set(ItemGoal()); Assert(note.X == -1 && note.Y == -1, "Default note placement not deferred.");
            f.Progress.MoveGoal(note.Id, 10, float.NaN); Assert(note.X == 1 && note.Y == -1, "Invalid drag coordinates persisted.");
        });
        Check("Large craft requests retain exact batch quantities and reject overflowing ingredient totals", () =>
        {
            var f = new Fixture();
            var note = f.Set(new GoalProposal("Many swords", new[] { new GoalStepDefinition("craft", "Terraria/TestSword", "Sword", 100000) }));
            Assert(note.Steps[0].Target == 300000 && note.Steps[1].Target == 200000, "Large request silently became one batch.");
            Assert(!f.Progress.TrySetGoal(new GoalProposal("Too many swords", new[] { new GoalStepDefinition("craft", "Terraria/TestSword", "Sword", 999999) }), out _, out _), "Overflowing recipe accepted.");
        });
        Check("UI reads reuse read-only collection wrappers", () =>
        {
            var f = new Fixture(); var note = f.Set(ItemGoal());
            Assert(ReferenceEquals(note.Steps, note.Steps) && ReferenceEquals(f.Progress.ActiveGoals, f.Progress.ActiveGoals), "Per-frame wrapper allocation returned.");
        });
        Check("Ambiguous public item display names cannot silently target the wrong modded item", () =>
        {
            var f = new Fixture(); ContentSamples.ItemsByType[90] = new Item { type = 90, stack = 1, Name = "Test Sword", ModItem = new Terraria.ModLoader.ModItem { FullName = "OtherMod/Sword" } };
            var note = f.Set(new GoalProposal("Sword", new[] { new GoalStepDefinition("item", "Test Sword", "Test Sword") }));
            Assert(note.Steps.Single().IsManual, "Ambiguous name silently resolved to first item.");
        });
        Check("Canonical item keys still resolve when a different mod duplicates the display name", () =>
        {
            var f = new Fixture(); ContentSamples.ItemsByType[90] = new Item { type = 90, stack = 1, Name = "Test Sword", ModItem = new Terraria.ModLoader.ModItem { FullName = "OtherMod/Sword" } };
            var note = f.Set(ItemGoal()); Assert(!note.Steps.Single().IsManual && note.Steps.Single().Key == "Terraria/TestSword", "Canonical identity lost.");
        });
        Check("Changed installed recipes invalidate old ingredient automation after reload", () =>
        {
            var f = new Fixture(); f.Set(CraftGoal()); var saved = new TagCompound(); f.Progress.SaveData(saved);
            GameReferenceFacts.Recipes[0] = Recipe(new RecipeIngredientSnapshot("Diamond", 4, new[] { "Terraria/Diamond" }));
            f.Progress.LoadData(saved); f.Progress.OnEnterWorld(); var note = f.Progress.ActiveGoals.Single();
            Assert(note.Steps.Take(2).All(step => step.IsManual && step.Detail.Contains("recipe changed")), "Stale installed recipe continued to give false counts.");
        });
        Check("Corrupt saved world data cannot prevent normal player load", () =>
        {
            var f = new Fixture(); var tag = new TagCompound { ["worlds"] = new List<TagCompound> { new() { ["world"] = WorldA.ToString("N"), ["notes"] = "invalid" } } };
            f.Progress.LoadData(tag); Assert(f.Progress.ActiveGoals.Count == 0, "Malformed save unexpectedly created notes.");
        });
        return results.ToArray();
    }

    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static Item Stack(int type, int stack) => new() { type = type, stack = stack, Name = type switch { 1 => "Iron Bar", 2 => "Torch", 3 => "Diamond", 4 => "Test Sword", 5 => "Copper Bar", _ => "Unknown" } };
    private static Chest Chest(string name, params Item[] items) => new() { x = 123, y = 456, name = name, item = items };
    private static GoalProposal ItemGoal() => new("Get sword", new[] { new GoalStepDefinition("item", "Terraria/TestSword", "Test Sword") });
    private static GoalProposal CraftGoal() => new("Craft sword", new[] { new GoalStepDefinition("craft", "Terraria/TestSword", "Test Sword") });
    private static GoalProposal ManualGoal(string label) => new(label, new[] { new GoalStepDefinition("manual", label, label) });
    private static RecipeReferenceSnapshot Recipe(params RecipeIngredientSnapshot[] ingredients) => new("Terraria/TestSword", "Test Sword", 1, ingredients, new[] { "Test Anvil" }, Array.Empty<string>());

    private sealed class Fixture
    {
        public Player Player { get; }
        public GptProgressPlayer Progress => Player.GetModPlayer<GptProgressPlayer>();
        public GptKnowledgePlayer Knowledge => Player.GetModPlayer<GptKnowledgePlayer>();
        public Fixture()
        {
            Main.dedServ = Main.gameMenu = Main.hardMode = Main.remixWorld = false; Main.myPlayer = 0; Main.netMode = NetmodeID.SinglePlayer;
            NPCID.Sets.NoTownNPCHappiness = new bool[1000]; NPCID.Sets.IsTownPet = new bool[1000];
            Main.chest = new ChestReadSpy(); Main.mouseItem = new Item(); Main.ActiveWorldFileData = new WorldFileData { UniqueId = WorldA };
            Main.Achievements = new AchievementManager();
            foreach (var field in typeof(NPC).GetFields().Where(field => field.IsStatic && field.FieldType == typeof(bool))) field.SetValue(null, false);
            ContentSamples.ItemsByType = Enumerable.Range(1, 5).ToDictionary(type => type, type => Stack(type, 1));
            GameReferenceFacts.Recipes = new List<RecipeReferenceSnapshot> { Recipe(new RecipeIngredientSnapshot("Iron Bar", 3, new[] { "Terraria/IronBar" }), new RecipeIngredientSnapshot("Copper Bar", 2, new[] { "Terraria/CopperBar" })) };
            Player = new Player { whoAmI = 0 }; Knowledge.OnEnterWorld(); Progress.OnEnterWorld();
        }
        public GoalNote Set(GoalProposal proposal) { Assert(Progress.TrySetGoal(proposal, out GoalNote? note, out string error), error); return note!; }
        public void Tick() { for (int i = 0; i < 60; i++) { Knowledge.PostUpdate(); Progress.PostUpdate(); } }
        public void Open(Chest chest) { Main.chest.Set(7, chest); Main.chest.AllowedIndex = 7; Player.chest = 7; }
        public void Close() { Player.chest = -1; Main.chest.AllowedIndex = null; Main.chest.Reads.Clear(); }
        public void Talk(int type, string name, double price, string report)
        {
            // Mirrors SetTalkNPC's synchronous association, without running the game's
            // resident-inspecting happiness calculation in this isolated test host.
            Player.TalkNPC = new NPC { type = type, TypeName = name };
            Player.currentShoppingSettings = new ShoppingSettings { PriceAdjustment = price, HappinessReport = report };
        }
        public void EndTalk()
        {
            Player.TalkNPC = null;
            Player.currentShoppingSettings = new ShoppingSettings { PriceAdjustment = 1, HappinessReport = "" };
        }
    }
}
