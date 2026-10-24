#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TerrariaChatGPT.Common.Goals;
using TerrariaChatGPT.Common.Knowledge;

namespace TerrariaChatGPT.Common.Players;

/// <summary>
/// Read-only progress and local sticky notes. Reads public defeat/rescue records,
/// personal achievements and the existing opened-chest ledger. No map, tile,
/// seed, unopened-storage or active-entity enumeration is allowed here.
/// </summary>
public sealed class GptProgressPlayer : ModPlayer
{
    private readonly Dictionary<string, WorldGoalLedger> _worlds = new(StringComparer.Ordinal);
    private Guid? _activeWorld;
    private WorldGoalLedger? _activeLedger;
    private ProgressSnapshot? _snapshot;
    private KnowledgeSnapshot? _knowledge;
    private int _refreshTicks;
    private bool _legacyDataChecked;
    private NPC? _observedHappinessNpc;
    private int _observedHappinessType;
    private double _observedHappinessPrice;
    private string? _observedHappinessReport;
    private string? _currentHappinessKey;
    private bool IsLocal => !Main.dedServ && !Main.gameMenu && Player.whoAmI == Main.myPlayer;
    public IReadOnlyList<GoalNote> ActiveGoals => IsLocal ? CurrentLedger.NoteView : Array.Empty<GoalNote>();

    public override void Initialize()
    {
        _worlds.Clear();
        _activeWorld = null;
        _activeLedger = null;
        _snapshot = null;
        _knowledge = null;
        _refreshTicks = 0;
        _legacyDataChecked = false;
        ClearCurrentHappiness();
    }

    public override void OnEnterWorld()
    {
        _activeWorld = null;
        _activeLedger = null;
        _snapshot = null;
        _knowledge = null;
        _refreshTicks = 0;
        ClearCurrentHappiness();
        if (!IsLocal) return;
        EnsureLegacyData();
        Refresh();
        ValidateSavedRecipes();
        RefreshNotes();
    }

    public override void PostUpdate()
    {
        if (!IsLocal) { ClearCurrentHappiness(); return; }
        // A short NPC conversation must not be missed by the heavier one-second
        // progress refresh. This only compares that single active conversation.
        ObserveNpcHappiness();
        if (++_refreshTicks < 60) return;
        _refreshTicks = 0;
        Refresh();
    }

    /// <summary>Game-thread-only detached capture, cached for at most one second.</summary>
    public ProgressSnapshot CreateSnapshot()
    {
        if (!IsLocal) throw new InvalidOperationException("Progress is available only for the local player in a loaded world.");
        EnsureLegacyData();
        // Accessing the ledger invalidates observations if the world changed.
        _ = CurrentLedger;
        ObserveNpcHappiness();
        if (_snapshot is null) Refresh();
        return _snapshot!;
    }

    public bool TrySetGoal(GoalProposal proposal, out GoalNote? goal, out string message)
    {
        goal = null;
        if (!IsLocal) { message = "Enter a world to set a goal."; return false; }
        ProgressSnapshot snapshot = CreateSnapshot();
        GoalNote? created = GoalPlanner.Create(proposal, snapshot, _knowledge!, ResolveItem, RecipesFor);
        if (created is null) { message = "That goal could not be added. Try a shorter, more specific goal."; return false; }
        GoalNote? existing = CurrentLedger.Notes.FirstOrDefault(note => note.Signature == created.Signature);
        if (existing is not null) { goal = existing; message = "This goal is already pinned."; return true; }
        if (CurrentLedger.Notes.Count >= GoalPlanner.MaximumNotes)
        { message = "You can pin up to " + GoalPlanner.MaximumNotes + " goals. Close a note first."; return false; }
        CurrentLedger.Notes.Add(created);
        goal = created;
        message = "Goal pinned. Progress updates automatically; manual steps can be checked yourself.";
        return true;
    }

    public bool RemoveGoal(string id) => IsLocal && CurrentLedger.Notes.RemoveAll(note => note.Id == id) > 0;
    public void MoveGoal(string id, float x, float y)
    {
        GoalNote? note = FindNote(id);
        if (note is null) return;
        note.X = WorldGoalLedger.ValidPosition(x);
        note.Y = WorldGoalLedger.ValidPosition(y);
    }
    public void SetCollapsed(string id, bool collapsed) { GoalNote? note = FindNote(id); if (note is not null) note.Collapsed = collapsed; }
    public void ToggleManualStep(string id, int stepIndex)
    {
        GoalNote? note = FindNote(id);
        if (note is null || stepIndex < 0 || stepIndex >= note.MutableSteps.Count) return;
        GoalStepState step = note.MutableSteps[stepIndex];
        if (!step.IsManual) return;
        step.IsComplete = !step.IsComplete;
        step.Current = step.IsComplete ? 1 : 0;
    }
    private GoalNote? FindNote(string id) => IsLocal ? CurrentLedger.Notes.FirstOrDefault(note => note.Id == id) : null;

    public override void SaveData(TagCompound tag)
    {
        EnsureLegacyData();
        if (IsLocal) ObserveNpcHappiness();
        // Happiness is an optional additive field; the existing goal schema stays compatible.
        tag["version"] = 1;
        tag["worlds"] = _worlds.Select(pair => pair.Value.Save(pair.Key)).ToList();
    }

    public override void LoadData(TagCompound tag)
    {
        Initialize();
        _legacyDataChecked = true;
        foreach (TagCompound world in tag.GetList<TagCompound>("worlds"))
        {
            string key = world.GetString("world");
            if (!Guid.TryParse(key, out Guid id)) continue;
            try { _worlds[id.ToString("N")] = WorldGoalLedger.Load(world); }
            catch (Exception) { /* A malformed note must not prevent loading the player's game. */ }
        }
    }

    private void EnsureLegacyData()
    {
        if (_legacyDataChecked) return;
        _legacyDataChecked = true;
        TagCompound? legacy = LegacyPlayerData.Read(Player, nameof(GptProgressPlayer));
        if (legacy is null) return;
        try { LoadData(legacy); }
        catch (Exception) { /* Original unloaded data stays available for recovery. */ }
    }

    private WorldGoalLedger CurrentLedger
    {
        get
        {
            Guid id = Main.ActiveWorldFileData.UniqueId;
            if (_activeWorld == id && _activeLedger is not null) return _activeLedger;
            string key = id.ToString("N");
            if (!_worlds.TryGetValue(key, out WorldGoalLedger? ledger)) _worlds[key] = ledger = new WorldGoalLedger();
            _activeWorld = id;
            _activeLedger = ledger;
            _snapshot = null;
            _knowledge = null;
            ClearCurrentHappiness();
            return ledger;
        }
    }

    private void Refresh()
    {
        WorldGoalLedger ledger = CurrentLedger;
        ObserveNpcHappiness();
        _knowledge = Player.GetModPlayer<GptKnowledgePlayer>().CreateSnapshot();
        // TalkNPC resolves only the NPC the player is currently interacting with.
        NPC? talked = Player.TalkNPC;
        if (talked is not null && talked.active)
        {
            string key = NpcKey(talked);
            ledger.TalkedNpcs[key] = new ProgressSubject(key, GoalPlanner.CleanText(talked.TypeName, 100));
        }

        bool[] defeats =
        {
            NPC.downedSlimeKing, NPC.downedBoss1, NPC.downedBoss2, NPC.downedQueenBee, NPC.downedBoss3,
            NPC.downedDeerclops, Main.hardMode, NPC.downedQueenSlime, NPC.downedMechBoss2,
            NPC.downedMechBoss1, NPC.downedMechBoss3, NPC.downedPlantBoss, NPC.downedGolemBoss,
            NPC.downedFishron, NPC.downedEmpressOfLight, NPC.downedAncientCultist, NPC.downedMoonlord
        };
        var bosses = GoalCatalog.Bosses.Select((boss, index) => new ProgressFlag(boss.Key, boss.Name, defeats[index], "world")).ToArray();
        var worldProgress = new[]
        {
            new ProgressFlag("hardmode", "Hardmode", Main.hardMode, "world"),
            new ProgressFlag("goblin_army", "Goblin Army defeated", NPC.downedGoblins, "world"),
            new ProgressFlag("pirate_invasion", "Pirate Invasion defeated", NPC.downedPirates, "world"),
            new ProgressFlag("frost_legion", "Frost Legion defeated", NPC.downedFrost, "world"),
            new ProgressFlag("martian_madness", "Martian Madness defeated", NPC.downedMartians, "world")
        };
        bool[] rescues = { NPC.savedGoblin, NPC.savedWizard, NPC.savedMech, NPC.savedStylist, NPC.savedAngler,
            NPC.savedTaxCollector, NPC.savedBartender, NPC.savedGolfer };
        var npcs = GoalPlanner.RescueNpcs.Select((npc, index) => new NpcProgress(npc.Key, npc.Name,
            ledger.TalkedNpcs.ContainsKey(npc.Key), rescues[index])).ToList();
        foreach (ProgressSubject npc in ledger.TalkedNpcs.Values)
            if (!npcs.Any(value => value.Key == npc.Key)) npcs.Add(new NpcProgress(npc.Key, npc.Name, true, false));
        // AchievementManager is local profile/account state, never a proof about this world.
        ProgressFlag[] achievements = Main.Achievements is null ? Array.Empty<ProgressFlag>()
            : Main.Achievements.CreateAchievementsList().Take(500).Select(value => new ProgressFlag(
                value.Name, GoalPlanner.CleanText(value.FriendlyName.Value, 120), value.IsCompleted, "account")).ToArray();
        _snapshot = new ProgressSnapshot(DateTimeOffset.UtcNow,
            Array.AsReadOnly(_knowledge.Inventory.Select(item => new ProgressItem(item.ItemKey, item.Name, item.Stack, item.Location, item.CanUseForCrafting)).ToArray()),
            Array.AsReadOnly(_knowledge.OwnedChests.SelectMany(chest => chest.Items.Select(item => new RememberedProgressItem(item.ItemKey, item.Name,
                item.Stack, SafeChestLabel(chest.Label), chest.LastObservedUtc, chest.IsCurrentlyOpen))).ToArray()),
            Array.AsReadOnly(bosses), Array.AsReadOnly(worldProgress), Array.AsReadOnly(achievements),
            Array.AsReadOnly(_knowledge.KnownSubjects.Where(subject => subject.Kind == "biome").Select(subject => new ProgressSubject(subject.Key, subject.Name)).ToArray()),
            npcs.AsReadOnly(), Array.AsReadOnly(_knowledge.CurrentBiomes.ToArray()))
        {
            NpcHappiness = ledger.HappinessSnapshot(_currentHappinessKey),
            KnownChests = Array.AsReadOnly(_knowledge.OwnedChests
                .Where(chest => !string.IsNullOrWhiteSpace(chest.Label) && !chest.Label.StartsWith("Chest at tile ", StringComparison.OrdinalIgnoreCase))
                .Select(chest => new KnownProgressChest(SafeChestLabel(chest.Label), chest.LastObservedUtc)).ToArray())
        };
        RefreshNotes();
    }

    private void ObserveNpcHappiness()
    {
        WorldGoalLedger ledger = CurrentLedger;
        NPC? talked = Player.TalkNPC;
        // SetTalkNPC synchronously assigns currentShoppingSettings for the new NPC
        // before returning (verified against the installed 1.4.4 tML assembly).
        // Reading here/on chat submission runs on the game thread, after that call.
        // Never call GetShoppingSettings: it recomputes mood by inspecting residents.
        bool applicable = talked is { active: true, townNPC: true }
            && talked.type >= 0 && talked.type < NPCID.Sets.NoTownNPCHappiness.Length
            && !NPCID.Sets.NoTownNPCHappiness[talked.type]
            && talked.type < NPCID.Sets.IsTownPet.Length && !NPCID.Sets.IsTownPet[talked.type];
        ShoppingSettings settings = Player.currentShoppingSettings;
        if (!applicable || Main.remixWorld || !WorldGoalLedger.ValidHappinessPrice(settings.PriceAdjustment)
            || string.IsNullOrWhiteSpace(settings.HappinessReport))
        {
            if (_currentHappinessKey is not null)
            {
                ClearCurrentHappiness();
                UpdateHappinessSnapshot(ledger);
            }
            return;
        }

        if (ReferenceEquals(talked, _observedHappinessNpc) && talked!.type == _observedHappinessType
            && settings.PriceAdjustment == _observedHappinessPrice && settings.HappinessReport == _observedHappinessReport)
            return;
        ClearCurrentHappiness();
        string key = GoalPlanner.CleanText(NpcKey(talked!), 128);
        if (ledger.RememberHappiness(key, talked!.TypeName, settings.PriceAdjustment, settings.HappinessReport,
            DateTimeOffset.UtcNow, ObservedPlayerBiomes()))
        {
            _observedHappinessNpc = talked;
            _observedHappinessType = talked.type;
            _observedHappinessPrice = settings.PriceAdjustment;
            _observedHappinessReport = settings.HappinessReport;
            _currentHappinessKey = key;
            ledger.TalkedNpcs[key] = new ProgressSubject(key, GoalPlanner.CleanText(talked.TypeName, 100));
        }
        UpdateHappinessSnapshot(ledger);
    }

    private void UpdateHappinessSnapshot(WorldGoalLedger ledger)
    {
        if (_snapshot is not null) _snapshot = _snapshot with
        { CapturedAtUtc = DateTimeOffset.UtcNow, NpcHappiness = ledger.HappinessSnapshot(_currentHappinessKey) };
    }

    private void ClearCurrentHappiness()
    {
        if (_currentHappinessKey is not null && _snapshot is not null)
            _snapshot = _snapshot with { CapturedAtUtc = DateTimeOffset.UtcNow, NpcHappiness = Array.AsReadOnly(_snapshot.NpcHappiness
                .Select(value => value with { IsCurrent = false }).ToArray()) };
        _observedHappinessNpc = null;
        _observedHappinessType = 0;
        _observedHappinessPrice = 0;
        _observedHappinessReport = null;
        _currentHappinessKey = null;
    }

    private IReadOnlyList<string> ObservedPlayerBiomes()
    {
        var biomes = new List<string>();
        void Add(bool active, string name) { if (active) biomes.Add(name); }
        Add(Player.ZoneForest, "Forest"); Add(Player.ZoneDesert, "Desert");
        Add(Player.ZoneUndergroundDesert, "Underground Desert"); Add(Player.ZoneJungle, "Jungle");
        Add(Player.ZoneSnow, "Snow"); Add(Player.ZoneBeach, "Ocean");
        Add(Player.ZoneCorrupt, "Corruption"); Add(Player.ZoneCrimson, "Crimson");
        Add(Player.ZoneHallow, "Hallow"); Add(Player.ZoneDungeon, "Dungeon");
        Add(Player.ZoneGlowshroom, "Glowing Mushroom"); Add(Player.ZoneMeteor, "Meteorite");
        Add(Player.ZoneGraveyard, "Graveyard"); Add(Player.ZoneShimmer, "Shimmer");
        Add(Player.ZoneSkyHeight, "Space"); Add(Player.ZoneDirtLayerHeight, "Underground");
        Add(Player.ZoneRockLayerHeight, "Cavern"); Add(Player.ZoneUnderworldHeight, "Underworld");
        return biomes;
    }

    private static string SafeChestLabel(string label) => label.StartsWith("Chest at tile ", StringComparison.Ordinal)
        ? "Previously opened chest" : GoalPlanner.CleanText(label, 100);

    private static string NpcKey(NPC npc)
    {
        if (npc.ModNPC is not null) return npc.ModNPC.FullName;
        return npc.type == NPCID.DD2Bartender ? "Terraria/Tavernkeep" : "Terraria/" + NPCID.Search.GetName(npc.type);
    }

    private void RefreshNotes()
    {
        if (_snapshot is null || _knowledge is null) return;
        foreach (GoalNote note in CurrentLedger.Notes) GoalPlanner.Refresh(note, _snapshot, _knowledge);
    }

    private static ProgressSubject? ResolveItem(string key)
    {
        Item? item = FindItem(key);
        return item is null ? null : new ProgressSubject(GameReferenceFacts.Key(item), item.Name);
    }
    private static Item? FindItem(string key)
    {
        Item? exactKey = ContentSamples.ItemsByType.Values.FirstOrDefault(item => !item.IsAir
            && string.Equals(GameReferenceFacts.Key(item), key, StringComparison.OrdinalIgnoreCase));
        if (exactKey is not null) return exactKey;
        Item[] names = ContentSamples.ItemsByType.Values.Where(item => !item.IsAir
            && string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        // Two mods may give different items the same display name. Only a unique name is safe to automate.
        return names.Length == 1 ? names[0] : null;
    }

    private static IReadOnlyList<RecipeReferenceSnapshot> RecipesFor(string key)
    {
        Item? item = FindItem(key);
        return item is null ? Array.Empty<RecipeReferenceSnapshot>() : GameReferenceFacts.BuildRecipes(new[] { item });
    }

    private void ValidateSavedRecipes()
    {
        foreach (GoalNote note in CurrentLedger.Notes)
        foreach (GoalStepState craft in note.MutableSteps.Where(step => step.Kind == "craft" && step.Recipe is not null))
        {
            RecipeReferenceSnapshot? matching = RecipesFor(craft.Key).FirstOrDefault(recipe => RecipeIdentity(recipe) == RecipeIdentity(craft.Recipe!));
            if (matching is not null) { craft.Recipe = matching; continue; }
            craft.Recipe = null;
            foreach (GoalStepState ingredient in note.MutableSteps.Where(step => step.Kind == "ingredient" && step.CraftGroup == craft.CraftGroup))
            {
                ingredient.Kind = "manual";
                ingredient.Target = 1;
                ingredient.Detail = "Installed recipe changed or was removed. Recreate this goal for current ingredient counts.";
                ingredient.IsComplete = craft.Acquired;
            }
        }
    }

    private static string RecipeIdentity(RecipeReferenceSnapshot recipe) => recipe.ResultKey + ":" + recipe.ResultStack + ":"
        + string.Join(";", recipe.Ingredients.Select(value => value.Stack + "=" + string.Join(",", value.AcceptedItemKeys.OrderBy(key => key, StringComparer.Ordinal))))
        + ":" + string.Join(";", recipe.Stations) + ":" + string.Join(";", recipe.Conditions);
}
