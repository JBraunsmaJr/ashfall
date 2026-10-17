namespace Ashfall.Core;

/// <summary>Deterministic maths functions on <see cref="Fix64"/>, built from integer operations only.</summary>
public static class FixMath
{
    /// <summary>Square root, truncated to 2^-32. Throws for negative input.</summary>
    public static Fix64 Sqrt(Fix64 value)
    {
        if (value.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Square root of a negative number.");
        }

        // sqrt(raw / 2^32) * 2^32 == sqrt(raw * 2^32)
        var root = IntegerSqrt((UInt128)(ulong)value.RawValue << Fix64.FractionalBits);
        return Fix64.FromRaw((long)root);
    }

    /// <summary>Sine of an angle in radians.</summary>
    public static Fix64 Sin(Fix64 radians)
    {
        // Reduce to [0, 2π), then fold into [0, π/2] keeping track of the sign.
        var x = Modulo(radians, Fix64.TwoPi);
        var negative = false;
        if (x >= Fix64.Pi)
        {
            x -= Fix64.Pi;
            negative = true;
        }

        if (x > Fix64.HalfPi)
        {
            x = Fix64.Pi - x;
        }

        var result = SinQuadrant(x);
        return negative ? -result : result;
    }

    /// <summary>Cosine of an angle in radians.</summary>
    public static Fix64 Cos(Fix64 radians) => Sin(radians + Fix64.HalfPi);

    /// <summary>Remainder of <paramref name="value"/> / <paramref name="modulus"/>, always in [0, modulus).</summary>
    public static Fix64 Modulo(Fix64 value, Fix64 modulus)
    {
        var remainder = value.RawValue % modulus.RawValue;
        if (remainder < 0)
        {
            remainder += modulus.RawValue;
        }

        return Fix64.FromRaw(remainder);
    }

    /// <summary>Linear interpolation from <paramref name="a"/> to <paramref name="b"/> by <paramref name="t"/> (0..1).</summary>
    public static Fix64 Lerp(Fix64 a, Fix64 b, Fix64 t) => a + ((b - a) * t);

    // Taylor series for sin on [0, π/2] in Horner form:
    // x(1 - x²/6(1 - x²/20(1 - x²/42(1 - x²/72(1 - x²/110(1 - x²/156)))))).
    // The truncation error is below 1e-8; each step uses exact integer division by a small constant.
    private static Fix64 SinQuadrant(Fix64 x)
    {
        var x2 = x * x;
        var term = Fix64.One - (x2 / 156);
        term = Fix64.One - (x2 * term / 110);
        term = Fix64.One - (x2 * term / 72);
        term = Fix64.One - (x2 * term / 42);
        term = Fix64.One - (x2 * term / 20);
        term = Fix64.One - (x2 * term / 6);
        return x * term;
    }

    private static UInt128 IntegerSqrt(UInt128 n)
    {
        if (n < 2)
        {
            return n;
        }

        // Bit-by-bit method: exact floor(sqrt(n)), no floating point.
        UInt128 result = 0;
        var bit = (UInt128)1 << 126;
        while (bit > n)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            if (n >= result + bit)
            {
                n -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }

            bit >>= 2;
        }

        return result;
    }
}
