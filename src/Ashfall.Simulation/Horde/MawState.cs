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

    public MawState(ulong seed, HordeSettings settings, int players = 1)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        Settings = settings;
        Random = new DeterministicRandom(seed, RandomStream);
        Director = settings.WaveIntervalTicks > 0 ? new MawDirector(settings, players) : null;
    }

    public HordeSettings Settings { get; }

    public DeterministicRandom Random { get; }

    /// <summary>Creatures spawned so far.</summary>
    public long Spawned { get; internal set; }

    /// <summary>Creatures killed so far.</summary>
    public long Killed { get; internal set; }

    /// <summary>Highest number of creatures alive at once.</summary>
    public int PeakAlive { get; internal set; }

    /// <summary>Whether nests' creatures brood until the director sends them (see <see cref="HordeSettings.WaveIntervalTicks"/>).</summary>
    public bool HasWaves => Director is not null;

    /// <summary>The director, in a match with waves; otherwise null.</summary>
    public MawDirector? Director { get; }

    /// <summary>
    /// How far the nests may breed now. Without a director: <see cref="HordeSettings.Population"/> creatures alive.
    /// With one: the brood the next assault may grow to (its wave size, scaled by the difficulty's budget), counting only
    /// the creatures still brooding, so the last wave's survivors do not hold the next back; the Maw never has more
    /// than <see cref="HordeSettings.Population"/> alive either way.
    /// </summary>
    public int PopulationLimit
    {
        get
        {
            if (Director is null)
            {
                return Settings.Population;
            }

            var limit = (Settings.FirstWaveSize + ((long)Settings.WaveGrowth * Director.Assaults)) * Director.Profile.BudgetPercent / 100;
            return limit < Settings.Population ? (int)limit : Settings.Population;
        }
    }

    public void HashInto(ref StateHasher hasher)
    {
        var random = Random.Snapshot;
        hasher.Add(random.State);
        hasher.Add(random.Increment);
        hasher.Add(Spawned);
        hasher.Add(Killed);
        hasher.Add(PeakAlive);
        // Only matches with a director hash it, so the swarm test hashes as it always has.
        Director?.HashInto(ref hasher);
    }
}
