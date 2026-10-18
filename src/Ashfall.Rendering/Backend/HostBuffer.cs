using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace Ashfall.Rendering.Backend;

/// <summary>
/// A buffer in host-visible, coherent memory, mapped for its whole life. Used for data the CPU
/// rewrites every frame (vertices, lights) and for reading frames back. Each frame in flight owns its
/// own buffers, so a buffer is only rewritten after the fence of the frame that last used it.
/// </summary>
internal sealed unsafe class HostBuffer : IDisposable
{
    private readonly VulkanContext _context;
    private readonly VkBufferUsageFlags _usage;

    public HostBuffer(VulkanContext context, VkBufferUsageFlags usage, long capacity)
    {
        _context = context;
        _usage = usage;
        Allocate(capacity);
    }

    public VkBuffer Buffer { get; private set; }

    public long Capacity { get; private set; }

    public byte* Mapped { get; private set; }

    private VkDeviceMemory Memory { get; set; }

    /// <summary>Grows the buffer (discarding its contents) if it is smaller than <paramref name="bytes"/>. Returns true if it was reallocated.</summary>
    public bool EnsureCapacity(long bytes)
    {
        if (bytes <= Capacity)
        {
            return false;
        }

        Release();
        Allocate(Math.Max(bytes, Capacity * 2));
        return true;
    }

    public void Write<T>(ReadOnlySpan<T> data, long offset = 0)
        where T : unmanaged
    {
        var bytes = (long)data.Length * sizeof(T);
        if (offset + bytes > Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "Data does not fit in the buffer.");
        }

        data.CopyTo(new Span<T>(Mapped + offset, data.Length));
    }

    public ReadOnlySpan<byte> Read(long bytes) => new(Mapped, (int)Math.Min(bytes, Capacity));

    public void Dispose() => Release();

    private void Allocate(long capacity)
    {
        var device = _context.Device;
        Buffer = device.CreateBuffer(
            new VkBufferCreateInfo
            {
                Size = (ByteSize64)(ulong)capacity,
                Usage = _usage,
                SharingMode = VkSharingMode.Exclusive,
            },
            null);
        Memory = device.AllocateMemory(
            _context.PhysicalDevice,
            Buffer,
            VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,
            null);
        device.BindBufferMemory(Buffer, Memory, (ByteSize64)0);
        Mapped = (byte*)device.MapMemory(new MapInfo(Memory)).Pointer;
        Capacity = capacity;
    }

    private void Release()
    {
        if (Memory.IsNull())
        {
            return;
        }

        var device = _context.Device;
        device.UnmapMemory(Memory);
        device.DestroyBuffer(Buffer, null);
        device.FreeMemory(Memory, null);
        Buffer = default;
        Memory = default;
        Mapped = null;
        Capacity = 0;
    }
}
