using System.Net;
using Ashfall.Networking.Protocol;
using Ashfall.Networking.Transport;
using Ashfall.Session;
using Ashfall.Session.Network;

namespace Ashfall.E2E.Tests;

/// <summary>A clock tests advance by hand, for timestamps and wall-clock time alike.</summary>
internal sealed class SteppedClock : TimeProvider
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public TimeSpan Elapsed { get; private set; }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Elapsed.Ticks;

    public override DateTimeOffset GetUtcNow() => Epoch + Elapsed;

    public void Advance(TimeSpan by) => Elapsed += by;
}

/// <summary>
/// Several players in one process, each with its own <see cref="NetworkSession"/>, on a simulated
/// network (deterministic: a stepped clock and seeded loss/jitter) or on real UDP sockets on loopback.
/// Every player is a bot: the scripted AI plays its seat through the network like a person would.
/// </summary>
internal sealed class LanParty : IDisposable
{
    private readonly List<NetworkSession> _sessions = [];

    private LanParty(SteppedClock? clock, SimulatedNetwork? network)
    {
        Clock = clock;
        Network = network;
    }

    public SteppedClock? Clock { get; }

    public SimulatedNetwork? Network { get; }

    public IReadOnlyList<NetworkSession> Sessions => _sessions;

    public NetworkSession Host => _sessions[0];

    public static LanParty Simulated(int humans, int ais = 0, ulong seed = 11, double loss = 0, int? inputDelay = null, string? dumps = null)
    {
        var clock = new SteppedClock();
        var network = new SimulatedNetwork(clock, seed: (int)seed) { Loss = loss };
        var party = new LanParty(clock, network);
        party.Open(humans, ais, seed, inputDelay, dumps, () => network.CreateSocket());
        return party;
    }

    public static LanParty Udp(int humans, int ais = 0, ulong seed = 11)
    {
        var party = new LanParty(null, null);
        party.Open(humans, ais, seed, null, null, () => new UdpDatagramSocket(0, IPAddress.Loopback));
        return party;
    }

    public NetworkOptions Options(string? dumps) => new()
    {
        Bot = true,
        AutoReady = true,
        Speed = GameSpeed.Unthrottled,
        Time = (TimeProvider?)Clock ?? TimeProvider.System,
        DumpDirectory = dumps,
        MaxTicksPerUpdate = 60,
    };

    public ReliableTransport Transport(IDatagramSocket socket) => new(socket, (TimeProvider?)Clock ?? TimeProvider.System);

    /// <summary>Pumps every session (advancing the simulated clock) until the condition holds.</summary>
    public bool RunUntil(Func<LanParty, bool> condition, TimeSpan budget)
    {
        var step = TimeSpan.FromMilliseconds(5);
        var deadline = DateTime.UtcNow + budget;
        var simulated = TimeSpan.Zero;
        while (!condition(this))
        {
            if (Clock is not null)
            {
                Clock.Advance(step);
                simulated += step;
                if (simulated > budget)
                {
                    return false;
                }
            }
            else if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            foreach (var session in _sessions)
            {
                session.Update();
            }

            if (Clock is null)
            {
                Thread.Yield();
            }
        }

        return true;
    }

    /// <summary>Every invited player has joined and is ready, and the host could start.</summary>
    public bool ReadyToStart =>
        Host.Lobby is { } lobby
        && lobby.Slots.All(slot => slot.Kind != Networking.Protocol.SlotKind.Open)
        && Host.StartBlocker is null;

    public bool AllInState(SessionState state) => _sessions.All(session => session.State == state);

    public void Replace(int index, NetworkSession session) => _sessions[index] = session;

    public void Add(NetworkSession session) => _sessions.Add(session);

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }
    }

    private void Open(int humans, int ais, ulong seed, int? inputDelay, string? dumps, Func<IDatagramSocket> socket)
    {
        var settings = HostSettings.For(humans, ais, seed) with { InputDelay = inputDelay };
        var host = NetworkSession.Host(Transport(socket()), NetworkIdentity.CreateRandom("HOST"), settings, Options(dumps));
        _sessions.Add(host);
        var hostAddress = Clock is null ? new IPEndPoint(IPAddress.Loopback, host.LocalEndPoint.Port) : host.LocalEndPoint;
        for (var i = 1; i < humans; i++)
        {
            _sessions.Add(NetworkSession.Join(Transport(socket()), NetworkIdentity.CreateRandom("P" + i), hostAddress, Options(dumps)));
        }
    }
}
