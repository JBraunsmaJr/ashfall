using Ashfall.Campaign;
using Ashfall.Simulation.Horde;

namespace Ashfall.Campaign.Tests;

/// <summary>Strategic turns (GDD section 7): results, the Maw's spread, events, the meter, difficulty and the end.</summary>
public class TurnRuleTests
{
    private static CampaignState New(uint seed = 7, MawDifficulty difficulty = MawDifficulty.Veteran) =>
        CampaignState.Start(seed, CampaignRuleSets.Standard, difficulty);

    private static int FrontRegion(CampaignState campaign, Func<int, bool>? where = null) =>
        campaign.Targets.Where(target => !target.IsEvent).Select(target => target.Id).First(region => where?.Invoke(region) ?? true);

    private static TurnReport Win(CampaignState campaign, CampaignTarget target) =>
        campaign.Play(new TurnRecord(target, campaign.Difficulty, MissionResult.Victory(campaign.PlanMission(target).Nests)));

    private static TurnReport Lose(CampaignState campaign, CampaignTarget target, int standing = 2) =>
        campaign.Play(new TurnRecord(target, campaign.Difficulty, MissionResult.Defeat(1, standing)));

    [Fact]
    public void The_front_is_every_region_bordering_liberated_ground()
    {
        var campaign = New();
        var world = campaign.World;
        var front = campaign.Targets.Where(target => !target.IsEvent).Select(target => target.Id).ToArray();
        Assert.Equal(world.Regions[world.Home].Neighbours, front);

        var far = world.Regions.First(region => world.Distance(world.Home, region.Id) >= 2).Id;
        Assert.False(campaign.CanTarget(CampaignTarget.Region(far), out var reason));
        Assert.Contains("border", reason, StringComparison.Ordinal);
        Assert.False(campaign.CanTarget(CampaignTarget.Region(world.Home), out _));
        Assert.False(campaign.CanTarget(CampaignTarget.Event(99), out _));
        Assert.Throws<InvalidOperationException>(() => campaign.Play(new TurnRecord(CampaignTarget.Region(far), MawDifficulty.Veteran, MissionResult.Victory(2))));
        Assert.Equal(0, campaign.Turn);
    }

    [Fact]
    public void A_win_liberates_the_region_pushes_back_its_neighbours_and_opens_the_front_beyond()
    {
        var campaign = New();
        var world = campaign.World;
        var region = FrontRegion(campaign, id => world.Regions[id].Neighbours.Any(n => !campaign.IsLiberated(n) && campaign.InfestationOf(n) > 1 && !campaign.IsHive(n)));
        var before = world.Regions[region].Neighbours.ToDictionary(n => n, campaign.InfestationOf);
        var report = Win(campaign, CampaignTarget.Region(region));

        Assert.Equal(1, campaign.Turn);
        Assert.True(report.Won);
        Assert.Contains(region, report.Liberated);
        Assert.True(campaign.IsLiberated(region) || report.Reinfested.Contains(region));
        Assert.Equal(1, campaign.AttemptsAt(region));
        foreach (var (neighbour, level) in before)
        {
            if (!campaign.IsLiberated(neighbour) && !campaign.IsHive(neighbour) && level > 1 && !report.Spread.Contains(neighbour) && !report.Reinfested.Contains(neighbour))
            {
                Assert.Equal(level - 1, campaign.InfestationOf(neighbour));
            }
        }

        Assert.All(world.Regions[region].Neighbours.Where(n => !campaign.IsLiberated(n)), n => Assert.True(campaign.CanTarget(CampaignTarget.Region(n), out _)));
        Assert.Single(campaign.History);
    }

    [Fact]
    public void A_loss_raises_the_region_a_level_and_costs_a_turn_but_never_the_run()
    {
        var campaign = New();
        var region = FrontRegion(campaign);
        var level = campaign.InfestationOf(region);
        var report = Lose(campaign, CampaignTarget.Region(region));
        Assert.False(report.Won);
        Assert.Equal(1, campaign.Turn);
        Assert.Equal(level + 1, campaign.InfestationOf(region) - (report.Spread.Contains(region) ? 1 : 0));
        Assert.Equal(CampaignOutcome.InProgress, campaign.Outcome);
        Assert.True(campaign.CanTarget(CampaignTarget.Region(region), out _), "A lost region can be tried again.");

        var mission = campaign.PlanMission(CampaignTarget.Region(region));
        Assert.Equal(1, mission.Attempt);
    }

    [Fact]
    public void Difficulty_can_be_lowered_between_turns_but_never_raised()
    {
        var campaign = New(difficulty: MawDifficulty.Elite);
        var target = CampaignTarget.Region(FrontRegion(campaign));
        Assert.Throws<InvalidOperationException>(() => campaign.Play(new TurnRecord(target, MawDifficulty.Extinction, MissionResult.Victory(2))));
        var meter = campaign.Meter;
        campaign.Play(new TurnRecord(target, MawDifficulty.Recruit, MissionResult.Defeat(0, 2)));
        Assert.Equal(MawDifficulty.Recruit, campaign.Difficulty);
        Assert.Equal(MawDifficulty.Recruit, campaign.PlanMission(target).Difficulty);
        Assert.Throws<InvalidOperationException>(() => campaign.Play(new TurnRecord(target, MawDifficulty.Veteran, MissionResult.Victory(2))));
        Assert.True(campaign.Meter < meter + 10, "An easier difficulty also slows the meter.");
    }

    [Fact]
    public void The_maw_spreads_downhill_and_never_into_home_or_onto_a_hive()
    {
        var spreads = 0;
        for (uint seed = 1; seed <= 20; seed++)
        {
            var campaign = New(seed, MawDifficulty.Extinction);
            for (var turn = 0; turn < 15 && campaign.Outcome == CampaignOutcome.InProgress; turn++)
            {
                var levels = campaign.World.Regions.Select(region => campaign.InfestationOf(region.Id)).ToArray();
                var report = Lose(campaign, CampaignTarget.Region(FrontRegion(campaign)), standing: 0);
                foreach (var region in report.Spread)
                {
                    spreads++;
                    Assert.False(campaign.World.Regions[region].IsHome);
                    Assert.InRange(campaign.InfestationOf(region), 2, CampaignState.MaxInfestation);
                }

                Assert.True(campaign.IsLiberated(campaign.World.Home));
                Assert.All(campaign.World.Regions, region => Assert.True(campaign.InfestationOf(region.Id) <= Math.Max(levels[region.Id] + 2, 3) || campaign.IsHive(region.Id)));
            }
        }

        Assert.True(spreads > 20, $"The Maw should spread on its own ({spreads} spreads).");
    }

    [Fact]
    public void Unanswered_events_expire_with_their_consequences()
    {
        var seen = new HashSet<CampaignEventKind>();
        for (uint seed = 1; seed <= 60 && seen.Count < 5; seed++)
        {
            var campaign = New(seed, MawDifficulty.Extinction);
            for (var turn = 0; turn < 40 && campaign.Outcome == CampaignOutcome.InProgress; turn++)
            {
                var expiring = campaign.Events.Where(e => e.ExpiresAfterTurn == campaign.Turn + 1).ToList();
                var rewardsLost = campaign.RewardsLost;
                var eventRegions = campaign.Events.Select(e => e.Region).ToHashSet();
                var front = campaign.Targets.Where(t => !t.IsEvent && !eventRegions.Contains(t.Id)).ToList();
                if (front.Count == 0)
                {
                    break;
                }

                var report = Win(campaign, front[0]);
                foreach (var expired in expiring)
                {
                    seen.Add(expired.Kind);
                    Assert.DoesNotContain(campaign.Events, e => e.Id == expired.Id);
                    switch (expired.Kind)
                    {
                        case CampaignEventKind.MeteorStrike:
                            Assert.False(campaign.IsLiberated(expired.Region));
                            Assert.True(campaign.InfestationOf(expired.Region) >= 3);
                            break;
                        case CampaignEventKind.OutpostSiege:
                            Assert.False(campaign.IsLiberated(expired.Region));
                            break;
                        case CampaignEventKind.HiveEmergence:
                            Assert.True(campaign.IsHive(expired.Region));
                            break;
                        case CampaignEventKind.SurvivorConvoy:
                            Assert.True(campaign.RewardsLost > rewardsLost);
                            break;
                        case CampaignEventKind.BehemothMigration:
                            Assert.Contains(report.Lines, line => line.Contains("behemoth column", StringComparison.Ordinal));
                            break;
                        default:
                            Assert.Fail($"{expired.Kind} should never be rolled before M9.");
                            break;
                    }
                }
            }
        }

        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public void Answering_an_event_takes_the_turn_and_clears_it()
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            var campaign = New(seed, MawDifficulty.Extinction);
            while (campaign.Events.Count == 0 && campaign.Turn < 20)
            {
                Win(campaign, CampaignTarget.Region(FrontRegion(campaign)));
            }

            if (campaign.Events.Count == 0)
            {
                continue;
            }

            var answered = campaign.Events[0];
            var target = CampaignTarget.Event(answered.Id);
            Assert.Contains(target, campaign.Targets);
            var mission = campaign.PlanMission(target);
            Assert.Equal(answered.Kind, mission.Event);
            Assert.Equal(answered.Region, mission.Region);
            var report = Win(campaign, target);
            Assert.DoesNotContain(campaign.Events, e => e.Id == answered.Id);
            Assert.NotEmpty(report.Lines);
            return;
        }

        Assert.Fail("No event appeared in 30 campaigns.");
    }

    [Fact]
    public void Destroying_every_hive_wins_and_a_full_meter_loses_and_either_ends_the_campaign()
    {
        var winner = CampaignState.Start(3, CampaignRuleSets.Quick, MawDifficulty.Recruit);
        var run = CampaignAutopilot.PlayToEnd(winner, 95, 3);
        Assert.Equal(CampaignOutcome.Won, run.Outcome);
        Assert.Equal(0, winner.HivesLeft);
        Assert.Empty(winner.Targets);
        Assert.Throws<InvalidOperationException>(() => winner.Play(new TurnRecord(CampaignTarget.Region(winner.World.Home), MawDifficulty.Recruit, MissionResult.Victory(1))));

        var loser = CampaignState.Start(3, CampaignRuleSets.Quick, MawDifficulty.Extinction);
        run = CampaignAutopilot.PlayToEnd(loser, 5, 3);
        Assert.Equal(CampaignOutcome.Lost, run.Outcome);
        Assert.Equal(100, loser.Meter);
        Assert.Empty(loser.Targets);
    }
}
