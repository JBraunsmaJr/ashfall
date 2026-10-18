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

    /// <summary>
    /// Co-op Purge (GDD section 2), the way Ashfall is meant to be played: 1–4 Bulwark players on one team, each with
    /// a Construction Yard on a generated map, against Maw nests breeding on the Cindrite fields. The players win when
    /// every nest is destroyed; the Maw wins when every player has lost all their structures.
    /// </summary>
    Purge,
}
