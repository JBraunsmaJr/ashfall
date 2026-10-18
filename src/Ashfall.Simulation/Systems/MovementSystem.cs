using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Walks every moving entity along its planned waypoints at its speed, stopping exactly on each one.
/// If the next step would enter a blocked cell (a structure was built in the way), the route is re-planned.
/// </summary>
public sealed class MovementSystem : ISystem
{
    public void Update(in TickContext context)
    {
        var movers = context.World.Store<Mover>();
        var positions = context.World.Store<Position>();
        var paths = context.Navigator.Paths;
        var entityIndices = movers.EntityIndices;
        var moverSpan = movers.Components;

        for (var i = 0; i < moverSpan.Length; i++)
        {
            ref var mover = ref moverSpan[i];
            if (!mover.HasTarget)
            {
                continue;
            }

            var index = entityIndices[i];
            var waypoints = paths.For(index);
            if (mover.NextWaypoint >= waypoints.Count)
            {
                mover.HasTarget = false;
                continue;
            }

            ref var position = ref positions.Get(index);
            var waypoint = waypoints[mover.NextWaypoint];
            var toWaypoint = waypoint - position.Value;
            var step = mover.SpeedPerTick;

            if (toWaypoint.LengthSquared <= step * step)
            {
                position.Value = waypoint;
                mover.NextWaypoint++;
                if (mover.NextWaypoint >= waypoints.Count)
                {
                    mover.HasTarget = false;
                    waypoints.Clear();
                    mover.NextWaypoint = 0;
                }

                continue;
            }

            var next = position.Value + (toWaypoint.Normalized() * step);
            if (!context.Map.IsPassable(Cell.Containing(next)))
            {
                context.Navigator.PlanRoute(index, position.Value, mover.Target, ref mover);
                continue;
            }

            position.Value = next;
        }
    }
}
