namespace Ashfall.Session;

/// <summary>What kind of match a <see cref="MatchSpec"/> describes.</summary>
public enum MatchMode
{
    /// <summary>Open map, bare units wandering at random. No economy, no victory. Proves determinism.</summary>
    ProvingGround,

    /// <summary>Duel map, Bulwark bases, scripted AI on both sides, played until one side has no structures.</summary>
    Skirmish,

    /// <summary>
    /// The M5 horde stress test: a Bulwark fortress with idle defenders on an open map, and a Maw spawner that keeps
    /// <see cref="MatchSpec.Creatures"/> alive. No victory; runs for a fixed tick count. Proves the swarm is
    /// deterministic and fits the tick budget.
    /// </summary>
    Swarm,
}
