using Ashfall.Core;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Orders;

namespace Ashfall.Simulation.Components;

/// <summary>A harvester's cargo and where it is in the field-to-refinery loop.</summary>
public record struct Harvester(int Cargo, HarvestState State, Cell Field, Entity Refinery, int Timer) : IStateHashable
{
    public static Harvester Empty => new(0, HarvestState.Idle, new Cell(-1, -1), Entity.None, 0);

    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(Cargo);
        hasher.Add((int)State);
        hasher.Add(Field.X);
        hasher.Add(Field.Y);
        hasher.Add(Refinery.Index);
        hasher.Add(Refinery.Generation);
        hasher.Add(Timer);
    }
}
