using System.Numerics;
using Ashfall.Rendering.Backend;
using Ashfall.Rendering.Scene;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace Ashfall.Rendering;

/// <summary>
/// The game's renderer. Each frame it uploads a <see cref="FrameScene"/> (sorted, lit quads and point
/// lights, built by the UI layer) and draws it in one pass with dynamic rendering. Frames can be read
/// back for screenshots and CI image checks.
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
    private readonly FrameScene _clearOnly = new();
    private VkSemaphore[] _renderFinished = [];
    private Swapchain? _swapchain;
    private ScenePipeline? _pipeline;
    private HostBuffer? _readback;
    private bool _captureRequested;
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

    /// <summary>The most recent frame read back after <see cref="RequestCapture"/>, or null.</summary>
    public CapturedFrame? LastCapture { get; private set; }

    /// <summary>Quads drawn in the last frame.</summary>
    public int LastQuadCount { get; private set; }

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

    /// <summary>Reads the next presented frame back into <see cref="LastCapture"/> (if the surface allows it).</summary>
    public void RequestCapture() => _captureRequested = true;

    /// <summary>Records, submits and presents one frame that only clears the window to <paramref name="clearColor"/>.</summary>
    public FrameResult RenderFrame(LinearColor clearColor)
    {
        _clearOnly.Clear();
        _clearOnly.ClearColor = clearColor;
        return RenderFrame(_clearOnly);
    }

    /// <summary>Uploads <paramref name="scene"/>, then records, submits and presents one frame.</summary>
    public FrameResult RenderFrame(FrameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
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
        Upload(ref frame, scene);
        var capture = _captureRequested && swapchain.SupportsCapture;
        if (capture)
        {
            var bytes = (long)swapchain.Extent.Width * swapchain.Extent.Height * 4;
            _readback ??= new HostBuffer(_context, VkBufferUsageFlags.TransferDstBit, bytes);
            _readback.EnsureCapacity(bytes);
        }

        Record(frame, swapchain, imageIndex, scene, capture);
        Submit(ref frame, imageIndex);
        Present(swapchain, imageIndex);
        if (capture)
        {
            device.WaitForFences([frame.InFlight], true, NoTimeout);
            LastCapture = ReadCapture(swapchain);
            _captureRequested = false;
        }

        LastQuadCount = scene.QuadCount;

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
        _pipeline?.Dispose();
        _readback?.Dispose();
        foreach (var frame in _frames)
        {
            frame.Vertices.Dispose();
            frame.Indices.Dispose();
            frame.Lights.Dispose();
            device.DestroyFence(frame.InFlight, null);
            device.DestroySemaphore(frame.ImageAvailable, null);
            device.DestroyCommandPool(frame.CommandPool, null);
        }

        _context.Dispose();
    }

    private void Upload(ref FrameSlot frame, FrameScene scene)
    {
        var vertices = scene.Vertices;
        frame.Vertices.EnsureCapacity(Math.Max(1, vertices.Length) * (long)SceneVertex.SizeInBytes);
        frame.Vertices.Write(vertices);

        // Two triangles per quad, sharing the quad's four vertices. The pattern never changes, so it is
        // only rewritten when the buffer grows.
        var indexCount = scene.QuadCount * 6;
        if (frame.Indices.EnsureCapacity(Math.Max(1, indexCount) * (long)sizeof(uint)) || frame.IndexQuads < scene.QuadCount)
        {
            var quads = (int)(frame.Indices.Capacity / (6 * sizeof(uint)));
            var indices = new uint[quads * 6];
            for (var quad = 0; quad < quads; quad++)
            {
                var vertex = (uint)(quad * 4);
                var at = quad * 6;
                indices[at] = vertex;
                indices[at + 1] = vertex + 1;
                indices[at + 2] = vertex + 2;
                indices[at + 3] = vertex + 2;
                indices[at + 4] = vertex + 3;
                indices[at + 5] = vertex;
            }

            frame.Indices.Write<uint>(indices);
            frame.IndexQuads = quads;
        }

        var lightCount = Math.Min(scene.Lights.Count, FrameScene.MaxLights);
        Span<PointLight> lights = stackalloc PointLight[Math.Max(1, lightCount)];
        for (var i = 0; i < lightCount; i++)
        {
            lights[i] = scene.Lights[i];
        }

        frame.Lights.Write<PointLight>(lights[..Math.Max(1, lightCount)]);
        frame.LightCount = lightCount;
    }

    private void Record(in FrameSlot frame, Swapchain swapchain, int imageIndex, FrameScene scene, bool capture)
    {
        var commands = frame.Commands;
        commands.Reset(default);
        commands.Begin(new VkCommandBufferBeginInfo { Flags = VkCommandBufferUsageFlags.OneTimeSubmitBit });

        var image = swapchain.Images[imageIndex];

        // Undefined → colour attachment. Waits on the acquire semaphore's stage, discards old contents.
        Transition(commands, image,
            VkPipelineStageFlags2.ColorAttachmentOutputBit, VkAccessFlags2.None, VkImageLayout.Undefined,
            VkPipelineStageFlags2.ColorAttachmentOutputBit, VkAccessFlags2.ColorAttachmentWriteBit, VkImageLayout.ColorAttachmentOptimal);

        var clear = scene.ClearColor;
        var colorAttachment = new VkRenderingAttachmentInfo
        {
            ImageView = swapchain.Views[imageIndex],
            ImageLayout = VkImageLayout.ColorAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.Store,
            ClearValue = new VkClearValue
            {
                Color = new VkClearColorValue { Float32 = new float4(clear.R, clear.G, clear.B, clear.A) },
            },
        };

        commands.BeginRendering(new VkRenderingInfo
        {
            RenderArea = new VkRect2D { Offset = default, Extent = swapchain.Extent },
            LayerCount = 1,
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
        });

        if (scene.QuadCount > 0 && _pipeline is not null)
        {
            DrawScene(commands, frame, swapchain.Extent, scene);
        }

        commands.EndRendering();

        if (capture)
        {
            Transition(commands, image,
                VkPipelineStageFlags2.ColorAttachmentOutputBit, VkAccessFlags2.ColorAttachmentWriteBit, VkImageLayout.ColorAttachmentOptimal,
                VkPipelineStageFlags2.CopyBit, VkAccessFlags2.TransferReadBit, VkImageLayout.TransferSrcOptimal);
            var region = new VkBufferImageCopy
            {
                BufferOffset = (ByteSize64)0,
                ImageSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageExtent = new VkExtent3D { Width = swapchain.Extent.Width, Height = swapchain.Extent.Height, Depth = 1 },
            };
            commands.CopyImageToBuffer(image, VkImageLayout.TransferSrcOptimal, _readback!.Buffer, new ReadOnlySpan<VkBufferImageCopy>(&region, 1));
            Transition(commands, image,
                VkPipelineStageFlags2.CopyBit, VkAccessFlags2.TransferReadBit, VkImageLayout.TransferSrcOptimal,
                VkPipelineStageFlags2.None, VkAccessFlags2.None, VkImageLayout.PresentSrcKHR);
        }
        else
        {
            // Colour attachment → present.
            Transition(commands, image,
                VkPipelineStageFlags2.ColorAttachmentOutputBit, VkAccessFlags2.ColorAttachmentWriteBit, VkImageLayout.ColorAttachmentOptimal,
                VkPipelineStageFlags2.None, VkAccessFlags2.None, VkImageLayout.PresentSrcKHR);
        }

        commands.End();
    }

    private void DrawScene(CommandBuffer commands, in FrameSlot frame, VkExtent2D extent, FrameScene scene)
    {
        var pipeline = _pipeline!;
        commands.BindPipeline(VkPipelineBindPoint.Graphics, pipeline.Pipeline);

        var viewport = new VkViewport { X = 0, Y = 0, Width = extent.Width, Height = extent.Height, MinDepth = 0, MaxDepth = 1 };
        commands.SetViewport(0, new ReadOnlySpan<VkViewport>(&viewport, 1));
        var scissor = new VkRect2D { Offset = default, Extent = extent };
        commands.SetScissor(0, new ReadOnlySpan<VkRect2D>(&scissor, 1));

        var set = frame.DescriptorSet;
        commands.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipeline.Layout, 0, new ReadOnlySpan<VkDescriptorSet>(&set, 1), []);

        var sun = Vector3.Normalize(scene.SunDirection);
        var constants = new ScenePipeline.SceneConstants
        {
            Viewport = new Vector2(extent.Width, extent.Height),
            LightCount = (uint)frame.LightCount,
            Time = scene.Time,
            SunDirection = new Vector4(sun, 0),
            SunColor = new Vector4(scene.SunColor.R, scene.SunColor.G, scene.SunColor.B, 1),
            Ambient = new Vector4(scene.Ambient.R, scene.Ambient.G, scene.Ambient.B, 1),
        };
        commands.PushConstants(pipeline.Layout, VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit, ByteSize.Zero, constants);

        commands.BindVertexBuffer(0, frame.Vertices.Buffer);
        commands.BindIndexBuffer(frame.Indices.Buffer, (ByteSize64)0, VkIndexType.UInt32);
        commands.DrawIndexed(scene.QuadCount * 6, 1, 0, 0, 0);
    }

    private static void Transition(
        CommandBuffer commands,
        VkImage image,
        VkPipelineStageFlags2 sourceStage,
        VkAccessFlags2 sourceAccess,
        VkImageLayout oldLayout,
        VkPipelineStageFlags2 destinationStage,
        VkAccessFlags2 destinationAccess,
        VkImageLayout newLayout)
    {
        var barrier = new VkImageMemoryBarrier2
        {
            SrcStageMask = sourceStage,
            SrcAccessMask = sourceAccess,
            DstStageMask = destinationStage,
            DstAccessMask = destinationAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
            DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
            Image = image,
            SubresourceRange = Swapchain.ColorSubresource,
        };
        commands.PipelineBarrier2(new VkDependencyInfo { ImageMemoryBarrierCount = 1, ImageMemoryBarriers = &barrier });
    }

    // Copies the read-back image into tightly packed RGBA, swapping channels for BGRA swapchains.
    private CapturedFrame ReadCapture(Swapchain swapchain)
    {
        var width = swapchain.Extent.Width;
        var height = swapchain.Extent.Height;
        var source = _readback!.Read((long)width * height * 4);
        var rgba = source.ToArray();
        var format = swapchain.Format.Format;
        if (format is VkFormat.B8G8R8A8SRGB or VkFormat.B8G8R8A8UNorm)
        {
            for (var i = 0; i < rgba.Length; i += 4)
            {
                (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
            }
        }

        for (var i = 3; i < rgba.Length; i += 4)
        {
            rgba[i] = 255;
        }

        return new CapturedFrame(new PixelSize(width, height), rgba);
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

        if (_pipeline is null || _pipeline.ColorFormat != replacement.Format.Format)
        {
            _pipeline?.Dispose();
            _pipeline = new ScenePipeline(_context, replacement.Format.Format, FramesInFlight);
            for (var i = 0; i < FramesInFlight; i++)
            {
                _frames[i].DescriptorSet = _pipeline.AllocateSet();
                _pipeline.BindLights(_frames[i].DescriptorSet, _frames[i].Lights);
            }
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
            Vertices = new HostBuffer(_context, VkBufferUsageFlags.VertexBufferBit, 1024L * 1024),
            Indices = new HostBuffer(_context, VkBufferUsageFlags.IndexBufferBit, 256L * 1024),
            Lights = new HostBuffer(_context, VkBufferUsageFlags.StorageBufferBit, (long)FrameScene.MaxLights * PointLight.SizeInBytes),
        };
    }

    private struct FrameSlot
    {
        public VkCommandPool CommandPool;
        public CommandBuffer Commands;
        public VkSemaphore ImageAvailable;
        public VkFence InFlight;
        public HostBuffer Vertices;
        public HostBuffer Indices;
        public HostBuffer Lights;
        public VkDescriptorSet DescriptorSet;
        public int IndexQuads;
        public int LightCount;
    }
}
