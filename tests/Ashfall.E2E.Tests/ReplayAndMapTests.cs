using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Networking.Wire;
using Ashfall.Session;
using Ashfall.Session.Ai;
using Ashfall.Session.Commands;
using Ashfall.Session.Maps;
using Ashfall.Session.Replays;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Map;

namespace Ashfall.E2E.Tests;

public class ReplayAndMapTests
{
    [Fact]
    public void A_single_player_match_replays_exactly_from_its_recording()
    {
        // The "person" is the scripted AI typing into the local command queue.
        var match = HeadlessMatch.Start(MatchSpec.Skirmish(6).WithController(0, PlayerController.Human), GameSpeed.Unthrottled);
        var person = new ScriptedSkirmishAi(0, 6);
        var typed = new List<Command>();
        while (!match.IsOver && match.Tick < MatchSpec.SkirmishTickLimit)
        {
            typed.Clear();
            person.CollectCommands(match.Simulation, match.Tick, typed);
            foreach (var command in typed)
            {
                match.LocalPlayer!.Enqueue(command);
            }

            match.Step();
        }

        Assert.True(match.IsOver);
        Assert.True(match.Log.CommandCount > 20);

        var replay = Replay.Decode(Replay.From(match).Encode());
        Assert.Equal(PlayerController.Remote, replay.Spec.Controllers[0]);
        Assert.Equal(PlayerController.ScriptedAi, replay.Spec.Controllers[1]);
        var (hash, matches) = replay.Verify();
        Assert.True(matches, $"Replay reached {StateHasher.Format(hash)}, recording {StateHasher.Format(replay.FinalHash)}");
    }

    [Fact]
    public void Corrupt_or_foreign_replays_are_refused()
    {
        Assert.Throws<ProtocolException>(() => Replay.Decode(new byte[] { 1, 2, 3 }));

        var match = HeadlessMatch.Start(MatchSpec.Skirmish(1), GameSpeed.Unthrottled);
        var bytes = Replay.From(match).Encode();
        Assert.Throws<ProtocolException>(() => Replay.Decode(bytes.AsMemory(0, bytes.Length - 3)));

        var foreign = new Replay(match.Spec, 12345, [], 0, 0);
        Assert.Throws<InvalidOperationException>(() => foreign.CreateMatch(GameSpeed.Unthrottled));
    }

    [Fact]
    public void Match_settings_round_trip_through_the_replay_codec()
    {
        var spec = MatchSpec.Skirmish(77, 4).WithTeams(0, 1, 0, 1).WithController(2, PlayerController.Remote) with { InputDelay = 3 };
        var writer = new PacketWriter();
        MatchSpecCodec.Write(writer, spec);

        var read = MatchSpecCodec.Read(new PacketReader(writer.ToArray()));

        Assert.Equal(spec.Seed, read.Seed);
        Assert.Equal(spec.Players, read.Players);
        Assert.Equal(spec.Teams, read.Teams);
        Assert.Equal(spec.Controllers, read.Controllers);
        Assert.Equal(3, read.InputDelay);
        Assert.Equal(spec.Map, read.Map);
    }

    [Fact]
    public void Swarm_settings_round_trip_through_the_replay_codec()
    {
        var spec = MatchSpec.Swarm(5, creatures: 2_500, nests: 6);
        var writer = new PacketWriter();
        MatchSpecCodec.Write(writer, spec);

        var read = MatchSpecCodec.Read(new PacketReader(writer.ToArray()));

        Assert.Equal(MatchMode.Swarm, read.Mode);
        Assert.Equal(2_500, read.Creatures);
        Assert.Equal(6, read.Nests);
        Assert.Equal(spec.Map, read.Map);
    }

    [Fact]
    public void The_quad_map_is_four_way_symmetric_with_room_for_every_base()
    {
        var map = QuadMap.Create();
        for (var y = 0; y < QuadMap.Size; y++)
        {
            for (var x = 0; x < QuadMap.Size; x++)
            {
                var cell = new Cell(x, y);
                var turned = QuadMap.Rotate(cell);
                Assert.Equal(map.TerrainAt(cell), map.TerrainAt(turned));
                Assert.Equal(map.CindriteAt(cell), map.CindriteAt(turned));
            }
        }

        var finder = new Simulation.Navigation.GridPathfinder(map);
        for (var player = 0; player < QuadMap.MaxPlayers; player++)
        {
            var origin = QuadMap.StartOrigin(player);
            Assert.Equal(QuadMap.RotateFootprint(origin, 3), QuadMap.StartOrigin((player + 1) % 4));
            for (var dy = -2; dy < 5; dy++)
            {
                for (var dx = -2; dx < 5; dx++)
                {
                    Assert.True(map.IsBuildable(new Cell(origin.X + dx, origin.Y + dy)));
                }
            }

            Assert.True(finder.FindPath(origin, QuadMap.StartOrigin((player + 1) % 4), []));
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Three_and_four_player_skirmishes_end_in_victory(int players)
    {
        var match = HeadlessMatch.Start(MatchSpec.Skirmish(2, players), GameSpeed.Unthrottled);
        while (!match.IsOver && match.Tick < MatchSpec.SkirmishTickLimit)
        {
            match.Step();
        }

        Assert.True(match.IsOver);
        Assert.Equal(QuadMap.Size, match.Simulation.Map.Width);
    }

    [Fact]
    public void Teams_win_together()
    {
        var match = HeadlessMatch.Start(MatchSpec.Skirmish(3, 4).WithTeams(0, 1, 0, 1), GameSpeed.Unthrottled);
        while (!match.IsOver && match.Tick < MatchSpec.SkirmishTickLimit)
        {
            match.Step();
        }

        Assert.True(match.IsOver);
        var winners = match.Simulation.Players.Where(p => p.Team == match.WinningTeam).ToList();
        Assert.Equal(2, winners.Count);
        Assert.All(match.Simulation.Players.Where(p => p.Team != match.WinningTeam), p => Assert.True(p.Defeated));
    }

    [Fact]
    public void Invalid_skirmish_settings_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpec.Skirmish(1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessMatch.Start(MatchSpec.Skirmish(1, 3).WithTeams(0, 0, 0), GameSpeed.Unthrottled));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessMatch.Start(MatchSpec.Skirmish(1, 3).WithTeams(0, 1), GameSpeed.Unthrottled));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessMatch.Start(
            MatchSpec.Skirmish(1).WithController(0, PlayerController.Human).WithController(1, PlayerController.Human), GameSpeed.Unthrottled));
    }

    [Fact]
    public void The_content_fingerprint_changes_when_any_rule_does()
    {
        var standard = GameContent.Standard;
        var units = standard.Units.ToList();
        units[0] = units[0] with { Cost = units[0].Cost + 1 };
        var tweaked = new GameContent(units, standard.Structures, standard.Factions, standard.Economy, standard.Creatures);
        var creatures = standard.Creatures.ToList();
        creatures[0] = creatures[0] with { Health = creatures[0].Health + 1 };

        Assert.Equal(standard.ComputeHash(), MawContent.AddTo(BulwarkContent.Create()).ComputeHash());
        Assert.NotEqual(standard.ComputeHash(), tweaked.ComputeHash());
        Assert.NotEqual(standard.ComputeHash(), standard.WithCreatures(creatures).ComputeHash());
    }
}
