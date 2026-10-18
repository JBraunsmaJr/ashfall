using Brutal.VulkanApi;

namespace Ashfall.Rendering;

/// <summary>
/// Whatever owns the window (GLFW in the client). Keeps the renderer free of any windowing library.
/// </summary>
public interface IVulkanSurfaceSource
{
    /// <summary>Instance extensions the window system needs, e.g. VK_KHR_surface and VK_KHR_xlib_surface.</summary>
    IReadOnlyCollection<string> RequiredInstanceExtensions { get; }

    /// <summary>Current framebuffer size in pixels. Zero in either dimension means minimised: nothing is drawn.</summary>
    PixelSize FramebufferSize { get; }

    /// <summary>Creates the presentation surface for this window on <paramref name="instance"/>.</summary>
    VkSurfaceKHR CreateSurface(Instance instance);
}
