using Ashfall.Client;
using Ashfall.Rendering;
using Ashfall.UI.Input;
using Brutal.GlfwApi;
using Brutal.Numerics;

// The client: the main menu (skirmish, host, join), or straight into a mode when command-line flags
// ask for one (scripts, smoke tests and screenshots).

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

    var input = new GlfwInput(window, new InputState());
    if (options.IsNetwork)
    {
        return MultiplayerClient.Run(window, renderer, input, options).ExitCode;
    }

    if (options.StartsInMenu)
    {
        return await MenuClient.Run(window, renderer, input, options).ConfigureAwait(false);
    }

    await SkirmishClient.Run(window, renderer, input, options).ConfigureAwait(false);
    return 0;
}
finally
{
    Glfw.Terminate();
}
