#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace TerrariaChatGPT.Common.Knowledge;

/// <summary>Public encyclopedia data only. Never reads tiles, entities or world state.</summary>
public static class GameReferenceFacts
{
    private static object? _sampleSource;
    private static int _sampleCount = -1;
    private static string? _cultureName;
    private static PublicItem[] _publicItems = Array.Empty<PublicItem>();
    private static readonly Dictionary<int, PublicItem> PublicByType = new();

    private sealed class PublicItem
    {
        public Item Item { get; }
        public string Name { get; }
        public string[] DisplayNames { get; }
        public string[] MatchNames { get; }
        public string? Tooltip { get; set; }
        public HashSet<string>? CapabilityTerms { get; set; }

        public PublicItem(Item item)
        {
            Item = item;
            Name = item.Name;
            DisplayNames = LocalKnowledgeAnswer.ItemNameForms(Name).Select(name => " " + name + " ").ToArray();
            string internalName = item.ModItem?.FullName.Split('/').Last() ?? ItemID.Search.GetName(item.type) ?? "";
            string spacedAlias = Regex.Replace(internalName, "(?<=[a-z])(?=[A-Z])", " ");
            spacedAlias = Regex.Replace(spacedAlias, @"\b(\w+)of\b", "$1 of");
            MatchNames = LocalKnowledgeAnswer.ItemNameForms(Name).Concat(LocalKnowledgeAnswer.ItemNameForms(internalName))
                .Concat(LocalKnowledgeAnswer.ItemNameForms(spacedAlias)).Distinct(StringComparer.Ordinal)
                .Select(name => " " + name + " ").ToArray();
        }

        public int MatchRank(string paddedQuestion)
        {
            foreach (string name in DisplayNames)
                if (paddedQuestion.Contains(name, StringComparison.Ordinal)) return 2;
            foreach (string name in MatchNames)
                if (paddedQuestion.Contains(name, StringComparison.Ordinal)) return 1;
            return 0;
        }

        public bool Matches(string paddedQuestion) => MatchRank(paddedQuestion) > 0;
    }

    /// <summary>Clear public-only search indexes after a content reload. Recipes are never cached.</summary>
    public static void ResetCache()
    {
        _sampleSource = null;
        _sampleCount = -1;
        _cultureName = null;
        _publicItems = Array.Empty<PublicItem>();
        PublicByType.Clear();
    }

    private static IReadOnlyList<PublicItem> PublicItems()
    {
        var samples = ContentSamples.ItemsByType;
        string culture = Language.ActiveCulture.Name;
        if (!ReferenceEquals(_sampleSource, samples) || _sampleCount != samples.Count || _cultureName != culture)
        {
            ResetCache();
            _sampleSource = samples;
            _sampleCount = samples.Count;
            _cultureName = culture;
            _publicItems = samples.Values.Where(item => !item.IsAir).Select(item => new PublicItem(item)).ToArray();
            foreach (PublicItem entry in _publicItems) PublicByType[entry.Item.type] = entry;
        }
        return _publicItems;
    }

    private static readonly HashSet<string> QuestionWords = new(
        "what which how where when why can could do does i me my am is are it a an the for to of and or in on with make craft need missing ingredients prevent prevents stop stops negate negates avoid get find have use help gives give lets let".Split(' '),
        StringComparer.Ordinal);

    public static Item? ResolveItem(string question)
    {
        string normalized = LocalKnowledgeAnswer.Normalize(question);
        string padded = " " + normalized + " ";
        PublicItem[] matches = PublicItems().Select(item => (Entry: item, Rank: item.MatchRank(padded)))
            .Where(match => match.Entry.Name.Length > 1 && match.Rank > 0)
            .OrderByDescending(match => match.Rank).ThenByDescending(match => match.Entry.Name.Length).ThenBy(match => match.Entry.Item.type)
            .Select(match => match.Entry).ToArray();
        if (LocalKnowledgeAnswer.NeedsCraftingAdvice(question))
        {
            Match cue = Regex.Match(normalized, @"\b(for|craft|crafting|make|build|create)\s+", RegexOptions.RightToLeft);
            if (cue.Success)
            {
                string target = normalized[(cue.Index + cue.Length)..];
                target = Regex.Split(target, @"\b(with|using|from|out of)\b")[0].Trim();
                if (target.Length == 0)
                    return matches.FirstOrDefault()?.Item;
                // A named ingredient before the cue must not be mistaken for
                // the item the player wants to make. Let the wiki clarify an
                // unnamed/general result such as 'an anvil'.
                string paddedTarget = " " + target + " ";
                return matches.Where(item => item.Matches(paddedTarget)).OrderByDescending(item => item.MatchRank(paddedTarget))
                    .ThenByDescending(item => item.Name.Length).ThenBy(item => item.Item.type).FirstOrDefault()?.Item;
            }
        }
        return matches.FirstOrDefault()?.Item;
    }

    public static string Key(Item item) => item.ModItem?.FullName ?? "Terraria/" + ItemID.Search.GetName(item.type);

    /// <summary>Resolve capability questions against public tooltips, without consulting a player's state.</summary>
    public static IReadOnlyList<Item> ResolveSubjects(string question, Item? namedSubject)
    {
        if (namedSubject is not null)
            return new[] { namedSubject };
        string[] terms = LocalKnowledgeAnswer.Normalize(question).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length > 2 && !QuestionWords.Contains(term)).Distinct(StringComparer.Ordinal).ToArray();
        if (terms.Length < 2)
            return Array.Empty<Item>();
        var matches = new List<(Item Item, int Score)>();
        foreach (PublicItem entry in PublicItems())
        {
            HashSet<string> publicTerms = CapabilityTerms(entry);
            int score = 0;
            foreach (string term in terms)
                if (publicTerms.Contains(term)) score++;
            if (score >= 2) matches.Add((entry.Item, score));
        }
        return matches.OrderByDescending(match => match.Score).ThenBy(match => match.Item.type)
            .Take(6).Select(match => match.Item).ToArray();
    }

    /// <summary>Copy recipe definitions, never recipe availability or condition predicates.</summary>
    public static IReadOnlyList<RecipeReferenceSnapshot> BuildRecipes(IReadOnlyList<Item> subjects)
    {
        var types = subjects.Select(item => item.type).ToHashSet();
        return CopyRecipes(recipe => types.Contains(recipe.createItem.type));
    }

    /// <summary>Enabled public recipes accepting this ingredient, including supported recipe groups.</summary>
    public static IReadOnlyList<RecipeReferenceSnapshot> BuildIngredientUses(Item ingredient) =>
        CopyRecipes(recipe => AcceptsIngredient(recipe, ingredient.type));

    private static bool AcceptsIngredient(Recipe recipe, int type) => recipe.requiredItem.Any(ingredient =>
        ingredient is not null && !ingredient.IsAir && ingredient.stack > 0 && (ingredient.type == type
            || recipe.acceptedGroups.Where(RecipeGroup.recipeGroups.ContainsKey).Select(id => RecipeGroup.recipeGroups[id])
                .Any(group => group.ValidItems.Contains(ingredient.type) && group.ValidItems.Contains(type))
                && recipe.AcceptedByItemGroups(type, ingredient.type)));

    private static IReadOnlyList<RecipeReferenceSnapshot> CopyRecipes(Func<Recipe, bool> accepts)
    {
        var results = new List<RecipeReferenceSnapshot>();
        for (int i = 0; i < Recipe.numRecipes; i++)
        {
            Recipe recipe = Main.recipe[i];
            if (recipe is null || recipe.Disabled || recipe.createItem.IsAir || !accepts(recipe))
                continue;
            var ingredients = new List<RecipeIngredientSnapshot>();
            foreach (Item ingredient in recipe.requiredItem)
            {
                if (ingredient is null || ingredient.IsAir || ingredient.stack <= 0) continue;
                var alternatives = new HashSet<int> { ingredient.type };
                var groups = recipe.acceptedGroups.Where(RecipeGroup.recipeGroups.ContainsKey)
                    .Select(id => RecipeGroup.recipeGroups[id])
                    .Where(group => group.ValidItems.Contains(ingredient.type)).ToArray();
                foreach (RecipeGroup group in groups)
                    foreach (int type in group.ValidItems)
                        if (recipe.AcceptedByItemGroups(type, ingredient.type))
                            alternatives.Add(type);
                string name = groups.Length == 0 ? ingredient.Name : string.Join(" or ", groups.Select(group => group.GetText()));
                string[] keys = new[] { Key(ingredient) }.Concat(alternatives.OrderBy(type => type)
                    .Where(ContentSamples.ItemsByType.ContainsKey).Select(type => Key(ContentSamples.ItemsByType[type])))
                    .Distinct(StringComparer.Ordinal).ToArray();
                ingredients.Add(new RecipeIngredientSnapshot(name, ingredient.stack, Array.AsReadOnly(keys)));
            }
            results.Add(new RecipeReferenceSnapshot(Key(recipe.createItem), recipe.createItem.Name, recipe.createItem.stack,
                ingredients.AsReadOnly(), recipe.requiredTile.Select(TileName).ToArray(),
                recipe.Conditions.Select(condition => condition.Description.Value).ToArray()));
        }
        return results.AsReadOnly();
    }

    public static IReadOnlyList<KnowledgeFact> Build(string question, Item? subject)
    {
        if (subject is null) return Array.Empty<KnowledgeFact>();
        string key = Key(subject);
        var facts = new List<KnowledgeFact>();
        void Add(string text) => facts.Add(new KnowledgeFact("game-" + facts.Count, key, text, "Installed game reference data"));
        string tooltip = Tooltip(subject);
        if (tooltip.Length > 0) Add($"{subject.Name} tooltip: {tooltip}");
        bool stats = LocalKnowledgeAnswer.NameAppearsInQuestion(question, subject.Name)
            && !Regex.IsMatch(question, @"\b(missing|craft|make|ingredients?|need)\b", RegexOptions.IgnoreCase);
        if (stats)
        {
            Add($"{subject.Name}: maximum stack {subject.maxStack}.");
            if (subject.damage > 0) Add($"{subject.Name}: base damage {subject.damage}, base use time {subject.useTime} ticks, knockback {subject.knockBack:0.##}.");
            if (subject.defense > 0) Add($"{subject.Name}: defense {subject.defense}.");
            if (subject.healLife > 0) Add($"{subject.Name}: restores {subject.healLife} life.");
            if (subject.healMana > 0) Add($"{subject.Name}: restores {subject.healMana} mana.");
            if (subject.pick > 0) Add($"{subject.Name}: pickaxe power {subject.pick}%.");
            if (subject.axe > 0) Add($"{subject.Name}: axe power {subject.axe * 5}%.");
            if (subject.hammer > 0) Add($"{subject.Name}: hammer power {subject.hammer}%.");
        }

        string normalized = LocalKnowledgeAnswer.Normalize(question);
        bool uses = LocalKnowledgeAnswer.IsIngredientUsesQuestion(question);
        bool hasRecipe = false;
        for (int i = 0; i < Recipe.numRecipes && facts.Count < 20; i++)
        {
            Recipe recipe = Main.recipe[i];
            if (recipe is null || recipe.Disabled || recipe.createItem.IsAir) continue;
            if (uses ? !AcceptsIngredient(recipe, subject.type) : recipe.createItem.type != subject.type) continue;
            hasRecipe = true;
            var ingredients = recipe.requiredItem.Select(item => $"{item.stack} {item.Name}").ToArray();
            string stations = recipe.requiredTile.Count == 0 ? "by hand" : "at " + string.Join(", ", recipe.requiredTile.Select(TileName));
            string conditions = recipe.Conditions.Count == 0 ? "" : " Conditions: " + string.Join(", ", recipe.Conditions.Select(condition => condition.Description.Value));
            // Recipe-group substitutions are not inferred from the representative ingredient.
            string groupNote = recipe.acceptedGroups.Count == 0 ? "" : " This recipe allows ingredient groups; these are representative ingredients.";
            Add($"Craft {recipe.createItem.stack} {recipe.createItem.Name}: {string.Join(", ", ingredients)} {stations}.{conditions}{groupNote}");
        }
        if (!uses && !hasRecipe)
            Add($"{subject.Name} has no enabled crafting recipe in this installed game. Acquisition must be checked separately; this does not mean it is unobtainable.");
        return facts.AsReadOnly();
    }

    private static string Tooltip(Item item)
    {
        PublicItems();
        if (PublicByType.TryGetValue(item.type, out PublicItem? entry) && ReferenceEquals(entry.Item, item))
            return entry.Tooltip ??= ReadTooltip(item);
        return ReadTooltip(item);
    }

    private static HashSet<string> CapabilityTerms(PublicItem entry) => entry.CapabilityTerms ??=
        LocalKnowledgeAnswer.Normalize(entry.Name + " " + (entry.Tooltip ??= ReadTooltip(entry.Item)))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static string ReadTooltip(Item item)
    {
        var tooltip = Lang.GetTooltip(item.type);
        return string.Join(" ", Enumerable.Range(0, Math.Min(tooltip.Lines, 12)).Select(tooltip.GetLine));
    }

    private static string TileName(int type)
    {
        string name = type >= TileID.Count ? TileLoader.GetTile(type)?.Name ?? "crafting station" : TileID.Search.GetName(type) ?? "crafting station";
        if (type < TileID.Count)
            name = name switch
            {
                "Bottles" => "Placed Bottle", "AlchemyTable" => "Alchemy Table",
                "Anvils" => "Iron or Lead Anvil", "MythrilAnvil" => "Mythril or Orichalcum Anvil",
                "AdamantiteForge" => "Adamantite or Titanium Forge", "WorkBenches" => "Work Bench",
                "Furnaces" => "Furnace", "TinkerersWorkbench" => "Tinkerer's Workshop",
                "CookingPots" => "Cooking Pot", "Bookcases" => "Bookcase", _ => name
            };
        return Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
    }
}
