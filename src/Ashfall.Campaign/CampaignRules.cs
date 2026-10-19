using Ashfall.Core;
using Ashfall.Simulation.Horde;

namespace Ashfall.Campaign;

/// <summary>The campaign events of GDD section 7: each demands a mission, and an unanswered one expires with a consequence.</summary>
public enum CampaignEventKind
{
    /// <summary>Purge a fresh crater before nests take root. Ignored: the region jumps to infestation 3.</summary>
    MeteorStrike,

    /// <summary>Intercept a column of behemoths. Ignored: they reach a liberated region and reinfest it.</summary>
    BehemothMigration,

    /// <summary>Hold a liberated region. Ignored: the region is lost.</summary>
    OutpostSiege,

    /// <summary>Purge a new hive before it evolves. Ignored: a new hive appears in the theatre.</summary>
    HiveEmergence,

    /// <summary>Escort survivors across a map. Ignored: a pending research reward is lost.</summary>
    SurvivorConvoy,

    /// <summary>Defend Kindred defectors (needs the Kindred, M9; never rolled before then).</summary>
    KindredSchism,

    /// <summary>Retake a Lattice node (needs the Lattice, M9; never rolled before then).</summary>
    LatticeBeacon,
}

/// <summary>What a research project does for every mission after it (GDD section 7, "Research").</summary>
public enum ResearchEffect
{
    /// <summary>+Amount% damage for every player's units and defences.</summary>
    Damage,

    /// <summary>+Amount% hit points for every player's units.</summary>
    UnitHealth,

    /// <summary>+Amount% hit points for every player's structures.</summary>
    StructureHealth,

    /// <summary>+Amount% hit points for harvesters.</summary>
    HarvesterHealth,

    /// <summary>Repairs cost Amount% less.</summary>
    RepairCost,

    /// <summary>+Amount starting credits for every player.</summary>
    Credits,

    /// <summary>+Amount starting harvesters for every player.</summary>
    Harvesters,

    /// <summary>Every nest is revealed at the start of a mission.</summary>
    RevealNests,

    /// <summary>+Amount ticks of warning before every telegraphed wave.</summary>
    Telegraph,
}

/// <summary>A research project: bought once with samples, for the whole team, once its prerequisite is done.</summary>
/// <param name="Requires">The project that must be done first, or null.</param>
public sealed record ResearchNode(string Id, string Name, string Description, int Cost, string? Requires, ResearchEffect Effect, int Amount);

/// <summary>How often an event comes up and how long it waits for an answer.</summary>
/// <param name="Weight">Relative chance among the events that have a valid region; 0 never rolls it.</param>
/// <param name="Lifetime">Turns it stays on the map before it expires with its consequence.</param>
/// <param name="FirstTurn">The first turn it may appear on.</param>
public readonly record struct CampaignEventRule(CampaignEventKind Kind, int Weight, int Lifetime, int FirstTurn);

/// <summary>
/// The campaign rule-set (GDD section 14, <c>CampaignRules</c>): world size, starting spread, how fast the Maw spreads,
/// how often events come, the meter, and how a region becomes a mission. Every number is a starting value for
/// tuning with the campaign preview tool. A save records the rule-set's id and <see cref="ComputeHash"/>.
/// </summary>
public sealed record CampaignRules
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int MinRegions { get; init; } = 24;

    public int MaxRegions { get; init; } = 40;

    public int MinTheatres { get; init; } = 4;

    public int MaxTheatres { get; init; } = 6;

    /// <summary>No theatre is smaller than this; worlds are regenerated until it holds.</summary>
    public int MinRegionsPerTheatre { get; init; } = 5;

    /// <summary>No hive starts closer to home than this many borders.</summary>
    public int MinHiveDistance { get; init; } = 3;

    /// <summary>Percent chance per region of a city with survivors.</summary>
    public int SurvivorsPercent { get; init; } = 18;

    public int TechSitePercent { get; init; } = 10;

    public int CraterPercent { get; init; } = 8;

    public int KindredShrinePercent { get; init; } = 6;

    public int LatticeNodePercent { get; init; } = 6;

    /// <summary>Percent chance per turn that an infested region spreads, before its level and the difficulty are added.</summary>
    public int SpreadBasePercent { get; init; }

    /// <summary>Extra spread chance per infestation level of the spreading region.</summary>
    public int SpreadPerLevelPercent { get; init; } = 1;

    /// <summary>Extra spread chance for a hive region…</summary>
    public int HivePushPercent { get; init; } = 6;

    /// <summary>…growing by this much every ten turns of the campaign: hives push harder over time.</summary>
    public int HivePushPerTenTurnsPercent { get; init; } = 5;

    /// <summary>Extra spread chance, for the turn, from a region where a lost mission left nests standing.</summary>
    public int NestsStandingPercent { get; init; } = 25;

    /// <summary>Infestation levels a liberation takes off each infested neighbour (never below 1, never a hive).</summary>
    public int PushBackLevels { get; init; } = 1;

    /// <summary>Spread chance added per difficulty (Recruit, Veteran, Elite, Extinction).</summary>
    public IReadOnlyList<int> DifficultySpreadPercent { get; init; } = [-1, 0, 2, 4];

    /// <summary>Percent chance per turn of a new event, per difficulty.</summary>
    public IReadOnlyList<int> DifficultyEventPercent { get; init; } = [25, 35, 45, 55];

    /// <summary>
    /// How full the world must be for the meter to read 100%, as a percent of every region at infestation 5, per
    /// difficulty: a lower ceiling makes the meter climb faster.
    /// </summary>
    public IReadOnlyList<int> DifficultyMeterCeilingPercent { get; init; } = [95, 90, 85, 80];

    /// <summary>Samples earned per nest destroyed, by the tier the nests started at (index 0 unused).</summary>
    public IReadOnlyList<int> SamplesPerNest { get; init; } = [0, 2, 3, 5];

    /// <summary>Samples for winning a mission.</summary>
    public int SamplesForVictory { get; init; } = 5;

    /// <summary>Samples brought home by survivors: a liberated city or a convoy brought through.</summary>
    public int SamplesPerSurvivors { get; init; } = 6;

    /// <summary>Surviving units each player may keep for the next mission (GDD: up to 6).</summary>
    public int DetachmentSize { get; init; } = 6;

    /// <summary>The shared research tree. Ids are written in saves, so they never change once released.</summary>
    public IReadOnlyList<ResearchNode> Research { get; init; } =
    [
        new("weapons-1", "Hardened Rounds", "+10% damage for every unit and defence.", 20, null, ResearchEffect.Damage, 10),
        new("weapons-2", "Depleted Cores", "+10% more damage.", 45, "weapons-1", ResearchEffect.Damage, 10),
        new("armour-1", "Composite Plating", "+10% hit points for every unit.", 20, null, ResearchEffect.UnitHealth, 10),
        new("armour-2", "Reactive Plating", "+10% more hit points for every unit.", 45, "armour-1", ResearchEffect.UnitHealth, 10),
        new("fortify-1", "Reinforced Concrete", "+20% hit points for every structure.", 25, null, ResearchEffect.StructureHealth, 20),
        new("harvest-1", "Armoured Haulers", "+25% hit points for harvesters.", 15, null, ResearchEffect.HarvesterHealth, 25),
        new("repair-1", "Field Engineering", "Repairs cost 25% less.", 25, "fortify-1", ResearchEffect.RepairCost, 25),
        new("logistics-1", "Forward Supply", "+750 starting credits in every mission.", 20, null, ResearchEffect.Credits, 750),
        new("logistics-2", "Field Refineries", "An extra harvester at the start of every mission.", 40, "logistics-1", ResearchEffect.Harvesters, 1),
        new("intel-1", "Seismic Mapping", "Every nest is revealed when a mission starts.", 30, null, ResearchEffect.RevealNests, 1),
        new("intel-2", "Early Warning", "Five more seconds of warning before every wave.", 35, "intel-1", ResearchEffect.Telegraph, 75),
    ];

    /// <summary>Samples a mission earns: its nests destroyed, by starting tier, and a bonus for winning.</summary>
    public int SamplesFor(bool won, int nestsDestroyed, int nestTier) =>
        (nestsDestroyed * SamplesPerNest[nestTier < 1 ? 1 : nestTier > 3 ? 3 : nestTier]) + (won ? SamplesForVictory : 0);

    public ResearchNode? FindResearch(string id)
    {
        foreach (var node in Research)
        {
            if (node.Id == id)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>Most events on the map at once.</summary>
    public int MaxActiveEvents { get; init; } = 3;

    public IReadOnlyList<CampaignEventRule> Events { get; init; } =
    [
        new(CampaignEventKind.MeteorStrike, Weight: 3, Lifetime: 3, FirstTurn: 2),
        new(CampaignEventKind.BehemothMigration, Weight: 3, Lifetime: 2, FirstTurn: 4),
        new(CampaignEventKind.OutpostSiege, Weight: 3, Lifetime: 2, FirstTurn: 3),
        new(CampaignEventKind.HiveEmergence, Weight: 1, Lifetime: 4, FirstTurn: 8),
        new(CampaignEventKind.SurvivorConvoy, Weight: 2, Lifetime: 3, FirstTurn: 2),
        new(CampaignEventKind.KindredSchism, Weight: 0, Lifetime: 3, FirstTurn: 0),
        new(CampaignEventKind.LatticeBeacon, Weight: 0, Lifetime: 3, FirstTurn: 0),
    ];

    /// <summary>Spawning Pits in a region's mission by its infestation (index 0 is unused: liberated ground).</summary>
    public IReadOnlyList<int> NestsByInfestation { get; init; } = [2, 2, 3, 4, 6, 8];

    /// <summary>Nests guarding a hive.</summary>
    public int HiveNests { get; init; } = 10;

    /// <summary>Nest tier (1 Spawning Pit, 2 Brood Nest, 3 Behemoth Den) a region's nests start at, by infestation.</summary>
    public IReadOnlyList<int> NestTierByInfestation { get; init; } = [1, 1, 1, 2, 2, 3];

    /// <summary>Starting credits each player gets per liberated neighbour of the mission's region.</summary>
    public int CreditsPerLiberatedNeighbour { get; init; } = 500;

    /// <summary>Extra starting credits per liberated neighbour with survivors.</summary>
    public int CreditsPerSurvivorNeighbour { get; init; } = 1_000;

    /// <summary>Liberated neighbours per extra starting harvester…</summary>
    public int NeighboursPerHarvester { get; init; } = 2;

    /// <summary>…up to this many.</summary>
    public int MaxBonusHarvesters { get; init; } = 2;

    public int SpreadPercent(MawDifficulty difficulty) => DifficultySpreadPercent[(int)difficulty];

    public int EventPercent(MawDifficulty difficulty) => DifficultyEventPercent[(int)difficulty];

    public int MeterCeilingPercent(MawDifficulty difficulty) => DifficultyMeterCeilingPercent[(int)difficulty];

    public CampaignEventRule EventRule(CampaignEventKind kind)
    {
        foreach (var rule in Events)
        {
            if (rule.Kind == kind)
            {
                return rule;
            }
        }

        return new CampaignEventRule(kind, 0, 1, 0);
    }

    /// <summary>Throws if the rule-set cannot generate or run a campaign.</summary>
    public void Validate()
    {
        if (MinRegions < 8 || MaxRegions < MinRegions || MaxRegions > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(MinRegions), "A world has 8 to 64 regions.");
        }

        if (MinTheatres < 1 || MaxTheatres < MinTheatres || MinRegions < MinTheatres * MinRegionsPerTheatre)
        {
            throw new ArgumentOutOfRangeException(nameof(MinTheatres), "The smallest world must fit its theatres at their minimum size.");
        }

        var difficulties = Enum.GetValues<MawDifficulty>().Length;
        if (DifficultySpreadPercent.Count != difficulties || DifficultyEventPercent.Count != difficulties
            || DifficultyMeterCeilingPercent.Count != difficulties)
        {
            throw new ArgumentOutOfRangeException(nameof(DifficultySpreadPercent), "One value per difficulty.");
        }

        if (NestsByInfestation.Count != 6 || NestTierByInfestation.Count != 6)
        {
            throw new ArgumentOutOfRangeException(nameof(NestsByInfestation), "One value per infestation level 0-5.");
        }

        foreach (var ceiling in DifficultyMeterCeilingPercent)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(ceiling, 10, nameof(DifficultyMeterCeilingPercent));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(ceiling, 100, nameof(DifficultyMeterCeilingPercent));
        }

        if (SamplesPerNest.Count != 4)
        {
            throw new ArgumentOutOfRangeException(nameof(SamplesPerNest), "One value per nest tier 1-3 (and an unused 0).");
        }

        foreach (var node in Research)
        {
            if (node.Requires is { } requires && FindResearch(requires) is null)
            {
                throw new ArgumentOutOfRangeException(nameof(Research), node.Id, $"Requires the unknown project {requires}.");
            }
        }

        foreach (var tier in NestTierByInfestation)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1, nameof(NestTierByInfestation));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(tier, 3, nameof(NestTierByInfestation));
        }
    }

    /// <summary>A hash of every value in the rule-set: saves record it, and lobbies compare it.</summary>
    public ulong ComputeHash()
    {
        var hasher = default(StateHasher);
        hasher.Add(Id);
        hasher.Add(MinRegions);
        hasher.Add(MaxRegions);
        hasher.Add(MinTheatres);
        hasher.Add(MaxTheatres);
        hasher.Add(MinRegionsPerTheatre);
        hasher.Add(MinHiveDistance);
        hasher.Add(SurvivorsPercent);
        hasher.Add(TechSitePercent);
        hasher.Add(CraterPercent);
        hasher.Add(KindredShrinePercent);
        hasher.Add(LatticeNodePercent);
        hasher.Add(SpreadBasePercent);
        hasher.Add(SpreadPerLevelPercent);
        hasher.Add(HivePushPercent);
        hasher.Add(HivePushPerTenTurnsPercent);
        hasher.Add(NestsStandingPercent);
        hasher.Add(PushBackLevels);
        AddAll(ref hasher, DifficultySpreadPercent);
        AddAll(ref hasher, DifficultyEventPercent);
        AddAll(ref hasher, DifficultyMeterCeilingPercent);
        hasher.Add(MaxActiveEvents);
        hasher.Add(Events.Count);
        foreach (var rule in Events)
        {
            hasher.Add((int)rule.Kind);
            hasher.Add(rule.Weight);
            hasher.Add(rule.Lifetime);
            hasher.Add(rule.FirstTurn);
        }

        AddAll(ref hasher, NestsByInfestation);
        hasher.Add(HiveNests);
        AddAll(ref hasher, NestTierByInfestation);
        hasher.Add(CreditsPerLiberatedNeighbour);
        hasher.Add(CreditsPerSurvivorNeighbour);
        hasher.Add(NeighboursPerHarvester);
        hasher.Add(MaxBonusHarvesters);
        AddAll(ref hasher, SamplesPerNest);
        hasher.Add(SamplesForVictory);
        hasher.Add(SamplesPerSurvivors);
        hasher.Add(DetachmentSize);
        hasher.Add(Research.Count);
        foreach (var node in Research)
        {
            hasher.Add(node.Id);
            hasher.Add(node.Cost);
            hasher.Add(node.Requires);
            hasher.Add((int)node.Effect);
            hasher.Add(node.Amount);
        }

        return hasher.Value;
    }

    private static void AddAll(ref StateHasher hasher, IReadOnlyList<int> values)
    {
        hasher.Add(values.Count);
        foreach (var value in values)
        {
            hasher.Add(value);
        }
    }
}

/// <summary>The rule-sets this build knows. A campaign code names one by its position here, so the list is append-only.</summary>
public static class CampaignRuleSets
{
    /// <summary>The standard campaign: 24–40 regions in 4–6 theatres.</summary>
    public static CampaignRules Standard { get; } = new() { Id = "standard", Name = "Standard" };

    /// <summary>A short campaign for quick sessions and tests: 12–16 regions in 2 theatres.</summary>
    public static CampaignRules Quick { get; } = new()
    {
        Id = "quick",
        Name = "Quick",
        MinRegions = 12,
        MaxRegions = 16,
        MinTheatres = 2,
        MaxTheatres = 2,
        MinHiveDistance = 2,
    };

    public static IReadOnlyList<CampaignRules> All { get; } = [Standard, Quick];

    public static CampaignRules? Find(string id)
    {
        foreach (var rules in All)
        {
            if (string.Equals(rules.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return rules;
            }
        }

        return null;
    }

    public static int IndexOf(CampaignRules rules)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (ReferenceEquals(All[i], rules) || All[i].Id == rules?.Id)
            {
                return i;
            }
        }

        return -1;
    }
}
