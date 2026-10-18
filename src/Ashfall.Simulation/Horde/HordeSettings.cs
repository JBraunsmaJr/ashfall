namespace Ashfall.Simulation.Horde;

/// <summary>
/// How the Maw spawns in a match: from nests placed with <see cref="GameSimulation.PlaceNest"/>, which feed on
/// Cindrite, and (unless <see cref="EdgeSpawns"/> is off) from the map edges, topping the population up for the
/// swarm stress test. The director's waves (GDD section 3) come later.
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
    /// Ticks between the Maw's waves, or 0 for none. With waves, what the nests breed broods around them, guarding
    /// them, until the next wave is launched: the first <see cref="WaveIntervalTicks"/> after
    /// <see cref="FirstSpawnTick"/>, then one every interval. Without, every creature marches as soon as it spawns.
    /// </summary>
    public int WaveIntervalTicks { get; init; }

    /// <summary>
    /// With waves: the most creatures alive while the first wave broods. Each wave launched raises the limit by
    /// <see cref="WaveGrowth"/>, never past <see cref="Population"/>, so the waves escalate.
    /// </summary>
    public int FirstWaveSize { get; init; } = 40;

    /// <summary>With waves: how much each launched wave raises the population limit.</summary>
    public int WaveGrowth { get; init; } = 30;

    /// <summary>Ticks of warning before a wave: the window in which clients telegraph it.</summary>
    public int WaveWarningTicks { get; init; } = 225;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(CreatureType, nameof(CreatureType));
        ArgumentOutOfRangeException.ThrowIfNegative(Population, nameof(Population));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SpawnPerTick, nameof(SpawnPerTick));
        ArgumentOutOfRangeException.ThrowIfNegative(FirstSpawnTick, nameof(FirstSpawnTick));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(EdgeDepth, nameof(EdgeDepth));
        ArgumentOutOfRangeException.ThrowIfNegative(MinimumDistance, nameof(MinimumDistance));
        ArgumentOutOfRangeException.ThrowIfNegative(WaveIntervalTicks, nameof(WaveIntervalTicks));
        ArgumentOutOfRangeException.ThrowIfNegative(WaveWarningTicks, nameof(WaveWarningTicks));
        ArgumentOutOfRangeException.ThrowIfNegative(FirstWaveSize, nameof(FirstWaveSize));
        ArgumentOutOfRangeException.ThrowIfNegative(WaveGrowth, nameof(WaveGrowth));
    }
}
