using Ashfall.Session;

namespace Ashfall.E2E.Tests;

public class GameSpeedTests
{
    [Theory]
    [InlineData("unthrottled", 0, 1)]
    [InlineData("MAX", 0, 1)]
    [InlineData("1x", 1, 1)]
    [InlineData("4x", 4, 1)]
    [InlineData("4", 4, 1)]
    [InlineData("0.5x", 1, 2)]
    [InlineData("1/2x", 1, 2)]
    [InlineData("2.25", 9, 4)]
    [InlineData(" 10X ", 10, 1)]
    public void Parses_speed_text(string text, int numerator, int denominator)
    {
        var speed = GameSpeed.Parse(text);

        Assert.Equal(numerator, speed.Numerator);
        Assert.Equal(denominator, speed.Denominator);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x")]
    [InlineData("-1x")]
    [InlineData("fast")]
    [InlineData("1/0")]
    [InlineData("0.")]
    public void Rejects_invalid_speed_text(string text)
    {
        Assert.False(GameSpeed.TryParse(text, out _));
    }

    [Fact]
    public void Due_offsets_follow_the_speed()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), GameSpeed.RealTime.DueOffset(15));
        Assert.Equal(TimeSpan.FromSeconds(1), GameSpeed.Times(4).DueOffset(60));
        Assert.Equal(TimeSpan.FromSeconds(2), GameSpeed.Ratio(1, 2).DueOffset(15));
        Assert.Equal(TimeSpan.Zero, GameSpeed.Unthrottled.DueOffset(1_000_000));
    }

    [Fact]
    public void Formats_for_humans()
    {
        Assert.Equal("unthrottled", GameSpeed.Unthrottled.ToString());
        Assert.Equal("4x", GameSpeed.Times(4).ToString());
        Assert.Equal("1/2x", GameSpeed.Ratio(2, 4).ToString());
    }

    [Fact]
    public void Clock_waits_only_until_a_tick_is_due()
    {
        var time = new ManualTimeProvider();
        var clock = new SessionClock(GameSpeed.RealTime, time);

        Assert.Equal(TimeSpan.Zero, clock.TimeUntilDue(0));
        Assert.Equal(TimeSpan.FromSeconds(1), clock.TimeUntilDue(15));

        time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(TimeSpan.FromMilliseconds(600), clock.TimeUntilDue(15));

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.Zero, clock.TimeUntilDue(15));
    }

    [Fact]
    public void Restarting_the_clock_re_anchors_at_the_current_tick()
    {
        var time = new ManualTimeProvider();
        var clock = new SessionClock(GameSpeed.RealTime, time);
        time.Advance(TimeSpan.FromSeconds(30));

        clock.Restart(GameSpeed.Times(2), currentTick: 300);

        Assert.Equal(TimeSpan.Zero, clock.TimeUntilDue(300));
        Assert.Equal(TimeSpan.FromSeconds(1), clock.TimeUntilDue(330));
    }
}
