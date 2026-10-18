using System.Numerics;
using System.Runtime.InteropServices;

namespace Ashfall.Rendering.Scene;

/// <summary>A point light in world space, as the fragment shader reads it from its storage buffer.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct PointLight(Vector3 position, float radius, LinearColor color, float intensity)
{
    /// <summary>xyz: position in cells, w: radius in cells.</summary>
    public readonly Vector4 PositionRadius = new(position, radius);

    /// <summary>rgb: linear colour, a: intensity.</summary>
    public readonly Vector4 ColorIntensity = new(color.R, color.G, color.B, intensity);

    public static readonly int SizeInBytes = Marshal.SizeOf<PointLight>();
}
