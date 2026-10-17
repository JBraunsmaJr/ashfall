using Ashfall.Core;
using Ashfall.Session;
using Ashfall.Session.Commands;
using Ashfall.Simulation;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;

namespace Ashfall.E2E.Tests;

public class HeadlessMatchTests
{
    [Fact]
    public async Task Two_minutes_of_game_time_runs_headless_with_a_checkpoint_every_30_ticks()
    {
        await using var match = await HeadlessMatch.StartAsync(
            MatchSpec.ProvingGround(1), GameSpeed.Unthrottled);

        var result = await match.RunTicksAsync(1_800, TestContext.Current.CancellationToken);

        Assert.Equal(1_800, result.FinalTick);
        Assert.Equal(StopReason.TickLimit, result.StopReason);
        Assert.Equal(60, result.Checkpoints.Count);
        Assert.Equal(30, result.Checkpoints[0].Tick);
        Assert.Equal(200, match.Simulation.World.AliveCount);
    }

    [Fact]
    public async Task The_same_spec_produces_identical_checkpoints()
    {
        var spec = MatchSpec.ProvingGround(77).WithPlayers(4).WithUnitsPerPlayer(30);

        var first = await RunAsync(spec, 600);
        var second = await RunAsync(spec, 600);

        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(first.Checkpoints, second.Checkpoints);
    }

    [Fact]
    public async Task Different_seeds_diverge()
    {
        var first = await RunAsync(MatchSpec.ProvingGround(1), 300);
        var second = await RunAsync(MatchSpec.ProvingGround(2), 300);

        Assert.NotEqual(first.FinalStateHash, second.FinalStateHash);
    }

    [Fact]
    public async Task Game_speed_changes_pacing_but_never_the_outcome()
    {
        var spec = MatchSpec.ProvingGround(9);
        var time = new ManualTimeProvider();
        await using var paced = await HeadlessMatch.StartAsync(spec, GameSpeed.Times(4), time);

        // At 4× a tick is due every 1/60 s. Drive the fake clock forward exactly as far as each tick needs.
        Assert.Equal(TimeSpan.Zero, paced.Clock.TimeUntilDue(0));
        paced.Step();
        Assert.Equal(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60), paced.Clock.TimeUntilDue(paced.Tick));
        while (paced.Tick < 300)
        {
            time.Advance(paced.Clock.TimeUntilDue(paced.Tick));
            Assert.Equal(TimeSpan.Zero, paced.Clock.TimeUntilDue(paced.Tick));
            paced.Step();
        }

        var unthrottled = await RunAsync(spec, 300);

        Assert.Equal(unthrottled.FinalStateHash, paced.Simulation.ComputeStateHash());
    }

    [Fact]
    public async Task Run_until_stops_when_the_condition_is_met()
    {
        await using var match = await HeadlessMatch.StartAsync(MatchSpec.ProvingGround(3), GameSpeed.Unthrottled);

        var result = await match.RunUntilAsync(m => m.Tick == 100, maxTicks: 10_000, TestContext.Current.CancellationToken);

        Assert.Equal(StopReason.ConditionMet, result.StopReason);
        Assert.Equal(100, result.FinalTick);
    }

    [Fact]
    public async Task A_scripted_order_is_carried_out()
    {
        var spec = MatchSpec.ProvingGround(5).WithUnitsPerPlayer(1).WithOrders(intervalTicks: 0, ordersPerInterval: 0);
        await using var match = await HeadlessMatch.StartAsync(spec, GameSpeed.Unthrottled);
        var unit = match.Simulation.World.EntityAt(0);
        var target = FixVec2.FromInts(40, 40);
        match.AddCommandSource(new ScriptedCommandSource().Add(tick: 15, Command.Move(0, unit, target)));

        // Unit speed is 0.1 cells per tick and the map is 64 cells across, so 1,000 ticks is plenty.
        var result = await match.RunUntilAsync(
            m => m.Simulation.World.Get<Position>(unit).Value == target,
            maxTicks: 1_000,
            TestContext.Current.CancellationToken);

        Assert.Equal(StopReason.ConditionMet, result.StopReason);
        Assert.Equal(0, match.Simulation.RejectedCommands);
    }

    [Fact]
    public async Task Cancellation_stops_a_run()
    {
        await using var match = await HeadlessMatch.StartAsync(MatchSpec.ProvingGround(3), GameSpeed.RealTime);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => match.RunTicksAsync(1_000, cancellation.Token));
    }

    [Fact]
    public void Invalid_specs_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HeadlessMatch.Start(MatchSpec.ProvingGround(1).WithPlayers(9), GameSpeed.Unthrottled));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HeadlessMatch.Start(MatchSpec.ProvingGround(1).WithMap(0, 10), GameSpeed.Unthrottled));
    }

    private static async Task<MatchResult> RunAsync(MatchSpec spec, long ticks)
    {
        await using var match = await HeadlessMatch.StartAsync(spec, GameSpeed.Unthrottled);
        return await match.RunTicksAsync(ticks, TestContext.Current.CancellationToken);
    }
}
