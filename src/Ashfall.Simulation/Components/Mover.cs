using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>
/// Lets an entity travel to <see cref="Target"/>. The route's waypoints live in the simulation's
/// <see cref="Navigation.PathStore"/>; <see cref="NextWaypoint"/> is the one being walked toward.
/// </summary>
public record struct Mover(Fix64 SpeedPerTick, FixVec2 Target, bool HasTarget, int NextWaypoint = 0) : IStateHashable
{
    public static Mover Idle(Fix64 speedPerTick) => new(speedPerTick, FixVec2.Zero, false);

    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(SpeedPerTick);
        hasher.Add(Target);
        hasher.Add(HasTarget);
        hasher.Add(NextWaypoint);
    }
}
