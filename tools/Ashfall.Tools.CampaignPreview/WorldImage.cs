using Ashfall.Campaign;

namespace Ashfall.Tools.CampaignPreview;

/// <summary>
/// Draws a campaign's world map: each region's territory (the ground nearest its centre) tinted by theatre and shaded
/// by who holds it, borders as lines between centres, and a marker per region coloured by infestation, with rings for
/// home and the hives and a diamond for each active event.
/// </summary>
internal static class WorldImage
{
    private static readonly (byte R, byte G, byte B)[] TheatreTints =
    [
        (92, 104, 84), (110, 96, 70), (84, 98, 112), (104, 84, 96), (96, 110, 104), (112, 104, 84),
    ];

    private static readonly (byte R, byte G, byte B) Liberated = (40, 100, 224);
    private static readonly (byte R, byte G, byte B) Maw = (150, 64, 210);
    private static readonly (byte R, byte G, byte B)[] Levels =
    [
        Liberated, (226, 204, 70), (232, 150, 40), (214, 70, 40), (160, 30, 50), Maw,
    ];

    public static byte[] Render(CampaignState campaign)
    {
        var world = campaign.World;
        const int width = WorldMap.Width;
        const int height = WorldMap.Height;
        var rgb = new byte[width * height * 3];
        var nearest = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var best = 0;
                var bestDistance = long.MaxValue;
                foreach (var region in world.Regions)
                {
                    long dx = region.X - x;
                    long dy = region.Y - y;
                    var distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance)
                    {
                        best = region.Id;
                        bestDistance = distance;
                    }
                }

                nearest[(y * width) + x] = best;
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var region = world.Regions[nearest[(y * width) + x]];
                var tint = TheatreTints[region.Theatre % TheatreTints.Length];
                var color = campaign.IsLiberated(region.Id)
                    ? Mix(tint, Liberated, 0.45f)
                    : Mix(tint, Maw, 0.12f * campaign.InfestationOf(region.Id));
                var edge = (x + 1 < width && nearest[(y * width) + x + 1] != region.Id)
                    || (y + 1 < height && nearest[((y + 1) * width) + x] != region.Id);
                var theatreEdge = edge
                    && ((x + 1 < width && world.Regions[nearest[(y * width) + x + 1]].Theatre != region.Theatre)
                        || (y + 1 < height && world.Regions[nearest[((y + 1) * width) + x]].Theatre != region.Theatre));
                Set(rgb, x, y, theatreEdge ? ((byte)24, (byte)22, (byte)26) : edge ? Scale(color, 0.7f) : color);
            }
        }

        foreach (var (a, b) in world.Borders)
        {
            Line(rgb, world.Regions[a].X, world.Regions[a].Y, world.Regions[b].X, world.Regions[b].Y, (200, 196, 186));
        }

        foreach (var region in world.Regions)
        {
            if (campaign.IsHive(region.Id))
            {
                Disc(rgb, region.X, region.Y, 17, (255, 72, 34));
                Disc(rgb, region.X, region.Y, 14, (20, 10, 14));
            }

            if (region.IsHome)
            {
                Disc(rgb, region.X, region.Y, 16, (240, 240, 240));
            }

            Disc(rgb, region.X, region.Y, 11, (16, 16, 20));
            var level = campaign.IsLiberated(region.Id) ? 0 : campaign.InfestationOf(region.Id);
            Disc(rgb, region.X, region.Y, 9, Levels[level]);
        }

        foreach (var campaignEvent in campaign.Events)
        {
            var region = world.Regions[campaignEvent.Region];
            Diamond(rgb, region.X, region.Y - 20, 7, (255, 210, 60));
        }

        // The global meter along the bottom edge.
        var filled = width * campaign.Meter / 100;
        for (var y = height - 8; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Set(rgb, x, y, x < filled ? Maw : ((byte)30, (byte)30, (byte)34));
            }
        }

        return rgb;
    }

    private static void Set(byte[] rgb, int x, int y, (byte R, byte G, byte B) color)
    {
        if (x < 0 || y < 0 || x >= WorldMap.Width || y >= WorldMap.Height)
        {
            return;
        }

        var i = ((y * WorldMap.Width) + x) * 3;
        rgb[i] = color.R;
        rgb[i + 1] = color.G;
        rgb[i + 2] = color.B;
    }

    private static void Disc(byte[] rgb, int cx, int cy, int radius, (byte R, byte G, byte B) color)
    {
        for (var y = -radius; y <= radius; y++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                if ((x * x) + (y * y) <= radius * radius)
                {
                    Set(rgb, cx + x, cy + y, color);
                }
            }
        }
    }

    private static void Diamond(byte[] rgb, int cx, int cy, int radius, (byte R, byte G, byte B) color)
    {
        for (var y = -radius; y <= radius; y++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                if (Math.Abs(x) + Math.Abs(y) <= radius)
                {
                    Set(rgb, cx + x, cy + y, color);
                }
            }
        }
    }

    private static void Line(byte[] rgb, int x0, int y0, int x1, int y1, (byte R, byte G, byte B) color)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            Set(rgb, x0, y0, color);
            Set(rgb, x0 + 1, y0, color);
            if (x0 == x1 && y0 == y1)
            {
                return;
            }

            var twice = 2 * error;
            if (twice >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (twice <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static (byte R, byte G, byte B) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return ((byte)(a.R + ((b.R - a.R) * t)), (byte)(a.G + ((b.G - a.G) * t)), (byte)(a.B + ((b.B - a.B) * t)));
    }

    private static (byte R, byte G, byte B) Scale((byte R, byte G, byte B) color, float factor) =>
        ((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));
}
