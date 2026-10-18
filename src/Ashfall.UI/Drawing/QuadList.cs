using Ashfall.Rendering.Scene;

namespace Ashfall.UI.Drawing;

/// <summary>A growable list of quads (four vertices each), for staging geometry before sorting.</summary>
internal sealed class QuadList
{
    private SceneVertex[] _vertices = new SceneVertex[4 * 2048];

    public int VertexCount { get; private set; }

    public ReadOnlySpan<SceneVertex> Slice(int start, int count) => _vertices.AsSpan(start, count);

    public void Clear() => VertexCount = 0;

    public void Add(in SceneVertex a, in SceneVertex b, in SceneVertex c, in SceneVertex d)
    {
        if (VertexCount + 4 > _vertices.Length)
        {
            Array.Resize(ref _vertices, _vertices.Length * 2);
        }

        _vertices[VertexCount] = a;
        _vertices[VertexCount + 1] = b;
        _vertices[VertexCount + 2] = c;
        _vertices[VertexCount + 3] = d;
        VertexCount += 4;
    }
}
