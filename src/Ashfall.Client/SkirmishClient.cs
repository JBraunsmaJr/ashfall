using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Ashfall.Campaign;
using Ashfall.Core;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.Session;
using Ashfall.Session.Ai;
using Ashfall.Session.Campaigns;
using Ashfall.UI;
using Brutal.GlfwApi;

namespace Ashfall.Client;

/// <summary>
/// A single-player match (skirmish, Purge, swarm or a campaign mission): the simulation runs on this thread between
/// frames, and frames interpolate between the last two ticks.
/// </summary>
internal static class SkirmishClient
{
    // Unthrottled play would never yield to the window; cap the catch-up per frame instead.
    private const int MaxTicksPerFrame = 30;

    /// <summary>
    /// Plays <paramref name="spec"/> (by default the match the options describe) until it is left or the window closes.
    /// Returns the campaign result read from its end state, or null if it was left before it ended.
    /// </summary>
    public static async Task<MissionResult?> Run(GlfwWindow window, VulkanRenderer renderer, GlfwInput input, ClientOptions options, MatchSpec? spec = null)
    {
        await using var match = await HeadlessMatch.StartAsync(spec ?? options.ToSpec(), options.Speed).ConfigureAwait(false);
        if (options.Autopilot && match.LocalPlayer is { } local)
        {
            match.AddCommandSource(new ScriptedSkirmishAi(local.PlayerId, options.Seed));
        }

        var screen = new GameScreen(match);
        screen.Camera.StepZoom(options.Zoom - screen.Camera.ZoomStep, new Vector2(options.Width / 2f, options.Height / 2f));

        if (options.LookAt is { } lookAt)
        {
            screen.Camera.CenterOn(new Vector2(lookAt.X, lookAt.Y));
        }

        var scene = new FrameScene();
        var clock = Stopwatch.StartNew();
        var lastFrame = TimeSpan.Zero;
        var lastTitle = TimeSpan.Zero;
        long framesAtLastTitle = 0;
        var wasPaused = false;
        input.State.EndFrame();

        while (!window.ShouldClose && !screen.ExitRequested)
        {
            if (options.Frames > 0 && renderer.FramesPresented >= options.Frames)
            {
                break;
            }

            if (options.UntilTick > 0 && match.Tick >= options.UntilTick)
            {
                break;
            }

            Glfw.PollEvents();
            input.Poll();
            var now = clock.Elapsed;
            input.State.DeltaSeconds = (float)(now - lastFrame).TotalSeconds;
            input.State.Time = now.TotalSeconds;
            lastFrame = now;

            var framebuffer = new Vector2(window.FramebufferSize.X, window.FramebufferSize.Y);
            screen.Update(input.State, framebuffer);

            // Run the ticks that are due (none while paused; restart the clock on resume so it doesn't sprint).
            if (wasPaused && !screen.Paused)
            {
                match.SetSpeed(match.Clock.Speed);
            }

            wasPaused = screen.Paused;
            var ran = 0;
            while (!screen.Paused && !match.IsOver && ran < MaxTicksPerFrame && match.Clock.TimeUntilDue(match.Tick) == TimeSpan.Zero)
            {
                match.Step();
                screen.OnTick();
                ran++;
            }

            if (ran == MaxTicksPerFrame)
            {
                match.SetSpeed(match.Clock.Speed);
            }

            var alpha = TickAlpha(match);
            if (options.Screenshot is not null && IsLastFrame(options, renderer, match))
            {
                renderer.RequestCapture();
            }

            screen.Draw(scene, alpha, now.TotalSeconds);
            if (renderer.RenderFrame(scene) == FrameResult.Skipped)
            {
                // Minimised or mid-resize: don't spin.
                Thread.Sleep(5);
            }

            input.State.EndFrame();

            if (clock.Elapsed - lastTitle >= TimeSpan.FromSeconds(1))
            {
                var fps = (renderer.FramesPresented - framesAtLastTitle) / (clock.Elapsed - lastTitle).TotalSeconds;
                window.Title = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Ashfall — tick {match.Tick} — {fps:F0} fps — {renderer.LastQuadCount} quads — state {StateHasher.Format(match.Simulation.ComputeStateHash())}");
                framesAtLastTitle = renderer.FramesPresented;
                lastTitle = clock.Elapsed;
            }
        }

        // Exiting on a tick limit: capture the frame now if the loop ended before the planned last frame.
        if (options.Screenshot is not null && renderer.LastCapture is null)
        {
            renderer.RequestCapture();
            screen.Draw(scene, TickAlpha(match), clock.Elapsed.TotalSeconds);
            renderer.RenderFrame(scene);
        }

        if (options.Record is not null)
        {
            Ashfall.Session.Replays.Replay.From(match).Save(options.Record);
            Console.WriteLine($"Saved replay to {options.Record}.");
        }

        if (options.Screenshot is not null && renderer.LastCapture is { } capture)
        {
            PngEncoder.WriteFile(options.Screenshot, capture);
            Console.WriteLine($"Saved {capture.Size} screenshot to {options.Screenshot}.");
        }

        var result = match.IsOver ? MatchReport.Outcome(match.Simulation) : "in progress";
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Presented {renderer.FramesPresented} frames at {renderer.RenderSize} in {clock.Elapsed.TotalSeconds:F1}s ({renderer.LastQuadCount} quads in the last); " +
            $"swapchain built {renderer.SwapchainGenerations}x; simulation at tick {match.Tick} ({result}), state {StateHasher.Format(match.Simulation.ComputeStateHash())}."));
        return match.IsOver ? CampaignMatches.ResultOf(match) : null;
    }

    /// <summary>How far between the previous tick and the next the frame is, for interpolation.</summary>
    public static float TickAlpha(HeadlessMatch match)
    {
        var speed = match.Clock.Speed;
        if (speed.IsUnthrottled)
        {
            return 1f;
        }

        var tick = speed.DueOffset(1);
        var remaining = match.Clock.TimeUntilDue(match.Tick);
        return tick <= TimeSpan.Zero ? 1f : Math.Clamp(1f - (float)(remaining / tick), 0f, 1f);
    }

    private static bool IsLastFrame(ClientOptions options, VulkanRenderer renderer, HeadlessMatch match) =>
        (options.Frames > 0 && renderer.FramesPresented == options.Frames - 1)
        || (options.UntilTick > 0 && match.Tick >= options.UntilTick - 1);
}
