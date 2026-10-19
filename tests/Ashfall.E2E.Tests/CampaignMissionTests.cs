using Ashfall.Campaign;
using Ashfall.Content;
using Ashfall.Networking.Wire;
using Ashfall.Session;
using Ashfall.Session.Campaigns;
using Ashfall.Session.Replays;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Horde;

namespace Ashfall.E2E.Tests;

/// <summary>Campaign missions as real matches: region data in, a co-op match out, and the result read back from its end.</summary>
public class CampaignMissionTests
{
    [Fact]
    public async Task A_planned_mission_starts_with_its_nest_tier_bonuses_and_map()
    {
        // Win a few turns so the next region has liberated neighbours, then pick a deep one for higher-tier nests.
        var campaign = CampaignState.Start(21, CampaignRuleSets.Standard, MawDifficulty.Recruit);
        for (var turn = 0; turn < 6; turn++)
        {
            campaign.Play(new TurnRecord(CampaignAutopilot.ChooseTarget(campaign), campaign.Difficulty, MissionResult.Victory(2)));
        }

        var mission = campaign.Targets.Where(t => !t.IsEvent).Select(campaign.PlanMission).MaxBy(m => (m.NestTier, m.BonusCredits))!;
        var spec = CampaignMatches.ToMatchSpec(mission, players: 2);
        Assert.Equal(MatchMode.Purge, spec.Mode);
        Assert.Equal(mission.Seed, spec.Seed);
        Assert.Equal(mission.Map, spec.GeneratedMap);
        Assert.Equal(MawDifficulty.Recruit, spec.Difficulty);

        await using var match = await HeadlessMatch.StartAsync(spec, GameSpeed.Unthrottled);
        var simulation = match.Simulation;
        var world = simulation.World;
        var nestType = mission.NestTier switch { 3 => MawContent.BehemothDen, 2 => MawContent.BroodNest, _ => MawContent.SpawningPit };
        Assert.Equal(match.NestsAtStart, world.Store<Nest>().Count);
        Assert.InRange(match.NestsAtStart, 1, mission.Nests);
        foreach (var index in world.Store<Nest>().EntityIndices)
        {
            Assert.Equal(nestType, simulation.Content.Structure(world.Store<StructureInfo>().Get(index).StructureType).Id);
        }

        foreach (var player in simulation.Players)
        {
            Assert.Equal(simulation.Content.Economy.StartingCredits + mission.BonusCredits, player.Credits);
        }

        var harvesters = world.Store<UnitInfo>().EntityIndices.ToArray()
            .Count(index => world.Store<UnitInfo>().Get(index).UnitType == simulation.Content.UnitIndex(BulwarkContent.Harvester));
        Assert.Equal(2 * mission.BonusHarvesters, harvesters);

        await match.RunTicksAsync(60, TestContext.Current.CancellationToken);
        var result = CampaignMatches.ResultOf(match);
        Assert.False(result.Won);
        Assert.Equal(0, result.NestsDestroyed);
        Assert.Equal(match.NestsAtStart, result.NestsStanding);
        Assert.Equal(match.Tick, result.Ticks);
    }

    [Fact]
    public async Task A_purged_map_reads_back_as_a_won_mission_that_liberates_the_region()
    {
        var campaign = CampaignState.Start(8, CampaignRuleSets.Quick, MawDifficulty.Recruit);
        var target = CampaignAutopilot.ChooseTarget(campaign);
        var mission = campaign.PlanMission(target);
        await using var match = await HeadlessMatch.StartAsync(CampaignMatches.ToMatchSpec(mission, players: 1), GameSpeed.Unthrottled);
        var world = match.Simulation.World;
        foreach (var index in world.Store<Nest>().EntityIndices.ToArray())
        {
            world.Store<Health>().Get(index).Current = 0;
        }

        await match.RunTicksAsync(5, TestContext.Current.CancellationToken);
        Assert.True(match.IsOver);
        var result = CampaignMatches.ResultOf(match);
        Assert.True(result.Won);
        Assert.Equal(match.NestsAtStart, result.NestsDestroyed);
        Assert.Equal(0, result.NestsStanding);

        campaign.Play(new TurnRecord(target, campaign.Difficulty, result));
        Assert.True(campaign.IsLiberated(mission.Region) || campaign.History.Count == 1);
        Assert.True(campaign.History[0].Result.Won);
    }

    [Fact]
    public void Campaign_settings_travel_in_replays_and_old_replays_read_without_them()
    {
        var campaign = CampaignState.Start(2, CampaignRuleSets.Standard, MawDifficulty.Elite);
        var spec = CampaignMatches.ToMatchSpec(campaign.PlanMission(campaign.Targets[0]), players: 3) with { NestTier = 2, BonusCredits = 1_500, BonusHarvesters = 1 };
        var writer = new PacketWriter();
        MatchSpecCodec.Write(writer, spec);
        var read = MatchSpecCodec.Read(new PacketReader(writer.ToArray()));
        Assert.Equal(2, read.NestTier);
        Assert.Equal(1_500, read.BonusCredits);
        Assert.Equal(1, read.BonusHarvesters);
        Assert.Equal(spec.GeneratedMap, read.GeneratedMap);

        // Format 5 stopped after the difficulty: tier 1 nests, no bonuses.
        var bytes = writer.ToArray();
        var old = MatchSpecCodec.Read(new PacketReader(bytes.AsSpan(0, bytes.Length - 6).ToArray()), formatVersion: 5);
        Assert.Equal(1, old.NestTier);
        Assert.Equal(0, old.BonusCredits);
        Assert.Equal(0, old.BonusHarvesters);

        Assert.Throws<ArgumentOutOfRangeException>(() => (spec with { NestTier = 4 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (spec with { BonusHarvesters = MatchSpec.MaxBonusHarvesters + 1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (spec with { BonusCredits = -1 }).Validate());
    }

    [Fact]
    public void Every_first_turn_mission_of_many_campaigns_builds_a_valid_match()
    {
        for (uint seed = 1; seed <= 12; seed++)
        {
            var campaign = CampaignState.Start(seed, CampaignRuleSets.Standard, MawDifficulty.Veteran);
            foreach (var target in campaign.Targets)
            {
                var spec = CampaignMatches.ToMatchSpec(campaign.PlanMission(target), players: 2);
                spec.Validate();
            }
        }
    }
}
