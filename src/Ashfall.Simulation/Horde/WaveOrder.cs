using Ashfall.Core;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Horde;

/// <summary>
/// A wave the director has called and is telegraphing: what it is, whom it is for, where it will come from and when
/// it goes. The creatures are picked when it launches, from whatever broods then.
/// </summary>
/// <param name="Target">The player it is aimed at.</param>
/// <param name="SecondTarget">For a split assault, the player the second half goes for; otherwise -1.</param>
/// <param name="Source">Where it emerges: the mouth of the nest nearest the target.</param>
/// <param name="Size">How many creatures it will take (at most; fewer if some die before it goes).</param>
public readonly record struct WaveOrder(WaveKind Kind, int Target, int SecondTarget, Cell Source, long LaunchTick, int Size)
{
    public void AddTo(ref StateHasher hasher)
    {
        hasher.Add((int)Kind);
        hasher.Add(Target);
        hasher.Add(SecondTarget);
        hasher.Add(Source.X);
        hasher.Add(Source.Y);
        hasher.Add(LaunchTick);
        hasher.Add(Size);
    }
}
