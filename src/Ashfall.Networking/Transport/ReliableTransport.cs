using System.Buffers.Binary;
using System.Net;

namespace Ashfall.Networking.Transport;

/// <summary>
/// Reliable, ordered messages over an unreliable datagram socket: the only channel the lockstep
/// protocol needs. Each connection numbers its data packets; the receiver acknowledges the next
/// sequence it expects (cumulative) and buffers anything that arrives early; the sender resends
/// whatever is unacknowledged after a timeout derived from the measured round trip. Messages larger
/// than one packet are split into fragments and reassembled. Pings measure the round trip and keep
/// idle connections alive; silence for <see cref="TransportOptions.Timeout"/> closes a connection.
/// </summary>
/// <remarks>
/// Single-threaded: call <see cref="Update"/> regularly (every frame or tick) from one thread; it
/// sends, resends and receives, and queues <see cref="TransportEvent"/>s for <see cref="TryDequeue"/>.
/// </remarks>
public sealed class ReliableTransport : IDisposable
{
    private const ushort Magic = 0xA5F1;
    private const byte Version = 1;
    private const int HeaderSize = 4;
    private const int DataHeaderSize = HeaderSize + 4 + 4 + 1;

    /// <summary>Payload bytes per data packet, keeping datagrams under a typical 1280-byte MTU.</summary>
    public const int FragmentSize = 1100;

    private readonly IDatagramSocket _socket;
    private readonly TimeProvider _time;
    private readonly TransportOptions _options;
    private readonly Dictionary<IPEndPoint, Connection> _byEndPoint = [];
    private readonly Dictionary<int, Connection> _byId = [];
    private readonly Queue<TransportEvent> _events = new();
    private readonly List<Connection> _scratch = [];
    private readonly byte[] _packet = new byte[DataHeaderSize + FragmentSize];
    private int _nextId = 1;

    public ReliableTransport(IDatagramSocket socket, TimeProvider? time = null, TransportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _time = time ?? TimeProvider.System;
        _options = options ?? new TransportOptions();
    }

    public IPEndPoint LocalEndPoint => _socket.LocalEndPoint;

    /// <summary>Accept connections other peers open to us (hosts and lobby members do; a lone client need not).</summary>
    public bool AcceptIncoming { get; set; } = true;

    /// <summary>Data packets sent again because no acknowledgement came back in time.</summary>
    public long Resends { get; private set; }

    /// <summary>Opens a connection; <see cref="TransportEventKind.Connected"/> or <see cref="TransportEventKind.Disconnected"/> follows.</summary>
    public ConnectionId Connect(IPEndPoint remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        if (_byEndPoint.TryGetValue(remote, out var existing))
        {
            return existing.Id;
        }

        var connection = Add(remote, outgoing: true);
        connection.Nonce = (ulong)Random.Shared.NextInt64();
        SendControl(connection, PacketType.Connect, connection.Nonce);
        connection.LastHandshake = Now;
        return connection.Id;
    }

    public bool IsConnected(ConnectionId id) => _byId.TryGetValue(id.Value, out var connection) && connection.Established;

    public IPEndPoint? RemoteEndPoint(ConnectionId id) => _byId.TryGetValue(id.Value, out var connection) ? connection.Remote : null;

    /// <summary>Smoothed round-trip time, or null before the first measurement.</summary>
    public TimeSpan? RoundTrip(ConnectionId id) =>
        _byId.TryGetValue(id.Value, out var connection) && connection.SmoothedRtt is { } rtt ? rtt : null;

    /// <summary>Queues a message for reliable, ordered delivery (also allowed before the handshake completes).</summary>
    public void Send(ConnectionId id, ReadOnlySpan<byte> message)
    {
        if (!_byId.TryGetValue(id.Value, out var connection))
        {
            return;
        }

        var offset = 0;
        do
        {
            var length = Math.Min(FragmentSize, message.Length - offset);
            var final = offset + length == message.Length;
            connection.Outgoing.Enqueue(new Fragment(connection.NextSendSequence++, message.Slice(offset, length).ToArray(), final));
            offset += length;
        }
        while (offset < message.Length);
    }

    /// <summary>Closes a connection, telling the other side (best effort).</summary>
    public void Disconnect(ConnectionId id)
    {
        if (_byId.TryGetValue(id.Value, out var connection))
        {
            // Flush what we can first so a final message (a goodbye) usually arrives.
            Flush(connection, force: true);
            SendControl(connection, PacketType.Disconnect, 0);
            Close(connection, notify: false);
        }
    }

    public bool TryDequeue(out TransportEvent transportEvent) => _events.TryDequeue(out transportEvent);

    /// <summary>Receives, acknowledges, resends, pings and times out. Call often.</summary>
    public void Update()
    {
        var now = Now;
        while (_socket.TryReceive(out var datagram, out var source))
        {
            Receive(datagram, source, now);
        }

        _scratch.Clear();
        _scratch.AddRange(_byId.Values);
        foreach (var connection in _scratch)
        {
            if (!connection.Established)
            {
                if (now - connection.Created > _options.ConnectTimeout)
                {
                    Close(connection, notify: true);
                }
                else if (connection.Outgoing2Handshake && now - connection.LastHandshake > _options.HandshakeInterval)
                {
                    SendControl(connection, PacketType.Connect, connection.Nonce);
                    connection.LastHandshake = now;
                }

                continue;
            }

            if (now - connection.LastHeard > _options.Timeout)
            {
                Close(connection, notify: true);
                continue;
            }

            if (connection.AckPending)
            {
                SendAck(connection);
            }

            if (now - connection.LastPing > _options.PingInterval)
            {
                SendControl(connection, PacketType.Ping, (ulong)now.UtcTicks);
                connection.LastPing = now;
            }

            Flush(connection, force: false);
        }
    }

    public void Dispose()
    {
        foreach (var connection in _byId.Values.ToList())
        {
            SendControl(connection, PacketType.Disconnect, 0);
        }

        _socket.Dispose();
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    private Connection Add(IPEndPoint remote, bool outgoing)
    {
        var connection = new Connection(new ConnectionId(_nextId++), remote, Now) { Outgoing2Handshake = outgoing };
        _byEndPoint[remote] = connection;
        _byId[connection.Id.Value] = connection;
        return connection;
    }

    private void Close(Connection connection, bool notify)
    {
        _byEndPoint.Remove(connection.Remote);
        _byId.Remove(connection.Id.Value);
        if (notify)
        {
            _events.Enqueue(new TransportEvent(TransportEventKind.Disconnected, connection.Id));
        }
    }

    private void Establish(Connection connection, DateTimeOffset now)
    {
        if (connection.Established)
        {
            return;
        }

        connection.Established = true;
        connection.LastHeard = now;
        connection.LastPing = now - _options.PingInterval;
        _events.Enqueue(new TransportEvent(TransportEventKind.Connected, connection.Id));
    }

    private void Receive(byte[] datagram, IPEndPoint source, DateTimeOffset now)
    {
        if (datagram.Length < HeaderSize || BinaryPrimitives.ReadUInt16LittleEndian(datagram) != Magic || datagram[2] != Version)
        {
            return; // not ours, or another protocol version
        }

        var type = (PacketType)datagram[3];
        _byEndPoint.TryGetValue(source, out var connection);

        if (type == PacketType.Connect && datagram.Length >= HeaderSize + 8)
        {
            var nonce = BinaryPrimitives.ReadUInt64LittleEndian(datagram.AsSpan(HeaderSize));
            if (connection is { Established: true } && connection.RemoteNonce != nonce)
            {
                // A new handshake from an endpoint we already talk to: the peer restarted (a rejoin).
                // Drop the old connection, then accept the new one.
                Close(connection, notify: true);
                connection = null;
            }

            if (connection is null)
            {
                if (!AcceptIncoming)
                {
                    return;
                }

                connection = Add(source, outgoing: false);
            }

            // Also covers both sides connecting at once: each accepts the other's handshake.
            connection.RemoteNonce = nonce;
            SendControl(connection, PacketType.Accept, nonce);
            Establish(connection, now);
            return;
        }

        if (connection is null)
        {
            return;
        }

        connection.LastHeard = now;
        switch (type)
        {
            case PacketType.Accept:
                if (connection.Outgoing2Handshake && datagram.Length >= HeaderSize + 8
                    && BinaryPrimitives.ReadUInt64LittleEndian(datagram.AsSpan(HeaderSize)) == connection.Nonce)
                {
                    Establish(connection, now);
                }

                break;

            case PacketType.Data when connection.Established && datagram.Length >= DataHeaderSize:
                var sequence = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(HeaderSize));
                var ack = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(HeaderSize + 4));
                var final = datagram[HeaderSize + 8] != 0;
                Acknowledge(connection, ack, now);
                ReceiveData(connection, sequence, datagram.AsSpan(DataHeaderSize).ToArray(), final);
                break;

            case PacketType.Ack when datagram.Length >= HeaderSize + 4:
                Acknowledge(connection, BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(HeaderSize)), now);
                break;

            case PacketType.Ping when datagram.Length >= HeaderSize + 8:
                SendControl(connection, PacketType.Pong, BinaryPrimitives.ReadUInt64LittleEndian(datagram.AsSpan(HeaderSize)));
                break;

            case PacketType.Pong when datagram.Length >= HeaderSize + 8:
                var sent = new DateTimeOffset((long)BinaryPrimitives.ReadUInt64LittleEndian(datagram.AsSpan(HeaderSize)), TimeSpan.Zero);
                MeasureRoundTrip(connection, now - sent);
                break;

            case PacketType.Disconnect:
                Close(connection, notify: true);
                break;
        }
    }

    private void ReceiveData(Connection connection, uint sequence, byte[] payload, bool final)
    {
        connection.AckPending = true;
        var ahead = unchecked(sequence - connection.NextExpected);
        if (ahead >= int.MaxValue)
        {
            return; // an old duplicate (sequence behind what we expect)
        }

        if (ahead > (uint)_options.ReceiveWindow)
        {
            return; // too far ahead to buffer; it will be resent
        }

        connection.Early[sequence] = (payload, final);
        while (connection.Early.Remove(connection.NextExpected, out var next))
        {
            connection.NextExpected++;
            connection.Assembly.Write(next.Payload);
            if (next.Final)
            {
                _events.Enqueue(new TransportEvent(TransportEventKind.Message, connection.Id, connection.Assembly.ToArray()));
                connection.Assembly.SetLength(0);
            }
        }
    }

    private static void Acknowledge(Connection connection, uint nextExpectedByPeer, DateTimeOffset now)
    {
        // A fragment is acknowledged when its sequence is before the peer's next expected one (mod 2^32).
        while (connection.InFlight.Count > 0 && unchecked(nextExpectedByPeer - connection.InFlight.Peek().Fragment.Sequence - 1) < int.MaxValue)
        {
            var acknowledged = connection.InFlight.Dequeue();
            if (acknowledged.SendCount == 1)
            {
                MeasureRoundTrip(connection, now - acknowledged.LastSent);
            }
        }
    }

    private static void MeasureRoundTrip(Connection connection, TimeSpan sample)
    {
        if (sample < TimeSpan.Zero)
        {
            return;
        }

        // RFC 6298 smoothing.
        if (connection.SmoothedRtt is not { } srtt)
        {
            connection.SmoothedRtt = sample;
            connection.RttVariance = sample / 2;
            return;
        }

        var error = sample - srtt;
        connection.RttVariance = (connection.RttVariance * 3 / 4) + (error.Duration() / 4);
        connection.SmoothedRtt = (srtt * 7 / 8) + (sample / 8);
    }

    private TimeSpan RetransmitTimeout(Connection connection)
    {
        var timeout = connection.SmoothedRtt is { } srtt ? srtt + (connection.RttVariance * 4) : _options.InitialRetransmit;
        return timeout < _options.MinRetransmit ? _options.MinRetransmit
            : timeout > _options.MaxRetransmit ? _options.MaxRetransmit
            : timeout;
    }

    private void Flush(Connection connection, bool force)
    {
        var now = Now;
        var timeout = RetransmitTimeout(connection);

        // Resend what has waited too long for its acknowledgement.
        foreach (var inFlight in connection.InFlight)
        {
            if (force || now - inFlight.LastSent >= timeout * Math.Min(inFlight.SendCount, 4))
            {
                SendData(connection, inFlight.Fragment);
                inFlight.LastSent = now;
                inFlight.SendCount++;
                Resends++;
            }
        }

        // Then new fragments, up to the send window.
        while (connection.Outgoing.Count > 0 && (force || connection.InFlight.Count < _options.SendWindow))
        {
            var fragment = connection.Outgoing.Dequeue();
            SendData(connection, fragment);
            connection.InFlight.Enqueue(new InFlight(fragment) { LastSent = now, SendCount = 1 });
        }
    }

    private void SendData(Connection connection, Fragment fragment)
    {
        var packet = _packet.AsSpan(0, DataHeaderSize + fragment.Payload.Length);
        WriteHeader(packet, PacketType.Data);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[HeaderSize..], fragment.Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[(HeaderSize + 4)..], connection.NextExpected);
        packet[HeaderSize + 8] = fragment.Final ? (byte)1 : (byte)0;
        fragment.Payload.CopyTo(packet[DataHeaderSize..]);
        _socket.SendTo(connection.Remote, packet);
        connection.AckPending = false;
    }

    private void SendAck(Connection connection)
    {
        Span<byte> packet = stackalloc byte[HeaderSize + 4];
        WriteHeader(packet, PacketType.Ack);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[HeaderSize..], connection.NextExpected);
        _socket.SendTo(connection.Remote, packet);
        connection.AckPending = false;
    }

    private void SendControl(Connection connection, PacketType type, ulong value)
    {
        Span<byte> packet = stackalloc byte[HeaderSize + 8];
        WriteHeader(packet, type);
        BinaryPrimitives.WriteUInt64LittleEndian(packet[HeaderSize..], value);
        _socket.SendTo(connection.Remote, packet);
    }

    private static void WriteHeader(Span<byte> packet, PacketType type)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(packet, Magic);
        packet[2] = Version;
        packet[3] = (byte)type;
    }

    private enum PacketType : byte
    {
        Connect = 1,
        Accept = 2,
        Data = 3,
        Ack = 4,
        Ping = 5,
        Pong = 6,
        Disconnect = 7,
    }

    private sealed record Fragment(uint Sequence, byte[] Payload, bool Final);

    private sealed class InFlight(Fragment fragment)
    {
        public Fragment Fragment { get; } = fragment;

        public DateTimeOffset LastSent { get; set; }

        public int SendCount { get; set; }
    }

    private sealed class Connection(ConnectionId id, IPEndPoint remote, DateTimeOffset created)
    {
        public ConnectionId Id { get; } = id;

        public IPEndPoint Remote { get; } = remote;

        public DateTimeOffset Created { get; } = created;

        public bool Outgoing2Handshake { get; init; }

        public bool Established { get; set; }

        /// <summary>Our handshake nonce, for connections we opened.</summary>
        public ulong Nonce { get; set; }

        /// <summary>The peer's handshake nonce, for connections it opened.</summary>
        public ulong RemoteNonce { get; set; }

        public DateTimeOffset LastHandshake { get; set; }

        public DateTimeOffset LastHeard { get; set; }

        public DateTimeOffset LastPing { get; set; }

        public TimeSpan? SmoothedRtt { get; set; }

        public TimeSpan RttVariance { get; set; }

        public uint NextSendSequence { get; set; }

        public uint NextExpected { get; set; }

        public bool AckPending { get; set; }

        public Queue<Fragment> Outgoing { get; } = new();

        public Queue<InFlight> InFlight { get; } = new();

        public Dictionary<uint, (byte[] Payload, bool Final)> Early { get; } = [];

        public MemoryStream Assembly { get; } = new();
    }
}
