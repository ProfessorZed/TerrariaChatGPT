#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TerrariaChatGPT.Common.Goals;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Tests;

/// <summary>Pure integration-boundary tests; no game, network, browser or account access.</summary>
public static class ChatFeatureBoundaryTests
{
    public static string[] Run()
    {
        var output = new List<string>();
        void Check(string name, Action action) { action(); output.Add(name); }
        Check("Observed context exports only the explicit public contract", () =>
        {
            using var json = Project(Empty());
            string[] expected = { "enabled", "capturedAtUtc", "inventory", "rememberedItems", "bosses", "worldProgress", "achievements", "biomes", "npcs", "npcHappiness", "knownChests", "sortingCandidates", "currentBiomes", "truncated" };
            Assert(json.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(value => value).SequenceEqual(expected.OrderBy(value => value)), "Unapproved field in observation export.");
        });
        Check("Default chest coordinates are removed while observation age and open state survive", () =>
        {
            using var json = Project(Empty() with { RememberedItems = new[] { new RememberedProgressItem("Terraria/Wood", "Wood", 20, "Chest at tile 999, 123", DateTimeOffset.UnixEpoch, false) } });
            JsonElement item = json.RootElement.GetProperty("rememberedItems")[0];
            Assert(!json.RootElement.GetRawText().Contains("999, 123") && item.GetProperty("lastObservedUtc").GetString()!.StartsWith("1970-01-01"), "Chest position/age handling incorrect.");
            Assert(!item.GetProperty("isCurrentlyOpen").GetBoolean(), "Remembered storage promoted to current.");
        });
        Check("Empty known chest names survive export without coordinates or invented nearby state", () =>
        {
            using var json = Project(Empty() with { KnownChests = new[] {
                new KnownProgressChest("Vanity & Paints", DateTimeOffset.UnixEpoch),
                new KnownProgressChest("Vanity & Paints", DateTimeOffset.UnixEpoch.AddDays(1)),
                new KnownProgressChest("Chest at tile 999, 123", DateTimeOffset.UnixEpoch) } });
            var rows = json.RootElement.GetProperty("knownChests");
            Assert(rows.GetArrayLength() == 1 && rows[0].GetProperty("name").GetString() == "Vanity & Paints", "Known names lost or duplicated.");
            Assert(rows[0].EnumerateObject().Count() == 2 && !rows.GetRawText().Contains("999"), "Non-label chest data escaped.");
        });
        Check("Every context collection has an explicit bound and discloses truncation", () =>
        {
            var snapshot = Empty() with
            {
                Inventory = Enumerable.Range(0, 140).Select(index => new ProgressItem("Terraria/Item" + index, "Item" + index, 1, "Inventory", true)).ToArray(),
                RememberedItems = Enumerable.Range(0, 180).Select(index => new RememberedProgressItem("Terraria/Item" + index, "Item" + index, 1, "Chest" + index, DateTimeOffset.UnixEpoch, false)).ToArray(),
                Bosses = Enumerable.Range(0, 50).Select(index => new ProgressFlag("boss" + index, "Boss" + index, false, "world")).ToArray(),
                WorldProgress = Enumerable.Range(0, 50).Select(index => new ProgressFlag("event" + index, "Event" + index, false, "world")).ToArray(),
                Achievements = Enumerable.Range(0, 130).Select(index => new ProgressFlag("achievement" + index, "Achievement" + index, false, "account")).ToArray(),
                VisitedBiomes = Enumerable.Range(0, 50).Select(index => new ProgressSubject("biome" + index, "Biome" + index)).ToArray(),
                KnownNpcs = Enumerable.Range(0, 90).Select(index => new NpcProgress("npc" + index, "NPC" + index, true, false)).ToArray(),
                NpcHappiness = Enumerable.Range(0, 90).Select(index => Happiness() with { Key = "Terraria/Npc" + index, Name = "NPC" + index }).ToArray(),
                KnownChests = Enumerable.Range(0, 90).Select(index => new KnownProgressChest("Chest" + index, DateTimeOffset.UnixEpoch)).ToArray(),
                CurrentBiomes = Enumerable.Range(0, 30).Select(index => "Biome" + index).ToArray()
            };
            using var json = Project(snapshot);
            foreach (var limit in new[] { ("inventory", 120), ("rememberedItems", 160), ("bosses", 40), ("worldProgress", 40), ("achievements", 120), ("biomes", 40), ("npcs", 80), ("npcHappiness", 80), ("knownChests", 80), ("currentBiomes", 24) })
                Assert(json.RootElement.GetProperty(limit.Item1).GetArrayLength() == limit.Item2, "Missing bound on " + limit.Item1);
            Assert(json.RootElement.GetProperty("truncated").GetBoolean(), "Truncation hidden from model.");
        });
        Check("Sorting candidates are bounded exact item identities without inventory slots or private state", () =>
        {
            var candidates = Enumerable.Range(0, 45).Select(index => new ProgressSubject("Terraria/Item" + index, "Item " + index)).ToArray();
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(ObservedContextBuilder.Build(Empty(), "Categorise them according to my chests", candidates)));
            var rows = json.RootElement.GetProperty("sortingCandidates");
            Assert(rows.GetArrayLength() == 40 && json.RootElement.GetProperty("truncated").GetBoolean(), "Sorting candidate limit missing.");
            Assert(rows.EnumerateArray().All(row => row.EnumerateObject().Select(field => field.Name).OrderBy(name => name).SequenceEqual(new[] { "key", "name" })), "Sorting exported unauthorized fields.");
        });
        Check("Sorting candidates preserve canonical mod keys and exclude malformed identities", () =>
        {
            string longKey = "ExampleMod/" + new string('x', 150);
            var candidates = new[] { new ProgressSubject(longKey, "Long key"), new ProgressSubject(longKey, "Duplicate"),
                new ProgressSubject("Terraria/Wood\n", "Invalid"), new ProgressSubject("../private/file", "Invalid"),
                new ProgressSubject("Terraria/" + new string('x', 181), "Too long") };
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(ObservedContextBuilder.Build(Empty(), "sort", candidates)));
            var rows = json.RootElement.GetProperty("sortingCandidates");
            Assert(rows.GetArrayLength() == 1 && rows[0].GetProperty("key").GetString() == longKey, "Canonical key was truncated, duplicated or malformed identity accepted.");
        });
        Check("Multi-word current biomes use the public keys accepted by both companion validators", () =>
        {
            using var json = Project(Empty() with { CurrentBiomes = new[] { "Glowing Mushroom", "Underground Desert", "Terraria/Forest" } });
            Assert(json.RootElement.GetProperty("currentBiomes").EnumerateArray().Select(value => value.GetString())
                .SequenceEqual(new[] { "Terraria/GlowingMushroom", "Terraria/UndergroundDesert", "Terraria/Forest" }), "Current biome identity lost during transport.");
        });
        Check("Mentioned inventory items are retained ahead of unrelated rows when truncating", () =>
        {
            var inventory = Enumerable.Range(0, 125).Select(index => new ProgressItem("Terraria/Item" + index, "Item" + index, 1, "Inventory", true)).ToList();
            inventory.Add(new ProgressItem("Terraria/ZebraSword", "Zebra Sword", 1, "Inventory", true));
            using var json = Project(Empty() with { Inventory = inventory }, "Do I have Zebra Sword?");
            Assert(json.RootElement.GetProperty("inventory")[0].GetProperty("name").GetString() == "Zebra Sword", "Relevant item omitted by truncation.");
        });
        Check("Inventory grouping preserves crafting eligibility and caps integer sums", () =>
        {
            using var json = Project(Empty() with { Inventory = new[]
            {
                new ProgressItem("Terraria/Wood", "Wood", int.MaxValue, "Inventory", true),
                new ProgressItem("Terraria/Wood", "Wood", int.MaxValue, "Inventory", true),
                new ProgressItem("Terraria/Wood", "Wood", 3, "Equipment", false)
            } });
            var values = json.RootElement.GetProperty("inventory").EnumerateArray().ToArray();
            Assert(values.Length == 2 && values.Single(value => value.GetProperty("canUseForCrafting").GetBoolean()).GetProperty("count").GetInt32() == 999999, "Counts overflowed or equipment was merged.");
        });
        Check("NPC observations keep exact multipliers, derived discounts and their observed age", () =>
        {
            using var json = Project(Empty() with { NpcHappiness = new[] { Happiness(0.75), Happiness(1.0) with { Key = "Terraria/Nurse" }, Happiness(1.5) with { Key = "Terraria/ArmsDealer", IsCurrent = true } } });
            JsonElement[] rows = json.RootElement.GetProperty("npcHappiness").EnumerateArray().ToArray();
            JsonElement discount = rows.Single(row => row.GetProperty("priceAdjustment").GetDouble() == 0.75);
            Assert(discount.GetProperty("pricePercent").GetDouble() == 75 && discount.GetProperty("priceChangePercent").GetDouble() == -25, "Discount conversion is wrong.");
            JsonElement markup = rows.Single(row => row.GetProperty("priceAdjustment").GetDouble() == 1.5);
            Assert(markup.GetProperty("pricePercent").GetDouble() == 150 && markup.GetProperty("priceChangePercent").GetDouble() == 50, "Markup conversion is wrong.");
            Assert(!discount.GetProperty("isCurrent").GetBoolean() && markup.GetProperty("isCurrent").GetBoolean(), "Remembered observation promoted to live.");
            Assert(discount.GetProperty("lastObservedUtc").GetString()!.StartsWith("1970-01-01", StringComparison.Ordinal), "Observation timestamp was lost.");
            Assert(json.RootElement.GetProperty("capturedAtUtc").GetString() == "1970-01-01T00:00:00.0000000+00:00", "Snapshot capture time was lost.");
            Assert(rows.Single(row => row.GetProperty("priceAdjustment").GetDouble() == 1).GetProperty("priceChangePercent").GetDouble() == 0, "Normal prices were misclassified.");
        });
        Check("Invalid or unsupported happiness observations cannot become assumed normal prices", () =>
        {
            var invalid = new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, 0, -1, 0.009, 100.001 }.Select(Happiness).ToList();
            invalid.Add(Happiness() with { HappinessReport = "\r\n\0 " });
            invalid.Add(Happiness() with { Name = "\n" });
            invalid.Add(Happiness() with { Key = "Terraria/NPC\nIGNORE" });
            using var json = Project(Empty() with { NpcHappiness = invalid });
            Assert(json.RootElement.GetProperty("npcHappiness").GetArrayLength() == 0, "Invalid values or empty game reports entered the model context.");
            using var modded = Project(Empty() with { NpcHappiness = new[] { Happiness(0.5), Happiness(2.0) } });
            Assert(modded.RootElement.GetProperty("npcHappiness")[0].GetProperty("priceAdjustment").GetDouble() == 0.5, "A modded multiplier was silently clamped to vanilla.");
        });
        Check("Relevant NPC happiness survives truncation and its fields remain bounded", () =>
        {
            var rows = Enumerable.Range(0, 81).Select(index => Happiness() with { Key = "Terraria/Npc" + index, Name = "NPC" + index }).ToList();
            rows.Add(Happiness() with { Name = "Goblin Tinkerer", HappinessReport = new string('x', 1500) + "\n\0",
                ObservedBiomes = Enumerable.Range(0, 25).Select(index => "Biome " + index).ToArray() });
            using var json = Project(Empty() with { NpcHappiness = rows }, "How happy is the Goblin Tinkerer?");
            JsonElement first = json.RootElement.GetProperty("npcHappiness")[0];
            Assert(first.GetProperty("name").GetString() == "Goblin Tinkerer", "Relevant NPC omitted.");
            Assert(first.GetProperty("happinessReport").GetString()!.Length == 1200, "Happiness report exceeded the export bound.");
            Assert(first.GetProperty("observedBiomes").GetArrayLength() == 24 && json.RootElement.GetProperty("truncated").GetBoolean(), "NPC biome bound was not disclosed.");
        });
        Check("Happiness biome context is a detached observation and does not replace current biome state", () =>
        {
            using var json = Project(Empty() with { NpcHappiness = new[] { Happiness() with { ObservedBiomes = new[] { "Underground Desert", "Terraria/Forest", "Terraria/Forest", "", "Not a key!" } } } });
            Assert(json.RootElement.GetProperty("currentBiomes").GetArrayLength() == 0, "Old NPC observation became current player state.");
            Assert(json.RootElement.GetProperty("npcHappiness")[0].GetProperty("observedBiomes").EnumerateArray().Select(value => value.GetString())
                .SequenceEqual(new[] { "Terraria/UndergroundDesert", "Terraria/Forest" }), "Observed biome names were not normalized, validated and deduplicated.");
        });
        Check("Account and world progress remain distinct", () =>
        {
            using var json = Project(Empty() with { Bosses = new[] { new ProgressFlag("eye_of_cthulhu", "Eye", false, "world") }, Achievements = new[] { new ProgressFlag("EYE_ON_YOU", "Eye", true, "account") } });
            Assert(json.RootElement.GetProperty("bosses")[0].GetProperty("scope").GetString() == "world"
                && !json.RootElement.GetProperty("bosses")[0].GetProperty("completed").GetBoolean(), "World state changed.");
            Assert(json.RootElement.GetProperty("achievements")[0].GetProperty("scope").GetString() == "account", "Account scope lost.");
        });
        Check("Control characters and excessive display strings are removed from exported labels", () =>
        {
            using var json = Project(Empty() with { Inventory = new[] { new ProgressItem("Terraria/Wood", new string('a', 300) + "\n\0", 1, "Inventory\r\n", true) } });
            JsonElement row = json.RootElement.GetProperty("inventory")[0];
            Assert(row.GetProperty("name").GetString()!.Length == 120 && row.GetProperty("location").GetString() == "Inventory", "Display strings not bounded.");
        });
        Check("Valid goal suggestions remain inert typed metadata", () =>
        {
            using var json = JsonDocument.Parse("""{"goalSuggestion":{"title":"Make a sword","steps":[{"kind":"craft","key":"Terraria/BladeofGrass","label":"Blade of Grass","target":1}]}}""");
            GoalProposal? goal = GoalProposalReader.Read(json.RootElement);
            Assert(goal?.Title == "Make a sword" && goal.Steps[0].Target == 1, "Valid goal rejected.");
        });
        Check("Malformed goal targets, kinds and nested types are rejected without throwing", () =>
        {
            foreach (string step in new[]
            {
                "null", "17", "[]", "{}",
                """{"kind":"spawn","key":"x","label":"x","target":1}""",
                """{"kind":"item","key":"x","label":"x","target":0}""",
                """{"kind":"item","key":"x","label":"x","target":1000000}""",
                """{"kind":"item","key":"x","label":"x","target":2147483648}""",
                """{"kind":"item","key":"x","label":"x","target":1.5}""",
                """{"kind":"item","key":"x","label":"x","target":"1"}"""
            })
            {
                using var json = JsonDocument.Parse("{\"goalSuggestion\":{\"title\":\"Goal\",\"steps\":[" + step + "]}}");
                Assert(GoalProposalReader.Read(json.RootElement) is null, "Malformed goal accepted: " + step);
            }
        });
        Check("Overlong or control-bearing goal titles cannot reach UI", () =>
        {
            foreach (string title in new[] { "", new string('x', 101), "a\0b" })
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { goalSuggestion = new { title, steps = new[] { new { kind = "manual", key = "x", label = "x", target = 1 } } } }));
                Assert(GoalProposalReader.Read(json.RootElement) is null, "Unsafe title accepted.");
            }
        });
        Check("Manual goal steps accept an empty automation key", () =>
        {
            using var json = JsonDocument.Parse("""{"goalSuggestion":{"title":"Build arena","steps":[{"kind":"manual","key":"","label":"Build the platforms","target":1}]}}""");
            GoalProposal? goal = GoalProposalReader.Read(json.RootElement);
            Assert(goal is not null && goal.Steps[0].Kind == "manual" && goal.Steps[0].Key == "", "Valid manual step was discarded.");
        });
        Check("Automatic goal steps still require a nonempty lookup key", () =>
        {
            foreach (string kind in new[] { "boss", "item", "craft", "biome", "npc", "achievement" })
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { goalSuggestion = new { title = "Goal", steps = new[] { new { kind, key = "", label = "Step", target = 1 } } } }));
                Assert(GoalProposalReader.Read(json.RootElement) is null, "Empty automatic key accepted for " + kind);
            }
        });
        Check("Goal step count is bounded and cannot silently truncate a promised checklist", () =>
        {
            foreach (int length in new[] { 0, 25 })
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { goalSuggestion = new { title = "Goal", steps = Enumerable.Range(0, length).Select(index => new { kind = "manual", key = "x", label = "Step" + index, target = 1 }) } }));
                Assert(GoalProposalReader.Read(json.RootElement) is null, "Invalid step count accepted.");
            }
        });
        Check("Citation links allow ordinary public HTTP(S) pages only", () =>
        {
            Assert(WebCitationLinks.TryGetPublicUri("https://terraria.wiki.gg/wiki/Starfury", out _), "Ordinary source blocked.");
            foreach (string url in new[] { "file:///C:/Windows/win.ini", "javascript:alert(1)", "data:text/html,x", "http://localhost/", "http://127.0.0.1/", "http://[::1]/", "http://192.168.1.1/", "https://server.local/", "https://server.internal/", "https://user:pass@example.com/", "https://example.com:4444/", "https://example.com/\n" })
                Assert(!WebCitationLinks.TryGetPublicUri(url, out _), "Unsafe source accepted: " + url);
        });
        Check("Invalid Unicode DNS citations are rejected rather than throwing", () =>
        {
            // Uri.TryCreate accepts this, but Uri.IdnHost throws UriFormatException.
            Assert(!WebCitationLinks.TryGetPublicUri("https://a\u200D.com/", out _), "Invalid IDN accepted.");
        });
        Check("Only referenced citation numbers are presented and conflicts are dropped", () =>
        {
            using var json = JsonDocument.Parse("""{"citations":[{"index":1,"url":"https://example.com/a"},{"index":1,"url":"https://example.com/b"},{"index":2,"url":"https://example.com/c"},{"index":3,"url":"https://example.com/d"}]}""");
            IReadOnlyList<WebCitation> sources = WebCitationLinks.Read(json.RootElement, "Answer [1] [2]");
            Assert(sources.Count == 1 && sources[0].Index == 2, "Conflicting or unreferenced citation retained.");
        });
        Check("Malformed optional citation rows do not prevent valid sources", () =>
        {
            using var json = JsonDocument.Parse("""{"citations":[null,3,{}, {"index":{},"url":3},{"index":1,"url":"https://terraria.wiki.gg/","title":"Official\n Wiki"}]}""");
            var source = WebCitationLinks.Read(json.RootElement, "Answer [1]").Single();
            Assert(source.Title == "Official Wiki", "Malformed optional row disrupted source parser.");
        });
        return output.ToArray();
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static JsonDocument Project(ProgressSnapshot snapshot, string question = "Hello") => JsonDocument.Parse(JsonSerializer.Serialize(ObservedContextBuilder.Build(snapshot, question)));
    private static NpcHappinessObservation Happiness(double price = 0.9) => new("Terraria/GoblinTinkerer", "Goblin Tinkerer", price,
        "I like this place.", DateTimeOffset.UnixEpoch, false, Array.Empty<string>());
    private static ProgressSnapshot Empty() => new(DateTimeOffset.UnixEpoch, Array.Empty<ProgressItem>(), Array.Empty<RememberedProgressItem>(),
        Array.Empty<ProgressFlag>(), Array.Empty<ProgressFlag>(), Array.Empty<ProgressFlag>(), Array.Empty<ProgressSubject>(), Array.Empty<NpcProgress>(), Array.Empty<string>());
}
