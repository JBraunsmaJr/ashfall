using System.Buffers.Binary;
using System.Text;

namespace Ashfall.Networking.Wire;

/// <summary>Builds a little-endian binary message. Reuse one writer: <see cref="Clear"/> keeps its buffer.</summary>
public sealed class PacketWriter
{
    private byte[] _buffer;

    public PacketWriter(int capacity = 256) => _buffer = new byte[Math.Max(16, capacity)];

    public int Length { get; private set; }

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, Length);

    public byte[] ToArray() => WrittenSpan.ToArray();

    public void Clear() => Length = 0;

    public void WriteByte(byte value) => Reserve(1)[0] = value;

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteUInt16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);

    public void WriteInt32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);

    public void WriteUInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);

    public void WriteInt64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);

    public void WriteUInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);

    /// <summary>UTF-8 with a 16-bit length prefix.</summary>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var count = Encoding.UTF8.GetByteCount(value);
        if (count > ushort.MaxValue)
        {
            throw new ArgumentException("String too long for the wire format.", nameof(value));
        }

        WriteUInt16((ushort)count);
        Encoding.UTF8.GetBytes(value, Reserve(count));
    }

    /// <summary>Bytes with a 32-bit length prefix.</summary>
    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteInt32(value.Length);
        WriteRaw(value);
    }

    public void WriteRaw(ReadOnlySpan<byte> value) => value.CopyTo(Reserve(value.Length));

    private Span<byte> Reserve(int count)
    {
        if (Length + count > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(Length + count, _buffer.Length * 2));
        }

        var span = _buffer.AsSpan(Length, count);
        Length += count;
        return span;
    }
}
