using Ashfall.Simulation.Map;

namespace Ashfall.MapGen.Tests;

public class SettingsAndValidatorTests
{
    [Fact]
    public void Map_codes_round_trip_for_every_archetype_and_slider_extreme()
    {
        foreach (var archetype in MapArchetypes.All)
        {
            foreach (var seed in new uint[] { 0, 1, 123_456, uint.MaxValue })
            {
                foreach (var value in new[] { 0, 37, 100 })
                {
                    var settings = new MapSettings(archetype, seed, Enumerable.Repeat(value, MapSettings.SliderCount).ToArray());
                    var code = settings.ToCode();
                    Assert.StartsWith(archetype.Code + "-", code);
                    Assert.True(MapSettings.TryParseCode(code, out var parsed), code);
                    Assert.Equal(settings, parsed);
                }
            }
        }
    }

    [Fact]
    public void Map_codes_forgive_case_dashes_and_lookalike_letters_but_catch_typos()
    {
        var settings = new MapSettings(MapArchetypes.Riverlands, 99).With(MapSlider.Water, 80);
        var code = settings.ToCode();
        Assert.True(MapSettings.TryParseCode(code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal), out var relaxed));
        Assert.Equal(settings, relaxed);
        var lookalike = code[..4] + code[4..].Replace('0', 'O').Replace('1', 'I');
        Assert.True(MapSettings.TryParseCode(lookalike, out var fromLookalike), lookalike);
        Assert.Equal(settings, fromLookalike);

        // Change one character of the payload: the checksum (or the slider range check) rejects it.
        var payloadIndex = code.Length - 3;
        var typo = code[..payloadIndex] + (code[payloadIndex] == 'A' ? 'B' : 'A') + code[(payloadIndex + 1)..];
        Assert.False(MapSettings.TryParseCode(typo, out var wrong) && wrong.Equals(settings));
        Assert.False(MapSettings.TryParseCode("XYZ-000000-000000-000000", out _));
        Assert.False(MapSettings.TryParseCode("", out _));
        Assert.False(MapSettings.TryParseCode("CRS-12", out _));
    }

    [Fact]
    public void Archetype_limits_clamp_sliders_and_say_so()
    {
        var tiny = MapGenerator.ApplyLimits(new MapSettings(MapArchetypes.Riverlands, 1).With(MapSlider.Size, 0), out var notes);
        Assert.Equal(20, tiny[MapSlider.Size]);
        Assert.Contains(notes, note => note.StartsWith("Size limited to 20", StringComparison.Ordinal));

        var cluttered = new MapSettings(MapArchetypes.Crossroads, 1)
            .With(MapSlider.Water, 100).With(MapSlider.Chokepoints, 100).With(MapSlider.Vegetation, 100);
        var limited = MapGenerator.ApplyLimits(cluttered, out var clutterNotes);
        Assert.True(limited[MapSlider.Vegetation] < 100);
        Assert.NotEmpty(clutterNotes);

        // The generated map reports the limits and keeps what was asked for.
        var generated = MapGenerator.Generate(cluttered, 2);
        Assert.Equal(cluttered, generated.Requested);
        Assert.Equal(limited, generated.Effective);
        Assert.Equal(clutterNotes, generated.Clamps);
        Assert.Empty(MapGenerator.ApplyLimits(new MapSettings(MapArchetypes.Crossroads, 1), out var none).Equals(new MapSettings(MapArchetypes.Crossroads, 1)) ? none : ["defaults changed"]);
    }

    [Fact]
    public void The_validator_rejects_walled_off_starts_blocked_yards_and_missing_resources()
    {
        var map = new GridMap(64, 64);
        Cell[] starts = [new(6, 6), new(55, 55)];
        for (var i = 0; i < 64; i++)
        {
            map.SetTerrain(new Cell(i, 63 - i), Terrain.Rock);
            if (i < 63)
            {
                map.SetTerrain(new Cell(i + 1, 63 - i), Terrain.Rock); // no diagonal slips through the wall
            }
        }

        map.SetTerrain(new Cell(56, 56), Terrain.Trees);
        var report = MapValidator.Validate(map, starts, 2, 2);
        Assert.False(report.Valid);
        Assert.Contains(report.Failures, f => f.Contains("cannot reach", StringComparison.Ordinal));
        Assert.Contains(report.Failures, f => f.Contains("footprint is blocked", StringComparison.Ordinal));
        Assert.Contains(report.Failures, f => f.Contains("no Cindrite", StringComparison.Ordinal));
        Assert.Equal(0, report.FairnessScore);
    }

    [Fact]
    public void The_validator_measures_unfair_resources()
    {
        var map = new GridMap(64, 64);
        Cell[] starts = [new(6, 6), new(55, 55)];
        map.SetCindrite(new Cell(12, 6), 500);
        map.SetCindrite(new Cell(50, 56), 500);
        map.SetCindrite(new Cell(50, 55), 500); // the second start has twice as much nearby
        var report = MapValidator.Validate(map, starts, 2, 2);
        Assert.Equal(2000, report.ResourceRatio);
        Assert.Contains(report.Failures, f => f.Contains("home resources differ", StringComparison.Ordinal));
        Assert.Equal(1000, report.PathRatio);
        Assert.Equal(1000, report.ReachableResources);
    }

    [Fact]
    public void Symmetry_turns_cells_and_footprints_exactly()
    {
        var quarter = new MapSymmetry(64, 4);
        var cell = new Cell(3, 10);
        Assert.Equal(new Cell(53, 3), quarter.Rotate(cell, 1));
        Assert.Equal(cell, quarter.Rotate(cell, 4));
        Assert.Equal(quarter.Rotate(cell, 3), quarter.Rotate(cell, -1));
        var half = new MapSymmetry(64, 2);
        Assert.Equal(new Cell(60, 53), half.Rotate(cell, 1));
        Assert.Equal(new Cell(58, 58), half.RotateFootprint(new Cell(3, 3), 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapSymmetry(64, 3));
    }

    [Fact]
    public void Value_noise_is_smooth_and_in_range()
    {
        var noise = new ValueNoise(5, 16, 3);
        var previous = noise.Sample(0, 0);
        for (var x = 1; x < 200; x++)
        {
            var value = noise.Sample(x, -x / 3);
            Assert.InRange(value, 0, 1023);
            Assert.True(Math.Abs(value - previous) < 200, $"jump at {x}: {previous} -> {value}");
            previous = value;
        }
    }
}
