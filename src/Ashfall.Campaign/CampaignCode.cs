using System.Text;
using Ashfall.Simulation.Horde;

namespace Ashfall.Campaign;

/// <summary>
/// A shareable campaign code (GDD section 7): the seed, the rule-set and the starting difficulty in base 32 with a
/// checksum, like <c>ASH-3C91K-7QZ0M</c>, so friends can start the same world. Read case-insensitively; O reads as 0
/// and I or L as 1.
/// </summary>
public readonly record struct CampaignCode(uint Seed, int RuleSet, MawDifficulty Difficulty)
{
    /// <summary>Bumped whenever the layout changes.</summary>
    public const int Version = 1;

    private const string Prefix = "ASH";
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base 32
    private const int Characters = 10;

    public CampaignRules Rules => CampaignRuleSets.All[RuleSet];

    public static CampaignCode For(uint seed, CampaignRules rules, MawDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var index = CampaignRuleSets.IndexOf(rules);
        if (index < 0)
        {
            throw new ArgumentException($"The {rules.Id} rules are not a known rule-set.", nameof(rules));
        }

        return new CampaignCode(seed, index, difficulty);
    }

    public override string ToString()
    {
        // 4 version + 32 seed + 4 rule-set + 3 difficulty + 5 checksum = 48 bits, in 10 characters.
        ulong bits = Version;
        bits = (bits << 32) | Seed;
        bits = (bits << 4) | (uint)RuleSet;
        bits = (bits << 3) | (uint)Difficulty;
        bits = (bits << 5) | Checksum(bits);
        var chars = new char[Characters];
        for (var i = Characters - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(bits & 31)];
            bits >>= 5;
        }

        var text = new string(chars);
        return Prefix + "-" + text[..5] + "-" + text[5..];
    }

    public static bool TryParse(string? code, out CampaignCode result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var cleaned = new StringBuilder();
        foreach (var c in code.Trim().ToUpperInvariant())
        {
            if (c is not ('-' or ' '))
            {
                cleaned.Append(c);
            }
        }

        var text = cleaned.ToString();
        if (text.Length != Prefix.Length + Characters || !text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ulong bits = 0;
        foreach (var raw in text[Prefix.Length..])
        {
            var c = raw switch { 'O' => '0', 'I' or 'L' => '1', _ => raw };
            var digit = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (digit < 0)
            {
                return false;
            }

            bits = (bits << 5) | (uint)digit;
        }

        var checksum = (uint)(bits & 31);
        bits >>= 5;
        if (Checksum(bits) != checksum)
        {
            return false;
        }

        var difficulty = (MawDifficulty)(bits & 7);
        var ruleSet = (int)((bits >> 3) & 15);
        var seed = (uint)((bits >> 7) & uint.MaxValue);
        var version = (int)(bits >> 39);
        if (version != Version || ruleSet >= CampaignRuleSets.All.Count || !Enum.IsDefined(difficulty))
        {
            return false;
        }

        result = new CampaignCode(seed, ruleSet, difficulty);
        return true;
    }

    private static uint Checksum(ulong bits)
    {
        uint sum = 7;
        for (var i = 1; bits != 0; i++)
        {
            sum = ((sum * 31) + ((uint)(bits & 31) * (uint)i)) % 32;
            bits >>= 5;
        }

        return sum;
    }
}
