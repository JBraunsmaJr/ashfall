namespace Ashfall.Content;

/// <summary>How a target resists damage. Values are part of the replay format: append only.</summary>
public enum ArmorType : byte
{
    /// <summary>Infantry.</summary>
    None = 0,
    Light = 1,
    Heavy = 2,
    Structure = 3,
    Air = 4,

    /// <summary>Maw chaff and specialists: soft flesh, shredded by flame, shrugs off anti-armour.</summary>
    Biomass = 5,

    /// <summary>Maw behemoths and nests: crystal-grown plates that only anti-armour cracks.</summary>
    Carapace = 6,
}
