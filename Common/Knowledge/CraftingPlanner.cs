#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TerrariaChatGPT.Common.Knowledge;

public sealed record CraftingIngredientSource(string ItemName, int Stack, string Location, bool IsRemembered, DateTimeOffset? LastObservedUtc);

public sealed record CraftingIngredientAssessment(
    string Name,
    int Required,
    int InventoryCount,
    int OpenChestCount,
    int RememberedChestCount,
    int MissingCount,
    IReadOnlyList<CraftingIngredientSource> Sources);

/// <summary>Counts apply to whole batches of one installed recipe, never a mixture of alternatives.</summary>
public sealed record CraftingAssessment(
    string ResultKey,
    string ResultName,
    int ResultStack,
    IReadOnlyList<CraftingIngredientAssessment> Ingredients,
    IReadOnlyList<string> Stations,
    IReadOnlyList<string> Conditions,
    int AlternativeCount)
{
    public int BatchCount { get; init; } = 1;
    public int BatchOutputStack { get; init; } = 1;
    public int? RequestedOutputCount { get; init; }
    public bool InventoryHasAllIngredients => Ingredients.All(item => item.InventoryCount == item.Required);
    public bool ObservedHasAllIngredients => Ingredients.All(item => item.MissingCount == 0);
    public bool UsesRememberedChests => Ingredients.Any(item => item.RememberedChestCount > 0);
}

/// <summary>Read-only recipe comparison over detached observations. No game, world or network access.</summary>
public static class CraftingPlanner
{
    public static bool IsCraftingQuestion(string question)
    {
        if (LocalKnowledgeAnswer.IsIngredientUsesQuestion(question)) return false;
        string text = LocalKnowledgeAnswer.Normalize(question);
        if (LocalKnowledgeAnswer.IsInventoryQuestion(question)
            && !LocalKnowledgeAnswer.NeedsCraftingAdvice(question))
            return false;
        // Questions combining a recommendation/acquisition question with crafting need wiki reasoning.
        if (Regex.IsMatch(text, @"\b(and|also)\s+(where|what|how|which|why|can|does|do|when|is)\b"))
            return false;
        if (Regex.IsMatch(text, @"\b(?:what|which)\b.*\b(?:craft|make|build|create)\s+(?:with|using|from)\b"))
            return false;
        return LocalKnowledgeAnswer.NeedsCraftingAdvice(question);
    }

    public static CraftingAssessment? Assess(string question, KnowledgeSnapshot snapshot, string? resolvedItemKey = null, string? resolvedItemName = null)
    {
        if (!IsCraftingQuestion(question) || !snapshot.InventoryShared)
            return null;
        // The public catalog's resolved identity is authoritative, including
        // aliases that do not repeat the display name in the question.
        RecipeReferenceSnapshot[] named = snapshot.Recipes
            .Where(recipe => resolvedItemKey is not null ? recipe.ResultKey == resolvedItemKey
                : LocalKnowledgeAnswer.NameAppearsInQuestion(question, recipe.ResultName))
            .ToArray();
        if (named.Length == 0)
            return null;

        // Prefer the named target after a crafting cue over an ingredient also mentioned in the question.
        string text = LocalKnowledgeAnswer.Normalize(question);
        Match targetCue = Regex.Match(text, @"\b(for|craft|crafting|make|build|create)\b", RegexOptions.RightToLeft);
        string targetText = targetCue.Success ? text[(targetCue.Index + targetCue.Length)..] : text;
        RecipeReferenceSnapshot selected = named
            .OrderByDescending(recipe => LocalKnowledgeAnswer.NameAppearsInQuestion(targetText, recipe.ResultName))
            .ThenByDescending(recipe => recipe.ResultName.Length)
            .ThenByDescending(recipe => recipe.ResultKey == resolvedItemKey)
            .First();
        RecipeReferenceSnapshot[] alternatives = named.Where(recipe => recipe.ResultKey == selected.ResultKey).ToArray();
        if (!RequestedQuantity(question, selected, out int? requested)) return null;
        CraftingAssessment[] assessments = alternatives.Select(recipe => CompareBatches(recipe, snapshot, alternatives.Length - 1, requested))
            .Where(result => result is not null).Select(result => result!).ToArray();
        // Choose a complete recipe first, then the least missing; never join ingredients from different recipes.
        return assessments.OrderBy(result => result.Ingredients.Sum(item => (long)item.MissingCount))
            .ThenBy(result => result.Ingredients.Sum(item => (long)item.RememberedChestCount))
            .ThenBy(result => result.Ingredients.Sum(item => (long)item.OpenChestCount))
            .FirstOrDefault();
    }

    private static bool RequestedQuantity(string question, RecipeReferenceSnapshot recipe, out int? quantity)
    {
        quantity = null;
        // Fractional/negative requests require clarification, not silent
        // conversion to a different positive whole-item amount.
        if (Regex.IsMatch(question, @"(?<!\p{L})[-−]\s*\d|\d+[.,]\d|\d+\s*/\s*\d+")) return false;
        string normalized = LocalKnowledgeAnswer.Normalize(question.Replace('×', 'x'));
        string internalName = recipe.ResultKey[(recipe.ResultKey.LastIndexOf('/') + 1)..];
        var aliases = LocalKnowledgeAnswer.ItemNameForms(recipe.ResultName)
            .Concat(LocalKnowledgeAnswer.ItemNameForms(internalName)).ToArray();
        foreach (string alias in aliases)
        {
            string target = Regex.Escape(alias);
            Match count = Regex.Match(normalized, @"(?:^| )(?:x\s*)?(?<count>\d+)\s*(?:x\s*)?(?:of\s+)?(?:the\s+)?" + target + @"(?= |$)");
            if (!count.Success)
                count = Regex.Match(normalized, @"(?:^| )" + target + @"\s*x\s*(?<count>\d+)(?= |$)");
            if (!count.Success && Regex.IsMatch(normalized, @"(?:^| )\d+e\d+\s*" + target + @"(?= |$)")) return false;
            if (!count.Success) continue;
            if (!int.TryParse(count.Groups["count"].Value, out int parsed) || parsed is < 1 or > 1_000_000) return false;
            quantity = parsed;
            return true;
        }
        return true;
    }

    private static CraftingAssessment? CompareBatches(RecipeReferenceSnapshot recipe, KnowledgeSnapshot snapshot, int alternatives, int? requested)
    {
        if (recipe.ResultStack <= 0 || recipe.Ingredients.Any(item => item.Stack <= 0 || item.Stack > 1_000_000)) return null;
        long batches = requested.HasValue ? ((long)requested.Value + recipe.ResultStack - 1) / recipe.ResultStack : 1;
        long output = batches * recipe.ResultStack;
        if (batches > int.MaxValue || output > int.MaxValue || recipe.Ingredients.Any(item => (long)item.Stack * batches > 1_000_000)) return null;
        RecipeReferenceSnapshot scaled = recipe with { ResultStack = (int)output,
            Ingredients = recipe.Ingredients.Select(item => item with { Stack = (int)(item.Stack * batches) }).ToArray() };
        CraftingAssessment? result = Compare(scaled, snapshot, alternatives);
        return result is null ? null : result with { BatchCount = (int)batches, BatchOutputStack = recipe.ResultStack, RequestedOutputCount = requested };
    }

    public static string? TryAnswer(string question, KnowledgeSnapshot snapshot, string? resolvedItemKey = null, string? resolvedItemName = null)
    {
        CraftingAssessment? assessment = Assess(question, snapshot, resolvedItemKey, resolvedItemName);
        return assessment is null ? null : Format(assessment);
    }

    public static string Format(CraftingAssessment assessment)
    {
        var answer = new StringBuilder();
        string result = assessment.ResultStack == 1 ? assessment.ResultName : $"{assessment.ResultStack} {assessment.ResultName}";
        if (assessment.InventoryHasAllIngredients)
            answer.Append($"You have all the ingredients in your inventory for {result}.");
        else if (assessment.ObservedHasAllIngredients && assessment.UsesRememberedChests)
            answer.Append($"Your inventory and remembered chest contents account for all the ingredients for {result}. Retrieve the chest items and reopen those chests to confirm they are still there.");
        else if (assessment.ObservedHasAllIngredients)
            answer.Append($"You have all the ingredients for {result} across your inventory and the open chest. Move the chest ingredients into your inventory before crafting.");
        else
            answer.Append($"Still need: " + string.Join(", ", assessment.Ingredients.Where(item => item.MissingCount > 0).Select(item => $"{item.MissingCount} {item.Name}")) + $" for {result}.");

        string[] carried = assessment.Ingredients.Where(item => item.InventoryCount > 0)
            .Select(item => $"{item.InventoryCount} {item.Name}").ToArray();
        if (carried.Length > 0)
            answer.Append("\nYou have: " + string.Join(", ", carried) + " in inventory toward this recipe.");
        answer.Append("\nRecipe: " + string.Join(", ", assessment.Ingredients.Select(item => $"{item.Required} {item.Name}")) + ".");
        if (assessment.RequestedOutputCount.HasValue)
            answer.Append($"\nCrafting batches: {assessment.BatchCount} ({assessment.BatchOutputStack} output per batch; {assessment.ResultStack} total for {assessment.RequestedOutputCount.Value} requested).");
        foreach (CraftingIngredientAssessment item in assessment.Ingredients.Where(item => item.OpenChestCount + item.RememberedChestCount > 0))
        {
            answer.Append("\nIn chests:");
            var chestSources = item.Sources.Where(source => source.LastObservedUtc.HasValue)
                .GroupBy(source => (source.ItemName, source.Location, source.IsRemembered, source.LastObservedUtc))
                .Select(group => new CraftingIngredientSource(group.Key.ItemName, group.Sum(source => source.Stack), group.Key.Location, group.Key.IsRemembered, group.Key.LastObservedUtc)).ToArray();
            foreach (CraftingIngredientSource source in chestSources.Take(4))
            {
                string freshness = source.IsRemembered ? $"last seen {source.LastObservedUtc!.Value.ToLocalTime():g}" : "currently open";
                answer.Append($" {source.Location}: {source.Stack} {source.ItemName} ({freshness}).");
            }
            if (chestSources.Length > 4)
                answer.Append($" Plus {chestSources.Skip(4).Sum(source => source.Stack)} more {item.Name} across {chestSources.Length - 4} additional chest sources included in these counts.");
        }
        if (assessment.UsesRememberedChests && !assessment.ObservedHasAllIngredients)
            answer.Append("\nClosed-chest counts are remembered, not checked live. Retrieve those ingredients and reopen the chests to refresh the counts.");
        if (assessment.Stations.Count > 0)
            answer.Append("\nCrafting station required: " + string.Join(", ", assessment.Stations) + ". I have not checked whether that station is nearby.");
        if (assessment.Conditions.Count > 0)
            answer.Append("\nOther requirements: " + string.Join("; ", assessment.Conditions) + ". These conditions have not been checked.");
        if (assessment.AlternativeCount > 0)
            answer.Append($"\nThis is the recipe needing the fewest missing ingredients from {assessment.AlternativeCount + 1} installed alternatives.");
        return answer.ToString();
    }

    private static CraftingAssessment? Compare(RecipeReferenceSnapshot recipe, KnowledgeSnapshot snapshot, int alternativeCount)
    {
        if (recipe.ResultStack <= 0 || recipe.Ingredients.Count > 64 || recipe.Ingredients.Any(item => item.Stack <= 0 || item.Stack > 1_000_000 || item.AcceptedItemKeys.Count == 0))
            return null;
        int demand = recipe.Ingredients.Sum(item => item.Stack);
        var accepted = recipe.Ingredients.Select(item => new HashSet<string>(item.AcceptedItemKeys, StringComparer.Ordinal)).ToArray();
        var relevantKeys = new HashSet<string>(accepted.SelectMany(keys => keys), StringComparer.Ordinal);
        var resources = new List<Resource>();
        var resourceIndex = new Dictionary<(string, int), Resource>();
        void Add(ItemStackSnapshot stack, int kind, string location, DateTimeOffset? observed)
        {
            if (!stack.CanUseForCrafting || stack.Stack <= 0 || !relevantKeys.Contains(stack.ItemKey)) return;
            if (!resourceIndex.TryGetValue((stack.ItemKey, kind), out Resource? resource))
            {
                resource = new Resource(stack.ItemKey, kind);
                resourceIndex.Add((stack.ItemKey, kind), resource);
                resources.Add(resource);
            }
            resource.Parts.Add(new SourcePart(stack.Name, Math.Min(stack.Stack, demand), location, observed));
            resource.Total = (int)Math.Min(demand, (long)resource.Total + stack.Stack);
        }
        foreach (ItemStackSnapshot stack in snapshot.Inventory) Add(stack, 0, stack.Location, null);
        foreach (OwnedChestSnapshot chest in snapshot.OwnedChests)
            foreach (ItemStackSnapshot stack in chest.Items)
                Add(stack, chest.IsCurrentlyOpen ? 1 : 2, chest.Label, chest.LastObservedUtc);

        int sink = resources.Count + recipe.Ingredients.Count + 1;
        var flow = new FlowNetwork(sink + 1);
        var sourceEdges = new List<Edge>();
        var assignments = new Dictionary<(int Resource, int Ingredient), Edge>();
        for (int r = 0; r < resources.Count; r++)
        {
            sourceEdges.Add(flow.Add(0, r + 1, 0));
            for (int i = 0; i < recipe.Ingredients.Count; i++)
                if (accepted[i].Contains(resources[r].Key))
                    assignments[(r, i)] = flow.Add(r + 1, resources.Count + i + 1, demand);
        }
        for (int i = 0; i < recipe.Ingredients.Count; i++)
            flow.Add(resources.Count + i + 1, sink, recipe.Ingredients[i].Stack);

        // Keeping the existing residual graph lets a later ingredient reassign a substitution
        // without consuming the same item twice. Current inventory is allocated before old memory.
        for (int kind = 0; kind < 3; kind++)
        {
            for (int r = 0; r < resources.Count; r++)
                if (resources[r].Kind == kind) sourceEdges[r].Capacity = resources[r].Total;
            flow.Fill(0, sink);
        }
        var counts = new int[recipe.Ingredients.Count, 3];
        var sources = Enumerable.Range(0, recipe.Ingredients.Count).Select(_ => new List<CraftingIngredientSource>()).ToArray();
        for (int r = 0; r < resources.Count; r++)
        {
            Resource resource = resources[r];
            int partIndex = 0, partUsed = 0;
            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                if (!assignments.TryGetValue((r, i), out Edge? edge)) continue;
                int used = edge.Reverse.Capacity;
                counts[i, resource.Kind] += used;
                while (used > 0 && partIndex < resource.Parts.Count)
                {
                    SourcePart part = resource.Parts[partIndex];
                    int take = Math.Min(used, part.Stack - partUsed);
                    sources[i].Add(new CraftingIngredientSource(part.Name, take, part.Location, resource.Kind == 2, part.Observed));
                    partUsed += take;
                    used -= take;
                    if (partUsed == part.Stack) { partIndex++; partUsed = 0; }
                }
            }
        }
        var ingredients = new List<CraftingIngredientAssessment>();
        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            RecipeIngredientSnapshot item = recipe.Ingredients[i];
            ingredients.Add(new CraftingIngredientAssessment(item.Name, item.Stack, counts[i, 0], counts[i, 1], counts[i, 2], item.Stack - counts[i, 0] - counts[i, 1] - counts[i, 2], sources[i].AsReadOnly()));
        }
        return new CraftingAssessment(recipe.ResultKey, recipe.ResultName, recipe.ResultStack, ingredients.AsReadOnly(), recipe.Stations, recipe.Conditions, alternativeCount);
    }

    private sealed record SourcePart(string Name, int Stack, string Location, DateTimeOffset? Observed);
    private sealed class Resource(string key, int kind)
    {
        public string Key { get; } = key;
        public int Kind { get; } = kind;
        public int Total { get; set; }
        public List<SourcePart> Parts { get; } = new();
    }

    private sealed class Edge(int to, int capacity)
    {
        public int To { get; } = to;
        public int Capacity { get; set; } = capacity;
        public Edge Reverse { get; set; } = null!;
    }

    private sealed class FlowNetwork(int size)
    {
        private readonly List<Edge>[] _graph = Enumerable.Range(0, size).Select(_ => new List<Edge>()).ToArray();

        public Edge Add(int from, int to, int capacity)
        {
            var forward = new Edge(to, capacity);
            var reverse = new Edge(from, 0);
            forward.Reverse = reverse;
            reverse.Reverse = forward;
            _graph[from].Add(forward);
            _graph[to].Add(reverse);
            return forward;
        }

        public void Fill(int source, int sink)
        {
            while (true)
            {
                var visited = new bool[_graph.Length];
                var previous = new Edge?[_graph.Length];
                var queue = new Queue<int>();
                visited[source] = true;
                queue.Enqueue(source);
                while (queue.Count > 0 && !visited[sink])
                {
                    int node = queue.Dequeue();
                    foreach (Edge edge in _graph[node])
                    {
                        if (edge.Capacity <= 0 || visited[edge.To]) continue;
                        visited[edge.To] = true;
                        previous[edge.To] = edge;
                        queue.Enqueue(edge.To);
                    }
                }
                if (!visited[sink]) return;
                int amount = int.MaxValue;
                for (int node = sink; node != source; node = previous[node]!.Reverse.To)
                    amount = Math.Min(amount, previous[node]!.Capacity);
                for (int node = sink; node != source; node = previous[node]!.Reverse.To)
                {
                    Edge edge = previous[node]!;
                    edge.Capacity -= amount;
                    edge.Reverse.Capacity += amount;
                }
            }
        }
    }
}

public sealed record IngredientUsesAnswer(string Text, IReadOnlyList<CraftingAssessment> Plans);

/// <summary>Direct public crafting uses; independent result recipes do not share or consume supplies.</summary>
public static class IngredientUsesPlanner
{
    public static IngredientUsesAnswer? TryAnswer(string question, KnowledgeSnapshot snapshot, string sourceName)
    {
        if (!LocalKnowledgeAnswer.IsIngredientUsesQuestion(question)
            || Regex.IsMatch(LocalKnowledgeAnswer.Normalize(question), @"\b(?:why|where|when)\b|\b(?:and|also)\s+(?:what|how|which|can|does|do|is)\b"))
            return null;
        var groups = snapshot.Recipes.GroupBy(recipe => recipe.ResultKey, StringComparer.Ordinal)
            .OrderBy(group => group.First().ResultName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (groups.Length == 0)
            return new IngredientUsesAnswer(snapshot.RecipeLookupComplete
                ? $"No enabled crafting recipe accepts {sourceName} as an ingredient. This checks direct crafting uses only; it does not cover equipment effects, trading or other uses."
                : $"I do not have a complete crafting-recipe list for {sourceName}. No matching uses appear in the supplied recipes; other recipes may exist.", Array.Empty<CraftingAssessment>());
        const int summaryLimit = 24;
        var text = new StringBuilder($"{sourceName} is an ingredient for: " + string.Join(", ", groups.Take(summaryLimit).Select(group => group.First().ResultName)) + ".");
        if (groups.Length > summaryLimit)
            text.Append($" Showing {summaryLimit} of {groups.Length} distinct crafting results.");
        if (!snapshot.RecipeLookupComplete)
            text.Append(" This supplied recipe list may be incomplete.");
        var plans = new List<CraftingAssessment>();
        if (groups.Length <= 6)
        {
            foreach (var group in groups)
            {
                CraftingAssessment? plan = CraftingPlanner.Assess("Recipe", snapshot with { Recipes = group.ToArray() }, group.Key, group.First().ResultName);
                if (plan is null) continue;
                string details = CraftingPlanner.Format(plan);
                if (text.Length + details.Length + 2 > 10_000) break;
                text.Append("\n\n" + details);
                plans.Add(plan);
            }
            if (plans.Count == groups.Length && groups.Length > 1)
                text.Append("\nEach result is compared independently; these recipes do not reserve or spend your items.");
            else if (plans.Count < groups.Length) text.Append($"\nFull comparisons shown for {plans.Count} of {groups.Length} crafting results. Ask how to make a named result for its complete checklist.");
        }
        else text.Append("\nAsk how to make a named result for its full recipe and exact missing materials.");
        return new IngredientUsesAnswer(text.ToString(), plans.AsReadOnly());
    }
}
