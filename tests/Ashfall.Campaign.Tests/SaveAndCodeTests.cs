using Ashfall.Campaign;
using Ashfall.Core;
using Ashfall.Simulation.Horde;

namespace Ashfall.Campaign.Tests;

/// <summary>Campaign codes, saves and the campaign-state hash (GDD section 7, "Saves and hosting without online services").</summary>
public class SaveAndCodeTests
{
    [Fact]
    public void Codes_carry_seed_rules_and_difficulty_and_forgive_typing()
    {
        foreach (var seed in new uint[] { 0, 1, 42, 0xDEADBEEF, uint.MaxValue })
        {
            foreach (var rules in CampaignRuleSets.All)
            {
                foreach (var difficulty in Enum.GetValues<MawDifficulty>())
                {
                    var code = CampaignCode.For(seed, rules, difficulty);
                    var text = code.ToString();
                    Assert.Matches("^ASH-[0-9A-Z]{5}-[0-9A-Z]{5}$", text);
                    Assert.True(CampaignCode.TryParse(text, out var parsed));
                    Assert.Equal(code, parsed);
                    Assert.True(CampaignCode.TryParse(text.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal), out parsed));
                    Assert.Equal(code, parsed);
                    Assert.Same(rules, parsed.Rules);
                }
            }
        }

        var good = CampaignCode.For(1234, CampaignRuleSets.Standard, MawDifficulty.Veteran).ToString();
        Assert.True(CampaignCode.TryParse(good.Replace('0', 'O').Replace('1', 'I'), out var forgiven));
        Assert.Equal(1234u, forgiven.Seed);

        // A slip of one character is caught by the checksum.
        var caught = 0;
        for (var i = 4; i < good.Length; i++)
        {
            if (good[i] == '-')
            {
                continue;
            }

            var typo = good[..i] + (good[i] == 'Z' ? 'Y' : (char)(good[i] + 1)) + good[(i + 1)..];
            caught += CampaignCode.TryParse(typo, out var wrong) && wrong == forgiven ? 0 : 1;
            caught += CampaignCode.TryParse(typo, out wrong) && wrong != forgiven ? -1 : 0;
        }

        Assert.True(caught >= 8, $"Only {caught} of 10 one-character typos were caught.");
        Assert.False(CampaignCode.TryParse("CRS-123", out _));
        Assert.False(CampaignCode.TryParse(null, out _));
    }

    [Fact]
    public void A_save_replays_to_the_same_campaign_and_hash()
    {
        var campaign = CampaignState.Start(9, CampaignRuleSets.Standard, MawDifficulty.Elite);
        CampaignAutopilot.PlayToEnd(campaign, 70, 9, maxTurns: 25);
        var text = CampaignSave.Write(campaign);
        Assert.StartsWith("ashfall-campaign 1\ncode ASH-", text, StringComparison.Ordinal);
        Assert.True(text.Length < 4_096, "A save is a few kilobytes.");

        var loaded = CampaignSave.Read(text);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(campaign.ComputeHash(), loaded.Campaign.ComputeHash());
        Assert.Equal(campaign.Turn, loaded.Campaign.Turn);
        Assert.Equal(campaign.History, loaded.Campaign.History);
        Assert.Equal(campaign.Events, loaded.Campaign.Events);
        Assert.Equal(text, CampaignSave.Write(loaded.Campaign));

        // Windows line endings and blank lines are forgiven.
        Assert.Equal(campaign.ComputeHash(), CampaignSave.Read(text.Replace("\n", "\r\n\r\n", StringComparison.Ordinal)).Campaign.ComputeHash());
    }

    [Fact]
    public void A_campaign_hash_changes_with_every_turn_and_differs_between_campaigns()
    {
        var campaign = CampaignState.Start(9, CampaignRuleSets.Quick, MawDifficulty.Veteran);
        var hashes = new HashSet<ulong> { campaign.ComputeHash() };
        for (var turn = 0; turn < 8 && campaign.Outcome == CampaignOutcome.InProgress; turn++)
        {
            campaign.Play(new TurnRecord(CampaignAutopilot.ChooseTarget(campaign), campaign.Difficulty, MissionResult.Defeat(0, 1)));
            Assert.True(hashes.Add(campaign.ComputeHash()));
        }

        Assert.NotEqual(CampaignState.Start(9, CampaignRuleSets.Quick, MawDifficulty.Recruit).ComputeHash(), CampaignState.Start(9, CampaignRuleSets.Quick, MawDifficulty.Veteran).ComputeHash());
        Assert.NotEqual(CampaignState.Start(10, CampaignRuleSets.Quick, MawDifficulty.Veteran).ComputeHash(), CampaignState.Start(9, CampaignRuleSets.Quick, MawDifficulty.Veteran).ComputeHash());
    }

    [Fact]
    public void Edited_or_foreign_saves_are_flagged_or_refused()
    {
        var campaign = CampaignState.Start(4, CampaignRuleSets.Quick, MawDifficulty.Veteran);
        CampaignAutopilot.PlayToEnd(campaign, 60, 4, maxTurns: 6);
        var text = CampaignSave.Write(campaign);
        var lines = text.Split('\n');

        // A different rule-set hash (other mods or another build): loaded, with a warning.
        var foreign = text.Replace(StateHasher.Format(campaign.RulesHash), "0x0000000000000001", StringComparison.Ordinal);
        Assert.Contains(CampaignSave.Read(foreign).Warnings, warning => warning.Contains("different", StringComparison.Ordinal));

        // The last result flipped by hand no longer matches the saved state hash.
        var turnLine = Array.FindLastIndex(lines, line => line.StartsWith("turn ", StringComparison.Ordinal));
        var flipped = lines.ToArray();
        flipped[turnLine] = flipped[turnLine].Contains(" won ", StringComparison.Ordinal)
            ? flipped[turnLine].Replace(" won ", " lost ", StringComparison.Ordinal)
            : flipped[turnLine].Replace(" lost ", " won ", StringComparison.Ordinal);
        Assert.Contains(CampaignSave.Read(string.Join('\n', flipped)).Warnings, warning => warning.Contains("hash", StringComparison.Ordinal));

        // A turn that could never have been played, a bad header or a broken line: refused.
        turnLine = Array.FindIndex(lines, line => line.StartsWith("turn ", StringComparison.Ordinal));
        var impossible = lines.ToArray();
        impossible[turnLine] = "turn region 9999 Veteran won 1 0 100";
        Assert.Throws<FormatException>(() => CampaignSave.Read(string.Join('\n', impossible)));
        Assert.Throws<FormatException>(() => CampaignSave.Read("hello\n" + text));
        Assert.Throws<FormatException>(() => CampaignSave.Read(text.Replace("turn region", "turn somewhere", StringComparison.Ordinal)));
        var raised = lines.ToArray();
        raised[turnLine] = raised[turnLine].Replace("Veteran", "Extinction", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => CampaignSave.Read(string.Join('\n', raised)));
    }
}
