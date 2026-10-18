using Ashfall.Simulation.Map;

namespace Ashfall.MapGen.Tests;

public class GeneratorTests
{
    public static TheoryData<string, int> ArchetypesAndPlayers
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach (var archetype in MapArchetypes.All)
            {
                for (var players = MapGenerator.MinPlayers; players <= MapGenerator.MaxPlayers; players++)
                {
                    data.Add(archetype.Id, players);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ArchetypesAndPlayers))]
    public void The_same_settings_always_make_the_same_map(string archetype, int players)
    {
        var settings = new MapSettings(MapArchetypes.Find(archetype)!, 4242);
        var first = MapGenerator.Generate(settings, players);
        var second = MapGenerator.Generate(settings, players);
        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(first.StartOrigins, second.StartOrigins);
        Assert.NotEqual(first.Hash, MapGenerator.Generate(settings.WithSeed(4243), players).Hash);
    }

    [Theory]
    [MemberData(nameof(ArchetypesAndPlayers))]
    public void Every_map_is_exactly_rotationally_symmetric(string archetype, int players)
    {
        var generated = MapGenerator.Generate(new MapSettings(MapArchetypes.Find(archetype)!, 77).With(MapSlider.Elevation, 80).With(MapSlider.Vegetation, 70), players);
        var map = generated.Map;
        var symmetry = new MapSymmetry(map.Width, generated.SymmetryOrder);
        Assert.Equal(players <= 2 ? 2 : 4, generated.SymmetryOrder);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var cell = new Cell(x, y);
                var turned = symmetry.Rotate(cell, 1);
                Assert.Equal(map.TerrainAt(cell), map.TerrainAt(turned));
                Assert.Equal(map.HeightAt(cell), map.HeightAt(turned));
                Assert.Equal(map.CindriteAt(cell), map.CindriteAt(turned));
            }
        }

        // Each start is the previous one turned by one symmetry step.
        for (var player = 1; player < players; player++)
        {
            Assert.Equal(symmetry.RotateFootprint(generated.StartOrigins[player - 1], 3, 1), generated.StartOrigins[player]);
        }
    }

    [Theory]
    [MemberData(nameof(ArchetypesAndPlayers))]
    public void Slider_sweeps_produce_valid_fair_maps(string archetype, int players)
    {
        // GDD section 6, "property tests": every slider at its extremes and middle, a few seeds each.
        var rule = MapArchetypes.Find(archetype)!;
        var maps = 0;
        var firstTry = 0;
        foreach (var slider in Enum.GetValues<MapSlider>())
        {
            foreach (var value in new[] { 0, 50, 100 })
            {
                for (uint seed = 1; seed <= 3; seed++)
                {
                    var generated = MapGenerator.Generate(new MapSettings(rule, seed * 7919).With(slider, value), players);
                    Assert.True(generated.Report.Valid, $"{generated.Effective.ToCode()} ({players}p): {string.Join("; ", generated.Report.Failures)}");
                    Assert.Equal(100, generated.Report.FairnessScore);
                    Assert.InRange(generated.Report.PathRatio, 1000, MapValidator.MaxRatio);
                    Assert.InRange(generated.Report.ResourceRatio, 1000, MapValidator.MaxRatio);
                    maps++;
                    firstTry += generated.Attempts == 1 ? 1 : 0;
                }
            }
        }

        Assert.True(firstTry * 100 / maps >= 90, $"only {firstTry} of {maps} maps passed on the first attempt");
    }

    [Fact]
    public void The_size_slider_sets_the_map_edge_within_the_archetype_range()
    {
        var small = MapGenerator.Generate(new MapSettings(MapArchetypes.Crossroads, 3).With(MapSlider.Size, 0), 2);
        var large = MapGenerator.Generate(new MapSettings(MapArchetypes.Crossroads, 3).With(MapSlider.Size, 100), 2);
        Assert.Equal(64, small.Size);
        Assert.Equal(128, large.Size);
        Assert.Equal(64, small.Map.Height);
    }

    [Fact]
    public void Elevation_adds_height_levels_and_cliffs_while_zero_elevation_is_flat()
    {
        var flat = MapGenerator.Generate(new MapSettings(MapArchetypes.Crossroads, 9).With(MapSlider.Elevation, 0), 2).Map;
        var hilly = MapGenerator.Generate(new MapSettings(MapArchetypes.Crossroads, 9).With(MapSlider.Elevation, 100), 2).Map;
        Assert.Equal(0, MaxLevel(flat));
        Assert.Equal(3, MaxLevel(hilly));
        Assert.True(Count(hilly, Terrain.Ramp) > 0, "lanes cross level changes on ramps");
        Assert.Equal(0, Count(flat, Terrain.Ramp));
    }

    [Fact]
    public void Riverlands_rivers_are_crossed_only_by_bridges_and_bases_stay_dry()
    {
        foreach (var players in new[] { 2, 4 })
        {
            var generated = MapGenerator.Generate(new MapSettings(MapArchetypes.Riverlands, 5).With(MapSlider.Water, 100), players);
            var map = generated.Map;
            Assert.True(Count(map, Terrain.Water) > map.Width * 4, "there is a river");
            Assert.True(Count(map, Terrain.Bridge) > 0, "lanes cross it on bridges");
            foreach (var origin in generated.StartOrigins)
            {
                Assert.True(map.IsBuildable(origin));
            }

            // Without the bridges no start could reach another: the river really does split the map.
            var bridgeless = Copy(map);
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    if (map.TerrainAt(new Cell(x, y)) == Terrain.Bridge)
                    {
                        bridgeless.SetTerrain(new Cell(x, y), Terrain.Water);
                    }
                }
            }

            var report = MapValidator.Validate(bridgeless, generated.StartOrigins, players, generated.SymmetryOrder);
            Assert.Contains(report.Failures, failure => failure.Contains("cannot reach", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Crossroads_lakes_grow_with_the_water_slider()
    {
        var dry = MapGenerator.Generate(new MapSettings(MapArchetypes.Crossroads, 21).With(MapSlider.Water, 0), 2).Map;
        var wet = MapGenerator.Generate(new MapSettings(MapArchetypes.Crossroads, 21).With(MapSlider.Water, 100), 2).Map;
        Assert.Equal(0, Count(dry, Terrain.Water));
        Assert.True(Count(wet, Terrain.Water) > dry.Width * dry.Height / 10, $"{Count(wet, Terrain.Water)} water cells of {dry.Width * dry.Height}");
    }

    [Fact]
    public void Three_players_use_three_of_the_four_symmetric_starts()
    {
        var three = MapGenerator.Generate(new MapSettings(MapArchetypes.Crossroads, 8), 3);
        var four = MapGenerator.Generate(new MapSettings(MapArchetypes.Crossroads, 8), 4);
        Assert.Equal(3, three.StartOrigins.Count);
        Assert.Equal(four.StartOrigins.Take(3), three.StartOrigins);
        Assert.Equal(four.Hash, three.Hash);
    }

    [Fact]
    public void Generation_rejects_unsupported_player_counts()
    {
        var settings = new MapSettings(MapArchetypes.Crossroads, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.Generate(settings, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.Generate(settings, 5));
    }

    private static int MaxLevel(GridMap map)
    {
        var max = 0;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                max = Math.Max(max, map.HeightAt(new Cell(x, y)));
            }
        }

        return max;
    }

    private static int Count(GridMap map, Terrain terrain)
    {
        var count = 0;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                count += map.TerrainAt(new Cell(x, y)) == terrain ? 1 : 0;
            }
        }

        return count;
    }

    private static GridMap Copy(GridMap map)
    {
        var copy = new GridMap(map.Width, map.Height);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var cell = new Cell(x, y);
                copy.SetTerrain(cell, map.TerrainAt(cell));
                copy.SetHeight(cell, map.HeightAt(cell));
                if (map.CindriteAt(cell) > 0)
                {
                    copy.SetCindrite(cell, map.CindriteAt(cell));
                }
            }
        }

        return copy;
    }
}
