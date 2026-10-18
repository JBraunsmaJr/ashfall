using Ashfall.Simulation.Map;

namespace Ashfall.Session.Maps;

/// <summary>
/// The hand-made M1 duel map, 64×64 and rotationally symmetric (cell (x, y) mirrors to (63−x, 63−y)) so
/// neither start is favoured. A rock ridge crosses the middle with a contested gap at the centre and a
/// flank gap on each side; each base has its own Cindrite field and a richer field sits in the centre gap.
/// The default when no generated map is chosen, and the map the golden skirmish replay is played on.
/// </summary>
public static class DuelMap
{
    public const int Size = 64;

    /// <summary>Cindrite per cell in each base field.</summary>
    public const int BaseFieldDensity = 400;

    /// <summary>Cindrite per cell in the central field.</summary>
    public const int CentreFieldDensity = 500;

    private const int ConstructionYardSize = 3;

    /// <summary>Top-left cell of a player's starting Construction Yard.</summary>
    public static Cell StartOrigin(int player) => player switch
    {
        0 => new Cell(6, 6),
        1 => MirrorFootprint(new Cell(6, 6), ConstructionYardSize, ConstructionYardSize),
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "The duel map has two start positions."),
    };

    public static GridMap Create()
    {
        var map = new GridMap(Size, Size);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var cell = new Cell(x, y);
                if (IsRidge(x, y))
                {
                    map.SetTerrain(cell, Terrain.Rock);
                }
                else if (IsBaseField(x, y) || IsBaseField(Size - 1 - x, Size - 1 - y))
                {
                    map.SetCindrite(cell, BaseFieldDensity);
                }
                else if (IsCentreField(x, y))
                {
                    map.SetCindrite(cell, CentreFieldDensity);
                }
            }
        }

        return map;
    }

    /// <summary>The cell rotationally opposite <paramref name="cell"/>.</summary>
    public static Cell Mirror(Cell cell) => new(Size - 1 - cell.X, Size - 1 - cell.Y);

    /// <summary>The origin of the footprint rotationally opposite one at <paramref name="origin"/>.</summary>
    public static Cell MirrorFootprint(Cell origin, int width, int height) =>
        new(Size - origin.X - width, Size - origin.Y - height);

    // A band along the anti-diagonal, with a centre gap and two flank gaps.
    private static bool IsRidge(int x, int y)
    {
        var along = x + y;
        if (along < 61 || along > 65)
        {
            return false;
        }

        var across = x > y ? x - y : y - x;
        var centreGap = across <= 5;
        var flankGap = across >= 36 && across <= 42;
        return !centreGap && !flankGap;
    }

    private static bool IsBaseField(int x, int y) => x >= 14 && x <= 18 && y >= 3 && y <= 7;

    // A diamond around the map centre, measured in half cells so it is exactly symmetric.
    private static bool IsCentreField(int x, int y)
    {
        var dx = (2 * x) - (Size - 1);
        var dy = (2 * y) - (Size - 1);
        return (dx < 0 ? -dx : dx) + (dy < 0 ? -dy : dy) <= 8;
    }
}
