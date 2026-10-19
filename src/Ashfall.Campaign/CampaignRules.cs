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
