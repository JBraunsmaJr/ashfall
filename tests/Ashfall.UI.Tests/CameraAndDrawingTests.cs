using System.Numerics;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.UI.Drawing;
using Ashfall.UI.Input;
using Ashfall.UI.Text;

namespace Ashfall.UI.Tests;

public class CameraAndDrawingTests
{
    [Fact]
    public void Screen_and_ground_positions_round_trip_at_every_zoom()
    {
        var camera = new IsoCamera(64, 64) { ViewportSize = new Vector2(1000, 700) };
        camera.CenterOn(new Vector2(20, 30));
        for (var step = 0; step < IsoCamera.ZoomStepCount; step++)
        {
            var world = new Vector2(23.25f, 27.5f);
            var screen = camera.WorldToScreen(world);
            var back = camera.ScreenToGround(screen);
            Assert.True(Vector2.Distance(world, back) < 1e-3f, $"Zoom step {camera.ZoomStep}: {world} -> {screen} -> {back}");
            camera.StepZoom(1, new Vector2(500, 350));
        }
    }

    [Fact]
    public void The_view_is_two_to_one_dimetric()
    {
        var camera = new IsoCamera(64, 64) { ViewportSize = new Vector2(1000, 700) };
        var origin = camera.WorldToScreen(new Vector2(10, 10));

        Assert.Equal(new Vector2(32, 16), camera.WorldToScreen(new Vector2(11, 10)) - origin);   // +x: down-right
        Assert.Equal(new Vector2(-32, 16), camera.WorldToScreen(new Vector2(10, 11)) - origin);  // +y: down-left
        Assert.Equal(new Vector2(0, -32), camera.WorldToScreen(new Vector3(10, 10, 1)) - origin); // height: straight up
        Assert.Equal(new Vector2(500, 350), camera.WorldToScreen(camera.Center));
    }

    [Fact]
    public void Zooming_keeps_the_point_under_the_cursor_still()
    {
        var camera = new IsoCamera(64, 64) { ViewportSize = new Vector2(1000, 700) };
        var cursor = new Vector2(700, 200);
        var before = camera.ScreenToGround(cursor);

        camera.StepZoom(1, cursor);

        Assert.Equal(2, camera.ZoomStep);
        Assert.True(Vector2.Distance(before, camera.ScreenToGround(cursor)) < 1e-3f);
        camera.StepZoom(1, cursor);
        Assert.Equal(2, camera.ZoomStep); // three steps only
    }

    [Fact]
    public void The_camera_cannot_leave_the_map()
    {
        var camera = new IsoCamera(64, 64);
        camera.PanPixels(new Vector2(-100_000, -100_000));
        Assert.True(camera.Center.X >= 0 && camera.Center.Y >= 0 && camera.Center.X <= 64 && camera.Center.Y <= 64);
    }

    [Fact]
    public void Input_presses_last_one_frame_and_holds_persist()
    {
        var input = new InputState();
        input.SetKey(Key.A, true);
        input.SetButton(MouseButton.Left, true);
        Assert.True(input.WasPressed(Key.A));
        Assert.True(input.WasPressed(MouseButton.Left));

        input.EndFrame();
        Assert.False(input.WasPressed(Key.A));
        Assert.True(input.IsHeld(Key.A));
        Assert.True(input.IsDown(MouseButton.Left));

        input.SetButton(MouseButton.Left, false);
        Assert.True(input.WasReleased(MouseButton.Left));
    }

    [Fact]
    public void Every_glyph_is_five_by_seven_and_text_draws_runs_of_pixels()
    {
        foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,:-+/%()[]!?'<>=_#$")
        {
            var rows = PixelFont.Rows(c);
            Assert.Equal(PixelFont.GlyphHeight, rows.Length);
            foreach (var row in rows)
            {
                Assert.True(row < 1 << PixelFont.GlyphWidth, $"'{c}' is wider than five pixels.");
            }
        }

        Assert.Equal(59f, PixelFont.Measure("HELLO", 2f) + 1f);

        var scene = new FrameScene();
        var width = new Canvas(scene).Text(0, 0, "I", 1f, LinearColor.Black);
        Assert.Equal(PixelFont.Advance, width);
        Assert.Equal(7, scene.QuadCount); // a top bar, five single pixels, a bottom bar
    }

    [Fact]
    public void Unknown_characters_draw_a_box_rather_than_nothing()
    {
        Assert.Equal(PixelFont.Rows('□').ToArray(), PixelFont.Rows('☃').ToArray());
        Assert.Equal(PixelFont.Rows('A').ToArray(), PixelFont.Rows('a').ToArray());
    }
}
