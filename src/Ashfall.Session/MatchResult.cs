using Ashfall.Core;

namespace Ashfall.Session;

/// <summary>Why a run stopped.</summary>
public enum StopReason
{
    ConditionMet,
    TickLimit,
}

/// <summary>Outcome of <see cref="HeadlessMatch.RunUntilAsync"/>.</summary>
public sealed record MatchResult(
    long FinalTick,
    ulong FinalStateHash,
    StopReason StopReason,
    long TicksRun,
    TimeSpan WallClockElapsed,
    IReadOnlyList<HashCheckpoint> Checkpoints)
{
    /// <summary>Simulation throughput of this run, in ticks per wall-clock second.</summary>
    public long TicksPerSecond =>
        WallClockElapsed <= TimeSpan.Zero ? TicksRun : TicksRun * TimeSpan.TicksPerSecond / WallClockElapsed.Ticks;

    public string FinalStateHashText => StateHasher.Format(FinalStateHash);
}
