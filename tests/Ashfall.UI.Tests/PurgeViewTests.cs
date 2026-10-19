
using System.Numerics;
using Ashfall.Session;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Horde;
using Ashfall.UI.Hud;

namespace Ashfall.UI.Tests;

/// <summary>What a Purge shows the player: the Maw line under the clock, and the telegraphs before and during a wave.</summary>
public class PurgeViewTests
{
    [Fact]
    public void The_maw_telegraphs_every_wave_with_its_direction_and_each_nest_that_falls_is_announced()
    {
        var ui = new UiDriver(MatchSpec.Purge(3, players: 1).WithController(0, PlayerController.Human));
        ui.Frame();
        var director = ui.Simulation.Maw!.Director!;
        Assert.Equal("NESTS 4  BROOD 0/40", ui.Screen.MawStatus);

        // The first wave called: announced with what it is and where it comes from, counted down under the clock.
        ui.RunUntil(_ => director.Pending is not null, (int)MatchSpec.PurgeGraceTicks + MatchSpec.PurgeWaveIntervalTicks);
        ui.Frame();
        var order = director.Pending!.Value;
        var called = MawTelegraph.Called(ui.Simulation, order, ui.Player);
        Assert.Contains(called, ui.Screen.Announcements);
        Assert.Contains(" FROM THE ", called, StringComparison.Ordinal);
        Assert.StartsWith("NESTS 4  " + order.Kind.ToString().ToUpperInvariant() + " IN 00:", ui.Screen.MawStatus, StringComparison.Ordinal);

        // Launched: announced, and the line goes back to the brood.
        ui.RunUntil(_ => director.Waves == 1, (int)(order.LaunchTick - ui.Simulation.Tick) + 2);
        ui.Frame();
        Assert.Contains(MawTelegraph.Launched(order.Kind), ui.Screen.Announcements);
        Assert.StartsWith("NESTS 4  BROOD ", ui.Screen.MawStatus, StringComparison.Ordinal);

        var nest = ui.Simulation.World.EntityAt(ui.Simulation.World.Store<Nest>().EntityIndices[0]);
        ui.Simulation.World.Get<Health>(nest).Current = 0;
        ui.Frame(2);
        Assert.Contains("NEST DESTROYED", ui.Screen.Announcements);
        Assert.StartsWith("NESTS 3", ui.Screen.MawStatus, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0f, -5f, "NORTH")]
    [InlineData(5f, -5f, "NORTH-EAST")]
    [InlineData(5f, 0f, "EAST")]
    [InlineData(5f, 5f, "SOUTH-EAST")]
    [InlineData(0f, 5f, "SOUTH")]
    [InlineData(-5f, 5f, "SOUTH-WEST")]
    [InlineData(-5f, 0f, "WEST")]
    [InlineData(-5f, -5f, "NORTH-WEST")]
    [InlineData(5f, -1f, "EAST")]
    public void Directions_are_named_as_on_the_minimap_with_north_up(float dx, float dy, string expected)
    {
        Assert.Equal(expected, MawTelegraph.Compass(new Vector2(10, 10), new Vector2(10 + dx, 10 + dy)));
    }

    [Fact]
    public void Announcements_say_whom_a_wave_is_for()
    {
        var ui = new UiDriver(MatchSpec.Purge(3, players: 2).WithController(0, PlayerController.Human));
        var source = new Ashfall.Simulation.Map.Cell(-1, -1); // no source: no direction
        string Say(WaveKind kind, int target, int second = -1, int viewer = 0) =>
            MawTelegraph.Called(ui.Simulation, new WaveOrder(kind, target, second, source, 0, 10), viewer);

        Assert.Equal("THE MAW STIRS - ASSAULT ON YOU", Say(WaveKind.Assault, 0));
        Assert.Equal("THE MAW STIRS - ASSAULT ON AN ALLY", Say(WaveKind.Assault, 1));
        Assert.Equal("THE MAW STIRS - ASSAULT ON YOU AND AN ALLY", Say(WaveKind.Assault, 1, second: 0));
        Assert.Equal("RAID ON YOUR HARVESTERS", Say(WaveKind.Raid, 0));
        Assert.Equal("RAID ON AN ALLY'S HARVESTERS", Say(WaveKind.Raid, 1));
        Assert.Equal("MAW PROBE AT PLAYER 2", Say(WaveKind.Probe, 1, viewer: -1));

        // The direction is seen from your own base when the wave is coming for you, even as the second of a split.
        var home = MawTelegraph.BaseOf(ui.Simulation, 0)!.Value;
        var east = new Ashfall.Simulation.Map.Cell((int)home.X + 15, (int)home.Y);
        var split = new WaveOrder(WaveKind.Assault, 1, 0, east, 0, 50);
        var said = MawTelegraph.Called(ui.Simulation, split, viewer: 0);
        Assert.True(said.EndsWith("FROM THE EAST", StringComparison.Ordinal), said);
    }

    [Fact]
    public void Skirmishes_and_the_swarm_test_have_no_maw_line()
    {
        Assert.Null(new UiDriver().Screen.MawStatus);
        Assert.Null(new UiDriver(MatchSpec.Swarm(7, creatures: 50, nests: 1).WithController(0, PlayerController.Human)).Screen.MawStatus);
    }
}
