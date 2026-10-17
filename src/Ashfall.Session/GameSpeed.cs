using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Ashfall.Simulation;

namespace Ashfall.Session;

/// <summary>
/// How fast simulation ticks are paced against the wall clock: a rational multiplier of real time
/// (1× = 15 ticks per second), or <see cref="Unthrottled"/>, which runs ticks back to back.
/// Speed never changes results, only how long they take.
/// </summary>
public readonly record struct GameSpeed
{
    private GameSpeed(int numerator, int denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>Multiplier numerator; 0 means unthrottled.</summary>
    public int Numerator { get; }

    /// <summary>Multiplier denominator; always positive.</summary>
    public int Denominator { get; }

    /// <summary>Run ticks as fast as the CPU allows. Used by tests, replays and reconnect catch-up.</summary>
    public static GameSpeed Unthrottled => new(0, 1);

    /// <summary>15 ticks per second of wall-clock time.</summary>
    public static GameSpeed RealTime => new(1, 1);

    public bool IsUnthrottled => Numerator == 0;

    /// <summary>A whole-number multiple of real time, e.g. <c>Times(4)</c> for 4×.</summary>
    public static GameSpeed Times(int multiplier)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(multiplier);
        return new(multiplier, 1);
    }

    /// <summary>A fractional multiple of real time, e.g. <c>Ratio(1, 2)</c> for 0.5×.</summary>
    public static GameSpeed Ratio(int numerator, int denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        var divisor = GreatestCommonDivisor(numerator, denominator);
        return new(numerator / divisor, denominator / divisor);
    }

    /// <summary>
    /// Wall-clock time from the start of a run until <paramref name="ticks"/> ticks are due.
    /// Computed exactly from the tick count, so pacing never drifts.
    /// </summary>
    public TimeSpan DueOffset(long ticks) =>
        IsUnthrottled
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(ticks * TimeSpan.TicksPerSecond * Denominator
                                 / ((long)SimulationConstants.TicksPerSecond * Numerator));

    /// <summary>Parses <c>unthrottled</c> (or <c>max</c>), <c>4x</c>, <c>0.5x</c> or <c>1/2x</c>. The trailing x is optional.</summary>
    public static GameSpeed Parse(string text) =>
        TryParse(text, out var speed)
            ? speed
            : throw new FormatException($"'{text}' is not a game speed. Use unthrottled, 1x, 4x, 0.5x or 1/2x.");

    public static bool TryParse([NotNullWhen(true)] string? text, out GameSpeed speed)
    {
        speed = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().ToUpperInvariant();
        if (value is "UNTHROTTLED" or "MAX")
        {
            speed = Unthrottled;
            return true;
        }

        if (value.EndsWith('X') || value.EndsWith('×'))
        {
            value = value[..^1];
        }

        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0)
        {
            if (int.TryParse(value[..slash], NumberStyles.None, CultureInfo.InvariantCulture, out var top)
                && int.TryParse(value[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var bottom)
                && top > 0 && bottom > 0)
            {
                speed = Ratio(top, bottom);
                return true;
            }

            return false;
        }

        // Decimal such as 0.5 or 2.25, parsed exactly as a ratio.
        var dot = value.IndexOf('.', StringComparison.Ordinal);
        var digits = dot < 0 ? value : string.Concat(value.AsSpan(0, dot), value.AsSpan(dot + 1));
        var scale = 1;
        if (dot >= 0)
        {
            var fractionDigits = value.Length - dot - 1;
            if (fractionDigits is < 1 or > 6)
            {
                return false;
            }

            for (var i = 0; i < fractionDigits; i++)
            {
                scale *= 10;
            }
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var scaled) || scaled <= 0)
        {
            return false;
        }

        speed = Ratio(scaled, scale);
        return true;
    }

    public override string ToString() =>
        IsUnthrottled ? "unthrottled"
        : Denominator == 1 ? string.Create(CultureInfo.InvariantCulture, $"{Numerator}x")
        : string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}x");

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
