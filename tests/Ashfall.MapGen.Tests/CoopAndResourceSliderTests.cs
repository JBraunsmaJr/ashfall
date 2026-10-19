using Ashfall.Simulation.Map;

namespace Ashfall.MapGen.Tests;

/// <summary>Co-op maps with a shared team area, and the Fields and Regrowth sliders.</summary>
public class CoopAndResourceSliderTests
{
    [Theory]
    [InlineData("crossroads", 1)]
    [InlineData("crossroads", 2)]
    [InlineData("crossroads", 4)]
    [InlineData("riverlands", 3)]
    [InlineData("riverlands", 4)]
    public void Coop_allies_start_together_in_one_team_area_far_from_the_mirrored_side(string archetype, int players)
    {
        for (uint seed = 1; seed <= 12; seed++)
        {
            var settings = MapSettings.Default(MapArchetypes.Find(archetype)!, seed).With(MapSlider.BaseSpacing, (int)(seed * 17 % 101));
            var map = MapGenerator.Generate(settings, players, coop: true);
            Assert.False(map.FellBack, $"seed {seed}: {string.Join("; ", map.Rejected)}");
            Assert.Equal(players, map.StartOrigins.Count);

            var team = map.StartOrigins[0];
            foreach (var ally in map.StartOrigins)
            {
                Assert.InRange(Math.Max(Math.Abs(ally.X - team.X), Math.Abs(ally.Y - team.Y)), 0, 16);
                for (var y = 0; y < 3; y++)
                {
                    for (var x = 0; x < 3; x++)
                    {
                        Assert.True(map.Map.IsBuildable(new Cell(ally.X + x, ally.Y + y)));
                    }
                }
            }

            // The other half of the map (the Maw's) is a long way from the team.
            var far = new Cell(map.Size - 1 - team.X - 2, map.Size - 1 - team.Y - 2);
            Assert.True(Math.Max(Math.Abs(far.X - team.X), Math.Abs(far.Y - team.Y)) > map.Size / 3);
        }
    }

    [Fact]
    public void A_coop_map_is_the_same_every_time_and_differs_from_the_versus_one()
    {
        var settings = MapSettings.Default(MapArchetypes.Crossroads, 99);
        Assert.Equal(MapGenerator.Generate(settings, 3, coop: true).Hash, MapGenerator.Generate(settings, 3, coop: true).Hash);
        Assert.NotEqual(MapGenerator.Generate(settings, 3).Hash, MapGenerator.Generate(settings, 3, coop: true).Hash);
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.Generate(settings, 0, coop: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.Generate(settings, 1));
    }

    [Fact]
    public void The_fields_slider_sets_how_many_cindrite_fields_there_are()
    {
        var previous = -1;
        foreach (var value in new[] { 0, 20, 40, 60, 80, 100 })
        {
            var map = MapGenerator.Generate(MapSettings.Default(MapArchetypes.Crossroads, 7).With(MapSlider.Fields, value), 2);
            Assert.True(map.Report.Valid);
            var fissures = Fissures(map.Map);
            Assert.True(fissures >= previous, $"fields {value}: {fissures} fissures after {previous}");
            previous = fissures;
        }

        Assert.Equal(2, Fissures(MapGenerator.Generate(MapSettings.Default(MapArchetypes.Crossroads, 7).With(MapSlider.Fields, 0), 2).Map)); // base fields only
        Assert.True(previous > Fissures(MapGenerator.Generate(MapSettings.Default(MapArchetypes.Crossroads, 7), 2).Map)); // wild fields at 100
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(50, 100)]
    [InlineData(100, 300)]
    [InlineData(25, 62)]
    [InlineData(75, 200)]
    public void The_regrowth_slider_sets_the_maps_regrowth_rate(int value, int percent)
    {
        var settings = MapSettings.Default(MapArchetypes.Crossroads, 3).With(MapSlider.Regrowth, value);
        Assert.Equal(percent, MapGenerator.RegrowthPercent(settings));
        Assert.Equal(percent, MapGenerator.Generate(settings, 2).Map.RegrowthPercent);
    }

    [Fact]
    public void Version_one_map_codes_still_read_with_the_new_sliders_at_their_defaults()
    {
        Assert.True(MapSettings.TryParseCode("RIV-20000Z-N5NJHJ-TM6XWS", out var old));
        Assert.Equal(50, old[MapSlider.Fields]);
        Assert.Equal(50, old[MapSlider.Regrowth]);
        Assert.True(MapSettings.TryParseCode(old.ToCode(), out var again));
        Assert.Equal(old, again);
        Assert.Equal(21 + 3 + 3, old.ToCode().Length); // three letters, 21 characters, three dashes
    }

    private static int Fissures(GridMap map)
    {
        var count = 0;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                count += map.IsFissure(new Cell(x, y)) ? 1 : 0;
            }
        }

        return count;
    }
}
