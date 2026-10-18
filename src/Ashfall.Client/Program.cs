using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Ashfall.Client;
using Ashfall.Core;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.Session;
using Ashfall.Session.Ai;
using Ashfall.UI;
using Ashfall.UI.Input;
using Brutal.GlfwApi;
using Brutal.Numerics;

// M2 client: a skirmish on the duel map against the scripted (or idle) AI, drawn by the Vulkan
// renderer through the UI layer. The simulation runs on this thread between frames; frames
// interpolate between the last two ticks.

#if DEBUG
const bool ValidationByDefault = true;
#else
const bool ValidationByDefault = false;
#endif

// Unthrottled play would never yield to the window; cap the catch-up per frame instead.
const int MaxTicksPerFrame = 30;

var options = ClientOptions.Parse(args, ValidationByDefault, out var error);
if (options is null)
{
    await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
    await Console.Error.WriteLineAsync(ClientOptions.Usage).ConfigureAwait(false);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(ClientOptions.Usage);
    return 0;
}

NativeLibraryFallbacks.Register();

if (!Glfw.Init())
{
    await Console.Error.WriteLineAsync("Failed to initialise GLFW.").ConfigureAwait(false);
    return 1;
}

try
{
    // No OpenGL context: the renderer creates a Vulkan surface for this window.
    Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
    using var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
    {
        Title = "Ashfall",
        Size = new int2(options.Width, options.Height),
    });

    using var renderer = VulkanRenderer.Create(
        new GlfwSurfaceSource(window),
        new RendererOptions { EnableValidation = options.Validation, VSync = options.VSync });
    window.OnFramebufferSize += (_, _) => renderer.NotifyResized();
    Console.WriteLine($"GPU: {renderer.Gpu}");

    await using var match = await HeadlessMatch.StartAsync(options.ToSpec(), options.Speed).ConfigureAwait(false);
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
    var input = new GlfwInput(window, new InputState());
    var scene = new FrameScene();
    var clock = Stopwatch.StartNew();
    var lastFrame = TimeSpan.Zero;
    var lastTitle = TimeSpan.Zero;
    long framesAtLastTitle = 0;
    var wasPaused = false;

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

    if (options.Screenshot is not null && renderer.LastCapture is { } capture)
    {
        PngEncoder.WriteFile(options.Screenshot, capture);
        Console.WriteLine($"Saved {capture.Size} screenshot to {options.Screenshot}.");
    }

    var result = match.IsOver
        ? match.WinningTeam >= 0 ? $"team {match.WinningTeam} won" : "draw"
        : "in progress";
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"Presented {renderer.FramesPresented} frames at {renderer.RenderSize} in {clock.Elapsed.TotalSeconds:F1}s ({renderer.LastQuadCount} quads in the last); " +
        $"swapchain built {renderer.SwapchainGenerations}x; simulation at tick {match.Tick} ({result}), state {StateHasher.Format(match.Simulation.ComputeStateHash())}."));
    return 0;
}
finally
{
    Glfw.Terminate();
}

// How far between the previous tick and the next the frame is, for interpolation.
static float TickAlpha(HeadlessMatch match)
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

static bool IsLastFrame(ClientOptions options, VulkanRenderer renderer, HeadlessMatch match) =>
    (options.Frames > 0 && renderer.FramesPresented == options.Frames - 1)
    || (options.UntilTick > 0 && match.Tick >= options.UntilTick - 1);
