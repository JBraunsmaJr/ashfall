using System.Globalization;

namespace Ashfall.Rendering.Backend;

/// <summary>VK_MAKE_API_VERSION and friends.</summary>
internal static class VulkanVersion
{
    public static int Make(int major, int minor, int patch = 0) => (major << 22) | (minor << 12) | patch;

    public static int Major(int version) => (version >> 22) & 0x7F;

    public static int Minor(int version) => (version >> 12) & 0x3FF;

    public static int Patch(int version) => version & 0xFFF;

    public static string Format(int version) =>
        string.Create(CultureInfo.InvariantCulture, $"{Major(version)}.{Minor(version)}.{Patch(version)}");

    /// <summary>The minimum the renderer needs: dynamic rendering and synchronization2 are core in 1.3.</summary>
    public static int Required => Make(1, 3);
}
