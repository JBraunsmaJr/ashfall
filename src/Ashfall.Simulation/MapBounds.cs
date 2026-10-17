using Ashfall.Core;

namespace Ashfall.Simulation;

/// <summary>The playable area, in cells. Positions are clamped to [0, Width) × [0, Height).</summary>
public readonly record struct MapBounds(int Width, int Height)
{
    public bool Contains(FixVec2 point) =>
        point.X >= Fix64.Zero && point.Y >= Fix64.Zero
        && point.X < Fix64.FromInt(Width) && point.Y < Fix64.FromInt(Height);

    /// <summary>Nearest point inside the map.</summary>
    public FixVec2 Clamp(FixVec2 point)
    {
        // Largest representable value strictly inside the map edge.
        var maxX = Fix64.FromInt(Width) - Fix64.FromRaw(1);
        var maxY = Fix64.FromInt(Height) - Fix64.FromRaw(1);
        return new FixVec2(
            Fix64.Clamp(point.X, Fix64.Zero, maxX),
            Fix64.Clamp(point.Y, Fix64.Zero, maxY));
    }
}
