using Ashfall.Session.Commands;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Ecs;

namespace Ashfall.E2E.Tests;

public class TurnFeedTests
{
    [Fact]
    public void A_tick_is_complete_only_when_every_fed_seat_has_its_turn()
    {
        var feed = new TurnFeed([0, 2], players: 3, inputDelay: 2);

        Assert.True(feed.IsComplete(0));  // before the input delay: empty by definition
        Assert.True(feed.IsComplete(1));
        Assert.False(feed.IsComplete(2));
        Assert.Equal([0, 2], feed.Missing(2));

        feed.Set(2, 0, []);
        feed.Set(2, 2, [Command.Stop(2, new Entity(1, 1))]);

        Assert.True(feed.IsComplete(2));
        Assert.Single(feed.Get(2, 2)!);
        Assert.False(feed.Set(2, 2, []));      // a different turn for the same tick is refused
        Assert.True(feed.Set(2, 0, []));       // the same turn again is fine
        Assert.False(feed.Set(3, 1, []));      // seat 1 is an AI, not fed
    }

    [Fact]
    public void Turns_after_a_seat_leaves_are_empty()
    {
        var feed = new TurnFeed([0, 1], players: 2, inputDelay: 2);
        feed.Set(2, 1, []);
        feed.SetLeft(1, 2);
        feed.Set(3, 0, []);

        Assert.True(feed.IsComplete(3));
        Assert.Empty(feed.Get(50, 1)!);
        Assert.Equal(2, feed.LeftAfter(1));
        Assert.Equal(3, feed.LastTurn(0));
    }
}
