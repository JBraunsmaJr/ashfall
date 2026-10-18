using Ashfall.Content;
using Ashfall.Simulation;
using Ashfall.Simulation.Map;

namespace Ashfall.Session.Maps;

/// <summary>
/// The horde stress test's battlefield: an open square map with a Bulwark fortress in the middle — a Construction
/// Yard ringed by power plants and production, a wall ring with a gate on each side, and 152 idle defenders (Pyros
/// inside the wall, then riflemen, rocketeers and Warden tanks) that open fire on whatever comes in range. The Maw arrives from every edge. Everything is placed by fixed rules, so
/// the setup is identical on every machine.
/// </summary>
public static class SwarmFortress
{
    /// <summary>Map side in cells: room for 5,000 creatures at the swarm's density limit, well clear of the walls.</summary>
    public const int Size = 128;

    /// <summary>Defenders per kind (rifleman, rocketeer, Warden).</summary>
    public const int DefendersPerKind = 40;

    /// <summary>Pyros on the inside of the wall, where the swarm piles up.</summary>
    public const int Pyros = 32;

    /// <summary>Radius of the square wall ring around the fortress, in cells.</summary>
    public const int WallRadius = 17;

    /// <summary>Width of the gate left open in the middle of each side of the wall ring, in cells.</summary>
    public const int GateWidth = 3;

    public static GridMap Create() => new(Size, Size);

    /// <summary>Places the fortress for <paramref name="player"/> on an empty <see cref="Size"/>-cell map.</summary>
    public static void Build(GameSimulation simulation, int player)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var content = simulation.Content;
        const int middle = Size / 2;

        simulation.PlaceStructure(player, content.StructureIndex(BulwarkContent.ConstructionYard), new Cell(middle - 1, middle - 1));

        // Support structures about eight cells out, inside the innermost ring of defenders (radius 10): power on the
        // diagonals, production on the axes.
        var power = content.StructureIndex(BulwarkContent.PowerPlant);
        simulation.PlaceStructure(player, power, new Cell(middle - 9, middle - 9));
        simulation.PlaceStructure(player, power, new Cell(middle + 7, middle - 9));
        simulation.PlaceStructure(player, power, new Cell(middle - 9, middle + 7));
        simulation.PlaceStructure(player, power, new Cell(middle + 7, middle + 7));
        simulation.PlaceStructure(player, content.StructureIndex(BulwarkContent.Barracks), new Cell(middle - 1, middle - 9));
        simulation.PlaceStructure(player, content.StructureIndex(BulwarkContent.Factory), new Cell(middle - 1, middle + 7));
        simulation.PlaceStructure(player, content.StructureIndex(BulwarkContent.Refinery), new Cell(middle - 8, middle - 1));

        // A wall ring with a gate in the middle of each side, so the swarm either chews through or funnels into
        // the gates, where the flame is.
        var wall = content.StructureIndex(BulwarkContent.Wall);
        for (var step = 0; step < WallRadius * 8; step++)
        {
            var (dx, dy) = PointOnRing(WallRadius, step);
            var gate = (Math.Abs(dy) == WallRadius && Math.Abs(dx) <= GateWidth / 2)
                || (Math.Abs(dx) == WallRadius && Math.Abs(dy) <= GateWidth / 2);
            if (!gate)
            {
                simulation.PlaceStructure(player, wall, new Cell(middle + dx, middle + dy));
            }
        }

        var pyro = content.UnitIndex(BulwarkContent.Pyro);
        for (var i = 0; i < Pyros; i++)
        {
            var (dx, dy) = PointOnRing(WallRadius - 2, i * ((WallRadius - 2) * 8) / Pyros);
            simulation.SpawnUnit(player, pyro, new Cell(middle + dx, middle + dy).Center);
        }

        // Defenders in three rings, outermost first: riflemen, then rocketeers, then Wardens closest to the yard.
        string[] kinds = [BulwarkContent.Rifleman, BulwarkContent.Rocketeer, BulwarkContent.Warden];
        for (var ring = 0; ring < kinds.Length; ring++)
        {
            var unitType = content.UnitIndex(kinds[ring]);
            var radius = 14 - (ring * 2);
            for (var i = 0; i < DefendersPerKind; i++)
            {
                // Walk the square ring of this radius, spreading the defenders evenly along its perimeter.
                var step = i * (radius * 8) / DefendersPerKind;
                var (dx, dy) = PointOnRing(radius, step);
                simulation.SpawnUnit(player, unitType, new Cell(middle + dx, middle + dy).Center);
            }
        }
    }

    // The step-th cell (0 .. 8r-1) clockwise along the square ring of radius r, starting at the top-left corner.
    private static (int Dx, int Dy) PointOnRing(int radius, int step)
    {
        var side = 2 * radius;
        return (step / side) switch
        {
            0 => (-radius + step, -radius),
            1 => (radius, -radius + (step - side)),
            2 => (radius - (step - (2 * side)), radius),
            _ => (-radius, radius - (step - (3 * side))),
        };
    }

    /// <summary>Distance from the map's centre to each nest, in cells: about two thirds of the way to the edge.</summary>
    public const int NestDistance = 44;

    /// <summary>Radius of the Cindrite field laid around each nest, in cells.</summary>
    public const int NestFieldRadius = 4;

    /// <summary>Cindrite per cell in a nest's field.</summary>
    public const int NestFieldDensity = 400;

    // Where nests go, in order: the four sides first, then the four diagonals (scaled so all sit NestDistance out).
    private static readonly (int Dx, int Dy)[] NestDirections =
    [
        (NestDistance, 0), (0, NestDistance), (-NestDistance, 0), (0, -NestDistance),
        (31, 31), (-31, 31), (-31, -31), (31, -31),
    ];

    /// <summary>
    /// Places <paramref name="count"/> Spawning Pits (up to eight) in a ring around the fortress, each on a disc of
    /// Cindrite to feed on. The match must have a horde.
    /// </summary>
    public static void PlaceNests(GameSimulation simulation, int count)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, NestDirections.Length);
        var pit = simulation.Content.StructureIndex(MawContent.SpawningPit);
        const int middle = Size / 2;
        for (var i = 0; i < count; i++)
        {
            var center = new Cell(middle + NestDirections[i].Dx, middle + NestDirections[i].Dy);
            simulation.PlaceNest(pit, new Cell(center.X - 1, center.Y - 1));
            for (var y = center.Y - NestFieldRadius; y <= center.Y + NestFieldRadius; y++)
            {
                for (var x = center.X - NestFieldRadius; x <= center.X + NestFieldRadius; x++)
                {
                    var cell = new Cell(x, y);
                    var (dx, dy) = (x - center.X, y - center.Y);
                    if ((dx * dx) + (dy * dy) <= NestFieldRadius * NestFieldRadius && simulation.Map.StructureAt(cell) < 0)
                    {
                        simulation.Map.SetCindrite(cell, NestFieldDensity);
                    }
                }
            }
        }
    }

    /// <summary>The fortress's wall segments still standing.</summary>
    public static int Walls(GameSimulation simulation, int player)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var owners = simulation.World.Store<Simulation.Components.Owner>();
        var infos = simulation.World.Store<Simulation.Components.StructureInfo>();
        var walls = 0;
        foreach (var index in infos.EntityIndices)
        {
            var isWall = simulation.Content.Structure(infos.Get(index).StructureType).Role == StructureRole.Wall;
            walls += owners.Get(index).PlayerId == player && isWall ? 1 : 0;
        }

        return walls;
    }

    /// <summary>The fortress's player's buildings (walls not counted) and units still standing.</summary>
    public static (int Structures, int Units) Survivors(GameSimulation simulation, int player)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var world = simulation.World;
        var owners = world.Store<Simulation.Components.Owner>();
        var infos = world.Store<Simulation.Components.StructureInfo>();
        var structures = 0;
        foreach (var index in infos.EntityIndices)
        {
            var isWall = simulation.Content.Structure(infos.Get(index).StructureType).Role == StructureRole.Wall;
            structures += owners.Get(index).PlayerId == player && !isWall ? 1 : 0;
        }

        var units = 0;
        foreach (var index in world.Store<Simulation.Components.UnitInfo>().EntityIndices)
        {
            units += owners.Get(index).PlayerId == player ? 1 : 0;
        }

        return (structures, units);
    }
}
