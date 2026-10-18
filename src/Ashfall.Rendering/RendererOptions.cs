namespace Ashfall.Rendering;

/// <summary>Choices made once, when the renderer is created.</summary>
public sealed record RendererOptions
{
    public string ApplicationName { get; init; } = "Ashfall";

    /// <summary>
    /// Enables VK_LAYER_KHRONOS_validation when it is installed (it ships with the Vulkan SDK).
    /// Validation messages go to standard output. Ignored with a warning when the layer is missing.
    /// </summary>
    public bool EnableValidation { get; init; }

    /// <summary>Wait for vertical blank (FIFO). When false, prefers mailbox, then immediate.</summary>
    public bool VSync { get; init; } = true;
}
