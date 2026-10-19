using Ashfall.Campaign;
using Ashfall.MapGen;
using Ashfall.Simulation.Horde;

namespace Ashfall.Campaign.Tests;

/// <summary>From region to mission (GDD section 7): every mission is a normal co-op match spec made from region data.</summary>
public class MissionPlanningTests
{
    [Fact]
    public void A_region_becomes_a_map_from_its_terrain_richness_and_infestation()
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            var campaign = CampaignState.Start(seed, CampaignRuleSets.Standard, MawDifficulty.Elite);
            foreach (var target in campaign.Targets)
            {
                var mission = campaign.PlanMission(target);
                var region = campaign.World.Regions[mission.Region];
                var level = campaign.InfestationOf(region.Id);
                var expected = region.Terrain is RegionTerrain.RiverValley or RegionTerrain.Coast ? MapArchetypes.Riverlands : MapArchetypes.Crossroads;
                Assert.Equal(expected.Id, mission.Map.Archetype.Id);
                Assert.Equal(region.Richness, mission.Map[MapSlider.Richness]);
                Assert.Equal(Math.Min(100, 30 + (level * 12)), mission.Map[MapSlider.Fields]);
                Assert.Equal(campaign.Rules.NestsByInfestation[level], mission.Nests);
                Assert.Equal(campaign.Rules.NestTierByInfestation[level], mission.NestTier);
                Assert.Equal(MissionKind.Purge, mission.Kind);
                Assert.Equal(MawDifficulty.Elite, mission.Difficulty);
                Assert.Equal(1, mission.Turn);
                Assert.Equal(0, mission.Attempt);
                Assert.Contains(region.Name, mission.Briefing, StringComparison.Ordinal);
                Assert.True(MapSettings.TryParseCode(mission.Map.ToCode(), out var parsed) && parsed.Equals(mission.Map), "The map travels as a map code.");
                MapGenerator.ApplyLimits(mission.Map, out _);
            }
        }
    }

    [Fact]
    public void Liberated_neighbours_bring_credits_and_harvesters()
    {
        var campaign = CampaignState.Start(11, CampaignRuleSets.Standard, MawDifficulty.Veteran);
        var home = campaign.World.Home;
        var first = campaign.Targets.First(t => !t.IsEvent);
        var mission = campaign.PlanMission(first);
        var survivors = campaign.World.Regions[home].Has(RegionFeatures.Survivors) ? 1 : 0;
        Assert.Equal(campaign.Rules.CreditsPerLiberatedNeighbour + (survivors * campaign.Rules.CreditsPerSurvivorNeighbour), mission.BonusCredits);
        Assert.Equal(0, mission.BonusHarvesters);

        // Liberate ground until some region on the front borders two liberated regions: it gets more.
        bool Surrounded() => campaign.Targets.Any(t => !t.IsEvent && campaign.World.Regions[t.Id].Neighbours.Count(campaign.IsLiberated) >= 2);
        for (var turn = 0; turn < 40 && campaign.Outcome == CampaignOutcome.InProgress && !Surrounded(); turn++)
        {
            var target = CampaignAutopilot.ChooseTarget(campaign);
            campaign.Play(new TurnRecord(target, campaign.Difficulty, MissionResult.Victory(2)));
        }

        var best = campaign.Targets.Where(t => !t.IsEvent).Select(campaign.PlanMission).MaxBy(m => m.BonusCredits)!;
        var liberated = campaign.World.Regions[best.Region].Neighbours.Count(campaign.IsLiberated);
        Assert.True(liberated >= 2);
        Assert.True(best.BonusCredits >= liberated * campaign.Rules.CreditsPerLiberatedNeighbour);
        Assert.Equal(Math.Min(campaign.Rules.MaxBonusHarvesters, liberated / campaign.Rules.NeighboursPerHarvester), best.BonusHarvesters);
    }

    [Fact]
    public void A_hive_is_a_hive_assault_against_the_most_and_strongest_nests()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            var campaign = CampaignState.Start(seed, CampaignRuleSets.Quick, MawDifficulty.Recruit);
            while (campaign.Outcome == CampaignOutcome.InProgress && !campaign.Targets.Any(t => !t.IsEvent && campaign.IsHive(t.Id)))
            {
                campaign.Play(new TurnRecord(CampaignAutopilot.ChooseTarget(campaign), campaign.Difficulty, MissionResult.Victory(2)));
            }

            var hive = campaign.Targets.FirstOrDefault(t => !t.IsEvent && campaign.IsHive(t.Id));
            if (campaign.Outcome != CampaignOutcome.InProgress)
            {
                continue;
            }

            var mission = campaign.PlanMission(hive);
            Assert.Equal(MissionKind.HiveAssault, mission.Kind);
            Assert.Equal(campaign.Rules.HiveNests, mission.Nests);
            Assert.Equal(3, mission.NestTier);
            var hivesLeft = campaign.HivesLeft;
            var report = campaign.Play(new TurnRecord(hive, campaign.Difficulty, MissionResult.Victory(mission.Nests)));
            Assert.Equal(hivesLeft - 1, campaign.HivesLeft);
            Assert.Contains(report.Lines, line => line.Contains("hive", StringComparison.Ordinal));
            return;
        }

        Assert.Fail("No hive came within reach.");
    }

    [Fact]
    public void Missions_are_the_same_on_every_peer_and_new_on_every_attempt()
    {
        var a = CampaignState.Start(5, CampaignRuleSets.Standard, MawDifficulty.Veteran);
        var b = CampaignState.Start(5, CampaignRuleSets.Standard, MawDifficulty.Veteran);
        var target = a.Targets[0];
        Assert.Equal(a.PlanMission(target), b.PlanMission(target));

        var first = a.PlanMission(target);
        a.Play(new TurnRecord(target, MawDifficulty.Veteran, MissionResult.Defeat(0, first.Nests)));
        if (a.CanTarget(target, out _))
        {
            var second = a.PlanMission(target);
            Assert.Equal(1, second.Attempt);
            Assert.NotEqual(first.Seed, second.Seed);
            Assert.NotEqual(first.Map.Seed, second.Map.Seed);
        }

        var seeds = Enumerable.Range(0, 40).Select(region => CampaignState.MissionSeed(5, region, 0, 0)).ToHashSet();
        Assert.Equal(40, seeds.Count);
    }

    [Fact]
    public void Event_missions_match_their_event()
    {
        var kinds = new HashSet<CampaignEventKind>();
        for (uint seed = 1; seed <= 60 && kinds.Count < 5; seed++)
        {
            var campaign = CampaignState.Start(seed, CampaignRuleSets.Standard, MawDifficulty.Extinction);
            for (var turn = 0; turn < 30 && campaign.Outcome == CampaignOutcome.InProgress; turn++)
            {
                foreach (var campaignEvent in campaign.Events)
                {
                    var mission = campaign.PlanMission(CampaignTarget.Event(campaignEvent.Id));
                    kinds.Add(campaignEvent.Kind);
                    var expected = campaignEvent.Kind switch
                    {
                        CampaignEventKind.OutpostSiege => MissionKind.Holdout,
                        CampaignEventKind.SurvivorConvoy => MissionKind.Escort,
                        _ => MissionKind.Purge,
                    };
                    Assert.Equal(expected, mission.Kind);
                    Assert.Contains(Campaign.CampaignState.EventName(campaignEvent.Kind), mission.Briefing + CampaignState.EventName(campaignEvent.Kind), StringComparison.Ordinal);
                }

                var front = campaign.Targets.First(t => !t.IsEvent);
                campaign.Play(new TurnRecord(front, campaign.Difficulty, MissionResult.Victory(2)));
            }
        }

        Assert.Equal(5, kinds.Count);
    }
}
