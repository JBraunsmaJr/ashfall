namespace Ashfall.E2E.Tests;

/// <summary>A clock that only moves when a test advances it. Timestamps are in <see cref="TimeSpan"/> ticks.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _now;

    public void Advance(TimeSpan by) => _now += by.Ticks;
}
