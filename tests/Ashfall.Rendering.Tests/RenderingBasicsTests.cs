using Ashfall.Rendering.Backend;

namespace Ashfall.Rendering.Tests;

public class RenderingBasicsTests
{
    [Theory]
    [InlineData(0, 720, true)]
    [InlineData(1280, 0, true)]
    [InlineData(-1, 10, true)]
    [InlineData(1280, 720, false)]
    public void A_size_with_no_area_is_empty(int width, int height, bool empty)
    {
        Assert.Equal(empty, new PixelSize(width, height).IsEmpty);
    }

    [Fact]
    public void Pixel_sizes_print_as_width_by_height()
    {
        Assert.Equal("1280x720", new PixelSize(1280, 720).ToString());
    }

    [Fact]
    public void Vulkan_versions_pack_like_VK_MAKE_API_VERSION()
    {
        Assert.Equal(4_206_592, VulkanVersion.Make(1, 3));
        Assert.Equal(VulkanVersion.Make(1, 3), VulkanVersion.Required);

        var version = VulkanVersion.Make(1, 4, 318);
        Assert.Equal(1, VulkanVersion.Major(version));
        Assert.Equal(4, VulkanVersion.Minor(version));
        Assert.Equal(318, VulkanVersion.Patch(version));
        Assert.Equal("1.4.318", VulkanVersion.Format(version));
    }

    [Fact]
    public void The_renderer_does_not_depend_on_any_other_Ashfall_assembly()
    {
        // Presentation reads simulation state through snapshots handed to it (from M2), never by reference.
        var references = typeof(VulkanRenderer).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Ashfall", StringComparison.Ordinal));

        Assert.Empty(references);
    }
}
