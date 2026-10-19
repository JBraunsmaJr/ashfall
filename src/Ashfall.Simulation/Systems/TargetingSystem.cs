using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Orders;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Decides what every armed unit is shooting at and where it needs to be:
/// keeps explicit Attack targets; lets idle and attack-moving units pick the nearest enemy in acquisition range
/// (units and creatures before structures, and a structure they picked is dropped when a unit or creature comes into
/// range); turns an idle unit on whoever shoots it; closes to weapon range; and resumes an attack-move when its
/// target dies. A unit whose move has arrived is idle again.
/// </summary>
/// <remarks>
/// A unit on its own never picks, and gives up, an enemy it can neither shoot from where it stands nor walk to (one
/// in a pocket of ground its feet cannot reach): chasing it would only replan a route that never arrives.
/// </remarks>
/// <remarks>
/// An idle unit's <see cref="UnitStance"/> sets how far it goes on its own: <see cref="UnitStance.Guard"/> chases a
/// short way (<see cref="GuardLeash"/> beyond its acquisition range from where it stood) and walks back,
/// <see cref="UnitStance.HoldGround"/> never moves and only fires at what is in weapon range, and
/// <see cref="UnitStance.Free"/> pursues anywhere. Attack orders and attack-moves always pursue.
/// </remarks>
public sealed class TargetingSystem : ISystem
{
    /// <summary>Guard stance: how much further than its acquisition range a unit chases from where it stood.</summary>
    public static readonly Fix64 GuardLeash = Fix64.FromInt(2);

    // Ticks between target scans and chase re-plans for one unit (staggered across units).
    private const int ScanPeriod = 4;
    private const int ChasePeriod = 10;

    // A guard counts as back at its post this close to it.
    private static readonly Fix64 AtPostSquared = Fix64.FromRatio(1, 4);

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
            if (world.Has<StructureInfo>(unit) || world.Has<Garrisoned>(unit))
            {
                // Turrets and units in bunkers never move: they fire at what comes within reach.
                FireAtWill(context, unit, ref armament);
                continue;
            }

            ref var order = ref world.Get<UnitOrder>(unit);
            ref var mover = ref world.Get<Mover>(unit);
            var position = world.Get<Position>(unit).Value;
            var definition = context.Content.Unit(world.Get<UnitInfo>(unit).UnitType);
            var owner = world.Get<Owner>(unit).PlayerId;

            // A move that has arrived (or cannot go on) leaves the unit idle, watching for enemies again.
            if (order.Kind == OrderKind.Move && !mover.HasTarget)
            {
                order = UnitOrder.Idle;
            }

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

            var weapon = definition.Weapon!;
            var onItsOwn = order.Kind is OrderKind.Idle or OrderKind.AttackMove;
            var stance = order.Kind == OrderKind.AttackMove ? UnitStance.Free : armament.Stance;
            var reach = stance == UnitStance.HoldGround ? weapon.Range : definition.AcquisitionRange;
            ref var health = ref world.Get<Health>(unit);
            if (onItsOwn && !armament.Returning)
            {
                // Shot at: answer the attacker if it is something that fights back and the stance lets the unit reach it.
                var attacker = health.LastAttacker;
                if ((armament.Target.IsNone || world.Has<StructureInfo>(armament.Target))
                    && IsValidTarget(context, owner, attacker) && !world.Has<StructureInfo>(attacker)
                    && (stance != UnitStance.HoldGround || InRange(position, world.Get<Position>(attacker).Value, weapon.Range)))
                {
                    armament.Target = attacker;
                }

                if (!armament.Target.IsNone && !CanChase(context, position, armament.Target, weapon.Range))
                {
                    armament.Target = Entity.None; // out of range and out of reach: let it go
                }

                if (context.IsStaggeredTick(unit.Index, ScanPeriod) && (armament.Target.IsNone || world.Has<StructureInfo>(armament.Target)))
                {
                    // A building it picked for itself is dropped as soon as something that fights back comes into range.
                    var found = FindNearestEnemy(context, _candidates, owner, position, reach, structures: armament.Target.IsNone,
                        walker: stance == UnitStance.HoldGround ? null : weapon.Range);
                    if (!found.IsNone || armament.Target.IsNone)
                    {
                        armament.Target = found;
                    }
                }
            }

            health.LastAttacker = Entity.None;

            // Guard: remember the post when leaving it to fight, and give up a chase that strays too far from it.
            if (order.Kind == OrderKind.Idle && stance == UnitStance.Guard && !armament.Target.IsNone)
            {
                if (!armament.Guarding)
                {
                    armament.Guarding = true;
                    armament.GuardPoint = position;
                }
                else if (!InRange(armament.GuardPoint, position, definition.AcquisitionRange + GuardLeash))
                {
                    armament.Target = Entity.None;
                    armament.Returning = true;
                    context.Navigator.PlanRoute(unit.Index, position, armament.GuardPoint, ref mover);
                }
            }

            if (!armament.Target.IsNone)
            {
                if (stance == UnitStance.HoldGround && order.Kind == OrderKind.Idle)
                {
                    // Holding ground: fire at what is in range, never chase.
                    if (!InRange(position, context.Simulation.ClosestPointOf(armament.Target, position), weapon.Range))
                    {
                        armament.Target = Entity.None;
                    }
                }
                else
                {
                    Engage(context, unit, armament.Target, position, weapon.Range, ref mover);
                }
            }
            else if (order.Kind == OrderKind.AttackMove && !mover.HasTarget)
            {
                ContinueAttackMove(context, unit, position, ref order, ref mover);
            }
            else if (order.Kind == OrderKind.Idle && armament.Guarding)
            {
                ReturnToPost(context, unit, position, ref armament, ref mover);
            }
        }
    }

    // A turret, or a unit firing out of a bunker: keeps a target while it stays in reach, answers whoever shoots it
    // (a bunker's occupants answer whoever shoots the bunker), and otherwise looks for the nearest enemy in reach.
    private void FireAtWill(in TickContext context, Entity shooter, ref Armament armament)
    {
        var world = context.World;
        var simulation = context.Simulation;
        var owner = world.Get<Owner>(shooter).PlayerId;
        var position = world.Get<Position>(shooter).Value;
        var reach = simulation.ReachOf(shooter);
        var body = world.Has<Garrisoned>(shooter) ? world.Get<Garrisoned>(shooter).Bunker : shooter;
        if (world.Has<StructureInfo>(shooter) && !world.Get<StructureInfo>(shooter).IsComplete)
        {
            armament.Target = Entity.None;
            return;
        }

        if (!IsValidTarget(context, owner, armament.Target) || !InRange(position, simulation.ClosestPointOf(armament.Target, position), reach))
        {
            armament.Target = Entity.None;
        }

        var attacker = world.Get<Health>(body).LastAttacker;
        if ((armament.Target.IsNone || world.Has<StructureInfo>(armament.Target))
            && IsValidTarget(context, owner, attacker) && !world.Has<StructureInfo>(attacker)
            && InRange(position, world.Get<Position>(attacker).Value, reach))
        {
            armament.Target = attacker;
        }

        if (body == shooter)
        {
            world.Get<Health>(body).LastAttacker = Entity.None; // a bunker's attacker is left for every occupant to see
        }

        if (context.IsStaggeredTick(shooter.Index, ScanPeriod) && (armament.Target.IsNone || world.Has<StructureInfo>(armament.Target)))
        {
            var found = FindNearestEnemy(context, _candidates, owner, position, reach, structures: armament.Target.IsNone);
            if (!found.IsNone || armament.Target.IsNone)
            {
                armament.Target = found;
            }
        }
    }

    // A guard with nothing left to fight walks back to its post; once there it is at rest again.
    private static void ReturnToPost(in TickContext context, Entity unit, FixVec2 position, ref Armament armament, ref Mover mover)
    {
        if (FixVec2.DistanceSquared(position, armament.GuardPoint) <= AtPostSquared)
        {
            armament.Guarding = false;
            armament.Returning = false;
            mover.HasTarget = false;
            return;
        }

        if (!mover.HasTarget || !armament.Returning)
        {
            armament.Returning = true;
            if (!context.Navigator.PlanRoute(unit.Index, position, armament.GuardPoint, ref mover))
            {
                armament.Guarding = false; // the post cannot be reached any more: this is the post now
                armament.Returning = false;
            }
        }
    }

    // An enemy is worth chasing if it is in weapon range already or on ground the unit can walk to (for a structure:
    // the free ground beside the nearest point of its footprint).
    private static bool CanChase(in TickContext context, FixVec2 position, Entity target, Fix64 weaponRange)
    {
        var at = context.Simulation.ClosestPointOf(target, position);
        return InRange(position, at, weaponRange) || context.Navigator.CanReach(position, at);
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

        // A fresh chase plans at once; otherwise re-plan every few ticks as the target moves. One standing where its
        // last route ended, or with no route that arrives (an explicit attack on something out of reach), waits for the
        // re-plan too: planning again every tick would find the same route that falls short.
        var replan = context.IsStaggeredTick(unit.Index, ChasePeriod);
        if (!replan && !mover.HasTarget)
        {
            var stuck = FixVec2.DistanceSquared(position, mover.Target) <= AtPostSquared && context.Navigator.Paths.CountFor(unit.Index) == 0
                && Cell.Containing(position) != Cell.Containing(aimPoint)
                && !context.Navigator.CanReach(position, aimPoint);
            replan = !stuck;
        }

        if (replan)
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
        && !context.World.Has<Garrisoned>(target)
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
    /// <param name="structures">False to consider only units and creatures.</param>
    /// <param name="walker">
    /// For a scanner that moves: its weapon range. Units and creatures beyond it on ground the scanner cannot walk to
    /// are skipped. Null for one that stays put (turrets, bunkers, holding ground).
    /// </param>
    internal static Entity FindNearestEnemy(
        in TickContext context, List<Candidate> candidates, int owner, FixVec2 position, Fix64 range, bool structures = true, Fix64? walker = null)
    {
        var weaponSquared = walker is { } weaponRange ? weaponRange * weaponRange : Fix64.Zero;
        var world = context.World;
        var simulation = context.Simulation;
        var healths = world.Store<Health>();
        var owners = world.Store<Owner>();
        var rangeSquared = range * range;
        candidates.Clear();

        foreach (var index in structures ? world.Store<StructureInfo>().EntityIndices : [])
        {
            if (!simulation.AreEnemies(owner, owners.Get(index).PlayerId))
            {
                continue;
            }

            var closest = simulation.ClosestPointOf(world.EntityAt(index), position);
            var distance = FixVec2.DistanceSquared(position, closest);
            if (distance <= rangeSquared
                && (walker is null || distance <= weaponSquared || context.Navigator.CanReach(position, closest)))
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

                    var at = positions.Get(index).Value;
                    var distance = FixVec2.DistanceSquared(position, at);
                    if (distance <= rangeSquared
                        && (walker is null || distance <= weaponSquared || context.Navigator.CanReach(position, at)))
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
