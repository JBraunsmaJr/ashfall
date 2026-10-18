using Ashfall.Networking.Wire;

namespace Ashfall.Networking.Protocol;

/// <summary>The host's view of the lobby, broadcast to everyone whenever it changes.</summary>
/// <param name="Revision">Increases with every change, so stale copies can be ignored.</param>
public sealed record LobbyState(int Revision, ulong Seed, ulong ContentHash, IReadOnlyList<LobbySlot> Slots)
{
    public const int MaxSlots = 8;

    /// <summary>The generated map's code (peers generate it locally), or empty for the hand-made map.</summary>
    public string MapCode { get; init; } = string.Empty;

    /// <summary>Seats that will play: humans and AIs, in slot order (open and closed slots are left out).</summary>
    public IEnumerable<(int Slot, LobbySlot Seat)> Seats() =>
        Slots.Select((slot, index) => (index, slot)).Where(pair => pair.slot.Kind is SlotKind.Human or SlotKind.Ai);

    /// <summary>The seat number (0-based, among playing slots) of slot <paramref name="slot"/>, or -1.</summary>
    public int SeatOfSlot(int slot)
    {
        var seat = 0;
        for (var i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].Kind is SlotKind.Human or SlotKind.Ai)
            {
                if (i == slot)
                {
                    return seat;
                }

                seat++;
            }
        }

        return -1;
    }

    public int SlotOf(PeerId peer)
    {
        for (var i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].Kind == SlotKind.Human && Slots[i].Peer == peer)
            {
                return i;
            }
        }

        return -1;
    }

    public LobbyState WithSlot(int index, LobbySlot slot)
    {
        var slots = Slots.ToArray();
        slots[index] = slot;
        return this with { Revision = Revision + 1, Slots = slots };
    }

    public void Write(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteInt32(Revision);
        writer.WriteUInt64(Seed);
        writer.WriteUInt64(ContentHash);
        writer.WriteInt32(Slots.Count);
        foreach (var slot in Slots)
        {
            slot.Write(writer);
        }

        writer.WriteString(MapCode);
    }

    public static LobbyState Read(PacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var revision = reader.ReadInt32();
        var seed = reader.ReadUInt64();
        var contentHash = reader.ReadUInt64();
        var slots = new LobbySlot[reader.ReadCount(MaxSlots)];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = LobbySlot.Read(reader);
        }

        return new LobbyState(revision, seed, contentHash, slots) { MapCode = reader.ReadString() };
    }
}
