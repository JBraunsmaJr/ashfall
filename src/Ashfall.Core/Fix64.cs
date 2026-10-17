using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ashfall.Core;

/// <summary>
/// A signed 32.32 fixed-point number. Every simulation quantity (positions, speeds, damage
/// multipliers) uses this type so results are bit-identical on every machine and OS.
/// </summary>
/// <remarks>
/// Arithmetic is plain 64-bit integer arithmetic: addition and subtraction wrap on overflow,
/// multiplication and division go through <see cref="Int128"/> and truncate toward negative infinity.
/// Range is roughly ±2,147,483,647 with a resolution of 2^-32 (about 2.3e-10).
/// </remarks>
public readonly struct Fix64 :
    IEquatable<Fix64>,
    IComparable<Fix64>,
    IComparisonOperators<Fix64, Fix64, bool>,
    IAdditionOperators<Fix64, Fix64, Fix64>,
    ISubtractionOperators<Fix64, Fix64, Fix64>,
    IMultiplyOperators<Fix64, Fix64, Fix64>,
    IDivisionOperators<Fix64, Fix64, Fix64>,
    IUnaryNegationOperators<Fix64, Fix64>
{
    /// <summary>Number of bits after the binary point.</summary>
    public const int FractionalBits = 32;

    private const long OneRaw = 1L << FractionalBits;
    private const long FractionMask = OneRaw - 1;

    /// <summary>The raw 32.32 bit pattern. Stable across platforms; safe to serialise and hash.</summary>
    public long RawValue { get; }

    private Fix64(long rawValue) => RawValue = rawValue;

    public static Fix64 Zero => default;
    public static Fix64 One => new(OneRaw);
    public static Fix64 Half => new(OneRaw >> 1);
    public static Fix64 MaxValue => new(long.MaxValue);
    public static Fix64 MinValue => new(long.MinValue);

    /// <summary>Pi, rounded to the nearest 2^-32.</summary>
    public static Fix64 Pi => new(13_493_037_705L);

    /// <summary>2·Pi, rounded to the nearest 2^-32.</summary>
    public static Fix64 TwoPi => new(26_986_075_409L);

    /// <summary>Pi/2, rounded to the nearest 2^-32.</summary>
    public static Fix64 HalfPi => new(6_746_518_852L);

    /// <summary>Creates a value from its raw 32.32 bit pattern.</summary>
    public static Fix64 FromRaw(long rawValue) => new(rawValue);

    /// <summary>Creates a value from a whole number.</summary>
    public static Fix64 FromInt(int value) => new((long)value << FractionalBits);

    /// <summary>Creates <paramref name="numerator"/> / <paramref name="denominator"/>, truncated toward negative infinity.</summary>
    public static Fix64 FromRatio(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException();
        }

        return new((long)FloorDivide((Int128)numerator << FractionalBits, denominator));
    }

    public static implicit operator Fix64(int value) => FromInt(value);

    public static Fix64 operator +(Fix64 left, Fix64 right) => new(unchecked(left.RawValue + right.RawValue));

    public static Fix64 operator -(Fix64 left, Fix64 right) => new(unchecked(left.RawValue - right.RawValue));

    public static Fix64 operator -(Fix64 value) => new(unchecked(-value.RawValue));

    public static Fix64 operator *(Fix64 left, Fix64 right) =>
        new(unchecked((long)(((Int128)left.RawValue * right.RawValue) >> FractionalBits)));

    public static Fix64 operator /(Fix64 left, Fix64 right)
    {
        if (right.RawValue == 0)
        {
            throw new DivideByZeroException();
        }

        return new(unchecked((long)FloorDivide((Int128)left.RawValue << FractionalBits, right.RawValue)));
    }

    /// <summary>Multiplies by a whole number without rounding.</summary>
    public static Fix64 operator *(Fix64 left, int right) => new(unchecked(left.RawValue * right));

    /// <summary>Divides by a whole number, truncating toward negative infinity.</summary>
    public static Fix64 operator /(Fix64 left, int right)
    {
        if (right == 0)
        {
            throw new DivideByZeroException();
        }

        return new((long)FloorDivide(left.RawValue, right));
    }

    public static bool operator ==(Fix64 left, Fix64 right) => left.RawValue == right.RawValue;

    public static bool operator !=(Fix64 left, Fix64 right) => left.RawValue != right.RawValue;

    public static bool operator <(Fix64 left, Fix64 right) => left.RawValue < right.RawValue;

    public static bool operator >(Fix64 left, Fix64 right) => left.RawValue > right.RawValue;

    public static bool operator <=(Fix64 left, Fix64 right) => left.RawValue <= right.RawValue;

    public static bool operator >=(Fix64 left, Fix64 right) => left.RawValue >= right.RawValue;

    public bool IsNegative => RawValue < 0;

    /// <summary>Largest whole number less than or equal to this value.</summary>
    public int FloorToInt() => (int)(RawValue >> FractionalBits);

    /// <summary>Smallest whole number greater than or equal to this value.</summary>
    public int CeilingToInt() => (int)((RawValue + FractionMask) >> FractionalBits);

    /// <summary>Nearest whole number; halves round toward positive infinity.</summary>
    public int RoundToInt() => (int)((RawValue + (OneRaw >> 1)) >> FractionalBits);

    public Fix64 Floor() => new(RawValue & ~FractionMask);

    public Fix64 Fraction() => new(RawValue & FractionMask);

    public static Fix64 Abs(Fix64 value) => value.RawValue < 0 ? -value : value;

    public static Fix64 Min(Fix64 a, Fix64 b) => a.RawValue <= b.RawValue ? a : b;

    public static Fix64 Max(Fix64 a, Fix64 b) => a.RawValue >= b.RawValue ? a : b;

    public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max) => Max(min, Min(max, value));

    /// <summary>
    /// Parses an invariant decimal string such as <c>"-12.375"</c>, truncating toward zero at 2^-32.
    /// Used when content files are cooked, so no floating-point value is ever involved.
    /// </summary>
    public static Fix64 Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out var value)
            ? value
            : throw new FormatException($"'{text}' is not a valid fixed-point number.");
    }

    /// <summary>Parses an invariant decimal string without throwing.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out Fix64 value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        var negative = false;
        if (span[0] is '-' or '+')
        {
            negative = span[0] == '-';
            span = span[1..];
        }

        var dot = span.IndexOf('.');
        var wholePart = dot < 0 ? span : span[..dot];
        var fractionPart = dot < 0 ? [] : span[(dot + 1)..];
        if (wholePart.IsEmpty && fractionPart.IsEmpty)
        {
            return false;
        }

        Int128 whole = 0;
        foreach (var c in wholePart)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }

            whole = (whole * 10) + (c - '0');
            if (whole > int.MaxValue)
            {
                return false;
            }
        }

        // Fraction digits beyond 19 cannot change a 2^-32 result.
        Int128 fraction = 0;
        Int128 scale = 1;
        for (var i = 0; i < fractionPart.Length; i++)
        {
            var c = fractionPart[i];
            if (c is < '0' or > '9')
            {
                return false;
            }

            if (i < 19)
            {
                fraction = (fraction * 10) + (c - '0');
                scale *= 10;
            }
        }

        var raw = (whole << FractionalBits) + ((fraction << FractionalBits) / scale);
        value = new(negative ? -(long)raw : (long)raw);
        return true;
    }

    /// <summary>Formats with up to <paramref name="decimals"/> decimal places, using integer maths only.</summary>
    public string ToString(int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 10);

        var negative = RawValue < 0;
        var magnitude = negative ? -(Int128)RawValue : RawValue;
        var whole = magnitude >> FractionalBits;
        var fraction = magnitude & FractionMask;

        Int128 pow = 1;
        for (var i = 0; i < decimals; i++)
        {
            pow *= 10;
        }

        // Round half up at the last printed digit.
        var digits = ((fraction * pow) + (OneRaw >> 1)) >> FractionalBits;
        if (digits >= pow)
        {
            whole += 1;
            digits -= pow;
        }

        var sign = negative && (whole != 0 || digits != 0) ? "-" : string.Empty;
        var wholeText = whole.ToString(CultureInfo.InvariantCulture);
        return decimals == 0
            ? sign + wholeText
            : sign + wholeText + "." + digits.ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0');
    }

    public override string ToString() => ToString(4);

    public bool Equals(Fix64 other) => RawValue == other.RawValue;

    public override bool Equals(object? obj) => obj is Fix64 other && Equals(other);

    public override int GetHashCode() => RawValue.GetHashCode();

    public int CompareTo(Fix64 other) => RawValue.CompareTo(other.RawValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Int128 FloorDivide(Int128 numerator, Int128 denominator)
    {
        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        return remainder != 0 && ((remainder < 0) != (denominator < 0)) ? quotient - 1 : quotient;
    }
}
