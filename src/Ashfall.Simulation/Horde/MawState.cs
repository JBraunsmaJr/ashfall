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
        NextWaveTick = settings.WaveIntervalTicks > 0 ? settings.FirstSpawnTick + settings.WaveIntervalTicks : -1;
    }

    public HordeSettings Settings { get; }

    public DeterministicRandom Random { get; }

    /// <summary>Creatures spawned so far.</summary>
    public long Spawned { get; internal set; }

    /// <summary>Creatures killed so far.</summary>
    public long Killed { get; internal set; }

    /// <summary>Highest number of creatures alive at once.</summary>
    public int PeakAlive { get; internal set; }

    /// <summary>Whether nests' creatures brood until a wave is launched (see <see cref="HordeSettings.WaveIntervalTicks"/>).</summary>
    public bool HasWaves => Settings.WaveIntervalTicks > 0;

    /// <summary>The tick the next wave is launched, or -1 in a match without waves.</summary>
    public long NextWaveTick { get; internal set; }

    /// <summary>Waves launched so far.</summary>
    public int Waves { get; internal set; }

    /// <summary>
    /// The most creatures the nests may have alive now: <see cref="HordeSettings.Population"/>, or with waves the
    /// escalating limit for the wave now brooding.
    /// </summary>
    public int PopulationLimit
    {
        get
        {
            if (!HasWaves)
            {
                return Settings.Population;
            }

            var limit = Settings.FirstWaveSize + ((long)Settings.WaveGrowth * Waves);
            return limit < Settings.Population ? (int)limit : Settings.Population;
        }
    }

    /// <summary>True during the warning window before a wave (for the client's telegraph).</summary>
    public bool IsWaveImminent(long tick) => HasWaves && NextWaveTick - tick <= Settings.WaveWarningTicks && NextWaveTick >= tick;

    public void HashInto(ref StateHasher hasher)
    {
        var random = Random.Snapshot;
        hasher.Add(random.State);
        hasher.Add(random.Increment);
        hasher.Add(Spawned);
        hasher.Add(Killed);
        hasher.Add(PeakAlive);
        if (HasWaves)
        {
            // Only matches with waves hash the wave clock, so the swarm test hashes as it always has.
            hasher.Add(NextWaveTick);
            hasher.Add(Waves);
        }
    }
}
