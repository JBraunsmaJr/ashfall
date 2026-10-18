using System.Net;

namespace Ashfall.Networking.Transport;

/// <summary>
/// An in-memory network for tests: sockets get addresses 10.0.0.x, and every datagram is delayed by a
/// latency plus random jitter, or dropped with some probability, so the reliable layer and the
/// lockstep protocol are exercised under bad conditions without real sockets. Time comes from the
/// supplied <see cref="TimeProvider"/>, so a test can drive it deterministically.
/// </summary>
public sealed class SimulatedNetwork(TimeProvider time, int seed = 1)
{
    private readonly Dictionary<IPEndPoint, SimulatedSocket> _sockets = [];
    private readonly Random _random = new(seed);
    private int _nextHost = 1;

    /// <summary>One-way delay applied to every datagram.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>Extra random delay, 0..Jitter; datagrams can arrive out of order.</summary>
    public TimeSpan Jitter { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>Probability (0..1) that a datagram is lost.</summary>
    public double Loss { get; set; }

    public long Sent { get; private set; }

    public long Dropped { get; private set; }

    public TimeProvider Time { get; } = time;

    public IDatagramSocket CreateSocket(int port = 47600)
    {
        var address = new IPEndPoint(IPAddress.Parse($"10.0.0.{_nextHost++}"), port);
        var socket = new SimulatedSocket(this, address);
        _sockets.Add(address, socket);
        return socket;
    }

    /// <summary>Cuts a socket off (as if its machine lost its connection); datagrams to and from it vanish.</summary>
    public void Disconnect(IPEndPoint address)
    {
        if (_sockets.TryGetValue(address, out var socket))
        {
            socket.Unplugged = true;
        }
    }

    private void Deliver(SimulatedSocket from, IPEndPoint to, ReadOnlySpan<byte> datagram)
    {
        Sent++;
        if (from.Unplugged || !_sockets.TryGetValue(to, out var target) || target.Unplugged || _random.NextDouble() < Loss)
        {
            Dropped++;
            return;
        }

        var delay = Latency + TimeSpan.FromTicks((long)(_random.NextDouble() * Jitter.Ticks));
        target.Enqueue(Time.GetUtcNow() + delay, from.LocalEndPoint, datagram.ToArray());
    }

    private void Remove(SimulatedSocket socket) => _sockets.Remove(socket.LocalEndPoint);

    private sealed class SimulatedSocket(SimulatedNetwork network, IPEndPoint address) : IDatagramSocket
    {
        private readonly PriorityQueue<(IPEndPoint Source, byte[] Data), (DateTimeOffset Due, long Order)> _inbox = new();
        private long _order;

        public IPEndPoint LocalEndPoint { get; } = address;

        public bool Unplugged { get; set; }

        public void SendTo(IPEndPoint destination, ReadOnlySpan<byte> datagram) => network.Deliver(this, destination, datagram);

        public bool TryReceive(out byte[] datagram, out IPEndPoint source)
        {
            if (_inbox.TryPeek(out var item, out var priority) && priority.Due <= network.Time.GetUtcNow())
            {
                _inbox.Dequeue();
                datagram = item.Data;
                source = item.Source;
                return true;
            }

            datagram = [];
            source = null!;
            return false;
        }

        public void Enqueue(DateTimeOffset due, IPEndPoint source, byte[] data) => _inbox.Enqueue((source, data), (due, _order++));

        public void Dispose() => network.Remove(this);
    }
}
