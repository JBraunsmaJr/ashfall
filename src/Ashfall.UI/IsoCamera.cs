using System.Numerics;

namespace Ashfall.UI;

/// <summary>
/// The fixed 2:1 dimetric camera (GDD section 5): orthographic, yaw 45°, no rotation, three zoom steps.
/// World x runs down-right on screen, world y down-left, height straight up. Converts between world
/// positions (cells, height in cells) and framebuffer pixels.
/// </summary>
public sealed class IsoCamera
{
    /// <summary>Width of one cell's diamond at zoom 1, in pixels.</summary>
    public const float TileWidth = 64f;

    /// <summary>Height of one cell's diamond at zoom 1, in pixels.</summary>
    public const float TileHeight = 32f;

    /// <summary>Pixels per cell of height at zoom 1.</summary>
    public const float HeightScale = 32f;

    private static readonly float[] ZoomSteps = [0.75f, 1f, 1.5f];

    private int _zoomStep = 1;

    public IsoCamera(int mapWidth, int mapHeight)
    {
        MapWidth = mapWidth;
        MapHeight = mapHeight;
        Center = new Vector2(mapWidth / 2f, mapHeight / 2f);
    }

    public int MapWidth { get; }

    public int MapHeight { get; }

    /// <summary>World point at the middle of the view.</summary>
    public Vector2 Center { get; private set; }

    /// <summary>The part of the framebuffer showing the world (everything left of the sidebar).</summary>
    public Vector2 ViewportOrigin { get; set; }

    public Vector2 ViewportSize { get; set; } = new(1280, 720);

    public float Zoom => ZoomSteps[_zoomStep];

    public int ZoomStep => _zoomStep;

    public static int ZoomStepCount => ZoomSteps.Length;

    private Vector2 ViewMiddle => ViewportOrigin + (ViewportSize / 2f);

    public Vector2 WorldToScreen(Vector3 world)
    {
        var relative = new Vector2(world.X - Center.X, world.Y - Center.Y);
        return ViewMiddle + new Vector2(
            (relative.X - relative.Y) * (TileWidth / 2f) * Zoom,
            ((relative.X + relative.Y) * (TileHeight / 2f) * Zoom) - (world.Z * HeightScale * Zoom));
    }

    public Vector2 WorldToScreen(Vector2 ground) => WorldToScreen(new Vector3(ground, 0));

    /// <summary>The ground point (height 0) under a screen pixel.</summary>
    public Vector2 ScreenToGround(Vector2 screen) => ScreenToGround(screen, 0f);

    /// <summary>The point at world height <paramref name="height"/> under a screen pixel.</summary>
    public Vector2 ScreenToGround(Vector2 screen, float height)
    {
        var offset = screen - ViewMiddle + new Vector2(0, height * HeightScale * Zoom);
        var a = offset.X / ((TileWidth / 2f) * Zoom);   // x - y
        var b = offset.Y / ((TileHeight / 2f) * Zoom);  // x + y
        return Center + new Vector2((a + b) / 2f, (b - a) / 2f);
    }

    /// <summary>
    /// The terrain point under a screen pixel when the ground has height: refine the flat answer a few
    /// times against <paramref name="groundHeight"/> (world z at a ground point).
    /// </summary>
    public Vector2 PickGround(Vector2 screen, Func<float, float, float> groundHeight)
    {
        ArgumentNullException.ThrowIfNull(groundHeight);
        var point = ScreenToGround(screen);
        for (var i = 0; i < 3; i++)
        {
            point = ScreenToGround(screen, groundHeight(point.X, point.Y));
        }

        return point;
    }

    public bool IsInViewport(Vector2 screen) =>
        screen.X >= ViewportOrigin.X && screen.Y >= ViewportOrigin.Y
        && screen.X < ViewportOrigin.X + ViewportSize.X && screen.Y < ViewportOrigin.Y + ViewportSize.Y;

    public void CenterOn(Vector2 world) => Center = Clamp(world);

    /// <summary>Moves the view by a screen-space distance (pixels).</summary>
    public void PanPixels(Vector2 pixels)
    {
        var a = pixels.X / ((TileWidth / 2f) * Zoom);
        var b = pixels.Y / ((TileHeight / 2f) * Zoom);
        CenterOn(Center + new Vector2((a + b) / 2f, (b - a) / 2f));
    }

    /// <summary>Steps zoom in (positive) or out (negative), keeping the world point under <paramref name="anchor"/> still.</summary>
    public void StepZoom(int direction, Vector2 anchor)
    {
        var step = Math.Clamp(_zoomStep + Math.Sign(direction), 0, ZoomSteps.Length - 1);
        if (step == _zoomStep)
        {
            return;
        }

        var before = ScreenToGround(anchor);
        _zoomStep = step;
        var after = ScreenToGround(anchor);
        CenterOn(Center + (before - after));
    }

    private Vector2 Clamp(Vector2 world) =>
        new(Math.Clamp(world.X, 0f, MapWidth), Math.Clamp(world.Y, 0f, MapHeight));
}
