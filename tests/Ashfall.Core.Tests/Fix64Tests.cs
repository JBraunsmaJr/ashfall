namespace Ashfall.Core.Tests;

public class Fix64Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(255)]
    [InlineData(-32_768)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Whole_numbers_round_trip(int value)
    {
        var fixedValue = Fix64.FromInt(value);

        Assert.Equal(value, fixedValue.FloorToInt());
        Assert.Equal(value, fixedValue.CeilingToInt());
    }

    [Fact]
    public void Arithmetic_on_exact_values_is_exact()
    {
        var oneAndHalf = Fix64.FromRatio(3, 2);

        Assert.Equal(Fix64.FromInt(3), oneAndHalf * 2);
        Assert.Equal(Fix64.FromInt(3), oneAndHalf + oneAndHalf);
        Assert.Equal(Fix64.FromRatio(9, 4), oneAndHalf * oneAndHalf);
        Assert.Equal(Fix64.FromRatio(3, 4), oneAndHalf / 2);
        Assert.Equal(Fix64.FromRatio(3, 4), oneAndHalf / Fix64.FromInt(2));
        Assert.Equal(Fix64.Zero, oneAndHalf - oneAndHalf);
        Assert.Equal(Fix64.FromRatio(-3, 2), -oneAndHalf);
    }

    [Fact]
    public void Division_truncates_toward_negative_infinity()
    {
        var third = Fix64.FromRatio(1, 3);
        var minusThird = Fix64.FromRatio(-1, 3);

        Assert.Equal(1_431_655_765L, third.RawValue);
        Assert.Equal(-1_431_655_766L, minusThird.RawValue);
        Assert.Equal(minusThird, Fix64.FromInt(-1) / 3);
        Assert.Equal(minusThird, Fix64.FromInt(-1) / Fix64.FromInt(3));
    }

    [Fact]
    public void Dividing_by_zero_throws()
    {
        Assert.Throws<DivideByZeroException>(() => Fix64.One / Fix64.Zero);
        Assert.Throws<DivideByZeroException>(() => Fix64.One / 0);
        Assert.Throws<DivideByZeroException>(() => Fix64.FromRatio(1, 0));
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("1", 4_294_967_296L)]
    [InlineData("0.5", 2_147_483_648L)]
    [InlineData(".25", 1_073_741_824L)]
    [InlineData("+2", 8_589_934_592L)]
    [InlineData("-12.375", -53_150_220_288L)]
    [InlineData("  3.0  ", 12_884_901_888L)]
    public void Parse_reads_decimal_strings_exactly(string text, long expectedRaw)
    {
        Assert.Equal(expectedRaw, Fix64.Parse(text).RawValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("--1")]
    [InlineData("1e5")]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("3000000000")]
    public void TryParse_rejects_invalid_text(string text)
    {
        Assert.False(Fix64.TryParse(text, out _));
    }

    [Fact]
    public void Parse_matches_ratio_for_inexact_decimals()
    {
        // 0.1 is not exactly representable; parsing and FromRatio must agree bit for bit.
        Assert.Equal(Fix64.FromRatio(1, 10), Fix64.Parse("0.1"));
        Assert.Equal(Fix64.FromRatio(9, 10), Fix64.Parse("0.9"));
    }

    [Fact]
    public void ToString_formats_with_integer_maths()
    {
        Assert.Equal("1.0000", Fix64.One.ToString());
        Assert.Equal("-1.50", Fix64.FromRatio(-3, 2).ToString(2));
        Assert.Equal("1.00", Fix64.FromRatio(99_999, 100_000).ToString(2));
        Assert.Equal("0.00", Fix64.FromRaw(-1).ToString(2));
        Assert.Equal("3", Fix64.FromRatio(5, 2).ToString(0));
        Assert.Equal("3.1416", Fix64.Pi.ToString());
    }

    [Fact]
    public void Rounding_helpers_handle_negative_values()
    {
        var value = Fix64.FromRatio(-3, 2);

        Assert.Equal(-2, value.FloorToInt());
        Assert.Equal(-1, value.CeilingToInt());
        Assert.Equal(-1, value.RoundToInt());
        Assert.Equal(Fix64.FromInt(-2), value.Floor());
        Assert.Equal(Fix64.Half, value.Fraction());
    }

    [Fact]
    public void Comparison_and_clamping_follow_numeric_order()
    {
        var low = Fix64.FromInt(-1);
        var high = Fix64.FromInt(2);

        Assert.True(low < high);
        Assert.True(high >= low);
        Assert.Equal(low, Fix64.Min(low, high));
        Assert.Equal(high, Fix64.Max(low, high));
        Assert.Equal(Fix64.Zero, Fix64.Clamp(Fix64.Zero, low, high));
        Assert.Equal(high, Fix64.Clamp(Fix64.FromInt(9), low, high));
        Assert.Equal(Fix64.One, Fix64.Abs(-Fix64.One));
        Assert.Equal(-1, low.CompareTo(high));
    }

    [Fact]
    public void Pi_constants_are_consistent()
    {
        // Each constant is rounded independently, so they agree to within one unit in the last place.
        Assert.InRange(Fix64.TwoPi.RawValue, (Fix64.Pi * 2).RawValue - 1, (Fix64.Pi * 2).RawValue + 1);
        Assert.InRange(Fix64.HalfPi.RawValue, (Fix64.Pi / 2).RawValue - 1, (Fix64.Pi / 2).RawValue + 1);
    }
}
