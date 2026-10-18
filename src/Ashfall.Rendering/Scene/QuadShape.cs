namespace Ashfall.Rendering.Scene;

/// <summary>How the fragment shader shapes a quad, using its local -1..1 coordinates.</summary>
public enum QuadShape
{
    /// <summary>The whole quad.</summary>
    Solid = 0,

    /// <summary>An anti-aliased disc (or ellipse, if the quad is) inscribed in the quad.</summary>
    Disc = 1,

    /// <summary>The outer quarter of that disc: selection circles.</summary>
    Ring = 2,

    /// <summary>A soft radial glow fading to nothing at the edge: flashes, fireballs, smoke.</summary>
    Glow = 3,
}
