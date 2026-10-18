using Brutal.VulkanApi;

namespace Ashfall.Rendering.Backend;

/// <summary>The presentable images for the window, rebuilt whenever the window size changes.</summary>
internal sealed unsafe class Swapchain : IDisposable
{
    private readonly Device _device;

    private Swapchain(
        Device device,
        VkSwapchainKHR handle,
        VkSurfaceFormatKHR format,
        VkPresentModeKHR presentMode,
        VkExtent2D extent,
        VkImage[] images,
        VkImageView[] views)
    {
        _device = device;
        Handle = handle;
        Format = format;
        PresentMode = presentMode;
        Extent = extent;
        Images = images;
        Views = views;
    }

    public VkSwapchainKHR Handle { get; }

    public VkSurfaceFormatKHR Format { get; }

    public VkPresentModeKHR PresentMode { get; }

    public VkExtent2D Extent { get; }

    public VkImage[] Images { get; }

    public VkImageView[] Views { get; }

    /// <summary>Creates a swapchain, or returns null while the window has no area (minimised).</summary>
    /// <param name="previous">The swapchain being replaced, handed to the driver so it can reuse resources. Not disposed here.</param>
    public static Swapchain? Create(VulkanContext context, PixelSize framebufferSize, bool vsync, Swapchain? previous)
    {
        var physicalDevice = context.PhysicalDevice;
        var surface = context.Surface;
        var capabilities = physicalDevice.GetSurfaceCapabilitiesKHR(surface);
        var extent = ChooseExtent(capabilities, framebufferSize);
        if (extent.Width == 0 || extent.Height == 0)
        {
            return null;
        }

        var format = ChooseFormat(physicalDevice, surface);
        var presentMode = ChoosePresentMode(physicalDevice, surface, vsync);

        // One more than the minimum so the CPU never waits on the presentation engine to release an image.
        var imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
        {
            imageCount = capabilities.MaxImageCount;
        }

        var handle = context.Device.CreateSwapchainKHR(
            new VkSwapchainCreateInfoKHR
            {
                Surface = surface,
                MinImageCount = imageCount,
                ImageFormat = format.Format,
                ImageColorSpace = format.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = Usage(capabilities),
                ImageSharingMode = VkSharingMode.Exclusive,
                PreTransform = capabilities.CurrentTransform,
                CompositeAlpha = ChooseCompositeAlpha(capabilities.SupportedCompositeAlpha),
                PresentMode = presentMode,
                Clipped = true,
                OldSwapchain = previous?.Handle ?? default,
            },
            null);

        var images = new VkImage[context.Device.GetSwapchainImagesKHR_Count(handle)];
        context.Device.GetSwapchainImagesKHR_Write(handle, images);

        var views = new VkImageView[images.Length];
        for (var i = 0; i < images.Length; i++)
        {
            views[i] = context.Device.CreateImageView(
                new VkImageViewCreateInfo
                {
                    Image = images[i],
                    ViewType = VkImageViewType._2D,
                    Format = format.Format,
                    Components = new VkComponentMapping
                    {
                        R = VkComponentSwizzle.Identity,
                        G = VkComponentSwizzle.Identity,
                        B = VkComponentSwizzle.Identity,
                        A = VkComponentSwizzle.Identity,
                    },
                    SubresourceRange = ColorSubresource,
                },
                null);
        }

        return new Swapchain(context.Device, handle, format, presentMode, extent, images, views)
        {
            SupportsCapture = capabilities.SupportedUsageFlags.HasFlag(VkImageUsageFlags.TransferSrcBit),
        };
    }

    /// <summary>True when images can be copied out (screenshots, CI frame captures).</summary>
    public bool SupportsCapture { get; private init; }

    /// <summary>The whole colour aspect of a single-mip, single-layer image.</summary>
    public static VkImageSubresourceRange ColorSubresource => new()
    {
        AspectMask = VkImageAspectFlags.ColorBit,
        BaseMipLevel = 0,
        LevelCount = 1,
        BaseArrayLayer = 0,
        LayerCount = 1,
    };

    public void Dispose()
    {
        foreach (var view in Views)
        {
            _device.DestroyImageView(view, null);
        }

        _device.DestroySwapchainKHR(Handle, null);
    }

    // Colour attachment always; transfer source too where supported, so frames can be read back.
    private static VkImageUsageFlags Usage(VkSurfaceCapabilitiesKHR capabilities)
    {
        var usage = VkImageUsageFlags.ColorAttachmentBit | VkImageUsageFlags.TransferDstBit;
        return capabilities.SupportedUsageFlags.HasFlag(VkImageUsageFlags.TransferSrcBit)
            ? usage | VkImageUsageFlags.TransferSrcBit
            : usage;
    }

    private static VkExtent2D ChooseExtent(VkSurfaceCapabilitiesKHR capabilities, PixelSize framebufferSize)
    {
        // A current extent of 0xFFFFFFFF means "the swapchain decides"; otherwise it must match the window.
        if (capabilities.CurrentExtent.Width != -1)
        {
            return capabilities.CurrentExtent;
        }

        return new VkExtent2D
        {
            Width = Math.Clamp(framebufferSize.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
            Height = Math.Clamp(framebufferSize.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height),
        };
    }

    private static VkSurfaceFormatKHR ChooseFormat(PhysicalDevice physicalDevice, VkSurfaceKHR surface)
    {
        var formats = new VkSurfaceFormatKHR[physicalDevice.GetSurfaceFormatsKHR_Count(surface)];
        physicalDevice.GetSurfaceFormatsKHR_Write(surface, formats);

        // sRGB formats let the hardware do gamma-correct writes; the renderer works in linear space.
        ReadOnlySpan<VkFormat> preferred = [VkFormat.B8G8R8A8SRGB, VkFormat.R8G8B8A8SRGB];
        foreach (var wanted in preferred)
        {
            foreach (var format in formats)
            {
                if (format.Format == wanted && format.ColorSpace == VkColorSpaceKHR.SRGBNonlinearKHR)
                {
                    return format;
                }
            }
        }

        return formats.Length > 0
            ? formats[0]
            : throw new InvalidOperationException("The window surface reports no supported formats.");
    }

    private static VkPresentModeKHR ChoosePresentMode(PhysicalDevice physicalDevice, VkSurfaceKHR surface, bool vsync)
    {
        // FIFO is the only mode every driver must support, and it is true vsync.
        if (vsync)
        {
            return VkPresentModeKHR.FifoKHR;
        }

        var modes = new VkPresentModeKHR[physicalDevice.GetSurfacePresentModesKHR_Count(surface)];
        physicalDevice.GetSurfacePresentModesKHR_Write(surface, modes);
        if (Array.IndexOf(modes, VkPresentModeKHR.MailboxKHR) >= 0)
        {
            return VkPresentModeKHR.MailboxKHR;
        }

        return Array.IndexOf(modes, VkPresentModeKHR.ImmediateKHR) >= 0
            ? VkPresentModeKHR.ImmediateKHR
            : VkPresentModeKHR.FifoKHR;
    }

    private static VkCompositeAlphaFlagsKHR ChooseCompositeAlpha(VkCompositeAlphaFlagsKHR supported)
    {
        ReadOnlySpan<VkCompositeAlphaFlagsKHR> preferred =
        [
            VkCompositeAlphaFlagsKHR.OpaqueBitKHR,
            VkCompositeAlphaFlagsKHR.InheritBitKHR,
            VkCompositeAlphaFlagsKHR.PreMultipliedBitKHR,
            VkCompositeAlphaFlagsKHR.PostMultipliedBitKHR,
        ];

        foreach (var mode in preferred)
        {
            if (supported.HasFlag(mode))
            {
                return mode;
            }
        }

        return VkCompositeAlphaFlagsKHR.OpaqueBitKHR;
    }
}
