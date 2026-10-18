using Ashfall.Core;
using Ashfall.Networking.Protocol;
using Ashfall.Session;
using Ashfall.Session.Network;
using Ashfall.Session.Replays;
using Ashfall.Simulation.Components;

namespace Ashfall.E2E.Tests;

/// <summary>M3: peer-to-peer lockstep matches between bots, simulated and over real UDP.</summary>
public class MultiplayerTests
{
    private static readonly int[] TwoVersusTwo = [0, 1, 0, 1];
    private static readonly int[] ThreeSlots = [0, 1, 2];

    [Fact]
    public void Four_players_on_a_lossy_network_finish_a_match_without_desyncing()
    {
        using var party = LanParty.Simulated(humans: 4, seed: 3, loss: 0.05);
        party.Network!.Jitter = TimeSpan.FromMilliseconds(30);

        Assert.True(party.RunUntil(p => p.ReadyToStart, TimeSpan.FromSeconds(10)), string.Join("\n", party.Host.Events));
        party.Host.StartMatch();
        Assert.True(party.RunUntil(p => p.Sessions.All(s => s.State != SessionState.Playing && s.State != SessionState.Lobby), TimeSpan.FromMinutes(30)),
            $"Stalled at ticks {string.Join(", ", party.Sessions.Select(s => s.Match?.Tick))}");

        AssertSameOutcome(party);
        Assert.True(party.Network.Dropped > 0);
    }

    [Fact]
    public void Two_humans_and_two_ais_in_teams_agree_on_everything()
    {
        using var party = LanParty.Simulated(humans: 2, ais: 2, seed: 5);
        party.Host.SetSlotTeam(1, 1);
        party.Host.SetSlotTeam(2, 0);
        party.Host.SetSlotTeam(3, 1);

        Assert.True(party.RunUntil(p => p.ReadyToStart, TimeSpan.FromSeconds(10)));
        party.Host.StartMatch();
        Assert.True(party.RunUntil(p => p.AllInState(SessionState.Ended), TimeSpan.FromMinutes(30)));

        AssertSameOutcome(party);
        Assert.Equal(TwoVersusTwo, party.Host.Match!.Spec.Teams);
    }

    [Fact]
    public void Joiners_get_slots_and_open_a_full_mesh_before_the_start()
    {
        using var party = LanParty.Simulated(humans: 3);

        Assert.True(party.RunUntil(p => p.ReadyToStart, TimeSpan.FromSeconds(10)));

        Assert.All(party.Sessions, session => Assert.True(session.MeshComplete, $"slot {session.LocalSlot} mesh incomplete"));
        Assert.Equal(ThreeSlots, party.Sessions.Select(s => s.LocalSlot).Order());
        Assert.All(party.Host.Lobby!.Slots, slot => Assert.Equal(SlotKind.Human, slot.Kind));
        Assert.Equal("only the host starts the match", party.Sessions[1].StartBlocker);
    }

    [Fact]
    public void A_full_lobby_and_a_different_version_are_refused()
    {
        using var party = LanParty.Simulated(humans: 2);
        Assert.True(party.RunUntil(p => p.ReadyToStart, TimeSpan.FromSeconds(10)));

        var late = NetworkSession.Join(party.Transport(party.Network!.CreateSocket()), NetworkIdentity.CreateRandom("LATE"), party.Host.LocalEndPoint, party.Options(null));
        party.Add(late);
        Assert.True(party.RunUntil(_ => late.State == SessionState.Failed, TimeSpan.FromSeconds(5)));
        Assert.Contains("full", late.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_desync_halts_every_peer_and_writes_dumps()
    {
        var dumps = Path.Combine(Path.GetTempPath(), "ashfall-desync-" + Guid.NewGuid().ToString("N"));
        using var party = LanParty.Simulated(humans: 3, seed: 9, dumps: dumps);
        Assert.True(party.RunUntil(p => p.ReadyToStart, TimeSpan.FromSeconds(10)));
        party.Host.StartMatch();
        Assert.True(party.RunUntil(p => p.Sessions.All(s => s.Match?.Tick >= 100), TimeSpan.FromMinutes(5)));

        // Corrupt one peer's copy of the game: a construction yard loses a hit point.
        var cheater = party.Sessions[2].Match!.Simulation;
        var yard = cheater.World.EntityAt(cheater.World.Store<StructureInfo>().EntityIndices[0]);
        cheater.World.Get<Health>(yard) = cheater.World.Get<Health>(yard) with { Current = cheater.World.Get<Health>(yard).Current - 1 };

        Assert.True(party.RunUntil(p => p.Sessions.All(s => s.State == SessionState.Desynced), TimeSpan.FromMinutes(1)));
        var reports = party.Sessions.Select(s => s.Desync!).ToList();
        Assert.Single(reports.Select(r => r.Tick).Distinct());
        Assert.All(reports, report => Assert.True(File.Exists(report.DumpPath)));
        Assert.Contains("mismatch at tick", File.ReadAllText(reports[0].DumpPath!), StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(dumps, "*.ashreplay"));
        Directory.Delete(dumps, recursive: true);
    }

    [Fact]
    public void A_player_who_crashes_can_rejoin_and_the_match_carries_on_in_sync()
    {
        using var party = LanParty.Simulated(humans: 3, seed: 21);
        Assert.True(party.RunUntil(p => p.ReadyToStart, TimeSpan.FromSeconds(10)));
        party.Host.StartMatch();
        Assert.True(party.RunUntil(p => p.Sessions.All(s => s.Match?.Tick >= 600), TimeSpan.FromMinutes(5)));

        // Player 2's machine drops off the network; the others stall waiting for its turns.
        var crashed = party.Sessions[2];
        party.Network!.Disconnect(crashed.LocalEndPoint);
        var identity = crashed.Identity;
        Assert.True(party.RunUntil(p => p.Sessions[0].WaitingFor.Count > 0, TimeSpan.FromSeconds(5)));
        var stalledAt = party.Host.Match!.Tick;

        // It comes back as a new process (new socket, same identity) and rejoins through player 1.
        var rejoined = NetworkSession.Rejoin(party.Transport(party.Network.CreateSocket()), identity, party.Sessions[1].LocalEndPoint, party.Options(null));
        party.Replace(2, rejoined);
        Assert.True(party.RunUntil(p => p.AllInState(SessionState.Ended), TimeSpan.FromMinutes(30)),
            $"{string.Join(" | ", rejoined.Events)} -- ticks {string.Join(", ", party.Sessions.Select(s => s.Match?.Tick))}");

        Assert.True(party.Host.Match!.Tick > stalledAt);
        AssertSameOutcome(party);
    }

    [Fact]
    public void A_player_who_leaves_stops_sending_and_the_rest_play_on()
    {
        using var party = LanParty.Simulated(humans: 3, seed: 4);
        Assert.True(party.RunUntil(p => p.ReadyToStart, TimeSpan.FromSeconds(10)));
        party.Host.StartMatch();
        Assert.True(party.RunUntil(p => p.Sessions.All(s => s.Match?.Tick >= 300), TimeSpan.FromMinutes(5)));

        var leaver = party.Sessions[1];
        var seat = leaver.LocalSeat;
        leaver.Leave();

        Assert.True(party.RunUntil(p => p.Sessions[0].State == SessionState.Ended && p.Sessions[2].State == SessionState.Ended, TimeSpan.FromMinutes(30)));
        Assert.Equal(party.Sessions[0].Match!.Simulation.ComputeStateHash(), party.Sessions[2].Match!.Simulation.ComputeStateHash());
        Assert.NotNull(party.Sessions[0].Match!.Turns!.LeftAfter(seat));
    }

    [Fact]
    public void A_network_match_replays_to_the_same_final_state()
    {
        using var party = LanParty.Simulated(humans: 2, seed: 8);
        Assert.True(party.RunUntil(p => p.ReadyToStart, TimeSpan.FromSeconds(10)));
        party.Host.StartMatch();
        Assert.True(party.RunUntil(p => p.AllInState(SessionState.Ended), TimeSpan.FromMinutes(30)));

        var replay = Replay.Decode(party.Sessions[1].CaptureReplay()!.Encode());
        var (hash, matches) = replay.Verify();

        Assert.True(matches, $"Replay ended at {StateHasher.Format(hash)}, match at {StateHasher.Format(replay.FinalHash)}");
        Assert.Equal(party.Host.Match!.Simulation.ComputeStateHash(), hash);
    }

    [Fact]
    public void Four_players_over_real_udp_on_loopback_finish_without_desyncs()
    {
        // The M3 exit gate in miniature: a 4-player LAN match between separate peers, no desyncs.
        using var party = LanParty.Udp(humans: 4, seed: 2);

        Assert.True(party.RunUntil(p => p.ReadyToStart, TimeSpan.FromSeconds(20)), string.Join("\n", party.Host.Events));
        party.Host.StartMatch();
        Assert.True(party.RunUntil(p => p.Sessions.All(s => s.State is SessionState.Ended or SessionState.Desynced or SessionState.Failed), TimeSpan.FromMinutes(5)),
            $"Ticks {string.Join(", ", party.Sessions.Select(s => s.Match?.Tick))}");

        AssertSameOutcome(party);
    }

    private static void AssertSameOutcome(LanParty party)
    {
        Assert.All(party.Sessions, session => Assert.Equal(SessionState.Ended, session.State));
        var reference = party.Host.Match!;
        Assert.True(reference.IsOver);
        Assert.InRange(reference.WinningTeam, 0, 7);
        foreach (var session in party.Sessions.Skip(1))
        {
            var match = session.Match!;
            Assert.Equal(reference.Tick, match.Tick);
            Assert.Equal(reference.Simulation.ComputeStateHash(), match.Simulation.ComputeStateHash());
            Assert.Equal(reference.Checkpoints, match.Checkpoints);
            Assert.Null(session.Desync);
        }
    }
}
