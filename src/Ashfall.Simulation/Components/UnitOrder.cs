using Ashfall.Core;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Orders;

namespace Ashfall.Simulation.Components;

/// <summary>What the player last told a unit to do.</summary>
public record struct UnitOrder(OrderKind Kind, Entity Target, FixVec2 Destination) : IStateHashable
{
    public static UnitOrder Idle => new(OrderKind.Idle, Entity.None, FixVec2.Zero);

    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add((int)Kind);
        hasher.Add(Target.Index);
        hasher.Add(Target.Generation);
        hasher.Add(Destination);
    }
}
