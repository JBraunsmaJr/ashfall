using Ashfall.Simulation.Vision;

namespace Ashfall.UI.World;

/// <summary>
/// The local player's fog of war as a smooth field: each cell is 0 (shroud), 0.5 (fog) or 1 (visible)
/// at its centre, interpolated bilinearly in between, so fog edges blend across a cell instead of
/// stepping.
/// </summary>
internal sealed class FogField
{
    private float[] _values = [];
    private int _width;
    private int _height;

    /// <summary>When false (no local player, or spectating), everything is visible.</summary>
    public bool Enabled { get; private set; }

    public void Update(VisibilityMap visibility, int player, int width, int height)
    {
        Enabled = player >= 0 && player < visibility.Players;
        if (!Enabled)
        {
            return;
        }

        if (_values.Length != width * height)
        {
            _values = new float[width * height];
        }

        _width = width;
        _height = height;
        var cells = visibility.Cells(player);
        for (var i = 0; i < cells.Length; i++)
        {
            _values[i] = cells[i] switch
            {
                CellVisibility.Visible => 1f,
                CellVisibility.Fog => 0.5f,
                _ => 0f,
            };
        }
    }

    /// <summary>The value at a cell (outside the map: shroud).</summary>
    public float At(int x, int y) =>
        !Enabled ? 1f
        : x < 0 || y < 0 || x >= _width || y >= _height ? 0f
        : _values[(y * _width) + x];

    /// <summary>Smoothly interpolated value at a world point.</summary>
    public float Sample(float x, float y)
    {
        if (!Enabled)
        {
            return 1f;
        }

        var fx = x - 0.5f;
        var fy = y - 0.5f;
        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;
        var top = Lerp(Clamped(x0, y0), Clamped(x0 + 1, y0), tx);
        var bottom = Lerp(Clamped(x0, y0 + 1), Clamped(x0 + 1, y0 + 1), tx);
        return Lerp(top, bottom, ty);
    }

    // Edge cells extend to the map border rather than fading to shroud outside it.
    private float Clamped(int x, int y) => At(Math.Clamp(x, 0, _width - 1), Math.Clamp(y, 0, _height - 1));

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
