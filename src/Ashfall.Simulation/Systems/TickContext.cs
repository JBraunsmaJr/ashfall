using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Navigation;
using Ashfall.Simulation.Players;

namespace Ashfall.Simulation.Systems;

/// <summary>Everything a system may touch during one tick.</summary>
public readonly record struct TickContext(long Tick, GameSimulation Simulation)
{
    public World World => Simulation.World;

    public DeterministicRandom Random => Simulation.Random;

    public GridMap Map => Simulation.Map;

    public Navigator Navigator => Simulation.Navigator;

    public GameContent Content => Simulation.Content;

    public IReadOnlyList<PlayerState> Players => Simulation.Players;

    /// <summary>
    /// True on the ticks an entity should do expensive periodic work (target scans, re-planning).
    /// Spreads that work evenly across ticks instead of every unit doing it at once.
    /// </summary>
    public bool IsStaggeredTick(int entityIndex, int period) => (Tick + entityIndex) % period == 0;
}
