using System.Reflection;
using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Session;
using Ashfall.Simulation;

namespace Ashfall.Architecture.Tests;

/// <summary>
/// Enforces the dependency direction from the GDD:
/// Core ← Content ← Simulation ← (MapGen, Networking) ← Session ← UI ← Client, with Session ← Headless and Rendering ← UI.
/// The simulation must never see rendering,
/// audio, UI, networking or BRUTAL, so the headless runner exercises exactly what players run.
/// </summary>
public class LayeringTests
{
    private static readonly Assembly Core = typeof(Fix64).Assembly;
    private static readonly Assembly ContentAssembly = typeof(GameContent).Assembly;
    private static readonly Assembly Simulation = typeof(GameSimulation).Assembly;
    private static readonly Assembly Session = typeof(HeadlessMatch).Assembly;
    private static readonly Assembly Interface = typeof(Ashfall.UI.GameScreen).Assembly;
    private static readonly Assembly Renderer = typeof(Ashfall.Rendering.VulkanRenderer).Assembly;

    [Fact]
    public void Core_depends_on_nothing_but_the_runtime()
    {
        Assert.Empty(NonRuntimeReferences(Core));
    }

    [Fact]
    public void Content_depends_only_on_core()
    {
        string[] expected = ["Ashfall.Core"];
        Assert.Equal(expected, NonRuntimeReferences(ContentAssembly));
    }

    [Fact]
    public void Simulation_depends_only_on_core_and_content()
    {
        string[] expected = ["Ashfall.Content", "Ashfall.Core"];
        Assert.Equal(expected, NonRuntimeReferences(Simulation));
    }

    [Fact]
    public void Networking_depends_only_on_core_and_simulation()
    {
        string[] expected = ["Ashfall.Core", "Ashfall.Simulation"];
        Assert.Equal(expected, NonRuntimeReferences(typeof(Ashfall.Networking.Transport.ReliableTransport).Assembly));
    }

    [Fact]
    public void Map_generation_depends_only_on_core_and_simulation()
    {
        string[] expected = ["Ashfall.Core", "Ashfall.Simulation"];
        Assert.Equal(expected, NonRuntimeReferences(typeof(Ashfall.MapGen.MapGenerator).Assembly));
    }

    [Fact]
    public void Session_depends_only_on_core_content_simulation_map_generation_and_networking()
    {
        string[] expected = ["Ashfall.Content", "Ashfall.Core", "Ashfall.MapGen", "Ashfall.Networking", "Ashfall.Simulation"];
        Assert.Equal(expected, NonRuntimeReferences(Session));
    }

    [Fact]
    public void The_interface_reads_the_game_and_feeds_the_renderer_but_never_touches_the_window_system()
    {
        var references = NonRuntimeReferences(Interface);

        Assert.Contains("Ashfall.Session", references);
        Assert.Contains("Ashfall.Rendering", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Brutal.Glfw", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Brutal.Vulkan", StringComparison.Ordinal));
    }

    [Fact]
    public void The_renderer_knows_nothing_of_the_game()
    {
        Assert.DoesNotContain(NonRuntimeReferences(Renderer), name => name.StartsWith("Ashfall", StringComparison.Ordinal));
    }

    private static string[] NonRuntimeReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !IsRuntimeAssembly(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsRuntimeAssembly(string name) =>
        name is "netstandard" or "mscorlib"
        || name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Win32", StringComparison.Ordinal);
}
