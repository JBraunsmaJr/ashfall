using System.Numerics;
using Ashfall.Networking.Protocol;
using Ashfall.Networking.Transport;
using Ashfall.Rendering.Scene;
using Ashfall.Session;
using Ashfall.Session.Network;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

public class LobbyScreenTests
{
    [Fact]
    public void The_host_starts_a_network_match_from_the_lobby_screen_and_plays_it_networked()
    {
        var clock = new LobbyClock();
        var network = new SimulatedNetwork(clock);
        using var host = NetworkSession.Host(new ReliableTransport(network.CreateSocket(), clock), NetworkIdentity.CreateRandom("JON"),
            HostSettings.For(2, 1, 4), new NetworkOptions { Time = clock, Speed = GameSpeed.Unthrottled, DumpDirectory = null });
        using var guest = NetworkSession.Join(new ReliableTransport(network.CreateSocket(), clock), NetworkIdentity.CreateRandom("ANA"),
            host.LocalEndPoint, new NetworkOptions { Time = clock, Speed = GameSpeed.Unthrottled, Bot = true, AutoReady = true, DumpDirectory = null });
        var screen = new LobbyScreen(host);
        var input = new InputState { Mouse = new Vector2(10, 10) };
        var scene = new FrameScene();

        void Frame()
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            host.Update();
            guest.Update();
            screen.Update(input, new Vector2(1280, 720));
            screen.Draw(scene);
            input.EndFrame();
        }

        for (var i = 0; i < 400 && (host.StartBlocker is not null || host.Lobby!.Slots[1].Kind != SlotKind.Human); i++)
        {
            Frame();
        }

        Assert.Null(host.StartBlocker);
        Assert.Equal(SlotKind.Human, host.Lobby!.Slots[1].Kind);
        Assert.True(scene.QuadCount > 100);

        // The START button sits under the slot rows; find it by sweeping a click down the left side.
        for (var y = 200f; y < 400 && host.State == SessionState.Lobby; y += 8)
        {
            input.Mouse = new Vector2(330, y);
            input.SetButton(MouseButton.Left, true);
            Frame();
            input.SetButton(MouseButton.Left, false);
            Frame();
        }

        Assert.Equal(SessionState.Playing, host.State);
        var game = new GameScreen(host.Match!, host.Orders, networked: true);
        input.SetKey(Key.P, true);
        game.Update(input, new Vector2(1280, 720));
        Assert.False(game.Paused); // nobody pauses a network match alone
        Assert.True(game.Networked);
    }

    private sealed class LobbyClock : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private TimeSpan _elapsed;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _elapsed.Ticks;

        public override DateTimeOffset GetUtcNow() => Epoch + _elapsed;

        public void Advance(TimeSpan by) => _elapsed += by;
    }
}
