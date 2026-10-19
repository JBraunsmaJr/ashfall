namespace Ashfall.Simulation.Horde;

/// <summary>
/// How the Maw spawns in a match: from nests placed with <see cref="GameSimulation.PlaceNest"/>, which feed on
/// Cindrite, and (unless <see cref="EdgeSpawns"/> is off) from the map edges, topping the population up for the
/// swarm stress test. With <see cref="WaveIntervalTicks"/> set, the director (GDD section 3) holds what the nests
/// breed back and sends it in telegraphed waves.
/// </summary>
public sealed record HordeSettings
{
    /// <summary>Index into <see cref="Content.GameContent.Creatures"/> of the creature to spawn.</summary>
    public required int CreatureType { get; init; }

    /// <summary>
    /// The most creatures the Maw has alive at once. The edge spawner tops the swarm back up to this many as they die;
    /// nests stop spawning while it is reached.
    /// </summary>
    public required int Population { get; init; }

    /// <summary>Whether creatures also appear at the map edges (the swarm stress test) or only from nests.</summary>
    public bool EdgeSpawns { get; init; } = true;

    /// <summary>Most creatures spawned in one tick.</summary>
    public int SpawnPerTick { get; init; } = 20;

    /// <summary>The tick the first creatures arrive (from the edges or from nests; nests feed before then).</summary>
    public long FirstSpawnTick { get; init; }

    /// <summary>Creatures appear within this many cells of the map edge.</summary>
    public int EdgeDepth { get; init; } = 3;

    /// <summary>Creatures never appear closer than this many cells' walk to a player structure.</summary>
    public int MinimumDistance { get; init; } = 12;

    /// <summary>
    /// 0 for no director: every creature a nest breeds marches at once. Otherwise what the nests breed broods around
    /// them, guarding them, until the director sends it, and this is the longest the director waits between assaults
    /// (it calls one sooner once the brood is three-quarters grown).
    /// </summary>
    public int WaveIntervalTicks { get; init; }

    /// <summary>
    /// With a director: the brood the first assault may grow to, before <see cref="DirectorProfile.BudgetPercent"/>.
    /// Each assault raises it by <see cref="WaveGrowth"/>, never past <see cref="Population"/>, so assaults escalate.
    /// </summary>
    public int FirstWaveSize { get; init; } = 40;

    /// <summary>With a director: how much each assault raises the brood the next may grow to.</summary>
    public int WaveGrowth { get; init; } = 30;

    /// <summary>With a director: how hard it pushes.</summary>
    public MawDifficulty Difficulty { get; init; } = MawDifficulty.Veteran;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(CreatureType, nameof(CreatureType));
        ArgumentOutOfRangeException.ThrowIfNegative(Population, nameof(Population));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SpawnPerTick, nameof(SpawnPerTick));
        ArgumentOutOfRangeException.ThrowIfNegative(FirstSpawnTick, nameof(FirstSpawnTick));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(EdgeDepth, nameof(EdgeDepth));
        ArgumentOutOfRangeException.ThrowIfNegative(MinimumDistance, nameof(MinimumDistance));
        ArgumentOutOfRangeException.ThrowIfNegative(WaveIntervalTicks, nameof(WaveIntervalTicks));
        if (!Enum.IsDefined(Difficulty))
        {
            throw new ArgumentOutOfRangeException(nameof(Difficulty), Difficulty, "Unknown difficulty.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(FirstWaveSize, nameof(FirstWaveSize));
        ArgumentOutOfRangeException.ThrowIfNegative(WaveGrowth, nameof(WaveGrowth));
    }
}
