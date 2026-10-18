using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Orders;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Decides what every armed unit is shooting at and where it needs to be:
/// keeps explicit Attack targets, lets idle and attack-moving units pick the nearest enemy in
/// acquisition range, closes to weapon range, and resumes an attack-move when its target dies.
/// </summary>
public sealed class TargetingSystem : ISystem
{
    // Ticks between target scans and chase re-plans for one unit (staggered across units).
    private const int ScanPeriod = 8;
    private const int ChasePeriod = 10;

    public void Update(in TickContext context)
    {
        var world = context.World;
        var armaments = world.Store<Armament>();
        var indices = armaments.EntityIndices;
        var weapons = armaments.Components;
        for (var i = 0; i < weapons.Length; i++)
        {
            var unit = world.EntityAt(indices[i]);
            ref var armament = ref weapons[i];
            ref var order = ref world.Get<UnitOrder>(unit);
            ref var mover = ref world.Get<Mover>(unit);
            var position = world.Get<Position>(unit).Value;
            var definition = context.Content.Unit(world.Get<UnitInfo>(unit).UnitType);
            var owner = world.Get<Owner>(unit).PlayerId;

            if (!IsValidTarget(context, owner, armament.Target))
            {
                armament.Target = Entity.None;
                if (order.Kind == OrderKind.Attack)
                {
                    order = UnitOrder.Idle;
                    mover.HasTarget = false;
                }
            }

            if (order.Kind == OrderKind.Attack)
            {
                armament.Target = order.Target;
            }

            if (armament.Target.IsNone
                && order.Kind is OrderKind.Idle or OrderKind.AttackMove
                && context.IsStaggeredTick(unit.Index, ScanPeriod))
            {
                armament.Target = FindNearestEnemy(context, owner, position, definition.AcquisitionRange);
            }

            if (!armament.Target.IsNone)
            {
                Engage(context, unit, armament.Target, position, definition.Weapon!.Range, ref mover);
            }
            else if (order.Kind == OrderKind.AttackMove && !mover.HasTarget)
            {
                ContinueAttackMove(context, unit, position, ref order, ref mover);
            }
        }
    }

    /// <summary>True when <paramref name="point"/> is within <paramref name="range"/> of <paramref name="from"/>.</summary>
    public static bool InRange(FixVec2 from, FixVec2 point, Fix64 range) =>
        FixVec2.DistanceSquared(from, point) <= range * range;

    private static void Engage(in TickContext context, Entity unit, Entity target, FixVec2 position, Fix64 range, ref Mover mover)
    {
        var aimPoint = context.Simulation.ClosestPointOf(target, position);
        if (InRange(position, aimPoint, range))
        {
            if (mover.HasTarget)
            {
                mover.HasTarget = false;
                context.Navigator.Paths.Clear(unit.Index);
            }

            return;
        }

        if (!mover.HasTarget || context.IsStaggeredTick(unit.Index, ChasePeriod))
        {
            context.Navigator.PlanRoute(unit.Index, position, aimPoint, ref mover);
        }
    }

    private static void ContinueAttackMove(in TickContext context, Entity unit, FixVec2 position, ref UnitOrder order, ref Mover mover)
    {
        if (FixVec2.DistanceSquared(position, order.Destination) <= Fix64.One)
        {
            order = UnitOrder.Idle;
            return;
        }

        if (context.IsStaggeredTick(unit.Index, ChasePeriod)
            && !context.Navigator.PlanRoute(unit.Index, position, order.Destination, ref mover))
        {
            // Destination unreachable: hold here.
            order = UnitOrder.Idle;
        }
    }

    private static bool IsValidTarget(in TickContext context, int owner, Entity target) =>
        !target.IsNone
        && context.World.IsAlive(target)
        && context.World.Has<Health>(target)
        && context.Simulation.AreEnemies(owner, context.World.Get<Owner>(target).PlayerId);

    // Nearest enemy (closest point, then lowest entity index) within range; units before structures at equal distance.
    private static Entity FindNearestEnemy(in TickContext context, int owner, FixVec2 position, Fix64 range)
    {
        var world = context.World;
        var healths = world.Store<Health>();
        var owners = world.Store<Owner>();
        var indices = healths.EntityIndices;
        var best = Entity.None;
        var bestDistance = range * range;
        var bestIsStructure = true;
        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            if (!context.Simulation.AreEnemies(owner, owners.Get(index).PlayerId))
            {
                continue;
            }

            var candidate = world.EntityAt(index);
            var distance = FixVec2.DistanceSquared(position, context.Simulation.ClosestPointOf(candidate, position));
            var isStructure = world.Has<StructureInfo>(candidate);
            if (distance > bestDistance)
            {
                continue;
            }

            var better = best.IsNone
                || (bestIsStructure && !isStructure)
                || (bestIsStructure == isStructure && (distance < bestDistance || (distance == bestDistance && index < best.Index)));
            if (better)
            {
                best = candidate;
                bestDistance = distance;
                bestIsStructure = isStructure;
            }
        }

        return best;
    }
}
