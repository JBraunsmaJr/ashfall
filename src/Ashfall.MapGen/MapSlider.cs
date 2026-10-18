namespace Ashfall.MapGen;

/// <summary>
/// The lobby's map sliders (GDD section 6), each 0–100. What a slider means is up to the archetype:
/// it maps the value onto its own parameter range.
/// </summary>
public enum MapSlider
{
    /// <summary>Grid dimensions.</summary>
    Size,

    /// <summary>Cindrite field size and value; a second expansion per player when rich.</summary>
    Richness,

    /// <summary>How many height levels the map uses, hence how many cliffs.</summary>
    Elevation,

    /// <summary>Lakes (Crossroads) or river width (Riverlands).</summary>
    Water,

    /// <summary>Lane width, and how much rock fills the ground between lanes.</summary>
    Chokepoints,

    /// <summary>How far apart the starting bases are.</summary>
    BaseSpacing,

    /// <summary>Woodland density (never on lanes or in bases).</summary>
    Vegetation,
}
