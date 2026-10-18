using System.Numerics;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.UI.Text;

namespace Ashfall.UI.Drawing;

/// <summary>
/// Screen-space drawing for the interface: unlit rectangles, lines, discs and pixel-font text, appended
/// straight to the frame scene in call order (later calls draw on top).
/// </summary>
public sealed class Canvas(FrameScene scene)
{
    private static readonly Vector3 Up = Vector3.UnitZ;

    public FrameScene Scene { get; } = scene;

    public void Rect(float x, float y, float width, float height, LinearColor color) =>
        Quad(new Vector2(x, y), new Vector2(x + width, y), new Vector2(x + width, y + height), new Vector2(x, y + height), color, QuadShape.Solid);

    /// <summary>A one-pixel-wide (times <paramref name="thickness"/>) rectangle outline.</summary>
    public void Frame(float x, float y, float width, float height, float thickness, LinearColor color)
    {
        Rect(x, y, width, thickness, color);
        Rect(x, y + height - thickness, width, thickness, color);
        Rect(x, y + thickness, thickness, height - (2 * thickness), color);
        Rect(x + width - thickness, y + thickness, thickness, height - (2 * thickness), color);
    }

    public void Line(Vector2 from, Vector2 to, float thickness, LinearColor color)
    {
        var direction = to - from;
        var length = direction.Length();
        if (length < 0.001f)
        {
            return;
        }

        var side = new Vector2(-direction.Y, direction.X) / length * (thickness / 2f);
        Quad(from + side, to + side, to - side, from - side, color, QuadShape.Solid);
    }

    public void Disc(Vector2 center, float radiusX, float radiusY, LinearColor color, QuadShape shape = QuadShape.Disc) =>
        Quad(
            center + new Vector2(-radiusX, -radiusY),
            center + new Vector2(radiusX, -radiusY),
            center + new Vector2(radiusX, radiusY),
            center + new Vector2(-radiusX, radiusY),
            color,
            shape);

    /// <summary>Draws <paramref name="text"/> with its top-left at (x, y); returns the width drawn.</summary>
    public float Text(float x, float y, string text, float scale, LinearColor color)
    {
        var cursor = x;
        foreach (var c in text)
        {
            var rows = PixelFont.Rows(c);
            for (var row = 0; row < PixelFont.GlyphHeight; row++)
            {
                // One quad per horizontal run of lit pixels.
                var bits = rows[row];
                var column = 0;
                while (column < PixelFont.GlyphWidth)
                {
                    if ((bits & (1 << (PixelFont.GlyphWidth - 1 - column))) == 0)
                    {
                        column++;
                        continue;
                    }

                    var start = column;
                    while (column < PixelFont.GlyphWidth && (bits & (1 << (PixelFont.GlyphWidth - 1 - column))) != 0)
                    {
                        column++;
                    }

                    Rect(cursor + (start * scale), y + (row * scale), (column - start) * scale, scale, color);
                }
            }

            cursor += PixelFont.Advance * scale;
        }

        return cursor - x;
    }

    public void TextCentered(float centerX, float y, string text, float scale, LinearColor color) =>
        Text(centerX - (PixelFont.Measure(text, scale) / 2f), y, text, scale, color);

    public void TextRight(float right, float y, string text, float scale, LinearColor color) =>
        Text(right - PixelFont.Measure(text, scale), y, text, scale, color);

    /// <summary>Text with a one-pixel drop shadow, for labels drawn over the world.</summary>
    public void TextShadowed(float x, float y, string text, float scale, LinearColor color)
    {
        Text(x + scale, y + scale, text, scale, new LinearColor(0, 0, 0, 0.8f));
        Text(x, y, text, scale, color);
    }

    private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, LinearColor color, QuadShape shape)
    {
        var tint = new Vector4(color.R, color.G, color.B, color.A);
        var extra = new Vector3((float)shape, 1f, 1f); // unlit, fully visible
        Scene.AddQuad(
            new SceneVertex { Screen = a, Normal = Up, Color = tint, Local = new Vector2(-1, -1), Extra = extra },
            new SceneVertex { Screen = b, Normal = Up, Color = tint, Local = new Vector2(1, -1), Extra = extra },
            new SceneVertex { Screen = c, Normal = Up, Color = tint, Local = new Vector2(1, 1), Extra = extra },
            new SceneVertex { Screen = d, Normal = Up, Color = tint, Local = new Vector2(-1, 1), Extra = extra });
    }
}
