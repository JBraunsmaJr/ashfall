using System.Numerics;

namespace Ashfall.UI.Hud;

/// <summary>Where everything in the right-hand sidebar sits for a given framebuffer size.</summary>
public readonly record struct SidebarLayout(float X, float Width, float Height, float Scale)
{
    public const float BaseWidth = 248f;

    public float Padding => 8f * Scale;

    public float TextScale => 2f * Scale;

    public float CreditsY => Padding;

    public float PowerY => CreditsY + (20f * Scale);

    public float MinimapY => PowerY + (28f * Scale);

    public float MinimapSize => Width - (2 * Padding);

    public float ButtonsY => MinimapY + MinimapSize + (10f * Scale);

    public float ButtonHeight => 26f * Scale;

    public float ButtonGap => 4f * Scale;

    public float HeaderHeight => 18f * Scale;

    public static SidebarLayout For(Vector2 framebuffer)
    {
        var scale = framebuffer.Y >= 1300 ? 2f : framebuffer.Y >= 960 ? 1.5f : 1f;
        var width = BaseWidth * scale;
        return new SidebarLayout(framebuffer.X - width, width, framebuffer.Y, scale);
    }

    public bool Contains(Vector2 point) => point.X >= X;

    public (float X, float Y, float Size) Minimap => (X + Padding, MinimapY, MinimapSize);
}
