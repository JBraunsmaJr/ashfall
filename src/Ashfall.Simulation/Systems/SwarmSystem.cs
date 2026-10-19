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
/// chases a unit it has noticed, or steps along the flow field toward the nearest player structure. A brooding
/// creature (see <see cref="Creature.Brooding"/>) instead keeps to its place by its nest until the wave goes,
/// fighting only units that come near; if its nest dies, it marches at once.
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
    private const int ScanPeriod = 4;

    // How much further than twice its aggro range a creature chases a unit before letting it go.
    private static readonly Fix64 ChaseSlack = Fix64.FromInt(4);

    // A brooding creature attacks player units this close to its nest's centre.
    private static readonly Fix64 GuardRadius = Fix64.FromInt(9);

    private static readonly (int Dx, int Dy, int Cost)[] Neighbours =
    [
        (1, 0, FlowField.StraightCost), (0, 1, FlowField.StraightCost), (-1, 0, FlowField.StraightCost), (0, -1, FlowField.StraightCost),
        (1, 1, FlowField.DiagonalCost), (-1, 1, FlowField.DiagonalCost), (-1, -1, FlowField.DiagonalCost), (1, -1, FlowField.DiagonalCost),
    ];

    // Where brooding creatures wait, relative to their nest's mouth: anywhere within four cells of it.
    private static readonly (int Dx, int Dy)[] Roosts = BuildRoosts();

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
        var healths = world.Store<Health>();
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
            _occupancy[map.IndexOf(Cell.Containing(positions.Get(indices[i]).Value))] += context.Content.Creature(states[i].CreatureType).CellWeight;
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

            if (creature.Brooding && !(world.IsAlive(creature.Home) && world.Has<Nest>(creature.Home)))
            {
                creature.Brooding = false; // its nest is gone: nothing left to guard
            }

            // Shot at: turn on the attacker (a unit, not a building), whatever it was doing.
            ref var health = ref healths.Get(index);
            if (!health.LastAttacker.IsNone)
            {
                if ((creature.Target.IsNone || world.Has<StructureInfo>(creature.Target))
                    && IsValidTarget(simulation, health.LastAttacker) && !world.Has<StructureInfo>(health.LastAttacker))
                {
                    creature.Target = health.LastAttacker;
                }

                health.LastAttacker = Entity.None;
            }

            // Looks around for units: when it has nothing to do, and while it chews on a building (a unit that comes
            // near matters more than the wall).
            if ((creature.Target.IsNone || world.Has<StructureInfo>(creature.Target)) && context.IsStaggeredTick(index, ScanPeriod))
            {
                // A brood guards its nest: it goes for any unit near the nest, not just near itself.
                var found = creature.Brooding
                    ? FindUnitInReach(simulation, world.Get<Position>(creature.Home).Value, GuardRadius)
                    : FindUnitInReach(simulation, position.Value, definition.AggroRange);
                if (!found.IsNone)
                {
                    creature.Target = found;
                }
            }

            if (!creature.Target.IsNone && Engage(simulation, definition, world.EntityAt(index), ref creature, ref position))
            {
                continue;
            }

            if (creature.Brooding)
            {
                Roost(simulation, definition, index, creature.Home, ref position);
                continue;
            }

            // A wave the director aimed follows its own field; once its target has nothing of that kind left, it
            // goes for the nearest player structure like the rest.
            var way = field;
            if (creature.Goal != SwarmGoal.Nearest)
            {
                way = simulation.FieldFor(creature.Goal);
                if (way.SourceCells == 0)
                {
                    creature.Goal = SwarmGoal.Nearest;
                    way = field;
                }
            }

            FollowField(simulation, way, definition, index, ref creature, ref position);
        }
    }

    // Walks to (or stays at) this creature's place in the brood around its nest's mouth (the exit cell, where it was
    // born). Away from that place it takes the open neighbouring cell nearest to it, so a crowd at the mouth spreads
    // out cell by cell instead of queueing behind one full cell.
    private void Roost(GameSimulation simulation, CreatureDefinition definition, int index, Entity home, ref Position position)
    {
        var map = simulation.Map;
        var mouth = simulation.ExitCell(simulation.World.Get<StructureInfo>(home));
        var (dx, dy) = Roosts[index % Roosts.Length];
        var roost = new Cell(mouth.X + dx, mouth.Y + dy);
        var cell = Cell.Containing(position.Value);
        if (cell == roost)
        {
            var point = roost.Center + Jitter(index);
            if (position.Value != point)
            {
                TryStepToward(map, ref position, point, definition);
            }

            return;
        }

        var best = cell;
        var bestDistance = Distance(cell, roost);
        foreach (var (nx, ny, _) in Neighbours)
        {
            var next = new Cell(cell.X + nx, cell.Y + ny);
            var distance = Distance(next, roost);
            if (distance < bestDistance && map.InBounds(next) && map.IsPassable(next)
                && _occupancy[map.IndexOf(next)] + definition.CellWeight <= CellCapacity)
            {
                best = next;
                bestDistance = distance;
            }
        }

        if (best != cell)
        {
            TryStepToward(map, ref position, best.Center + Jitter(index), definition);
        }
    }

    private static int Distance(Cell a, Cell b) => ((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y));

    private static (int Dx, int Dy)[] BuildRoosts()
    {
        var roosts = new List<(int Dx, int Dy)>();
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                var distance = (dx * dx) + (dy * dy);
                if (distance >= 1 && distance <= 16)
                {
                    roosts.Add((dx, dy));
                }
            }
        }

        // Spread consecutive entity indices around the ring rather than along one row of it.
        var spread = new (int Dx, int Dy)[roosts.Count];
        var stride = 7;
        while (System.Numerics.BigInteger.GreatestCommonDivisor(stride, roosts.Count) != 1)
        {
            stride++; // coprime with the ring's size, so every place is used once
        }

        for (var i = 0; i < roosts.Count; i++)
        {
            spread[i] = roosts[i * stride % roosts.Count];
        }

        return spread;
    }

    /// <summary>
    /// Attacks the target when in reach, otherwise closes on it. Returns false when the creature should follow the
    /// flow field instead (the target is a unit that got away, or the straight way to it is blocked).
    /// </summary>
    private bool Engage(GameSimulation simulation, CreatureDefinition definition, Entity self, ref Creature creature, ref Position position)
    {
        var world = simulation.World;
        var aim = simulation.ClosestPointOf(creature.Target, position.Value);
        if (TargetingSystem.InRange(position.Value, aim, definition.Weapon.Range))
        {
            if (creature.Cooldown == 0)
            {
                ref var health = ref world.Get<Health>(creature.Target);
                health.Hit(DamageTable.Apply(definition.Weapon.Damage, definition.Weapon.Warhead, health.Armor), self);
                creature.Cooldown = definition.Weapon.CooldownTicks;
            }

            return true;
        }

        // A unit that has got well beyond its aggro range (twice it, and a little more) is let go; the swarm moves on.
        var leash = (definition.AggroRange * 2) + ChaseSlack;
        if (!world.Has<StructureInfo>(creature.Target) && FixVec2.DistanceSquared(position.Value, aim) > leash * leash)
        {
            creature.Target = Entity.None;
            return false;
        }

        return TryStepToward(simulation.Map, ref position, aim, definition);
    }

    /// <summary>
    /// Steps to the cheapest open neighbour on the flow field, or starts attacking a player structure it touches.
    /// Ranged creatures (weapon reach beyond a cell) stop as soon as a player building other than a wall is in reach:
    /// they walk up to the wall and arc acid over it. A wall they bump into they attack like anyone else.
    /// </summary>
    private void FollowField(GameSimulation simulation, FlowField field, CreatureDefinition definition, int index, ref Creature creature, ref Position position)
    {
        var map = simulation.Map;
        var cell = Cell.Containing(position.Value);
        var here = field.CostAt(cell);
        if (here == FlowField.Unreachable)
        {
            return; // nothing to attack from here: wait for the layout to change
        }

        var reach = definition.Weapon.Range;
        if (reach > Fix64.One && here <= (reach.CeilingToInt() + 2) * FlowField.StraightCost
            && FindStructureInReach(simulation, position.Value, reach) is { IsNone: false } building)
        {
            creature.Target = building;
            return;
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
            if (cost < bestCost && map.IsPassable(next) && _occupancy[map.IndexOf(next)] + definition.CellWeight <= CellCapacity)
            {
                best = next;
                bestCost = cost;
            }
        }

        if (best.X < 0)
        {
            return; // every way on is full or uphill: wait
        }

        TryStepToward(map, ref position, best.Center + Jitter(index), definition);
    }

    // Moves straight toward the point if the step stays on passable ground and does not crowd a full cell (the
    // step may clip a cell beside the one aimed at, so the cell actually entered is the one checked).
    private bool TryStepToward(GridMap map, ref Position position, FixVec2 point, CreatureDefinition definition)
    {
        var from = Cell.Containing(position.Value);
        var next = Advance(position.Value, point, definition.SpeedPerTick);
        var to = Cell.Containing(next);
        if (!map.IsPassable(to) || (to != from && _occupancy[map.IndexOf(to)] + definition.CellWeight > CellCapacity))
        {
            return false;
        }

        position.Value = map.Bounds.Clamp(next);
        to = Cell.Containing(position.Value);
        if (to != from)
        {
            _occupancy[map.IndexOf(from)] -= definition.CellWeight;
            _occupancy[map.IndexOf(to)] += definition.CellWeight;
        }

        return true;
    }

    // The player building (not a wall) within reach to spit at: the nearest (closest point of its footprint), then
    // the lowest entity index.
    private static Entity FindStructureInReach(GameSimulation simulation, FixVec2 position, Fix64 reach)
    {
        var map = simulation.Map;
        var content = simulation.Content;
        var structures = simulation.World.Store<StructureInfo>();
        var best = Entity.None;
        var bestDistance = reach * reach;
        var (minX, minY, maxX, maxY) = simulation.Spatial.CellsAround(position, reach);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var index = map.StructureAt(new Cell(x, y));
                var entity = simulation.World.EntityAt(index);
                if (index < 0 || entity == best || !IsValidTarget(simulation, entity)
                    || content.Structure(structures.Get(index).StructureType).Role == StructureRole.Wall)
                {
                    continue;
                }

                var distance = FixVec2.DistanceSquared(position, simulation.ClosestPointOf(entity, position));
                if (distance < bestDistance || (distance == bestDistance && (best.IsNone || index < best.Index)))
                {
                    best = entity;
                    bestDistance = distance;
                }
            }
        }

        return best;
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
