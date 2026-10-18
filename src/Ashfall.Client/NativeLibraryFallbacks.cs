using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Ashfall.Client;

/// <summary>
/// BRUTAL loads the Vulkan loader as <c>vulkan</c>, which on Linux resolves to <c>libvulkan.so</c>: a
/// development symlink that most players' systems do not have. Every Linux system with a Vulkan driver
/// does have the runtime soname <c>libvulkan.so.1</c>, so fall back to it when the default lookup fails.
/// </summary>
internal static class NativeLibraryFallbacks
{
    private static readonly string[] LinuxVulkanNames = ["libvulkan.so.1", "libvulkan.so"];

    public static void Register()
    {
        if (OperatingSystem.IsLinux())
        {
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveLinuxVulkan;
        }
    }

    private static nint ResolveLinuxVulkan(Assembly assembly, string libraryName)
    {
        if (libraryName != "vulkan")
        {
            return nint.Zero;
        }

        foreach (var name in LinuxVulkanNames)
        {
            if (NativeLibrary.TryLoad(name, out var handle))
            {
                return handle;
            }
        }

        return nint.Zero;
    }
}
