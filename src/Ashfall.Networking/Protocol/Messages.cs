using Ashfall.Networking.Wire;
using Ashfall.Simulation.Commands;

namespace Ashfall.Networking.Protocol;

public enum MessageType : byte
{
    Hello = 1,
    Welcome = 2,
    Reject = 3,
    Lobby = 4,
    Ready = 5,
    SetTeam = 6,
    Start = 7,
    Turn = 8,
    Hash = 9,
    Leave = 10,
    Chat = 11,
    PeerHello = 12,
    CatchUp = 13,
}

/// <summary>
/// Everything peers say to each other. Each message is one reliable, ordered transport message whose
/// first byte is its <see cref="MessageType"/>.
/// </summary>
public abstract record Message
{
    /// <summary>Bumped whenever the wire format changes; peers with different versions cannot play together.</summary>
    public const int ProtocolVersion = 2;

    public abstract MessageType Type { get; }

    public byte[] Encode()
    {
        var writer = new PacketWriter();
        writer.WriteByte((byte)Type);
        WriteBody(writer);
        return writer.ToArray();
    }

    public static Message Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new PacketReader(data);
        var type = (MessageType)reader.ReadByte();
        Message message = type switch
        {
            MessageType.Hello => HelloMessage.Read(reader),
            MessageType.Welcome => new WelcomeMessage(reader.ReadInt32()),
            MessageType.Reject => new RejectMessage(reader.ReadString()),
            MessageType.Lobby => new LobbyMessage(LobbyState.Read(reader)),
            MessageType.Ready => new ReadyMessage(reader.ReadBool()),
            MessageType.SetTeam => new SetTeamMessage(reader.ReadInt32()),
            MessageType.Start => StartMessage.Read(reader),
            MessageType.Turn => TurnMessage.Read(reader),
            MessageType.Hash => new HashMessage(reader.ReadInt64(), reader.ReadInt32(), reader.ReadUInt64()),
            MessageType.Leave => new LeaveMessage(reader.ReadInt32(), reader.ReadInt64()),
            MessageType.Chat => new ChatMessage(reader.ReadInt32(), reader.ReadString()),
            MessageType.PeerHello => new PeerHelloMessage(PeerId.Read(reader), reader.ReadInt32(), reader.ReadInt64()),
            MessageType.CatchUp => CatchUpMessage.Read(reader),
            _ => throw new ProtocolException($"Unknown message type {(byte)type}."),
        };
        reader.ExpectEnd();
        return message;
    }

    /// <summary>Writes the body without the type byte, for messages nested inside others.</summary>
    public void WriteBodyTo(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteBody(writer);
    }

    protected abstract void WriteBody(PacketWriter writer);
}

/// <summary>First message on a connection to the host (or, when rejoining, to any peer).</summary>
public sealed record HelloMessage(int Version, PeerId Peer, string Name, ulong ContentHash, int ListenPort, bool Rejoin) : Message
{
    public override MessageType Type => MessageType.Hello;

    public static HelloMessage Read(PacketReader reader) =>
        new(reader.ReadInt32(), PeerId.Read(reader), reader.ReadString(), reader.ReadUInt64(), reader.ReadInt32(), reader.ReadBool());

    protected override void WriteBody(PacketWriter writer)
    {
        writer.WriteInt32(Version);
        Peer.Write(writer);
        writer.WriteString(Name);
        writer.WriteUInt64(ContentHash);
        writer.WriteInt32(ListenPort);
        writer.WriteBool(Rejoin);
    }
}

/// <summary>The host accepted the joiner into lobby slot <paramref name="Slot"/>.</summary>
public sealed record WelcomeMessage(int Slot) : Message
{
    public override MessageType Type => MessageType.Welcome;

    protected override void WriteBody(PacketWriter writer) => writer.WriteInt32(Slot);
}

public sealed record RejectMessage(string Reason) : Message
{
    public override MessageType Type => MessageType.Reject;

    protected override void WriteBody(PacketWriter writer) => writer.WriteString(Reason);
}

public sealed record LobbyMessage(LobbyState Lobby) : Message
{
    public override MessageType Type => MessageType.Lobby;

    protected override void WriteBody(PacketWriter writer) => Lobby.Write(writer);
}

public sealed record ReadyMessage(bool Ready) : Message
{
    public override MessageType Type => MessageType.Ready;

    protected override void WriteBody(PacketWriter writer) => writer.WriteBool(Ready);
}

public sealed record SetTeamMessage(int Team) : Message
{
    public override MessageType Type => MessageType.SetTeam;

    protected override void WriteBody(PacketWriter writer) => writer.WriteInt32(Team);
}

/// <summary>The host starts the match: final lobby, the agreed input delay and the expected map hash.</summary>
public sealed record StartMessage(LobbyState Lobby, int InputDelay, ulong MapHash) : Message
{
    public override MessageType Type => MessageType.Start;

    public static StartMessage Read(PacketReader reader) => new(LobbyState.Read(reader), reader.ReadInt32(), reader.ReadUInt64());

    protected override void WriteBody(PacketWriter writer)
    {
        Lobby.Write(writer);
        writer.WriteInt32(InputDelay);
        writer.WriteUInt64(MapHash);
    }
}

/// <summary>Seat <paramref name="Seat"/>'s commands for tick <paramref name="Tick"/> (often none: an empty turn still counts).</summary>
public sealed record TurnMessage(long Tick, int Seat, IReadOnlyList<Command> Commands) : Message
{
    public override MessageType Type => MessageType.Turn;

    public static TurnMessage Read(PacketReader reader)
    {
        var tick = reader.ReadInt64();
        var seat = reader.ReadInt32();
        return new TurnMessage(tick, seat, CommandCodec.ReadList(reader, seat));
    }

    protected override void WriteBody(PacketWriter writer)
    {
        writer.WriteInt64(Tick);
        writer.WriteInt32(Seat);
        CommandCodec.WriteList(writer, Commands);
    }
}

/// <summary>Seat <paramref name="Seat"/>'s state hash after tick <paramref name="Tick"/>.</summary>
public sealed record HashMessage(long Tick, int Seat, ulong Hash) : Message
{
    public override MessageType Type => MessageType.Hash;

    protected override void WriteBody(PacketWriter writer)
    {
        writer.WriteInt64(Tick);
        writer.WriteInt32(Seat);
        writer.WriteUInt64(Hash);
    }
}

/// <summary>Seat <paramref name="Seat"/> quits; its turns after <paramref name="LastTurn"/> are empty for everyone.</summary>
public sealed record LeaveMessage(int Seat, long LastTurn) : Message
{
    public override MessageType Type => MessageType.Leave;

    protected override void WriteBody(PacketWriter writer)
    {
        writer.WriteInt32(Seat);
        writer.WriteInt64(LastTurn);
    }
}

public sealed record ChatMessage(int Seat, string Text) : Message
{
    public override MessageType Type => MessageType.Chat;

    protected override void WriteBody(PacketWriter writer)
    {
        writer.WriteInt32(Seat);
        writer.WriteString(Text);
    }
}

/// <summary>
/// First message on a mesh connection between two players. <paramref name="ResumeFrom"/> is -1 normally;
/// a rejoining peer sets it to the first tick it is missing turns for, and the receiver resends its own.
/// </summary>
public sealed record PeerHelloMessage(PeerId Peer, int Seat, long ResumeFrom) : Message
{
    public override MessageType Type => MessageType.PeerHello;

    protected override void WriteBody(PacketWriter writer)
    {
        Peer.Write(writer);
        writer.WriteInt32(Seat);
        writer.WriteInt64(ResumeFrom);
    }
}

/// <summary>
/// Everything a rejoining peer needs to rebuild the match: how it started, every turn this peer holds,
/// and who has left. The rejoiner replays it at full speed, then carries on.
/// </summary>
public sealed record CatchUpMessage(StartMessage Start, int Seat, IReadOnlyList<TurnMessage> Turns, IReadOnlyList<LeaveMessage> Leaves) : Message
{
    public const int MaxTurns = 8 * 15 * 60 * 120;

    public override MessageType Type => MessageType.CatchUp;

    public static CatchUpMessage Read(PacketReader reader)
    {
        var start = StartMessage.Read(reader);
        var seat = reader.ReadInt32();
        var turns = new TurnMessage[reader.ReadCount(MaxTurns)];
        for (var i = 0; i < turns.Length; i++)
        {
            turns[i] = TurnMessage.Read(reader);
        }

        var leaves = new LeaveMessage[reader.ReadCount(LobbyState.MaxSlots)];
        for (var i = 0; i < leaves.Length; i++)
        {
            leaves[i] = new LeaveMessage(reader.ReadInt32(), reader.ReadInt64());
        }

        return new CatchUpMessage(start, seat, turns, leaves);
    }

    protected override void WriteBody(PacketWriter writer)
    {
        Start.WriteBodyTo(writer);
        writer.WriteInt32(Seat);
        writer.WriteInt32(Turns.Count);
        foreach (var turn in Turns)
        {
            turn.WriteBodyTo(writer);
        }

        writer.WriteInt32(Leaves.Count);
        foreach (var leave in Leaves)
        {
            writer.WriteInt32(leave.Seat);
            writer.WriteInt64(leave.LastTurn);
        }
    }
}
