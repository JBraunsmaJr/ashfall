namespace Ashfall.Session;

/// <summary>What kind of match a <see cref="MatchSpec"/> describes.</summary>
public enum MatchMode
{
    /// <summary>Open map, bare units wandering at random. No economy, no victory. Proves determinism.</summary>
    ProvingGround,

    /// <summary>Duel map, Bulwark bases, scripted AI on both sides, played until one side has no structures.</summary>
    Skirmish,
}
