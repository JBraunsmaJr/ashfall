namespace Ashfall.Session;

/// <summary>
/// Paces simulation ticks against the wall clock at a <see cref="GameSpeed"/>. If the machine falls
/// behind, ticks run back to back until it catches up; it never skips ticks.
/// </summary>
public sealed class SessionClock
{
    private readonly TimeProvider _timeProvider;
    private long _anchorTimestamp;
    private long _anchorTick;

    public SessionClock(GameSpeed speed, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        Restart(speed, 0);
    }

    public GameSpeed Speed { get; private set; }

    /// <summary>Changes speed (or resumes after a pause) with <paramref name="currentTick"/> due immediately.</summary>
    public void Restart(GameSpeed speed, long currentTick)
    {
        Speed = speed;
        _anchorTick = currentTick;
        _anchorTimestamp = _timeProvider.GetTimestamp();
    }

    /// <summary>How long to wait before <paramref name="tick"/> may run; zero if it is already due.</summary>
    public TimeSpan TimeUntilDue(long tick)
    {
        if (Speed.IsUnthrottled)
        {
            return TimeSpan.Zero;
        }

        var due = Speed.DueOffset(tick - _anchorTick);
        var elapsed = _timeProvider.GetElapsedTime(_anchorTimestamp);
        return due > elapsed ? due - elapsed : TimeSpan.Zero;
    }

    /// <summary>Waits until <paramref name="tick"/> is due. Completes synchronously when unthrottled or behind.</summary>
    public ValueTask WaitUntilDueAsync(long tick, CancellationToken cancellationToken = default)
    {
        var delay = TimeUntilDue(tick);
        return delay == TimeSpan.Zero
            ? ValueTask.CompletedTask
            : new ValueTask(Task.Delay(delay, _timeProvider, cancellationToken));
    }
}
