using System.Reflection;
using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Session;
using Ashfall.Simulation;

namespace Ashfall.Architecture.Tests;

/// <summary>
/// Enforces the dependency direction from the GDD:
/// Core ← Content ← Simulation ← Session ← (Client, Headless). The simulation must never see rendering,
/// audio, UI, networking or BRUTAL, so the headless runner exercises exactly what players run.
/// </summary>
public class LayeringTests
{
    private static readonly Assembly Core = typeof(Fix64).Assembly;
    private static readonly Assembly ContentAssembly = typeof(GameContent).Assembly;
    private static readonly Assembly Simulation = typeof(GameSimulation).Assembly;
    private static readonly Assembly Session = typeof(HeadlessMatch).Assembly;

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
    public void Session_depends_only_on_core_content_and_simulation()
    {
        string[] expected = ["Ashfall.Content", "Ashfall.Core", "Ashfall.Simulation"];
        Assert.Equal(expected, NonRuntimeReferences(Session));
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
