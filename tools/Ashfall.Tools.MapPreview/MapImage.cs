using Ashfall.MapGen;
using Ashfall.Simulation.Map;

namespace Ashfall.Tools.MapPreview;

/// <summary>Paints a generated map into an RGB buffer: one block of pixels per cell, coloured by terrain, height and Cindrite.</summary>
internal static class MapImage
{
    private static readonly (byte R, byte G, byte B)[] Players = [(60, 120, 240), (220, 60, 50), (70, 190, 80), (230, 200, 60)];

    public static void Paint(byte[] rgb, int imageWidth, int left, int top, int scale, GeneratedMap generated)
    {
        var map = generated.Map;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var color = ColorOf(map, new Cell(x, y));
                Fill(rgb, imageWidth, left + (x * scale), top + (y * scale), scale, scale, color);
            }
        }

        for (var player = 0; player < generated.StartOrigins.Count; player++)
        {
            var origin = generated.StartOrigins[player];
            Fill(rgb, imageWidth, left + (origin.X * scale), top + (origin.Y * scale), 3 * scale, 3 * scale, Players[player % Players.Length]);
        }

        if (!generated.Report.Valid || generated.FellBack)
        {
            // A red bar over the top marks maps that needed the fallback.
            Fill(rgb, imageWidth, left, top, map.Width * scale, 2 * scale, (255, 0, 0));
        }
    }

    public static (byte R, byte G, byte B) ColorOf(GridMap map, Cell cell)
    {
        var level = map.HeightAt(cell);
        var shade = 18 * level;
        if (map.CindriteAt(cell) > 0)
        {
            return (255, (byte)(120 + (map.CindriteAt(cell) / 20)), 30);
        }

        return map.TerrainAt(cell) switch
        {
            Terrain.Rock => (70, 70, 78),
            Terrain.Water => (40, 80, 150),
            Terrain.Bridge => (150, 110, 70),
            Terrain.Ramp => ((byte)(150 + shade), (byte)(138 + shade), (byte)(110 + shade)),
            Terrain.Trees => (40, (byte)(100 + shade), 45),
            _ => ((byte)(110 + shade), (byte)(100 + shade), (byte)(84 + shade)),
        };
    }

    public static void Fill(byte[] rgb, int imageWidth, int left, int top, int width, int height, (byte R, byte G, byte B) color)
    {
        var imageHeight = rgb.Length / 3 / imageWidth;
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                if (x < 0 || y < 0 || x >= imageWidth || y >= imageHeight)
                {
                    continue;
                }

                var i = ((y * imageWidth) + x) * 3;
                rgb[i] = color.R;
                rgb[i + 1] = color.G;
                rgb[i + 2] = color.B;
            }
        }
    }
}
