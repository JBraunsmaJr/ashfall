using Ashfall.Content;
using Ashfall.Simulation;
using Ashfall.Simulation.Map;

namespace Ashfall.Session.Maps;

/// <summary>
/// The horde stress test's battlefield: an open square map with a Bulwark fortress in the middle — a Construction
/// Yard ringed by power plants and production, and 120 idle defenders (riflemen, rocketeers and Warden tanks) that
/// open fire on whatever comes in range. The Maw arrives from every edge. Everything is placed by fixed rules, so
/// the setup is identical on every machine.
/// </summary>
public static class SwarmFortress
{
    /// <summary>Map side in cells: room for 5,000 creatures at the swarm's density limit, well clear of the walls.</summary>
    public const int Size = 128;

    /// <summary>Defenders per kind (rifleman, rocketeer, Warden).</summary>
    public const int DefendersPerKind = 40;

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

    /// <summary>The fortress's player's structures and units still standing.</summary>
    public static (int Structures, int Units) Survivors(GameSimulation simulation, int player)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var world = simulation.World;
        var owners = world.Store<Simulation.Components.Owner>();
        var structures = 0;
        foreach (var index in world.Store<Simulation.Components.StructureInfo>().EntityIndices)
        {
            structures += owners.Get(index).PlayerId == player ? 1 : 0;
        }

        var units = 0;
        foreach (var index in world.Store<Simulation.Components.UnitInfo>().EntityIndices)
        {
            units += owners.Get(index).PlayerId == player ? 1 : 0;
        }

        return (structures, units);
    }
}
