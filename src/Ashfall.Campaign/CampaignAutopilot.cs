using Ashfall.Core;

namespace Ashfall.Campaign;

/// <summary>How an autopiloted campaign went.</summary>
/// <param name="Turns">Turns played.</param>
/// <param name="Outcome">How it ended (<see cref="CampaignOutcome.InProgress"/> if the turn limit stopped it).</param>
/// <param name="Wins">Missions won.</param>
/// <param name="PeakMeter">The highest the global meter read.</param>
/// <param name="Stuck">True if a turn came with no target to choose: a rules bug.</param>
public readonly record struct AutopilotRun(int Turns, CampaignOutcome Outcome, int Wins, int PeakMeter, bool Stuck);

/// <summary>
/// Plays campaigns with auto-resolved missions (GDD section 13, the campaign test suite): a simple, sensible commander
/// choosing targets and a dice roll standing in for each mission, its odds set by a skill percentage and the mission's
/// strength. Used by the campaign tests and the preview tool to show campaigns are winnable, losable and never stuck,
/// and to tune the rules on statistics rather than single runs.
/// </summary>
public static class CampaignAutopilot
{
    // The autopilot's own random stream ("AUTO"), separate from the campaign's.
    private const ulong Stream = 0x4155544F;

    /// <summary>
    /// The autopilot's choice: an event about to land with a hard consequence, else a hive within reach, else the
    /// front region that best weighs how infested it is against how near it leads to a hive.
    /// </summary>
    public static CampaignTarget ChooseTarget(CampaignState campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        CampaignEvent? urgent = null;
        foreach (var campaignEvent in campaign.Events)
        {
            if (campaignEvent.ExpiresAfterTurn == campaign.Turn + 1 && Severity(campaignEvent.Kind) > 0
                && (urgent is null || Severity(campaignEvent.Kind) > Severity(urgent.Kind)))
            {
                urgent = campaignEvent;
            }
        }

        if (urgent is not null)
        {
            return CampaignTarget.Event(urgent.Id);
        }

        var best = -1;
        foreach (var target in campaign.Targets)
        {
            if (target.IsEvent)
            {
                continue;
            }

            var region = target.Id;
            if (campaign.IsHive(region))
            {
                return target;
            }

            if (best < 0 || Better(campaign, region, best))
            {
                best = region;
            }
        }

        return best >= 0 ? CampaignTarget.Region(best) : campaign.Targets.Count > 0 ? campaign.Targets[0] : default;
    }

    /// <summary>A dice roll for <paramref name="mission"/>: the skill percentage, less for more and stronger nests and harder difficulty.</summary>
    public static MissionResult Resolve(CampaignMission mission, int skillPercent, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(random);
        var chance = skillPercent - (3 * (mission.Nests - 2)) - (8 * (mission.NestTier - 1)) - (5 * (int)mission.Difficulty)
            + (2 * (mission.BonusCredits / 500)) + (3 * mission.BonusHarvesters);
        chance = chance < 5 ? 5 : chance > 95 ? 95 : chance;
        var ticks = 27_000 + (mission.Nests * 1_500);
        if (random.Next(100) < chance)
        {
            return MissionResult.Victory(mission.Nests, ticks);
        }

        var destroyed = random.Next(mission.Nests);
        return MissionResult.Defeat(destroyed, mission.Nests - destroyed, ticks);
    }

    /// <summary>Plays <paramref name="campaign"/> until it ends or <paramref name="maxTurns"/> turns have gone by.</summary>
    public static AutopilotRun PlayToEnd(CampaignState campaign, int skillPercent, ulong seed, int maxTurns = 400)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var random = new DeterministicRandom(seed, Stream);
        var wins = 0;
        var peak = campaign.Meter;
        while (campaign.Outcome == CampaignOutcome.InProgress && campaign.Turn < maxTurns)
        {
            if (campaign.Targets.Count == 0)
            {
                return new AutopilotRun(campaign.Turn, campaign.Outcome, wins, peak, Stuck: true);
            }

            var target = ChooseTarget(campaign);
            var result = Resolve(campaign.PlanMission(target), skillPercent, random);
            campaign.Play(new TurnRecord(target, campaign.Difficulty, result));
            wins += result.Won ? 1 : 0;
            peak = campaign.Meter > peak ? campaign.Meter : peak;
        }

        return new AutopilotRun(campaign.Turn, campaign.Outcome, wins, peak, Stuck: false);
    }

    private static int Severity(CampaignEventKind kind) => kind switch
    {
        CampaignEventKind.HiveEmergence => 4,
        CampaignEventKind.OutpostSiege => 3,
        CampaignEventKind.BehemothMigration => 2,
        CampaignEventKind.MeteorStrike => 1,
        _ => 0,
    };

    // Lighter regions are cheaper to take, and regions nearer a hive open the way to it.
    private static bool Better(CampaignState campaign, int region, int other)
    {
        var score = (2 * campaign.InfestationOf(region)) + (3 * NearestHive(campaign, region));
        var otherScore = (2 * campaign.InfestationOf(other)) + (3 * NearestHive(campaign, other));
        return score != otherScore ? score < otherScore : region < other;
    }

    private static int NearestHive(CampaignState campaign, int region)
    {
        var nearest = int.MaxValue;
        foreach (var other in campaign.World.Regions)
        {
            if (campaign.IsHive(other.Id))
            {
                var distance = campaign.World.Distance(region, other.Id);
                nearest = distance < nearest ? distance : nearest;
            }
        }

        return nearest;
    }
}
