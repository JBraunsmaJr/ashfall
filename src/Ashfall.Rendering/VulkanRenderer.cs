using Ashfall.Rendering.Backend;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace Ashfall.Rendering;

/// <summary>
/// The game's renderer. M0 clears the window through dynamic rendering, which is the same path the
/// terrain and sprite passes will draw through in M2.
/// </summary>
/// <remarks>
/// Frame pacing: <see cref="FramesInFlight"/> frames are recorded ahead, each with its own command
/// buffer, "image available" semaphore and fence. "Render finished" semaphores belong to swapchain
/// images, not frames, because an image can be presented again before the frame slot comes round.
/// </remarks>
public sealed unsafe class VulkanRenderer : IDisposable
{
    public const int FramesInFlight = 2;

    // UINT64_MAX: wait forever.
    private const nint NoTimeout = -1;

    private readonly VulkanContext _context;
    private readonly IVulkanSurfaceSource _surfaceSource;
    private readonly RendererOptions _options;
    private readonly FrameSlot[] _frames = new FrameSlot[FramesInFlight];
    private VkSemaphore[] _renderFinished = [];
    private Swapchain? _swapchain;
    private int _frameIndex;
    private bool _swapchainStale = true;
    private bool _disposed;

    private VulkanRenderer(VulkanContext context, IVulkanSurfaceSource surfaceSource, RendererOptions options)
    {
        _context = context;
        _surfaceSource = surfaceSource;
        _options = options;

        for (var i = 0; i < FramesInFlight; i++)
        {
            _frames[i] = CreateFrameSlot();
        }

        RecreateSwapchain();
    }

    /// <summary>Total frames handed to the presentation engine.</summary>
    public long FramesPresented { get; private set; }

    /// <summary>Number of times the swapchain has been (re)built, e.g. after resizes.</summary>
    public int SwapchainGenerations { get; private set; }

    /// <summary>The size of the images currently being presented.</summary>
    public PixelSize RenderSize => _swapchain is null
        ? default
        : new PixelSize(_swapchain.Extent.Width, _swapchain.Extent.Height);

    /// <summary>The GPU and presentation settings in use.</summary>
    public GpuInfo Gpu => new(
        _context.DeviceName,
        _context.DeviceType,
        _context.ApiVersion,
        _context.QueueFamily,
        _swapchain?.Format.Format.ToString() ?? "none yet",
        _swapchain?.PresentMode.ToString() ?? "none yet",
        _context.ValidationEnabled);

    /// <summary>Creates the renderer for the window behind <paramref name="surfaceSource"/>.</summary>
    public static VulkanRenderer Create(IVulkanSurfaceSource surfaceSource, RendererOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(surfaceSource);
        options ??= new RendererOptions();
        var context = VulkanContext.Create(surfaceSource, options);
        try
        {
            return new VulkanRenderer(context, surfaceSource, options);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    /// <summary>Tells the renderer the window size changed; the swapchain is rebuilt before the next frame.</summary>
    public void NotifyResized() => _swapchainStale = true;

    /// <summary>Records, submits and presents one frame that clears the window to <paramref name="clearColor"/>.</summary>
    public FrameResult RenderFrame(LinearColor clearColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ref var frame = ref _frames[_frameIndex];
        var device = _context.Device;

        // Wait until the GPU has finished with this slot's command buffer from FramesInFlight frames ago.
        device.WaitForFences([frame.InFlight], true, NoTimeout);

        if (_swapchainStale && !RecreateSwapchain())
        {
            return FrameResult.Skipped;
        }

        var swapchain = _swapchain!;
        int imageIndex;
        try
        {
            var acquired = device.AcquireNextImageKHR(swapchain.Handle, NoTimeout, frame.ImageAvailable, default, out imageIndex);
            if (acquired == VkResult.SuboptimalKHR)
            {
                // Still presentable; rebuild once this frame is out.
                _swapchainStale = true;
            }
        }
        catch (VkResultError error) when (error.Result == VkResult.ErrorOutOfDateKHR)
        {
            _swapchainStale = true;
            return FrameResult.Skipped;
        }

        // Only reset the fence once work is certain to be submitted, or the next wait would deadlock.
        device.ResetFences([frame.InFlight]);
        Record(frame.Commands, swapchain, imageIndex, clearColor);
        Submit(ref frame, imageIndex);
        Present(swapchain, imageIndex);

        _frameIndex = (_frameIndex + 1) % FramesInFlight;
        FramesPresented++;
        return FrameResult.Presented;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var device = _context.Device;
        device.WaitIdle();

        DestroyRenderFinishedSemaphores();
        _swapchain?.Dispose();
        foreach (var frame in _frames)
        {
            device.DestroyFence(frame.InFlight, null);
            device.DestroySemaphore(frame.ImageAvailable, null);
            device.DestroyCommandPool(frame.CommandPool, null);
        }

        _context.Dispose();
    }

    private static void Record(CommandBuffer commands, Swapchain swapchain, int imageIndex, LinearColor clearColor)
    {
        commands.Reset(default);
        commands.Begin(new VkCommandBufferBeginInfo { Flags = VkCommandBufferUsageFlags.OneTimeSubmitBit });

        var image = swapchain.Images[imageIndex];

        // Undefined → colour attachment. Waits on the acquire semaphore's stage, discards old contents.
        var toAttachment = new VkImageMemoryBarrier2
        {
            SrcStageMask = VkPipelineStageFlags2.ColorAttachmentOutputBit,
            SrcAccessMask = VkAccessFlags2.None,
            DstStageMask = VkPipelineStageFlags2.ColorAttachmentOutputBit,
            DstAccessMask = VkAccessFlags2.ColorAttachmentWriteBit,
            OldLayout = VkImageLayout.Undefined,
            NewLayout = VkImageLayout.ColorAttachmentOptimal,
            SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
            DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
            Image = image,
            SubresourceRange = Swapchain.ColorSubresource,
        };
        commands.PipelineBarrier2(new VkDependencyInfo { ImageMemoryBarrierCount = 1, ImageMemoryBarriers = &toAttachment });

        var colorAttachment = new VkRenderingAttachmentInfo
        {
            ImageView = swapchain.Views[imageIndex],
            ImageLayout = VkImageLayout.ColorAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.Store,
            ClearValue = new VkClearValue
            {
                Color = new VkClearColorValue { Float32 = new float4(clearColor.R, clearColor.G, clearColor.B, clearColor.A) },
            },
        };

        commands.BeginRendering(new VkRenderingInfo
        {
            RenderArea = new VkRect2D { Offset = default, Extent = swapchain.Extent },
            LayerCount = 1,
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
        });

        // Terrain, sprite and UI passes are recorded here from M2.
        commands.EndRendering();

        // Colour attachment → present.
        var toPresent = new VkImageMemoryBarrier2
        {
            SrcStageMask = VkPipelineStageFlags2.ColorAttachmentOutputBit,
            SrcAccessMask = VkAccessFlags2.ColorAttachmentWriteBit,
            DstStageMask = VkPipelineStageFlags2.None,
            DstAccessMask = VkAccessFlags2.None,
            OldLayout = VkImageLayout.ColorAttachmentOptimal,
            NewLayout = VkImageLayout.PresentSrcKHR,
            SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
            DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
            Image = image,
            SubresourceRange = Swapchain.ColorSubresource,
        };
        commands.PipelineBarrier2(new VkDependencyInfo { ImageMemoryBarrierCount = 1, ImageMemoryBarriers = &toPresent });

        commands.End();
    }

    private void Submit(ref FrameSlot frame, int imageIndex)
    {
        var wait = new VkSemaphoreSubmitInfo
        {
            Semaphore = frame.ImageAvailable,
            StageMask = VkPipelineStageFlags2.ColorAttachmentOutputBit,
        };
        var signal = new VkSemaphoreSubmitInfo
        {
            Semaphore = _renderFinished[imageIndex],
            StageMask = VkPipelineStageFlags2.ColorAttachmentOutputBit,
        };
        var commandBuffer = new VkCommandBufferSubmitInfo { CommandBuffer = frame.Commands };
        var submit = new VkSubmitInfo2
        {
            WaitSemaphoreInfoCount = 1,
            WaitSemaphoreInfos = &wait,
            CommandBufferInfoCount = 1,
            CommandBufferInfos = &commandBuffer,
            SignalSemaphoreInfoCount = 1,
            SignalSemaphoreInfos = &signal,
        };

        _context.Queue.Submit2(new Span<VkSubmitInfo2>(&submit, 1), frame.InFlight);
    }

    private void Present(Swapchain swapchain, int imageIndex)
    {
        var waitSemaphore = _renderFinished[imageIndex];
        var swapchainHandle = swapchain.Handle;
        var presentInfo = new VkPresentInfoKHR
        {
            WaitSemaphoreCount = 1,
            WaitSemaphores = &waitSemaphore,
            SwapchainCount = 1,
            Swapchains = &swapchainHandle,
            ImageIndices = &imageIndex,
        };

        try
        {
            if (_context.Queue.PresentKHR(presentInfo) == VkResult.SuboptimalKHR)
            {
                _swapchainStale = true;
            }
        }
        catch (VkResultError error) when (error.Result == VkResult.ErrorOutOfDateKHR)
        {
            _swapchainStale = true;
        }
    }

    /// <summary>Rebuilds the swapchain for the current window size. Returns false while the window has no area.</summary>
    private bool RecreateSwapchain()
    {
        var framebufferSize = _surfaceSource.FramebufferSize;
        if (framebufferSize.IsEmpty)
        {
            return false;
        }

        _context.Device.WaitIdle();
        var previous = _swapchain;
        var replacement = Swapchain.Create(_context, framebufferSize, _options.VSync, previous);
        previous?.Dispose();
        DestroyRenderFinishedSemaphores();

        _swapchain = replacement;
        if (replacement is null)
        {
            return false;
        }

        _renderFinished = new VkSemaphore[replacement.Images.Length];
        for (var i = 0; i < _renderFinished.Length; i++)
        {
            _renderFinished[i] = _context.Device.CreateSemaphore(new VkSemaphoreCreateInfo(), null);
        }

        _swapchainStale = false;
        SwapchainGenerations++;
        return true;
    }

    private void DestroyRenderFinishedSemaphores()
    {
        foreach (var semaphore in _renderFinished)
        {
            _context.Device.DestroySemaphore(semaphore, null);
        }

        _renderFinished = [];
    }

    private FrameSlot CreateFrameSlot()
    {
        var device = _context.Device;
        var pool = device.CreateCommandPool(
            new VkCommandPoolCreateInfo
            {
                Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit,
                QueueFamilyIndex = _context.QueueFamily,
            },
            null);

        return new FrameSlot
        {
            CommandPool = pool,
            Commands = device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo
            {
                CommandPool = pool,
                Level = VkCommandBufferLevel.Primary,
            }),
            ImageAvailable = device.CreateSemaphore(new VkSemaphoreCreateInfo(), null),

            // Created signalled so the first wait on each slot returns immediately.
            InFlight = device.CreateFence(new VkFenceCreateInfo { Flags = VkFenceCreateFlags.SignaledBit }, null),
        };
    }

    private struct FrameSlot
    {
        public VkCommandPool CommandPool;
        public CommandBuffer Commands;
        public VkSemaphore ImageAvailable;
        public VkFence InFlight;
    }
}
