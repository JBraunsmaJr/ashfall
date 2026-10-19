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

        var mission = campaign.Targets.Where(t => !t.IsEvent).Select(t => campaign.PlanMission(t, campaign.Difficulty, seats: 2)).MaxBy(m => (m.NestTier, m.BonusCredits))!;
        var spec = CampaignMatches.ToMatchSpec(mission);
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
        await using var match = await HeadlessMatch.StartAsync(CampaignMatches.ToMatchSpec(mission), GameSpeed.Unthrottled);
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
        var spec = CampaignMatches.ToMatchSpec(campaign.PlanMission(campaign.Targets[0], campaign.Difficulty, seats: 3)) with
        {
            NestTier = 2,
            BonusCredits = 1_500,
            BonusHarvesters = 1,
            Loadouts = [],
        };
        var writer = new PacketWriter();
        MatchSpecCodec.Write(writer, spec);
        var read = MatchSpecCodec.Read(new PacketReader(writer.ToArray()));
        Assert.Equal(2, read.NestTier);
        Assert.Equal(1_500, read.BonusCredits);
        Assert.Equal(1, read.BonusHarvesters);
        Assert.Equal(spec.GeneratedMap, read.GeneratedMap);

        // Format 5 stopped after the difficulty: tier 1 nests, no bonuses (format 6 added 6 bytes, format 7 another 9 here).
        var bytes = writer.ToArray();
        var old = MatchSpecCodec.Read(new PacketReader(bytes.AsSpan(0, bytes.Length - 15).ToArray()), formatVersion: 5);
        Assert.Equal(1, old.NestTier);
        Assert.Equal(0, old.BonusCredits);
        Assert.Equal(0, old.BonusHarvesters);

        Assert.Throws<ArgumentOutOfRangeException>(() => (spec with { NestTier = 4 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (spec with { BonusHarvesters = MatchSpec.MaxBonusHarvesters + 1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (spec with { BonusCredits = -1 }).Validate());
    }

    [Fact]
    public async Task A_turn_played_headless_advances_the_campaign_and_survives_a_save()
    {
        var campaign = CampaignState.Start(3, CampaignRuleSets.Quick, MawDifficulty.Recruit);
        var target = CampaignAutopilot.ChooseTarget(campaign);

        // A short tick limit: the mission is cut off with nests standing, which counts as a loss.
        var played = await CampaignRunner.PlayTurnAsync(campaign, target, players: 2, tickLimit: 600, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, campaign.Turn);
        Assert.False(played.Result.Won);
        Assert.Equal(600, played.Result.Ticks);
        Assert.True(played.Result.NestsStanding > 0);
        Assert.Equal(target, campaign.History[0].Target);
        Assert.NotEmpty(played.Report.Lines);

        var folder = Path.Combine(Path.GetTempPath(), "ashfall-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "campaign.save");
            CampaignFiles.Save(campaign, path);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.True(CampaignFiles.TryLoad(path, out var loaded, out _));
            Assert.Equal(campaign.ComputeHash(), loaded!.Campaign.ComputeHash());
            Assert.StartsWith(campaign.Code.ToString() + ", QUICK, TURN 1, RECRUIT, METER ", CampaignFiles.Summary(loaded.Campaign), StringComparison.Ordinal);

            File.WriteAllText(path, "not a save");
            Assert.False(CampaignFiles.TryLoad(path, out _, out var error));
            Assert.Contains("could not be read", error, StringComparison.Ordinal);
            Assert.False(CampaignFiles.TryLoad(Path.Combine(folder, "missing.save"), out _, out _));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Nests_are_never_placed_where_the_players_cannot_walk()
    {
        // This map's corner is sealed off by a thin diagonal ridge; a nest used to land in it, out of every unit's reach.
        Assert.True(Ashfall.MapGen.MapSettings.TryParseCode("CRS-283T99-Q2N23R-F8TG7PSQ1", out var map));
        await using var match = await HeadlessMatch.StartAsync(MatchSpec.Purge(1, players: 2, nests: 3, map: map), GameSpeed.Unthrottled);
        var simulation = match.Simulation;
        var home = match.Map!.StartOrigins[0];
        Assert.True(simulation.Navigator.TryFindNearestPassable(home, 8, out var from));
        Assert.Equal(3, match.NestsAtStart);
        foreach (var index in simulation.World.Store<Nest>().EntityIndices)
        {
            var origin = simulation.World.Store<StructureInfo>().Get(index).Origin;
            var approachable = Enumerable.Range(-1, 5).SelectMany(dy => Enumerable.Range(-1, 5).Select(dx => new Ashfall.Simulation.Map.Cell(origin.X + dx, origin.Y + dy)))
                .Any(cell => simulation.Map.IsPassable(cell) && simulation.Navigator.CanReach(from, cell));
            Assert.True(approachable, $"The nest at {origin} cannot be reached.");
        }
    }

    [Fact]
    public async Task Loadouts_bring_research_perks_and_detachments_into_the_match_and_survivors_come_back()
    {
        var campaign = CampaignState.Start(4, CampaignRuleSets.Quick, MawDifficulty.Recruit);
        campaign.Play(new TurnRecord(CampaignAutopilot.ChooseTarget(campaign), campaign.Difficulty,
            new MissionResult(true, 2, 0, 20_000, 150, [["bulwark.warden", "bulwark.rocketeer"], ["bulwark.rifleman"]])));
        foreach (var id in new[] { "armour-1", "fortify-1", "intel-1", "intel-2", "weapons-1" })
        {
            campaign.Research(id);
        }

        var perks = new CommanderBonus(new Ashfall.Simulation.Players.PlayerModifiers(DamagePercent: 105), 250, 1, 1);
        var mission = campaign.PlanMission(CampaignAutopilot.ChooseTarget(campaign), campaign.Difficulty, seats: 2, commanders: [perks]);
        var spec = CampaignMatches.ToMatchSpec(mission);
        Assert.Equal(2, spec.Players);
        Assert.True(spec.RevealNests);
        Assert.Equal(75, spec.TelegraphBonusTicks);

        var writer = new PacketWriter();
        MatchSpecCodec.Write(writer, spec);
        var read = MatchSpecCodec.Read(new PacketReader(writer.ToArray()));
        Assert.Equal(spec.Loadouts, read.Loadouts);
        Assert.Equal((spec.RevealNests, spec.TelegraphBonusTicks), (read.RevealNests, read.TelegraphBonusTicks));

        await using var match = await HeadlessMatch.StartAsync(spec, GameSpeed.Unthrottled);
        var simulation = match.Simulation;
        var world = simulation.World;
        var content = simulation.Content;
        Assert.Equal(115, simulation.Players[0].Modifiers.DamagePercent);
        Assert.Equal(110, simulation.Players[1].Modifiers.DamagePercent);
        Assert.Equal(content.Economy.StartingCredits + mission.BonusCredits + 250, simulation.Players[0].Credits);
        Assert.Equal(DirectorProfile.For(MawDifficulty.Recruit).TelegraphTicks + 75, simulation.Maw!.Director!.Profile.TelegraphTicks);

        // The detachments landed, ten percent tougher; the yards are twenty percent tougher.
        int Count(int player, string id) => world.Store<UnitInfo>().EntityIndices.ToArray()
            .Count(index => world.Store<Owner>().Get(index).PlayerId == player && world.Store<UnitInfo>().Get(index).UnitType == content.UnitIndex(id));
        Assert.Equal(1, Count(0, BulwarkContent.Warden));
        Assert.Equal(1, Count(0, BulwarkContent.Rocketeer));
        Assert.Equal(1, Count(1, BulwarkContent.Rifleman));
        Assert.Equal(1 + mission.BonusHarvesters, Count(0, BulwarkContent.Harvester));
        var warden = world.Store<UnitInfo>().EntityIndices.ToArray().First(index => world.Store<UnitInfo>().Get(index).UnitType == content.UnitIndex(BulwarkContent.Warden));
        Assert.Equal(content.Unit(content.UnitIndex(BulwarkContent.Warden)).Health * 110 / 100, world.Store<Health>().Get(warden).Max);
        var yard = world.Store<StructureInfo>().EntityIndices.ToArray().First(index => world.Store<Owner>().Get(index).PlayerId == 0);
        Assert.Equal(content.Structure(content.StructureIndex(BulwarkContent.ConstructionYard)).Health * 120 / 100, world.Store<Health>().Get(yard).Max);

        // Seismic mapping: every nest is explored by both players from the start.
        await match.RunTicksAsync(2, TestContext.Current.CancellationToken);
        foreach (var index in world.Store<Nest>().EntityIndices)
        {
            var origin = world.Store<StructureInfo>().Get(index).Origin;
            Assert.True(simulation.Visibility.IsExplored(0, new Ashfall.Simulation.Map.Cell(origin.X + 1, origin.Y + 1)));
            Assert.True(simulation.Visibility.IsExplored(1, new Ashfall.Simulation.Map.Cell(origin.X + 1, origin.Y + 1)));
        }

        // Survivors: armed, non-harvester units, the most valuable first, as many as each seat may keep.
        var result = CampaignMatches.ResultOf(match, mission, campaign.Rules);
        Assert.Equal([BulwarkContent.Warden, BulwarkContent.Rocketeer], result.Survivors![0]);
        Assert.Equal([BulwarkContent.Rifleman], result.Survivors[1]);
        Assert.Equal(campaign.Rules.SamplesFor(false, 0, mission.NestTier), result.Samples);
    }

    [Fact]
    public void Every_first_turn_mission_of_many_campaigns_builds_a_valid_match()
    {
        for (uint seed = 1; seed <= 12; seed++)
        {
            var campaign = CampaignState.Start(seed, CampaignRuleSets.Standard, MawDifficulty.Veteran);
            foreach (var target in campaign.Targets)
            {
                var spec = CampaignMatches.ToMatchSpec(campaign.PlanMission(target, campaign.Difficulty, seats: 2));
                spec.Validate();
            }
        }
    }
}
