namespace Ashfall.Rendering.Tests;

public class LinearColorTests
{
    [Fact]
    public void Black_and_white_are_unchanged_by_the_sRGB_curve()
    {
        Assert.Equal(new LinearColor(0f, 0f, 0f), LinearColor.FromSrgb(0, 0, 0));
        Assert.Equal(new LinearColor(1f, 1f, 1f), LinearColor.FromSrgb(255, 255, 255));
    }

    [Theory]
    [InlineData(10, 0.003035f)]
    [InlineData(128, 0.215861f)]
    [InlineData(188, 0.502886f)]
    public void Mid_tones_follow_the_sRGB_transfer_function(int encoded, float expectedLinear)
    {
        var value = (byte)encoded;
        var color = LinearColor.FromSrgb(value, value, value);

        Assert.InRange(color.R, expectedLinear - 1e-5f, expectedLinear + 1e-5f);
        Assert.Equal(color.R, color.G);
        Assert.Equal(color.R, color.B);
    }

    [Fact]
    public void Alpha_is_linear()
    {
        Assert.InRange(LinearColor.FromSrgb(0, 0, 0, 128).A, 0.5019f, 0.502f);
    }
}
