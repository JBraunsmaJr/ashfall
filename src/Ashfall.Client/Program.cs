using System.Diagnostics;
using System.Globalization;
using Ashfall.Client;
using Ashfall.Core;
using Ashfall.Rendering;
using Ashfall.Session;
using Brutal.GlfwApi;
using Brutal.Numerics;

// M0 client: a BRUTAL/GLFW window, a Vulkan 1.3 renderer clearing it every frame, and the
// proving-ground match running in real time behind it. The tick, unit count and state hash are in the
// title bar; the clear colour pulses once per 30 ticks so you can see the simulation driving the frame.
// The BRUTAL splash logo (licence section 2.2) arrives with the M2 renderer passes.

#if DEBUG
const bool ValidationByDefault = true;
#else
const bool ValidationByDefault = false;
#endif

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

    await using var match = await HeadlessMatch.StartAsync(MatchSpec.ProvingGround(options.Seed), options.Speed)
        .ConfigureAwait(false);

    var baseColor = LinearColor.FromSrgb(24, 28, 36);
    var clock = Stopwatch.StartNew();
    long framesAtLastTitle = 0;
    var lastTitle = TimeSpan.Zero;

    while (!window.ShouldClose && (options.Frames == 0 || renderer.FramesPresented < options.Frames))
    {
        Glfw.PollEvents();

        // Run every simulation tick that is due; never block the window loop waiting for one.
        while (match.Clock.TimeUntilDue(match.Tick) == TimeSpan.Zero)
        {
            match.Step();
        }

        if (renderer.RenderFrame(PulseColor(baseColor, match.Tick)) == FrameResult.Skipped)
        {
            // Minimised or mid-resize: don't spin.
            Thread.Sleep(5);
        }

        if (clock.Elapsed - lastTitle >= TimeSpan.FromSeconds(1))
        {
            var fps = (renderer.FramesPresented - framesAtLastTitle) / (clock.Elapsed - lastTitle).TotalSeconds;
            window.Title = string.Create(
                CultureInfo.InvariantCulture,
                $"Ashfall — tick {match.Tick} — {match.Simulation.World.AliveCount} units — {fps:F0} fps — state {StateHasher.Format(match.Simulation.ComputeStateHash())}");
            framesAtLastTitle = renderer.FramesPresented;
            lastTitle = clock.Elapsed;
        }
    }

    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"Presented {renderer.FramesPresented} frames at {renderer.RenderSize} in {clock.Elapsed.TotalSeconds:F1}s; " +
        $"swapchain built {renderer.SwapchainGenerations}x; simulation at tick {match.Tick}, state {StateHasher.Format(match.Simulation.ComputeStateHash())}."));
    return 0;
}
finally
{
    Glfw.Terminate();
}

// Brightness rises and falls once every 30 ticks (2 s at 1×): visible proof the simulation drives the frame.
static LinearColor PulseColor(LinearColor baseColor, long tick)
{
    var phase = tick % 30 / 30f;
    var brightness = 1f + (0.6f * MathF.Sin(phase * MathF.Tau));
    return new LinearColor(baseColor.R * brightness, baseColor.G * brightness, baseColor.B * brightness);
}
