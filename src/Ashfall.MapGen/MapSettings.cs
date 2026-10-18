using System.Globalization;
using System.Text;

namespace Ashfall.MapGen;

/// <summary>
/// Everything that decides a generated map apart from the player count: the archetype, a seed and the
/// seven slider values. Every peer generates the same map from the same settings, so this (as a map
/// code) is all a lobby needs to send. Map codes look like <c>CRS-7F3AQ2-0M9KX1-ZT4R8B</c>.
/// </summary>
public sealed record MapSettings
{
    /// <summary>Bumped whenever a code's layout changes.</summary>
    public const int CodeVersion = 1;

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base 32: no I, L, O, U
    private const int SliderBits = 7;
    private const int PayloadBits = 4 + 32 + (SliderBits * 7);
    private const int ChecksumBits = 5;
    private const int CodeChars = (PayloadBits + ChecksumBits) / 5;

    private readonly int[] _sliders;

    public MapSettings(MapArchetype archetype, uint seed, IReadOnlyList<int>? sliders = null)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        Archetype = archetype;
        Seed = seed;
        var count = SliderCount;
        _sliders = new int[count];
        for (var i = 0; i < count; i++)
        {
            var value = sliders is not null && i < sliders.Count ? sliders[i] : archetype.Sliders[i].Default;
            _sliders[i] = value < 0 ? 0 : value > 100 ? 100 : value;
        }
    }

    public static int SliderCount => Enum.GetValues<MapSlider>().Length;

    public MapArchetype Archetype { get; }

    public uint Seed { get; }

    public IReadOnlyList<int> Sliders => _sliders;

    public int this[MapSlider slider] => _sliders[(int)slider];

    /// <summary>The archetype's defaults with <paramref name="seed"/>.</summary>
    public static MapSettings Default(MapArchetype archetype, uint seed) => new(archetype, seed);

    public MapSettings With(MapSlider slider, int value)
    {
        var sliders = _sliders.ToArray();
        sliders[(int)slider] = value;
        return new MapSettings(Archetype, Seed, sliders);
    }

    public MapSettings WithSeed(uint seed) => new(Archetype, seed, _sliders);

    public MapSettings WithArchetype(MapArchetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        return new MapSettings(archetype, Seed);
    }

    public bool Equals(MapSettings? other) =>
        other is not null && other.Archetype.Id == Archetype.Id && other.Seed == Seed && other._sliders.AsSpan().SequenceEqual(_sliders);

    public override int GetHashCode() => HashCode.Combine(Archetype.Id, Seed, _sliders[0], _sliders[3], _sliders[6]);

    /// <summary>The shareable map code: archetype letters, then seed and sliders in base 32 with a checksum.</summary>
    public string ToCode()
    {
        UInt128 bits = (uint)CodeVersion;
        bits = (bits << 32) | Seed;
        foreach (var value in _sliders)
        {
            bits = (bits << SliderBits) | (uint)value;
        }

        bits = (bits << ChecksumBits) | Checksum(bits);
        var chars = new char[CodeChars];
        for (var i = CodeChars - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(bits & 31)];
            bits >>= 5;
        }

        var text = new string(chars);
        return string.Create(CultureInfo.InvariantCulture, $"{Archetype.Code}-{text[..6]}-{text[6..12]}-{text[12..]}");
    }

    /// <summary>Reads a map code (case and dashes are forgiven; O and I read as 0 and 1).</summary>
    public static bool TryParseCode(string? code, out MapSettings settings)
    {
        settings = null!;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        // The archetype letters are kept as typed; in the base-32 part O reads as 0 and I or L as 1.
        var cleaned = new StringBuilder();
        foreach (var c in code.Trim().ToUpperInvariant())
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            cleaned.Append(cleaned.Length < 3 ? c : c switch { 'O' => '0', 'I' or 'L' => '1', _ => c });
        }

        var text = cleaned.ToString();
        if (text.Length != 3 + CodeChars || MapArchetypes.Find(text[..3]) is not { } archetype)
        {
            return false;
        }

        UInt128 bits = 0;
        foreach (var c in text[3..])
        {
            var digit = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (digit < 0)
            {
                return false;
            }

            bits = (bits << 5) | (uint)digit;
        }

        var checksum = (uint)(bits & ((1u << ChecksumBits) - 1));
        bits >>= ChecksumBits;
        if (Checksum(bits) != checksum)
        {
            return false;
        }

        var sliders = new int[SliderCount];
        for (var i = sliders.Length - 1; i >= 0; i--)
        {
            sliders[i] = (int)(bits & ((1u << SliderBits) - 1));
            bits >>= SliderBits;
            if (sliders[i] > 100)
            {
                return false;
            }
        }

        var seed = (uint)(bits & uint.MaxValue);
        bits >>= 32;
        if (bits != CodeVersion)
        {
            return false;
        }

        settings = new MapSettings(archetype, seed, sliders);
        return true;
    }

    public override string ToString() => ToCode();

    private static uint Checksum(UInt128 bits)
    {
        uint sum = 7;
        while (bits != 0)
        {
            sum = ((sum * 31) + (uint)(bits & 31)) % 32;
            bits >>= 5;
        }

        return sum;
    }
}
