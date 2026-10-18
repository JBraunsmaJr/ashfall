using Ashfall.Core;

namespace Ashfall.Simulation.Horde;

/// <summary>
/// The Maw's side of the simulation: its settings, its own random stream (so the horde never perturbs the
/// match's main stream, and adding creatures cannot change how anything else plays) and running totals.
/// Simulation state: included in the state hash of matches that have a horde.
/// </summary>
public sealed class MawState
{
    /// <summary>Stream id for the Maw's random numbers ("MAW").</summary>
    public const ulong RandomStream = 0x4D_41_57;

    public MawState(ulong seed, HordeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        Settings = settings;
        Random = new DeterministicRandom(seed, RandomStream);
    }

    public HordeSettings Settings { get; }

    public DeterministicRandom Random { get; }

    /// <summary>Creatures spawned so far.</summary>
    public long Spawned { get; internal set; }

    /// <summary>Creatures killed so far.</summary>
    public long Killed { get; internal set; }

    /// <summary>Highest number of creatures alive at once.</summary>
    public int PeakAlive { get; internal set; }

    public void HashInto(ref StateHasher hasher)
    {
        var random = Random.Snapshot;
        hasher.Add(random.State);
        hasher.Add(random.Increment);
        hasher.Add(Spawned);
        hasher.Add(Killed);
        hasher.Add(PeakAlive);
    }
}
