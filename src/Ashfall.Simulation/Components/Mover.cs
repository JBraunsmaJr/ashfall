using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>Lets an entity travel toward a target point. M0 moves in a straight line; pathfinding arrives in M1.</summary>
public record struct Mover(Fix64 SpeedPerTick, FixVec2 Target, bool HasTarget) : IStateHashable
{
    public static Mover Idle(Fix64 speedPerTick) => new(speedPerTick, FixVec2.Zero, false);

    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(SpeedPerTick);
        hasher.Add(Target);
        hasher.Add(HasTarget);
    }
}
