using System.Net;

namespace Ashfall.Networking.Transport;

/// <summary>An unreliable datagram endpoint: a UDP socket, or a simulated one in tests.</summary>
public interface IDatagramSocket : IDisposable
{
    IPEndPoint LocalEndPoint { get; }

    void SendTo(IPEndPoint destination, ReadOnlySpan<byte> datagram);

    /// <summary>Returns the next datagram that has arrived, without blocking.</summary>
    bool TryReceive(out byte[] datagram, out IPEndPoint source);
}
