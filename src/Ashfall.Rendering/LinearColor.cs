namespace Ashfall.Rendering;

/// <summary>
/// A colour in linear space, 0..1 per channel. The swapchain is sRGB, so the hardware applies the
/// sRGB curve on write: specify colours here as linear values.
/// </summary>
public readonly record struct LinearColor(float R, float G, float B, float A = 1f)
{
    public static LinearColor Black => new(0f, 0f, 0f);

    /// <summary>Converts an sRGB-encoded colour (as picked in an image editor, 0..255) to linear.</summary>
    public static LinearColor FromSrgb(byte r, byte g, byte b, byte a = 255) =>
        new(SrgbToLinear(r / 255f), SrgbToLinear(g / 255f), SrgbToLinear(b / 255f), a / 255f);

    private static float SrgbToLinear(float c) =>
        c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
}
