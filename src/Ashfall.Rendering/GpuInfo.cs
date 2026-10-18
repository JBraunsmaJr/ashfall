namespace Ashfall.Rendering;

/// <summary>What the renderer ended up running on; logged at startup and useful in bug reports.</summary>
public sealed record GpuInfo(
    string DeviceName,
    string DeviceType,
    string ApiVersion,
    int QueueFamily,
    string SurfaceFormat,
    string PresentMode,
    bool ValidationEnabled)
{
    public override string ToString() =>
        $"{DeviceName} ({DeviceType}, Vulkan {ApiVersion}) · {SurfaceFormat} · {PresentMode}" +
        (ValidationEnabled ? " · validation on" : string.Empty);
}
