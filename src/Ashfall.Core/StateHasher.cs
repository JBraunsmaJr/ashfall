namespace Ashfall.Core;

/// <summary>
/// 64-bit FNV-1a hash over simulation state, fed value by value in a fixed byte order.
/// Peers compare these hashes every few ticks to detect desyncs, so the result must not depend on
/// endianness, runtime or process: never feed it <see cref="object.GetHashCode"/> or memory layouts.
/// </summary>
public struct StateHasher
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong _hash;
    private bool _started;

    /// <summary>The hash of everything added so far.</summary>
    public readonly ulong Value => _started ? _hash : OffsetBasis;

    public void Add(ulong value)
    {
        if (!_started)
        {
            _hash = OffsetBasis;
            _started = true;
        }

        unchecked
        {
            for (var shift = 0; shift < 64; shift += 8)
            {
                _hash ^= (byte)(value >> shift);
                _hash *= Prime;
            }
        }
    }

    public void Add(long value) => Add(unchecked((ulong)value));

    public void Add(int value) => Add((long)value);

    public void Add(uint value) => Add((ulong)value);

    public void Add(bool value) => Add(value ? 1UL : 0UL);

    public void Add(Fix64 value) => Add(value.RawValue);

    public void Add(FixVec2 value)
    {
        Add(value.X);
        Add(value.Y);
    }

    /// <summary>Formats a hash the way logs, replays and CI artifacts print it.</summary>
    public static string Format(ulong hash) => $"0x{hash:X16}";
}
