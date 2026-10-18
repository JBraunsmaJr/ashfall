using System.Runtime.InteropServices;
using Ashfall.Rendering;
using Brutal.GlfwApi;
using Brutal.VulkanApi;

namespace Ashfall.Client;

/// <summary>Connects a GLFW window to the renderer without the renderer knowing about GLFW.</summary>
internal sealed unsafe class GlfwSurfaceSource : IVulkanSurfaceSource
{
    private readonly GlfwWindow _window;

    public GlfwSurfaceSource(GlfwWindow window)
    {
        _window = window;

        var names = Glfw.GetRequiredInstanceExtensions(out var count);
        if (names is null)
        {
            throw new InvalidOperationException("GLFW found no Vulkan support on this system (is a Vulkan driver installed?).");
        }

        var extensions = new string[count];
        for (var i = 0; i < count; i++)
        {
            extensions[i] = Marshal.PtrToStringUTF8((nint)names[i]) ?? string.Empty;
        }

        RequiredInstanceExtensions = extensions;
    }

    public IReadOnlyCollection<string> RequiredInstanceExtensions { get; }

    public PixelSize FramebufferSize => new(_window.FramebufferSize.X, _window.FramebufferSize.Y);

    public VkSurfaceKHR CreateSurface(Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var result = Glfw.CreateWindowSurface(instance.VkHandle, _window, out var surface);
        return result == (int)VkResult.Success
            ? new VkSurfaceKHR((nint)surface)
            : throw new InvalidOperationException($"glfwCreateWindowSurface failed with {(VkResult)result}.");
    }
}
