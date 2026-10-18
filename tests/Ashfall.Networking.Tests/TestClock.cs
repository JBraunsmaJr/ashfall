namespace Ashfall.Networking.Tests;

/// <summary>A clock that only moves when a test advances it: both timestamps and wall-clock time.</summary>
internal sealed class TestClock : TimeProvider
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public TimeSpan Elapsed { get; private set; }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Elapsed.Ticks;

    public override DateTimeOffset GetUtcNow() => Epoch + Elapsed;

    public void Advance(TimeSpan by) => Elapsed += by;
}
