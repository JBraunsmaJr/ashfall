using Ashfall.Simulation.Map;

namespace Ashfall.MapGen;

/// <summary>
/// Exact rotational symmetry of a square grid about its centre: order 2 (half turns) for duels, order 4
/// (quarter turns) for three or four players. Rotating a cell lands exactly on another cell, so anything
/// painted in one sector and copied to its rotations is identical for every start.
/// </summary>
public sealed class MapSymmetry
{
    public MapSymmetry(int size, int order)
    {
        if (order is not (2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(order), order, "Symmetry order must be 2 or 4.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        Size = size;
        Order = order;
    }

    public int Size { get; }

    /// <summary>How many rotations map the grid onto itself: 2 or 4.</summary>
    public int Order { get; }

    /// <summary>Symmetry order for a player count: half turns for two, quarter turns for three or four.</summary>
    public static int OrderFor(int players) => players <= 2 ? 2 : 4;

    /// <summary><paramref name="cell"/> turned by <paramref name="step"/> symmetry steps (a half or a quarter turn each).</summary>
    public Cell Rotate(Cell cell, int step)
    {
        var quarters = (step % Order + Order) % Order * (4 / Order);
        var last = Size - 1;
        for (var i = 0; i < quarters; i++)
        {
            cell = new Cell(last - cell.Y, cell.X);
        }

        return cell;
    }

    /// <summary>The origin of a square footprint turned by <paramref name="step"/> steps.</summary>
    public Cell RotateFootprint(Cell origin, int footprint, int step)
    {
        // Turn the far corner too: the footprint's new origin is the smaller of the two turned corners.
        var a = Rotate(origin, step);
        var b = Rotate(new Cell(origin.X + footprint - 1, origin.Y + footprint - 1), step);
        return new Cell(a.X < b.X ? a.X : b.X, a.Y < b.Y ? a.Y : b.Y);
    }

    public bool InBounds(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Size && cell.Y < Size;
}
