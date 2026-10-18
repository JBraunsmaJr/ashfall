namespace Ashfall.Networking.Transport;

public enum TransportEventKind
{
    /// <summary>A connection completed its handshake (either direction).</summary>
    Connected,

    /// <summary>A connection closed, timed out, or never completed.</summary>
    Disconnected,

    /// <summary>A whole message arrived, in order.</summary>
    Message,
}

/// <summary>Something that happened on a connection.</summary>
public readonly record struct TransportEvent(TransportEventKind Kind, ConnectionId Connection, ReadOnlyMemory<byte> Data = default);

/// <summary>A connection handle, unique within one transport.</summary>
public readonly record struct ConnectionId(int Value)
{
    public override string ToString() => $"conn#{Value}";
}
