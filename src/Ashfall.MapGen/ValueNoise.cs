namespace Ashfall.MapGen;

/// <summary>
/// Integer value noise: random lattice values smoothly interpolated, summed over octaves (fBm). No
/// floating point anywhere, so it is bit-identical on every machine. Output is 0–1023.
/// </summary>
public sealed class ValueNoise(ulong seed, int cellSize, int octaves)
{
    private const int One = 1024;

    public int Sample(int x, int y)
    {
        long sum = 0;
        long weight = 0;
        var size = cellSize;
        var amplitude = 256;
        for (var octave = 0; octave < octaves && size >= 2; octave++)
        {
            sum += (long)Octave(x, y, size, (ulong)octave) * amplitude;
            weight += amplitude;
            size /= 2;
            amplitude /= 2;
        }

        return (int)(sum / weight);
    }

    /// <summary>A well-mixed 64-bit hash of a lattice point (SplitMix64 finaliser).</summary>
    public static ulong Hash(ulong seed, long x, long y, ulong salt)
    {
        var z = seed ^ ((ulong)x * 0x9E3779B97F4A7C15UL) ^ ((ulong)y * 0xC2B2AE3D27D4EB4FUL) ^ (salt * 0x165667B19E3779F9UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private int Octave(int x, int y, int size, ulong octave)
    {
        var gx = FloorDiv(x, size);
        var gy = FloorDiv(y, size);
        var tx = Smooth((x - (gx * size)) * One / size);
        var ty = Smooth((y - (gy * size)) * One / size);
        var a = Lattice(gx, gy, octave);
        var b = Lattice(gx + 1, gy, octave);
        var c = Lattice(gx, gy + 1, octave);
        var d = Lattice(gx + 1, gy + 1, octave);
        var top = a + ((b - a) * tx / One);
        var bottom = c + ((d - c) * tx / One);
        return top + ((bottom - top) * ty / One);
    }

    private int Lattice(int x, int y, ulong octave) => (int)(Hash(seed, x, y, octave + 1) % One);

    // 3t² − 2t³ on 0..1024.
    private static int Smooth(int t) => (int)(((3L * t * t * One) - (2L * t * t * t)) / (One * One));

    private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : ((value + 1) / divisor) - 1;
}
