using Ashfall.Simulation.Map;

namespace Ashfall.Session.Maps;

/// <summary>
/// The hand-made M3 map for three or four players: 64×64 with four-fold rotational symmetry
/// (cell (x, y) rotates to (63−y, x)), one base per corner. Rock walls with a gap in each split the
/// quadrants; every base has its own Cindrite field and a richer field sits in the open centre.
/// With teams of two, the default team order puts allies in opposite corners.
/// </summary>
public static class QuadMap
{
    public const int Size = 64;

    public const int MaxPlayers = 4;

    private const int ConstructionYardSize = 3;

    /// <summary>Top-left cell of a player's starting Construction Yard (players go round the corners).</summary>
    public static Cell StartOrigin(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, MaxPlayers);
        var origin = new Cell(6, 6);
        for (var i = 0; i < player; i++)
        {
            origin = RotateFootprint(origin, ConstructionYardSize);
        }

        return origin;
    }

    public static GridMap Create()
    {
        var map = new GridMap(Size, Size);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var cell = new Cell(x, y);
                if (AnyRotation(cell, IsWall))
                {
                    map.SetTerrain(cell, Terrain.Rock);
                }
                else if (AnyRotation(cell, IsBaseField))
                {
                    map.SetCindrite(cell, DuelMap.BaseFieldDensity);
                }
                else if (IsCentreField(x, y))
                {
                    map.SetCindrite(cell, DuelMap.CentreFieldDensity);
                }
            }
        }

        // Each field rises from a fissure at its heart: every base field's centre and the four middle cells.
        Cell base0 = new(17, 5), middle = new((Size / 2) - 1, (Size / 2) - 1);
        for (var i = 0; i < 4; i++)
        {
            map.SetFissure(base0, map.CindriteAt(base0));
            map.SetFissure(middle, map.CindriteAt(middle));
            base0 = Rotate(base0);
            middle = Rotate(middle);
        }

        return map;
    }

    /// <summary>The cell a quarter turn clockwise (on the map grid) from <paramref name="cell"/>.</summary>
    public static Cell Rotate(Cell cell) => new(Size - 1 - cell.Y, cell.X);

    /// <summary>The origin of a square footprint a quarter turn from one at <paramref name="origin"/>.</summary>
    public static Cell RotateFootprint(Cell origin, int size) => new(Size - origin.Y - size, origin.X);

    private static bool AnyRotation(Cell cell, Func<int, int, bool> test)
    {
        for (var i = 0; i < 4; i++)
        {
            if (test(cell.X, cell.Y))
            {
                return true;
            }

            cell = Rotate(cell);
        }

        return false;
    }

    // One arm of the cross: a wall from the map edge toward the centre, with a passage through it.
    private static bool IsWall(int x, int y) => x >= 30 && x <= 33 && y <= 23 && (y < 10 || y > 15);

    private static bool IsBaseField(int x, int y) => x >= 15 && x <= 19 && y >= 3 && y <= 7;

    private static bool IsCentreField(int x, int y)
    {
        var dx = (2 * x) - (Size - 1);
        var dy = (2 * y) - (Size - 1);
        return (dx < 0 ? -dx : dx) + (dy < 0 ? -dy : dy) <= 8;
    }
}
