using Ashfall.Core;
using Ashfall.Simulation;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;

namespace Ashfall.Session.Commands;

/// <summary>
/// M0 stand-in for an AI player: at a fixed interval, sends a few random owned units to random points.
/// Uses its own random stream, so it never disturbs the simulation's.
/// </summary>
public sealed class WanderOrdersSource : ICommandSource
{
    private readonly int _playerId;
    private readonly int _intervalTicks;
    private readonly int _ordersPerInterval;
    private readonly DeterministicRandom _random;
    private readonly List<int> _ownedScratch = [];

    public WanderOrdersSource(int playerId, ulong seed, int intervalTicks, int ordersPerInterval)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(ordersPerInterval);

        _playerId = playerId;
        _intervalTicks = intervalTicks;
        _ordersPerInterval = ordersPerInterval;
        _random = new DeterministicRandom(seed, stream: 1000UL + (ulong)playerId);
    }

    public void CollectCommands(GameSimulation simulation, long tick, List<Command> output)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(output);

        if (tick % _intervalTicks != 0 || _ordersPerInterval == 0)
        {
            return;
        }

        var owners = simulation.World.Store<Owner>();
        var indices = owners.EntityIndices;
        var components = owners.Components;
        _ownedScratch.Clear();
        for (var i = 0; i < indices.Length; i++)
        {
            if (components[i].PlayerId == _playerId)
            {
                _ownedScratch.Add(indices[i]);
            }
        }

        if (_ownedScratch.Count == 0)
        {
            return;
        }

        var bounds = simulation.Bounds;
        for (var order = 0; order < _ordersPerInterval; order++)
        {
            var unit = simulation.World.EntityAt(_ownedScratch[_random.Next(_ownedScratch.Count)]);
            var target = new FixVec2(
                _random.NextFix64(Fix64.Zero, Fix64.FromInt(bounds.Width)),
                _random.NextFix64(Fix64.Zero, Fix64.FromInt(bounds.Height)));
            output.Add(Command.Move(_playerId, unit, target));
        }
    }
}
