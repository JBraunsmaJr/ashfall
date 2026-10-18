using System.Numerics;

namespace Ashfall.Rendering.Scene;

/// <summary>
/// Everything the renderer draws in one frame: quads in painter's order (first drawn first), point
/// lights and the frame's lighting environment. Built on the CPU each frame by the UI layer, then
/// uploaded as-is. Reuse one instance across frames: <see cref="Clear"/> keeps its buffers.
/// </summary>
public sealed class FrameScene
{
    /// <summary>The renderer's light buffer holds this many; extra lights are dropped (nearest-first is the builder's job).</summary>
    public const int MaxLights = 256;

    private SceneVertex[] _vertices = new SceneVertex[4 * 1024];
    private readonly List<PointLight> _lights = [];

    public LinearColor ClearColor { get; set; } = LinearColor.Black;

    /// <summary>Unit vector pointing towards the sun, in world space (z up).</summary>
    public Vector3 SunDirection { get; set; } = Vector3.Normalize(new Vector3(-0.55f, 0.15f, 0.82f));

    /// <summary>Sun colour times intensity.</summary>
    public LinearColor SunColor { get; set; } = new(0.95f, 0.85f, 0.7f);

    public LinearColor Ambient { get; set; } = new(0.35f, 0.36f, 0.42f);

    /// <summary>Seconds since start, for animated effects in shaders.</summary>
    public float Time { get; set; }

    public int QuadCount { get; private set; }

    public ReadOnlySpan<SceneVertex> Vertices => _vertices.AsSpan(0, QuadCount * 4);

    public IReadOnlyList<PointLight> Lights => _lights;

    public void Clear()
    {
        QuadCount = 0;
        _lights.Clear();
    }

    /// <summary>Adds a quad; corners go round the edge (a → b → c → d), either winding.</summary>
    public void AddQuad(in SceneVertex a, in SceneVertex b, in SceneVertex c, in SceneVertex d)
    {
        EnsureCapacity((QuadCount + 1) * 4);
        var at = QuadCount * 4;
        _vertices[at] = a;
        _vertices[at + 1] = b;
        _vertices[at + 2] = c;
        _vertices[at + 3] = d;
        QuadCount++;
    }

    /// <summary>Appends quads already built elsewhere (four vertices each).</summary>
    public void AddQuads(ReadOnlySpan<SceneVertex> vertices)
    {
        if (vertices.Length % 4 != 0)
        {
            throw new ArgumentException("Quads have four vertices each.", nameof(vertices));
        }

        EnsureCapacity((QuadCount * 4) + vertices.Length);
        vertices.CopyTo(_vertices.AsSpan(QuadCount * 4));
        QuadCount += vertices.Length / 4;
    }

    /// <summary>Adds a light; returns false (and drops it) once <see cref="MaxLights"/> is reached.</summary>
    public bool AddLight(in PointLight light)
    {
        if (_lights.Count >= MaxLights)
        {
            return false;
        }

        _lights.Add(light);
        return true;
    }

    private void EnsureCapacity(int vertices)
    {
        if (vertices > _vertices.Length)
        {
            Array.Resize(ref _vertices, Math.Max(vertices, _vertices.Length * 2));
        }
    }
}
