using System.Globalization;
using System.Security.Cryptography;
using Ashfall.Networking.Wire;

namespace Ashfall.Networking.Protocol;

/// <summary>A player's anonymous identity: 128 random bits, generated once and kept on their machine.</summary>
public readonly record struct PeerId(ulong High, ulong Low)
{
    public static PeerId None => default;

    public bool IsNone => High == 0 && Low == 0;

    public static PeerId NewRandom()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return new PeerId(BitConverter.ToUInt64(bytes), BitConverter.ToUInt64(bytes[8..]));
    }

    public static PeerId Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();
        if (text.Length != 32)
        {
            throw new FormatException("A peer id is 32 hex digits.");
        }

        return new PeerId(
            ulong.Parse(text.AsSpan(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            ulong.Parse(text.AsSpan(16), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public void Write(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteUInt64(High);
        writer.WriteUInt64(Low);
    }

    public static PeerId Read(PacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new PeerId(reader.ReadUInt64(), reader.ReadUInt64());
    }

    /// <summary>All 32 hex digits, as stored in the identity file.</summary>
    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"{High:x16}{Low:x16}");

    /// <summary>Short form for logs and the lobby.</summary>
    public override string ToString() => ToHex()[..8];
}
