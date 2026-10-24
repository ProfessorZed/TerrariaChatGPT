using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaChatGPT.Common.Players;
using TerrariaChatGPT.Common.Sorting;

namespace TerrariaChatGPT.Tests;

public static class ChestSortRuntimeTests
{
    /// <summary>Replays a captured real model reply using production validation,
    /// planning and transfer code in this isolated fake world.</summary>
    public static string[] ReplayCaptured(SortSuggestion captured)
    {
        List<string> results = [];
        Check("Captured live GPT six-item choices reach reviewed destinations exactly", () =>
        {
            string[] names = ["Amethyst Hook", "Magic Mirror", "Money Trough", "Shadow Key", "Blue Jellyfish Kite", "Defender Medal"];
            string[] keys = Enumerable.Range(20, 6).Select(type => "Terraria/" + ItemID.Search.GetName(type)).ToArray();
            Equal(6, captured.Assignments.Count);
            True(keys.OrderBy(key => key).SequenceEqual(captured.Assignments.Select(row => row.ItemKey).OrderBy(key => key)));
            for (int index = 0; index < names.Length; index++)
            {
                Item item = Put(index + 10, index + 20, index + 1);
                item.Name = names[index]; item.TestCategory = ChestSortCategory.None;
            }
            Item hotbar = Put(0, 20, 3), favorite = Put(16, 21, 1); favorite.favorited = true;
            Item unassigned = Put(17, 1, 25);
            Chest travel = AddChest(0, "Tools & Travel"), consumables = AddChest(1, "Consumables", 13, 10);
            AddChest(2, "Blocks", 16, 10);
            SortSuggestion proposal = SortSuggestionValidator.Validate(captured, keys, ["Tools & Travel", "Consumables", "Blocks"])!;
            Equal(6, proposal.Assignments.Count);
            ChestSortPreview preview = ChestSortService.BeginPreview(proposal)!;
            Equal(6, preview.Plan.MovedStackCount); Equal(21, preview.Plan.MovedItemCount);
            foreach (SortAssignment assignment in proposal.Assignments)
            {
                ChestSortMove move = preview.Plan.Moves.Single(row => row.ItemKey == assignment.ItemKey);
                Equal(assignment.ChestName, move.ChestName);
            }
            // Merely receiving and previewing the captured reply moves nothing.
            True(travel.item.All(item => item.IsAir)); True(consumables.item.All(item => item.IsAir));
            True(Main.LocalPlayer.inventory.Skip(10).Take(6).All(item => !item.IsAir));
            Equal(ChestSortUnsortedReason.NotAssigned, preview.Plan.Unsorted.Single().Code);
            True(ChestSortService.RememberChoices(proposal, out _) is not null);
            Equal(6, Main.LocalPlayer.Preferences.Rules.Count);
            True(travel.item.All(item => item.IsAir)); True(consumables.item.All(item => item.IsAir));
            // Saving invalidates the previous preview; refresh is required.
            True(!ChestSortService.Confirm().Success);
            ChestSortService.BeginPreview(proposal); True(ChestSortService.Confirm().Success);
            Equal(15, travel.item.Sum(item => item.stack)); Equal(6, consumables.item.Sum(item => item.stack));
            True(Main.LocalPlayer.inventory.Skip(10).Take(6).All(item => item.IsAir));
            True(ReferenceEquals(hotbar, Main.LocalPlayer.inventory[0])); True(ReferenceEquals(favorite, Main.LocalPlayer.inventory[16]));
            True(ReferenceEquals(unassigned, Main.LocalPlayer.inventory[17]));
        }, results);
        return results.ToArray();
    }

    public static string[] Run()
    {
        List<string> results = [];
        Check("Preview observes only and does not change any live item or reference", () =>
        {
            var source = Put(10, 1, 25); var chest = AddChest(0, "Blocks"); var empty = chest.item[0];
            var preview = ChestSortService.BeginPreview();
            True(preview is not null && preview.Plan.MovedItemCount == 25);
            True(ReferenceEquals(source, Main.LocalPlayer.inventory[10])); Equal(25, source.stack);
            True(ReferenceEquals(empty, chest.item[0])); Equal(0, Item.CloneCalls);
        }, results);
        Check("Confirmation transfers exact quantities and updates recipe and chest observations", () =>
        {
            Put(10, 1, 25); var chest = AddChest(0, "Blocks");
            ChestSortService.BeginPreview(); var result = ChestSortService.Confirm();
            True(result.Success); Equal(25, result.MovedItemCount); True(Main.LocalPlayer.inventory[10].IsAir); Equal(25, chest.item[0].stack);
            Equal(1, Main.LocalPlayer.Memory.Refreshed.Count); Equal(1, Recipe.Refreshes); True(ChestSortService.CurrentPreview is null);
        }, results);
        Check("Confirming without preview never changes inventory", () =>
        {
            var source = Put(10, 1, 25); AddChest(0, "Blocks");
            True(!ChestSortService.Confirm().Success); Equal(25, source.stack);
        }, results);
        Check("Cancelled previews cannot be applied", () =>
        {
            var source = Put(10, 1, 25); AddChest(0, "Blocks");
            ChestSortService.BeginPreview(); ChestSortService.Cancel(); True(!ChestSortService.Confirm().Success); Equal(25, source.stack);
        }, results);
        Check("Confirmation is one-shot and double clicks cannot replay transfers", () =>
        {
            Put(10, 1, 25); var chest = AddChest(0, "Blocks"); ChestSortService.BeginPreview();
            True(ChestSortService.Confirm().Success); True(!ChestSortService.Confirm().Success); Equal(25, chest.item[0].stack);
        }, results);
        Check("Hotbar favorites coin ammo slots and equipment keep original references", () =>
        {
            var hotbar = Put(0, 1, 25); var favorite = Put(11, 1, 25); favorite.favorited = true;
            var coinSlot = Put(50, 1, 25); var ammoSlot = Put(54, 1, 25); var bagCoin = Put(12, 4, 25); bagCoin.IsACoin = true;
            var equipped = NewItem(3, 1); Main.LocalPlayer.armor[0] = equipped;
            Put(10, 1, 25); AddChest(0, "Blocks"); ChestSortService.BeginPreview(); True(ChestSortService.Confirm().Success);
            True(ReferenceEquals(hotbar, Main.LocalPlayer.inventory[0])); True(ReferenceEquals(favorite, Main.LocalPlayer.inventory[11]));
            True(ReferenceEquals(coinSlot, Main.LocalPlayer.inventory[50])); True(ReferenceEquals(ammoSlot, Main.LocalPlayer.inventory[54]));
            True(ReferenceEquals(bagCoin, Main.LocalPlayer.inventory[12])); True(ReferenceEquals(equipped, Main.LocalPlayer.armor[0]));
        }, results);
        Check("Unmatched items remain in inventory and are flagged with a chest recommendation", () =>
        {
            var source = Put(10, 1, 25); AddChest(0, "Weapons"); var preview = ChestSortService.BeginPreview();
            Equal("Building Blocks", preview!.Plan.Unsorted.Single().SuggestedChest); True(!ChestSortService.Confirm().Success);
            True(ReferenceEquals(source, Main.LocalPlayer.inventory[10])); Equal(25, source.stack);
        }, results);
        Check("Partial transfers conserve remainder and never exceed max stack", () =>
        {
            Put(10, 1, 25); var chest = AddChest(0, "Blocks");
            for (int i = 0; i < 40; i++) chest.item[i] = NewItem(2, 9999);
            chest.item[3] = NewItem(1, 9990); ChestSortService.BeginPreview(); True(ChestSortService.Confirm().Success);
            Equal(16, Main.LocalPlayer.inventory[10].stack); Equal(9999, chest.item[3].stack);
        }, results);
        Check("Custom ModItems preserve the entire original item and attached data", () =>
        {
            var source = Put(10, 1100, 25); source.ModItem = new(); source.TestMetadata = new object();
            var metadata = source.TestMetadata; var chest = AddChest(0, "Blocks"); ChestSortService.BeginPreview(); True(ChestSortService.Confirm().Success);
            True(ReferenceEquals(source, chest.item[0])); True(ReferenceEquals(metadata, chest.item[0].TestMetadata)); Equal(0, Item.CloneCalls);
        }, results);
        Check("Vanilla items with global mod data move whole without clone hooks", () =>
        {
            var source = Put(10, 1, 25); source.Globals.Add(new()); var chest = AddChest(0, "Blocks");
            chest.item[0] = NewItem(1, 500); ChestSortService.BeginPreview(); True(ChestSortService.Confirm().Success);
            True(ReferenceEquals(source, chest.item[1])); Equal(500, chest.item[0].stack); Equal(0, Item.CloneCalls);
        }, results);
        Check("Opaque existing chest items block only their occupied slot", () =>
        {
            Put(10, 1, 25); var chest = AddChest(0, "Blocks"); var custom = NewItem(1, 500); custom.Globals.Add(new()); chest.item[0] = custom;
            ChestSortService.BeginPreview(); True(ChestSortService.Confirm().Success); True(ReferenceEquals(custom, chest.item[0])); Equal(25, chest.item[1].stack);
        }, results);
        foreach (var field in MutableFields()) Check("Different plain item " + field.Name + " prevents merging", () =>
        {
            var source = Put(10, 1, 25); field.Change(source); var chest = AddChest(0, "Blocks"); chest.item[0] = NewItem(1, 500);
            ChestSortService.BeginPreview(); True(ChestSortService.Confirm().Success);
            Equal(500, chest.item[0].stack); Equal(25, chest.item[1].stack);
        }, results);
        foreach (var field in MutableFields()) Check("Changed plain item " + field.Name + " invalidates preview", () =>
        {
            var source = Put(10, 1, 25); var chest = AddChest(0, "Blocks"); ChestSortService.BeginPreview(); field.Change(source);
            True(!ChestSortService.Confirm().Success); Equal(25, source.stack); True(chest.item.All(item => item.IsAir));
        }, results);
        Check("Global items cannot partially merge when no empty destination exists", () =>
        {
            var source = Put(10, 1, 25); source.Globals.Add(new()); var chest = AddChest(0, "Blocks");
            for (int i = 0; i < 40; i++) chest.item[i] = NewItem(1, 1);
            var preview = ChestSortService.BeginPreview(); Equal(0, preview!.Plan.Moves.Count); Equal(25, preview.Plan.Unsorted.Single().Remaining);
            True(!ChestSortService.Confirm().Success); True(ReferenceEquals(source, Main.LocalPlayer.inventory[10]));
        }, results);
        Check("A clone failure during staging leaves all live sources and destinations untouched", () =>
        {
            var source = Put(10, 1, 25); var other = Put(11, 2, 30); var chest = AddChest(0, "Blocks"); var originalSlots = chest.item.ToArray();
            ChestSortService.BeginPreview(); Item.FailCloneAt = 3; True(!ChestSortService.Confirm().Success);
            True(ReferenceEquals(source, Main.LocalPlayer.inventory[10])); True(ReferenceEquals(other, Main.LocalPlayer.inventory[11]));
            True(chest.item.SequenceEqual(originalSlots)); Equal(25, source.stack); Equal(30, other.stack);
        }, results);
        Check("Cache refresh failure cannot report an already committed transfer as failed", () =>
        {
            Put(10, 1, 25); var chest = AddChest(0, "Blocks"); Main.LocalPlayer.Memory.ThrowOnRefresh = true;
            ChestSortService.BeginPreview(); True(ChestSortService.Confirm().Success); Equal(25, chest.item[0].stack); True(!ChestSortService.Confirm().Success);
        }, results);
        Check("Recipe refresh failure cannot undo or replay committed transfers", () =>
        {
            Put(10, 1, 25); var chest = AddChest(0, "Blocks"); Recipe.ThrowOnRefresh = true;
            ChestSortService.BeginPreview(); True(ChestSortService.Confirm().Success); Equal(25, chest.item[0].stack);
        }, results);
        Check("Unopened chests are never resolved or read", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks"); AddChest(1, "Blocks", 13, 10, remembered: false); Main.chest.Forbidden.Add(1);
            var preview = ChestSortService.BeginPreview(); True(preview is not null); Equal(1, preview!.EligibleChestCount);
            True(!Main.chest.Reads.Contains(1)); True(!Chest.FindCalls.Contains((13, 10))); True(ChestSortService.Confirm().Success); True(!Main.chest.Reads.Contains(1));
        }, results);
        Check("Out of range remembered chests are not resolved or read", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks", 100, 100); Main.chest.Forbidden.Add(0);
            var preview = ChestSortService.BeginPreview(); Equal(0, preview!.EligibleChestCount); Equal(0, Main.chest.Reads.Count); Equal(0, Chest.FindCalls.Count); Equal(0, Main.tile.Reads.Count);
        }, results);
        Check("Quick Stack range uses exact radial strict-less-than 600 pixel boundary", () =>
        {
            True(ChestSortService.WithinQuickStackRange(new Vector2(615.99f, 16), 0, 0));
            True(!ChestSortService.WithinQuickStackRange(new Vector2(616, 16), 0, 0));
            True(!ChestSortService.WithinQuickStackRange(new Vector2(616.01f, 16), 0, 0));
            True(!ChestSortService.WithinQuickStackRange(new Vector2(466, 466), 0, 0));
        }, results);
        Check("Locked chests do not become eligible", () =>
        { Put(10, 1, 25); AddChest(0, "Blocks"); Chest.Locked.Add((10, 10)); Equal(0, ChestSortService.BeginPreview()!.EligibleChestCount); }, results);
        Check("Chests in use do not become eligible", () =>
        { Put(10, 1, 25); AddChest(0, "Blocks"); Chest.InUse.Add(0); Equal(0, ChestSortService.BeginPreview()!.EligibleChestCount); }, results);
        Check("Replaced chest instances cannot reuse an old remembered location", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks"); Main.chest[0] = new Chest { x = 10, y = 10, name = "Blocks" };
            Equal(0, ChestSortService.BeginPreview()!.EligibleChestCount);
        }, results);
        Check("Broken or nonstandard chest tiles are excluded before resolving storage", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks"); Main.tile[11, 11] = default;
            Equal(0, ChestSortService.BeginPreview()!.EligibleChestCount); Equal(0, Chest.FindCalls.Count);
        }, results);
        Check("Duplicate remembered chest entries fail safely", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks"); Main.LocalPlayer.Memory.Candidates.Add(Main.LocalPlayer.Memory.Candidates[0]);
            True(ChestSortService.BeginPreview() is null); Equal(25, Main.LocalPlayer.inventory[10].stack);
        }, results);
        Check("Aliased live item instances fail safely", () =>
        {
            var source = Put(10, 1, 25); Main.LocalPlayer.inventory[11] = source; AddChest(0, "Blocks");
            True(ChestSortService.BeginPreview() is null); Equal(25, source.stack);
        }, results);
        Check("A bag item aliased in the protected hotbar fails preview safely", () =>
        {
            var source = Put(10, 1, 25); Main.LocalPlayer.inventory[0] = source; AddChest(0, "Blocks");
            True(ChestSortService.BeginPreview() is null); Equal(25, source.stack);
        }, results);
        Check("A bag item aliased in equipped gear fails preview safely", () =>
        {
            var source = Put(10, 1, 25); Main.LocalPlayer.armor[0] = source; AddChest(0, "Blocks");
            True(ChestSortService.BeginPreview() is null); Equal(25, source.stack);
        }, results);
        Check("A bag item aliased in a personal bank fails preview safely", () =>
        {
            var source = Put(10, 1, 25); Main.LocalPlayer.bank.item[0] = source; AddChest(0, "Blocks");
            True(ChestSortService.BeginPreview() is null); Equal(25, source.stack);
        }, results);
        Check("Extreme custom stack totals cannot overflow a result after committing", () =>
        {
            var first = Put(10, 1, int.MaxValue); first.maxStack = int.MaxValue;
            var second = Put(11, 2, int.MaxValue); second.maxStack = int.MaxValue;
            AddChest(0, "Blocks"); True(ChestSortService.BeginPreview() is null);
            True(!ChestSortService.Confirm().Success); Equal(int.MaxValue, first.stack); Equal(int.MaxValue, second.stack);
        }, results);
        Check("Multiplayer preview is disabled without reading chests", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks"); Main.netMode = NetmodeID.MultiplayerClient;
            True(ChestSortService.BeginPreview() is null); Equal(0, Main.chest.Reads.Count);
        }, results);
        foreach (var scenario in new (string Name, Action Change)[]
        {
            ("main menu", () => Main.gameMenu = true), ("dedicated server", () => Main.dedServ = true),
            ("dead player", () => Main.LocalPlayer.dead = true), ("ghost", () => Main.LocalPlayer.ghost = true),
            ("inactive player", () => Main.LocalPlayer.active = false), ("open chest", () => Main.LocalPlayer.chest = 0),
            ("personal bank", () => Main.LocalPlayer.chest = -2), ("inventory lock", () => Main.LocalPlayer.LockedInventory = true),
            ("cursor item", () => Main.mouseItem = NewItem(1, 1))
        }) Check("Preview blocks " + scenario.Name, () => { Put(10, 1, 25); AddChest(0, "Blocks"); scenario.Change(); True(ChestSortService.BeginPreview() is null); }, results);

        foreach (var scenario in new (string Name, Action<Chest> Change)[]
        {
            ("source stack", _ => Main.LocalPlayer.inventory[10].stack++),
            ("source favorite", _ => Main.LocalPlayer.inventory[10].favorited = true),
            ("source prefix", _ => Main.LocalPlayer.inventory[10].prefix++),
            ("source object reference", _ => Main.LocalPlayer.inventory[10] = NewItem(1, 25)),
            ("source maximum stack", _ => Main.LocalPlayer.inventory[10].maxStack--),
            ("source global status", _ => Main.LocalPlayer.inventory[10].Globals.Add(new())),
            ("inventory array", _ => Main.LocalPlayer.inventory = Main.LocalPlayer.inventory.ToArray()),
            ("another bag slot", _ => Put(40, 2, 1)),
            ("new hotbar alias", _ => Main.LocalPlayer.inventory[0] = Main.LocalPlayer.inventory[10]),
            ("new equipped alias", _ => Main.LocalPlayer.armor[0] = Main.LocalPlayer.inventory[10]),
            ("new bank alias", _ => Main.LocalPlayer.bank.item[0] = Main.LocalPlayer.inventory[10]),
            ("new safe alias", _ => Main.LocalPlayer.bank2.item[0] = Main.LocalPlayer.inventory[10]),
            ("new forge alias", _ => Main.LocalPlayer.bank3.item[0] = Main.LocalPlayer.inventory[10]),
            ("new void vault alias", _ => Main.LocalPlayer.bank4.item[0] = Main.LocalPlayer.inventory[10]),
            ("destination stack", chest => chest.item[3] = NewItem(2, 1)),
            ("destination slot array", chest => chest.item = chest.item.ToArray()),
            ("chest name", chest => chest.name = "Materials"),
            ("chest reference", _ => Main.chest[0] = new Chest { x = 10, y = 10, name = "Blocks" }),
            ("chest tile", _ => Main.tile[10, 10] = default),
            ("chest frame", _ => { var tile = Main.tile[10, 10]; tile.TileFrameX += 36; Main.tile[10, 10] = tile; }),
            ("chest became locked", _ => Chest.Locked.Add((10, 10))),
            ("chest opened", _ => Chest.InUse.Add(0)),
            ("memory removed", _ => Main.LocalPlayer.Memory.Candidates.Clear()),
            ("session changed", _ => Main.LocalPlayer.Memory.SortingSessionId = Guid.NewGuid()),
            ("world changed", _ => Main.ActiveWorldFileData.UniqueId = Guid.NewGuid()),
            ("player moved out of range", _ => Main.LocalPlayer.Center = new Vector2(10000, 10000)),
            ("cursor occupied", _ => Main.mouseItem = NewItem(2, 1)),
            ("multiplayer entered", _ => Main.netMode = NetmodeID.MultiplayerClient)
        }) Check("Confirmation rejects stale " + scenario.Name, () =>
        {
            Put(10, 1, 25); var chest = AddChest(0, "Blocks"); ChestSortService.BeginPreview(); scenario.Change(chest);
            var sourceNow = Main.LocalPlayer.inventory[10]; int countNow = sourceNow.stack; Item[] slotsNow = chest.item.ToArray();
            True(!ChestSortService.Confirm().Success); Equal(countNow, sourceNow.stack); True(chest.item.SequenceEqual(slotsNow)); True(!ChestSortService.Confirm().Success);
        }, results);
        Check("Confirmation from another thread is rejected", () =>
        {
            var source = Put(10, 1, 25); AddChest(0, "Blocks"); ChestSortService.BeginPreview();
            True(!Task.Run(() => ChestSortService.Confirm()).GetAwaiter().GetResult().Success); Equal(25, source.stack);
        }, results);
        Check("Hotbar-only changes do not unnecessarily invalidate the bag preview", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks"); ChestSortService.BeginPreview(); Put(0, 2, 1); True(ChestSortService.Confirm().Success);
        }, results);
        Check("Saved category changes only the preview until explicit confirmation", () =>
        {
            var item = Put(10, 1, 25); var normal = AddChest(0, "Blocks"); var preferred = AddChest(1, "Materials", 13, 10);
            Main.LocalPlayer.Preferences.Rules["Terraria/Wood"] = new("material");
            var preview = ChestSortService.BeginPreview()!;
            Equal(1, preview.Plan.Moves.Single().ChestId); Equal(25, item.stack); True(preferred.item.All(slot => slot.IsAir));
            True(ChestSortService.Confirm().Success); Equal(25, preferred.item[0].stack); True(normal.item.All(slot => slot.IsAir));
        }, results);
        Check("Saved exact chest label uses only an eligible known destination", () =>
        {
            var item = Put(10, 1, 25); AddChest(0, "Blocks"); AddChest(1, "Crafting stash", 13, 10, remembered: false);
            Main.LocalPlayer.Preferences.Rules["Terraria/Wood"] = new("chest:Crafting stash");
            var preview = ChestSortService.BeginPreview()!;
            Equal(0, preview.Plan.Moves.Count); Equal("Crafting stash", preview.Plan.Unsorted.Single().SuggestedChest);
            True(!ChestSortService.Confirm().Success); Equal(25, item.stack);
        }, results);
        Check("Hook group uses Travel while an exact hook exception takes precedence", () =>
        {
            Reset();
            var first = Put(10, 1, 1); first.TestHook = true;
            var second = Put(11, 2, 1); second.TestHook = true;
            var raw = Put(12, 3, 1); raw.TestCategory = ChestSortCategory.Material;
            AddChest(0, "Travel & Tools"); AddChest(1, "Vanity & Paints", 13, 10); AddChest(2, "Materials", 16, 10);
            Main.LocalPlayer.Preferences.Rules["group:grappling_hooks"] = new("travel");
            Main.LocalPlayer.Preferences.Rules["Terraria/Stone"] = new("vanity");
            var preview = ChestSortService.BeginPreview()!;
            Equal(0, preview.Plan.Moves.Single(move => move.SourceSlot == 10).ChestId);
            Equal(1, preview.Plan.Moves.Single(move => move.SourceSlot == 11).ChestId);
            Equal(2, preview.Plan.Moves.Single(move => move.SourceSlot == 12).ChestId);
            True(ChestSortService.Confirm().Success);
        }, results);
        Check("Remembered chest restored without a session object can be previewed and confirmed", () =>
        {
            Reset(); Put(10, 1, 25); var chest = AddChest(0, "Blocks");
            Main.LocalPlayer.Memory.Candidates[0] = Main.LocalPlayer.Memory.Candidates[0] with { Instance = null };
            Equal(25, ChestSortService.BeginPreview()!.Plan.MovedItemCount);
            True(ChestSortService.Confirm().Success); Equal(25, chest.item[0].stack);
        }, results);
        Check("Broad family preference applies to all intrinsic potion items", () =>
        {
            var potion = Put(10, 1, 25); potion.TestCategory = ChestSortCategory.Potion;
            AddChest(0, "Potions"); AddChest(1, "Materials", 13, 10);
            Main.LocalPlayer.Preferences.Rules["group:potion"] = new("material");
            Equal(1, ChestSortService.BeginPreview()!.Plan.Moves.Single().ChestId);
        }, results);
        Check("Conflicting overlapping family rules cannot fall back to a default chest", () =>
        {
            var hybrid = Put(10, 1, 1); hybrid.TestCategory = ChestSortCategory.Armor | ChestSortCategory.Accessory;
            AddChest(0, "Equipment"); AddChest(1, "Travel", 13, 10); AddChest(2, "Vanity", 16, 10);
            Main.LocalPlayer.Preferences.Rules["group:armor"] = new("travel");
            Main.LocalPlayer.Preferences.Rules["group:accessory"] = new("vanity");
            var plan = ChestSortService.BeginPreview()!.Plan;
            Equal(0, plan.Moves.Count); Equal(ChestSortUnsortedReason.AmbiguousCategory, plan.Unsorted.Single().Code);
        }, results);
        Check("Disabling memory restores ordinary sorting categories", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks"); AddChest(1, "Materials", 13, 10);
            Main.LocalPlayer.Preferences.Rules["Terraria/Wood"] = new("material"); Main.LocalPlayer.Preferences.Enabled = false;
            Equal(0, ChestSortService.BeginPreview()!.Plan.Moves.Single().ChestId);
        }, results);
        Check("Changing saved memory invalidates a previously reviewed sort", () =>
        {
            var item = Put(10, 1, 25); var chest = AddChest(0, "Blocks"); ChestSortService.BeginPreview();
            Main.LocalPlayer.Preferences.Revision++;
            True(!ChestSortService.Confirm().Success); Equal(25, item.stack); True(chest.item.All(slot => slot.IsAir));
        }, results);
        Check("GPT choices route all six reported loose items including subjective currency", () =>
        {
            string[] names = ["Amethyst Hook", "Magic Mirror", "Money Trough", "Shadow Key", "Blue Jellyfish Kite", "Defender Medal"];
            for (int i = 0; i < names.Length; i++) { var item = Put(10 + i, 20 + i, i + 1); item.Name = names[i]; item.TestCategory = ChestSortCategory.None; }
            AddChest(0, "Tools & Travel"); AddChest(1, "Consumables", 13, 10);
            SortSuggestion suggestion = new(Enumerable.Range(0, 6).Select(i => new SortAssignment("Terraria/" + ItemID.Search.GetName(20 + i), i == 5 ? "Consumables" : "Tools & Travel")).ToArray());
            var plan = ChestSortService.BeginPreview(suggestion)!.Plan;
            Equal(6, plan.MovedStackCount); Equal(21, plan.MovedItemCount);
            Equal(5, plan.Moves.Count(move => move.ChestName == "Tools & Travel"));
            True(ChestSortService.Confirm().Success);
            True(Main.LocalPlayer.inventory.Skip(10).Take(6).All(item => item.IsAir));
        }, results);
        Check("A GPT plan overrides categories only for its assigned keys", () =>
        {
            Put(10, 1, 25); var untouched = Put(11, 2, 12);
            AddChest(0, "Blocks"); AddChest(1, "Vanity & Paints", 13, 10);
            var preview = ChestSortService.BeginPreview(new([new("Terraria/Wood", "Vanity & Paints")]))!;
            Equal(1, preview.Plan.Moves.Single().ChestId);
            Equal(ChestSortUnsortedReason.NotAssigned, preview.Plan.Unsorted.Single().Code);
            True(ChestSortService.Confirm().Success); True(ReferenceEquals(untouched, Main.LocalPlayer.inventory[11]));
        }, results);
        Check("GPT assignment respects hotbar favorites coins and equipment protection", () =>
        {
            var hotbar = Put(0, 1, 7); var favorite = Put(11, 1, 6); favorite.favorited = true;
            Put(10, 1, 25); var coin = Put(12, 4, 2); coin.IsACoin = true; AddChest(0, "Blocks");
            var preview = ChestSortService.BeginPreview(new([new("Terraria/Wood", "Blocks")]))!;
            Equal(25, preview.Plan.MovedItemCount); True(ChestSortService.Confirm().Success);
            True(ReferenceEquals(hotbar, Main.LocalPlayer.inventory[0])); True(ReferenceEquals(favorite, Main.LocalPlayer.inventory[11]));
            True(ReferenceEquals(coin, Main.LocalPlayer.inventory[12]));
        }, results);
        Check("Assignments to now absent or protected-only keys fail without fallback", () =>
        {
            Put(0, 1, 25); Put(10, 2, 6); AddChest(0, "Blocks");
            True(ChestSortService.BeginPreview(new([new("Terraria/Wood", "Blocks")])) is null);
            True(ChestSortService.Status.Contains("no longer match")); True(!ChestSortService.Confirm().Success);
        }, results);
        Check("Unknown and conflicting GPT chest assignments cannot authorize a partial runtime plan", () =>
        {
            Put(10, 1, 25); Put(11, 2, 12); AddChest(0, "Blocks"); AddChest(1, "Materials", 13, 10);
            foreach (SortSuggestion suggestion in new SortSuggestion[]
            {
                new([new("Terraria/Wood", "Unseen"), new("Terraria/Stone", "Blocks")]),
                new([new("Terraria/Wood", "Blocks"), new("Terraria/Wood", "Materials")])
            }) True(ChestSortService.BeginPreview(suggestion) is null);
            Equal(25, Main.LocalPlayer.inventory[10].stack);
        }, results);
        Check("An assigned remembered chest outside range is reported without reading it", () =>
        {
            Put(10, 1, 25); AddChest(0, "Tools & Travel", 100, 100); Main.chest.Forbidden.Add(0);
            var preview = ChestSortService.BeginPreview(new([new("Terraria/Wood", "Tools & Travel")]))!;
            Equal(0, preview.Plan.Moves.Count); Equal("Tools & Travel", preview.Plan.Unsorted.Single().SuggestedChest);
            Equal(0, Main.chest.Reads.Count); Equal(0, Chest.FindCalls.Count);
        }, results);
        Check("A full exact GPT destination does not spill into an available generic chest", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks"); var chosen = AddChest(1, "Tools & Travel", 13, 10);
            for (int i = 0; i < 40; i++) chosen.item[i] = NewItem(2, 9999);
            var preview = ChestSortService.BeginPreview(new([new("Terraria/Wood", "Tools & Travel")]))!;
            Equal(0, preview.Plan.Moves.Count); Equal("Tools & Travel", preview.Plan.Unsorted.Single().SuggestedChest);
            Equal(ChestSortUnsortedReason.NoSpace, preview.Plan.Unsorted.Single().Code);
        }, results);
        Check("Changing world after a GPT preview invalidates confirmation", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks"); ChestSortService.BeginPreview(new([new("Terraria/Wood", "Blocks")]));
            Main.ActiveWorldFileData.UniqueId = Guid.NewGuid(); True(!ChestSortService.Confirm().Success);
        }, results);
        Check("Remember choices performs no item or chest-content mutation", () =>
        {
            var item = Put(10, 1, 25); var chest = AddChest(0, "Tools & Travel");
            var receipt = ChestSortService.RememberChoices(new([new("Terraria/Wood", "Tools & Travel")]), out _);
            True(receipt is not null); Equal("chest:Tools & Travel", Main.LocalPlayer.Preferences.Rules["Terraria/Wood"].Target);
            True(ReferenceEquals(item, Main.LocalPlayer.inventory[10])); True(chest.item.All(slot => slot.IsAir)); Equal(0, Main.chest.Reads.Count);
        }, results);
        Check("Remember choices validates current inventory and labels and blocks multiplayer", () =>
        {
            Put(10, 1, 25); AddChest(0, "Blocks");
            True(ChestSortService.RememberChoices(new([new("Terraria/Stone", "Blocks")]), out _) is null);
            True(ChestSortService.RememberChoices(new([new("Terraria/Wood", "Secret")]), out _) is null);
            Main.netMode = NetmodeID.MultiplayerClient;
            True(ChestSortService.RememberChoices(new([new("Terraria/Wood", "Blocks")]), out _) is null);
            Equal(0, Main.LocalPlayer.Preferences.Rules.Count);
        }, results);
        Check("Validator excludes unknown rows and all conflicting duplicates but preserves independent choices", () =>
        {
            var result = SortSuggestionValidator.Validate(new([new("Terraria/Wood", "Blocks"), new("Terraria/Wood", "Materials"),
                new("Terraria/Stone", "blocks"), new("Terraria/Stone", "Blocks"), new("Terraria/Secret", "Blocks")]),
                ["Terraria/Wood", "Terraria/Stone"], ["Blocks", "Materials"]);
            Equal(1, result!.Assignments.Count); Equal("Terraria/Stone", result.Assignments[0].ItemKey); Equal("Blocks", result.Assignments[0].ChestName);
        }, results);
        Check("Validator does not rescue conflicting valid and unknown destinations", () =>
        {
            True(SortSuggestionValidator.Validate(new([new("Terraria/Wood", "Blocks"), new("Terraria/Wood", "Unknown")]), ["Terraria/Wood"], ["Blocks"]) is null);
            True(SortSuggestionValidator.Validate(new(Enumerable.Range(0, 41).Select(_ => new SortAssignment("Terraria/Wood", "Blocks")).ToArray()), ["Terraria/Wood"], ["Blocks"]) is null);
        }, results);
        return results.ToArray();
    }

    private static void Reset()
    {
        ChestSortService.Cancel(); Main.dedServ = Main.gameMenu = false; Main.netMode = Main.myPlayer = 0;
        Main.LocalPlayer = new(); Main.mouseItem = new(); Main.ActiveWorldFileData = new(); Main.chest = new(); Main.tile = new();
        Chest.Locations = []; Chest.InUse = []; Chest.Locked = []; Chest.FindCalls = [];
        Recipe.Refreshes = 0; Recipe.ThrowOnRefresh = false; Item.CloneCalls = Item.FailCloneAt = 0;
    }
    private static Item NewItem(int type, int count) => new() { type = type, stack = count, Name = type == 1 ? "Wood" : type == 2 ? "Stone" : "Item " + type, TestCategory = ChestSortCategory.Block };
    private static (string Name, Action<Item> Change)[] MutableFields() =>
    [
        ("damage", item => item.damage = 42), ("defense", item => item.defense = 4),
        ("value", item => item.value = 1000), ("rarity", item => item.rare = 3),
        ("knockback", item => item.knockBack = 2.5f), ("scale", item => item.scale = 1.5f),
        ("projectile", item => item.shoot = 3), ("projectile speed", item => item.shootSpeed = 5.5f),
        ("use time", item => item.useTime = 10), ("use animation", item => item.useAnimation = 10),
        ("mana cost", item => item.mana = 20), ("critical chance", item => item.crit = 7)
    ];
    private static Item Put(int slot, int type, int count) => Main.LocalPlayer.inventory[slot] = NewItem(type, count);
    private static Chest AddChest(int id, string name, int x = 10, int y = 10, bool remembered = true)
    {
        Chest chest = new() { x = x, y = y, name = name }; Main.chest[id] = chest; Chest.Locations[(x, y)] = id;
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
            Main.tile[x + dx, y + dy] = new Tile { HasTile = true, TileType = TileID.Containers, TileFrameX = (short)(dx * 18), TileFrameY = (short)(dy * 18) };
        if (remembered) Main.LocalPlayer.Memory.Candidates.Add(new GptKnowledgePlayer.SortingChest(x, y, chest, Label: name));
        return chest;
    }
    private static void Check(string name, Action action, List<string> results)
    {
        Reset();
        try { action(); }
        catch (Exception ex) { throw new InvalidOperationException(name + ": " + ex.Message, ex); }
        results.Add("PASS " + name);
    }
    private static void True(bool value) { if (!value) throw new InvalidOperationException("Assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
}

