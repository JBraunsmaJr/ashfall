using System.Globalization;
using Ashfall.Core;
using Ashfall.Session;
using Brutal.GlfwApi;
using Brutal.Numerics;

// M0 client: opens a window through BRUTAL's GLFW bindings and runs the proving-ground match in
// real time, showing the tick and state hash in the title bar. The Vulkan renderer replaces the
// title-bar readout in M2; the BRUTAL splash logo (licence section 2.2) is added with it.

if (!Glfw.Init())
{
    throw new InvalidOperationException("Failed to initialise GLFW.");
}

try
{
    // No OpenGL context: the renderer will create a Vulkan surface for this window.
    Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
    using var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
    {
        Title = "Ashfall",
        Size = new int2(1280, 720),
    });

    await using var match = await HeadlessMatch.StartAsync(
        MatchSpec.ProvingGround(ProvingGround.DefaultSeed),
        GameSpeed.RealTime).ConfigureAwait(false);

    while (!window.ShouldClose)
    {
        Glfw.PollEvents();

        // Run every tick that is due; never block the window loop waiting for one.
        while (match.Clock.TimeUntilDue(match.Tick) == TimeSpan.Zero)
        {
            match.Step();
            if (match.Tick % 15 == 0)
            {
                window.Title = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Ashfall — tick {match.Tick} — {match.Simulation.World.AliveCount} units — state {StateHasher.Format(match.Simulation.ComputeStateHash())}");
            }
        }

        Thread.Sleep(1);
    }
}
finally
{
    Glfw.Terminate();
}
