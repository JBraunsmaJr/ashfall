using Ashfall.Core;

namespace Ashfall.Simulation.Horde;

/// <summary>
/// The director's memory (GDD section 3), kept by <see cref="Systems.DirectorSystem"/>: the wave being telegraphed,
/// the breathing room after the last assault, when the next probe or raid may go, and when each player was last hit.
/// Simulation state: hashed with the Maw in matches that have waves.
/// </summary>
public sealed class MawDirector
{
    private readonly long[] _lastTargeted;

    internal MawDirector(HordeSettings settings, int players)
    {
        var profile = DirectorProfile.For(settings.Difficulty);
        Profile = settings.TelegraphBonusTicks > 0 ? profile with { TelegraphTicks = profile.TelegraphTicks + settings.TelegraphBonusTicks } : profile;
        _lastTargeted = new long[players];
        Array.Fill(_lastTargeted, -1);

        // The first assault may not come before half an interval of brooding; probes start one harassment interval in.
        CooldownUntil = settings.FirstSpawnTick + (settings.WaveIntervalTicks / 2);
        LastAssaultTick = settings.FirstSpawnTick;
        NextHarassTick = settings.FirstSpawnTick + Profile.HarassIntervalTicks;
    }

    public DirectorProfile Profile { get; }

    /// <summary>The wave being telegraphed, or null while the director waits.</summary>
    public WaveOrder? Pending { get; internal set; }

    /// <summary>No assault is called before this tick (breathing room after the last).</summary>
    public long CooldownUntil { get; internal set; }

    /// <summary>The tick the last assault went (the grace period's end before the first).</summary>
    public long LastAssaultTick { get; internal set; }

    /// <summary>No probe or raid is called before this tick.</summary>
    public long NextHarassTick { get; internal set; }

    /// <summary>Assaults launched: each raises the brood the next may grow to.</summary>
    public int Assaults { get; internal set; }

    /// <summary>Waves of every kind launched.</summary>
    public int Waves { get; internal set; }

    /// <summary>The tick a wave last went for <paramref name="player"/>, or -1 if none has.</summary>
    public long LastTargeted(int player) => _lastTargeted[player];

    internal void MarkTargeted(int player, long tick) => _lastTargeted[player] = tick;

    public void HashInto(ref StateHasher hasher)
    {
        hasher.Add(Pending.HasValue);
        if (Pending is { } pending)
        {
            pending.AddTo(ref hasher);
        }
        hasher.Add(CooldownUntil);
        hasher.Add(LastAssaultTick);
        hasher.Add(NextHarassTick);
        hasher.Add(Assaults);
        hasher.Add(Waves);
        foreach (var tick in _lastTargeted)
        {
            hasher.Add(tick);
        }
    }
}
