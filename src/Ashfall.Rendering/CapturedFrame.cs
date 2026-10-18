namespace Ashfall.Rendering;

/// <summary>A frame read back from the GPU: tightly packed 8-bit RGBA rows, top row first, sRGB-encoded.</summary>
public sealed class CapturedFrame(PixelSize size, byte[] rgba)
{
    public PixelSize Size { get; } = size;

    public byte[] Rgba { get; } = rgba;

    /// <summary>The pixel at (x, y) as (r, g, b, a).</summary>
    public (byte R, byte G, byte B, byte A) PixelAt(int x, int y)
    {
        var at = ((y * Size.Width) + x) * 4;
        return (Rgba[at], Rgba[at + 1], Rgba[at + 2], Rgba[at + 3]);
    }
}
