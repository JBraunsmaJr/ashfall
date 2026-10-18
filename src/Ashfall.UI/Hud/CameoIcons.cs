using System.Numerics;
using Ashfall.Content;
using Ashfall.Rendering;
using Ashfall.UI.Drawing;
using Ashfall.UI.Text;

namespace Ashfall.UI.Hud;

/// <summary>
/// Placeholder cameo art until cooked sprites arrive: each structure and unit drawn from a few flat shapes, in the
/// same colours the world uses for it, with the player's colour as the team marking. Anything without a drawing
/// gets its initial.
/// </summary>
public static class CameoIcons
{
    /// <summary>Draws the icon for <paramref name="id"/> into the box; <paramref name="dim"/> greys it for unavailable cameos.</summary>
    public static void Draw(Canvas canvas, string id, float x, float y, float width, float height, LinearColor team, bool dim)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var icon = new Icon(canvas, x, y, width, height, dim);

        // A strip of ash ground for everything to stand on, and a dusk sky behind.
        icon.Band(0, 1, LinearColor.FromSrgb(40, 44, 52));
        icon.Band(0.78f, 0.22f, Palette.GroundDark);
        switch (id)
        {
            case BulwarkContent.PowerPlant:
                icon.Box(0.18f, 0.55f, 0.64f, 0.25f, Palette.ConcreteDark);
                Tower(icon, 0.28f);
                Tower(icon, 0.56f);
                icon.Fill(0.18f, 0.6f, 0.64f, 0.04f, team);
                break;

            case BulwarkContent.Refinery:
                icon.Box(0.12f, 0.42f, 0.52f, 0.38f, Palette.Concrete);
                icon.Fill(0.12f, 0.48f, 0.52f, 0.04f, team);
                icon.Box(0.66f, 0.24f, 0.18f, 0.56f, Palette.Metal);
                icon.Oval(0.75f, 0.24f, 0.09f, 0.05f, Palette.ConcreteDark);
                icon.Fill(0.5f, 0.62f, 0.3f, 0.06f, Palette.Metal);
                icon.Oval(0.36f, 0.66f, 0.1f, 0.06f, Palette.Cindrite, glow: true);
                break;

            case BulwarkContent.Barracks:
                icon.Box(0.2f, 0.5f, 0.56f, 0.3f, Palette.Concrete);
                icon.Fill(0.2f, 0.44f, 0.56f, 0.08f, Palette.ConcreteDark);
                icon.Fill(0.42f, 0.62f, 0.12f, 0.18f, Palette.Metal);
                icon.Fill(0.8f, 0.18f, 0.02f, 0.62f, Palette.Metal);
                icon.Fill(0.82f, 0.18f, 0.12f, 0.1f, team);
                break;

            case BulwarkContent.Factory:
                icon.Box(0.1f, 0.36f, 0.8f, 0.44f, Palette.Concrete);
                icon.Fill(0.1f, 0.4f, 0.8f, 0.05f, team);
                icon.Fill(0.3f, 0.52f, 0.4f, 0.28f, Palette.Metal);
                for (var slat = 0; slat < 4; slat++)
                {
                    icon.Fill(0.3f, 0.55f + (slat * 0.065f), 0.4f, 0.02f, Palette.ConcreteDark);
                }

                icon.Fill(0.74f, 0.2f, 0.08f, 0.18f, Palette.ConcreteDark);
                break;

            case BulwarkContent.Wall:
                for (var row = 0; row < 3; row++)
                {
                    var offset = row % 2 == 0 ? 0f : 0.09f;
                    for (var brick = 0; brick < 4; brick++)
                    {
                        var left = 0.14f + offset + (brick * 0.18f);
                        if (left + 0.16f <= 0.9f)
                        {
                            icon.Box(left, 0.5f + (row * 0.1f), 0.16f, 0.08f, Palette.Wall);
                        }
                    }
                }

                break;

            case BulwarkContent.Rifleman:
                Soldier(icon, 0.5f, team);
                icon.Line(0.52f, 0.5f, 0.74f, 0.42f, 0.03f, Palette.Metal);
                break;

            case BulwarkContent.Rocketeer:
                Soldier(icon, 0.5f, team);
                icon.Line(0.34f, 0.38f, 0.7f, 0.3f, 0.06f, Palette.Metal);
                icon.Oval(0.72f, 0.3f, 0.03f, 0.03f, Palette.Muzzle, glow: true);
                break;

            case BulwarkContent.Pyro:
                Soldier(icon, 0.42f, team);
                icon.Line(0.44f, 0.52f, 0.6f, 0.5f, 0.04f, Palette.Metal);
                icon.Oval(0.68f, 0.49f, 0.08f, 0.06f, Palette.Flame, glow: true);
                icon.Oval(0.78f, 0.47f, 0.06f, 0.05f, Palette.CindriteHot, glow: true);
                break;

            case BulwarkContent.Warden:
                icon.Box(0.16f, 0.62f, 0.68f, 0.14f, Palette.Metal);
                icon.Box(0.2f, 0.5f, 0.6f, 0.14f, team);
                icon.Box(0.36f, 0.38f, 0.26f, 0.13f, Palette.Scale(team, 0.8f));
                icon.Line(0.6f, 0.44f, 0.88f, 0.44f, 0.04f, Palette.Metal);
                break;

            case BulwarkContent.Harvester:
                icon.Box(0.12f, 0.64f, 0.72f, 0.12f, Palette.Metal);
                icon.Box(0.14f, 0.42f, 0.5f, 0.24f, Palette.HarvesterYellow);
                icon.Oval(0.36f, 0.42f, 0.18f, 0.06f, Palette.Cindrite, glow: true);
                icon.Box(0.64f, 0.48f, 0.18f, 0.18f, Palette.HarvesterYellow);
                icon.Fill(0.68f, 0.51f, 0.1f, 0.06f, Palette.PowerGlow);
                icon.Fill(0.14f, 0.6f, 0.5f, 0.03f, team);
                break;

            default:
                var letter = id.Split('.')[^1][..1].ToUpperInvariant();
                var scale = MathF.Max(1f, MathF.Floor(height / (PixelFont.GlyphHeight * 1.6f)));
                canvas.TextCentered(x + (width / 2f), y + ((height - (PixelFont.GlyphHeight * scale)) / 2f), letter, scale, icon.Tint(Palette.Text));
                break;
        }
    }

    // A cooling tower: a stack of narrowing slabs, with a power glow at the lip.
    private static void Tower(Icon icon, float left)
    {
        for (var band = 0; band < 4; band++)
        {
            var inset = band * 0.015f;
            icon.Fill(left + inset, 0.3f + (band * 0.07f), 0.16f - (2 * inset), 0.07f, band % 2 == 0 ? Palette.Concrete : Palette.ConcreteDark);
        }

        icon.Fill(left + 0.04f, 0.27f, 0.08f, 0.03f, Palette.PowerGlow);
    }

    // A soldier facing right: legs, team-coloured body, head.
    private static void Soldier(Icon icon, float center, LinearColor team)
    {
        icon.Fill(center - 0.05f, 0.62f, 0.035f, 0.16f, Palette.Metal);
        icon.Fill(center + 0.015f, 0.62f, 0.035f, 0.16f, Palette.Metal);
        icon.Fill(center - 0.06f, 0.4f, 0.12f, 0.23f, team);
        icon.Oval(center, 0.33f, 0.05f, 0.06f, LinearColor.FromSrgb(214, 180, 150));
        icon.Fill(center - 0.055f, 0.26f, 0.11f, 0.04f, Palette.Metal);
    }

    /// <summary>Drawing in the icon's own 0..1 square: the box is fitted to the cameo's height and centred across it.</summary>
    private readonly struct Icon(Canvas canvas, float x, float y, float width, float height, bool dim)
    {
        private float Side => MathF.Min(width, height);

        private float Left => x + ((width - Side) / 2f);

        public LinearColor Tint(LinearColor color) =>
            dim ? Palette.Scale(Palette.Mix(color, LinearColor.FromSrgb(110, 110, 110), 0.7f), 0.55f) : color;

        // A band across the whole cameo (sky, ground), measured in the cameo's height.
        public void Band(float fy, float fh, LinearColor color) => canvas.Rect(x, y + (fy * height), width, fh * height, Tint(color));

        public void Fill(float fx, float fy, float fw, float fh, LinearColor color) =>
            canvas.Rect(Left + (fx * Side), y + (fy * Side) + ((height - Side) / 2f), fw * Side, fh * Side, Tint(color));

        // A block with a lit top edge and a shaded right side, so shapes read as solid.
        public void Box(float fx, float fy, float fw, float fh, LinearColor color)
        {
            Fill(fx, fy, fw, fh, color);
            Fill(fx, fy, fw, MathF.Min(0.025f, fh / 3f), Palette.Mix(color, Palette.Text, 0.35f));
            Fill(fx + fw - 0.025f, fy, 0.025f, fh, Palette.Scale(color, 0.7f));
        }

        public void Oval(float cx, float cy, float rx, float ry, LinearColor color, bool glow = false)
        {
            var center = new Vector2(Left + (cx * Side), y + (cy * Side) + ((height - Side) / 2f));
            if (glow && !dim)
            {
                canvas.Disc(center, rx * Side * 1.8f, ry * Side * 1.8f, Palette.WithAlpha(color, 0.25f));
            }

            canvas.Disc(center, rx * Side, ry * Side, Tint(color));
        }

        public void Line(float x1, float y1, float x2, float y2, float thickness, LinearColor color)
        {
            var offset = (height - Side) / 2f;
            canvas.Line(
                new Vector2(Left + (x1 * Side), y + (y1 * Side) + offset),
                new Vector2(Left + (x2 * Side), y + (y2 * Side) + offset),
                thickness * Side,
                Tint(color));
        }
    }
}
