using System.Globalization;
using System.Text;
using Ashfall.Core;
using Ashfall.Simulation.Horde;

namespace Ashfall.Campaign;

/// <summary>
/// Campaign saves (GDD section 7, "Saves and hosting"): the campaign code, the rule-set's id and hash, and the ordered
/// list of turns, as plain text a few kilobytes long, ending with the campaign-state hash. Loading replays every turn,
/// so a save can only hold a campaign this build could have played. Every player keeps a copy; any of them can host.
/// </summary>
/// <example>
/// <code>
/// ashfall-campaign 1
/// code ASH-3C91K-7QZ0M
/// rules standard 0x1F2E3D4C5B6A7988
/// turn region 12 Veteran won 4 0 31500
/// turn event 2 Veteran lost 1 3 40210
/// hash 0x0123456789ABCDEF
/// </code>
/// </example>
public static class CampaignSave
{
    private const string Header = "ashfall-campaign";
    private const int FormatVersion = 1;

    public static string Write(CampaignState campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{Header} {FormatVersion}\n");
        text.Append(CultureInfo.InvariantCulture, $"code {campaign.Code}\n");
        text.Append(CultureInfo.InvariantCulture, $"rules {campaign.Rules.Id} {StateHasher.Format(campaign.RulesHash)}\n");
        foreach (var turn in campaign.History)
        {
            var result = turn.Result;
            text.Append(CultureInfo.InvariantCulture,
                $"turn {(turn.Target.IsEvent ? "event" : "region")} {turn.Target.Id} {turn.Difficulty} {(result.Won ? "won" : "lost")} {result.NestsDestroyed} {result.NestsStanding} {result.Ticks}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"hash {StateHasher.Format(campaign.ComputeHash())}\n");
        return text.ToString();
    }

    /// <summary>
    /// Reads a save and replays it. Throws <see cref="FormatException"/> if it is malformed or holds a turn this build
    /// would not allow. A different rule-set hash (other mods, another build) or a final hash that does not match is
    /// reported in <see cref="LoadedCampaign.Warnings"/>, not refused: the campaign is still the one these turns make.
    /// </summary>
    public static LoadedCampaign Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 3 || lines[0] != string.Create(CultureInfo.InvariantCulture, $"{Header} {FormatVersion}"))
        {
            throw new FormatException($"Not a campaign save (expected \"{Header} {FormatVersion}\" first).");
        }

        var warnings = new List<string>();
        var codeLine = Fields(lines[1], "code", 2);
        if (!CampaignCode.TryParse(codeLine[1], out var code))
        {
            throw new FormatException($"Unknown campaign code {codeLine[1]}.");
        }

        var rulesLine = Fields(lines[2], "rules", 3);
        if (!string.Equals(rulesLine[1], code.Rules.Id, StringComparison.Ordinal))
        {
            throw new FormatException($"The save names the {rulesLine[1]} rules but its code is for {code.Rules.Id}.");
        }

        var campaign = CampaignState.Start(code);
        if (!string.Equals(rulesLine[2], StateHasher.Format(campaign.RulesHash), StringComparison.Ordinal))
        {
            warnings.Add($"The save was made with different {code.Rules.Id} rules ({rulesLine[2]}, this build has {StateHasher.Format(campaign.RulesHash)}).");
        }

        string? savedHash = null;
        for (var i = 3; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("hash ", StringComparison.Ordinal))
            {
                savedHash = Fields(lines[i], "hash", 2)[1];
                continue;
            }

            var fields = Fields(lines[i], "turn", 8);
            var target = fields[1] switch
            {
                "region" => CampaignTarget.Region(Number(fields[2])),
                "event" => CampaignTarget.Event(Number(fields[2])),
                _ => throw new FormatException($"Unknown target kind {fields[1]} on line {i + 1}."),
            };
            if (!Enum.TryParse<MawDifficulty>(fields[3], ignoreCase: false, out var difficulty) || !Enum.IsDefined(difficulty))
            {
                throw new FormatException($"Unknown difficulty {fields[3]} on line {i + 1}.");
            }

            var won = fields[4] switch
            {
                "won" => true,
                "lost" => false,
                _ => throw new FormatException($"Expected won or lost on line {i + 1}, found {fields[4]}."),
            };
            var result = new MissionResult(won, Number(fields[5]), Number(fields[6]), long.Parse(fields[7], NumberStyles.None, CultureInfo.InvariantCulture));
            try
            {
                campaign.Play(new TurnRecord(target, difficulty, result));
            }
            catch (InvalidOperationException error)
            {
                throw new FormatException($"Line {i + 1} is not a turn this campaign allows: {error.Message}", error);
            }
        }

        if (savedHash is not null && !string.Equals(savedHash, StateHasher.Format(campaign.ComputeHash()), StringComparison.Ordinal))
        {
            warnings.Add($"The replayed campaign does not match the saved state hash ({savedHash}, replayed {StateHasher.Format(campaign.ComputeHash())}).");
        }

        return new LoadedCampaign(campaign, warnings);
    }

    private static string[] Fields(string line, string keyword, int count)
    {
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != count || fields[0] != keyword)
        {
            throw new FormatException($"Expected a \"{keyword}\" line with {count - 1} values, found \"{line}\".");
        }

        return fields;
    }

    private static int Number(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException($"Not a number: {text}.");
}

/// <summary>A campaign read from a save, with anything the players should be warned about.</summary>
public sealed record LoadedCampaign(CampaignState Campaign, IReadOnlyList<string> Warnings);
