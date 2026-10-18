namespace Ashfall.Networking.Transport;

/// <summary>Timers and windows for <see cref="ReliableTransport"/>. Defaults suit home internet and LAN.</summary>
public sealed record TransportOptions
{
    /// <summary>Silence after which a connection is considered lost.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a new connection may take to complete its handshake.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan HandshakeInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan PingInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan InitialRetransmit { get; init; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan MinRetransmit { get; init; } = TimeSpan.FromMilliseconds(40);

    public TimeSpan MaxRetransmit { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Unacknowledged data packets allowed in flight per connection.</summary>
    public int SendWindow { get; init; } = 256;

    /// <summary>How far ahead of the next expected packet the receiver buffers.</summary>
    public int ReceiveWindow { get; init; } = 1024;
}
