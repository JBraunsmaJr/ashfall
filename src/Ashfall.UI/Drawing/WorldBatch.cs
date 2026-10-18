using System.Numerics;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;

namespace Ashfall.UI.Drawing;

/// <summary>
/// Collects world geometry as objects, each a run of quads with an isometric depth, then emits them
/// back to front. Primitives take world coordinates (cells, height in cells) and are projected by the
/// camera; every vertex carries its world position and normal for lighting and a fog visibility.
/// </summary>
internal sealed class WorldBatch
{
    private readonly QuadList _quads = new();
    private readonly List<(float Depth, int Sequence, int Start, int Count)> _objects = [];
    private int _objectStart = -1;
    private float _objectDepth;

    public IsoCamera Camera { get; set; } = null!;

    /// <summary>Fog visibility (0 shroud, 0.5 fog, 1 visible) at a world point; null means fully visible.</summary>
    public Func<float, float, float>? Visibility { get; set; }

    public void Clear()
    {
        _quads.Clear();
        _objects.Clear();
        _objectStart = -1;
    }

    /// <summary>Starts an object; <paramref name="depth"/> is x + y of its ground position (larger is nearer the viewer).</summary>
    public void Begin(float depth)
    {
        _objectStart = _quads.VertexCount;
        _objectDepth = depth;
    }

    public void End()
    {
        if (_objectStart >= 0 && _quads.VertexCount > _objectStart)
        {
            _objects.Add((_objectDepth, _objects.Count, _objectStart, _quads.VertexCount - _objectStart));
        }

        _objectStart = -1;
    }

    /// <summary>Appends every object to <paramref name="scene"/>, far to near (stable for equal depth).</summary>
    public void FlushTo(FrameScene scene)
    {
        _objects.Sort(static (a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : a.Sequence.CompareTo(b.Sequence));
        foreach (var (_, _, start, count) in _objects)
        {
            scene.AddQuads(_quads.Slice(start, count));
        }

        Clear();
    }

    /// <summary>A quad through four world points (a → b → c → d) facing <paramref name="normal"/>.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, LinearColor color, float emissive = 0f, QuadShape shape = QuadShape.Solid)
    {
        var tint = new Vector4(color.R, color.G, color.B, color.A);
        _quads.Add(
            Vertex(a, normal, tint, new Vector2(-1, -1), shape, emissive),
            Vertex(b, normal, tint, new Vector2(1, -1), shape, emissive),
            Vertex(c, normal, tint, new Vector2(1, 1), shape, emissive),
            Vertex(d, normal, tint, new Vector2(-1, 1), shape, emissive));
    }

    /// <summary>A flat axis-aligned rectangle on the ground (or at height <paramref name="z"/>).</summary>
    public void Flat(float x, float y, float width, float height, float z, LinearColor color, float emissive = 0f, QuadShape shape = QuadShape.Solid) =>
        Quad(
            new Vector3(x, y, z),
            new Vector3(x + width, y, z),
            new Vector3(x + width, y + height, z),
            new Vector3(x, y + height, z),
            Vector3.UnitZ,
            color,
            emissive,
            shape);

    /// <summary>A disc (ellipse on screen) lying at height <paramref name="z"/>.</summary>
    public void Disc(Vector2 center, float radius, float z, LinearColor color, float emissive = 0f, QuadShape shape = QuadShape.Disc) =>
        Flat(center.X - radius, center.Y - radius, radius * 2, radius * 2, z, color, emissive, shape);

    /// <summary>
    /// An axis-aligned box: the two side faces the camera can see (+x and +y), then the top.
    /// </summary>
    public void Box(float x, float y, float width, float depth, float bottom, float top, LinearColor sides, LinearColor topColor, float emissive = 0f)
    {
        var x1 = x + width;
        var y1 = y + depth;

        // +y face (screen lower-left).
        Quad(new Vector3(x, y1, bottom), new Vector3(x1, y1, bottom), new Vector3(x1, y1, top), new Vector3(x, y1, top), Vector3.UnitY, sides, emissive);

        // +x face (screen lower-right).
        Quad(new Vector3(x1, y, bottom), new Vector3(x1, y1, bottom), new Vector3(x1, y1, top), new Vector3(x1, y, top), Vector3.UnitX, sides, emissive);
        Flat(x, y, width, depth, top, topColor, emissive);
    }

    /// <summary>
    /// A box rotated by <paramref name="angle"/> (radians, 0 = facing +x) around its centre: the side
    /// faces turned toward the camera, then the top.
    /// </summary>
    public void RotatedBox(Vector2 center, float length, float width, float angle, float bottom, float top, LinearColor sides, LinearColor topColor, float emissive = 0f)
    {
        var forward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var right = new Vector2(-forward.Y, forward.X);
        var f = forward * (length / 2f);
        var r = right * (width / 2f);
        Span<Vector2> corners = [center + f + r, center + f - r, center - f - r, center - f + r];

        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            var edge = b - a;
            var outward = Vector2.Normalize(new Vector2(edge.Y, -edge.X));

            // Make the normal point away from the centre whatever the winding.
            if (Vector2.Dot(outward, ((a + b) / 2f) - center) < 0)
            {
                outward = -outward;
            }

            // Faces whose normal points toward screen-down (+x +y) face the viewer.
            if (outward.X + outward.Y <= 0)
            {
                continue;
            }

            Quad(new Vector3(a, bottom), new Vector3(b, bottom), new Vector3(b, top), new Vector3(a, top), new Vector3(outward, 0), sides, emissive);
        }

        Quad(new Vector3(corners[0], top), new Vector3(corners[1], top), new Vector3(corners[2], top), new Vector3(corners[3], top), Vector3.UnitZ, topColor, emissive);
    }

    /// <summary>An outline of an axis-aligned ground rectangle, <paramref name="thickness"/> cells wide.</summary>
    public void Outline(float x, float y, float width, float height, float thickness, float z, LinearColor color, float emissive = 1f)
    {
        Flat(x, y, width, thickness, z, color, emissive);
        Flat(x, y + height - thickness, width, thickness, z, color, emissive);
        Flat(x, y + thickness, thickness, height - (2 * thickness), z, color, emissive);
        Flat(x + width - thickness, y + thickness, thickness, height - (2 * thickness), z, color, emissive);
    }

    /// <summary>A screen-facing disc centred on a world point (fireballs, flashes), <paramref name="pixels"/> in radius at zoom 1.</summary>
    public void Billboard(Vector3 center, float pixels, LinearColor color, float emissive = 1f, QuadShape shape = QuadShape.Glow)
    {
        var middle = Camera.WorldToScreen(center);
        var radius = pixels * Camera.Zoom;
        var tint = new Vector4(color.R, color.G, color.B, color.A);
        var extra = new Vector3((float)shape, emissive, Visible(center));
        var toViewer = Vector3.Normalize(new Vector3(1, 1, 1.2f));
        _quads.Add(
            new SceneVertex { Screen = middle + new Vector2(-radius, -radius), World = center, Normal = toViewer, Color = tint, Local = new Vector2(-1, -1), Extra = extra },
            new SceneVertex { Screen = middle + new Vector2(radius, -radius), World = center, Normal = toViewer, Color = tint, Local = new Vector2(1, -1), Extra = extra },
            new SceneVertex { Screen = middle + new Vector2(radius, radius), World = center, Normal = toViewer, Color = tint, Local = new Vector2(1, 1), Extra = extra },
            new SceneVertex { Screen = middle + new Vector2(-radius, radius), World = center, Normal = toViewer, Color = tint, Local = new Vector2(-1, 1), Extra = extra });
    }

    /// <summary>A screen-facing line between two world points, <paramref name="pixels"/> wide.</summary>
    public void Beam(Vector3 from, Vector3 to, float pixels, LinearColor color)
    {
        var a = Camera.WorldToScreen(from);
        var b = Camera.WorldToScreen(to);
        var direction = b - a;
        var length = direction.Length();
        if (length < 0.5f)
        {
            return;
        }

        var side = new Vector2(-direction.Y, direction.X) / length * (pixels / 2f);
        var tint = new Vector4(color.R, color.G, color.B, color.A);
        var extra = new Vector3(0, 1, Visible(from));
        _quads.Add(
            new SceneVertex { Screen = a + side, World = from, Normal = Vector3.UnitZ, Color = tint, Extra = extra },
            new SceneVertex { Screen = b + side, World = to, Normal = Vector3.UnitZ, Color = tint, Extra = extra },
            new SceneVertex { Screen = b - side, World = to, Normal = Vector3.UnitZ, Color = tint, Extra = extra },
            new SceneVertex { Screen = a - side, World = from, Normal = Vector3.UnitZ, Color = tint, Extra = extra });
    }

    private float Visible(Vector3 world) => Visibility?.Invoke(world.X, world.Y) ?? 1f;

    private SceneVertex Vertex(Vector3 world, Vector3 normal, Vector4 color, Vector2 local, QuadShape shape, float emissive) => new()
    {
        Screen = Camera.WorldToScreen(world),
        World = world,
        Normal = normal,
        Color = color,
        Local = local,
        Extra = new Vector3((float)shape, emissive, Visible(world)),
    };
}
