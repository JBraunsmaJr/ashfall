using System.Net;
using System.Net.Sockets;

namespace Ashfall.Networking.Transport;

/// <summary>A non-blocking UDP socket on one port (0 picks a free one).</summary>
public sealed class UdpDatagramSocket : IDatagramSocket
{
    private const int MaxDatagram = 2048;

    private readonly Socket _socket;
    private readonly byte[] _receiveBuffer = new byte[MaxDatagram];
    private EndPoint _anySource = new IPEndPoint(IPAddress.Any, 0);

    public UdpDatagramSocket(int port, IPAddress? bindAddress = null)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        if (OperatingSystem.IsWindows())
        {
            // Without this, an ICMP "port unreachable" from a closed peer surfaces as a receive error.
            const int SioUdpConnreset = -1744830452;
            _socket.IOControl(SioUdpConnreset, [0, 0, 0, 0], null);
        }

        _socket.Bind(new IPEndPoint(bindAddress ?? IPAddress.Any, port));
        LocalEndPoint = (IPEndPoint)_socket.LocalEndPoint!;
    }

    public IPEndPoint LocalEndPoint { get; }

    public void SendTo(IPEndPoint destination, ReadOnlySpan<byte> datagram)
    {
        try
        {
            _socket.SendTo(datagram, SocketFlags.None, destination);
        }
        catch (SocketException)
        {
            // Unreachable or full: UDP is lossy anyway, the reliable layer resends.
        }
    }

    public bool TryReceive(out byte[] datagram, out IPEndPoint source)
    {
        while (_socket.Available > 0)
        {
            try
            {
                var length = _socket.ReceiveFrom(_receiveBuffer, SocketFlags.None, ref _anySource);
                datagram = _receiveBuffer.AsSpan(0, length).ToArray();
                source = (IPEndPoint)_anySource;
                return true;
            }
            catch (SocketException)
            {
                // A stale ICMP error for an earlier send; keep reading.
            }
        }

        datagram = [];
        source = null!;
        return false;
    }

    public void Dispose() => _socket.Dispose();
}
