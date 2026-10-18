using System.Buffers.Binary;
using System.Text;

namespace Ashfall.Networking.Wire;

/// <summary>Reads what <see cref="PacketWriter"/> wrote. Throws <see cref="ProtocolException"/> on truncated or oversized data.</summary>
public sealed class PacketReader(ReadOnlyMemory<byte> data)
{
    private int _position;

    public int Remaining => data.Length - _position;

    public bool AtEnd => Remaining == 0;

    public byte ReadByte() => Take(1)[0];

    public bool ReadBool() => ReadByte() switch
    {
        0 => false,
        1 => true,
        var other => throw new ProtocolException($"Invalid boolean {other}."),
    };

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public string ReadString()
    {
        var count = ReadUInt16();
        return Encoding.UTF8.GetString(Take(count));
    }

    public byte[] ReadBytes(int maxLength = 64 * 1024 * 1024)
    {
        var count = ReadInt32();
        if (count < 0 || count > maxLength)
        {
            throw new ProtocolException($"Byte block of {count} bytes is out of range.");
        }

        return Take(count).ToArray();
    }

    /// <summary>A count prefix, checked against a sane maximum so a corrupt packet cannot allocate gigabytes.</summary>
    public int ReadCount(int max)
    {
        var count = ReadInt32();
        return count < 0 || count > max ? throw new ProtocolException($"Count {count} is out of range (max {max}).") : count;
    }

    public void ExpectEnd()
    {
        if (!AtEnd)
        {
            throw new ProtocolException($"{Remaining} unexpected trailing bytes.");
        }
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > Remaining)
        {
            throw new ProtocolException($"Message truncated: wanted {count} bytes, {Remaining} left.");
        }

        var span = data.Span.Slice(_position, count);
        _position += count;
        return span;
    }
}
