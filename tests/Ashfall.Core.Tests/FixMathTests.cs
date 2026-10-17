namespace Ashfall.Core.Tests;

public class FixMathTests
{
    // 1e-7 in raw units (2^32 * 1e-7 ≈ 429.5).
    private const long SevenDecimalPlaces = 430;

    [Theory]
    [InlineData(0, 1, 0, 1)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(4, 1, 2, 1)]
    [InlineData(9, 4, 3, 2)]
    [InlineData(1, 4, 1, 2)]
    [InlineData(1_000_000, 1, 1_000, 1)]
    public void Sqrt_of_a_perfect_square_is_exact(long numerator, long denominator, long rootNumerator, long rootDenominator)
    {
        Assert.Equal(
            Fix64.FromRatio(rootNumerator, rootDenominator),
            FixMath.Sqrt(Fix64.FromRatio(numerator, denominator)));
    }

    [Fact]
    public void Sqrt_of_two_squares_back_to_two()
    {
        var root = FixMath.Sqrt(Fix64.FromInt(2));

        Assert.InRange((root * root).RawValue, Fix64.FromInt(2).RawValue - 8, Fix64.FromInt(2).RawValue);
    }

    [Fact]
    public void Sqrt_of_a_negative_number_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FixMath.Sqrt(-Fix64.One));
    }

    [Fact]
    public void Sine_and_cosine_hit_their_landmarks()
    {
        Assert.Equal(Fix64.Zero, FixMath.Sin(Fix64.Zero));
        AssertClose(Fix64.One, FixMath.Sin(Fix64.HalfPi));
        AssertClose(Fix64.Zero, FixMath.Sin(Fix64.Pi));
        AssertClose(-Fix64.One, FixMath.Sin(-Fix64.HalfPi));
        AssertClose(Fix64.Half, FixMath.Sin(Fix64.Pi / 6));
        AssertClose(Fix64.One, FixMath.Cos(Fix64.Zero));
        AssertClose(-Fix64.One, FixMath.Cos(Fix64.Pi));
    }

    [Fact]
    public void Sine_squared_plus_cosine_squared_is_one_across_the_circle()
    {
        for (var step = -64; step <= 64; step++)
        {
            var angle = Fix64.Pi * step / 16;
            var sin = FixMath.Sin(angle);
            var cos = FixMath.Cos(angle);

            AssertClose(Fix64.One, (sin * sin) + (cos * cos));
        }
    }

    [Fact]
    public void Modulo_is_never_negative()
    {
        Assert.Equal(Fix64.FromInt(1), FixMath.Modulo(Fix64.FromInt(-2), Fix64.FromInt(3)));
        Assert.Equal(Fix64.FromInt(2), FixMath.Modulo(Fix64.FromInt(5), Fix64.FromInt(3)));
        Assert.Equal(Fix64.Zero, FixMath.Modulo(Fix64.FromInt(6), Fix64.FromInt(3)));
    }

    [Fact]
    public void Lerp_interpolates_linearly()
    {
        Assert.Equal(Fix64.FromInt(15), FixMath.Lerp(Fix64.FromInt(10), Fix64.FromInt(20), Fix64.Half));
    }

    private static void AssertClose(Fix64 expected, Fix64 actual) =>
        Assert.InRange(actual.RawValue, expected.RawValue - SevenDecimalPlaces, expected.RawValue + SevenDecimalPlaces);
}
