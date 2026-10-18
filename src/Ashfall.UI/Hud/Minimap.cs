using System.Numerics;
using Ashfall.Rendering;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;
using Ashfall.UI.Drawing;
using Ashfall.UI.World;

namespace Ashfall.UI.Hud;

/// <summary>
/// The radar: a top-down view of the whole map under the local player's fog, with units and
/// structures as dots and the camera's view outlined. Clicking it moves the camera.
/// </summary>
internal static class Minimap
{
    public static void Draw(Canvas canvas, GameSimulation simulation, int player, FogField fog, IsoCamera camera, (float X, float Y, float Size) area)
    {
        var map = simulation.Map;
        var cell = area.Size / Math.Max(map.Width, map.Height);
        canvas.Rect(area.X - 2, area.Y - 2, area.Size + 4, area.Size + 4, Palette.PanelEdge);
        canvas.Rect(area.X, area.Y, area.Size, area.Size, Palette.Clear);

        // Terrain, merged into horizontal runs of equal colour.
        for (var y = 0; y < map.Height; y++)
        {
            var runStart = 0;
            var runColor = CellColor(map, fog, 0, y);
            for (var x = 1; x <= map.Width; x++)
            {
                var color = x < map.Width ? CellColor(map, fog, x, y) : default;
                if (x < map.Width && color == runColor)
                {
                    continue;
                }

                if (runColor.A > 0)
                {
                    canvas.Rect(area.X + (runStart * cell), area.Y + (y * cell), (x - runStart) * cell, cell, runColor);
                }

                runStart = x;
                runColor = color;
            }
        }

        var world = simulation.World;
        foreach (var index in world.Store<StructureInfo>().EntityIndices)
        {
            var info = world.Store<StructureInfo>().Get(index);
            var owner = world.Store<Owner>().Get(index).PlayerId;
            var definition = simulation.Content.Structure(info.StructureType);
            if (owner != player && fog.At(info.Origin.X, info.Origin.Y) <= 0f)
            {
                continue;
            }

            canvas.Rect(area.X + (info.Origin.X * cell), area.Y + (info.Origin.Y * cell), definition.Width * cell, definition.Height * cell, Palette.Player(owner));
        }

        var dot = Math.Max(2f, cell * 1.2f);
        foreach (var index in world.Store<UnitInfo>().EntityIndices)
        {
            var position = MotionTracker.ToVector(world.Store<Position>().Get(index).Value);
            var owner = world.Store<Owner>().Get(index).PlayerId;
            if (owner != player && fog.Sample(position.X, position.Y) < 0.75f)
            {
                continue;
            }

            canvas.Rect(area.X + (position.X * cell) - (dot / 2), area.Y + (position.Y * cell) - (dot / 2), dot, dot, Palette.Scale(Palette.Player(owner), 1.3f));
        }

        // The camera's view: the ground under the viewport's four corners.
        var origin = camera.ViewportOrigin;
        var size = camera.ViewportSize;
        Span<Vector2> corners =
        [
            camera.ScreenToGround(origin),
            camera.ScreenToGround(origin + new Vector2(size.X, 0)),
            camera.ScreenToGround(origin + size),
            camera.ScreenToGround(origin + new Vector2(0, size.Y)),
        ];
        for (var i = 0; i < 4; i++)
        {
            var a = Clamp(corners[i], map) * cell;
            var b = Clamp(corners[(i + 1) % 4], map) * cell;
            canvas.Line(new Vector2(area.X, area.Y) + a, new Vector2(area.X, area.Y) + b, 1.5f, Palette.Text);
        }
    }

    /// <summary>The world point under a minimap pixel.</summary>
    public static Vector2 ToWorld(Vector2 point, GameSimulation simulation, (float X, float Y, float Size) area)
    {
        var map = simulation.Map;
        var cell = area.Size / Math.Max(map.Width, map.Height);
        return new Vector2((point.X - area.X) / cell, (point.Y - area.Y) / cell);
    }

    private static Vector2 Clamp(Vector2 point, GridMap map) =>
        new(Math.Clamp(point.X, 0, map.Width), Math.Clamp(point.Y, 0, map.Height));

    private static LinearColor CellColor(GridMap map, FogField fog, int x, int y)
    {
        var visibility = fog.At(x, y);
        if (visibility <= 0f)
        {
            return default; // shroud: leave the black background
        }

        var cell = new Cell(x, y);
        var color = map.TerrainAt(cell) == Terrain.Rock ? Palette.RockTop
            : map.CindriteAt(cell) > 0 ? Palette.Cindrite
            : Palette.Ground;
        return visibility < 1f ? Palette.Scale(color, 0.45f) : color;
    }
}
