using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Session.Commands;
using Ashfall.Simulation;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Orders;
using Ashfall.Simulation.Players;

namespace Ashfall.Session.Ai;

/// <summary>
/// The M1 stand-in for an AI player: a fixed build order, continuous production, and attack waves.
/// It reads the simulation and issues ordinary commands, exactly as the real AI (M5) will. Each instance
/// draws its personality (wave size, unit mix) from its own random stream, so two scripted AIs on a
/// symmetric map still play differently and a match reaches a result.
/// </summary>
public sealed class ScriptedSkirmishAi : ICommandSource
{
    // Seconds between decisions; staggered by player so both AIs don't think on the same tick.
    private const int ThinkPeriod = 15;

    // Combat units this far from home are "in the field" and keep pushing toward the enemy.
    private const int HomeRadius = 14;

    // In a Purge, units kept at home between the Maw's waves.
    private const int PurgeHomeGuard = 6;

    private readonly int _playerId;
    private readonly int _waveSize;
    private readonly int _infantryPerTank;
    private readonly int _harvesterTarget;
    private readonly List<Entity> _idleAtHome = [];
    private int _trained;

    // Credits held back this think for the next economic goal (a harvester or the next structure).
    private int _reserve;

    // No harvester (none alive or paid for) and too little money to buy one: no income will ever come again, so
    // there is nothing to save for. Everything goes into the army, and the army goes in.
    private bool _starved;

    public ScriptedSkirmishAi(int playerId, ulong seed)
    {
        _playerId = playerId;
        var personality = new DeterministicRandom(seed, stream: 2_000UL + (ulong)playerId);
        _waveSize = personality.Next(5, 10);
        _infantryPerTank = personality.Next(1, 4);
        _harvesterTarget = personality.Next(1, 3);
    }

    public void CollectCommands(GameSimulation simulation, long tick, List<Command> output)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(output);

        if ((tick + (_playerId * 7)) % ThinkPeriod != 0 || simulation.IsOver)
        {
            return;
        }

        var player = simulation.Players[_playerId];
        if (player.Defeated)
        {
            return;
        }

        var home = FindHome(simulation);
        if (home is null)
        {
            return;
        }

        _reserve = 0;
        var harvester = simulation.Content.UnitIndex(BulwarkContent.Harvester);
        _starved = CountUnits(simulation, harvester) == 0
            && QueuedAt(simulation, FindProducer(simulation, StructureRole.Factory), harvester) == 0
            && !player.CanAfford(simulation.Content.Unit(harvester).Cost);
        Construct(simulation, player, home.Value, output);
        Train(simulation, player, output);
        Attack(simulation, home.Value, output);
    }

    private void Construct(GameSimulation simulation, PlayerState player, Cell home, List<Command> output)
    {
        if (simulation.World.IsAlive(player.ConstructionSite) || _starved)
        {
            return;
        }

        // Without a Construction Yard nothing can be built, so nothing is worth saving for: what is left goes
        // into the army instead of sitting in reserve for a refinery that will never come.
        var yard = simulation.Content.StructureIndex(BulwarkContent.ConstructionYard);
        if (CountStructures(simulation, yard) == 0)
        {
            return;
        }

        var content = simulation.Content;
        var next = NextStructure(simulation, player);
        if (next < 0)
        {
            return;
        }

        if (!player.CanAfford(content.Structure(next).Cost))
        {
            // Save for it rather than let the barracks spend every credit as it arrives.
            _reserve = content.Structure(next).Cost;
            return;
        }

        if (TryFindSite(simulation, next, home, out var origin))
        {
            output.Add(Command.Build(_playerId, next, origin));
        }
    }

    // Power first whenever the next building would overload the grid, then the fixed order.
    private int NextStructure(GameSimulation simulation, PlayerState player)
    {
        var content = simulation.Content;
        var powerPlant = content.StructureIndex(BulwarkContent.PowerPlant);
        (string Id, int Count)[] buildOrder =
        [
            (BulwarkContent.PowerPlant, 1),
            (BulwarkContent.Refinery, 1),
            (BulwarkContent.Barracks, 1),
            (BulwarkContent.PowerPlant, 2),
            (BulwarkContent.Factory, 1),
            (BulwarkContent.Refinery, 2),
            (BulwarkContent.PowerPlant, 3),
        ];

        foreach (var (id, count) in buildOrder)
        {
            var type = content.StructureIndex(id);
            if (CountStructures(simulation, type) >= count)
            {
                continue;
            }

            var demand = player.PowerDemand - content.Structure(type).Power;
            return type != powerPlant && demand > player.PowerSupply ? powerPlant : type;
        }

        return player.PowerDemand > player.PowerSupply ? powerPlant : -1;
    }

    // The factory tops up harvesters first and the army is paid for out of what is left above the reserve,
    // so the economy grows instead of every credit going into riflemen.
    private void Train(GameSimulation simulation, PlayerState player, List<Command> output)
    {
        var content = simulation.Content;
        var harvesterType = content.UnitIndex(BulwarkContent.Harvester);
        var factory = FindProducer(simulation, StructureRole.Factory);
        var barracks = FindProducer(simulation, StructureRole.Barracks);
        var wantHarvester = CountUnits(simulation, harvesterType) + QueuedAt(simulation, factory, harvesterType) < _harvesterTarget;

        if (!factory.IsNone && wantHarvester && !_starved)
        {
            var cost = content.Unit(harvesterType).Cost;
            if (player.CanAfford(cost))
            {
                output.Add(Command.Train(_playerId, factory, harvesterType));
                _reserve = 0;
                return;
            }

            _reserve = Math.Max(_reserve, cost);
        }

        if (!factory.IsNone && (!wantHarvester || _starved) && QueueCount(simulation, factory) < 2)
        {
            TryTrain(simulation, player, factory, content.UnitIndex(BulwarkContent.Warden), output);
        }

        if (!barracks.IsNone && QueueCount(simulation, barracks) < 2)
        {
            var unitId = _trained % (_infantryPerTank + 1) == 0 ? BulwarkContent.Rocketeer : BulwarkContent.Rifleman;
            TryTrain(simulation, player, barracks, content.UnitIndex(unitId), output);
        }

    }

    private void TryTrain(GameSimulation simulation, PlayerState player, Entity producer, int unitType, List<Command> output)
    {
        var cost = simulation.Content.Unit(unitType).Cost;
        if (player.Credits - _reserve >= cost)
        {
            output.Add(Command.Train(_playerId, producer, unitType));
            _reserve += cost; // commands apply later this tick; don't double-spend the same credits
            _trained++;
        }
    }

    // The player's first completed producer with this role (lowest entity index), or none.
    private Entity FindProducer(GameSimulation simulation, StructureRole role)
    {
        var world = simulation.World;
        var producers = world.Store<Producer>();
        foreach (var index in producers.EntityIndices)
        {
            var info = world.Store<StructureInfo>().Get(index);
            if (world.Store<Owner>().Get(index).PlayerId == _playerId
                && info.IsComplete
                && simulation.Content.Structure(info.StructureType).Role == role)
            {
                return world.EntityAt(index);
            }
        }

        return Entity.None;
    }

    private static int QueueCount(GameSimulation simulation, Entity producer) =>
        producer.IsNone ? 0 : simulation.World.Get<Producer>(producer).QueueCount;

    private static int QueuedAt(GameSimulation simulation, Entity producer, int unitType) =>
        !producer.IsNone && QueueCount(simulation, producer) > 0 && simulation.World.Get<Producer>(producer).Current == unitType ? 1 : 0;

    private void Attack(GameSimulation simulation, Cell home, List<Command> output)
    {
        var world = simulation.World;
        var homePoint = home.Center;
        var target = FindEnemyStructureNearest(simulation, homePoint);
        if (target.IsNone)
        {
            return;
        }

        _idleAtHome.Clear();
        var armaments = world.Store<Armament>();
        foreach (var index in armaments.EntityIndices)
        {
            var unit = world.EntityAt(index);
            if (world.Get<Owner>(unit).PlayerId != _playerId
                || world.Get<UnitOrder>(unit).Kind != OrderKind.Idle
                || !armaments.Get(index).Target.IsNone)
            {
                continue;
            }

            var position = world.Get<Position>(unit).Value;
            var aim = simulation.ClosestPointOf(target, position);
            if (FixVec2.DistanceSquared(position, homePoint) > Fix64.FromInt(HomeRadius * HomeRadius))
            {
                // Already out in the field: keep pushing toward the nearest enemy structure.
                output.Add(Command.AttackMove(_playerId, unit, aim));
            }
            else
            {
                _idleAtHome.Add(unit);
            }
        }

        // All in: with no income for good (starved), or no harvester and no barracks to train at, the army will not
        // grow into a full wave, so waiting for one would stall the match forever. Send whatever is at home.
        var broke = _starved
            || (CountUnits(simulation, simulation.Content.UnitIndex(BulwarkContent.Harvester)) == 0
                && FindProducer(simulation, StructureRole.Barracks).IsNone);

        // Against the Maw the base is attacked in waves, so a home guard stays behind (unless there is nothing left
        // to defend with): only what is at home beyond it goes out.
        var guard = simulation.Goal == MatchGoal.Purge && !broke ? PurgeHomeGuard : 0;
        if (_idleAtHome.Count - guard < (broke ? 1 : _waveSize))
        {
            return;
        }

        for (var i = guard; i < _idleAtHome.Count; i++)
        {
            var unit = _idleAtHome[i];
            output.Add(Command.AttackMove(_playerId, unit, simulation.ClosestPointOf(target, world.Get<Position>(unit).Value)));
        }
    }

    private Cell? FindHome(GameSimulation simulation)
    {
        var yard = simulation.Content.StructureIndex(BulwarkContent.ConstructionYard);
        var world = simulation.World;
        var structures = world.Store<StructureInfo>();
        Cell? fallback = null;
        foreach (var index in structures.EntityIndices)
        {
            if (world.Store<Owner>().Get(index).PlayerId != _playerId)
            {
                continue;
            }

            var info = structures.Get(index);
            var definition = simulation.Content.Structure(info.StructureType);
            var middle = new Cell(info.Origin.X + (definition.Width / 2), info.Origin.Y + (definition.Height / 2));
            if (info.StructureType == yard)
            {
                return middle;
            }

            fallback ??= middle;
        }

        return fallback;
    }

    // Nearest enemy structure to our home: the next objective for attack waves.
    private Entity FindEnemyStructureNearest(GameSimulation simulation, FixVec2 from)
    {
        var world = simulation.World;
        var structures = world.Store<StructureInfo>();
        var best = Entity.None;
        var bestDistance = Fix64.MaxValue;
        foreach (var index in structures.EntityIndices)
        {
            if (!simulation.AreEnemies(_playerId, world.Store<Owner>().Get(index).PlayerId))
            {
                continue;
            }

            var distance = FixVec2.DistanceSquared(from, world.Store<Position>().Get(index).Value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = world.EntityAt(index);
            }
        }

        return best;
    }

    // Spiral outward from home; keep a one-cell lane around every new structure so units can get out.
    // The search runs in the base's own frame, so bases at symmetric starts are laid out identically
    // relative to the terrain: a half turn for duels, and for three or four players the quarter turns
    // that carry the top-left base onto each of the others.
    private bool TryFindSite(GameSimulation simulation, int structureType, Cell home, out Cell origin)
    {
        var definition = simulation.Content.Structure(structureType);
        var map = simulation.Map;
        var flipX = home.X * 2 >= map.Width;
        var flipY = home.Y * 2 >= map.Height;
        var quarterTurns = simulation.Players.Count <= 2 || definition.Width != definition.Height
            ? -1
            : flipX ? (flipY ? 2 : 1) : (flipY ? 3 : 0);
        var reach = simulation.Content.Economy.BuildRadius + 4;
        for (var radius = 3; radius <= reach; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (dx != -radius && dx != radius && dy != -radius && dy != radius)
                    {
                        continue;
                    }

                    var candidate = quarterTurns >= 0
                        ? Turned(home, dx, dy, definition.Width, quarterTurns)
                        : new Cell(
                            LocalToOrigin(home.X, dx, definition.Width, flipX),
                            LocalToOrigin(home.Y, dy, definition.Height, flipY));
                    if (HasLane(map, candidate, definition.Width, definition.Height)
                        && simulation.CheckPlacement(_playerId, structureType, candidate) == PlacementResult.Ok)
                    {
                        origin = candidate;
                        return true;
                    }
                }
            }
        }

        origin = default;
        return false;
    }

    // Footprint origin along one axis for a footprint centred at home + offset, mirrored about home when flipped.
    private static int LocalToOrigin(int home, int offset, int size, bool flip) =>
        flip ? home - offset + (size / 2) - size + 1 : home + offset - (size / 2);

    // The origin of a square footprint centred at home + (dx, dy), turned about home by quarter turns
    // (x, y) -> (-y, x), worked in doubled coordinates so even-sized footprints turn exactly.
    private static Cell Turned(Cell home, int dx, int dy, int size, int quarterTurns)
    {
        var x = (2 * (dx - (size / 2))) + size - 1;
        var y = (2 * (dy - (size / 2))) + size - 1;
        for (var i = 0; i < quarterTurns; i++)
        {
            (x, y) = (-y, x);
        }

        return new Cell(home.X + ((x - size + 1) / 2), home.Y + ((y - size + 1) / 2));
    }

    private static bool HasLane(GridMap map, Cell origin, int width, int height)
    {
        for (var y = -1; y <= height; y++)
        {
            for (var x = -1; x <= width; x++)
            {
                var cell = new Cell(origin.X + x, origin.Y + y);
                if (map.InBounds(cell) && map.StructureAt(cell) >= 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private int CountStructures(GameSimulation simulation, int structureType)
    {
        var world = simulation.World;
        var structures = world.Store<StructureInfo>();
        var count = 0;
        foreach (var index in structures.EntityIndices)
        {
            if (structures.Get(index).StructureType == structureType && world.Store<Owner>().Get(index).PlayerId == _playerId)
            {
                count++;
            }
        }

        return count;
    }

    private int CountUnits(GameSimulation simulation, int unitType)
    {
        var world = simulation.World;
        var units = world.Store<UnitInfo>();
        var count = 0;
        foreach (var index in units.EntityIndices)
        {
            if (units.Get(index).UnitType == unitType && world.Store<Owner>().Get(index).PlayerId == _playerId)
            {
                count++;
            }
        }

        return count;
    }
}
