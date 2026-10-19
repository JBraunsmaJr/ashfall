using Ashfall.Campaign;
using Ashfall.Simulation.Horde;

namespace Ashfall.Campaign.Tests;

/// <summary>
/// The campaign suite (GDD section 13): whole campaigns with auto-resolved missions over many seeds are winnable by a
/// strong team, losable by a weak one, and never stuck. The preview tool runs the larger sweep in CI.
/// </summary>
public class AutopilotSuiteTests
{
    [Theory]
    [InlineData("standard")]
    [InlineData("quick")]
    public void Veteran_campaigns_are_winnable_losable_and_never_stuck(string id)
    {
        var rules = CampaignRuleSets.Find(id)!;
        int strongWins = 0, weakLosses = 0;
        const int seeds = 30;
        for (uint seed = 1; seed <= seeds; seed++)
        {
            var strong = CampaignAutopilot.PlayToEnd(CampaignState.Start(seed, rules, MawDifficulty.Veteran), 90, seed);
            var weak = CampaignAutopilot.PlayToEnd(CampaignState.Start(seed, rules, MawDifficulty.Veteran), 40, seed);
            Assert.False(strong.Stuck || weak.Stuck, $"Seed {seed} got stuck.");
            strongWins += strong.Outcome == CampaignOutcome.Won ? 1 : 0;
            weakLosses += weak.Outcome == CampaignOutcome.Lost ? 1 : 0;
        }

        Assert.True(strongWins >= seeds * 9 / 10, $"A strong team won only {strongWins} of {seeds}.");
        Assert.True(weakLosses >= seeds * 8 / 10, $"A weak team lost only {weakLosses} of {seeds}.");
    }

    [Fact]
    public void Harder_difficulties_are_harder()
    {
        var wins = Enum.GetValues<MawDifficulty>().Select(difficulty =>
            Enumerable.Range(1, 30).Count(seed =>
                CampaignAutopilot.PlayToEnd(CampaignState.Start((uint)seed, CampaignRuleSets.Quick, difficulty), 70, (uint)seed).Outcome == CampaignOutcome.Won)).ToArray();
        Assert.True(wins[0] >= wins[1] && wins[1] >= wins[2] && wins[2] >= wins[3] && wins[0] > wins[3], string.Join(", ", wins));
    }

    [Fact]
    public void An_autopiloted_campaign_is_the_same_every_time()
    {
        var a = CampaignState.Start(77, CampaignRuleSets.Standard, MawDifficulty.Veteran);
        var b = CampaignState.Start(77, CampaignRuleSets.Standard, MawDifficulty.Veteran);
        Assert.Equal(CampaignAutopilot.PlayToEnd(a, 75, 77), CampaignAutopilot.PlayToEnd(b, 75, 77));
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
    }
}
