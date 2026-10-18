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

    /// <summary>The tick the first creatures arrive.</summary>
    public long FirstSpawnTick { get; init; }

    /// <summary>Creatures appear within this many cells of the map edge.</summary>
    public int EdgeDepth { get; init; } = 3;

    /// <summary>Creatures never appear closer than this many cells' walk to a player structure.</summary>
    public int MinimumDistance { get; init; } = 12;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(CreatureType, nameof(CreatureType));
        ArgumentOutOfRangeException.ThrowIfNegative(Population, nameof(Population));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SpawnPerTick, nameof(SpawnPerTick));
        ArgumentOutOfRangeException.ThrowIfNegative(FirstSpawnTick, nameof(FirstSpawnTick));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(EdgeDepth, nameof(EdgeDepth));
        ArgumentOutOfRangeException.ThrowIfNegative(MinimumDistance, nameof(MinimumDistance));
    }
}
