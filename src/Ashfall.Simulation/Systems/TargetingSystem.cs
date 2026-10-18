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

    // Scratch for target scans: enemies within range, sorted back into Health-store order.
    private readonly List<Candidate> _candidates = [];

    public void Update(in TickContext context)
    {
        var world = context.World;
        context.Simulation.Spatial.Rebuild(world);
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
                armament.Target = FindNearestEnemy(context, _candidates, owner, position, definition.AcquisitionRange);
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

    /// <summary>
    /// The enemy an idle unit at <paramref name="position"/> engages: within <paramref name="range"/>, preferring
    /// units to structures, then the nearest (closest point of a structure's footprint), then the lowest entity index.
    /// </summary>
    /// <remarks>
    /// The rule is applied as a fold over candidates in <see cref="Health"/>-store order, and a structure met
    /// first can hide a farther unit, so the result depends on that order. Candidates come from the spatial grid
    /// (units and creatures near the scanner) and the structure list, then are sorted back into store order, so
    /// the answer is exactly what a scan of every entity would give, at a fraction of the cost.
    /// </remarks>
    internal static Entity FindNearestEnemy(in TickContext context, List<Candidate> candidates, int owner, FixVec2 position, Fix64 range)
    {
        var world = context.World;
        var simulation = context.Simulation;
        var healths = world.Store<Health>();
        var owners = world.Store<Owner>();
        var rangeSquared = range * range;
        candidates.Clear();

        var structures = world.Store<StructureInfo>();
        foreach (var index in structures.EntityIndices)
        {
            if (!simulation.AreEnemies(owner, owners.Get(index).PlayerId))
            {
                continue;
            }

            var distance = FixVec2.DistanceSquared(position, simulation.ClosestPointOf(world.EntityAt(index), position));
            if (distance <= rangeSquared)
            {
                candidates.Add(new Candidate(healths.SlotOf(index), index, distance, IsStructure: true));
            }
        }

        var grid = simulation.Spatial;
        var positions = world.Store<Position>();
        var (minX, minY, maxX, maxY) = grid.CellsAround(position, range);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                foreach (var index in grid.At(x, y))
                {
                    if (!simulation.AreEnemies(owner, owners.Get(index).PlayerId))
                    {
                        continue;
                    }

                    var distance = FixVec2.DistanceSquared(position, positions.Get(index).Value);
                    if (distance <= rangeSquared)
                    {
                        candidates.Add(new Candidate(healths.SlotOf(index), index, distance, IsStructure: false));
                    }
                }
            }
        }

        candidates.Sort(static (a, b) => a.Slot.CompareTo(b.Slot));

        var best = Entity.None;
        var bestDistance = rangeSquared;
        var bestIsStructure = true;
        foreach (var candidate in candidates)
        {
            if (candidate.Distance > bestDistance)
            {
                continue;
            }

            var better = best.IsNone
                || (bestIsStructure && !candidate.IsStructure)
                || (bestIsStructure == candidate.IsStructure
                    && (candidate.Distance < bestDistance || (candidate.Distance == bestDistance && candidate.Index < best.Index)));
            if (better)
            {
                best = world.EntityAt(candidate.Index);
                bestDistance = candidate.Distance;
                bestIsStructure = candidate.IsStructure;
            }
        }

        return best;
    }

    /// <summary>An enemy within range: its place in Health-store order, entity index, squared distance and kind.</summary>
    internal readonly record struct Candidate(int Slot, int Index, Fix64 Distance, bool IsStructure);
}
