namespace Ashfall.MapGen;

/// <summary>
/// A rule-set for the generator (GDD section 6): its name, the three-letter code used in map codes,
/// how it reads each slider, and which structural features it builds.
/// </summary>
public sealed record MapArchetype
{
    public required string Id { get; init; }

    /// <summary>Three upper-case letters at the start of a map code, e.g. CRS.</summary>
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    /// <summary>One rule per <see cref="MapSlider"/>, in enum order.</summary>
    public required IReadOnlyList<SliderRule> Sliders { get; init; }

    /// <summary>Map edge in cells at slider 0 and 100.</summary>
    public (int Min, int Max) SizeRange { get; init; } = (64, 128);

    /// <summary>Rivers between the bases, crossed by bridges where lanes meet them; the centre and flanks become islands.</summary>
    public bool Rivers { get; init; }

    /// <summary>Lakes in the lowest ground, sized by the Water slider.</summary>
    public bool Lakes { get; init; }

    /// <summary>Duel bases may sit at the middle of opposite map edges as well as in opposite corners.</summary>
    public bool EdgeStarts { get; init; }

    /// <summary>Most of the map's area that water, rock and trees together may cover, in percent.</summary>
    public int MaxObstruction { get; init; } = 55;

    public SliderRule Rule(MapSlider slider) => Sliders[(int)slider];
}
