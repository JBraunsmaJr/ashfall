using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Moves and fights for every Maw creature (GDD section 12, "the swarm path"). Each tick a creature either
/// attacks its target (a player unit that came within its aggro range, or a player structure it touches),
/// chases a unit it has noticed, or steps along the flow field toward the nearest player structure.
/// </summary>
/// <remarks>
/// <para>No creature pathfinds: the shared <see cref="FlowField"/> gives every cell's walking cost to the
/// nearest structure and a creature steps to its cheapest neighbour. Collision is density, not reservation:
/// at most <see cref="CellCapacity"/> creatures may stand in one cell, and a creature whose way on is full
/// waits, so waves pile up at walls and chokepoints instead of overlapping into a single point.</para>
/// <para>Creatures are processed in <see cref="Creature"/>-store order, which is identical on every peer, and
/// cell counts are updated as each one moves, so the result never depends on anything but simulation state.</para>
/// </remarks>
public sealed class SwarmSystem : ISystem
{
    /// <summary>Most creatures that may stand in one cell.</summary>
    public const int CellCapacity = 8;

    // Ticks between a creature's looks around for units to attack (staggered across creatures).
    private const int ScanPeriod = 6;

    private static readonly (int Dx, int Dy, int Cost)[] Neighbours =
    [
        (1, 0, FlowField.StraightCost), (0, 1, FlowField.StraightCost), (-1, 0, FlowField.StraightCost), (0, -1, FlowField.StraightCost),
        (1, 1, FlowField.DiagonalCost), (-1, 1, FlowField.DiagonalCost), (-1, -1, FlowField.DiagonalCost), (1, -1, FlowField.DiagonalCost),
    ];

    private int[] _occupancy = [];

    public void Update(in TickContext context)
    {
        var world = context.World;
        var creatures = world.Store<Creature>();
        if (creatures.Count == 0)
        {
            return;
        }

        var simulation = context.Simulation;
        var map = context.Map;
        var field = simulation.RefreshSwarmField();
        var positions = world.Store<Position>();
        var indices = creatures.EntityIndices;
        var states = creatures.Components;

        if (_occupancy.Length != map.Width * map.Height)
        {
            _occupancy = new int[map.Width * map.Height];
        }
        else
        {
            Array.Clear(_occupancy);
        }

        for (var i = 0; i < indices.Length; i++)
        {
            _occupancy[map.IndexOf(Cell.Containing(positions.Get(indices[i]).Value))]++;
        }

        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            ref var creature = ref states[i];
            ref var position = ref positions.Get(index);
            var definition = context.Content.Creature(creature.CreatureType);
            if (creature.Cooldown > 0)
            {
                creature.Cooldown--;
            }

            if (!IsValidTarget(simulation, creature.Target))
            {
                creature.Target = Entity.None;
            }

            if (creature.Target.IsNone && context.IsStaggeredTick(index, ScanPeriod))
            {
                creature.Target = FindUnitInReach(simulation, position.Value, definition.AggroRange);
            }

            if (!creature.Target.IsNone && Engage(simulation, definition, ref creature, ref position))
            {
                continue;
            }

            FollowField(simulation, field, definition, index, ref creature, ref position);
        }
    }

    /// <summary>
    /// Attacks the target when in reach, otherwise closes on it. Returns false when the creature should follow the
    /// flow field instead (the target is a unit that got away, or the straight way to it is blocked).
    /// </summary>
    private bool Engage(GameSimulation simulation, CreatureDefinition definition, ref Creature creature, ref Position position)
    {
        var world = simulation.World;
        var aim = simulation.ClosestPointOf(creature.Target, position.Value);
        if (TargetingSystem.InRange(position.Value, aim, definition.Weapon.Range))
        {
            if (creature.Cooldown == 0)
            {
                ref var health = ref world.Get<Health>(creature.Target);
                health.Current -= DamageTable.Apply(definition.Weapon.Damage, definition.Weapon.Warhead, health.Armor);
                creature.Cooldown = definition.Weapon.CooldownTicks;
            }

            return true;
        }

        // A unit that has outrun twice the aggro range is let go; the swarm moves on.
        var leash = definition.AggroRange * 2;
        if (!world.Has<StructureInfo>(creature.Target) && FixVec2.DistanceSquared(position.Value, aim) > leash * leash)
        {
            creature.Target = Entity.None;
            return false;
        }

        return TryStepToward(simulation.Map, ref position, aim, definition.SpeedPerTick);
    }

    /// <summary>Steps to the cheapest open neighbour on the flow field, or starts attacking a player structure it touches.</summary>
    private void FollowField(GameSimulation simulation, FlowField field, CreatureDefinition definition, int index, ref Creature creature, ref Position position)
    {
        var map = simulation.Map;
        var cell = Cell.Containing(position.Value);
        var here = field.CostAt(cell);
        if (here == FlowField.Unreachable)
        {
            return; // nothing to attack from here: wait for the layout to change
        }

        var best = new Cell(-1, -1);
        var bestCost = here;
        foreach (var (dx, dy, _) in Neighbours)
        {
            var next = new Cell(cell.X + dx, cell.Y + dy);
            if (!map.InBounds(next) || !field.CanStep(cell, next))
            {
                continue;
            }

            var structure = map.StructureAt(next);
            if (structure >= 0)
            {
                var entity = simulation.World.EntityAt(structure);
                if (IsValidTarget(simulation, entity))
                {
                    creature.Target = entity; // touching a player structure: attack it from next tick
                    return;
                }

                continue;
            }

            var cost = field.CostAt(next);
            if (cost < bestCost && map.IsPassable(next) && _occupancy[map.IndexOf(next)] < CellCapacity)
            {
                best = next;
                bestCost = cost;
            }
        }

        if (best.X < 0)
        {
            return; // every way on is full or uphill: wait
        }

        TryStepToward(map, ref position, best.Center + Jitter(index), definition.SpeedPerTick);
    }

    // Moves straight toward the point if the step stays on passable ground and does not crowd a full cell (the
    // step may clip a cell beside the one aimed at, so the cell actually entered is the one checked).
    private bool TryStepToward(GridMap map, ref Position position, FixVec2 point, Fix64 speed)
    {
        var from = Cell.Containing(position.Value);
        var next = Advance(position.Value, point, speed);
        var to = Cell.Containing(next);
        if (!map.IsPassable(to) || (to != from && _occupancy[map.IndexOf(to)] >= CellCapacity))
        {
            return false;
        }

        Move(map, ref position, from, to, next);
        return true;
    }

    private void Move(GridMap map, ref Position position, Cell from, Cell to, FixVec2 next)
    {
        position.Value = map.Bounds.Clamp(next);
        to = Cell.Containing(position.Value);
        if (to != from)
        {
            _occupancy[map.IndexOf(from)]--;
            _occupancy[map.IndexOf(to)]++;
        }
    }

    private static FixVec2 Advance(FixVec2 from, FixVec2 to, Fix64 speed)
    {
        var delta = to - from;
        return delta.LengthSquared <= speed * speed ? to : from + (delta.Normalized() * speed);
    }

    /// <summary>
    /// A fixed offset from the cell centre for each creature, so a crowd fills a cell instead of stacking on one
    /// point. Derived from the entity index: no state, no random numbers.
    /// </summary>
    internal static FixVec2 Jitter(int index) =>
        new(Fix64.FromRatio(((index * 37) % 7) - 3, 10), Fix64.FromRatio(((index * 53) % 7) - 3, 10));

    private static bool IsValidTarget(GameSimulation simulation, Entity target)
    {
        var world = simulation.World;
        return !target.IsNone
            && world.IsAlive(target)
            && world.Has<Health>(target)
            && !world.Get<Health>(target).IsDead
            && simulation.AreEnemies(GameSimulation.MawPlayerId, world.Get<Owner>(target).PlayerId);
    }

    // The nearest player unit within reach (lowest entity index on ties), from the spatial grid.
    private static Entity FindUnitInReach(GameSimulation simulation, FixVec2 position, Fix64 reach)
    {
        var world = simulation.World;
        var owners = world.Store<Owner>();
        var positions = world.Store<Position>();
        var grid = simulation.Spatial;
        var best = -1;
        var bestDistance = reach * reach;
        var (minX, minY, maxX, maxY) = grid.CellsAround(position, reach);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                foreach (var index in grid.At(x, y))
                {
                    if (!simulation.IsPlayer(owners.Get(index).PlayerId))
                    {
                        continue;
                    }

                    var distance = FixVec2.DistanceSquared(position, positions.Get(index).Value);
                    if (distance < bestDistance || (distance == bestDistance && (best < 0 || index < best)))
                    {
                        best = index;
                        bestDistance = distance;
                    }
                }
            }
        }

        return best < 0 ? Entity.None : world.EntityAt(best);
    }
}
