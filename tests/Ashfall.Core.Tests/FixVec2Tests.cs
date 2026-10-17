namespace Ashfall.Core.Tests;

public class FixVec2Tests
{
    [Fact]
    public void Length_of_a_3_4_5_triangle_is_exact()
    {
        Assert.Equal(Fix64.FromInt(5), FixVec2.FromInts(3, 4).Length);
        Assert.Equal(Fix64.FromInt(25), FixVec2.FromInts(-3, 4).LengthSquared);
    }

    [Fact]
    public void Normalizing_gives_a_unit_vector()
    {
        var unit = FixVec2.FromInts(3, 4).Normalized();

        AssertWithinOneUnit(Fix64.FromRatio(3, 5), unit.X);
        AssertWithinOneUnit(Fix64.FromRatio(4, 5), unit.Y);
    }

    [Fact]
    public void Normalizing_zero_gives_zero()
    {
        Assert.Equal(FixVec2.Zero, FixVec2.Zero.Normalized());
    }

    [Fact]
    public void Operators_work_per_component()
    {
        var a = FixVec2.FromInts(1, 2);
        var b = FixVec2.FromInts(3, -4);

        Assert.Equal(FixVec2.FromInts(4, -2), a + b);
        Assert.Equal(FixVec2.FromInts(-2, 6), a - b);
        Assert.Equal(FixVec2.FromInts(2, 4), a * Fix64.FromInt(2));
        Assert.Equal(Fix64.FromInt(-5), FixVec2.Dot(a, b));
        Assert.Equal(Fix64.FromInt(40), FixVec2.DistanceSquared(a, b));
    }

    private static void AssertWithinOneUnit(Fix64 expected, Fix64 actual) =>
        Assert.InRange(actual.RawValue, expected.RawValue - 1, expected.RawValue + 1);
}
