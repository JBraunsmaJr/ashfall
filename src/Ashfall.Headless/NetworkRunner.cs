using System.Globalization;
using System.Net;
using Ashfall.Core;
using Ashfall.Networking.Transport;
using Ashfall.Session;
using Ashfall.Session.Network;

namespace Ashfall.Headless;

/// <summary>Runs bot players in network matches: one per process (--host/--join/--rejoin) or several in one (--lan).</summary>
internal static class NetworkRunner
{
    public const int DefaultPort = 47600;

    public static int Run(HeadlessOptions options, CancellationToken cancellation)
    {
        if (options.Lan > 0)
        {
            return RunLan(options, cancellation);
        }

        var port = options.Port ?? (options.Host ? DefaultPort : 0);
        var transport = new ReliableTransport(new UdpDatagramSocket(port));
        var identity = options.Identity is null
            ? NetworkIdentity.CreateRandom(options.Name)
            : NetworkIdentity.LoadOrCreate(options.Identity, options.Name);
        var networkOptions = BotOptions(options);

        using var session = options.Host
            ? NetworkSession.Host(transport, identity, Settings(options), networkOptions)
            : options.Rejoin is { } rejoin
                ? NetworkSession.Rejoin(transport, identity, ParseEndPoint(rejoin), networkOptions)
                : NetworkSession.Join(transport, identity, ParseEndPoint(options.Join!), networkOptions);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{identity.Name} ({identity.Peer}) on UDP port {transport.LocalEndPoint.Port}"));
        var result = Pump([session], options, cancellation);
        Report(session);
        if (options.HashOut is not null && session.Match is { } match)
        {
            File.WriteAllText(options.HashOut, StateHasher.Format(match.Simulation.ComputeStateHash()) + "\n");
        }

        if (options.RecordOut is not null && session.CaptureReplay() is { } replay)
        {
            replay.Save(options.RecordOut);
        }

        return result;
    }

    private static int RunLan(HeadlessOptions options, CancellationToken cancellation)
    {
        var sessions = new List<NetworkSession>();
        try
        {
            var settings = Settings(options) with
            {
                OtherSlots = [.. Enumerable.Repeat(Networking.Protocol.SlotKind.Open, options.Lan - 1), .. Enumerable.Repeat(Networking.Protocol.SlotKind.Ai, options.Ais)],
            };
            var host = NetworkSession.Host(new ReliableTransport(new UdpDatagramSocket(0, IPAddress.Loopback)), NetworkIdentity.CreateRandom("BOT0"), settings, BotOptions(options));
            sessions.Add(host);
            for (var i = 1; i < options.Lan; i++)
            {
                sessions.Add(NetworkSession.Join(
                    new ReliableTransport(new UdpDatagramSocket(0, IPAddress.Loopback)),
                    NetworkIdentity.CreateRandom("BOT" + i.ToString(CultureInfo.InvariantCulture)),
                    new IPEndPoint(IPAddress.Loopback, host.LocalEndPoint.Port),
                    BotOptions(options)));
            }

            var result = Pump(sessions, options, cancellation);
            foreach (var session in sessions)
            {
                Report(session);
            }

            var hashes = sessions.Select(s => s.Match?.Simulation.ComputeStateHash()).Distinct().ToList();
            if (result == 0 && hashes.Count != 1)
            {
                Console.Error.WriteLine("DESYNC: peers finished with different states.");
                return 4;
            }

            if (result == 0)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"All {sessions.Count} peers agree: {StateHasher.Format(hashes[0]!.Value)}"));
                if (options.HashOut is not null)
                {
                    File.WriteAllText(options.HashOut, StateHasher.Format(hashes[0]!.Value) + "\n");
                }
            }

            return result;
        }
        finally
        {
            foreach (var session in sessions)
            {
                session.Dispose();
            }
        }
    }

    // Updates every session until all have finished, auto-starting the host's match when the lobby is full.
    private static int Pump(List<NetworkSession> sessions, HeadlessOptions options, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(options.TimeoutSeconds);
        var lastReport = 0L;
        while (!cancellation.IsCancellationRequested)
        {
            foreach (var session in sessions)
            {
                session.Update();
                if (session.IsHost && session.State == SessionState.Lobby
                    && session.Lobby!.Slots.All(slot => slot.Kind != Networking.Protocol.SlotKind.Open) && session.StartBlocker is null)
                {
                    session.StartMatch();
                }
            }

            if (sessions.Any(s => s.State == SessionState.Desynced))
            {
                return 4;
            }

            if (sessions.Any(s => s.State == SessionState.Failed))
            {
                return 5;
            }

            if (sessions.All(s => s.State == SessionState.Ended))
            {
                return 0;
            }

            if (DateTime.UtcNow > deadline)
            {
                Console.Error.WriteLine("Timed out.");
                return 5;
            }

            var tick = sessions[0].Match?.Tick ?? 0;
            if (tick - lastReport >= 1800)
            {
                lastReport = tick;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"tick {tick} ({tick / 900.0:F1} min)"));
            }

            Thread.Sleep(1);
        }

        return 5;
    }

    private static void Report(NetworkSession session)
    {
        foreach (var line in session.Events)
        {
            Console.WriteLine($"  [{session.Identity.Name}] {line}");
        }

        if (session.Match is { } match)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{session.Identity.Name}: seat {session.LocalSeat} state {session.State} tick {match.Tick} hash {StateHasher.Format(match.Simulation.ComputeStateHash())} winner {(match.IsOver ? "team " + match.WinningTeam.ToString(CultureInfo.InvariantCulture) : "none")}"));
        }
        else
        {
            Console.WriteLine($"{session.Identity.Name}: {session.State} {session.Error}");
        }
    }

    private static HostSettings Settings(HeadlessOptions options) => HostSettings.For(options.Humans, options.Ais, options.Seed, options.GeneratedMap) with
    {
        Teams = options.Teams,
        InputDelay = options.InputDelay,
    };

    private static NetworkOptions BotOptions(HeadlessOptions options) => new()
    {
        Bot = true,
        AutoReady = true,
        Speed = options.Speed,
        AdvertisedPort = null,
        MaxTicksPerUpdate = 60,
    };

    private static IPEndPoint ParseEndPoint(string text) =>
        IPEndPoint.TryParse(text, out var endPoint) && endPoint.Port != 0
            ? endPoint
            : throw new ArgumentException($"'{text}' is not an ip:port address.");
}
