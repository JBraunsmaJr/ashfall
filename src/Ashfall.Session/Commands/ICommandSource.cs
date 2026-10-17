using Ashfall.Simulation;
using Ashfall.Simulation.Commands;

namespace Ashfall.Session.Commands;

/// <summary>
/// Anything that issues orders: local input, an AI player, a scripted test, or (from M3) remote peers.
/// Every source talks to the simulation only through <see cref="Command"/>s.
/// </summary>
public interface ICommandSource
{
    /// <summary>Appends the commands this source issues for <paramref name="tick"/>.</summary>
    /// <param name="simulation">Read-only view of the current state. Sources must not modify it.</param>
    void CollectCommands(GameSimulation simulation, long tick, List<Command> output);
}
