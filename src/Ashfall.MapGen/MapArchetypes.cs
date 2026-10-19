namespace Ashfall.MapGen;

/// <summary>The archetypes this build knows. M4 ships two of the five in the GDD; the others follow in M6.</summary>
public static class MapArchetypes
{
    /// <summary>Open central plain with lanes between every pair of neighbours and a rich centre field.</summary>
    public static MapArchetype Crossroads { get; } = new()
    {
        Id = "crossroads",
        Code = "CRS",
        Name = "Crossroads",
        Description = "Open plain, lanes between neighbours, a rich contested centre.",
        Sliders =
        [
            new SliderRule(35), // size
            new SliderRule(50), // richness
            new SliderRule(30), // elevation
            new SliderRule(20), // water: lakes
            new SliderRule(35), // chokepoints
            new SliderRule(50), // base spacing
            new SliderRule(30), // vegetation
            new SliderRule(50), // fields
            new SliderRule(50), // regrowth
        ],
        Lakes = true,
        EdgeStarts = true,
    };

    /// <summary>Rivers split the bases; bridges are the objectives and the centre is an island.</summary>
    public static MapArchetype Riverlands { get; } = new()
    {
        Id = "riverlands",
        Code = "RIV",
        Name = "Riverlands",
        Description = "Rivers between the bases, bridges to hold, island fields in the middle.",
        Sliders =
        [
            new SliderRule(45, 20, 100), // size: rivers need room
            new SliderRule(50),
            new SliderRule(25, 0, 70),   // elevation: rivers already make the chokepoints
            new SliderRule(45),          // water: river width
            new SliderRule(40),
            new SliderRule(55, 20, 100), // base spacing: bases must stay clear of the banks
            new SliderRule(35),
            new SliderRule(50),
            new SliderRule(50),
        ],
        Rivers = true,
    };

    public static IReadOnlyList<MapArchetype> All { get; } = [Crossroads, Riverlands];

    public static MapArchetype? Find(string idOrCode)
    {
        foreach (var archetype in All)
        {
            if (string.Equals(archetype.Id, idOrCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(archetype.Code, idOrCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(archetype.Name, idOrCode, StringComparison.OrdinalIgnoreCase))
            {
                return archetype;
            }
        }

        return null;
    }
}
