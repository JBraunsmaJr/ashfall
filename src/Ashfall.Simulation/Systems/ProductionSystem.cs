using Ashfall.Simulation.Components;

namespace Ashfall.Simulation.Systems;

/// <summary>Trains queued units at completed production structures (slowed by low power).</summary>
public sealed class ProductionSystem : ISystem
{
    private readonly List<(int Structure, int UnitType)> _finished = [];

    public void Update(in TickContext context)
    {
        var producers = context.World.Store<Producer>();
        var structures = context.World.Store<StructureInfo>();
        var owners = context.World.Store<Owner>();
        var indices = producers.EntityIndices;
        var queues = producers.Components;
        _finished.Clear();

        for (var i = 0; i < queues.Length; i++)
        {
            ref var producer = ref queues[i];
            if (producer.IsIdle || !structures.Get(indices[i]).IsComplete)
            {
                continue;
            }

            var playerId = owners.Get(indices[i]).PlayerId;
            if (playerId >= 0 && playerId < context.Players.Count
                && context.Players[playerId].HasLowPower
                && context.Tick % context.Content.Economy.LowPowerSlowdown != 0)
            {
                continue;
            }

            producer.Remaining--;
            if (producer.Remaining > 0)
            {
                continue;
            }

            _finished.Add((indices[i], producer.Dequeue()));
            producer.Remaining = producer.IsIdle ? 0 : context.Content.Unit(producer.Current).BuildTicks;
        }

        foreach (var (structure, unitType) in _finished)
        {
            context.Simulation.SpawnUnitAt(context.World.EntityAt(structure), unitType);
        }
    }
}
