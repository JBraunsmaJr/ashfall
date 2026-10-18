using System.Globalization;
using System.Text;
using Brutal.VulkanApi;
using VulkanApi = Brutal.VulkanApi.Vulkan;

namespace Ashfall.Rendering.Backend;

/// <summary>
/// The long-lived Vulkan objects: instance, window surface, chosen GPU, logical device and the one
/// graphics-and-present queue. Everything else is created from this.
/// </summary>
internal sealed unsafe class VulkanContext : IDisposable
{
    private bool _disposed;

    private VulkanContext(
        Instance instance,
        VkSurfaceKHR surface,
        PhysicalDevice physicalDevice,
        Device device,
        int queueFamily,
        bool validationEnabled)
    {
        Instance = instance;
        Surface = surface;
        PhysicalDevice = physicalDevice;
        Device = device;
        QueueFamily = queueFamily;
        Queue = device.GetQueue(queueFamily, 0);
        ValidationEnabled = validationEnabled;

        var properties = physicalDevice.GetProperties();
        DeviceName = NativeText.FromNullTerminated(properties.DeviceName);
        DeviceType = properties.DeviceType.ToString();
        ApiVersion = VulkanVersion.Format(properties.ApiVersion);
    }

    public Instance Instance { get; }

    public VkSurfaceKHR Surface { get; }

    public PhysicalDevice PhysicalDevice { get; }

    public Device Device { get; }

    public Queue Queue { get; }

    public int QueueFamily { get; }

    public bool ValidationEnabled { get; }

    public string DeviceName { get; }

    public string DeviceType { get; }

    public string ApiVersion { get; }

    public static VulkanContext Create(IVulkanSurfaceSource surfaceSource, RendererOptions options)
    {
        var validation = options.EnableValidation && IsInstanceLayerAvailable(VK.LAYER_KHRONOS_VALIDATION);
        if (options.EnableValidation && !validation)
        {
            Console.Error.WriteLine(
                $"Vulkan validation requested but {VK.LAYER_KHRONOS_VALIDATION} is not installed; continuing without it.");
        }

        var instance = CreateInstance(surfaceSource, options, validation);
        VkSurfaceKHR surface = default;
        try
        {
            surface = surfaceSource.CreateSurface(instance);
            var (physicalDevice, queueFamily) = SelectPhysicalDevice(instance, surface);
            var device = CreateDevice(physicalDevice, queueFamily);
            return new VulkanContext(instance, surface, physicalDevice, device, queueFamily, validation);
        }
        catch
        {
            if (!surface.IsNull())
            {
                instance.DestroySurfaceKHR(surface, null);
            }

            instance.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Device.WaitIdle();
        Device.Dispose();
        Instance.DestroySurfaceKHR(Surface, null);
        Instance.Dispose();
    }

    private static Instance CreateInstance(IVulkanSurfaceSource surfaceSource, RendererOptions options, bool validation)
    {
        var extensions = new HashSet<string>(surfaceSource.RequiredInstanceExtensions, StringComparer.Ordinal);
        var layers = new HashSet<string>(StringComparer.Ordinal);
        if (validation)
        {
            layers.Add(VK.LAYER_KHRONOS_VALIDATION);
        }

        var applicationName = Encoding.UTF8.GetBytes(options.ApplicationName + "\0");
        fixed (byte* applicationNamePtr = applicationName)
        fixed (byte* engineNamePtr = "Ashfall"u8)
        {
            return VulkanApi.CreateInstance(new Instance.CreateInfo
            {
                AppInfo = new VkApplicationInfo
                {
                    ApplicationName = applicationNamePtr,
                    ApplicationVersion = VulkanVersion.Make(0, 1),
                    EngineName = engineNamePtr,
                    EngineVersion = VulkanVersion.Make(0, 1),
                    ApiVersion = VulkanVersion.Required,
                },
                EnabledExtensions = extensions,
                EnabledLayers = layers,
            });
        }
    }

    private static bool IsInstanceLayerAvailable(string layerName)
    {
        var layers = new VkLayerProperties[VulkanApi.EnumerateInstanceLayerProperties_Count()];
        VulkanApi.EnumerateInstanceLayerProperties_Write(layers);
        for (var i = 0; i < layers.Length; i++)
        {
            fixed (byte* name = layers[i].LayerName)
            {
                if (NativeText.FromNullTerminated(name) == layerName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Picks the best GPU that has Vulkan 1.3, dynamic rendering, synchronization2, swapchain support and a
    /// queue family that can both draw and present to <paramref name="surface"/>. Discrete beats integrated
    /// beats virtual beats software.
    /// </summary>
    private static (PhysicalDevice Device, int QueueFamily) SelectPhysicalDevice(Instance instance, VkSurfaceKHR surface)
    {
        PhysicalDevice? best = null;
        var bestFamily = -1;
        var bestScore = -1;
        var rejections = new StringBuilder();

        foreach (var candidate in instance.PhysicalDevices)
        {
            var properties = candidate.GetProperties();
            var name = NativeText.FromNullTerminated(properties.DeviceName);
            var reason = Disqualify(candidate, properties, surface, out var family);
            if (reason is not null)
            {
                rejections.Append(CultureInfo.InvariantCulture, $"\n  {name}: {reason}");
                continue;
            }

            var score = properties.DeviceType switch
            {
                VkPhysicalDeviceType.DiscreteGpu => 4,
                VkPhysicalDeviceType.IntegratedGpu => 3,
                VkPhysicalDeviceType.VirtualGpu => 2,
                VkPhysicalDeviceType.Cpu => 1,
                _ => 0,
            };

            if (score > bestScore)
            {
                best = candidate;
                bestFamily = family;
                bestScore = score;
            }
        }

        return best is null
            ? throw new InvalidOperationException(
                "No GPU meets Ashfall's requirements (Vulkan 1.3 with dynamic rendering, synchronization2 and presentation)." +
                (rejections.Length > 0 ? rejections.ToString() : " No Vulkan devices were found."))
            : (best, bestFamily);
    }

    private static string? Disqualify(
        PhysicalDevice candidate,
        VkPhysicalDeviceProperties properties,
        VkSurfaceKHR surface,
        out int queueFamily)
    {
        queueFamily = -1;
        if ((uint)properties.ApiVersion < (uint)VulkanVersion.Required)
        {
            return $"supports Vulkan {VulkanVersion.Format(properties.ApiVersion)}, needs 1.3";
        }

        if (!candidate.SupportsExtension(VK.KHR_SWAPCHAIN_EXTENSION_NAME))
        {
            return "no swapchain support";
        }

        var features13 = new VkPhysicalDeviceVulkan13Features();
        var features = new VkPhysicalDeviceFeatures2 { Next = &features13 };
        candidate.GetFeatures2(ref features);
        if (!features13.DynamicRendering || !features13.Synchronization2)
        {
            return "missing dynamic rendering or synchronization2";
        }

        var families = new VkQueueFamilyProperties[candidate.GetQueueFamilyProperties_Count()];
        candidate.GetQueueFamilyProperties_Write(families);
        for (var i = 0; i < families.Length; i++)
        {
            if (families[i].QueueFlags.HasFlag(VkQueueFlags.GraphicsBit) && candidate.GetSurfaceSupportKHR(i, surface))
            {
                queueFamily = i;
                return null;
            }
        }

        return "no queue family can both draw and present to the window";
    }

    private static Device CreateDevice(PhysicalDevice physicalDevice, int queueFamily)
    {
        var priority = 1f;
        var queueInfo = new VkDeviceQueueCreateInfo
        {
            QueueFamilyIndex = queueFamily,
            QueueCount = 1,
            QueuePriorities = &priority,
        };

        var features13 = new VkPhysicalDeviceVulkan13Features
        {
            DynamicRendering = true,
            Synchronization2 = true,
        };

        return physicalDevice.CreateDevice(new Device.CreateInfo
        {
            Extensions = [VK.KHR_SWAPCHAIN_EXTENSION_NAME],
            QueueCreateInfos = new ReadOnlySpan<VkDeviceQueueCreateInfo>(&queueInfo, 1),
            Next = &features13,
        });
    }
}
