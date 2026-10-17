using System.Numerics;

namespace Ashfall.Core;

/// <summary>
/// PCG32 random number generator (O'Neill, pcg-random.org). Same seed, same sequence, on every machine.
/// The simulation owns exactly one instance per match; tools and AI derive their own streams.
/// </summary>
public sealed class DeterministicRandom
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    /// <summary>Creates a generator. Different <paramref name="stream"/> values give independent sequences from one seed.</summary>
    public DeterministicRandom(ulong seed, ulong stream = 0)
    {
        // SplitMix64 spreads low-entropy seeds (1, 2, 3...) across the whole state space.
        var mixer = seed;
        _increment = (SplitMix64(ref mixer) ^ (stream * 0xD1B54A32D192ED03UL)) | 1UL;
        _state = 0;
        NextUInt32();
        _state += SplitMix64(ref mixer);
        NextUInt32();
    }

    private DeterministicRandom(RandomSnapshot snapshot)
    {
        _state = snapshot.State;
        _increment = snapshot.Increment | 1UL;
    }

    /// <summary>The generator's full state; part of simulation snapshots and the state hash.</summary>
    public RandomSnapshot Snapshot => new(_state, _increment);

    /// <summary>Recreates a generator that continues exactly where <paramref name="snapshot"/> was taken.</summary>
    public static DeterministicRandom Restore(RandomSnapshot snapshot) => new(snapshot);

    /// <summary>Next uniformly distributed 32-bit value.</summary>
    public uint NextUInt32()
    {
        var old = _state;
        _state = unchecked((old * Multiplier) + _increment);
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rotation = (int)(old >> 59);
        return BitOperations.RotateRight(xorShifted, rotation);
    }

    /// <summary>Uniform integer in [0, <paramref name="maxExclusive"/>), without modulo bias (Lemire's method).</summary>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);

        var bound = (uint)maxExclusive;
        var product = (ulong)NextUInt32() * bound;
        var low = (uint)product;
        if (low < bound)
        {
            var threshold = unchecked(0u - bound) % bound;
            while (low < threshold)
            {
                product = (ulong)NextUInt32() * bound;
                low = (uint)product;
            }
        }

        return (int)(product >> 32);
    }

    /// <summary>Uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minInclusive, maxExclusive);
        return minInclusive + Next(maxExclusive - minInclusive);
    }

    /// <summary>Uniform value in [0, 1) with 32 bits of precision.</summary>
    public Fix64 NextFix64() => Fix64.FromRaw(NextUInt32());

    /// <summary>Uniform value in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public Fix64 NextFix64(Fix64 min, Fix64 max) => min + ((max - min) * NextFix64());

    private static ulong SplitMix64(ref ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            var z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
