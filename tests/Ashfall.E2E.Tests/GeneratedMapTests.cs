using Ashfall.Core;
using Ashfall.MapGen;
using Ashfall.Session;
using Ashfall.Session.Maps;
using Ashfall.Session.Network;
using Ashfall.Session.Replays;
using Ashfall.Simulation;

namespace Ashfall.E2E.Tests;

/// <summary>
/// GDD section 10, "Map generation" suite: generated maps are played, not just validated. AIs finish
/// matches on every archetype, network peers each generate the same map from its code, and replays
/// carry the code so they rebuild the map they were played on.
/// </summary>
public class GeneratedMapTests
{
    public static TheoryData<string, int> Matches => new()
    {
        { "crossroads", 2 },
        { "crossroads", 4 },
        { "riverlands", 2 },
        { "riverlands", 4 },
    };

    [Theory]
    [MemberData(nameof(Matches))]
    public async Task Scripted_ais_play_a_generated_map_to_victory_and_the_replay_rebuilds_it(string archetype, int players)
    {
        var map = new MapSettings(MapArchetypes.Find(archetype)!, 31);
        var spec = MatchSpec.Skirmish(31, players).WithGeneratedMap(map);
        if (players == 4)
        {
            spec = spec.WithTeams(0, 1, 0, 1);
        }

        await using var match = await HeadlessMatch.StartAsync(spec, GameSpeed.Unthrottled);
        Assert.NotNull(match.Map?.Generated);
        Assert.Equal(MapGenerator.SizeOf(map), match.Simulation.Map.Width);
        Assert.StartsWith(MapArchetypes.Find(archetype)!.Name, match.Map!.Name);

        var result = await match.RunToCompletionAsync(MatchSpec.SkirmishTickLimit, TestContext.Current.CancellationToken);
        Assert.True(result.IsOver, $"no winner on {map.ToCode()} with {players} players by tick {result.FinalTick}");

        var replay = Replay.Decode(Replay.From(match).Encode());
        Assert.Equal(map, replay.Spec.GeneratedMap);
        var (hash, matches) = replay.Verify();
        Assert.True(matches, $"replay reached {StateHasher.Format(hash)}, recording {StateHasher.Format(replay.FinalHash)}");
    }

    [Fact]
    public void Network_peers_generate_the_hosts_map_from_its_code_and_play_it_in_sync()
    {
        var map = new MapSettings(MapArchetypes.Riverlands, 404).With(MapSlider.Elevation, 60);
        using var party = LanParty.Simulated(humans: 3, seed: 12, map: map);
        Assert.True(party.RunUntil(p => p.ReadyToStart, TimeSpan.FromSeconds(10)));

        // Every lobby copy carries the code; each peer decodes the same settings.
        Assert.All(party.Sessions, session => Assert.Equal(map, NetworkSession.MapOf(session.Lobby)));

        party.Host.StartMatch();
        Assert.True(party.RunUntil(p => p.AllInState(SessionState.Ended), TimeSpan.FromMinutes(30)));
        var expected = MatchMap.HashOf(map, 3);
        Assert.All(party.Sessions, session =>
        {
            Assert.Equal(map, session.Match!.Spec.GeneratedMap);
            Assert.Equal(expected, session.Match.Map!.Hash);
            Assert.Equal(party.Host.Match!.Simulation.ComputeStateHash(), session.Match.Simulation.ComputeStateHash());
        });
    }

    [Fact]
    public void Hand_made_and_generated_specs_validate_their_map_size()
    {
        var map = new MapSettings(MapArchetypes.Crossroads, 1).With(MapSlider.Size, 100);
        var spec = MatchSpec.Skirmish(1).WithGeneratedMap(map);
        Assert.Equal(128, spec.Map.Width);
        spec.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (spec with { Map = new MapBounds(64, 64) }).Validate());
        var back = spec.WithGeneratedMap(null);
        Assert.Null(back.GeneratedMap);
        Assert.Equal(64, back.Map.Width);
        back.Validate();
    }

    [Fact]
    public void Hand_made_maps_keep_their_hash_so_old_matches_still_agree()
    {
        // Height and the new terrain types sit in bits the hand-made maps leave at zero.
        var duel = MatchMap.Create(null, 2);
        Assert.Equal("Duel", duel.Name);
        Assert.Null(duel.Generated);
        Assert.Equal(DuelMap.StartOrigin(1), duel.StartOrigins[1]);
        var hasher = new StateHasher();
        DuelMap.Create().HashInto(ref hasher);
        Assert.Equal(hasher.Value, duel.Hash);
    }
}
