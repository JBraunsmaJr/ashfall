using Ashfall.Core;
using Ashfall.Networking.Protocol;
using Ashfall.Networking.Wire;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;

namespace Ashfall.Networking.Tests;

public class WireTests
{
    [Fact]
    public void Values_round_trip_little_endian()
    {
        var writer = new PacketWriter(1);
        writer.WriteByte(7);
        writer.WriteBool(true);
        writer.WriteUInt16(0xBEEF);
        writer.WriteInt32(-5);
        writer.WriteUInt64(ulong.MaxValue - 1);
        writer.WriteInt64(long.MinValue);
        writer.WriteString("Ashfall ✓");
        writer.WriteBytes([1, 2, 3]);

        Assert.Equal(new byte[] { 7, 1, 0xEF, 0xBE }, writer.WrittenSpan[..4].ToArray());
        var reader = new PacketReader(writer.ToArray());
        Assert.Equal(7, reader.ReadByte());
        Assert.True(reader.ReadBool());
        Assert.Equal(0xBEEF, reader.ReadUInt16());
        Assert.Equal(-5, reader.ReadInt32());
        Assert.Equal(ulong.MaxValue - 1, reader.ReadUInt64());
        Assert.Equal(long.MinValue, reader.ReadInt64());
        Assert.Equal("Ashfall ✓", reader.ReadString());
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.ReadBytes());
        reader.ExpectEnd();
    }

    [Fact]
    public void Truncated_or_hostile_data_is_rejected()
    {
        Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 1, 2 }).ReadInt32());
        Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 2 }).ReadBool());
        Assert.Throws<ProtocolException>(() => new PacketReader(BitConverter.GetBytes(int.MaxValue)).ReadCount(100));
        Assert.Throws<ProtocolException>(() => Message.Decode(new byte[] { 99 }));
        Assert.Throws<ProtocolException>(() => Message.Decode(new byte[] { (byte)MessageType.Ready, 1, 0 })); // trailing byte
    }

    [Fact]
    public void Commands_round_trip_exactly_and_take_their_seat_from_the_turn()
    {
        Command[] commands =
        [
            Command.Move(3, new Entity(12, 4), new FixVec2(Fix64.FromRaw(123456789), Fix64.FromRaw(-987654321))),
            Command.Attack(3, new Entity(1, 1), new Entity(40, 2)),
            Command.Build(3, 2, new Cell(10, 11)),
            Command.Train(3, new Entity(7, 3), 1),
            Command.Stop(3, new Entity(9, 9)),
        ];

        var bytes = new TurnMessage(42, 3, commands).Encode();
        var decoded = (TurnMessage)Message.Decode(bytes);

        Assert.Equal(commands, decoded.Commands);

        // The seat is the only source of a command's player: a forged id in the payload is impossible.
        var reader = new PacketReader(bytes.AsMemory(1 + 8 + 4));
        var forged = CommandCodec.ReadList(reader, playerId: 1);
        Assert.All(forged, command => Assert.Equal(1, command.PlayerId));
    }

    [Fact]
    public void Every_message_type_round_trips()
    {
        var peer = PeerId.NewRandom();
        var lobby = new LobbyState(3, 99, 0xABCDEF, [
            new LobbySlot(SlotKind.Human, peer, "Jon", 0, true, "10.0.0.1:47600"),
            LobbySlot.Ai(1),
            LobbySlot.Open(0),
        ]);
        var start = new StartMessage(lobby, 3, 0x1234);
        Message[] messages =
        [
            new HelloMessage(Message.ProtocolVersion, peer, "Jon", 77, 47600, false),
            new WelcomeMessage(2),
            new RejectMessage("full"),
            new LobbyMessage(lobby),
            new ReadyMessage(true),
            new SetTeamMessage(1),
            start,
            new TurnMessage(10, 0, []),
            new HashMessage(30, 1, 0xDEADBEEF),
            new LeaveMessage(1, 55),
            new ChatMessage(0, "gl hf"),
            new PeerHelloMessage(peer, 0, -1),
            new CatchUpMessage(start, 0, [new TurnMessage(4, 0, [Command.Stop(0, new Entity(1, 1))])], [new LeaveMessage(1, 3)]),
        ];

        Assert.Equal(Enum.GetValues<MessageType>().Length, messages.Length);
        foreach (var message in messages)
        {
            var decoded = Message.Decode(message.Encode());
            Assert.Equal(message.Type, decoded.Type);
            Assert.Equal(Hex(message.Encode()), Hex(decoded.Encode()));
        }
    }

    [Fact]
    public void Peer_ids_print_and_parse()
    {
        var id = PeerId.NewRandom();

        Assert.Equal(id, PeerId.Parse(id.ToHex()));
        Assert.Equal(8, id.ToString().Length);
        Assert.False(id.IsNone);
        Assert.True(PeerId.None.IsNone);
    }

    [Fact]
    public void Lobby_seats_skip_open_and_closed_slots()
    {
        var lobby = new LobbyState(0, 1, 2, [
            LobbySlot.Open(0),
            LobbySlot.Ai(1),
            new LobbySlot(SlotKind.Closed, PeerId.None, string.Empty, 0, false, string.Empty),
            new LobbySlot(SlotKind.Human, PeerId.NewRandom(), "P", 0, false, string.Empty),
        ]);

        Assert.Equal([1, 3], lobby.Seats().Select(seat => seat.Slot));
        Assert.Equal(-1, lobby.SeatOfSlot(0));
        Assert.Equal(0, lobby.SeatOfSlot(1));
        Assert.Equal(1, lobby.SeatOfSlot(3));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
}
