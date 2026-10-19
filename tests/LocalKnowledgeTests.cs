using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaGPT.Common.Knowledge;

namespace TerrariaGPT.Tests;

/// <summary>Executable behavioral checks, with no Terraria or network dependency.</summary>
public static class LocalKnowledgeTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);

    public static string[] Run()
    {
        var results = new List<string>();
        Check("Carried counts include equipment without adding remembered chest quantities", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot(
                [Stack("MiningHelmet", "Mining Helmet", 1, "Inventory, slot 1"), Stack("MiningHelmet", "Mining Helmet", 1, "Equipment and vanity, slot 2")],
                [Chest("Workshop", false, Stack("MiningHelmet", "Mining Helmet", 1, "Workshop"))]);

            string answer = Answer("How many Mining Helmet do I have?", snapshot);
            Contains(answer, "You have 2 Mining Helmet with you");
            Contains(answer, "Inventory, slot 1 x1");
            Contains(answer, "Equipment and vanity, slot 2 x1");
            Contains(answer, "Workshop: 1 Mining Helmet");
            Excludes(answer, "You have 3");
        }, results);

        Check("Closed chest contents retain their observation timestamp and refresh guidance", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot([], [Chest("Mining supplies", false, Stack("IronBar", "Iron Bar", 12, "Mining supplies"))]);
            string answer = Answer("Which chest has my Iron Bar?", snapshot);
            Contains(answer, "Mining supplies: 12 Iron Bar");
            Contains(answer, ObservedAt.ToLocalTime().ToString("g"));
            Contains(answer, "last seen");
            Contains(answer, "not checked live");
            Contains(answer, "Reopen a chest");
            Excludes(answer, "currently open");
        }, results);

        Check("An open chest is identified separately from saved closed-chest memory", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot([], [Chest("Current chest", true, Stack("IronBar", "Iron Bar", 3, "Current chest"))]);
            string answer = Answer("Where are my Iron Bar?", snapshot);
            Contains(answer, "Current chest: 3 Iron Bar (currently open)");
            Excludes(answer, "last seen");
            Excludes(answer, "Closed-chest contents");
        }, results);

        Check("A missing known item is absent only from available inventory and chest memory", () =>
        {
            string answer = Answer("Do I have Diamond?", Snapshot([], []), "Terraria/Diamond", "Diamond");
            Contains(answer, "current inventory or equipment");
            Contains(answer, "remembered contents");
            Contains(answer, "Unopened and forgotten chests are never searched");
            Excludes(answer, "anywhere in your world");
            Excludes(answer, "does not exist");
            Excludes(answer, "you don't own");
        }, results);

        Check("An unresolved item asks for clarification instead of guessing ownership", () =>
        {
            string answer = Answer("Do I have the thing I need?", Snapshot([], []));
            Contains(answer, "Which item");
            Excludes(answer, "You have 0");
            Excludes(answer, "don't see");
        }, results);

        Check("Resolved item identity combines prefixed and ordinary names", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot(
                [Stack("CopperShortsword", "Legendary Copper Shortsword", 1, "Inventory, slot 1"),
                 Stack("CopperShortsword", "Copper Shortsword", 1, "Inventory, slot 2"),
                 Stack("IronShortsword", "Iron Shortsword", 1, "Inventory, slot 3")], []);
            string answer = Answer("Do I have a Copper Shortsword?", snapshot, "Terraria/CopperShortsword", "Copper Shortsword");
            Contains(answer, "You have 2 Copper Shortsword with you");
            Contains(answer, "Inventory, slot 1 x1");
            Contains(answer, "Inventory, slot 2 x1");
            Excludes(answer, "Inventory, slot 3");
        }, results);

        Check("A more specific item name wins over an overlapping short name", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot(
                [Stack("Wood", "Wood", 99, "Inventory, slot 1"), Stack("BorealWood", "Boreal Wood", 6, "Inventory, slot 2")], []);
            string answer = Answer("Do I have Boreal Wood?", snapshot);
            Contains(answer, "You have 6 Boreal Wood with you");
            Excludes(answer, "Inventory, slot 1");
        }, results);

        Check("Plural item questions resolve from observed names without injected item keys", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot(
                [Stack("IronBar", "Iron Bar", 9, "Inventory, slot 1"), Stack("Starfury", "Starfury", 1, "Inventory, slot 2")], []);
            string answer = Answer("How many Iron Bars do I have?", snapshot);
            Contains(answer, "You have 9 Iron Bar with you");
            answer = Answer("Do I have any Starfuries?", snapshot);
            Contains(answer, "You have 1 Starfury with you");
            Contains(answer, "Inventory, slot 2 x1");
        }, results);

        Check("Name matching respects complete names and common final-noun plurals", () =>
        {
            if (!LocalKnowledgeAnswer.NameAppearsInQuestion("Do I have Iron Bars?", "Iron Bar") ||
                !LocalKnowledgeAnswer.NameAppearsInQuestion("Do I have an Iron Bar?", "Iron Bar") ||
                !LocalKnowledgeAnswer.NameAppearsInQuestion("Do I have Starfuries?", "Starfury") ||
                !LocalKnowledgeAnswer.NameAppearsInQuestion("Where are my Golden Keys?", "Golden Key") ||
                !LocalKnowledgeAnswer.NameAppearsInQuestion("How many Sand Boxes do I have?", "Sand Box") ||
                !LocalKnowledgeAnswer.NameAppearsInQuestion("Do I have Bone Javelins?", "Bone Javelins"))
                throw new InvalidOperationException("A complete singular, plural, or already-plural name was missed.");
            if (LocalKnowledgeAnswer.NameAppearsInQuestion("Do I have Goldfish?", "Gold") ||
                LocalKnowledgeAnswer.NameAppearsInQuestion("Do I have Bars?", "Iron Bar") ||
                LocalKnowledgeAnswer.NameAppearsInQuestion("Do I have anything?", ""))
                throw new InvalidOperationException("A partial or empty item name incorrectly matched.");
        }, results);
        Check("Name variants accept omitted possessive punctuation and the actual noun before of", () =>
        {
            foreach (string name in new[] { "Night's Edge", "Night’s Edge", "Nights Edge" })
                if (!LocalKnowledgeAnswer.NameAppearsInQuestion("How do I make " + name + "?", "Night's Edge"))
                    throw new InvalidOperationException("Possessive spelling was missed: " + name);
            if (!LocalKnowledgeAnswer.NameAppearsInQuestion("How do I make Blades of Grass?", "Blade of Grass")
                || !LocalKnowledgeAnswer.NameAppearsInQuestion("Do I have Jungle Spore?", "Jungle Spores"))
                throw new InvalidOperationException("A natural singular/plural noun was missed.");
            if (LocalKnowledgeAnswer.NameAppearsInQuestion("Where is grassland?", "Grass"))
                throw new InvalidOperationException("A partial word became an item match.");
            if (!LocalKnowledgeAnswer.NameAppearsInQuestion("How do I make Phoenix10?", "Phoenix10"))
                throw new InvalidOperationException("A numeric public item name was lost while normalizing stack notation.");
        }, results);

        Check("A specific plural item name wins over an overlapping shorter item name", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot(
                [Stack("Wood", "Wood", 99, "Inventory, slot 1"), Stack("BorealWood", "Boreal Wood", 6, "Inventory, slot 2")], []);
            string answer = Answer("How many Boreal Woods do I have?", snapshot);
            Contains(answer, "You have 6 Boreal Wood with you");
            Excludes(answer, "Inventory, slot 1");
        }, results);

        Check("General wiki questions fall through without manufacturing inventory evidence", () =>
        {
            if (LocalKnowledgeAnswer.TryAnswer("How do I craft a Terra Blade?", Snapshot([], [])) is not null)
                throw new InvalidOperationException("A general crafting question was incorrectly treated as an ownership query.");
        }, results);

        Check("Crafting quantity questions fall through to wiki help even for carried ingredients", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot([Stack("IronBar", "Iron Bar", 9, "Inventory, slot 1")], []);
            if (LocalKnowledgeAnswer.TryAnswer("How many Iron Bars do I need to craft an Anvil?", snapshot, "Terraria/IronBar", "Iron Bar") is not null)
                throw new InvalidOperationException("A crafting ingredient quantity was answered as an owned quantity.");
            if (LocalKnowledgeAnswer.TryAnswer("How many Wood are needed for a Work Bench?", snapshot, "Terraria/Wood", "Wood") is not null)
                throw new InvalidOperationException("An unowned recipe ingredient quantity was answered as an ownership query.");
        }, results);

        Check("Explicit ownership quantity questions still report carried amounts", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot([Stack("IronBar", "Iron Bar", 9, "Inventory, slot 1")], []);
            string answer = Answer("How many Iron Bars do I have?", snapshot, "Terraria/IronBar", "Iron Bar");
            Contains(answer, "You have 9 Iron Bar with you");
            Contains(answer, "Inventory, slot 1 x9");
            answer = Answer("How many Iron Bars am I carrying?", snapshot, "Terraria/IronBar", "Iron Bar");
            Contains(answer, "You have 9 Iron Bar with you");
        }, results);

        Check("Compound crafting advice never becomes an ownership lookup for the finished item", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot([Stack("Wood", "Wood", 9, "Inventory, slot 1")], []);
            if (LocalKnowledgeAnswer.TryAnswer("Do I have what I need for Blade of Grass and where can I get the rest?", snapshot, "Terraria/BladeofGrass", "Blade of Grass") is not null
                || LocalKnowledgeAnswer.TryAnswer("Do I have enough Wood to craft a Work Bench?", snapshot, "Terraria/WorkBench", "Work Bench") is not null)
                throw new InvalidOperationException("Crafting advice was replaced by ownership of the finished item.");
        }, results);

        Check("Inventory overview reports observed carrying slots without chest contents", () =>
        {
            KnowledgeSnapshot snapshot = Snapshot([Stack("Torch", "Torch", 5, "Inventory, slot 1")],
                [Chest("Storage", false, Stack("Diamond", "Diamond", 10, "Storage"))]);
            string answer = Answer("What is in my inventory?", snapshot);
            Contains(answer, "Torch x5");
            Excludes(answer, "Diamond");
            Excludes(answer, "Storage");
        }, results);

        Check("Ownership answers leave immutable observation values unchanged", () =>
        {
            ItemStackSnapshot item = Stack("Torch", "Torch", 5, "Inventory, slot 1");
            OwnedChestSnapshot chest = Chest("Storage", false, Stack("Torch", "Torch", 10, "Storage"));
            KnowledgeSnapshot snapshot = Snapshot([item], [chest]);
            Answer("How many Torch do I have?", snapshot);
            if (snapshot.Inventory.Single() != item || snapshot.OwnedChests.Single() != chest ||
                item.Stack != 5 || chest.Items.Single().Stack != 10 || chest.LastObservedUtc != ObservedAt)
                throw new InvalidOperationException("Answering changed the captured observation.");
        }, results);

        return results.ToArray();
    }

    private static ItemStackSnapshot Stack(string key, string name, int count, string location) =>
        new("Terraria/" + key, name, count, location);

    private static OwnedChestSnapshot Chest(string label, bool open, params ItemStackSnapshot[] items) =>
        new(label, 20, 30, ObservedAt, Array.AsReadOnly(items)) { IsCurrentlyOpen = open };

    private static KnowledgeSnapshot Snapshot(ItemStackSnapshot[] inventory, OwnedChestSnapshot[] chests) =>
        new("Test character", ObservedAt, Array.Empty<KnowledgeSubject>(), Array.AsReadOnly(inventory), Array.AsReadOnly(chests), Array.Empty<string>());

    private static string Answer(string question, KnowledgeSnapshot snapshot, string? key = null, string? name = null) =>
        LocalKnowledgeAnswer.TryAnswer(question, snapshot, key, name) ?? throw new InvalidOperationException("Expected an inventory answer, got no answer.");

    private static void Contains(string answer, string expected)
    {
        if (!answer.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected answer to contain '{expected}'. Actual answer: {answer}");
    }

    private static void Excludes(string answer, string forbidden)
    {
        if (answer.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Answer incorrectly contained '{forbidden}'. Actual answer: {answer}");
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
