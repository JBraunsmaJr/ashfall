using System.Numerics;
using System.Runtime.InteropServices;

namespace Ashfall.Rendering.Scene;

/// <summary>
/// One corner of a scene quad, laid out exactly as the vertex shader reads it (Shaders/Scene.slang).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceneVertex
{
    /// <summary>Position on screen in pixels, origin top-left.</summary>
    public Vector2 Screen;

    /// <summary>Position in the world (cells, height above ground) for lighting.</summary>
    public Vector3 World;

    /// <summary>World-space surface normal.</summary>
    public Vector3 Normal;

    /// <summary>Linear RGB with straight alpha.</summary>
    public Vector4 Color;

    /// <summary>-1..1 across the quad; shapes discs and rings.</summary>
    public Vector2 Local;

    /// <summary>x: <see cref="QuadShape"/>, y: emissive (0 lit .. 1 unlit), z: fog visibility (0 shroud .. 0.5 fog .. 1 visible).</summary>
    public Vector3 Extra;

    /// <summary>Size of one vertex in bytes: the vertex buffer stride.</summary>
    public static readonly int SizeInBytes = Marshal.SizeOf<SceneVertex>();
}
