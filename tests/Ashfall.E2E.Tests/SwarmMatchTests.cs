using Ashfall.Content;
using Ashfall.Session;
using Ashfall.Session.Maps;
using Ashfall.Simulation;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;

namespace Ashfall.E2E.Tests;

public class SwarmMatchTests
{
    [Fact]
    public async Task The_swarm_stress_match_is_identical_on_every_run()
    {
        var spec = MatchSpec.Swarm(31, creatures: 400);

        var first = await RunAsync(spec, 450);
        var second = await RunAsync(spec, 450);

        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(first.Checkpoints, second.Checkpoints);
        Assert.Equal(15, first.Checkpoints.Count);
    }

    [Fact]
    public async Task The_fortress_stands_ready_and_the_maw_arrives_from_the_edges()
    {
        await using var match = await HeadlessMatch.StartAsync(MatchSpec.Swarm(31, creatures: 300), GameSpeed.Unthrottled);
        var simulation = match.Simulation;
        var (structures, defenders) = SwarmFortress.Survivors(simulation, player: 0);
        Assert.Equal(8, structures);
        Assert.Equal((3 * SwarmFortress.DefendersPerKind) + SwarmFortress.Pyros, defenders);
        Assert.Equal((SwarmFortress.WallRadius * 8) - (4 * SwarmFortress.GateWidth), SwarmFortress.Walls(simulation, player: 0));
        Assert.False(simulation.VictoryEnabled);

        await match.RunTicksAsync(30, TestContext.Current.CancellationToken);

        Assert.Equal(300, simulation.World.Store<Creature>().Count);
        foreach (var index in simulation.World.Store<Creature>().EntityIndices)
        {
            var position = simulation.World.Store<Position>().Get(index).Value;
            var x = position.X.FloorToInt();
            var y = position.Y.FloorToInt();
            Assert.True(x < 8 || y < 8 || x >= SwarmFortress.Size - 8 || y >= SwarmFortress.Size - 8, $"A creature appeared at ({x}, {y}).");
        }

        // A minute later the swarm has reached the walls and both sides have taken losses.
        await match.RunTicksAsync(870, TestContext.Current.CancellationToken);
        Assert.True(simulation.Maw!.Killed > 0);
        Assert.True(SwarmFortress.Survivors(simulation, player: 0).Units < defenders);
    }

    [Fact]
    public async Task With_nests_the_maw_breeds_from_regrowing_fields_instead_of_the_edges()
    {
        await using var match = await HeadlessMatch.StartAsync(MatchSpec.Swarm(31, creatures: 2_000, nests: 3), GameSpeed.Unthrottled);
        var simulation = match.Simulation;
        Assert.Equal(3, simulation.World.Store<Nest>().Count);
        Assert.True(simulation.CindriteRegrowth);
        Assert.False(simulation.Maw!.Settings.EdgeSpawns);
        var cindrite = simulation.Map.TotalCindrite;
        Assert.True(cindrite > 0);

        await match.RunTicksAsync(60, TestContext.Current.CancellationToken);

        // Every creature so far came out of a nest, so each is near one: well inside the edges, far from the fortress.
        Assert.NotEmpty(simulation.World.Store<Creature>().EntityIndices.ToArray());
        foreach (var index in simulation.World.Store<Creature>().EntityIndices)
        {
            var position = simulation.World.Store<Position>().Get(index).Value;
            var x = position.X.FloorToInt() - (SwarmFortress.Size / 2);
            var y = position.Y.FloorToInt() - (SwarmFortress.Size / 2);
            var distance = Math.Max(Math.Abs(x), Math.Abs(y));
            Assert.InRange(distance, SwarmFortress.NestDistance - 12, SwarmFortress.NestDistance + 8);
        }

        await match.RunTicksAsync(900, TestContext.Current.CancellationToken);
        Assert.True(simulation.Map.TotalCindrite < cindrite, "The nests should be eating their fields.");
    }

    [Fact]
    public async Task A_fortress_held_by_a_person_is_recorded_and_replays_to_the_same_end()
    {
        await using var match = await HeadlessMatch.StartAsync(
            MatchSpec.Swarm(5, creatures: 400, nests: 2).WithController(0, PlayerController.Human), GameSpeed.Unthrottled);
        var simulation = match.Simulation;
        var orders = match.LocalPlayer!;
        var content = simulation.Content;
        var wall = content.StructureIndex(BulwarkContent.Wall);
        var barracks = simulation.World.EntityAt(simulation.World.Store<StructureInfo>().EntityIndices.ToArray()
            .First(index => content.Structure(simulation.World.Store<StructureInfo>().Get(index).StructureType).Id == BulwarkContent.Barracks));

        // A short wall line just inside the fortress, and two Pyros.
        var placed = 0;
        for (var x = 50; x < 80 && placed < 6; x++)
        {
            var cell = new Ashfall.Simulation.Map.Cell(x, 56);
            if (simulation.CheckPlacement(0, wall, cell) == Ashfall.Simulation.PlacementResult.Ok)
            {
                orders.Enqueue(Command.Build(0, wall, cell));
                placed++;
            }
        }

        orders.Enqueue(Command.Train(0, barracks, content.UnitIndex(BulwarkContent.Pyro)));
        orders.Enqueue(Command.Train(0, barracks, content.UnitIndex(BulwarkContent.Pyro)));
        await match.RunTicksAsync(900, TestContext.Current.CancellationToken);
        Assert.Equal(6, placed);
        Assert.Equal(0, simulation.RejectedCommands);

        var replay = Ashfall.Session.Replays.Replay.Decode(Ashfall.Session.Replays.Replay.From(match).Encode());
        Assert.Equal(PlayerController.Remote, replay.Spec.Controllers[0]);
        Assert.Equal((2, 400), (replay.Spec.Nests, replay.Spec.Creatures));
        Assert.True(replay.Verify().Matches);
    }

    [Fact]
    public void Swarm_specs_are_validated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpec.Swarm(1).WithController(0, PlayerController.ScriptedAi).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpec.Swarm(1, nests: MatchSpec.SwarmMaxNests + 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpec.Swarm(1, nests: -1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (MatchSpec.Swarm(1) with { Creatures = MatchSpec.SwarmMaxCreatures + 1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (MatchSpec.Swarm(1) with { Players = 2 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpec.Swarm(1).WithMap(64, 64).Validate());
        MatchSpec.Swarm(1).Validate();
        Assert.Equal(MatchSpec.SwarmDefaultCreatures, MatchSpec.Swarm(1).Creatures);
    }

    private static async Task<MatchResult> RunAsync(MatchSpec spec, long ticks)
    {
        await using var match = await HeadlessMatch.StartAsync(spec, GameSpeed.Unthrottled);
        return await match.RunTicksAsync(ticks, TestContext.Current.CancellationToken);
    }
}
