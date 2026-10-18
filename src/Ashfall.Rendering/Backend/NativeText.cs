using System.Runtime.InteropServices;
using System.Text;

namespace Ashfall.Rendering.Backend;

/// <summary>Reads the null-terminated UTF-8 strings Vulkan returns in fixed-size buffers.</summary>
internal static unsafe class NativeText
{
    public static string FromNullTerminated(byte* text) =>
        text is null ? string.Empty : Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text));
}
