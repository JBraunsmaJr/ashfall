namespace Ashfall.Simulation;

/// <summary>Fixed parameters of the simulation. Changing any of these changes every replay and state hash.</summary>
public static class SimulationConstants
{
    /// <summary>Simulation ticks per second of game time at 1× speed (one tick = 66.7 ms).</summary>
    public const int TicksPerSecond = 15;

    /// <summary>Peers exchange state hashes every this many ticks (2 seconds of game time).</summary>
    public const int HashCheckInterval = 30;
}
