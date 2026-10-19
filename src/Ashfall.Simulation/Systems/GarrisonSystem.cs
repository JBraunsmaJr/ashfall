using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Orders;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Takes infantry into bunkers: a unit ordered to enter walks to the bunker's door and, once beside it, goes inside if
/// there is room (otherwise it waits outside, idle). Inside it stands at the bunker's middle, cannot be seen or hit,
/// takes no orders, and fires out at enemies within its weapon's range of the bunker's walls plus
/// <see cref="RangeBonus"/>. Unloading, or the bunker falling, puts everyone back outside.
/// </summary>
public sealed class GarrisonSystem : ISystem
{
    /// <summary>How much further a unit reaches from inside a bunker than in the open, in cells.</summary>
    public static readonly Fix64 RangeBonus = Fix64.One;

    // How close to a bunker's footprint a unit must be to step in.
    private static readonly Fix64 EnterReach = Fix64.FromRatio(3, 2);

    private const int ReplanPeriod = 15;

    private readonly List<int> _entering = [];

    public void Update(in TickContext context)
    {
        var world = context.World;
        var simulation = context.Simulation;
        var orders = world.Store<UnitOrder>();
        _entering.Clear();
        foreach (var index in orders.EntityIndices)
        {
            if (orders.Get(index).Kind == OrderKind.Enter)
            {
                _entering.Add(index);
            }
        }

        foreach (var index in _entering)
        {
            var unit = world.EntityAt(index);
            ref var order = ref world.Get<UnitOrder>(unit);
            ref var mover = ref world.Get<Mover>(unit);
            var bunker = order.Target;
            if (!simulation.IsBunkerOf(bunker, world.Get<Owner>(unit).PlayerId))
            {
                order = UnitOrder.Idle;
                continue;
            }

            var position = world.Get<Position>(unit).Value;
            if (TargetingSystem.InRange(position, simulation.ClosestPointOf(bunker, position), EnterReach))
            {
                order = UnitOrder.Idle;
                mover.HasTarget = false;
                context.Navigator.Paths.Clear(index);
                var room = context.Content.Structure(world.Get<StructureInfo>(bunker).StructureType).Garrison;
                if (simulation.OccupantsOf(bunker) < room)
                {
                    world.Add(unit, new Garrisoned(bunker));
                    world.Get<Position>(unit).Value = world.Get<Position>(bunker).Value;
                    if (world.Has<Armament>(unit))
                    {
                        ref var armament = ref world.Get<Armament>(unit);
                        armament.Target = Entity.None;
                        armament.Guarding = false;
                        armament.Returning = false;
                    }
                }

                continue;
            }

            if (!mover.HasTarget && context.IsStaggeredTick(index, ReplanPeriod))
            {
                var door = simulation.ExitCell(world.Get<StructureInfo>(bunker)).Center;
                context.Navigator.PlanRoute(index, position, door, ref mover);
            }
        }
    }
}
