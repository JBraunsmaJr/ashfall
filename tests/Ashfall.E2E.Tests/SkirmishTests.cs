using Ashfall.Session;
using Ashfall.Session.Maps;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Navigation;

namespace Ashfall.E2E.Tests;

/// <summary>The M1 exit gate: a headless 1v1 between scripted AIs ends in victory, deterministically.</summary>
public class SkirmishTests
{
    [Theory]
    [InlineData(ProvingGround.DefaultSeed)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public async Task A_headless_skirmish_ends_in_victory_and_replays_identically(ulong seed)
    {
        var first = await RunAsync(seed);
        var second = await RunAsync(seed);

        Assert.Equal(StopReason.ConditionMet, first.StopReason);
        Assert.True(first.IsOver);
        Assert.InRange(first.WinningTeam, 0, 1);
        Assert.Equal(first.FinalTick, second.FinalTick);
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(first.Checkpoints, second.Checkpoints);
        Assert.Equal(first.WinningTeam, second.WinningTeam);
    }

    [Fact]
    public async Task Different_seeds_play_out_differently()
    {
        var first = await RunAsync(1);
        var second = await RunAsync(2);

        Assert.NotEqual(first.FinalStateHash, second.FinalStateHash);
    }

    [Fact]
    public async Task Both_sides_build_a_base_and_an_army()
    {
        await using var match = await HeadlessMatch.StartAsync(MatchSpec.Skirmish(1), GameSpeed.Unthrottled);

        await match.RunTicksAsync(2_700, TestContext.Current.CancellationToken); // three minutes

        var report = MatchReport.Describe(match.Simulation);
        foreach (var expected in new[] { "Power Plant", "Refinery", "Barracks", "War Factory", "Harvester", "Rifleman" })
        {
            var players = report.Split("\n  player ").Skip(1).ToList();
            Assert.Equal(2, players.Count);
            Assert.All(players, player => Assert.Contains(expected, player, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Skirmishes_are_one_versus_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HeadlessMatch.Start(MatchSpec.Skirmish(1).WithPlayers(3), GameSpeed.Unthrottled));
    }

    [Fact]
    public void The_duel_map_is_rotationally_symmetric()
    {
        var map = DuelMap.Create();

        for (var y = 0; y < DuelMap.Size; y++)
        {
            for (var x = 0; x < DuelMap.Size; x++)
            {
                var cell = new Cell(x, y);
                var mirror = DuelMap.Mirror(cell);
                Assert.Equal(map.TerrainAt(cell), map.TerrainAt(mirror));
                Assert.Equal(map.CindriteAt(cell), map.CindriteAt(mirror));
            }
        }

        Assert.Equal(DuelMap.StartOrigin(1), DuelMap.MirrorFootprint(DuelMap.StartOrigin(0), 3, 3));
    }

    [Fact]
    public void The_bases_are_connected_and_have_room_to_build()
    {
        var map = DuelMap.Create();
        var start0 = DuelMap.StartOrigin(0);
        var start1 = DuelMap.StartOrigin(1);

        for (var y = -2; y < 5; y++)
        {
            for (var x = -2; x < 5; x++)
            {
                Assert.True(map.IsBuildable(new Cell(start0.X + x, start0.Y + y)), $"Start area blocked at offset ({x}, {y}).");
            }
        }

        var path = new List<Cell>();
        Assert.True(new GridPathfinder(map).FindPath(start0, start1, path));
        Assert.True(map.TotalCindrite > 0);
    }

    private static async Task<MatchResult> RunAsync(ulong seed)
    {
        await using var match = await HeadlessMatch.StartAsync(MatchSpec.Skirmish(seed), GameSpeed.Unthrottled);
        return await match.RunToCompletionAsync(MatchSpec.SkirmishTickLimit, TestContext.Current.CancellationToken);
    }
}
