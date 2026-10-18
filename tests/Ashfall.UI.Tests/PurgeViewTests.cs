using Ashfall.Session;
using Ashfall.Simulation.Components;

namespace Ashfall.UI.Tests;

/// <summary>What a Purge shows the player: the Maw line under the clock, and the telegraphs before and during a wave.</summary>
public class PurgeViewTests
{
    [Fact]
    public void The_maw_telegraphs_its_waves_and_each_nest_that_falls_is_announced()
    {
        var ui = new UiDriver(MatchSpec.Purge(3, players: 1).WithController(0, PlayerController.Human));
        ui.Frame();
        var maw = ui.Simulation.Maw!;
        Assert.StartsWith("NESTS 4  NEXT WAVE 05:00", ui.Screen.MawStatus, StringComparison.Ordinal);

        ui.RunUntil(s => maw.IsWaveImminent(s.Tick), (int)maw.NextWaveTick);
        ui.Frame();
        Assert.Contains("THE MAW STIRS", ui.Screen.Announcements);
        Assert.Contains("THE MAW STIRS", ui.Screen.MawStatus, StringComparison.Ordinal);

        ui.RunUntil(s => s.Maw!.Waves == 1, maw.Settings.WaveWarningTicks + 2);
        ui.Frame();
        Assert.Contains("WAVE 1 INCOMING", ui.Screen.Announcements);
        Assert.StartsWith("NESTS 4  NEXT WAVE 01:5", ui.Screen.MawStatus, StringComparison.Ordinal); // two minutes, less the ticks since the launch

        var nest = ui.Simulation.World.EntityAt(ui.Simulation.World.Store<Nest>().EntityIndices[0]);
        ui.Simulation.World.Get<Health>(nest).Current = 0;
        ui.Frame(2);
        Assert.Contains("NEST DESTROYED", ui.Screen.Announcements);
        Assert.StartsWith("NESTS 3", ui.Screen.MawStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void Skirmishes_and_the_swarm_test_have_no_maw_line()
    {
        Assert.Null(new UiDriver().Screen.MawStatus);
        Assert.Null(new UiDriver(MatchSpec.Swarm(7, creatures: 50, nests: 1).WithController(0, PlayerController.Human)).Screen.MawStatus);
    }
}
