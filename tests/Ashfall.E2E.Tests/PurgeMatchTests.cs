using Ashfall.Content;
using Ashfall.MapGen;
using Ashfall.Session;
using Ashfall.Session.Maps;
using Ashfall.Session.Replays;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;

namespace Ashfall.E2E.Tests;

/// <summary>The co-op Purge, end to end: setup on a generated map, the Maw's grace period and waves, and both endings.</summary>
public class PurgeMatchTests
{
    [Fact]
    public async Task Nests_sit_by_fissures_far_from_the_bases_and_stay_quiet_through_the_grace_period()
    {
        await using var match = await HeadlessMatch.StartAsync(MatchSpec.Purge(3, players: 2), GameSpeed.Unthrottled);
        var simulation = match.Simulation;
        var world = simulation.World;
        Assert.Equal(MatchGoal.Purge, simulation.Goal);
        Assert.True(simulation.CindriteRegrowth);
        Assert.True(simulation.Maw!.HasWaves);
        Assert.Equal(MatchSpec.PurgeDefaultNests, world.Store<Nest>().Count);

        var yards = match.Map!.StartOrigins.Take(2).ToArray();
        foreach (var index in world.Store<Nest>().EntityIndices)
        {
            var origin = world.Store<StructureInfo>().Get(index).Origin;
            Assert.Equal(GameSimulation.MawPlayerId, world.Store<Owner>().Get(index).PlayerId);

            // A fissure just outside the 3x3 footprint, and no base within the minimum distance of it.
            var fissure = Enumerable.Range(-3, 9).SelectMany(dy => Enumerable.Range(-3, 9).Select(dx => new Cell(origin.X + dx, origin.Y + dy)))
                .Where(simulation.Map.InBounds).First(simulation.Map.IsFissure);
            foreach (var yard in yards)
            {
                var (dx, dy) = (fissure.X - yard.X, fissure.Y - yard.Y);
                Assert.True((dx * dx) + (dy * dy) >= PurgeNests.MinimumDistance * PurgeNests.MinimumDistance);
            }
        }

        await match.RunTicksAsync(MatchSpec.PurgeGraceTicks - 1, TestContext.Current.CancellationToken);
        Assert.Equal(0, world.Store<Creature>().Count);
        Assert.False(simulation.IsOver);

        await match.RunTicksAsync(300, TestContext.Current.CancellationToken);
        Assert.NotEmpty(world.Store<Creature>().EntityIndices.ToArray());
        Assert.All(world.Store<Creature>().Components.ToArray(), creature => Assert.True(creature.Brooding));
        Assert.Equal(MatchSpec.PurgeGraceTicks + MatchSpec.PurgeWaveIntervalTicks, simulation.Maw.NextWaveTick);
    }

    [Fact]
    public async Task Players_who_never_act_are_overrun_once_the_waves_come()
    {
        var spec = MatchSpec.Purge(3, players: 2).WithController(0, PlayerController.IdleAi).WithController(1, PlayerController.IdleAi);
        await using var match = await HeadlessMatch.StartAsync(spec, GameSpeed.Unthrottled);

        var result = await match.RunToCompletionAsync(MatchSpec.SkirmishTickLimit, TestContext.Current.CancellationToken);

        Assert.True(result.IsOver);
        Assert.Equal(GameSimulation.MawTeam, result.WinningTeam);
        Assert.True(result.FinalTick > MatchSpec.PurgeGraceTicks + MatchSpec.PurgeWaveIntervalTicks, "Nothing should attack before the first wave.");
        Assert.All(match.Simulation.Players, player => Assert.True(player.Defeated));
    }

    [Fact]
    public async Task Scripted_allies_can_purge_every_nest_and_the_match_plays_the_same_every_time()
    {
        // The golden purge: two scripted AIs that win.
        var first = await RunAsync(MatchSpec.Purge(1, players: 2));
        var second = await RunAsync(MatchSpec.Purge(1, players: 2));

        Assert.True(first.IsOver);
        Assert.Equal(0, first.WinningTeam);
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(first.Checkpoints, second.Checkpoints);
    }

    [Fact]
    public async Task A_purge_with_a_person_in_it_is_recorded_and_replays_to_the_same_end()
    {
        await using var match = await HeadlessMatch.StartAsync(
            MatchSpec.Purge(2, players: 2, nests: 2).WithController(0, PlayerController.Human), GameSpeed.Unthrottled);
        var simulation = match.Simulation;
        var content = simulation.Content;
        var yard = simulation.World.EntityAt(simulation.World.Store<StructureInfo>().EntityIndices.ToArray()
            .First(index => simulation.World.Store<Owner>().Get(index).PlayerId == 0));
        var origin = simulation.World.Get<StructureInfo>(yard).Origin;

        // A power plant somewhere legal near the yard, then let the Maw come.
        var plant = content.StructureIndex(BulwarkContent.PowerPlant);
        var site = Enumerable.Range(-8, 17).SelectMany(dy => Enumerable.Range(-8, 17).Select(dx => new Cell(origin.X + dx, origin.Y + dy)))
            .First(cell => simulation.CheckPlacement(0, plant, cell) == PlacementResult.Ok);
        match.LocalPlayer!.Enqueue(Ashfall.Simulation.Commands.Command.Build(0, plant, site));
        await match.RunTicksAsync(MatchSpec.PurgeGraceTicks + MatchSpec.PurgeWaveIntervalTicks + 600, TestContext.Current.CancellationToken);
        Assert.Contains(simulation.World.Store<StructureInfo>().EntityIndices.ToArray(), index =>
            simulation.World.Store<Owner>().Get(index).PlayerId == 0 && simulation.World.Store<StructureInfo>().Get(index).StructureType == plant);
        Assert.True(simulation.Maw!.Waves >= 1);

        var replay = Replay.Decode(Replay.From(match).Encode());
        Assert.Equal(MatchMode.Purge, replay.Spec.Mode);
        Assert.Equal(PlayerController.Remote, replay.Spec.Controllers[0]);
        Assert.Equal(2, replay.Spec.Nests);
        Assert.Equal(match.Spec.GeneratedMap, replay.Spec.GeneratedMap);
        Assert.True(replay.Verify().Matches);
    }

    [Fact]
    public void Purge_specs_are_validated()
    {
        MatchSpec.Purge(1).Validate();
        MatchSpec.Purge(1, players: 4, nests: MatchSpec.PurgeMaxNests).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpec.Purge(1, players: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpec.Purge(1, nests: 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpec.Purge(1, nests: MatchSpec.PurgeMaxNests + 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpec.Purge(1, players: 2).WithTeams(0, 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (MatchSpec.Purge(1) with { GeneratedMap = null }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpec.Purge(1).WithMap(64, 64).Validate());

        var riverlands = MatchSpec.Purge(1, map: MapSettings.Default(MapArchetypes.Riverlands, 9));
        riverlands.Validate();
        Assert.Equal(MapArchetypes.Riverlands, riverlands.GeneratedMap!.Archetype);
    }

    private static async Task<MatchResult> RunAsync(MatchSpec spec)
    {
        await using var match = await HeadlessMatch.StartAsync(spec, GameSpeed.Unthrottled);
        return await match.RunToCompletionAsync(MatchSpec.SkirmishTickLimit, TestContext.Current.CancellationToken);
    }
}
