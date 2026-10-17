namespace Ashfall.Session;

/// <summary>
/// The reference run behind the M0 exit gate ("same replay hash on Windows and Linux"). The headless
/// CLI defaults and the determinism golden test both use these values, so CI compares like with like.
/// </summary>
public static class ProvingGround
{
    public const ulong DefaultSeed = 20_261_017;

    /// <summary>Two minutes of game time.</summary>
    public const long DefaultTicks = 1_800;
}
