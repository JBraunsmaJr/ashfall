using Ashfall.Core;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Tests;

public class GridMapTests
{
    [Fact]
    public void Rock_structures_and_the_map_edge_block_movement()
    {
        var map = new GridMap(8, 8);
        map.SetTerrain(new Cell(1, 1), Terrain.Rock);
        map.SetStructure(new Cell(4, 4), 2, 2, entityIndex: 3);

        Assert.True(map.IsPassable(new Cell(0, 0)));
        Assert.False(map.IsPassable(new Cell(1, 1)));
        Assert.False(map.IsPassable(new Cell(5, 5)));
        Assert.False(map.IsPassable(new Cell(-1, 0)));
        Assert.False(map.IsPassable(new Cell(8, 0)));
        Assert.Equal(3, map.StructureAt(new Cell(4, 5)));

        map.ClearStructure(new Cell(4, 4), 2, 2);
        Assert.True(map.IsPassable(new Cell(5, 5)));
    }

    [Fact]
    public void Cindrite_can_be_walked_on_but_not_built_on()
    {
        var map = new GridMap(8, 8);
        map.SetCindrite(new Cell(2, 2), 100);

        Assert.True(map.IsPassable(new Cell(2, 2)));
        Assert.False(map.IsBuildable(new Cell(2, 2)));
        Assert.True(map.IsBuildable(new Cell(3, 2)));
    }

    [Fact]
    public void Taking_cindrite_never_takes_more_than_is_there_and_keeps_the_total()
    {
        var map = new GridMap(8, 8);
        map.SetCindrite(new Cell(2, 2), 60);
        map.SetCindrite(new Cell(3, 2), 40);

        Assert.Equal(25, map.TakeCindrite(new Cell(2, 2), 25));
        Assert.Equal(35, map.TakeCindrite(new Cell(2, 2), 50));
        Assert.Equal(0, map.TakeCindrite(new Cell(2, 2), 50));
        Assert.Equal(40, map.TotalCindrite);
        Assert.Equal(0, map.CindriteAt(new Cell(-1, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.TakeCindrite(new Cell(-1, 2), 50));
    }

    [Fact]
    public void Map_state_is_part_of_the_hash()
    {
        var a = new GridMap(8, 8);
        var b = new GridMap(8, 8);
        Assert.Equal(Hash(a), Hash(b));

        b.SetCindrite(new Cell(1, 1), 5);
        Assert.NotEqual(Hash(a), Hash(b));
    }

    [Fact]
    public void Cells_contain_their_points_and_measure_chebyshev_distance()
    {
        Assert.Equal(new Cell(3, 4), Cell.Containing(new Cell(3, 4).Center));
        Assert.Equal(new Cell(-1, 0), Cell.Containing(new FixVec2(-Fix64.Half, Fix64.Half)));
        Assert.Equal(5, Cell.ChebyshevDistance(new Cell(0, 0), new Cell(5, -3)));
    }

    private static ulong Hash(GridMap map)
    {
        var hasher = default(StateHasher);
        map.HashInto(ref hasher);
        return hasher.Value;
    }
}
