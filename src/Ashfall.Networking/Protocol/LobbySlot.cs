using Ashfall.Networking.Wire;

namespace Ashfall.Networking.Protocol;

public enum SlotKind : byte
{
    Open = 0,
    Human = 1,
    Ai = 2,
    Closed = 3,
}

/// <summary>One seat in the lobby.</summary>
/// <param name="Address">Where the seat's peer accepts mesh connections ("ip:port"), as the host saw it.</param>
public sealed record LobbySlot(SlotKind Kind, PeerId Peer, string Name, int Team, bool Ready, string Address)
{
    public static LobbySlot Open(int team) => new(SlotKind.Open, PeerId.None, string.Empty, team, false, string.Empty);

    public static LobbySlot Ai(int team) => new(SlotKind.Ai, PeerId.None, "AI", team, true, string.Empty);

    public void Write(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteByte((byte)Kind);
        Peer.Write(writer);
        writer.WriteString(Name);
        writer.WriteInt32(Team);
        writer.WriteBool(Ready);
        writer.WriteString(Address);
    }

    public static LobbySlot Read(PacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var kind = (SlotKind)reader.ReadByte();
        if (!Enum.IsDefined(kind))
        {
            throw new ProtocolException($"Unknown slot kind {(byte)kind}.");
        }

        return new LobbySlot(kind, PeerId.Read(reader), reader.ReadString(), reader.ReadInt32(), reader.ReadBool(), reader.ReadString());
    }
}
