using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Numerics;
using Ashfall.Core;
using Ashfall.Networking.Transport;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.Session;
using Ashfall.Session.Network;
using Ashfall.UI;
using Brutal.GlfwApi;

namespace Ashfall.Client;

/// <summary>The client's multiplayer loop: lobby screen, then the match, both driven by a <see cref="NetworkSession"/>.</summary>
internal static class MultiplayerClient
{
    public const int DefaultPort = 47600;

    public static int Run(GlfwWindow window, VulkanRenderer renderer, GlfwInput input, ClientOptions options)
    {
        var port = options.Port ?? (options.Host ? DefaultPort : 0);
        var transport = new ReliableTransport(new UdpDatagramSocket(port));
        var identity = NetworkIdentity.LoadOrCreate(NetworkIdentity.DefaultPath, options.Name);
        var networkOptions = new NetworkOptions { Speed = GameSpeed.RealTime, Bot = options.Autopilot };
        using var session = options.Host
            ? NetworkSession.Host(transport, identity, HostSettings.For(options.Humans, options.Ais, options.Seed), networkOptions)
            : options.Rejoin is { } rejoin
                ? NetworkSession.Rejoin(transport, identity, IPEndPoint.Parse(rejoin), networkOptions)
                : NetworkSession.Join(transport, identity, IPEndPoint.Parse(options.Join!), networkOptions);
        Console.WriteLine($"{identity.Name} ({identity.Peer}) on UDP port {transport.LocalEndPoint.Port}");

        var lobby = new LobbyScreen(session);
        GameScreen? game = null;
        var scene = new FrameScene();
        var clock = Stopwatch.StartNew();
        var lastFrame = TimeSpan.Zero;
        var waitingSince = (TimeSpan?)null;

        while (!window.ShouldClose)
        {
            if (options.Frames > 0 && renderer.FramesPresented >= options.Frames)
            {
                break;
            }

            Glfw.PollEvents();
            input.Poll();
            var now = clock.Elapsed;
            input.State.DeltaSeconds = (float)(now - lastFrame).TotalSeconds;
            input.State.Time = now.TotalSeconds;
            lastFrame = now;
            var framebuffer = new Vector2(window.FramebufferSize.X, window.FramebufferSize.Y);

            session.Update();

            if (session.Match is { } match && session.State is SessionState.Playing or SessionState.Ended or SessionState.Desynced)
            {
                if (game is null)
                {
                    game = new GameScreen(match, session.Orders, networked: true);
                    session.Ticked += game.OnTick;
                }

                // Only call it waiting once a stall lasts long enough to notice.
                waitingSince = session.WaitingFor.Count == 0 ? null : waitingSince ?? now;
                game.Overlay = session.State == SessionState.Desynced
                    ? "DESYNC - MATCH HALTED, DUMP WRITTEN"
                    : waitingSince is { } since && now - since > TimeSpan.FromMilliseconds(600)
                        ? "WAITING FOR " + string.Join(", ", session.WaitingFor.Select(session.NameOfSeat))
                        : null;
                game.Update(input.State, framebuffer);
                game.Draw(scene, TickAlpha(match), now.TotalSeconds);
                if (game.ExitRequested)
                {
                    break;
                }
            }
            else
            {
                lobby.Update(input.State, framebuffer);
                lobby.Draw(scene);
                if (lobby.ExitRequested)
                {
                    break;
                }
            }

            if (options.Screenshot is not null && options.Frames > 0 && renderer.FramesPresented == options.Frames - 1)
            {
                renderer.RequestCapture();
            }

            if (renderer.RenderFrame(scene) == FrameResult.Skipped)
            {
                Thread.Sleep(5);
            }

            input.State.EndFrame();
            window.Title = string.Create(CultureInfo.InvariantCulture,
                $"Ashfall — {session.State} — {identity.Name} — tick {session.Match?.Tick ?? 0}");
        }

        if (options.Screenshot is not null && renderer.LastCapture is { } capture)
        {
            PngEncoder.WriteFile(options.Screenshot, capture);
            Console.WriteLine($"Saved {capture.Size} screenshot to {options.Screenshot}.");
        }

        if (options.Record is not null && session.CaptureReplay() is { } replay)
        {
            replay.Save(options.Record);
            Console.WriteLine($"Saved replay to {options.Record}.");
        }

        // Say goodbye properly: keep pumping briefly so the leave message and our last turns arrive.
        session.Leave();
        var linger = Stopwatch.StartNew();
        while (linger.Elapsed < TimeSpan.FromMilliseconds(500))
        {
            session.Update();
            Thread.Sleep(10);
        }

        foreach (var line in session.Events)
        {
            Console.WriteLine("  " + line);
        }

        if (session.Match is { } final)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Left at tick {final.Tick} ({session.State}), state {StateHasher.Format(final.Simulation.ComputeStateHash())}."));
        }

        return session.State == SessionState.Failed ? 1 : 0;
    }

    private static float TickAlpha(HeadlessMatch match)
    {
        var speed = match.Clock.Speed;
        if (speed.IsUnthrottled)
        {
            return 1f;
        }

        var tick = speed.DueOffset(1);
        var remaining = match.Clock.TimeUntilDue(match.Tick);
        return tick <= TimeSpan.Zero ? 1f : Math.Clamp(1f - (float)(remaining / tick), 0f, 1f);
    }
}
