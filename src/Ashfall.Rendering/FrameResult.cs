namespace Ashfall.Rendering;

/// <summary>Outcome of <see cref="VulkanRenderer.RenderFrame"/>.</summary>
public enum FrameResult
{
    /// <summary>The frame was submitted and queued for presentation.</summary>
    Presented,

    /// <summary>Nothing was drawn: the window is minimised or the swapchain was being rebuilt.</summary>
    Skipped,
}
