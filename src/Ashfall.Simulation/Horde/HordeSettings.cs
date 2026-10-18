namespace Ashfall.Simulation.Horde;

/// <summary>
/// How the Maw spawns in a match. M5's first step: keep a population of one creature type alive, arriving from
/// the map edges. Nests, biomass and the director's waves (GDD section 3) replace this as they land.
/// </summary>
public sealed record HordeSettings
{
    /// <summary>Index into <see cref="Content.GameContent.Creatures"/> of the creature to spawn.</summary>
    public required int CreatureType { get; init; }

    /// <summary>Creatures the spawner keeps alive: it tops the swarm back up to this many as they die.</summary>
    public required int Population { get; init; }

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
