using Ashfall.Campaign;
using Ashfall.Core;
using Ashfall.Simulation.Horde;

namespace Ashfall.Campaign.Tests;

/// <summary>Generated worlds (GDD section 7, "World generation"): sizes, borders, theatres, home and hives, starting spread.</summary>
public class WorldGenerationTests
{
    public static TheoryData<string> RuleSets => new() { "standard", "quick" };

    [Theory]
    [MemberData(nameof(RuleSets))]
    public void Every_world_fits_its_rules_and_is_one_connected_map(string id)
    {
        var rules = CampaignRuleSets.Find(id)!;
        for (uint seed = 1; seed <= 120; seed++)
        {
            var world = WorldGenerator.Generate(seed, rules);
            Assert.InRange(world.Regions.Count, rules.MinRegions, rules.MaxRegions);
            Assert.InRange(world.Theatres.Count, rules.MinTheatres, rules.MaxTheatres);
            foreach (var region in world.Regions)
            {
                Assert.InRange(region.X, 0, WorldMap.Width);
                Assert.InRange(region.Y, 0, WorldMap.Height);
                Assert.NotEmpty(region.Neighbours);
                Assert.Equal(region.Neighbours.Order(), region.Neighbours);
                Assert.All(region.Neighbours, neighbour => Assert.True(world.AreNeighbours(neighbour, region.Id), "Borders go both ways."));
                Assert.True(world.Distance(world.Home, region.Id) >= 0, $"Seed {seed}: region {region.Id} cannot be reached.");
                Assert.InRange(region.Richness, 0, 100);
            }

            Assert.Equal(world.Regions.Count, world.Regions.Select(region => region.Name).Distinct().Count());
        }
    }

    [Theory]
    [MemberData(nameof(RuleSets))]
    public void Theatres_are_connected_pieces_each_with_one_hive_far_from_home(string id)
    {
        var rules = CampaignRuleSets.Find(id)!;
        for (uint seed = 1; seed <= 120; seed++)
        {
            var world = WorldGenerator.Generate(seed, rules);
            Assert.Equal(world.Regions.Count, world.Theatres.Sum(theatre => theatre.Regions.Count));
            foreach (var theatre in world.Theatres)
            {
                Assert.True(theatre.Regions.Count >= rules.MinRegionsPerTheatre, $"Seed {seed}: theatre {theatre.Id} is too small.");
                Assert.All(theatre.Regions, region => Assert.Equal(theatre.Id, world.Regions[region].Theatre));

                // One piece: every region of the theatre reachable from its first without leaving the theatre.
                var reached = new HashSet<int> { theatre.Regions[0] };
                var queue = new Queue<int>(reached);
                while (queue.TryDequeue(out var region))
                {
                    foreach (var neighbour in world.Regions[region].Neighbours.Where(n => world.Regions[n].Theatre == theatre.Id && reached.Add(n)))
                    {
                        queue.Enqueue(neighbour);
                    }
                }

                Assert.Equal(theatre.Regions.Count, reached.Count);
                Assert.Contains(theatre.Hive, theatre.Regions);
                Assert.True(world.Regions[theatre.Hive].StartsAsHive);
                Assert.True(world.Distance(world.Home, theatre.Hive) >= rules.MinHiveDistance);
            }

            Assert.Equal(world.Theatres.Count, world.Regions.Count(region => region.StartsAsHive));
        }
    }

    [Fact]
    public void Home_is_liberated_its_ring_is_light_and_hives_are_the_deepest()
    {
        for (uint seed = 1; seed <= 60; seed++)
        {
            var campaign = CampaignState.Start(seed, CampaignRuleSets.Standard, MawDifficulty.Veteran);
            var world = campaign.World;
            Assert.True(world.Regions[world.Home].IsHome);
            Assert.True(campaign.IsLiberated(world.Home));
            Assert.Equal(0, campaign.InfestationOf(world.Home));
            Assert.Equal(RegionOwner.Liberated, campaign.OwnerOf(world.Home));
            Assert.All(world.Regions[world.Home].Neighbours, neighbour => Assert.InRange(campaign.InfestationOf(neighbour), 1, 2));
            foreach (var region in world.Regions.Where(region => !region.IsHome))
            {
                Assert.False(campaign.IsLiberated(region.Id));
                Assert.Equal(region.StartsAsHive ? 5 : campaign.InfestationOf(region.Id), campaign.InfestationOf(region.Id));
                Assert.InRange(campaign.InfestationOf(region.Id), 1, region.StartsAsHive ? 5 : 4);
            }

            Assert.Equal(world.Theatres.Count, campaign.HivesLeft);
            Assert.InRange(campaign.Meter, 30, 75); // a fight still to win, not one already lost
            Assert.Equal(CampaignOutcome.InProgress, campaign.Outcome);
        }
    }

    [Fact]
    public void The_same_seed_makes_the_same_world_and_another_seed_another()
    {
        static ulong Fingerprint(WorldMap world)
        {
            var hasher = default(StateHasher);
            foreach (var region in world.Regions)
            {
                hasher.Add(region.X);
                hasher.Add(region.Y);
                hasher.Add(region.Theatre);
                hasher.Add(region.Name);
                hasher.Add((int)region.Biome);
                hasher.Add((int)region.Terrain);
                hasher.Add(region.Richness);
                hasher.Add((int)region.Features);
                hasher.Add(region.StartingInfestation);
                foreach (var neighbour in region.Neighbours)
                {
                    hasher.Add(neighbour);
                }
            }

            return hasher.Value;
        }

        var first = Fingerprint(WorldGenerator.Generate(42, CampaignRuleSets.Standard));
        Assert.Equal(first, Fingerprint(WorldGenerator.Generate(42, CampaignRuleSets.Standard)));
        Assert.NotEqual(first, Fingerprint(WorldGenerator.Generate(43, CampaignRuleSets.Standard)));
        Assert.NotEqual(first, Fingerprint(WorldGenerator.Generate(42, CampaignRuleSets.Quick)));
    }

    [Fact]
    public void Worlds_have_features_terrain_and_biomes_in_variety()
    {
        var regions = Enumerable.Range(1, 40).SelectMany(seed => WorldGenerator.Generate((uint)seed, CampaignRuleSets.Standard).Regions).ToList();
        Assert.All(Enum.GetValues<RegionTerrain>(), terrain => Assert.Contains(regions, region => region.Terrain == terrain));
        Assert.All(Enum.GetValues<Biome>(), biome => Assert.Contains(regions, region => region.Biome == biome));
        foreach (var feature in new[] { RegionFeatures.Survivors, RegionFeatures.TechSite, RegionFeatures.Crater, RegionFeatures.KindredShrine, RegionFeatures.LatticeNode })
        {
            Assert.Contains(regions, region => region.Has(feature));
        }

        Assert.DoesNotContain(regions, region => (region.IsHome || region.StartsAsHive) && region.Features != RegionFeatures.None);
        Assert.All(regions.Where(region => region.Has(RegionFeatures.Crater)), region => Assert.True(region.Richness >= 60));
    }
}
