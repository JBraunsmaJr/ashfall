using System.Net;
using System.Text;
using Ashfall.Networking.Transport;

namespace Ashfall.Networking.Tests;

public class TransportTests
{
    private static readonly string[] Expected = ["one", "two", "three"];

    [Fact]
    public void Two_peers_connect_and_exchange_messages()
    {
        var (clock, network) = Network();
        using var a = new ReliableTransport(network.CreateSocket(), clock);
        using var b = new ReliableTransport(network.CreateSocket(), clock);

        var toB = a.Connect(b.LocalEndPoint);
        Pump(clock, [a, b], TimeSpan.FromMilliseconds(200));

        Assert.True(a.IsConnected(toB));
        var connectedOnB = Drain(b).Single(e => e.Kind == TransportEventKind.Connected);
        Assert.Contains(Drain(a), e => e.Kind == TransportEventKind.Connected && e.Connection == toB);

        a.Send(toB, "hello"u8);
        b.Send(connectedOnB.Connection, "hi"u8);
        Pump(clock, [a, b], TimeSpan.FromMilliseconds(200));

        Assert.Equal("hello", Text(Drain(b).Single(e => e.Kind == TransportEventKind.Message)));
        Assert.Equal("hi", Text(Drain(a).Single(e => e.Kind == TransportEventKind.Message)));
    }

    [Fact]
    public void Messages_arrive_once_and_in_order_through_heavy_loss_and_jitter()
    {
        var (clock, network) = Network();
        network.Loss = 0.25;
        network.Jitter = TimeSpan.FromMilliseconds(60);
        using var a = new ReliableTransport(network.CreateSocket(), clock);
        using var b = new ReliableTransport(network.CreateSocket(), clock);
        var toB = a.Connect(b.LocalEndPoint);
        Pump(clock, [a, b], TimeSpan.FromSeconds(2));
        Assert.True(a.IsConnected(toB));

        for (var i = 0; i < 500; i++)
        {
            a.Send(toB, Encoding.UTF8.GetBytes(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var received = new List<string>();
        for (var step = 0; step < 2_000 && received.Count < 500; step++)
        {
            Pump(clock, [a, b], TimeSpan.FromMilliseconds(10));
            received.AddRange(Drain(b).Where(e => e.Kind == TransportEventKind.Message).Select(Text));
        }

        Assert.Equal(Enumerable.Range(0, 500).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), received);
        Assert.True(a.Resends > 0);
        Assert.True(network.Dropped > 0);
    }

    [Fact]
    public void Large_messages_are_fragmented_and_reassembled()
    {
        var (clock, network) = Network();
        network.Loss = 0.1;
        using var a = new ReliableTransport(network.CreateSocket(), clock);
        using var b = new ReliableTransport(network.CreateSocket(), clock);
        var toB = a.Connect(b.LocalEndPoint);
        Pump(clock, [a, b], TimeSpan.FromSeconds(1));

        var payload = new byte[300_000];
        new Random(5).NextBytes(payload);
        a.Send(toB, payload);

        byte[]? received = null;
        for (var step = 0; step < 3_000 && received is null; step++)
        {
            Pump(clock, [a, b], TimeSpan.FromMilliseconds(10));
            received = Drain(b).FirstOrDefault(e => e.Kind == TransportEventKind.Message).Data.ToArray() is { Length: > 0 } data ? data : null;
        }

        Assert.Equal(payload, received);
    }

    [Fact]
    public void A_silent_peer_times_out()
    {
        var (clock, network) = Network();
        using var a = new ReliableTransport(network.CreateSocket(), clock);
        var socketB = network.CreateSocket();
        using var b = new ReliableTransport(socketB, clock);
        var toB = a.Connect(b.LocalEndPoint);
        Pump(clock, [a, b], TimeSpan.FromMilliseconds(500));
        Drain(a);

        network.Disconnect(b.LocalEndPoint);
        Pump(clock, [a, b], TimeSpan.FromSeconds(12));

        Assert.Contains(Drain(a), e => e.Kind == TransportEventKind.Disconnected && e.Connection == toB);
        Assert.False(a.IsConnected(toB));
    }

    [Fact]
    public void Connecting_to_nobody_fails_after_the_handshake_timeout()
    {
        var (clock, network) = Network();
        using var a = new ReliableTransport(network.CreateSocket(), clock);

        var attempt = a.Connect(new IPEndPoint(IPAddress.Parse("10.9.9.9"), 1));
        Pump(clock, [a], TimeSpan.FromSeconds(6));

        Assert.Contains(Drain(a), e => e.Kind == TransportEventKind.Disconnected && e.Connection == attempt);
    }

    [Fact]
    public void Round_trip_time_is_measured()
    {
        var (clock, network) = Network();
        network.Latency = TimeSpan.FromMilliseconds(40);
        network.Jitter = TimeSpan.Zero;
        using var a = new ReliableTransport(network.CreateSocket(), clock);
        using var b = new ReliableTransport(network.CreateSocket(), clock);
        var toB = a.Connect(b.LocalEndPoint);

        Pump(clock, [a, b], TimeSpan.FromSeconds(3));

        Assert.InRange(a.RoundTrip(toB)!.Value.TotalMilliseconds, 70, 110);
    }

    [Fact]
    public void Real_udp_sockets_work_on_loopback()
    {
        using var a = new ReliableTransport(new UdpDatagramSocket(0, IPAddress.Loopback));
        using var b = new ReliableTransport(new UdpDatagramSocket(0, IPAddress.Loopback));
        var toB = a.Connect(new IPEndPoint(IPAddress.Loopback, b.LocalEndPoint.Port));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var got = new List<string>();
        var sent = false;
        while (DateTime.UtcNow < deadline && got.Count < 3)
        {
            a.Update();
            b.Update();
            if (!sent && a.IsConnected(toB))
            {
                a.Send(toB, "one"u8);
                a.Send(toB, "two"u8);
                a.Send(toB, "three"u8);
                sent = true;
            }

            got.AddRange(Drain(b).Where(e => e.Kind == TransportEventKind.Message).Select(Text));
            Thread.Sleep(2);
        }

        Assert.Equal(Expected, got);
    }

    private static (TestClock Clock, SimulatedNetwork Network) Network()
    {
        var clock = new TestClock();
        return (clock, new SimulatedNetwork(clock, seed: 3));
    }

    private static void Pump(TestClock clock, ReliableTransport[] transports, TimeSpan duration)
    {
        var step = TimeSpan.FromMilliseconds(5);
        for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += step)
        {
            clock.Advance(step);
            foreach (var transport in transports)
            {
                transport.Update();
            }
        }
    }

    private static List<TransportEvent> Drain(ReliableTransport transport)
    {
        var events = new List<TransportEvent>();
        while (transport.TryDequeue(out var e))
        {
            events.Add(e);
        }

        return events;
    }

    private static string Text(TransportEvent e) => Encoding.UTF8.GetString(e.Data.Span);
}
