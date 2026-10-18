using Ashfall.Core;

namespace Ashfall.Simulation.Map;

/// <summary>A cell on the map grid. Cell (x, y) covers positions [x, x+1) × [y, y+1).</summary>
public readonly record struct Cell(int X, int Y)
{
    /// <summary>The middle of the cell, where paths route units.</summary>
    public FixVec2 Center => new(Fix64.FromInt(X) + Fix64.Half, Fix64.FromInt(Y) + Fix64.Half);

    /// <summary>The cell containing <paramref name="position"/>.</summary>
    public static Cell Containing(FixVec2 position) => new(position.X.FloorToInt(), position.Y.FloorToInt());

    /// <summary>Chebyshev distance: the number of king moves between two cells.</summary>
    public static int ChebyshevDistance(Cell a, Cell b) => Max(Abs(a.X - b.X), Abs(a.Y - b.Y));

    public static Cell operator +(Cell a, Cell b) => new(a.X + b.X, a.Y + b.Y);

    public override string ToString() => $"({X}, {Y})";

    private static int Abs(int value) => value < 0 ? -value : value;

    private static int Max(int a, int b) => a > b ? a : b;
}
